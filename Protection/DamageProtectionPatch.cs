using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Rejects damage only for the local player while the plugin is active.
    [HarmonyPatch(typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) })]
    internal static class DamageProtectionPatch
    {
        // Avoids Valheim's god/debug flags so normal achievement eligibility is unchanged.
        private static bool Prefix(Character __instance)
        {
            return !PlayerProtection.Applies(__instance);
        }
    }
}
