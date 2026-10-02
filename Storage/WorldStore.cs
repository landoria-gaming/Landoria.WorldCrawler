using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;

namespace Landoria.WorldCrawler.Storage
{
    // Owns an exclusive, restartable world export with one committed file per zone.
    public sealed class WorldStore : IDisposable
    {
        private readonly WorldIdentity identity;
        private readonly FileStream directoryLock;
        private bool disposed;
        public string DirectoryPath
        {
            get; private set;
        }
        public WorldManifest Manifest
        {
            get; private set;
        }
        public string RecoveryNotice
        {
            get; private set;
        }
        public Action<int, int, byte[], string, int> PayloadValidator
        {
            get; set;
        }
        private string ManifestPath
        {
            get
            {
                return Path.Combine(DirectoryPath, "manifest.json");
            }
        }

        // Holds the world lock for the lifetime of this store.
        private WorldStore(string directory, WorldIdentity world, FileStream worldLock)
        {
            DirectoryPath = directory;
            identity = world.Copy();
            directoryLock = worldLock;
        }

        // Opens existing progress or creates a new manifest under a safe world directory.
        public static WorldStore Open(string root, WorldIdentity world, bool reconcile = true, string scope = null)
        {
            if (world == null || world.SeedText == null || world.Name == null)
            {
                throw new ArgumentException("A complete connected world identity is required.");
            }
            var directory = Path.Combine(Path.GetFullPath(root), StoreValidation.DirectoryName(world, scope));
            Directory.CreateDirectory(directory);
            var worldLock = new FileStream(Path.Combine(directory, ".worldcrawler.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var store = new WorldStore(directory, world, worldLock);
            try
            {
                store.LoadManifest();
                if (reconcile)
                {
                    store.Reconcile();
                }
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Initializes a recording without changing legacy export folders or creating a route.
        public void BeginRecording(string characterId, string characterName, string gameVersion)
        {
            EnsureOpen();
            if (Manifest.FormatVersion != 2)
            {
                throw new NotSupportedException("This folder contains a legacy export; it is preserved read-only.");
            }
            if (!Manifest.InventoryInitialized)
            {
                Manifest.CharacterId = characterId;
                Manifest.CharacterName = characterName ?? string.Empty;
            }
            Manifest.GameVersion = gameVersion;
            Manifest.InventoryInitialized = true;
            Manifest.CrawlState = "recording";
            Save();
        }

        // Commits progress after validating world identity and zone filenames.
        public void Save()
        {
            EnsureOpen();
            Manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            StoreValidation.Manifest(Manifest, identity);
            AtomicJson.Write(ManifestPath, Manifest, value => StoreValidation.Manifest(value, identity));
        }

        // Commits a validated zone before checkpointing the corresponding manifest entry.
        public void WriteZone(int x, int z, byte[] payload, string captureVersion, int objects)
        {
            EnsureOpen();
            var entry = Manifest.Zones.SingleOrDefault(zone => zone.X == x && zone.Z == z)
                ?? new ZoneEntry { X = x, Z = z, Origin = ExplorationOrigin.Received };
            if (payload == null || payload.Length > AtomicJson.MaximumFileLength / 2 ||
                objects < 0 || string.IsNullOrEmpty(captureVersion))
            {
                throw new ArgumentException("A bounded payload, version, and valid object count are required.");
            }
            payload = MergeDeletions(x, z, payload);
            PayloadValidator?.Invoke(x, z, payload, captureVersion, objects);
            var envelope = new ZoneEnvelope
            {
                World = identity.Copy(),
                X = x,
                Z = z,
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                CaptureVersion = captureVersion,
                ObjectCount = objects,
                PayloadLength = payload.Length,
                Checksum = StoreValidation.Hash(payload),
                PayloadBase64 = Convert.ToBase64String(payload)
            };
            AtomicJson.Write(ZonePath(x, z), envelope, value => ValidateEnvelope(value, x, z));
            if (!Manifest.Zones.Contains(entry))
            {
                Manifest.Zones.Add(entry);
            }
            MarkCaptured(entry, envelope);
            Save();
        }

        // Carries confirmed removals forward when a later complete snapshot replaces the same sector.
        private byte[] MergeDeletions(int x, int z, byte[] payload)
        {
            var snapshot = Capture.ZoneSnapshot.Decode(payload);
            if (snapshot.PayloadVersion < 2 || !File.Exists(ZonePath(x, z)))
            {
                return payload;
            }
            try
            {
                var previous = Capture.ZoneSnapshot.Decode(ValidateEnvelope(
                    AtomicJson.Read<ZoneEnvelope>(ZonePath(x, z)), x, z));
                snapshot.Departures = snapshot.Departures.Concat(previous.Departures ??
                    new System.Collections.Generic.List<Capture.CapturedDeparture>())
                    .GroupBy(item => item.SourceUser + ":" + item.SourceId)
                    .Select(group => group.OrderByDescending(item => item.ObservedUtcTicks).First()).ToList();
                snapshot.Deletions = snapshot.Deletions.Concat(previous.Deletions ??
                    new System.Collections.Generic.List<Capture.CapturedDeletion>())
                    .GroupBy(item => item.SourceUser + ":" + item.SourceId)
                    .Select(group => group.OrderByDescending(item => item.ObservedUtcTicks).First()).ToList();
            }
            catch (Exception error) when (IsInvalidFile(error))
            {
                throw new InvalidDataException("Previous deletion evidence cannot be read; old file preserved.", error);
            }
            return snapshot.Encode();
        }

        // Reads verified payload bytes for an inventoried zone.
        public byte[] ReadZone(int x, int z)
        {
            EnsureOpen();
            FindZone(x, z);
            return ValidateEnvelope(AtomicJson.Read<ZoneEnvelope>(ZonePath(x, z)), x, z);
        }

        // Recovers valid orphan writes and makes damaged or interrupted captures retryable.
        public void Reconcile()
        {
            EnsureOpen();
            RecoverOrphanZones();
            foreach (var zone in Manifest.Zones)
            {
                var path = ZonePath(zone.X, zone.Z);
                if (File.Exists(path))
                {
                    try
                    {
                        var envelope = AtomicJson.Read<ZoneEnvelope>(path);
                        ValidateEnvelope(envelope, zone.X, zone.Z);
                        MarkCaptured(zone, envelope);
                    }
                    catch (Exception error) when (IsInvalidFile(error))
                    {
                        MarkPending(zone, "Stored zone is invalid and was preserved: " + error.Message);
                    }
                }
                else if (zone.Status == "captured")
                {
                    MarkPending(zone, "Completed zone file is missing; recapture is required.");
                }
                else if (zone.Status == "loaded")
                {
                    MarkPending(zone, "Zone loading was interrupted before its capture was committed.");
                }
            }
            Save();
        }

        // Recovers atomic files committed just before a crash interrupted manifest checkpointing.
        private void RecoverOrphanZones()
        {
            if (Manifest.FormatVersion != 2)
            {
                return;
            }
            var known = new System.Collections.Generic.HashSet<string>(
                Manifest.Zones.Select(zone => StoreValidation.ZoneFileName(zone.X, zone.Z)), StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "zone_*.worldcrawler.json"))
            {
                if (known.Contains(Path.GetFileName(path)))
                {
                    continue;
                }
                try
                {
                    var envelope = AtomicJson.Read<ZoneEnvelope>(path);
                    if (Path.GetFileName(path) != StoreValidation.ZoneFileName(envelope.X, envelope.Z))
                    {
                        continue;
                    }
                    ValidateEnvelope(envelope, envelope.X, envelope.Z);
                    if (!Manifest.Zones.Any(zone => zone.X == envelope.X && zone.Z == envelope.Z))
                    {
                        var zone = new ZoneEntry { X = envelope.X, Z = envelope.Z, Origin = ExplorationOrigin.Received };
                        MarkCaptured(zone, envelope);
                        Manifest.Zones.Add(zone);
                    }
                }
                catch (Exception error) when (IsInvalidFile(error))
                {
                    RecoveryNotice = "Invalid zone files were preserved and excluded: " + error.Message;
                }
            }
        }

        // Releases the exclusive directory lock without deleting captured data.
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                directoryLock.Dispose();
            }
        }

        // Loads checkpoints and preserves a corrupt primary before recovering its prior revision.
        private void LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                if (File.Exists(ManifestPath + ".previous"))
                {
                    Manifest = ReadManifest(ManifestPath + ".previous");
                    RecoveryNotice = "The primary manifest was missing; the previous checkpoint was recovered. ";
                    return;
                }
                Manifest = new WorldManifest { World = identity.Copy(), CreatedUtc = DateTime.UtcNow.ToString("o") };
                return;
            }
            try
            {
                Manifest = ReadManifest(ManifestPath);
            }
            catch (Exception error) when (IsInvalidFile(error) && File.Exists(ManifestPath + ".previous"))
            {
                Manifest = ReadManifest(ManifestPath + ".previous");
                var evidence = ManifestPath + ".invalid-" + Guid.NewGuid().ToString("N");
                File.Move(ManifestPath, evidence);
                RecoveryNotice = "The primary manifest was corrupt and preserved; the previous checkpoint was recovered. ";
            }
            Manifest.World.Name = identity.Name;
        }

