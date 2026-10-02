using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Prevents temporary physics or nearby creatures from destroying unfinished imported scenery.
    [HarmonyPatch(typeof(Destructible), "RPC_Damage", new[] { typeof(long), typeof(HitData) })]
    internal static class RestoreDestructibleDamagePatch
    {
        // Protects only marked imported objects, including after a pause or reload.
        private static bool Prefix(Destructible __instance)
        {
            return !RestoreProtection.Pending(__instance.GetComponent<ZNetView>());
        }
    }
}
