using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Landoria.WorldCrawler.Runtime
{
    // Mirrors the large-map progress rectangles on the native small minimap.
    internal sealed partial class ExportMapOverlay
    {
        private readonly List<ExportMapOverlayGraphic> _smallCaptured = new List<ExportMapOverlayGraphic>();
        private readonly List<ExportMapOverlayGraphic> _smallRemaining = new List<ExportMapOverlayGraphic>();
        private RectTransform _smallLayer;
        private RawImage _smallImage;

        // Reparents the minimap overlay after native UI recreation.
        private void EnsureSmallLayer(RawImage image)
        {
            if (_smallLayer != null && _smallImage == image)
            {
                return;
            }
            DestroySmallLayer();
            _smallImage = image;
            _smallLayer = CreateRectangle("WorldCrawler Minimap Overlay", image.rectTransform);
            _smallLayer.SetAsFirstSibling();
            _dataChanged = true;
        }

        // Uses the same source regions and batching limits as the large map.
        private void UpdateSmallBatches()
        {
            UpdateBatches(_smallRemaining, _remaining, "Remaining Minimap Sectors ", _smallLayer);
            UpdateBatches(_smallCaptured, _captured, "Captured Minimap Sectors ", _smallLayer);
        }

        // Follows the minimap's independent UV viewport each frame.
        private void UpdateSmallViews(Minimap map)
        {
            SetViews(_smallRemaining, map, _smallImage,
                new Color(1f, 0.65f, 0.1f, CrawlerConstants.RemainingZoneOpacity));
            SetViews(_smallCaptured, map, _smallImage,
                new Color(0.2f, 1f, 0.35f, CrawlerConstants.ExportedZoneOpacity));
        }

        // Hides only this mod's minimap layer.
        private void SetSmallVisible(bool visible)
        {
            if (_smallLayer != null && _smallLayer.gameObject.activeSelf != visible)
            {
                _smallLayer.gameObject.SetActive(visible);
            }
        }

        // Removes graphics owned by the replaced minimap image.
        private void DestroySmallLayer()
        {
            if (_smallLayer != null)
            {
                _smallLayer.gameObject.SetActive(false);
                Object.Destroy(_smallLayer.gameObject);
            }
            _smallLayer = null;
            _smallImage = null;
            _smallCaptured.Clear();
            _smallRemaining.Clear();
        }
    }
}
