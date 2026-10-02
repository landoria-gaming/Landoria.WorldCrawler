using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Storage
{
    // Describes new source objects after one zone file was committed.
    internal sealed class ZoneSaveReport
    {
        internal int X;
        internal int Z;
        internal List<CaptureCount> Added = new List<CaptureCount>();
        internal Dictionary<string, string> Categories = new Dictionary<string, string>();
        internal Dictionary<string, int> PrefabHashes = new Dictionary<string, int>();
    }
}
