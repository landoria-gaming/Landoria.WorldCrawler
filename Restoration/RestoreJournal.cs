using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Saves recoverable import checkpoints separately from untouched export files.
    internal sealed class RestoreJournal : IDisposable
    {
        private readonly FileStream _lock;
        private readonly PreparedWorld _target;
        public string DirectoryPath
        {
            get;
        }
        public RestoreState State
        {
            get;
        }

        // Holds one world-bound restoration writer, regardless of which character is used.
        public RestoreJournal(string root, PreparedWorld target, string character)
        {
            if (string.IsNullOrWhiteSpace(character))
            {
                throw new InvalidOperationException("A loaded character is required.");
            }
            _target = target;
            DirectoryPath = Path.Combine(Path.GetFullPath(root), "_restorations", target.Token);
            Directory.CreateDirectory(DirectoryPath);
            _lock = new FileStream(Path.Combine(DirectoryPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                var path = Path.Combine(DirectoryPath, "restore.json");
                State = File.Exists(path) ? AtomicJson.Read<RestoreState>(path) : new RestoreState
                {
                    World = target.World.Copy(),
                    Token = target.Token,
                    Fingerprint = target.ExportFingerprint,
                    Character = character,
                    AcceptedZones = target.InitialZones == null ? null :
                        new System.Collections.Generic.Dictionary<string, string>(target.InitialZones)
                };
                Validate(State);
                State.Character = character;
            }
            catch
            {
                _lock.Dispose();
                throw;
            }
        }

        // Atomically commits only import state, preserving its preceding revision.
        public void Save()
        {
            State.UpdatedUtc = DateTime.UtcNow.ToString("o");
            AtomicJson.Write(Path.Combine(DirectoryPath, "restore.json"), State, Validate);
        }

        // Extends the known capture set without changing stable object tags or completed-zone checkpoints.
        public void AcceptArchive(ExportArchive archive)
        {
            _target.ValidateArchive(archive);
            if (State.AcceptedZones != null)
            {
                archive.RequirePresent(State.AcceptedZones);
            }
            var signatures = archive.ZoneSignatures();
            if (State.Completed.Any(key => !signatures.ContainsKey(key)))
            {
                throw new InvalidDataException("The restore journal contains zones outside the available captures.");
            }
            if (State.AcceptedZones != null)
            {
                var changed = State.AcceptedZones.Where(v => signatures[v.Key] != v.Value).Select(v => v.Key).ToList();
                State.Completed.RemoveAll(changed.Contains);
                if (changed.Count > 0)
                {
                    State.Warnings.Add(changed.Count + " recaptured zones are pending reimport; existing source identity tags retained.");
                }
            }
            State.AcceptedZones = signatures;
            Save();
        }

        // Rejects cross-world or malformed recovery information.
        private void Validate(RestoreState state)
        {
            if (state == null || state.FormatVersion != 1 || state.World == null ||
                !state.World.Matches(_target.World) || state.Token != _target.Token ||
                state.Fingerprint != _target.ExportFingerprint || string.IsNullOrWhiteSpace(state.Character) ||
                state.Completed == null || state.Objects == null || state.Warnings == null ||
                state.Completed.Count != state.Completed.Distinct().Count() ||
                state.Objects.Any(v => v == null || string.IsNullOrEmpty(v.Source) ||
                    !long.TryParse(v.TargetUser, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) ||
                state.Objects.Select(v => v.Source).Distinct().Count() != state.Objects.Count)
            {
                throw new InvalidDataException("The restore journal is invalid or belongs to another target.");
            }
            if (_target.SourceSeries != null && (state.AcceptedZones == null || state.Completed.Any(key => !state.AcceptedZones.ContainsKey(key))))
            {
                throw new InvalidDataException("The incremental restore journal has missing capture signatures.");
            }
        }

        // Releases the writer lock without deleting its recovery records.
        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
