using System.IO;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads saved import progress after a restart without opening a restoration writer.
    internal static class RestorationMapSource
    {
        // Shows the UID-matched world's available captures and committed restore checkpoints.
        internal static ExportMapOverlayData Read(WorldIdentity world)
        {
            var export = ExportSourceResolver.Resolve(world);
            var manifest = AtomicJson.Read<WorldManifest>(Path.Combine(export, "manifest.json"));
            StoreValidation.Manifest(manifest, manifest.World);
            RestoreWorldIdentity.Require(world, manifest.World);
            var progress = RestoreJournal.Read(export, world.Uid);
            var zones = CommittedMapFiles.Read(export, manifest, out var warning);
            var result = ProgressMapSnapshot.Restore(zones, progress.RestoredZones);
            result.Warning = warning;
            return result;
        }
    }
}
