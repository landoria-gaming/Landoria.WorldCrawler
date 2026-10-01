using System;
using System.Collections;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Exercises map unions, negative boundaries, and conservative pixel footprints.
    internal static class InventoryTests
    {
        // Runs the inventory checks without game or network dependencies.
        internal static void Run()
        {
            PixelCrossesZoneBoundary();
            ExactNegativeBoundary();
            UnionIsDeterministic();
            TestSupport.Throws<ArgumentException>(() => ExploredZoneInventory.Build(new BitArray(3), new BitArray(3), 2, 12));
            TestSupport.Throws<ArgumentException>(() => ExploredZoneInventory.Build(new BitArray(4), new BitArray(4), 2, double.NaN));
        }

        // Keeps both sides when one revealed pixel straddles a zone boundary.
        private static void PixelCrossesZoneBoundary()
        {
            var personal = new BitArray(64);
            personal[4 * 8 + 7] = true;
            var result = ExploredZoneInventory.Build(personal, new BitArray(64), 8, 12);
            TestSupport.Check(result.Zones.Count == 2, "A straddling pixel must contribute both zones.");
            TestSupport.Check(result.Zones[0].X == 0 && result.Zones[1].X == 1 &&
                result.Zones.All(zone => zone.Z == 0), "Unexpected footprint bounds.");
        }

        // Keeps half-open edges from creating a spurious adjacent negative zone.
        private static void ExactNegativeBoundary()
        {
            var personal = new BitArray(4);
            var shared = new BitArray(4);
            personal[3] = true;
            shared[2] = true;
            var result = ExploredZoneInventory.Build(personal, shared, 2, 64);
            TestSupport.Check(result.Zones.Count == 2, "Exact boundary pixels should map to two zones total.");
            TestSupport.Check(result.Zones[0].X == -1 && result.Zones[1].X == 0 &&
                result.Zones.All(zone => zone.Z == 0), "Negative half-open zone boundaries are incorrect.");
        }

        // Retains shared origins while deduplicating pixels and sorted zone coordinates.
        private static void UnionIsDeterministic()
        {
            var personal = new BitArray(64);
            var shared = new BitArray(64);
            personal[39] = shared[39] = shared[32] = true;
            var result = ExploredZoneInventory.Build(personal, shared, 8, 12);
            var repeat = ExploredZoneInventory.Build(personal, shared, 8, 12);
            TestSupport.Check(result.PersonalPixels == 1 && result.SharedPixels == 2 && result.CombinedPixels == 2,
                "Exploration counts must count the union once.");
            TestSupport.Check(result.Zones[0].Origin == ExplorationOrigin.Shared &&
                result.Zones.Skip(1).All(zone => zone.Origin == (ExplorationOrigin.Personal | ExplorationOrigin.Shared)),
                "Zone exploration origin was lost.");
            TestSupport.Check(string.Join(";", result.Zones.Select(zone => zone.X + "," + zone.Z)) ==
                string.Join(";", repeat.Zones.Select(zone => zone.X + "," + zone.Z)), "Inventory ordering must be deterministic.");
        }
    }
}
