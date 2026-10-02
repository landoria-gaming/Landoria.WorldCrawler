using System.Collections.Generic;
using System.Runtime.Serialization;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Keeps a durable batch until all canonical zone files and manifest entries are committed.
    [DataContract]
    internal sealed class RecordingCheckpoint
    {
        [DataMember] internal WorldIdentity World;
        [DataMember] internal string Version;
        [DataMember] internal List<ZoneSnapshot> Zones;
    }
}
