using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Keeps source-world drops and character inventories untouched by the survey.
    [HarmonyPatch(typeof(Player), "AutoPickup", new[] { typeof(float) })]
    internal static class AutoPickupPatch
    {
        // Skips nearby item collection only for the controlled player.
        private static bool Prefix(Player __instance)
        {
            return !FlightController.IsControlled(__instance);
        }
    }
}
