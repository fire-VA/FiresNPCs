using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Modules.Guilds.UI
{
    /// <summary>
    /// Browse and apply to open guilds.
    /// Also provides a "Found a Guild" shortcut for players not yet in one.
    /// </summary>
    public class GuildSearchScreen : MonoBehaviour
    {
        public event Action OnClose;
        public event Action OnCreateGuild;

        // Match the info-UI panel opacity for visual consistency.
        private static readonly Color BgColor    = new Color(0.04f, 0.04f, 0.06f, 0.85f);
        private static readonly Color PanelColor  = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        private static readonly Color BorderColor = new Color(0.35f, 0.27f, 0.14f, 1f);
        private static readonly Color GoldAccent  = new Color(0.83f, 0.64f, 0.31f, 1f);
        private static readonly Color TextNormal  = new Color(0.85f, 0.80f, 0.65f, 1f);
        private static readonly Color TextMuted   = new Color(0.55f, 0.50f, 0.40f, 1f);
        private static readonly Color BtnNormal   = new Color(0.22f, 0.17f, 0.10f, 1f);
        private static readonly Color BtnDanger   = new Color(0.40f, 0.12f, 0.10f, 1f);
        private static readonly Color RowEven     = new Color(0.10f, 0.08f, 0.05f, 0.60f);
        private static readonly Color RowOdd      = new Color(0.14f, 0.11f, 0.08f, 0.60f);

        private const float W        = 620f;
        private const float H        = 480f;
        private const float PAD      = 10f;
        private const float HEADER_H = 50f;
        private const float FOOTER_H = 20f;
        private const float ROW_H    = 64f;
        private const float BTN_H    = 28f;
        private const float BTN_W    = 100f;
        private const float FONT_SM  = 11f;
        private const float FONT_MD  = 13f;
        private const float FONT_LG  = 16f;

        private GameObject     _canvasRoot;
        private GameObject     _root;
        private bool           _built;
        private bool           _isDragging;
        private Vector2        _dragOffset;
        private RectTransform  _headerRT;
        private TMP_InputField _searchInput;
        private Transform      _listContent;
        private GameObject     _emptyState;
        private TextMeshProUGUI _emptyMsg;
        private string         _searchFilter = "";

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Show()
        {
            if (!_built) Build();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            _root.SetActive(true);
            GuildAPI.OnGuildsUpdated += OnGuildsUpdated;
            _searchFilter = "";
            if (_searchInput != null) _searchInput.text = "";
            RefreshList();

            GUIManager.BlockInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Debug.Log($"[GuildSearch] Show() — InputBlocked={GUIManager.IsInputBlocked} " +
                      $"Cursor.visible={Cursor.visible} Cursor.lockState={Cursor.lockState} " +
                      $"EventSystem={(EventSystem.current != null ? EventSystem.current.name : "NULL")}");
        }

        public void Hide()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;
            _isDragging = false;
            GUIManager.BlockInput(false);
        }

        private void OnDestroy() => GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;

        private void OnGuildsUpdated(IReadOnlyList<GuildData> _) => RefreshList();

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
                // Drag only when the click is in the title bar AND not over a
                // selectable (button/input/toggle) so controls keep their clicks.
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

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build()
        {
            // DEDICATED CANVAS — sharing Hud.instance's Canvas caused a Canvas
            // Rebuild Storm (every guild layout change dirtied the entire HUD
            // canvas, tanking FPS). Cursor works because GuildScreenManager is
            // registered in GUIManager.IsCustomPanelOpen() — see GuildMainScreen
            // for the same pattern.
            _canvasRoot = new GameObject("SearchPanelCanvas",
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

            _root = new GameObject("SearchPanel", typeof(RectTransform));
            _root.transform.SetParent(_canvasRoot.transform, false);
            var bg = _root.AddComponent<Image>();
            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = Vector2.zero;

            var bgSprite = CodegenUIWiring.ResolveSpriteByNamePublic("crafting_panel_bkg");
            if (bgSprite != null)
            {
                bg.sprite = bgSprite;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(1f, 1f, 1f, 0.75f);
            }
            else
            {
                bg.color = UIFontConfig.Colors.PanelBackground;
            }

            AddBorder(_root, BorderColor);
            BuildHeader();
            BuildGuildList();
            BuildFooter();

            _built = true;
        }

        private void BuildHeader()
        {
            var header = new GameObject("Header");
            header.transform.SetParent(_root.transform, false);
            var hbg = header.AddComponent<Image>(); hbg.color = PanelColor;
            var hrt = header.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
            hrt.offsetMin = new Vector2(0, -HEADER_H); hrt.offsetMax = Vector2.zero;
            _headerRT = hrt;

            // Title
            var title = AddTxt(header.transform, "Browse Guilds", FONT_LG, TextNormal, FontStyles.Bold);
            UIBuilderHelper.ApplyValheimFont(title);
            Anchor(title.GetComponent<RectTransform>(), PAD, -PAD, W * 0.4f, 24f);

            // Search input
            _searchInput = MakeInput(header.transform, "Search guilds...", W * 0.4f, 26f);
            Anchor(_searchInput.GetComponent<RectTransform>(), W * 0.35f, -(HEADER_H - 26f) / 2f, W * 0.4f, 26f);
            _searchInput.onValueChanged.AddListener(filter => { _searchFilter = filter; RefreshList(); });

            // Create guild button
            var createBtn = AddBtn(header.transform, "+ Create Guild", BTN_W * 1.2f, BTN_H,
                BtnNormal, GoldAccent, W - BTN_W * 1.2f - BTN_H - PAD * 2, -(HEADER_H - BTN_H) / 2f);
            createBtn.onClick.AddListener(() => OnCreateGuild?.Invoke());

            // Close button
            var closeBtn = AddBtn(header.transform, "X", BTN_H, BTN_H,
                BtnDanger, new Color(0.85f, 0.30f, 0.25f, 1f), W - BTN_H - PAD, -(HEADER_H - BTN_H) / 2f);
            closeBtn.onClick.AddListener(() => OnClose?.Invoke());
        }

        private void BuildGuildList()
        {
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(_root.transform, false);
            var srt = scrollGO.AddComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(0, FOOTER_H + PAD); srt.offsetMax = new Vector2(0, -HEADER_H);

            // RectMask2D (scissor-rect) is faster than Mask (stencil buffer)
            // for ScrollRects with many children — Mask adds 2 stencil draw
            // calls per masked element.
            var maskGO = new GameObject("Mask");
            maskGO.transform.SetParent(scrollGO.transform, false);
            var mi = maskGO.AddComponent<Image>(); mi.color = new Color(0, 0, 0, 0.01f);
            maskGO.AddComponent<RectMask2D>();
            var mrt = maskGO.GetComponent<RectTransform>();
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = mrt.offsetMax = Vector2.zero;

            var content = new GameObject("Content");
            content.transform.SetParent(maskGO.transform, false);
            var crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = Vector2.one;
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            crt.pivot = new Vector2(0.5f, 1f);
            _listContent = content.transform;

            var sr = scrollGO.AddComponent<ScrollRect>();
            sr.content = crt; sr.viewport = mrt;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            // Empty state — shown when the list has nothing to display.
            _emptyState = new GameObject("EmptyState");
            _emptyState.transform.SetParent(_root.transform, false);
            var ert = _emptyState.AddComponent<RectTransform>();
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.anchoredPosition = new Vector2(0, 20f);
            ert.sizeDelta = new Vector2(360f, 100f);

            _emptyMsg = AddTxt(_emptyState.transform, "No open guilds yet.", FONT_MD, TextNormal, FontStyles.Bold);
            _emptyMsg.alignment = TextAlignmentOptions.Center;
            Anchor(_emptyMsg.GetComponent<RectTransform>(), 0f, -4f, 360f, 24f);

            var subMsg = AddTxt(_emptyState.transform, "Be the first to found one!", FONT_SM, TextMuted, FontStyles.Italic);
            subMsg.alignment = TextAlignmentOptions.Center;
            Anchor(subMsg.GetComponent<RectTransform>(), 0f, -30f, 360f, 18f);

            var bigCreate = AddBtn(_emptyState.transform, "Create a Guild", 200f, 36f,
                new Color(0.25f, 0.18f, 0.08f, 1f), GoldAccent, 80f, -52f);
            bigCreate.onClick.AddListener(() => OnCreateGuild?.Invoke());
        }

        private void BuildFooter()
        {
            var keyCode = ConfigManager.Instance?.configGuildHotkey?.Value ?? KeyCode.G;
            string keyName = keyCode == KeyCode.None ? "disabled" : keyCode.ToString();
            var hint = AddTxt(_root.transform, $"Tip: Press [{keyName}] to open this screen at any time.",
                FONT_SM, TextMuted, FontStyles.Italic);
            hint.alignment = TextAlignmentOptions.Center;
            var hrt = hint.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0, PAD * 0.5f);
            hrt.sizeDelta = new Vector2(-PAD * 2, FOOTER_H);
        }

        // ── List refresh ──────────────────────────────────────────────────────

        private void RefreshList()
        {
            ClearChildren(_listContent);

            var localId = Player.m_localPlayer?.GetPlayerID() ?? 0;
            bool inGuild = GuildAPI.GetOwnGuild() != null;

            var guilds = GuildAPI.GetAllGuilds();
            int shown = 0;

            foreach (var guild in guilds)
            {
                if (!guild.IsOpen) continue;
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    guild.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    guild.Description.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                BuildGuildRow(guild, localId, inGuild, shown);
                shown++;
            }

            bool isEmpty = shown == 0;
            _emptyState.SetActive(isEmpty);
            if (isEmpty && _emptyMsg != null)
            {
                _emptyMsg.text = string.IsNullOrEmpty(_searchFilter)
                    ? "No open guilds yet."
                    : $"No guilds match \"{_searchFilter}\".";
            }

            if (_listContent is RectTransform lrt)
                lrt.sizeDelta = new Vector2(0, shown * ROW_H);
        }

        private void BuildGuildRow(GuildData guild, long localId, bool inGuild, int index)
        {
            var row = new GameObject("GuildRow");
            row.transform.SetParent(_listContent, false);
            var bg = row.AddComponent<Image>(); bg.color = index % 2 == 0 ? RowEven : RowOdd;
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1);
            rrt.sizeDelta = new Vector2(0, ROW_H);
            rrt.anchoredPosition = new Vector2(0, -index * ROW_H);

            // Color bar on left edge
            var colorBar = new GameObject("ColorBar");
            colorBar.transform.SetParent(row.transform, false);
            var cbi = colorBar.AddComponent<Image>(); cbi.color = guild.GuildColor;
            var cbrt = colorBar.GetComponent<RectTransform>();
            cbrt.anchorMin = new Vector2(0, 0); cbrt.anchorMax = new Vector2(0, 1);
            cbrt.offsetMin = Vector2.zero; cbrt.offsetMax = new Vector2(5f, 0);

            // Guild name
            var nameTxt = AddTxt(row.transform, guild.Name, FONT_MD, TextNormal, FontStyles.Bold);
            Anchor(nameTxt.GetComponent<RectTransform>(), 12f, -8f, W * 0.4f, 20f);

            // Member count
            var memberTxt = AddTxt(row.transform, $"{guild.Members.Count} members", FONT_SM, TextMuted, FontStyles.Normal);
            Anchor(memberTxt.GetComponent<RectTransform>(), 12f, -28f, W * 0.3f, 16f);

            // Description
            var descTxt = AddTxt(row.transform, guild.Description, FONT_SM, TextMuted, FontStyles.Normal);
            descTxt.textWrappingMode = TextWrappingModes.Normal;
            Anchor(descTxt.GetComponent<RectTransform>(), W * 0.4f + 8f, -8f, W * 0.35f, ROW_H - PAD * 2);

            // Apply / Applied label
            bool hasApplied = guild.HasApplied(localId);
            if (!inGuild && !guild.IsMember(localId))
            {
                if (hasApplied)
                {
                    var applyLbl = AddTxt(row.transform, "Applied ✓", FONT_SM, GoldAccent, FontStyles.Normal);
                    Anchor(applyLbl.GetComponent<RectTransform>(), W - BTN_W - PAD * 2, -(ROW_H - BTN_H) / 2f, BTN_W, BTN_H);
                    applyLbl.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    var applyBtn = AddBtn(row.transform, "Apply", BTN_W, BTN_H, BtnNormal, GoldAccent,
                        W - BTN_W - PAD * 2, -(ROW_H - BTN_H) / 2f);
                    string guildId = guild.GuildId;
                    applyBtn.onClick.AddListener(() =>
                    {
                        GuildAPI.ApplyToGuild(guildId, "");
                        RefreshList();
                    });
                }
            }
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private static TextMeshProUGUI AddTxt(Transform parent, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject("Txt"); go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            UIBuilderHelper.ApplyValheimFont(tmp);
            go.AddComponent<RectTransform>();
            return tmp;
        }

        private static TMP_InputField MakeInput(Transform parent, string ph, float w, float h)
        {
            var go = new GameObject("Input"); go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>(); bg.color = new Color(0.06f, 0.05f, 0.03f, 1f);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);

            var txtGO = new GameObject("Txt"); txtGO.transform.SetParent(go.transform, false);
            var txt = txtGO.AddComponent<TextMeshProUGUI>(); txt.fontSize = 12f;
            txt.color = new Color(0.85f, 0.80f, 0.65f, 1f);
            UIBuilderHelper.ApplyValheimFont(txt);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0); trt.offsetMax = new Vector2(-6f, 0);

            var phGO = new GameObject("PH"); phGO.transform.SetParent(go.transform, false);
            var phTxt = phGO.AddComponent<TextMeshProUGUI>();
            phTxt.text = ph; phTxt.fontSize = 12f;
            phTxt.color = new Color(0.45f, 0.40f, 0.35f, 1f); phTxt.fontStyle = FontStyles.Italic;
            UIBuilderHelper.ApplyValheimFont(phTxt);
            var phrt = phTxt.GetComponent<RectTransform>();
            phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(6f, 0); phrt.offsetMax = new Vector2(-6f, 0);

            var f = go.AddComponent<TMP_InputField>();
            f.textComponent = txt; f.placeholder = phTxt;
            f.textViewport = go.GetComponent<RectTransform>();
            f.targetGraphic = bg;
            return f;
        }

        private static Button AddBtn(Transform parent, string label, float w, float h,
            Color bgColor, Color textColor, float x, float y)
        {
            var go = new GameObject("Btn"); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            Anchor(go.GetComponent<RectTransform>(), x, y, w, h);

            var lbl = new GameObject("Lbl"); lbl.transform.SetParent(go.transform, false);
            var txt = lbl.AddComponent<TextMeshProUGUI>();
            txt.text = label; txt.fontSize = 12f; txt.color = textColor;
            txt.alignment = TextAlignmentOptions.Center;
            UIBuilderHelper.ApplyValheimFont(txt);
            var lrt = txt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            return btn;
        }

        private static void AddBorder(GameObject target, Color color)
        {
            var b = new GameObject("Border"); b.transform.SetParent(target.transform, false);
            var img = b.AddComponent<Image>(); img.color = color;
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-1, -1); rt.offsetMax = new Vector2(1, 1);
            b.transform.SetAsFirstSibling();
        }

        private static void Anchor(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
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
