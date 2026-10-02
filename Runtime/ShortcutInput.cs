using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects one shortcut while excluding text entry and configuration capture.
    internal static class ShortcutInput
    {
        // Selects a fixed shortcut without intercepting text input or another mod's editor.
        public static int Action()
        {
            if (OtherModifierHeld() || ZInput.GetKey(KeyCode.LeftControl) ||
                ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt) || BlockReason() != null)
            {
                return -1;
            }
            var index = ZInput.GetKeyDown(CrawlerConstants.ExportKey) ? 0 :
                ZInput.GetKeyDown(CrawlerConstants.PrepareKey) ? 1 :
                ZInput.GetKeyDown(CrawlerConstants.RestoreKey) ? 2 : -1;
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
