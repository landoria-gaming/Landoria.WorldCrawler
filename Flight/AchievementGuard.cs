using System;
using System.Collections.Generic;
using System.Reflection;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Flight
{
    // Observes achievement and cheat state without changing eligibility or counters.
    internal sealed class AchievementGuard
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Static | BindingFlags.Instance;
        private readonly Dictionary<string, bool> baseline;
        private readonly Player player;
        private readonly PlayerProfile profile;
        private readonly Type achievements;
        public string GameVersion
        {
            get; private set;
        }
        public bool? Eligible
        {
            get; private set;
        }
        public string Summary
        {
            get; private set;
        }

        // Records only the supported runtime's original achievement inputs.
        public AchievementGuard(Player player)
        {
            this.player = player;
            profile = Game.instance.GetPlayerProfile();
            GameVersion = CheckCompatibility();
            achievements = typeof(Player).Assembly.GetType("Achievements");
            if (SupportedGameVersions.IsCurrent(GameVersion) != (achievements != null))
            {
                throw new InvalidOperationException("The achievement API does not match the game version.");
            }
            baseline = ReadFlags();
            Eligible = achievements == null ? (bool?)null : Call(achievements, null,
                "CanGetAchievements", new[] { typeof(bool) }, false);
            Summary = achievements == null ? "Legacy game: no achievement API; cheat flags monitored." :
                "Achievement eligibility at start: " + Eligible + "; modded session: " +
                baseline["modded"] + ". No achievements are granted; other eligibility checks remain active.";
        }

        // Rejects unsupported branches before validating the selected runtime's achievement APIs.
        public static string CheckCompatibility()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            PropertyInfo current = version.GetProperty("CurrentVersion", All);
            string value = current == null ? "unknown" : current.GetValue(null, null).ToString();
            if (!SupportedGameVersions.CanExport(value))
            {
                throw new NotSupportedException("Unsupported Valheim version for flight: " + value + ".");
            }
            return value;
        }

        // Fails when a profile, world, item, or mode flag differs from the initial state.
        public void Validate(bool manual = false)
        {
            if (Game.instance == null || !ReferenceEquals(profile, Game.instance.GetPlayerProfile()))
            {
                throw new InvalidOperationException("The player profile changed during the crawl.");
            }
            if (manual)
            {
                return;
            }
            Dictionary<string, bool> current = ReadFlags();
            foreach (KeyValuePair<string, bool> flag in baseline)
            {
                if (current[flag.Key] != flag.Value)
                {
                    throw new InvalidOperationException("Achievement or cheat state changed: " + flag.Key +
                        ". World Crawler stopped without resetting the flag.");
                }
            }
        }

        // Reads shared flags plus the new version's persisted eligibility inputs.
        private Dictionary<string, bool> ReadFlags()
        {
            var flags = new Dictionary<string, bool>
            {
                { "modded", ReadField(typeof(Game), null, "isModded") },
                { "devcommands", ReadField(typeof(Terminal), null, "m_cheat") },
                { "debug", Player.m_debugMode },
                { "debugFly", player.InDebugFlyMode() },
                { "god", player.InGodMode() },
                { "ghost", ReadField(typeof(Player), player, "m_ghostMode") },
                { "noCost", player.NoCostCheat() }
            };
            if (achievements != null)
            {
                flags.Add("profileUsedCheats", ReadField(typeof(PlayerProfile), profile, "m_usedCheats"));
                flags.Add("worldCheated", Call(achievements, null, "IsWorldCheated", Type.EmptyTypes));
                flags.Add("itemsCheated", Call(typeof(global::Inventory), player.GetInventory(),
                    "AnyCheatedItem", Type.EmptyTypes));
                PropertyInfo bypass = typeof(PlayerProfile).GetProperty("s_bypassCheatChecks", All);
                if (bypass == null || bypass.PropertyType != typeof(bool))
                {
                    throw new MissingMemberException("The achievement bypass property cannot be audited.");
                }
                flags.Add("bypass", (bool)bypass.GetValue(null, null));
            }
            return flags;
        }

        // Reads an expected Boolean field and fails instead of assuming a default.
        private static bool ReadField(Type type, object target, string name)
        {
            FieldInfo field = type.GetField(name, All);
            if (field == null || field.FieldType != typeof(bool))
            {
                throw new MissingFieldException(type.FullName, name);
            }
            return (bool)field.GetValue(target);
        }

        // Calls an exact read-only Boolean API without referencing new-only game types.
        private static bool Call(Type type, object target, string name, Type[] types, params object[] args)
        {
            MethodInfo method = type.GetMethod(name, All, null, types, null);
            if (method == null || method.ReturnType != typeof(bool))
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return (bool)method.Invoke(target, args);
        }
    }
}
