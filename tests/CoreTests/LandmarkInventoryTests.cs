using System;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Checks circular sector selection independently of game state and exploration pixels.
    internal static class LandmarkInventoryTests
    {
        // Runs boundary, overlap, identity, and malformed-input regression checks.
        internal static void Run()
        {
            CircleExcludesOuterCorners();
            TouchingEdgesAreIncluded();
            NegativeCornerCrossesFourZones();
            OverlapsAreDeterministic();
            DistantAndHighLandmarksAreIncluded();
            SavedMapPinsAreAccepted();
            InvalidInputsAreRejected();
            DuplicateIdentitiesAreRejected();
        }

        // Uses rectangle intersection rather than a square or a zone-centre distance test.
        private static void CircleExcludesOuterCorners()
        {
            var result = LandmarkZoneInventory.Build(Inventory(Point("home", 0, 0)), 120);
            TestSupport.Check(result.Zones.Count == 21, "A centred 120-metre disk must intersect 21 sectors.");
            TestSupport.Check(Contains(result, 2, 0), "An intersecting sector with its centre outside the disk was lost.");
            TestSupport.Check(!Contains(result, 2, 2), "A square corner outside the disk was included.");
            TestSupport.Check(result.Zones.All(zone => zone.Origin == ExplorationOrigin.PointOfInterest),
                "Radius selection must not claim these sectors were personally or jointly explored.");
        }

        // Preserves all four tangent neighbours and excludes diagonal sectors.
        private static void TouchingEdgesAreIncluded()
        {
            var result = LandmarkZoneInventory.Build(Inventory(Point("start", 0, 0)), 32);
            TestSupport.Check(result.Zones.Count == 5 && Contains(result, -1, 0) && Contains(result, 1, 0) &&
                Contains(result, 0, -1) && Contains(result, 0, 1), "Touching a boundary must include its neighbour.");
            var inside = LandmarkZoneInventory.Build(Inventory(Point("start", 0, 0)), 31.99f);
            TestSupport.Check(inside.Zones.Count == 1, "A disk inside a sector must not select its neighbours.");
        }

        // Uses floor-compatible coordinates on the negative side of the origin.
        private static void NegativeCornerCrossesFourZones()
        {
            var result = LandmarkZoneInventory.Build(Inventory(Point("portal", -32, -32)), 1);
            TestSupport.Check(result.Zones.Count == 4 && Contains(result, -1, -1) && Contains(result, -1, 0) &&
                Contains(result, 0, -1) && Contains(result, 0, 0), "Negative corner intersection is incorrect.");
        }

        // Makes duplicate coverage and input ordering irrelevant to the itinerary.
        private static void OverlapsAreDeterministic()
        {
            var first = Point("one", -50, 60);
            var second = Point("two", 25, 60);
            var third = Point("three", -50, 60);
            var forward = LandmarkZoneInventory.Build(Inventory(first, second, third), 120);
            var reverse = LandmarkZoneInventory.Build(Inventory(third, second, first), 120);
            var keys = forward.Zones.Select(zone => zone.X + "," + zone.Z).ToArray();
            TestSupport.Check(keys.Length == keys.Distinct().Count(), "Overlapping disks must not duplicate sectors.");
            TestSupport.Check(keys.SequenceEqual(reverse.Zones.Select(zone => zone.X + "," + zone.Z)),
                "Reversing landmark discovery order must not alter the ordered itinerary.");
            TestSupport.Check(forward.Zones.SequenceEqual(forward.Zones.OrderBy(zone => zone.Z).ThenBy(zone => zone.X)),
                "Landmark sectors must use the existing Z-then-X stable ordering.");
        }

        // Treats height as metadata and does not require any neighbouring exploration pixels.
        private static void DistantAndHighLandmarksAreIncluded()
        {
            var point = Point("quest", 6400, -6400);
            point.Kind = "hildir";
            point.Y = 5000;
            var result = LandmarkZoneInventory.Build(Inventory(point), 120);
            TestSupport.Check(result.Zones.Count == 21 && Contains(result, 100, -100),
                "Distant or elevated landmarks must select their horizontal surroundings.");
            TestSupport.Check(result.CombinedPixels == 0 && result.PersonalPixels == 0 && result.SharedPixels == 0,
                "Radius inventory must not invent exploration counts.");
            TestSupport.Check(LandmarkZoneInventory.Build(new LandmarkInventory(), 120).Zones.Count == 0,
                "An empty discovery should remain empty, not fall back to every explored zone.");
        }

        // Treats eligible saved map markers as destinations after runtime icon filtering.
        private static void SavedMapPinsAreAccepted()
        {
            var point = Point("marker", 128, 64);
            point.Kind = "pin";
            point.Name = "Saved camp";
            point.Source = "saved-map-pin";
            var result = LandmarkZoneInventory.Build(Inventory(point), 120);
            TestSupport.Check(result.Zones.Count == 21 && Contains(result, 2, 1),
                "A generic saved map pin must contribute its surrounding sectors.");
        }

        // Rejects values that cannot safely describe a bounded finite itinerary.
        private static void InvalidInputsAreRejected()
        {
            var valid = Inventory(Point("valid", 0, 0));
            foreach (var radius in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 1001f })
            {
                TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Build(valid, radius));
            }
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Build(null, 120));
            valid.Points[0].X = float.NaN;
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Build(valid, 120));
            valid.Points[0].X = LandmarkZoneInventory.MaximumCoordinate + 1;
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Build(valid, 120));
            valid.Points[0].X = 0;
            valid.Points[0].Kind = "creature";
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Build(valid, 120));
        }

        // Distinguishes different colocated places from conflicting records of one identity.
        private static void DuplicateIdentitiesAreRejected()
        {
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Validate(
                Inventory(Point("same", 0, 0), Point("same", 100, 100)), 120));
            var unnamed = Point("portal", 0, 0);
            unnamed.Name = "";
            LandmarkZoneInventory.Validate(Inventory(unnamed), 1000);
            unnamed.Source = null;
            TestSupport.Throws<ArgumentException>(() => LandmarkZoneInventory.Validate(Inventory(unnamed), 120));
        }

        // Constructs a minimal discovered-place snapshot for one geometry case.
        private static LandmarkInventory Inventory(params LandmarkPoint[] points)
        {
            var inventory = new LandmarkInventory();
            inventory.Points.AddRange(points);
            return inventory;
        }

        // Supplies valid metadata while letting each test specify horizontal coordinates.
        private static LandmarkPoint Point(string id, float x, float z)
        {
            return new LandmarkPoint { Id = id, Kind = "portal", Name = id, Source = "test", X = x, Y = 0, Z = z };
        }

        // Locates a sector without depending on presentation labels or output ordering.
        private static bool Contains(InventoryResult result, int x, int z)
        {
            return result.Zones.Any(zone => zone.X == x && zone.Z == z);
        }
    }
}
