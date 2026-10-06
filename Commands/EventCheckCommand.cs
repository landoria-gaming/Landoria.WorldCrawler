using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Commands
{
    // Reports the native raid scheduler and event eligibility without starting an event.
    internal sealed class EventCheckCommand
    {
        private readonly ManualLogSource _log;
        private static readonly System.Reflection.FieldInfo Timer = AccessTools.Field(typeof(RandEventSystem), "m_eventTimer");
        private static readonly System.Reflection.MethodInfo Candidates =
            AccessTools.Method(typeof(RandEventSystem), "GetPossibleRandomEvents");

        // Registers a read-only native console command.
        internal EventCheckCommand(ManualLogSource log)
        {
            _log = log;
            var constructor = typeof(Terminal.ConsoleCommand).GetConstructors().Single(info =>
            {
                var p = info.GetParameters();
                return p.Length >= 3 && p[0].ParameterType == typeof(string) &&
                    p[1].ParameterType == typeof(string) && p[2].ParameterType == typeof(Terminal.ConsoleEvent) &&
                    p.Skip(3).All(parameter => parameter.IsOptional);
            });
            var values = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
            values[0] = "eventcheck";
            values[1] = "Show local raid timer, base value and possible events without starting one.";
            values[2] = new Terminal.ConsoleEvent(Execute);
            constructor.Invoke(values);
        }

        // Reads the host scheduler at the player's current position.
        private void Execute(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (!GameContext.Ready() || !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() ||
                    RandEventSystem.instance == null || Timer == null || Candidates == null)
                {
                    throw new InvalidOperationException("Use eventcheck while playing in a loaded local world.");
                }
                var system = RandEventSystem.instance;
                var timer = (float)Timer.GetValue(system);
                var period = system.m_eventIntervalMin * Game.m_eventRate;
                var chance = Game.m_eventRate > 0f ? system.m_eventChance / Game.m_eventRate : 0f;
                args.Context.AddString("Raids: rate " + Game.m_eventRate.ToString("F2", CultureInfo.InvariantCulture) +
                    ", roll every " + period.ToString("F1", CultureInfo.InvariantCulture) +
                    " min, chance " + chance.ToString("F1", CultureInfo.InvariantCulture) + "%.");
                args.Context.AddString("Timer: " + (timer / 60f).ToString("F1", CultureInfo.InvariantCulture) +
                    " min; base value: " + Player.m_localPlayer.GetBaseValue() + " (most raids need 3).");
                ReportCandidates(args, system);
            }
            catch (Exception error)
            {
                _log.LogWarning("Event check: " + error);
                args.Context.AddString(error.Message);
            }
        }

        // Uses Valheim's own eligibility calculation and reports its candidates.
        private static void ReportCandidates(Terminal.ConsoleEventArgs args, RandEventSystem system)
        {
            var possible = (List<KeyValuePair<RandomEvent, Vector3>>)Candidates.Invoke(system, null);
            args.Context.AddString("Possible random events here: " + possible.Count + ".");
            foreach (var item in possible.Take(20)) args.Context.AddString("  " + item.Key.m_name);
            if (possible.Count > 20) args.Context.AddString("  ... and " + (possible.Count - 20) + " more.");
            if (system.GetCurrentRandomEvent() != null)
            {
                args.Context.AddString("Current event: " + system.GetCurrentRandomEvent().m_name);
            }
        }
    }
}
