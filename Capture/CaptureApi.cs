using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Resolves the few zone APIs whose binary signatures changed between supported games.
    internal sealed class CaptureApi
    {
        private readonly MethodInfo _getZone;
        private readonly MethodInfo _findObjects;
        private readonly FieldInfo _zones;
        private readonly FieldInfo _zoneRoot;
        private readonly FieldInfo _proxyInstance;
        private readonly FieldInfo _locationInstances;
        private readonly FieldInfo _locationPosition;
        private readonly FieldInfo _locationDefinition;
        private readonly FieldInfo _zoneX;
        private readonly FieldInfo _zoneY;
        private readonly bool _legacy;
        private readonly object _distance;
        private readonly Dictionary<int, string> _locationNames = new Dictionary<int, string>();

        // Validates all reflected members before the session begins collecting data.
        internal CaptureApi()
        {
            _getZone = typeof(ZoneSystem).GetMethod("GetZone", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Vector3) }, null);
            if (_getZone == null)
            {
                throw new MissingMethodException("ZoneSystem.GetZone(Vector3) is unavailable.");
            }
            var coordinateType = _getZone.ReturnType;
            _zoneX = RequiredField(coordinateType, "x");
            _zoneY = RequiredField(coordinateType, "y");
            _legacy = coordinateType.Name == "Vector2i";
            _findObjects = ResolveFindObjects(coordinateType, out _distance);
            _zones = RequiredField(typeof(ZoneSystem), "m_zones");
            _zoneRoot = RequiredField(_zones.FieldType.GetGenericArguments()[1], "m_root");
            _proxyInstance = RequiredField(typeof(LocationProxy), "m_instance");
            _locationInstances = RequiredField(typeof(ZoneSystem), "m_locationInstances");
            var locationType = _locationInstances.FieldType.GetGenericArguments()[1];
            _locationPosition = RequiredField(locationType, "m_position");
            _locationDefinition = RequiredField(locationType, "m_location");
            if (!typeof(IDictionary).IsAssignableFrom(_zones.FieldType)
                || _zoneRoot.FieldType != typeof(GameObject) || _proxyInstance.FieldType != typeof(GameObject))
            {
                throw new NotSupportedException("The game's loaded-zone or location-proxy layout changed.");
            }
        }

        // Finds persistent candidates in exactly one server sector without expanding visibility.
        internal void FindObjects(Vector3 center, List<ZDO> destination)
        {
            destination.Clear();
            var zone = _getZone.Invoke(null, new object[] { center });
            var arguments = _legacy
                ? new object[] { zone, 0, 0, destination, null }
                : new object[] { zone, _distance, destination, null };
            _findObjects.Invoke(ZDOMan.instance, arguments);
        }

        // Reads the existing zone root without asking the game to generate or load anything.
        internal GameObject GetZoneRoot(Vector3 center)
        {
            var zones = (IDictionary)_zones.GetValue(ZoneSystem.instance);
            var zone = _getZone.Invoke(null, new object[] { center });
            var data = zones[zone];
            return data == null ? null : _zoneRoot.GetValue(data) as GameObject;
        }

        // Returns the client-spawned static location hierarchy once the proxy has finished loading.
        internal GameObject GetLocationInstance(LocationProxy proxy)
        {
            return _proxyInstance.GetValue(proxy) as GameObject;
        }

        // Converts one world position to version-independent integer sector coordinates.
        internal void GetZone(Vector3 position, out int x, out int z)
        {
            var zone = _getZone.Invoke(null, new object[] { position });
            x = Convert.ToInt32(_zoneX.GetValue(zone));
            z = Convert.ToInt32(_zoneY.GetValue(zone));
        }

        // Removes the same generated location type from its native sector registry and lookup caches.
        internal bool RemoveLocationRegistration(Vector3 center, int expectedHash)
        {
            if (_legacy)
            {
                throw new NotSupportedException("Location restoration requires Valheim 1.0.x.");
            }
            var caches = ResolveLocationCaches();
            var locations = (IDictionary)_locationInstances.GetValue(ZoneSystem.instance);
            var zone = _getZone.Invoke(null, new object[] { center });
            var location = locations[zone];
            if (location == null)
            {
                return true;
            }
            var definition = (ZoneSystem.ZoneLocation)_locationDefinition.GetValue(location);
            if (definition == null || definition.m_prefabName.GetStableHashCode() != expectedHash)
            {
                return false;
            }
            locations.Remove(zone);
            foreach (var cacheField in caches)
            {
                RemoveCachedLocation((IDictionary)cacheField.GetValue(ZoneSystem.instance), location);
            }
            return true;
        }

        // Resolves current-only cleanup fields before mutation, never during shared export startup.
        private static FieldInfo[] ResolveLocationCaches()
        {
            return new[] { RequiredField(typeof(ZoneSystem), "m_locationIDCache"),
                RequiredField(typeof(ZoneSystem), "m_locationGroupCache"),
                RequiredField(typeof(ZoneSystem), "m_locationMaxGroupCache") };
        }

        // Removes the matching boxed location value from every list in one native cache.
        private void RemoveCachedLocation(IDictionary cache, object location)
        {
            var expected = (Vector3)_locationPosition.GetValue(location);
            foreach (DictionaryEntry entry in cache)
            {
                var values = (IList)entry.Value;
                for (var index = values.Count - 1; index >= 0; index--)
                {
                    var position = (Vector3)_locationPosition.GetValue(values[index]);
                    if (HorizontalDistance(position, expected) < 0.1f)
                    {
                        values.RemoveAt(index);
                    }
                }
            }
        }

        // Cache entries may retain a different terrain height for the same horizontal site.
        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            var x = first.x - second.x;
            var z = first.z - second.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        // Resolves a location hash through the local catalogue without generating any location.
        internal string GetLocationName(int hash)
        {
            if (_locationNames.Count == 0)
            {
                foreach (var location in ZoneSystem.instance.m_locations)
                {
                    var name = location.m_prefabName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        _locationNames[name.GetStableHashCode()] = name;
                    }
                }
            }
            string result;
            if (!_locationNames.TryGetValue(hash, out result))
            {
                throw new InvalidOperationException("A source location hash is missing from the local catalogue: " + hash);
            }
            return result;
        }

        // Selects an exact legacy or current sector-query signature.
        private MethodInfo ResolveFindObjects(Type coordinateType, out object distance)
        {
            distance = null;
            Type[] signature;
            if (_legacy)
            {
                signature = new[] { coordinateType, typeof(int), typeof(int), typeof(List<ZDO>), typeof(List<ZDO>) };
            }
            else
            {
                var distanceType = typeof(ZoneSystem).Assembly.GetType("SimulationDistance", true);
                distance = Activator.CreateInstance(distanceType, new object[] { 0, 0, false });
                signature = new[] { coordinateType, distanceType, typeof(List<ZDO>), typeof(List<ZDO>) };
            }
            var method = typeof(ZDOMan).GetMethod("FindSectorObjects", signature);
            if (method == null)
            {
                throw new MissingMethodException("The game's sector-query signature is unsupported.");
            }
            return method;
        }

        // Requires each private field to exist rather than silently omitting generated layout.
        private static FieldInfo RequiredField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, name);
            }
            return field;
        }
    }
}
