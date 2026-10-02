using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Capture
{
    // Records an explicit native destruction, never an inference from missing or unloaded objects.
    [DataContract]
    public sealed class CapturedDeletion
    {
        [DataMember(Order = 0)] public string SourceUser;
        [DataMember(Order = 1)] public uint SourceId;
        [DataMember(Order = 2)] public int PrefabHash;
        [DataMember(Order = 3)] public long ObservedUtcTicks;
    }
}
