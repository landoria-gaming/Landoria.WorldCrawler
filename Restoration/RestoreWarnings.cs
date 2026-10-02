using System;
using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Persistence;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps import review messages in both the persistent journal and the BepInEx warning log.
    internal sealed class RestoreWarnings
    {
        private readonly List<string> _messages;
        private readonly Action<string> _log;

        // Shares the journal list without replaying all previous warnings on every resume.
        internal RestoreWarnings(List<string> messages, Action<string> log)
        {
            _messages = messages;
            _log = log;
        }

        // Emits each distinct warning once while retaining it for the next journal checkpoint.
        internal void Add(string message)
        {
            var warning = SingleLine(message);
            if (string.IsNullOrWhiteSpace(warning) || _messages.Contains(warning))
            {
                return;
            }
            _messages.Add(warning);
            _log("Restoration warning: " + warning);
        }

        // Identifies a source record independently of the currently visited target zone.
        internal static string Describe(CapturedObject source)
        {
            return "zone=" + source.ZoneX + ":" + source.ZoneZ + "; prefab=" + SingleLine(source.PrefabName) +
                "; source=" + ExportArchive.Key(source);
        }

        // Prevents user-supplied names from inserting misleading extra log lines.
        private static string SingleLine(string value)
        {
            return value?.Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
