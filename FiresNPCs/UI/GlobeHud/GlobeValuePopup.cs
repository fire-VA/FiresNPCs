using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// "current / max" above one globe while its value is changing, fading out once it has been steady for the hold
    /// time. Lives on the HUD canvas itself, not under the globe HUD, so a stretched HUD scale never distorts the text.
    /// </summary>
    internal sealed class GlobeValuePopup
    {
        private const int SortingOrder = 5;
        private const float FontSizePerGlobeWidth = 0.2f;
        private const float MinFontSize = 14f;
        private const float MaxFontSize = 30f;
        private const float FadeOutSeconds = 0.45f;
        private const float LabelWidth = 260f;
        private const float LabelHeight = 44f;
        private const float OutlineWidth = 0.22f;
        private const float TextWhiteBlend = 0.55f;

        private readonly TextMeshProUGUI _label;
        private readonly RectTransform _labelRect;
        private int _shownCurrent;
        private int _shownMax;
        private float _lastChangeTime = float.NegativeInfinity;
        private float _shownAlpha = -1f;
        private bool _primed;

        public GlobeValuePopup(Transform hudCanvas, GlobeView view)
        {
            var color = Color.Lerp(view.LiquidColor, Color.white, TextWhiteBlend);
            _label = UIBuilderHelper.CreateLabel(hudCanvas, "GlobeValue_" + view.Resource, "", MinFontSize, color,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Bottom);
            _label.raycastTarget = false;
            _label.outlineWidth = OutlineWidth;
            _label.outlineColor = Color.black;
            _labelRect = _label.rectTransform;
            _labelRect.pivot = new Vector2(0.5f, 0f);
            _labelRect.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            var canvas = _label.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            SetAlpha(0f);
        }

        /// <summary>How far above the globe's top the text sits, as a share of the globe's height (clears the ring art).</summary>
        public static float GapAboveGlobe(GlobeResource resource)
        {
            switch (resource)
            {
                case GlobeResource.Eitr: return 0.5f;
                case GlobeResource.Adrenaline: return 1.1f;
                default: return 0.33f;
            }
        }

        public void Tick(GlobeView view, float now, float holdSeconds, bool show)
        {
            int current = Mathf.CeilToInt(view.Current);
            int max = Mathf.RoundToInt(view.Max);
            if (!_primed)
            {
                _shownCurrent = current;
                _shownMax = max;
                _primed = true;
            }
            if (current != _shownCurrent || max != _shownMax)
            {
                _shownCurrent = current;
                _shownMax = max;
                _label.text = current + " / " + max;
                _lastChangeTime = now;
            }

            float fade = 1f - Mathf.Clamp01((now - _lastChangeTime - holdSeconds) / FadeOutSeconds);
            float alpha = show && max > 0 ? fade : 0f;
            SetAlpha(alpha);
            if (alpha <= 0f) return;

            float scale = Mathf.Max(_labelRect.lossyScale.y, Mathf.Epsilon);
            _labelRect.position = view.TopCenterWorld() + Vector3.up * view.WorldHeight() * GapAboveGlobe(view.Resource);
            _label.fontSize = Mathf.Clamp(view.WorldWidth() / scale * FontSizePerGlobeWidth, MinFontSize, MaxFontSize);
        }

        public void Hide() => SetAlpha(0f);

        public void Destroy()
        {
            if (_label != null) Object.Destroy(_label.gameObject);
        }

        private void SetAlpha(float alpha)
        {
            if (Mathf.Approximately(alpha, _shownAlpha)) return;
            _shownAlpha = alpha;
            _label.alpha = alpha;
            _label.enabled = alpha > 0f;
        }
    }
}
