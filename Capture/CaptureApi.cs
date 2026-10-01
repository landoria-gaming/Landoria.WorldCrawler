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
            _legacy = coordinateType.Name == "Vector2i";
            _findObjects = ResolveFindObjects(coordinateType, out _distance);
            _zones = RequiredField(typeof(ZoneSystem), "m_zones");
            _zoneRoot = RequiredField(_zones.FieldType.GetGenericArguments()[1], "m_root");
            _proxyInstance = RequiredField(typeof(LocationProxy), "m_instance");
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
