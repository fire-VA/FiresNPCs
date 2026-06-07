using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Base class for every Marketplace-module player UI (Gambler, Marketplace, Achievement, Leaderboard,
    /// Mail, Transmog). Owns the parchment-colored panel shell + title bar + close button + ESC handling.
    /// Derived classes override <see cref="PanelTitle"/> / <see cref="PanelSize"/> and implement
    /// <see cref="BuildContent"/> to populate the content area with their module-specific layout.
    ///
    /// Each derived module is a singleton component on its own host GameObject (DontDestroyOnLoad), opened
    /// via <see cref="Open"/> with the NPC's profile name + display name.
    /// </summary>
    public abstract class BasePlayerModulePanel : MonoBehaviour
    {
        protected GameObject _canvasGo;
        protected GameObject _panelGo;
        protected RectTransform _contentArea;
        protected TMP_Text _subtitleText;
        private bool _built;

        protected abstract string PanelTitle { get; }
        protected virtual Vector2 PanelSize => new Vector2(720f, 520f);

        protected abstract void BuildContent(RectTransform contentArea);
        protected virtual void OnOpen(string profileName, string npcDisplayName) { }

        public void Open(string profileName, string npcDisplayName)
        {
            EnsureBuilt();
            if (_subtitleText != null)
                _subtitleText.text = npcDisplayName + "  -  Profile: " + (string.IsNullOrEmpty(profileName) ? "<none>" : profileName);
            OnOpen(profileName, npcDisplayName);
            _panelGo.SetActive(true);
            try { GUIManager.BlockInput(true); } catch { }
        }

        public void Close()
        {
            if (_panelGo != null) _panelGo.SetActive(false);
            try { GUIManager.BlockInput(false); } catch { }
        }

        public bool IsOpen => _panelGo != null && _panelGo.activeSelf;

        protected virtual void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildShell();
            BuildContent(_contentArea);
        }

        private void BuildShell()
        {
            _canvasGo = new GameObject(PanelTitle + "Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panelGo = new GameObject(PanelTitle + "Panel", typeof(RectTransform), typeof(Image));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _panelGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = PanelSize;
            _panelGo.GetComponent<Image>().color = new Color(0.16f, 0.12f, 0.07f, 0.96f);

            BuildTitleBar(rt);
            BuildCloseButton(rt);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(rt, false);
            _contentArea = contentGo.GetComponent<RectTransform>();
            _contentArea.anchorMin = new Vector2(0f, 0f);
            _contentArea.anchorMax = new Vector2(1f, 1f);
            _contentArea.offsetMin = new Vector2(16f, 16f);
            _contentArea.offsetMax = new Vector2(-16f, -68f);
        }

        private void BuildTitleBar(RectTransform parent)
        {
            var bar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(parent, false);
            var bt = bar.GetComponent<RectTransform>();
            bt.anchorMin = new Vector2(0f, 1f); bt.anchorMax = new Vector2(1f, 1f);
            bt.pivot = new Vector2(0.5f, 1f);
            bt.offsetMin = new Vector2(0f, -54f); bt.offsetMax = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(0.10f, 0.07f, 0.04f, 0.85f);

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(bt, false);
            var trt = titleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.45f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(14f, 0f); trt.offsetMax = new Vector2(-44f, 0f);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.font = TMP_Settings.defaultFontAsset;
            title.text = PanelTitle; title.fontSize = 22f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(1f, 0.85f, 0.55f); title.fontStyle = FontStyles.Bold;

            var subGo = new GameObject("Subtitle", typeof(RectTransform));
            subGo.transform.SetParent(bt, false);
            var srt = subGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.45f);
            srt.offsetMin = new Vector2(14f, 0f); srt.offsetMax = new Vector2(-14f, 0f);
            _subtitleText = subGo.AddComponent<TextMeshProUGUI>();
            _subtitleText.font = TMP_Settings.defaultFontAsset;
            _subtitleText.text = ""; _subtitleText.fontSize = 13f;
            _subtitleText.alignment = TextAlignmentOptions.MidlineLeft;
            _subtitleText.color = new Color(0.85f, 0.78f, 0.62f);
        }

        private void BuildCloseButton(RectTransform parent)
        {
            var go = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(36f, 36f);
            rt.anchoredPosition = new Vector2(-8f, -8f);
            go.GetComponent<Image>().color = new Color(0.42f, 0.15f, 0.10f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(Close);

            var label = new GameObject("X", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var x = label.AddComponent<TextMeshProUGUI>();
            x.font = TMP_Settings.defaultFontAsset; x.text = "X";
            x.fontSize = 20f; x.alignment = TextAlignmentOptions.Center;
            x.color = new Color(1f, 0.9f, 0.85f); x.fontStyle = FontStyles.Bold;
        }

        // ---- shared widget helpers ----

        protected static TMP_Text MakeLabel(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax,
                                            Vector2 offMin, Vector2 offMax, float size = 14f,
                                            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
                                            Color? color = null)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset; t.text = text; t.fontSize = size;
            t.alignment = align; t.color = color ?? new Color(0.96f, 0.91f, 0.76f);
            t.enableWordWrapping = true;
            return t;
        }

        protected static Image MakePanel(Transform parent, string name, Color color,
                                         Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        protected static Button MakeButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
                                           Vector2 offMin, Vector2 offMax, Color? bgColor = null, float fontSize = 15f)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            go.GetComponent<Image>().color = bgColor ?? new Color(0.22f, 0.42f, 0.22f, 0.95f);
            var btn = go.GetComponent<Button>();

            var labelGo = new GameObject("Text", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset; t.text = label; t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.96f, 0.91f, 0.76f);
            return btn;
        }

        /// <summary>Creates a vertical ScrollRect inside contentArea. Returns the Content RectTransform that
        /// child entries should be parented under — it has a VerticalLayoutGroup + ContentSizeFitter so
        /// rows auto-stack and the scroll area expands.</summary>
        protected static RectTransform MakeVerticalScroll(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                                          Vector2 offMin, Vector2 offMax, float rowSpacing = 4f,
                                                          int padding = 6, float scrollSensitivity = 60f)
        {
            var go = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            go.GetComponent<Image>().color = new Color(0.08f, 0.06f, 0.04f, 0.85f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(go.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(4f, 4f); vrt.offsetMax = new Vector2(-4f, -4f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.0f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(padding, padding, padding, padding);
            vlg.spacing = rowSpacing;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = go.GetComponent<ScrollRect>();
            sr.viewport = vrt; sr.content = crt;
            sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = scrollSensitivity;
            return crt;
        }
    }
}
