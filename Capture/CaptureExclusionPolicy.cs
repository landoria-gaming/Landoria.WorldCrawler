using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Excludes players and fauna by components rather than fragile prefab-name lists.
    internal static class CaptureExclusionPolicy
    {
        // Identifies players, every Character-derived creature, fish, and non-Character flying birds.
        internal static string Classify(GameObject instance)
        {
            if (instance.name == "_ZoneCtrl")
            {
                return "zone-control";
            }
            if (instance.GetComponent<Player>() != null)
            {
                return "player";
            }
            if (instance.GetComponent<Character>() != null)
            {
                return "creature";
            }
            foreach (var component in instance.GetComponents<Component>())
            {
                var name = component == null ? "" : component.GetType().Name;
                if (name == "Fish")
                {
                    return "fish";
                }
                if (name == "RandomFlyingBird")
                {
                    return "bird";
                }
            }
            return null;
        }

    }
}
