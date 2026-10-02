using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BepInEx.Logging;
using Landoria.WorldCrawler.Storage;
using Landoria.WorldCrawler.Restoration;
using UnityEngine;
using UnityEngine.UI;

namespace Landoria.WorldCrawler.Runtime
{
    // Displays committed export progress on the large map without altering the game map.
    internal sealed class ExportMapOverlay : IDisposable
    {
        private const int BatchSize = 12000;
        private readonly ManualLogSource _log;
        private readonly CrawlController _export;
        private readonly RestorationController _restore;
        private readonly List<ExportMapOverlayGraphic> _capturedGraphics = new List<ExportMapOverlayGraphic>();
        private readonly List<ExportMapOverlayGraphic> _remainingGraphics = new List<ExportMapOverlayGraphic>();
        private ExportMapOverlayRegion[] _captured = Array.Empty<ExportMapOverlayRegion>();
        private ExportMapOverlayRegion[] _remaining = Array.Empty<ExportMapOverlayRegion>();
        private Task<ExportMapOverlayData> _read;
        private RectTransform _layer;
        private RawImage _image;
        private WorldIdentity _world;
        private string _configuredRoot, _directory, _warning;
        private long _profileId;
        private int _radius, _revision, _readRevision;
        private float _nextRead;
        private bool _disposed, _dataChanged;

        // Keeps all game access on the main thread and filesystem reads on one worker.
        public ExportMapOverlay(ManualLogSource log, CrawlController export, RestorationController restore)
        {
            _log = log;
            _export = export;
            _restore = restore;
        }

