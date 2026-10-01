using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Checks landmark resume and separate export scopes without touching game files.
    internal static class LandmarkStorageTests
    {
        // Exercises selection boundaries, preserved progress, and independent export scopes.
        internal static void Run()
        {
            TestSupport.InDirectory(ScopesAreSafeAndSeparate);
            TestSupport.InDirectory(MergeRetainsPointsAndProgress);
            TestSupport.InDirectory(MergeRefreshesMapPins);
            TestSupport.InDirectory(MergeDropsUndiscoveredMapLocations);
            TestSupport.InDirectory(RejectSelectionChanges);
            TestSupport.InDirectory(LegacySelectionRemainsOptional);
            TestSupport.InDirectory(RejectMalformedSelection);
        }

        // Supplies a deterministic source world independent of the user's data.
        private static WorldIdentity World(long uid = 500)
        {
            return new WorldIdentity { Name = "Source", Uid = uid, Seed = 42,
                SeedText = "fixed", GenerationVersion = 1 };
        }

        // Creates one named source landmark at a known zone center.
        private static LandmarkPoint Point(string id, float x, string name = "Portal")
        {
            return new LandmarkPoint { Id = id, Kind = "portal", Name = name,
                Source = "test", X = x, Y = 40, Z = 0 };
        }

        // Builds a landmark observation with one reproducible diagnostic warning.
        private static LandmarkInventory Points(params LandmarkPoint[] points)
        {
            return new LandmarkInventory { Points = points.ToList(), Warnings = new List<string> { "Cache is partial." } };
        }

        // Opens an isolated selection directory and initializes its target circles.
        private static WorldStore Selected(string root, params LandmarkPoint[] points)
        {
            var store = WorldStore.Open(root, World(), reconcile: false, scope: "landmarks_r10");
            store.SelectLandmarks(Points(points), 10f, "character", "Test");
            return store;
        }

        // Ensures a selection cannot collide with or escape the legacy full-map directory.
        private static void ScopesAreSafeAndSeparate(string root)
        {
            string legacy;
            using (var store = WorldStore.Open(root, World())) { legacy = store.DirectoryPath; }
            using (var store = Selected(root, Point("a", 0)))
            {
                TestSupport.Check(store.DirectoryPath != legacy && store.DirectoryPath.EndsWith("_landmarks_r10"),
                    "Scoped selection must use a distinct directory suffix.");
                TestSupport.Check(Path.GetDirectoryName(store.DirectoryPath) == root, "Scope escaped the export root.");
            }
            var directories = Directory.GetDirectories(root).Length;
            foreach (var scope in new[] { "", "../bad", "/bad", "x.y", "two words", "é", new string('a', 61) })
            {
                TestSupport.Throws<ArgumentException>(() => WorldStore.Open(root, World(), scope: scope));
            }
            TestSupport.Check(Directory.GetDirectories(root).Length == directories, "Invalid scope created a directory.");
        }

        // Retains unseen cached points, completed coordinates, and the original return checkpoint.
        private static void MergeRetainsPointsAndProgress(string root)
        {
            string previousCapture;
            using (var store = Selected(root, Point("a", 0), Point("b", 128)))
            {
                store.WriteZone(0, 0, new byte[] { 1, 2 }, "0.221.12", 1);
                previousCapture = Path.Combine(store.DirectoryPath, StoreValidation.ZoneFileName(0, 0));
                SaveReturn(store);
                store.SelectLandmarks(Points(Point("a", 0, "Renamed"), Point("c", 256)), 10, "character", "Test");
                TestSupport.Check(store.Manifest.Selection.Points.Count == 3 && store.Manifest.Selection.Warnings.Count == 1,
                    "Selection must retain absent points and deduplicate repeated warnings.");
                TestSupport.Check(store.Manifest.Zones.Single(zone => zone.X == 0).Status == "captured",
                    "Selection expansion reset a completed coordinate.");
                TestSupport.Check(store.Manifest.ReturnPending && store.Manifest.ReturnPosition[1] == 60,
                    "Selection changed the pending return checkpoint.");
            }
            using (var store = Selected(root, Point("a", 64)))
            {
                TestSupport.Check(store.Manifest.Selection.Points.Single(point => point.Id == "a").X == 64,
                    "A new observation must replace the same point ID.");
                TestSupport.Check(store.Manifest.Zones.All(zone => zone.X != 0) && File.Exists(previousCapture),
                    "Removed coordinates must not delete old capture files.");
                TestSupport.Check(store.Manifest.Selection.Points.Count == 3, "Resume lost unseen known points.");
                store.SelectLandmarks(Points(Point("a", 0)), 10, "character", "Test");
                store.Reconcile();
                TestSupport.Check(store.Manifest.Zones.Single(zone => zone.X == 0).Status == "captured",
                    "A previously removed coordinate should recover its untouched capture when selected again.");
            }
        }

        // Removes cached personal/shared pins and repopulates only current personal observations.
        private static void MergeRefreshesMapPins(string root)
        {
            var personal = Point("pin:1:old", 64, "Old personal"); personal.Source = "personal-map-pin";
            var shared = Point("pin:2:shared", 128, "Old shared"); shared.Source = "shared-map-pin";
            var portal = Point("portal:1", 192, "Portal"); portal.Source = "received-portal-zdo";
            using (var store = Selected(root, personal, shared, portal))
            {
                var current = Point("pin:1:new", 256, "Current personal"); current.Source = "personal-map-pin";
                store.SelectLandmarks(Points(current), 10, "character", "Test");
                var ids = store.Manifest.Selection.Points.Select(point => point.Id).ToList();
                TestSupport.Check(ids.SequenceEqual(new[] { "pin:1:new", "portal:1" }),
                    "Map-pin refresh retained removed personal/shared pins or dropped a physical landmark.");
            }
        }

        // Drops stale quest markers while retaining completed shared coordinates and physical evidence.
        private static void MergeDropsUndiscoveredMapLocations(string root)
        {
            var hidden = Point("hildir:hidden", 128); hidden.Source = "native-location-icon";
            var combined = Point("boss:hidden", 192); combined.Source = "personal-map-pin+native-location-icon";
            var known = Point("merchant:known", 0); known.Source = "native-location-icon";
            var portal = Point("portal:physical", 0); portal.Source = "received-portal-zdo";
            using (var store = Selected(root, hidden, combined, known, portal))
            {
                store.WriteZone(0, 0, new byte[] { 1, 2 }, "0.221.12", 1);
                SaveReturn(store);
                store.SelectLandmarks(Points(known), 10, "character", "Test");
                TestSupport.Check(store.Manifest.Selection.Points.Count == 2 &&
                    store.Manifest.Selection.Points.All(point => !point.Id.EndsWith(":hidden")),
                    "Undiscovered native or combined map markers survived a fresh map observation.");
                TestSupport.Check(store.Manifest.Zones.Count == 1 && store.Manifest.Zones[0].Status == "captured",
                    "Refreshing discovered destinations lost captured overlap or retained hidden-only zones.");
                TestSupport.Check(store.Manifest.ReturnPending, "Map refresh erased the return checkpoint.");
            }
        }

        // Records an owned return origin that selection updates must leave untouched.
        private static void SaveReturn(WorldStore store)
        {
            store.Manifest.ReturnPending = true;
            store.Manifest.ReturnCharacterId = "character";
            store.Manifest.ReturnPosition = new[] { 1f, 60f, 3f };
            store.Manifest.ReturnRotation = new[] { 0f, 0f, 0f, 1f };
            store.Save();
        }

        // Rejects changes to the radius, source character, or frozen-inventory mode.
        private static void RejectSelectionChanges(string root)
        {
            using (var store = Selected(root, Point("a", 0)))
            {
                TestSupport.Throws<InvalidOperationException>(() => store.SelectLandmarks(Points(Point("a", 0)), 20, "character", "Test"));
                TestSupport.Throws<InvalidOperationException>(() => store.SelectLandmarks(Points(Point("a", 0)), 10, "other", "Test"));
                TestSupport.Throws<InvalidOperationException>(() => store.InitializeInventory(new InventoryResult(), "character", "Test"));
            }
            using (var store = WorldStore.Open(root, World()))
            {
                store.InitializeInventory(new InventoryResult(), "character", "Test");
                TestSupport.Throws<InvalidOperationException>(() => store.SelectLandmarks(Points(Point("a", 0)), 10, "character", "Test"));
            }
        }

        // Creates a legacy full-map inventory with three committed zone envelopes.
        private static string Legacy(string root, WorldIdentity world, string character = "character")
        {
            using (var store = WorldStore.Open(root, world))
            {
                var inventory = new InventoryResult();
                foreach (var x in new[] { 0, 2, 4 })
                {
                    inventory.Zones.Add(new ZoneEntry { X = x, Z = 0, Origin = ExplorationOrigin.Personal });
                }
                store.InitializeInventory(inventory, character, "Test");
                foreach (var x in new[] { 0, 2, 4 }) { store.WriteZone(x, 0, new byte[] { (byte)(x + 1) }, "0.221.12", 1); }
                return store.DirectoryPath;
            }
        }

        // Reads and resumes old manifests without adding an optional selection member.
        private static void LegacySelectionRemainsOptional(string root)
        {
            var legacy = Legacy(root, World());
            var path = Path.Combine(legacy, "manifest.json");
            TestSupport.Check(!File.ReadAllText(path).Contains("\"Selection\""), "Legacy manifests should omit selection.");
            using (var store = WorldStore.Open(root, World()))
            {
                TestSupport.Check(store.Manifest.Selection == null && store.Manifest.Zones.Count == 3,
                    "Optional selection broke legacy progress.");
            }
        }

        // Validates stored selection metadata without accepting unknown modes or duplicate point IDs.
        private static void RejectMalformedSelection(string root)
        {
            using (var store = Selected(root, Point("a", 0)))
            {
                store.Manifest.Selection.Points.Add(Point("a", 64));
                TestSupport.Throws<InvalidDataException>(() => store.Save());
                store.Manifest.Selection.Points.RemoveAt(1);
                store.Manifest.Selection.Mode = "future";
                TestSupport.Throws<NotSupportedException>(() => store.Save());
            }
        }

    }
}
