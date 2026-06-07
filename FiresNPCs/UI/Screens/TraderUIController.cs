using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresCore.Items;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Controls the Trader UI screen.  Builds the entire UI hierarchy in code using
    /// exact values captured from the TraderUIFixed codegen layout.  No asset-bundle
    /// prefab is needed — the background sprite is resolved at runtime.
    ///
    /// Layout approach:
    ///   • A root 800×750 panel is created with localScale = 1.8 so it renders at
    ///     ~1440×1350 screen pixels while keeping all child coordinates in the
    ///     800×750 local space.
    ///   • Seven child ScrollView columns (Icons, Items, BuyPrice, AmountSoldBought,
    ///     SellPrice, TotalStock, RestockTime) are positioned with exact rects
    ///     derived from the codegen capture (÷1.8).
    ///   • Each column has Viewport → Content(VLG + CSF) → dynamic rows.
    ///   • Every row uses the same fixed height so all columns stay aligned.
    ///   • All ScrollRects are synchronised so scrolling one scrolls them all.
    /// </summary>
    public class TraderUIController : MonoBehaviour
    {
        public static TraderUIController Instance { get; private set; }

        // --- Layout constants ---
        // Base panel is authored at 800×750 then scaled up by UI_SCALE via localScale.
        // All child positions, sizes and font metrics are in the 800×750 coordinate space.
        private const float UI_SCALE = 1.8f;
        private const float PANEL_W = 800f;
        private const float PANEL_H = 750f;
        private const float ROW_HEIGHT = 14f;
        private const float ICON_SIZE = 14f;
        private const float ROW_SPACING = 0f;
        private const float FONT_SIZE = 11f;
        private const float SCROLL_SENSITIVITY = 30f;

        // Parchment-friendly colours
        private static readonly Color COL_TEXT_DARK    = new Color(0.15f, 0.10f, 0.05f, 1f);   // near-black brown
        private static readonly Color COL_TEXT_BROWN   = new Color(0.30f, 0.20f, 0.10f, 1f);   // dark brown
        private static readonly Color COL_BUY_OK       = new Color(0.60f, 0.15f, 0.10f, 1f);   // red (spending gold)
        private static readonly Color COL_BUY_NO       = new Color(0.40f, 0.30f, 0.25f, 1f);   // greyed-out brown
        private static readonly Color COL_SELL         = new Color(0.10f, 0.40f, 0.10f, 1f);   // green (earning gold)
        private static readonly Color COL_UNAVAILABLE  = new Color(0.50f, 0.45f, 0.40f, 0.6f); // greyed-out for unavailable
        private static readonly Color COL_OWNED        = new Color(0.35f, 0.28f, 0.18f, 0.8f); // muted brown for owned count
        private static readonly Color COL_STOCK        = new Color(0.25f, 0.20f, 0.15f, 1f);   // warm brown
        private static readonly Color COL_STOCK_EMPTY  = new Color(0.50f, 0.18f, 0.12f, 1f);   // muted red-brown
        private static readonly Color COL_RESTOCK      = new Color(0.35f, 0.28f, 0.18f, 1f);   // tan brown
        private static readonly Color COL_INPUT_TEXT   = new Color(0.12f, 0.08f, 0.04f, 1f);   // dark ink
        private static readonly Color COL_INPUT_BG     = new Color(0.85f, 0.78f, 0.62f, 0.5f); // light parchment tint
        private static readonly Color COL_BTN_BG_OK    = new Color(0.72f, 0.65f, 0.48f, 0.45f);
        private static readonly Color COL_BTN_BG_NO    = new Color(0.60f, 0.50f, 0.40f, 0.25f);
        private static readonly Color COL_GOLD_TEXT    = new Color(0.65f, 0.55f, 0.15f, 1f);   // gold/amber for coin display
        private const float FONT_SIZE_SMALL = 7f;  // small font for unavailable text and owned count

        private GameObject _panelRoot;
        private GameObject _standaloneCanvas;
        private NpcController _currentNpc;
        private TraderProfile _currentProfile;

        // Per-row state
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
            public bool isBuyTrade;
            public bool isSellTrade;
        }

        // Column content transforms (children of each scroll rect's Content GO)
        private RectTransform _iconsContent;
        private RectTransform _itemsContent;
        private RectTransform _buyPriceContent;
        private RectTransform _amountContent;
        private RectTransform _sellPriceContent;
        private RectTransform _stockContent;
        private RectTransform _restockContent;

        private readonly List<ScrollRect> _columnScrollRects = new List<ScrollRect>();
        private readonly List<GameObject> _rows = new List<GameObject>();

        // Gold display at top of right panel
        private TextMeshProUGUI _goldAmountText;
        private GameObject _goldDisplayRoot;

        // Shop name header above the items column
        private TextMeshProUGUI _shopNameText;

        // Confirmation dialog
        private GameObject _confirmRoot;
        private TMP_Text _confirmText;
        private Button _confirmYesBtn;
        private Button _confirmNoBtn;
        private Action _pendingTradeAction;

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        #region Lifecycle

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Destroy the standalone canvas we own (it's a top-level GO, so Unity
            // doesn't auto-clean it up when the panel itself is destroyed by a sibling).
            if (_standaloneCanvas != null)
            {
                Destroy(_standaloneCanvas);
                _standaloneCanvas = null;
            }
        }

        private void Update()
        {
            if (!IsVisible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        #endregion

        #region Show / Hide

        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) return;

            string profileName = npc.traderProfile;
            if (string.IsNullOrEmpty(profileName))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{npc.GetDisplayName()} has nothing to trade.");
                return;
            }

            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(profileName);
            if (profile == null || profile.Trades.Count == 0)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{npc.GetDisplayName()} has no items to trade.");
                return;
            }

            EnsureInstance();
            if (Instance == null) return;

            Instance._currentNpc = npc;
            Instance._currentProfile = profile;
            Instance.PopulateUI();
            Instance._panelRoot.SetActive(true);
            GUIManager.BlockInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public static void ShowForCompanion(string displayName, string traderProfile)
        {
            if (string.IsNullOrEmpty(traderProfile))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{displayName} has nothing to trade.");
                return;
            }

            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(traderProfile);
            if (profile == null || profile.Trades.Count == 0)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{displayName} has no items to trade.");
                return;
            }

            EnsureInstance();
            if (Instance == null) return;

            Instance._currentNpc = null;
            Instance._currentProfile = profile;
            Instance.PopulateUI();
            Instance._panelRoot.SetActive(true);
            GUIManager.BlockInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Hide()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
            _currentNpc = null;
            _currentProfile = null;
            GUIManager.BlockInput(false);
        }

        public static void HideIfVisible()
        {
            if (Instance != null && Instance.IsVisible) Instance.Hide();
        }

        #endregion

        #region Instance Creation

        private static void EnsureInstance()
        {
            if (Instance != null && Instance._panelRoot != null) return;

            if (Hud.instance == null) return;

            // Build a standalone, top-level Canvas (matches the help panel pattern).
            // Parenting trader UI under Hud's canvas dirties the inventory grid every
            // show/hide and tanks fps. The canvas is unparented so it's a true render
            // root; we destroy it explicitly in OnDestroy.
            var canvasGo = new GameObject("TraderUI_Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above Valheim's IngameGui sibling canvases (HudMessage etc.) so MessageHud
            // toasts don't render over the trader panel. Stays below the help panel (300).
            canvas.sortingOrder = 210;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            // Build the root panel entirely in code — no asset-bundle prefab needed.
            var go = new GameObject("TraderUI_Instance", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            go.transform.localScale = new Vector3(UI_SCALE, UI_SCALE, 1f);

            // Background image
            var bgImg = go.AddComponent<Image>();
            bgImg.color = Color.white;
            bgImg.raycastTarget = true;
            bgImg.type = Image.Type.Simple;
            var bgSprite = VAMiscAssetManager.GetSprite("va_background5");
            if (bgSprite != null) bgImg.sprite = bgSprite;

            // Drag support
            go.AddComponent<TraderDragHandler>();

            var controller = go.AddComponent<TraderUIController>();
            controller._panelRoot = go;
            controller._standaloneCanvas = canvasGo;
            controller.BuildColumns();
            controller.BuildGoldDisplay();
            controller.BuildShopNameHeader();
            go.SetActive(false);
        }

        /// <summary>
        /// Destroys the trader UI's standalone canvas and panel. Called from
        /// UIInitializer.ResetInitialization on logout/scene change.
        /// </summary>
        public static void Cleanup()
        {
            if (Instance != null && Instance._standaloneCanvas != null)
            {
                UnityEngine.Object.Destroy(Instance._standaloneCanvas);
            }
            Instance = null;
        }

        #endregion

        #region Column Building

        /// <summary>
        /// Column definitions — positions and sizes in the 800×750 local coordinate space.
        /// These are the codegen 1440×1350 values divided by UI_SCALE (1.8) so they are
        /// correct for the 800×750 sizeDelta panel that gets visually scaled up via localScale.
        /// </summary>
        private static readonly (string name, Vector2 pos, Vector2 size)[] ColumnDefs =
        {
            ("Icons",            new Vector2(-216.71f, -22.93f), new Vector2( 41.77f, 289.12f)),
            ("Items",            new Vector2(-118.44f, -22.93f), new Vector2(125f,    289.12f)),
            ("BuyPrice",         new Vector2(  41f,    -22.93f), new Vector2( 53f,    289.12f)),
            ("AmountSoldBought", new Vector2(  87.22f, -22.93f), new Vector2( 40f,    289.12f)),
            ("SellPrice",        new Vector2( 134.92f, -22.93f), new Vector2( 47.23f, 289.12f)),
            ("TotalStock",       new Vector2( 178.47f, -22.93f), new Vector2( 48.5f,  289.12f)),
            ("RestockTime",      new Vector2( 227.47f, -22.93f), new Vector2( 43.73f, 289.12f)),
        };

        private void BuildColumns()
        {
            _columnScrollRects.Clear();

            var contents = new RectTransform[ColumnDefs.Length];
            for (int i = 0; i < ColumnDefs.Length; i++)
            {
                var (colName, pos, size) = ColumnDefs[i];
                contents[i] = CreateColumnScrollView(transform, colName, pos, size);
            }

            _iconsContent    = contents[0];
            _itemsContent    = contents[1];
            _buyPriceContent = contents[2];
            _amountContent   = contents[3];
            _sellPriceContent = contents[4];
            _stockContent    = contents[5];
            _restockContent  = contents[6];

            foreach (var sr in _columnScrollRects)
            {
                var captured = sr;
                sr.onValueChanged.AddListener(pos => SyncScroll(captured, pos));
            }
        }

        /// <summary>
        /// Creates a complete scroll-column from scratch:
        /// column GO (Image bg, ScrollRect) → Viewport (RectMask2D) → Content (VLG + CSF).
        /// Returns the Content RectTransform to parent rows into.
        /// </summary>
        private RectTransform CreateColumnScrollView(Transform parent, string colName,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            // Column root
            var col = new GameObject(colName, typeof(RectTransform));
            col.transform.SetParent(parent, false);
            var colRect = (RectTransform)col.transform;
            colRect.anchorMin = new Vector2(0.5f, 0.5f);
            colRect.anchorMax = new Vector2(0.5f, 0.5f);
            colRect.pivot = new Vector2(0.5f, 0.5f);
            colRect.anchoredPosition = anchoredPos;
            colRect.sizeDelta = sizeDelta;

            // Transparent background image (needed for ScrollRect raycast)
            var bgImg = col.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0f);
            bgImg.raycastTarget = false;

            // Viewport — fills column, clips children
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(col.transform, false);
            var vpRect = (RectTransform)viewport.transform;
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(2, 2);
            vpRect.offsetMax = new Vector2(-2, -2);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            vpImg.raycastTarget = true;

            // Content — top-anchored, grows downward via VLG
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = new Vector2(1, 1);
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

            // ScrollRect on the column root
            var sr = col.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.viewport = vpRect;
            sr.content = cRect;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = SCROLL_SENSITIVITY;
            sr.inertia = false;

            _columnScrollRects.Add(sr);
            return cRect;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private bool _syncing;
        private void SyncScroll(ScrollRect source, Vector2 pos)
        {
            if (_syncing) return;
            _syncing = true;
            foreach (var sr in _columnScrollRects)
            {
                if (sr != source)
                    sr.verticalNormalizedPosition = pos.y;
            }
            _syncing = false;
        }

        #endregion

        #region Populate UI

        private void PopulateUI()
        {
            ClearRows();
            _tradeAmounts.Clear();
            _rowRefs.Clear();
            if (_currentProfile == null) return;

            foreach (var trade in _currentProfile.Trades)
                AddTradeRow(trade);

            RefreshGoldDisplay();
            if (_shopNameText != null)
                _shopNameText.text = _currentProfile.Name ?? "Shop";
        }

        private void AddTradeRow(TradeEntry trade)
        {
            var primaryReward = trade.RewardItems.FirstOrDefault();
            var primaryCost = trade.CostItems.FirstOrDefault();
            if (primaryReward == null || primaryCost == null) return;

            if (!_tradeAmounts.ContainsKey(trade))
                _tradeAmounts[trade] = 1;
            int qty = _tradeAmounts[trade];
            bool canAfford = CanAffordTrade(trade, qty);

            // Determine trade direction:
            //   BUY  = CostItems is currency (Coins), RewardItems is the item
            //   SELL = CostItems is the item, RewardItems is currency (Coins)
            bool isBuyTrade = IsCurrencyStack(primaryCost);
            bool isSellTrade = IsCurrencyStack(primaryReward);

            // Display item = the non-currency side
            var displayItem = FindDisplayItem(trade);
            ResolveItemInfo(displayItem.PrefabName, out string displayName, out Sprite icon);
            if (displayItem.Level > 0) displayName += $" ★{displayItem.Level - 1}";

            // Show the base item quantity in the item name when > 1
            string itemLabel = displayItem.Amount > 1 ? $"{displayItem.Amount} {displayName}" : displayName;

            // Count how many of this item the player owns
            int ownedCount = CountPlayerItems(displayItem.PrefabName, displayItem.Level);

            // --- Icons column ---
            AddIconCell(_iconsContent, icon);

            // --- Items column (with owned count) ---
            var ownedTmp = AddItemCellWithOwned(_itemsContent, itemLabel, ownedCount);

            // --- Buy column (clickable) ---
            // Shows the price to BUY this item (only for buy trades where player pays currency)
            // Buy column lights up RED when available
            string buyText;
            Color buyColor;
            bool buyInteractable;
            (TextMeshProUGUI text, Image bg, Button btn) buyRefs;
            if (isBuyTrade)
            {
                buyText = FormatCostSummary(trade.CostItems, qty);
                buyColor = canAfford ? COL_BUY_OK : COL_BUY_NO;
                buyInteractable = canAfford;
                buyRefs = AddButtonCell(_buyPriceContent, buyText, buyColor, buyInteractable, () => ShowTradeConfirmation(trade, true));
            }
            else
            {
                // Not a buy trade — show "unavailable" in small greyed-out text
                buyRefs = (AddUnavailableCell(_buyPriceContent), null, null);
            }

            // --- Amount column (input = trade multiplier) ---
            AddAmountInputCell(_amountContent, trade, qty);

            // --- Sell column ---
            // Sell column lights up GREEN when available
            TextMeshProUGUI sellTmp = null;
            Image sellBg = null;
            Button sellBtn = null;
            if (isSellTrade)
            {
                string sellText = FormatCostSummary(trade.RewardItems, qty);
                var sellRefs = AddButtonCell(_sellPriceContent, sellText, canAfford ? COL_SELL : COL_BUY_NO,
                    canAfford, () => ShowTradeConfirmation(trade, false));
                sellTmp = sellRefs.text;
                sellBg = sellRefs.bg;
                sellBtn = sellRefs.btn;
            }
            else
            {
                // Not a sell trade — show "unavailable" in small greyed-out text
                sellTmp = AddUnavailableCell(_sellPriceContent);
            }

            _rowRefs[trade] = new RowUIRefs
            {
                buyPriceText = buyRefs.text,
                buyPriceBg = buyRefs.bg,
                buyPriceBtn = buyRefs.btn,
                sellPriceText = sellTmp,
                sellPriceBg = sellBg,
                sellPriceBtn = sellBtn,
                ownedText = ownedTmp,
                isBuyTrade = isBuyTrade,
                isSellTrade = isSellTrade
            };

            // --- Stock column ---
            string stockText = trade.Stock < 0 ? "∞" : trade.Stock.ToString();
            Color stockColor = trade.Stock == 0 ? COL_STOCK_EMPTY : COL_STOCK;
            AddTextCell(_stockContent, stockText, TextAlignmentOptions.Center, stockColor);

            // --- Restock column ---
            string restockText;
            if (trade.Stock < 0) restockText = "—";
            else if (trade.Stock > 0) restockText = "Available";
            else if (trade.RestockSeconds > 0) restockText = FormatTime(trade.RestockSeconds);
            else restockText = "Sold Out";
            AddTextCell(_restockContent, restockText, TextAlignmentOptions.Center, COL_RESTOCK);
        }

        private void OnAmountChanged(TradeEntry trade, int newAmount)
        {
            newAmount = Mathf.Max(1, newAmount);
            _tradeAmounts[trade] = newAmount;
            if (!_rowRefs.TryGetValue(trade, out var refs)) return;

            bool canAfford = CanAffordTrade(trade, newAmount);

            // Update buy column for buy trades
            if (refs.isBuyTrade)
            {
                if (refs.buyPriceText != null)
                {
                    refs.buyPriceText.text = FormatCostSummary(trade.CostItems, newAmount);
                    refs.buyPriceText.color = canAfford ? COL_BUY_OK : COL_BUY_NO;
                }
                if (refs.buyPriceBg != null)
                    refs.buyPriceBg.color = canAfford ? COL_BTN_BG_OK : COL_BTN_BG_NO;
                if (refs.buyPriceBtn != null)
                    refs.buyPriceBtn.interactable = canAfford;
            }

            // Update sell column for sell trades
            if (refs.isSellTrade)
            {
                if (refs.sellPriceText != null)
                    refs.sellPriceText.text = FormatCostSummary(trade.RewardItems, newAmount);
                if (refs.sellPriceBg != null)
                    refs.sellPriceBg.color = canAfford ? COL_BTN_BG_OK : COL_BTN_BG_NO;
                if (refs.sellPriceBtn != null)
                    refs.sellPriceBtn.interactable = canAfford;
                if (refs.sellPriceText != null)
                    refs.sellPriceText.color = canAfford ? COL_SELL : COL_BUY_NO;
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
                ir.anchorMin = new Vector2(0.5f, 0.5f);
                ir.anchorMax = new Vector2(0.5f, 0.5f);
                ir.pivot = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
                var iconLE = iconGo.GetComponent<LayoutElement>();
                iconLE.ignoreLayout = true;
                var img = iconGo.GetComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
        }

        private TextMeshProUGUI AddTextCell(RectTransform parent, string text, TextAlignmentOptions align, Color color)
        {
            if (parent == null) return null;

            var row = CreateRow(parent, "TextRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = FONT_SIZE;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private (TextMeshProUGUI text, Image bg, Button btn) AddButtonCell(
            RectTransform parent, string text, Color textColor, bool interactable, Action onClick)
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
            tmp.text = text;
            tmp.fontSize = FONT_SIZE;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);

            return (tmp, bgImg, btn);
        }

        /// <summary>
        /// Adds a small "unavailable" cell for items that cannot be bought or sold.
        /// </summary>
        private TextMeshProUGUI AddUnavailableCell(RectTransform parent)
        {
            if (parent == null) return null;

            var row = CreateRow(parent, "UnavailRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.text = "unavailable";
            tmp.fontSize = FONT_SIZE_SMALL;
            tmp.color = COL_UNAVAILABLE;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.margin = new Vector4(2, 0, 2, 0);
            ApplyFont(tmp);
            return tmp;
        }

        /// <summary>
        /// Adds an item name cell with a smaller "(owned-X)" suffix.
        /// Uses TMP rich text to render the owned count at a smaller size.
        /// </summary>
        private TextMeshProUGUI AddItemCellWithOwned(RectTransform parent, string itemLabel, int ownedCount)
        {
            if (parent == null) return null;

            var row = CreateRow(parent, "TextRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.text = FormatItemWithOwned(itemLabel, ownedCount);
            tmp.fontSize = FONT_SIZE;
            tmp.color = COL_TEXT_DARK;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.margin = new Vector4(4, 0, 4, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private static string FormatItemWithOwned(string itemLabel, int ownedCount)
        {
            string ownedTag = $"<size=60%><color=#{ColorUtility.ToHtmlStringRGBA(COL_OWNED)}>(owned-{ownedCount})</color></size>";
            return $"{itemLabel} {ownedTag}";
        }

        private void AddAmountInputCell(RectTransform parent, TradeEntry trade, int currentAmount)
        {
            if (parent == null) return;

            var row = CreateRow(parent, "AmountRow");
            var rowRT = (RectTransform)row.transform;

            var bgImg = row.AddComponent<Image>();
            bgImg.color = COL_INPUT_BG;

            // Text area — fills the row, acts as both viewport and text host
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(row.transform, false);
            var textRT = (RectTransform)textGo.transform;
            Stretch(textRT);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = FONT_SIZE;
            tmp.color = COL_INPUT_TEXT;
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
            // Real-time price updates as the user types
            inputField.onValueChanged.AddListener(val =>
            {
                if (int.TryParse(val, out int parsed) && parsed >= 1)
                    OnAmountChanged(captured, parsed);
            });
            // Clamp on finish editing
            inputField.onEndEdit.AddListener(val =>
            {
                if (int.TryParse(val, out int parsed) && parsed >= 1)
                    OnAmountChanged(captured, parsed);
                else
                {
                    inputField.text = "1";
                    OnAmountChanged(captured, 1);
                }
            });
        }

        /// <summary>
        /// Creates a row GameObject with a fixed-height LayoutElement, parented to the column content.
        /// This is the ONLY place LayoutElement is added — cell factories must not add another.
        /// </summary>
        private GameObject CreateRow(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = ROW_HEIGHT;
            le.minHeight = ROW_HEIGHT;
            le.flexibleHeight = 0;
            _rows.Add(go);
            return go;
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
                if (row != null) Destroy(row);
            _rows.Clear();
        }

        private static void ApplyFont(TMP_Text tmp)
        {
            var font = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaSerif);
            if (font != null) tmp.font = font;
        }

        /// <summary>
        /// Builds the gold/coin display above the sell column header on the right panel.
        /// Shows a coin icon and the total coins from inventory.
        /// Positioned ~33px above the top of the sell column (60px at 1.8× scale ≈ 33 local).
        /// </summary>
        private void BuildGoldDisplay()
        {
            // Position above the sell column area, centered on the right-page columns
            _goldDisplayRoot = new GameObject("GoldDisplay", typeof(RectTransform));
            _goldDisplayRoot.transform.SetParent(transform, false);
            var rt = (RectTransform)_goldDisplayRoot.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Center horizontally over the right page columns, ~33 local units above the column top
            rt.anchoredPosition = new Vector2(134.92f, -22.93f + 289.12f / 2f + 33f);
            rt.sizeDelta = new Vector2(120f, 18f);

            // Horizontal layout: icon + text
            var hlg = _goldDisplayRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;

            // Coin icon
            var iconGo = new GameObject("CoinIcon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            iconGo.transform.SetParent(_goldDisplayRoot.transform, false);
            var iconRT = (RectTransform)iconGo.transform;
            iconRT.sizeDelta = new Vector2(12f, 12f);
            var iconLE = iconGo.GetComponent<LayoutElement>();
            iconLE.preferredWidth = 12f;
            iconLE.preferredHeight = 12f;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            // Resolve coin icon from game prefabs
            var coinPrefab = ZNetScene.instance?.GetPrefab("Coins");
            if (coinPrefab != null)
            {
                var coinDrop = coinPrefab.GetComponent<ItemDrop>();
                if (coinDrop?.m_itemData != null)
                    iconImg.sprite = coinDrop.m_itemData.GetIcon();
            }

            // Gold amount text
            var textGo = new GameObject("GoldText", typeof(RectTransform), typeof(LayoutElement));
            textGo.transform.SetParent(_goldDisplayRoot.transform, false);
            var textLE = textGo.GetComponent<LayoutElement>();
            textLE.preferredWidth = 90f;
            textLE.preferredHeight = 16f;
            _goldAmountText = textGo.AddComponent<TextMeshProUGUI>();
            _goldAmountText.fontSize = 10f;
            _goldAmountText.color = COL_GOLD_TEXT;
            _goldAmountText.alignment = TextAlignmentOptions.Left;
            _goldAmountText.textWrappingMode = TextWrappingModes.NoWrap;
            _goldAmountText.raycastTarget = false;
            ApplyFont(_goldAmountText);
            _goldAmountText.text = "0";
        }

        /// <summary>
        /// Builds a shop name header centered above the Items column at the same
        /// vertical position as the gold display.
        /// </summary>
        private void BuildShopNameHeader()
        {
            var go = new GameObject("ShopNameHeader", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Same Y as gold display, centered over the Items column (x = -118.44)
            rt.anchoredPosition = new Vector2(-118.44f, -22.93f + 289.12f / 2f + 33f);
            rt.sizeDelta = new Vector2(180f, 18f);

            _shopNameText = go.AddComponent<TextMeshProUGUI>();
            _shopNameText.fontSize = 11f;
            _shopNameText.color = COL_TEXT_DARK;
            _shopNameText.alignment = TextAlignmentOptions.Center;
            _shopNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _shopNameText.overflowMode = TextOverflowModes.Ellipsis;
            _shopNameText.raycastTarget = false;
            ApplyFont(_shopNameText);
            _shopNameText.text = "";
        }

        /// <summary>
        /// Refreshes the gold display with the player's current coin count.
        /// </summary>
        private void RefreshGoldDisplay()
        {
            if (_goldAmountText == null) return;
            int coins = CountPlayerItems("Coins", 0);
            _goldAmountText.text = coins.ToString("N0");
        }

        /// <summary>
        /// Refreshes owned counts on all visible item rows.
        /// </summary>
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

        #region Trade Logic

        private bool CanAffordTrade(TradeEntry trade, int multiplier)
        {
            if (trade == null || trade.Stock == 0) return false;
            foreach (var cost in trade.CostItems)
            {
                if (CountPlayerItems(cost.PrefabName, cost.Level) < cost.Amount * multiplier)
                    return false;
            }
            return true;
        }

        private void ExecuteTrade(TradeEntry trade)
        {
            int qty = _tradeAmounts.TryGetValue(trade, out int q) ? q : 1;
            if (!CanAffordTrade(trade, qty))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!");
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null) return;
            var inventory = player.GetInventory();

            foreach (var cost in trade.CostItems)
            {
                if (!RemovePlayerItems(inventory, cost.PrefabName, cost.Amount * qty, cost.Level))
                    return;
            }

            foreach (var reward in trade.RewardItems)
                GivePlayerItems(player, reward.PrefabName, reward.Amount * qty, reward.Level);

            if (trade.Stock > 0)
                trade.Stock = Mathf.Max(0, trade.Stock - qty);

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
                    data.m_quality = quality;
                    data.m_stack = 1;
                    if (!inv.AddItem(data))
                        ItemDrop.DropItem(data, 1,
                            player.transform.position + player.transform.forward + Vector3.up,
                            Quaternion.identity);
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
                    if (level > 0)
                        creature.GetComponent<Character>()?.SetLevel(level);
                    creature.GetComponent<Tameable>()?.Tame();
                }
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Formats a cost/reward summary for display in the Buy/Sell columns.
        /// Currency items (Coins) show just the numeric amount (e.g. "50").
        /// Non-currency items show "amount name" (e.g. "5 Iron").
        /// </summary>
        private string FormatCostSummary(List<TradeItemStack> items, int multiplier)
        {
            var parts = new List<string>();
            foreach (var item in items)
            {
                int amt = item.Amount * multiplier;
                if (IsCurrencyStack(item))
                {
                    // Just show the number — column context makes it clear it's gold
                    parts.Add(amt.ToString());
                }
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
            displayName = prefabName;
            icon = null;
            var go = ZNetScene.instance?.GetPrefab(prefabName);
            if (go == null) return;
            var drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData == null) return;
            displayName = ItemDataHelper.GetDisplayName(drop.m_itemData);
            icon = drop.m_itemData.GetIcon();
        }

        private static TradeItemStack FindDisplayItem(TradeEntry trade)
        {
            foreach (var item in trade.RewardItems)
                if (!IsCurrencyStack(item))
                    return item;
            foreach (var item in trade.CostItems)
                if (!IsCurrencyStack(item))
                    return item;
            return trade.RewardItems.FirstOrDefault() ?? trade.CostItems.First();
        }

        /// <summary>
        /// Returns true if the item stack is a currency (Coins, etc.) rather than
        /// a tradeable item.  Used to determine buy vs sell trade direction.
        /// </summary>
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
            int hours = minutes / 60;
            int mins = minutes % 60;
            return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }

        #endregion

        #region Trade Confirmation

        /// <summary>
        /// Shows a confirmation popup before executing a trade.
        /// </summary>
        private void ShowTradeConfirmation(TradeEntry trade, bool isBuy)
        {
            int qty = _tradeAmounts.TryGetValue(trade, out int q) ? q : 1;
            if (!CanAffordTrade(trade, qty))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!");
                return;
            }

            var displayItem = FindDisplayItem(trade);
            ResolveItemInfo(displayItem.PrefabName, out string itemName, out _);
            int itemAmount = displayItem.Amount * qty;

            // Determine the gold price from the currency side
            var currencyStack = isBuy ? trade.CostItems.FirstOrDefault() : trade.RewardItems.FirstOrDefault();
            int goldAmount = currencyStack != null ? currencyStack.Amount * qty : 0;

            string message;
            if (isBuy)
                message = $"Buy {itemAmount} {itemName} for {goldAmount} gold?";
            else
                message = $"Sell {itemAmount} {itemName} for {goldAmount} gold?";

            EnsureConfirmDialog();
            _confirmText.text = message;
            _pendingTradeAction = () => ExecuteTrade(trade);
            _confirmRoot.SetActive(true);
        }

        private void EnsureConfirmDialog()
        {
            if (_confirmRoot != null) return;

            // Full-screen overlay parented to the trader panel
            _confirmRoot = new GameObject("TradeConfirmDialog");
            _confirmRoot.transform.SetParent(_panelRoot.transform, false);
            var rootRect = _confirmRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Dim background that blocks clicks
            var dimGo = new GameObject("DimBG");
            dimGo.transform.SetParent(_confirmRoot.transform, false);
            var dimRect = dimGo.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImg = dimGo.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.5f);

            // Dialog box
            var boxGo = new GameObject("DialogBox");
            boxGo.transform.SetParent(_confirmRoot.transform, false);
            var boxRect = boxGo.AddComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(280f, 100f);
            var boxImg = boxGo.AddComponent<Image>();
            boxImg.color = new Color(0.12f, 0.10f, 0.08f, 0.95f);

            // Message text
            var textGo = new GameObject("ConfirmText");
            textGo.transform.SetParent(boxGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.05f, 0.4f);
            textRect.anchorMax = new Vector2(0.95f, 0.95f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _confirmText = textGo.AddComponent<TextMeshProUGUI>();
            _confirmText.fontSize = 11f;
            _confirmText.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            _confirmText.alignment = TextAlignmentOptions.Center;
            _confirmText.textWrappingMode = TextWrappingModes.Normal;
            GUIManager.ApplyGameFont(_confirmText);

            // Yes button
            var yesGo = CreateConfirmButton(boxGo.transform, "Yes", new Vector2(-40f, -35f),
                new Color(0.15f, 0.45f, 0.15f, 1f), () =>
                {
                    _confirmRoot.SetActive(false);
                    _pendingTradeAction?.Invoke();
                    _pendingTradeAction = null;
                });

            // No button
            var noGo = CreateConfirmButton(boxGo.transform, "No", new Vector2(40f, -35f),
                new Color(0.50f, 0.18f, 0.12f, 1f), () =>
                {
                    _confirmRoot.SetActive(false);
                    _pendingTradeAction = null;
                });

            _confirmRoot.SetActive(false);
        }

        private static GameObject CreateConfirmButton(Transform parent, string label, Vector2 pos, Color bgColor, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(60f, 22f);
            rect.anchoredPosition = pos;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 11f;
            tmp.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            GUIManager.ApplyGameFont(tmp);

            return go;
        }

        #endregion
    }

    /// <summary>
    /// Allows dragging the TraderUI panel around the screen.
    /// </summary>
    public class TraderDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        private RectTransform _rect;
        private Canvas _canvas;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rect == null || _canvas == null) return;
            float scale = _canvas.scaleFactor;
            if (scale <= 0) scale = 1f;
            _rect.anchoredPosition += eventData.delta / scale;
        }
    }
}
