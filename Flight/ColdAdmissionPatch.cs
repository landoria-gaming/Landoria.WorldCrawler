using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Rejects cold effects before their startup messages and visuals run while the plugin is active.
    [HarmonyPatch(typeof(StatusEffect), "CanAdd", new[] { typeof(Character) })]
    internal static class ColdAdmissionPatch
    {
        // Leaves all other effects and all other characters on their native path.
        private static bool Prefix(StatusEffect __instance, Character character, ref bool __result)
        {
            if (!PlayerProtection.Applies(character))
            {
                return true;
            }
            var hash = __instance.NameHash();
            if (hash != SEMan.s_statusEffectCold && hash != SEMan.s_statusEffectFreezing)
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
