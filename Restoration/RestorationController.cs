using System;
using System.Collections.Generic;
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
    // Applies source files only around the manually moved player, with batched native checkpoints.
    internal sealed partial class RestorationController : IDisposable
    {
        private readonly ManualLogSource _log;
        private RestoreSession _session;
        private RestorePhase _phase;
        private ObjectRestorer _objects;
        private RestoreWarnings _warnings;
        private readonly NearZoneScope _scope = new NearZoneScope();
        private readonly HashSet<string> _visited = new HashSet<string>();
        private readonly HashSet<string> _applied = new HashSet<string>();
        private IEnumerator<CapturedObject> _scan;
        private Task<string> _backup;
        private Task<ZoneImportData> _read;
        private Task _closing;
        private ZoneEntry _zone;
        private ZoneRestorer _writer;
        private uint _saveBefore;
        private bool _pause, _initialSave, _finalSave, _dirty, _finalized, _teleportTransit;
        private float _waitingSince, _lastSave, _retrySaveAt;
        private string _lastSaveUtc = "none";
        private int _restoredObjects;
        public bool Active => _phase != RestorePhase.Idle && _phase != RestorePhase.Stopped;
        public bool Busy => Active || _closing != null && !_closing.IsCompleted;
        internal event Action<ZoneSaveReport> ZoneRestored;
        internal string HudStatus => !Active ? null : _pause ? "Finishing restoration..." :
            Preparing ? "Preparing restoration..." : IsSavePhase() ? "Saving restoration..." : "Restoring...";

        // Groups validation and backup phases that run before nearby zones can be restored.
        private bool Preparing => _phase == RestorePhase.Preparing || _phase == RestorePhase.Preflight ||
            _phase == RestorePhase.InitialSave || _phase == RestorePhase.Backup;

        // Returns durable zone progress, pending checkpoints, and objects processed this session.
        internal void GetHudStats(out int saved, out int total, out int pending, out int objects)
        {
            saved = _session?.Journal?.State?.Completed?.Count ?? 0;
            total = _session?.Archive?.PlannedZoneCount ?? 0;
            pending = _applied.Count;
            objects = _restoredObjects;
        }

        // Keeps export selection independent of this manual restore operation.
        public RestorationController(ManualLogSource log)
        {
            _log = log;
        }

        // F10 starts or requests a final checkpoint without choosing any destination.
        public void Toggle()
        {
            try
            {
                if (Active)
                {
                    _pause = true;
                    _retrySaveAt = 0f;
                    Say("Stopping restoration; saving changes. Movement remains unrestricted.");
                    return;
                }
                if (Busy)
                {
                    Say("Finishing the previous operation.");
                    return;
                }
                _closing?.GetAwaiter().GetResult();
                Reset();
                _session = new RestoreSession();
                _phase = RestorePhase.Preparing;
                Say("Validating the export and prepared local world...");
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Clears session-local visits, never durable identities or completed checkpoints.
        private void Reset()
        {
            _closing = null;
            _zone = null;
            _writer = null;
            _pause = _dirty = _finalSave = _finalized = _teleportTransit = false;
            _visited.Clear();
            _applied.Clear();
            _retrySaveAt = 0f;
            _restoredObjects = 0;
            _cleanupIndex = 0;
            _nextCleanup = 0f;
        }

        // Keeps all native APIs on the Unity thread and suspends mutation during native saves.
        public void Update()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                _session.Check();
                if (Time.timeScale <= 0f || ManualTransit())
                {
                    return;
                }
                _scope.Refresh(Player.m_localPlayer.transform.position);
                if (!ZNet.instance.IsSaving() || IsSavePhase())
                {
                    Advance();
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Dispatches bounded data work; there are no route, travel, or landing states.
        private void Advance()
        {
            if (_pause && CanStopAtBoundary())
            {
                SuspendZone();
                QueueSave(false, true);
                return;
            }
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
                case RestorePhase.Reading:
                    ReadZone();
                    break;
                case RestorePhase.Restoring:
                    Restore();
                    break;
                case RestorePhase.Connecting:
                    Connect();
                    break;
                default:
                    AdvanceCheckpoint();
                    break;
            }
        }

        // Advances saving and idle work separately from preparation and zone application.
        private void AdvanceCheckpoint()
        {
            switch (_phase)
            {
                case RestorePhase.RequestSave:
                    RequestSave();
                    break;
                case RestorePhase.Saving:
                    FinishSave();
                    break;
                case RestorePhase.Finalizing:
                    FinalizeObjects();
                    break;
                case RestorePhase.Waiting:
                    WaitForZone();
                    break;
            }
        }

        // Allows cancellation after a file read, never in the middle of an atomic/native save.
        private bool CanStopAtBoundary()
        {
            return _phase == RestorePhase.Waiting || _phase == RestorePhase.Restoring ||
                _phase == RestorePhase.Connecting || _phase == RestorePhase.Finalizing ||
                _phase == RestorePhase.Reading && _read.IsCompleted;
        }

        // Allows the owner of a save to poll completion while mutation remains suspended.
        private bool IsSavePhase()
        {
            return _phase == RestorePhase.RequestSave || _phase == RestorePhase.Saving ||
                _phase == RestorePhase.InitialSave || _phase == RestorePhase.Backup;
        }

        // Builds source-aware identity and cleanup indexes before any world mutation.
        private void Prepare()
        {
            if (!_session.Prepare())
            {
                return;
            }
            _warnings = new RestoreWarnings(_session.Journal.State.Warnings, message => _log.LogWarning(message));
            var prefabs = _session.Archive.Cleanup.PrefabNames;
            Say("Restoration source authority: " + prefabs.Length + " distinct prefabs across " +
                _session.Archive.Manifest.Zones.Count + " exported zones: " + string.Join(", ", prefabs) + ".");
            _objects = new ObjectRestorer(_session.World.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture), _session.Journal.State,
                _session.Archive.Cleanup, AddWarning);
            _scan = _session.Archive.Records.GetEnumerator();
            _phase = RestorePhase.Preflight;
        }

        // Validates all included prefabs before changing a single destination object.
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
                    QueueSave(true, false);
                    return;
                }
                if (RestoreRecordPolicy.Include(_scan.Current))
                {
                    ObjectRestorer.Validate(_scan.Current);
                }
            }
        }

        // Records distinct review warnings without changing the cleanup policy.
        private void AddWarning(string warning)
        {
            if (_warnings == null)
            {
                _log.LogWarning(warning);
            }
            else
            {
                _warnings.Add(warning);
            }
        }

        // Uses one deterministic sector label in files and journals.
        private static string ZoneKey(int x, int z)
        {
            return NearZoneScope.Key(x, z);
        }

        // Keeps dirty local changes pending and protected after a same-session data failure.
        private void Fail(Exception error)
        {
            _log.LogError("Restoration phase=" + _phase + "; zone=" +
                (_zone == null ? "none" : ZoneKey(_zone.X, _zone.Z)) + ": " + error);
            if (_session != null && GameContext.Ready(true) &&
                GameContext.SameSession(_session.World, Player.m_localPlayer) && _dirty && !IsSavePhase())
            {
                AddWarning("Incomplete work remains pending: " + error.Message);
                _pause = true;
                SuspendZone();
                QueueSave(false, true);
                return;
            }
            if (RetryFailedSave(error))
            {
                return;
            }
            var changed = _dirty;
            var notStarted = _session == null || Preparing;
            _phase = RestorePhase.Stopped;
            Close();
            var message = changed ? "Restoration interrupted. Unsaved work may need replay." :
                "Restoration could not continue. No restoration changes are pending.";
            if (notStarted && !changed)
            {
                message = "Restoration not started. No world data was changed.";
            }
            Say(message + " " + error.Message);
            NativeConfirmation.Report(message + "\n\n" + error.Message);
        }

        // Retains the live protected session and pending changes while a failed native save is retried.
        private bool RetryFailedSave(Exception error)
        {
            if (_session != null && _dirty && IsSavePhase() &&
                GameContext.Ready(true) && GameContext.SameSession(_session.World, Player.m_localPlayer))
            {
                AddWarning("Native save not checkpointed; changes remain pending. F10 retries stopping: " + error.Message);
                _initialSave = false;
                _retrySaveAt = Time.unscaledTime + 5f;
                _waitingSince = _retrySaveAt;
                _phase = RestorePhase.RequestSave;
                return true;
            }
            return false;
        }

        // Releases resources only after outstanding disk reads and backups have finished.
        private void Close()
        {
            RestoreProtection.Clear();
            _scan?.Dispose();
            _scan = null;
            var session = _session;
            _session = null;
            if (session == null)
            {
                return;
            }
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
            }, TaskScheduler.Default).Unwrap();
        }

        // Records state transitions in the BepInEx log.
        private void Say(string message)
        {
            _log.LogInfo(message);
        }

        // Cannot promise a native final save during forced plugin shutdown.
        public void Dispose()
        {
            if (_session != null)
            {
                _log.LogWarning("Restoration unloaded. Last confirmed native save: " + _lastSaveUtc +
                    "; unsaved changes may need replay.");
                _phase = RestorePhase.Stopped;
                Close();
            }
        }
    }
}
