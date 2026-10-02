using System.IO;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads committed recording geometry without locking or modifying an export.
    internal static class ExportMapOverlaySource
    {
        // No rectangle is produced for pending, missing, corrupt, or temporary data.
        public static ExportMapOverlayData ReadProgress(string directory, WorldIdentity world)
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path))
            {
                return new ExportMapOverlayData();
            }
            var manifest = AtomicJson.Read<WorldManifest>(path);
            StoreValidation.Manifest(manifest, world);
            var zones = CommittedMapFiles.Read(directory, manifest, out var warning);
            return new ExportMapOverlayData
            {
                Captured = ExportMapOverlayGeometry.Merge(zones),
                Warning = warning
            };
        }
    }
}
