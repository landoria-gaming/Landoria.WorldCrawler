using System;
using System.Reflection;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Reads the native player's sprint target instead of assuming the prefab's base speed.
    internal static class SprintFlightSpeed
    {
        private static readonly MethodInfo Factor = typeof(Player).GetMethod("GetRunSpeedFactor",
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        // Includes running skill and equipment but excludes terrain, attacks and temporary status effects.
        internal static float Read(Player player)
        {
            if (Factor == null || Factor.ReturnType != typeof(float))
            {
                throw new MissingMethodException("Player.GetRunSpeedFactor() is unavailable.");
            }
            var speed = player.m_runSpeed * (float)Factor.Invoke(player, null);
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 1f || speed > 100f)
            {
                throw new InvalidOperationException("The native sprint speed is outside safe flight limits.");
            }
            return speed;
        }
    }
}
