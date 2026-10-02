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
            if (target == null || source == null)
            {
                throw new InvalidOperationException("World identity is unavailable. Wait for the local world to finish loading.");
            }
            if (!string.Equals(target.Name, source.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("World name mismatch. Current: '" + target.Name +
                    "'. Export: '" + source.Name + "'. Enter a local world with the exact export name.");
            }
            if (target.Seed != source.Seed || !string.Equals(target.SeedText, source.SeedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("World seed mismatch. Current: '" + target.SeedText +
                    "' (" + target.Seed + "). Export: '" + source.SeedText + "' (" + source.Seed +
                    "). Create a local world with the exact export seed, then prepare it with F9.");
            }
            if (target.Uid != source.Uid)
            {
                throw new InvalidOperationException("World UID mismatch. Current: " + target.Uid +
                    ". Export: " + source.Uid + ". Return to the main menu and press F9 to apply the export UID, " +
                    "then enter that local world and press F10.");
            }
        }
    }
}
