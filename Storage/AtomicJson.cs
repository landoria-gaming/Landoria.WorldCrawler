using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Landoria.WorldCrawler.Storage
{
    // Writes flushed JSON checkpoints before atomically replacing the committed file.
    internal static class AtomicJson
    {
        internal const long MaximumFileLength = 512L * 1024 * 1024;

        // Reads a bounded file with no dependency on game serializers.
        internal static T Read<T>(string path)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > MaximumFileLength)
                {
                    throw new InvalidDataException("World Crawler file exceeds the supported size limit.");
                }
                return (T)Serializer(typeof(T)).ReadObject(input);
            }
        }

        // Validates the fully written temporary file and preserves one prior committed file.
        internal static void Write<T>(string path, T value, Action<T> validate)
        {
            var temporary = WriteTemporary(path, value, validate);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, path + ".previous");
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        // Commits a verified new file without ever replacing an existing destination.
        internal static void WriteNew<T>(string path, T value, Action<T> validate)
        {
            var temporary = WriteTemporary(path, value, validate);
            File.Move(temporary, path);
        }

        // Flushes and validates a private temporary file before a separate commit operation.
        private static string WriteTemporary<T>(string path, T value, Action<T> validate)
        {
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Serializer(typeof(T)).WriteObject(output, value);
                output.Flush(true);
            }
            validate(Read<T>(temporary));
            return temporary;
        }

        // Creates a serializer that can handle large zone payloads.
        private static DataContractJsonSerializer Serializer(Type type)
        {
            return new DataContractJsonSerializer(type,
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        }
    }
}
