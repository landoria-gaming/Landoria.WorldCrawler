using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Defers native ground correction until imported terrain and neighboring supports are complete.
    [HarmonyPatch(typeof(ItemDrop), "TerrainCheck", new System.Type[0])]
    internal static class RestoreItemTerrainPatch
    {
        // Avoids moving a deliberately frozen imported drop or writing velocity to its kinematic body.
        private static bool Prefix(ItemDrop __instance)
        {
            return !RestoreProtection.Pending(__instance.GetComponent<ZNetView>());
        }
    }
}
