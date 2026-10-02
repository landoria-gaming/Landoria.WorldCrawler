using System.Collections.Generic;
using System.Linq;

namespace Landoria.WorldCrawler.Capture
{
    // Owns detached data for one 64-metre sector until its next disk flush.
    internal sealed class RecordingZone
    {
        internal readonly int X, Z;
        internal readonly Dictionary<string, CapturedObject> Objects = new Dictionary<string, CapturedObject>();
        internal readonly Dictionary<string, CapturedDeletion> Deletions = new Dictionary<string, CapturedDeletion>();
        internal readonly Dictionary<string, CapturedDeparture> Departures = new Dictionary<string, CapturedDeparture>();
        internal readonly Dictionary<string, CapturedSceneNode> Nodes = new Dictionary<string, CapturedSceneNode>();

        // Stores coordinates independently of Unity's loaded sector lifetime.
        internal RecordingZone(int x, int z)
        {
            X = x;
            Z = z;
        }

        // Produces a received-data snapshot, never a claim that the whole server sector arrived.
        internal ZoneSnapshot Snapshot(long ticks)
        {
            var result = new ZoneSnapshot { PayloadVersion = 3, ObservationQuality = "received-object-cache",
                ZoneX = X, ZoneZ = Z, StartedUtcTicks = ticks, FinishedUtcTicks = ticks,
                Objects = Objects.Values.ToList(), Deletions = Deletions.Values.ToList(),
                Departures = Departures.Values.ToList(), SceneNodes = Nodes.Values.ToList() };
            result.BuildSummaries();
            return result;
        }
    }
}
