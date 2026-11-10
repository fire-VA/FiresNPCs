using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FiresCore.UI.FiresChat;

namespace FiresRPGmaker.Modules.ChatSystem
{
    /// <summary>
    /// The Fires chat window — a COMPLETE replacement for the vanilla chat's visuals. Vanilla <see cref="Chat"/> stays
    /// the engine (its buffer, input field, send path, Discord relay and media patches all keep working); its window
    /// art is hidden and this panel renders everything: FiresChatSkin panel + header with channel tabs + minimize,
    /// a scrollable message body fed from m_chatBuffer, and an input row that MIRRORS the (invisible) vanilla input
    /// with a real blinking caret — so focus, Enter-to-send, Esc and history remain 100% vanilla and cannot desync.
    /// Drag / resize via the Alt+C arrange mode (cursor-grab), persisted through the Chat config entries.
    /// Lives on its own standalone canvas (never under Hud). Config Chat/CustomChatWindow=false reverts to vanilla.
    /// </summary>
    public class ChatChannelUI : MonoBehaviour
    {
        private static ChatChannelUI _instance;
        public static ChatChannelUI Instance => _instance;

        // ── poker table feed (the action log rides the chat as its own tab while a table is open) ────────────────
        public static bool PokerActive;
        public static readonly System.Collections.Generic.List<string> PokerFeed = new System.Collections.Generic.List<string>();
        public static int PokerFeedVersion;

        public static void PokerFeedAdd(string line)
        {
            PokerFeed.Add(line);
            if (PokerFeed.Count > 300) PokerFeed.RemoveAt(0);
            PokerFeedVersion++;
        }

        public static void PokerFeedReset()
        {
            PokerFeed.Clear();
            PokerFeedVersion++;
        }

        private const float WinW = 460f, WinH = 300f;
        private const float HeadH = 30f, InputH = 30f;
        private const float TabW = 58f, TabH = 20f;

        private GameObject _canvasGO;
        private Canvas _canvas;
        private RectTransform _win;
        private Button _pokerTab;
        private Image _pokerTabBg, _pokerTabFrame;
        private TMP_Text _pokerTabLbl;
        private bool _pokerView, _pokerWasActive;
        private int _lastFeedVer = -1;
        private GameObject _bodyGroup;          // collapsible: body + input row
        private TMP_Text _bodyText, _inputMirror, _placeholder;
        private ScrollRect _scroll;
        private RectTransform _content, _vpRect;   // fixed-size scroll area
        private float _lastVpH = -1f;
        private float _inputReserve;            // extra bottom room under the last line while the input row is taller than one line
        private Image _caret;
        private RectMask2D _inputMask;
        private Button[] _tabBtns;
        private Image[] _tabBgs, _tabFrames;
        private TMP_Text[] _tabLbls;
        private bool _built, _collapsed, _vanillaHidden, _arranging;
        private GameObject _dragSurf, _resizeGrip;
        private int _lastBufCount = -1;
        private string _lastBufTail;
        private float _lastScale = -1f;
        // grow UPWARD only while typing: a TEMPORARY visual height. The root _win (the rect Core's dock places, hit-tests
        // and saves) is never resized; a child background (_growBg) plus the header and body group are pushed up by _growExtra local units.
        private const float GrowMax = 300f, GrowTopMargin = 20f;
        private RectTransform _growBg, _bodyRect;
        private Image _winImg, _growImg;
        private Color _winImgColor;
        private float _growExtra;
        // the grown chat draws ON TOP of every HUD element: active-state order of the chat's own root canvas
        // (Core lifts the minimap column/status to 30000 and the inventory to 30100; dock grips 30900, layout overlay 31000).
        internal const int FrontOrder = 30500;
        private bool _front;
        private int _baseOrder = 28;
        internal bool TestFront => _front;
        internal int TestBaseOrder => _baseOrder;
        internal Canvas TestCanvas => _canvas;
        private Vector2 _visShift;   // visible-content shift that keeps the panel on screen (root untouched)
        private Vector2 _anchorBR; private bool _anchorValid;   // compact drawn bottom-right (screen px), pinned while active

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            if (Chat.instance == null) return;
            if (ChatChannelManager.CustomWindow != null && !ChatChannelManager.CustomWindow.Value) return;
            var go = new GameObject("VA_FiresChat");
            go.transform.SetParent(Chat.instance.transform, false);   // dies with Chat
            _instance = go.AddComponent<ChatChannelUI>();
        }

        private void Awake()
        {
            _instance = this;
            ChatChannelManager.OnChannelChanged += OnChannelChanged;
        }

        private void OnDestroy()
        {
            ChatChannelManager.OnChannelChanged -= OnChannelChanged;
            if (_canvasGO != null) Destroy(_canvasGO);
            if (_instance == this) _instance = null;
        }

