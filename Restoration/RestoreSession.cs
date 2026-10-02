using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Owns the local target, read-only export and journal for one import session.
    internal sealed class RestoreSession : IDisposable
    {
        private readonly Player _player;
        private CharacterCheatMarker _characterMarker;
        private Task<ExportArchive> _loading;
        private readonly PreparedWorld _marker;
        public WorldIdentity World
        {
            get;
        }
        public string WorldDirectory
        {
            get;
        }
        public RestoreJournal Journal
        {
            get;
        }
        public ExportArchive Archive
        {
            get; private set;
        }
        public FlightController Flight
        {
            get; private set;
        }
        public Task ReleaseTask { get; private set; } = Task.CompletedTask;

        // Validates local ownership before opening a target-bound recovery journal.
        public RestoreSession(RestoreSelection restoration)
        {
            LatestWorldApi.RequireCurrent();
            LegacyItemData.Validate();
            GeneratedObjectMatch.Validate();
            _player = Player.m_localPlayer;
            World = GameContext.Identity();
            Check();
            WorldDirectory = LatestWorldApi.DirectoryFor(ZNet.World);
            _marker = AtomicJson.Read<PreparedWorld>(Path.Combine(WorldDirectory, PreparedWorld.FileName));
            _marker.Validate(World, _marker.ExportFingerprint);
            var source = restoration.SourceDirectory;
            if (string.IsNullOrWhiteSpace(source))
            {
                source = ExportSourceResolver.Resolve(_marker);
                restoration.SourceDirectory = source;
            }
            var character = Game.instance.GetPlayerProfile().GetPlayerID().ToString(CultureInfo.InvariantCulture);
            Journal = new RestoreJournal(CrawlerConstants.ExportRoot, _marker, character);
            try
            {
                _loading = Task.Run(() => new ExportArchive(source));
            }
            catch
            {
                try
                {
                    Flight?.Abort();
                }
                finally
                {
                    Journal.Dispose();
                }
                throw;
            }
        }

        // Refuses remote servers, other players, cloud saves and session changes on every frame.
        public void Check()
        {
            if (!GameContext.Ready(true) || !GameContext.SameSession(World, _player) ||
                !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() || ZNet.instance.GetPeers().Count != 0 ||
                ZNet.World.m_fileSource != LatestWorldApi.LocalSource)
            {
                throw new InvalidOperationException("Restoration requires the same local world, alone, with a living character.");
            }
        }

        // Takes background validation results only after the whole export passed its integrity checks.
        public bool Prepare()
        {
            if (!_loading.IsCompleted)
            {
                return false;
            }
            var load = _loading;
            _loading = null;
            Archive = load.GetAwaiter().GetResult();
            if (!Archive.Manifest.World.Matches(World))
            {
                throw new InvalidOperationException("The source world identity differs.");
            }
            Journal.AcceptArchive(Archive);
            return true;
        }

        // Preserves the active character before resetting its requested cheat marker.
        public void PrepareCharacter(Action<string> log)
        {
            _characterMarker = new CharacterCheatMarker(System.IO.Path.Combine(Journal.DirectoryPath, "characters"), log);
            _characterMarker.Begin();
        }

        // Clears only the bound character flag when a restoration session ends normally.
        public void FinishCharacter()
        {
            _characterMarker?.Clear();
        }

        // Starts protected travel from the current position, never a saved origin.
        public void StartFlight(float speed)
        {
            if (Flight != null && Flight.Active)
            {
                return;
            }
            Flight = new FlightController { Speed = speed };
            Flight.Begin(_player, _player.transform.position, _player.transform.rotation);
            Journal.State.Status = "restoring";
            Journal.State.Error = null;
            Journal.Save();
        }

        // Retains recoverable progress after failures without claiming an emergency return succeeded.
        public void Fault(string message)
        {
            try
            {
                Flight?.Abort();
            }
            finally
            {
                Journal.State.Status = "faulted";
                Journal.State.Error = message;
                Journal.Save();
            }
        }

        // Releases a late worker result and returns a task callers can await before restarting.
        public void Dispose()
        {
            try
            {
                Flight?.Abort();
            }
            finally
            {
                DisposeResources();
            }
        }

        // Releases file-only resources after the main thread has handled controlled flight.
        public void DisposeResources()
        {
            Archive?.Dispose();
            Archive = null;
            Journal.Dispose();
            if (_loading != null)
            {
                ReleaseTask = _loading.ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result.Dispose();
                    }
                    else if (task.IsFaulted)
                    {
                        var observed = task.Exception;
                    }
                }
, TaskScheduler.Default);
                _loading = null;
            }
        }
    }
}
