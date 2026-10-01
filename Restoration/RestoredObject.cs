using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Restoration
{
    // Maps an exported identity to the destination object's own network identity.
    [DataContract]
    internal sealed class RestoredObject
    {
        [DataMember] public string Source;
        [DataMember] public string TargetUser;
        [DataMember] public uint TargetId;
        [DataMember] public int Prefab;
    }
}
