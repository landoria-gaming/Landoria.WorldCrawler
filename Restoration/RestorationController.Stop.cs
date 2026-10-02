namespace Landoria.WorldCrawler.Restoration
{
    // Ends restoration only after the final native save and journal checkpoint succeed.
    internal sealed partial class RestorationController
    {
        // Leaves the player at the current position; no landing, return flight, or teleport occurs.
        private void FinishStop()
        {
            var state = _session.Journal.State;
            state.Status = state.Completed.Count == _session.Archive.Manifest.Zones.Count ? "completed-with-review" : "paused";
            _session.Journal.Save();
            _session.FinishCharacter();
            Say("Restoration stopped and saved. Last confirmed save: " + _lastSaveUtc +
                ". Unfinished zones remain pending.");
            Close();
            _phase = RestorePhase.Stopped;
        }
    }
}
