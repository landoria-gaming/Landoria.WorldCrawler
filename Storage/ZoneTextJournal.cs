using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Keeps an append-only text history of committed zone exports.
    internal static class ZoneTextJournal
    {
        internal const string FileName = "export-summary.txt";

        // Appends counts after a successful save, allowing viewers to read the existing history.
        internal static void Append(string directory, ZoneSnapshot snapshot, string version,
            IDictionary<string, string> labels)
        {
            var block = new StringBuilder();
            block.AppendLine("============================================================");
            block.AppendLine("Capture UTC: " + new DateTime(snapshot.FinishedUtcTicks, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            block.AppendLine($"Zone: {snapshot.ZoneX}, {snapshot.ZoneZ} | Valheim: {version}");
            block.AppendLine($"Total: {snapshot.Objects.Count} exported persistent objects");
            block.AppendLine("Counts by type (internal prefab names in brackets):");
            foreach (var group in snapshot.Objects.GroupBy(item => item.PrefabName)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                var label = labels.TryGetValue(group.Key, out var name) ? name : group.Key;
                block.AppendLine($"  {group.Count(),6} x {Line(label)} [{Line(group.Key)}]");
            }
            if (snapshot.Objects.Count == 0)
            {
                block.AppendLine("  No persistent objects.");
            }
            block.AppendLine("Container contents are stored in their data and are not counted as separate objects.");
            block.AppendLine();
            using (var stream = new FileStream(Path.Combine(directory, FileName), FileMode.Append,
                FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(block.ToString());
            }
        }

        // Keeps object names on a single report line.
        private static string Line(string value)
        {
            return (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }
    }
}
