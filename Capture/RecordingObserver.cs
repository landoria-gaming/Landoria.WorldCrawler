using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Coalesces received changes and already-loaded state within the native near-zone scope.
    internal sealed class RecordingObserver : IDisposable
    {
        private static RecordingObserver _current;
        private readonly CaptureApi _api = new CaptureApi();
        private readonly CaptureReceivePolicy _policy = new CaptureReceivePolicy();
        private readonly Dictionary<ZDOID, uint> _sampledRevision = new Dictionary<ZDOID, uint>();
        private readonly Queue<ZDOID> _received = new Queue<ZDOID>();
        private readonly HashSet<ZDOID> _queued = new HashSet<ZDOID>();
        private readonly Dictionary<ZDOID, string> _fingerprints = new Dictionary<ZDOID, string>();
        private readonly Dictionary<ZDOID, string> _objectZones = new Dictionary<ZDOID, string>();
        private readonly Dictionary<string, long> _revisions = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _saved = new Dictionary<string, long>();
        private readonly List<ZDO> _sample = new List<ZDO>();
        private readonly Queue<ZoneEntry> _scan = new Queue<ZoneEntry>();
        private float _nextScan;
        private int _scopeRevision = -1;
        private long _revision;
        private readonly Dictionary<string, List<CapturedDeletion>> _deletions = new Dictionary<string, List<CapturedDeletion>>();
        private readonly Dictionary<ZDOID, int> _prefabs = new Dictionary<ZDOID, int>();
        private readonly Dictionary<string, List<CapturedDeparture>> _departures = new Dictionary<string, List<CapturedDeparture>>();
        public NearZoneScope Scope { get; } = new NearZoneScope();
        public int Pending => _revisions.Count(pair => !_saved.TryGetValue(pair.Key, out var saved) || saved != pair.Value);
        public bool HasQueuedObjects => _received.Count != 0;
        public string Error { get; private set; }

        // Installs a passive receiver; startup sweeps also include unchanged objects already in memory.
        public RecordingObserver()
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A recording observer is already active.");
            }
            _current = this;
        }

        // Queues IDs after deserialization; expensive serialization runs under the frame budget.
        public static void Received(ZDO source)
        {
            var observer = _current;
            if (observer == null || source == null || !source.IsValid() || !source.Persistent)
            {
                return;
            }
            try
            {
                var point = source.GetPosition();
                var x = Mathf.FloorToInt((point.x + 32f) / 64f);
                var z = Mathf.FloorToInt((point.z + 32f) / 64f);
                if ((observer.Scope.Contains(x, z) || observer._objectZones.ContainsKey(source.m_uid)) && observer._policy.For(source.GetPrefab()) != 0 &&
                    observer._queued.Add(source.m_uid))
                {
                    observer._received.Enqueue(source.m_uid);
                }
            }
            catch (Exception error)
            {
                observer.Error = error.Message;
            }
        }

        // Refreshes the real near scope and processes bounded local observations.
        public void Step(Vector3 position)
        {
            if (Error != null)
            {
                throw new InvalidOperationException("Recording observer: " + Error);
            }
            Scope.Refresh(position);
            if (_scopeRevision != Scope.Revision)
            {
                UpdateScope();
                _scopeRevision = Scope.Revision;
                _scan.Clear();
                _nextScan = 0f;
            }
            Sweep();
            var clock = Stopwatch.StartNew();
            for (var count = 0; _received.Count > 0 && count < 40 && clock.ElapsedMilliseconds < 4; count++)
            {
                var id = _received.Dequeue();
                _queued.Remove(id);
                var source = ZDOMan.instance.GetZDO(id);
                if (source != null && source.IsValid() && source.Persistent)
                {
                    Observe(source);
                }
            }
        }

        // Forgets visit bookkeeping outside coverage, but never treats departure as a deletion.
        private void UpdateScope()
        {
            var keys = new HashSet<string>(Scope.Zones.Select(zone => NearZoneScope.Key(zone.X, zone.Z)));
            foreach (var key in _revisions.Keys.Where(key => !keys.Contains(key)).ToArray())
            {
                _revisions.Remove(key);
                _saved.Remove(key);
            }
            foreach (var id in _objectZones.Where(pair => !keys.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            {
                _objectZones.Remove(id);
                _fingerprints.Remove(id);
                _prefabs.Remove(id);
                _sampledRevision.Remove(id);
            }
            foreach (var zone in Scope.Zones)
            {
                var key = NearZoneScope.Key(zone.X, zone.Z);
                if (!_revisions.ContainsKey(key))
                {
                    _revisions.Add(key, ++_revision);
                }
            }
        }

        // Reads one sector per frame and periodically catches local-owner changes missed by network hooks.
        private void Sweep()
        {
            if (_scan.Count == 0 && Time.realtimeSinceStartup >= _nextScan)
            {
                foreach (var zone in Scope.Zones)
                {
                    _scan.Enqueue(zone);
                }
                _nextScan = Time.realtimeSinceStartup + 1f;
            }
            if (_scan.Count == 0)
            {
                return;
            }
            var next = _scan.Dequeue();
            _api.FindObjects(new Vector3(next.X * 64f, 0f, next.Z * 64f), _sample);
            foreach (var source in _sample)
            {
                if (source != null && (!_sampledRevision.TryGetValue(source.m_uid, out var revision) || revision != source.DataRevision))
                {
                    Received(source);
                }
            }
        }

        // Accepts only included, changed serialized state before holding movement or dirtying a sector.
        private void Observe(ZDO source)
        {
            _api.GetZone(source.GetPosition(), out var x, out var z);
            RecordDeparture(source, x, z);
            if (!Scope.Contains(x, z) || _policy.For(source.GetPrefab()) == 0)
            {
                return;
            }
            _sampledRevision[source.m_uid] = source.DataRevision;
            var fingerprint = ObservationFingerprint.Read(source);
            if (_fingerprints.TryGetValue(source.m_uid, out var old) && old == fingerprint)
            {
                return;
            }
            var key = NearZoneScope.Key(x, z);
            _prefabs[source.m_uid] = source.GetPrefab();
            _fingerprints[source.m_uid] = fingerprint;
            _objectZones[source.m_uid] = key;
            _revisions[key] = ++_revision;
            CaptureReceiveWatch.Changed(x, z);
            ReceiveMotionGate.Worked();
        }

        // Dirties the origin even when an object moves beyond near coverage; departure is not destruction.
        private void RecordDeparture(ZDO source, int x, int z)
        {
            var key = NearZoneScope.Key(x, z);
            if (!_objectZones.TryGetValue(source.m_uid, out var previous) || previous == key || !_revisions.ContainsKey(previous))
            {
                return;
            }
            if (!_departures.TryGetValue(previous, out var records))
            {
                records = new List<CapturedDeparture>();
                _departures.Add(previous, records);
            }
            records.Add(new CapturedDeparture { SourceUser = source.m_uid.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SourceId = source.m_uid.ID, PrefabHash = _prefabs[source.m_uid], DestinationX = x, DestinationZ = z,
                ObservedUtcTicks = DateTime.UtcNow.Ticks });
            _revisions[previous] = ++_revision;
            _objectZones[source.m_uid] = key;
            var parts = previous.Split(':');
            CaptureReceiveWatch.Changed(int.Parse(parts[0]), int.Parse(parts[1]));
            ReceiveMotionGate.Worked();
        }

        // Copies departure evidence; it never authorizes exporting an incomplete destination sector.
        public List<CapturedDeparture> Departures(ZoneEntry zone)
        {
            return _departures.TryGetValue(NearZoneScope.Key(zone.X, zone.Z), out var records) ?
                records.ToList() : new List<CapturedDeparture>();
        }

        // Picks nearby work only; this never selects a destination for the character.
        public ZoneEntry Next(Vector3 position)
        {
            return Scope.Zones.Where(zone =>
                !_saved.TryGetValue(NearZoneScope.Key(zone.X, zone.Z), out var saved) ||
                saved != Revision(zone)).OrderBy(zone =>
                    (new Vector2(zone.X * 64f, zone.Z * 64f) - new Vector2(position.x, position.z)).sqrMagnitude).FirstOrDefault();
        }

        // Captures the accepted revision at a validation boundary.
        public long Revision(ZoneEntry zone)
        {
            return _revisions.TryGetValue(NearZoneScope.Key(zone.X, zone.Z), out var value) ? value : -1;
        }

        // Acknowledges exactly the revision written, keeping later arrivals pending.
        public void Committed(ZoneEntry zone, long revision)
        {
            _saved[NearZoneScope.Key(zone.X, zone.Z)] = revision;
        }

        // Dirties a known sector on an explicit native destruction, never on scene unload.
        public static void Destroyed(ZDOID id)
        {
            var observer = _current;
            if (observer != null && observer._objectZones.TryGetValue(id, out var key))
            {
                if (!observer._deletions.TryGetValue(key, out var list))
                {
                    list = new List<CapturedDeletion>();
                    observer._deletions.Add(key, list);
                }
                list.Add(new CapturedDeletion { SourceUser = id.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SourceId = id.ID, PrefabHash = observer._prefabs[id], ObservedUtcTicks = DateTime.UtcNow.Ticks });
                observer._revisions[key] = ++observer._revision;
                observer._fingerprints.Remove(id);
                observer._objectZones.Remove(id);
                observer._prefabs.Remove(id);
                observer._sampledRevision.Remove(id);
                var coordinates = key.Split(':');
                CaptureReceiveWatch.Changed(int.Parse(coordinates[0]), int.Parse(coordinates[1]));
                ReceiveMotionGate.Worked();
            }
        }

        // Copies confirmed removals into a fully validated sector commit.
        public List<CapturedDeletion> Deletions(ZoneEntry zone)
        {
            return _deletions.TryGetValue(NearZoneScope.Key(zone.X, zone.Z), out var records) ?
                records.ToList() : new List<CapturedDeletion>();
        }

        // Detaches callbacks before releasing the recording folder.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
