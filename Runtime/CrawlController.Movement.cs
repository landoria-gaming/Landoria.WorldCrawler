using System;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Separates receive-controlled motion from the export checkpoint state machine.
    internal sealed partial class CrawlController
    {
        private ContinuousCaptureFlight _continuous;
        private ReceiveMotionGate _motionGate;
        private bool _receiveHold;

        // Selects native sprint travel for F8 without changing the existing return route.
        private void BeginContinuousFlight()
        {
            _receiveHold = false;
            if (!_settings.ContinuousSprint.Value) { return; }
            _continuous = new ContinuousCaptureFlight(_player, _flight, _settings.Clearance.Value);
            _motionGate = new ReceiveMotionGate(_player, Mathf.Clamp(_settings.ZoneTimeout.Value, 30f, 600f));
            _log.LogInfo($"Receive-controlled flight: native sprint target={_continuous.Speed:F2} m/s; quiet period=2s; outbound jumps and cruise disabled.");
        }

        // Holds immediately on relevant arrivals and reports only transitions rather than every frame.
        private bool HoldForReception()
        {
            if (_motionGate == null) { return false; }
            var hold = _motionGate.Hold();
            if (hold != _receiveHold)
            {
                _receiveHold = hold;
                _log.LogInfo(hold ? "Flight paused: receiving nearby export data."
                    : "Flight resumed: no relevant data received for two seconds.");
            }
            return hold;
        }

        // Opens a capture on approach and reserves an in-sector exit while network checks run.
        private void StartCapture()
        {
            _zone.Status = "loaded";
            _zone.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _store.Save();
            _capture = new ZoneCaptureSession(_zone.X, _zone.Z,
                Mathf.Clamp(_settings.MinimumDwell.Value, 5f, 120f),
                Mathf.Clamp(_settings.QuietSeconds.Value, 2f, 60f),
                Mathf.Clamp(_settings.ZoneTimeout.Value, 30f, 600f),
                Mathf.Clamp(_settings.ObjectsPerFrame.Value, 5, 200), _continuous != null);
            var next = _store.Manifest.Zones.Where(z => z != _zone && z.Status != "captured" && z.Status != "skipped")
                .OrderBy(z => DistanceSquared(z, new Vector3(_zone.X * 64f, 0f, _zone.Z * 64f)))
                .ThenBy(z => z.Z).ThenBy(z => z.X).FirstOrDefault();
            _continuous?.Begin(_zone, next);
            _phase = CrawlPhase.Capturing;
        }

        // Advances the route while work proceeds, holding on actual reception or at the safe sector exit.
        private void CaptureMovement()
        {
            if (_continuous != null && !HoldForReception()) { _continuous.Tick(Time.unscaledDeltaTime); }
            else { _flight.Tick(_player.transform.position, Time.unscaledDeltaTime); }
        }
    }
}
