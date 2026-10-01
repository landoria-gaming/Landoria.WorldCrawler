using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Prevents interactions and building while the unattended flight owns the player.
    [HarmonyPatch(typeof(Player), "TakeInput", new System.Type[0])]
    internal static class PlayerInputPatch
    {
        // Returns no gameplay input while leaving the crawler's F8 handling independent.
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (!FlightController.IsControlled(__instance))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
