using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Prevents the native stamina debit while leaving normal regeneration and movement intact.
    [HarmonyPatch(typeof(Player), "RPC_UseStamina", new[] { typeof(long), typeof(float) })]
    internal static class StaminaProtectionPatch
    {
        // Patches the shared local/RPC debit path without enabling debug mode or native god flags.
        private static bool Prefix(Player __instance)
        {
            return !PlayerProtection.Applies(__instance);
        }
    }
}
