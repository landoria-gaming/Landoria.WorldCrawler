using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Groups execution behavior without changing the controller's shared state.
    internal sealed partial class RestorationController
    {
        // Backs up only a completed native-format generation, never an active write.
        private void InitialSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory))
            {
                return;
            }
            var state = _session.Journal.State;
            if (!_manual && !string.IsNullOrEmpty(state.BackupDirectory))
            {
                if (!Directory.Exists(state.BackupDirectory))
                {
                    throw new IOException("The original restoration backup is missing.");
                }
                BeginRoute();
                return;
            }
            var source = _session.WorldDirectory;
            var journal = _session.Journal.DirectoryPath;
            _saveBefore = LatestWorldApi.SaveNumber();
            _backup = Task.Run(() => WorldBackup.Create(source, journal));
            _phase = RestorePhase.Backup;
        }

        // Publishes the verified backup before allowing the first import mutation.
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
                throw new IOException("The native world changed during backup. No restoration was started; retry.");
            }
            if (!_manual)
            {
                _session.Journal.State.BackupDirectory = path;
                _session.Journal.Save();
            }
            _log.LogInfo("Verified pre-" + Operation.ToLowerInvariant() + " backup: " + path);
            BeginRoute();
        }

        // Starts automatic flight or waits for the user's manual movement.
        private void BeginRoute()
        {
            _session.PrepareCharacter(message => _log.LogInfo(message));
            if (_manual)
            {
                BeginManualMode();
                return;
            }
            _session.StartFlight(CrawlerConstants.Speed);
            _restoreNavigation = new RestoreFlightNavigator(_session.Flight, Player.m_localPlayer,
                CrawlerConstants.SprintMultiplier, CrawlerConstants.SprintMultiplier);
            _motionGate = new ReceiveMotionGate(Player.m_localPlayer, CrawlerConstants.ZoneTimeout);
            RestoreProtection.Active = true;
            AddWarning("Client observations cannot prove server absence. Flagged natural-resource cleanup requires review.");
            AddWarning("Non-network zone-root scenery and changed location assets require separate review.");
            NextZone();
        }

        // Chooses the closest unfinished captured zone without consulting the character's map.
        private void NextZone()
        {
            if (_session.Journal.State.Completed.Count == _session.Archive.Manifest.Zones.Count)
            {
                if (_session.Archive.Manifest.Zones.Count < _session.Archive.PlannedZoneCount)
                {
                    AddWarning("Available captures restored. Building support protection remains active until the remaining source zones are exported and restored.");
                    StopHere();
                    return;
                }
                _scan = _session.Archive.Records.GetEnumerator();
                _phase = RestorePhase.Finalizing;
                return;
            }
            if (_pause)
            {
                StopHere();
                return;
            }
            var position = Player.m_localPlayer.transform.position;
            _zone = _session.Archive.Manifest.Zones.Where(v => !_session.Journal.State.Completed.Contains(ZoneKey(v.X, v.Z)))
                .OrderBy(v => Math.Pow(v.X * 64.0 - position.x, 2) + Math.Pow(v.Z * 64.0 - position.z, 2))
                .ThenBy(v => v.Z).ThenBy(v => v.X).FirstOrDefault();
            if (_zone == null)
            {
                StopHere();
                return;
            }
            _phase = RestorePhase.Travelling;
        }

        // Loads the target sector through ordinary controlled movement before importing it.
        private void Travel()
        {
            if (_pause)
            {
                StopHere();
                return;
            }
            if (HoldForReception())
            {
                _session.Hold(Time.unscaledDeltaTime);
                return;
            }
            if (!_restoreNavigation.Travel(_zone.X * 64f, _zone.Z * 64f, Time.unscaledDeltaTime))
            {
                return;
            }
            var center = new Vector3(_zone.X * 64f, Player.m_localPlayer.transform.position.y, _zone.Z * 64f);
            if (!ZNetScene.instance.IsAreaReady(center))
            {
                if (_waitingSince == 0f)
                {
                    _waitingSince = Time.unscaledTime;
                }
                if (Time.unscaledTime - _waitingSince > 180f)
                {
                    throw new TimeoutException("Target zone did not load.");
                }
                return;
            }
            _waitingSince = 0;
            var archive = _session.Archive;
            var zone = _zone;
            _read = Task.Run(() => new ZoneImportData { Snapshot = archive.ReadZone(zone), Records = archive.ZoneObjects(zone.X, zone.Z) });
            _phase = RestorePhase.Reading;
        }

        // Holds low flight while nearby world data arrives and resumes after two quiet seconds.
        private bool HoldForReception()
        {
            var hold = _motionGate.Hold();
            if (hold != _receiveHold)
            {
                _receiveHold = hold;
                _log.LogInfo(hold ? Operation + " flight paused: receiving nearby world data."
                    : Operation + " flight resumed: nearby world data is quiet.");
            }
            return hold;
        }

        // Hands validated files to a bounded main-thread restorer.
        private void ReadZone()
        {
            if (!_read.IsCompleted || !ManualZoneReady())
            {
                return;
            }
            var read = _read;
            _read = null;
            var data = read.GetAwaiter().GetResult();
            if (_pause)
            {
                StopHere();
                return;
            }
            _writer = new ZoneRestorer(_objects, data.Records, data.Snapshot, _zone, AddWarning);
            _phase = RestorePhase.Restoring;
        }

        // Finishes the current zone before honoring a pause request.
        private void Restore()
        {
            _writer.Step(CrawlerConstants.ObjectsPerFrame);
            if (!_writer.Done)
            {
                return;
            }
            _scan = _session.Archive.Connections.AsEnumerable().GetEnumerator();
            _phase = RestorePhase.Connecting;
        }

        // Repairs cross-zone relationships incrementally as more destination objects become available.
        private void Connect()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose();
                    _scan = null;
                    QueueSave(false, false);
                    return;
                }
                if (RestoreRecordPolicy.Include(_scan.Current) && !_objects.Connect(_scan.Current) &&
                    _session.Journal.State.Completed.Count + 1 >= _session.Archive.Manifest.Zones.Count)
                {
                    AddWarning(RestoreWarnings.Describe(_scan.Current) + "; unresolved connection to source=" +
                    _scan.Current.ConnectionTargetUser + ":" + _scan.Current.ConnectionTargetId + "; link left unresolved.");
                }
            }
        }

        // Marks a zone complete only after the engine confirms its native files were committed.
        private void FinishSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory))
            {
                return;
            }
            if (_finalSave)
            {
                _finalSave = false;
                if (_manual && !_pause)
                {
                    _phase = RestorePhase.ManualWaiting;
                    return;
                }
                StopHere();
                return;
            }
            var key = ZoneKey(_zone.X, _zone.Z);
            if (!_session.Journal.State.Completed.Contains(key))
            {
                _session.Journal.State.Completed.Add(key);
            }
            _session.Journal.Save();
            _writer = null;
            _waitingSince = 0f;
            _log.LogInfo("Restored and native-saved zone " + key);
            if (_manual)
            {
                FinishManualZone();
                return;
            }
            NextZone();
        }

        // Releases persistent support protection only after every exported zone was restored.
        private void FinalizeObjects()
        {
            for (var i = 0; i < 40; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose();
                    _scan = null;
                    QueueSave(false, true);
                    return;
                }
                if (!RestoreRecordPolicy.Include(_scan.Current))
                {
                    continue;
                }
                var target = _objects.Resolve(_scan.Current);
                if (target == null)
                {
                    AddWarning(RestoreWarnings.Describe(_scan.Current) + "; imported object is no longer present at finalization.");
                    continue;
                }
                target.Set("WorldCrawler.pending", false);
                ObjectRestorer.Refresh(target);
            }
        }

        // Queues an owned save without mistaking a concurrent autosave for our commit.
        private void QueueSave(bool initial, bool final)
        {
            _initialSave = initial;
            _finalSave = final;
            _waitingSince = Time.unscaledTime;
            _phase = RestorePhase.RequestSave;
        }

        // Waits for any existing save before requesting the generation used by our checkpoint.
        private void RequestSave()
        {
            CheckSaveTimeout();
            if (ZNet.instance.IsSaving())
            {
                return;
            }
            _saveBefore = LatestWorldApi.BeginSave();
            _waitingSince = Time.unscaledTime;
            _phase = _initialSave ? RestorePhase.InitialSave : RestorePhase.Saving;
        }
    }
}
