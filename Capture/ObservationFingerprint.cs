using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Compares exported state, excluding ownership/revision counters and continuous fire-clock ticks.
    internal static class ObservationFingerprint
    {
        // Canonicalizes fields so duplicate packets and dictionary ordering cannot prolong a hold.
        internal static string Read(ZDO source)
        {
            var data = new ZPackage();
            var fields = new ObservationFields(source.m_uid);
            data.Write(source.GetPrefab());
            data.Write(source.GetPosition());
            data.Write(source.GetRotation());
            var view = ZNetScene.instance.FindInstance(source);
            data.Write(view == null ? Vector3.one : view.transform.localScale);
            var prefab = ZNetScene.instance.GetPrefab(source.GetPrefab());
            var fire = prefab != null && prefab.GetComponent<Fireplace>() != null;
            Fields(data, fields.Get<float>(0), (key, value) =>
                data.Write(fire && key == ZDOVars.s_fuel ? Mathf.Ceil(value) : value));
            Fields(data, fields.Get<Vector3>(1), (key, value) => data.Write(value));
            Fields(data, fields.Get<Quaternion>(2), (key, value) => data.Write(value));
            Fields(data, fields.Get<int>(3), (key, value) => data.Write(value));
            Fields(data, fields.Get<long>(4).Where(pair => !fire || pair.Key != ZDOVars.s_lastTime),
                (key, value) => data.Write(value));
            Fields(data, fields.Get<string>(5), (key, value) => data.Write(value));
            Fields(data, fields.Get<byte[]>(6), (key, value) => data.Write(value));
            var connection = source.GetConnection();
            data.Write(connection == null ? 0 : (int)connection.m_type);
            if (connection != null)
            {
                data.Write(connection.m_target);
            }
            return StoreValidation.Hash(data.GetArray());
        }

        // Preserves field boundaries and stable ordering without modifying the live object.
        private static void Fields<T>(ZPackage data, IEnumerable<KeyValuePair<int, T>> fields, Action<int, T> write)
        {
            var ordered = fields.OrderBy(pair => pair.Key).ToArray();
            data.Write(ordered.Length);
            foreach (var pair in ordered)
            {
                data.Write(pair.Key);
                write(pair.Key, pair.Value);
            }
        }
    }
}
