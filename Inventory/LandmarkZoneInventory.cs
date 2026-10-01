using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Inventory
{
    // Selects every 64-metre sector touching a horizontal disk around an eligible place.
    public static class LandmarkZoneInventory
    {
        public const float MaximumRadius = 1000f;
        public const float MaximumCoordinate = 20000f;
        private static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "portal", "start", "merchant", "boss", "hildir", "pin"
        };

        // Produces a deterministic union without clipping disks to revealed map pixels.
        public static InventoryResult Build(LandmarkInventory inventory, float radius)
        {
            Validate(inventory, radius);
            var zones = new Dictionary<long, ZoneEntry>();
            foreach (var point in inventory.Points)
            {
                AddDisk(zones, point, radius);
            }
            return new InventoryResult
            {
                Zones = zones.Values.OrderBy(zone => zone.Z).ThenBy(zone => zone.X).ToList()
            };
        }

        // Rejects malformed inventory records before they become a movement itinerary.
        public static void Validate(LandmarkInventory inventory, float radius)
        {
            if (!Finite(radius) || radius <= 0 || radius > MaximumRadius)
            {
                throw new ArgumentException("Landmark radius must be finite, positive, and at most 1000 metres.");
            }
            if (inventory == null || inventory.Points == null || inventory.Warnings == null ||
                inventory.Warnings.Any(warning => warning == null))
            {
                throw new ArgumentException("The landmark inventory and its collections are required.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in inventory.Points)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.Id) || !ids.Add(point.Id) ||
                    string.IsNullOrWhiteSpace(point.Source) || point.Name == null ||
                    point.Kind == null || !Kinds.Contains(point.Kind) || !Finite(point.X) ||
                    !Finite(point.Y) || !Finite(point.Z) || Math.Abs(point.X) > MaximumCoordinate ||
                    Math.Abs(point.Z) > MaximumCoordinate)
                {
                    throw new ArgumentException("A landmark has an invalid identity, category, or position.");
                }
            }
        }

        // Includes closed rectangle edges so a disk tangent never loses its neighbouring zone.
        private static void AddDisk(Dictionary<long, ZoneEntry> zones, LandmarkPoint point, double radius)
        {
            var minX = (int)Math.Ceiling((point.X - radius - 32.0) / 64.0);
            var maxX = (int)Math.Floor((point.X + radius + 32.0) / 64.0);
            var minZ = (int)Math.Ceiling((point.Z - radius - 32.0) / 64.0);
            var maxZ = (int)Math.Floor((point.Z + radius + 32.0) / 64.0);
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = Math.Max(Math.Abs(point.X - x * 64.0) - 32.0, 0.0);
                    var dz = Math.Max(Math.Abs(point.Z - z * 64.0) - 32.0, 0.0);
                    if (dx * dx + dz * dz > radius * radius) { continue; }
                    var key = ((long)x << 32) | (uint)z;
                    if (!zones.ContainsKey(key))
                    {
                        zones.Add(key, new ZoneEntry { X = x, Z = z, Origin = ExplorationOrigin.PointOfInterest });
                    }
                }
            }
        }

        // Excludes undefined and unbounded numeric positions and radii.
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
