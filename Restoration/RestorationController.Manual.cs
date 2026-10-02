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
        // Shows durable saved revisions in green; applied but unsaved changes remain amber.
        internal ExportMapOverlayData MapProgress()
        {
            return _session?.Archive == null ? null :
                ProgressMapSnapshot.Restore(_session.Archive.Manifest.Zones, _session.Journal.State.Completed);
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
            _zone = _session.Archive.Manifest.Zones.FirstOrDefault(zone =>
                _scope.Contains(zone.X, zone.Z) && !_visited.Contains(ZoneKey(zone.X, zone.Z)));
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

        // Suspends unfinished mutations when free movement or a teleport leaves the loaded sector.
        private bool ManualTransit()
        {
            var transit = Player.m_localPlayer.IsTeleporting();
            _scope.Refresh(Player.m_localPlayer.transform.position);
            if (_zone != null && (transit || !_scope.Contains(_zone.X, _zone.Z)) &&
                (_phase == RestorePhase.Reading || _phase == RestorePhase.Restoring || _phase == RestorePhase.Connecting))
            {
                if (_read != null && !_read.IsCompleted)
                {
                    return true;
                }
                AddWarning("External travel interrupted zone " + ZoneKey(_zone.X, _zone.Z) +
                    "; unfinished work remains pending for a later visit.");
                SuspendZone();
                _phase = RestorePhase.Waiting;
            }
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
