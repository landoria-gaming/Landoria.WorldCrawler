using System;
using System.IO;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Prevents changing radius or mode from abandoning another itinerary's return checkpoint.
    internal static class SelectionRecovery
    {
        // Reads sibling world inventories without rewriting their progress or recovery evidence.
        internal static void Check(string root, WorldIdentity world, string scope)
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root))
            {
                return;
            }
            var basename = StoreValidation.DirectoryName(world);
            var selected = basename + (scope == null ? "" : "_" + scope);
            foreach (var directory in Directory.GetDirectories(root, basename + "*"))
            {
                var name = Path.GetFileName(directory);
                if (name == selected || name != basename && !name.StartsWith(basename + "_", StringComparison.Ordinal))
                {
                    continue;
                }
                var manifest = ReadCheckpoint(Path.Combine(directory, "manifest.json"), world);
                if (manifest?.ReturnPending == true)
                {
                    throw new InvalidOperationException("Resume the interrupted export in " + name
                        + " with its previous selection mode/radius before changing the itinerary.");
                }
            }
        }

        // Uses a valid previous checkpoint only when the primary is absent or malformed.
        private static WorldManifest ReadCheckpoint(string path, WorldIdentity world)
        {
            if (!File.Exists(path) && !File.Exists(path + ".previous"))
            {
                return null;
            }
            if (!File.Exists(path))
            {
                path += ".previous";
            }
            try
            {
                return Read(path, world);
            }
            catch (Exception error) when ((error is System.Runtime.Serialization.SerializationException ||
                error is System.Xml.XmlException || error is InvalidDataException || error is FormatException ||
                error is EndOfStreamException || error is ArgumentException) && File.Exists(path + ".previous"))
            {
                return Read(path + ".previous", world);
            }
        }

        // Validates identity and return ownership before trusting a sibling checkpoint.
        private static WorldManifest Read(string path, WorldIdentity world)
        {
            var manifest = AtomicJson.Read<WorldManifest>(path);
            StoreValidation.Manifest(manifest, world);
            return manifest;
        }
    }
}
