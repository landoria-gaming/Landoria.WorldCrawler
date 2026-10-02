using HarmonyLib;

namespace Landoria.WorldCrawler.Capture
{
    // Copies final local state before the native pool clears or reuses an object.
    [HarmonyPatch(typeof(ZDOPool), "Release", typeof(ZDO))]
    internal static class CaptureUnloadPatch
    {
        // Actual destruction is already tombstoned; simple unload must not imply deletion.
        private static void Prefix(ZDO zdo)
        {
            RecordingObserver.Received(zdo, false);
        }
    }
}
