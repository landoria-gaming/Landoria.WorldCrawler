using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.WorldPreparation
{
    // Chooses the most recently captured export, independently of the previous F9 selection.
    internal static class LatestExport
    {
        // Requires saved zones and never falls back after the chosen archive fails full validation.
        internal static string Find(string root)
        {
            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException("No export found. Save at least one zone with F8 first.");
            }
            var candidates = Directory.GetDirectories(root).Select(directory =>
                new { Directory = directory, Timestamp = ReadTimestamp(directory) })
                .Where(v => v.Timestamp.HasValue).OrderByDescending(v => v.Timestamp.Value)
                .ThenBy(v => v.Directory, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No export contains a saved zone. Use F8 first.");
            }
            return candidates[0].Directory;
        }

        // Uses capture timestamps rather than folder-copy times or a later inspection of an old export.
        private static DateTimeOffset? ReadTimestamp(string directory)
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path))
            {
                return null;
            }
            var manifest = AtomicJson.Read<WorldManifest>(path);
            if (manifest?.Zones == null)
            {
                throw new InvalidDataException("Unreadable export manifest: " + path);
            }
            var captured = manifest.Zones.Where(v => v.Status == "captured").ToList();
            if (!manifest.InventoryInitialized || captured.Count == 0)
            {
                return null;
            }
            return captured.Max(zone => Timestamp(zone.UpdatedUtc ?? manifest.UpdatedUtc ?? manifest.CreatedUtc));
        }

        // Refuses an ambiguous timestamp instead of silently selecting another world.
        private static DateTimeOffset Timestamp(string value)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                throw new InvalidDataException("An export has no valid capture timestamp.");
            }
            return parsed;
        }
    }
}
