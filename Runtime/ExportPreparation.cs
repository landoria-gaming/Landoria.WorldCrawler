using System;
using System.IO;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Validates disk checkpoints without scanning maps, pins, portals, or remote zones.
    internal sealed class ExportPreparation : IDisposable
    {
        private readonly Task<WorldStore> _work;
        private bool _taken;
        public Task ReleaseTask { get; private set; } = Task.CompletedTask;

        // Copies the small identity record before moving disk work off the Unity thread.
        public ExportPreparation(WorldIdentity world)
        {
            var profile = Game.instance.GetPlayerProfile();
            var id = profile.GetPlayerID().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var name = profile.GetName();
            var version = GameContext.GameVersion;
            _work = Task.Run(() => Open(world, id, name, version));
        }

        // Opens only the recording folder; old landmark folders remain untouched.
        private static WorldStore Open(WorldIdentity world, string id, string name, string version)
        {
            var store = WorldStore.Open(CrawlerConstants.ExportRoot, world, false);
            try
            {
                store.PayloadValidator = ValidatePayload;
                store.BeginRecording(id, name, version);
                store.Reconcile();
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Checks decoded coordinates and counts, not only the outer file checksum.
        private static void ValidatePayload(int x, int z, byte[] payload, string version, int objects)
        {
            if (!SupportedGameVersions.CanExport(version))
            {
                throw new NotSupportedException("Unsupported capture source version: " + version);
            }
            var snapshot = ZoneSnapshot.Decode(payload);
            if (snapshot.ZoneX != x || snapshot.ZoneZ != z || snapshot.Objects.Count != objects)
            {
                throw new InvalidDataException("The zone payload disagrees with its file header.");
            }
        }

        // Transfers store ownership only after every disk check has finished.
        public bool TryTake(out WorldStore store)
        {
            store = null;
            if (!_work.IsCompleted)
            {
                return false;
            }
            _taken = true;
            store = _work.GetAwaiter().GetResult();
            return true;
        }

        // Releases a late result when recording stops during initialization.
        public void Dispose()
        {
            if (_taken)
            {
                return;
            }
            _taken = true;
            ReleaseTask = _work.ContinueWith(task =>
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
        }
    }
}
