using System.Collections.Generic;
using System.Runtime.Serialization;
using Landoria.WorldCrawler.Inventory;

namespace Landoria.WorldCrawler.Storage
{
    // Retains legacy export metadata for read-only compatibility; never plans new work.
    [DataContract]
    public sealed class LandmarkSelection
    {
        [DataMember(Order = 0)] public string Mode { get; set; } = "landmarks";
        [DataMember(Order = 1)]
        public float Radius
        {
            get; set;
        }
        [DataMember(Order = 2)] public List<LandmarkPoint> Points { get; set; } = new List<LandmarkPoint>();
        [DataMember(Order = 3)] public List<string> Warnings { get; set; } = new List<string>();
    }
}
