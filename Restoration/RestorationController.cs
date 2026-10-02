using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Visits exported sectors and commits resumable native-world restoration one zone at a time.
    internal sealed partial class RestorationController : IDisposable
    {
        private readonly RestoreSelection _options;
        private readonly ManualLogSource _log;
        private RestoreSession _session;
        private RestorePhase _phase;
        private ObjectRestorer _objects;
        private RestoreWarnings _warnings;
        private RestoreFlightNavigator _restoreNavigation;
        private ReceiveMotionGate _motionGate;
        private bool _receiveHold;
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
        private float _nextMessage;
        private float _waitingSince;
        public bool Active => _phase != RestorePhase.Idle && _phase != RestorePhase.Stopped;
        public bool Busy => Active || _closing != null && !_closing.IsCompleted;

        // Shares the selected export and logs while keeping export and import controllers separate.
        public RestorationController(RestoreSelection options, ManualLogSource log)
        {
            _options = options;
            _log = log;
        }

        // Starts explicitly or pauses after the current restoration/save transaction.
        public void Toggle(bool manual = false)
        {
            try
            {
                if (Active)
                {
                    if (_manual != manual)
                    {
                        Say("Pause the active operation with its own shortcut before switching modes.");
                        return;
                    }
                    _pause = true;
                    Say("Pause requested after zone validation and saving.");
                    return;
                }
                if (Busy)
                {
                    Say("Finishing the previous operation.");
                    return;
                }
                if (_closing?.IsFaulted == true)
                {
                    _log.LogError(_closing.Exception);
                }
                _closing = null;
                _manual = manual;
                _session = new RestoreSession(_options, manual);
                _pause = false;
                _finalSave = false;
                ResetRoute();
                _phase = RestorePhase.Preparing;
                Say("Validating the export and prepared local world...");
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Clears only transient route state before a new operation starts.
        private void ResetRoute()
        {
            _zone = null;
            _lastManualZone = null;
            _writer = null;
            _waitingSince = 0;
            _receiveHold = false;
            _manualVisited = 0;
        }

        // Keeps game APIs on the main thread and stops immediately on loss of local authority.
        public void Update()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                _session.Check();
                if (ManualTransit())
                {
                    return;
                }
                if (Time.timeScale <= 0f)
                {
                    return;
                }
                if (!_manual && _phase != RestorePhase.Travelling && _phase != RestorePhase.Landing)
                {
                    _session.Hold(Time.unscaledDeltaTime);
                }
                if (_manual && _session.Flight?.Active == true)
                {
                    _session.Flight.TickManual(_phase != RestorePhase.ManualWaiting || HoldForReception(), Time.unscaledDeltaTime);
                }
                Advance();
                if (Active && Time.unscaledTime >= _nextMessage)
                {
                    _nextMessage = Time.unscaledTime + 5f;
                    ShowProgress();
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Dispatches a single bounded state-machine step.
        private void Advance()
        {
            switch (_phase)
            {
                case RestorePhase.Preparing:
                    Prepare();
                    break;
                case RestorePhase.Preflight:
                    Preflight();
                    break;
                case RestorePhase.InitialSave:
                    InitialSave();
                    break;
                case RestorePhase.Backup:
                    Backup();
                    break;
                default:
                    AdvanceRoute();
                    break;
            }
        }

        // Advances automatic travel or manually visited zones and durable save transactions.
        private void AdvanceRoute()
        {
            switch (_phase)
            {
                case RestorePhase.Travelling:
                    Travel();
                    break;
                case RestorePhase.Reading:
                    ReadZone();
                    break;
                case RestorePhase.Restoring:
                    Restore();
                    break;
                case RestorePhase.Connecting:
                    Connect();
                    break;
                case RestorePhase.RequestSave:
                    RequestSave();
                    break;
                case RestorePhase.Saving:
                    FinishSave();
                    break;
                case RestorePhase.Finalizing:
                    FinalizeObjects();
                    break;
                case RestorePhase.ManualWaiting:
                    WaitForManualZone();
                    break;
                case RestorePhase.Landing:
                    Land();
                    break;
            }
        }

        // Begins all-prefab validation before the first object is changed.
        private void Prepare()
        {
            if (!_session.Prepare())
            {
                return;
            }
            _warnings = new RestoreWarnings(_session.Journal.State.Warnings, message => _log.LogWarning(message));
            if (_session.Archive.Manifest.Zones.Count < _session.Archive.PlannedZoneCount)
            {
                AddWarning($"Partial export: restoring {_session.Archive.Manifest.Zones.Count}/{_session.Archive.PlannedZoneCount} zones; uncaptured zones will be skipped.");
            }
            _objects = new ObjectRestorer(_session.Journal.State.Fingerprint, _session.Journal.State,
                _session.Archive.Cleanup, AddWarning);
            SelectRecords();
            _phase = RestorePhase.Preflight;
        }

        // Validates selected pending records without reopening already completed zones.
        private void Preflight()
        {
            if (!_objects.IndexIdentities())
            {
                return;
            }
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose();
                    _scan = null;
                    _log.LogInfo("Restoration preflight complete: " + _session.Journal.State.Completed.Count
                        + " saved zones retained; " + _session.Journal.State.Objects.Count + " source mappings checked.");
                    QueueSave(true, false);
                    return;
                }
                var record = _scan.Current;
                if (!RestoreRecordPolicy.Include(record))
                {
                    continue;
                }
                ObjectRestorer.Validate(record);
                _objects.Resolve(record);
            }
        }

        // Bounds waits for a native save without confusing silence with success.
        private void CheckSaveTimeout()
        {
            if (Time.unscaledTime - _waitingSince > 300f)
            {
                throw new TimeoutException("Native world save timed out.");
            }
        }

        // Records and logs each distinct review item without changing restoration decisions.
        private void AddWarning(string warning)
        {
            _warnings.Add(warning);
        }

        // Uses the same deterministic sector label throughout the journal.
        private static string ZoneKey(int x, int z)
        {
            return x + ":" + z;
        }

        // Stops and preserves unfinished work instead of claiming a partial zone was committed.
        private void Fail(Exception error)
        {
            _log.LogError(Operation + " stopped in phase=" + _phase +
                (_zone == null ? "" : "; zone=" + ZoneKey(_zone.X, _zone.Z)) + ". " + error);
            _phase = RestorePhase.Stopped;
            try
            {
                _session?.Fault(error.Message);
            }
            catch (Exception save)
            {
                _log.LogError(save);
            }
            finally
            {
                Close();
            }
            Say(Operation + " interrupted: " + error.Message);
        }

        // Releases locks only after outstanding file reads and backups have finished.
        private void Close()
        {
            RestoreProtection.Clear();
            _motionGate?.Dispose();
            _motionGate = null;
            _scan?.Dispose();
            _scan = null;
            var session = _session;
            _session = null;
            if (session == null)
            {
                return;
            }
            try
            {
                session.Flight?.Abort();
            }
            catch (Exception error)
            {
                _log.LogError(error);
            }
            finally
            {
                var workers = Task.WhenAll((Task)_read ?? Task.CompletedTask, (Task)_backup ?? Task.CompletedTask);
                _read = null;
                _backup = null;
                _closing = workers.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        var observed = task.Exception;
                    }
                    session.DisposeResources();
                    return session.ReleaseTask;
                }
, TaskScheduler.Default).Unwrap();
            }
        }

        // Reports concise state transitions in the log and HUD.
        private void Say(string message)
        {
            _log.LogInfo(message);
            HudNotification.Show(message);
        }

        // Restores controlled state when the plugin unloads while retaining resumable progress.
        public void Dispose()
        {
            if (_session != null)
            {
                Fail(new OperationCanceledException("Plugin unloaded."));
            }
        }
    }
}
