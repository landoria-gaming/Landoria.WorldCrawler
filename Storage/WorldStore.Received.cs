using System.IO;
using System.Linq;
using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Saves passive observations using the same canonical per-zone files and atomic replacement.
    public sealed partial class WorldStore
    {
        // Reads prior data on the worker before merging, preserving objects not seen on this visit.
        internal ZoneSaveReport WriteReceived(ZoneSnapshot snapshot, string version)
        {
            EnsureOpen();
            snapshot = ZoneSnapshot.Decode(snapshot.Encode());
            snapshot.Objects.RemoveAll(item => RecordedObjects.Contains(item.SourceUser + ":" + item.SourceId));
            if (snapshot.Objects.Count == 0)
            {
                return new ZoneSaveReport { X = snapshot.ZoneX, Z = snapshot.ZoneZ };
            }
            var path = ZonePath(snapshot.ZoneX, snapshot.ZoneZ);
            ZoneSnapshot previous = null;
            if (File.Exists(path))
            {
                previous = ZoneSnapshot.Decode(ValidateEnvelope(AtomicJson.Read<ZoneEnvelope>(path), snapshot.ZoneX, snapshot.ZoneZ));
            }
            var merged = ReceivedZoneMerge.Merge(previous, snapshot);
            WriteZone(merged.ZoneX, merged.ZoneZ, merged.Encode(), version, merged.Objects.Count);
            var known = new HashSet<string>((previous?.Objects ?? new List<CapturedObject>())
                .Select(item => item.SourceUser + ":" + item.SourceId));
            var groups = merged.Objects.Where(item => !known.Contains(item.SourceUser + ":" + item.SourceId))
                .GroupBy(item => item.PrefabName).OrderBy(group => group.Key).ToList();
            var added = groups
                .Select(group => new CaptureCount { Name = group.Key, Count = group.Count() }).ToList();
            var categories = groups.ToDictionary(group => group.Key, group =>
                string.Join(", ", group.SelectMany(item => item.Categories ?? new string[0]).Distinct()));
            var hashes = groups.ToDictionary(group => group.Key, group => group.First().PrefabHash);
            return new ZoneSaveReport { X = merged.ZoneX, Z = merged.ZoneZ,
                Added = added, Categories = categories, PrefabHashes = hashes };
        }
    }
}
