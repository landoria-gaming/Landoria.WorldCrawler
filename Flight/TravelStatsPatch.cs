using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Avoids counting crawler movement as normal player exploration and travel progress.
    [HarmonyPatch(typeof(Player), "UpdateStats", new System.Type[0])]
    internal static class TravelStatsPatch
    {
        // Leaves real counters untouched and lets only the separate survival update run.
        private static bool Prefix(Player __instance)
        {
            return !FlightController.IsControlled(__instance);
        }
    }
}
