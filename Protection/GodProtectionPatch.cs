using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Makes native god-protection checks honor the local plugin without setting a saved cheat field.
    [HarmonyPatch(typeof(Player), "InGodMode", new System.Type[] { })]
    internal static class GodProtectionPatch
    {
        // Keeps every other player's native protection unchanged.
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (PlayerProtection.Applies(__instance))
            {
                __result = true;
            }
        }
    }
}
