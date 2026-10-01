using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Verifies schema validation even when envelope checksums were recomputed successfully.
    internal static class PayloadStorageTests
    {
        // Checks ingestion, restart, and unknown payload versions independently of Unity.
        internal static void Run()
        {
            TestSupport.InDirectory(RejectInvalidOnWrite);
            TestSupport.InDirectory(RejectRehashedCorruption);
            TestSupport.InDirectory(RefuseFuturePayload);
        }

        // Opens one initialized zone and attaches the runtime's payload-validation contract.
        private static WorldStore Open(string root)
        {
            var world = new WorldIdentity { Name = "Test", Uid = 10, SeedText = "test", Seed = 1, GenerationVersion = 1 };
            var store = WorldStore.Open(root, world, reconcile: false);
            store.PayloadValidator = Validate;
            if (!store.Manifest.InventoryInitialized)
            {
                var inventory = new InventoryResult();
                inventory.Zones.Add(new ZoneEntry { X = 0, Z = 0, Origin = ExplorationOrigin.Personal });
                store.InitializeInventory(inventory, "test-player", "Test");
            }
            return store;
        }

        // Checks that portable payload metadata agrees with its outer zone envelope.
        private static void Validate(int x, int z, byte[] payload, string version, int count)
        {
            var snapshot = ZoneSnapshot.Decode(payload);
            if (snapshot.ZoneX != x || snapshot.ZoneZ != z || snapshot.Objects.Count != count)
            {
                throw new InvalidDataException("Zone envelope does not match the observed payload.");
            }
        }

        // Builds a valid empty observation that does not certify authoritative absence.
        private static byte[] Payload()
        {
            return new ZoneSnapshot { StartedUtcTicks = 1, FinishedUtcTicks = 2, ObservationPasses = 2,
                TerrainReady = true, DwellSeconds = 10, StableSeconds = 5 }.Encode();
        }

        // Rejects structurally invalid bytes before a zone file can be committed.
        private static void RejectInvalidOnWrite(string root)
        {
            using (var store = Open(root))
            {
                TestSupport.Throws<InvalidDataException>(() => store.WriteZone(0, 0, Payload(), "1.0.16", 99));
                TestSupport.Check(Directory.GetFiles(store.DirectoryPath, "zone_*.json").Length == 0,
                    "Payload schema validation must precede creating a committed file.");
            }
        }

        // Detects a changed inner coordinate even if the outer payload checksum is valid.
        private static void RejectRehashedCorruption(string root)
        {
            using (var store = Open(root))
            {
                store.WriteZone(0, 0, Payload(), "1.0.16", 0);
                var path = Path.Combine(store.DirectoryPath, store.Manifest.Zones[0].FileName);
                RewritePayload(path, json => json.Replace("\"ZoneX\":0", "\"ZoneX\":7"));
                TestSupport.Throws<InvalidDataException>(() => store.ReadZone(0, 0));
                store.Reconcile();
                TestSupport.Check(store.Manifest.Zones[0].Status == "pending" && File.Exists(path),
                    "Rehashed invalid data must be retryable while the evidence stays intact.");
            }
        }

        // Refuses unknown inner schemas without overwriting or downgrading their capture.
        private static void RefuseFuturePayload(string root)
        {
            using (var store = Open(root))
            {
                store.WriteZone(0, 0, Payload(), "1.0.16", 0);
                var path = Path.Combine(store.DirectoryPath, store.Manifest.Zones[0].FileName);
                RewritePayload(path, json => json.Replace("\"PayloadVersion\":1", "\"PayloadVersion\":999"));
                TestSupport.Throws<NotSupportedException>(() => store.Reconcile());
                TestSupport.Check(store.Manifest.Zones[0].Status == "captured", "Future payload must not become an overwrite candidate.");
            }
        }

        // Simulates a producer with a valid checksum but inconsistent or newer content.
        private static void RewritePayload(string path, Func<string, string> rewrite)
        {
            var envelope = AtomicJson.Read<ZoneEnvelope>(path);
            var original = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64));
            var payload = Encoding.UTF8.GetBytes(rewrite(original));
            envelope.PayloadBase64 = Convert.ToBase64String(payload);
            envelope.PayloadLength = payload.Length;
            envelope.Checksum = StoreValidation.Hash(payload);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                new DataContractJsonSerializer(typeof(ZoneEnvelope)).WriteObject(stream, envelope);
            }
        }
    }
}
