using System;
using System.Collections.Generic;
using System.IO;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Replays interrupted flushes idempotently before accepting a new ten-second batch.
    internal static class RecordingFlush
    {
        internal const string FileName = "pending-recording.json";

        // Durably stages detached data before rewriting any per-zone file.
        internal static List<ZoneSaveReport> Write(WorldStore store, List<ZoneSnapshot> zones, string version)
        {
            Recover(store);
            var checkpoint = new RecordingCheckpoint { World = store.Manifest.World.Copy(), Version = version, Zones = zones };
            var path = Path.Combine(store.DirectoryPath, FileName);
            AtomicJson.Write(path, checkpoint, value => Validate(value, store.Manifest.World));
            var reports = Apply(store, checkpoint);
            File.Delete(path);
            return reports;
        }

        // Preserves a corrupt checkpoint for diagnosis rather than silently forgetting the batch.
        internal static void Recover(WorldStore store)
        {
            var path = Path.Combine(store.DirectoryPath, FileName);
            if (!File.Exists(path))
            {
                return;
            }
            var checkpoint = AtomicJson.Read<RecordingCheckpoint>(path);
            Validate(checkpoint, store.Manifest.World);
            Apply(store, checkpoint);
            File.Delete(path);
        }

        // Uses the same idempotent merge for first writes, retries and process restarts.
        private static List<ZoneSaveReport> Apply(WorldStore store, RecordingCheckpoint checkpoint)
        {
            var reports = new List<ZoneSaveReport>();
            foreach (var zone in checkpoint.Zones)
            {
                reports.Add(store.WriteReceived(zone, checkpoint.Version));
            }
            return reports;
        }

        // Checks identity and payloads before any recovery can modify this world's files.
        private static void Validate(RecordingCheckpoint value, WorldIdentity world)
        {
            if (value == null || value.World == null || !world.Matches(value.World) || value.Zones == null ||
                !SupportedGameVersions.CanExport(value.Version))
            {
                throw new InvalidDataException("Invalid pending recording checkpoint; file preserved.");
            }
            var keys = new HashSet<string>();
            foreach (var zone in value.Zones)
            {
                if (zone == null || zone.PayloadVersion != 3 || !keys.Add(zone.ZoneX + ":" + zone.ZoneZ))
                {
                    throw new InvalidDataException("Invalid or duplicate pending recording sector.");
                }
                zone.Validate();
            }
        }
    }
}
