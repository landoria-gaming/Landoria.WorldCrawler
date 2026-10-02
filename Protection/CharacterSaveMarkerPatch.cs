using HarmonyLib;

namespace Landoria.WorldCrawler.Protection
{
    // Prevents the loaded local profile from persisting a newly set cheat marker.
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    internal static class CharacterSaveMarkerPatch
    {
        // Ignores other profiles and old games through the checked character policy.
        private static void Prefix(PlayerProfile __instance)
        {
            CharacterMarkerPolicy.Enforce(__instance);
        }
    }
}
