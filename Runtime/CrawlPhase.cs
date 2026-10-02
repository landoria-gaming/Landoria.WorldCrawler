namespace Landoria.WorldCrawler.Runtime
{
    // Separates manual observations from their validated atomic commits.
    internal enum CrawlPhase
    {
        Idle, Preparing, Waiting, Writing, Stopped
    }
}
