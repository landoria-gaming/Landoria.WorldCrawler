namespace Landoria.WorldCrawler.Protection
{
    // Applies requested passive protections to the local player for the plugin's whole lifetime.
    internal static class PlayerProtection
    {
        internal static bool Enabled;

        // Never changes another player, monster or animal, and does not take over input or physics.
        internal static bool Applies(Character character)
        {
            return Enabled && character != null && character == Player.m_localPlayer;
        }
    }
}
