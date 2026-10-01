using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Samples overworld terrain data without hitting dungeon colliders above the surface.
    internal static class SurfaceHeight
    {
        // Includes loaded terrain edits and water, falling back only when no heightmap is available.
        internal static float Read(Vector3 point)
        {
            if (!Heightmap.GetHeight(point, out var height))
            {
                if (WorldGenerator.instance == null)
                {
                    throw new InvalidOperationException("World generator unavailable during surface flight.");
                }
                height = WorldGenerator.instance.GetHeight(point.x, point.z);
            }
            var water = ZoneSystem.instance == null ? height : ZoneSystem.instance.m_waterLevel;
            if (!Finite(height) || !Finite(water))
            {
                throw new InvalidOperationException("Invalid terrain or water height during surface flight.");
            }
            return Mathf.Max(height, water);
        }

        // Rejects malformed samples before they become player coordinates.
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
