using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Restoration
{
    // Limits cleanup to observed zones and object types that the export can reconstruct.
    internal sealed class CleanupSourceIndex
    {
        private readonly Dictionary<int, HashSet<string>> _prefabs = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<int, HashSet<string>> _locations = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<string, List<CapturedObject>> _positions = new Dictionary<string, List<CapturedObject>>();
        private readonly HashSet<string> _complete = new HashSet<string>();
        private readonly HashSet<string> _sceneTypes = new HashSet<string>(StringComparer.Ordinal);

        // Indexes all observations, including older positions, so uncertain matches are preserved.
        internal void Observe(ZoneSnapshot snapshot)
        {
            foreach (var node in snapshot.SceneNodes)
            {
                _sceneTypes.Add(SceneType(node.Name, node.Components));
            }
            if (snapshot.PayloadVersion >= 2 && snapshot.NearCoverageValidated && snapshot.InstancesValidated &&
                snapshot.SceneValidated && snapshot.NaturalAbsenceComplete && snapshot.TerrainReady &&
                (!snapshot.DungeonExpected || snapshot.DungeonEvidenceComplete))
            {
                _complete.Add(ZoneKey(snapshot.ZoneX, snapshot.ZoneZ));
            }
            foreach (var item in snapshot.Objects)
            {
                AddName(_prefabs, item.PrefabHash, item.PrefabName);
                if (item.LocationHash != 0 && !string.IsNullOrEmpty(item.LocationName))
                {
                    AddName(_locations, item.LocationHash, item.LocationName);
                }
                var key = PositionKey(item.PrefabHash, Cell(item.Position[0]), Cell(item.Position[2]));
                if (!_positions.TryGetValue(key, out var values))
                {
                    values = new List<CapturedObject>();
                    _positions.Add(key, values);
                }
                // Only identity and position are needed; do not retain large network payloads.
                values.Add(new CapturedObject { PrefabHash = item.PrefabHash, PrefabName = item.PrefabName,
                    Position = item.Position, LocationHash = item.LocationHash });
            }
        }

        // Requires the exact name and hash, not a substring or the common LocationProxy type alone.
        internal bool Known(int prefab, string name, int location, string locationName)
        {
            return _prefabs.TryGetValue(prefab, out var names) && names.Contains(name) &&
                (location == 0 || _locations.TryGetValue(location, out var locations) && locations.Contains(locationName));
        }

        // Rejects static scene types that were never captured anywhere in the archive.
        internal bool KnownScene(string name, IEnumerable<string> components)
        {
            return _sceneTypes.Contains(SceneType(name, components));
        }

        // Rejects uncaptured and partially observed zones independently of the current workflow.
        internal bool Complete(int x, int z)
        {
            return _complete.Contains(ZoneKey(x, z));
        }

        // Preserves source positions even before import and ignores rotation differences conservatively.
        internal bool Present(int prefab, string name, float x, float y, float z, int location)
        {
            for (var cx = Cell(x) - 1; cx <= Cell(x) + 1; cx++)
            {
                for (var cz = Cell(z) - 1; cz <= Cell(z) + 1; cz++)
                {
                    if (!_positions.TryGetValue(PositionKey(prefab, cx, cz), out var values))
                    {
                        continue;
                    }
                    if (values.Any(v => v.PrefabName == name && v.LocationHash == location &&
                        Math.Pow(v.Position[0] - x, 2) + Math.Pow(v.Position[1] - y, 2) +
                        Math.Pow(v.Position[2] - z, 2) <= 0.25 * 0.25))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Requires every sector touched by a site's bounding square to be fully observed.
        internal bool Covers(float x, float z, float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f || radius > 512f)
            {
                return false;
            }
            for (var zx = Sector(x - radius); zx <= Sector(x + radius); zx++)
            {
                for (var zz = Sector(z - radius); zz <= Sector(z + radius); zz++)
                {
                    if (!Complete(zx, zz))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Ignores Unity's clone suffix but requires the same observed component types.
        private static string SceneType(string name, IEnumerable<string> components)
        {
            return name.Replace("(Clone)", "").Trim() + "|" + string.Join("|", components.OrderBy(v => v, StringComparer.Ordinal));
        }

        // Collects names without silently accepting a hash collision.
        private static void AddName(Dictionary<int, HashSet<string>> index, int hash, string name)
        {
            if (!index.TryGetValue(hash, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                index.Add(hash, values);
            }
            values.Add(name);
        }

        // Uses small horizontal cells to keep source matching inexpensive on large exports.
        private static int Cell(float coordinate)
        {
            return (int)Math.Floor(coordinate / 2.0);
        }

        // Matches Valheim's sector boundaries, including negative coordinates.
        private static int Sector(float coordinate)
        {
            return (int)Math.Floor((coordinate + 32.0) / 64.0);
        }

        // Names a spatial lookup bucket independently of the process locale.
        private static string PositionKey(int prefab, int x, int z)
        {
            return prefab + ":" + ZoneKey(x, z);
        }

        // Shares the stable zone label used by the export manifest.
        private static string ZoneKey(int x, int z)
        {
            return x + ":" + z;
        }
    }
}
