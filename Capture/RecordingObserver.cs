using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Copies received state immediately so native unloads cannot discard pending disk data.
    internal sealed partial class RecordingObserver : IDisposable
    {
        private static RecordingObserver _current;
        private readonly CaptureApi _api = new CaptureApi();
        private readonly ObjectCapture _reader;
        private readonly CaptureReceivePolicy _policy = new CaptureReceivePolicy();
        private readonly Dictionary<ZDOID, RecordingStamp> _known = new Dictionary<ZDOID, RecordingStamp>();
        private readonly HashSet<ZDOID> _destroyed = new HashSet<ZDOID>();
        private Dictionary<string, RecordingZone> _pending = new Dictionary<string, RecordingZone>();
        private readonly Queue<ZDOID> _sweep = new Queue<ZDOID>();
        private readonly FieldInfo _objects = typeof(ZDOMan).GetField("m_objectsByID", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly long _worldUid;
        private float _nextSweep;
        private long _ticks;
        private bool _frozen;
        public int Pending => _pending.Count;
        public int PendingObjects => _pending.Values.Sum(zone => zone.Objects.Count);
        public long Errors { get; private set; }
        public string LastError { get; private set; }

        // Activates capture before the disk store is opened, retaining arrivals during preparation.
        public RecordingObserver(long worldUid)
        {
            if (_current != null || _objects == null ||
                _objects.FieldType != typeof(Dictionary<ZDOID, ZDO>))
            {
                throw new InvalidOperationException("Recording is already active or the native object catalogue is unavailable.");
            }
            _reader = new ObjectCapture(_api);
            _worldUid = worldUid;
            _current = this;
        }

        // Copies the complete deserialized object, never merely queuing a mutable ZDO reference.
        public static void Received(ZDO source, bool fromNetwork = true, bool force = false)
        {
            var observer = _current;
            if (observer == null || ZNet.World == null || ZNet.World.m_uid != observer._worldUid)
            {
                return;
            }
            observer._diagnostics.Received(fromNetwork);
            try
            {
                observer.Record(source, fromNetwork || force);
            }
            catch (Exception error)
            {
                observer.Errors++;
                observer.LastError = "Object " + source?.m_uid + ": " + error.Message;
            }
        }

        // Includes local-owner edits and objects present before F8 without controlling movement.
        public void Step(Vector3 position)
        {
            if (_frozen || ZDOMan.instance == null)
            {
                return;
            }
            if (_sweep.Count == 0 && Time.realtimeSinceStartup >= _nextSweep)
            {
                foreach (var id in AllObjects().Keys.ToArray())
                {
                    _sweep.Enqueue(id);
                }
                _nextSweep = Time.realtimeSinceStartup + 1f;
            }
            var clock = Stopwatch.StartNew();
            for (var count = 0; _sweep.Count > 0 && count < 40 && clock.ElapsedMilliseconds < 4; count++)
            {
                Received(ZDOMan.instance.GetZDO(_sweep.Dequeue()), false);
            }
            ObserveScene(position);
        }

        // Serializes included records before comparing content, retaining exact data rather than fire-clock approximations.
        private void Record(ZDO source, bool force)
        {
            if (source == null || !source.IsValid() || !source.Persistent || _destroyed.Contains(source.m_uid) ||
                _policy.For(source.GetPrefab()) == 0)
            {
                return;
            }
            _diagnostics.Processed++;
            var view = ZNetScene.instance.FindInstance(source);
            _known.TryGetValue(source.m_uid, out var prior);
            if (!force && prior != null && prior.Revision == source.DataRevision &&
                prior.HadInstance == (view != null) && prior.Position == source.GetPosition())
            {
                return;
            }
            _api.GetZone(source.GetPosition(), out var x, out var z);
            var item = _reader.Read(source, x, z);
            item.LocalScale = item.LocalScale ?? prior?.LocalScale;
            var fingerprint = ContentFingerprint(item);
            if (prior != null && prior.Fingerprint == fingerprint)
            {
                prior.Revision = source.DataRevision;
                prior.HadInstance = view != null;
                _diagnostics.Unchanged++;
                return;
            }
            Cache(source, item, prior, view != null, fingerprint);
        }

        // Tracks sector moves separately from deletion and retains known instantiated scale.
        private void Cache(ZDO source, CapturedObject item, RecordingStamp prior, bool hasInstance, string fingerprint)
        {
            var x = item.ZoneX;
            var z = item.ZoneZ;
            item.ObservedUtcTicks = NextTicks();
            if (prior != null && (prior.X != x || prior.Z != z))
            {
                Zone(prior.X, prior.Z).Departures[Key(item)] = new CapturedDeparture {
                    SourceUser = item.SourceUser, SourceId = item.SourceId, PrefabHash = prior.Prefab,
                    DestinationX = x, DestinationZ = z, ObservedUtcTicks = item.ObservedUtcTicks - 1 };
            }
            Zone(x, z).Objects[Key(item)] = item;
            _known[source.m_uid] = new RecordingStamp { X = x, Z = z, Prefab = item.PrefabHash,
                Revision = source.DataRevision, HadInstance = hasInstance, Position = source.GetPosition(),
                Fingerprint = fingerprint, LocalScale = item.LocalScale };
            _diagnostics.Changed(source, x, z);
        }

        // Records actual deletion messages; ordinary unloads only copy their last known state.
        public static void Destroyed(ZDOID id)
        {
            var observer = _current;
            if (observer == null || ZNet.World == null || ZNet.World.m_uid != observer._worldUid)
            {
                return;
            }
            try
            {
                Received(ZDOMan.instance.GetZDO(id), false);
                if (observer._known.TryGetValue(id, out var prior))
                {
                    var item = new CapturedDeletion { SourceUser = id.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SourceId = id.ID, PrefabHash = prior.Prefab, ObservedUtcTicks = observer.NextTicks() };
                    observer.Zone(prior.X, prior.Z).Deletions[item.SourceUser + ":" + item.SourceId] = item;
                    observer.Zone(prior.X, prior.Z).Objects.Remove(item.SourceUser + ":" + item.SourceId);
                    observer._destroyed.Add(id);
                    observer._known.Remove(id);
                }
            }
            catch (Exception error)
            {
                observer.Errors++;
                observer.LastError = "Deletion " + id + ": " + error.Message;
            }
        }

        // Hands immutable owned records to one worker while newer arrivals use a fresh cache.
        internal List<ZoneSnapshot> TakeBatch()
        {
            var batch = _pending.Values.Select(zone => zone.Snapshot(NextTicks())).ToList();
            _pending = new Dictionary<string, RecordingZone>();
            return batch;
        }

        // Captures loaded local-owner changes once more, then stops accepting new callbacks.
        internal void Freeze()
        {
            if (_frozen)
            {
                return;
            }
            if (ZDOMan.instance != null && ZNet.World != null && ZNet.World.m_uid == _worldUid)
            {
                foreach (var source in AllObjects().Values.ToArray())
                {
                    Received(source, false, true);
                }
            }
            _frozen = true;
            RetainScene();
            Dispose();
        }

        // Builds map entries only for data actually cached, not planned neighboring sectors.
        internal IEnumerable<ZoneEntry> PendingZones()
        {
            return _pending.Values.Select(zone => new ZoneEntry { X = zone.X, Z = zone.Z, Status = "pending" });
        }

        // Exposes only detached source IDs for the HUD, never native object references.
        internal IEnumerable<string> PendingObjectKeys()
        {
            return _pending.Values.SelectMany(zone => zone.Objects.Keys);
        }

        // Resolves a cache tile without making any completeness assertion.
        private RecordingZone Zone(int x, int z)
        {
            var key = NearZoneScope.Key(x, z);
            if (!_pending.TryGetValue(key, out var zone))
            {
                zone = new RecordingZone(x, z);
                _pending.Add(key, zone);
            }
            return zone;
        }

        // Reads the already-existing catalogue; it never requests new server objects.
        private Dictionary<ZDOID, ZDO> AllObjects()
        {
            return (Dictionary<ZDOID, ZDO>)_objects.GetValue(ZDOMan.instance);
        }

        // Orders same-frame moves and deletions deterministically even on a coarse system clock.
        private long NextTicks()
        {
            _ticks = Math.Max(_ticks + 1, DateTime.UtcNow.Ticks);
            return _ticks;
        }

        // Uses native identities rather than object names or approximate positions.
        private static string Key(CapturedObject item)
        {
            return item.SourceUser + ":" + item.SourceId;
        }

        // Hashes complete serialized content and explicit transforms without volatile capture timestamps.
        private static string ContentFingerprint(CapturedObject item)
        {
            var data = new ZPackage();
            data.Write(item.RawDataBase64);
            foreach (var value in item.Position.Concat(item.Rotation).Concat(item.LocalScale ?? new float[0]))
            {
                data.Write(value);
            }
            data.Write(item.ConnectionType);
            data.Write(item.ConnectionTargetUser ?? "");
            data.Write(item.ConnectionTargetId);
            return StoreValidation.Hash(data.GetArray());
        }

        // Detaches callbacks without discarding the owned data waiting to be flushed.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
