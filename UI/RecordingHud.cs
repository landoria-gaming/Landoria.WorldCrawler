using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Landoria.WorldCrawler.UI
{
    // Shows operation status, recording counters and a scrollable history of committed prefabs.
    internal sealed partial class RecordingHud : IDisposable
    {
        private const int HistoryLimit = 2000;
        private readonly ManualLogSource _log;
        private readonly List<string> _lines = new List<string>();
        private readonly EnglishPrefabNames _names = new EnglishPrefabNames();
        private Hud _hud;
        private Minimap _minimap;
        private GameObject _panel;
        private TextMeshProUGUI _history, _stats, _status, _title;
        private RectTransform _content;
        private ScrollRect _scroll;
        private float _nextStats;
        private bool _warned, _dirty;

        // Receives a logger so presentation failures never stop recording.
        internal RecordingHud(ManualLogSource log)
        {
            _log = log;
        }

        // Records only new source IDs after their zone file was committed.
        internal void AddReport(ZoneSaveReport report)
        {
            try
            {
                AppendReport(report);
            }
            catch (Exception error)
            {
                Warn(error);
            }
        }

        // Adds one successfully applied restoration zone to the same scrollable journal.
        internal void AddRestoreReport(ZoneSaveReport report)
        {
            try
            {
                AppendReport(report, "Applied zone ");
            }
            catch (Exception error)
            {
                Warn(error);
            }
        }

        // Appends only newly committed prefab types with one timestamp per zone.
        private void AppendReport(ZoneSaveReport report)
        {
            AppendReport(report, "Zone ");
        }

        // Appends one zone summary using the operation-specific line prefix.
        private void AppendReport(ZoneSaveReport report, string prefix)
        {
            if (report.Added.Count == 0)
            {
                return;
            }
            _lines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + prefix + report.X + ":" + report.Z);
            foreach (var item in report.Added)
            {
                report.Categories.TryGetValue(item.Name, out var category);
                report.PrefabHashes.TryGetValue(item.Name, out var hash);
                var english = _names.Resolve(hash, item.Name);
                _lines.Add("  +" + item.Count + " " + item.Name +
                    (english == null ? "" : " - " + english) +
                    " (" + (string.IsNullOrEmpty(category) ? "Other" : category) + ")");
            }
            if (_lines.Count > HistoryLimit)
            {
                _lines.RemoveRange(0, _lines.Count - HistoryLimit);
            }
            _dirty = true;
        }

        // Keeps the journal visible in game and updates counters once per second.
        internal void Update(CrawlController controller, RestorationController restoration)
        {
            try
            {
                UpdateCore(controller, restoration);
                _warned = false;
            }
            catch (Exception error)
            {
                Hide();
                Warn(error);
            }
        }

        // Attaches to the live native HUD and refreshes counters once per second.
        private void UpdateCore(CrawlController controller, RestorationController restoration)
        {
            if (Player.m_localPlayer == null || Hud.instance == null || Minimap.instance == null ||
                Minimap.instance.m_biomeNameSmall == null || Minimap.instance.m_mapImageSmall == null)
            {
                Hide();
                return;
            }
            Ensure(Hud.instance, Minimap.instance);
            if (_dirty)
            {
                UpdateHistory();
                _dirty = false;
            }
            _panel.SetActive(_lines.Count > 0);
            var recording = controller.Recording;
            var restorationStatus = restoration.HudStatus;
            var restoring = restorationStatus != null;
            _stats.gameObject.SetActive(recording || restoring);
            _status.gameObject.SetActive(recording || restorationStatus != null);
            _status.text = recording ? RecordingStatus(controller) : restorationStatus ?? string.Empty;
            _title.text = restoring ? "Restored prefabs" : "Saved prefabs";
            PositionStatus();
            if ((recording || restoring) && Time.realtimeSinceStartup >= _nextStats)
            {
                _stats.text = recording ? RecordingStats(controller) : RestorationStats(restoration);
                _nextStats = Time.realtimeSinceStartup + 1f;
            }
        }

        // Selects the current recording label without affecting restoration state.
        private static string RecordingStatus(CrawlController controller)
        {
            return controller.Stopping ? "Finishing recording..." : controller.TeleportPaused ?
                "Capture paused: teleporting..." : "Recording...";
        }

        // Formats the three recording counters shown across the top of the screen.
        private static string RecordingStats(CrawlController controller)
        {
            controller.GetStats(out var zones, out var saved, out var cached);
            return "Zones saved: " + zones + "    Objects saved: " + saved +
                "    Objects cached: " + cached;
        }

        // Formats durable restore progress separately from changes awaiting a native save.
        private static string RestorationStats(RestorationController restoration)
        {
            restoration.GetHudStats(out var saved, out var total, out var pending, out var objects);
            return "Zones saved: " + saved + "/" + total + "    Zones awaiting save: " + pending +
                "    Objects restored: " + objects;
        }

        // Reports one UI failure without repeatedly filling the BepInEx log.
        private void Warn(Exception error)
        {
            if (_warned)
            {
                return;
            }
            _warned = true;
            _log.LogWarning("Recording display unavailable: " + error);
        }

        // Recreates owned UI only when the native HUD or minimap was replaced.
        private void Ensure(Hud hud, Minimap minimap)
        {
            if (_panel != null && _hud == hud && _minimap == minimap && _status != null &&
                _history != null && _title != null && _scroll != null &&
                _scroll.viewport != null && _content != null)
            {
                return;
            }
            Dispose();
            _hud = hud;
            _minimap = minimap;
            CreatePanel(minimap);
            CreateStats(minimap);
            CreateStatus(minimap);
            UpdateHistory();
            _dirty = false;
        }

        // Hides transient indicators without losing recorded journal lines.
        private void Hide()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
            if (_stats != null)
            {
                _stats.gameObject.SetActive(false);
            }
            if (_status != null)
            {
                _status.gameObject.SetActive(false);
            }
        }

        // Removes only UI created by this mod.
        public void Dispose()
        {
            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel);
            }
            if (_stats != null)
            {
                UnityEngine.Object.Destroy(_stats.gameObject);
            }
            if (_status != null)
            {
                UnityEngine.Object.Destroy(_status.gameObject);
            }
            _panel = null;
            _history = null;
            _stats = null;
            _status = null;
            _title = null;
            _scroll = null;
            _content = null;
        }
    }
}
