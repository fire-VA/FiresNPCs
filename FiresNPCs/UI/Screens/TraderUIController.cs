using System;
using System.Collections.Generic;
using System.IO;
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
    /// Player-facing trader UI rendered as a TWO-PAGE OPEN-BOOK overlay using <c>va_background5.png</c>
    /// as the panel artwork. Column headers (Item / Buy / Amount / Sell / Stock / Restock) are baked
    /// into the artwork — this controller positions a synchronized data overlay over the painted columns.
    ///
    /// Left page = Item column (icon + name + owned-count badge per row).
    /// Right page = 5 narrow scroll columns: Buy / Amount / Sell / Stock / Restock.
    /// Above the book panel: KG-Marketplace-parity Search input + x1/x5/x10/x100 quantity modifier.
    ///
    /// Trade engine (CanAffordTrade / ExecuteTrade / CountPlayerItems / RemovePlayerItems /
    /// GivePlayerItems / ResolveItemInfo / FindDisplayItem / IsCurrencyStack / FormatCostSummary /
    /// FormatTime) is preserved verbatim across all the layout iterations of this UI — same trade
    /// semantics, new view.
    /// </summary>
    public class TraderUIController : MonoBehaviour
    {
        public static TraderUIController Instance { get; private set; }

        // ── Image + panel layout constants ──
        // Panel size + column rects pulled verbatim from the original saved layout
        // `traderuifixed.json` (UI Layouts > Custom > TraderUIFixed). Background sprite reference in
        // that file is "cached:va_background5" — the same PNG we now ship as an embedded resource.
        // The va_background5 image is 1200×1080 but the original panel stretched it to 1440×1350 to
        // give the rows more vertical room; we honor that stretch (preserveAspect=false on the BG).
        private const float PANEL_W = 1440f;
        private const float PANEL_H = 1410f;                 // 1350 book + 60 header strip above
        private const float BOOK_W = 1440f;
        private const float BOOK_H = 1350f;
        private const float BOOK_Y_OFFSET = -30f;            // shift book down 30 so a 60px strip is clear at panel top

        // Header strip (above the book): search + modifier buttons.
        private const float HEADER_STRIP_Y = 675f;            // (PANEL_H / 2) - 30 = top edge of strip

        // Column rects — exact AnchoredPosition + SizeDelta from traderuifixed.json elements:
        //   Icons / Items / BuyPrice / AmountSoldBought / SellPrice / TotalStock / RestockTime
        // All columns are AnchorMin/Max (0.5, 0.5), pivot (0.5, 0.5), Y = -50.274, height 520.416.
        private const float BODY_CENTER_Y = -50.274f + BOOK_Y_OFFSET;
        private const float BODY_HEIGHT   = 520.416f;

        private const float COL_X_ICONS   = -390.078f;
        private const float COL_W_ICONS   =   75.186f;
        // Widened from the saved layout's 207 → 290 so long names like "Carapace Breastplate (owned-0)"
        // don't ellipsize. The Icons column right edge is at -352.5 and Buy column left edge is at +39.6;
        // 290 wide centered at -165 leaves comfortable gaps on both sides (left edge -310, right -20).
        private const float COL_X_ITEMS   = -165f;
        private const float COL_W_ITEMS   =  290f;
        private const float COL_X_BUY     =   82.800f;
        private const float COL_W_BUY     =   86.400f;
        private const float COL_X_AMOUNT  =  157.000f;
        private const float COL_W_AMOUNT  =   72f;
        private const float COL_X_SELL    =  242.856f;
        private const float COL_W_SELL    =   85.014f;
        private const float COL_X_STOCK   =  321.246f;
        private const float COL_W_STOCK   =   87.300f;
        private const float COL_X_RESTOCK =  409.446f;
        private const float COL_W_RESTOCK =   78.714f;

        // Top-of-page overlays — shop name centered on the left page's title band, gold display
        // centered on the right page's title band. The va_background5 artwork paints a small
        // horizontal title rule just below each page's top-corner decoration; this Y puts the
        // text right on that rule. (Body top is at BODY_CENTER_Y + BODY_HEIGHT/2 ≈ 180; the
        // painted title band sits ~220 above that — between the rune-border decoration and the
        // column-header text strip.)
        private const float OVERLAY_Y    = BODY_CENTER_Y + BODY_HEIGHT * 0.5f + 220f;
        private const float SHOP_NAME_X  = -193.400f;        // centered over Items column (left page)
        private const float GOLD_X       =  242.856f;        // centered over SellPrice column (right page)

        private const float ROW_HEIGHT = 32.4f;              // matches the IconRow SizeDelta from the saved layout
        private const float ICON_SIZE  = 28.8f;              // matches Icon SizeDelta from the saved layout
        private const float ROW_SPACING = 1.8f;
        private const float FONT_SIZE = 16f;
        private const float FONT_SIZE_SMALL = 10f;
        private const float SCROLL_SENSITIVITY = 40f;

        // KG modifier values.
        private static readonly int[] ModifierValues = { 1, 5, 10, 100 };

        // Inks chosen to read on aged-parchment.
        private static readonly Color INK_DARK      = new Color(0.18f, 0.10f, 0.04f, 1f);
        private static readonly Color INK_BROWN     = new Color(0.36f, 0.22f, 0.10f, 1f);
        private static readonly Color INK_RED       = new Color(0.62f, 0.18f, 0.12f, 1f);
        private static readonly Color INK_GREEN     = new Color(0.13f, 0.42f, 0.13f, 1f);
        private static readonly Color INK_DIM       = new Color(0.50f, 0.42f, 0.32f, 0.7f);
        private static readonly Color INK_GOLD      = new Color(0.68f, 0.54f, 0.16f, 1f);
        private static readonly Color INK_OWNED     = new Color(0.40f, 0.30f, 0.18f, 0.85f);
        private static readonly Color INK_STOCK_OK  = new Color(0.25f, 0.20f, 0.15f, 1f);
        private static readonly Color INK_STOCK_OUT = new Color(0.55f, 0.20f, 0.15f, 1f);
        private static readonly Color BTN_BG_OK     = new Color(0.78f, 0.65f, 0.40f, 0.30f);
        private static readonly Color BTN_BG_NO     = new Color(0.60f, 0.50f, 0.40f, 0.18f);
        private static readonly Color SEARCH_BG     = new Color(0.92f, 0.82f, 0.62f, 0.65f);
        private static readonly Color MOD_ON        = new Color(0.55f, 0.32f, 0.14f, 0.85f);
        private static readonly Color MOD_OFF       = new Color(0.35f, 0.24f, 0.14f, 0.55f);

        // Runtime state
        private GameObject _panelRoot;
        private GameObject _standaloneCanvas;
        private NpcController _currentNpc;
        private TraderProfile _currentProfile;

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

        // Scroll content roots — 7 synced columns matching the original saved layout
        // (Icons / Items / BuyPrice / AmountSoldBought / SellPrice / TotalStock / RestockTime).
        private RectTransform _iconsContent;
        private RectTransform _itemContent;
        private RectTransform _buyContent;
        private RectTransform _amountContent;
        private RectTransform _sellContent;
        private RectTransform _stockContent;
        private RectTransform _restockContent;

        private readonly List<ScrollRect> _columnScrollRects = new List<ScrollRect>();
        private readonly List<GameObject> _rows = new List<GameObject>();

        // Top overlay widgets
        private TextMeshProUGUI _shopNameText;
        private TextMeshProUGUI _goldAmountText;
        private TMP_InputField _searchInput;
        private string _searchLower = "";
        private int _modifier = 1;
        private readonly Image[] _modifierBgs = new Image[4];

        // Confirm dialog
        private GameObject _confirmRoot;
        private TMP_Text _confirmText;
        private Action _pendingTradeAction;

        // Cached book sprite (loaded once from disk, shared across opens).
        private static Sprite _bookSprite;

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

        #region Background sprite loader

        /// <summary>Loads <c>va_background5.png</c> once and caches the Sprite. Tries the embedded resource
        /// (shipped inside the DLL) first, then falls back to a loose PNG next to the DLL for hot-swap dev.
        /// Returns null on missing/corrupt PNG — the panel falls back to a flat color in that case.</summary>
        private static Sprite LoadBookSprite()
        {
            if (_bookSprite != null) return _bookSprite;
            try
            {
                byte[] data = null;
                // 1) Embedded resource — preferred (ships with the DLL).
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var stream = asm.GetManifestResourceStream("FiresRPGmaker.Resources.va_background5.png"))
                    {
                        if (stream != null)
                        {
                            data = new byte[stream.Length];
                            int read = 0, off = 0;
                            while ((read = stream.Read(data, off, data.Length - off)) > 0) off += read;
                        }
                    }
                }
                catch { /* fall through to disk */ }

                // 2) Disk fallback — loose PNG next to the DLL for asset iteration without rebuilding.
                if (data == null)
                {
                    string asmPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string pluginDir = Path.GetDirectoryName(asmPath);
                    string pngPath = Path.Combine(pluginDir, "va_background5.png");
                    if (!File.Exists(pngPath))
                    {
                        Debug.LogWarning($"[TraderUIController] book background not embedded AND missing on disk at {pngPath} — falling back to flat panel");
                        return null;
                    }
                    data = File.ReadAllBytes(pngPath);
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                // LoadImage lives in UnityEngine.ImageConversionModule which targets netstandard 2.1 —
                // calling it directly trips the .NET Standard reference mismatch in our 2.0 project.
                // Reflection-invoke sidesteps the compile-time dependency entirely.
                // The extension lives in a DIFFERENT assembly than Texture2D (UnityEngine.ImageConversionModule
                // vs UnityEngine.CoreModule), so we resolve it by fully-qualified name. Pattern copied from
                // FiresSteamyDumps/Debug/YoinkAssets.cs which encodes PNGs the same way in reverse.
                bool loaded = false;
                var imageConvType = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConvType != null)
                {
                    var mi = imageConvType.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (mi != null) loaded = (bool)mi.Invoke(null, new object[] { tex, data });
                }
                // Fallback: legacy instance method on Texture2D (removed in newer Unity but try anyway).
                if (!loaded)
                {
                    var mi = typeof(Texture2D).GetMethod("LoadImage", new[] { typeof(byte[]) });
                    if (mi != null) loaded = (bool)mi.Invoke(tex, new object[] { data });
                }
                if (!loaded) { Debug.LogWarning("[TraderUIController] LoadImage reflection failed — UnityEngine.ImageConversionModule not loaded?"); return null; }
                _bookSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _bookSprite.name = "va_background5";
                return _bookSprite;
            }
            catch (Exception ex) { Debug.LogError("[TraderUIController] LoadBookSprite failed: " + ex.Message); return null; }
        }

        #endregion

        #region Bootstrap

        private static void EnsureInstance()
        {
            if (Instance != null && Instance._panelRoot != null) return;
            if (Hud.instance == null) return;

            // Standalone overlay canvas so opening/closing doesn't dirty Hud's inventory grid.
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

            // Panel root — 1200×1140 (1080 book + 60 header strip).
            var panel = new GameObject("TraderUI_Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            panel.AddComponent<TraderDragHandler>();

            // Book artwork at the bottom of the panel.
            var bookGo = new GameObject("BookBackground", typeof(RectTransform), typeof(Image));
            bookGo.transform.SetParent(panel.transform, false);
            var brt = (RectTransform)bookGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, BOOK_Y_OFFSET);
            brt.sizeDelta = new Vector2(BOOK_W, BOOK_H);
            var bookImg = bookGo.GetComponent<Image>();
            bookImg.raycastTarget = true;
            var sprite = LoadBookSprite();
            // Original layout stretched the 1200×1080 source to 1440×1350 — preserveAspect=false to match.
            if (sprite != null) { bookImg.sprite = sprite; bookImg.color = Color.white; bookImg.preserveAspect = false; }
            else                { bookImg.color = new Color(0.30f, 0.20f, 0.10f, 0.95f); }

            var controller = panel.AddComponent<TraderUIController>();
            controller._panelRoot = panel;
            controller._standaloneCanvas = canvasGo;
            controller.BuildColumns();
            controller.BuildTopOverlay();
            controller.BuildHeaderStrip();
            controller.BuildCloseButton();
            panel.SetActive(false);
        }

        #endregion

        #region Top overlay (shop name + gold display)

        private void BuildTopOverlay()
        {
            // Shop name — italic serif, overlaid above the Item column header on the LEFT page.
            var shopGo = new GameObject("ShopName", typeof(RectTransform));
            shopGo.transform.SetParent(_panelRoot.transform, false);
            var srt = (RectTransform)shopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(SHOP_NAME_X, OVERLAY_Y);
            srt.sizeDelta = new Vector2(380f, 32f);
            _shopNameText = shopGo.AddComponent<TextMeshProUGUI>();
            _shopNameText.fontSize = 22f;
            _shopNameText.color = INK_DARK;
            _shopNameText.alignment = TextAlignmentOptions.Center;
            _shopNameText.fontStyle = FontStyles.Italic | FontStyles.Bold;
            _shopNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _shopNameText.raycastTarget = false;
            ApplyFont(_shopNameText);

            // Gold display — coin icon + balance, overlaid above the right page header band.
            var goldGo = new GameObject("GoldDisplay", typeof(RectTransform));
            goldGo.transform.SetParent(_panelRoot.transform, false);
            var grt = (RectTransform)goldGo.transform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(GOLD_X, OVERLAY_Y);
            grt.sizeDelta = new Vector2(220f, 32f);

            var hlg = goldGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false; hlg.childControlHeight = false;

            var iconGo = new GameObject("CoinIcon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            iconGo.transform.SetParent(goldGo.transform, false);
            var iconLE = iconGo.GetComponent<LayoutElement>();
            // Match the original FiresVAngarde GoldDisplay proportions — small ornate icon, not a
            // big inventory-style nugget. Original was 12×12 at localScale 1.8 (≈22px effective);
            // here we run unscaled so use 18 to land at the same effective size on screen.
            iconLE.preferredWidth = 18f; iconLE.preferredHeight = 18f;
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
            textLE.preferredWidth = 140f; textLE.preferredHeight = 22f;
            _goldAmountText = textGo.AddComponent<TextMeshProUGUI>();
            _goldAmountText.fontSize = 18f;
            _goldAmountText.color = INK_GOLD;
            _goldAmountText.alignment = TextAlignmentOptions.MidlineLeft;
            _goldAmountText.fontStyle = FontStyles.Bold;
            _goldAmountText.textWrappingMode = TextWrappingModes.NoWrap;
            _goldAmountText.raycastTarget = false;
            ApplyFont(_goldAmountText);
            _goldAmountText.text = "0";
        }

        #endregion

        #region Header strip (search + modifier buttons)

        private void BuildHeaderStrip()
        {
            // Search input on the left half of the strip.
            var searchGo = new GameObject("Search", typeof(RectTransform), typeof(Image));
            searchGo.transform.SetParent(_panelRoot.transform, false);
            var srt = (RectTransform)searchGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(-220f, HEADER_STRIP_Y);
            srt.sizeDelta = new Vector2(360f, 38f);
            searchGo.GetComponent<Image>().color = SEARCH_BG;

            var input = searchGo.AddComponent<TMP_InputField>();
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(searchGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            Stretch(trt); trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 18f;
            tmp.color = INK_DARK;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            ApplyFont(tmp);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(searchGo.transform, false);
            var prt = (RectTransform)placeholderGo.transform;
            Stretch(prt); prt.offsetMin = new Vector2(10f, 0f); prt.offsetMax = new Vector2(-10f, 0f);
            var ptmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            ptmp.text = "Search…";
            ptmp.fontSize = 18f;
            ptmp.color = new Color(0.30f, 0.22f, 0.10f, 0.5f);
            ptmp.alignment = TextAlignmentOptions.MidlineLeft;
            ptmp.fontStyle = FontStyles.Italic;
            ptmp.raycastTarget = false;
            ApplyFont(ptmp);

            input.textViewport = trt;
            input.textComponent = tmp;
            input.placeholder = ptmp;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onValueChanged.AddListener(val => { _searchLower = (val ?? "").ToLowerInvariant(); PopulateUIRows(); });
            _searchInput = input;

            // x1 / x5 / x10 / x100 buttons on the right half of the strip.
            for (int i = 0; i < ModifierValues.Length; i++)
            {
                int idx = i;
                int val = ModifierValues[i];
                var btn = BuildModifierButton(val, 60f + i * 80f, HEADER_STRIP_Y);
                _modifierBgs[i] = btn.GetComponent<Image>();
                btn.onClick.AddListener(() => SetModifier(idx));
            }
            RefreshModifierButtonsVisual();
        }

        private Button BuildModifierButton(int value, float xPos, float yPos)
        {
            var go = new GameObject($"x{value}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(xPos, yPos);
            rt.sizeDelta = new Vector2(68f, 34f);
            go.GetComponent<Image>().color = MOD_OFF;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)labelGo.transform);
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = $"x{value}";
            t.fontSize = 18f;
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
                _modifierBgs[i].color = (ModifierValues[i] == _modifier) ? MOD_ON : MOD_OFF;
            }
        }

        private void BuildCloseButton()
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-12f, -12f);
            rt.sizeDelta = new Vector2(36f, 36f);
            go.GetComponent<Image>().color = new Color(0.42f, 0.15f, 0.10f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(Hide);

            var labelGo = new GameObject("X", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)labelGo.transform);
            var t = labelGo.AddComponent<TextMeshProUGUI>();
            t.text = "X"; t.fontSize = 20f;
            t.color = new Color(1f, 0.9f, 0.85f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
            ApplyFont(t);
        }

        #endregion

        #region 6 Synced Columns (left page Item + 5 right-page columns)

        private void BuildColumns()
        {
            _columnScrollRects.Clear();
            _iconsContent   = MakeCol("Icons",            COL_X_ICONS,   COL_W_ICONS);
            _itemContent    = MakeCol("Items",            COL_X_ITEMS,   COL_W_ITEMS);
            _buyContent     = MakeCol("BuyPrice",         COL_X_BUY,     COL_W_BUY);
            _amountContent  = MakeCol("AmountSoldBought", COL_X_AMOUNT,  COL_W_AMOUNT);
            _sellContent    = MakeCol("SellPrice",        COL_X_SELL,    COL_W_SELL);
            _stockContent   = MakeCol("TotalStock",       COL_X_STOCK,   COL_W_STOCK);
            _restockContent = MakeCol("RestockTime",      COL_X_RESTOCK, COL_W_RESTOCK);

            foreach (var sr in _columnScrollRects)
            {
                var captured = sr;
                sr.onValueChanged.AddListener(pos => SyncScroll(captured, pos));
            }
        }

        private RectTransform MakeCol(string name, float x, float w)
            => CreateColumnScrollView(_panelRoot.transform, name,
                                       new Vector2(x, BODY_CENTER_Y),
                                       new Vector2(w, BODY_HEIGHT));

        private RectTransform CreateColumnScrollView(Transform parent, string colName, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var col = new GameObject(colName, typeof(RectTransform));
            col.transform.SetParent(parent, false);
            var colRect = (RectTransform)col.transform;
            colRect.anchorMin = colRect.anchorMax = new Vector2(0.5f, 0.5f);
            colRect.pivot = new Vector2(0.5f, 0.5f);
            colRect.anchoredPosition = anchoredPos;
            colRect.sizeDelta = sizeDelta;

            // Transparent BG so the parchment shows through; raycast off so it doesn't eat clicks.
            var bgImg = col.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0f);
            bgImg.raycastTarget = false;

            // Viewport with RectMask2D (no Graphic needed — works without sprite/material).
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(col.transform, false);
            var vpRect = (RectTransform)viewport.transform;
            vpRect.anchorMin = Vector2.zero; vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(2, 2); vpRect.offsetMax = new Vector2(-2, -2);

            // Raycast catcher so the column accepts scroll-wheel input.
            var raycastGo = new GameObject("RaycastCatcher", typeof(RectTransform), typeof(Image));
            raycastGo.transform.SetParent(viewport.transform, false);
            var rcRect = (RectTransform)raycastGo.transform;
            rcRect.anchorMin = Vector2.zero; rcRect.anchorMax = Vector2.one;
            rcRect.offsetMin = Vector2.zero; rcRect.offsetMax = Vector2.zero;
            var rcImg = raycastGo.GetComponent<Image>();
            rcImg.color = new Color(0f, 0f, 0f, 0f);
            rcImg.raycastTarget = true;

            // Content rect — top-anchored, grows downward via VerticalLayoutGroup + ContentSizeFitter.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1); cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0, 1);
            cRect.anchoredPosition = Vector2.zero;
            cRect.sizeDelta = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = ROW_SPACING;
            // padding.top = 17 mirrors the first IconRow's AnchoredPosition.Y (-17.2) in the original
            // traderuifixed.json layout — without this offset the rows render ~13px too high and miss
            // the painted horizontal lines on the parchment artwork. padding.bottom = 24 leaves the
            // matching breathing room at the bottom of the page that the original had.
            vlg.padding = new RectOffset(2, 2, 17, 24);
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

            // ── Left page: separate Icons column then Items column (matches saved layout) ──
            AddIconCell(_iconsContent, icon);
            var ownedTmp = AddItemCellWithOwned(_itemContent, itemLabel, ownedCount);

            // ── Right page: 5 narrow columns ──
            (TextMeshProUGUI text, Image bg, Button btn) buyRefs;
            if (isBuyTrade)
            {
                string buyText = FormatCostSummary(trade.CostItems, qty);
                buyRefs = AddButtonCell(_buyContent, buyText, canAfford ? INK_RED : INK_DIM,
                                        canAfford, () => ShowTradeConfirmation(trade, true));
            }
            else buyRefs = (AddUnavailableCell(_buyContent), null, null);

            var qtyInput = AddAmountInputCell(_amountContent, trade, qty);

            TextMeshProUGUI sellTmp = null; Image sellBg = null; Button sellBtn = null;
            if (isSellTrade)
            {
                string sellText = FormatCostSummary(trade.RewardItems, qty);
                var sellRefs = AddButtonCell(_sellContent, sellText, canAfford ? INK_GREEN : INK_DIM,
                                             canAfford, () => ShowTradeConfirmation(trade, false));
                sellTmp = sellRefs.text; sellBg = sellRefs.bg; sellBtn = sellRefs.btn;
            }
            else sellTmp = AddUnavailableCell(_sellContent);

            _rowRefs[trade] = new RowUIRefs
            {
                buyPriceText = buyRefs.text, buyPriceBg = buyRefs.bg, buyPriceBtn = buyRefs.btn,
                sellPriceText = sellTmp, sellPriceBg = sellBg, sellPriceBtn = sellBtn,
                ownedText = ownedTmp, qtyInput = qtyInput,
                isBuyTrade = isBuyTrade, isSellTrade = isSellTrade,
            };

            string stockText = trade.Stock < 0 ? "∞" : trade.Stock.ToString();
            Color stockColor = trade.Stock == 0 ? INK_STOCK_OUT : INK_STOCK_OK;
            AddTextCell(_stockContent, stockText, TextAlignmentOptions.Center, stockColor);

            string restockText = trade.Stock < 0 ? "—"
                              : trade.Stock > 0 ? "Available"
                              : trade.RestockSeconds > 0 ? FormatTime(trade.RestockSeconds)
                              : "Sold Out";
            AddTextCell(_restockContent, restockText, TextAlignmentOptions.Center, INK_BROWN);
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
                    refs.buyPriceText.color = canAfford ? INK_RED : INK_DIM;
                }
                if (refs.buyPriceBg != null) refs.buyPriceBg.color = canAfford ? BTN_BG_OK : BTN_BG_NO;
                if (refs.buyPriceBtn != null) refs.buyPriceBtn.interactable = canAfford;
            }
            if (refs.isSellTrade)
            {
                if (refs.sellPriceText != null)
                {
                    refs.sellPriceText.text = FormatCostSummary(trade.RewardItems, newAmount);
                    refs.sellPriceText.color = canAfford ? INK_GREEN : INK_DIM;
                }
                if (refs.sellPriceBg != null) refs.sellPriceBg.color = canAfford ? BTN_BG_OK : BTN_BG_NO;
                if (refs.sellPriceBtn != null) refs.sellPriceBtn.interactable = canAfford;
            }
        }

        #endregion

        #region Cell Factories

        /// <summary>Icons column cell — centered item sprite at ICON_SIZE.</summary>
        private void AddIconCell(RectTransform parent, Sprite icon)
        {
            if (parent == null) return;
            var row = CreateRow(parent, "IconRow");
            if (icon == null) return;
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            iconGo.transform.SetParent(row.transform, false);
            var ir = (RectTransform)iconGo.transform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
            iconGo.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = iconGo.GetComponent<Image>();
            img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
        }

        /// <summary>Items column cell — name + "(owned-N)" suffix in the same TMP via rich text.</summary>
        private TextMeshProUGUI AddItemCellWithOwned(RectTransform parent, string itemLabel, int ownedCount)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "ItemRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.text = FormatItemWithOwned(itemLabel, ownedCount);
            tmp.fontSize = FONT_SIZE;
            tmp.color = INK_DARK;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.margin = new Vector4(6, 0, 4, 0);
            ApplyFont(tmp);
            return tmp;
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
            var row = CreateRow(parent, "BtnRow");
            var bgImg = row.AddComponent<Image>();
            bgImg.color = interactable ? BTN_BG_OK : BTN_BG_NO;
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
            tmp.fontStyle = FontStyles.Bold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(2, 0, 2, 0);
            ApplyFont(tmp);
            return (tmp, bgImg, btn);
        }

        private TextMeshProUGUI AddUnavailableCell(RectTransform parent)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "UnavailRow");
            var tmp = row.AddComponent<TextMeshProUGUI>();
            tmp.text = "unavailable"; tmp.fontSize = FONT_SIZE_SMALL; tmp.color = INK_DIM;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Italic;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false; tmp.margin = new Vector4(2, 0, 2, 0);
            ApplyFont(tmp);
            return tmp;
        }

        private static string FormatItemWithOwned(string itemLabel, int ownedCount)
        {
            string ownedTag = $"<size=72%><color=#{ColorUtility.ToHtmlStringRGBA(INK_OWNED)}>(owned-{ownedCount})</color></size>";
            return $"{itemLabel} {ownedTag}";
        }

        private TMP_InputField AddAmountInputCell(RectTransform parent, TradeEntry trade, int currentAmount)
        {
            if (parent == null) return null;
            var row = CreateRow(parent, "AmountRow");
            // Near-transparent dark overlay so the parchment shows through but you can still see the
            // input field is interactive. Image is required because TMP_InputField uses it as the
            // raycast target; alpha 0 would still register clicks but reads visually cleaner.
            var bgImg = row.AddComponent<Image>(); bgImg.color = new Color(0f, 0f, 0f, 0.06f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(row.transform, false);
            var textRT = (RectTransform)textGo.transform;
            Stretch(textRT);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = FONT_SIZE; tmp.color = INK_DARK;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
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
            // Caret config — without this, TMP_InputField on a programmatic field shows no cursor on
            // focus, so the player can't tell the field is editable. customCaretColor=true lets us
            // override the default white caret (invisible on parchment) with the dark ink color.
            inputField.caretBlinkRate = 0.85f;
            inputField.caretWidth = 2;
            inputField.customCaretColor = true;
            inputField.caretColor = INK_DARK;
            inputField.selectionColor = new Color(0.55f, 0.40f, 0.18f, 0.55f);

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

        #region Trade Logic (verbatim)

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
            boxRect.sizeDelta = new Vector2(380f, 140f);
            boxGo.AddComponent<Image>().color = new Color(0.18f, 0.12f, 0.08f, 0.97f);

            var textGo = new GameObject("ConfirmText");
            textGo.transform.SetParent(boxGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.05f, 0.42f); textRect.anchorMax = new Vector2(0.95f, 0.95f);
            textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
            _confirmText = textGo.AddComponent<TextMeshProUGUI>();
            _confirmText.fontSize = 16f;
            _confirmText.color = new Color(0.95f, 0.88f, 0.72f, 1f);
            _confirmText.alignment = TextAlignmentOptions.Center;
            _confirmText.textWrappingMode = TextWrappingModes.Normal;
            ApplyFont(_confirmText);

            CreateConfirmButton(boxGo.transform, "Yes", new Vector2(-58f, -45f),
                                new Color(0.18f, 0.50f, 0.18f, 1f),
                                () => { _confirmRoot.SetActive(false); _pendingTradeAction?.Invoke(); _pendingTradeAction = null; });
            CreateConfirmButton(boxGo.transform, "No", new Vector2(58f, -45f),
                                new Color(0.55f, 0.20f, 0.14f, 1f),
                                () => { _confirmRoot.SetActive(false); _pendingTradeAction = null; });
            _confirmRoot.SetActive(false);
        }

        private static GameObject CreateConfirmButton(Transform parent, string label, Vector2 pos, Color bgColor, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(86f, 30f);
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
            tmp.text = label; tmp.fontSize = 14f;
            tmp.color = new Color(0.95f, 0.88f, 0.72f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            try { FiresCore.UI.UIBuilderHelper.ApplyValheimFont(tmp); } catch { }
            return go;
        }

        #endregion
    }

    /// <summary>Drag handler so the player can reposition the trader panel by clicking and dragging it.</summary>
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
