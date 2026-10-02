using System;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Restoration;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects the latest export and creates a fresh local world without presenting a choice.
    internal sealed class WorldPreparation : IDisposable
    {
        private readonly RestoreSelection _restore;
        private readonly ManualLogSource _log;
        private Task<ExportArchive> _load;
        private ExportArchive _archive;
        private Action _closeWorking;
        private bool _disposed;
        public bool Busy => _load != null || _archive != null || _closeWorking != null;

        // Shares only successfully confirmed exports with the restoration workflow.
        public WorldPreparation(RestoreSelection restore, ManualLogSource log)
        {
            _restore = restore;
            _log = log;
        }

        // Starts one asynchronous validation from the main menu without a custom window.
        public void Start()
        {
            LatestWorldApi.RequireCurrent();
            if (!InMenu())
            {
                throw new InvalidOperationException("Prepare the world from the main menu.");
            }
            if (Busy)
            {
                return;
            }
            if (!UnifiedPopup.IsAvailable() || UnifiedPopup.IsVisible())
            {
                throw new InvalidOperationException("Close the current game dialog, then press F9.");
            }
            _closeWorking = NativeConfirmation.ShowWorking(
                "Preparing local world...\n\nChecking the latest export and existing worlds. Please wait.");
            _load = Task.Run(() => new ExportArchive(LatestExport.Find(CrawlerConstants.ExportRoot)));
            _log.LogInfo("F9: selecting and verifying the latest saved export...");
        }

        // Takes validation results on the game thread and waits for the native popup to be available.
        public void Update()
        {
            try
            {
                if (_load?.IsCompleted == true)
                {
                    var load = _load;
                    _load = null;
                    _archive = load.GetAwaiter().GetResult();
                    _log.LogInfo("F9 selected latest export: " + _archive.DirectoryPath);
                    Prepare();
                }
                if (!InMenu())
                {
                    Release();
                    return;
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Creates a fresh world immediately after export validation and collision checks.
        private void Prepare()
        {
            if (_disposed || !InMenu() || _archive == null)
            {
                Release();
                return;
            }
            try
            {
                var path = WorldCreation.Create(_archive);
                _restore.SourceDirectory = _archive.DirectoryPath;
                _log.LogInfo("Prepared native-format world: " + path);
                var world = _archive.Manifest.World.Copy();
                Release();
                NativeConfirmation.Report($"Local world '{world.Name}' was created.\n\n" +
                    $"UID: {world.Uid}\nSeed: {world.SeedText}\n\n" +
                    "Enter the new world, then press F10 to restore the saved areas.");
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Rejects confirmations after entering a world or leaving the main-menu scene.
        private static bool InMenu()
        {
            return Player.m_localPlayer == null && FejdStartup.instance != null;
        }

        // Releases the read-only archive without changing export data or a previously selected world.
        private void Release()
        {
            _archive?.Dispose();
            _archive = null;
            _closeWorking?.Invoke();
            _closeWorking = null;
        }

        // Keeps detailed diagnostics in the log and uses a short native error dialog.
        private void Fail(Exception error)
        {
            Release();
            _log.LogError("World preparation failed: " + error);
            if (InMenu())
            {
                NativeConfirmation.Report(error.Message);
            }
        }

        // Releases a late validation result even when the plugin unloads before the task finishes.
        public void Dispose()
        {
            _disposed = true;
            Release();
            _load?.ContinueWith(task =>
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
            _load = null;
        }
    }
}
