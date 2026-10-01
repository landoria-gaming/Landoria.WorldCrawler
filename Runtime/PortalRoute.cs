using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Freezes the two native endpoint identities chosen for one travel leg.
    internal sealed class PortalRoute
    {
        internal ZDOID Source;
        internal ZDOID Target;
        internal Vector3 SourcePosition;
        internal Vector3 TargetPosition;

        // Rechecks the actual link and refuses moved, destroyed, or retagged endpoints.
        internal bool TryResolve(out ZDO source, out ZDO target)
        {
            source = ZDOMan.instance.GetZDO(Source);
            target = ZDOMan.instance.GetZDO(Target);
            return LandmarkPortalSource.ValidPortal(source) && LandmarkPortalSource.ValidPortal(target)
                && source.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == Target
                && target.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == Source
                && Vector3.Distance(source.GetPosition(), SourcePosition) < 1f
                && Vector3.Distance(target.GetPosition(), TargetPosition) < 1f;
        }
    }
}
