using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Reports received data and flush errors without controlling recording or movement.
    internal sealed partial class CrawlController
    {
        // Counts each unsaved source object once across the live cache and the in-flight batch.
        private HashSet<string> CachedObjectKeys()
        {
            var pending = _observer.PendingObjectKeys();
            var writing = _batch == null ? Enumerable.Empty<string>() :
                _batch.SelectMany(zone => zone.Objects).Select(item => item.SourceUser + ":" + item.SourceId);
            return new HashSet<string>(pending.Concat(writing));
        }

        // Distinguishes data cached in memory from batches confirmed on disk.
        private void LogRecordingProgress()
        {
            if (Time.realtimeSinceStartup < _nextDiagnostic)
            {
                return;
            }
            _nextDiagnostic = Time.realtimeSinceStartup + 5f;
            var message = "Recording diagnostic: phase=" + _phase + "; flushingZones=" +
                (_batch?.Count ?? 0) + "; diskWorker=" + (_write?.Status.ToString() ?? "idle") +
                "; movement=unrestricted; " + _observer.Describe();
            if (_observer.Errors > _reportedErrors)
            {
                _reportedErrors = _observer.Errors;
                _log.LogWarning(message);
            }
            else
            {
                _log.LogInfo(message);
            }
        }
    }
}
