using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Capture
{
    // Preserves a persistent network object without interpreting its version-specific payload.
    [DataContract]
    public sealed class CapturedObject
    {
        [DataMember(Order = 0)] public string SourceUser;
        [DataMember(Order = 1)] public uint SourceId;
        [DataMember(Order = 2)] public string PrefabName;
        [DataMember(Order = 3)] public int PrefabHash;
        [DataMember(Order = 4)] public int ZoneX;
        [DataMember(Order = 5)] public int ZoneZ;
        [DataMember(Order = 6)] public float[] Position;
        [DataMember(Order = 7)] public float[] Rotation;
        [DataMember(Order = 8)] public float[] LocalScale;
        [DataMember(Order = 9)] public uint DataRevision;
        [DataMember(Order = 10)] public string SourceOwner;
        [DataMember(Order = 11)] public string Creator;
        [DataMember(Order = 12)] public int ObjectType;
        [DataMember(Order = 13)] public bool Distant;
        [DataMember(Order = 14)] public int ConnectionType;
        [DataMember(Order = 15)] public string ConnectionTargetUser;
        [DataMember(Order = 16)] public uint ConnectionTargetId;
        [DataMember(Order = 17)] public string RawDataBase64;
        [DataMember(Order = 18)] public long ObservedUtcTicks;
        [DataMember(Order = 19)] public string[] Categories;
        [DataMember(Order = 20)] public int LocationHash;
        [DataMember(Order = 21)] public int LocationSeed;
        [DataMember(Order = 22)] public string LocationName;
    }
}
