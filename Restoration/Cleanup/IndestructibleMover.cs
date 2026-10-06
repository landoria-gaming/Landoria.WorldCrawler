using System;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Cleanup
{
    // Chooses a loaded, level and unoccupied place for a standalone decoration.
    internal static class IndestructibleMover
    {
        internal const string Marker = "WorldCrawler.movedIndestructible";
        private const float Distance = 20f;
        private const float WaterClearance = 8f;

        // Tests directions around the original position and rests the visible base on terrain.
        internal static Vector3 Find(GameObject root, Vector3 origin)
        {
            var footprint = Footprint(root, origin);
            var offset = origin.y - Bottom(root);
            for (var step = 0; step < 16; step++)
            {
                var angle = step * Mathf.PI * 2f / 16f;
                var point = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Distance;
                if (!ZoneSystem.instance.IsZoneLoaded(point) ||
                    !ZoneSystem.instance.GetGroundHeight(point, out var height) ||
                    !Level(point, height, footprint))
                {
                    continue;
                }
                point.y = height + offset + 0.05f;
                if (point.y >= ZoneSystem.instance.m_waterLevel + WaterClearance &&
                    Clear(root, point, footprint))
                {
                    return point;
                }
            }
            throw new InvalidOperationException("No dry, clear, level loaded place was found about 20m away. Nothing moved.");
        }

        // Grounds a decoration at its current horizontal position, whether moved before or not.
        internal static Vector3 Ground(GameObject root, Vector3 origin)
        {
            if (!ZoneSystem.instance.IsZoneLoaded(origin) ||
                !ZoneSystem.instance.GetGroundHeight(origin, out var height) ||
                !Level(origin, height, Footprint(root, origin)))
            {
                throw new InvalidOperationException("No dry, level loaded ground is available below this decoration.");
            }
            return new Vector3(origin.x, origin.y + height - Bottom(root) + 0.05f, origin.z);
        }

        // Measures the bottom of the visible model, falling back to solid colliders.
        private static float Bottom(GameObject root)
        {
            var meshes = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.enabled).Select(renderer => renderer.bounds.min.y).ToArray();
            if (meshes.Length != 0)
            {
                return meshes.Min();
            }
            var colliders = root.GetComponentsInChildren<Collider>(true)
                .Where(collider => collider.enabled && !collider.isTrigger)
                .Select(collider => collider.bounds.min.y).ToArray();
            if (colliders.Length == 0)
            {
                throw new InvalidOperationException("This decoration has no visible model or solid base to place on the ground.");
            }
            return colliders.Min();
        }

        // Estimates horizontal clearance from the object's existing solid colliders.
        private static float Footprint(GameObject root, Vector3 origin)
        {
            var radius = root.GetComponentsInChildren<Collider>()
                .Where(collider => collider.enabled && !collider.isTrigger)
                .Select(collider => Mathf.Max(
                    Mathf.Abs(collider.bounds.min.x - origin.x), Mathf.Abs(collider.bounds.max.x - origin.x),
                    Mathf.Abs(collider.bounds.min.z - origin.z), Mathf.Abs(collider.bounds.max.z - origin.z)))
                .DefaultIfEmpty(1f).Max();
            return Mathf.Clamp(radius, 1f, 8f) + 0.5f;
        }

        // Refuses a slope that would leave part of the decoration floating or buried.
        private static bool Level(Vector3 point, float centerHeight, float radius)
        {
            if (centerHeight < ZoneSystem.instance.m_waterLevel + WaterClearance)
            {
                return false;
            }
            foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                if (!ZoneSystem.instance.GetGroundHeight(point + direction * radius, out var edgeHeight) ||
                    edgeHeight < ZoneSystem.instance.m_waterLevel + WaterClearance ||
                    Mathf.Abs(edgeHeight - centerHeight) > 1f)
                {
                    return false;
                }
            }
            return true;
        }

        // Ignores terrain, triggers and the decoration itself when checking nearby solid objects.
        private static bool Clear(GameObject root, Vector3 point, float radius)
        {
            var center = point + Vector3.up;
            return !Physics.OverlapSphere(center, radius)
                .Any(collider => collider.enabled && !collider.isTrigger &&
                    collider.GetComponentInParent<Heightmap>() == null &&
                    !collider.transform.IsChildOf(root.transform));
        }
    }
}
