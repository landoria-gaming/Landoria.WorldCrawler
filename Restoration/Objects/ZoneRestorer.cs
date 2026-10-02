using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Restoration.Persistence;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Restores one loaded sector and removes extra objects of globally exported types.
    internal sealed class ZoneRestorer
    {
        private readonly ObjectRestorer _objects;
        private readonly List<CapturedObject> _records;
        private readonly ZoneSnapshot _snapshot;
        private readonly string _version;
        private readonly Action<string> _warning;
        private readonly Vector3 _center;
        private readonly float _started;
        private int _index;
        private int _verify;
        private float _nextCleanup;
        private float _readySince;
        private readonly HashSet<string> _layouts = new HashSet<string>();
        public bool Done
        {
            get; private set;
        }

        // Summarizes the prefabs processed for this zone after validation succeeds.
        internal ZoneSaveReport Report()
        {
            var groups = _records.Where(_objects.WasApplied).GroupBy(item => item.PrefabName).OrderBy(group => group.Key).ToList();
            return new ZoneSaveReport
            {
                X = _snapshot.ZoneX,
                Z = _snapshot.ZoneZ,
                Added = groups.Select(group => new CaptureCount
                    { Name = group.Key, Count = group.Count() }).ToList(),
                Categories = groups.ToDictionary(group => group.Key, group => string.Join(", ",
                    group.SelectMany(item => item.Categories ?? new string[0]).Distinct())),
                PrefabHashes = groups.ToDictionary(group => group.Key, group => group.First().PrefabHash)
            };
        }

        // Receives already decoded data so disk work never blocks a Unity frame.
        public ZoneRestorer(ObjectRestorer objects, List<CapturedObject> records, ZoneSnapshot snapshot,
            ZoneEntry zone, Action<string> warning)
        {
            _objects = objects;
            _warning = warning;
            _records = records.Where(RestoreRecordPolicy.Include).ToList();
            if (_records.Count != records.Count)
            {
                _warning("Ignored the engine-owned zone controller; Valheim keeps its generated destination controller.");
            }
            _snapshot = snapshot;
            _version = zone.CaptureVersion;
            _center = new Vector3(zone.X * 64f, 0f, zone.Z * 64f);
            _started = Time.unscaledTime;
            _objects.BeginZone(zone.X, zone.Z);
            RestoreProtection.Hold(zone.X, zone.Z);
            foreach (var record in _records)
            {
                ObjectRestorer.Validate(record);
            }
        }

        // Budgets writes and waits for the engine to instantiate and load the resulting objects.
        public void Step(int budget)
        {
            if (Time.unscaledTime - _started > 240f)
            {
                throw new TimeoutException("Restored zone did not become ready.");
            }
            var watch = Stopwatch.StartNew();
            while (_index < _records.Count && budget-- > 0 && watch.ElapsedMilliseconds < 4)
            {
                _objects.Restore(_records[_index], _version);
                _index++;
            }
            if (_index != _records.Count)
            {
                return;
            }
            ReconcileSource();
            if (!ReadyForVerification())
            {
                return;
            }
            while (_verify < _records.Count && budget-- > 0 && watch.ElapsedMilliseconds < 4)
            {
                if (!Verify(_records[_verify]))
                {
                    return;
                }
                _verify++;
            }
            Done = _verify == _records.Count && Cleanup() == 0 && !_objects.DeletionsPending();
        }

        // Rechecks live generated arrivals until restoration has settled.
        private void ReconcileSource()
        {
            if (Time.unscaledTime >= _nextCleanup)
            {
                Cleanup();
                _nextCleanup = Time.unscaledTime + 0.5f;
            }
        }

        // Restarts scene stabilization whenever source-authoritative cleanup removes an extra copy.
        private int Cleanup()
        {
            var removed = _objects.CleanupZone(_snapshot.ZoneX, _snapshot.ZoneZ);
            if (removed > 0)
            {
                _readySince = 0f;
            }
            return removed;
        }


        // Waits for native scene and terrain stabilization before validating restored views.
        private bool ReadyForVerification()
        {
            if (_objects.DeletionsPending() || !NearZoneScope.Ready(_snapshot.ZoneX, _snapshot.ZoneZ))
            {
                return false;
            }
            var heightmap = Heightmap.FindHeightmap(_center);
            if (heightmap == null || heightmap.IsDistantLod || heightmap.HaveQueuedRebuild())
            {
                _readySince = 0f;
                return false;
            }
            if (_readySince == 0f)
            {
                _readySince = Time.unscaledTime;
            }
            return Time.unscaledTime - _readySince >= 2f;
        }

        // Confirms identity, pose and live view before considering an object restorable on disk.
        private bool Verify(CapturedObject source)
        {
            var target = _objects.Resolve(source);
            if (target == null)
            {
                throw new InvalidOperationException("Imported identity is missing during validation; " + RestoreWarnings.Describe(source));
            }
            var instance = ZNetScene.instance.FindInstance(target);
            if (target.GetBool("WorldCrawler.initialized", true))
            {
                if (source.LocationHash != 0 && instance != null)
                {
                    var proxy = instance.GetComponent<LocationProxy>();
                    _objects.CleanupMerchantDuplicates(source, proxy == null ? null : new CaptureApi().GetLocationInstance(proxy));
                }
                return true;
            }
            if (instance == null)
            {
                return false;
            }
            VerifyPosition(source, target);
            if (source.LocalScale != null)
            {
                instance.SetLocalScale(ObjectRestorer.Vector(source.LocalScale));
            }
            if (!InitializeLocation(source, target, instance))
            {
                return false;
            }
            target.Set("WorldCrawler.initialized", true);
            return true;
        }

        // Attaches captured static layout only during the original import, including interrupted imports.
        private bool InitializeLocation(CapturedObject source, ZDO target, ZNetView instance)
        {
            if (source.LocationHash != 0 && !_layouts.Contains(ExportArchive.Key(source)))
            {
                var proxy = instance.GetComponent<LocationProxy>();
                if (proxy == null || new CaptureApi().GetLocationInstance(proxy) == null)
                {
                    return false;
                }
                LocationRegistry.Apply(source);
                _objects.CleanupMerchantDuplicates(source, new CaptureApi().GetLocationInstance(proxy));
                var warning = LocationLayout.Attach(target, source, _snapshot);
                if (warning != null)
                {
                    _warning(RestoreWarnings.Describe(source) + "; " + warning);
                }
                _layouts.Add(ExportArchive.Key(source));
            }
            return true;
        }

        // Classifies native prefab behavior before distinguishing settled drops from misplaced structures.
        private void VerifyPosition(CapturedObject source, ZDO target)
        {
            var distance = Vector3.Distance(target.GetPosition(), ObjectRestorer.Vector(source.Position));
            var prefab = ZNetScene.instance.GetPrefab(source.PrefabHash);
            var itemDrop = prefab.GetComponent<ItemDrop>() != null;
            var sync = prefab.GetComponent<ZSyncTransform>();
            var placedItem = itemDrop && target.GetBool("piece", false);
            var movable = RestorePositionPolicy.IsMovable(itemDrop, placedItem, sync != null && sync.m_syncPosition);
            if (!RestorePositionPolicy.Accepts(distance, movable))
            {
                throw new InvalidOperationException("Imported position is invalid; " + RestoreWarnings.Describe(source)
                    + "; displacement=" + distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + "m; movable=" + movable + "; itemDrop=" + itemDrop + "; placedItem=" + placedItem + ".");
            }
            if (distance > RestorePositionPolicy.FixedTolerance)
            {
                _warning(RestoreWarnings.Describe(source) + "; dynamic object settled "
                    + distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + "m from its captured position; identity is intact.");
            }
        }
    }
}
