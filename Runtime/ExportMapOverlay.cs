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
    // Displays committed export progress on both native maps without altering map data.
    internal sealed partial class ExportMapOverlay : IDisposable
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
        private int _revision, _readRevision;
        private float _nextRead;
        private bool _disposed, _dataChanged;

        // Keeps all game access on the main thread and filesystem reads on one worker.
        public ExportMapOverlay(ManualLogSource log, CrawlController export, RestorationController restore)
        {
            _log = log;
            _export = export;
            _restore = restore;
        }

        // Refreshes progress while either native map is visible.
        public void Update()
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                var map = Minimap.instance;
                if (!Connected(map))
                {
                    SetVisible(false);
                    SetSmallVisible(false);
                    return;
                }
                RefreshSession();
                ShowMaps(map);
            }
            catch (Exception error)
            {
                SetVisible(false);
                SetSmallVisible(false);
                Warn(error.Message);
            }
        }

        // Keeps filesystem reads and mesh updates shared between the two map views.
        private void ShowMaps(Minimap map)
        {
            var large = map.m_largeRoot != null && map.m_largeRoot.activeInHierarchy &&
                map.m_mapImageLarge != null && map.m_mapImageLarge.isActiveAndEnabled;
            var small = map.m_smallRoot != null && map.m_smallRoot.activeInHierarchy &&
                map.m_mapImageSmall != null && map.m_mapImageSmall.isActiveAndEnabled;
            SetVisible(large);
            SetSmallVisible(small);
            if (!large && !small)
            {
                return;
            }
            if (large)
            {
                EnsureLayer(map.m_mapImageLarge);
            }
            if (small)
            {
                EnsureSmallLayer(map.m_mapImageSmall);
            }
            Poll();
            if (_dataChanged)
            {
                if (_layer != null)
                {
                    UpdateBatches();
                }
                if (_smallLayer != null)
                {
                    UpdateSmallBatches();
                }
                _dataChanged = false;
            }
            if (large)
            {
                UpdateViews(map);
            }
            if (small)
            {
                UpdateSmallViews(map);
            }
        }

        // Requires only the connected character, independent of map size or visibility.
        private bool Connected(Minimap map)
        {
            return map != null && Player.m_localPlayer != null && Game.instance != null &&
                Game.instance.GetPlayerProfile() != null
                && ZNet.World != null && ZNet.instance != null
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        // Invalidates cached rectangles immediately when character, world, or selection changes.
        private void RefreshSession()
        {
            var live = ZNet.World;
            var id = Game.instance.GetPlayerProfile().GetPlayerID();
            var root = CrawlerConstants.ExportRoot;
            if (_world != null && _world.Uid == live.m_uid && _world.Seed == live.m_seed
                && _world.SeedText == live.m_seedName && _world.GenerationVersion == live.m_worldGenVersion
                && _profileId == id && _configuredRoot == root)
            {
                return;
            }
            var world = GameContext.Identity();
            var directory = Path.Combine(Path.GetFullPath(root), StoreValidation.DirectoryName(world));
            _world = world;
            _profileId = id;
            _configuredRoot = root;
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
                        if (data.Warning != null)
                        {
                            Warn(data.Warning);
                        }
                        else
                        {
                            _warning = null;
                        }
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
            var restoring = LocalRestoration();
            var root = _configuredRoot;
            var directory = _directory;
            var world = _world.Copy();
            _readRevision = _revision;
            _nextRead = Time.realtimeSinceStartup + 5f;
            _read = Task.Run(() => restoring ? RestorationMapSource.Read(world) :
                ExportMapOverlaySource.ReadProgress(directory, world));
        }

        // Selects restoration progress only for a supported local world, never a remote server.
        private static bool LocalRestoration()
        {
            return SupportedGameVersions.IsCurrent(GameContext.GameVersion) && ZNet.instance.IsServer() &&
                ZNet.World.m_fileSource == LatestWorldApi.LocalSource;
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
            UpdateBatches(_remainingGraphics, _remaining, "Remaining Sectors ", _layer);
            UpdateBatches(_capturedGraphics, _captured, "Captured Sectors ", _layer);
        }

        // Resizes one color layer under Unity UI's 16-bit vertex limit.
        private void UpdateBatches(List<ExportMapOverlayGraphic> graphics,
            ExportMapOverlayRegion[] regions, string prefix, RectTransform parent)
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
                var rectangle = CreateRectangle(prefix + graphics.Count, parent);
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
            SetViews(_remainingGraphics, map, _image, new Color(1f, 0.65f, 0.1f, remaining));
            SetViews(_capturedGraphics, map, _image, new Color(0.2f, 1f, 0.35f, complete));
        }

        // Updates each fixed-color layer without changing map exploration.
        private void SetViews(List<ExportMapOverlayGraphic> graphics, Minimap map, RawImage image, Color color)
        {
            foreach (var graphic in graphics)
            {
                graphic.SetView(image.uvRect, map.m_textureSize, map.m_pixelSize, color);
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
            DestroySmallLayer();
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
