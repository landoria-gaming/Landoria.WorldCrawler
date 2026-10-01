using System;
using System.Reflection;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads common game state without referencing version-specific value types.
    internal static class GameContext
    {
        public static string GameVersion
        {
            get; private set;
        }

        // Selects supported version families; adapters still require their exact API signatures.
        public static void ValidateVersion()
        {
            var type = typeof(Player).Assembly.GetType("Version", true);
            var version = type.GetProperty("CurrentVersion", BindingFlags.Public | BindingFlags.Static);
            GameVersion = version?.GetValue(null)?.ToString();
            if (!SupportedGameVersions.CanExport(GameVersion))
            {
                throw new NotSupportedException("Unsupported Valheim version: " + GameVersion);
            }
        }

        // Waits for a connected, living character with a fully initialized world.
        public static bool Ready(bool allowControlledTransit = false)
        {
            return Player.m_localPlayer != null && !Player.m_localPlayer.IsDead()
                && (allowControlledTransit || !Player.m_localPlayer.IsTeleporting()) && !Player.m_localPlayer.InCutscene()
                && ZNet.instance != null && ZNet.World != null && Game.instance != null
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
                && ZDOMan.instance != null && ZNetScene.instance != null;
        }

        // Copies the exact connected world identity into export metadata.
        public static WorldIdentity Identity()
        {
            var world = ZNet.World ?? throw new InvalidOperationException("No connected world.");
            return new WorldIdentity
            {
                Name = world.m_name,
                Uid = world.m_uid,
                SeedText = world.m_seedName,
                Seed = world.m_seed,
                GenerationVersion = world.m_worldGenVersion
            };
        }

        // Checks session identity before touching a player or saving a capture.
        public static bool SameSession(WorldIdentity world, Player player)
        {
            return player != null && Player.m_localPlayer == player && ZNet.World != null
                && world.Matches(Identity()) && ZNet.instance != null
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }
    }
}
