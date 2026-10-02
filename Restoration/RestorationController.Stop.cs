using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Stops restoration locally and never travels back to an earlier position.
    internal sealed partial class RestorationController
    {
        private FlightLanding _landing;

        // Manual mode stops in place; automatic mode first lands at its current horizontal location.
        private void StopHere()
        {
            if (_manual || _session.Flight == null)
            {
                FinishLanding();
                return;
            }
            _landing = new FlightLanding(_session.Flight, Player.m_localPlayer);
            _phase = RestorePhase.Landing;
            Say("Progress saved. Landing here; no return to the starting point.");
        }

        // Ends automated movement only after a local landing, without using stored origins.
        private void Land()
        {
            if (_landing.Step(Time.unscaledDeltaTime))
            {
                FinishLanding();
            }
        }

        // Records durable status while leaving the character at the last visited location.
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
            _session.FinishCharacter();
            if (state.Warnings.Count > 0)
            {
                _log.LogWarning($"Restoration {state.Status}: {state.Warnings.Count} review warnings in {_session.Journal.DirectoryPath}.");
            }
            Say(Operation + " " + state.Status + ". Position unchanged; report and backup preserved.");
            Close();
            _phase = RestorePhase.Stopped;
        }
    }
}
