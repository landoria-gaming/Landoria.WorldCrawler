using System;
using System.Collections.Generic;
using System.Reflection;

namespace Landoria.WorldCrawler.Restoration.Compatibility
{
    // Uses the current engine's item migration to preserve old quantities and equipment properties.
    internal static class LegacyItemData
    {
        private static MethodInfo _convert;
        private static object _worldVersion;

        // Requires the exact native converter before starting any restoration writes.
        internal static void Validate()
        {
            if (_convert != null)
            {
                return;
            }
            var version = typeof(ZDOMan).Assembly.GetType("Version+World", true);
            var method = typeof(ZDOMan).GetMethod("ConvertInventories", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(List<ZDOID>), version, typeof(int) }, null);
            if (method == null || method.ReturnType != typeof(int))
            {
                throw new NotSupportedException("Incompatible native legacy item converter: ZDOMan.ConvertInventories.");
            }
            _worldVersion = Enum.ToObject(version, 37);
            _convert = method;
        }

        // Converts only this restored object's old item slots, never the rest of the destination world.
        internal static void Apply(ZDO target)
        {
            Validate();
            var identifiers = new List<ZDOID> { target.m_uid };
            for (var index = -1; index < 32; index++)
            {
                var prefix = index < 0 ? "" : index + "_";
                if (target.GetInt(prefix + "stack", int.MinValue) == int.MinValue
                    || target.GetByteArray(prefix + "itemData") != null)
                {
                    continue;
                }
                if ((int)_convert.Invoke(ZDOMan.instance, new[] { (object)identifiers, _worldVersion, index }) != 1
                    || target.GetByteArray(prefix + "itemData") == null)
                {
                    throw new InvalidOperationException("Native legacy item conversion did not produce the expected item data.");
                }
            }
        }
    }
}
