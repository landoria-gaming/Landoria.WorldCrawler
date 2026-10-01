using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Identifies a landmark sector while retaining its existing serialized value.
    [DataContract]
    public enum ExplorationOrigin
    {
        [EnumMember] None = 0,
        [EnumMember] PointOfInterest = 4
    }
}
