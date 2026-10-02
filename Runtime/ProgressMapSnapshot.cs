using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Creates immutable map geometry from saved exports and restored destination zones.
    internal static class ProgressMapSnapshot
    {
        // Shows committed captures, retaining the last good rectangle while its replacement is being validated.
        internal static ExportMapOverlayData Export(WorldManifest manifest, ZoneEntry active)
        {
            var captured = manifest.Zones.Where(v => v.Status == "captured").ToList();
            return Build(captured, captured.Select(v => v.X + ":" + v.Z));
        }

        // Shows restored zones without waiting for their next native save.
        internal static ExportMapOverlayData Restore(IEnumerable<ZoneEntry> zones, IEnumerable<string> completed)
        {
            return Build(zones, completed);
        }

        // Separates unsaved received data from durable snapshots without claiming complete server coverage.
        internal static ExportMapOverlayData Recording(IEnumerable<ZoneEntry> saved, IEnumerable<ZoneEntry> pending)
        {
            var dirty = new HashSet<string>(pending.Select(zone => zone.X + ":" + zone.Z));
            return Build(saved.Concat(pending), saved.Where(zone => !dirty.Contains(zone.X + ":" + zone.Z))
                .Select(zone => zone.X + ":" + zone.Z));
        }

        // Splits the same unique sector inventory into completed and remaining rectangles.
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
