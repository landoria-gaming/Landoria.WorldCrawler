using System.IO;
using System.Text;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Computes the stable identity shared by one export directory and its prepared world.
    internal static class ExportSeries
    {
        // Uses only validated manifest metadata so selection does not read every zone twice.
        internal static string Identity(WorldManifest manifest, string directory)
        {
            return StoreValidation.Hash(Encoding.UTF8.GetBytes("export-series-v1\n" +
                StoreValidation.DirectoryName(manifest.World) + "\n" + manifest.CreatedUtc + "\n" +
                manifest.CharacterId + "\n" + Path.GetFileName(Path.GetFullPath(directory))));
        }
    }
}
