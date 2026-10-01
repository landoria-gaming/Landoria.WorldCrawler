using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Acquires the checked export and movement state before beginning the route.
    internal sealed partial class CrawlController
    {
        // Takes the prepared inventory and persists an origin before moving the player.
        private void FinishPreparation()
        {
            if (!_preparation.TryTake(out var store)) { return; }
            _preparation.Dispose();
            _preparation = null;
            _store = store;
            SelectionReport.Write(store, _log);
            if (!string.IsNullOrEmpty(store.RecoveryNotice)) { _log.LogWarning(store.RecoveryNotice); }
            _log.LogInfo($"Export: {store.DirectoryPath}; {store.Manifest.Zones.Count} zones; personal pixels={store.Manifest.PersonalPixels}; shared={store.Manifest.SharedPixels}.");
            if (_settings.InventoryOnly.Value && (_flight == null || !_flight.Active) ||
                (_pauseRequested || !Pending()) && !store.Manifest.ReturnPending)
            {
                _phase = Pending() ? CrawlPhase.Paused : CrawlPhase.Completed;
                SessionCheckpoint.SetState(store, _phase == CrawlPhase.Completed ? "completed" : "paused", null);
                Say($"Inventory saved: {store.Manifest.Zones.Count} zones.");
                CloseStore();
                return;
            }
            if (_flight == null)
            {
                _flight = new FlightController { Speed = Mathf.Clamp(_settings.Speed.Value, 5f, 50f) };
                _flight.Begin(_player);
            }
            SessionCheckpoint.Begin(store, _player);
            _navigation = new TravelNavigator(_flight, _player, _settings.Clearance.Value,
                _settings.PreferPortals.Value, _settings.CruiseSpeed.Value, _settings.CruiseThreshold.Value,
                _settings.AllowCoordinateJumps.Value, _settings.CoordinateJumpThreshold.Value);
            BeginContinuousFlight();
            _log.LogInfo(_flight.AchievementSummary);
            if (_settings.InventoryOnly.Value || _pauseRequested || !Pending()) { ReturnHome(); return; }
            _testTarget = _player.transform.position + Vector3.up * 15f;
            _testUntil = 0f;
            _phase = CrawlPhase.Testing;
            Say("Testing movement, then starting the automatic route. Press F8 to pause and return.");
        }

        // Holds a recovered airborne player before large export files are checked on a worker.
        private void HoldRecovery(WorldStore store)
        {
            if (!store.Manifest.ReturnPending) { return; }
            _flight = new FlightController { Speed = Mathf.Clamp(_settings.Speed.Value, 5f, 50f) };
            _flight.Begin(_player, SessionCheckpoint.Origin(store), SessionCheckpoint.Rotation(store));
        }
    }
}
