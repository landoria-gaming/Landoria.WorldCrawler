using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration
{
    // Avoids evaluating incomplete building support while the importer creates neighboring pieces.
    [HarmonyPatch(typeof(WearNTear), "UpdateWear", new[] { typeof(float) })]
    internal static class RestoreWearPatch
    {
        // Leaves every object outside active restoration zones on its ordinary update path.
        private static bool Prefix(WearNTear __instance)
        {
            var view = __instance.GetComponent<ZNetView>();
            return !(view != null && view.GetZDO() != null && view.GetZDO().GetBool("WorldCrawler.pending", false)) &&
                !RestoreProtection.HoldWear(__instance.transform.position);
        }
    }
}
