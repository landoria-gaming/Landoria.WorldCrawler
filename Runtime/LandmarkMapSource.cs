using System;
using System.Collections.Generic;
using System.Reflection;
using Landoria.WorldCrawler.Inventory;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads native map discovery rather than reconstructing hidden locations from the world seed.
    internal static class LandmarkMapSource
    {
        // Checks the same loaded-map guard used by the exploration snapshot.
        internal static bool Ready(Minimap map)
        {
            return (bool)Field("m_hasGenerated").GetValue(map);
        }

        // Combines saved personal markers with known automatic location icons.
        internal static void Read(LandmarkInventory result)
        {
            var map = Minimap.instance;
            ReadPersonalPins(result, map);
            ReadLocationIcons(result, map);
            result.Warnings.Add("Only saved personal markers are included; shared, death, and round pins are excluded. "
                + "Generic marker text is not treated as proof of a portal, merchant or quest site.");
        }

        // Refreshes map-derived destinations together so stale undiscovered locations are removed.
        internal static LandmarkInventory ReadPersonalPins()
        {
            var result = new LandmarkInventory();
            var map = Minimap.instance;
            if (map == null || !Ready(map) || ZoneSystem.instance == null || Game.instance == null)
            { throw new InvalidOperationException("Wait for the world and map before refreshing destinations."); }
            Read(result);
            return result;
        }

        // Shares the exact same ownership, death-pin and round-pin filters with initial selection.
        private static void ReadPersonalPins(LandmarkInventory result, Minimap map)
        {
            var pins = (List<Minimap.PinData>)Field("m_pins").GetValue(map);
            ReadSavedPins(result, pins, CircleType(map), map);
        }

        // Includes personal saved markers while excluding shared, death, round, and deleted pins.
        private static void ReadSavedPins(LandmarkInventory result, List<Minimap.PinData> pins, Minimap.PinType circle, Minimap map)
        {
            foreach (var pin in pins)
            {
                if (pin == null || !pin.m_save || pin.m_shouldDelete || pin.m_ownerID != 0
                    || pin.m_type == Minimap.PinType.Death) { continue; }
                var kind = PinKind(pin.m_type, circle);
                if (kind == null || !Explored(map, pin.m_pos)) { continue; }
                var identity = kind == "pin" ? "pin:" + (int)pin.m_type : kind;
                LandmarkSnapshot.Add(result, LandmarkSnapshot.PositionId(identity, pin.m_pos), kind,
                    pin.m_name, "personal-map-pin", pin.m_pos);
            }
        }

        // Reads only the game's published icon list, never its complete location-instance table.
        private static void ReadLocationIcons(LandmarkInventory result, Minimap map)
        {
            var icons = new Dictionary<Vector3, string>();
            ZoneSystem.instance.GetLocationIcons(icons);
            var start = Game.instance.m_StartLocation;
            var foundStart = false;
            foreach (var icon in icons)
            {
                var kind = icon.Value == start ? "start" : LocationKind(icon.Value);
                if (kind == null) { continue; }
                if (!Explored(map, icon.Key)) { continue; }
                if (kind == "start") { foundStart = true; }
                LandmarkSnapshot.Add(result, LandmarkSnapshot.PositionId(kind, icon.Key), kind,
                    icon.Value, "native-location-icon", icon.Key);
            }
            if (!foundStart)
            { result.Warnings.Add("The native start-temple icon is not available; its position was not guessed."); }
        }

        // Uses semantic native pin categories instead of localized or user-entered names.
        private static string PinKind(Minimap.PinType type, Minimap.PinType circle)
        {
            if (type == Minimap.PinType.Boss) { return "boss"; }
            if (type == Minimap.PinType.Hildir1 || type == Minimap.PinType.Hildir2 || type == Minimap.PinType.Hildir3)
            { return "hildir"; }
            return type == circle ? null : "pin";
        }

        // Resolves the verified round sprite from the live catalogue instead of guessing an Icon enum.
        private static Minimap.PinType CircleType(Minimap map)
        {
            Minimap.PinType? result = null;
            foreach (var icon in map.m_icons)
            {
                if (icon.m_icon == null || icon.m_icon.name != "mapicon_pin") { continue; }
                if (result.HasValue && result.Value != icon.m_name)
                { throw new NotSupportedException("The round map icon has an ambiguous native mapping."); }
                result = icon.m_name;
            }
            return result ?? throw new NotSupportedException("The native round map icon could not be identified; "
                + "manual markers cannot be safely selected on this game setup.");
        }

        // Recognizes vanilla location prefab families independently of world names and translations.
        private static string LocationKind(string name)
        {
            if (string.IsNullOrEmpty(name)) { return null; }
            if (name.StartsWith("Vendor_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("BogWitch", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Hildir_camp", StringComparison.OrdinalIgnoreCase)) { return "merchant"; }
            if (name.StartsWith("Hildir_", StringComparison.OrdinalIgnoreCase)) { return "hildir"; }
            return null;
        }

        // Calls the native predicate that includes both personal and shared exploration.
        private static bool Explored(Minimap map, Vector3 position)
        {
            var method = typeof(Minimap).GetMethod("IsExplored", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector3) }, null) ?? throw new MissingMethodException("Minimap.IsExplored");
            return (bool)method.Invoke(map, new object[] { position });
        }

        // Resolves audited private map collections whose public accessor does not exist.
        private static FieldInfo Field(string name)
        {
            return typeof(Minimap).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(Minimap).FullName, name);
        }
    }
}
