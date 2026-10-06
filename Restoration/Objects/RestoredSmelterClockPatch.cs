using HarmonyLib;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Clears a negative processing clock left by earlier imports from another world.
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class RestoredSmelterClockPatch
    {
        // Repairs only locally owned smelters marked as restored by World Crawler.
        private static void Prefix(Smelter __instance)
        {
            var view = __instance.GetComponent<ZNetView>() ?? __instance.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }
            var zdo = view.GetZDO();
            if (string.IsNullOrEmpty(zdo.GetString(ObjectRestorer.IdentityTag, "")))
            {
                return;
            }
            var now = ZNet.instance.GetTime().Ticks;
            if (zdo.GetLong(ZDOVars.s_startTime, now) > now)
            {
                zdo.Set(ZDOVars.s_startTime, now);
            }
            if (zdo.GetFloat(ZDOVars.s_accTime) < 0f)
            {
                zdo.Set(ZDOVars.s_accTime, 0f);
            }
        }
    }
}
