using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Restoration
{
    // Removes only our stale origin copy after an explicit source-sector crossing.
    internal sealed partial class ObjectRestorer
    {
        // Confirms queued native removals before recording a successful save boundary.
        public bool DeletionsPending()
        {
            _removed.RemoveWhere(id => ZDOMan.instance.GetZDO(id) == null);
            return _removed.Count != 0 || _cleanup.DeletionsPending();
        }

        // Never affects an already-relocated copy or unrelated player-owned/generated objects.
        public void ApplyDeparture(CapturedDeparture source, int originX, int originZ)
        {
            var key = source.SourceUser + ":" + source.SourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var target = ResolveKey(key);
            if (target == null || !CaptureTransform.InZone(target.GetPosition(), originX, originZ))
            {
                return;
            }
            EnsureSafe(target, source.PrefabHash);
            _removed.Add(target.m_uid);
            target.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(target);
        }
    }
}
