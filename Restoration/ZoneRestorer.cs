using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Restores and verifies one loaded sector over multiple frames without deleting absent objects.
    internal sealed class ZoneRestorer
    {
        private readonly ObjectRestorer _objects;
        private readonly List<CapturedObject> _records;
        private readonly ZoneSnapshot _snapshot;
        private readonly string _version;
        private readonly RestoreState _state;
        private readonly Vector3 _center;
        private readonly float _started;
        private int _index;
        private int _verify;
        private bool _naturalCleanup;
        private float _readySince;
        private readonly HashSet<string> _layouts = new HashSet<string>();
        public bool Done { get; private set; }

        // Receives already decoded data so disk work never blocks a Unity frame.
        public ZoneRestorer(ObjectRestorer objects, List<CapturedObject> records, ZoneSnapshot snapshot,
            ZoneEntry zone, RestoreState state)
        {
            _objects = objects; _records = records; _snapshot = snapshot; _version = zone.CaptureVersion; _state = state;
            _center = new Vector3(zone.X * 64f, 0f, zone.Z * 64f); _started = Time.unscaledTime;
            _objects.BeginZone(zone.X, zone.Z);
            RestoreProtection.Hold(zone.X, zone.Z);
            foreach (var record in records) { ObjectRestorer.Validate(record); }
        }

        // Budgets writes and waits for the engine to instantiate and load the resulting objects.
        public void Step(int budget)
        {
            if (Time.unscaledTime - _started > 240f) { throw new TimeoutException("Restored zone did not become ready."); }
            var watch = Stopwatch.StartNew();
            while (_index < _records.Count && budget-- > 0 && watch.ElapsedMilliseconds < 4)
            {
                _objects.Restore(_records[_index], _version);
                _index++;
            }
            if (_index != _records.Count) { return; }
            if (!_naturalCleanup)
            {
                var removed = _objects.RemoveAbsentNaturalResources(_snapshot.NaturalAbsenceComplete);
                if (removed > 0)
                {
                    var warning = "Removed " + removed + " absent generated natural resources from "
                        + _snapshot.ZoneX + ":" + _snapshot.ZoneZ + ".";
                    if (!_state.Warnings.Contains(warning)) { _state.Warnings.Add(warning); }
                }
                _naturalCleanup = true;
            }
            if (!ZNetScene.instance.IsAreaReady(_center)) { return; }
            var heightmap = Heightmap.FindHeightmap(_center);
            if (heightmap == null || heightmap.IsDistantLod || heightmap.HaveQueuedRebuild())
            { _readySince = 0f; return; }
            if (_readySince == 0f) { _readySince = Time.unscaledTime; }
            if (Time.unscaledTime - _readySince < 3f) { return; }
            while (_verify < _records.Count && budget-- > 0 && watch.ElapsedMilliseconds < 4)
            {
                if (!Verify(_records[_verify])) { return; }
                _verify++;
            }
            Done = _verify == _records.Count;
        }

        // Confirms identity, pose and live view before considering an object restorable on disk.
        private bool Verify(CapturedObject source)
        {
            var target = _objects.Resolve(source);
            if (target == null || Vector3.Distance(target.GetPosition(), ObjectRestorer.Vector(source.Position)) > 0.1f)
            { throw new InvalidOperationException("An imported object moved or disappeared during validation: " + source.PrefabName); }
            var instance = ZNetScene.instance.FindInstance(target);
            if (instance == null) { return false; }
            if (source.LocalScale != null) { instance.SetLocalScale(ObjectRestorer.Vector(source.LocalScale)); }
            if (source.LocationHash != 0 && !_layouts.Contains(ExportArchive.Key(source)))
            {
                var proxy = instance.GetComponent<LocationProxy>();
                if (proxy == null || new CaptureApi().GetLocationInstance(proxy) == null) { return false; }
                LocationRegistry.Apply(source);
                var warning = LocationLayout.Attach(target, source, _snapshot);
                if (warning != null && !_state.Warnings.Contains(warning)) { _state.Warnings.Add(warning); }
                _layouts.Add(ExportArchive.Key(source));
            }
            return true;
        }
    }
}
