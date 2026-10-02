using System.Collections.Generic;
using Landoria.WorldCrawler.Restoration.Objects;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps disposable session state in memory; only Completed is saved separately.
    internal sealed class RestoreState
    {
        public readonly List<string> Completed = new List<string>();
        public readonly List<RestoredObject> Objects = new List<RestoredObject>();
        public readonly List<string> Warnings = new List<string>();
    }
}
