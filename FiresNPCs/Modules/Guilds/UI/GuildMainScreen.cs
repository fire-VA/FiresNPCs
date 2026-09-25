using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Modules.Guilds.UI
{
    /// <summary>
    /// Main guild management screen - tabbed layout:
    ///   [Roster] [Applications] [Chat] [Settings]
    ///
    /// STYLE
    /// -----
    /// Matches the warm brown/gold palette used by CompanionRadialMenu and
    /// GroupHudController throughout the mod.  All geometry is procedural
    /// Unity UI - no asset-bundle dependency.
    /// </summary>
    public class GuildMainScreen : MonoBehaviour
    {
        // -- Events back to GuildScreenManager --------------------------------
        public event Action OnClose;
        public event Action OnOpenSearch;
        public event Action OnOpenCreate;

        // -- Colours (match existing mod palette) ------------------------------
        // Match the info-UI panel opacity (UIFontConfig.Colors.PanelBackground = alpha 0.85)
        // so guild screens feel consistent with InfoNpcPlayerPanel and friends.
        private static readonly Color BgColor      = new Color(0.04f, 0.04f, 0.06f, 0.85f);
        private static readonly Color PanelColor   = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        private static readonly Color BorderColor  = new Color(0.35f, 0.27f, 0.14f, 1f);
        private static readonly Color GoldAccent   = new Color(0.83f, 0.64f, 0.31f, 1f);
        private static readonly Color TextNormal   = new Color(0.85f, 0.80f, 0.65f, 1f);
        private static readonly Color TextMuted    = new Color(0.55f, 0.50f, 0.40f, 1f);
        private static readonly Color TextDanger   = new Color(0.85f, 0.30f, 0.25f, 1f);
        private static readonly Color RowEven      = new Color(0.10f, 0.08f, 0.05f, 0.60f);
        private static readonly Color RowOdd       = new Color(0.14f, 0.11f, 0.08f, 0.60f);
        private static readonly Color TabActive    = new Color(0.25f, 0.19f, 0.11f, 1f);
        private static readonly Color TabInactive  = new Color(0.14f, 0.11f, 0.08f, 1f);
        private static readonly Color BtnNormal    = new Color(0.22f, 0.17f, 0.10f, 1f);
        private static readonly Color BtnHover     = new Color(0.35f, 0.27f, 0.14f, 1f);
        private static readonly Color BtnDanger    = new Color(0.40f, 0.12f, 0.10f, 1f);
        private static readonly Color OnlineColor  = new Color(0.40f, 0.85f, 0.40f, 1f);
        private static readonly Color OfflineColor = new Color(0.45f, 0.40f, 0.35f, 1f);

        // -- Layout constants --------------------------------------------------
        private const float SCREEN_W  = 760f;
        private const float SCREEN_H  = 540f;
        private const float TAB_H     = 34f;
        private const float HEADER_H  = 60f;
        private const float BTN_H     = 28f;
        private const float BTN_W     = 110f;
        private const float ROW_H     = 36f;
        private const float PADDING   = 10f;
        private const float FONT_SM   = 11f;
        private const float FONT_MD   = 13f;
        private const float FONT_LG   = 16f;

        // -- State -------------------------------------------------------------
        private GuildData _guild;
        private bool _built;
        private bool _isDragging;
        private Vector2 _dragOffset;
        private RectTransform _headerRT;

        private enum Tab { Roster, Applications, Chat, Settings }
        private Tab _activeTab = Tab.Roster;

        // -- Root objects ------------------------------------------------------
        private GameObject     _canvasRoot;
        private GameObject     _root;
        private GameObject     _tabContent;

        // Tab buttons
        private readonly Button[] _tabBtns = new Button[4];
        private readonly Image[]  _tabBgs  = new Image[4];

        // Header
        private TextMeshProUGUI _guildNameTxt;
        private TextMeshProUGUI _guildDescTxt;
        private Image           _guildColorSwatch;
        private TextMeshProUGUI _memberCountTxt;

        // Roster tab
        private GameObject _rosterTab;
        private Transform  _rosterList;

        // Applications tab
        private GameObject _appsTab;
        private Transform  _appsList;
        private TextMeshProUGUI _noAppsTxt;

        // Chat tab
        private GameObject     _chatTab;
        private Transform      _chatLog;
        private ScrollRect     _chatScrollRect;
        private TMP_InputField _chatInput;
        private readonly List<TextMeshProUGUI> _chatLines = new List<TextMeshProUGUI>();
        private const int MAX_CHAT_LINES = 50;

        // Persistent chat-history buffer - captures messages even when the
        // panel is closed so opening the Chat tab shows the recent backlog.
        // Subscribed once on first Show(); never unsubscribed.
        private static readonly List<string> _chatHistory = new List<string>();
        private static bool _chatHistoryHooked;
        private const int MAX_CHAT_HISTORY = 100;

        // Settings tab
        private GameObject     _settingsTab;
        private TMP_InputField _nameInput;
        private TMP_InputField _descInput;
        private Toggle         _openToggle;
        private Image          _colorPreview;
        private Slider         _colorR, _colorG, _colorB;

        // -- Lifecycle ---------------------------------------------------------

        public void Show(GuildData guild)
        {
            _guild = guild;
            if (!_built) Build();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            _root.SetActive(true);

            EnsureChatHistoryHook();
            GuildAPI.OnGuildsUpdated += OnGuildsUpdated;
            GuildAPI.OnGuildChatReceived += OnChatReceived;

            // Replay backlog into the chat log on every show so reopening the
            // panel doesn't show an empty conversation.
            RebuildChatLogFromHistory();

            RefreshAll();

            GUIManager.BlockInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Debug.Log($"[GuildMain] Show() - InputBlocked={GUIManager.IsInputBlocked} " +
                      $"Cursor.visible={Cursor.visible} Cursor.lockState={Cursor.lockState} " +
                      $"EventSystem={(EventSystem.current != null ? EventSystem.current.name : "NULL")}");
        }

        public void Hide()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;
            GuildAPI.OnGuildChatReceived -= OnChatReceived;
            _isDragging = false;
            GUIManager.BlockInput(false);
        }

        private void OnDestroy()
        {
            GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;
            GuildAPI.OnGuildChatReceived -= OnChatReceived;
        }

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { OnClose?.Invoke(); return; }
            HandleDrag();
        }

        private void HandleDrag()
        {
            if (_headerRT == null) return;
            var rootRT = _root.GetComponent<RectTransform>();
            if (rootRT == null) return;
            Vector2 mousePos = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
                // Drag only when the click is in the title bar AND the EventSystem
                // didn't already route the click to a button/input/toggle.
                if (RectTransformUtility.RectangleContainsScreenPoint(_headerRT, mousePos, null)
                    && !PointerOverInteractive())
                {
                    _isDragging = true;
                    _dragOffset = (Vector2)rootRT.position - mousePos;
                }
            }
            if (_isDragging && Input.GetMouseButton(0))
                rootRT.position = mousePos + _dragOffset;
            if (_isDragging && Input.GetMouseButtonUp(0))
                _isDragging = false;
        }

        private static bool PointerOverInteractive()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = Input.mousePosition };
            var results = new List<RaycastResult>(8);
            es.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++)
            {
                var t = results[i].gameObject;
                if (t == null) continue;
                if (t.GetComponentInParent<Selectable>() != null) return true;
            }
            return false;
        }

        // -- Event callbacks ---------------------------------------------------

        private void OnGuildsUpdated(System.Collections.Generic.IReadOnlyList<GuildData> guilds)
        {
            // Refresh with the updated version of our guild.
            _guild = GuildAPI.GetOwnGuild();
            if (_guild == null) { OnClose?.Invoke(); return; }
            RefreshAll();
        }

        private void OnChatReceived(string sender, string guildName, string text)
        {
            // History is already updated by the static hook; just push to the
            // visible log (avoids duplication).
            AppendChatLineToUI(FormatChatLine(sender, text));
        }

        private static string FormatChatLine(string sender, string text)
            => $"<b>{sender}</b>: {text}";

        private static void EnsureChatHistoryHook()
        {
            if (_chatHistoryHooked) return;
            _chatHistoryHooked = true;
            GuildAPI.OnGuildChatReceived += (sender, guildName, text) =>
            {
                _chatHistory.Add(FormatChatLine(sender, text));
                while (_chatHistory.Count > MAX_CHAT_HISTORY)
                    _chatHistory.RemoveAt(0);
            };
            // When the server ships the persisted backlog, replace the
            // in-memory cache so the Chat tab shows previous-session messages.
            GuildAPI.OnGuildChatHistoryReceived += (guildName, messages) =>
            {
                _chatHistory.Clear();
                if (messages != null)
                    foreach (var m in messages)
                        if (m != null && !string.IsNullOrEmpty(m.Text))
                            _chatHistory.Add(FormatChatLine(m.SenderName ?? "?", m.Text));
                // Force any open Chat tab to redraw.
                var inst = GuildScreenManager.Instance;
                inst?.RefreshOpenChatLogs();
            };
        }

        private void RebuildChatLogFromHistory()
        {
            if (_chatLog == null) return;
            for (int i = _chatLines.Count - 1; i >= 0; i--)
                if (_chatLines[i] != null) Destroy(_chatLines[i].gameObject);
            _chatLines.Clear();
            foreach (var line in _chatHistory)
                AppendChatLineToUI(line);
        }

        /// <summary>Called by GuildScreenManager when the server pushes a fresh chat backlog.</summary>
        public void RebuildChatLogPublic() => RebuildChatLogFromHistory();

        // -- Full UI build -----------------------------------------------------

        private void Build()
        {
            // DEDICATED CANVAS - we used to share Hud.instance's Canvas to inherit
            // its cursor/raycaster behavior, but that caused a Canvas Rebuild Storm:
            // every guild-UI layout change dirtied the entire HUD canvas (575+ HUD
            // nodes), tanking FPS to ~5 while the screen was open. The cursor
            // works without parenting under HUD because GuildScreenManager is
            // registered in GUIManager.IsCustomPanelOpen(), which makes the
            // Menu.IsVisible Harmony postfix return true and keeps vanilla from
            // re-locking the cursor. So: own Canvas + own GraphicRaycaster, same
            // pattern as CompanionRadialMenu.
            _canvasRoot = new GameObject("GuildMainCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot.transform.SetParent(transform, false);

            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;

            var scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var canvasRect = _canvasRoot.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            _root = MakePanel(_canvasRoot.transform, SCREEN_W, SCREEN_H, BgColor);
            var rootRT = _root.GetComponent<RectTransform>();
            rootRT.anchorMin = rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            rootRT.anchoredPosition = Vector2.zero;

            // Solid semi-transparent background to match the info-UI panels.
            // (Previously this path optionally used crafting_panel_bkg at alpha
            // 0.75, which made the guild screens look noticeably different
            // from InfoNpcPlayerPanel etc. - using the shared PanelBackground
            // keeps the whole mod's panel styling consistent.)
            var rootBg = _root.GetComponent<Image>();
            rootBg.sprite = null;
            rootBg.color = UIFontConfig.Colors.PanelBackground;

            AddBorder(_root, BorderColor);
            BuildHeader(_root.transform);
            BuildTabs(_root.transform);
            BuildTabContent(_root.transform);

            _built = true;
        }

        private void BuildHeader(Transform parent)
        {
            var header = MakePanel(parent, SCREEN_W, HEADER_H, PanelColor, 0, SCREEN_H / 2 - HEADER_H / 2);
            var hl = header.GetComponent<RectTransform>();
            hl.anchorMin = new Vector2(0, 1); hl.anchorMax = new Vector2(1, 1);
            hl.offsetMin = new Vector2(0, -HEADER_H); hl.offsetMax = Vector2.zero;
            _headerRT = hl;

            // Color swatch
            _guildColorSwatch = MakeImage(header.transform, "ColorSwatch", GoldAccent);
            var sw = _guildColorSwatch.GetComponent<RectTransform>();
            sw.anchorMin = sw.anchorMax = new Vector2(0, 0.5f);
            sw.sizeDelta = new Vector2(14f, 36f);
            sw.anchoredPosition = new Vector2(8f, 0);

            // Guild name (decorative Norse font for the header)
            _guildNameTxt = MakeText(header.transform, "", FONT_LG, TextNormal, FontStyles.Bold);
            UIBuilderHelper.ApplyValheimFont(_guildNameTxt);
            var nt = _guildNameTxt.GetComponent<RectTransform>();
            nt.anchorMin = new Vector2(0, 0.5f); nt.anchorMax = new Vector2(0.6f, 1f);
            nt.offsetMin = new Vector2(28f, 0); nt.offsetMax = new Vector2(-PADDING, 0);

            // Description
            _guildDescTxt = MakeText(header.transform, "", FONT_SM, TextMuted, FontStyles.Normal);
            var dt = _guildDescTxt.GetComponent<RectTransform>();
            dt.anchorMin = new Vector2(0, 0); dt.anchorMax = new Vector2(0.7f, 0.5f);
            dt.offsetMin = new Vector2(28f, 0); dt.offsetMax = new Vector2(-PADDING, 0);

            // Close button - small, anchored to the very top-right corner
            // so it doesn't compete for space with the member count.
            const float CLOSE_SIZE = 22f;
            var closeBtn = MakeButton(header.transform, "X", CLOSE_SIZE, CLOSE_SIZE, BtnDanger, TextDanger);
            var cbRT = closeBtn.GetComponent<RectTransform>();
            cbRT.anchorMin = cbRT.anchorMax = new Vector2(1f, 1f);
            cbRT.pivot = new Vector2(1f, 1f);
            cbRT.anchoredPosition = new Vector2(-4f, -4f);
            cbRT.sizeDelta = new Vector2(CLOSE_SIZE, CLOSE_SIZE);
            closeBtn.GetComponent<Button>().onClick.AddListener(() => OnClose?.Invoke());

            // Member count (right side, leaves room for the close button corner).
            _memberCountTxt = MakeText(header.transform, "", FONT_SM, TextMuted, FontStyles.Normal);
            var mc = _memberCountTxt.GetComponent<RectTransform>();
            mc.anchorMin = new Vector2(0.6f, 0); mc.anchorMax = new Vector2(1f, 1f);
            mc.offsetMin = new Vector2(0, PADDING);
            mc.offsetMax = new Vector2(-(CLOSE_SIZE + 12f), -PADDING);
            _memberCountTxt.alignment = TextAlignmentOptions.MidlineRight;
        }

        private void BuildTabs(Transform parent)
        {
            string[] labels = { "Roster", "Applications", "Chat", "Settings" };
            float tabW = SCREEN_W / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var tab = MakePanel(parent, tabW, TAB_H, TabInactive);
                var rt = tab.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2((float)i / labels.Length, 1);
                rt.anchorMax = new Vector2((float)(i + 1) / labels.Length, 1);
                rt.offsetMin = new Vector2(0, -(HEADER_H + TAB_H));
                rt.offsetMax = new Vector2(0, -HEADER_H);

                var lbl = MakeText(tab.transform, labels[i], FONT_MD, TextNormal, FontStyles.Normal);
                var lrt = lbl.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                lbl.alignment = TextAlignmentOptions.Center;

                var btn = tab.AddComponent<Button>();
                btn.targetGraphic = tab.GetComponent<Image>();
                btn.onClick.AddListener(() => SwitchTab((Tab)idx));

                _tabBtns[i] = btn;
                _tabBgs[i]  = tab.GetComponent<Image>();
            }
        }

        private void BuildTabContent(Transform parent)
        {
            _tabContent = new GameObject("TabContent", typeof(RectTransform));
            _tabContent.transform.SetParent(parent, false);
            var rt = _tabContent.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0, 0);
            rt.offsetMax = new Vector2(0, -(HEADER_H + TAB_H));

            BuildRosterTab();
            BuildApplicationsTab();
            BuildChatTab();
            BuildSettingsTab();

            SwitchTab(Tab.Roster);
        }

        // -- Roster tab --------------------------------------------------------

        private void BuildRosterTab()
        {
            _rosterTab = MakeScrollView(_tabContent.transform, out _rosterList);
        }

        private void RefreshRoster()
        {
            ClearChildren(_rosterList);
            if (_guild == null) return;

            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            bool canManage = _guild.GetMember(localId)?.IsOfficer ?? false;

            int row = 0;
            foreach (var member in _guild.Members)
            {
                bool online = GuildAPI.IsPlayerOnline(member.PlayerId);
                bool isSelf = member.PlayerId == localId;
                var rowBg = (row % 2 == 1) ? RowOdd : RowEven;
                row++;

                var rowGO = MakePanel(_rosterList, 0, ROW_H, rowBg);
                var rrt = rowGO.GetComponent<RectTransform>();
                rrt.sizeDelta = new Vector2(0, ROW_H);
                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.minHeight = ROW_H;
                rowLE.preferredHeight = ROW_H;

                // Online dot
                var dot = MakeImage(rowGO.transform, "Dot", online ? OnlineColor : OfflineColor);
                var drt = dot.GetComponent<RectTransform>();
                drt.anchorMin = drt.anchorMax = new Vector2(0, 0.5f);
                drt.sizeDelta = new Vector2(8, 8);
                drt.anchoredPosition = new Vector2(10f, 0);

                // Player name
                var nameTxt = MakeText(rowGO.transform, member.PlayerName, FONT_MD, TextNormal, isSelf ? FontStyles.Bold : FontStyles.Normal);
                var nrt = nameTxt.GetComponent<RectTransform>();
                nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(0.35f, 1);
                nrt.offsetMin = new Vector2(24f, 0); nrt.offsetMax = Vector2.zero;

                // Rank
                var rankTxt = MakeText(rowGO.transform, member.RankLabel(), FONT_SM, GoldAccent, FontStyles.Normal);
                var rkt = rankTxt.GetComponent<RectTransform>();
                rkt.anchorMin = new Vector2(0.35f, 0); rkt.anchorMax = new Vector2(0.55f, 1);
                rkt.offsetMin = rkt.offsetMax = Vector2.zero;
                rankTxt.alignment = TextAlignmentOptions.Center;

                // Last seen
                string lastSeen = online ? "Online" : FormatLastSeen(member.LastOnlineUtcTicks);
                var seenTxt = MakeText(rowGO.transform, lastSeen, FONT_SM, TextMuted, FontStyles.Normal);
                var srt = seenTxt.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.55f, 0); srt.anchorMax = new Vector2(0.75f, 1);
                srt.offsetMin = srt.offsetMax = Vector2.zero;
                seenTxt.alignment = TextAlignmentOptions.Center;

                // Management buttons (only for officers+, only for lower-rank members)
                if (canManage && !isSelf && _guild.CanKick(localId, member.PlayerId))
                {
                    long memberId = member.PlayerId;
                    var btnContainer = new GameObject("BtnContainer", typeof(RectTransform));
                    btnContainer.transform.SetParent(rowGO.transform, false);
                    var brt = btnContainer.GetComponent<RectTransform>();
                    brt.anchorMin = new Vector2(0.75f, 0); brt.anchorMax = new Vector2(1f, 1f);
                    brt.offsetMin = new Vector2(PADDING, 3f); brt.offsetMax = new Vector2(-PADDING, -3f);
                    var bhl = btnContainer.AddComponent<HorizontalLayoutGroup>();
                    bhl.spacing = 4f; bhl.childAlignment = TextAnchor.MiddleRight;
                    bhl.childControlWidth = false; bhl.childControlHeight = true;

                    if (_guild.CanPromote(localId, memberId))
                        AddSmallButton(btnContainer.transform, "\u2191", BtnNormal, GoldAccent,
                            () => GuildAPI.PromoteMember(memberId));

                    if (_guild.CanDemote(localId, memberId))
                        AddSmallButton(btnContainer.transform, "\u2193", BtnNormal, TextMuted,
                            () => GuildAPI.DemoteMember(memberId));

                    if (_guild.CanKick(localId, memberId))
                        AddSmallButton(btnContainer.transform, "Kick", BtnDanger, TextDanger,
                            () => ConfirmKick(memberId, member.PlayerName));

                    // Transfer companion button (only if leader)
                    if (_guild.IsLeader(localId))
                        AddSmallButton(btnContainer.transform, "Give Comp", BtnNormal, TextNormal,
                            () => OpenTransferCompanionPicker(memberId, member.PlayerName));
                }
            }
        }

        // -- Applications tab --------------------------------------------------

        private void BuildApplicationsTab()
        {
            _appsTab = MakeScrollView(_tabContent.transform, out _appsList);
            _noAppsTxt = MakeText(_appsTab.transform, "No pending applications.", FONT_MD, TextMuted, FontStyles.Normal);
            var nrt = _noAppsTxt.GetComponent<RectTransform>();
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = Vector2.zero;
        }

        private void RefreshApplications()
        {
            ClearChildren(_appsList);
            if (_guild == null) return;

            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            bool canManage = _guild.CanManageApplications(localId);

            _noAppsTxt.gameObject.SetActive(_guild.Applications.Count == 0);

            int row = 0;
            foreach (var app in _guild.Applications)
            {
                row++;
                var rowGO = MakePanel(_appsList, 0, ROW_H * 2, row % 2 == 0 ? RowEven : RowOdd);
                var rrt = rowGO.GetComponent<RectTransform>();
                rrt.sizeDelta = new Vector2(0, ROW_H * 2);
                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.minHeight = ROW_H * 2;
                rowLE.preferredHeight = ROW_H * 2;

                var nameTxt = MakeText(rowGO.transform, app.PlayerName, FONT_MD, TextNormal, FontStyles.Bold);
                var nrt2 = nameTxt.GetComponent<RectTransform>();
                nrt2.anchorMin = new Vector2(0, 0.5f); nrt2.anchorMax = new Vector2(0.5f, 1f);
                nrt2.offsetMin = new Vector2(PADDING, 0); nrt2.offsetMax = Vector2.zero;

                var msgTxt = MakeText(rowGO.transform, app.Message, FONT_SM, TextMuted, FontStyles.Normal);
                var mrt = msgTxt.GetComponent<RectTransform>();
                mrt.anchorMin = Vector2.zero; mrt.anchorMax = new Vector2(0.65f, 0.5f);
                mrt.offsetMin = new Vector2(PADDING, 0); mrt.offsetMax = Vector2.zero;
                msgTxt.textWrappingMode = TextWrappingModes.Normal;

                if (canManage)
                {
                    long appId = app.PlayerId;
                    string appName = app.PlayerName;

                    var acceptBtn = MakeButton(rowGO.transform, "Accept", BTN_H, BTN_W,
                        new Color(0.15f, 0.35f, 0.15f), OnlineColor);
                    var abt = acceptBtn.GetComponent<RectTransform>();
                    abt.anchorMin = new Vector2(0.65f, 0.1f); abt.anchorMax = new Vector2(0.82f, 0.9f);
                    abt.offsetMin = abt.offsetMax = Vector2.zero;
                    acceptBtn.GetComponent<Button>().onClick.AddListener(() => GuildAPI.HandleApplication(appId, true));

                    var rejectBtn = MakeButton(rowGO.transform, "Reject", BTN_H, BTN_W, BtnDanger, TextDanger);
                    var rbt = rejectBtn.GetComponent<RectTransform>();
                    rbt.anchorMin = new Vector2(0.83f, 0.1f); rbt.anchorMax = new Vector2(1f, 0.9f);
                    rbt.offsetMin = new Vector2(0, 0); rbt.offsetMax = new Vector2(-PADDING, 0);
                    rejectBtn.GetComponent<Button>().onClick.AddListener(() => GuildAPI.HandleApplication(appId, false));
                }
            }
        }

        // -- Chat tab ----------------------------------------------------------

        private void BuildChatTab()
        {
            _chatTab = new GameObject("ChatTab", typeof(RectTransform));
            _chatTab.transform.SetParent(_tabContent.transform, false);
            var rt = _chatTab.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Scroll area for chat lines
            var chatScrollGO = MakeScrollView(_chatTab.transform, out _chatLog, reserveBottom: 40f);
            _chatScrollRect = chatScrollGO.GetComponent<ScrollRect>();

            // Chat input bar
            var inputBar = MakePanel(_chatTab.transform, 0, 36f, PanelColor);
            var ibrt = inputBar.GetComponent<RectTransform>();
            ibrt.anchorMin = new Vector2(0, 0); ibrt.anchorMax = new Vector2(1, 0);
            ibrt.offsetMin = Vector2.zero; ibrt.offsetMax = new Vector2(0, 36f);

            _chatInput = MakeTMPInputField(inputBar.transform, "Type a message...", SCREEN_W - BTN_W - PADDING * 3, 30f);
            var cirt = _chatInput.GetComponent<RectTransform>();
            cirt.anchorMin = new Vector2(0, 0.5f); cirt.anchorMax = new Vector2(1, 0.5f);
            cirt.offsetMin = new Vector2(PADDING, -15f); cirt.offsetMax = new Vector2(-(BTN_W + PADDING * 2), 15f);

            var sendBtn = MakeButton(inputBar.transform, "Send", BTN_H, BTN_W, BtnNormal, GoldAccent);
            var sbrt = sendBtn.GetComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(1, 0.5f); sbrt.anchorMax = new Vector2(1, 0.5f);
            sbrt.anchoredPosition = new Vector2(-(BTN_W / 2 + PADDING), 0);
            sbrt.sizeDelta = new Vector2(BTN_W, BTN_H);
            sendBtn.GetComponent<Button>().onClick.AddListener(SubmitChatMessage);

            _chatInput.onSubmit.AddListener(_ => SubmitChatMessage());
        }

        private void SubmitChatMessage()
        {
            var text = _chatInput?.text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            GuildAPI.SendChatMessage(text);
            _chatInput.text = "";
            _chatInput.ActivateInputField();
        }

        private void AppendChatLineToUI(string line)
        {
            if (_chatLog == null) return;

            var txt = MakeText(_chatLog, line, FONT_SM, TextNormal, FontStyles.Normal);
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.alignment = TextAlignmentOptions.TopLeft;
            txt.overflowMode = TextOverflowModes.Overflow;
            // TMP implements ILayoutElement.preferredHeight using the wrapped
            // text height - VLG (childControlHeight=true) uses that directly,
            // so no LayoutElement is needed for chat lines.

            _chatLines.Add(txt);

            // Trim to max lines
            while (_chatLines.Count > MAX_CHAT_LINES)
            {
                Destroy(_chatLines[0].gameObject);
                _chatLines.RemoveAt(0);
            }

            // Scroll to bottom on next frame after layout settles.
            if (_chatScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _chatScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        // -- Settings tab ------------------------------------------------------

        private void BuildSettingsTab()
        {
            _settingsTab = new GameObject("SettingsTab", typeof(RectTransform));
            _settingsTab.transform.SetParent(_tabContent.transform, false);
            var rt = _settingsTab.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Wrap absolute-positioned widgets in a manual scroll view so the
            // bottom buttons (Save / Transfer / Dissolve / Leave) are always
            // reachable even on small panel heights.
            Transform inner = MakeManualScrollView(_settingsTab.transform, out RectTransform innerContent);

            float y = -PADDING;

            // Name field
            AddSettingsLabel(inner, "Guild Name", ref y);
            _nameInput = MakeTMPInputField(inner, "Guild name...", SCREEN_W - PADDING * 3, BTN_H);
            SetAnchored(_nameInput.GetComponent<RectTransform>(), PADDING, y, SCREEN_W - PADDING * 2, BTN_H);
            y -= BTN_H + PADDING;

            // Description field
            AddSettingsLabel(inner, "Description", ref y);
            _descInput = MakeTMPInputField(inner, "Guild description...", SCREEN_W - PADDING * 3, BTN_H * 2);
            SetAnchored(_descInput.GetComponent<RectTransform>(), PADDING, y, SCREEN_W - PADDING * 2, BTN_H * 2);
            y -= BTN_H * 2 + PADDING;

            // Open/closed toggle
            AddSettingsLabel(inner, "Open Recruitment (anyone can apply)", ref y);
            var toggleGO = new GameObject("OpenToggle", typeof(RectTransform));
            toggleGO.transform.SetParent(inner, false);
            var tBg = toggleGO.AddComponent<Image>();
            tBg.color = BtnNormal;
            var trt = toggleGO.GetComponent<RectTransform>();
            SetAnchored(trt, PADDING, y, 24f, 24f);

            // Checkmark child - Toggle.graphic; only visible while isOn.
            var checkGO = new GameObject("Checkmark", typeof(RectTransform));
            checkGO.transform.SetParent(toggleGO.transform, false);
            var checkImg = checkGO.AddComponent<Image>();
            checkImg.color = GoldAccent;
            var checkRT = checkGO.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkRT.offsetMin = checkRT.offsetMax = Vector2.zero;

            _openToggle = toggleGO.AddComponent<Toggle>();
            _openToggle.targetGraphic = tBg;
            _openToggle.graphic = checkImg;
            y -= 24f + PADDING;

            // Color sliders
            AddSettingsLabel(inner, "Guild Color", ref y);
            _colorR = AddColorSlider(inner, "R", ref y);
            _colorG = AddColorSlider(inner, "G", ref y);
            _colorB = AddColorSlider(inner, "B", ref y);

            _colorPreview = MakeImage(inner, "ColorPreview", GoldAccent);
            SetAnchored(_colorPreview.GetComponent<RectTransform>(), PADDING, y, 60f, 24f);
            y -= 24f + PADDING * 2;

            void OnColorChanged(float _)
            {
                if (_colorPreview == null) return;
                _colorPreview.color = new Color(_colorR.value, _colorG.value, _colorB.value);
            }
            _colorR.onValueChanged.AddListener(OnColorChanged);
            _colorG.onValueChanged.AddListener(OnColorChanged);
            _colorB.onValueChanged.AddListener(OnColorChanged);

            // Save settings button
            var saveBtn = MakeButton(inner, "Save Changes", BTN_H, BTN_W * 1.5f, BtnNormal, GoldAccent);
            SetAnchored(saveBtn.GetComponent<RectTransform>(), PADDING, y, BTN_W * 1.5f, BTN_H);
            saveBtn.GetComponent<Button>().onClick.AddListener(SaveSettings);
            y -= BTN_H + PADDING;

            // Transfer leadership - moved up into the gap that used to sit
            // between Save and the divider. The divider is dropped so the
            // bottom-row danger buttons (Dissolve / Leave) have headroom
            // inside the settings tab content area.
            var transferBtn = MakeButton(inner, "Transfer Leadership", BTN_H, BTN_W * 1.8f, BtnNormal, TextMuted);
            SetAnchored(transferBtn.GetComponent<RectTransform>(), PADDING, y, BTN_W * 1.8f, BTN_H);
            transferBtn.GetComponent<Button>().onClick.AddListener(OpenTransferLeadershipPicker);
            y -= BTN_H + PADDING;

            // Delete guild (leader only)
            var deleteBtn = MakeButton(inner, "Dissolve Guild", BTN_H, BTN_W * 1.5f, BtnDanger, TextDanger);
            SetAnchored(deleteBtn.GetComponent<RectTransform>(), PADDING, y, BTN_W * 1.5f, BTN_H);
            deleteBtn.GetComponent<Button>().onClick.AddListener(ConfirmDeleteGuild);

            // Leave guild - same row as Dissolve.
            var leaveBtn = MakeButton(inner, "Leave Guild", BTN_H, BTN_W * 1.5f, BtnDanger, TextDanger);
            SetAnchored(leaveBtn.GetComponent<RectTransform>(), PADDING * 2 + BTN_W * 1.5f, y, BTN_W * 1.5f, BTN_H);
            leaveBtn.GetComponent<Button>().onClick.AddListener(ConfirmLeaveGuild);
            y -= BTN_H + PADDING;

            // Now that all children are placed, fix the scroll content height.
            innerContent.sizeDelta = new Vector2(0, -y + PADDING);
        }

        private void RefreshSettings()
        {
            if (_guild == null) return;
            if (_nameInput != null) _nameInput.text = _guild.Name;
            if (_descInput != null) _descInput.text = _guild.Description;
            if (_openToggle != null) _openToggle.isOn = _guild.IsOpen;
            if (_colorR != null) { _colorR.value = _guild.ColorR / 255f; }
            if (_colorG != null) { _colorG.value = _guild.ColorG / 255f; }
            if (_colorB != null) { _colorB.value = _guild.ColorB / 255f; }
            if (_colorPreview != null) _colorPreview.color = _guild.GuildColor;

            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            bool isLeader = _guild.IsLeader(localId);
            bool canEdit  = _guild.CanEditGuild(localId);

            if (_nameInput != null)  _nameInput.interactable = isLeader;
            if (_descInput != null)  _descInput.interactable = canEdit;
            if (_openToggle != null) _openToggle.interactable = canEdit;
            if (_colorR != null)     _colorR.interactable = canEdit;
            if (_colorG != null)     _colorG.interactable = canEdit;
            if (_colorB != null)     _colorB.interactable = canEdit;
        }

        private void SaveSettings()
        {
            if (_guild == null) return;
            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;

            if (_guild.IsLeader(localId) && _nameInput != null &&
                !string.IsNullOrWhiteSpace(_nameInput.text) && _nameInput.text != _guild.Name)
                GuildAPI.RenameGuild(_nameInput.text.Trim());

            if (_guild.CanEditGuild(localId))
            {
                if (_descInput != null && _descInput.text != _guild.Description)
                    GuildAPI.SetDescription(_descInput.text.Trim());
                if (_openToggle != null && _openToggle.isOn != _guild.IsOpen)
                    GuildAPI.SetOpen(_openToggle.isOn);
                if (_colorR != null && _colorG != null && _colorB != null)
                    GuildAPI.SetColor(
                        (byte)Mathf.RoundToInt(_colorR.value * 255),
                        (byte)Mathf.RoundToInt(_colorG.value * 255),
                        (byte)Mathf.RoundToInt(_colorB.value * 255));
            }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Guild settings saved.");
        }

        // -- Tab switching -----------------------------------------------------

        private void SwitchTab(Tab tab)
        {
            _activeTab = tab;

            _rosterTab?.SetActive(tab == Tab.Roster);
            _appsTab?.SetActive(tab == Tab.Applications);
            _chatTab?.SetActive(tab == Tab.Chat);
            _settingsTab?.SetActive(tab == Tab.Settings);

            for (int i = 0; i < 4; i++)
                if (_tabBgs[i] != null) _tabBgs[i].color = (i == (int)tab) ? TabActive : TabInactive;

            RefreshTabContent();
        }

        private void RefreshTabContent()
        {
            switch (_activeTab)
            {
                case Tab.Roster:       RefreshRoster();       break;
                case Tab.Applications: RefreshApplications(); break;
                case Tab.Chat:         /* chat is append-only */ break;
                case Tab.Settings:     RefreshSettings();     break;
            }
        }

        private void RefreshAll()
        {
            if (_guild == null || !_built) return;

            _guildNameTxt.text        = _guild.Name;
            _guildDescTxt.text        = _guild.Description;
            _guildColorSwatch.color   = _guild.GuildColor;
            _memberCountTxt.text      = $"{_guild.Members.Count} member{(_guild.Members.Count == 1 ? "" : "s")}";

            RefreshTabContent();
        }

        // -- Confirmation dialogs ----------------------------------------------

        private void ConfirmKick(long memberId, string name)
        {
            UnifiedPopup.Push(new YesNoPopup("Kick Member", $"Kick {name} from the guild?",
                yesCallback: () => GuildAPI.KickMember(memberId),
                noCallback: () => { },
                localizeText: false));
        }

        private void ConfirmDeleteGuild()
        {
            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            if (!_guild.IsLeader(localId)) return;
            UnifiedPopup.Push(new YesNoPopup("Dissolve Guild",
                $"This will permanently dissolve '{_guild.Name}' for all members. Continue?",
                yesCallback: GuildAPI.DeleteGuild,
                noCallback: () => { },
                localizeText: false));
        }

        private void ConfirmLeaveGuild()
        {
            UnifiedPopup.Push(new YesNoPopup("Leave Guild", $"Leave '{_guild.Name}'?",
                yesCallback: GuildAPI.LeaveGuild,
                noCallback: () => { },
                localizeText: false));
        }

        private void OpenTransferLeadershipPicker()
        {
            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            if (!_guild.IsLeader(localId)) return;

            // Simplified: pick first online non-leader member.
            foreach (var m in _guild.Members)
            {
                if (m.PlayerId == localId) continue;
                if (!GuildAPI.IsPlayerOnline(m.PlayerId)) continue;
                long tid = m.PlayerId; string tname = m.PlayerName;
                UnifiedPopup.Push(new YesNoPopup("Transfer Leadership", $"Transfer leadership to {tname}?",
                    yesCallback: () => GuildAPI.TransferLeadership(tid),
                    noCallback: () => { },
                    localizeText: false));
                return;
            }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                "No online members to transfer to.");
        }

        private void OpenTransferCompanionPicker(long newOwnerId, string newOwnerName)
        {
            // Companion ownership-transfer removed from RPGMaker (decouple) — companion management,
            // including guild ownership transfer, lives in FiresCompanions now.
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                "Companion transfer is handled by the Companions mod.");
        }

        private static Player FindOnlinePlayer(long playerId)
        {
            foreach (var p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerID() == playerId) return p;
            return null;
        }

        // -- Utility builders --------------------------------------------------

        private static string FormatLastSeen(long ticks)
        {
            if (ticks == 0) return "Unknown";
            var ts = DateTime.UtcNow - new DateTime(ticks);
            if (ts.TotalMinutes < 1) return "Just now";
            if (ts.TotalHours < 1)   return $"{(int)ts.TotalMinutes}m ago";
            if (ts.TotalDays < 1)    return $"{(int)ts.TotalHours}h ago";
            return $"{(int)ts.TotalDays}d ago";
        }

        private void AddSettingsLabel(Transform parent, string text, ref float y)
        {
            var lbl = MakeText(parent, text, FONT_SM, TextMuted, FontStyles.Normal);
            SetAnchored(lbl.GetComponent<RectTransform>(), PADDING, y, SCREEN_W - PADDING * 2, 18f);
            y -= 20f;
        }

        private void AddSettingsDivider(Transform parent, ref float y)
        {
            var div = MakeImage(parent, "Divider", BorderColor);
            SetAnchored(div.GetComponent<RectTransform>(), PADDING, y, SCREEN_W - PADDING * 2, 1f);
            y -= PADDING * 2;
        }

        private Slider AddColorSlider(Transform parent, string label, ref float y)
        {
            var lbl = MakeText(parent, label, FONT_SM, TextMuted, FontStyles.Normal);
            SetAnchored(lbl.GetComponent<RectTransform>(), PADDING, y, 20f, 18f);

            var sliderGO = new GameObject($"Slider_{label}", typeof(RectTransform));
            sliderGO.transform.SetParent(parent, false);
            SetAnchored(sliderGO.GetComponent<RectTransform>(), PADDING + 25f, y, SCREEN_W - PADDING * 3 - 25f, 18f);

            // Background - fills the slider rect.
            var bg = MakeImage(sliderGO.transform, "Background", new Color(0.15f, 0.12f, 0.08f));
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

            // Fill area + Fill - Slider expects the fillRect to live inside an
            // area whose anchors stretch the slider width. Without this the
            // Fill image renders at default 100x100 size and overlaps the rest
            // of the panel (this was the giant yellow blob).
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGO.transform, false);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(5, 0);
            faRT.offsetMax = new Vector2(-5, 0);

            var fill = MakeImage(fillArea.transform, "Fill", GoldAccent);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            // Handle - a thin draggable bar.
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(5, 0); haRT.offsetMax = new Vector2(-5, 0);

            var handle = MakeImage(handleArea.transform, "Handle", TextNormal);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(8, 0);

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 0; slider.maxValue = 1;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.targetGraphic = handle;

            y -= 22f;
            return slider;
        }

        private void AddSmallButton(Transform parent, string label, Color bg, Color fg, UnityEngine.Events.UnityAction action)
        {
            var btn = MakeButton(parent, label, 22f, 50f, bg, fg);
            btn.GetComponent<Button>().onClick.AddListener(action);
        }

        // -- Primitive UI helpers ----------------------------------------------

        private static GameObject MakePanel(Transform parent, float w, float h, Color color, float x = 0, float y = 0)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            return go;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            // Image has [RequireComponent(typeof(RectTransform))] so the RT is
            // added automatically - calling AddComponent<RectTransform>() here
            // would just print a redundant warning to the log.
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.color     = color;
            tmp.fontStyle = style;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            UIBuilderHelper.ApplyValheimFont(tmp);
            return tmp;
        }

        private static GameObject MakeButton(Transform parent, string label, float h, float w, Color bgColor, Color textColor)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cs = btn.colors;
            cs.highlightedColor = bgColor * 1.3f;
            cs.pressedColor     = bgColor * 0.8f;
            btn.colors = cs;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);

            var lbl = MakeText(go.transform, label, FONT_SM, textColor, FontStyles.Normal);
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            lbl.alignment = TextAlignmentOptions.Center;
            return go;
        }

        private static GameObject MakeScrollView(Transform parent, out Transform content, float reserveBottom = 0f)
        {
            var scroll = new GameObject("ScrollView", typeof(RectTransform));
            scroll.transform.SetParent(parent, false);
            var rt = scroll.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0, reserveBottom); rt.offsetMax = Vector2.zero;

            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            // Viewport. RectMask2D (scissor-rect based) is dramatically faster
            // than the older Mask component for ScrollRects with many children
            // - Mask adds 2 stencil draw calls per masked element, which with
            // 50 chat lines + roster + apps balloons the UI draw call count
            // and tanks frame rate. RectMask2D adds zero draw calls.
            // We still need a transparent raycast-target Image so the scroll
            // wheel works inside the viewport.
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scroll.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            var vimg = viewport.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<RectMask2D>();

            sr.viewport = vrt;

            // Content with VerticalLayoutGroup + ContentSizeFitter so rows
            // simply stack and the scroll height auto-fits.
            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewport.transform, false);
            var crt = contentGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            // Drive height from each child's ILayoutElement (LayoutElement on
            // roster/apps rows, TMP intrinsic preferredHeight on chat lines).
            vlg.childControlHeight = true;
            vlg.childControlWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = crt;
            content = contentGO.transform;
            return scroll;
        }

        // Variant for the Settings tab - children use absolute SetAnchored
        // positions, so no VerticalLayoutGroup. Caller must set
        // contentRT.sizeDelta after placing children.
        private static Transform MakeManualScrollView(Transform parent, out RectTransform contentRT)
        {
            var scroll = new GameObject("ScrollView", typeof(RectTransform));
            scroll.transform.SetParent(parent, false);
            var rt = scroll.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scroll.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            var vimg = viewport.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<RectMask2D>();
            sr.viewport = vrt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);

            sr.content = contentRT;
            return content.transform;
        }

        private static TMP_InputField MakeTMPInputField(Transform parent, string placeholder, float w, float h)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>(); bg.color = new Color(0.06f, 0.05f, 0.03f, 1f);
            var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(w, h);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var txt = textGO.AddComponent<TextMeshProUGUI>();
            txt.fontSize = FONT_SM; txt.color = TextNormal;
            UIBuilderHelper.ApplyValheimFont(txt);
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(PADDING, 0); trt.offsetMax = new Vector2(-PADDING, 0);

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var ph = phGO.AddComponent<TextMeshProUGUI>();
            ph.text = placeholder; ph.fontSize = FONT_SM;
            ph.color = TextMuted; ph.fontStyle = FontStyles.Italic;
            UIBuilderHelper.ApplyValheimFont(ph);
            var phrt = phGO.GetComponent<RectTransform>();
            phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(PADDING, 0); phrt.offsetMax = new Vector2(-PADDING, 0);

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport  = rt;
            field.textComponent = txt;
            field.placeholder   = ph;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.targetGraphic = bg;
            return field;
        }

        private static void AddBorder(GameObject target, Color color)
        {
            var border = new GameObject("Border");
            border.transform.SetParent(target.transform, false);
            var img = border.AddComponent<Image>(); img.color = color;
            var rt = border.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-1, -1); rt.offsetMax = new Vector2(1, 1);
            border.transform.SetAsFirstSibling();
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void SetAnchored(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
