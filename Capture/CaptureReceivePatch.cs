using HarmonyLib;

namespace Landoria.WorldCrawler.Capture
{
    // Observes deserialized network records without reading or consuming the socket queue.
    [HarmonyPatch(typeof(ZDO), "Deserialize", new[] { typeof(ZPackage) })]
    internal static class CaptureReceivePatch
    {
        // Notifies the active capture only after the complete object packet has been applied.
        private static void Postfix(ZDO __instance)
        {
            RecordingObserver.Received(__instance);
        }
    }
}
