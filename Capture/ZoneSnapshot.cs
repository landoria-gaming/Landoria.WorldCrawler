using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Landoria.WorldCrawler.Capture
{
    // Contains portable source observations and their explicit completeness limits.
    [DataContract]
    public sealed class ZoneSnapshot
    {
        [DataMember(Order = 0)] public int PayloadVersion = 1;
        [DataMember(Order = 1)] public int ZoneX;
        [DataMember(Order = 2)] public int ZoneZ;
        [DataMember(Order = 3)] public long StartedUtcTicks;
        [DataMember(Order = 4)] public long FinishedUtcTicks;
        [DataMember(Order = 5)] public string ObservationQuality = "stable-client-observation";
        [DataMember(Order = 6)] public bool AbsenceAuthoritative = false;
        [DataMember(Order = 7)] public int ObservationPasses;
        [DataMember(Order = 8)] public float DwellSeconds;
        [DataMember(Order = 9)] public float StableSeconds;
        [DataMember(Order = 10)] public bool TerrainReady;
        [DataMember(Order = 11)] public List<CapturedObject> Objects = new List<CapturedObject>();
        [DataMember(Order = 12)] public List<CapturedSceneNode> SceneNodes = new List<CapturedSceneNode>();
        [DataMember(Order = 13)] public List<CaptureCount> PrefabCounts = new List<CaptureCount>();
        [DataMember(Order = 14)] public List<CaptureCount> CategoryCounts = new List<CaptureCount>();
        [DataMember(Order = 15)]
        public string[] Limitations =
        {
            "The server does not acknowledge a complete zone snapshot to the client.",
            "Absence must not authorize automatic destructive restoration.",
            "Objects are observed over several frames, not in one atomic server transaction.",
            "Scene nodes describe layout, not Unity assets or arbitrary component state.",
            "Unsent hidden objects and unloaded dungeon interiors cannot be guaranteed.",
            "Players and all creatures, including tame animals, birds and fish, are intentionally excluded.",
            "Restoration must protect every player and creature, including those absent from the observed exclusions."
        };
        [DataMember(Order = 16)] public string[] ExcludedCategories = { "Player", "Character", "Fish", "RandomFlyingBird" };
        [DataMember(Order = 17)] public List<CaptureCount> ExclusionCounts = new List<CaptureCount>();
        [DataMember(Order = 18)] public bool DungeonExpected;
        [DataMember(Order = 19)] public bool DungeonEvidenceComplete;
        [DataMember(Order = 20)] public int InteriorObjectCount;
        [DataMember(Order = 21)] public bool NaturalAbsenceComplete;

        // Creates the versioned JSON payload after checking that every object has data.
        public byte[] Encode()
        {
            Validate();
            using (var stream = new MemoryStream())
            {
                Serializer().WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        // Reads and validates exported observations without loading them into the game world.
        public static ZoneSnapshot Decode(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                throw new InvalidDataException("The zone payload is empty.");
            }
            using (var stream = new MemoryStream(payload, false))
            {
                var snapshot = Serializer().ReadObject(stream) as ZoneSnapshot;
                if (snapshot == null)
                {
                    throw new InvalidDataException("The zone payload does not contain a snapshot.");
                }
                snapshot.Validate();
                return snapshot;
            }
        }

        // Counts skipped fauna without exporting individual creature or player records.
        public void SetExclusionCounts(IEnumerable<string> reasons)
        {
            ExclusionCounts = Count(reasons);
        }

        // Builds deterministic summaries without altering any captured source payload.
        public void BuildSummaries()
        {
            Objects = Objects.OrderBy(item => item.SourceUser, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId).ToList();
            PrefabCounts = Count(Objects.Select(item => item.PrefabName));
            CategoryCounts = Count(Objects.SelectMany(item => item.Categories));
            SceneNodes = SceneNodes.OrderBy(item => item.Root, StringComparer.Ordinal)
                .ThenBy(item => item.Path, StringComparer.Ordinal).ToList();
        }

        // Rejects duplicate identifiers and missing network payloads before storage.
        public void Validate()
        {
            SnapshotValidation.Validate(this);
        }

        // Counts labels in a stable order for inspection and later comparison.
        private static List<CaptureCount> Count(IEnumerable<string> labels)
        {
            return labels.GroupBy(value => value, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new CaptureCount { Name = group.Key, Count = group.Count() }).ToList();
        }

        // Allows dense base zones to exceed the framework's default 65,536-member graph limit.
        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(ZoneSnapshot),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        }
    }
}
