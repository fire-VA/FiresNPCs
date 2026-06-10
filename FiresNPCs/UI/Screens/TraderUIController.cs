using System;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Items;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Player-facing trader UI — 7-column table layout (Icons / Items / BuyPrice / Qty / SellPrice /
    /// TotalStock / RestockTime) with synchronized vertical scrolling, restored from the original
    /// FiresVAngarde implementation and grafted with KG-Marketplace features (search bar +
    /// x1/x5/x10/x100 global qty modifier).
    ///
    /// Built entirely programmatically — no asset-bundle prefab needed. Panel renders at 800×750 local
    /// coordinates and is upscaled via localScale for visibility on high-DPI displays.
    ///
    /// Trade engine (CanAffordTrade / ExecuteTrade / CountPlayerItems / RemovePlayerItems /
    /// GivePlayerItems / ResolveItemInfo / FindDisplayItem / IsCurrencyStack / FormatTime / FormatCostSummary)
    /// is preserved verbatim from the prior bundle-prefab implementation — same behaviour, new view.
    /// </summary>
    public class TraderUIController : MonoBehaviour
    {
        public static TraderUIController Instance { get; private set; }

        // ── Layout constants (panel is authored at 800×750, scaled up at runtime) ──
        private const float UI_SCALE = 1.8f;
        private const float PANEL_W = 800f;
        private const float PANEL_H = 750f;
        private const float ROW_HEIGHT = 14f;
        private const float ICON_SIZE = 14f;
        private const float ROW_SPACING = 0f;
        private const float FONT_SIZE = 11f;
        private const float FONT_SIZE_SMALL = 7f;
        private const float SCROLL_SENSITIVITY = 30f;

        // Top strip layout (header + toolbar). All Y offsets are RELATIVE to panel center.
        private const float HEADER_Y = 340f;       // ~340 = 750/2 - 35
        private const float TOOLBAR_Y = 320f;
        private const float COLUMN_HEADER_Y = 300f;
        private const float BODY_TOP_Y = -22.93f;
        private const float BODY_HEIGHT = 289.12f;

        // KG modifier values (x1 / x5 / x10 / x100).
        private static readonly int[] ModifierValues = { 1, 5, 10, 100 };

        // Parchment-friendly colours
        private static readonly Color COL_TEXT_DARK    = new Color(0.15f, 0.10f, 0.05f, 1f);
        private static readonly Color COL_TEXT_BROWN   = new Color(0.30f, 0.20f, 0.10f, 1f);
        private static readonly Color COL_BUY_OK       = new Color(0.60f, 0.15f, 0.10f, 1f);
        private static readonly Color COL_BUY_NO       = new Color(0.40f, 0.30f, 0.25f, 1f);
        private static readonly Color COL_SELL         = new Color(0.10f, 0.40f, 0.10f, 1f);
        private static readonly Color COL_UNAVAILABLE  = new Color(0.50f, 0.45f, 0.40f, 0.6f);
        private static readonly Color COL_OWNED        = new Color(0.35f, 0.28f, 0.18f, 0.8f);
        private static readonly Color COL_STOCK        = new Color(0.25f, 0.20f, 0.15f, 1f);
        private static readonly Color COL_STOCK_EMPTY  = new Color(0.50f, 0.18f, 0.12f, 1f);
        private static readonly Color COL_RESTOCK      = new Color(0.35f, 0.28f, 0.18f, 1f);
        private static readonly Color COL_INPUT_TEXT   = new Color(0.12f, 0.08f, 0.04f, 1f);
        private static readonly Color COL_INPUT_BG     = new Color(0.85f, 0.78f, 0.62f, 0.5f);
        private static readonly Color COL_BTN_BG_OK    = new Color(0.72f, 0.65f, 0.48f, 0.45f);
        private static readonly Color COL_BTN_BG_NO    = new Color(0.60f, 0.50f, 0.40f, 0.25f);
        private static readonly Color COL_GOLD_TEXT    = new Color(0.65f, 0.55f, 0.15f, 1f);
        private static readonly Color COL_MOD_ON       = new Color(0.32f, 0.22f, 0.14f, 0.95f);
        private static readonly Color COL_MOD_OFF      = new Color(0.18f, 0.14f, 0.08f, 0.85f);
        private static readonly Color COL_PANEL_BG     = new Color(0.20f, 0.14f, 0.08f, 0.96f);

        // Runtime state
        private GameObject _panelRoot;
        private GameObject _standaloneCanvas;
        private NpcController _currentNpc;
        private TraderProfile _currentProfile;

        // Per-trade row data
        private readonly Dictionary<TradeEntry, int> _tradeAmounts = new Dictionary<TradeEntry, int>();
        private readonly Dictionary<TradeEntry, RowUIRefs> _rowRefs = new Dictionary<TradeEntry, RowUIRefs>();

        private struct RowUIRefs
        {
            public TextMeshProUGUI buyPriceText;
            public Image buyPriceBg;
            public Button buyPriceBtn;
            public TextMeshProUGUI sellPriceText;
            public Image sellPriceBg;
            public Button sellPriceBtn;
            public TextMeshProUGUI ownedText;
            public TMP_InputField qtyInput;
            public bool isBuyTrade;
            public bool isSellTrade;
        }

        // 7 column content roots
        private RectTransform _iconsContent;
        private RectTransform _itemsContent;
        private RectTransform _buyPriceContent;
        private RectTransform _amountContent;
        private RectTransform _sellPriceContent;
        private RectTransform _stockContent;
        private RectTransform _restockContent;

        private readonly List<ScrollRect> _columnScrollRects = new List<ScrollRect>();
        private readonly List<GameObject> _rows = new List<GameObject>();

        // Top strip widgets
        private TextMeshProUGUI _goldAmountText;
        private TextMeshProUGUI _shopNameText;
        private TMP_InputField _searchInput;
        private string _searchLower = "";
        private int _modifier = 1;
        private readonly Image[] _modifierBgs = new Image[4];

        // Confirm dialog
        private GameObject _confirmRoot;
        private TMP_Text _confirmText;
        private Action _pendingTradeAction;

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        #region Lifecycle

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_standaloneCanvas != null) { Destroy(_standaloneCanvas); _standaloneCanvas = null; }
        }

        private void Update()
        {
            if (!IsVisible) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_confirmRoot != null && _confirmRoot.activeSelf) { _confirmRoot.SetActive(false); _pendingTradeAction = null; }
                else Hide();
            }
        }

        #endregion

        #region Show / Hide / Cleanup

        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) return;
            string profileName = npc.traderProfile;
            if (string.IsNullOrEmpty(profileName)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has nothing to trade."); return; }

            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(profileName);
            if (profile == null || profile.Trades.Count == 0)
            { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has no items to trade."); return; }

            EnsureInstance();
            if (Instance == null) return;
            Instance._currentNpc = npc;
            Instance._currentProfile = profile;
            Instance.PopulateUI(npc.GetDisplayName());
            Instance._panelRoot.SetActive(true);
            GUIManager.BlockInput(true);
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }

        public static void ShowForCompanion(string displayName, string traderProfile)
        {
            if (string.IsNullOrEmpty(traderProfile)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{displayName} has nothing to trade."); return; }
            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(traderProfile);
            if (profile == null || profile.Trades.Count == 0)
            { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{displayName} has no items to trade."); return; }

            EnsureInstance();
            if (Instance == null) return;
            Instance._currentNpc = null;
            Instance._currentProfile = profile;
            Instance.PopulateUI(displayName);
            Instance._panelRoot.SetActive(true);
            GUIManager.BlockInput(true);
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }

        public void Hide()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
            NpcHoverHudSuppressor.Clear();
            _currentNpc = null; _currentProfile = null;
            GUIManager.BlockInput(false);
        }

        public static void HideIfVisible() { if (Instance != null && Instance.IsVisible) Instance.Hide(); }

        public static void Cleanup()
        {
            if (Instance != null && Instance._standaloneCanvas != null) UnityEngine.Object.Destroy(Instance._standaloneCanvas);
            Instance = null;
        }

        #endregion

        #region Bootstrap

        private static void EnsureInstance()
        {
            if (Instance != null && Instance._panelRoot != null) return;
            if (Hud.instance == null) return;

            // Standalone top-level canvas so opening/closing doesn't dirty Hud's inventory grid.
            var canvasGo = new GameObject("TraderUI_Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var cr = canvasGo.GetComponent<RectTransform>();
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;

            // Panel root — 800×750 authoring rect, upscaled by UI_SCALE so all child offsets stay in the
            // small-coordinate space the codegen layout values were captured in.
            var panel = new GameObject("TraderUI_Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            prt.localScale = Vector3.one * UI_SCALE;
            var bg = panel.GetComponent<Image>();
            bg.color = COL_PANEL_BG;
            bg.raycastTarget = true;
            panel.AddComponent<TraderDragHandler>();

            var controller = panel.AddComponent<TraderUIController>();
            controller._panelRoot = panel;
            controller._standaloneCanvas = canvasGo;
            controller.BuildColumns();
            controller.BuildColumnHeaderLabels();
            controller.BuildTopStrip();
            controller.BuildCloseButton();
            panel.SetActive(false);
        }

        #endregion

        #region Top Strip (shop name + gold + search + modifier)

        private void BuildTopStrip()
        {
            // Shop name (centered over the panel, top).
            var shopGo = new GameObject("ShopName", typeof(RectTransform));
            shopGo.transform.SetParent(_panelRoot.transform, false);
            var srt = (RectTransform)shopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, HEADER_Y);
            srt.sizeDelta = new Vector2(360f, 22f);
            _shopNameText = shopGo.AddComponent<TextMeshProUGUI>();
            _shopNameText.fontSize = 14f;
            _shopNameText.color = new Color(1f, 0.85f, 0.55f);
            _shopNameText.alignment = TextAlignmentOptions.Center;
            _shopNameText.fontStyle = FontStyles.Bold;
            _shopNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _shopNameText.raycastTarget = false;
            ApplyFont(_shopNameText);

            // Gold display (right side of header).
            BuildGoldDisplay();

            // Search input (left side of toolbar).
            var searchGo = new GameObject("Search", typeof(RectTransform), typeof(Image));
            searchGo.transform.SetParent(_panelRoot.transform, false);
            var srrt = (RectTransform)searchGo.transform;
            srrt.anchorMin = srrt.anchorMax = new Vector2(0.5f, 0.5f);
            srrt.pivot = new Vector2(0.5f, 0.5f);
            srrt.anchoredPosition = new Vector2(-140f, TOOLBAR_Y);
            srrt.sizeDelta = new Vector2(260f, 20f);
            searchGo.GetComponent<Image>().color = COL_INPUT_BG;
            var searchInput = searchGo.AddComponent<TMP_InputField>();

            var searchTextGo = new GameObject("Text", typeof(RectTransform));
            searchTextGo.transform.SetParent(searchGo.transform, false);
            var strt = (RectTransform)searchTextGo.transform;
            Stretch(strt); strt.offsetMin = new Vector2(6f, 0f); strt.offsetMax = new Vector2(-6f, 0f);
            var searchTmp = searchTextGo.AddComponent<TextMeshProUGUI>();
            searchTmp.fontSize = 11f;
            searchTmp.color = COL_INPUT_TEXT;
            searchTmp.alignment = TextAlignmentOptions.MidlineLeft;
            ApplyFont(searchTmp);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(searchGo.transform, false);
            var prt2 = (RectTransform)placeholderGo.transform;
            Stretch(prt2); prt2.offsetMin = new Vector2(6f, 0f); prt2.offsetMax = new Vector2(-6f, 0f);
            var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderTmp.text = "Search…";
            placeholderTmp.fontSize = 11f;
            placeholderTmp.color = new Color(0.4f, 0.32f, 0.20f, 0.7f);
            placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;
            placeholderTmp.raycastTarget = false;
            ApplyFont(placeholderTmp);

            searchInput.textViewport = strt;
            searchInput.textComponent = searchTmp;
            searchInput.placeholder = placeholderTmp;
            searchInput.lineType = TMP_InputField.LineType.SingleLine;
            searchInput.onValueChanged.AddListener(val => { _searchLower = (val ?? "").ToLowerInvariant(); PopulateUIRows(); });
            _searchInput = searchInput;

            // Modifier buttons (x1/x5/x10/x100) on the right side of toolbar.
            for (int i = 0; i < ModifierValues.Length; i++)
            {
                int idx = i;
                int val = ModifierValues[i];
                var btn = BuildModifierButton(val, 120f + i * 56f, TOOLBAR_Y);
                _modifierBgs[i] = btn.GetComponent<Image>();
                btn.onClick.AddListener(() => SetModifier(idx));
            }
            RefreshModifierButtonsVisual();
        }

        private void BuildGoldDisplay()
        {
            var goldGo = new GameObject("GoldDisplay", typeof(RectTransform));
            goldGo.transform.SetParent(_panelRoot.transform, false);
            var grt = (RectTransform)goldGo.transform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(260f, HEADER_Y);
            grt.sizeDelta = new Vector2(140f, 22f);

            var hlg = goldGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false; hlg.childControlHeight = false;

            var iconGo = new GameObject("CoinIcon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            iconGo.transform.SetParent(goldGo.transform, false);
            var iconLE = iconGo.GetComponent<LayoutElement>();
            iconLE.preferredWidth = 14f; iconLE.preferredHeight = 14f;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true; iconImg.raycastTarget = false;
            var coinPrefab = ZNetScene.instance?.GetPrefab("Coins");
            if (coinPrefab != null)
            {
                var coinDrop = coinPrefab.GetComponent<ItemDrop>();
                if (coinDrop?.m_itemData != null) iconImg.sprite = coinDrop.m_itemData.GetIcon();
            }

            var textGo = new GameObject("GoldText", typeof(RectTransform), typeof(LayoutElement));
            textGo.transform.SetParent(goldGo.transform, false);
            var textLE = textGo.GetComponent<LayoutElement>();
            textLE.preferredWidth = 110f; textLE.preferredHeight = 18f;
            _goldAmountText = textGo.AddComponent<TextMeshProUGUI>();
            _goldAmountText.fontSize = 12f;
            _goldAmountText.color = COL_GOLD_TEXT;
            _goldAmountText.alignment = TextAlignmentOptions.MidlineLeft;
            _goldAmountText.fontStyle = FontStyles.Bold;
            _goldAmountText.textWrappingMode = TextWrappingModes.NoWrap;
            _goldAmountText.raycastTarget = false;
            ApplyFont(_goldAmountText);
            _goldAmountText.text = "0";
        }

        private Button BuildModifierButton(int value, float xPos, float yPos)
        {
            var go = new GameObject($"x{value}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(xPos, yPos);
            rt.sizeDelta = new Vector2(48f, 20f);
            go.GetComponent<Image>().color = COL_MOD_OFF;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)labelGo.transform);
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = $"x{value}";
            t.fontSize = 12f;
            t.color = new Color(0.96f, 0.91f, 0.76f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            ApplyFont(t);
            return go.GetComponent<Button>();
        }

        private void SetModifier(int idx)
        {
            if (idx < 0 || idx >= ModifierValues.Length) return;
            _modifier = ModifierValues[idx];
            RefreshModifierButtonsVisual();
            // Push the new modifier into every active row's qty input + re-cost summary.
            if (_currentProfile != null)
            {
                foreach (var trade in _currentProfile.Trades)
                {
                    _tradeAmounts[trade] = _modifier;
                    if (_rowRefs.TryGetValue(trade, out var refs))
                    {
                        if (refs.qtyInput != null && refs.qtyInput.text != _modifier.ToString())
                            refs.qtyInput.SetTextWithoutNotify(_modifier.ToString());
                        OnAmountChanged(trade, _modifier);
                    }
                }
            }
        }

        private void RefreshModifierButtonsVisual()
        {
            for (int i = 0; i < _modifierBgs.Length; i++)
            {
                if (_modifierBgs[i] == null) continue;
                _modifierBgs[i].color = (ModifierValues[i] == _modifier) ? COL_MOD_ON : COL_MOD_OFF;
            }
        }

        private void BuildCloseButton()
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-8f, -8f);
            rt.sizeDelta = new Vector2(28f, 28f);
            go.GetComponent<Image>().color = new Color(0.42f, 0.15f, 0.10f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(Hide);

            var labelGo = new GameObject("X", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)labelGo.transform);
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = "X";
            t.fontSize = 16f;
            t.color = new Color(1f, 0.9f, 0.85f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            ApplyFont(t);
        }

        #endregion

        #region 7 Columns

        // Codegen-derived column positions (800×750 local coordinate space).
        private static readonly (string name, Vector2 pos, Vector2 size)[] ColumnDefs =
        {
            ("Icons",     new Vector2(-216.71f, BODY_TOP_Y), new Vector2( 41.77f, BODY_HEIGHT)),
            ("Items",     new Vector2(-118.44f, BODY_TOP_Y), new Vector2(125f,    BODY_HEIGHT)),
            ("BuyPrice",  new Vector2(  41f,    BODY_TOP_Y), new Vector2( 53f,    BODY_HEIGHT)),
            ("Qty",       new Vector2(  87.22f, BODY_TOP_Y), new Vector2( 40f,    BODY_HEIGHT)),
            ("SellPrice", new Vector2( 134.92f, BODY_TOP_Y), new Vector2( 47.23f, BODY_HEIGHT)),
            ("Stock",     new Vector2( 178.47f, BODY_TOP_Y), new Vector2( 48.5f,  BODY_HEIGHT)),
            ("Restock",   new Vector2( 227.47f, BODY_TOP_Y), new Vector2( 43.73f, BODY_HEIGHT)),
        };

        private void BuildColumns()
        {
            _columnScrollRects.Clear();
            var contents = new RectTransform[ColumnDefs.Length];
            for (int i = 0; i < ColumnDefs.Length; i++)
            {
                var (n, pos, size) = ColumnDefs[i];
                contents[i] = CreateColumnScrollView(_panelRoot.transform, n, pos, size);
            }
            _iconsContent     = contents[0];
            _itemsContent     = contents[1];
            _buyPriceContent  = contents[2];
            _amountContent    = contents[3];
            _sellPriceContent = contents[4];
            _stockContent     = contents[5];
            _restockContent   = contents[6];

            foreach (var sr in _columnScrollRects)
            {
                var captured = sr;
                sr.onValueChanged.AddListener(pos => SyncScroll(captured, pos));
            }
        }

        private void BuildColumnHeaderLabels()
        {
            // Single-line column headers above each scroll column.
            string[] labels = { "Icon", "Item", "Buy", "Qty", "Sell", "Stock", "Restock" };
            for (int i = 0; i < ColumnDefs.Length; i++)
            {
                var def = ColumnDefs[i];
                var go = new GameObject($"H_{def.name}", typeof(RectTransform));
                go.transform.SetParent(_panelRoot.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(def.pos.x, COLUMN_HEADER_Y);
                rt.sizeDelta = new Vector2(def.size.x, 14f);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = labels[i];
                tmp.fontSize = 11f;
                tmp.color = new Color(1f, 0.85f, 0.55f);
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold;
                tmp.raycastTarget = false;
                ApplyFont(tmp);
            }
        }

        private RectTransform CreateColumnScrollView(Transform parent, string colName, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var col = new GameObject(colName, typeof(RectTransform));
            col.transform.SetParent(parent, false);
            var colRect = (RectTransform)col.transform;
            colRect.anchorMin = colRect.anchorMax = new Vector2(0.5f, 0.5f);
            colRect.pivot = new Vector2(0.5f, 0.5f);
            colRect.anchoredPosition = anchoredPos;
            colRect.sizeDelta = sizeDelta;

            var bgImg = col.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0f);
            bgImg.raycastTarget = false;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(col.transform, false);
            var vpRect = (RectTransform)viewport.transform;
            vpRect.anchorMin = Vector2.zero; vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(2, 2); vpRect.offsetMax = new Vector2(-2, -2);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1); cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0, 1);
            cRect.anchoredPosition = Vector2.zero;
            cRect.sizeDelta = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = ROW_SPACING;
            vlg.padding = new RectOffset(1, 1, 3, 1);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childAlignment = TextAnchor.UpperCenter;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = col.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.viewport = vpRect; sr.content = cRect;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = SCROLL_SENSITIVITY;
            sr.inertia = false;

            _columnScrollRects.Add(sr);
            return cRect;
        }

        private bool _syncing;
        private void SyncScroll(ScrollRect source, Vector2 pos)
        {
            if (_syncing) return;
            _syncing = true;
            foreach (var sr in _columnScrollRects)
                if (sr != source) sr.verticalNormalizedPosition = pos.y;
            _syncing = false;
        }

        #endregion

        #region Populate UI

        private void PopulateUI(string shopName)
        {
            _shopNameText.text = shopName ?? _currentProfile?.Name ?? "Shop";
            PopulateUIRows();
            RefreshGoldDisplay();
        }

        /// <summary>Rebuilds every row from <see cref="_currentProfile"/> applying the current
        /// search filter. Called on first show, on search changes, and after trade execution.</summary>
        private void PopulateUIRows()
        {
            ClearRows();
            _rowRefs.Clear();
            if (_currentProfile == null) return;

            foreach (var trade in _currentProfile.Trades)
            {
                if (!PassesSearch(trade)) continue;
                AddTradeRow(trade);
            }
        }

        private bool PassesSearch(TradeEntry trade)
        {
            if (string.IsNullOrEmpty(_searchLower)) return true;
            var displayItem = FindDisplayItem(trade);
            if (displayItem == null) return false;
            ResolveItemInfo(displayItem.PrefabName, out string displayName, out _);
            return displayName != null && displayName.ToLowerInvariant().Contains(_searchLower);
        }

        private void AddTradeRow(TradeEntry trade)
        {
            var primaryReward = trade.RewardItems.FirstOrDefault();
            var primaryCost = trade.CostItems.FirstOrDefault();
            if (primaryReward == null || primaryCost == null) return;

            if (!_tradeAmounts.ContainsKey(trade)) _tradeAmounts[trade] = _modifier;
            int qty = _tradeAmounts[trade];
            bool canAfford = CanAffordTrade(trade, qty);

            bool isBuyTrade = IsCurrencyStack(primaryCost);
            bool isSellTrade = IsCurrencyStack(primaryReward);

            var displayItem = FindDisplayItem(trade);
            ResolveItemInfo(displayItem.PrefabName, out string displayName, out Sprite icon);
            if (displayItem.Level > 0) displayName += $" ★{displayItem.Level - 1}";
            string itemLabel = displayItem.Amount > 1 ? $"{displayItem.Amount} {displayName}" : displayName;
            int ownedCount = CountPlayerItems(displayItem.PrefabName, displayItem.Level);

            AddIconCell(_iconsContent, icon);
            var ownedTmp = AddItemCellWithOwned(_itemsContent, itemLabel, ownedCount);

            (TextMeshProUGUI text, Image bg, Button btn) buyRefs;
            if (isBuyTrade)
            {
                string buyText = FormatCostSummary(trade.CostItems, qty);
                buyRefs = AddButtonCell(_buyPriceContent, buyText, canAfford ? COL_BUY_OK : COL_BUY_NO,
                                        canAfford, () => ShowTradeConfirmation(trade, true));
            }
            else buyRefs = (AddUnavailableCell(_buyPriceContent), null, null);

            var qtyInput = AddAmountInputCell(_amountContent, trade, qty);

            TextMeshProUGUI sellTmp = null; Image sellBg = null; Button sellBtn = null;
            if (isSellTrade)
            {
                string sellText = FormatCostSummary(trade.RewardItems, qty);
                var sellRefs = AddButtonCell(_sellPriceContent, sellText, canAfford ? COL_SELL : COL_BUY_NO,
                                             canAfford, () => ShowTradeConfirmation(trade, false));
                sellTmp = sellRefs.text; sellBg = sellRefs.bg; sellBtn = sellRefs.btn;
            }
            else sellTmp = AddUnavailableCell(_sellPriceContent);

            _rowRefs[trade] = new RowUIRefs
            {
                buyPriceText = buyRefs.text, buyPriceBg = buyRefs.bg, buyPriceBtn = buyRefs.btn,
                sellPriceText = sellTmp, sellPriceBg = sellBg, sellPriceBtn = sellBtn,
                ownedText = ownedTmp, qtyInput = qtyInput,
                isBuyTrade = isBuyTrade, isSellTrade = isSellTrade,
            };

            string stockText = trade.Stock < 0 ? "∞" : trade.Stock.ToString();
            Color stockColor = trade.Stock == 0 ? COL_STOCK_EMPTY : COL_STOCK;
            AddTextCell(_stockContent, stockText, TextAlignmentOptions.Center, stockColor);

            string restockText = trade.Stock < 0 ? "—"
                              : trade.Stock > 0 ? "Available"
                              : trade.RestockSeconds > 0 ? FormatTime(trade.RestockSeconds)
                              : "Sold Out";
            AddTextCell(_restockContent, restockText, TextAlignmentOptions.Center, COL_RESTOCK);
        }

        private void OnAmountChanged(TradeEntry trade, int newAmount)
        {
            newAmount = Mathf.Max(1, newAmount);
            _tradeAmounts[trade] = newAmount;
            if (!_rowRefs.TryGetValue(trade, out var refs)) return;
            bool canAfford = CanAffordTrade(trade, newAmount);

            if (refs.isBuyTrade)
            {
                if (refs.buyPriceText != null)
                {
                    refs.buyPriceText.text = FormatCostSummary(trade.CostItems, newAmount);
                    refs.buyPriceText.color = canAfford ? COL_BUY_OK : COL_BUY_NO;
                }
                if (refs.buyPriceBg != null) refs.buyPriceBg.color = canAfford ? COL_BTN_BG_OK : COL_BTN_BG_NO;
                if (refs.buyPriceBtn != null) refs.buyPriceBtn.interactable = canAfford;
            }
            if (refs.isSellTrade)
            {
                if (refs.sellPriceText != null)
                {
                    refs.sellPriceText.text = FormatCostSummary(trade.RewardItems, newAmount);
                    refs.sellPriceText.color = canAfford ? COL_SELL : COL_BUY_NO;
                }
                if (refs.sellPriceBg != null) refs.sellPriceBg.color = canAfford ? COL_BTN_BG_OK : COL_BTN_BG_NO;
                if (refs.sellPriceBtn != null) refs.sellPriceBtn.interactable = canAfford;
            }
        }

        #endregion

        #region Cell Factories

        private void AddIconCell(RectTransform parent, Sprite sprite)
        {
            if (parent == null) return;
            var row = CreateRow(parent, "IconRow");
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
                iconGo.transform.SetParent(row.transform, false);
                var ir = (RectTransform)iconGo.transform;
                ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
                ir.pivot = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
                iconGo.GetComponent<LayoutElement>().ignoreLayout = true;
                var img = iconGo.GetComponent<Image>();
                img.sprite = sprite; img.preserveAspect = true; img.raycastTarget = false;
            }
        }

        private TextMeshProUGUI AddTextCell(RectTransform parent, string text, TextAlignmentOptions align, Color color)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "TextRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = FONT_SIZE; tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private (TextMeshProUGUI text, Image bg, Button btn) AddButtonCell(RectTransform parent, string text,
                                                                            Color textColor, bool interactable, Action onClick)
        {
            if (parent == null) return (null, null, null);
            var row = CreateRow(parent, "BuyRow");
            var bgImg = row.AddComponent<Image>();
            bgImg.color = interactable ? COL_BTN_BG_OK : COL_BTN_BG_NO;
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(row.transform, false);
            Stretch((RectTransform)txtGo.transform);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = FONT_SIZE; tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);
            return (tmp, bgImg, btn);
        }

        private TextMeshProUGUI AddUnavailableCell(RectTransform parent)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "UnavailRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.text = "unavailable"; tmp.fontSize = FONT_SIZE_SMALL; tmp.color = COL_UNAVAILABLE;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(2, 0, 2, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private TextMeshProUGUI AddItemCellWithOwned(RectTransform parent, string itemLabel, int ownedCount)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "TextRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.text = FormatItemWithOwned(itemLabel, ownedCount);
            tmp.fontSize = FONT_SIZE; tmp.color = COL_TEXT_DARK;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private static string FormatItemWithOwned(string itemLabel, int ownedCount)
        {
            string ownedTag = $"<size=60%><color=#{ColorUtility.ToHtmlStringRGBA(COL_OWNED)}>(owned-{ownedCount})</color></size>";
            return $"{itemLabel} {ownedTag}";
        }

        private TMP_InputField AddAmountInputCell(RectTransform parent, TradeEntry trade, int currentAmount)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "AmountRow");
            var bgImg = row.AddComponent<Image>(); bgImg.color = COL_INPUT_BG;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(row.transform, false);
            var textRT = (RectTransform)textGo.transform;
            Stretch(textRT);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = FONT_SIZE; tmp.color = COL_INPUT_TEXT;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.margin = new Vector4(2, 0, 2, 0);
            ApplyFont(tmp);

            var inputField = row.AddComponent<TMP_InputField>();
            inputField.textComponent = tmp;
            inputField.textViewport = textRT;
            inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputField.characterLimit = 4;
            inputField.text = currentAmount.ToString();

            var captured = trade;
            inputField.onValueChanged.AddListener(val =>
            {
                if (int.TryParse(val, out int parsed) && parsed >= 1) OnAmountChanged(captured, parsed);
            });
            inputField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int parsed) && parsed >= 1) OnAmountChanged(captured, parsed);
                else { inputField.text = "1"; OnAmountChanged(captured, 1); }
            });
            return inputField;
        }

        private GameObject CreateRow(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = ROW_HEIGHT; le.minHeight = ROW_HEIGHT; le.flexibleHeight = 0;
            _rows.Add(go);
            return go;
        }

        private void ClearRows()
        {
            foreach (var row in _rows) if (row != null) Destroy(row);
            _rows.Clear();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void ApplyFont(TMP_Text tmp)
        {
            try { FiresCore.UI.UIBuilderHelper.ApplyValheimFont(tmp); } catch { }
        }

        private void RefreshGoldDisplay()
        {
            if (_goldAmountText == null) return;
            int coins = CountPlayerItems("Coins", 0);
            _goldAmountText.text = coins.ToString("N0");
        }

        private void RefreshOwnedCounts()
        {
            if (_currentProfile == null) return;
            foreach (var trade in _currentProfile.Trades)
            {
                if (!_rowRefs.TryGetValue(trade, out var refs)) continue;
                if (refs.ownedText == null) continue;
                var displayItem = FindDisplayItem(trade);
                ResolveItemInfo(displayItem.PrefabName, out string displayName, out _);
                if (displayItem.Level > 0) displayName += $" ★{displayItem.Level - 1}";
                string itemLabel = displayItem.Amount > 1 ? $"{displayItem.Amount} {displayName}" : displayName;
                int owned = CountPlayerItems(displayItem.PrefabName, displayItem.Level);
                refs.ownedText.text = FormatItemWithOwned(itemLabel, owned);
            }
        }

        #endregion

        #region Trade Logic (verbatim from prior implementation)

        private bool CanAffordTrade(TradeEntry trade, int multiplier)
        {
            if (trade == null || trade.Stock == 0) return false;
            foreach (var cost in trade.CostItems)
                if (CountPlayerItems(cost.PrefabName, cost.Level) < cost.Amount * multiplier) return false;
            return true;
        }

        private void ExecuteTrade(TradeEntry trade)
        {
            int qty = _tradeAmounts.TryGetValue(trade, out int q) ? q : 1;
            if (!CanAffordTrade(trade, qty))
            { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!"); return; }

            var player = Player.m_localPlayer; if (player == null) return;
            var inventory = player.GetInventory();
            foreach (var cost in trade.CostItems)
                if (!RemovePlayerItems(inventory, cost.PrefabName, cost.Amount * qty, cost.Level)) return;
            foreach (var reward in trade.RewardItems)
                GivePlayerItems(player, reward.PrefabName, reward.Amount * qty, reward.Level);

            if (trade.Stock > 0) trade.Stock = Mathf.Max(0, trade.Stock - qty);

            OnAmountChanged(trade, qty);
            RefreshGoldDisplay();
            RefreshOwnedCounts();
        }

        private static int CountPlayerItems(string prefabName, int level = 0)
        {
            var player = Player.m_localPlayer;
            if (player == null) return 0;
            int count = 0;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (!ItemDataHelper.MatchesPrefab(item, prefabName)) continue;
                if (level > 0 && item.m_quality != level) continue;
                count += item.m_stack;
            }
            return count;
        }

        private static bool RemovePlayerItems(Inventory inventory, string prefabName, int amount, int level = 0)
        {
            int remaining = amount;
            foreach (var item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                if (remaining <= 0) break;
                if (!ItemDataHelper.MatchesPrefab(item, prefabName)) continue;
                if (level > 0 && item.m_quality != level) continue;
                int take = Mathf.Min(item.m_stack, remaining);
                inventory.RemoveItem(item, take);
                remaining -= take;
            }
            return remaining <= 0;
        }

        private static void GivePlayerItems(Player player, string prefabName, int amount, int level)
        {
            var prefab = ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab == null) return;
            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                int quality = Mathf.Max(1, level);
                var inv = player.GetInventory();
                for (int i = 0; i < amount; i++)
                {
                    var data = itemDrop.m_itemData.Clone();
                    data.m_quality = quality; data.m_stack = 1;
                    if (!inv.AddItem(data))
                        ItemDrop.DropItem(data, 1, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
                }
                return;
            }
            var character = prefab.GetComponent<Character>();
            if (character != null)
            {
                for (int i = 0; i < amount; i++)
                {
                    var spawnPos = player.transform.position + player.transform.forward * 2f + Vector3.up;
                    var creature = Instantiate(prefab, spawnPos, Quaternion.identity);
                    if (level > 0) creature.GetComponent<Character>()?.SetLevel(level);
                    creature.GetComponent<Tameable>()?.Tame();
                }
            }
        }

        #endregion

        #region Helpers

        private string FormatCostSummary(List<TradeItemStack> items, int multiplier)
        {
            var parts = new List<string>();
            foreach (var item in items)
            {
                int amt = item.Amount * multiplier;
                if (IsCurrencyStack(item)) parts.Add(amt.ToString());
                else
                {
                    ResolveItemInfo(item.PrefabName, out string name, out _);
                    parts.Add(amt > 1 ? $"{amt} {name}" : name);
                }
            }
            return string.Join(" + ", parts);
        }

        private static void ResolveItemInfo(string prefabName, out string displayName, out Sprite icon)
        {
            displayName = prefabName; icon = null;
            var go = ZNetScene.instance?.GetPrefab(prefabName);
            if (go == null) return;
            var drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData == null) return;
            displayName = ItemDataHelper.GetDisplayName(drop.m_itemData);
            icon = drop.m_itemData.GetIcon();
        }

        private static TradeItemStack FindDisplayItem(TradeEntry trade)
        {
            foreach (var item in trade.RewardItems) if (!IsCurrencyStack(item)) return item;
            foreach (var item in trade.CostItems) if (!IsCurrencyStack(item)) return item;
            return trade.RewardItems.FirstOrDefault() ?? trade.CostItems.First();
        }

        private static bool IsCurrencyStack(TradeItemStack stack)
        {
            if (stack == null) return false;
            return string.Equals(stack.PrefabName, "Coins", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatTime(int seconds)
        {
            if (seconds < 60) return $"{seconds}s";
            int minutes = seconds / 60;
            if (minutes < 60) return $"{minutes}m";
            int hours = minutes / 60; int mins = minutes % 60;
            return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }

        #endregion

        #region Confirm Dialog

        private void ShowTradeConfirmation(TradeEntry trade, bool isBuy)
        {
            int qty = _tradeAmounts.TryGetValue(trade, out int q) ? q : 1;
            if (!CanAffordTrade(trade, qty))
            { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!"); return; }

            var displayItem = FindDisplayItem(trade);
            ResolveItemInfo(displayItem.PrefabName, out string itemName, out _);
            int itemAmount = displayItem.Amount * qty;
            var currencyStack = isBuy ? trade.CostItems.FirstOrDefault() : trade.RewardItems.FirstOrDefault();
            int goldAmount = currencyStack != null ? currencyStack.Amount * qty : 0;
            string message = isBuy
                ? $"Buy {itemAmount} {itemName} for {goldAmount} gold?"
                : $"Sell {itemAmount} {itemName} for {goldAmount} gold?";

            EnsureConfirmDialog();
            _confirmText.text = message;
            _pendingTradeAction = () => ExecuteTrade(trade);
            _confirmRoot.SetActive(true);
        }

        private void EnsureConfirmDialog()
        {
            if (_confirmRoot != null) return;
            _confirmRoot = new GameObject("TradeConfirmDialog");
            _confirmRoot.transform.SetParent(_panelRoot.transform, false);
            var rootRect = _confirmRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;

            var dimGo = new GameObject("DimBG");
            dimGo.transform.SetParent(_confirmRoot.transform, false);
            var dimRect = dimGo.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero; dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero; dimRect.offsetMax = Vector2.zero;
            dimGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var boxGo = new GameObject("DialogBox");
            boxGo.transform.SetParent(_confirmRoot.transform, false);
            var boxRect = boxGo.AddComponent<RectTransform>();
            boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(280f, 100f);
            boxGo.AddComponent<Image>().color = new Color(0.12f, 0.10f, 0.08f, 0.95f);

            var textGo = new GameObject("ConfirmText");
            textGo.transform.SetParent(boxGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.05f, 0.4f); textRect.anchorMax = new Vector2(0.95f, 0.95f);
            textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
            _confirmText = textGo.AddComponent<TextMeshProUGUI>();
            _confirmText.fontSize = 11f;
            _confirmText.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            _confirmText.alignment = TextAlignmentOptions.Center;
            _confirmText.textWrappingMode = TextWrappingModes.Normal;
            ApplyFont(_confirmText);

            CreateConfirmButton(boxGo.transform, "Yes", new Vector2(-40f, -35f),
                                new Color(0.15f, 0.45f, 0.15f, 1f),
                                () => { _confirmRoot.SetActive(false); _pendingTradeAction?.Invoke(); _pendingTradeAction = null; });
            CreateConfirmButton(boxGo.transform, "No", new Vector2(40f, -35f),
                                new Color(0.50f, 0.18f, 0.12f, 1f),
                                () => { _confirmRoot.SetActive(false); _pendingTradeAction = null; });
            _confirmRoot.SetActive(false);
        }

        private static GameObject CreateConfirmButton(Transform parent, string label, Vector2 pos, Color bgColor, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(60f, 22f);
            rect.anchoredPosition = pos;
            var img = go.AddComponent<Image>(); img.color = bgColor;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 11f;
            tmp.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            try { FiresCore.UI.UIBuilderHelper.ApplyValheimFont(tmp); } catch { }
            return go;
        }

        #endregion
    }

    /// <summary>Drag handler so the player can reposition the trader panel by clicking and dragging the background.</summary>
    public class TraderDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        private RectTransform _rect;
        private Canvas _canvas;

        private void Awake() { _rect = GetComponent<RectTransform>(); _canvas = GetComponentInParent<Canvas>(); }
        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData)
        {
            if (_rect == null || _canvas == null) return;
            float scale = _canvas.scaleFactor; if (scale <= 0) scale = 1f;
            _rect.anchoredPosition += eventData.delta / scale;
        }
    }
}
