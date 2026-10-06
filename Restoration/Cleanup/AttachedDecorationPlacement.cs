using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Cleanup
{
    // Reapplies moved decorative children whenever Valheim respawns a generated site.
    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    internal static class AttachedDecorationPlacement
    {
        private const string PositionKey = "WorldCrawler.attachedDecorationPosition";
        private const string LegacyKey = "WorldCrawler.attachedVegvisirPosition";
        private static readonly FieldInfo InstanceField = AccessTools.Field(typeof(LocationProxy), "m_instance");

        // Finds separate solid decorations without selecting functional or networked structures.
        internal static GameObject[] FindAll(GameObject site)
        {
            if (site == null || !site.activeInHierarchy)
            {
                return new GameObject[0];
            }
            var accepted = new List<GameObject>();
            foreach (var node in site.GetComponentsInChildren<Transform>(true).Skip(1))
            {
                if (accepted.Any(parent => node.IsChildOf(parent.transform)) || !Safe(node.gameObject, site))
                {
                    continue;
                }
                accepted.Add(node.gameObject);
            }
            return accepted.ToArray();
        }

        // Requires the candidate to own a solid visible model and no functional descendants.
        private static bool Safe(GameObject candidate, GameObject site)
        {
            if (!candidate.activeInHierarchy ||
                candidate.GetComponent<Collider>() == null && candidate.GetComponent<MeshRenderer>() == null &&
                candidate.GetComponent<Vegvisir>() == null ||
                candidate.GetComponent<Vegvisir>() == null &&
                    candidate.GetComponentInChildren<Vegvisir>(true) != null ||
                !candidate.GetComponentsInChildren<Collider>(true).Any(item => item.enabled && !item.isTrigger) ||
                !candidate.GetComponentsInChildren<Renderer>(true).Any(item => item.enabled) ||
                candidate.GetComponentsInChildren<ZNetView>(true).Any(item => item.GetZDO() != null) ||
                candidate.GetComponentsInChildren<Transform>(true).Any(item =>
                    CaptureExclusionPolicy.Classify(item.gameObject) != null) ||
                candidate.GetComponentsInChildren<MonoBehaviour>(true).Any(Functional))
            {
                return false;
            }
            for (var parent = candidate.transform.parent; parent != null && parent != site.transform; parent = parent.parent)
            {
                if (parent.GetComponents<MonoBehaviour>().Any(Functional) ||
                    parent.GetComponent<ZNetView>() != null)
                {
                    return false;
                }
            }
            return true;
        }

        // Rejects gameplay scripts while retaining the Elder stone's interaction component.
        private static bool Functional(MonoBehaviour script)
        {
            return script == null || script is Interactable && !(script is Vegvisir) ||
                script is Piece || script is Trader || script is DungeonGenerator ||
                script is TerrainComp || script is TerrainModifier || script is CreatureSpawner ||
                script is TeleportWorld || script is Container || script is ItemStand || script is ArmorStand;
        }

        // Uses a deterministic child path to distinguish decorations inside the same site.
        internal static string PositionKeyFor(GameObject proxy, GameObject candidate)
        {
            return KeyFor(proxy, candidate, PositionKey);
        }

        // Builds a persistent key from sibling indices beneath the location proxy.
        private static string KeyFor(GameObject proxy, GameObject candidate, string prefix)
        {
            var parts = new Stack<int>();
            var node = candidate.transform;
            while (node != null && node != proxy.transform)
            {
                parts.Push(node.GetSiblingIndex());
                node = node.parent;
            }
            return node == proxy.transform ? prefix + "." + string.Join("_", parts) : null;
        }

        // Restores each moved child and reads both old Vegvisir position formats.
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }
            var data = __instance.GetComponent<ZNetView>()?.GetZDO();
            var site = InstanceField?.GetValue(__instance) as GameObject;
            if (data == null || site == null)
            {
                return;
            }
            var candidates = FindAll(site);
            var elder = candidates.Where(item => Utils.GetPrefabName(item) == "Vegvisir_GDKing").ToArray();
            var legacy = data.GetVec3(LegacyKey, out var oldPosition);
            foreach (var candidate in candidates)
            {
                var key = PositionKeyFor(__instance.gameObject, candidate);
                var oldKey = KeyFor(__instance.gameObject, candidate, LegacyKey);
                if (key != null && data.GetVec3(key, out var position) ||
                    oldKey != null && data.GetVec3(oldKey, out position))
                {
                    candidate.transform.position = position;
                }
                else if (legacy && elder.Length == 1 && candidate == elder[0])
                {
                    candidate.transform.position = oldPosition;
                }
            }
        }
    }
}
