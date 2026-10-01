using BepInEx;
using System;
using HarmonyLib;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Restoration;
using UnityEngine;

namespace Landoria.WorldCrawler
{
    // Loads guarded export, preparation and local-restoration workflows.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "Landoria.WorldCrawler";
        internal const string PluginName = "World Crawler";
        internal const string PluginVersion = "1.0.0";
        private CrawlerSettings _settings;
        private CrawlController _controller;
        private Harmony _harmony;
        private ShortcutSettings _shortcuts;
        private ConfigReload _reload;
        private PreparationWindow _preparation;
        private RestorationController _restoration;
        private ExportMapOverlay _mapOverlay;

        // Validates the runtime version before installing movement hooks.
        private void Awake()
        {
            GameContext.ValidateVersion();
            HudNotification.Initialize();
            _settings = new CrawlerSettings(Config);
            _shortcuts = new ShortcutSettings(Config);
            var restoreSettings = new RestorationSettings(Config);
            _reload = new ConfigReload(Config, Logger);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            _controller = new CrawlController(_settings, Logger);
            _preparation = new PreparationWindow(_settings, restoreSettings, Logger);
            _restoration = new RestorationController(_settings, restoreSettings, Logger);
            _mapOverlay = new ExportMapOverlay(_settings, Logger);
            Logger.LogInfo($"{PluginName} {PluginVersion} is loaded.");
        }

        // Applies live configuration and advances only mutually exclusive world operations.
        private void Update()
        {
            if (_controller == null) { return; }
            Flight.FlightController.ReleaseStaleControl();
            _reload.Update();
            _preparation.Update();
            try
            {
                var action = ShortcutInput.Action(_shortcuts);
                if (action == 0 && !_restoration.Busy && !_preparation.Visible) { _controller.Toggle(); }
                if (action == 1 && !_controller.Busy && !_restoration.Busy) { _preparation.Toggle(); }
                if (action == 2 && !_controller.Busy && !_preparation.Visible) { _restoration.Toggle(); }
            }
            catch (Exception error) { Logger.LogWarning(error.Message); HudNotification.Show(error.Message); }
            _controller.Update();
            _restoration.Update();
        }

        // Follows the map viewport after Minimap has applied this frame's zoom and pan.
        private void LateUpdate() { _mapOverlay?.Update(); }

        // Displays preparation selection and confirmation only in the supported main menu.
        private void OnGUI() { _preparation?.Draw(); }

        // Restores controlled physics and removes only this plugin's Harmony patches.
        private void OnDestroy()
        {
            try { _controller?.Dispose(); _restoration?.Dispose(); _preparation?.Dispose(); }
            finally
            {
                try { _mapOverlay?.Dispose(); }
                finally { _harmony?.UnpatchSelf(); }
            }
        }
    }
}