        private void LateUpdate()
        {
            if (Chat.instance == null || Chat.instance.m_input == null || Chat.instance.m_chatWindow == null) return;
            if (!_built) { try { Build(); RegisterWithDock(); } catch (Exception ex) { Debug.LogError("[FiresChat] build failed: " + ex); enabled = false; return; } }

            HideVanillaOnce();

            // Poker table open → chat forces itself on, jumps to the Poker tab, and rises above the game overlay.
            if (PokerActive && !_pokerWasActive) { _pokerView = true; _lastBufCount = -1; _lastFeedVer = -1; RefreshTabs(); }
            if (!PokerActive && _pokerWasActive && _pokerView) { _pokerView = false; _lastBufCount = -1; RefreshTabs(); }
            _pokerWasActive = PokerActive;
            if (_pokerTab != null && _pokerTab.gameObject.activeSelf != PokerActive) _pokerTab.gameObject.SetActive(PokerActive);
            // the grown chat draws ON TOP of every HUD element it covers: the chat owns its own root ScreenSpaceOverlay
            // canvas and Core's dock never reparents a registered rect (DockService only adds grip children with their own override
            // canvases), but Core raises the minimap column/status canvases to 30000 while the cursor is free (ShellMinimapWatch.Apply,
            // ShellMinimapClicks.Raise) - i.e. while typing. Active = the root canvas at FrontOrder; compact = the exact base order again.
            // Only sortingOrder changes: no rect, parent or sibling the dock places or saves.
            bool front = !_collapsed && Chat.instance.HasFocus();
            int baseOrder = PokerActive ? 6600 : 28;
            if (_canvas != null)
            {
                int want = front ? FrontOrder : baseOrder;
                if (_canvas.sortingOrder != want) _canvas.sortingOrder = want;
                if (front && !_front) global::FiresRPGmaker.FiresLogger.LogDebug($"[ChatChannelUI] brought to front (root canvas sortingOrder, order {FrontOrder}, base {baseOrder})");
                _front = front; _baseOrder = baseOrder;
            }

            // Mirror vanilla's own show/hide (fade timer toggles Chat_box) + always show while typing or at a table.
            bool focused = Chat.instance.m_input.isFocused;
            bool show = PokerActive || Chat.instance.m_chatWindow.gameObject.activeInHierarchy || focused;
            if (_win.gameObject.activeSelf != show) _win.gameObject.SetActive(show);
            if (!show) return;

            ApplyLayoutConfig();
            ApplyActiveGrow(Chat.instance.HasFocus() && !_collapsed);   // vanilla's own 'chat active' (window shown + input focused)
            SyncBuffer();
            // the dock grip changed the window -> re-fit the content to the new viewport (content only)
            if (_vpRect != null && Mathf.Abs(_vpRect.rect.height - _lastVpH) > 0.5f) { _lastVpH = _vpRect.rect.height; FitContent(true); }
            SyncInputRow(focused);
            HandleTabCycle(focused);
            HandleArrange();
            SaveDockSize();
        }

        // ── build ───────────────────────────────────────────────────────────────────────────────────────────────
        private void Build()
        {
            _canvasGO = new GameObject("FiresChatCanvas");
            var canvas = _canvasGO.AddComponent<Canvas>();
            _canvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 28;   // above the HUD; raised above the game overlays while a table is open
            var scaler = _canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            var win = NewImg(_canvasGO.transform, "Window", FiresChatSkin.Panel, Image.Type.Sliced);
            win.raycastTarget = true;
            _win = win.rectTransform;
            _win.anchorMin = _win.anchorMax = new Vector2(1f, 0f);   // native bottom-right
            _win.pivot = new Vector2(1f, 0f);
            _win.sizeDelta = new Vector2(CurW, CurH);   // the saved size (Core grip)
            _win.anchoredPosition = new Vector2(-24f, 22f);
            _winImg = win; _winImgColor = win.color;

            // the active-state background - a child of the root that extends ABOVE it by _growExtra (root rect untouched).
            _growImg = NewImg(_win, "GrowBg", FiresChatSkin.Panel, Image.Type.Sliced);
            _growImg.color = _winImgColor; _growImg.raycastTarget = true; _growImg.enabled = false;
            _growBg = _growImg.rectTransform;
            _growBg.anchorMin = Vector2.zero; _growBg.anchorMax = Vector2.one; _growBg.offsetMin = Vector2.zero; _growBg.offsetMax = Vector2.zero;
            _growBg.SetAsFirstSibling();

            // Header — title, tabs, minimize.
            var head = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            head.SetParent(_win, false);
            head.anchorMin = new Vector2(0f, 1f); head.anchorMax = new Vector2(1f, 1f); head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(-24f, HeadH); head.anchoredPosition = new Vector2(0f, -9f);
            _headRect = head;   // Core's live drag handle (empty header space; title, tabs and buttons keep their clicks)
            { var hi = head.gameObject.AddComponent<Image>(); hi.color = new Color(0f, 0f, 0f, 0f); hi.raycastTarget = true; }

            var title = NewTxt(head, "Chat", 14f, FiresChatSkin.Gold);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 0.5f);
            trt.sizeDelta = new Vector2(60f, 0f); trt.anchoredPosition = new Vector2(2f, 0f);

