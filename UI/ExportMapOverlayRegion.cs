namespace Landoria.WorldCrawler.UI
{
    // Combines adjacent captured sectors into one visually identical horizontal strip.
    internal readonly struct ExportMapOverlayRegion
    {
        public readonly int MinX;
        public readonly int MaxX;
        public readonly int Z;

        // Stores inclusive sector coordinates without any game or UI dependencies.
        public ExportMapOverlayRegion(int minX, int maxX, int z)
        {
            MinX = minX;
            MaxX = maxX;
            Z = z;
        }
    }
}
