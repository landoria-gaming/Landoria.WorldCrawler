using System;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.UI;

namespace Landoria.WorldCrawler.WorldPreparation
{
    // Selects the latest export and attaches a user-created local world without presenting a choice.
    internal sealed class WorldPreparationController : IDisposable
    {
        private readonly ManualLogSource _log;
        private Task<ExportArchive> _load;
        private ExportArchive _archive;
        private Action _closeWorking;
        private bool _disposed;
        public bool Busy => _load != null || _archive != null || _closeWorking != null;

        // Reports preparation independently of the UID-based restoration lookup.
        public WorldPreparationController(ManualLogSource log)
        {
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

        // Matches an existing local world and reports its UID preparation in an OK-only dialog.
        private void Prepare()
        {
            if (_disposed || !InMenu() || _archive == null)
            {
                Release();
                return;
            }
            try
            {
                var message = LocalWorldPreparation.Prepare(_archive);
                _log.LogInfo("F9: " + message.Replace('\n', ' '));
                Release();
                NativeConfirmation.Report(message);
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
