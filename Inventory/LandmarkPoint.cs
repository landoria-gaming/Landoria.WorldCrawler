using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Inventory
{
    // Retains legacy manifest fields without participating in recording or movement.
    [DataContract]
    public sealed class LandmarkPoint
    {
        [DataMember(Order = 0)] public string Id;
        [DataMember(Order = 1)] public string Kind;
        [DataMember(Order = 2)] public string Name;
        [DataMember(Order = 3)] public string Source;
        [DataMember(Order = 4)] public float X;
        [DataMember(Order = 5)] public float Y;
        [DataMember(Order = 6)] public float Z;
    }
}
