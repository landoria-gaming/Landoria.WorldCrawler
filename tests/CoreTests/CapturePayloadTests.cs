using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Tests
{
    // Verifies portable capture payloads without invoking or loading the game runtime.
    internal static class CapturePayloadTests
    {
        // Exercises raw-byte retention, large captures, connections, and invalid snapshots.
        internal static void Run()
        {
            RoundTrip();
            LargeZone();
            RejectMalformedObject();
            RejectInvalidObservation();
            RejectInvalidTransformsAndHeaders();
        }

        // Builds a small structurally valid source observation with a location child.
        private static ZoneSnapshot Sample()
        {
            var ticks = DateTime.UtcNow.Ticks;
            var snapshot = new ZoneSnapshot { ZoneX = 0, ZoneZ = 0, StartedUtcTicks = ticks - TimeSpan.TicksPerSecond,
                FinishedUtcTicks = ticks, TerrainReady = true, ObservationPasses = 3, DwellSeconds = 10f, StableSeconds = 5f };
            snapshot.Objects.Add(Item(1));
            snapshot.SceneNodes.Add(new CapturedSceneNode { Root = "location:11:7", Path = "root/0:stone", Name = "stone",
                Position = new[] { 0f, 10f, 0f }, Rotation = new[] { 0f, 0f, 0f, 1f }, LocalPosition = new[] { 0f, 1f, 0f },
                LocalRotation = new[] { 0f, 0f, 0f, 1f }, LocalScale = new[] { 1f, 1f, 1f },
                Components = new[] { "Transform", "MeshRenderer" }, ActiveSelf = true });
            snapshot.BuildSummaries();
            return snapshot;
        }

        // Supplies raw persistent-ZDO header bytes and explicit cross-zone connection metadata.
        private static CapturedObject Item(uint id)
        {
            return new CapturedObject { SourceUser = "11", SourceId = id, PrefabName = "test-prefab", PrefabHash = 123,
                ZoneX = 0, ZoneZ = 0, Position = new[] { 0f, 10f, 0f }, Rotation = new[] { 0f, 0f, 0f, 1f },
                LocalScale = new[] { 1f, 1f, 1f }, SourceOwner = "11", Creator = "22", ObservedUtcTicks = DateTime.UtcNow.Ticks,
                ConnectionType = 1, ConnectionTargetUser = "99", ConnectionTargetId = 22,
                RawDataBase64 = Convert.ToBase64String(new byte[] { 0, 1, 123, 0, 0, 0 }), Categories = new[] { "Container", "Piece" } };
        }

        // Decodes portable payloads using the same strict validator as offline inspection.
        private static ZoneSnapshot Decode(byte[] bytes)
        {
            return ZoneSnapshot.Decode(bytes);
        }

        // Preserves exact raw bytes, transforms, connection targets, and scene layout.
        private static void RoundTrip()
        {
            var source = Sample();
            var copy = Decode(source.Encode());
            TestSupport.Check(copy.Objects[0].RawDataBase64 == source.Objects[0].RawDataBase64, "Raw ZDO bytes changed in JSON.");
            TestSupport.Check(copy.Objects[0].ConnectionTargetUser == "99" && copy.Objects[0].ConnectionTargetId == 22,
                "A cross-zone connection target was lost.");
            TestSupport.Check(copy.SceneNodes.Count == 1 && copy.SceneNodes[0].LocalRotation.SequenceEqual(new[] { 0f, 0f, 0f, 1f }),
                "Generated-location scene transforms were lost.");
            TestSupport.Check(!copy.AbsenceAuthoritative, "Client observations must not certify absent server objects.");
            TestSupport.Check(copy.PrefabCounts[0].Count == 1 && copy.CategoryCounts.Sum(count => count.Count) == 2,
                "Capture summaries do not describe their objects.");
        }

        // Exceeds the framework's default serializer item limit with a realistic large base zone.
        private static void LargeZone()
        {
            var source = Sample();
            source.Objects.Clear();
            for (uint id = 1; id <= 10000; id++) { source.Objects.Add(Item(id)); }
            source.BuildSummaries();
            var copy = Decode(source.Encode());
            TestSupport.Check(copy.Objects.Count == 10000 && copy.PrefabCounts[0].Count == 10000,
                "A large zone failed serialization or lost objects.");
        }

        // Rejects duplicated source IDs, foreign-zone objects, and missing source bytes.
        private static void RejectMalformedObject()
        {
            var source = Sample();
            source.Objects.Add(Item(1));
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.Objects[0].ZoneX = 5;
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.Objects[0].RawDataBase64 = string.Empty;
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
        }

        // Prevents an observation file from asserting authoritative absence or omitting fauna protection.
        private static void RejectInvalidObservation()
        {
            var source = Sample();
            source.PayloadVersion = 999;
            TestSupport.Throws<NotSupportedException>(() => source.Encode());
            source = Sample();
            source.AbsenceAuthoritative = true;
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.ExcludedCategories = new[] { "Player" };
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            TestSupport.Throws<InvalidDataException>(() => Decode(new byte[0]));
        }

        // Rejects transforms and raw object headers that cannot describe a valid source record.
        private static void RejectInvalidTransformsAndHeaders()
        {
            var source = Sample();
            source.Objects[0].Rotation = new[] { 0f, 0f, 0f };
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.SceneNodes[0].LocalPosition[0] = float.NaN;
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.Objects[0].PrefabHash = 999;
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
            source = Sample();
            source.Objects[0].RawDataBase64 = Convert.ToBase64String(new byte[] { 0, 0, 123, 0, 0, 0 });
            TestSupport.Throws<InvalidDataException>(() => source.Encode());
        }
    }
}
