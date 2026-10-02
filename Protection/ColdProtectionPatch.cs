using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Removes environmental cold effects after the game has evaluated the protected local player.
    [HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects", new[] { typeof(float) })]
    internal static class ColdProtectionPatch
    {
        // Leaves every other environmental status untouched and applies only while the plugin is active.
        private static void Postfix(Player __instance)
        {
            if (!PlayerProtection.Applies(__instance))
            {
                return;
            }
            var effects = __instance.GetSEMan();
            effects.RemoveStatusEffect(SEMan.s_statusEffectCold, true);
            effects.RemoveStatusEffect(SEMan.s_statusEffectFreezing, true);
        }
    }
}
