namespace Landoria.WorldCrawler.Runtime
{
    // Shares a user-selected export for the current game session without storing configuration.
    internal sealed class RestoreSelection
    {
        public string SourceDirectory
        {
            get; set;
        }
    }
}
