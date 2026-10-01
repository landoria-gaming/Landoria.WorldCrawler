using System.Linq;
using BepInEx.Logging;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Makes the actual eligible point list and its completeness limits visible before flight.
    internal static class SelectionReport
    {
        // Reports the persisted inventory rather than guessing a server-wide portal count.
        public static void Write(WorldStore store, ManualLogSource log)
        {
            var selection = store.Manifest.Selection;
            if (selection == null) { return; }
            var counts = string.Join(", ", selection.Points.GroupBy(p => p.Kind)
                .OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count()));
            var summary = $"Known locations: {selection.Points.Count} | radius {selection.Radius:0} m | {store.Manifest.Zones.Count} zones";
            log.LogInfo(summary + " | " + counts);
            foreach (var point in selection.Points)
            {
                var name = point.Name.Replace('\r', ' ').Replace('\n', ' ');
                log.LogInfo($"Landmark [{point.Kind}] {name}: X={point.X:0.0}, Z={point.Z:0.0}; source={point.Source}.");
            }
            foreach (var warning in selection.Warnings) { log.LogWarning(warning); }
            HudNotification.Show(summary);
        }
    }
}
