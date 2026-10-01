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
            if (!_preparation.TryTake(out var store))
            {
                return;
            }
            _preparation.Dispose();
            _preparation = null;
            _store = store;
            SelectionReport.Write(store, _log);
            if (!string.IsNullOrEmpty(store.RecoveryNotice))
            {
                _log.LogWarning(store.RecoveryNotice);
            }
            _log.LogInfo($"Landmark export: {store.DirectoryPath}; {store.Manifest.Zones.Count} zones; radius={store.Manifest.Selection?.Radius}m.");
            if ((_pauseRequested || !Pending()) && !store.Manifest.ReturnPending)
            {
                _phase = Pending() ? CrawlPhase.Paused : CrawlPhase.Completed;
                SessionCheckpoint.SetState(store, _phase == CrawlPhase.Completed ? "completed" : "paused", null);
                Say($"Inventory saved: {store.Manifest.Zones.Count} zones.");
                CloseStore();
                return;
            }
            StartPreparedFlight(store);
        }

        // Starts the protected flight only after the inventory and return checkpoint are ready.
        private void StartPreparedFlight(WorldStore store)
        {
            if (_flight == null)
            {
                _flight = new FlightController { Speed = CrawlerConstants.Speed };
                _flight.Begin(_player);
            }
            SessionCheckpoint.Begin(store, _player);
            _navigation = new TravelNavigator(_flight, _player, CrawlerConstants.Clearance,
                CrawlerConstants.PreferPortals, CrawlerConstants.CruiseSpeed, CrawlerConstants.CruiseThreshold,
                CrawlerConstants.AllowCoordinateJumps, CrawlerConstants.CoordinateJumpThreshold);
            BeginContinuousFlight();
            _log.LogInfo(_flight.AchievementSummary);
            if (_pauseRequested || !Pending())
            {
                ReturnHome();
                return;
            }
            _testTarget = _player.transform.position + Vector3.up * 15f;
            _testUntil = 0f;
            _phase = CrawlPhase.Testing;
            Say("Testing movement, then starting the automatic route. Press F8 to pause and return.");
        }

        // Holds a recovered airborne player before large export files are checked on a worker.
        private void HoldRecovery(WorldStore store)
        {
            if (!store.Manifest.ReturnPending)
            {
                return;
            }
            _flight = new FlightController { Speed = CrawlerConstants.Speed };
            _flight.Begin(_player, SessionCheckpoint.Origin(store), SessionCheckpoint.Rotation(store));
        }
    }
}
