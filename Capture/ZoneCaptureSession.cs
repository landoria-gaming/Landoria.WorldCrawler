using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Observes one loaded sector across frames and records only evidence actually received by the client.
    public sealed class ZoneCaptureSession : IDisposable
    {
        private readonly int _x;
        private readonly int _z;
        private readonly Vector3 _center;
        private readonly float _minimumDwell;
        private readonly float _quiet;
        private readonly float _timeout;
        private readonly int _budget;
        private readonly float _started;
        private readonly long _startedUtc;
        private readonly CaptureApi _api;
        private readonly ObjectCapture _reader;
        private readonly List<ZDO> _candidates = new List<ZDO>();
        private readonly Dictionary<int, string> _excludedPrefabs = new Dictionary<int, string>();
        private readonly Dictionary<string, GameObject> _locationRoots = new Dictionary<string, GameObject>();
        private List<CapturedObject> _sample = new List<CapturedObject>();
        private List<string> _exclusions = new List<string>();
        private List<string> _sceneExclusions = new List<string>();
        private Dictionary<string, uint> _shape = new Dictionary<string, uint>();
        private Dictionary<string, uint> _previousShape;
        private SceneCaptureCursor _scene;
        private List<CapturedSceneNode> _sceneNodes;
        private int _index = -1;
        private int _passes;
        private int _pendingViews;
        private float _stableSince;
        private float _readySince = -1f;
        private float _nextSample;
        private bool _verifiedScene;
        private readonly CaptureReceiveWatch _receiver;
        private readonly bool _adaptive;
        private float _networkQuiet;
        private float _nextLoadCheck;
        private bool _areaReady;

        public ZoneSnapshot Result { get; private set; }
        public string Status { get; private set; }
        public int ObservedObjectCount { get; private set; }

        // Configures bounded work and waits without enabling world generation or network commands.
        public ZoneCaptureSession(int x, int z, float minimumDwellSeconds = 8f,
            float quietSeconds = 3f, float timeoutSeconds = 90f, int objectsPerFrame = 40,
            bool adaptiveLoading = false)
        {
            if (minimumDwellSeconds < 0f || quietSeconds <= 0f || timeoutSeconds <= minimumDwellSeconds
                || objectsPerFrame < 1 || InvalidTiming(minimumDwellSeconds)
                || InvalidTiming(quietSeconds) || InvalidTiming(timeoutSeconds))
            {
                throw new ArgumentOutOfRangeException("Capture timing and frame budgets must be positive.");
            }
            _x = x;
            _z = z;
            _center = new Vector3(x * 64f, 0f, z * 64f);
            _minimumDwell = minimumDwellSeconds;
            _quiet = quietSeconds;
            _timeout = timeoutSeconds;
            _budget = objectsPerFrame;
            _started = Time.realtimeSinceStartup;
            _startedUtc = DateTime.UtcNow.Ticks;
            _api = new CaptureApi();
            _reader = new ObjectCapture(_api);
            _adaptive = adaptiveLoading;
            _receiver = new CaptureReceiveWatch(x, z);
            Status = "Waiting for terrain and received objects.";
        }

        // Advances a small amount of main-thread capture work and reports completion.
        public bool Step()
        {
            if (Result != null)
            {
                return true;
            }
            if (Time.realtimeSinceStartup - _started >= _timeout)
            {
                throw new TimeoutException("Zone observation timed out: " + Status);
            }
            if (!Ready())
            {
                ResetObservation();
                return false;
            }
            if (_scene != null)
            {
                return StepScene();
            }
            if (_index < 0 && Time.realtimeSinceStartup < _nextSample)
            {
                return false;
            }
            if (_index < 0)
            {
                BeginSample();
            }
            return StepSample();
        }

        // Requires a real local terrain tile and completed pending terrain rebuilds.
        private bool Ready()
        {
            if (_receiver.Error != null) { throw new InvalidOperationException("Capture receiver failed: " + _receiver.Error); }
            if (ZNet.instance == null || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected ||
                ZNet.instance.HasBadConnection())
            { Status = "Waiting for a healthy server connection."; return false; }
            if (ZoneSystem.instance == null || ZNetScene.instance == null || ZDOMan.instance == null
                || !ZoneSystem.instance.IsZoneLoaded(_center))
            {
                Status = "Waiting for loaded terrain and generated-location proxies.";
                return false;
            }
            var heightmap = Heightmap.FindHeightmap(_center);
            if (heightmap == null || heightmap.IsDistantLod || heightmap.HaveQueuedRebuild())
            {
                Status = "Waiting for the terrain heightmap.";
                return false;
            }
            if (!LoadQueueReady()) { return false; }
            if (_readySince < 0f)
            {
                _readySince = Time.realtimeSinceStartup;
                _stableSince = _readySince;
            }
            return true;
        }

        // Polls native instance readiness without consuming packets or mistaking outgoing queues for downloads.
        private bool LoadQueueReady()
        {
            if (Time.realtimeSinceStartup >= _nextLoadCheck)
            {
                _nextLoadCheck = Time.realtimeSinceStartup + 0.25f;
                _areaReady = ZNetScene.instance.IsAreaReady(_center);
                ZNet.instance.GetNetStats(out _, out _, out var ping, out _, out _);
                _networkQuiet = CaptureStability.QuietSeconds(ping);
            }
            if (!_areaReady) { Status = "Waiting for native object creation in this area."; }
            return _areaReady;
        }

        // Restarts the stability window when a zone unloads or its terrain becomes unavailable.
        private void ResetObservation()
        {
            _index = -1;
            _readySince = -1f;
            _passes = 0;
            _previousShape = null;
            _scene = null;
            _sceneNodes = null;
            _verifiedScene = false;
            _nextSample = Time.realtimeSinceStartup + 0.5f;
        }

        // Copies a sector candidate list and starts a new incremental observation pass.
        private void BeginSample()
        {
            _api.FindObjects(_center, _candidates);
            _sample = new List<CapturedObject>();
            _exclusions = new List<string>();
            _shape = new Dictionary<string, uint>(StringComparer.Ordinal);
            _locationRoots.Clear();
            _pendingViews = 0;
            _index = 0;
        }

        // Serializes objects under both an object-count and a small elapsed-time budget.
        private bool StepSample()
        {
            var clock = Stopwatch.StartNew();
            var count = 0;
            while (_index < _candidates.Count && count++ < _budget && clock.ElapsedMilliseconds < 4)
            {
                Observe(_candidates[_index++]);
            }
            Status = "Observing persistent objects: " + _sample.Count + "; pending instances: " + _pendingViews;
            if (_index < _candidates.Count)
            {
                return false;
            }
            _index = -1;
            return EndSample();
        }

        // Records one currently valid persistent object and checks its generated scene readiness.
        private void Observe(ZDO source)
        {
            if (source == null || !source.IsValid()
                || !CaptureTransform.InZone(source.GetPosition(), _x, _z))
            {
                return;
            }
            if (Exclude(source) || !source.Persistent)
            {
                return;
            }
            var item = _reader.Read(source, _x, _z);
            var key = item.SourceUser + ":" + item.SourceId + ":" + item.PrefabHash;
            _shape.Add(key, _receiver.TracksUpdates(item.PrefabHash) ? item.DataRevision : 0u);
            _sample.Add(item);
            var instance = ZNetScene.instance.FindInstance(source);
            if (instance == null)
            {
                _pendingViews++;
                return;
            }
            var proxy = instance.GetComponent<LocationProxy>();
            if (proxy != null)
            {
                var root = _api.GetLocationInstance(proxy);
                if (root == null)
                {
                    _pendingViews++;
                }
                else
                {
                    _locationRoots[item.SourceUser + ":" + item.SourceId] = root;
                }
            }
        }

        // Filters all fauna before serialization, including nonpersistent players and fish.
        private bool Exclude(ZDO source)
        {
            var prefab = ZNetScene.instance.GetPrefab(source.GetPrefab());
            if (prefab == null)
            {
                throw new InvalidOperationException("Cannot classify an unknown prefab: " + source.GetPrefab());
            }
            string reason;
            if (!_excludedPrefabs.TryGetValue(source.GetPrefab(), out reason))
            {
                reason = CaptureExclusionPolicy.Classify(prefab);
                _excludedPrefabs[source.GetPrefab()] = reason;
            }
            if (reason == null)
            {
                return false;
            }
            _exclusions.Add("network:" + reason);
            return true;
        }

        // Uses membership and terrain revision stability so live fires or creatures cannot stall forever.
        private bool EndSample()
        {
            _passes++;
            ObservedObjectCount = _sample.Count;
            var sameShape = SameShape(_shape, _previousShape);
            var now = Time.realtimeSinceStartup;
            if (!sameShape || _pendingViews > 0)
            {
                _stableSince = now;
                _verifiedScene = false;
                _sceneNodes = null;
            }
            _previousShape = _shape;
            _nextSample = now + 0.5f;
            var quiet = _adaptive ? _networkQuiet : Mathf.Max(_quiet, _networkQuiet);
            if ((!_adaptive && now - _readySince < _minimumDwell) ||
                !CaptureStability.Ready(_passes, _pendingViews, now, _stableSince, _receiver.LastChange, quiet))
            {
                Status = "Validating zone: " + _sample.Count + " objects; pending instances=" + _pendingViews +
                    "; relevant receive age=" + (now - _receiver.LastChange).ToString("F1") +
                    "s; required quiet=" + quiet.ToString("F1") + "s.";
                return false;
            }
            if (_verifiedScene)
            {
                Finish();
                return true;
            }
            BeginScene();
            return false;
        }

        // Starts a layout audit of the loaded zone and every observed location proxy.
        private void BeginScene()
        {
            _scene = new SceneCaptureCursor(_api.GetZoneRoot(_center));
            foreach (var location in _locationRoots)
            {
                _scene.AddRoot(location.Value, location.Key);
            }
            Status = "Recording observed generated-location layout.";
        }

        // Finishes static layout before rechecking the sector membership one last time.
        private bool StepScene()
        {
            if (!_scene.Step(_budget))
            {
                return false;
            }
            _sceneNodes = _scene.Nodes;
            _sceneExclusions = _scene.Excluded;
            _scene = null;
            _verifiedScene = true;
            _nextSample = 0f;
            return false;
        }

        // Finalizes a client observation without claiming proof of absent server objects.
        private void Finish()
        {
            var dungeonExpected = _sample.Any(item => item.Categories.Contains("DungeonGenerator")) &&
                (_sceneNodes ?? new List<CapturedSceneNode>()).Any(node =>
                    node.Components.Any(component => component == "Teleport"));
            var interiorObjects = _sample.Count(item => Math.Abs(item.Position[1]) >= 1000f);
            if (dungeonExpected && interiorObjects == 0)
            {
                throw new InvalidOperationException("A dungeon entrance was loaded but its interior objects were not received; zone not accepted.");
            }
            Result = new ZoneSnapshot
            {
                ZoneX = _x, ZoneZ = _z, StartedUtcTicks = _startedUtc, FinishedUtcTicks = DateTime.UtcNow.Ticks,
                ObservationPasses = _passes, DwellSeconds = Time.realtimeSinceStartup - _readySince,
                StableSeconds = Time.realtimeSinceStartup - Mathf.Max(_stableSince, _receiver.LastChange), TerrainReady = true,
                Objects = _sample, SceneNodes = _sceneNodes ?? new List<CapturedSceneNode>(),
                DungeonExpected = dungeonExpected, DungeonEvidenceComplete = !dungeonExpected || interiorObjects > 0,
                InteriorObjectCount = interiorObjects, NaturalAbsenceComplete = true
            };
            Result.SetExclusionCounts(_exclusions.Concat(_sceneExclusions));
            Result.BuildSummaries();
            Result.Validate();
            Status = "Stable client observation recorded: " + Result.Objects.Count + " persistent objects.";
        }

        // Compares exact identifiers instead of relying on a possibly colliding aggregate hash.
        private static bool SameShape(Dictionary<string, uint> current, Dictionary<string, uint> previous)
        {
            if (previous == null || current.Count != previous.Count)
            {
                return false;
            }
            foreach (var item in current)
            {
                uint value;
                if (!previous.TryGetValue(item.Key, out value) || value != item.Value)
                {
                    return false;
                }
            }
            return true;
        }

        // Prevents invalid configuration values from creating an unbounded unattended wait.
        private static bool InvalidTiming(float seconds)
        {
            return float.IsNaN(seconds) || float.IsInfinity(seconds);
        }

        // Detaches network observation when this zone is committed or abandoned.
        public void Dispose() { _receiver.Dispose(); }
    }
}
