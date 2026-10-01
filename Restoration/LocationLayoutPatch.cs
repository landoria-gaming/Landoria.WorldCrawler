using System;
using HarmonyLib;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Replays imported static layout when the native game finishes spawning a location proxy.
    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation", new Type[] { })]
    internal static class LocationLayoutPatch
    {
        // Leaves ordinary proxies untouched and reports incompatible persisted layouts.
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (!__result) { return; }
            try { LocationLayout.Replay(__instance); }
            catch (Exception error) { Debug.LogWarning("World Crawler location layout skipped: " + error.Message); }
        }
    }
}
