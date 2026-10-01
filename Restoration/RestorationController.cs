using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Visits exported sectors and commits resumable native-world restoration one zone at a time.
    internal sealed class RestorationController : IDisposable
    {
        private readonly CrawlerSettings _settings;
        private readonly RestorationSettings _options;
        private readonly ManualLogSource _log;
        private RestoreSession _session;
        private RestorePhase _phase;
        private ObjectRestorer _objects;
        private TravelNavigator _navigation;
        private IEnumerator<CapturedObject> _scan;
        private Task<string> _backup;
        private Task<ZoneImportData> _read;
        private Task _closing;
        private ZoneEntry _zone;
        private ZoneRestorer _writer;
        private uint _saveBefore;
        private bool _pause;
        private bool _finalSave;
        private bool _initialSave;
        private int _processed;
        private float _nextMessage;
        private float _waitingSince;
        public bool Active => _phase != RestorePhase.Idle && _phase != RestorePhase.Stopped;
        public bool Busy => Active || _closing != null && !_closing.IsCompleted;

        // Shares configuration and logs while keeping export and import controllers separate.
        public RestorationController(CrawlerSettings settings, RestorationSettings options, ManualLogSource log)
        { _settings = settings; _options = options; _log = log; }

        // Starts explicitly or pauses after the current restoration/save transaction.
        public void Toggle()
        {
            try
            {
                if (Active) { _pause = true; Say("Pause requested after zone validation and saving."); return; }
                if (Busy) { Say("Finishing the previous operation."); return; }
                if (_closing?.IsFaulted == true) { _log.LogError(_closing.Exception); }
                _closing = null;
                _session = new RestoreSession(_settings, _options);
                _pause = false; _finalSave = false; _processed = 0; _zone = null; _writer = null; _waitingSince = 0;
                _phase = RestorePhase.Preparing;
                Say("Validating the export and prepared local world...");
            }
            catch (Exception error) { Fail(error); }
        }

        // Keeps game APIs on the main thread and stops immediately on loss of local authority.
        public void Update()
        {
            if (!Active) { return; }
            try
            {
                _session.Check();
                if (Time.timeScale <= 0f) { return; }
                if (_phase != RestorePhase.Travelling && _phase != RestorePhase.Returning && _phase != RestorePhase.Landing)
                { _session.Hold(Time.unscaledDeltaTime); }
                Advance();
                if (Active && Time.unscaledTime >= _nextMessage)
                {
                    _nextMessage = Time.unscaledTime + 5f;
                    HudNotification.Show($"Restoration: {_phase} | {_session.Journal.State.Completed.Count} validated zones");
                }
            }
            catch (Exception error) { Fail(error); }
        }

        // Dispatches a single bounded state-machine step.
        private void Advance()
        {
            switch (_phase)
            {
                case RestorePhase.Preparing: Prepare(); break;
                case RestorePhase.Preflight: Preflight(); break;
                case RestorePhase.InitialSave: InitialSave(); break;
                case RestorePhase.Backup: Backup(); break;
                case RestorePhase.Travelling: Travel(); break;
                case RestorePhase.Reading: ReadZone(); break;
                case RestorePhase.Restoring: Restore(); break;
                case RestorePhase.Connecting: Connect(); break;
                case RestorePhase.RequestSave: RequestSave(); break;
                case RestorePhase.Saving: FinishSave(); break;
                case RestorePhase.Finalizing: FinalizeObjects(); break;
                case RestorePhase.Returning: Return(); break;
                case RestorePhase.Landing: Land(); break;
            }
        }

        // Begins all-prefab validation before the first object is changed.
        private void Prepare()
        {
            if (!_session.Prepare()) { return; }
            _objects = new ObjectRestorer(_session.Journal.State.Fingerprint, _session.Journal.State);
            _scan = _session.Archive.Records.GetEnumerator();
            _phase = RestorePhase.Preflight;
        }

        // Rejects missing assets and repairs stale progress after a rolled-back native world save.
        private void Preflight()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose(); _scan = null;
                    QueueSave(true, false); return;
                }
                var record = _scan.Current;
                ObjectRestorer.Validate(record);
                if (_objects.Resolve(record) == null)
                { _session.Journal.State.Completed.Remove(ZoneKey(record.ZoneX, record.ZoneZ)); }
            }
        }

        // Backs up only a completed native-format generation, never an active write.
        private void InitialSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory)) { return; }
            var state = _session.Journal.State;
            if (!string.IsNullOrEmpty(state.BackupDirectory))
            {
                if (!Directory.Exists(state.BackupDirectory)) { throw new IOException("The original restoration backup is missing."); }
                BeginRoute(); return;
            }
            var source = _session.WorldDirectory; var journal = _session.Journal.DirectoryPath;
            _saveBefore = LatestWorldApi.SaveNumber();
            _backup = Task.Run(() => WorldBackup.Create(source, journal));
            _phase = RestorePhase.Backup;
        }

        // Publishes the verified backup before allowing the first import mutation.
        private void Backup()
        {
            if (!_backup.IsCompleted) { return; }
            var backup = _backup; _backup = null;
            var path = backup.GetAwaiter().GetResult();
            if (ZNet.instance.IsSaving() || LatestWorldApi.SaveNumber() != _saveBefore)
            { throw new IOException("The native world changed during backup. No restoration was started; retry."); }
            _session.Journal.State.BackupDirectory = path;
            _session.Journal.Save();
            _log.LogInfo("Verified pre-restoration backup: " + path);
            BeginRoute();
        }

        // Reuses controlled flight after storing the original return position.
        private void BeginRoute()
        {
            _session.StartFlight(_settings.Speed.Value);
            _navigation = new TravelNavigator(_session.Flight, Player.m_localPlayer, _settings.Clearance.Value,
                _settings.PreferPortals.Value, _settings.CruiseSpeed.Value, _settings.CruiseThreshold.Value,
                _settings.AllowCoordinateJumps.Value, _settings.CoordinateJumpThreshold.Value);
            RestoreProtection.Active = true;
            AddWarning("Generated objects absent from client observations are not deleted automatically.");
            AddWarning("Non-network zone-root scenery and changed location assets require separate review.");
            NextZone();
        }

        // Chooses the closest unfinished captured zone without consulting the test character's map.
        private void NextZone()
        {
            if (_session.Journal.State.Completed.Count == _session.Archive.Manifest.Zones.Count)
            {
                if (_session.Archive.Manifest.Zones.Count < _session.Archive.PlannedZoneCount)
                {
                    AddWarning("Available captures restored. Building support protection remains active until the remaining source zones are exported and restored.");
                    ReturnHome(); return;
                }
                _scan = _session.Archive.Records.GetEnumerator(); _phase = RestorePhase.Finalizing; return;
            }
            if (_pause || _options.MaximumZones.Value > 0 && _processed >= _options.MaximumZones.Value)
            { ReturnHome(); return; }
            var position = Player.m_localPlayer.transform.position;
            _zone = _session.Archive.Manifest.Zones.Where(v => !_session.Journal.State.Completed.Contains(ZoneKey(v.X, v.Z)))
                .OrderBy(v => Math.Pow(v.X * 64.0 - position.x, 2) + Math.Pow(v.Z * 64.0 - position.z, 2))
                .ThenBy(v => v.Z).ThenBy(v => v.X).FirstOrDefault();
            if (_zone == null) { ReturnHome(); return; }
            _phase = RestorePhase.Travelling;
        }

        // Loads the target sector through ordinary controlled movement before importing it.
        private void Travel()
        {
            if (_pause) { ReturnHome(); return; }
            if (!_navigation.Travel(_zone.X * 64f, _zone.Z * 64f, Time.unscaledDeltaTime)) { return; }
            var center = new Vector3(_zone.X * 64f, Player.m_localPlayer.transform.position.y, _zone.Z * 64f);
            if (!ZNetScene.instance.IsAreaReady(center))
            {
                if (_waitingSince == 0f) { _waitingSince = Time.unscaledTime; }
                if (Time.unscaledTime - _waitingSince > 180f) { throw new TimeoutException("Target zone did not load."); }
                return;
            }
            _waitingSince = 0;
            var archive = _session.Archive; var zone = _zone;
            _read = Task.Run(() => new ZoneImportData { Snapshot = archive.ReadZone(zone), Records = archive.ZoneObjects(zone.X, zone.Z) });
            _phase = RestorePhase.Reading;
        }

        // Hands validated files to a bounded main-thread restorer.
        private void ReadZone()
        {
            if (!_read.IsCompleted) { return; }
            var read = _read; _read = null;
            var data = read.GetAwaiter().GetResult();
            if (_pause) { ReturnHome(); return; }
            _writer = new ZoneRestorer(_objects, data.Records, data.Snapshot, _zone, _session.Journal.State);
            _phase = RestorePhase.Restoring;
        }

        // Finishes the current zone before honoring a pause request.
        private void Restore()
        {
            _writer.Step(Mathf.Clamp(_settings.ObjectsPerFrame.Value, 5, 200));
            if (!_writer.Done) { return; }
            _scan = ((IEnumerable<CapturedObject>)_session.Archive.Connections).GetEnumerator();
            _phase = RestorePhase.Connecting;
        }

        // Repairs cross-zone relationships incrementally as more destination objects become available.
        private void Connect()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose(); _scan = null;
                    QueueSave(false, false); return;
                }
                if (!_objects.Connect(_scan.Current) && _session.Journal.State.Completed.Count + 1 >= _session.Archive.Manifest.Zones.Count)
                { AddWarning("Unresolved source connection: " + ExportArchive.Key(_scan.Current)); }
            }
        }

        // Marks a zone complete only after the engine confirms its native files were committed.
        private void FinishSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory)) { return; }
            if (_finalSave) { _finalSave = false; ReturnHome(); return; }
            var key = ZoneKey(_zone.X, _zone.Z);
            if (!_session.Journal.State.Completed.Contains(key)) { _session.Journal.State.Completed.Add(key); }
            _session.Journal.Save(); _processed++; _writer = null; _waitingSince = 0f;
            _log.LogInfo("Restored and native-saved zone " + key);
            NextZone();
        }

        // Releases persistent support protection only after every exported zone was restored.
        private void FinalizeObjects()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose(); _scan = null;
                    QueueSave(false, true); return;
                }
                var target = _objects.Resolve(_scan.Current);
                if (target == null) { throw new InvalidOperationException("An imported object disappeared before finalization."); }
                target.Set("WorldCrawler.pending", false);
                ObjectRestorer.Refresh(target);
            }
        }

        // Queues an owned save without mistaking a concurrent autosave for our commit.
        private void QueueSave(bool initial, bool final)
        {
            _initialSave = initial; _finalSave = final; _waitingSince = Time.unscaledTime;
            _phase = RestorePhase.RequestSave;
        }

        // Waits for any existing save before requesting the generation used by our checkpoint.
        private void RequestSave()
        {
            CheckSaveTimeout();
            if (ZNet.instance.IsSaving()) { return; }
            _saveBefore = LatestWorldApi.BeginSave(); _waitingSince = Time.unscaledTime;
            _phase = _initialSave ? RestorePhase.InitialSave : RestorePhase.Saving;
        }

        // Starts an ordinary controlled return without erasing interruption recovery.
        private void ReturnHome()
        {
            _session.Journal.State.Status = "returning"; _session.Journal.Save();
            _phase = RestorePhase.Returning;
            Say("Restoration saved. Returning to the starting point...");
        }

        // Waits for the saved origin to be loaded before descending.
        private void Return()
        {
            var origin = _session.Flight.Origin;
            if (!_navigation.Travel(origin.x, origin.z, Time.unscaledDeltaTime, true)) { return; }
            _waitingSince = Time.unscaledTime; _phase = RestorePhase.Landing;
        }

        // Releases physics and marks pause or completion only after reaching the origin.
        private void Land()
        {
            var flight = _session.Flight;
            if (Time.unscaledTime - _waitingSince > 180f) { throw new TimeoutException("Return area did not stabilize."); }
            if (!ZNetScene.instance.IsAreaReady(flight.Origin))
            {
                _session.Hold(Time.unscaledDeltaTime);
                return;
            }
            var heightmap = Heightmap.FindHeightmap(flight.Origin);
            if (heightmap == null || heightmap.HaveQueuedRebuild()) { _session.Hold(Time.unscaledDeltaTime); return; }
            if (ZoneSystem.instance.GetSolidHeight(flight.Origin, out var height, 1000) && height > flight.Origin.y + 0.5f &&
                flight.RaiseReturnHeight(height + 0.5f))
            {
                _session.Journal.State.ReturnPosition = CaptureTransform.Vector(flight.Origin);
                _session.Journal.Save(); Say("Restored terrain has changed: returning above the surface.");
            }
            if (!flight.Tick(flight.Origin, Time.unscaledDeltaTime)) { return; }
            flight.End();
            var state = _session.Journal.State; state.ReturnPending = false;
            state.Status = state.Completed.Count == _session.Archive.Manifest.Zones.Count ? "completed-with-review" : "paused";
            if (state.Status == "completed-with-review" && _session.Archive.Manifest.Zones.Count < _session.Archive.PlannedZoneCount)
            { state.Status = "awaiting-export"; }
            _session.Journal.Save();
            Say("Restoration " + state.Status + ". Report and backup preserved. Check the world after reloading.");
            Close(); _phase = RestorePhase.Stopped;
        }

        // Bounds waits for a native save without confusing silence with success.
        private void CheckSaveTimeout()
        { if (Time.unscaledTime - _waitingSince > 300f) { throw new TimeoutException("Native world save timed out."); } }

        // Records a review item once in the import report.
        private void AddWarning(string warning)
        { if (!_session.Journal.State.Warnings.Contains(warning)) { _session.Journal.State.Warnings.Add(warning); } }

        // Uses the same deterministic sector label throughout the journal.
        private static string ZoneKey(int x, int z) { return x + ":" + z; }

        // Stops and preserves unfinished work instead of claiming a partial zone was committed.
        private void Fail(Exception error)
        {
            _log.LogError(error); _phase = RestorePhase.Stopped;
            try { _session?.Fault(error.Message); }
            catch (Exception save) { _log.LogError(save); }
            finally { Close(); }
            Say("Restoration interrupted: " + error.Message);
        }

        // Releases locks only after outstanding file reads and backups have finished.
        private void Close()
        {
            RestoreProtection.Clear(); _scan?.Dispose(); _scan = null;
            var session = _session; _session = null;
            if (session == null) { return; }
            try { session.Flight?.Abort(); }
            catch (Exception error) { _log.LogError(error); }
            finally
            {
                var workers = Task.WhenAll((Task)_read ?? Task.CompletedTask, (Task)_backup ?? Task.CompletedTask);
                _read = null; _backup = null;
                _closing = workers.ContinueWith(task =>
                {
                    if (task.IsFaulted) { var observed = task.Exception; }
                    session.DisposeResources(); return session.ReleaseTask;
                }, TaskScheduler.Default).Unwrap();
            }
        }

        // Reports concise state transitions in the log and HUD.
        private void Say(string message) { _log.LogInfo(message); HudNotification.Show(message); }

        // Restores controlled state when the plugin unloads while retaining resumable progress.
        public void Dispose() { if (_session != null) { Fail(new OperationCanceledException("Plugin unloaded.")); } }
    }
}
