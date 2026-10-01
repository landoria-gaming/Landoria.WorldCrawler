using System;
using System.Reflection;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Cancels only the crawler's own native transition before requesting its normal emergency return.
    internal static class NativeTransitRecovery
    {
        private static readonly FieldInfo Target = Field("m_teleportTargetPos", typeof(Vector3));
        private static readonly FieldInfo Active = Field("m_teleporting", typeof(bool));
        private static readonly FieldInfo Cooldown = Field("m_teleportCooldown", typeof(float));

        // Validates private recovery bindings before any native transition can start.
        internal static void Validate()
        {
            if (Target == null || Active == null || Cooldown == null)
            { throw new MissingFieldException("Native teleport recovery is unavailable."); }
        }

        // Includes the frame after native completion, when the cooldown still blocks an ordinary return.
        internal static bool Return(Player player, Vector3 expectedTarget, Vector3 origin, Quaternion rotation)
        {
            var distance = Vector3.Distance((Vector3)Target.GetValue(player), expectedTarget);
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > 0.01f) { return false; }
            Active.SetValue(player, false);
            Cooldown.SetValue(player, 2f);
            if (!player.TeleportTo(origin, rotation, true))
            { throw new InvalidOperationException("The owned native transition stopped, but emergency return was refused."); }
            return true;
        }

        // Requires exact private field types rather than guessing a different game's teleport layout.
        private static FieldInfo Field(string name, Type type)
        {
            var field = typeof(Player).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != type) { throw new MissingFieldException(typeof(Player).FullName, name); }
            return field;
        }
    }
}
