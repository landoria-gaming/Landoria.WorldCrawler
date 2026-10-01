using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Inventory;

namespace Landoria.WorldCrawler.Storage
{
    // Merges observed landmarks without dropping points absent from the current client cache.
    internal static class LandmarkSelectionBuilder
    {
        // Checks selection metadata without regenerating its geometry during every manifest save.
        internal static void Validate(LandmarkSelection selection)
        {
            if (selection == null)
            {
                return;
            }
            if (selection.Mode != "landmarks")
            {
                throw new NotSupportedException("Unsupported World Crawler selection mode.");
            }
            try
            {
                LandmarkZoneInventory.Validate(Inventory(selection), selection.Radius);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Landmark selection metadata is invalid.", error);
            }
        }

        // Keeps known points and replaces only matching IDs with newer observations.
        internal static LandmarkSelection Merge(LandmarkSelection previous, LandmarkInventory observed, float radius)
        {
            LandmarkZoneInventory.Validate(observed, radius);
            Validate(previous);
            if (previous != null && previous.Radius != radius)
            {
                throw new InvalidOperationException("This landmark crawl uses a different radius; use its separate export directory.");
            }
            var points = new Dictionary<string, LandmarkPoint>(StringComparer.Ordinal);
            foreach (var point in previous == null ? Enumerable.Empty<LandmarkPoint>() : previous.Points)
            {
                if (!IsMapPin(point))
                {
                    points[point.Id] = Copy(point);
                }
            }
            foreach (var point in observed.Points)
            {
                points[point.Id] = Copy(point);
            }
            var warnings = (previous == null ? Enumerable.Empty<string>() : previous.Warnings).Concat(observed.Warnings);
            return new LandmarkSelection
            {
                Radius = radius,
                Points = points.Values.OrderBy(point => point.Id, StringComparer.Ordinal).ToList(),
                Warnings = warnings.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList()
            };
        }

        // Adapts stored metadata to the pure landmark geometry API.
        internal static LandmarkInventory Inventory(LandmarkSelection selection)
        {
            return new LandmarkInventory { Points = selection.Points, Warnings = selection.Warnings };
        }

        // Preserves completed records by coordinate while keeping removed files untouched on disk.
        internal static List<ZoneEntry> Zones(WorldManifest manifest, InventoryResult selected)
        {
            var previous = manifest.Zones.ToDictionary(zone => Key(zone.X, zone.Z));
            return selected.Zones.Select(zone => Existing(previous, zone)).OrderBy(zone => zone.Z)
                .ThenBy(zone => zone.X).ToList();
        }

        // Reuses the existing entry when a selected coordinate was already inventoried.
        private static ZoneEntry Existing(Dictionary<long, ZoneEntry> previous, ZoneEntry selected)
        {
            ZoneEntry existing;
            if (!previous.TryGetValue(Key(selected.X, selected.Z), out existing))
            {
                return new ZoneEntry { X = selected.X, Z = selected.Z, Origin = ExplorationOrigin.PointOfInterest };
            }
            return existing;
        }

        // Makes caller-owned landmark objects independent of the persisted selection.
        private static LandmarkPoint Copy(LandmarkPoint point)
        {
            return new LandmarkPoint
            {
                Id = point.Id,
                Kind = point.Kind,
                Name = point.Name,
                Source = point.Source,
                X = point.X,
                Y = point.Y,
                Z = point.Z
            };
        }

        // Replaces map-only evidence, including combined native/personal provenance, on every refresh.
        private static bool IsMapPin(LandmarkPoint point)
        {
            return point.Source.Split('+').All(source => source == "personal-map-pin"
                || source == "shared-map-pin" || source == "native-location-icon");
        }

        // Packs signed coordinates into a unique dictionary key.
        private static long Key(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }
    }
}
