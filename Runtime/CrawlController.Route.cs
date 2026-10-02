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
    // Groups route behavior without changing the controller's shared state.
    internal sealed partial class CrawlController
    {
        // Performs a short nearby move and waits before beginning the long route.
        private void TestMovement()
        {
            if (HoldForReception())
            {
                _flight.Tick(_player.transform.position, Time.unscaledDeltaTime);
                return;
            }
            if (!_flight.Tick(_testTarget, Time.unscaledDeltaTime))
            {
                return;
            }
            if (_testUntil == 0f)
            {
                _testUntil = Time.unscaledTime + 2f;
            }
            if (Time.unscaledTime >= _testUntil)
            {
                NextZone();
            }
        }

        // Selects the closest unfinished target without expanding the original inventory.
        private void NextZone()
        {
            if (_pauseRequested)
            {
                StopHere();
                return;
            }
            if (_manual)
            {
                _zone = null;
                _phase = CrawlPhase.ManualWaiting;
                return;
            }
            RefreshPersonalPins();
            var position = _player.transform.position;
            _zone = _store.Manifest.Zones.Where(z => z.Status != "captured" && z.Status != "skipped")
                .OrderBy(z => DistanceSquared(z, position)).ThenBy(z => z.Z).ThenBy(z => z.X).FirstOrDefault();
            if (_zone == null)
            {
                StopHere();
                return;
            }
            _phase = CrawlPhase.Travelling;
        }

        // Incorporates marker additions/removals only between zones, never during capture or an atomic write.
        private void RefreshPersonalPins()
        {
            if (_store.Manifest.Selection == null || Time.unscaledTime < _nextPinRefresh)
            {
                return;
            }
            _nextPinRefresh = Time.unscaledTime + 2f;
            var before = _store.Manifest.Zones.Count;
            _store.SelectLandmarks(LandmarkMapSource.ReadPersonalPins(),
                _store.Manifest.Selection.Radius, _store.Manifest.CharacterId, _store.Manifest.CharacterName);
            var after = _store.Manifest.Zones.Count;
            if (after != before)
            {
                _log.LogInfo($"Personal map pins refreshed during F8: {before} -> {after} selected zones.");
            }
        }

        // Compares horizontal travel distance with deterministic tie breaking.
        private static double DistanceSquared(ZoneEntry zone, Vector3 position)
        {
            var dx = (double)zone.X * 64 - position.x;
            var dz = (double)zone.Z * 64 - position.z;
            return dx * dx + dz * dz;
        }

        // Flies to the destination sector while staying above sampled terrain.
        private void Travel()
        {
            var dt = HoldForReception() ? 0f : Time.unscaledDeltaTime;
            if (_continuous.Approach(_zone, dt))
            {
                StartCapture();
            }
        }

        // Serializes only observed zone data and schedules the atomic file commit.
        private void Capture()
        {
            CaptureMovement();
            if (!_capture.Step())
            {
                return;
            }
            var result = _capture.Result;
            _capture.Dispose();
            var x = _zone.X;
            var z = _zone.Z;
            var version = GameContext.GameVersion;
            var store = _store;
            _write = Task.Run(() => store.WriteZone(x, z, result.Encode(), version, result.Objects.Count));
            _phase = CrawlPhase.Writing;
        }

        // Waits for the committed checkpoint before choosing the next zone or stopping here.
        private void FinishWrite()
        {
            CaptureMovement();
            if (!_write.IsCompleted)
            {
                return;
            }
            var work = _write;
            _write = null;
            work.GetAwaiter().GetResult();
            _log.LogInfo($"Exported zone {_zone.X},{_zone.Z}: {_zone.ObjectCount} objects; {_zone.Checksum}.");
            _capture = null;
            NextZone();
        }
    }
}
