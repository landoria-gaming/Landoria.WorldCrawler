using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects one shortcut while excluding text entry, configuration capture and Tomrer conflicts.
    internal static class ShortcutInput
    {
        // Selects a fixed shortcut without intercepting text input or another mod's editor.
        public static int Action()
        {
            if (GUIUtility.keyboardControl != 0 || Console.IsVisible() ||
                Chat.instance != null && Chat.instance.HasFocus() || ConfigurationVisible())
            {
                return -1;
            }
            var index = Input.GetKeyDown(CrawlerConstants.ExportKey) ? 0 :
                Input.GetKeyDown(CrawlerConstants.PrepareKey) ? 1 :
                Input.GetKeyDown(CrawlerConstants.RestoreKey) ? 2 : -1;
            if (index < 0)
            {
                return -1;
            }
            CheckTomrer();
            return index;
        }

        // Blocks keys still used by the reference recorder, including modified-key variants.
        private static void CheckTomrer()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.mikamarik.valheimtomrer"))
            {
                throw new InvalidOperationException("Disable ValheimTomrer before using World Crawler: F8/F9/F10 conflict.");
            }
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
