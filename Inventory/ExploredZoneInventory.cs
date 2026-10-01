using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Inventory
{
    // Converts rounded minimap pixel footprints into intersecting 64-metre zones.
    public static class ExploredZoneInventory
    {
        // Builds a fixed route from snapshots of both exploration maps.
        public static InventoryResult Build(BitArray personal, BitArray shared, int textureSize, double pixelSize)
        {
            Validate(personal, shared, textureSize, pixelSize);
            var result = new InventoryResult();
            var zones = new Dictionary<long, ZoneEntry>();
            for (var index = 0; index < personal.Length; index++)
            {
                var origin = ExplorationOrigin.None;
                if (personal[index]) { result.PersonalPixels++; origin |= ExplorationOrigin.Personal; }
                if (shared[index]) { result.SharedPixels++; origin |= ExplorationOrigin.Shared; }
                if (origin == ExplorationOrigin.None) { continue; }
                result.CombinedPixels++;
                AddFootprint(zones, index % textureSize, index / textureSize, textureSize, pixelSize, origin);
            }
            result.Zones = zones.Values.OrderBy(zone => zone.Z).ThenBy(zone => zone.X).ToList();
            return result;
        }

        // Rejects map metadata that cannot describe a finite world grid.
        private static void Validate(BitArray personal, BitArray shared, int size, double scale)
        {
            if (size <= 0 || size > 32768 || personal == null || shared == null ||
                personal.Length != checked(size * size) || shared.Length != personal.Length ||
                double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 || scale > 4096)
            {
                throw new ArgumentException("Exploration maps must match a positive finite square grid.");
            }
        }

        // Includes each zone overlapping a pixel's half-open, centre-based footprint.
        private static void AddFootprint(Dictionary<long, ZoneEntry> zones, int px, int pz,
            int size, double scale, ExplorationOrigin origin)
        {
            var minX = (px - size / 2.0 - 0.5) * scale;
            var minZ = (pz - size / 2.0 - 0.5) * scale;
            var startX = (int)Math.Floor((minX + 32.0) / 64.0);
            var startZ = (int)Math.Floor((minZ + 32.0) / 64.0);
            var endX = (int)Math.Ceiling((minX + scale + 32.0) / 64.0) - 1;
            var endZ = (int)Math.Ceiling((minZ + scale + 32.0) / 64.0) - 1;
            for (var z = startZ; z <= endZ; z++)
            {
                for (var x = startX; x <= endX; x++)
                {
                    var key = ((long)x << 32) | (uint)z;
                    ZoneEntry zone;
                    if (!zones.TryGetValue(key, out zone))
                    {
                        zone = new ZoneEntry { X = x, Z = z };
                        zones.Add(key, zone);
                    }
                    zone.Origin |= origin;
                }
            }
        }
    }
}
