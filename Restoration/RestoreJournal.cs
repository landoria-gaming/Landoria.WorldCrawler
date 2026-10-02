using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Persists only the zones restored at least once, beside their exported files.
    internal sealed class RestoreJournal
    {
        internal const string FileName = "restore.json";
        private readonly long _uid;
        public string DirectoryPath { get; }
        public RestoreState State { get; } = new RestoreState();

        // Reuses the archive's exclusive lock without creating another tracking directory.
        public RestoreJournal(WorldIdentity world, ExportArchive archive)
        {
            RestoreWorldIdentity.Require(world, archive.Manifest.World);
            _uid = world.Uid;
            DirectoryPath = archive.DirectoryPath;
            State.Completed.AddRange(Read(DirectoryPath, _uid).RestoredZones);
        }

        // Writes no character information, object ledger or source revision history.
        public void Save()
        {
            var progress = new RestoreProgress
            {
                WorldUid = _uid,
                RestoredZones = State.Completed.Distinct().OrderBy(key => key, StringComparer.Ordinal).ToList()
            };
            AtomicJson.Write(Path.Combine(DirectoryPath, FileName), progress, item => item.Validate(_uid));
        }

        // Reads the optional map progress without opening any world save.
        internal static RestoreProgress Read(string directory, long uid)
        {
            var path = Path.Combine(directory, FileName);
            var progress = File.Exists(path) ? AtomicJson.Read<RestoreProgress>(path) :
                new RestoreProgress { WorldUid = uid };
            if (progress == null)
            {
                throw new InvalidDataException("Restoration map progress is empty.");
            }
            progress.Validate(uid);
            return progress;
        }

        // Clears map colors when F9 attaches a newly created destination to the source UID.
        internal static void Reset(string directory, long uid)
        {
            AtomicJson.Write(Path.Combine(directory, FileName), new RestoreProgress { WorldUid = uid },
                item => item.Validate(uid));
        }
    }
}
