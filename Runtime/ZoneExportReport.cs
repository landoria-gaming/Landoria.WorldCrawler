using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Runtime
{
    // Produces stable English prefab labels independently of the game's selected language.
    internal static class ZoneExportReport
    {
        // Spaces internal prefab identifiers for readability while preserving their exact names in the report.
        internal static Dictionary<string, string> Labels(ZoneSnapshot snapshot)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in snapshot.Objects.GroupBy(value => value.PrefabName).Select(group => group.First()))
            {
                labels[item.PrefabName] = item.PrefabName.Replace('_', ' ');
            }
            return labels;
        }
    }
}
