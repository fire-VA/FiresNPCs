using System.Collections.Generic;
using FiresRPGmaker.UI.Patches;
using TMPro;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// With the inventory open, hovering a globe shows its breakdown in vanilla's own inventory tooltip prefab, restyled
    /// like every other tooltip and anchored the way vanilla anchors one. The pointer is polled because the open
    /// InventoryGui owns hover events.
    /// </summary>
    internal sealed class GlobeHoverTooltip
    {
        private const float HoverDelaySeconds = 0.25f;
        private const float RefreshSeconds = 0.25f;
        private const int SortingOrder = 40;
        private const string TopicChildName = "Topic";
        private const string TextChildName = "Text";

        private GameObject _tooltip;
        private TMP_Text _topic;
        private TMP_Text _text;
        private GlobeView _hovered;
        private float _hoverStart;
        private float _nextRefresh;

        public void Tick(IReadOnlyList<GlobeView> views, Player player, bool enabled, float now)
        {
            if (!enabled || player == null || views.Count == 0 || !InventoryGui.IsVisible())
            {
                _hovered = null;
                Hide();
                return;
            }

            var hit = FindHovered(views, ZInput.pointerPosition);
            if (hit != _hovered)
            {
                _hovered = hit;
                _hoverStart = now;
                _nextRefresh = 0f;
                Hide();
            }
            if (_hovered == null || now - _hoverStart < HoverDelaySeconds || !EnsureTooltip()) return;

            _tooltip.SetActive(true);
            if (now >= _nextRefresh)
            {
                _topic.text = GlobeStatBreakdown.Topic(_hovered.Resource);
                _text.text = GlobeStatBreakdown.Describe(_hovered.Resource, player);
                _nextRefresh = now + RefreshSeconds;
            }
            _tooltip.transform.position = _hovered.TopCenterWorld();
            Utils.ClampUIToScreen(_tooltip.transform.GetChild(0) as RectTransform);
        }

        public void Destroy()
        {
            if (_tooltip != null) Object.Destroy(_tooltip);
            _tooltip = null;
        }

        private static GlobeView FindHovered(IReadOnlyList<GlobeView> views, Vector2 pointer)
        {
            var eventCamera = EventCamera(views[0].Rect);
            foreach (var view in views)
            {
                if (view.Contains(pointer, eventCamera)) return view;
            }
            return null;
        }

        private void Hide()
        {
            if (_tooltip != null && _tooltip.activeSelf) _tooltip.SetActive(false);
        }

        private bool EnsureTooltip()
        {
            if (_tooltip != null) return _topic != null && _text != null;

            var grid = InventoryGui.instance != null ? InventoryGui.instance.m_playerGrid : null;
            var element = grid != null && grid.m_elementPrefab != null ? grid.m_elementPrefab.GetComponent<InventoryElement>() : null;
            var prefab = element != null && element.m_tooltip != null ? element.m_tooltip.m_tooltipPrefab : null;
            if (prefab == null) return false;

            _tooltip = Object.Instantiate(prefab, grid.GetComponentInParent<Canvas>().rootCanvas.transform);
            _tooltip.name = "GlobeHudTooltip";
            var canvas = _tooltip.GetComponent<Canvas>();
            if (canvas == null) canvas = _tooltip.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            _topic = FindText(_tooltip.transform, TopicChildName);
            _text = FindText(_tooltip.transform, TextChildName);
            UITooltipStyleOverride.Apply(_tooltip);
            _tooltip.SetActive(false);
            return _topic != null && _text != null;
        }

        private static TMP_Text FindText(Transform root, string childName)
        {
            var child = Utils.FindChild(root, childName, Utils.IterativeSearchType.DepthFirst);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static Camera EventCamera(Component hudElement)
        {
            var root = hudElement.GetComponentInParent<Canvas>().rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
    }
}
