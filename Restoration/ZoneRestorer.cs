using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Restores one loaded sector and reconciles known generated objects against complete captures.
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
        private bool _locationCleanup;
        private float _readySince;
        private readonly HashSet<string> _layouts = new HashSet<string>();
        public bool Done
        {
            get; private set;
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
                Landoria.WorldCrawler.Flight.ReceiveMotionGate.Worked();
                _index++;
            }
            if (_index != _records.Count)
            {
                return;
            }
            ReconcileGeneratedLocations();
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
            Done = _verify == _records.Count;
        }

        // Removes native generated locations that were absent from the fully observed source sector.
        private void ReconcileGeneratedLocations()
        {
            if (_locationCleanup)
            {
                return;
            }
            foreach (var deletion in _snapshot.Deletions ?? new List<CapturedDeletion>())
            {
                _objects.ApplyDeletion(deletion);
            }
            foreach (var departure in _snapshot.Departures ?? new List<CapturedDeparture>())
            {
                if (!_records.Any(record => record.SourceUser == departure.SourceUser && record.SourceId == departure.SourceId &&
                    record.ObservedUtcTicks > departure.ObservedUtcTicks))
                {
                    _objects.ApplyDeparture(departure, _snapshot.ZoneX, _snapshot.ZoneZ);
                }
            }
            var removed = _objects.CleanupZone(_snapshot.ZoneX, _snapshot.ZoneZ);
            if (removed > 0)
            {
                Landoria.WorldCrawler.Flight.ReceiveMotionGate.Worked();
                _warning("Removed " + removed + " absent known generated objects in " +
                    _snapshot.ZoneX + ":" + _snapshot.ZoneZ + ".");
            }
            _locationCleanup = true;
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
            if (instance == null)
            {
                return false;
            }
            VerifyPosition(source, target);
            if (source.LocalScale != null)
            {
                instance.SetLocalScale(ObjectRestorer.Vector(source.LocalScale));
            }
            if (source.LocationHash != 0 && !_layouts.Contains(ExportArchive.Key(source)))
            {
                var proxy = instance.GetComponent<LocationProxy>();
                if (proxy == null || new CaptureApi().GetLocationInstance(proxy) == null)
                {
                    return false;
                }
                LocationRegistry.Apply(source);
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
