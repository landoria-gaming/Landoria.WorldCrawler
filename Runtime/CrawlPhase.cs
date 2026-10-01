namespace Landoria.WorldCrawler.Runtime
{
    // Identifies the current resumable export operation.
    internal enum CrawlPhase
    {
        Idle, Preparing, Testing, Travelling, Capturing, Writing,
        Returning, Landing, Paused, Completed, Faulted
    }
}
