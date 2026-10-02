using System;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects one shortcut while excluding text entry and configuration capture.
    internal static class ShortcutInput
    {
        // Selects a fixed shortcut without intercepting text input or another mod's editor.
        public static int Action(ManualLogSource log)
        {
            var restorePressed = ZInput.GetKeyDown(CrawlerConstants.RestoreKey);
            var currentZone = ZInput.GetKey(CrawlerConstants.CurrentZoneModifier);
            var alt = ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt);
            var other = alt || OtherModifierHeld();
            var index = restorePressed ? RestoreShortcutPolicy.Select(currentZone, other) :
                ZInput.GetKeyDown(CrawlerConstants.ExportKey) ? (other ? -1 : currentZone ? 4 : 0) :
                ZInput.GetKeyDown(CrawlerConstants.PrepareKey) ? 1 : -1;
            if (!restorePressed && index < 0)
            {
                return -1;
            }
            var blocked = BlockReason();
            if (restorePressed)
            {
                log.LogInfo($"F10 input: LeftControl={currentZone}; Alt={alt}; otherModifier={other}; action={index}; blockedBy={blocked ?? "none"}.");
            }
            if (blocked != null || index < 0)
            {
                return -1;
            }
            return index;
        }

        // Prevents unsupported modifier combinations from starting the ordinary restore route.
        private static bool OtherModifierHeld()
        {
            return ZInput.GetKey(KeyCode.RightControl) || ZInput.GetKey(KeyCode.LeftShift) ||
                ZInput.GetKey(KeyCode.RightShift) || ZInput.GetKey(KeyCode.LeftWindows) ||
                ZInput.GetKey(KeyCode.RightWindows) || ZInput.GetKey(KeyCode.LeftCommand) ||
                ZInput.GetKey(KeyCode.RightCommand);
        }

        // Names the interface currently consuming keyboard input for shortcut diagnostics.
        private static string BlockReason()
        {
            if (Console.IsVisible() || Chat.instance != null && Chat.instance.HasFocus())
            {
                return "console or chat";
            }
            if (UnifiedPopup.IsAvailable() && UnifiedPopup.IsVisible())
            {
                return "game dialog";
            }
            if (GUIUtility.keyboardControl != 0)
            {
                return "GUI keyboard focus";
            }
            return ConfigurationVisible() ? "configuration window" : null;
        }

        // Respects the optional BepInEx configuration editor without taking a runtime dependency.
        private static bool ConfigurationVisible()
        {
            foreach (var plugin in Chainloader.PluginInfos.Values)
            {
                if (plugin.Metadata.GUID.IndexOf("configurationmanager", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                var property = plugin.Instance.GetType().GetProperty("DisplayingWindow");
                if (property?.PropertyType == typeof(bool) && (bool)property.GetValue(plugin.Instance))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
