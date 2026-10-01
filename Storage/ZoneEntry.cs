using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Tracks one zone in the frozen exploration inventory.
    [DataContract]
    public sealed class ZoneEntry
    {
        [DataMember(Order = 0)]
        public int X
        {
            get; set;
        }
        [DataMember(Order = 1)]
        public int Z
        {
            get; set;
        }
        [DataMember(Order = 2)]
        public ExplorationOrigin Origin
        {
            get; set;
        }
        [DataMember(Order = 3)] public string Status { get; set; } = "pending";
        [DataMember(Order = 4)]
        public string UpdatedUtc
        {
            get; set;
        }
        [DataMember(Order = 5)]
        public string FailureReason
        {
            get; set;
        }
        [DataMember(Order = 6)]
        public string FileName
        {
            get; set;
        }
        [DataMember(Order = 7)]
        public string Checksum
        {
            get; set;
        }
        [DataMember(Order = 8)]
        public int ObjectCount
        {
            get; set;
        }
        [DataMember(Order = 9)]
        public string CaptureVersion
        {
            get; set;
        }
    }
}
