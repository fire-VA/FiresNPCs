using System;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Items;
using FiresRPGmaker.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Player-facing trader UI. KG-Marketplace-parity layout: loads the baked `MarketplaceTraderNewUI`
    /// prefab from the `firesnpcsui` bundle (extracted from KG's bundle 2026-06-07) + clones one
    /// `TraderInstantiateV3` row per trade. Each row shows cost items (NeededItems) on the left, a
    /// Conversion button in the middle, and reward items (ResultItems) on the right. A global x1/x5/x10/x100
    /// modifier scales quantities; the search input filters rows by display name. Stock/restock are shown
    /// as a small badge on the row corner (∞ = no badge).
    ///
    /// Trade logic (CanAffordTrade/ExecuteTrade/CountPlayerItems/RemovePlayerItems/GivePlayerItems/
    /// ResolveItemInfo/FindDisplayItem/IsCurrencyStack/FormatTime) is preserved verbatim from the prior
    /// programmatic version. Confirmation dialog is the same pattern, repurposed for the new layout.
    /// </summary>
    public class TraderUIController : MonoBehaviour
    {
        public static TraderUIController Instance { get; private set; }

        // Modifier (KG-style global quantity multiplier).
        private static readonly int[] ModifierValues = { 1, 5, 10, 100 };
        private int _modifier = 1;
        private readonly Image[] _modifierBgs = new Image[4];
        private readonly Text[] _modifierTexts = new Text[4];

        // Prefab roots.
        private GameObject _panelRoot;
        private GameObject _standaloneCanvas;
        private GameObject _rowTemplate;             // TraderInstantiateV3 (loaded once, cloned per trade)

        // Navigated nodes (resolved once on init).
        private Transform _scrollContent;            // Canvas/Scroll View/Viewport/Content
        private ScrollRect _scrollRect;
        private Text _headerText;                    // Canvas/Header/Text
        private InputField _searchInput;             // Canvas/Background/InputField

        // State.
        private NpcController _currentNpc;
        private TraderProfile _currentProfile;
        private readonly List<RowRefs> _rows = new List<RowRefs>();

        // Confirm dialog.
        private GameObject _confirmRoot;
        private TMP_Text _confirmText;
        private Action _pendingTradeAction;

        // Conversion arrow color encoding.
        private static readonly Color CONV_OK   = new Color(0.657f, 1f,    0.373f, 1f);       // KG's default green
        private static readonly Color CONV_NO   = new Color(1f,     0.30f, 0.20f,  1f);       // affordability fail / sold out
        private static Font _legacyFont;
        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            try { _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_legacyFont == null) try { _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return _legacyFont;
        }

        private static readonly Color BADGE_OK  = new Color(1f,     1f,    1f,     1f);
        private static readonly Color BADGE_OUT = new Color(1f,     0.45f, 0.40f,  1f);

        private const string PREFAB_PANEL = "MarketplaceTraderNewUI";
        private const string PREFAB_ROW   = "TraderInstantiateV3";

        // KG's prefab references sprite assets that live in his bundle (NOT ours) — when the prefab was
        // baked into firesnpcsui those refs became null because the sprite assets weren't packaged with it.
        // So at runtime we re-resolve sprites BY NAME using `CodegenUIWiring.ResolveSpriteByNamePublic`
        // (scans live game sprites). Names captured from Tools\kg-trader-extract.txt.
        private static readonly Dictionary<string, string> PanelSpriteByPath = new Dictionary<string, string>
        {
            { "Background",                                      "little_background_frame" },
            { "Background/Border",                               "icon_frame_Cms" },
            { "Background/Pergament",                            "Paper_01" },
            { "Background/InputField",                           "InputField_Glow" },
            { "Background/InputField/Image",                     "midbutton_frame" },
            { "Background/x1",                                   "InputField_Glow" },
            { "Background/x1/Image",                             "midbutton_frame" },
            { "Background/x5",                                   "InputField_Glow" },
            { "Background/x5/Image",                             "midbutton_frame" },
            { "Background/x10",                                  "InputField_Glow" },
            { "Background/x10/Image",                            "midbutton_frame" },
            { "Background/x100",                                 "InputField_Glow" },
            { "Background/x100/Image",                           "midbutton_frame" },
            { "Header/Background",                               "Background_grey_1" },
            { "Header/Background/_bg",                           "icon_frame_Cms" },
            { "Scroll View/Viewport",                            "UIMask" },
            { "Scroll View/Scrollbar Vertical",                  "scroll_frame" },
            { "Scroll View/Scrollbar Vertical/Sliding Area/Handle", "mid_button_vert" },
        };

        // Row template (TraderInstantiateV3) sprite map — applied per-clone.
        private static readonly Dictionary<string, string> RowSpriteByPath = new Dictionary<string, string>
        {
            { "Background",                                "basic_bar" },
            { "Background/Conversion/Background",          "Button_Island" },
            { "Background/Conversion/Conversion",          "CS3D_Arrow_Foreground" },
            { "Background/NeededItems/NeededItem",         "Paper_02" },
            { "Background/ResultItems/ResultItem",         "Paper_02" },
        };

        private struct RowRefs
        {
            public TradeEntry Trade;
            public GameObject Root;
            public Button ConversionButton;
            public Image ConversionArrow;
            public TMP_Text StockBadge;
            public string SearchKey;
        }

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        // ─────────────── Lifecycle ───────────────
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

        // ─────────────── Show / Hide ───────────────
        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) return;
            Debug.Log($"[TraderUIController] ShowForNpc: npc='{npc.GetDisplayName()}', traderProfile='{npc.traderProfile}'");
            string profileName = npc.traderProfile;
            if (string.IsNullOrEmpty(profileName)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has nothing to trade."); return; }

            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(profileName);
            if (profile == null || profile.Trades.Count == 0) { Debug.LogWarning($"[TraderUIController] profile '{profileName}' missing or has no trades (profile null = {profile == null})"); MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has no items to trade."); return; }
            Debug.Log($"[TraderUIController] profile '{profileName}' loaded with {profile.Trades.Count} trade(s)");

            EnsureInstance();
            if (Instance == null) { Debug.LogError("[TraderUIController] EnsureInstance failed — Instance is null"); return; }
            Instance._currentNpc = npc;
            Instance._currentProfile = profile;
            Instance.PopulateUI(npc.GetDisplayName());
            Instance._panelRoot.SetActive(true);
            GUIManager.BlockInput(true);
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            Debug.Log($"[TraderUIController] panel active — IsVisible={Instance.IsVisible}");
            // Diagnostic: panel root rect + image counts so we can see if it's positioned / has visuals
            var prt = Instance._panelRoot.transform as RectTransform;
            var allImages = Instance._panelRoot.GetComponentsInChildren<Image>(true);
            int withSprite = 0; foreach (var i in allImages) if (i.sprite != null) withSprite++;
            Debug.Log($"[TraderUIController] panel rect: aMin={prt?.anchorMin} aMax={prt?.anchorMax} offMin={prt?.offsetMin} offMax={prt?.offsetMax} sizeDelta={prt?.sizeDelta} | total images={allImages.Length}, with-sprite={withSprite}");
        }

        public static void ShowForCompanion(string displayName, string traderProfile)
        {
            if (string.IsNullOrEmpty(traderProfile)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{displayName} has nothing to trade."); return; }
            TraderProfileManager.Initialize();
            var profile = TraderProfileManager.GetProfile(traderProfile);
            if (profile == null || profile.Trades.Count == 0) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{displayName} has no items to trade."); return; }

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

        /// <summary>Called from UIInitializer.ResetInitialization on logout/scene change.</summary>
        public static void Cleanup()
        {
            if (Instance != null && Instance._standaloneCanvas != null) Destroy(Instance._standaloneCanvas);
            Instance = null;
        }

        // ─────────────── Bootstrap (load + wire KG prefab) ───────────────
        private static void EnsureInstance()
        {
            if (Instance != null && Instance._panelRoot != null) return;
            if (Hud.instance == null) return;

            var prefab = LoadBundlePrefab(PREFAB_PANEL);
            var rowTemplate = LoadBundlePrefab(PREFAB_ROW);
            if (prefab == null || rowTemplate == null)
            {
                Debug.LogError($"[TraderUIController] Missing baked prefabs in firesnpcsui (panel={prefab != null}, row={rowTemplate != null}); cannot show trader UI.");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Trader UI assets missing.");
                return;
            }

            // Standalone top-level canvas (same pattern as the prior controller — keeps the trader off Hud's
            // canvas so opening/closing doesn't dirty the inventory grid and tank fps).
            var canvasGo = new GameObject("TraderUI_Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210; // above HudMessage etc., below the help panel (300)
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var cr = canvasGo.GetComponent<RectTransform>();
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;

            // KG's prefab is self-canvased: a plain-Transform root with a single [Canvas] child carrying its
            // own Canvas + CanvasScaler + GraphicRaycaster, and editor-time RectTransform offsets that put
            // the content off-screen under any other canvas. Nesting that under our outer ScreenSpaceOverlay
            // canvas (sortingOrder 210) leaves the inner canvas at sortingOrder 0 (behind HudMessage) with
            // its captured offMin = (-1455,-762) — the "blank screen" symptom. Reparent the [Canvas] child
            // directly to our outer canvas, strip the redundant Canvas/Scaler/Raycaster, reset its
            // RectTransform to fill, and dump the empty wrapper.
            var tempRoot = Instantiate(prefab);
            var inner = tempRoot.transform.Find("Canvas");
            if (inner == null) { Debug.LogError("[TraderUIController] KG prefab missing inner [Canvas] child"); Destroy(tempRoot); return; }
            inner.SetParent(canvasGo.transform, false);
            var panel = inner.gameObject;
            panel.name = "TraderUI_Panel";
            Destroy(tempRoot);

            if (panel.TryGetComponent<CanvasScaler>(out var innerScaler)) Destroy(innerScaler);
            if (panel.TryGetComponent<GraphicRaycaster>(out var innerRaycaster)) Destroy(innerRaycaster);
            if (panel.TryGetComponent<Canvas>(out var innerCanvas)) Destroy(innerCanvas);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one; rt.localPosition = Vector3.zero;

            var controller = panel.AddComponent<TraderUIController>();
            controller._panelRoot = panel;
            controller._standaloneCanvas = canvasGo;
            controller._rowTemplate = rowTemplate;
            controller.WireNodes();
            panel.SetActive(false);
            Debug.Log($"[TraderUIController] EnsureInstance complete — panel under canvas (sortingOrder {canvas.sortingOrder})");
        }

        /// <summary>Case-insensitive lookup against the firesnpcsui bundle (KG's prefab names preserve original casing).</summary>
        private static GameObject LoadBundlePrefab(string name)
        {
            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) { Debug.LogError("[TraderUIController] FiresRPGmaker.uiBundle is null"); return null; }
            try
            {
                var go = bundle.LoadAsset<GameObject>(name);
                if (go != null) return go;
                string suffix = "/" + name.ToLowerInvariant() + ".prefab";
                foreach (var an in bundle.GetAllAssetNames())
                    if (an.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var hit = bundle.LoadAsset<GameObject>(an);
                        if (hit != null) return hit;
                    }
            }
            catch (Exception e) { Debug.LogError($"[TraderUIController] LoadBundlePrefab('{name}') threw: {e}"); }
            return null;
        }

        private void WireNodes()
        {
            // After EnsureInstance reparented KG's inner [Canvas] to be the panel root, navigation paths
            // are now ROOT-relative (no "Canvas/" prefix). Matches Tools\kg-trader-extract.txt sans Canvas.
            _scrollContent = _panelRoot.transform.Find("Scroll View/Viewport/Content");
            // Header/Text and Background/InputField may have lost their Text/InputField components during
            // KG's-prefab → our-project bake (the original Averia-Bold font + KG's MonoBehaviour refs don't
            // resolve in our editor), so search ROBUSTLY: prefer the named child path, fall back to any
            // descendant Text/InputField under [Header]/[Background] respectively. Logs diagnostics.
            var headerGo = _panelRoot.transform.Find("Header");
            var headerTextGo = headerGo != null ? headerGo.Find("Text") : null;
            _headerText = headerTextGo?.GetComponent<Text>() ?? headerGo?.GetComponentInChildren<Text>(true);
            if (_headerText == null && headerGo != null)
            {
                // Last-resort fallback: add a fresh Text on Header/Text (or on Header if Text node missing).
                var host = headerTextGo != null ? headerTextGo.gameObject : headerGo.gameObject;
                _headerText = host.AddComponent<Text>();
                _headerText.font = LegacyFont();
                _headerText.fontSize = 32; _headerText.color = Color.white; _headerText.alignment = TextAnchor.MiddleCenter;
                _headerText.raycastTarget = false;
            }

            var bgGo = _panelRoot.transform.Find("Background");
            var searchGo = bgGo != null ? bgGo.Find("InputField") : null;
            _searchInput = searchGo?.GetComponent<InputField>() ?? bgGo?.GetComponentInChildren<InputField>(true);

            Debug.Log($"[TraderUIController] WireNodes: header={_headerText != null} (path-find={headerTextGo != null}), scroll={_scrollContent != null}, search={_searchInput != null} (path-find={searchGo != null})");
            if (headerGo == null || bgGo == null)
            {
                // The big structural elements should exist — log direct child names so we can see what's actually there.
                var names = new System.Text.StringBuilder();
                foreach (Transform t in _panelRoot.transform) names.Append('[').Append(t.name).Append("] ");
                Debug.LogWarning($"[TraderUIController] missing structural child(ren): Header={headerGo!=null}, Background={bgGo!=null}. Direct children: {names}");
            }

            // KG had a custom ScrollView_Optimization MonoBehaviour on the Scroll View that the extract stripped
            // (missing-script); add a vanilla Unity ScrollRect so scrolling/sizing work.
            var scrollGo = _panelRoot.transform.Find("Scroll View");
            if (scrollGo != null)
            {
                _scrollRect = scrollGo.GetComponent<ScrollRect>() ?? scrollGo.gameObject.AddComponent<ScrollRect>();
                _scrollRect.horizontal = false;
                _scrollRect.vertical = true;
                _scrollRect.movementType = ScrollRect.MovementType.Clamped;
                _scrollRect.scrollSensitivity = 30f;
                var vp = scrollGo.Find("Viewport") as RectTransform;
                if (vp != null) _scrollRect.viewport = vp;
                if (_scrollContent is RectTransform contentRT) _scrollRect.content = contentRT;
                var sbar = scrollGo.Find("Scrollbar Vertical")?.GetComponent<Scrollbar>();
                if (sbar != null) _scrollRect.verticalScrollbar = sbar;
            }

            // VerticalLayoutGroup + ContentSizeFitter on Content so rows stack and the scrollbar gets a real range.
            if (_scrollContent != null)
            {
                var contentGo = _scrollContent.gameObject;
                var vlg = contentGo.GetComponent<VerticalLayoutGroup>() ?? contentGo.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(8, 8, 8, 8);
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true; vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
                var csf = contentGo.GetComponent<ContentSizeFitter>() ?? contentGo.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // Modifier buttons (x1/x5/x10/x100) — wire onClick + capture refs for the selected-state tint.
            string[] modNames = { "x1", "x5", "x10", "x100" };
            for (int i = 0; i < modNames.Length; i++)
            {
                var t = _panelRoot.transform.Find("Background/" + modNames[i]);
                if (t == null) continue;
                int captured = i;
                var btn = t.GetComponent<Button>(); if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => SetModifier(captured)); }
                _modifierBgs[i]   = t.GetComponent<Image>();
                _modifierTexts[i] = t.Find("Text")?.GetComponent<Text>();
            }
            SetModifier(0); // default x1

            // Search input → filter rows.
            if (_searchInput != null)
            {
                _searchInput.onValueChanged.RemoveAllListeners();
                _searchInput.onValueChanged.AddListener(ApplySearchFilter);
            }

            // ToBank/FromBank/ToFromText — hidden until a Banker module is wired (memory: deferred).
            SetActive("Background/ToBank", false);
            SetActive("Background/FromBank", false);
            SetActive("Background/ToFromText", false);

            // Re-resolve KG's sprite references against live Valheim sprites (bundle bake lost them).
            ApplySpriteMap(_panelRoot.transform, PanelSpriteByPath);
        }

        private static int ApplySpriteMap(Transform root, Dictionary<string, string> map)
        {
            // Runtime FALLBACK — only fires for Images that lost their sprite during bake (shouldn't happen
            // now that KgUiExtractFull packages KG's sprites into firesnpcsui, but kept as safety net).
            int applied = 0, missing = 0, noPath = 0, noImage = 0, hadSprite = 0;
            foreach (var kv in map)
            {
                var t = root.Find(kv.Key);
                if (t == null) { noPath++; continue; }
                var img = t.GetComponent<Image>();
                if (img == null) { noImage++; continue; }
                if (img.sprite != null) { hadSprite++; continue; }
                var sprite = CodegenUIWiring.ResolveSpriteByNamePublic(kv.Value);
                if (sprite != null) { img.sprite = sprite; applied++; }
                else missing++;
            }
            Debug.Log($"[TraderUIController] sprite map: had-valid={hadSprite}, applied-fallback={applied}, missing={missing}, no-path={noPath}, no-image={noImage} (of {map.Count})");
            return applied;

            // Close button (top-right of KG's panel) — KG didn't bake one; let Esc / OnDestroy handle close.
        }

        private void SetActive(string path, bool active)
        {
            var t = _panelRoot.transform.Find(path);
            if (t != null) t.gameObject.SetActive(active);
        }

        // ─────────────── Populate ───────────────
        private void PopulateUI(string npcDisplayName)
        {
            ClearRows();
            if (_currentProfile == null) return;

            int coins = CountPlayerItems("Coins", 0);
            if (_headerText != null) _headerText.text = $"{npcDisplayName}    {coins} Coins";
            if (_searchInput != null) { _searchInput.text = ""; }

            foreach (var trade in _currentProfile.Trades) AddTradeRow(trade);
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            RefreshAllRowVisuals();
        }

        /// <summary>Clones one TraderInstantiateV3 + populates its NeededItem/ResultItem slots from the trade.</summary>
        private void AddTradeRow(TradeEntry trade)
        {
            if (_scrollContent == null || _rowTemplate == null) return;
            var row = Instantiate(_rowTemplate, _scrollContent, false);
            row.name = $"Row_{trade.GetHashCode()}";
            row.SetActive(true);
            ApplySpriteMap(row.transform, RowSpriteByPath);

            var bg = row.transform.Find("Background");
            if (bg == null) { Destroy(row); return; }

            // Locate templates inside the row (one NeededItem + one ResultItem ship in the prefab — we
            // reuse those for index 0, then clone for indices 1..N).
            var neededList  = bg.Find("NeededItems");
            var resultList  = bg.Find("ResultItems");
            var conversion  = bg.Find("Conversion");

            var neededTemplate = neededList?.Find("NeededItem");
            var resultTemplate = resultList?.Find("ResultItem");

            // Populate cost slots.
            string costsKey = PopulateItemSlots(neededList, neededTemplate, trade.CostItems);
            // Populate reward slots.
            string rewardsKey = PopulateItemSlots(resultList, resultTemplate, trade.RewardItems);

            // Conversion button → execute the trade (confirm dialog first).
            Button convBtn = null;
            Image convArrow = null;
            if (conversion != null)
            {
                convBtn = conversion.GetComponent<Button>() ?? conversion.gameObject.AddComponent<Button>();
                convArrow = conversion.Find("Conversion")?.GetComponent<Image>();
                var captured = trade;
                convBtn.onClick.RemoveAllListeners();
                convBtn.onClick.AddListener(() => ShowTradeConfirmation(captured));
            }

            // Stock / restock badge (top-right of the row Background; ∞ = no badge).
            var badge = CreateStockBadge(bg);

            _rows.Add(new RowRefs
            {
                Trade = trade,
                Root = row,
                ConversionButton = convBtn,
                ConversionArrow = convArrow,
                StockBadge = badge,
                SearchKey = (costsKey + " " + rewardsKey).ToLowerInvariant(),
            });
        }

        /// <summary>Adds one item slot per stack — first uses the existing template, the rest are cloned.</summary>
        private string PopulateItemSlots(Transform listRoot, Transform template, List<TradeItemStack> stacks)
        {
            if (listRoot == null || template == null || stacks == null || stacks.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                Transform slot = i == 0 ? template : Instantiate(template.gameObject, listRoot, false).transform;
                slot.gameObject.SetActive(true);
                slot.name = i == 0 ? template.name : $"{template.name}_{i}";

                ResolveItemInfo(stack.PrefabName, out string itemName, out Sprite icon);
                if (stack.Level > 0) itemName += $" ★{stack.Level - 1}";
                sb.Append(itemName).Append(' ');

                int totalQty = stack.Amount * _modifier;
                var textT = slot.Find("Text")?.GetComponent<Text>();
                if (textT != null) textT.text = totalQty > 1 ? $"x{totalQty}  {itemName}" : itemName;

                var iconImg = slot.Find("Icon")?.GetComponent<Image>();
                if (iconImg != null)
                {
                    iconImg.sprite = icon;
                    iconImg.enabled = icon != null;
                }

                // UITooltip — try to populate the topic so vanilla hover-tooltip works (best-effort; the
                // baked template carries the UITooltip component but no text).
                var tooltip = slot.GetComponent<UITooltip>();
                if (tooltip != null)
                {
                    try { tooltip.m_topic = itemName; tooltip.m_text = $"{itemName}\nNeed: x{totalQty}\n(owned: {CountPlayerItems(stack.PrefabName, stack.Level)})"; } catch { }
                }
            }
            return sb.ToString();
        }

        private TMP_Text CreateStockBadge(Transform bg)
        {
            var go = new GameObject("StockBadge", typeof(RectTransform));
            go.transform.SetParent(bg, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-8f, -4f);
            rt.sizeDelta = new Vector2(160f, 24f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 18f;
            tmp.color = BADGE_OK;
            tmp.alignment = TextAlignmentOptions.TopRight;
            tmp.raycastTarget = false;
            tmp.text = "";
            try { tmp.font = TMP_Settings.defaultFontAsset; } catch { }
            return tmp;
        }

        private void RefreshAllRowVisuals()
        {
            int coins = CountPlayerItems("Coins", 0);
            if (_headerText != null && _currentNpc != null) _headerText.text = $"{_currentNpc.GetDisplayName()}    {coins} Coins";

            foreach (var r in _rows) RefreshRowVisual(r);
        }

        private void RefreshRowVisual(RowRefs r)
        {
            if (r.Trade == null || r.Root == null) return;
            bool inStock = r.Trade.Stock != 0;
            bool canAfford = CanAffordTrade(r.Trade, _modifier);
            bool can = inStock && canAfford;

            if (r.ConversionButton != null) r.ConversionButton.interactable = can;
            if (r.ConversionArrow != null) r.ConversionArrow.color = can ? CONV_OK : CONV_NO;
            if (r.StockBadge != null)
            {
                if (!inStock) { r.StockBadge.text = "Sold Out"; r.StockBadge.color = BADGE_OUT; }
                else if (r.Trade.Stock < 0) { r.StockBadge.text = ""; r.StockBadge.color = BADGE_OK; }
                else { r.StockBadge.text = $"Stock: {r.Trade.Stock}" + (r.Trade.RestockSeconds > 0 ? $"  ({FormatTime(r.Trade.RestockSeconds)})" : ""); r.StockBadge.color = BADGE_OK; }
            }
        }

        private void ClearRows()
        {
            foreach (var r in _rows) if (r.Root != null) Destroy(r.Root);
            _rows.Clear();
        }

        // ─────────────── Modifier + Search ───────────────
        private void SetModifier(int idx)
        {
            idx = Mathf.Clamp(idx, 0, ModifierValues.Length - 1);
            _modifier = ModifierValues[idx];
            for (int i = 0; i < _modifierBgs.Length; i++)
            {
                bool selected = i == idx;
                if (_modifierTexts[i] != null) _modifierTexts[i].color = selected ? Color.green : Color.white;
                if (_modifierBgs[i] != null) _modifierBgs[i].color = selected ? new Color(0.5f, 0.85f, 0.5f, 0.8f) : new Color(0.613f, 0.613f, 0.613f, 1f);
            }
            // Re-populate so item-quantity text reflects the new multiplier (cheaper than walking each cell).
            if (_currentProfile != null && _scrollContent != null)
            {
                ClearRows();
                foreach (var trade in _currentProfile.Trades) AddTradeRow(trade);
                if (!string.IsNullOrEmpty(_searchInput?.text)) ApplySearchFilter(_searchInput.text);
                RefreshAllRowVisuals();
            }
        }

        private void ApplySearchFilter(string filter)
        {
            string f = (filter ?? "").Trim().ToLowerInvariant();
            foreach (var r in _rows)
            {
                if (r.Root == null) continue;
                bool show = f.Length == 0 || (r.SearchKey != null && r.SearchKey.Contains(f));
                r.Root.SetActive(show);
            }
        }

        // ─────────────── Confirm dialog ───────────────
        private void ShowTradeConfirmation(TradeEntry trade)
        {
            if (!CanAffordTrade(trade, _modifier)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!"); return; }

            // Build a multi-line "give X / receive Y" message.
            var giveLines = trade.CostItems.Select(s => FormatStackLine(s, _modifier));
            var getLines  = trade.RewardItems.Select(s => FormatStackLine(s, _modifier));
            string msg = "Give:\n  " + string.Join("\n  ", giveLines) + "\n\nReceive:\n  " + string.Join("\n  ", getLines);

            EnsureConfirmDialog();
            _confirmText.text = msg;
            _pendingTradeAction = () => ExecuteTrade(trade);
            _confirmRoot.SetActive(true);
        }

        private static string FormatStackLine(TradeItemStack s, int mult)
        {
            ResolveItemInfo(s.PrefabName, out string name, out _);
            if (s.Level > 0) name += $" ★{s.Level - 1}";
            int total = s.Amount * mult;
            return total > 1 ? $"x{total}  {name}" : name;
        }

        private void EnsureConfirmDialog()
        {
            if (_confirmRoot != null) return;

            _confirmRoot = new GameObject("TradeConfirmDialog", typeof(RectTransform));
            _confirmRoot.transform.SetParent(_panelRoot.transform, false);
            var rootRect = (RectTransform)_confirmRoot.transform;
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one; rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;

            // Dim background blocks clicks behind the dialog.
            var dim = new GameObject("DimBG", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(_confirmRoot.transform, false);
            var dr = (RectTransform)dim.transform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = Vector2.zero; dr.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = new GameObject("DialogBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(_confirmRoot.transform, false);
            var bRt = (RectTransform)box.transform; bRt.anchorMin = new Vector2(0.5f, 0.5f); bRt.anchorMax = new Vector2(0.5f, 0.5f); bRt.sizeDelta = new Vector2(420f, 260f);
            box.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.08f, 0.96f);

            var textGo = new GameObject("ConfirmText", typeof(RectTransform));
            textGo.transform.SetParent(box.transform, false);
            var tRt = (RectTransform)textGo.transform; tRt.anchorMin = new Vector2(0.05f, 0.30f); tRt.anchorMax = new Vector2(0.95f, 0.95f); tRt.offsetMin = Vector2.zero; tRt.offsetMax = Vector2.zero;
            _confirmText = textGo.AddComponent<TextMeshProUGUI>();
            _confirmText.fontSize = 18f; _confirmText.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            _confirmText.alignment = TextAlignmentOptions.Top; _confirmText.textWrappingMode = TextWrappingModes.Normal;
            try { _confirmText.font = TMP_Settings.defaultFontAsset; } catch { }

            CreateConfirmButton(box.transform, "Confirm", new Vector2(-70f, -100f), new Color(0.15f, 0.45f, 0.15f, 1f), () =>
            {
                _confirmRoot.SetActive(false);
                _pendingTradeAction?.Invoke();
                _pendingTradeAction = null;
            });
            CreateConfirmButton(box.transform, "Cancel", new Vector2(70f, -100f), new Color(0.50f, 0.18f, 0.12f, 1f), () =>
            {
                _confirmRoot.SetActive(false);
                _pendingTradeAction = null;
            });
            _confirmRoot.SetActive(false);
        }

        private static GameObject CreateConfirmButton(Transform parent, string label, Vector2 pos, Color bgColor, Action onClick)
        {
            var go = new GameObject(label + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(120f, 36f); rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = bgColor;
            go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 18f; tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            try { tmp.font = TMP_Settings.defaultFontAsset; } catch { }
            return go;
        }

        // ─────────────── Trade logic (preserved verbatim from the prior controller) ───────────────
        private bool CanAffordTrade(TradeEntry trade, int multiplier)
        {
            if (trade == null || trade.Stock == 0) return false;
            foreach (var cost in trade.CostItems)
                if (CountPlayerItems(cost.PrefabName, cost.Level) < cost.Amount * multiplier) return false;
            return true;
        }

        private void ExecuteTrade(TradeEntry trade)
        {
            int qty = _modifier;
            if (!CanAffordTrade(trade, qty)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough items!"); return; }

            var player = Player.m_localPlayer;
            if (player == null) return;
            var inventory = player.GetInventory();

            foreach (var cost in trade.CostItems)
                if (!RemovePlayerItems(inventory, cost.PrefabName, cost.Amount * qty, cost.Level)) return;
            foreach (var reward in trade.RewardItems)
                GivePlayerItems(player, reward.PrefabName, reward.Amount * qty, reward.Level);

            if (trade.Stock > 0) trade.Stock = Mathf.Max(0, trade.Stock - qty);
            RefreshAllRowVisuals();
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
                for (int i = 0; i < amount; i++)
                {
                    var spawnPos = player.transform.position + player.transform.forward * 2f + Vector3.up;
                    var creature = Instantiate(prefab, spawnPos, Quaternion.identity);
                    if (level > 0) creature.GetComponent<Character>()?.SetLevel(level);
                    creature.GetComponent<Tameable>()?.Tame();
                }
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

        private static bool IsCurrencyStack(TradeItemStack stack)
            => stack != null && string.Equals(stack.PrefabName, "Coins", StringComparison.OrdinalIgnoreCase);

        private static string FormatTime(int seconds)
        {
            if (seconds < 60) return $"{seconds}s";
            int minutes = seconds / 60;
            if (minutes < 60) return $"{minutes}m";
            int hours = minutes / 60; int mins = minutes % 60;
            return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }
    }

    // Backward-compat helper for any callers that may still pass through a drag handler.
    // (The KG prefab provides its own draggable Background; we don't add a custom drag handler here.)
}
