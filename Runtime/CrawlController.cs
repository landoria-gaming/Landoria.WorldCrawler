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
    // Records complete locally validated sectors while the player alone chooses where to travel.
    internal sealed partial class CrawlController : IDisposable
    {
        private readonly ManualLogSource _log;
        private ExportPreparation _preparation;
        private WorldStore _store;
        private WorldIdentity _world;
        private Player _player;
        private FlightController _flight;
        private RecordingObserver _observer;
        private ReceiveMotionGate _motionGate;
        private ZoneCaptureSession _capture;
        private ZoneEntry _zone;
        private Task _write;
        private Task _closing;
        private CrawlPhase _phase;
        private long _writeRevision;
        private float _nextMessage;
        private bool _stop;
        private string _stall;
        private float _retryAt;
        private ExportMapOverlayData _map;
        public bool Busy => Active || _closing != null && !_closing.IsCompleted;
        private bool Active => _phase != CrawlPhase.Idle && _phase != CrawlPhase.Stopped;

        // Captures logging without starting any world operation.
        public CrawlController(ManualLogSource log)
        {
            _log = log;
        }

        // Starts recording or cancels unfinished observations after any validated disk write.
        public void Toggle()
        {
            try
            {
                if (Active)
                {
                    _stop = true;
                    Say("Stopping recording; unfinished observations will not be exported.");
                }
                else if (!Busy)
                {
                    Start();
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Acquires protected manual flight at the current position before loading the recording store.
        private void Start()
        {
            if (!GameContext.Ready())
            {
                Say("Join a world and wait for your character before pressing F8.");
                return;
            }
            _closing?.GetAwaiter().GetResult();
            _closing = null;
            _world = GameContext.Identity();
            _player = Player.m_localPlayer;
            _stop = false;
            _map = null;
            _stall = null;
            _zone = null;
            _flight = new FlightController();
            _flight.Begin(_player, _player.transform.position, _player.transform.rotation);
            _motionGate = new ReceiveMotionGate(_player, CrawlerConstants.ZoneTimeout);
            _observer = new RecordingObserver();
            _preparation = new ExportPreparation(_world);
            _phase = CrawlPhase.Preparing;
            Say("Opening recording. F8 stops; you control all movement.");
        }

        // Performs bounded main-thread work before allowing the next manual movement step.
        public void Update()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                CheckSession();
                if (_stop && _phase != CrawlPhase.Writing)
                {
                    Stop();
                    return;
                }
                if (Time.timeScale <= 0f || Transit())
                {
                    return;
                }
                _observer.Step(_player.transform.position);
                Advance();
                if (Active)
                {
                    _flight.TickManual(_phase != CrawlPhase.Waiting || _observer.Pending > 0 ||
                        _motionGate.Hold(), Time.unscaledDeltaTime);
                    Report();
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Rejects disconnection without serializing the incomplete in-memory sector.
        private void CheckSession()
        {
            if (!GameContext.SameSession(_world, _player) || _player.IsDead())
            {
                throw new InvalidOperationException("Recording disconnected or lost its living character.");
            }
        }

        // Publishes immutable geometry only after a worker has finished its manifest commit.
        internal ExportMapOverlayData MapProgress()
        {
            return Active ? _map ?? new ExportMapOverlayData() : null;
        }

        // Displays actual committed progress, with no invented planned total or percentage.
        private void Report()
        {
            if (Time.unscaledTime < _nextMessage)
            {
                return;
            }
            _nextMessage = Time.unscaledTime + 5f;
            var count = _phase == CrawlPhase.Writing ? "committing" :
                (_store?.Manifest.Zones.Count(zone => zone.Status == "captured") ?? 0).ToString();
            var work = _stall ?? _capture?.Status ?? (_observer.Pending > 0 ?
                "Validating loaded zones; movement locked" : _motionGate.Hold() ? "Waiting for 2 quiet seconds" : "Move to record");
            HudNotification.Show("Recording | " + count + " zones saved | " +
                _observer.Pending + " pending | " + work);
        }

        // Logs visible state changes without flooding the log with per-frame progress.
        private void Say(string message)
        {
            _log.LogInfo(message);
            HudNotification.Show(message);
        }

        // Retains validated files and releases control in place when a session fails.
        private void Fail(Exception error)
        {
            _log.LogError("Recording interrupted: " + error);
            Say("Recording interrupted. Saved files preserved; unfinished observations discarded.");
            Close(error.Message);
        }

        // Stops in place after the current atomic write, never saving incomplete observations.
        private void Stop()
        {
            Say("Recording stopped. " + (_observer?.Pending ?? 0) + " unfinished observations were not exported.");
            Close(null);
        }

        // Observes outstanding file work before releasing its exclusive store lock.
        private void Close(string error)
        {
            _phase = CrawlPhase.Stopped;
            _capture?.Dispose();
            _capture = null;
            _observer?.Dispose();
            _observer = null;
            _motionGate?.Dispose();
            _motionGate = null;
            _flight?.Abort();
            _flight = null;
            _preparation?.Dispose();
            var preparation = _preparation?.ReleaseTask ?? Task.CompletedTask;
            _preparation = null;
            var store = _store;
            var write = _write ?? Task.CompletedTask;
            _store = null;
            _write = null;
            _closing = Task.WhenAll(preparation, write).ContinueWith(task => Release(store, error, task.Exception));
        }

        // Checkpoints a stopped recording only after any earlier worker has finished.
        private void Release(WorldStore store, string error, Exception writeError)
        {
            try
            {
                if (writeError != null)
                {
                    _log.LogError(writeError);
                }
                if (store != null)
                {
                    store.Manifest.CrawlState = error == null ? "stopped" : "interrupted";
                    store.Manifest.LastError = error ?? writeError?.Message;
                    store.Save();
                }
            }
            finally
            {
                store?.Dispose();
            }
        }

        // Releases hooks and locks without silently flushing partial captures on shutdown.
        public void Dispose()
        {
            Close("Plugin unloaded before recording stopped.");
        }
    }
}
