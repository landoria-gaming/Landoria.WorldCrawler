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
        private CrawlController _controller;
        private Harmony _harmony;
        private PreparationWindow _preparation;
        private RestorationController _restoration;
        private ExportMapOverlay _mapOverlay;

        // Validates the runtime version before installing movement hooks.
        private void Awake()
        {
            GameContext.ValidateVersion();
            HudNotification.Initialize();
            var selection = new RestoreSelection();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            _controller = new CrawlController(Logger);
            _preparation = new PreparationWindow(selection, Logger);
            _restoration = new RestorationController(selection, Logger);
            _mapOverlay = new ExportMapOverlay(Logger);
            Logger.LogInfo($"{PluginName} {PluginVersion} is loaded.");
        }

        // Advances mutually exclusive world operations using fixed production shortcuts.
        private void Update()
        {
            if (_controller == null)
            {
                return;
            }
            Flight.FlightController.ReleaseStaleControl();
            _preparation.Update();
            try
            {
                var action = ShortcutInput.Action();
                if (action == 0 && !_restoration.Busy && !_preparation.Visible)
                {
                    _controller.Toggle();
                }
                if (action == 1 && !_controller.Busy && !_restoration.Busy)
                {
                    _preparation.Toggle();
                }
                if (action == 2 && !_controller.Busy && !_preparation.Visible)
                {
                    _restoration.Toggle();
                }
            }
            catch (Exception error)
            {
                Logger.LogWarning(error.Message);
                HudNotification.Show(error.Message);
            }
            _controller.Update();
            _restoration.Update();
        }

        // Follows the map viewport after Minimap has applied this frame's zoom and pan.
        private void LateUpdate()
        {
            _mapOverlay?.Update();
        }

        // Displays preparation selection and confirmation only in the supported main menu.
        private void OnGUI()
        {
            _preparation?.Draw();
        }

        // Restores controlled physics and removes only this plugin's Harmony patches.
        private void OnDestroy()
        {
            try
            {
                _controller?.Dispose();
                _restoration?.Dispose();
                _preparation?.Dispose();
            }
            finally
            {
                try
                {
                    _mapOverlay?.Dispose();
                }
                finally
                {
                    _harmony?.UnpatchSelf();
                }
            }
        }
    }
}
