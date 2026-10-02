using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Allows protected user-directed flight and external teleports without autonomous travel.
    internal sealed partial class FlightController
    {
        public bool Manual => true;
        private bool _movementLocked = true;
        private bool _manualTeleport;

        // Allows explicit map teleports only while accepted data work is idle.
        internal static bool BlocksManualTeleport(Player value)
        {
            return IsControlled(value) && current._movementLocked;
        }

        // Hands physics to an external teleport, then adopts its arrival without choosing a destination.
        internal bool WaitForManualTeleport()
        {
            if (!Manual || player == null)
            {
                return false;
            }
            if (player.IsTeleporting())
            {
                if (!_manualTeleport)
                {
                    physics.SuspendForNativeTransit();
                    _manualTeleport = true;
                }
                return true;
            }
            if (_manualTeleport)
            {
                physics.Activate();
                _manualTeleport = false;
            }
            expectedPosition = player.transform.position;
            OriginRotation = player.transform.rotation;
            return false;
        }

        // Moves with the user's keys at the common sprint multiplier, or holds during data work.
        internal void TickManual(bool hold, float deltaTime)
        {
            _movementLocked = hold;
            if (WaitForManualTeleport())
            {
                return;
            }
            Speed = SprintFlightSpeed.Read(player) * CrawlerConstants.SprintMultiplier;
            var direction = hold || InputBlocked() ? Vector3.zero : ManualDirection();
            var point = player.transform.position + direction * Speed * Mathf.Clamp(deltaTime, 0f, 0.1f);
            Tick(point, deltaTime);
        }

        // Blocks map clicks immediately when new work is accepted, before the next motion tick.
        internal void LockMovement()
        {
            _movementLocked = true;
        }

        // Uses the game's movement bindings and camera heading without enabling debug fly.
        private Vector3 ManualDirection()
        {
            var view = GameCamera.instance == null ? player.transform : GameCamera.instance.transform;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var x = (ZInput.GetButton("Right") ? 1f : 0f) - (ZInput.GetButton("Left") ? 1f : 0f);
            var z = (ZInput.GetButton("Forward") ? 1f : 0f) - (ZInput.GetButton("Backward") ? 1f : 0f);
            var y = (ZInput.GetButton("Jump") ? 1f : 0f) - (ZInput.GetButton("Crouch") ? 1f : 0f);
            return Vector3.ClampMagnitude(right * x + forward * z + Vector3.up * y, 1f);
        }

        // Never moves the character while a map, text field, menu or inventory consumes input.
        private static bool InputBlocked()
        {
            return Console.IsVisible() || Menu.IsVisible() || InventoryGui.IsVisible() || StoreGui.IsVisible() ||
                Minimap.IsOpen() || TextInput.IsVisible() || Chat.instance != null && Chat.instance.HasFocus() ||
                UnifiedPopup.IsAvailable() && UnifiedPopup.IsVisible() || GUIUtility.keyboardControl != 0;
        }
    }
}
