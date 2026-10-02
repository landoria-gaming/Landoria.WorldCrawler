using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Presents the protected local player as a ghost to native AI queries without changing its cheat field.
    [HarmonyPatch(typeof(Player), "InGhostMode", new System.Type[] { })]
    internal static class GhostProtectionPatch
    {
        // Native monsters, animals, turrets and perception helpers all honor this query.
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (PlayerProtection.Applies(__instance))
            {
                __result = true;
            }
        }
    }
}
