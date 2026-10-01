using System;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Coordinates one connected player's export without calling game APIs off-thread.
    internal sealed partial class CrawlController : IDisposable
    {
        private readonly ManualLogSource _log;
        private ExportPreparation _preparation;
        private WorldStore _store;
        private WorldIdentity _world;
        private Player _player;
        private FlightController _flight;
        private TravelNavigator _navigation;
        private ZoneCaptureSession _capture;
        private ZoneEntry _zone;
        private Task _write;
        private Task _closing;
        private CrawlPhase _phase;
        private bool _pauseRequested;
        private float _testUntil;
        private float _nextMessage;
        private float _landingSince;
        private float _nextPinRefresh;
        private Vector3 _testTarget;
        public bool Busy => Active() || _closing != null && !_closing.IsCompleted;

        // Captures logging without starting any world operation.
        public CrawlController(ManualLogSource log)
        {
            _log = log;
        }

        // Starts a crawl or requests a pause at the next committed operation boundary.
        public void Toggle()
        {
            try
            {
                if (_phase == CrawlPhase.Returning || _phase == CrawlPhase.Landing)
                {
                    return;
                }
                if (Active())
                {
                    _pauseRequested = true;
                    Say("Pause requested; saving, then returning to the starting point.");
                    return;
                }
                Start();
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Opens a frozen inventory while keeping game data access on Unity's main thread.
        private void Start()
        {
            if (!GameContext.Ready())
            {
                Say("Join a world and wait for the map to load before pressing F8.");
                return;
            }
            if (_closing != null && !_closing.IsCompleted)
            {
                Say("Finishing the previous save...");
                return;
            }
            if (_closing?.Exception != null)
            {
                _log.LogError(_closing.Exception);
            }
            _closing = null;
            CloseStore();
            _world = GameContext.Identity();
            _player = Player.m_localPlayer;
            _pauseRequested = false;
            _zone = null;
            _capture = null;
            _flight = null;
            _nextPinRefresh = 0f;
            _preparation = new ExportPreparation(_world, HoldRecovery);
            _phase = CrawlPhase.Preparing;
            Say("Building the inventory and checking saved zones...");
            _log.LogInfo($"World: {_world.Name}; UID={_world.Uid}; seed={_world.SeedText}/{_world.Seed}; generation={_world.GenerationVersion}.");
        }

        // Advances a bounded amount of work each Unity frame.
        public void Update()
        {
            if (!Active())
            {
                return;
            }
            try
            {
                if (!GameContext.SameSession(_world, _player))
                {
                    throw new InvalidOperationException("Disconnected or changed world; capture paused.");
                }
                if (_player.IsDead())
                {
                    throw new InvalidOperationException("Character died; capture paused.");
                }
                if (Time.timeScale <= 0f)
                {
                    return;
                }
                if (_phase == CrawlPhase.Preparing && _flight != null && _flight.Active)
                {
                    _flight.Tick(_player.transform.position, Time.unscaledDeltaTime);
                }
                Advance();
                ReportProgress();
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Dispatches one state transition while file operations run independently.
        private void Advance()
        {
            if (_phase == CrawlPhase.Preparing)
            {
                FinishPreparation();
                return;
            }
            if (_phase == CrawlPhase.Writing)
            {
                FinishWrite();
                return;
            }
            if (_pauseRequested && _phase != CrawlPhase.Returning && _phase != CrawlPhase.Landing)
            {
                ReturnHome();
            }
            switch (_phase)
            {
                case CrawlPhase.Testing:
                    TestMovement();
                    break;
                case CrawlPhase.Travelling:
                    Travel();
                    break;
                case CrawlPhase.Capturing:
                    Capture();
                    break;
                case CrawlPhase.Returning:
                    Return();
                    break;
                case CrawlPhase.Landing:
                    Land();
                    break;
            }
        }


        // Returns through the same controlled flight after finishing any disk operation.
        private void ReturnHome()
        {
            _capture?.Dispose();
            _capture = null;
            if (_zone != null && _zone.Status == "loaded")
            {
                _zone.Status = "pending";
            }
            SessionCheckpoint.SetState(_store, "returning", null);
            _phase = CrawlPhase.Returning;
            Say("Progress saved. Returning to the starting point...");
        }

        // Waits for the origin area to load before descending to the saved position.
        private void Return()
        {
            var origin = SessionCheckpoint.Origin(_store);
            if (!_navigation.Travel(origin.x, origin.z, Time.unscaledDeltaTime, true))
            {
                return;
            }
            _landingSince = Time.unscaledTime;
            _phase = CrawlPhase.Landing;
        }

        // Restores normal physics only after returning to a loaded origin area.
        private void Land()
        {
            var origin = SessionCheckpoint.Origin(_store);
            if (!ZNetScene.instance.IsAreaReady(origin))
            {
                _flight.Tick(_player.transform.position, Time.unscaledDeltaTime);
                if (Time.unscaledTime - _landingSince > 120f)
                {
                    throw new TimeoutException("Return area did not load.");
                }
                return;
            }
            if (!_flight.Tick(origin, Time.unscaledDeltaTime))
            {
                return;
            }
            _flight.End();
            SessionCheckpoint.Finish(_store, !Pending());
            _phase = Pending() ? CrawlPhase.Paused : CrawlPhase.Completed;
            Say(_phase == CrawlPhase.Completed ? "Export complete. All selected zones are saved."
                : "Export paused. Press F8 to resume the remaining zones.");
            CloseStore();
        }

        // Checks whether the frozen inventory still contains unfinished observations.
        private bool Pending()
        {
            return _store.Manifest.Zones.Any(z => z.Status != "captured" && z.Status != "skipped");
        }

        // Limits progress notifications to avoid filling the log every frame.
        private void ReportProgress()
        {
            if (Time.unscaledTime < _nextMessage)
            {
                return;
            }
            _nextMessage = Time.unscaledTime + 5f;
            var progress = _store == null ? "preparation" :
                $"{_store.Manifest.Zones.Count(z => z.Status == "captured")}/{_store.Manifest.Zones.Count} zones";
            var zone = _zone == null ? "" : $" | {_zone.X},{_zone.Z}";
            Say($"World Crawler : {_phase} | {progress}{zone}" +
                (_receiveHold ? " | Receiving data; flight paused" : "") +
                (_capture == null ? "" : " | " + _capture.Status), false);
        }

        // Distinguishes active state machines from sessions waiting for an F8 press.
        private bool Active()
        {
            return _phase != CrawlPhase.Idle && _phase != CrawlPhase.Paused &&
                _phase != CrawlPhase.Completed && _phase != CrawlPhase.Faulted;
        }

        // Records failures without accepting incomplete zones as captured.
        private void Fail(Exception error)
        {
            _log.LogError(error);
            _phase = CrawlPhase.Faulted;
            try
            {
                _flight?.Abort();
            }
            catch (Exception release)
            {
                _log.LogError(release);
            }
            CloseStore(error.Message);
            Say("Export interrupted: " + error.Message + " Progress preserved.");
        }

        // Avoids racing a worker's atomic commit when releasing its world lock.
        private void CloseStore(string error = null)
        {
            _capture?.Dispose();
            _capture = null;
            _motionGate?.Dispose();
            _motionGate = null;
            _continuous = null;
            _receiveHold = false;
            var preparation = _preparation;
            preparation?.Dispose();
            _preparation = null;
            if (preparation != null)
            {
                _closing = Task.WhenAll(_closing ?? Task.CompletedTask, preparation.ReleaseTask);
            }
            var store = _store;
            var work = _write;
            _store = null;
            _write = null;
            if (store == null)
            {
                return;
            }
            if (work != null && !work.IsCompleted)
            {
                var release = work.ContinueWith(task => ReleaseStore(store, error, task.Exception), TaskScheduler.Default);
                _closing = Task.WhenAll(_closing ?? Task.CompletedTask, release);
            }
            else
            {
                ReleaseStore(store, error, work?.Exception);
            }
        }

        // Saves an interruption checkpoint after any in-flight commit and releases resources.
        private void ReleaseStore(WorldStore store, string error, Exception writeError)
        {
            try
            {
                if (writeError != null)
                {
                    _log.LogError(writeError);
                }
                if (error != null)
                {
                    SessionCheckpoint.SetState(store, "faulted", error);
                }
            }
            catch (Exception saveError)
            {
                _log.LogError(saveError);
            }
            finally
            {
                store.Dispose();
            }
        }

        // Displays short status messages on the character and records significant transitions.
        private void Say(string message, bool log = true)
        {
            if (log)
            {
                _log.LogInfo(message);
            }
            HudNotification.Show(message);
        }

        // Releases runtime state when the plugin unloads without deleting export progress.
        public void Dispose()
        {
            try
            {
                _flight?.Abort();
            }
            finally
            {
                CloseStore("Plugin unloaded; resume with F8 after reconnecting.");
            }
        }
    }
}
