using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Capture
{
    // Records an observed sector crossing, not a deletion or an inferred absence.
    [DataContract]
    public sealed class CapturedDeparture
    {
        [DataMember(Order = 0)] public string SourceUser;
        [DataMember(Order = 1)] public uint SourceId;
        [DataMember(Order = 2)] public int PrefabHash;
        [DataMember(Order = 3)] public long ObservedUtcTicks;
        [DataMember(Order = 4)] public int DestinationX;
        [DataMember(Order = 5)] public int DestinationZ;
    }
}
