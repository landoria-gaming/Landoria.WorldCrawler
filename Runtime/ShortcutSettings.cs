using BepInEx.Configuration;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Exposes key combinations in the standard BepInEx configuration editor.
    internal sealed class ShortcutSettings
    {
        public ConfigEntry<KeyboardShortcut> Export { get; }
        public ConfigEntry<KeyboardShortcut> Prepare { get; }
        public ConfigEntry<KeyboardShortcut> Restore { get; }

        // Retains the existing export key name so single-key configuration upgrades cleanly.
        public ShortcutSettings(ConfigFile config)
        {
            Export = config.Bind("Controls", "ToggleKey", new KeyboardShortcut(KeyCode.F8),
                "Start, pause or resume export. Supports modifiers, for example F8 + LeftControl.");
            Prepare = config.Bind("Controls", "PrepareWorldShortcut", new KeyboardShortcut(KeyCode.F9),
                "Open world preparation from the main menu. Current Valheim only.");
            Restore = config.Bind("Controls", "RestoreShortcut", new KeyboardShortcut(KeyCode.F10),
                "Start, pause or resume restoration in a prepared local world. Use a test character.");
        }

        // Matches all configured modifiers without requiring a particular input backend.
        public static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey)) { return false; }
            foreach (var modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier)) { return false; }
            }
            return true;
        }
    }
}
