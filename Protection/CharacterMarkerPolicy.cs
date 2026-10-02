using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Protection
{
    // Keeps the active character's cheat-history flag false only while optional cheats are enabled.
    internal static class CharacterMarkerPolicy
    {
        private static PlayerProfile _profile;
        private static FieldInfo _field;
        private static bool _failed;
        private static ManualLogSource _log;

        // Registers logging without touching any character before a world has been joined.
        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        // Applies in memory before native saves without keeping character files or identities on disk.
        internal static void Enforce(PlayerProfile candidate)
        {
            if (!PlayerProtection.Enabled || !SupportedGameVersions.IsCurrent(GameContext.GameVersion) || Game.instance == null ||
                Player.m_localPlayer == null || Minimap.instance == null || candidate == null || Game.instance.GetPlayerProfile() != candidate)
            {
                return;
            }
            if (_profile != candidate)
            {
                _profile = candidate;
                _failed = false;
                _field = AccessTools.Field(typeof(PlayerProfile), "m_usedCheats");
            }
            if (_failed || _field == null || !(bool)_field.GetValue(candidate))
            {
                return;
            }
            Reset();
        }

        // Clears the single supported marker in the live profile; native saving owns persistence.
        private static void Reset()
        {
            try
            {
                _field.SetValue(_profile, false);
            }
            catch (Exception error)
            {
                _failed = true;
                _log?.LogError("Character marker was not reset: " + error);
            }
        }
    }
}
