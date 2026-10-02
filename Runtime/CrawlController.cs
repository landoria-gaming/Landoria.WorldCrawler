using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Passively records received data; the native game owns movement, physics and teleports.
    internal sealed partial class CrawlController : IDisposable
    {
        private readonly ManualLogSource _log;
        private ExportPreparation _preparation;
        private WorldStore _store;
        private WorldIdentity _world;
        private Player _player;
        private RecordingObserver _observer;
        private Task _write;
        private List<ZoneSnapshot> _batch;
        private CrawlPhase _phase;
        private float _nextFlush, _nextMessage, _nextDiagnostic;
        private bool _stop;
        private string _version;
        private float _retryAt;
        private ExportMapOverlayData _map;
        private List<ZoneEntry> _committed = new List<ZoneEntry>();
        private long _reportedErrors;
        private readonly HashSet<string> _savedObjectIds = new HashSet<string>();
        private readonly HashSet<string> _savedZones = new HashSet<string>();
        public bool Busy => Active;
        private bool Active => _phase != CrawlPhase.Idle && _phase != CrawlPhase.Stopped;

        // Keeps logging separate from the lifetime of each connected source world.
        public CrawlController(ManualLogSource log)
        {
            _log = log;
        }

        // F8 freezes new receipts and drains the cache instead of discarding unfinished sectors.
        public void Toggle()
        {
            try
            {
                if (Active)
                {
                    RequestStop();
                }
                else
                {
                    Start();
                }
            }
            catch (Exception error)
            {
                _log.LogError("Recording control failed: " + error);
            }
        }

        // Attaches reception before asynchronous store preparation and leaves the character untouched.
        private void Start()
        {
            if (!GameContext.Ready())
            {
                Say("Join a world before pressing F8.");
                return;
            }
            _world = GameContext.Identity();
            _version = GameContext.GameVersion;
            _player = Player.m_localPlayer;
            _stop = false;
            _retryAt = 0f;
            _batch = null;
            _map = new ExportMapOverlayData();
            _committed.Clear();
            _reportedErrors = 0;
            _savedObjectIds.Clear();
            _savedZones.Clear();
            _nextFlush = Time.realtimeSinceStartup + CrawlerConstants.RecordingFlushInterval;
            _nextDiagnostic = _nextMessage = 0f;
            _observer = new RecordingObserver(_world.Uid);
            PrepareStore();
            _phase = CrawlPhase.Preparing;
            Say("Recording received data. Disk flush every 10 seconds; movement is unrestricted.");
        }

        // Detaches the observer if setup fails before it can own a recoverable disk store.
        private void PrepareStore()
        {
            try
            {
                _preparation = new ExportPreparation(_world);
            }
            catch
            {
                _observer.Dispose();
                _observer = null;
                throw;
            }
        }

        // Drains completed workers even during a teleport or paused gameplay.
        public void Update()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                if (!_stop && (!GameContext.SameSession(_world, _player) || _player == null || _player.IsDead()))
                {
                    RequestStop();
                }
                if (Time.realtimeSinceStartup < _retryAt)
                {
                    Report();
                    return;
                }
                if (!_stop)
                {
                    _observer.Step(_player.transform.position);
                }
                Advance();
                if (Active)
                {
                    Report();
                }
            }
            catch (Exception error)
            {
                _log.LogError("Recording failed; cached data is retained for retry: " + error);
                _nextFlush = _retryAt = Time.realtimeSinceStartup + 5f;
            }
        }

        // Stops new observations but retains all detached values through the final disk commit.
        private void RequestStop()
        {
            if (!_stop)
            {
                _observer.Freeze();
                _stop = true;
                Say("Stopping recording; flushing all cached data. Movement remains unrestricted.");
            }
            _nextFlush = 0f;
            _retryAt = 0f;
        }

        // Returns only durable rectangles; dirty cached sectors are shown separately.
        internal ExportMapOverlayData MapProgress()
        {
            return Active ? _map : null;
        }

        // Shows real cache size and periodic diagnostics rather than a misleading pending-zone lock.
        private void Report()
        {
            if (Time.realtimeSinceStartup < _nextMessage)
            {
                return;
            }
            _nextMessage = Time.realtimeSinceStartup + 5f;
            RefreshMap();
            var cached = CachedObjectKeys();
            HudNotification.Show("Recording | " + _savedZones.Count + " zones saved | " +
                _savedObjectIds.Count(id => !cached.Contains(id)) + " objects saved | " + cached.Count + " objects cached");
            LogRecordingProgress();
        }

        // Finishes in place without modifying native player controls or physics.
        private void Finish()
        {
            var errors = _observer.Errors;
            _store.Manifest.CrawlState = errors == 0 ? "stopped" : "stopped-with-errors";
            _store.Manifest.LastError = errors == 0 ? null : _observer.LastError;
            _store.Save();
            _observer.Dispose();
            _store.Dispose();
            _observer = null;
            _store = null;
            _phase = CrawlPhase.Stopped;
            Say("Recording stopped; all cached data saved." + (errors == 0 ? "" :
                " WARNING: " + errors + " object capture errors; check the log."));
        }

        // Keeps visible messages short while retaining full errors in the BepInEx log.
        private void Say(string message)
        {
            _log.LogInfo(message);
            HudNotification.Show(message);
        }

        // Joins file workers and flushes the last cache on orderly plugin shutdown.
        public void Dispose()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                RequestStop();
                _store = _store ?? _preparation.TakeForShutdown();
                JoinWrite();
                if (_batch != null)
                {
                    RecordingFlush.Write(_store, _batch, _version);
                }
                var last = _observer.TakeBatch();
                if (last.Count != 0)
                {
                    RecordingFlush.Write(_store, last, _version);
                }
                Finish();
            }
            catch (Exception error)
            {
                _log.LogError("Final recording flush failed; pending checkpoint retained when available: " + error);
            }
            finally
            {
                _observer?.Dispose();
                _store?.Dispose();
                _preparation?.Dispose();
            }
        }

        // Retains a failed detached batch for one final retry during orderly shutdown.
        private void JoinWrite()
        {
            if (_write == null)
            {
                return;
            }
            try
            {
                _write.GetAwaiter().GetResult();
                _batch = null;
            }
            catch (Exception error)
            {
                _log.LogWarning("Retrying interrupted flush: " + error.Message);
            }
        }
    }
}
