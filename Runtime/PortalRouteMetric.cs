using System;

namespace Landoria.WorldCrawler.Runtime
{
    // Compares native portal travel against direct horizontal flight without game dependencies.
    internal static class PortalRouteMetric
    {
        // Requires a meaningful saving after the approach, exit leg, and portal loading allowance.
        internal static double Saving(double playerX, double playerY, double playerZ,
            double sourceX, double sourceY, double sourceZ, double exitX, double exitZ,
            double targetX, double targetZ)
        {
            var direct = Distance(playerX - targetX, 0, playerZ - targetZ);
            var approach = Distance(playerX - sourceX, playerY - sourceY, playerZ - sourceZ);
            var onward = Distance(exitX - targetX, 0, exitZ - targetZ);
            return direct - approach - onward - 128.0;
        }

        // Honors the explicit jump option, bounded threshold, and one-attempt-per-leg rule.
        internal static bool ShouldJump(bool enabled, bool attempted, double distance, double threshold, bool forced = false)
        {
            if (attempted || double.IsNaN(distance) || double.IsInfinity(distance)) { return false; }
            if (forced) { return distance >= 8.0; }
            return enabled && !double.IsNaN(threshold) && !double.IsInfinity(threshold)
                && threshold >= 128.0 && threshold <= 10000.0 && distance >= threshold;
        }

        // Computes an ordinary Euclidean distance for either horizontal or three-dimensional legs.
        private static double Distance(double x, double y, double z)
        {
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
