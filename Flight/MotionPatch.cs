using HarmonyLib;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Replaces only vanilla motion for the controlled player, leaving damage and death active.
    [HarmonyPatch(typeof(Character), "UpdateMotion", new[] { typeof(float) })]
    internal static class MotionPatch
    {
        // Avoids gravity, input motion, and accumulated fall damage while the body is controlled.
        private static bool Prefix(Character __instance, ref float ___m_maxAirAltitude,
            ref float ___m_lastGroundTouch, ref float ___m_fallTimer, ref Vector3 ___m_currentVel)
        {
            if (!FlightController.IsControlled(__instance))
            {
                return true;
            }
            ___m_maxAirAltitude = __instance.transform.position.y;
            ___m_lastGroundTouch = 1f;
            ___m_fallTimer = 0f;
            ___m_currentVel = Vector3.zero;
            return false;
        }
    }
}
