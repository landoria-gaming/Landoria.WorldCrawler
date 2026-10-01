using System;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Storage;
using Landoria.WorldCrawler.Capture;
using System.IO;

namespace Landoria.WorldCrawler.Runtime
{
    // Moves disk validation and landmark indexing off the Unity main thread.
    internal sealed class ExportPreparation : IDisposable
    {
        private Task<WorldStore> _work;
        private WorldStore _waiting;
        private ExportSelection _selection;
        private LandmarkPortalResolver _portals;
        private string _characterId;
        private string _characterName;
        private bool _taken;
        public Task ReleaseTask { get; private set; } = Task.CompletedTask;

        // Copies game data now and schedules work that never calls Unity APIs.
        public ExportPreparation(WorldIdentity world, Action<WorldStore> acquired)
        {
            var profile = Game.instance.GetPlayerProfile();
            var id = profile.GetPlayerID().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var name = profile.GetName();
            _selection = new ExportSelection(world);
            var store = WorldStore.Open(CrawlerConstants.ExportRoot, world, false, _selection.Scope);
            try
            {
                store.PayloadValidator = ValidatePayload;
                if (store.Manifest.InventoryInitialized)
                {
                    store.ValidateCharacter(id);
                }
                acquired(store);
                _characterId = id;
                _characterName = name;
                _portals = new LandmarkPortalResolver();
                _waiting = store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Checks decoded source coordinates and counts instead of trusting an outer checksum alone.
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

        // Opens and checks progress before applying the current landmark selection.
        private static WorldStore Prepare(WorldStore store, ExportSelection selection, string id, string name)
        {
            try
            {
                selection.Apply(store, id, name);
                store.Reconcile();
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Transfers exclusive store ownership to the main-thread crawl controller.
        public bool TryTake(out WorldStore store)
        {
            store = null;
            if (_work == null)
            {
                if (!_portals.Step())
                {
                    return false;
                }
                _selection.ReadLandmarks(_portals);
                var pending = _waiting;
                _work = Task.Run(() => Prepare(pending, _selection, _characterId, _characterName));
                _waiting = null;
            }
            if (!_work.IsCompleted)
            {
                return false;
            }
            _taken = true;
            store = _work.GetAwaiter().GetResult();
            return true;
        }

        // Releases a late result if the player logs out while preparation is running.
        public void Dispose()
        {
            if (_taken)
            {
                return;
            }
            _taken = true;
            if (_work == null)
            {
                _waiting?.Dispose();
                _waiting = null;
                return;
            }
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
            }
, TaskScheduler.Default);
        }
    }
}
