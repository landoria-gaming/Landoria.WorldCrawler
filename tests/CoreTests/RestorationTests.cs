using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Checks the import file boundary without Unity, real characters or user world saves.
    internal static class RestorationTests
    {
        // Covers immutable archives, moving-object deduplication, metadata, ownership and backups.
        internal static void Run()
        {
            TestSupport.InDirectory(ArchiveRoundTrip);
            TestSupport.InDirectory(ArchiveCurrentPatch);
            TestSupport.InDirectory(ArchiveAcceptsPartial);
            TestSupport.InDirectory(IncrementalJournal);
            TestSupport.InDirectory(JournalRecovery);
            TestSupport.InDirectory(MetadataAndNames);
            TestSupport.InDirectory(VerifiedBackup);
        }

        // Supplies an isolated synthetic world identity.
        private static WorldIdentity World()
        { return new WorldIdentity { Name = "Aydindril Test", Uid = 42, SeedText = "TestSeed", Seed = -123, GenerationVersion = 2 }; }

        // Creates observations of the same moving object in two different source sectors.
        private static ZoneSnapshot Snapshot(int x, long observed)
        {
            var snapshot = new ZoneSnapshot { ZoneX = x, ZoneZ = 0, StartedUtcTicks = observed - 1,
                FinishedUtcTicks = observed + 1, TerrainReady = true, ObservationPasses = 3, DwellSeconds = 10, StableSeconds = 5 };
            snapshot.Objects.Add(new CapturedObject { SourceUser = "11", SourceId = 7, PrefabName = "test", PrefabHash = 123,
                ZoneX = x, ZoneZ = 0, Position = new[] { x * 64f, 10f, 0f }, Rotation = new[] { 0f, 0f, 0f, 1f },
                LocalScale = new[] { 1f, 1f, 1f }, SourceOwner = "11", Creator = "11", ObservedUtcTicks = observed,
                RawDataBase64 = Convert.ToBase64String(new byte[] { 0, 1, 123, 0, 0, 0 }), Categories = new[] { "Piece" } });
            snapshot.BuildSummaries();
            return snapshot;
        }

        // Writes actual versioned export files so import tests use the production format.
        private static string Archive(string root, bool complete = true, string version = "0.221.12")
        {
            using (var store = WorldStore.Open(root, World()))
            {
                var inventory = new InventoryResult { PersonalPixels = 1, SharedPixels = 1, CombinedPixels = 2 };
                inventory.Zones.Add(new ZoneEntry { X = 0, Z = 0, Origin = ExplorationOrigin.Personal });
                inventory.Zones.Add(new ZoneEntry { X = 1, Z = 0, Origin = ExplorationOrigin.Shared });
                store.InitializeInventory(inventory, "source-character", "Source");
                var time = DateTime.UtcNow.Ticks;
                store.WriteZone(0, 0, Snapshot(0, time).Encode(), version, 1);
                if (complete) { store.WriteZone(1, 0, Snapshot(1, time + 10).Encode(), version, 1); }
                return store.DirectoryPath;
            }
        }

        // Reads real zone envelopes from a supported patch other than the original 1.0.16 fixture.
        private static void ArchiveCurrentPatch(string root)
        {
            var directory = Archive(root, version: "1.0.12");
            using (var archive = new ExportArchive(directory))
            {
                TestSupport.Check(archive.ZoneObjects(1, 0).Count == 1,
                    "A supported 1.0.x archive was rejected or lost its objects.");
            }
        }

        // Ensures import never rewrites source progress and moving objects are restored only once.
        private static void ArchiveRoundTrip(string root)
        {
            var directory = Archive(root);
            var path = Path.Combine(directory, "manifest.json");
            var original = File.ReadAllBytes(path);
            using (var archive = new ExportArchive(directory))
            {
                TestSupport.Check(archive.Records.Count() == 1, "Moving source object was not deduplicated.");
                TestSupport.Check(archive.ZoneObjects(0, 0).Count == 0 && archive.ZoneObjects(1, 0).Count == 1,
                    "Only the newest observation should own a moving object.");
                TestSupport.Check(archive.ZoneObjects(1, 0)[0].RawDataBase64 != null, "Zone loading lost raw source bytes.");
                TestSupport.Check(archive.Records.First().RawDataBase64 == null, "The global index retained every raw payload.");
                TestSupport.Throws<IOException>(() => { using (new ExportArchive(directory)) { } });
            }
            TestSupport.Check(original.SequenceEqual(File.ReadAllBytes(path)), "Import changed source export progress.");
            var zone = Path.Combine(directory, "zone_1_0.worldcrawler.json");
            var envelope = AtomicJson.Read<ZoneEnvelope>(zone);
            envelope.Checksum = new string('0', 64);
            AtomicJson.Write(zone, envelope, _ => { });
            TestSupport.Throws<InvalidDataException>(() => { using (new ExportArchive(directory)) { } });
        }

        // Reads only committed captures without modifying the source or trusting unfinished zone files.
        private static void ArchiveAcceptsPartial(string root)
        {
            var directory = Archive(root, false);
            var path = Path.Combine(directory, "manifest.json");
            var original = File.ReadAllBytes(path);
            File.WriteAllText(Path.Combine(directory, StoreValidation.ZoneFileName(1, 0)), "unfinished bytes");
            using (var archive = new ExportArchive(directory))
            {
                TestSupport.Check(archive.PlannedZoneCount == 2 && archive.Manifest.Zones.Count == 1 &&
                    archive.ZoneObjects(0, 0).Count == 1, "Partial export did not select exactly the committed zone.");
            }
            TestSupport.Check(original.SequenceEqual(File.ReadAllBytes(path)), "Partial import rewrote the source manifest.");
            File.Delete(Path.Combine(directory, StoreValidation.ZoneFileName(0, 0)));
            TestSupport.Throws<FileNotFoundException>(() => { using (new ExportArchive(directory)) { } });
            var manifest = AtomicJson.Read<WorldManifest>(path);
            manifest.Zones[0].Status = "pending";
            AtomicJson.Write(path, manifest, value => StoreValidation.Manifest(value, value.World));
            TestSupport.Throws<InvalidDataException>(() => { using (new ExportArchive(directory)) { } });
        }

        // Preserves completed zones and object identities across retries and additive export growth.
        private static void IncrementalJournal(string root)
        {
            var directory = Archive(root, false);
            PreparedWorld marker;
            using (var archive = new ExportArchive(directory))
            {
                marker = new PreparedWorld { World = World(), SourceCharacter = "source-character",
                    Token = Guid.NewGuid().ToString("N"), ExportFingerprint = archive.Fingerprint,
                    SourceSeries = archive.SeriesIdentity, InitialZones = archive.ZoneSignatures() };
                using (var journal = new RestoreJournal(root, marker, "test-character"))
                {
                    journal.AcceptArchive(archive); journal.AcceptArchive(archive);
                    journal.State.Completed.Add("0:0");
                    journal.State.Objects.Add(new RestoredObject { Source = "11:7", TargetUser = "22", TargetId = 1, Prefab = 123 });
                    journal.Save();
                }
            }
            using (var store = WorldStore.Open(root, World()))
            { store.WriteZone(1, 0, Snapshot(1, DateTime.UtcNow.Ticks).Encode(), "0.221.12", 1); }
            using (var archive = new ExportArchive(directory))
            using (var journal = new RestoreJournal(root, marker, "test-character"))
            {
                marker.ValidateArchive(archive); marker.ValidateArchive(archive);
                journal.AcceptArchive(archive); journal.AcceptArchive(archive);
                TestSupport.Check(journal.State.Fingerprint == marker.ExportFingerprint &&
                    archive.Fingerprint != marker.ExportFingerprint && journal.State.Completed.SequenceEqual(new[] { "0:0" }) &&
                    journal.State.Objects.Count == 1 && journal.State.AcceptedZones.Count == 2,
                    "Additive resume lost stable tags, duplicated mappings or reset completed zones.");
            }
            RejectChangedAcceptedZone(root, directory, marker);
        }

        // Rejects a recaptured accepted zone even when its new payload is valid and checksummed.
        private static void RejectChangedAcceptedZone(string root, string directory, PreparedWorld marker)
        {
            using (var store = WorldStore.Open(root, World()))
            { store.WriteZone(1, 0, Snapshot(1, DateTime.UtcNow.Ticks + 100).Encode(), "0.221.12", 1); }
            using (var archive = new ExportArchive(directory))
            using (var journal = new RestoreJournal(root, marker, "test-character"))
            {
                TestSupport.Throws<InvalidDataException>(() => journal.AcceptArchive(archive));
                TestSupport.Check(journal.State.Completed.SequenceEqual(new[] { "0:0" }),
                    "Rejected source mutation changed completion checkpoints.");
                marker.SourceSeries = new string('0', 64);
                TestSupport.Throws<InvalidDataException>(() => marker.ValidateArchive(archive));
            }
        }

        // Prevents using the source character or changing test characters during recovery.
        private static void JournalRecovery(string root)
        {
            var marker = new PreparedWorld { World = World(), SourceCharacter = "source-character",
                Token = Guid.NewGuid().ToString("N"), ExportFingerprint = "fingerprint", CreatedUtc = DateTime.UtcNow.ToString("o") };
            marker.Validate(World(), "fingerprint");
            TestSupport.Throws<InvalidDataException>(() => marker.Validate(World(), "another-export"));
            TestSupport.Throws<InvalidOperationException>(() => { using (new RestoreJournal(root, marker, "source-character")) { } });
            using (var journal = new RestoreJournal(root, marker, "test-character"))
            {
                journal.State.ReturnPending = true;
                journal.State.ReturnPosition = new[] { 1f, 2f, 3f };
                journal.State.ReturnRotation = new[] { 0f, 0f, 0f, 1f };
                journal.State.Objects.Add(new RestoredObject { Source = "11:7", TargetUser = "22", TargetId = 1, Prefab = 123 });
                journal.State.BackupDirectory = root; journal.State.Error = null;
                journal.State.Completed.Add("1:0"); journal.Save();
                TestSupport.Throws<IOException>(() => { using (new RestoreJournal(root, marker, "test-character")) { } });
            }
            using (var journal = new RestoreJournal(root, marker, "test-character"))
            {
                TestSupport.Check(journal.State.ReturnPending && journal.State.Completed.SequenceEqual(new[] { "1:0" }),
                    "Restore progress or return point did not survive a restart.");
                journal.State.ReturnPosition[0] = float.NaN;
                TestSupport.Throws<InvalidDataException>(() => journal.Save());
            }
            TestSupport.Throws<InvalidDataException>(() => { using (new RestoreJournal(root, marker, "different-test-character")) { } });
        }

        // Validates the exact new-format identity and rejects old metadata or unsafe names.
        private static void MetadataAndNames(string root)
        {
            foreach (var invalid in new[] { "../world", "CON", "LPT1.txt", "trailing.", "name/other", " name", "a:b" })
            { TestSupport.Throws<InvalidDataException>(() => NativeWorldMetadata.ValidateName(invalid)); }
            NativeWorldMetadata.ValidateName(World().Name);
            var path = Path.Combine(root, "_main.0.fwl2");
            WriteMetadata(path, World(), 41);
            NativeWorldMetadata.Verify(path, World());
            var changed = World(); changed.Uid++;
            TestSupport.Throws<InvalidDataException>(() => NativeWorldMetadata.Verify(path, changed));
            WriteMetadata(path, World(), 35);
            TestSupport.Throws<InvalidDataException>(() => NativeWorldMetadata.Verify(path, World()));
            var old = Path.Combine(root, "world.fwl"); WriteMetadata(old, World(), 41);
            TestSupport.Throws<InvalidDataException>(() => NativeWorldMetadata.Verify(old, World()));
        }

        // Generates test bytes using the same documented field order as the engine metadata writer.
        private static void WriteMetadata(string path, WorldIdentity world, int version)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer))
            {
                writer.Write(version); writer.Write(world.Name); writer.Write(world.SeedText); writer.Write(world.Seed);
                writer.Write(world.Uid); writer.Write(world.GenerationVersion); writer.Write(false); writer.Write(0); writer.Write(0);
                writer.Flush();
                using (var output = new BinaryWriter(File.Create(path)))
                { output.Write((int)buffer.Length); output.Write(buffer.ToArray()); }
            }
        }

        // Verifies recoverable copies and refuses unfinished or unexpectedly nested saves.
        private static void VerifiedBackup(string root)
        {
            var source = Path.Combine(root, "world"); Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "part.chunk"), new byte[] { 1, 2, 3, 4 });
            TestSupport.Throws<IOException>(() => WorldBackup.Create(source, Path.Combine(root, "journal")));
            File.WriteAllBytes(Path.Combine(source, "_main.1.ok"), BitConverter.GetBytes(41));
            var backup = WorldBackup.Create(source, Path.Combine(root, "journal"));
            TestSupport.Check(File.ReadAllBytes(Path.Combine(backup, "part.chunk")).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "World backup bytes differ from source.");
            Directory.CreateDirectory(Path.Combine(source, "unexpected"));
            TestSupport.Throws<IOException>(() => WorldBackup.Create(source, Path.Combine(root, "journal")));
        }
    }
}
