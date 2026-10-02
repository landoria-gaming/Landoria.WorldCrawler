using System.Runtime.Serialization;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Wraps a readable zone snapshot in independently verifiable metadata.
    [DataContract]
    public sealed class ZoneEnvelope
    {
        [DataMember(Order = 0)] public int FormatVersion { get; set; } = 2;
        [DataMember(Order = 1)]
        public WorldIdentity World
        {
            get; set;
        }
        [DataMember(Order = 2)]
        public int X
        {
            get; set;
        }
        [DataMember(Order = 3)]
        public int Z
        {
            get; set;
        }
        [DataMember(Order = 4)]
        public string CapturedUtc
        {
            get; set;
        }
        [DataMember(Order = 5)]
        public string CaptureVersion
        {
            get; set;
        }
        [DataMember(Order = 6)]
        public int ObjectCount
        {
            get; set;
        }
        [DataMember(Order = 7)]
        public int PayloadLength
        {
            get; set;
        }
        [DataMember(Order = 8)]
        public string Checksum
        {
            get; set;
        }
        [DataMember(Order = 9)]
        public ZoneSnapshot Payload
        {
            get; set;
        }
    }
}
