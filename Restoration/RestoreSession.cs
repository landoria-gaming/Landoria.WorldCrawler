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
            World = ZNet.World == null ? null : GameContext.Identity();
            Check();
            WorldDirectory = LatestWorldApi.DirectoryFor(ZNet.World);
            _loading = Task.Run(() => new ExportArchive(ExportSourceResolver.Resolve(World)));
        }

        // Reports the exact failed requirement instead of a combined local-world error.
        public void Check()
        {
            CheckConnection();
            CheckLocalWorld();
            CheckPlayer();
            if (!GameContext.SameSession(World, _player))
            {
                throw new InvalidOperationException("The world or character changed during restoration. " +
                    "Press F10 again in the prepared local world.");
            }
        }

        // Distinguishes an unloaded world from a lost or unfinished connection.
        private static void CheckConnection()
        {
            if (ZNet.instance == null || ZNet.World == null || Game.instance == null)
            {
                throw new InvalidOperationException("No world is loaded. Enter the local world prepared with F9, then press F10.");
            }
            var status = ZNet.GetConnectionStatus();
            if (status != ZNet.ConnectionStatus.Connected)
            {
                throw new InvalidOperationException("The world connection is not ready (" + status +
                    "). Wait until you are connected before pressing F10.");
            }
        }

        // Explains storage and hosting restrictions before any restoration touches the world.
        private static void CheckLocalWorld()
        {
            if (ZNet.instance.IsDedicated() || !ZNet.instance.IsServer())
            {
                throw new InvalidOperationException("F10 cannot restore a remote or dedicated server. " +
                    "Enter your own local world from the main menu.");
            }
            var source = ZNet.World.m_fileSource;
            if (source != LatestWorldApi.LocalSource)
            {
                var storage = source.ToString().IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) >= 0 ?
                    "in the cloud" : "with non-local storage (" + source + ")";
                throw new InvalidOperationException("World '" + ZNet.World.m_name + "' is saved " + storage +
                    ". F10 requires a LOCAL save, even in single-player.\n\n" +
                    "Return to the main menu > Manage saves > Worlds > Move to local. " +
                    "Then press F9, enter that local world, and press F10.");
            }
            var peers = ZNet.instance.GetPeers().Count;
            if (peers != 0)
            {
                throw new InvalidOperationException("F10 requires you to be alone. " + peers +
                    " other connection(s) are present. Disconnect other players before restoring.");
            }
        }

        // Gives separate recovery instructions for spawning, death and unfinished scene loading.
        private static void CheckPlayer()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("Your character has not spawned yet. Wait until you can move, then press F10.");
            }
            if (player.IsDead())
            {
                throw new InvalidOperationException("Your character is dead. Respawn before pressing F10.");
            }
            if (player.InCutscene() && !player.IsTeleporting())
            {
                throw new InvalidOperationException("Your character is in an arrival or respawn sequence. " +
                    "Wait until it finishes, then press F10.");
            }
            if (ZDOMan.instance == null || ZNetScene.instance == null)
            {
                throw new InvalidOperationException("World objects are still loading. Wait until the world is ready, then press F10.");
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
