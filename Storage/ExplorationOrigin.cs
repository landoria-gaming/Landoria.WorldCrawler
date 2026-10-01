using System;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Identifies how the initial map revealed a zone.
    [Flags]
    [DataContract]
    public enum ExplorationOrigin
    {
        [EnumMember] None = 0,
        [EnumMember] Personal = 1,
        [EnumMember] Shared = 2,
        [EnumMember] PointOfInterest = 4
    }
}
