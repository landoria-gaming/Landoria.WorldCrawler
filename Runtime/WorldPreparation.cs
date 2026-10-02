using System;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Restoration;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects the latest export automatically and asks for confirmation using Valheim's native dialog.
    internal sealed class WorldPreparation : IDisposable
    {
        private readonly RestoreSelection _restore;
        private readonly ManualLogSource _log;
        private Task<ExportArchive> _load;
        private ExportArchive _archive;
        private bool _confirming;
        private bool _disposed;
        public bool Busy => _load != null || _archive != null || _confirming;

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
                }
                if (!InMenu())
                {
                    Release();
                    return;
                }
                if (_archive != null && !_confirming && UnifiedPopup.IsAvailable() && !UnifiedPopup.IsVisible())
                {
                    Confirm();
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        // Shows the automatically selected world's identity before any native save is written.
        private void Confirm()
        {
            var selected = _archive;
            var world = selected.Manifest.World;
            NativeConfirmation.Show("World Crawler", $"Prepare or reuse local world '{world.Name}'?\n" +
                $"Seed: {world.SeedText}\nUID: {world.Uid}\nSaved zones: {_archive.Manifest.Zones.Count}\n\n" +
                "Existing unrelated worlds will not be overwritten. Back up your character: the same UID shares its map and saved positions.",
                accepted => { if (ReferenceEquals(selected, _archive)) { Answer(accepted); } });
            _confirming = true;
        }

        // Creates or reuses only the confirmed, still-locked archive while remaining at the main menu.
        private void Answer(bool accepted)
        {
            _confirming = false;
            if (_disposed || !accepted || !InMenu() || _archive == null)
            {
                Release();
                return;
            }
            try
            {
                var path = WorldCreation.Create(_archive);
                _restore.SourceDirectory = _archive.DirectoryPath;
                _log.LogInfo("Prepared native-format world: " + path);
                Release();
                NativeConfirmation.Report("World ready. Enter the local world, then press F10 to restore the saved areas.");
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
            _confirming = false;
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
