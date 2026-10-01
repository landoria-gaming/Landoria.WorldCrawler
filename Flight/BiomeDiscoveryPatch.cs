using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Prevents automatic biome and location discoveries during an unattended survey.
    [HarmonyPatch(typeof(Player), "UpdateBiome", new[] { typeof(float) })]
    internal static class BiomeDiscoveryPatch
    {
        // Preserves the player's discovered-biome data while the crawler travels.
        private static bool Prefix(Player __instance)
        {
            return !FlightController.IsControlled(__instance);
        }
    }
}
