using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Removes environmental cold effects after the game has evaluated the controlled player.
    [HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects", new[] { typeof(float) })]
    internal static class ColdProtectionPatch
    {
        // Leaves every other environmental status untouched and applies only during F8 control.
        private static void Postfix(Player __instance)
        {
            if (!FlightController.IsControlled(__instance))
            {
                return;
            }
            var effects = __instance.GetSEMan();
            effects.RemoveStatusEffect(SEMan.s_statusEffectCold, true);
            effects.RemoveStatusEffect(SEMan.s_statusEffectFreezing, true);
        }
    }
}
