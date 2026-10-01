using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Rejects damage only for the local player while World Crawler owns its movement.
    [HarmonyPatch(typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) })]
    internal static class DamageProtectionPatch
    {
        // Avoids Valheim's god/debug flags so normal achievement eligibility is unchanged.
        private static bool Prefix(Character __instance)
        {
            return !FlightController.IsControlled(__instance);
        }
    }
}
