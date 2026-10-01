using BepInEx.Configuration;

namespace Landoria.WorldCrawler.Runtime
{
    // Keeps restoration choices independent of export and character-map state.
    internal sealed class RestorationSettings
    {
        public ConfigEntry<string> Source { get; }
        public ConfigEntry<int> MaximumZones { get; }

        // Requires an explicit export selection when several candidates exist.
        public RestorationSettings(ConfigFile config)
        {
            Source = config.Bind("Restoration", "ExportDirectory", "",
                "Exact export directory selected in the preparation window. Never choose the newest automatically.");
            MaximumZones = config.Bind("Restoration", "MaximumZonesPerRun", 1,
                "Initial validation limit. One zone by default; zero processes all remaining zones.");
        }
    }
}
