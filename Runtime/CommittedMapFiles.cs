using System;
using System.Collections.Generic;
using System.IO;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Validates changed zone files on a worker, caching unchanged content to avoid repeated large reads.
    internal static class CommittedMapFiles
    {
        private static readonly Dictionary<string, Tuple<long, long, string, string>> Cache =
            new Dictionary<string, Tuple<long, long, string, string>>(StringComparer.OrdinalIgnoreCase);

        // Returns only complete canonical files and reports invalid entries without hiding valid neighbors.
        internal static List<ZoneEntry> Read(string directory, WorldManifest manifest, out string warning)
        {
            var zones = new List<ZoneEntry>();
            warning = null;
            foreach (var zone in manifest.Zones)
            {
                if (zone.Status != "captured")
                {
                    continue;
                }
                var error = Check(directory, manifest.World, zone);
                if (error == null)
                {
                    zones.Add(zone);
                }
                else if (warning == null)
                {
                    warning = "Map ignored zone " + zone.X + ":" + zone.Z + ": " + error;
                }
            }
            return zones;
        }

        // Uses file replacement metadata and the committed checksum as the validation cache key.
        private static string Check(string directory, WorldIdentity world, ZoneEntry zone)
        {
            var path = Path.Combine(directory, StoreValidation.ZoneFileName(zone.X, zone.Z));
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return "committed file is missing.";
                }
                var signature = StoreValidation.DirectoryName(world) + ":" + world.GenerationVersion + ":" +
                    zone.Checksum + ":" + zone.ObjectCount + ":" + zone.CaptureVersion;
                if (Cache.TryGetValue(path, out var cached) && cached.Item1 == info.LastWriteTimeUtc.Ticks &&
                    cached.Item2 == info.Length && cached.Item3 == signature)
                {
                    return cached.Item4;
                }
                var error = Validate(path, world, zone);
                if (Cache.Count > 10000)
                {
                    Cache.Clear();
                }
                Cache[path] = Tuple.Create(info.LastWriteTimeUtc.Ticks, info.Length, signature, error);
                return error;
            }
            catch (Exception error)
            {
                return error.Message;
            }
        }

        // Checks full payload integrity before painting a durable export rectangle.
        private static string Validate(string path, WorldIdentity world, ZoneEntry zone)
        {
            var envelope = AtomicJson.Read<ZoneEnvelope>(path);
            var payload = StoreValidation.Envelope(envelope, world, zone.X, zone.Z);
            var snapshot = ZoneSnapshot.Decode(payload);
            if (snapshot.ZoneX != zone.X || snapshot.ZoneZ != zone.Z || snapshot.Objects.Count != zone.ObjectCount ||
                envelope.Checksum != zone.Checksum || envelope.CaptureVersion != zone.CaptureVersion)
            {
                return "file and manifest do not agree.";
            }
            return null;
        }
    }
}
