using System;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Validates and commits one received sector at a time, without directing player movement.
    internal sealed partial class CrawlController
    {
        // Handles explicit teleports and invalidates observations if an external move leaves near coverage.
        private bool Transit()
        {
            var transit = _flight?.WaitForManualTeleport() == true || _player.IsTeleporting();
            _observer.Scope.Refresh(_player.transform.position);
            if (_capture != null && (transit || !_observer.Scope.Contains(_zone.X, _zone.Z)))
            {
                _capture.Dispose();
                _capture = null;
                _zone = null;
                _stall = null;
                _phase = CrawlPhase.Waiting;
                Say("External travel interrupted validation; the previous saved file is unchanged.");
            }
            return transit && _phase != CrawlPhase.Writing;
        }

        // Dispatches only preparation, passive observation, validation and disk commits.
        private void Advance()
        {
            switch (_phase)
            {
                case CrawlPhase.Preparing:
                    Prepare();
                    break;
                case CrawlPhase.Waiting:
                    BeginCapture();
                    break;
                case CrawlPhase.Capturing:
                    Capture();
                    break;
                case CrawlPhase.Writing:
                    FinishWrite();
                    break;
            }
        }

        // Takes exclusive store ownership after validation, leaving old export folders untouched.
        private void Prepare()
        {
            if (!_preparation.TryTake(out var store))
            {
                return;
            }
            _store = store;
            _preparation.Dispose();
            _preparation = null;
            _map = ProgressMapSnapshot.Export(store.Manifest, null);
            _phase = CrawlPhase.Waiting;
            if (!string.IsNullOrEmpty(store.RecoveryNotice))
            {
                _log.LogWarning(store.RecoveryNotice);
            }
            Say("Recording active. Movement is held only while useful data is being captured.");
        }

        // Begins dirty near-zone work, including terrain still loading on arrival.
        private void BeginCapture()
        {
            if (Time.unscaledTime < _retryAt)
            {
                return;
            }
            _zone = _observer.Next(_player.transform.position);
            if (_zone == null)
            {
                return;
            }
            _flight.LockMovement();
            _capture = new ZoneCaptureSession(_zone.X, _zone.Z);
            _phase = CrawlPhase.Capturing;
        }

        // Never converts a timeout or incomplete observation into a saved sector.
        private void Capture()
        {
            try
            {
                if (_observer.HasQueuedObjects || !_capture.Step())
                {
                    return;
                }
                var result = _capture.Result;
                result.Deletions = _observer.Deletions(_zone);
                result.Departures = _observer.Departures(_zone);
                _writeRevision = _observer.Revision(_zone);
                _capture.Dispose();
                _capture = null;
                var x = _zone.X;
                var z = _zone.Z;
                var version = GameContext.GameVersion;
                var store = _store;
                _write = Task.Run(() => store.WriteZone(x, z, result.Encode(), version, result.Objects.Count));
                _phase = CrawlPhase.Writing;
            }
            catch (TimeoutException error)
            {
                Stall(error.Message);
            }
        }

        // Keeps protection and the movement lock while offering F8 cancellation of stalled work.
        private void Stall(string reason)
        {
            _stall = reason + " F8 stops without exporting this zone.";
            _log.LogWarning(_stall);
            _capture.Dispose();
            _capture = null;
            _retryAt = Time.unscaledTime + 2f;
            _phase = CrawlPhase.Waiting;
        }

        // Accepts exactly the written revision; changes received during the write stay pending.
        private void FinishWrite()
        {
            if (!_write.IsCompleted)
            {
                return;
            }
            var write = _write;
            _write = null;
            write.GetAwaiter().GetResult();
            _observer.Committed(_zone, _writeRevision);
            _map = ProgressMapSnapshot.Export(_store.Manifest, null);
            _log.LogInfo("Recorded zone " + _zone.X + ":" + _zone.Z + ".");
            _zone = null;
            _stall = null;
            _phase = CrawlPhase.Waiting;
            if (_stop)
            {
                Stop();
            }
        }
    }
}
