using System;
using System.Collections.Generic;
using System.Reflection;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Reads the actual near simulation scope without referencing a version-specific zone struct.
    internal sealed class NearZoneScope
    {
        private readonly CaptureApi _api = new CaptureApi();
        private readonly MethodInfo _synced = typeof(ZNet).GetMethod("GetSyncedSimulationDistance");
        private readonly FieldInfo _legacy = typeof(ZoneSystem).GetField("m_activeArea");
        private readonly Dictionary<string, ZoneEntry> _zones = new Dictionary<string, ZoneEntry>();
        private int _centerX = int.MinValue, _centerZ = int.MinValue, _radius;
        private bool _square;
        public IEnumerable<ZoneEntry> Zones => _zones.Values;

        // Tracks only sectors the native near loader is responsible for, including unloaded arrivals.
        public void Refresh(Vector3 position)
        {
            _api.GetZone(position, out var x, out var z);
            ReadDistance(out var radius, out var square);
            if (_centerX == x && _centerZ == z && _radius == radius && _square == square)
            {
                return;
            }
            _centerX = x;
            _centerZ = z;
            _radius = radius;
            _square = square;
            _zones.Clear();
            for (var dz = -radius; dz <= radius; dz++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (!square && dx * dx + dz * dz >= (radius + 0.5f) * (radius + 0.5f))
                    {
                        continue;
                    }
                    var zone = new ZoneEntry { X = x + dx, Z = z + dz, Origin = ExplorationOrigin.Received };
                    _zones.Add(Key(zone.X, zone.Z), zone);
                }
            }
        }

        // Resolves current APIs only when present; old recording never loads restoration bindings.
        private void ReadDistance(out int radius, out bool square)
        {
            if (_synced != null)
            {
                var value = _synced.Invoke(ZNet.instance, null);
                radius = (int)value.GetType().GetProperty("NearSimulationDistance").GetValue(value);
                square = (bool)value.GetType().GetProperty("IsClassic").GetValue(value);
            }
            else if (_legacy != null)
            {
                radius = (int)_legacy.GetValue(ZoneSystem.instance);
                square = true;
            }
            else
            {
                throw new NotSupportedException("The game's near-zone loading scope cannot be read.");
            }
            if (radius < 1 || radius > 16)
            {
                throw new NotSupportedException("Unexpected near-zone loading radius: " + radius);
            }
        }

        // Tests membership using sector coordinates, never height or a guessed distance.
        public bool Contains(int x, int z)
        {
            return _zones.ContainsKey(Key(x, z));
        }

        // Uses native terrain and instance checks before capture or destination mutation.
        public static bool Ready(int x, int z)
        {
            var center = new Vector3(x * 64f, 0f, z * 64f);
            var terrain = Heightmap.FindHeightmap(center);
            return ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(center) &&
                terrain != null && !terrain.IsDistantLod && !terrain.HaveQueuedRebuild() &&
                ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(ZNet.instance.GetReferencePosition());
        }

        // Provides the shared stable sector label.
        public static string Key(int x, int z)
        {
            return x + ":" + z;
        }
    }
}
