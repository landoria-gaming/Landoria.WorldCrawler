using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Records world identity and restartable capture progress.
    [DataContract]
    public sealed class WorldManifest
    {
        [DataMember(Order = 0)] public int FormatVersion { get; set; } = 2;
        [DataMember(Order = 1)]
        public WorldIdentity World
        {
            get; set;
        }
        [DataMember(Order = 2)]
        public string CreatedUtc
        {
            get; set;
        }
        [DataMember(Order = 3)]
        public string UpdatedUtc
        {
            get; set;
        }
        [DataMember(Order = 4)]
        public bool InventoryInitialized
        {
            get; set;
        }
        [DataMember(Order = 10)] public List<ZoneEntry> Zones { get; set; } = new List<ZoneEntry>();
        [DataMember(Order = 11)] public string CrawlState { get; set; } = "idle";
        [DataMember(Order = 12)]
        public string GameVersion
        {
            get; set;
        }
        [DataMember(Order = 17)]
        public string LastError
        {
            get; set;
        }
        [DataMember(Order = 18, EmitDefaultValue = false)]
        public LandmarkSelection Selection
        {
            get; set;
        }
        [DataMember(Order = 19)]
        public List<string> ReceivedPrefabs { get; set; } = new List<string>();
    }
}
