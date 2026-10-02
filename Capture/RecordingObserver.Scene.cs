using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Supplements network records with already-instantiated static layout, without waiting for it.
    internal sealed partial class RecordingObserver
    {
        private readonly NearZoneScope _sceneScope = new NearZoneScope();
        private readonly Queue<Storage.ZoneEntry> _sceneQueue = new Queue<Storage.ZoneEntry>();
        private SceneCaptureCursor _scene;
        private Storage.ZoneEntry _sceneZone;
        private float _nextScenePass;

        // Advances a bounded static scan; departing a zone never cancels its cached network records.
        private void ObserveScene(Vector3 position)
        {
            try
            {
                _sceneScope.Refresh(position);
                if (_scene != null)
                {
                    if (_scene.Step(40))
                    {
                        RetainScene();
                    }
                    return;
                }
                if (_sceneQueue.Count == 0 && Time.realtimeSinceStartup >= _nextScenePass)
                {
                    foreach (var zone in _sceneScope.Zones)
                    {
                        _sceneQueue.Enqueue(zone);
                    }
                    _nextScenePass = Time.realtimeSinceStartup + 10f;
                }
                if (_sceneQueue.Count != 0)
                {
                    BeginScene(_sceneQueue.Dequeue());
                }
            }
            catch (Exception error)
            {
                RetainScene();
                LastError = "Static layout changed or unloaded; received network data retained: " + error.Message;
            }
        }

        // Reads only visible roots and proxies; missing locations are not declared absent.
        private void BeginScene(Storage.ZoneEntry zone)
        {
            if (!_sceneScope.Contains(zone.X, zone.Z))
            {
                return;
            }
            var center = new Vector3(zone.X * 64f, 0f, zone.Z * 64f);
            var root = _api.GetZoneRoot(center);
            if (root == null)
            {
                return;
            }
            _sceneZone = zone;
            _scene = new SceneCaptureCursor(root);
            var objects = new List<ZDO>();
            _api.FindObjects(center, objects);
            foreach (var source in objects.Where(value => value != null && value.IsValid()))
            {
                var view = ZNetScene.instance.FindInstance(source);
                var proxy = view == null ? null : view.GetComponent<LocationProxy>();
                var location = proxy == null ? null : _api.GetLocationInstance(proxy);
                if (location != null)
                {
                    _scene.AddRoot(location, source.m_uid.UserID + ":" + source.m_uid.ID);
                }
            }
        }

        // Preserves the detached nodes already traversed, even when the remainder unloads.
        private void RetainScene()
        {
            if (_scene != null && _sceneZone != null && _scene.Nodes.Count != 0)
            {
                var zone = Zone(_sceneZone.X, _sceneZone.Z);
                foreach (var node in _scene.Nodes)
                {
                    zone.Nodes[node.Root + ":" + node.Path] = node;
                }
            }
            _scene = null;
            _sceneZone = null;
        }
    }
}