            var channels = (ChatChannelManager.Channel[])Enum.GetValues(typeof(ChatChannelManager.Channel));
            _tabBtns = new Button[channels.Length];
            _tabBgs = new Image[channels.Length];
            _tabFrames = new Image[channels.Length];
            _tabLbls = new TMP_Text[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                var ch = channels[i];
                var tab = NewImg(head, "Tab_" + ch, FiresChatSkin.Tab, Image.Type.Sliced);
                tab.raycastTarget = true;
                var rt = tab.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f); rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(TabW, TabH);
                rt.anchoredPosition = new Vector2(-30f - (channels.Length - 1 - i) * (TabW + 5f), 0f);
                var btn = tab.gameObject.AddComponent<Button>();
                btn.targetGraphic = tab;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener(() => OnTabClick(ch));
                _tabBgs[i] = tab; _tabBtns[i] = btn;

                var frame = NewImg(tab.transform, "Frame", FiresChatSkin.Frame, Image.Type.Sliced);
                var frt = frame.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(-3f, -3f); frt.offsetMax = new Vector2(3f, 3f);
                _tabFrames[i] = frame;

                var lbl = NewTxt(tab.transform, ChatChannelManager.GetChannelDisplayName(ch), 11f, FiresChatSkin.Dim);
                FillRect(lbl.rectTransform);
                _tabLbls[i] = lbl;
            }

            // The Poker tab — view of the table's action feed; only visible while a table is open.
            {
                var tab = NewImg(head, "Tab_Poker", FiresChatSkin.Tab, Image.Type.Sliced);
                tab.raycastTarget = true;
                var rt = tab.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f); rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(TabW, TabH);
                rt.anchoredPosition = new Vector2(-30f - channels.Length * (TabW + 5f), 0f);
                var btn = tab.gameObject.AddComponent<Button>();
                btn.targetGraphic = tab;
                var nav2 = btn.navigation; nav2.mode = Navigation.Mode.None; btn.navigation = nav2;
                btn.onClick.AddListener(() => { _pokerView = true; _lastFeedVer = -1; RefreshTabs(); });
                _pokerTab = btn; _pokerTabBg = tab;

                var frame = NewImg(tab.transform, "Frame", FiresChatSkin.Frame, Image.Type.Sliced);
                var frt = frame.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(-3f, -3f); frt.offsetMax = new Vector2(3f, 3f);
                _pokerTabFrame = frame;

                _pokerTabLbl = NewTxt(tab.transform, "Poker", 11f, FiresChatSkin.Dim);
                FillRect(_pokerTabLbl.rectTransform);
                tab.gameObject.SetActive(false);
            }

            var min = NewImg(head, "Min", FiresChatSkin.Tab, Image.Type.Sliced);
            min.raycastTarget = true;
            var mrt = min.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(1f, 0.5f); mrt.pivot = new Vector2(1f, 0.5f);
            mrt.sizeDelta = new Vector2(24f, TabH); mrt.anchoredPosition = new Vector2(0f, 0f);
            var minBtn = min.gameObject.AddComponent<Button>();
            minBtn.targetGraphic = min;
            minBtn.onClick.AddListener(ToggleCollapse);
            var minLbl = NewTxt(min.transform, "–", 14f, FiresChatSkin.Gold);
            FillRect(minLbl.rectTransform);

            // Collapsible group: message body + input row.
            _bodyGroup = new GameObject("BodyGroup", typeof(RectTransform));
            var bg = (RectTransform)_bodyGroup.transform;
            bg.SetParent(_win, false);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = new Vector2(12f, 12f); bg.offsetMax = new Vector2(-12f, -(HeadH + 14f));
            _bodyRect = bg;

            var vp = NewImg(bg, "BodyViewport", null, Image.Type.Simple);
            vp.color = new Color(0f, 0f, 0f, 0.30f);
            vp.raycastTarget = true;
            var vprt = vp.rectTransform;
            vprt.anchorMin = new Vector2(0f, 0f); vprt.anchorMax = new Vector2(1f, 1f);
            vprt.offsetMin = new Vector2(0f, InputH + 6f); vprt.offsetMax = new Vector2(0f, 0f);
            vp.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("BodyContent", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(vprt, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f); content.sizeDelta = new Vector2(0f, 10f);
            _bodyText = content.gameObject.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize = 14f;
            _bodyText.color = FiresChatSkin.Cream;
            _bodyText.alignment = TextAlignmentOptions.BottomLeft;
            _bodyText.enableWordWrapping = true;
            _bodyText.richText = true;
            _bodyText.raycastTarget = false;
            _bodyText.margin = new Vector4(9f, 6f, 9f, 6f);
            // fixed-size scroll area: no ContentSizeFitter - FitContent() sizes ONLY the content inside the masked
            // viewport (max of viewport height and the text's preferred height), so short history sits at the bottom like vanilla
            // and long history scrolls; nothing above the content (viewport, body, window) is ever sized from the text.
            _content = content; _vpRect = vprt;

            _scroll = vp.gameObject.AddComponent<ScrollRect>();
            _scroll.content = content; _scroll.viewport = vprt;
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 16f;

            var inRow = NewImg(bg, "InputRow", FiresChatSkin.Field, Image.Type.Sliced);
            var irt = inRow.rectTransform;
            irt.anchorMin = new Vector2(0f, 0f); irt.anchorMax = new Vector2(1f, 0f); irt.pivot = new Vector2(0.5f, 0f);
            irt.sizeDelta = new Vector2(0f, InputH); irt.anchoredPosition = Vector2.zero;
            // Click-to-type: focus the (invisible) vanilla input — same focus Enter uses, works over the game UIs.
            inRow.raycastTarget = true;
            var rowTrig = inRow.gameObject.AddComponent<EventTrigger>();
            var rowEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            rowEntry.callback.AddListener(_ =>
            {
                var input = Chat.instance != null ? Chat.instance.m_input : null;
                if (input == null) return;
                input.Select();
                input.ActivateInputField();
            });
            rowTrig.triggers.Add(rowEntry);

            _inputMirror = NewTxt(inRow.transform, "", 14f, new Color(0.94f, 0.9f, 0.8f));
            _inputMirror.alignment = TextAlignmentOptions.TopLeft;   // top-left so wrapped lines grow downward within the row
            _inputMirror.enableWordWrapping = true;                  // wrap long messages instead of truncating to one line
            _inputMirror.overflowMode = TextOverflowModes.Overflow;
            var imrt = _inputMirror.rectTransform;
            imrt.anchorMin = Vector2.zero; imrt.anchorMax = Vector2.one; imrt.pivot = new Vector2(0f, 1f);
            imrt.offsetMin = new Vector2(12f, 4f); imrt.offsetMax = new Vector2(-14f, -4f);

            _placeholder = NewTxt(inRow.transform, "Enter text…", 14f, new Color(0.62f, 0.55f, 0.44f, 0.8f));
            _placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            _placeholder.fontStyle = FontStyles.Italic;
            var prt = _placeholder.rectTransform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(12f, 2f); prt.offsetMax = new Vector2(-30f, -2f);

            _caret = NewImg(_inputMirror.transform, "Caret", null, Image.Type.Simple);
            _caret.color = new Color(1f, 0.91f, 0.72f);
            var crt = _caret.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f); crt.pivot = new Vector2(0f, 0.5f); // mirror top-left origin (matches TMP char coords)
            crt.sizeDelta = new Vector2(2f, 15f); crt.anchoredPosition = new Vector2(0f, -9f);

            BuildArrangeHandles();

            var font = GrabFont();
            if (font != null) foreach (var t in _win.GetComponentsInChildren<TMP_Text>(true)) t.font = font;

            RefreshTabs();
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresChat] custom window built (tabs=" + channels.Length + ")");
            _built = true;
        }

        // Hide the vanilla window's art — engine keeps running underneath.
        private void HideVanillaOnce()
        {
            if (_vanillaHidden) return;
            try
            {
                var root = Chat.instance.m_chatWindow.Find("root") ?? Chat.instance.m_chatWindow;
                foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                    g.enabled = false;
                // The input must stay ALIVE (focus/submit/history) — just invisible.
                var inputGO = (Chat.instance.m_input as Component).gameObject;
                var cg = inputGO.GetComponent<CanvasGroup>() ?? inputGO.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                foreach (var g in inputGO.GetComponentsInChildren<Graphic>(true))
                    g.enabled = true;   // graphics re-enabled but fully transparent via the CanvasGroup
                _vanillaHidden = true;
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresChat] vanilla chat visuals hidden (engine untouched)");
            }
            catch (Exception ex) { Debug.LogWarning("[FiresChat] hide-vanilla: " + ex.Message); _vanillaHidden = true; }
        }

        // ── per-frame sync ──────────────────────────────────────────────────────────────────────────────────────
        private void ApplyLayoutConfig()
        {
            if (_dockOwned) return;   // Core's HUD dock owns position + scale
            float s = ChatChannelManager.UiScale != null ? Mathf.Clamp(ChatChannelManager.UiScale.Value, 0.6f, 2.5f) : 1f;
            if (!Mathf.Approximately(s, _lastScale)) { _lastScale = s; _win.localScale = new Vector3(s, s, 1f); }
            float ox = ChatChannelManager.OffsetX != null ? ChatChannelManager.OffsetX.Value : 0f;
            float oy = ChatChannelManager.OffsetY != null ? ChatChannelManager.OffsetY.Value : 0f;
            _win.anchoredPosition = new Vector2(-24f + ox, 22f + oy);
        }

        private void SyncBuffer()
        {
            if (_pokerView && PokerActive)
            {
                if (PokerFeedVersion == _lastFeedVer) return;
                _lastFeedVer = PokerFeedVersion;
                bool pAt = AtBottom();
                _bodyText.text = PokerFeed.Count > 0 ? string.Join("\n", PokerFeed) : "<i>The table is quiet…</i>";
                FitContent(pAt);
                return;
            }

            var buf = Chat.instance.m_chatBuffer;
            if (buf == null) return;
            string tail = buf.Count > 0 ? buf[buf.Count - 1] : null;
            if (buf.Count == _lastBufCount && tail == _lastBufTail) return;
            _lastBufCount = buf.Count; _lastBufTail = tail;
            bool at = AtBottom() || _lastBufCount <= 1;
            _bodyText.text = string.Join("\n", buf);
            FitContent(at);
        }

        // fixed-size chat scroll area, vanilla-like: true when the view is at (within 4 px of) the newest line or there
        // is nothing to scroll; a reader who scrolled up keeps his place when new lines arrive.
        private bool AtBottom()
        {
            if (_scroll == null || _content == null || _vpRect == null) return true;
            float over = _content.rect.height - _vpRect.rect.height;
            if (over <= 1f) return true;
            return _scroll.verticalNormalizedPosition * over <= 4f;
        }

        // sizes the CONTENT only (inside the RectMask2D viewport), never the viewport or window; optionally snaps to the newest line.
        private void FitContent(bool stick)
        {
            if (_content == null || _vpRect == null || _bodyText == null) return;
            var m = _bodyText.margin;
            if (Mathf.Abs(m.w - (6f + _inputReserve)) > 0.1f) _bodyText.margin = new Vector4(m.x, m.y, m.z, 6f + _inputReserve);
            float w = Mathf.Max(10f, _vpRect.rect.width);
            float pref = _bodyText.GetPreferredValues(_bodyText.text ?? "", w, 0f).y;
            float h = Mathf.Max(_vpRect.rect.height, pref);
            if (Mathf.Abs(_content.sizeDelta.y - h) > 0.5f) _content.sizeDelta = new Vector2(0f, h);
            if (stick && _scroll != null) { _scroll.StopMovement(); _scroll.verticalNormalizedPosition = 0f; }
        }

        // while the chat is ACTIVE the panel grows UPWARD only, may cover other UI; inactive = the compact saved/dock size .
        // Bottom-right never moves: the root rect is untouched (Core's dock places/hit-tests by the root's screen rect + top-left and only
        // saves a size from its own grip via SizeChanged; a root sizeDelta change would move that top-left and risk a re-place/save), so the
        // extra height lives in children only. extra = min(300, room up to screen top - 20 GUI above the compact top); 0 = exact restore.
        private void ApplyActiveGrow(bool active)
        {
            if (_win == null || _bodyRect == null || _headRect == null || _growBg == null) return;
            float extra = 0f;
            // Core's dock put the root 45.9 GUI past the right edge at 3440x1440: the VISIBLE content
            // (background, header, body) is shifted left/up inside the screen with the 20 GUI margin; the root rect the dock places and
            // saves is never touched. Measured every frame from the root's current screen rect, so a dock move re-fits at once.
            Vector2 shift = Vector2.zero;
            {
                var c = new Vector3[4]; _win.GetWorldCorners(c);
                var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
                Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, c[0]), tr = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                float bot = bl.y, top = tr.y;
                float ppu = _win.rect.height > 1f ? (top - bot) / _win.rect.height : 1f;   // screen px per window-local unit
                float inv = 1f / Mathf.Max(0.0001f, ppu);
                float gui = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
                float m = GrowTopMargin * gui;
                float overR = tr.x - (Screen.width - m);              // px past the right margin
                if (overR > 0f) shift.x = -Mathf.Min(overR, Mathf.Max(0f, bl.x - m)) * inv;   // never past the left margin
                float overB = m - bot;                                 // px below the bottom margin
                if (overB > 0f) shift.y = Mathf.Min(overB, Mathf.Max(0f, Screen.height - m - top)) * inv;
                shift = new Vector2(Mathf.Round(shift.x), Mathf.Round(shift.y));
                // Core's dock can re-place the ROOT while typing (and the
                // per-frame shift followed it down): the compact drawn bottom-right is captured in screen px while INACTIVE; while ACTIVE the
                // visual shift pins the drawn bottom-right to that point whatever the root does; growth is upward only, capped by the room
                // to the top margin. The root's position is never written here.
                if (!active) { _anchorBR = new Vector2(tr.x + shift.x * ppu, bot + shift.y * ppu); _anchorValid = true; }
                else if (_anchorValid) shift = new Vector2((_anchorBR.x - tr.x) * inv, (_anchorBR.y - bot) * inv);   // unrounded = sub-px exact
                if (active)
                {
                    float room = (Screen.height - m - top) * inv - shift.y;   // the grown top respects the screen top after the shift
                    // chat OPENED as a giant window: the grow was a flat GrowMax (300) the moment the
                    // input took focus, so every open doubled the panel. Open = the compact/saved size; the panel grows up only by
                    // the extra rows the typed text actually wraps to (_inputReserve), so the message area never shrinks while typing.
                    float want = Mathf.Max(0f, _inputReserve);
                    extra = Mathf.Floor(Mathf.Clamp(Mathf.Min(want, room), 0f, GrowMax));
                }
            }
            if (Mathf.Abs(extra - _growExtra) <= 0.01f && (shift - _visShift).sqrMagnitude <= 0.0001f && _growImg.enabled == (extra > 0f || shift != Vector2.zero)) return;
            _growExtra = extra; _visShift = shift;
            bool on = extra > 0f || shift != Vector2.zero;
            _growBg.offsetMin = shift; _growBg.offsetMax = new Vector2(shift.x, shift.y + extra);
            _growImg.enabled = on;
            if (_winImg != null) _winImg.color = on ? new Color(_winImgColor.r, _winImgColor.g, _winImgColor.b, 0f) : _winImgColor;   // alpha 0 keeps its raycast
            _headRect.anchoredPosition = new Vector2(shift.x, -9f + extra + shift.y);
            _bodyRect.offsetMin = new Vector2(12f + shift.x, 12f + shift.y);
            _bodyRect.offsetMax = new Vector2(-12f + shift.x, -(HeadH + 14f) + extra + shift.y);
            _lastVpH = -1f;   // re-fit the content to the new viewport height on this frame's check
        }

        // the VISUAL panel rect (grown background while active, else the root) + input handles.
        internal RectTransform TestVisual => _growImg != null && _growImg.enabled ? _growBg : _win;
        internal float TestGrowExtra => _growExtra;
        internal RectTransform TestInputRow => _inputMirror != null ? _inputMirror.transform.parent as RectTransform : null;
        internal TMP_Text TestInputMirror => _inputMirror;

        // read-only handles for the test.
        internal RectTransform TestWindow => _win;
        internal RectTransform TestViewport => _vpRect;
        internal TMP_Text TestBody => _bodyText;
        internal ScrollRect TestScroll => _scroll;

        private void SyncInputRow(bool focused)
        {
            string text = Chat.instance.m_input.text ?? "";
            // Prefix only while typing (plain ':' — the '▸' glyph is missing from the game font and drew a box),
            // and the placeholder yields whenever the prefix is up so they can never overlap.
            bool showPrefix = focused || text.Length > 0;
            string chan = ChatChannelManager.GetChannelDisplayName(ChatChannelManager.ActiveChannel);
            _inputMirror.text = showPrefix ? "<color=#FFD980>" + chan + ":</color> " + text : "";
            _placeholder.gameObject.SetActive(!showPrefix);

            // Re-assert wrapping every frame — NewTxt() creates the mirror with wrapping OFF, and something in the
            // TMP/layout pipeline appears to win over the one-time enable, leaving the line to run off-screen.
            _inputMirror.enableWordWrapping = true;
            _inputMirror.overflowMode = TextOverflowModes.Overflow;

            // Grow the input row to fit wrapped text (one line → up to ~5), pushing the message body up so they never overlap.
            var irt = _inputMirror.transform.parent as RectTransform;
            if (irt != null)
            {
                float wrapW = Mathf.Max(40f, irt.rect.width - 26f);
                float pref = string.IsNullOrEmpty(_inputMirror.text) ? 0f : _inputMirror.GetPreferredValues(_inputMirror.text, wrapW, 0f).y;
                // 'input line 5 y 298-322 px in row 300-433': the cap (InputH + 56) was below 5 wrapped lines + the 8 unit
                // text inset, so the last line hung under the row. Cap = 5 lines; past it the mirror scrolls so the caret's line stays in.
                float rowCap = InputH + 18f * 4f;
                float rowH = Mathf.Clamp(pref + 12f, InputH, rowCap);
                float scrollUp = Mathf.Max(0f, pref + 12f - rowCap);
                var mrt = _inputMirror.rectTransform;
                if (Mathf.Abs(mrt.offsetMax.y - (-4f + scrollUp)) > 0.1f) { mrt.offsetMin = new Vector2(12f, 4f + scrollUp); mrt.offsetMax = new Vector2(-14f, -4f + scrollUp); }
                if (_inputMask == null) { _inputMask = irt.gameObject.GetComponent<RectMask2D>(); if (_inputMask == null) _inputMask = irt.gameObject.AddComponent<RectMask2D>(); }   // scrolled-off top lines clip
                if (Mathf.Abs(irt.sizeDelta.y - rowH) > 0.5f)
                {
                    irt.sizeDelta = new Vector2(irt.sizeDelta.x, rowH);
                    // fixed message area: the viewport rect is NOT moved any more (it was re-written here whenever the
                    // input wrapped, so the message area resized while typing); the taller row overlays the viewport's bottom and the
                    // content gets that much bottom margin so the newest line stays visible above the row.
                    _inputReserve = rowH - InputH;
                    FitContent(true);
                }
                if (text.Length > 20 && Time.frameCount % 45 == 0)
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[ChatWrap] len={text.Length} rowW={irt.rect.width:F0} wrapW={wrapW:F0} pref={pref:F0} rowH={rowH:F0} wrap={_inputMirror.enableWordWrapping} of={_inputMirror.overflowMode} mirrorW={_inputMirror.rectTransform.rect.width:F0}");
            }

            bool caretOn = focused && (Time.unscaledTime % 1.06f) < 0.53f;
            if (_caret.gameObject.activeSelf != caretOn) _caret.gameObject.SetActive(caretOn);
            if (caretOn)
            {
                _inputMirror.ForceMeshUpdate();
                var ti = _inputMirror.textInfo;
                if (ti != null && ti.characterCount > 0)
                {
                    var ci = ti.characterInfo[ti.characterCount - 1];   // caret sits just past the last glyph (coords are mirror-local, top-left origin)
                    _caret.rectTransform.anchoredPosition = new Vector2(ci.topRight.x, (ci.topRight.y + ci.bottomRight.y) * 0.5f);
                }
                else _caret.rectTransform.anchoredPosition = new Vector2(0f, -9f);
            }
        }

        // ── tabs ────────────────────────────────────────────────────────────────────────────────────────────────
        private void OnTabClick(ChatChannelManager.Channel ch)
        {
            _pokerView = false;
            _lastBufCount = -1;   // force a body rebuild back to the chat buffer
            if (!ChatChannelManager.IsChannelAvailable(ch))
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    ChatChannelManager.GetChannelDisplayName(ch) + " chat — coming soon!");
            else
                ChatChannelManager.ActiveChannel = ch;
            RefreshTabs();
            Chat.instance?.m_input?.ActivateInputField();
        }

        private void OnChannelChanged(ChatChannelManager.Channel _) => RefreshTabs();

        private void RefreshTabs()
        {
            if (_tabBgs == null) return;
            var channels = (ChatChannelManager.Channel[])Enum.GetValues(typeof(ChatChannelManager.Channel));
            var active = ChatChannelManager.ActiveChannel;
            for (int i = 0; i < channels.Length && i < _tabBgs.Length; i++)
            {
                bool sel = channels[i] == active && !_pokerView;
                bool avl = ChatChannelManager.IsChannelAvailable(channels[i]);
                if (_tabFrames[i] != null) _tabFrames[i].enabled = sel;
                if (_tabBgs[i] != null) _tabBgs[i].color = sel ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.9f);
                if (_tabLbls[i] != null)
                    _tabLbls[i].color = sel ? FiresChatSkin.TabOn : avl ? FiresChatSkin.Dim : new Color(0.42f, 0.36f, 0.28f, 0.7f);
            }
            if (_pokerTabFrame != null) _pokerTabFrame.enabled = _pokerView;
            if (_pokerTabBg != null) _pokerTabBg.color = _pokerView ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.9f);
            if (_pokerTabLbl != null) _pokerTabLbl.color = _pokerView ? FiresChatSkin.TabOn : FiresChatSkin.Dim;
        }

        private void HandleTabCycle(bool focused)
        {
            if (!focused) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab))
            {
                ChatChannelManager.CycleChannel();
                Chat.instance.m_input.ActivateInputField();
            }
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            _bodyGroup.SetActive(!_collapsed);
            if (_collapsed) ApplyActiveGrow(false);   // collapse never grows
            _win.sizeDelta = new Vector2(CurW, _collapsed ? HeadH + 22f : CurH);
        }

        // ── arrange mode (Alt+C): free cursor, drag to move, corner grip to resize ────────────────────────────
        private void BuildArrangeHandles()
        {
            _dragSurf = new GameObject("DragSurf", typeof(RectTransform)).gameObject;
            var ds = (RectTransform)_dragSurf.transform;
            ds.SetParent(_win, false);
            ds.anchorMin = Vector2.zero; ds.anchorMax = Vector2.one; ds.offsetMin = Vector2.zero; ds.offsetMax = Vector2.zero;
            var dsImg = _dragSurf.AddComponent<Image>();
            dsImg.color = new Color(0.85f, 0.62f, 0.26f, 0.07f);
            dsImg.raycastTarget = true;
            AddDrag(_dragSurf, d =>
            {
                float s = Mathf.Max(0.6f, _lastScale);
                if (ChatChannelManager.OffsetX != null) ChatChannelManager.OffsetX.Value += d.delta.x / s;
                if (ChatChannelManager.OffsetY != null) ChatChannelManager.OffsetY.Value += d.delta.y / s;
            });
            _dragSurf.SetActive(false);

            _resizeGrip = new GameObject("Grip", typeof(RectTransform)).gameObject;
            var rg = (RectTransform)_resizeGrip.transform;
            rg.SetParent(_win, false);
            rg.anchorMin = rg.anchorMax = new Vector2(0f, 0f); rg.pivot = new Vector2(0f, 0f);
            rg.sizeDelta = new Vector2(22f, 22f); rg.anchoredPosition = new Vector2(3f, 3f);
            var rgImg = _resizeGrip.AddComponent<Image>();
            if (FiresChatSkin.Handle != null) { rgImg.sprite = FiresChatSkin.Handle; rgImg.type = Image.Type.Sliced; }
            else rgImg.color = new Color(0.85f, 0.62f, 0.26f, 0.9f);
            rgImg.raycastTarget = true;
            AddDrag(_resizeGrip, d =>
            {
                if (ChatChannelManager.UiScale == null) return;
                float delta = -(d.delta.x + d.delta.y) * 0.004f;
                ChatChannelManager.UiScale.Value = Mathf.Clamp(ChatChannelManager.UiScale.Value + delta, 0.6f, 2.5f);
            });
            _resizeGrip.SetActive(false);
        }

        private void HandleArrange()
        {
            var key = ChatChannelManager.ArrangeKey;
            if (key != null && FiresCore.Input.Chord.IsDown(key.Value)) ToggleArrange(!_arranging);
            else if (_arranging && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) ToggleArrange(false);
        }

        private void ToggleArrange(bool on)
        {
            if (on == _arranging) return;
            if (on && _dockOwned)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Chat is placed through the HUD layout mode now.");
                return;
            }
            _arranging = on;
            FiresCore.UI.InputBlock.Block(on);
            _dragSurf.SetActive(on);
            _resizeGrip.SetActive(on);
            if (on) MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                "Chat arrange - drag to move, corner grip to resize. Alt+C or Esc to finish.");
        }

        // Every HUD element is movable: the chat window registers with Core's HUD dock (0.2.522+) as
        // "firesnpcs.chatchannels". While registered, the dock moves/scales it and saves the layout, so the chat's own
        // Offset/Scale config and arrange mode stop writing. Older Core: own arrange mode as before.
        private bool _dockOwned;
        private RectTransform _headRect;
        private float CurW => ChatChannelManager.PanelWidth != null ? Mathf.Clamp(ChatChannelManager.PanelWidth.Value, 300f, 2000f) : WinW;
        private float CurH => ChatChannelManager.PanelHeight != null ? Mathf.Clamp(ChatChannelManager.PanelHeight.Value, 120f, 1400f) : WinH;
        private Vector2 _sizePending; private float _sizeDue = -1f;
        private void OnDockSize(Vector2 size)
        {
            _sizePending = new Vector2(size.x, _collapsed ? CurH : size.y);   // a collapsed window keeps its open height
            _sizeDue = Time.unscaledTime + 0.5f;
        }
        private void ResetDockSize()
        {
            _sizeDue = -1f;
            if (ChatChannelManager.PanelWidth != null) ChatChannelManager.PanelWidth.Value = WinW;
            if (ChatChannelManager.PanelHeight != null) ChatChannelManager.PanelHeight.Value = WinH;
            if (_win != null) _win.sizeDelta = new Vector2(WinW, _collapsed ? HeadH + 22f : WinH);
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresChat] chat window size reset to {WinW:0}x{WinH:0} (Core layout Reset)");
        }
        private void SaveDockSize()
        {
            if (_sizeDue < 0f || Time.unscaledTime < _sizeDue) return;
            _sizeDue = -1f;
            if (ChatChannelManager.PanelWidth != null) ChatChannelManager.PanelWidth.Value = Mathf.Clamp(_sizePending.x, 300f, 2000f);
            if (ChatChannelManager.PanelHeight != null) ChatChannelManager.PanelHeight.Value = Mathf.Clamp(_sizePending.y, 120f, 1400f);
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresChat] chat window size saved {_sizePending.x:0}x{_sizePending.y:0} (Core grip)");
        }
        private void RegisterWithDock()
        {
            if (_dockOwned || _win == null) return;
            try
            {
                var shell = Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore", false);
                // the options type is FiresCore.UI.Shell.HudElementOptions, not ...Dock...; take it from the method itself.
                var reg = shell == null ? null : System.Linq.Enumerable.FirstOrDefault(shell.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static), m => m.Name == "RegisterHudElement" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType == typeof(RectTransform));
                var optType = reg?.GetParameters()[2].ParameterType;
                if (reg == null) { global::FiresRPGmaker.FiresLogger.LogDebug("[FiresChat] Core has no HUD dock (RegisterHudElement, 0.2.522+): the chat keeps its own arrange mode"); return; }
                // chat misaligned vs his pre-overhaul default: the dock takes the window's CURRENT rect as its
                // default when no layout is saved, and this ran right after Build(), before the first ApplyLayoutConfig placed it. Place it
                // exactly where the pre-dock code does (anchor -24/22 + the Offset config, Scale config) first.
                ApplyLayoutConfig();
                var opt = Activator.CreateInstance(optType);
                optType.GetField("Owner")?.SetValue(opt, "FiresRPGmaker");
                optType.GetField("Label")?.SetValue(opt, "Chat channels");
                optType.GetField("Resizable")?.SetValue(opt, true);
                // a REAL resize - diagonal both axes, straight one axis: Core 0.2.578 SizeChanged + MinSize; Core sets
                // sizeDelta per axis, the window's children are stretch-anchored so they re-lay, and the size is saved here (debounced)
                optType.GetField("SizeChanged")?.SetValue(opt, (Action<Vector2>)OnDockSize);
                optType.GetField("MinSize")?.SetValue(opt, new Vector2(300f, 120f));
                // Core 0.2.579 SizeReset: the layout Reset also puts the chat back to its default 460x300
                optType.GetField("SizeReset")?.SetValue(opt, (Action)ResetDockSize);
                // Core 0.2.583 HudElementOptions.LiveEdit + DragHandle: the chat moves by its header and resizes by its corner at ANY
                // time, as its own arrange mode did before the dock - not only inside the layout editor
                optType.GetField("LiveEdit")?.SetValue(opt, true);
                optType.GetField("DragHandle")?.SetValue(opt, _headRect);
                reg.Invoke(null, new object[] { "firesnpcs.chatchannels", _win, opt });
                _dockOwned = true;
                if (_arranging) ToggleArrange(false);
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresChat] registered with Core's HUD dock as 'firesnpcs.chatchannels' (layout mode moves/scales it; own arrange off)");
            }
            catch (Exception ex) { _dockOwned = false; Debug.LogWarning("[FiresChat] HUD dock registration failed, keeping own arrange: " + (ex.InnerException ?? ex).Message); }
        }

        private static void AddDrag(GameObject go, Action<PointerEventData> onDrag)
        {
            var trig = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            entry.callback.AddListener(d => onDrag((PointerEventData)d));
            trig.triggers.Add(entry);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────
        private static Image NewImg(Transform parent, string name, Sprite sprite, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite; img.type = type; img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI NewTxt(Transform parent, string text, float size, Color c)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false; t.enableWordWrapping = false; t.richText = true;
            return t;
        }

        private static void FillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static TMP_FontAsset GrabFont()
        {
            var field = Chat.instance?.m_input as TMP_InputField;
            if (field?.textComponent?.font != null) return field.textComponent.font;
            return null;
        }
    }
}
