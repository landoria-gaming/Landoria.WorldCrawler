using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, RecordingZone> _pending = new Dictionary<string, RecordingZone>();
        private readonly Queue<ZDOID> _sweep = new Queue<ZDOID>();
        private readonly List<ZDO> _sectorObjects = new List<ZDO>();
        private readonly long _worldUid;
        private int _sweepX = int.MinValue, _sweepZ = int.MinValue;
        private long _ticks;
        private bool _frozen;
        public int Pending => _pending.Count;
        public int PendingObjects => _pending.Values.Sum(zone => zone.Objects.Count);
        public long Errors { get; private set; }
        public string LastError { get; private set; }

        // Activates capture before the disk store is opened, retaining arrivals during preparation.
        public RecordingObserver(long worldUid)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("Recording is already active.");
            }
            _reader = new ObjectCapture(_api);
            _worldUid = worldUid;
            _current = this;
        }

        // Copies the complete deserialized object, never merely queuing a mutable ZDO reference.
        public static void Received(ZDO source, bool fromNetwork = true)
        {
            var observer = _current;
            if (observer == null || ZNet.World == null || ZNet.World.m_uid != observer._worldUid)
            {
                return;
            }
            if (Teleporting())
            {
                return;
            }
            observer._diagnostics.Received(fromNetwork);
            try
            {
                observer.Record(source);
            }
            catch (Exception error)
            {
                observer.Errors++;
                observer.LastError = "Object " + source?.m_uid + ": " + error.Message;
            }
        }

        // Samples already-received objects only in the player's current sector on entry.
        public void Step(Vector3 position)
        {
            if (_frozen || ZDOMan.instance == null || Teleporting())
            {
                return;
            }
            SelectSector(position);
            var clock = Stopwatch.StartNew();
            for (var count = 0; _sweep.Count > 0 && count < 40 && clock.ElapsedMilliseconds < 4; count++)
            {
                Received(ZDOMan.instance.GetZDO(_sweep.Dequeue()), false);
            }
        }

        // Queues only the sector entered, never every object retained by the client.
        private void SelectSector(Vector3 position)
        {
            _api.GetZone(position, out var x, out var z);
            if (x == _sweepX && z == _sweepZ)
            {
                return;
            }
            _sweepX = x;
            _sweepZ = z;
            _sweep.Clear();
            _api.FindObjects(new Vector3(x * 64f, 0f, z * 64f), _sectorObjects);
            foreach (var source in _sectorObjects)
            {
                if (source != null && source.IsValid())
                {
                    _sweep.Enqueue(source.m_uid);
                }
            }
            _sectorObjects.Clear();
        }

        // Copies only the first accepted observation of each source identity.
        private void Record(ZDO source)
        {
            if (source == null || !source.IsValid() || !source.Persistent || !InCurrentSector(source.GetPosition()) ||
                _policy.For(source.GetPrefab()) == 0)
            {
                return;
            }
            _diagnostics.Processed++;
            var key = source.m_uid.UserID + ":" + source.m_uid.ID;
            if (_known.Contains(key))
            {
                _diagnostics.Unchanged++;
                return;
            }
            _api.GetZone(source.GetPosition(), out var x, out var z);
            var item = _reader.Read(source, x, z);
            Cache(source, item);
        }

        // Retains the first position and payload even if the source object later moves or changes.
        private void Cache(ZDO source, CapturedObject item)
        {
            var x = item.ZoneX;
            var z = item.ZoneZ;
            item.ObservedUtcTicks = NextTicks();
            Zone(x, z).Objects[Key(item)] = item;
            _known.Add(Key(item));
            _diagnostics.Changed(source, x, z);
        }

        // Discards startup receipts already saved in any sector and seeds the session-wide identity set.
        internal void AcceptExisting(IEnumerable<string> identities)
        {
            var saved = new HashSet<string>(identities, StringComparer.Ordinal);
            _known.UnionWith(saved);
            foreach (var zone in _pending.Values)
            {
                foreach (var key in zone.Objects.Keys.Where(saved.Contains).ToArray())
                {
                    zone.Objects.Remove(key);
                }
            }
            foreach (var key in _pending.Where(pair => pair.Value.Objects.Count == 0).Select(pair => pair.Key).ToArray())
            {
                _pending.Remove(key);
            }
        }

        // Hands immutable owned records to one worker while newer arrivals use a fresh cache.
        internal List<ZoneSnapshot> TakeBatch()
        {
            var batch = _pending.Values.Select(zone => zone.Snapshot(NextTicks())).ToList();
            _pending = new Dictionary<string, RecordingZone>();
            return batch;
        }

        // Finishes the current-sector queue, then detaches callbacks without dropping cached data.
        internal void Freeze()
        {
            if (_frozen)
            {
                return;
            }
            if (ZDOMan.instance != null && ZNet.World != null && ZNet.World.m_uid == _worldUid &&
                Player.m_localPlayer != null && !Teleporting())
            {
                SelectSector(Player.m_localPlayer.transform.position);
                while (_sweep.Count != 0)
                {
                    Received(ZDOMan.instance.GetZDO(_sweep.Dequeue()), false);
                }
            }
            _frozen = true;
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

        // Accepts a server record only while its native sector contains the local player.
        private bool InCurrentSector(Vector3 position)
        {
            if (Player.m_localPlayer == null)
            {
                return false;
            }
            _api.GetZone(Player.m_localPlayer.transform.position, out var playerX, out var playerZ);
            _api.GetZone(position, out var objectX, out var objectZ);
            return playerX == objectX && playerZ == objectZ;
        }

        // Pauses every observation while Valheim is moving the local player between locations.
        internal static bool Teleporting()
        {
            return Player.m_localPlayer != null && Player.m_localPlayer.IsTeleporting();
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
