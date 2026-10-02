using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Landoria.WorldCrawler.Commands;
using Landoria.WorldCrawler.Protection;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.UI;
using Landoria.WorldCrawler.WorldPreparation;
using UnityEngine;

namespace Landoria.WorldCrawler
{
    // Loads guarded export, preparation and local-restoration workflows.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "Landoria.WorldCrawler";
        internal const string PluginName = "World Crawler";
        internal const string PluginVersion = "1.0.1";
        private CrawlController _controller;
        private Harmony _harmony;
        private WorldPreparationController _preparation;
        private RestorationController _restoration;
        private ExportMapOverlay _mapOverlay;
        private RecordingHud _recordingHud;
        private IndestructibleCommand _indestructible;
        private ConfigEntry<bool> _enableCheats;
        private readonly LocalDaylight _daylight = new LocalDaylight();

        // Validates the runtime version before installing passive recording and protection hooks.
        private void Awake()
        {
            GameContext.ValidateVersion();
            _enableCheats = Config.Bind("General", "EnableCheats", false,
                "Enables god mode, ghost mode, cold immunity, unlimited stamina, and Alt-click map teleportation.");
            Protection.CharacterMarkerPolicy.Initialize(Logger);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ApplySettings();
            _controller = new CrawlController(Logger);
            _preparation = new WorldPreparationController(Logger);
            _restoration = new RestorationController(Logger);
            _indestructible = new IndestructibleCommand(Logger, () => _controller.Busy || _preparation.Busy, _restoration);
            _restoration.SceneryDeletionsPending = _indestructible.DeletionsPending;
            _mapOverlay = new ExportMapOverlay(Logger, _controller, _restoration);
            _recordingHud = new RecordingHud(Logger);
            _controller.ZoneSaved += _recordingHud.AddReport;
            _restoration.ZoneRestored += _recordingHud.AddRestoreReport;
            Logger.LogInfo($"{PluginName} {PluginVersion} is loaded. F8: manual recording. F9: prepare world. F10: manual restoration.");
        }

        // Advances mutually exclusive world operations using fixed production shortcuts.
        private void Update()
        {
            if (_controller == null)
            {
                return;
            }
            ApplySettings();
            Protection.CharacterMarkerPolicy.Enforce(Game.instance == null ? null : Game.instance.GetPlayerProfile());
            _daylight.Update();
            _preparation.Update();
            HandleShortcut();
            _controller.Update();
            _restoration.Update();
            _recordingHud?.Update(_controller, _restoration);
        }

        // Applies the live BepInEx cheat setting to every optional player aid.
        private void ApplySettings()
        {
            Protection.PlayerProtection.Enabled = _enableCheats != null && _enableCheats.Value;
        }

        // Dispatches shortcuts and reports when an active operation blocks manual cleanup.
        private void HandleShortcut()
        {
            try
            {
                var action = ShortcutInput.Action();
                if (action == 0 && !_restoration.Busy && !_preparation.Busy)
                {
                    _controller.Toggle();
                }
                if (action == 1 && !_controller.Busy && !_restoration.Busy)
                {
                    _preparation.Start();
                }
                if (action == 2 && !_controller.Busy && !_preparation.Busy)
                {
                    _restoration.Toggle();
                }
            }
            catch (Exception error)
            {
                Logger.LogWarning(error.Message);
            }
        }

        // Follows the map viewport after Minimap has applied this frame's zoom and pan.
        private void LateUpdate()
        {
            _mapOverlay?.Update();
        }

        // Flushes received data, restores local lighting and removes only this plugin's hooks.
        private void OnDestroy()
        {
            Protection.PlayerProtection.Enabled = false;
            _indestructible?.Dispose();
            _daylight.Reset();
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
                    _recordingHud?.Dispose();
                }
                finally
                {
                    _harmony?.UnpatchSelf();
                }
            }
        }
    }
}
