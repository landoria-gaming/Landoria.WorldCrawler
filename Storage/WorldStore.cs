using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Persistence;

namespace Landoria.WorldCrawler.Storage
{
    // Owns an exclusive, restartable world export with one committed file per zone.
    public sealed partial class WorldStore : IDisposable
    {
        private readonly WorldIdentity identity;
        private readonly FileStream directoryLock;
        private bool disposed;
        internal readonly System.Collections.Generic.HashSet<string> RecordedObjects =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
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
        public static WorldStore Open(string root, WorldIdentity world)
        {
            if (world == null || world.SeedText == null || world.Name == null)
            {
                throw new ArgumentException("A complete connected world identity is required.");
            }
            var directory = Path.Combine(Path.GetFullPath(root), StoreValidation.DirectoryName(world));
            Directory.CreateDirectory(directory);
            var worldLock = new FileStream(Path.Combine(directory, ".worldcrawler.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var store = new WorldStore(directory, world, worldLock);
            try
            {
                store.LoadManifest();
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Initializes a recording without changing legacy export folders or creating a route.
        public void BeginRecording(string gameVersion)
        {
            EnsureOpen();
            if (Manifest.FormatVersion != 2)
            {
                throw new NotSupportedException("This folder contains a legacy export; it is preserved read-only.");
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
        private void WriteZone(int x, int z, byte[] payload, string captureVersion, int objects)
        {
            EnsureOpen();
            var entry = Manifest.Zones.SingleOrDefault(zone => zone.X == x && zone.Z == z)
                ?? new ZoneEntry { X = x, Z = z, Origin = ExplorationOrigin.Received };
            if (payload == null || payload.Length > AtomicJson.MaximumFileLength / 2 ||
                objects < 0 || string.IsNullOrEmpty(captureVersion))
            {
                throw new ArgumentException("A bounded payload, version, and valid object count are required.");
            }
            PayloadValidator?.Invoke(x, z, payload, captureVersion, objects);
            var snapshot = Capture.ZoneSnapshot.Decode(payload);
            payload = snapshot.Encode();
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
                Payload = snapshot
            };
            AtomicJson.Write(ZonePath(x, z), envelope, value => ValidateEnvelope(value, x, z));
            CommitZone(entry, envelope, snapshot);
        }

        // Publishes zone metadata and the append-only source identity index after its file is committed.
        private void CommitZone(ZoneEntry entry, ZoneEnvelope envelope, Capture.ZoneSnapshot snapshot)
        {
            var names = Manifest.ReceivedPrefabs ?? new System.Collections.Generic.List<string>();
            Manifest.ReceivedPrefabs = names.Concat(snapshot.Objects.Select(item => item.PrefabName))
                .Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal).ToList();
            if (!Manifest.Zones.Contains(entry))
            {
                Manifest.Zones.Add(entry);
            }
            MarkCaptured(entry, envelope);
            Save();
            RecordedObjects.UnionWith(snapshot.Objects.Select(item => item.SourceUser + ":" + item.SourceId));
        }

        // Recovers valid orphan writes and makes damaged or interrupted captures retryable.
        public void Reconcile()
        {
            EnsureOpen();
            RecordedObjects.Clear();
            RecoverOrphanZones();
            var received = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in Manifest.Zones)
            {
                var path = ZonePath(zone.X, zone.Z);
                if (File.Exists(path))
                {
                    try
                    {
                        var envelope = AtomicJson.Read<ZoneEnvelope>(path);
                        ValidateEnvelope(envelope, zone.X, zone.Z);
                        received.UnionWith(envelope.Payload.Objects.Select(item => item.PrefabName));
                        RecordedObjects.UnionWith(envelope.Payload.Objects.Select(item => item.SourceUser + ":" + item.SourceId));
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
            Manifest.ReceivedPrefabs = received.Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal).ToList();
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

        // Requires the primary manifest for every existing export folder.
        private void LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                if (Directory.EnumerateFileSystemEntries(DirectoryPath)
                    .Any(path => Path.GetFileName(path) != ".worldcrawler.lock"))
                {
                    throw new FileNotFoundException("An existing export folder has no manifest.json.", ManifestPath);
                }
                Manifest = new WorldManifest { World = identity.Copy(), CreatedUtc = DateTime.UtcNow.ToString("o") };
                return;
            }
            Manifest = ReadManifest(ManifestPath);
            Manifest.World.Name = identity.Name;
        }

        // Validates a manifest against the world before it can become active.
        private WorldManifest ReadManifest(string path)
        {
            var manifest = AtomicJson.Read<WorldManifest>(path);
            StoreValidation.Manifest(manifest, identity);
            return manifest;
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
