using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Tests
{
    // Exercises receipt-driven pauses without sleeping or loading Unity.
    internal static class CaptureStabilityTests
    {
        // Checks immediate stops, delayed bursts, exact resume timing and strict capture readiness.
        internal static void Run()
        {
            TestSupport.Check(!CaptureStability.PauseForReceive(0, float.NegativeInfinity), "No traffic should not delay motion.");
            TestSupport.Check(CaptureStability.PauseForReceive(10, 10), "A receipt must stop motion immediately.");
            TestSupport.Check(CaptureStability.PauseForReceive(11.99f, 10), "Motion resumed before two quiet seconds.");
            TestSupport.Check(!CaptureStability.PauseForReceive(12, 10), "Motion should resume at two seconds.");
            TestSupport.Check(CaptureStability.PauseForReceive(12, 11.5f), "A later burst must extend the pause.");
            TestSupport.Check(!CaptureStability.PauseForReceive(13.5f, 11.5f), "A burst caused unnecessary extra waiting.");
            TestSupport.Check(CaptureStability.QuietSeconds(50) == 2, "Normal latency must not add a fixed dwell.");
            TestSupport.Check(CaptureStability.QuietSeconds(2000) == 4.5f, "High latency must extend capture verification.");
            TestSupport.Check(CaptureStability.Ready(2, 0, 12, 10, 10, 2), "Stable complete observations should be accepted.");
            TestSupport.Check(!CaptureStability.Ready(1, 0, 12, 10, 10, 2), "One pass is not sufficient.");
            TestSupport.Check(!CaptureStability.Ready(2, 1, 12, 10, 10, 2), "Pending instances cannot be accepted.");
            TestSupport.Check(!CaptureStability.Ready(2, 0, 12, 11, 10, 2), "New membership must restart stability.");
            TestSupport.Check(!CaptureStability.Ready(2, 0, 12, 10, 11.9f, 2), "A late receipt must prevent a stale commit.");
        }
    }
}
