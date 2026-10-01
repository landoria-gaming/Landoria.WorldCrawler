using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Landoria.WorldCrawler.Restoration
{
    // Copies a committed native world generation before any restoration changes.
    internal static class WorldBackup
    {
        // Keeps partial copies as evidence and declares a backup ready only after hash verification.
        public static string Create(string source, string journalDirectory)
        {
            source = Path.GetFullPath(source);
            var target = Path.Combine(journalDirectory, "backup-" + Guid.NewGuid().ToString("N"));
            if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            { throw new IOException("The backup must be outside the game world directory."); }
            var before = Files(source);
            Directory.CreateDirectory(target);
            foreach (var path in before)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                { throw new IOException("Linked save files are not accepted for backup."); }
                var copy = Path.Combine(target, Path.GetFileName(path));
                File.Copy(path, copy, false);
                if (Hash(path) != Hash(copy)) { throw new IOException("A world file changed while its backup was copied."); }
            }
            if (!before.SequenceEqual(Files(source))) { throw new IOException("The world save changed during backup. Retry safely."); }
            if (!before.Any(v => v.EndsWith(".ok", StringComparison.OrdinalIgnoreCase)))
            { throw new IOException("No committed new-format save is available to back up."); }
            return target;
        }

        // Orders only the native world's direct files; the audited format has no nested chunks.
        private static string[] Files(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Linked world folders are not accepted for backup."); }
            if (Directory.GetDirectories(directory).Length != 0)
            { throw new IOException("Unexpected subdirectories in the native world save; backup stopped."); }
            return Directory.GetFiles(directory).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        }

        // Checks byte-for-byte backup integrity without loading large chunk files into memory.
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            { return Convert.ToBase64String(sha.ComputeHash(stream)); }
        }
    }
}
