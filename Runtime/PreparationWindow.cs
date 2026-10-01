using System;
using System.IO;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Restoration;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Presents an explicit export selection and creation confirmation from the main menu.
    internal sealed class PreparationWindow : IDisposable
    {
        private readonly CrawlerSettings _settings;
        private readonly RestorationSettings _restore;
        private readonly ManualLogSource _log;
        private string[] _directories = Array.Empty<string>();
        private Task<ExportArchive> _load;
        private ExportArchive _archive;
        private string _message = "Select an export with at least one saved zone. Unfinished zones will be skipped.";
        private Vector2 _scroll;
        public bool Visible { get; private set; }

        // Retains configuration objects so changed shortcut and source values take effect live.
        public PreparationWindow(CrawlerSettings settings, RestorationSettings restore, ManualLogSource log)
        { _settings = settings; _restore = restore; _log = log; }

        // Opens only on the supported menu and never starts creation from a shortcut alone.
        public void Toggle()
        {
            LatestWorldApi.RequireCurrent();
            if (Player.m_localPlayer != null || FejdStartup.instance == null)
            { throw new InvalidOperationException("Prepare the world from the main menu."); }
            Visible = !Visible;
            if (!Visible) { return; }
            _directories = Directory.Exists(_settings.ExportRoot.Value)
                ? Directory.GetDirectories(_settings.ExportRoot.Value, "world_*") : Array.Empty<string>();
        }

        // Takes background validation results on the Unity thread.
        public void Update()
        {
            if (Visible && (FejdStartup.instance == null || Player.m_localPlayer != null))
            { Visible = false; _archive?.Dispose(); _archive = null; }
            if (_load == null || !_load.IsCompleted) { return; }
            var load = _load;
            _load = null;
            try
            {
                _archive = load.GetAwaiter().GetResult(); _message = "Export validated. Ready for confirmation.";
                if (!Visible) { _archive.Dispose(); _archive = null; }
            }
            catch (Exception error) { _message = error.Message; _log.LogError(error); }
        }

        // Draws a small modal-like menu without entering or selecting any game world.
        public void Draw()
        {
            if (!Visible || FejdStartup.instance == null || Player.m_localPlayer != null) { return; }
            GUILayout.BeginArea(new Rect(40, 60, Math.Min(780, Screen.width - 80), Math.Min(650, Screen.height - 120)), GUI.skin.box);
            GUILayout.Label("World Crawler - prepare a local world (current Valheim save format)");
            GUILayout.Label(_message);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(180));
            foreach (var directory in _directories)
            {
                if (GUILayout.Button(Path.GetFileName(directory)) && _load == null)
                {
                    _archive?.Dispose(); _archive = null;
                    _restore.Source.Value = directory;
                    _load = Task.Run(() => new ExportArchive(directory));
                    _message = "Verifying saved zone files...";
                }
            }
            GUILayout.EndScrollView();
            DrawConfirmation();
            if (GUILayout.Button("Close")) { Visible = false; _archive?.Dispose(); _archive = null; }
            GUILayout.EndArea();
        }

        // Explains same-UID character risks before allowing a non-overwriting local creation.
        private void DrawConfirmation()
        {
            if (_archive == null) { return; }
            var world = _archive.Manifest.World;
            GUILayout.Label($"World: {world.Name}\nSeed: {world.SeedText}\nUID: {world.Uid}\nSaved zones: {_archive.Manifest.Zones.Count}/{_archive.PlannedZoneCount}");
            GUILayout.Label("Base world only. Use a test character for restoration.\n" +
                "The same UID shares the source character's map, bed and positions. Back up your character.\n" +
                "Existing worlds will not be replaced. No character will be connected automatically.");
            if (!GUILayout.Button("Prepare or reuse local world")) { return; }
            try
            {
                var path = WorldCreation.Create(_archive);
                _message = "World ready and identity verified. Join with a test character to restore saved zones.";
                _log.LogInfo("Prepared native-format world: " + path);
                _archive.Dispose(); _archive = null;
            }
            catch (Exception error) { _message = error.Message; _log.LogError(error); }
        }

        // Releases an archive even if validation finishes after the plugin unloads.
        public void Dispose()
        {
            _archive?.Dispose(); _archive = null;
            _load?.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) { task.Result.Dispose(); }
                else if (task.IsFaulted) { var observed = task.Exception; }
            }, TaskScheduler.Default);
            _load = null;
        }
    }
}