        // Validates a manifest against the world before it can become active.
        private WorldManifest ReadManifest(string path)
        {
            var manifest = AtomicJson.Read<WorldManifest>(path);
            StoreValidation.Manifest(manifest, identity);
            return manifest;
        }

        // Locates only committed recording sectors.
        private ZoneEntry FindZone(int x, int z)
        {
            var entry = Manifest.Zones.SingleOrDefault(zone => zone.X == x && zone.Z == z);
            if (entry == null)
            {
                throw new InvalidOperationException("This zone is not in the committed recording.");
            }
            return entry;
        }

        // Resolves a generated basename instead of trusting a manifest-supplied path.
        private string ZonePath(int x, int z)
        {
            return Path.Combine(DirectoryPath, StoreValidation.ZoneFileName(x, z));
        }

        // Checks both storage integrity and any caller-supplied payload schema invariants.
        private byte[] ValidateEnvelope(ZoneEnvelope envelope, int x, int z)
        {
            var payload = StoreValidation.Envelope(envelope, identity, x, z);
            PayloadValidator?.Invoke(x, z, payload, envelope.CaptureVersion, envelope.ObjectCount);
            return payload;
        }

        // Records a fully validated committed zone, including recovered orphan files.
        private static void MarkCaptured(ZoneEntry entry, ZoneEnvelope envelope)
        {
            entry.Status = "captured";
            entry.FileName = StoreValidation.ZoneFileName(entry.X, entry.Z);
            entry.Checksum = envelope.Checksum;
            entry.ObjectCount = envelope.ObjectCount;
            entry.CaptureVersion = envelope.CaptureVersion;
            entry.UpdatedUtc = envelope.CapturedUtc;
            entry.FailureReason = null;
        }

        // Keeps damaged evidence on disk while making the affected zone retryable.
        private static void MarkPending(ZoneEntry entry, string reason)
        {
            entry.Status = "pending";
            entry.FailureReason = reason;
            entry.UpdatedUtc = DateTime.UtcNow.ToString("o");
        }

        // Separates recoverable corrupt data from filesystem permissions and lock failures.
        private static bool IsInvalidFile(Exception error)
        {
            return error is SerializationException || error is XmlException || error is InvalidDataException ||
                error is FormatException || error is EndOfStreamException || error is ArgumentException;
        }

        // Prevents writes after the exclusive directory lock has been released.
        private void EnsureOpen()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WorldStore));
            }
        }
    }
}
