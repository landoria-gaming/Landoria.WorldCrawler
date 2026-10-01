using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Finds a nearby loaded landing point outside the source portal's trigger volume.
    internal static class PortalApproach
    {
        // Uses the highest solid surface so descent does not pass through a roof or terrain.
        internal static bool TryPosition(TeleportWorld portal, out Vector3 position)
        {
            position = default(Vector3);
            if (portal == null || !portal.isActiveAndEnabled || ZoneSystem.instance == null
                || !ZNetScene.instance.IsAreaReady(portal.transform.position)) { return false; }
            var reach = portal.m_activationRange;
            if (float.IsNaN(reach) || float.IsInfinity(reach) || reach < 2f || reach > 20f) { return false; }
            var offset = Mathf.Clamp(reach - 0.75f, 1.25f, 4f);
            foreach (var side in new[] { 1f, -1f })
            {
                var candidate = portal.transform.position + portal.transform.forward * offset * side;
                if (!ZoneSystem.instance.GetSolidHeight(candidate, out var height, 1000)) { continue; }
                candidate.y = height + 0.5f;
                if (Vector3.Distance(candidate, portal.transform.position) > reach ||
                    !OutsideTriggers(portal, candidate)) { continue; }
                position = candidate;
                return true;
            }
            return false;
        }

        // Leaves a body-width margin around each active portal trigger before invoking native travel.
        private static bool OutsideTriggers(TeleportWorld portal, Vector3 position)
        {
            foreach (var collider in portal.GetComponentsInChildren<Collider>())
            {
                if (!collider.isTrigger || !collider.enabled) { continue; }
                if (collider.bounds.SqrDistance(position + Vector3.up) < 1f) { return false; }
            }
            return true;
        }
    }
}
