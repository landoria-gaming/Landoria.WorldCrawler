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
        public const float RecordingFlushInterval = 10f;
        public const float RestoreSaveInterval = 120f;
        public const int ObjectsPerFrame = 40;
        public const float ExportedZoneOpacity = 0.2f;
        public const float RemainingZoneOpacity = 0.3f;
        public static string ExportRoot => Path.Combine(Paths.ConfigPath, "WorldCrawler", "worlds");
    }
}
