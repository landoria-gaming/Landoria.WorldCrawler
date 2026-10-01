using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Prevents native ground recovery from moving a player whose body is owned by the crawler.
    [HarmonyPatch(typeof(Character), "UnderWorldCheck", new[] { typeof(float) })]
    internal static class UnderWorldCheckPatch
    {
        // Keeps native recovery for other characters, ordinary play and native teleport transitions.
        private static bool Prefix(Character __instance)
        {
            return !FlightController.OwnsFlightMotion(__instance);
        }
    }
}
