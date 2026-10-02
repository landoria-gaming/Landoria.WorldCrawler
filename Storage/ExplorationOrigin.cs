using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Identifies received sectors while retaining the legacy landmark value for file compatibility.
    [DataContract]
    public enum ExplorationOrigin
    {
        [EnumMember] None = 0,
        [EnumMember] PointOfInterest = 4,
        Received = 8
    }
}
