using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Tests cross-itinerary return recovery using only temporary manifests and no game runtime.
    internal static class SelectionRecoveryTests
    {
        // Covers scope changes, fallback policy, identity boundaries, and strict read-only behavior.
        internal static void Run()
        {
            TestSupport.InDirectory(BlocksOtherPendingItineraries);
            TestSupport.InDirectory(AllowsChosenPendingItinerary);
            TestSupport.InDirectory(IgnoresOtherWorldsAndUnrelatedPrefixes);
            TestSupport.InDirectory(ValidPrimaryOverridesPrevious);
            TestSupport.InDirectory(CorruptPrimaryUsesPrevious);
            TestSupport.InDirectory(MissingPrimaryUsesPrevious);
            TestSupport.InDirectory(FuturePrimaryDoesNotFallBack);
            TestSupport.InDirectory(ForeignPrimaryDoesNotFallBack);
            TestSupport.InDirectory(InvalidPreviousIsNotRecovered);
            TestSupport.InDirectory(MissingRootRemainsMissing);
        }

        // Creates an identity whose seed and UID appear in every tested directory name.
        private static WorldIdentity World()
        {
            return new WorldIdentity { Name = "Recovery test", Uid = 501, Seed = 12,
                SeedText = "recovery", GenerationVersion = 2 };
        }

        // Persists one owned checkpoint and returns its generated directory path.
        private static string Checkpoint(string root, WorldIdentity world, string scope, bool pending)
        {
            using (var store = WorldStore.Open(root, world, reconcile: false, scope: scope))
            {
                store.InitializeInventory(new InventoryResult(), "player", "Test");
                store.Manifest.ReturnPending = pending;
                store.Manifest.ReturnCharacterId = "player";
                store.Manifest.ReturnPosition = new[] { 10f, 40f, 20f };
                store.Manifest.ReturnRotation = new[] { 0f, 0f, 0f, 1f };
                store.Save();
                return store.DirectoryPath;
            }
        }

        // Blocks radius changes and switching between landmark and full-map itineraries.
        private static void BlocksOtherPendingItineraries(string root)
        {
            Checkpoint(root, World(), "landmarks_r120", true);
            var before = Files(root);
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(root, World(), "landmarks_r200"));
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(root, World(), null));
            Unchanged(before, root);
            var other = Path.Combine(root, "full-map");
            Checkpoint(other, World(), null, true);
            before = Files(other);
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(other, World(), "landmarks_r120"));
            Unchanged(before, other);
        }

        // Leaves the chosen scope to its normal store recovery even when its primary is corrupted.
        private static void AllowsChosenPendingItinerary(string root)
        {
            var chosen = Checkpoint(root, World(), "landmarks_r120", true);
            var before = Files(root);
            SelectionRecovery.Check(root, World(), "landmarks_r120");
            Unchanged(before, root);
            File.WriteAllText(Path.Combine(chosen, "manifest.json"), "{broken");
            before = Files(root);
            SelectionRecovery.Check(root, World(), "landmarks_r120");
            Unchanged(before, root);
            var legacyRoot = Path.Combine(root, "legacy-only");
            Checkpoint(legacyRoot, World(), null, true);
            SelectionRecovery.Check(legacyRoot, World(), null);
        }

        // Ignores unrelated worlds and names that only share the world's leading text.
        private static void IgnoresOtherWorldsAndUnrelatedPrefixes(string root)
        {
            var foreign = World();
            foreign.Uid++;
            Checkpoint(root, foreign, "landmarks_r120", true);
            var source = Checkpoint(Path.Combine(root, "fixtures"), World(), "pending", true);
            var unrelated = Path.Combine(root, StoreValidation.DirectoryName(World()) + "not-a-scope");
            Directory.CreateDirectory(unrelated);
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(unrelated, "manifest.json"));
            var before = Files(root);
            SelectionRecovery.Check(root, World(), "landmarks_r200");
            Unchanged(before, root);
        }

        // Trusts a valid cleared primary instead of reviving its stale pending predecessor.
        private static void ValidPrimaryOverridesPrevious(string root)
        {
            Checkpoint(root, World(), "landmarks_r120", true);
            using (var store = WorldStore.Open(root, World(), reconcile: false, scope: "landmarks_r120"))
            {
                store.Manifest.ReturnPending = false;
                store.Save();
            }
            var before = Files(root);
            SelectionRecovery.Check(root, World(), "landmarks_r200");
            Unchanged(before, root);
        }

        // Uses valid previous recovery evidence after syntax corruption without renaming either file.
        private static void CorruptPrimaryUsesPrevious(string root)
        {
            var directory = PendingPrevious(root);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{broken");
            var before = Files(root);
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(root, World(), "landmarks_r200"));
            Unchanged(before, root);
        }

        // Treats a missing primary as an interrupted checkpoint commit without recreating it.
        private static void MissingPrimaryUsesPrevious(string root)
        {
            var directory = PendingPrevious(root);
            File.Delete(Path.Combine(directory, "manifest.json"));
            var before = Files(root);
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(root, World(), "landmarks_r200"));
            Unchanged(before, root);
        }

        // Leaves an unsupported future schema untouched instead of using an older compatible version.
        private static void FuturePrimaryDoesNotFallBack(string root)
        {
            var directory = Checkpoint(root, World(), "landmarks_r120", false);
            var path = Path.Combine(directory, "manifest.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"FormatVersion\":1", "\"FormatVersion\":999"));
            var before = Files(root);
            TestSupport.Throws<NotSupportedException>(() => SelectionRecovery.Check(root, World(), "landmarks_r200"));
            Unchanged(before, root);
        }

        // Rejects a foreign identity inside a matching directory without trusting its previous file.
        private static void ForeignPrimaryDoesNotFallBack(string root)
        {
            var directory = Checkpoint(root, World(), "landmarks_r120", false);
            var path = Path.Combine(directory, "manifest.json");
            var manifest = AtomicJson.Read<WorldManifest>(path);
            manifest.World.Uid++;
            AtomicJson.Write(path, manifest, value => { });
            var before = Files(root);
            TestSupport.Throws<InvalidOperationException>(() => SelectionRecovery.Check(root, World(), "landmarks_r200"));
            Unchanged(before, root);
        }

        // Refuses malformed backup evidence without attempting a second recovery or source write.
        private static void InvalidPreviousIsNotRecovered(string root)
        {
            var directory = PendingPrevious(root);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{broken");
            File.WriteAllText(Path.Combine(directory, "manifest.json.previous"), "{broken");
            var before = Files(root);
            TestSupport.Throws<System.Runtime.Serialization.SerializationException>(() =>
                SelectionRecovery.Check(root, World(), "landmarks_r200"));
            Unchanged(before, root);
        }

        // Ensures read-only preflight does not create a new export root.
        private static void MissingRootRemainsMissing(string root)
        {
            var missing = Path.Combine(root, "not-created");
            SelectionRecovery.Check(missing, World(), "landmarks_r120");
            TestSupport.Check(!Directory.Exists(missing), "Recovery inspection created an export directory.");
        }

        // Produces a previous revision with a pending return and a newer cleared primary.
        private static string PendingPrevious(string root)
        {
            var directory = Checkpoint(root, World(), "landmarks_r120", true);
            using (var store = WorldStore.Open(root, World(), reconcile: false, scope: "landmarks_r120"))
            {
                store.Manifest.ReturnPending = false;
                store.Save();
            }
            return directory;
        }

        // Records file membership, content and modification times so repairs cannot pass unnoticed.
        private static Dictionary<string, string> Files(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path,
                path => StoreValidation.Hash(File.ReadAllBytes(path)) + ":" + File.GetLastWriteTimeUtc(path).Ticks,
                StringComparer.Ordinal);
        }

        // Checks that preflight has neither altered evidence nor created extra recovery files.
        private static void Unchanged(Dictionary<string, string> before, string root)
        {
            var after = Files(root);
            TestSupport.Check(before.Count == after.Count && before.All(pair =>
                after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value), "Recovery preflight modified source files.");
        }
    }
}
