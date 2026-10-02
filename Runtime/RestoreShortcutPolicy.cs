namespace Landoria.WorldCrawler.Runtime
{
    // Separates stationary zone import from travel and rejects unsupported modified F10 chords.
    internal static class RestoreShortcutPolicy
    {
        // Resolves a pressed F10 using the held modifiers without falling back to travel.
        internal static int Select(bool currentZoneModifier, bool otherModifier)
        {
            if (otherModifier)
            {
                return -1;
            }
            return currentZoneModifier ? 3 : 2;
        }
    }
}
