using UnityEngine;
using UnityEngine.UI;

namespace Landoria.WorldCrawler.Runtime
{
    // Draws a bounded batch of progress rectangles without intercepting map input.
    internal sealed class ExportMapOverlayGraphic : MaskableGraphic
    {
        private ExportMapOverlayRegion[] _regions;
        private int _start;
        private int _count;
        private int _textureSize;
        private float _pixelSize;
        private Rect _uv;

        // Replaces one immutable batch when a new committed manifest becomes available.
        public void SetRegions(ExportMapOverlayRegion[] regions, int start, int count)
        {
            _regions = regions;
            _start = start;
            _count = count;
            raycastTarget = false;
            SetVerticesDirty();
        }

        // Tracks native zoom and pan while avoiding unchanged per-frame mesh rebuilds.
        public void SetView(Rect uv, int textureSize, float pixelSize, Color tint)
        {
            if (_uv == uv && _textureSize == textureSize && _pixelSize == pixelSize && color == tint)
            {
                return;
            }
            _uv = uv;
            _textureSize = textureSize;
            _pixelSize = pixelSize;
            color = tint;
            SetVerticesDirty();
        }

        // Clips each strip in viewport space before generating at most 48000 vertices.
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_regions == null)
            {
                return;
            }
            var rectangle = rectTransform.rect;
            for (var index = _start; index < _start + _count; index++)
            {
                if (ExportMapOverlayGeometry.Project(_regions[index], _textureSize, _pixelSize,
                    _uv.x, _uv.y, _uv.width, _uv.height, out var bounds))
                {
                    AddRectangle(helper, rectangle, bounds);
                }
            }
        }

        // Appends a solid translucent quad using the default UI material and current tint.
        private void AddRectangle(VertexHelper helper, Rect rectangle, ExportMapOverlayBounds bounds)
        {
            var left = rectangle.xMin + (float)bounds.Left * rectangle.width;
            var right = rectangle.xMin + (float)bounds.Right * rectangle.width;
            var bottom = rectangle.yMin + (float)bounds.Bottom * rectangle.height;
            var top = rectangle.yMin + (float)bounds.Top * rectangle.height;
            var first = helper.currentVertCount;
            helper.AddVert(new Vector3(left, bottom), color, Vector2.zero);
            helper.AddVert(new Vector3(left, top), color, Vector2.zero);
            helper.AddVert(new Vector3(right, top), color, Vector2.zero);
            helper.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
