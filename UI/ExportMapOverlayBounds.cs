namespace Landoria.WorldCrawler.UI
{
    // Holds clipped rectangle corners normalized to the visible large-map viewport.
    internal readonly struct ExportMapOverlayBounds
    {
        public readonly double Left;
        public readonly double Bottom;
        public readonly double Right;
        public readonly double Top;

        // Retains double precision until coordinates reach the Unity UI mesh.
        public ExportMapOverlayBounds(double left, double bottom, double right, double top)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
        }
    }
}
