using System.IO;
using BepInEx;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Defines the single production behavior; no BepInEx configuration is read or written.
    internal static class CrawlerConstants
    {
        public const KeyCode ExportKey = KeyCode.F8;
        public const KeyCode PrepareKey = KeyCode.F9;
        public const KeyCode RestoreKey = KeyCode.F10;
        public const int LandmarkRadius = 80;
        public const float Speed = 40f;
        public const float Clearance = 100f;
        public const float ZoneTimeout = 120f;
        public const int ObjectsPerFrame = 40;
        public const bool PreferPortals = true;
        public const float CruiseSpeed = 80f;
        public const float CruiseThreshold = 128f;
        public const bool AllowCoordinateJumps = true;
        public const float CoordinateJumpThreshold = 500f;
        public const float ExportedZoneOpacity = 0.2f;
        public const float RemainingZoneOpacity = 0.3f;
        public static string ExportRoot => Path.Combine(Paths.ConfigPath, "WorldCrawler", "worlds");
    }
}
