namespace Landoria.WorldCrawler.Runtime
{
    // Identifies the current resumable export operation.
    internal enum CrawlPhase
    {
        Idle, Preparing, Testing, ManualWaiting, Travelling, Capturing, Writing,
        Landing, Paused, Completed, Faulted
    }
}
