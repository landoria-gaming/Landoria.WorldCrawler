using System;
using System.IO;

namespace Landoria.WorldCrawler.Restoration
{
    // Rejects cloud-relative or drive-root paths before native local-world creation.
    internal static class LocalSavePath
    {
        // Requires the engine's explicit local root to match its save-data directory.
        internal static string Root(string nativeRoot, string dataRoot)
        {
            var expected = Path.Combine(Absolute(dataRoot), "worlds_local");
            var actual = Absolute(nativeRoot);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Unexpected local save root: " + actual + "; expected " + expected);
            }
            return actual;
        }

        // Accepts only the named world's direct child directory under the validated local root.
        internal static string World(string directory, string root, string name)
        {
            NativeWorldMetadata.ValidateName(name);
            var actual = Absolute(directory);
            if (!string.Equals(actual, Path.Combine(root, name), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Unexpected local world directory: " + actual);
            }
            return actual;
        }

        // Never resolves a cloud path against the process's current drive or directory.
        private static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
                Path.DirectorySeparatorChar == '\\' && (path.Length < 3 || path[1] != ':' ||
                path[2] != '\\' && path[2] != '/'))
            {
                throw new IOException("A fully qualified local save path is required: " + path);
            }
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
