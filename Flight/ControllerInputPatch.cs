using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Allows camera look but prevents movement or attacks during controlled flight.
    [HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
    internal static class ControllerInputPatch
    {
        // Lets the normal controller clear its own input buttons without accepting actions.
        private static bool Prefix(Player ___m_character, bool look, ref bool __result)
        {
            if (look || !FlightController.IsControlled(___m_character))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
