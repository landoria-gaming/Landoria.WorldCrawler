using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Holds conservative crawler timing, movement, and storage settings.
    internal sealed class CrawlerSettings
    {
        public ConfigEntry<string> ExportRoot { get; }
        public ConfigEntry<float> Speed { get; }
        public ConfigEntry<bool> ContinuousSprint { get; }
        public ConfigEntry<float> Clearance { get; }
        public ConfigEntry<float> MinimumDwell { get; }
        public ConfigEntry<float> QuietSeconds { get; }
        public ConfigEntry<float> ZoneTimeout { get; }
        public ConfigEntry<int> ObjectsPerFrame { get; }
        public ConfigEntry<int> MaximumZonesPerRun { get; }
        public ConfigEntry<bool> InventoryOnly { get; }
        public ConfigEntry<ExportSelectionMode> SelectionMode { get; }
        public ConfigEntry<int> LandmarkRadius { get; }
        public ConfigEntry<bool> PreferPortals { get; }
        public ConfigEntry<float> CruiseSpeed { get; }
        public ConfigEntry<float> CruiseThreshold { get; }
        public ConfigEntry<bool> AllowCoordinateJumps { get; }
        public ConfigEntry<float> CoordinateJumpThreshold { get; }
        public ConfigEntry<bool> ShowExportedZones { get; }
        public ConfigEntry<float> ExportedZoneOpacity { get; }
        public ConfigEntry<bool> ShowRemainingZones { get; }
        public ConfigEntry<float> RemainingZoneOpacity { get; }

        // Binds tunable settings without enabling game debug or cheat features.
        public CrawlerSettings(ConfigFile config)
        {
            ExportRoot = config.Bind("Storage", "Directory", Path.Combine(Paths.ConfigPath,
                "WorldCrawler", "worlds"), "Export directory containing one folder per world UID and seed.");
            Speed = config.Bind("Flight", "Speed", 20f, "Flight speed in metres per second (5-50).");
            ContinuousSprint = config.Bind("Flight", "ReceiveControlledSprint", true,
                "F8 flies at native sprint speed, stops on nearby useful data, then resumes after two quiet seconds. Capture also waits for loaded objects. Overrides outbound portals, jumps, cruise and minimum dwell; return teleport is unchanged.");
            Clearance = config.Bind("Flight", "GroundClearance", 100f,
                "Minimum height above sampled terrain during travel (40-200 metres).");
            MinimumDwell = config.Bind("Capture", "MinimumDwellSeconds", 10f,
                "Minimum time at each loaded zone before accepting a stable observation.");
            QuietSeconds = config.Bind("Capture", "QuietSeconds", 5f,
                "Required stable object membership window. This is not a server completeness guarantee.");
            ZoneTimeout = config.Bind("Capture", "ZoneTimeoutSeconds", 120f,
                "Pause if a zone does not become stable in time.");
            ObjectsPerFrame = config.Bind("Capture", "ObjectsPerFrame", 40,
                "Maximum objects serialized in one frame.");
            MaximumZonesPerRun = config.Bind("Capture", "MaximumZonesPerRun", 0,
                "Optional test limit; zero exports all pending zones. Progress is always resumable.");
            InventoryOnly = config.Bind("Capture", "InventoryOnly", false,
                "Save and validate the inventory without moving the character.");
            SelectionMode = config.Bind("Selection", "Mode", ExportSelectionMode.Landmarks,
                "Landmarks selects portals, spawn, discovered places and saved non-round map pins. ExploredMap keeps the old full-map route.");
            LandmarkRadius = config.Bind("Selection", "RadiusMetres", 80,
                new ConfigDescription("Horizontal radius around each landmark; intersecting 64-metre zones are exported whole.",
                    new AcceptableValueRange<int>(1, 1000)));
            BindTravel(config, out var prefer, out var cruise, out var threshold, out var jumps, out var jumpThreshold);
            PreferPortals = prefer; CruiseSpeed = cruise; CruiseThreshold = threshold;
            AllowCoordinateJumps = jumps; CoordinateJumpThreshold = jumpThreshold;
            BindMap(config, out var show, out var opacity, out var remaining, out var remainingOpacity);
            ShowExportedZones = show; ExportedZoneOpacity = opacity;
            ShowRemainingZones = remaining; RemainingZoneOpacity = remainingOpacity;
        }

        // Exposes a read-only large-map progress layer without changing exploration data.
        private static void BindMap(ConfigFile config, out ConfigEntry<bool> show, out ConfigEntry<float> opacity,
            out ConfigEntry<bool> remaining, out ConfigEntry<float> remainingOpacity)
        {
            show = config.Bind("Map", "ShowExportedZones", true,
                "Highlight captured sectors from the current export selection on the large map only.");
            opacity = config.Bind("Map", "ExportedZoneOpacity", 0.2f,
                new ConfigDescription("Opacity of the green exported-zone overlay (0.05-0.6). Does not reveal fog.",
                    new AcceptableValueRange<float>(0.05f, 0.6f)));
            remaining = config.Bind("Map", "ShowRemainingZones", true,
                "Highlight unfinished sectors in amber on the large map only.");
            remainingOpacity = config.Bind("Map", "RemainingZoneOpacity", 0.3f,
                new ConfigDescription("Opacity of the amber remaining-zone overlay (0.03-0.4). Does not reveal fog.",
                    new AcceptableValueRange<float>(0.03f, 0.4f)));
        }

        // Keeps long-distance travel optional and bounded independently of slow observation movement.
        private static void BindTravel(ConfigFile config, out ConfigEntry<bool> prefer,
            out ConfigEntry<float> cruise, out ConfigEntry<float> threshold,
            out ConfigEntry<bool> jumps, out ConfigEntry<float> jumpThreshold)
        {
            prefer = config.Bind("Travel", "PreferPortals", true,
                "Use known connected portals when they shorten the route, respecting native portal restrictions.");
            cruise = config.Bind("Travel", "CruiseSpeed", 60f,
                new ConfigDescription("Flight speed for long horizontal legs (metres/second). Server corrections stop the crawl.",
                    new AcceptableValueRange<float>(5f, 100f)));
            threshold = config.Bind("Travel", "CruiseThresholdMetres", 500f,
                new ConfigDescription("Use cruise speed only beyond this horizontal distance; approach and descent stay slow.",
                    new AcceptableValueRange<float>(128f, 2000f)));
            jumps = config.Bind("Travel", "AllowCoordinateJumps", true,
                "Allow native distant teleports to known route coordinates when no useful portal is chosen. These do not apply portal cargo restrictions.");
            jumpThreshold = config.Bind("Travel", "CoordinateJumpThresholdMetres", 500f,
                new ConfigDescription("Minimum horizontal route distance for a native coordinate jump; wait for loading before capture.",
                    new AcceptableValueRange<float>(128f, 10000f)));
        }
    }
}
