using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Persistence;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Persists compatible static location transforms in the location's own native ZDO data.
    [DataContract]
    internal sealed class LocationLayout
    {
        internal const string DataKey = "WorldCrawler.locationLayout";
        [DataMember] public int Version = 1;
        [DataMember] public List<CapturedSceneNode> Nodes = new List<CapturedSceneNode>();

        // Saves only layout that matches the current prefab hierarchy before applying anything.
        public static string Attach(ZDO target, CapturedObject source, ZoneSnapshot snapshot)
        {
            var view = ZNetScene.instance.FindInstance(target);
            var proxy = view == null ? null : view.GetComponent<LocationProxy>();
            if (proxy == null)
            {
                return null;
            }
            var data = new LocationLayout { Nodes = snapshot.SceneNodes.Where(v => v.Root == ExportArchive.Key(source)).ToList() };
            if (data.Nodes.Count == 0)
            {
                return "Location has no captured static layout: " + source.LocationName;
            }
            var root = new CaptureApi().GetLocationInstance(proxy);
            try
            {
                var plan = data.Plan(root);
                var bytes = data.Encode();
                target.Set(DataKey, bytes);
                Apply(plan);
                return null;
            }
            catch (InvalidDataException error)
            {
                return source.LocationName + ": " + error.Message;
            }
        }

        // Reapplies native-ZDO-stored layout after an imported proxy loads again.
        public static void Replay(LocationProxy proxy)
        {
            var view = proxy.GetComponent<ZNetView>();
            var data = view?.GetZDO()?.GetByteArray(DataKey, null);
            if (data == null)
            {
                return;
            }
            if (data.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Location layout exceeds its size limit.");
            }
            using (var stream = new MemoryStream(data, false))
            {
                var layout = (LocationLayout)Serializer().ReadObject(stream);
                Apply(layout.Plan(new CaptureApi().GetLocationInstance(proxy)));
            }
        }

        // Validates every address first and excludes network objects and living subtrees.
        private List<Tuple<Transform, CapturedSceneNode>> Plan(GameObject root)
        {
            if (Version != 1 || root == null || Nodes == null || Nodes.Count > 100000)
            {
                throw new InvalidDataException("Unsupported or unavailable location hierarchy.");
            }
            var plan = new List<Tuple<Transform, CapturedSceneNode>>();
            foreach (var node in Nodes)
            {
                if (node == null || node.Path == null)
                {
                    throw new InvalidDataException("Invalid static node.");
                }
                var current = root.transform;
                foreach (var segment in node.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(segment, out var index) || index < 0 || index >= current.childCount)
                    {
                        throw new InvalidDataException("Static hierarchy changed; automatic layout changes were skipped.");
                    }
                    current = current.GetChild(index);
                }
                if (current.name != node.Name)
                {
                    throw new InvalidDataException("Static hierarchy names changed; automatic layout changes were skipped.");
                }
                if (!string.IsNullOrEmpty(node.NetworkUser) || current.GetComponent<ZNetView>() != null ||
                    RestoreProtection.Protected(current.gameObject) || current.GetComponentsInChildren<ZNetView>(true).Length > 0)
                {
                    continue;
                }
                if (node.ActiveSelf != current.gameObject.activeSelf && current.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
                {
                    throw new InvalidDataException("A scripted static variant changed; its activation needs review.");
                }
                CheckTransform(node);
                plan.Add(Tuple.Create(current, node));
            }
            return plan;
        }

        // Rejects malformed saved transforms independently of an export's validation.
        private static void CheckTransform(CapturedSceneNode node)
        {
            if (node.LocalPosition == null || node.LocalPosition.Length != 3 || node.LocalRotation == null ||
                node.LocalRotation.Length != 4 || node.LocalScale == null || node.LocalScale.Length != 3 ||
                node.LocalPosition.Concat(node.LocalRotation).Concat(node.LocalScale).Any(v => float.IsNaN(v) || float.IsInfinity(v)) ||
                Math.Abs(node.LocalRotation.Sum(v => v * v) - 1f) > 0.05f)
            {
                throw new InvalidDataException("Invalid location transform.");
            }
        }

        // Applies only a completely checked static-node plan without deleting anything.
        private static void Apply(IEnumerable<Tuple<Transform, CapturedSceneNode>> plan)
        {
            foreach (var item in plan)
            {
                var node = item.Item2;
                item.Item1.localPosition = ObjectRestorer.Vector(node.LocalPosition);
                item.Item1.localRotation = ObjectRestorer.Rotation(node.LocalRotation);
                item.Item1.localScale = ObjectRestorer.Vector(node.LocalScale);
                item.Item1.gameObject.SetActive(node.ActiveSelf);
            }
        }

        // Encodes the accepted layout for native saving alongside the proxy.
        private byte[] Encode()
        {
            using (var stream = new MemoryStream())
            {
                Serializer().WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        // Allows dense location hierarchies without the framework's small default graph limit.
        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(LocationLayout), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        }
    }
}
