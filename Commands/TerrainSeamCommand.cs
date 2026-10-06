using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Commands
{
    // Lists and repairs terrain seams throughout one loaded local world.
    internal sealed class TerrainSeamCommand
    {
        private const float Threshold = 0.5f;
        private readonly ManualLogSource _log;
        private readonly Func<bool> _busy;

        // Registers native console actions for preview and guarded world-wide repair.
        internal TerrainSeamCommand(ManualLogSource log, Func<bool> busy)
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
            var arguments = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
            arguments[0] = "terrainseam";
            arguments[1] = "list | repair - Find and fix terrain seams at restored zone borders in a local world.";
            arguments[2] = new Terminal.ConsoleEvent(Execute);
            constructor.Invoke(arguments);
        }

        // Validates the world and complete export before scanning or changing terrain.
        private void Execute(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (args.Length != 2 || args[1] != "list" && args[1] != "repair")
                {
                    throw new ArgumentException("Usage: terrainseam list or terrainseam repair");
                }
                RequireLocalWorld();
                var world = GameContext.Identity();
                using (var archive = new ExportArchive(ExportSourceResolver.Resolve(world)))
                {
                    var restored = new HashSet<string>(RestoreJournal.Read(archive.DirectoryPath, world.Uid).RestoredZones);
                    var compilers = ReadCompilers(restored);
                    var seams = FindSeams(restored, compilers);
                    Report(args, restored.Count, seams);
                    if (args[1] == "repair" && seams.Count != 0)
                    {
                        Repair(args, archive.DirectoryPath, seams);
                    }
                }
            }
            catch (Exception error)
            {
                _log.LogWarning("Terrain seam: " + error);
                args.Context.AddString(error.Message);
            }
        }

        // Restricts world-wide edits to an idle local host with no other players.
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

        // Reads every persistent terrain compiler ZDO without visiting its zone.
        private static Dictionary<string, TerrainCompilerData> ReadCompilers(HashSet<string> restored)
        {
            var records = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("_TerrainCompiler", records, ref index))
            {
            }
            var api = new CaptureApi();
            var result = new Dictionary<string, TerrainCompilerData>(StringComparer.Ordinal);
            foreach (var record in records.Where(item => item != null && item.IsValid() && item.Persistent))
            {
                api.GetZone(record.GetPosition(), out var x, out var z);
                if (!restored.Contains(Key(x, z)) && !restored.Contains(Key(x - 1, z)) &&
                    !restored.Contains(Key(x + 1, z)) && !restored.Contains(Key(x, z - 1)) &&
                    !restored.Contains(Key(x, z + 1)))
                {
                    continue;
                }
                var compiler = new TerrainCompilerData(record, api);
                var key = Key(compiler.X, compiler.Z);
                if (result.ContainsKey(key))
                {
                    throw new InvalidOperationException("Duplicate terrain compilers in zone " + key + ".");
                }
                result.Add(key, compiler);
            }
            return result;
        }

        // Compares current native height edits on every side of each restored zone.
        private static List<TerrainSeam> FindSeams(HashSet<string> restored,
            Dictionary<string, TerrainCompilerData> compilers)
        {
            var result = new List<TerrainSeam>();
            var directions = new[] { (Side: 'N', X: 0, Z: 1, Other: 'S'), (Side: 'S', X: 0, Z: -1, Other: 'N'),
                (Side: 'E', X: 1, Z: 0, Other: 'W'), (Side: 'W', X: -1, Z: 0, Other: 'E') };
            foreach (var compiler in compilers.Values.Where(item => restored.Contains(Key(item.X, item.Z))))
            {
                foreach (var direction in directions)
                {
                    var neighborKey = Key(compiler.X + direction.X, compiler.Z + direction.Z);
                    compilers.TryGetValue(neighborKey, out var neighbor);
                    var current = compiler.Edge(direction.Side);
                    var other = neighbor == null ? new float[65] : neighbor.Edge(direction.Other);
                    var seam = new TerrainSeam(compiler, direction.Side, current, other,
                        neighbor != null && restored.Contains(neighborKey), Threshold);
                    if (seam.Count != 0)
                    {
                        result.Add(seam);
                    }
                }
            }
            return result.OrderBy(item => item.Compiler.X).ThenBy(item => item.Compiler.Z)
                .ThenBy(item => item.Side).ToList();
        }

        // Shows each affected zone and the largest native border difference.
        private void Report(Terminal.ConsoleEventArgs args, int restoredCount, List<TerrainSeam> seams)
        {
            Reply(args, seams.Count + " terrain seam sides found across " + restoredCount +
                " restored zones (" + seams.Select(item => Key(item.Compiler.X, item.Compiler.Z))
                    .Distinct().Count() + " affected zones):");
            foreach (var seam in seams)
            {
                Reply(args, Key(seam.Compiler.X, seam.Compiler.Z) + " " + seam.Side + " | " + seam.Count +
                    " vertices | max " + seam.Maximum.ToString("F2", CultureInfo.InvariantCulture) + "m");
            }
        }

        // Backs up the committed save, applies all precomputed edits, then requests a native save.
        private void Repair(Terminal.ConsoleEventArgs args, string exportDirectory, List<TerrainSeam> seams)
        {
            var plan = seams.GroupBy(item => item.Compiler).Select(group =>
                (Compiler: group.Key, Bytes: group.Key.Repair(group))).ToArray();
            var before = LatestWorldApi.SaveNumber();
            var backup = WorldBackup.Create(LatestWorldApi.DirectoryFor(ZNet.World), exportDirectory);
            if (ZNet.instance.IsSaving() || LatestWorldApi.SaveNumber() != before)
            {
                throw new InvalidOperationException("The world changed during backup. No terrain was changed.");
            }
            var applied = new List<TerrainCompilerData>();
            try
            {
                foreach (var item in plan)
                {
                    var current = item.Compiler.Record.GetByteArray(ZDOVars.s_TCData);
                    if (current == null || !item.Compiler.Original.SequenceEqual(current))
                    {
                        throw new InvalidOperationException("Terrain data changed during scan. Repairs were rolled back.");
                    }
                    item.Compiler.Record.Set(ZDOVars.s_TCData, item.Bytes);
                    Heightmap.FindHeightmap(item.Compiler.Center)?.Poke();
                    applied.Add(item.Compiler);
                }
                LatestWorldApi.BeginSave();
            }
            catch
            {
                RollBack(applied);
                throw;
            }
            Reply(args, "Repaired " + plan.Length + " zones. Native world save requested. Backup: " + backup);
        }

        // Restores original compiler bytes if mutation or save startup fails.
        private static void RollBack(IEnumerable<TerrainCompilerData> applied)
        {
            foreach (var item in applied)
            {
                item.Record.Set(ZDOVars.s_TCData, item.Original);
                Heightmap.FindHeightmap(item.Center)?.Poke();
            }
        }

        // Formats the existing journal's zone coordinate key.
        private static string Key(int x, int z)
        {
            return x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture);
        }

        // Writes the same concise result to the console and plugin log.
        private void Reply(Terminal.ConsoleEventArgs args, string message)
        {
            args.Context.AddString(message);
            _log.LogInfo("Terrain seam: " + message);
        }
    }
}
