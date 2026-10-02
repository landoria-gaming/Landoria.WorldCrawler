using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Bridges the old per-type getters and current combined getter without binding either ABI directly.
    internal sealed class ObservationFields
    {
        private static readonly string[] Names =
            { "GetFloats", "GetVec3s", "GetQuaternions", "GetInts", "GetLongs", "GetStrings", "GetByteArrays" };
        private static readonly Type[] Values =
            { typeof(float), typeof(Vector3), typeof(Quaternion), typeof(int), typeof(long), typeof(string), typeof(byte[]) };
        private static readonly MethodInfo Combined = ResolveCombined();
        private static readonly MethodInfo[] Legacy = ResolveLegacy();
        private readonly object[] _data;

        // Reads all property collections without mutating them or creating game objects.
        public ObservationFields(ZDOID id)
        {
            _data = new object[9];
            _data[0] = id;
            if (Combined != null)
            {
                Combined.Invoke(null, _data);
                return;
            }
            for (var i = 0; i < Legacy.Length; i++)
            {
                _data[i + 1] = Legacy[i].Invoke(null, new object[] { id });
            }
        }

        // Exposes a strongly typed collection after the adapter signature has been validated.
        public IEnumerable<KeyValuePair<int, T>> Get<T>(int index)
        {
            return (IEnumerable<KeyValuePair<int, T>>)_data[index + 1];
        }

        // Requires the exact current GetData signature instead of selecting a name-only overload.
        private static MethodInfo ResolveCombined()
        {
            var signature = new Type[9];
            signature[0] = typeof(ZDOID);
            for (var i = 0; i < Values.Length; i++)
            {
                var pair = typeof(KeyValuePair<,>).MakeGenericType(typeof(int), Values[i]);
                signature[i + 1] = typeof(List<>).MakeGenericType(pair).MakeByRefType();
            }
            signature[8] = typeof(ZDOConnection).MakeByRefType();
            return typeof(ZDOExtraData).GetMethod("GetData", BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic, null, signature, null);
        }

        // Resolves legacy getters only if the current combined API is unavailable.
        private static MethodInfo[] ResolveLegacy()
        {
            if (Combined != null)
            {
                return Array.Empty<MethodInfo>();
            }
            var methods = new MethodInfo[Names.Length];
            for (var i = 0; i < methods.Length; i++)
            {
                methods[i] = typeof(ZDOExtraData).GetMethod(Names[i], new[] { typeof(ZDOID) })
                    ?? throw new MissingMethodException(typeof(ZDOExtraData).FullName, Names[i]);
            }
            return methods;
        }
    }
}
