using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps floating loot at its imported position until terrain restoration is complete.
    [HarmonyPatch(typeof(Floating), "TerrainCheck", new System.Type[0])]
    internal static class RestoreFloatingTerrainPatch
    {
        // Leaves ordinary floating items on their native terrain-correction path.
        private static bool Prefix(Floating __instance)
        {
            return !RestoreProtection.Pending(__instance.GetComponent<ZNetView>());
        }
    }
}
