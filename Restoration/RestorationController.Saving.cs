using System;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps periodic native saves separate from per-zone mutation and journal identity writes.
    internal sealed partial class RestorationController
    {
        // Queues a safe-boundary save; no additional mutations run until it is confirmed.
        private void QueueSave(bool initial, bool final)
        {
            _initialSave = initial;
            _finalSave = final;
            _waitingSince = Time.unscaledTime;
            _phase = RestorePhase.RequestSave;
        }

        // Waits out existing autosaves, then owns a new generation containing all current mutations.
        private void RequestSave()
        {
            if (Time.unscaledTime < _retrySaveAt)
            {
                return;
            }
            CheckSaveTimeout();
            if (ZNet.instance.IsSaving() || _objects?.DeletionsPending() == true)
            {
                return;
            }
            _session.Journal.Save();
            _saveBefore = LatestWorldApi.BeginSave();
            _waitingSince = Time.unscaledTime;
            _phase = _initialSave ? RestorePhase.InitialSave : RestorePhase.Saving;
        }

        // Publishes completion only after the game's committed native files have been verified.
        private void FinishSave()
        {
            CheckSaveTimeout();
            if (!LatestWorldApi.SaveFinished(_saveBefore, _session.WorldDirectory))
            {
                return;
            }
            foreach (var key in _applied)
            {
                if (!_session.Journal.State.Completed.Contains(key))
                {
                    _session.Journal.State.Completed.Add(key);
                }
            }
            _lastSaveUtc = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
            _session.Journal.Save();
            _applied.Clear();
            _validated.ExceptWith(_session.Journal.State.Completed);
            _dirty = false;
            _lastSave = Time.unscaledTime;
            _log.LogInfo("Native restoration checkpoint saved at " + _lastSaveUtc + ".");
            if (_finalSave)
            {
                FinishStop();
                return;
            }
            _phase = RestorePhase.Waiting;
        }

        // Keeps failed saves explicit instead of claiming completion from elapsed time.
        private void CheckSaveTimeout()
        {
            if (Time.unscaledTime - _waitingSince > 300f)
            {
                throw new TimeoutException("Native world save timed out; completion was not checkpointed.");
            }
        }
    }
}
