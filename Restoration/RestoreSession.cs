using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Owns the local target, read-only export and durable return point for one import session.
    internal sealed class RestoreSession : IDisposable
    {
        private readonly Player _player;
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
            _player = Player.m_localPlayer;
            World = GameContext.Identity();
            Check();
            if (string.IsNullOrWhiteSpace(restoration.SourceDirectory))
            {
                throw new InvalidOperationException("Select the source export in the world preparation menu first.");
            }
            WorldDirectory = LatestWorldApi.DirectoryFor(ZNet.World);
            _marker = AtomicJson.Read<PreparedWorld>(Path.Combine(WorldDirectory, PreparedWorld.FileName));
            _marker.Validate(World, _marker.ExportFingerprint);
            var character = Game.instance.GetPlayerProfile().GetPlayerID().ToString(CultureInfo.InvariantCulture);
            Journal = new RestoreJournal(CrawlerConstants.ExportRoot, _marker, character);
            try
            {
                if (Journal.State.ReturnPending)
                {
                    StartFlight(CrawlerConstants.Speed);
                }
                var source = restoration.SourceDirectory;
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
            if (!GameContext.Ready(Flight?.NativeTransitActive == true) || !GameContext.SameSession(World, _player) ||
                !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() || ZNet.instance.GetPeers().Count != 0 ||
                ZNet.World.m_fileSource != FileHelpers.FileSource.Local)
            {
                throw new InvalidOperationException("Restoration requires the same local world, alone, with a living test character.");
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

        // Saves the return checkpoint before any controlled travel or import mutation.
        public void StartFlight(float speed)
        {
            if (Flight != null && Flight.Active)
            {
                return;
            }
            Flight = new FlightController { Speed = speed };
            var state = Journal.State;
            if (state.ReturnPending)
            {
                Flight.Begin(_player, ObjectRestorer.Vector(state.ReturnPosition), ObjectRestorer.Rotation(state.ReturnRotation));
            }
            else
            {
                Flight.Begin(_player);
                state.ReturnPosition = Capture.CaptureTransform.Vector(Flight.Origin);
                state.ReturnRotation = Capture.CaptureTransform.Rotation(Flight.OriginRotation);
                state.ReturnPending = true;
            }
            state.Status = "restoring";
            state.Error = null;
            Journal.Save();
        }

        // Holds an airborne recovery while disk and preflight work complete.
        public void Hold(float deltaTime)
        {
            if (Flight != null && Flight.Active)
            {
                Flight.Tick(_player.transform.position, deltaTime);
            }
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
