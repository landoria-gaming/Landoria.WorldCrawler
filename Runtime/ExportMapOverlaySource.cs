using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads a bounded committed manifest without acquiring or modifying its writer lock.
    internal static class ExportMapOverlaySource
    {
        private const long MaximumManifestLength = 64L * 1024 * 1024;
        private const int MaximumZoneCount = 300000;

        // Returns only successful captures whose files still exist in the chosen selection.
        public static ExportMapOverlayRegion[] Read(string directory, WorldIdentity world,
            string characterId, int radius)
        {
            return ReadProgress(directory, world, characterId, radius).Captured;
        }

        // Returns separately merged committed and unfinished sectors from one validated checkpoint.
        public static ExportMapOverlayData ReadProgress(string directory, WorldIdentity world,
            string characterId, int radius)
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path))
            {
                return new ExportMapOverlayData();
            }
            var manifest = ReadManifest(path);
            StoreValidation.Manifest(manifest, world);
            if (!manifest.InventoryInitialized)
            {
                return new ExportMapOverlayData();
            }
            if (!string.Equals(manifest.CharacterId, characterId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The export map belongs to another character.");
            }
            ValidateSelection(manifest, radius);
            if (manifest.Zones.Count > MaximumZoneCount)
            {
                throw new InvalidDataException("The export map exceeds the supported sector count.");
            }
            var captured = manifest.Zones.Where(zone => Captured(directory, zone));
            var remaining = manifest.Zones.Where(zone => zone.Status != "skipped" && !Captured(directory, zone));
            return new ExportMapOverlayData
            {
                Captured = ExportMapOverlayGeometry.Merge(captured),
                Remaining = ExportMapOverlayGeometry.Merge(remaining)
            };
        }

        // Requires both committed metadata and the corresponding canonical zone file.
        private static bool Captured(string directory, ZoneEntry zone)
        {
            return zone.Status == "captured" && zone.FileName == StoreValidation.ZoneFileName(zone.X, zone.Z)
                && !string.IsNullOrEmpty(zone.Checksum) && File.Exists(Path.Combine(directory, zone.FileName));
        }

        // Allows atomic file replacement during reading without permitting in-place writes.
        private static WorldManifest ReadManifest(string path)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (input.Length > MaximumManifestLength)
                {
                    throw new InvalidDataException("The export map manifest exceeds 64 MiB.");
                }
                var serializer = new DataContractJsonSerializer(typeof(WorldManifest),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 8000000 });
                return (WorldManifest)serializer.ReadObject(input);
            }
        }

        // Refuses files moved into the wrong landmark radius directory.
        private static void ValidateSelection(WorldManifest manifest, int radius)
        {
            if (manifest.Selection == null || manifest.Selection.Mode != "landmarks" || manifest.Selection.Radius != radius)
            {
                throw new InvalidDataException("The export map selection does not match the landmark radius.");
            }
        }
    }
}
