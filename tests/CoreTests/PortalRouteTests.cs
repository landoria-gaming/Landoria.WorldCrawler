using Landoria.WorldCrawler.Runtime;

namespace Landoria.WorldCrawler.Tests
{
    // Exercises portal route decisions without requiring a connected game or Unity objects.
    internal static class PortalRouteTests
    {
        // Verifies useful links beat direct travel while detours and tiny gains do not.
        internal static void Run()
        {
            TestSupport.Check(PortalRouteMetric.Saving(0, 100, 0, 0, 0, 0, 900, 0, 1000, 0) > 0,
                "A nearby portal spanning most of a long journey should be used.");
            TestSupport.Check(PortalRouteMetric.Saving(0, 100, 0, -1000, 0, 0, 900, 0, 1000, 0) < 0,
                "A faraway entry portal must not create a longer detour.");
            TestSupport.Check(PortalRouteMetric.Saving(0, 0, 0, 0, 0, 0, 100, 0, 120, 0) < 0,
                "A tiny distance saving must not justify a portal transition.");
            TestSupport.Check(PortalRouteMetric.Saving(0, 0, 0, 0, 0, 0, -900, 0, 1000, 0) < 0,
                "A portal leading away from the target must not be selected.");
            TestSupport.Check(PortalRouteMetric.Saving(0, 1000, 0, 0, 0, 0, 900, 0, 1000, 0) < 0,
                "The portal approach must include the height difference.");
            TestSupport.Check(PortalRouteMetric.Saving(0, 0, 0, 0, 0, 0, 128, 0, 128, 0) == 0,
                "The exact benefit threshold is not a strictly useful shortcut.");
            CoordinateJumpPolicy();
        }

        // Keeps disabled jumps, short legs, repeated attempts and malformed thresholds on the flight path.
        private static void CoordinateJumpPolicy()
        {
            TestSupport.Check(PortalRouteMetric.ShouldJump(true, false, 500, 500),
                "An authorized distant leg at the threshold should permit one coordinate jump.");
            TestSupport.Check(!PortalRouteMetric.ShouldJump(false, false, 1000, 500),
                "Disabling coordinate jumps must preserve the flight fallback.");
            TestSupport.Check(!PortalRouteMetric.ShouldJump(true, true, 1000, 500),
                "A refused jump must not be retried on the same leg.");
            TestSupport.Check(!PortalRouteMetric.ShouldJump(true, false, 499.9, 500),
                "Nearby zones must be reached by flight.");
            TestSupport.Check(!PortalRouteMetric.ShouldJump(true, false, double.NaN, 500) &&
                !PortalRouteMetric.ShouldJump(true, false, 1000, double.NaN) &&
                !PortalRouteMetric.ShouldJump(true, false, double.PositiveInfinity, 500) &&
                !PortalRouteMetric.ShouldJump(true, false, 1000, 0),
                "Invalid travel distances or thresholds must not authorize a native jump.");
            TestSupport.Check(PortalRouteMetric.ShouldJump(false, false, 8, double.NaN, true)
                && !PortalRouteMetric.ShouldJump(false, false, 7.9, 500, true)
                && !PortalRouteMetric.ShouldJump(true, true, 1000, 500, true),
                "Forced return jumps ignore ordinary settings but remain non-trivial and single-attempt.");
        }
    }
}
