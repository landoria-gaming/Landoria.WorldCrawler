using System;
using System.Collections.Generic;
using System.Reflection;

namespace Landoria.WorldCrawler.Restoration
{
    // Checks the native save contract before creating folders or mutating a restored world.
    internal static class CurrentSaveCompatibility
    {
        // Supports patch releases with the known chunked format and exact save API shapes.
        internal static void Validate()
        {
            var version = typeof(Player).Assembly.GetType("Version", true);
            var format = version.GetField("c_WorldVersion", BindingFlags.Public | BindingFlags.Static);
            if (format == null || !format.IsLiteral || Convert.ToInt32(format.GetRawConstantValue()) != 41)
            {
                throw new NotSupportedException("This Valheim 1.0.x save format is not supported; expected format 41.");
            }
            Method(typeof(SaveSystem), "ClearWorldListCache", true, typeof(void), typeof(bool));
            Method(typeof(SaveSystem), "GetWorldList", true, typeof(List<World>));
            Method(typeof(SaveSystem), "GetWorldsSaveRootPath", true, typeof(string), typeof(FileHelpers.FileSource));
            Method(typeof(SaveSystem), "SetSaveNumber", true, typeof(void), typeof(uint));
            Method(typeof(SaveSystem), "GetSaveNumber", true, typeof(uint));
            Method(typeof(World), "GetSaveDirectory", false, typeof(string), typeof(FileHelpers.FileSource));
            Method(typeof(World), "SaveWorldFWLData", false, typeof(void), typeof(DateTime));
            Method(typeof(World), "GetSaveFWLPath", false, typeof(string));
            Method(typeof(ZNet), "Save", false, typeof(void), typeof(bool), typeof(bool), typeof(bool));
            Method(typeof(FejdStartup), "UpdateWorldList", false, typeof(void), typeof(bool));
        }

        // Fails with the changed member name before any operation uses that method.
        private static void Method(Type owner, string name, bool isStatic, Type result, params Type[] arguments)
        {
            var method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, arguments, null);
            if (method == null || method.ReturnType != result)
            {
                throw new NotSupportedException("Incompatible Valheim 1.0.x API: " + owner.Name + "." + name);
            }
        }
    }
}
