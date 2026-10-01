using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Inventory
{
    // Preserves discovered destinations and the limits of their client-side discovery.
    [DataContract]
    public sealed class LandmarkInventory
    {
        [DataMember(Order = 0)] public List<LandmarkPoint> Points = new List<LandmarkPoint>();
        [DataMember(Order = 1)] public List<string> Warnings = new List<string>();
    }
}
