namespace Landoria.WorldCrawler.Capture
{
    // Summarizes actual arrivals and cache state without interpreting them as missing server data.
    internal sealed partial class RecordingObserver
    {
        private readonly RecordingDiagnostics _diagnostics = new RecordingDiagnostics();

        // Reports cache size, source changes and explicit capture errors every few seconds.
        internal string Describe()
        {
            return "cachedZones=" + Pending + "; cachedObjects=" + PendingObjects +
                "; localSweepRemaining=" + _sweep.Count + "; captureErrors=" + Errors +
                "; lastError=" + (LastError ?? "none") + "; " + _diagnostics.Describe();
        }
    }
}
