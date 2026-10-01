using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Exercises interruption recovery and archive integrity under real file operations.
    internal static class StorageTests
    {
        // Runs isolated storage checks covering the most consequential recovery paths.
        internal static void Run()
        {
            TestSupport.InDirectory(RoundTripAndFrozenInventory);
            TestSupport.InDirectory(ExclusiveWorldLock);
            TestSupport.InDirectory(RejectIdentityMismatch);
            TestSupport.InDirectory(RecoverOrphanAndIgnoreTemporary);
            TestSupport.InDirectory(CorruptAndMissingZonesBecomePending);
            TestSupport.InDirectory(RecoverCorruptManifest);
            TestSupport.InDirectory(RejectPathTraversal);
            TestSupport.InDirectory(SeedSanitizationDoesNotCollide);
            TestSupport.InDirectory(RejectForeignZone);
            TestSupport.InDirectory(RejectFutureFormat);
            TestSupport.InDirectory(ValidateReturnCheckpoint);
            TestSupport.InDirectory(DeferredReconciliation);
            TestSupport.InDirectory(RejectUnownedReturnCheckpoint);
        }

        // Supplies a source identity without relying on any user's world data.
        private static WorldIdentity Identity(string seed = "Seed/A", int generation = 2)
        {
            return new WorldIdentity { Name = "Test world", Uid = 42, SeedText = seed, Seed = -123,
                GenerationVersion = generation };
        }

        // Supplies two adjacent zones with different exploration origins.
        private static InventoryResult Inventory()
        {
            var result = new InventoryResult { PersonalPixels = 1, SharedPixels = 1, CombinedPixels = 2 };
            result.Zones.Add(new ZoneEntry { X = -1, Z = 2, Origin = ExplorationOrigin.Personal });
            result.Zones.Add(new ZoneEntry { X = 0, Z = 2, Origin = ExplorationOrigin.Shared });
            return result;
        }

        // Checks exact byte persistence and prevents the crawler's exploration from growing its route.
        private static void RoundTripAndFrozenInventory(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.InitializeInventory(Inventory(), "character-1", "Test"), "Initial inventory must initialize.");
                var expanded = Inventory();
                expanded.Zones.Add(new ZoneEntry { X = 10, Z = 20, Origin = ExplorationOrigin.Personal });
                TestSupport.Check(!store.InitializeInventory(expanded, "character-1", "Test"), "Existing inventory must stay frozen.");
                TestSupport.Check(store.Manifest.Zones.Count == 2, "Flight must not expand the frozen map.");
                store.WriteZone(-1, 2, new byte[] { 0, 2, 255, 18 }, "0.221.12", 7);
                TestSupport.Check(store.ReadZone(-1, 2).SequenceEqual(new byte[] { 0, 2, 255, 18 }), "Raw payload bytes changed.");
                TestSupport.Throws<InvalidOperationException>(() => store.ValidateCharacter("different-character"));
                TestSupport.Throws<InvalidOperationException>(() => store.WriteZone(100, 100, new byte[0], "1.0.16", 0));
            }
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.Manifest.Zones[0].Status == "captured" && store.Manifest.Zones[1].Status == "pending",
                    "Only the committed zone should be completed on resume.");
            }
        }

        // Ensures a second writer cannot alter the same world concurrently.
        private static void ExclusiveWorldLock(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Throws<IOException>(() => WorldStore.Open(root, Identity()));
            }
        }

        // Stops resuming when the generator identity changed even though the folder still matches.
        private static void RejectIdentityMismatch(string root)
        {
            using (WorldStore.Open(root, Identity())) { }
            TestSupport.Throws<InvalidOperationException>(() => WorldStore.Open(root, Identity(generation: 99)));
        }

        // Restores completion after a committed zone file outlives a stale manifest checkpoint.
        private static void RecoverOrphanAndIgnoreTemporary(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.WriteZone(-1, 2, new byte[] { 1, 2, 3 }, "0.221.12", 2);
                store.Manifest.Zones[0].Status = "pending";
                store.Manifest.Zones[0].Checksum = null;
                store.Manifest.Zones[1].Status = "loaded";
                store.Save();
                File.WriteAllText(Path.Combine(store.DirectoryPath, "zone_0_2.worldcrawler.json.tmp-test"), "interrupted");
            }
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.Manifest.Zones[0].Status == "captured", "Valid orphan must recover.");
                TestSupport.Check(store.Manifest.Zones[1].Status == "pending", "Temporary capture must not count as completed.");
            }
        }

        // Preserves corrupt evidence and retries missing or damaged files.
        private static void CorruptAndMissingZonesBecomePending(string root)
        {
            string badPath;
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.WriteZone(-1, 2, new byte[] { 1, 2 }, "1.0.16", 1);
                store.WriteZone(0, 2, new byte[] { 3, 4 }, "1.0.16", 1);
                badPath = Path.Combine(store.DirectoryPath, store.Manifest.Zones[0].FileName);
                File.WriteAllText(badPath, File.ReadAllText(badPath).Replace("AQI=", "AQM="));
                File.Delete(Path.Combine(store.DirectoryPath, store.Manifest.Zones[1].FileName));
            }
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.Manifest.Zones.All(zone => zone.Status == "pending" && !string.IsNullOrEmpty(zone.FailureReason)),
                    "Damaged and missing files must become pending with reasons.");
                TestSupport.Check(File.Exists(badPath), "Corrupt evidence must not be deleted.");
            }
        }

        // Uses the previous manifest plus orphan files after a damaged primary checkpoint.
        private static void RecoverCorruptManifest(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.WriteZone(-1, 2, new byte[] { 1 }, "0.221.12", 1);
                File.WriteAllText(Path.Combine(store.DirectoryPath, "manifest.json"), "{broken");
            }
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.Manifest.Zones[0].Status == "captured", "Recovery lost a committed zone.");
                TestSupport.Check(!string.IsNullOrEmpty(store.RecoveryNotice), "Checkpoint recovery must be visible to the caller.");
                TestSupport.Check(Directory.GetFiles(store.DirectoryPath, "manifest.json.invalid-*").Length == 1,
                    "Invalid manifest evidence must be retained.");
            }
        }

        // Rejects an injected file path before accessing anything outside the export directory.
        private static void RejectPathTraversal(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.Manifest.Zones[0].FileName = "../../outside.json";
                TestSupport.Throws<InvalidDataException>(() => store.Save());
            }
        }

        // Distinguishes seeds whose sanitized visible names are identical.
        private static void SeedSanitizationDoesNotCollide(string root)
        {
            string first;
            using (var store = WorldStore.Open(root, Identity("a/b"))) { first = store.DirectoryPath; }
            using (var store = WorldStore.Open(root, Identity("a?b")))
            {
                TestSupport.Check(first != store.DirectoryPath, "Sanitized seeds must remain collision-resistant.");
                TestSupport.Check(Path.GetDirectoryName(store.DirectoryPath) == root, "A seed escaped the export root.");
            }
        }

        // Rejects a well-formed zone copied under another coordinate's filename.
        private static void RejectForeignZone(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.WriteZone(-1, 2, new byte[] { 1 }, "1.0.16", 1);
                var source = Path.Combine(store.DirectoryPath, store.Manifest.Zones[0].FileName);
                File.Copy(source, Path.Combine(store.DirectoryPath, "zone_0_2.worldcrawler.json"));
                store.Reconcile();
                TestSupport.Check(store.Manifest.Zones[1].Status == "pending" && store.Manifest.Zones[1].FailureReason != null,
                    "A file with foreign coordinates cannot certify capture completion.");
            }
        }

        // Refuses a future schema instead of silently falling back to an older checkpoint.
        private static void RejectFutureFormat(string root)
        {
            string path;
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                path = Path.Combine(store.DirectoryPath, "manifest.json");
            }
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"FormatVersion\":1", "\"FormatVersion\":2"));
            TestSupport.Throws<NotSupportedException>(() => WorldStore.Open(root, Identity()));
            TestSupport.Check(File.ReadAllText(path).Contains("\"FormatVersion\":2"), "Future-format evidence was modified.");
        }

        // Rejects unsafe return coordinates before persisting a durable flight checkpoint.
        private static void ValidateReturnCheckpoint(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.Manifest.ReturnPending = true;
                TestSupport.Throws<InvalidDataException>(() => store.Save());
                store.Manifest.ReturnPosition = new[] { 1f, 2f, 3f };
                store.Manifest.ReturnRotation = new[] { 0f, 0f, 0f, 1f };
                store.Manifest.ReturnCharacterId = "character-1";
                store.Save();
            }
            using (var store = WorldStore.Open(root, Identity()))
            {
                TestSupport.Check(store.Manifest.ReturnPending && store.Manifest.ReturnPosition[1] == 2f,
                    "Return checkpoint did not survive restart.");
            }
        }

        // Allows callers to protect an airborne character before validating all zone payloads.
        private static void DeferredReconciliation(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.WriteZone(-1, 2, new byte[] { 1 }, "1.0.16", 1);
                File.Delete(Path.Combine(store.DirectoryPath, store.Manifest.Zones[0].FileName));
            }
            using (var store = WorldStore.Open(root, Identity(), reconcile: false))
            {
                TestSupport.Check(store.Manifest.Zones[0].Status == "captured", "Deferred open must not reconcile zone files yet.");
                store.Reconcile();
                TestSupport.Check(store.Manifest.Zones[0].Status == "pending", "Explicit reconciliation must validate stored capture claims.");
            }
        }

        // Requires an initialized inventory and explicit owner before resuming airborne recovery.
        private static void RejectUnownedReturnCheckpoint(string root)
        {
            using (var store = WorldStore.Open(root, Identity()))
            {
                store.Manifest.ReturnPending = true;
                store.Manifest.ReturnPosition = new[] { 1f, 2f, 3f };
                store.Manifest.ReturnRotation = new[] { 0f, 0f, 0f, 1f };
                TestSupport.Throws<InvalidDataException>(() => store.Save());
                store.Manifest.CharacterId = "character-1";
                store.Manifest.ReturnCharacterId = "character-1";
                TestSupport.Throws<InvalidDataException>(() => store.Save());
                store.InitializeInventory(Inventory(), "character-1", "Test");
                store.Save();
                store.Manifest.ReturnCharacterId = string.Empty;
                TestSupport.Throws<InvalidDataException>(() => store.Save());
            }
        }
    }
}
