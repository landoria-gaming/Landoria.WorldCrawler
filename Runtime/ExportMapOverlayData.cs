namespace Landoria.WorldCrawler.Runtime
{
    // Separates committed and remaining itinerary geometry for independent map colors.
    internal sealed class ExportMapOverlayData
    {
        public ExportMapOverlayRegion[] Captured = System.Array.Empty<ExportMapOverlayRegion>();
        public ExportMapOverlayRegion[] Remaining = System.Array.Empty<ExportMapOverlayRegion>();
    }
}
