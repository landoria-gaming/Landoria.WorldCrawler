using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Changes exactly the UID bytes in validated metadata and keeps a recoverable original.
    internal static partial class NativeWorldMetadata
    {
        // Atomically replaces the metadata while preserving all other bytes and the original file.
        internal static void ReplaceUid(string path, WorldIdentity previous, long uid)
        {
            Verify(path, previous);
            var original = File.ReadAllBytes(path);
            var modified = (byte[])original.Clone();
            PatchUid(modified, previous, uid);
            var suffix = Guid.NewGuid().ToString("N");
            var temporary = Path.Combine(Path.GetDirectoryName(path), "worldcrawler-uid-" + suffix + ".tmp");
            var backup = Path.Combine(Path.GetDirectoryName(path), "worldcrawler-uid-backup-" + suffix + ".bin");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(modified, 0, modified.Length);
                    output.Flush(true);
                }
                var expected = previous.Copy();
                expected.Uid = uid;
                VerifyBytes(temporary, expected);
                if (!File.ReadAllBytes(path).SequenceEqual(original))
                {
                    throw new IOException("World metadata changed during preparation. Nothing was replaced; retry F9.");
                }
                File.Replace(temporary, path, backup);
                Verify(path, expected);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        // Locates the single Int64 UID without rebuilding the rest of the current-format metadata.
        private static void PatchUid(byte[] bytes, WorldIdentity previous, long uid)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                ReadIdentityPrefix(reader, previous);
                var offset = stream.Position;
                if (reader.ReadInt64() != previous.Uid || reader.ReadInt32() != previous.GenerationVersion)
                {
                    throw new InvalidDataException("World metadata changed before the UID could be prepared.");
                }
                stream.Position = offset;
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(uid);
                }
            }
        }

        // Validates the staged bytes without relying on the temporary filename extension.
        private static void VerifyBytes(string path, WorldIdentity expected)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                ReadIdentityPrefix(reader, expected);
                if (reader.ReadInt64() != expected.Uid || reader.ReadInt32() != expected.GenerationVersion)
                {
                    throw new InvalidDataException("The staged UID metadata failed verification.");
                }
            }
        }

        // Rejects unsupported formats, truncation or a different world's metadata.
        private static void ReadIdentityPrefix(BinaryReader input, WorldIdentity expected)
        {
            var length = input.ReadInt32();
            if (length <= 0 || length != input.BaseStream.Length - 4 || input.ReadInt32() != 41 ||
                input.ReadString() != expected.Name || input.ReadString() != expected.SeedText || input.ReadInt32() != expected.Seed)
            {
                throw new InvalidDataException("Invalid native world metadata identity or format.");
            }
        }
    }
}
