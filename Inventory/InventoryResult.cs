using System.Collections.Generic;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Inventory
{
    // Contains the deterministic union of personal and shared map exploration.
    public sealed class InventoryResult
    {
        public List<ZoneEntry> Zones { get; set; } = new List<ZoneEntry>();
    }
}
