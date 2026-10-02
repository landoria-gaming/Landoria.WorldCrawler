using System;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Matches the user-created destination while allowing a newer terrain generator.
    internal static class RestoreWorldIdentity
    {
        // Requires exactly the source name, seed and UID before any destination mutation.
        internal static void Require(WorldIdentity target, WorldIdentity source)
        {
            if (target == null || source == null || target.Uid != source.Uid || target.Seed != source.Seed ||
                !string.Equals(target.Name, source.Name, StringComparison.Ordinal) ||
                !string.Equals(target.SeedText, source.SeedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Restoration requires exactly the export's world name, seed and UID. " +
                    "Return to the main menu and press F9 to prepare your local world.");
            }
        }
    }
}