        // Refreshes the selected export lazily while only the large map is visible.
        public void Update()
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                var map = Minimap.instance;
                if (!Visible(map))
                {
                    SetVisible(false);
                    return;
                }
                RefreshSession();
                EnsureLayer(map.m_mapImageLarge);
                SetVisible(true);
                Poll();
                if (_dataChanged)
                {
                    UpdateBatches();
                }
                UpdateViews(map);
            }
            catch (Exception error)
            {
                SetVisible(false);
                Warn(error.Message);
            }
        }

        // Requires the connected character and native large-map image without changing their state.
        private bool Visible(Minimap map)
        {
            return map != null && map.m_largeRoot != null
                && map.m_largeRoot.activeInHierarchy && map.m_mapImageLarge != null && map.m_mapImageLarge.isActiveAndEnabled
                && Player.m_localPlayer != null && Game.instance != null && Game.instance.GetPlayerProfile() != null
                && ZNet.World != null && ZNet.instance != null
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        // Invalidates cached rectangles immediately when character, world, or selection changes.
        private void RefreshSession()
        {
            var live = ZNet.World;
            var id = Game.instance.GetPlayerProfile().GetPlayerID();
            var radius = CrawlerConstants.LandmarkRadius;
            var root = CrawlerConstants.ExportRoot;
            if (_world != null && _world.Uid == live.m_uid && _world.Seed == live.m_seed
                && _world.SeedText == live.m_seedName && _world.GenerationVersion == live.m_worldGenVersion
                && _profileId == id && _configuredRoot == root && _radius == radius)
            {
                return;
            }
            if (radius < 1 || radius > 1000)
            {
                throw new InvalidOperationException("Invalid export selection for the map overlay.");
            }
            var world = GameContext.Identity();
            var scope = "landmarks_r" + radius.ToString(CultureInfo.InvariantCulture);
            var directory = Path.Combine(Path.GetFullPath(root), StoreValidation.DirectoryName(world, scope));
            _world = world;
            _profileId = id;
            _configuredRoot = root;
            _radius = radius;
            _revision++;
            _directory = directory;
            _captured = Array.Empty<ExportMapOverlayRegion>();
            _remaining = Array.Empty<ExportMapOverlayRegion>();
            _dataChanged = true;
            _nextRead = 0;
        }

        // Applies only matching completed reads and polls committed metadata every five seconds.
        private void Poll()
        {
            if (_read != null && _read.IsCompleted)
            {
                var completed = _read;
                _read = null;
                try
                {
                    var data = completed.GetAwaiter().GetResult();
                    if (_readRevision == _revision)
                    {
                        ApplyRegions(data);
                        _warning = null;
                    }
                }
                catch (Exception error)
                {
                    if (_readRevision == _revision)
                    {
                        ApplyRegions(new ExportMapOverlayData());
                        Warn(error.Message);
                    }
                }
            }
            if (_read != null || Time.realtimeSinceStartup < _nextRead)
            {
                return;
            }
            var active = _export.MapProgress() ?? _restore.MapProgress();
            if (active != null)
            {
                ApplyRegions(active);
                _nextRead = Time.realtimeSinceStartup + 1f;
                return;
            }
            StartRead();
        }

        // Captures immutable filesystem arguments before starting the next background read.
        private void StartRead()
        {
            var marker = RestorationMarker();
            var root = _configuredRoot;
            var directory = _directory;
            var world = _world.Copy();
            var id = _profileId.ToString(CultureInfo.InvariantCulture);
            var radius = _radius;
            _readRevision = _revision;
            _nextRead = Time.realtimeSinceStartup + 5f;
            _read = Task.Run(() => marker == null ? ExportMapOverlaySource.ReadProgress(directory, world, id, radius) :
                RestorationMapSource.Read(marker, root, directory, world));
        }

        // Detects only the currently loaded local prepared world, never a same-UID remote server.
        private static string RestorationMarker()
        {
            if (!SupportedGameVersions.IsCurrent(GameContext.GameVersion) || !ZNet.instance.IsServer() ||
                ZNet.World.m_fileSource != LatestWorldApi.LocalSource)
            {
                return null;
            }
            var path = Path.Combine(LatestWorldApi.DirectoryFor(ZNet.World), PreparedWorld.FileName);
            return File.Exists(path) ? path : null;
        }

        // Avoids replacing identical immutable geometry when only unrelated progress changed.
        private void ApplyRegions(ExportMapOverlayData data)
        {
            if (Equal(_captured, data.Captured) && Equal(_remaining, data.Remaining))
            {
                return;
            }
            _captured = data.Captured;
            _remaining = data.Remaining;
            _dataChanged = true;
        }

        // Compares immutable projected region identities without rebuilding unchanged meshes.
        private static bool Equal(ExportMapOverlayRegion[] left, ExportMapOverlayRegion[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index].MinX != right[index].MinX || left[index].MaxX != right[index].MaxX
                    || left[index].Z != right[index].Z)
                {
                    return false;
                }
            }
            return true;
        }

        // Parents the progress layer directly under the map image and behind existing children.
        private void EnsureLayer(RawImage image)
        {
            if (_layer != null && _image == image)
            {
                return;
            }
            DestroyLayer();
            _image = image;
            _layer = CreateRectangle("WorldCrawler Export Overlay", image.rectTransform);
            _layer.SetAsFirstSibling();
            _dataChanged = true;
        }

        // Splits merged regions below Unity UI's 16-bit vertex limit without per-sector objects.
        private void UpdateBatches()
        {
            UpdateBatches(_remainingGraphics, _remaining, "Remaining Sectors ");
            UpdateBatches(_capturedGraphics, _captured, "Captured Sectors ");
            _dataChanged = false;
        }

        // Resizes one color layer under Unity UI's 16-bit vertex limit.
        private void UpdateBatches(List<ExportMapOverlayGraphic> graphics,
            ExportMapOverlayRegion[] regions, string prefix)
        {
            var needed = (regions.Length + BatchSize - 1) / BatchSize;
            while (graphics.Count > needed)
            {
                var index = graphics.Count - 1;
                UnityEngine.Object.Destroy(graphics[index].gameObject);
                graphics.RemoveAt(index);
            }
            while (graphics.Count < needed)
            {
                var rectangle = CreateRectangle(prefix + graphics.Count, _layer);
                graphics.Add(rectangle.gameObject.AddComponent<ExportMapOverlayGraphic>());
            }
            for (var index = 0; index < graphics.Count; index++)
            {
                graphics[index].SetRegions(regions, index * BatchSize, Math.Min(BatchSize, regions.Length - index * BatchSize));
            }
        }

        // Applies independent colors while preserving map pan, zoom, fog, and pointer input.
        private void UpdateViews(Minimap map)
        {
            var complete = CrawlerConstants.ExportedZoneOpacity;
            var remaining = CrawlerConstants.RemainingZoneOpacity;
            SetViews(_remainingGraphics, map, new Color(1f, 0.65f, 0.1f, remaining));
            SetViews(_capturedGraphics, map, new Color(0.2f, 1f, 0.35f, complete));
        }

        // Updates each fixed-color layer without changing map exploration.
        private void SetViews(List<ExportMapOverlayGraphic> graphics, Minimap map, Color color)
        {
            foreach (var graphic in graphics)
            {
                graphic.SetView(_image.uvRect, map.m_textureSize, map.m_pixelSize, color);
            }
        }

        // Matches the parent's viewport exactly while retaining its canvas and layer.
        private static RectTransform CreateRectangle(string name, RectTransform parent)
        {
            var rectangle = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rectangle.gameObject.layer = parent.gameObject.layer;
            rectangle.SetParent(parent, false);
            rectangle.anchorMin = Vector2.zero;
            rectangle.anchorMax = Vector2.one;
            rectangle.offsetMin = Vector2.zero;
            rectangle.offsetMax = Vector2.zero;
            rectangle.localScale = Vector3.one;
            return rectangle;
        }

        // Hides the whole layer without touching native map visibility or input handlers.
        private void SetVisible(bool visible)
        {
            if (_layer != null && _layer.gameObject.activeSelf != visible)
            {
                _layer.gameObject.SetActive(visible);
            }
        }

        // Logs one diagnostic per repeated failure without filling the console every frame.
        private void Warn(string message)
        {
            if (_warning == message)
            {
                return;
            }
            _warning = message;
            _log.LogWarning("Export map overlay unavailable: " + message);
        }

        // Removes all owned UI objects when the native map changes or the plugin unloads.
        private void DestroyLayer()
        {
            if (_layer != null)
            {
                _layer.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_layer.gameObject);
            }
            _layer = null;
            _image = null;
            _capturedGraphics.Clear();
            _remainingGraphics.Clear();
        }

        // Releases the overlay and observes a worker failure without waiting on game shutdown.
        public void Dispose()
        {
            _disposed = true;
            DestroyLayer();
            if (_read != null)
            {
                _read.ContinueWith(task =>
{
    var ignored = task.Exception;
}
, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
