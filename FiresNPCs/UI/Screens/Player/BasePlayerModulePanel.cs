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

        // Static "any panel open" registry — read by GUIManager.IsCustomPanelOpen so the existing
        // PlayerInputPatches block movement/camera/M-key while one of these panels is on screen.
        private static int _openCount;
        public static bool AnyOpen => _openCount > 0;
        // Per-instance open flag: tracks whether THIS panel currently counts toward _openCount, so we
        // never double-increment on repeated Open() calls and never double-decrement on Close().
        private bool _opened;

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
            if (!_opened)
            {
                _opened = true;
                _openCount++;
            }
            // CALL BLOCK INPUT EVERY OPEN (regression 2026-06-07): gating it on "wasOpen=false" missed the
            // first open because the just-built panel was already active=true by default → BlockInput never
            // ran → GameCamera.UpdateCamera kept rotating with mouse delta.
            try { GUIManager.BlockInput(true); } catch { }
            try { GUIManager.ApplyGameFontToAll(_panelGo.transform); } catch { }
        }

        public void Close()
        {
            if (_panelGo != null) _panelGo.SetActive(false);
            if (_opened)
            {
                _opened = false;
                _openCount = Mathf.Max(0, _openCount - 1);
            }
            if (_openCount == 0)
            {
                try { GUIManager.BlockInput(false); } catch { }
            }
        }

        public bool IsOpen => _panelGo != null && _panelGo.activeSelf;

        protected virtual void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // Per-frame cursor keep-alive — Valheim's Hud.Update / GUIManager hide the cursor again on
            // every frame, so a one-shot Cursor.visible=true in BlockInput isn't enough. Force it while
            // a player panel is open.
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildShell();
            BuildContent(_contentArea);
        }

        // Re-apply Valheim font lazily after the build pass so every child Text/TMP picks it up.
        private void EnsureValheimFont()
        {
            try { if (_panelGo != null) GUIManager.ApplyGameFontToAll(_panelGo.transform); } catch { }
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
            title.text = PanelTitle; title.fontSize = 22f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(1f, 0.85f, 0.55f); title.fontStyle = FontStyles.Bold;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(title);

            var subGo = new GameObject("Subtitle", typeof(RectTransform));
            subGo.transform.SetParent(bt, false);
            var srt = subGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.45f);
            srt.offsetMin = new Vector2(14f, 0f); srt.offsetMax = new Vector2(-14f, 0f);
            _subtitleText = subGo.AddComponent<TextMeshProUGUI>();
            _subtitleText.text = ""; _subtitleText.fontSize = 13f;
            _subtitleText.alignment = TextAlignmentOptions.MidlineLeft;
            _subtitleText.color = new Color(0.85f, 0.78f, 0.62f);
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(_subtitleText);
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
            x.text = "X";
            x.fontSize = 20f; x.alignment = TextAlignmentOptions.Center;
            x.color = new Color(1f, 0.9f, 0.85f); x.fontStyle = FontStyles.Bold;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(x);
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
            t.text = text; t.fontSize = size;
            t.alignment = align; t.color = color ?? new Color(0.96f, 0.91f, 0.76f);
            t.enableWordWrapping = true;
            // Apply the configured Valheim/Primary font directly — ApplyValheimFontToAll is conditional
            // (only swaps Liberation/null), so we'd be stuck with TMP's default pixel font otherwise.
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(t);
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

        /// <summary>Sets both LayoutElement min AND preferred height on a row, so the parent VerticalLayoutGroup
        /// (with childControlHeight=true) reliably sizes the row regardless of which property Unity reads.</summary>
        protected static void SetRowHeight(GameObject rowGo, float height)
        {
            var le = rowGo.GetComponent<LayoutElement>();
            if (le == null) le = rowGo.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            // Belt-and-suspenders: also stamp the RectTransform height so a missing/late layout pass
            // doesn't leave the row at sizeDelta.y=0 (the original empty-list bug 2026-06-07).
            var rt = rowGo.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
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
            t.text = label; t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.96f, 0.91f, 0.76f);
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(t);
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

            // Manual positioning (not VerticalLayoutGroup): VLG + ContentSizeFitter were silently leaving
            // rows at height 0 in this layering setup. Each panel calls ReflowScroll() after adding rows
            // to position them top-down by their LayoutElement.preferredHeight + spacing.
            // (Stash spacing/padding on the LayoutElement of the Content rect itself so ReflowScroll
            // can read them later without the call-sites needing to pass them.)
            var le = content.AddComponent<LayoutElement>();
            le.minHeight = padding * 2f;
            le.preferredHeight = rowSpacing; // re-purposed as "row spacing" by ReflowScroll
            le.flexibleHeight = padding;     // re-purposed as "padding"

            var sr = go.GetComponent<ScrollRect>();
            sr.viewport = vrt; sr.content = crt;
            sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = scrollSensitivity;
            return crt;
        }

        /// <summary>Call after adding/removing rows under <paramref name="content"/> (the rect returned by
        /// <see cref="MakeVerticalScroll"/>). Positions each child top-down by its LayoutElement.preferredHeight
        /// + the spacing/padding the scroll was built with, and resizes the content rect to fit.</summary>
        protected static void ReflowScroll(RectTransform content)
        {
            if (content == null) return;
            var meta = content.GetComponent<LayoutElement>();
            float spacing = meta != null ? meta.preferredHeight : 4f;
            float padding = meta != null ? meta.flexibleHeight : 6f;

            float y = padding;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null) continue;
                var cle = child.GetComponent<LayoutElement>();
                float h = cle != null && cle.preferredHeight > 0 ? cle.preferredHeight
                        : cle != null && cle.minHeight > 0 ? cle.minHeight
                        : child.sizeDelta.y > 0 ? child.sizeDelta.y : 24f;

                child.anchorMin = new Vector2(0f, 1f);
                child.anchorMax = new Vector2(1f, 1f);
                child.pivot = new Vector2(0.5f, 1f);
                child.offsetMin = new Vector2(padding, child.offsetMin.y);
                child.offsetMax = new Vector2(-padding, child.offsetMax.y);
                child.anchoredPosition = new Vector2(0f, -y);
                child.sizeDelta = new Vector2(-padding * 2f, h);
                y += h + spacing;
            }
            content.sizeDelta = new Vector2(content.sizeDelta.x, y + padding);
        }
    }
}
