using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Traverses existing zone and proxy hierarchies a few nodes per frame.
    internal sealed class SceneCaptureCursor
    {
        private readonly Queue<Tuple<Transform, string, string>> _pending = new Queue<Tuple<Transform, string, string>>();
        private readonly List<CapturedSceneNode> _nodes = new List<CapturedSceneNode>();
        private readonly List<string> _excluded = new List<string>();
        internal List<CapturedSceneNode> Nodes
        {
            get
            {
                return _nodes;
            }
        }
        internal List<string> Excluded
        {
            get
            {
                return _excluded;
            }
        }

        // Begins with the loaded terrain-zone root, which may also own non-network vegetation.
        internal SceneCaptureCursor(GameObject zoneRoot)
        {
            if (zoneRoot == null)
            {
                throw new InvalidOperationException("The loaded zone root disappeared before scene capture.");
            }
            AddRoot(zoneRoot, "zone");
        }

        // Adds a generated location hierarchy using its source ZDO as a stable owner.
        internal void AddRoot(GameObject root, string source)
        {
            if (root == null)
            {
                throw new InvalidOperationException("A source location has not finished spawning.");
            }
            _pending.Enqueue(Tuple.Create(root.transform, source, ""));
        }

        // Captures at most the requested node count and returns true when traversal is complete.
        internal bool Step(int budget)
        {
            while (budget-- > 0 && _pending.Count > 0)
            {
                var item = _pending.Dequeue();
                var transform = item.Item1;
                if (transform == null)
                {
                    throw new InvalidOperationException("A source scene hierarchy changed during capture; retry the zone.");
                }
                var reason = CaptureExclusionPolicy.Classify(transform.gameObject);
                if (reason != null)
                {
                    _excluded.Add("scene:" + reason);
                    continue;
                }
                _nodes.Add(ReadNode(transform, item.Item2, item.Item3));
                for (var i = 0; i < transform.childCount; i++)
                {
                    _pending.Enqueue(Tuple.Create(transform.GetChild(i), item.Item2, item.Item3 + "/" + i));
                }
            }
            return _pending.Count == 0;
        }

        // Records stable addresses and transforms while retaining inactive location variants.
        private static CapturedSceneNode ReadNode(Transform transform, string root, string path)
        {
            var view = transform.GetComponent<ZNetView>();
            var zdo = view == null ? null : view.GetZDO();
            return new CapturedSceneNode
            {
                Root = root,
                Path = path,
                Name = transform.name,
                Position = CaptureTransform.Vector(transform.position),
                Rotation = CaptureTransform.Rotation(transform.rotation),
                LocalPosition = CaptureTransform.Vector(transform.localPosition),
                LocalRotation = CaptureTransform.Rotation(transform.localRotation),
                LocalScale = CaptureTransform.Vector(transform.localScale),
                ActiveSelf = transform.gameObject.activeSelf,
                Components = transform.GetComponents<Component>().Select(component => component == null
                    ? "<missing>" : component.GetType().FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                NetworkUser = zdo == null ? null : zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture),
                NetworkId = zdo == null ? 0 : zdo.m_uid.ID
            };
        }
    }
}
