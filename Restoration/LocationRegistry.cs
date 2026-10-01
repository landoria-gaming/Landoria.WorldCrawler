using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reconciles native location metadata for observed proxies without deleting world objects.
    internal static class LocationRegistry
    {
        // Registers the captured location at its original position in the target generation table.
        public static void Apply(CapturedObject record)
        {
            if (record.LocationHash == 0)
            {
                return;
            }
            var system = ZoneSystem.instance;
            var source = system.m_locations.SingleOrDefault(v => v.m_prefabName == record.LocationName);
            if (source == null)
            {
                throw new InvalidOperationException("Missing source location: " + record.LocationName);
            }
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(ZoneSystem).GetField("m_locationInstances", flags)
                ?? throw new MissingFieldException("ZoneSystem.m_locationInstances");
            var registry = (IDictionary)field.GetValue(system);
            var position = ObjectRestorer.Vector(record.Position);
            var zone = typeof(ZoneSystem).GetMethod("GetZone", new[] { typeof(Vector3) }).Invoke(null, new object[] { position });
            var register = typeof(ZoneSystem).GetMethod("RegisterLocation", flags, null,
                new[] { typeof(ZoneSystem.ZoneLocation), typeof(Vector3), typeof(bool) }, null)
                ?? throw new MissingMethodException("ZoneSystem.RegisterLocation");
            var caches = new[] { "m_locationIDCache", "m_locationGroupCache", "m_locationMaxGroupCache" }
                .Select(name => (IDictionary)(typeof(ZoneSystem).GetField(name, flags)
                    ?? throw new MissingFieldException("ZoneSystem." + name)).GetValue(system)).ToArray();
            if (registry.Contains(zone))
            {
                var previous = registry[zone];
                foreach (var cache in caches)
                {
                    foreach (var entries in cache.Values)
                    {
                        ((IList)entries).Remove(previous);
                    }
                }
                registry.Remove(zone);
            }
            register.Invoke(system, new object[] { source, position, true });
        }
    }
}
