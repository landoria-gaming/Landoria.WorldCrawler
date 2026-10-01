using System;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Reloads file edits on the Unity thread while configuration-manager changes apply directly.
    internal sealed class ConfigReload
    {
        private readonly ConfigFile _config;
        private readonly ManualLogSource _log;
        private DateTime _stamp;
        private float _next;

        // Watches the active profile's configuration rather than a fixed installation path.
        public ConfigReload(ConfigFile config, ManualLogSource log)
        {
            _config = config;
            _log = log;
            _stamp = File.GetLastWriteTimeUtc(config.ConfigFilePath);
        }

        // Applies completed file edits without restarting or using Unity from another thread.
        public void Update()
        {
            if (Time.unscaledTime < _next) { return; }
            _next = Time.unscaledTime + 1f;
            try
            {
                if (!File.Exists(_config.ConfigFilePath)) { return; }
                var stamp = File.GetLastWriteTimeUtc(_config.ConfigFilePath);
                if (stamp == _stamp) { return; }
                _config.Reload();
                _stamp = stamp;
                _log.LogInfo("World Crawler configuration reloaded.");
            }
            catch (Exception error) { _log.LogWarning("Configuration reload failed: " + error.Message); }
        }
    }
}
