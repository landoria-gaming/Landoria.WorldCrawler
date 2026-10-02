using System.IO;
using BepInEx;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Defines fixed recording and restoration production defaults.
    internal static class CrawlerConstants
    {
        public const KeyCode ExportKey = KeyCode.F8;
        public const KeyCode PrepareKey = KeyCode.F9;
        public const KeyCode RestoreKey = KeyCode.F10;
        public const float RecordingFlushInterval = 10f;
        public const float RestoreSaveInterval = 60f;
        public const int ObjectsPerFrame = 40;
        public const float ExportedZoneOpacity = 0.2f;
        public const float RemainingZoneOpacity = 0.3f;
        public static string ExportRoot => Path.Combine(Paths.ConfigPath, "WorldCrawler", "worlds");
    }
}
