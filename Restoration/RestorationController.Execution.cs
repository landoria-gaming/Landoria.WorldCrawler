using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Applies, cleans and validates sectors before adding them to a native-save batch.
    internal sealed partial class RestorationController
    {
        // Backs up a confirmed native generation before the first mutation in each session.
        private void InitialSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory))
            {
                return;
            }
            var source = _session.WorldDirectory;
            var journal = _session.Journal.DirectoryPath;
            _saveBefore = LatestWorldApi.SaveNumber();
            _backup = Task.Run(() => WorldBackup.Create(source, journal));
            _phase = RestorePhase.Backup;
        }

        // Rejects a backup raced by an autosave rather than mutating without a recoverable copy.
        private void Backup()
        {
            if (!_backup.IsCompleted)
            {
                return;
            }
            var backup = _backup;
            _backup = null;
            var path = backup.GetAwaiter().GetResult();
            if (ZNet.instance.IsSaving() || LatestWorldApi.SaveNumber() != _saveBefore)
            {
                throw new IOException("The native world changed during backup. No restoration started; retry.");
            }
            RestoreProtection.Active = true;
            _lastSave = Time.unscaledTime;
            _lastSaveUtc = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
            _phase = RestorePhase.Waiting;
            Say("Restoration active. Move manually; F10 stops and saves. Backup: " + path);
        }

        // Starts mutation only after the complete file and the destination terrain are ready.
        private void ReadZone()
        {
            if (!_read.IsCompleted || !ZoneReady())
            {
                return;
            }
            var read = _read;
            _read = null;
            var data = read.GetAwaiter().GetResult();
            var key = ZoneKey(_zone.X, _zone.Z);
            _applied.Remove(key);
            _validated.Remove(key);
            _session.Journal.Save();
            _writer = new ZoneRestorer(_objects, data.Records, data.Snapshot, _zone, AddWarning);
            _visited.Add(key);
            _dirty = true;
            _finalized = false;
            _phase = RestorePhase.Restoring;
        }

        // Applies and cleans the loaded zone under the existing bounded writer.
        private void Restore()
        {
            if (!ZoneReady())
            {
                return;
            }
            _writer.Step(CrawlerConstants.ObjectsPerFrame);
            if (!_writer.Done)
            {
                return;
            }
            _validated.Add(ZoneKey(_zone.X, _zone.Z));
            _unresolved.Clear();
            _scan = _session.Archive.Connections.AsEnumerable().GetEnumerator();
            _phase = RestorePhase.Connecting;
        }

        // Leaves unavailable cross-zone links pending without holding the player indefinitely.
        private void Connect()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    FinishZone();
                    return;
                }
                var source = _scan.Current;
                if (RestoreRecordPolicy.Include(source) && !_objects.Connect(source))
                {
                    var key = ZoneKey(source.ZoneX, source.ZoneZ);
                    _unresolved.Add(key);
                    AddWarning("Zone " + key + " awaits a linked source object; revisit after its destination is restored.");
                }
            }
        }

        // Keeps validated but unresolved zones out of the durable-completion batch.
        private void FinishZone()
        {
            _scan.Dispose();
            _scan = null;
            var report = _writer.Report();
            _restoredObjects += report.Added.Sum(item => item.Count);
            _applied.UnionWith(_validated.Where(key => !_unresolved.Contains(key)));
            _applied.ExceptWith(_unresolved);
            _session.Journal.Save();
            _log.LogInfo("Applied zone " + ZoneKey(_zone.X, _zone.Z) + "; awaiting native save or unresolved links.");
            ZoneRestored?.Invoke(report);
            _writer = null;
            _zone = null;
            if (!_finalized && _session.Journal.State.Completed.Concat(_applied).Distinct().Count() ==
                _session.Archive.PlannedZoneCount)
            {
                _scan = _session.Archive.Records.GetEnumerator();
                _phase = RestorePhase.Finalizing;
                return;
            }
            _phase = RestorePhase.Waiting;
        }

        // Removes temporary support protection only after all available source zones have valid links.
        private void FinalizeObjects()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose();
                    _scan = null;
                    _finalized = true;
                    _phase = RestorePhase.Waiting;
                    return;
                }
                if (!RestoreRecordPolicy.Include(_scan.Current))
                {
                    continue;
                }
                var target = _objects.Resolve(_scan.Current);
                if (target == null)
                {
                    AddWarning("Missing imported object at finalization: " + RestoreWarnings.Describe(_scan.Current));
                    var key = ZoneKey(_scan.Current.ZoneX, _scan.Current.ZoneZ);
                    _applied.Remove(key);
                    continue;
                }
                if (target.GetBool("WorldCrawler.pending", false))
                {
                    target.Set("WorldCrawler.pending", false);
                    ObjectRestorer.Refresh(target);
                    _dirty = true;
                }
            }
        }
    }
}
