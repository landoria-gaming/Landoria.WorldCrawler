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
    // Groups return behavior without changing the controller's shared state.
    internal sealed partial class RestorationController
    {
        // Starts an ordinary controlled return without erasing interruption recovery.
        private void ReturnHome()
        {
            _session.Journal.State.Status = "returning";
            _session.Journal.Save();
            _phase = RestorePhase.Returning;
            Say("Restoration saved. Returning to the starting point...");
        }

        // Waits for the saved origin to be loaded before descending.
        private void Return()
        {
            var origin = _session.Flight.Origin;
            if (!_navigation.Travel(origin.x, origin.z, Time.unscaledDeltaTime, true))
            {
                return;
            }
            _waitingSince = Time.unscaledTime;
            _phase = RestorePhase.Landing;
        }

        // Releases physics and marks pause or completion only after reaching the origin.
        private void Land()
        {
            var flight = _session.Flight;
            if (Time.unscaledTime - _waitingSince > 180f)
            {
                throw new TimeoutException("Return area did not stabilize.");
            }
            if (!ZNetScene.instance.IsAreaReady(flight.Origin))
            {
                _session.Hold(Time.unscaledDeltaTime);
                return;
            }
            var heightmap = Heightmap.FindHeightmap(flight.Origin);
            if (heightmap == null || heightmap.HaveQueuedRebuild())
            {
                _session.Hold(Time.unscaledDeltaTime);
                return;
            }
            if (ZoneSystem.instance.GetSolidHeight(flight.Origin, out var height, 1000) && height > flight.Origin.y + 0.5f &&
                flight.RaiseReturnHeight(height + 0.5f))
            {
                _session.Journal.State.ReturnPosition = CaptureTransform.Vector(flight.Origin);
                _session.Journal.Save();
                Say("Restored terrain has changed: returning above the surface.");
            }
            if (!flight.Tick(flight.Origin, Time.unscaledDeltaTime))
            {
                return;
            }
            flight.End();
            FinishLanding();
        }

        // Records completion only after the character has safely landed.
        private void FinishLanding()
        {
            var state = _session.Journal.State;
            state.ReturnPending = false;
            state.Status = state.Completed.Count == _session.Archive.Manifest.Zones.Count ? "completed-with-review" : "paused";
            if (state.Status == "completed-with-review" && _session.Archive.Manifest.Zones.Count < _session.Archive.PlannedZoneCount)
            {
                state.Status = "awaiting-export";
            }
            _session.Journal.Save();
            if (state.Warnings.Count > 0)
            {
                _log.LogWarning($"Restoration {state.Status}: {state.Warnings.Count} distinct review warnings are recorded in {_session.Journal.DirectoryPath}.");
            }
            Say("Restoration " + state.Status + ". Report and backup preserved. Check the world after reloading.");
            Close();
            _phase = RestorePhase.Stopped;
        }
    }
}
