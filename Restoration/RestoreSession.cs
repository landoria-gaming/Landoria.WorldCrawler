using System;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Owns the local target, source archive and zone-only progress for one import session.
    internal sealed class RestoreSession : IDisposable
    {
        private readonly Player _player;
        private Task<ExportArchive> _loading;
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
            get; private set;
        }
        public ExportArchive Archive
        {
            get; private set;
        }
        public Task ReleaseTask { get; private set; } = Task.CompletedTask;

        // Validates local ownership before looking up the source export by world UID.
        public RestoreSession()
        {
            LatestWorldApi.RequireCurrent();
            LegacyItemData.Validate();
            GeneratedObjectMatch.Validate();
            _player = Player.m_localPlayer;
            World = GameContext.Identity();
            Check();
            WorldDirectory = LatestWorldApi.DirectoryFor(ZNet.World);
            _loading = Task.Run(() => new ExportArchive(ExportSourceResolver.Resolve(World)));
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
            RestoreWorldIdentity.Require(World, Archive.Manifest.World);
            Journal = new RestoreJournal(World, Archive);
            return true;
        }

        // Releases a late worker result and returns a task callers can await before restarting.
        public void Dispose()
        {
            DisposeResources();
        }

        // Releases file-only resources after the operation has stopped touching the world.
        public void DisposeResources()
        {
            Archive?.Dispose();
            Archive = null;
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
                }, TaskScheduler.Default);
                _loading = null;
            }
        }
    }
}
