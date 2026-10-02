using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Merges received observations without treating an unloaded or unseen object as deleted.
    internal static class ReceivedZoneMerge
    {
        // Keeps the latest source identity and carries explicit removals across partial visits.
        internal static ZoneSnapshot Merge(ZoneSnapshot previous, ZoneSnapshot received)
        {
            received = Copy(received);
            if (previous == null)
            {
                previous = new ZoneSnapshot { ZoneX = received.ZoneX, ZoneZ = received.ZoneZ };
            }
            if (previous.ZoneX != received.ZoneX || previous.ZoneZ != received.ZoneZ)
            {
                throw new InvalidDataException("Cannot merge different recording sectors.");
            }
            received.Objects = MergeObjects(previous.Objects, received.Objects);
            received.Deletions = (previous.Deletions ?? new List<CapturedDeletion>()).Concat(received.Deletions).GroupBy(v => v.SourceUser + ":" + v.SourceId)
                .Select(g => g.OrderByDescending(v => v.ObservedUtcTicks).First()).ToList();
            received.Departures = (previous.Departures ?? new List<CapturedDeparture>()).Concat(received.Departures).GroupBy(v => v.SourceUser + ":" + v.SourceId)
                .Select(g => g.OrderByDescending(v => v.ObservedUtcTicks).First()).ToList();
            var removed = received.Deletions.Select(v => Tuple.Create(v.SourceUser + ":" + v.SourceId, v.ObservedUtcTicks))
                .Concat(received.Departures.Select(v => Tuple.Create(v.SourceUser + ":" + v.SourceId, v.ObservedUtcTicks)))
                .GroupBy(v => v.Item1).ToDictionary(g => g.Key, g => g.Max(v => v.Item2));
            received.Objects.RemoveAll(v => removed.TryGetValue(v.SourceUser + ":" + v.SourceId, out var at) && v.ObservedUtcTicks <= at);
            received.SceneNodes = previous.SceneNodes.Concat(received.SceneNodes).GroupBy(v => v.Root + ":" + v.Path)
                .Select(g => g.Last()).ToList();
            received.StartedUtcTicks = previous.StartedUtcTicks > 0 ?
                Math.Min(previous.StartedUtcTicks, received.StartedUtcTicks) : received.StartedUtcTicks;
            received.BuildSummaries();
            received.Validate();
            return received;
        }

        // Leaves the immutable batch unchanged so retries and session counters retain receipt-only data.
        private static ZoneSnapshot Copy(ZoneSnapshot source)
        {
            return new ZoneSnapshot { PayloadVersion = 3, ObservationQuality = "received-object-cache",
                ZoneX = source.ZoneX, ZoneZ = source.ZoneZ, StartedUtcTicks = source.StartedUtcTicks,
                FinishedUtcTicks = source.FinishedUtcTicks, Objects = source.Objects, SceneNodes = source.SceneNodes,
                Deletions = source.Deletions, Departures = source.Departures };
        }

        // Rejects source-ID collisions instead of silently overwriting another kind of object.
        private static List<CapturedObject> MergeObjects(IEnumerable<CapturedObject> old, IEnumerable<CapturedObject> fresh)
        {
            return old.Concat(fresh).GroupBy(v => v.SourceUser + ":" + v.SourceId).Select(group =>
            {
                if (group.Select(v => v.PrefabHash).Distinct().Count() != 1)
                {
                    throw new InvalidDataException("Conflicting prefabs for source identity " + group.Key);
                }
                var ordered = group.OrderByDescending(v => v.ObservedUtcTicks).ToList();
                var newest = ordered[0].Copy();
                newest.LocalScale = newest.LocalScale ?? ordered.FirstOrDefault(v => v.LocalScale != null)?.LocalScale;
                return newest;
            }).ToList();
        }
    }
}
