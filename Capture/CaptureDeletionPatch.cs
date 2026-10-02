using HarmonyLib;

namespace Landoria.WorldCrawler.Capture
{
    // Observes explicit destruction, unlike ordinary local instance unloading.
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO", typeof(ZDOID))]
    internal static class CaptureDeletionPatch
    {
        // Records the identity before the engine releases its native data.
        private static void Prefix(ZDOID uid)
        {
            RecordingObserver.Destroyed(uid);
        }
    }
}
