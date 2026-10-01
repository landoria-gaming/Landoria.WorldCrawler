using System;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Restoration
{
    // Applies the proven Tomrer inventory and display migrations only to legacy observations.
    internal static class LegacyMigration
    {
        // Converts old typed fields without discarding any original serialized export bytes.
        public static void Apply(ZDO target, CapturedObject source, string version)
        {
            if (version != "0.221.12")
            {
                return;
            }
            var inventory = target.GetString("items", "");
            if (!string.IsNullOrEmpty(inventory))
            {
                target.Set("items", new ZPackage(inventory).GetArray());
            }
            ConvertItem(target, "item");
            for (var i = 0; i < 32; i++)
            {
                ConvertItem(target, i + "_item");
            }
            LegacyItemData.Apply(target);
        }

        // Converts a display's prefab name to the current game's stable-hash representation.
        private static void ConvertItem(ZDO target, string field)
        {
            var name = target.GetString(field, "");
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            if (ZNetScene.instance.GetPrefab(name.GetStableHashCode()) == null)
            {
                throw new InvalidOperationException("A legacy displayed item is missing from this game: " + name);
            }
            target.Set(field, name.GetStableHashCode());
        }
    }
}
