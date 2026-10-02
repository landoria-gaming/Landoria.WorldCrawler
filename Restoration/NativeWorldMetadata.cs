using System;
using System.IO;
using System.Text.RegularExpressions;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Verifies current-format world metadata without loading Unity or modifying any save.
    internal static partial class NativeWorldMetadata
    {
        // Rejects filename traversal, device names and ambiguous normalized names.
        internal static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith(".") ||
                name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || name == "." || name == ".." ||
                Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            {
                throw new InvalidDataException("The original world name is not a safe local save name.");
            }
        }

        // Rereads the native metadata header before any UID edit or destination use.
        internal static void Verify(string path, WorldIdentity expected)
        {
            if (!path.EndsWith(".fwl2", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The game did not write the new metadata format.");
            }
            using (var input = new BinaryReader(File.OpenRead(path)))
            {
                ReadIdentityPrefix(input, expected);
                if (input.ReadInt64() != expected.Uid || input.ReadInt32() != expected.GenerationVersion)
                {
                    throw new InvalidDataException("The local world metadata does not match its expected identity.");
                }
            }
        }
    }
}
