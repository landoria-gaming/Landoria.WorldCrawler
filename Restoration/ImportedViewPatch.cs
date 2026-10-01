using System;
using HarmonyLib;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reapplies imported scale and holds loose-object physics while an import remains incomplete.
    [HarmonyPatch(typeof(ZNetView), "Awake", new Type[] { })]
    internal static class ImportedViewPatch
    {
        // Affects only source-tagged non-creature objects created by this importer.
        private static void Postfix(ZNetView __instance)
        {
            var data = __instance.GetZDO();
            if (data == null || string.IsNullOrEmpty(data.GetString(ObjectRestorer.IdentityTag, "")) ||
                RestoreProtection.Protected(__instance.gameObject))
            {
                return;
            }
            var scale = data.GetVec3("WorldCrawler.scale", __instance.transform.localScale);
            if (float.IsNaN(scale.sqrMagnitude) || float.IsInfinity(scale.sqrMagnitude))
            {
                Debug.LogWarning("World Crawler ignored invalid imported scale: target=" + data.m_uid +
                    "; position=" + data.GetPosition() + ". Native scale was kept.");
                return;
            }
            __instance.transform.localScale = scale;
            if (!data.GetBool("WorldCrawler.pending", false))
            {
                return;
            }
            foreach (var body in __instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.useGravity = false;
            }
        }
    }
}
