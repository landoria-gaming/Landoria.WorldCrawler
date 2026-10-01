using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Reads a frozen export without rewriting its manifest or capture files.
    internal sealed class ExportArchive : IDisposable
    {
        private readonly FileStream _lock;
        private readonly Dictionary<string, CapturedObject> _latest = new Dictionary<string, CapturedObject>();
        public string DirectoryPath
        {
            get;
        }
        public WorldManifest Manifest
        {
            get;
        }
        public string Fingerprint
        {
            get;
        }
        public string SeriesIdentity
        {
            get;
        }
        public int PlannedZoneCount
        {
            get;
        }
        public IEnumerable<CapturedObject> Records => _latest.Values;
        public CapturedObject[] Connections
        {
            get;
        }

        // Locks and validates the entire export before permitting any world mutation.
        public ExportArchive(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory);
            _lock = new FileStream(Path.Combine(DirectoryPath, ".worldcrawler.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                Manifest = AtomicJson.Read<WorldManifest>(Path.Combine(DirectoryPath, "manifest.json"));
                StoreValidation.Manifest(Manifest, Manifest.World);
                PlannedZoneCount = Manifest.Zones.Count;
                Manifest.Zones = Manifest.Zones.Where(zone => zone.Status == "captured").ToList();
                if (!Manifest.InventoryInitialized || Manifest.Zones.Count == 0)
                {
                    throw new InvalidDataException("No saved zones are available. Export at least one zone with F8 first.");
                }
                SeriesIdentity = ExportSeries.Identity(Manifest, DirectoryPath);
                var signature = new StringBuilder();
                foreach (var zone in Manifest.Zones.OrderBy(z => z.Z).ThenBy(z => z.X))
                {
                    var snapshot = ReadZone(zone);
                    signature.Append(zone.FileName).Append(':').Append(zone.Checksum).Append('\n');
                    foreach (var item in snapshot.Objects)
                    {
                        Index(item);
                    }
                }
                Fingerprint = StoreValidation.Hash(Encoding.UTF8.GetBytes(signature.ToString()));
                Connections = _latest.Values.Where(v => v.ConnectionType != 0).ToArray();
            }
            catch
            {
                _lock.Dispose();
                throw;
            }
        }

        // Checks both the envelope and its inner payload every time a zone is used.
        public ZoneSnapshot ReadZone(ZoneEntry zone)
        {
            var file = Path.Combine(DirectoryPath, StoreValidation.ZoneFileName(zone.X, zone.Z));
            var envelope = AtomicJson.Read<ZoneEnvelope>(file);
            var data = StoreValidation.Envelope(envelope, Manifest.World, zone.X, zone.Z);
            if (envelope.Checksum != zone.Checksum || envelope.ObjectCount != zone.ObjectCount ||
                envelope.CaptureVersion != zone.CaptureVersion ||
                !SupportedGameVersions.CanExport(envelope.CaptureVersion))
            {
                throw new InvalidDataException("The export manifest and its zone file disagree.");
            }
            var snapshot = ZoneSnapshot.Decode(data);
            if (snapshot.ZoneX != zone.X || snapshot.ZoneZ != zone.Z || snapshot.Objects.Count != zone.ObjectCount)
            {
                throw new InvalidDataException("The zone payload does not match its envelope.");
            }
            return snapshot;
        }

        // Records exact accepted captures while allowing additional zones in the same export series.
        public Dictionary<string, string> ZoneSignatures()
        {
            return Manifest.Zones.ToDictionary(zone => zone.X.ToString(CultureInfo.InvariantCulture) + ":" +
                zone.Z.ToString(CultureInfo.InvariantCulture), zone => zone.CaptureVersion + ":" +
                zone.ObjectCount.ToString(CultureInfo.InvariantCulture) + ":" + zone.Checksum, StringComparer.Ordinal);
        }

        // Refuses changed or removed old captures before extending an existing restoration.
        public void RequireUnchanged(Dictionary<string, string> previous)
        {
            var current = ZoneSignatures();
            if (previous == null || previous.Any(entry => !current.TryGetValue(entry.Key, out var value) || value != entry.Value))
            {
                throw new InvalidDataException("Previously accepted zone captures changed or disappeared. Restore their original files before resuming this target.");
            }
        }

        // Deduplicates moving objects across sectors by source identity and observation time.
        private void Index(CapturedObject item)
        {
            var key = Key(item);
            if (_latest.TryGetValue(key, out var previous))
            {
                if (previous.PrefabHash != item.PrefabHash)
                {
                    throw new InvalidDataException("A source identity refers to different prefabs.");
                }
                if (previous.ObservedUtcTicks >= item.ObservedUtcTicks)
                {
                    return;
                }
            }
            item.RawDataBase64 = null;
            _latest[key] = item;
        }

        // Returns the latest unique observations owned by a target sector.
        public List<CapturedObject> ZoneObjects(int x, int z)
        {
            var zone = Manifest.Zones.Single(v => v.X == x && v.Z == z);
            return ReadZone(zone).Objects.Where(v => _latest[Key(v)].ZoneX == x && _latest[Key(v)].ZoneZ == z &&
                    _latest[Key(v)].ObservedUtcTicks == v.ObservedUtcTicks)
                .OrderBy(v => v.Categories.Contains("TerrainComp") || v.Categories.Contains("TerrainModifier") ? 0 :
                    v.Categories.Contains("LocationProxy") ? 1 : v.Categories.Contains("Piece") ? 2 :
                    v.Categories.Contains("ItemDrop") ? 4 : 3)
                .ThenBy(Key, StringComparer.Ordinal).ToList();
        }

        // Uses invariant original identifiers rather than position-based identity.
        public static string Key(CapturedObject item)
        {
            return item.SourceUser + ":" + item.SourceId.ToString(CultureInfo.InvariantCulture);
        }

        // Releases the export lock without changing export progress.
        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
