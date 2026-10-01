using HarmonyLib;

namespace Landoria.WorldCrawler.Flight
{
    // Prevents transit from expanding the personal map used to plan later crawl sessions.
    [HarmonyPatch(typeof(Minimap), "UpdateExplore", new[] { typeof(float), typeof(Player) })]
    internal static class MapExplorationPatch
    {
        // Leaves the existing personal and shared exploration data unchanged in flight.
        private static bool Prefix(Player player)
        {
            return !FlightController.IsControlled(player);
        }
    }
}
