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
                throw Missing(world.Uid);
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
                throw matches.Count == 0 ? Missing(world.Uid) :
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
            return manifest.Zones.Any(zone => zone.Status == "captured");
        }

        // Identifies which world's recording is unavailable.
        private static InvalidOperationException Missing(long uid)
        {
            return new InvalidOperationException("No saved export matches world UID " + uid + ".");
        }
    }
}
