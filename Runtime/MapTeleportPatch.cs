using HarmonyLib;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Teleports an idle local character to an Alt-clicked point using client-side game APIs only.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class MapTeleportPatch
    {
        // Replaces the ordinary map click only while the client owns an idle connected character.
        private static bool Prefix(Minimap __instance)
        {
            if (!CanTeleport() || !TryGetMapPosition(__instance, out var destination))
            {
                return true;
            }
            destination.y = WorldGenerator.instance.GetHeight(destination.x, destination.z) + 1f;
            var player = Player.m_localPlayer;
            player.TeleportTo(destination, player.transform.rotation, true);
            __instance.SetMapMode(Minimap.MapMode.Small);
            return false;
        }

        // Restricts manual map travel while no crawler flight owns the connected local player.
        private static bool CanTeleport()
        {
            var player = Player.m_localPlayer;
            var alt = ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt);
            if (!alt || player == null || player.IsDead() || WorldGenerator.instance == null ||
                ZNet.instance == null || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected ||
                FlightController.BlocksManualTeleport(player))
            {
                return false;
            }
            var view = player.GetComponent<ZNetView>();
            return view != null && view.IsValid() && view.IsOwner();
        }

        // Converts the clicked point in the visible map rectangle to world coordinates.
        private static bool TryGetMapPosition(Minimap map, out Vector3 position)
        {
            var rect = map.m_mapImageLarge.transform as RectTransform;
            if (rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, Input.mousePosition, null, out var localPoint))
            {
                position = Vector3.zero;
                return false;
            }
            var normalized = Rect.PointToNormalized(rect.rect, localPoint);
            var visible = map.m_mapImageLarge.uvRect;
            var mapX = visible.xMin + normalized.x * visible.width;
            var mapY = visible.yMin + normalized.y * visible.height;
            var half = map.m_textureSize / 2f;
            position = new Vector3((mapX * map.m_textureSize - half) * map.m_pixelSize, 0f,
                (mapY * map.m_textureSize - half) * map.m_pixelSize);
            return true;
        }
    }
}
