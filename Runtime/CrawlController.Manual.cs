using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Recaptures selected zones on manual arrival without replacing good files with partial observations.
    internal sealed partial class CrawlController
    {
        private bool _manual;
        private string _lastManualZone;
        private readonly CaptureApi _manualSectors = new CaptureApi();

        // Exposes the active export's committed geometry for both automatic and manual modes.
        internal ExportMapOverlayData MapProgress()
        {
            return _store == null ? null : ProgressMapSnapshot.Export(_store.Manifest,
                _phase == CrawlPhase.Capturing || _phase == CrawlPhase.Writing ? _zone : null);
        }

        // Uses the same receive pause and safe-flight protection while leaving routing to the user.
        private void BeginManualExport()
        {
            _motionGate = new ReceiveMotionGate(_player, CrawlerConstants.ZoneTimeout);
            _phase = CrawlPhase.ManualWaiting;
            Say("Manual export active. Move to selected zones; LeftCtrl+F8 stops. Saved zones will be replaced after a complete capture.");
        }

        // Recaptures the current selected sector once per visit, even when it already has a file.
        private void WaitForManualExport()
        {
            RefreshPersonalPins();
            _manualSectors.GetZone(_player.transform.position, out var x, out var z);
            var key = x + ":" + z;
            if (_lastManualZone == key)
            {
                return;
            }
            _zone = _store.Manifest.Zones.SingleOrDefault(v => v.X == x && v.Z == z && v.Status != "skipped");
            if (_zone == null)
            {
                return;
            }
            _lastManualZone = key;
            StartCapture();
        }

        // Waits through external teleports and discards only unfinished in-memory observations.
        private bool ManualExportTransit()
        {
            if (!_manual)
            {
                return false;
            }
            var teleport = _flight?.WaitForManualTeleport() == true || _player.IsTeleporting();
            if (_phase == CrawlPhase.Capturing && (teleport || LeftCaptureZone()))
            {
                _capture?.Dispose();
                _capture = null;
                _zone = null;
                _lastManualZone = null;
                _phase = CrawlPhase.ManualWaiting;
                Say("Manual travel detected; unfinished observation discarded. Existing zone file preserved.");
            }
            return teleport && _phase != CrawlPhase.Writing;
        }

        // Prevents unloading a sector from being mistaken for proof that its objects disappeared.
        private bool LeftCaptureZone()
        {
            _manualSectors.GetZone(_player.transform.position, out var x, out var z);
            return _zone != null && (_zone.X != x || _zone.Z != z);
        }
    }
}
