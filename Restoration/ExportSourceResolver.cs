using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Finds the recorded world by UID without a sidecar in the native save folder.
    internal static class ExportSourceResolver
    {
        // Requires one matching world and verifies its seed before any restoration begins.
        internal static string Resolve(WorldIdentity world)
        {
            if (!Directory.Exists(CrawlerConstants.ExportRoot))
            {
                throw new InvalidOperationException("The recording folder is missing: " + CrawlerConstants.ExportRoot +
                    ". Record the source world with F8 or copy your complete export folder here before using F9 and F10.");
            }
            var matches = new List<string>();
            foreach (var directory in Directory.GetDirectories(CrawlerConstants.ExportRoot))
            {
                if (Matches(directory, world))
                {
                    matches.Add(directory);
                }
            }
            if (matches.Count != 1)
            {
                throw matches.Count == 0 ? Missing(world) :
                    new InvalidOperationException("Multiple exports match world UID " + world.Uid +
                        ". Keep only one in the worlds folder.");
            }
            return matches[0];
        }

        // Checks UID first, then validates the seed and metadata without silently skipping corruption.
        private static bool Matches(string directory, WorldIdentity world)
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path))
            {
                return false;
            }
            var manifest = AtomicJson.Read<WorldManifest>(path);
            if (manifest?.World?.Uid != world.Uid)
            {
                return false;
            }
            StoreValidation.Manifest(manifest, manifest.World);
            RestoreWorldIdentity.Require(world, manifest.World);
            if (!manifest.Zones.Any(zone => zone.Status == "captured"))
            {
                throw new InvalidOperationException("The export for world '" + world.Name +
                    "' contains no saved zones. Record and save at least one source zone with F8 before restoring.");
            }
            return true;
        }

        // Explains the F9 step needed after creating or moving a world to local storage.
        private static InvalidOperationException Missing(WorldIdentity world)
        {
            return new InvalidOperationException("No saved export matches local world '" + world.Name +
                "' (UID " + world.Uid + "). Its UID may not have been prepared yet.\n\n" +
                "Return to the main menu and press F9 AFTER creating the world or moving it to local storage. " +
                "Wait for the world-ready confirmation, enter that local world, then press F10. " +
                "Keep the complete export folder in " + CrawlerConstants.ExportRoot + ".");
        }
    }
}
