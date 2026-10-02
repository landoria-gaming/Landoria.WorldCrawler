using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Creates immutable map geometry from actual durable export or import checkpoints.
    internal static class ProgressMapSnapshot
    {
        // Groups exported zones without duplicating shared coordinates or treating an active recapture as done.
        internal static ExportMapOverlayData Export(WorldManifest manifest, ZoneEntry active)
        {
            return Build(manifest.Zones, manifest.Zones.Where(v => v.Status == "captured" && v != active)
                .Select(v => v.X + ":" + v.Z));
        }

        // Uses import completion rather than export completion when showing the destination world.
        internal static ExportMapOverlayData Restore(IEnumerable<ZoneEntry> zones, IEnumerable<string> completed)
        {
            return Build(zones, completed);
        }

        // Splits the same unique sector inventory into saved and remaining rectangles.
        private static ExportMapOverlayData Build(IEnumerable<ZoneEntry> zones, IEnumerable<string> completed)
        {
            var saved = new HashSet<string>(completed);
            var selected = zones.Where(v => v.Status != "skipped").GroupBy(v => v.X + ":" + v.Z)
                .Select(v => v.First()).ToList();
            return new ExportMapOverlayData
            {
                Captured = ExportMapOverlayGeometry.Merge(selected.Where(v => saved.Contains(v.X + ":" + v.Z))),
                Remaining = ExportMapOverlayGeometry.Merge(selected.Where(v => !saved.Contains(v.X + ":" + v.Z)))
            };
        }
    }
}
