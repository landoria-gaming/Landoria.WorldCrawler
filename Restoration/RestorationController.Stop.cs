namespace Landoria.WorldCrawler.Restoration
{
    // Ends restoration only after the final native save and journal checkpoint succeed.
    internal sealed partial class RestorationController
    {
        // Leaves the player at the current position; no landing, return flight, or teleport occurs.
        private void FinishStop()
        {
            _session.Journal.Save();
            Say("Restoration stopped and saved. Last confirmed save: " + _lastSaveUtc +
                ". Unfinished zones remain pending.");
            Close();
            _phase = RestorePhase.Stopped;
        }
    }
}
