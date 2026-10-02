using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reconciles late native arrivals in loaded sectors already restored during F10.
    internal sealed partial class RestorationController
    {
        private int _cleanupIndex;
        private float _nextCleanup;

        // Checks one restored sector per interval and checkpoints any additional removals normally.
        private void CleanupRestoredZone()
        {
            if (Time.unscaledTime < _nextCleanup)
            {
                return;
            }
            _nextCleanup = Time.unscaledTime + 2f;
            var zones = _session.Archive.Manifest.Zones.Where(zone =>
                _scope.Contains(zone.X, zone.Z) && _visited.Contains(ZoneKey(zone.X, zone.Z)) &&
                (_applied.Contains(ZoneKey(zone.X, zone.Z)) ||
                    _session.Journal.State.Completed.Contains(ZoneKey(zone.X, zone.Z)))).ToArray();
            if (zones.Length == 0)
            {
                return;
            }
            _cleanupIndex %= zones.Length;
            var current = zones[_cleanupIndex++];
            if (!Capture.NearZoneScope.Ready(current.X, current.Z) ||
                _objects.CleanupZone(current.X, current.Z) == 0)
            {
                return;
            }
            var key = ZoneKey(current.X, current.Z);
            _applied.Add(key);
            _dirty = true;
            _session.Journal.Save();
        }
    }
}
