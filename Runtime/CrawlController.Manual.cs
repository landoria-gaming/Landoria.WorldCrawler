using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Flushes detached batches without waiting for server silence or freezing the player.
    internal sealed partial class CrawlController
    {
        // Serializes disk batches while reception continues in a separate cache.
        private void Advance()
        {
            if (_store == null)
            {
                if (!_preparation.TryTake(out _store))
                {
                    return;
                }
                _preparation.Dispose();
                _preparation = null;
                _phase = CrawlPhase.Waiting;
                PublishCommitted();
            }
            if (_write != null)
            {
                FinishWrite();
                return;
            }
            if (Time.realtimeSinceStartup < _nextFlush)
            {
                return;
            }
            _batch = _batch ?? _observer.TakeBatch();
            if (_batch.Count == 0)
            {
                _batch = null;
                _nextFlush = Time.realtimeSinceStartup + CrawlerConstants.RecordingFlushInterval;
                if (_stop)
                {
                    Finish();
                }
                return;
            }
            BeginWrite();
        }

        // Owns the current detached batch until every sector has been durably committed.
        private void BeginWrite()
        {
            var batch = _batch;
            var store = _store;
            var version = _version;
            _log.LogInfo("Recording flush started: zones=" + batch.Count +
                "; objects=" + batch.Sum(zone => zone.Objects.Count) + "; final=" + _stop + ".");
            _write = Task.Run(() => RecordingFlush.Write(store, batch, version));
            _nextFlush = Time.realtimeSinceStartup + CrawlerConstants.RecordingFlushInterval;
            _phase = CrawlPhase.Writing;
        }

        // Preserves the same batch on failure and only publishes progress after successful disk writes.
        private void FinishWrite()
        {
            if (!_write.IsCompleted)
            {
                return;
            }
            var task = _write;
            _write = null;
            _phase = CrawlPhase.Waiting;
            task.GetAwaiter().GetResult();
            foreach (var item in _batch.SelectMany(zone => zone.Objects))
            {
                _savedObjectIds.Add(item.SourceUser + ":" + item.SourceId);
            }
            foreach (var zone in _batch)
            {
                _savedZones.Add(zone.ZoneX + ":" + zone.ZoneZ);
            }
            _log.LogInfo("Recording flush saved: zones=" + _batch.Count +
                "; newlyCachedZones=" + _observer.Pending + ".");
            _batch = null;
            PublishCommitted();
            if (_stop)
            {
                _nextFlush = 0f;
            }
        }

        // Copies manifest geometry only when no background write can modify it.
        private void PublishCommitted()
        {
            _committed = _store.Manifest.Zones.Where(zone => zone.Status == "captured")
                .Select(zone => new ZoneEntry { X = zone.X, Z = zone.Z, Status = "captured" }).ToList();
            RefreshMap();
        }

        // Marks buffered replacements amber without erasing their older durable files.
        private void RefreshMap()
        {
            var dirty = _observer.PendingZones().ToList();
            if (_batch != null)
            {
                dirty.AddRange(_batch.Select(zone => new ZoneEntry { X = zone.ZoneX, Z = zone.ZoneZ, Status = "pending" }));
            }
            _map = ProgressMapSnapshot.Recording(_committed, dirty);
        }
    }
}
