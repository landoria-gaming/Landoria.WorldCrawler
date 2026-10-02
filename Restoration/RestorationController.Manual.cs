using System;
using System.Linq;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Follows manually visited sectors while sharing automatic import's writer and checkpoints.
    internal sealed partial class RestorationController
    {
        private bool _manual;
        private string _lastManualZone;
        private int _manualVisited;
        private readonly CaptureApi _sectors = new CaptureApi();
        private string Operation => _manual ? "Manual import" : "Restoration";

        // Exposes the same durable completion set for automatic and manual restoration overlays.
        internal ExportMapOverlayData MapProgress()
        {
            return _session?.Archive == null ? null :
                ProgressMapSnapshot.Restore(_session.Archive.Manifest.Zones, _session.Journal.State.Completed);
        }

        // Automatic preflight checks pending zones; manual mode validates each visited zone on demand.
        private void SelectRecords()
        {
            var completed = _session.Journal.State.Completed;
            _scan = (_manual ? Enumerable.Empty<CapturedObject>() :
                _session.Archive.Records.Where(v => !completed.Contains(ZoneKey(v.ZoneX, v.ZoneZ)))).GetEnumerator();
        }

        // Starts a continuous session without selecting an automatic route or old return point.
        private void BeginManualMode()
        {
            RestoreProtection.Active = true;
            _session.StartFlight(CrawlerConstants.Speed);
            _motionGate = new Landoria.WorldCrawler.Flight.ReceiveMotionGate(Player.m_localPlayer, CrawlerConstants.ZoneTimeout);
            _session.Journal.State.Status = "manual";
            _session.Journal.Save();
            _phase = RestorePhase.ManualWaiting;
            Say("Manual import active. Move to exported zones; LeftCtrl+F10 stops. Completed zones can be imported again.");
        }

        // Starts a visited sector once per arrival, including previously completed sectors.
        private void WaitForManualZone()
        {
            if (_pause)
            {
                StopHere();
                return;
            }
            _sectors.GetZone(Player.m_localPlayer.transform.position, out var x, out var z);
            var key = ZoneKey(x, z);
            if (_lastManualZone == key)
            {
                return;
            }
            _lastManualZone = key;
            _zone = _session.Archive.Manifest.Zones.SingleOrDefault(v => v.X == x && v.Z == z);
            if (_zone == null)
            {
                Say("Zone " + key + " has no export; waiting for another zone. Nothing was changed.");
                return;
            }
            _session.Journal.State.Completed.Remove(key);
            _session.Journal.Save();
            var archive = _session.Archive;
            var zone = _zone;
            _waitingSince = Time.unscaledTime;
            _read = Task.Run(() => new ZoneImportData { Snapshot = archive.ReadZone(zone),
                Records = archive.ZoneObjects(zone.X, zone.Z) });
            _phase = RestorePhase.Reading;
            Say("Importing visited zone " + key + "...");
        }

        // Pauses mutations during manual teleport and abandons an unfinished zone after departure.
        private bool ManualTransit()
        {
            if (!_manual)
            {
                return false;
            }
            var player = Player.m_localPlayer;
            if (_session.Flight?.WaitForManualTeleport() == true || player.IsTeleporting())
            {
                _waitingSince = Time.unscaledTime;
                return true;
            }
            if (_zone == null || _phase != RestorePhase.Reading && _phase != RestorePhase.Restoring)
            {
                return false;
            }
            _sectors.GetZone(player.transform.position, out var x, out var z);
            if (x == _zone.X && z == _zone.Z)
            {
                return false;
            }
            if (_read != null && !_read.IsCompleted)
            {
                return true;
            }
            _read?.GetAwaiter().GetResult();
            _read = null;
            AddWarning("Left zone " + ZoneKey(_zone.X, _zone.Z) + " before import completed; it remains pending.");
            _session.Journal.Save();
            _writer = null;
            _zone = null;
            _lastManualZone = null;
            _phase = RestorePhase.ManualWaiting;
            return false;
        }

        // Waits for loaded local terrain before starting a manually visited zone.
        private bool ManualZoneReady()
        {
            if (!_manual)
            {
                return true;
            }
            if (Time.unscaledTime - _waitingSince > 180f)
            {
                throw new TimeoutException("Visited-zone loading timed out.");
            }
            var center = new Vector3(_zone.X * 64f, Player.m_localPlayer.transform.position.y, _zone.Z * 64f);
            var terrain = Heightmap.FindHeightmap(center);
            return ZNetScene.instance.IsAreaReady(center) && terrain != null && !terrain.IsDistantLod &&
                !terrain.HaveQueuedRebuild();
        }

        // Returns to manual observation after the same journal used by F10 commits this zone.
        private void FinishManualZone()
        {
            _manualVisited++;
            Say("Zone " + ZoneKey(_zone.X, _zone.Z) + " imported and saved. Automatic F10 will skip it.");
            _zone = null;
            if (_session.Journal.State.Completed.Count == _session.Archive.PlannedZoneCount)
            {
                _scan = _session.Archive.Records.GetEnumerator();
                _phase = RestorePhase.Finalizing;
                return;
            }
            _phase = RestorePhase.ManualWaiting;
        }

        // Shows shared progress and the manual session's completed visit count.
        private void ShowProgress()
        {
            if (_session.Archive == null)
            {
                HudNotification.Show(Operation + ": validating export...");
                return;
            }
            var total = _session.Archive.Manifest.Zones.Count;
            var count = _session.Journal.State.Completed.Count;
            HudNotification.Show($"{Operation}: {_phase} | {count}/{total} zones ({(total == 0 ? 100 : 100 * count / total)}%)" +
                (_manual ? $" | {_manualVisited} visits saved" : ""));
        }
    }
}
