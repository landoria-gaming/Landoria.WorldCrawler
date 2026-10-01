using HarmonyLib;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps loose item identities separate until the import has finished validating and saving.
    [HarmonyPatch(typeof(ItemDrop), "AutoStackItems", new System.Type[0])]
    internal static class RestoreItemStackPatch
    {
        // Stops both imported and native stacks from consuming imported items during restoration.
        private static bool Prefix(ItemDrop __instance)
        {
            return !RestoreProtection.Active && !RestoreProtection.Pending(__instance.GetComponent<ZNetView>());
        }
    }
}
