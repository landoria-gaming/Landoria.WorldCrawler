using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Allows running immediately even if the character joined with an empty stamina bar.
    [HarmonyPatch(typeof(Player), "HaveStamina", new[] { typeof(float) })]
    internal static class StaminaAvailabilityPatch
    {
        // Keeps all remote-player checks native.
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (PlayerProtection.Applies(__instance))
            {
                __result = true;
            }
        }
    }
}
