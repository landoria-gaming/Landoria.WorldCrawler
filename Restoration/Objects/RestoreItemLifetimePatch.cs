using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Prevents source-world item ages from expiring unfinished imported drops.
    [HarmonyPatch(typeof(ItemDrop), "TimedDestruction", new System.Type[0])]
    internal static class RestoreItemLifetimePatch
    {
        // Keeps protection through pauses and reloads using the saved restoration marker.
        private static bool Prefix(ItemDrop __instance)
        {
            return !RestoreProtection.Pending(__instance.GetComponent<ZNetView>());
        }
    }
}
