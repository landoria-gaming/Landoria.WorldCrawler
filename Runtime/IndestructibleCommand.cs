using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Lists nearby generated indestructible scenery and deletes only an explicitly selected entry.
    internal sealed class IndestructibleCommand : IDisposable
    {
        private readonly ManualLogSource _log;
        private readonly Func<bool> _busy;
        private readonly RestorationController _restore;
        private readonly List<IndestructibleTarget> _listed = new List<IndestructibleTarget>();
        private readonly HashSet<ZDOID> _pending = new HashSet<ZDOID>();
        private World _pendingWorld;
        private World _world;
        private float _radius = 30f;
        private bool _disposed;

        // Registers through optional native arguments because the constructor differs across game versions.
        internal IndestructibleCommand(ManualLogSource log, Func<bool> busy, RestorationController restore)
        {
            _log = log;
            _busy = busy;
            _restore = restore;
            var constructor = typeof(Terminal.ConsoleCommand).GetConstructors().Single(info =>
            {
                var parameters = info.GetParameters();
                return parameters.Length >= 3 && parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(string) && parameters[2].ParameterType == typeof(Terminal.ConsoleEvent) &&
                    parameters.Skip(3).All(parameter => parameter.IsOptional);
            });
            var arguments = constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
            arguments[0] = "indestructible";
            arguments[1] = "list [radius=30] | delete <number> - Manage nearby generated indestructible scenery in a local world.";
            arguments[2] = new Terminal.ConsoleEvent(Execute);
            constructor.Invoke(arguments);
        }

        // Keeps preview read-only and requires a prior list before accepting a numbered deletion.
        private void Execute(Terminal.ConsoleEventArgs args)
        {
            try
            {
                RequireLocalWorld();
                if (args.Length >= 2 && args[1] == "list")
                {
                    WithSource(source => List(args, source));
                }
                else if (args.Length == 3 && args[1] == "delete")
                {
                    WithSource(source => Delete(args, source));
                }
                else
                {
                    Reply(args, "Usage: indestructible list [radius=30] or indestructible delete <number>");
                }
            }
            catch (Exception error)
            {
                _log.LogWarning("Indestructible command: " + error.Message);
                args.Context.AddString(error.Message);
            }
        }

        // Shares F10's frozen index, or validates the same export before allowing an idle command.
        private void WithSource(Action<CleanupSourceIndex> action)
        {
            if (_busy())
            {
                throw new InvalidOperationException("Wait for recording or world preparation to finish first.");
            }
            var source = _restore.ScenerySource();
            if (source != null)
            {
                action(source);
                return;
            }
            var world = GameContext.Identity();
            using (var archive = new ExportArchive(ExportSourceResolver.Resolve(world)))
            {
                RestoreWorldIdentity.Require(world, archive.Manifest.World);
                action(archive.Cleanup);
            }
        }

        // Restricts the destructive utility to a supported local world with no other connected players.
        private void RequireLocalWorld()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("World Crawler is not active.");
            }
            LatestWorldApi.RequireCurrent();
            if (!GameContext.Ready() || ZoneSystem.instance == null || !ZNet.instance.IsServer() ||
                ZNet.instance.IsDedicated() || ZNet.World.m_fileSource != LatestWorldApi.LocalSource || ZNet.instance.GetPeers().Count != 0)
            {
                throw new InvalidOperationException("Use this command alone in a loaded LOCAL world, outside a teleport.");
            }
        }

        // Numbers exact live candidates inside a bounded radius without touching their data.
        private void List(Terminal.ConsoleEventArgs args, CleanupSourceIndex source)
        {
            _listed.Clear();
            _world = null;
            _radius = 30f;
            if (args.Length > 3 || args.Length == 3 && (!float.TryParse(args[2], NumberStyles.Float,
                CultureInfo.InvariantCulture, out _radius) || float.IsNaN(_radius) || _radius < 1f || _radius > 64f))
            {
                throw new ArgumentException("Usage: indestructible list [radius between 1 and 64 metres]");
            }
            var center = Player.m_localPlayer.transform.position;
            var api = new CaptureApi();
            var candidates = NearbyViews(api, center)
                .Select(view => IndestructibleTarget.Read(view, api, source, center, _radius))
                .Where(target => target != null).OrderBy(target => target.Distance(center)).ToArray();
            _listed.AddRange(candidates);
            _world = ZNet.World;
            Reply(args, _listed.Count + " removable indestructible decorations within " + _radius + "m (by center):");
            for (var index = 0; index < _listed.Count; index++)
            {
                var item = _listed[index];
                Reply(args, (index + 1) + ": " + item.Name + " | " + item.Distance(center).ToString("F1", CultureInfo.InvariantCulture) +
                    "m | " + item.Position.ToString("F1"));
            }
            if (_listed.Count > 0)
            {
                Reply(args, "Back up your world first. indestructible delete <number> removes that entire listed decoration only.");
            }
        }

        // Queries only nearby server sectors and ignores objects without a loaded live view.
        private IEnumerable<ZNetView> NearbyViews(CaptureApi api, Vector3 center)
        {
            var margin = new Vector3(_radius, 0f, _radius);
            api.GetZone(center - margin, out var minX, out var minZ);
            api.GetZone(center + margin, out var maxX, out var maxZ);
            var objects = new List<ZDO>();
            var seen = new HashSet<ZDOID>();
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    api.FindObjects(new Vector3(x * 64f, 0, z * 64f), objects);
                    foreach (var data in objects)
                    {
                        var view = data == null ? null : ZNetScene.instance.FindInstance(data);
                        if (view != null && seen.Add(data.m_uid))
                        {
                            yield return view;
                        }
                    }
                }
            }
        }

        // Revalidates one list entry and reports deletion without pretending the world has already saved.
        private void Delete(Terminal.ConsoleEventArgs args, CleanupSourceIndex source)
        {
            if (ZNet.instance.IsSaving())
            {
                throw new InvalidOperationException("Wait for the current world save to finish before deleting scenery.");
            }
            if (!ReferenceEquals(_world, ZNet.World) || !int.TryParse(args[2], NumberStyles.None,
                CultureInfo.InvariantCulture, out var index) || index < 1 || index > _listed.Count)
            {
                throw new ArgumentException("Run indestructible list in this world, then use one of its numbers.");
            }
            var selected = _listed[index - 1];
            if (selected.Removed)
            {
                Reply(args, "This entry was already deleted. Nothing else was changed.");
                return;
            }
            DeletionsPending();
            var id = selected.Id;
            var registryNote = selected.Delete(new CaptureApi(), source, Player.m_localPlayer.transform.position, _radius);
            _pendingWorld = ZNet.World;
            _pending.Add(id);
            _restore.SceneryChanged();
            Reply(args, "Removed " + selected.Name + " at " + selected.Position.ToString("F1") +
                ". The deletion will persist with the next world save; restore a world backup to undo it.");
            if (!string.IsNullOrEmpty(registryNote))
            {
                Reply(args, registryNote);
            }
        }

        // Lets F10 wait for native destruction before starting a checkpoint, without stopping restoration.
        internal bool DeletionsPending()
        {
            if (!ReferenceEquals(_pendingWorld, ZNet.World) || ZDOMan.instance == null)
            {
                _pending.Clear();
                return false;
            }
            _pending.RemoveWhere(id => ZDOMan.instance.GetZDO(id) == null);
            return _pending.Count != 0;
        }

        // Mirrors command results in the console and BepInEx log.
        private void Reply(Terminal.ConsoleEventArgs args, string message)
        {
            args.Context.AddString(message);
            _log.LogInfo("Indestructible: " + message);
        }

        // Invalidates old handlers and previews if the plugin unloads.
        public void Dispose()
        {
            _disposed = true;
            _listed.Clear();
            _world = null;
        }
    }
}
