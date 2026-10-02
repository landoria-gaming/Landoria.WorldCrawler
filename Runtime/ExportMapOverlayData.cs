namespace Landoria.WorldCrawler.Runtime
{
    // Separates committed and pending data geometry for independent map colors.
    internal sealed class ExportMapOverlayData
    {
        internal string Warning;
        public ExportMapOverlayRegion[] Captured = System.Array.Empty<ExportMapOverlayRegion>();
        public ExportMapOverlayRegion[] Remaining = System.Array.Empty<ExportMapOverlayRegion>();
    }
}
