using System;

namespace Landoria.WorldCrawler.Capture
{
    // Defines bounded evidence-based quiet periods without an arbitrary minimum zone dwell.
    internal static class CaptureStability
    {
        // Resets the two-second motion pause on every relevant receipt, with no initial blind wait.
        internal static bool PauseForReceive(float now, float lastReceived) { return now - lastReceived < 2f; }

        // Keeps two seconds of relevant silence, extending it only for unusually high latency.
        internal static float QuietSeconds(int pingMilliseconds)
        {
            return Math.Max(2f, Math.Max(0, pingMilliseconds) * 0.002f + 0.5f);
        }

        // Rejects partial samples, pending instances and evidence received after the stable window began.
        internal static bool Ready(int passes, int pending, float now, float stableSince,
            float receivedSince, float quiet)
        {
            return passes >= 2 && pending == 0 && now - Math.Max(stableSince, receivedSince) >= quiet;
        }
    }
}
