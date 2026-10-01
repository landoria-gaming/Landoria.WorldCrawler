using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Checks map coverage and read-only progress selection without starting Unity.
    internal static class ExportMapOverlayTests
    {
        // Covers native projection, clipping, scope boundaries, absent files, and immutable reads.
        internal static void Run()
        {
            ProjectionMatchesNativeMap();
            ClipsZoomedRectangles();
            MergesWithoutBridgingGaps();
            TestSupport.InDirectory(ReadsOnlyCompletedExistingCaptures);
            TestSupport.InDirectory(RejectsForeignWorldCharacterAndScope);
            TestSupport.InDirectory(MissingAndCorruptSourcesAreReadOnly);
            TestSupport.InDirectory(LandmarkScopeIsChecked);
        }

        // Supplies a reproducible world unrelated to user saves.
        private static WorldIdentity World()
        { return new WorldIdentity { Name = "Overlay test", Uid = 701, Seed = 23, SeedText = "overlay", GenerationVersion = 1 }; }

        // Checks that world origin and signed sectors follow the game's UV convention exactly.
        private static void ProjectionMatchesNativeMap()
        {
            TestSupport.Check(ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(0, 0, 0),
                1024, 16, 0, 0, 1, 1, out var origin), "Origin sector should be visible.");
            Near(origin.Left, 0.5 - 32.0 / 16384); Near(origin.Right, 0.5 + 32.0 / 16384);
            Near(origin.Bottom, origin.Left); Near(origin.Top, origin.Right);
            ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(1, 1, -1), 1024, 16,
                0, 0, 1, 1, out var signed);
            Near(signed.Left, 0.5 + 32.0 / 16384); Near(signed.Top, 0.5 - 32.0 / 16384);
            ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(0, 0, 0), 1023, 16,
                0, 0, 1, 1, out var odd);
            Near((odd.Left + odd.Right) / 2, 511.0 / 1023);
        }

        // Covers zoom, pan, all viewport edges, and invalid or completely offscreen coordinates.
        private static void ClipsZoomedRectangles()
        {
            ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(0, 1, 0), 1024, 16,
                0.5, 0.5, 0.01, 0.01, out var clipped);
            Near(clipped.Left, 0); Near(clipped.Bottom, 0);
            Near(clipped.Right, 96.0 / 16384 / 0.01); Near(clipped.Top, 32.0 / 16384 / 0.01);
            ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(0, 1, 0), 1024, 16,
                0.499, 0.499, 0.001, 0.001, out var filled);
            Near(filled.Left, 0); Near(filled.Bottom, 0); Near(filled.Right, 1); Near(filled.Top, 1);
            TestSupport.Check(!ExportMapOverlayGeometry.Project(new ExportMapOverlayRegion(20, 20, 20),
                1024, 16, 0.499, 0.499, 0.001, 0.001, out _), "Offscreen sector must not render.");
            TestSupport.Check(!ExportMapOverlayGeometry.Project(default, 0, 16, 0, 0, 1, 1, out _)
                && !ExportMapOverlayGeometry.Project(default, 1024, 16, 0, 0, 0, 1, out _)
                && !ExportMapOverlayGeometry.Project(default, 1024, double.NaN, 0, 0, 1, 1, out _),
                "Invalid view geometry must not reach the mesh.");
        }

        // Coalesces contiguous same-row sectors but preserves gaps and separate rows.
        private static void MergesWithoutBridgingGaps()
        {
            var zones = new[] { Zone(2, 0), Zone(0, 0), Zone(1, 0), Zone(4, 0), Zone(0, 1) };
            var merged = ExportMapOverlayGeometry.Merge(zones);
            TestSupport.Check(merged.Length == 3 && merged[0].MinX == 0 && merged[0].MaxX == 2
                && merged[1].MinX == 4 && merged[1].MaxX == 4 && merged[2].Z == 1,
                "Merged rectangles may not cover uncaptured sectors.");
            var large = ExportMapOverlayGeometry.Merge(Enumerable.Range(-10000, 20000).Select(x => Zone(x, 0)));
            TestSupport.Check(large.Length == 1, "A large completed row should not need one quad per sector.");
        }

        // Creates a minimal pending inventory entry for a selected coordinate.
        private static ZoneEntry Zone(int x, int z)
        { return new ZoneEntry { X = x, Z = z, Origin = ExplorationOrigin.Personal }; }

        // Writes reproducible full-map progress while retaining the exclusive writer lock.
        private static WorldStore Export(string root)
        {
            var store = WorldStore.Open(root, World(), reconcile: false);
            store.InitializeInventory(new InventoryResult { Zones = Enumerable.Range(0, 4).Select(x => Zone(x, 0)).ToList() },
                "character", "Test");
            store.WriteZone(0, 0, new byte[] { 1 }, "test", 1);
            return store;
        }

        // Refuses pending, failed, and missing-payload sectors while the writer remains active.
        private static void ReadsOnlyCompletedExistingCaptures(string root)
        {
            using (var store = Export(root))
            {
                store.WriteZone(1, 0, new byte[] { 2 }, "test", 1);
                File.Delete(Path.Combine(store.DirectoryPath, StoreValidation.ZoneFileName(1, 0)));
                store.Manifest.Zones.Single(zone => zone.X == 2).Status = "failed";
                store.Save();
                var before = Snapshot(root);
                var regions = ExportMapOverlaySource.Read(store.DirectoryPath, World(), "character", false, 120);
                TestSupport.Check(regions.Length == 1 && regions[0].MinX == 0 && regions[0].MaxX == 0,
                    "Only committed existing captures should be colored.");
                var progress = ExportMapOverlaySource.ReadProgress(store.DirectoryPath, World(), "character", false, 120);
                TestSupport.Check(progress.Captured.Length == 1 && progress.Remaining.Length == 1
                    && progress.Remaining[0].MinX == 1 && progress.Remaining[0].MaxX == 3,
                    "Remaining overlay must include missing, failed, and pending sectors without gaps.");
                Unchanged(before, root);
            }
        }

        // Refuses another world, generation version, character, or configured selection mode.
        private static void RejectsForeignWorldCharacterAndScope(string root)
        {
            using (var store = Export(root))
            {
                var before = Snapshot(root);
                var foreign = World(); foreign.Uid++;
                TestSupport.Throws<InvalidOperationException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, foreign, "character", false, 120));
                foreign = World(); foreign.GenerationVersion++;
                TestSupport.Throws<InvalidOperationException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, foreign, "character", false, 120));
                TestSupport.Throws<InvalidDataException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, World(), "other", false, 120));
                TestSupport.Throws<InvalidDataException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, World(), "character", true, 120));
                Unchanged(before, root);
            }
        }

        // Does not create directories or fall back to obsolete progress after corruption.
        private static void MissingAndCorruptSourcesAreReadOnly(string root)
        {
            var missing = Path.Combine(root, "missing");
            TestSupport.Check(ExportMapOverlaySource.Read(missing, World(), "character", false, 120).Length == 0
                && !Directory.Exists(missing), "Missing exports must stay absent.");
            string directory;
            using (var store = Export(root)) { directory = store.DirectoryPath; }
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{broken");
            var before = Snapshot(root);
            TestSupport.Throws<SerializationException>(() => ExportMapOverlaySource.Read(directory, World(), "character", false, 120));
            Unchanged(before, root);
        }

        // Requires matching landmark radius even if a manifest is copied into the wrong directory.
        private static void LandmarkScopeIsChecked(string root)
        {
            using (var store = WorldStore.Open(root, World(), reconcile: false, scope: "landmarks_r120"))
            {
                store.SelectLandmarks(new LandmarkInventory { Points = new List<LandmarkPoint> {
                    new LandmarkPoint { Id = "start", Kind = "start", Name = "Start", Source = "test" } } }, 120, "character", "Test");
                store.WriteZone(0, 0, new byte[] { 1 }, "test", 1);
                var before = Snapshot(root);
                TestSupport.Check(ExportMapOverlaySource.Read(store.DirectoryPath, World(), "character", true, 120).Length == 1,
                    "Matching landmark export should be displayed.");
                TestSupport.Throws<InvalidDataException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, World(), "character", true, 200));
                TestSupport.Throws<InvalidDataException>(() => ExportMapOverlaySource.Read(store.DirectoryPath, World(), "character", false, 120));
                Unchanged(before, root);
            }
        }

        // Captures content and modification stamps while excluding the deliberately held writer lock.
        private static Dictionary<string, string> Snapshot(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != ".worldcrawler.lock")
                .ToDictionary(path => path, path => StoreValidation.Hash(File.ReadAllBytes(path)) + File.GetLastWriteTimeUtc(path).Ticks);
        }

        // Proves successful and rejected reads leave all stored progress byte-for-byte unchanged.
        private static void Unchanged(Dictionary<string, string> before, string root)
        {
            var after = Snapshot(root);
            TestSupport.Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value),
                "The map overlay must not write export files or recovery manifests.");
        }

        // Compares projected corners with tight tolerance independent of Unity's float conversions.
        private static void Near(double actual, double expected)
        { TestSupport.Check(Math.Abs(actual - expected) < 0.000000001, "Map projection differs from native coordinates."); }
    }
}
