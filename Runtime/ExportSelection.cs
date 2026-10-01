using System;
using System.Globalization;
using Landoria.WorldCrawler.Inventory;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Runtime
{
    // Freezes main-thread observations and applies a focused itinerary on a worker thread.
    internal sealed class ExportSelection
    {
        private readonly float _radius;
        private MapSnapshot _map;
        private LandmarkInventory _landmarks;
        public bool IsLandmarks { get; }
        public string Scope { get; }

        // Gives each radius a separate directory without modifying existing full-map captures.
        public ExportSelection(CrawlerSettings settings, WorldIdentity world)
        {
            if (!Enum.IsDefined(typeof(ExportSelectionMode), settings.SelectionMode.Value))
            { throw new InvalidOperationException("Unknown export selection mode."); }
            IsLandmarks = settings.SelectionMode.Value == ExportSelectionMode.Landmarks;
            _radius = settings.LandmarkRadius.Value;
            if (_radius < 1 || _radius > 1000) { throw new InvalidOperationException("Invalid landmark radius."); }
            Scope = IsLandmarks ? "landmarks_r" + settings.LandmarkRadius.Value.ToString(CultureInfo.InvariantCulture) : null;
            SelectionRecovery.Check(settings.ExportRoot.Value, world, Scope);
        }

        // Reads a fresh exploration map only when initializing a full-map itinerary.
        public void ReadMap(WorldStore store)
        { if (!store.Manifest.InventoryInitialized) { _map = MapSnapshot.Read(); } }

        // Takes eligible locations after bounded native portal-destination requests have finished.
        public void ReadLandmarks(LandmarkPortalResolver resolver)
        {
            _landmarks = LandmarkSnapshot.Read();
            _landmarks.Warnings.AddRange(resolver.Warnings);
        }

        // Merges known landmarks without copying captures from another export directory.
        public void Apply(WorldStore store, string id, string name)
        {
            if (IsLandmarks)
            {
                store.SelectLandmarks(_landmarks, _radius, id, name);
            }
            else if (store.Manifest.InventoryInitialized) { store.ValidateCharacter(id); }
            else { store.InitializeInventory(_map.Build(), id, name); }
        }
    }
}
