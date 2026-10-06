using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Objects;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Runtime;

namespace Landoria.WorldCrawler.Commands
{
    // Lists and repairs future source-world timestamps on restored objects.
    internal sealed class WorldClockCommand
    {
        private readonly ManualLogSource _log;
        private readonly Func<bool> _busy;

        // Registers read-only listing and explicit repair in the native console.
        internal WorldClockCommand(ManualLogSource log, Func<bool> busy)
        {
            _log = log;
            _busy = busy;
            var constructor = typeof(Terminal.ConsoleCommand).GetConstructors().Single(info =>
            {
                var parameters = info.GetParameters();
                return parameters.Length >= 3 && parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(string) && parameters[2].ParameterType == typeof(Terminal.ConsoleEvent) &&
                    parameters.Skip(3).All(parameter => parameter.IsOptional);
            });
            var values = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
            values[0] = "worldclock";
            values[1] = "list | repair - Find and fix clocks on restored objects in a local world.";
            values[2] = new Terminal.ConsoleEvent(Execute);
            constructor.Invoke(values);
        }

        // Rechecks the current local world before scanning all tagged ZDOs.
        private void Execute(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (args.Length != 2 || args[1] != "list" && args[1] != "repair")
                {
                    throw new ArgumentException("Usage: worldclock list or worldclock repair");
                }
                RequireLocalWorld();
                Scan(args, args[1] == "repair");
            }
            catch (Exception error)
            {
                _log.LogWarning("World clock: " + error);
                args.Context.AddString(error.Message);
            }
        }

        // Restricts mutations to an idle local host without remote players.
        private void RequireLocalWorld()
        {
            LatestWorldApi.RequireCurrent();
            if (_busy() || !GameContext.Ready() || !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() ||
                ZNet.World.m_fileSource != LatestWorldApi.LocalSource || ZNet.instance.GetPeers().Count != 0 ||
                ZNet.instance.IsSaving())
            {
                throw new InvalidOperationException("Use this command alone in a loaded LOCAL world, outside F8/F10 and world saves.");
            }
        }

        // Uses the source tag index so unloaded zones are included without visiting them.
        private static void Scan(Terminal.ConsoleEventArgs args, bool repair)
        {
            var ids = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.String,
                ObjectRestorer.IdentityTag.GetStableHashCode());
            var now = ZNet.instance.GetTime().Ticks;
            var affected = 0;
            var fields = 0;
            var targets = new List<ZDO>();
            foreach (var id in ids)
            {
                var zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || string.IsNullOrEmpty(zdo.GetString(ObjectRestorer.IdentityTag, ""))) continue;
                var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null) continue;
                var count = RestoredClockRepair.Inspect(zdo, prefab, now, repair);
                if (count == 0) continue;
                affected++;
                fields += count;
                targets.Add(zdo);
                if (affected <= 30)
                {
                    args.Context.AddString(prefab.name + " at " + zdo.GetPosition().ToString("F1") +
                        ": " + count + " blocked clock field(s)");
                }
            }
            args.Context.AddString("Found " + affected + " restored objects with " + fields +
                " blocked clock fields.");
            if (repair && targets.Count > 0) Repair(args, targets);
        }

        // Backs up the committed world before changing timers and requesting a native save.
        private static void Repair(Terminal.ConsoleEventArgs args, List<ZDO> targets)
        {
            var before = LatestWorldApi.SaveNumber();
            var export = ExportSourceResolver.Resolve(GameContext.Identity());
            var backup = WorldBackup.Create(LatestWorldApi.DirectoryFor(ZNet.World), export);
            if (ZNet.instance.IsSaving() || LatestWorldApi.SaveNumber() != before)
            {
                throw new InvalidOperationException("The world changed during backup. No timers were changed.");
            }
            var now = ZNet.instance.GetTime().Ticks;
            var fields = 0;
            foreach (var zdo in targets)
            {
                var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab != null) fields += RestoredClockRepair.Inspect(zdo, prefab, now, true);
            }
            LatestWorldApi.BeginSave();
            args.Context.AddString("Repaired " + fields + " clock fields. Native world save requested. Backup: " + backup);
        }
    }
}
