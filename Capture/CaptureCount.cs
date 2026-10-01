using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Capture
{
    // Stores a portable count for prefab and category summaries.
    [DataContract]
    public sealed class CaptureCount
    {
        [DataMember(Order = 0)] public string Name;
        [DataMember(Order = 1)] public int Count;
    }
}
