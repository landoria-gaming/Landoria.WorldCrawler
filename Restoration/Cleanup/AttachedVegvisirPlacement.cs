using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Cleanup
{
    // Reapplies a moved Vegvisir after its parent location is recreated from its proxy.
    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    internal static class AttachedVegvisirPlacement
    {
        internal const string PositionKey = "WorldCrawler.attachedVegvisirPosition";

        // Finds independent Elder stones without accepting their parent site's other objects.
        internal static Vegvisir[] FindAll(GameObject root)
        {
            if (root == null || !root.activeInHierarchy)
            {
                return new Vegvisir[0];
            }
            return root.GetComponentsInChildren<Vegvisir>(true)
                .Where(marker => Utils.GetPrefabName(marker.gameObject) == "Vegvisir_GDKing" &&
                    marker.gameObject != root && marker.GetComponentsInChildren<Collider>()
                        .Any(collider => collider.enabled && !collider.isTrigger) &&
                    !marker.GetComponentsInChildren<ZNetView>(true)
                        .Any(child => child.GetZDO() != null) &&
                    !marker.GetComponentsInChildren<Transform>(true)
                        .Any(node => CaptureExclusionPolicy.Classify(node.gameObject) != null) &&
                    !marker.GetComponentsInChildren<MonoBehaviour>(true).Any(Functional))
                .ToArray();
        }

        // Refuses unrelated gameplay components within the stone's own hierarchy.
        private static bool Functional(MonoBehaviour script)
        {
            return script == null || script is Interactable && !(script is Vegvisir) ||
                script is Piece || script is Trader || script is DungeonGenerator ||
                script is TerrainComp || script is TerrainModifier || script is CreatureSpawner ||
                script is TeleportWorld || script is Container || script is ItemStand || script is ArmorStand;
        }

        // Names a child by its sibling path so several stones in one site remain distinct.
        internal static string PositionKeyFor(GameObject proxy, Vegvisir marker)
        {
            var parts = new Stack<int>();
            var node = marker.transform;
            while (node != null && node != proxy.transform)
            {
                parts.Push(node.GetSiblingIndex());
                node = node.parent;
            }
            return node == proxy.transform ? PositionKey + "." + string.Join("_", parts) : null;
        }

        // Applies the saved position only after Valheim has spawned the full location hierarchy.
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }
            var data = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (data == null)
            {
                return;
            }
            var legacy = data.GetVec3(PositionKey, out var destination);
            var markers = FindAll(__instance.gameObject);
            foreach (var marker in markers)
            {
                var key = PositionKeyFor(__instance.gameObject, marker);
                if (key != null && data.GetVec3(key, out var saved))
                {
                    marker.transform.position = saved;
                }
                else if (legacy && markers.Length == 1)
                {
                    marker.transform.position = destination;
                }
            }
        }
    }
}
