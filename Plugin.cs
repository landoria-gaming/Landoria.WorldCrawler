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
        private WorldPreparation _preparation;
        private RestorationController _restoration;
        private ExportMapOverlay _mapOverlay;

        // Validates the runtime version before installing movement hooks.
        private void Awake()
        {
            GameContext.ValidateVersion();
            HudNotification.Initialize();
            Flight.CharacterMarkerPolicy.Initialize(Logger);
            var selection = new RestoreSelection();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            _controller = new CrawlController(Logger);
            _preparation = new WorldPreparation(selection, Logger);
            _restoration = new RestorationController(selection, Logger);
            _mapOverlay = new ExportMapOverlay(Logger, _controller, _restoration);
            Logger.LogInfo($"{PluginName} {PluginVersion} is loaded. F10: remaining zones at 4x sprint. LeftCtrl+F10: toggle manual import.");
        }

        // Advances mutually exclusive world operations using fixed production shortcuts.
        private void Update()
        {
            if (_controller == null)
            {
                return;
            }
            Flight.CharacterMarkerPolicy.Enforce(Game.instance == null ? null : Game.instance.GetPlayerProfile());
            Flight.FlightController.ReleaseStaleControl();
            _preparation.Update();
            HandleShortcut();
            _controller.Update();
            _restoration.Update();
        }

        // Dispatches shortcuts and reports when an active operation blocks manual cleanup.
        private void HandleShortcut()
        {
            try
            {
                var action = ShortcutInput.Action(Logger);
                if ((action == 0 || action == 4) && !_restoration.Busy && !_preparation.Busy)
                {
                    _controller.Toggle(action == 4);
                }
                if (action == 1 && !_controller.Busy && !_restoration.Busy)
                {
                    _preparation.Start();
                }
                if (action == 2 && !_controller.Busy && !_preparation.Busy)
                {
                    _restoration.Toggle();
                }
                HandleCurrentZoneShortcut(action);
            }
            catch (Exception error)
            {
                Logger.LogWarning(error.Message);
                HudNotification.Show(error.Message);
            }
        }

        // Routes manual reimport through the shared restoration journal and writer.
        private void HandleCurrentZoneShortcut(int action)
        {
            if (action != 3)
            {
                return;
            }
            if (_controller.Busy || _preparation.Busy)
            {
                throw new InvalidOperationException("Pause export or close world preparation before importing the current zone.");
            }
            Logger.LogInfo("LeftCtrl+F10 detected: toggling manual import.");
            _restoration.Toggle(true);
        }

        // Follows the map viewport after Minimap has applied this frame's zoom and pan.
        private void LateUpdate()
        {
            _mapOverlay?.Update();
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
