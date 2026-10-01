using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Prevents discovering new crafting recipes just by passing nearby source structures.
    [HarmonyPatch(typeof(Player), "UpdateStations", new[] { typeof(float) })]
    internal static class StationDiscoveryPatch
    {
        // Leaves existing known crafting stations unchanged during the capture.
        private static bool Prefix(Player __instance)
        {
            return !FlightController.IsControlled(__instance);
        }
    }
}
