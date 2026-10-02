using System.IO;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads saved import progress after a restart without opening a restoration writer.
    internal static class RestorationMapSource
    {
        // Shows only the prepared local world's available captures and committed restore checkpoints.
        internal static ExportMapOverlayData Read(string markerPath, string root, string export, WorldIdentity world)
        {
            var marker = AtomicJson.Read<PreparedWorld>(markerPath);
            marker.Validate(world, marker.ExportFingerprint);
            var manifest = AtomicJson.Read<WorldManifest>(Path.Combine(export, "manifest.json"));
            StoreValidation.Manifest(manifest, world);
            var path = Path.Combine(root, "_restorations", marker.Token, "restore.json");
            var state = File.Exists(path) ? AtomicJson.Read<RestoreState>(path) : null;
            if (state != null && (state.Token != marker.Token || state.Fingerprint != marker.ExportFingerprint ||
                !state.World.Matches(world)))
            {
                throw new InvalidDataException("The import map journal belongs to another world.");
            }
            return ProgressMapSnapshot.Restore(manifest.Zones.FindAll(v => v.Status == "captured"),
                state?.Completed ?? new System.Collections.Generic.List<string>());
        }
    }
}
