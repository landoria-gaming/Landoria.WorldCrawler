using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Records world identity and restartable capture progress.
    [DataContract]
    public sealed class WorldManifest
    {
        [DataMember(Order = 0)] public int FormatVersion { get; set; } = 1;
        [DataMember(Order = 1)] public WorldIdentity World { get; set; }
        [DataMember(Order = 2)] public string CreatedUtc { get; set; }
        [DataMember(Order = 3)] public string UpdatedUtc { get; set; }
        [DataMember(Order = 4)] public bool InventoryInitialized { get; set; }
        [DataMember(Order = 5)] public string CharacterId { get; set; }
        [DataMember(Order = 6)] public string CharacterName { get; set; }
        [DataMember(Order = 7)] public long PersonalPixels { get; set; }
        [DataMember(Order = 8)] public long SharedPixels { get; set; }
        [DataMember(Order = 9)] public long CombinedPixels { get; set; }
        [DataMember(Order = 10)] public List<ZoneEntry> Zones { get; set; } = new List<ZoneEntry>();
        [DataMember(Order = 11)] public string CrawlState { get; set; } = "idle";
        [DataMember(Order = 12)] public string GameVersion { get; set; }
        [DataMember(Order = 13)] public bool ReturnPending { get; set; }
        [DataMember(Order = 14)] public float[] ReturnPosition { get; set; }
        [DataMember(Order = 15)] public float[] ReturnRotation { get; set; }
        [DataMember(Order = 16)] public string ReturnCharacterId { get; set; }
        [DataMember(Order = 17)] public string LastError { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public LandmarkSelection Selection { get; set; }
    }
}
