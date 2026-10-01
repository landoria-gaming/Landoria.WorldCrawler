using System.Collections.Generic;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Chooses one useful native portal link using only endpoints already received by this client.
    internal static class PortalRoutePlanner
    {
        // Finds the greatest distance saving and never guesses a missing portal destination.
        internal static PortalRoute Choose(Vector3 current, Vector3 destination, HashSet<ZDOID> failed)
        {
            PortalRoute chosen = null;
            var best = 0.0;
            foreach (var source in LandmarkPortalSource.Portals())
            {
                if (!LandmarkPortalSource.ValidPortal(source) || failed.Contains(source.m_uid)) { continue; }
                var targetId = source.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (targetId == ZDOID.None || targetId == source.m_uid) { continue; }
                var target = ZDOMan.instance.GetZDO(targetId);
                if (!LandmarkPortalSource.ValidPortal(target) ||
                    target.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != source.m_uid) { continue; }
                var start = source.GetPosition();
                var end = target.GetPosition();
                if (!Valid(start) || !Valid(end)) { continue; }
                var saving = PortalRouteMetric.Saving(current.x, current.y, current.z, start.x, start.y, start.z,
                    end.x, end.z, destination.x, destination.z);
                if (saving <= best) { continue; }
                best = saving;
                chosen = new PortalRoute { Source = source.m_uid, Target = targetId,
                    SourcePosition = start, TargetPosition = end };
            }
            return chosen;
        }

        // Rejects malformed or out-of-world cached endpoint positions before route planning.
        private static bool Valid(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && Mathf.Abs(value.x) <= 20000f
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && Mathf.Abs(value.y) <= 20000f
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z) && Mathf.Abs(value.z) <= 20000f;
        }
    }
}
