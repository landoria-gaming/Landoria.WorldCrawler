using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Capture
{
    // Describes an observed scene node; this is not a serialized Unity prefab or asset.
    [DataContract]
    public sealed class CapturedSceneNode
    {
        [DataMember(Order = 0)] public string Root;
        [DataMember(Order = 1)] public string Path;
        [DataMember(Order = 2)] public string Name;
        [DataMember(Order = 3)] public float[] Position;
        [DataMember(Order = 4)] public float[] Rotation;
        [DataMember(Order = 5)] public float[] LocalPosition;
        [DataMember(Order = 6)] public float[] LocalRotation;
        [DataMember(Order = 7)] public float[] LocalScale;
        [DataMember(Order = 8)] public bool ActiveSelf;
        [DataMember(Order = 9)] public string[] Components;
        [DataMember(Order = 10)] public string NetworkUser;
        [DataMember(Order = 11)] public uint NetworkId;
    }
}
