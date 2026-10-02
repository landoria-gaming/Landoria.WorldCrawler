using System.IO;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Saves passive observations using the same canonical per-zone files and atomic replacement.
    public sealed partial class WorldStore
    {
        // Reads prior data on the worker before merging, preserving objects not seen on this visit.
        internal void WriteReceived(ZoneSnapshot snapshot, string version)
        {
            EnsureOpen();
            var path = ZonePath(snapshot.ZoneX, snapshot.ZoneZ);
            ZoneSnapshot previous = null;
            if (File.Exists(path))
            {
                previous = ZoneSnapshot.Decode(ValidateEnvelope(AtomicJson.Read<ZoneEnvelope>(path), snapshot.ZoneX, snapshot.ZoneZ));
            }
            var merged = ReceivedZoneMerge.Merge(previous, snapshot);
            WriteZone(merged.ZoneX, merged.ZoneZ, merged.Encode(), version, merged.Objects.Count);
        }
    }
}
