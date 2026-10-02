using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.UI
{
    // Mirrors the native map projection without changing map pixels or exploration flags.
    internal static class ExportMapOverlayGeometry
    {
        // Merges touching sectors so large completed areas need very few UI vertices.
        public static ExportMapOverlayRegion[] Merge(IEnumerable<ZoneEntry> zones)
        {
            var result = new List<ExportMapOverlayRegion>();
            foreach (var zone in zones.OrderBy(value => value.Z).ThenBy(value => value.X))
            {
                if (result.Count != 0)
                {
                    var previous = result[result.Count - 1];
                    if (previous.Z == zone.Z && (long)previous.MaxX + 1 == zone.X)
                    {
                        result[result.Count - 1] = new ExportMapOverlayRegion(previous.MinX, zone.X, zone.Z);
                        continue;
                    }
                }
                result.Add(new ExportMapOverlayRegion(zone.X, zone.X, zone.Z));
            }
            return result.ToArray();
        }

        // Projects exact 64-metre sector bounds and clips all four sides to the map image.
        public static bool Project(ExportMapOverlayRegion region, int textureSize, double pixelSize,
            double uvX, double uvY, double uvWidth, double uvHeight, out ExportMapOverlayBounds bounds)
        {
            bounds = default;
            if (textureSize <= 0 || !Positive(pixelSize) || !Positive(uvWidth) || !Positive(uvHeight)
                || !Finite(uvX) || !Finite(uvY) || region.MaxX < region.MinX)
            {
                return false;
            }
            var origin = (double)(textureSize / 2) / textureSize;
            var scale = pixelSize * textureSize;
            var left = Math.Max(0, ((region.MinX * 64.0 - 32) / scale + origin - uvX) / uvWidth);
            var right = Math.Min(1, ((region.MaxX * 64.0 + 32) / scale + origin - uvX) / uvWidth);
            var bottom = Math.Max(0, ((region.Z * 64.0 - 32) / scale + origin - uvY) / uvHeight);
            var top = Math.Min(1, ((region.Z * 64.0 + 32) / scale + origin - uvY) / uvHeight);
            if (right <= left || top <= bottom)
            {
                return false;
            }
            bounds = new ExportMapOverlayBounds(left, bottom, right, top);
            return true;
        }

        // Rejects invalid view scales before they reach mesh coordinates.
        private static bool Positive(double value)
        {
            return value > 0 && Finite(value);
        }

        // Excludes NaN and infinities without requiring recent framework APIs.
        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
