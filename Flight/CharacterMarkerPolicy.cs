using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Flight
{
    // Keeps only the active character's cheat-history flag false while preserving its first native backup.
    internal static class CharacterMarkerPolicy
    {
        private static PlayerProfile _profile;
        private static FieldInfo _field;
        private static CharacterCheatMarker _marker;
        private static bool _busy;
        private static bool _failed;
        private static ManualLogSource _log;

        // Registers logging without touching any character before a world has been joined.
        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        // Applies before saves and each frame; recursive native backup saves retain their original flag.
        internal static void Enforce(PlayerProfile candidate)
        {
            if (_busy || !SupportedGameVersions.IsCurrent(GameContext.GameVersion) || Game.instance == null ||
                Player.m_localPlayer == null || Minimap.instance == null || candidate == null || Game.instance.GetPlayerProfile() != candidate)
            {
                return;
            }
            if (_profile != candidate)
            {
                _profile = candidate;
                _marker = null;
                _failed = false;
                _field = AccessTools.Field(typeof(PlayerProfile), "m_usedCheats");
            }
            if (_failed || _field == null || !(bool)_field.GetValue(candidate))
            {
                return;
            }
            Reset();
        }

        // Backs up once per loaded profile before enforcing the requested false value.
        private static void Reset()
        {
            _busy = true;
            try
            {
                if (_marker == null)
                {
                    _marker = new CharacterCheatMarker(Path.Combine(CrawlerConstants.ExportRoot, "_characters"),
                        message => _log?.LogInfo(message));
                    _marker.Begin();
                }
                else
                {
                    _field.SetValue(_profile, false);
                }
            }
            catch (Exception error)
            {
                _failed = true;
                _log?.LogError("Character marker was not reset; backup/save failed: " + error);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
