using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Transfers checked zone data from a file-reading worker to the Unity thread.
    internal sealed class ZoneImportData
    {
        public ZoneSnapshot Snapshot;
        public List<CapturedObject> Records;
    }
}
