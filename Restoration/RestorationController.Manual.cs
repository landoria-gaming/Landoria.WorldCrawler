using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Restores loaded neighboring sectors once per encounter; the user controls every journey.
    internal sealed partial class RestorationController
    {
        // Shows restored zones in green immediately, including those awaiting a native save.
        internal ExportMapOverlayData MapProgress()
        {
            return _session?.Archive == null ? null :
                ProgressMapSnapshot.Restore(_session.Archive.Manifest.Zones,
                    _session.Journal.State.Completed.Concat(_applied));
        }

        // Queues source files in actual near coverage without steering toward them.
        private void WaitForZone()
        {
            if (_dirty && Time.unscaledTime - _lastSave >= CrawlerConstants.RestoreSaveInterval)
            {
                QueueSave(false, false);
                return;
            }
            var keys = new HashSet<string>(_scope.Zones.Select(zone => ZoneKey(zone.X, zone.Z)));
            _visited.RemoveWhere(key => !keys.Contains(key));
            CleanupRestoredZone();
            _zone = _session.Archive.Manifest.Zones.Where(zone =>
                    _scope.Contains(zone.X, zone.Z) && !_visited.Contains(ZoneKey(zone.X, zone.Z)))
                .OrderBy(zone => Restored(ZoneKey(zone.X, zone.Z)) ? 1 : 0)
                .FirstOrDefault();
            if (_zone == null)
            {
                return;
            }
            var archive = _session.Archive;
            var zoneToRead = _zone;
            _read = Task.Run(() => new ZoneImportData { Snapshot = archive.ReadZone(zoneToRead),
                Records = archive.ZoneObjects(zoneToRead.X, zoneToRead.Z) });
            _waitingSince = Time.unscaledTime;
            _phase = RestorePhase.Reading;
        }

        // Gives never-restored amber zones priority over repair passes for green zones.
        private bool Restored(string key)
        {
            return _applied.Contains(key) || _session.Journal.State.Completed.Contains(key);
        }

        // Silently suspends imports during teleports; native checkpoints may still finish.
        private bool ManualTransit()
        {
            var transit = Player.m_localPlayer.IsTeleporting();
            _teleportTransit |= transit;
            _scope.Refresh(Player.m_localPlayer.transform.position);
            if (_zone != null && (_teleportTransit || !_scope.Contains(_zone.X, _zone.Z)) &&
                (_phase == RestorePhase.Reading || _phase == RestorePhase.Restoring || _phase == RestorePhase.Connecting))
            {
                if (_read != null && !_read.IsCompleted)
                {
                    return true;
                }
                var key = ZoneKey(_zone.X, _zone.Z);
                if (!_teleportTransit)
                {
                    AddWarning("External travel interrupted zone " + key +
                        "; unfinished work remains pending for a later visit.");
                }
                _visited.Remove(key);
                SuspendZone();
                _phase = RestorePhase.Waiting;
            }
            _teleportTransit = transit;
            return transit && !IsSavePhase();
        }

        // Waits for terrain and native center-area creation without manufacturing a completion timeout.
        private bool ZoneReady()
        {
            if (NearZoneScope.Ready(_zone.X, _zone.Z))
            {
                return true;
            }
            if (Time.unscaledTime - _waitingSince >= 120f)
            {
                AddWarning("Zone " + ZoneKey(_zone.X, _zone.Z) +
                    " is still loading. Move freely or press F10 to stop safely.");
                _waitingSince = Time.unscaledTime;
            }
            return false;
        }

        // Discards only the unfinished operation, not imported identities or previous source files.
        private void SuspendZone()
        {
            if (_read != null && _read.IsCompleted)
            {
                if (_read.IsFaulted)
                {
                    var observed = _read.Exception;
                }
                _read = null;
            }
            _scan?.Dispose();
            _scan = null;
            _writer = null;
            _zone = null;
        }

    }
}
