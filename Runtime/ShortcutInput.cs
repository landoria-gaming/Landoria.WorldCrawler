using System;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Selects one shortcut while excluding text entry, configuration capture and Tomrer conflicts.
    internal static class ShortcutInput
    {
        // Gives modifier combinations priority and rejects two indistinguishable actions.
        public static int Action(ShortcutSettings settings)
        {
            if (GUIUtility.keyboardControl != 0 || Console.IsVisible() ||
                Chat.instance != null && Chat.instance.HasFocus() || ConfigurationVisible()) { return -1; }
            var bindings = new[] { settings.Export.Value, settings.Prepare.Value, settings.Restore.Value };
            var candidates = Enumerable.Range(0, bindings.Length).Where(i => ShortcutSettings.Pressed(bindings[i]))
                .OrderByDescending(i => bindings[i].Modifiers.Count()).ToList();
            if (candidates.Count == 0) { return -1; }
            if (candidates.Count > 1 && bindings[candidates[0]].Modifiers.Count() == bindings[candidates[1]].Modifiers.Count())
            { throw new InvalidOperationException("Deux actions ont le meme raccourci. Modifie les reglages BepInEx."); }
            var index = candidates[0];
            CheckTomrer(bindings[index]);
            return index;
        }

        // Blocks keys still used by the reference recorder, including modified-key variants.
        private static void CheckTomrer(KeyboardShortcut shortcut)
        {
            if ((shortcut.MainKey == KeyCode.F8 || shortcut.MainKey == KeyCode.F9 || shortcut.MainKey == KeyCode.F10) &&
                Chainloader.PluginInfos.ContainsKey("com.mikamarik.valheimtomrer"))
            { throw new InvalidOperationException("Desactive ValheimTomrer ou choisis d'autres raccourcis : conflit de touches."); }
        }

        // Respects the optional BepInEx configuration editor without taking a runtime dependency.
        private static bool ConfigurationVisible()
        {
            foreach (var plugin in Chainloader.PluginInfos.Values)
            {
                if (plugin.Metadata.GUID.IndexOf("configurationmanager", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                var property = plugin.Instance.GetType().GetProperty("DisplayingWindow");
                if (property?.PropertyType == typeof(bool) && (bool)property.GetValue(plugin.Instance)) { return true; }
            }
            return false;
        }
    }
}
