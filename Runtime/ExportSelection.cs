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
        private LandmarkInventory _landmarks;
        public string Scope
        {
            get;
        }

        // Gives each landmark radius a separate resumable directory.
        public ExportSelection(WorldIdentity world)
        {
            _radius = CrawlerConstants.LandmarkRadius;
            if (_radius < 1 || _radius > 1000)
            {
                throw new InvalidOperationException("Invalid landmark radius.");
            }
            Scope = "landmarks_r" + CrawlerConstants.LandmarkRadius.ToString(CultureInfo.InvariantCulture);
            SelectionRecovery.Check(CrawlerConstants.ExportRoot, world, Scope);
        }

        // Takes eligible locations after bounded native portal-destination requests have finished.
        public void ReadLandmarks(LandmarkPortalResolver resolver)
        {
            _landmarks = LandmarkSnapshot.Read();
            _landmarks.Warnings.AddRange(resolver.Warnings);
        }

        // Merges known landmarks without copying captures from another export directory.
        public void Apply(WorldStore store, string id, string name)
        {
            store.SelectLandmarks(_landmarks, _radius, id, name);
        }
    }
}
