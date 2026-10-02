using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Keeps unfinished restored drops out of player inventories during pauses and reloads.
    [HarmonyPatch(typeof(ItemDrop), "CanPickup", new[] { typeof(bool) })]
    internal static class RestoreItemPickupPatch
    {
        // Resumes ordinary pickup behavior when finalization clears the persistent marker.
        private static bool Prefix(ItemDrop __instance, ref bool __result)
        {
            if (!RestoreProtection.Pending(__instance.GetComponent<ZNetView>()))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
