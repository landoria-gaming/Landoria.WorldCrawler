using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Landoria.WorldCrawler.UI
{
    // Builds the native-HUD-attached recording display and scrollable journal.
    internal sealed partial class RecordingHud
    {
        // Creates a chat-sized translucent panel on the left side of the HUD.
        private void CreatePanel(Minimap minimap)
        {
            _panel = new GameObject("WorldCrawler Recording Journal", typeof(RectTransform),
                typeof(Image), typeof(ScrollRect));
            _panel.transform.SetParent(_hud.transform, false);
            _panel.SetActive(false);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(18f, 0f);
            rect.sizeDelta = new Vector2(380f, 430f);
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.58f);
            _title = CreateText("Saved prefabs", rect, minimap);
            Place(_title.rectTransform, Vector2.one, new Vector2(0f, 1f),
                new Vector2(12f, -8f), new Vector2(350f, 26f));
            _title.fontSize = 19f;
            _title.fontStyle = FontStyles.Bold;
            CreateScroll(rect, minimap);
        }

        // Adds a masked viewport so older lines remain reachable by mouse wheel.
        private void CreateScroll(RectTransform parent, Minimap minimap)
        {
            var viewport = new GameObject("Journal Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(parent, false);
            var viewRect = (RectTransform)viewport.transform;
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(12f, 12f);
            viewRect.offsetMax = new Vector2(-12f, -40f);
            var content = new GameObject("Journal Lines", typeof(RectTransform));
            content.transform.SetParent(viewRect, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _history = CreateText("Saved Prefab Lines", _content, minimap);
            _history.rectTransform.anchorMin = Vector2.zero;
            _history.rectTransform.anchorMax = Vector2.one;
            _history.rectTransform.offsetMin = Vector2.zero;
            _history.rectTransform.offsetMax = Vector2.zero;
            _history.fontSize = 16f;
            _history.alignment = TextAlignmentOptions.TopLeft;
            _scroll = _panel.GetComponent<ScrollRect>();
            _scroll.viewport = viewRect;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 32f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        // Places the three counters along the top starting at screen center.
        private void CreateStats(Minimap minimap)
        {
            _stats = CreateText("Recording Counters", _hud.transform, minimap);
            Place(_stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -12f), new Vector2(790f, 36f));
            _stats.fontSize = 19f;
            _stats.fontStyle = FontStyles.Bold;
            _stats.color = Color.white;
        }

        // Matches CharacterVault's status label immediately below the small map.
        private void CreateStatus(Minimap minimap)
        {
            _status = CreateText("Recording Status", minimap.m_smallRoot.transform, minimap);
            Place(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(480f, 30f));
            _status.alignment = TextAlignmentOptions.Center;
            _status.fontSize = minimap.m_biomeNameSmall.fontSize;
            _status.fontStyle = FontStyles.Bold;
            _status.color = Color.white;
            _status.text = "Recording...";
            PositionStatus();
        }

        // Repositions the minimap label when UI scale or resolution changes.
        private void PositionStatus()
        {
            var map = _minimap.m_mapImageSmall.rectTransform;
            _status.rectTransform.position = map.TransformPoint(
                new Vector3(map.rect.center.x, map.rect.yMin - 36f, 0f));
        }

        // Copies the game's font instead of shipping a separate UI asset.
        private static TextMeshProUGUI CreateText(string name, Transform parent, Minimap minimap)
        {
            var target = new GameObject(name, typeof(RectTransform));
            target.SetActive(false);
            target.transform.SetParent(parent, false);
            var label = target.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            var nativeFont = minimap.m_biomeNameSmall.font;
            label.font = nativeFont ??
                Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(asset => asset != null);
            if (nativeFont != null)
            {
                label.fontSharedMaterial = minimap.m_biomeNameSmall.fontSharedMaterial;
            }
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.color = Color.white;
            target.SetActive(true);
            return label;
        }

        // Applies one anchored rectangle without a layout component per line.
        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // Keeps the newest 20 or so lines visible while preserving scrollback.
        private void UpdateHistory()
        {
            if (_history == null || _scroll == null || _scroll.viewport == null || _content == null)
            {
                return;
            }
            var follow = string.IsNullOrEmpty(_history.text) ||
                _scroll.verticalNormalizedPosition < 0.05f;
            _history.text = _lines.Count == 0 ? "No saved objects yet." : string.Join("\n", _lines);
            _content.sizeDelta = new Vector2(0f,
                Mathf.Max(_scroll.viewport.rect.height, _history.preferredHeight + 8f));
            Canvas.ForceUpdateCanvases();
            if (follow)
            {
                _scroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
