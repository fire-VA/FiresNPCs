using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FiresRPGmaker.Managers;
using FiresCore.Npc;
using FiresRPGmaker.Modules.Companions.NpcMode;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.NpcFashion;
using FiresRPGmaker.UI.Screens;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Main controller for NPC interaction UI panel.
    /// Starts on a "main" configuration screen, tabs navigate to type-specific screens.
    /// </summary>
    public class NpcMainPanelController : MonoBehaviour
    {
        public static NpcMainPanelController Instance { get; private set; }
        
        /// <summary>
        /// Returns true if the NPC config panel is currently open.
        /// </summary>
        public static bool IsOpen => Instance != null && Instance.gameObject.activeSelf;

        [Header("UI References")]
        public Transform contentParent;
     public GameObject npcEntryPrefab;
        public GameObject dialogueEntryPrefab;
   public GameObject questEntryPrefab;
 public TMP_Text headerText;
        public Button closeButton;
        public Button backButton;
        public GameObject detailPanel;
        public GameObject loadingIndicator;

        [Header("Tab Buttons - NPC Types")]
        public Button questNpcTabButton;
        public Button infoNpcTabButton;
        public Button dialogueNpcTabButton;
        public Button traderTabButton;

        [Header("Configuration Fields")]
        public TMP_InputField nameOverrideInput;
        public TMP_InputField modelOverrideInput;
        public TMP_InputField profileInput;
        public TMP_Dropdown profileDropdown;
        public Button applyButton;

        [Header("Pagination")]
        public Button prevPageButton;
        public Button nextPageButton;
        public TMP_Text pageText;

       [Header("Search")]
        public TMP_InputField searchInput;

        [Header("Size Control")]
        public Button sizeButton;
     public TMP_Text sizeText;

        // Type-specific screen controllers
        private InfoNpcScreenController _infoScreenController;
        private QuestConfigScreenController _questConfigScreenController;
        private TraderConfigScreenController _traderConfigScreenController;
        private BankerConfigScreenController _bankerConfigScreenController;
        private Button _bankerTabButton;

        // Extra Marketplace-module tabs (Gambler, Marketplace, Achievement, Leaderboard, Mail, Transmog).
        // All v1-stubs that share GenericModuleConfigScreenController; tabs are stacked in a 2nd row to
        // keep the existing 5-tab row from blowing out horizontally. Dictionary keyed by NpcType so the
        // RefreshUI / Hide / display-name code dispatches uniformly.
        private readonly Dictionary<NpcType, Screens.GenericModuleConfigScreenController> _moduleScreens = new();
        private readonly Dictionary<NpcType, Button> _moduleTabs = new();
        private Transform _tabsRow2; // sibling of TabsBar, holds the new tab clones

        /// <summary>Creates a 5th tab button "Banker Npc" by cloning the Trader tab so its style/anchoring
        /// matches the baked tabs (the codegen prefab pre-bakes only Quest/Info/Dialogue/Trader). Idempotent.</summary>
        private void EnsureBankerTabButton()
        {
            if (_bankerTabButton != null || traderTabButton == null) return;
            var clone = Instantiate(traderTabButton.gameObject, traderTabButton.transform.parent);
            clone.name = "BankerNpcTabButton";
            _bankerTabButton = clone.GetComponent<Button>();
            if (_bankerTabButton != null)
            {
                _bankerTabButton.onClick.RemoveAllListeners();
                _bankerTabButton.onClick.AddListener(() => OnTabClicked(NpcType.Banker));
            }
            foreach (var tmp in clone.GetComponentsInChildren<TMPro.TMP_Text>(true)) tmp.text = "Banker Npc";
            foreach (var txt in clone.GetComponentsInChildren<UnityEngine.UI.Text>(true)) txt.text = "Banker Npc";
            // If the tab bar uses a HorizontalLayoutGroup, the clone auto-positions. If not, nudge it right
            // of the trader tab by its own width so it lands as the 5th column visually.
            var parent = clone.transform.parent;
            if (parent != null && parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() == null)
            {
                var srcRt = traderTabButton.GetComponent<RectTransform>();
                var dstRt = clone.GetComponent<RectTransform>();
                if (srcRt != null && dstRt != null) dstRt.anchoredPosition = srcRt.anchoredPosition + new Vector2(srcRt.sizeDelta.x + 4f, 0f);
            }
            Debug.Log("[NpcMainPanelController] Banker tab button created programmatically");
        }

        // Single source of truth for the 6 v1-stub modules. Order = visual order in the 2nd tab row.
        // Adding a new module = add an entry here + a corresponding ScreenMode case in PanelElementManager.
        private static readonly (NpcType Type, string FieldName, string Label,
                                 string ScreenName, string ApplyName, string TabLabel)[] EXTRA_MODULES =
        {
            (NpcType.Gambler,     "gamblerProfile",     "Gambler",     "GamblerConfigScreen",     "GamblerApplyButton",     "Gambler Npc"),
            (NpcType.Marketplace, "marketplaceProfile", "Marketplace", "MarketplaceConfigScreen", "MarketplaceApplyButton", "Marketplace Npc"),
            (NpcType.Achievement, "achievementProfile", "Achievement", "AchievementConfigScreen", "AchievementApplyButton", "Achievement Npc"),
            (NpcType.Leaderboard, "leaderboardProfile", "Leaderboard", "LeaderboardConfigScreen", "LeaderboardApplyButton", "Leaderboard Npc"),
            (NpcType.Mail,        "mailProfile",        "Mail",        "MailConfigScreen",        "MailApplyButton",        "Mail Npc"),
            (NpcType.Transmog,    "transmogProfile",    "Transmog",    "TransmogConfigScreen",    "TransmogApplyButton",    "Transmog Npc"),
        };

        /// <summary>Wires the 6 generic module screens + clones a tab button per module into a 2nd tab row.</summary>
        private void EnsureExtraTabsAndScreens()
        {
            foreach (var m in EXTRA_MODULES)
            {
                if (!_moduleScreens.ContainsKey(m.Type))
                {
                    _moduleScreens[m.Type] = CodegenUIWiring.WireGenericModuleConfigScreen(
                        transform, m.Type, m.FieldName, m.Label, m.ScreenName, m.ApplyName);
                }

                if (!_moduleTabs.ContainsKey(m.Type))
                {
                    var btn = CreateExtraTab(m.Type, m.TabLabel);
                    if (btn != null) _moduleTabs[m.Type] = btn;
                }
            }
            Debug.Log("[NpcMainPanelController] Wired " + _moduleScreens.Count + " extra module screens + " + _moduleTabs.Count + " extra tabs");
        }

        /// <summary>Returns the (lazily-created) 2nd tab-row Transform — a sibling of TabsBar placed directly below it.</summary>
        private Transform EnsureTabsRow2()
        {
            if (_tabsRow2 != null) return _tabsRow2;
            if (traderTabButton == null) return transform;
            var tabsBar = traderTabButton.transform.parent;
            if (tabsBar == null) return transform;

            var rowGo = new GameObject("TabsBarRow2", typeof(RectTransform));
            rowGo.transform.SetParent(tabsBar.parent, false);
            var rrt = rowGo.GetComponent<RectTransform>();
            var tbrt = tabsBar.GetComponent<RectTransform>();
            if (tbrt != null)
            {
                rrt.anchorMin = tbrt.anchorMin;
                rrt.anchorMax = tbrt.anchorMax;
                rrt.pivot = tbrt.pivot;
                // Match width, shift down by row height + a small gap so the rows visually stack
                float rowH = Mathf.Max(28f, tbrt.rect.height);
                rrt.offsetMin = new Vector2(tbrt.offsetMin.x, tbrt.offsetMin.y - rowH - 4f);
                rrt.offsetMax = new Vector2(tbrt.offsetMax.x, tbrt.offsetMax.y - rowH - 4f);
            }
            else
            {
                rrt.anchorMin = new Vector2(0f, 0.78f); rrt.anchorMax = new Vector2(1f, 0.86f);
                rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            }

            // Mirror the original tab row's HLG so child tabs auto-distribute the same way.
            var srcHlg = tabsBar.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            var hlg = rowGo.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (srcHlg != null)
            {
                hlg.spacing = srcHlg.spacing; hlg.padding = srcHlg.padding;
                hlg.childControlWidth = srcHlg.childControlWidth; hlg.childControlHeight = srcHlg.childControlHeight;
                hlg.childForceExpandWidth = srcHlg.childForceExpandWidth; hlg.childForceExpandHeight = srcHlg.childForceExpandHeight;
                hlg.childAlignment = srcHlg.childAlignment;
            }
            else
            {
                hlg.spacing = 4f; hlg.childControlWidth = true; hlg.childControlHeight = true;
                hlg.childForceExpandWidth = true; hlg.childAlignment = TextAnchor.MiddleCenter;
            }
            _tabsRow2 = rowGo.transform;
            return _tabsRow2;
        }

        /// <summary>Clones traderTabButton into the 2nd tab row, renames + relabels it, wires the click.</summary>
        private Button CreateExtraTab(NpcType type, string tabLabel)
        {
            if (traderTabButton == null) return null;
            var row = EnsureTabsRow2();
            var clone = Instantiate(traderTabButton.gameObject, row);
            clone.name = tabLabel.Replace(" ", "") + "TabButton";
            foreach (var tmp in clone.GetComponentsInChildren<TMPro.TMP_Text>(true)) tmp.text = tabLabel;
            foreach (var txt in clone.GetComponentsInChildren<UnityEngine.UI.Text>(true)) txt.text = tabLabel;
            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnTabClicked(type));
            }
            // Reset the cloned RectTransform's anchored position — the parent HLG positions the tab now,
            // not the inherited anchoredPosition (which could otherwise push the tab off-row).
            var rt = clone.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = Vector2.zero;
            return btn;
        }

        /// <summary>Maps an extra-module NpcType to its PanelElementManager screen mode.</summary>
        private static PanelElementManager.ScreenMode MapNpcTypeToScreenMode(NpcType type) => type switch
        {
            NpcType.Gambler     => PanelElementManager.ScreenMode.GamblerConfig,
            NpcType.Marketplace => PanelElementManager.ScreenMode.MarketplaceConfig,
            NpcType.Achievement => PanelElementManager.ScreenMode.AchievementConfig,
            NpcType.Leaderboard => PanelElementManager.ScreenMode.LeaderboardConfig,
            NpcType.Mail        => PanelElementManager.ScreenMode.MailConfig,
            NpcType.Transmog    => PanelElementManager.ScreenMode.TransmogConfig,
            _                   => PanelElementManager.ScreenMode.AdminConfig,
        };
        private DressingRoomScreenController _dressingRoomScreenController;
        private DialogueEditorScreenController _dialogueEditorScreenController;

        // Dressing room button (created programmatically)
        private Button _dressingRoomButton;

        // Idle wander toggle (created programmatically)
        private Toggle _idleWanderToggle;
        private GameObject _idleWanderToggleGO;

        // Runtime state
        private NpcController _currentNpc;
        private NpcType? _selectedNpcType = null;
        private int _currentPage;
        private int _currentMaxPage = 1;
        private const int MaxItemsPerPage = 10;
    private readonly List<GameObject> _currentGameObjects = new();

        // Search
        private string _searchValue = "";
     private int _selectedIndex = -1;

        // Configuration
        private static ConfigEntry<PanelSize> _panelSizeConfig;
        private static ConfigEntry<float> _panelPosXConfig;
        private static ConfigEntry<float> _panelPosYConfig;
        private static ConfigEntry<bool> _panelLockedConfig;
        private enum PanelSize { Large, Medium, Small }

        // Tab highlight colors - warm orange theme
      private static readonly Color HighlightBorderColor = new Color(0.8f, 0.5f, 0.2f, 0.9f); // Warm orange highlight
        private static readonly Color InactiveBorderColor = new Color(0.3f, 0.25f, 0.2f, 0.5f); // Muted warm inactive
        
        // Tab text colors - warm gold/orange
    private static readonly Color ActiveTabTextColor = new Color(1f, 0.95f, 0.7f); // Bright gold when active
        private static readonly Color InactiveTabTextColor = new Color(0.95f, 0.85f, 0.65f); // Warm cream when inactive ï¿½ readable on parchment

        // Tab highlight GameObjects (created at runtime)
        private readonly Dictionary<Button, GameObject> _tabHighlights = new();

      // Drag state
        private bool _isDragging;
  private Vector2 _dragOffset;
        private PanelDragHandler _dragHandler;

  // Initialization state
       private bool _initialized;
       private bool _fontsApplied;
       private bool _canvasVerified;

      private void Awake()
     {
  Instance = this;
            Debug.Log($"[NpcMainPanelController] Awake - Instance set. GameObject: {gameObject.name}");

   if (FiresRPGmaker.Instance != null)
      {
    if (_panelSizeConfig == null)
     _panelSizeConfig = FiresRPGmaker.Instance.Config.Bind("UI", "NPC Panel Size", PanelSize.Large, "Size of the NPC panel");
      if (_panelPosXConfig == null)
           _panelPosXConfig = FiresRPGmaker.Instance.Config.Bind("UI", "NPC Panel Position X", 0f, "X position of the NPC panel");
        if (_panelPosYConfig == null)
   _panelPosYConfig = FiresRPGmaker.Instance.Config.Bind("UI", "NPC Panel Position Y", 0f, "Y position of the NPC panel");
              if (_panelLockedConfig == null)
        _panelLockedConfig = FiresRPGmaker.Instance.Config.Bind("UI", "NPC Panel Locked", false, "Whether the panel position is locked");
            }

            ApplySize(false);
        }

        private void Start()
        {
     if (!_initialized)
       {
             Initialize();
            }
        }

        /// <summary>
        /// Public entry point to force initialization if it hasn't run yet.
        /// Called when quest screen needs the codegen hierarchy wired before Start() fires.
        /// </summary>
        public void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

        private void Initialize()
        {
    if (_initialized) return;
         _initialized = true;

            Debug.Log($"[NpcMainPanelController] Initialize - wiring button listeners");
       RewireButtonListeners();

   // Create tab highlights
     CreateTabHighlights();

 // Add section backgrounds for improved readability
   AddSectionBackgrounds();

// Create type-specific screens
   CreateTypeScreens();

     // Setup drag handler
      SetupDragHandler();

     // Don't set inactive here - let the caller control visibility
     // The calling code (ShowForNpc or QuestInteractionScreenController.Show) will manage visibility
         // gameObject.SetActive(false);
    }

        /// <summary>
        /// Adds semi-transparent backgrounds behind various panel sections for improved readability.
        /// Skips sections that already have backgrounds from the codegen layout.
        /// </summary>
        private void AddSectionBackgrounds()
        {
            // NOTE: Do NOT add a HeaderBackground ï¿½ the Header rect is only 10px tall
            // and an inner background extends above it, creating a visible dark bar
            // between the pagination row and the tab buttons.

            // NOTE: Do NOT add backgrounds to TabsBar ï¿½ it has a HorizontalLayoutGroup
            // and any child becomes a layout element, pushing the real tab buttons.

            // Add background behind detail panel (only if not already present)
            if (detailPanel != null && detailPanel.transform.Find("DetailPanelBackground") == null)
            {
                UIBuilderHelper.AddInnerBackground(detailPanel.transform, UIFontConfig.Colors.FormSectionBackground, "DetailPanelBackground");
                Debug.Log("[NpcMainPanelController] Added detail panel background");
            }

            // Add background behind search bar if it exists (only if not already present)
            if (searchInput != null)
            {
                var searchParent = searchInput.transform.parent;
                if (searchParent != null && searchParent.name != "Header" && searchParent.Find("SearchBackground") == null)
                {
                    UIBuilderHelper.AddInnerBackground(searchParent, UIFontConfig.Colors.SectionBackgroundLight, "SearchBackground");
                }
            }

            // Hide ALL non-essential children of PageControls (PaginationBackground panels, etc.)
            // and make the PageControls own Image fully transparent.
            var pageControls = transform.Find("PageControls");
            if (pageControls != null)
            {
                for (int i = 0; i < pageControls.childCount; i++)
                {
                    var child = pageControls.GetChild(i);
                    // Keep only PrevButton, NextButton, and PageText
                    if (child.name != "PrevButton" && child.name != "NextButton" && child.name != "PageText")
                        child.gameObject.SetActive(false);
                }

                // Ensure PageControls own Image is transparent
                var pcImg = pageControls.GetComponent<Image>();
                if (pcImg != null)
                    pcImg.color = new Color(0f, 0f, 0f, 0f);
            }

            // Remove any existing HeaderBackground that may have been created by older code
            var header = transform.Find("Header");
            if (header != null)
            {
                var oldHeaderBg = header.Find("HeaderBackground");
                if (oldHeaderBg != null)
                    Destroy(oldHeaderBg.gameObject);
            }
        }

        /// <summary>
        /// Clamps background children so they don't extend beyond their parent's bounds.
        /// The codegen may capture editor offsets that push backgrounds above/below the parent rect.
        /// </summary>
        private static void FixOverflowingBackgrounds(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.name.Contains("Background")) continue;

                var rect = child.GetComponent<RectTransform>();
                if (rect == null) continue;

                // If stretch-fill (anchor 0?1), clamp offsets so they don't extend outside parent
                if (rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one)
                {
                    var oMin = rect.offsetMin;
                    var oMax = rect.offsetMax;
                    // offsetMax.y > 0 means the top edge extends ABOVE the parent
                    if (oMax.y > 0) oMax.y = 0;
                    // offsetMin.y < 0 means the bottom edge extends BELOW the parent
                    if (oMin.y < 0) oMin.y = 0;
                    rect.offsetMin = oMin;
                    rect.offsetMax = oMax;
                }
            }
        }

   /// <summary>
/// Creates the type-specific screen controllers.
        /// </summary>
   private void CreateTypeScreens()
{
      // Wire Info NPC screen from codegen hierarchy
      _infoScreenController = CodegenUIWiring.WireInfoScreen(transform);

      // Wire Quest Config screen from codegen hierarchy
      _questConfigScreenController = CodegenUIWiring.WireQuestConfigScreen(transform);

      // Wire Dressing Room screen from codegen hierarchy
      _dressingRoomScreenController = CodegenUIWiring.WireDressingRoomScreen(transform);

   // Wire Dialogue Editor screen from codegen hierarchy
   _dialogueEditorScreenController = CodegenUIWiring.WireDialogueEditorScreen(transform);

   // Wire Trader Config screen (created programmatically; no baked trader screen yet)
   _traderConfigScreenController = CodegenUIWiring.WireTraderConfigScreen(transform);
   _bankerConfigScreenController = CodegenUIWiring.WireBankerConfigScreen(transform);
   EnsureBankerTabButton();

   // Wire the 6 v1-stub Marketplace-module screens + their tab buttons (Gambler, Marketplace,
   // Achievement, Leaderboard, Mail, Transmog). Tabs go in a 2nd row so the existing one isn't crammed.
   EnsureExtraTabsAndScreens();

   // Pre-create Quest Interaction screen via codegen wiring
   CodegenUIWiring.WireQuestInteractionScreen(transform);

   Debug.Log("[NpcMainPanelController] All type-specific screens wired from codegen hierarchy");
  }

        private void OnDestroy()
      {
  if (Instance == this)
              Instance = null;

            // Clean up highlights
    foreach (var highlight in _tabHighlights.Values)
            {
       if (highlight != null)
     Destroy(highlight);
   }
            _tabHighlights.Clear();
    }

        private void Update()
        {
            if (!gameObject.activeSelf) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // If quest interaction screen is visible, let it handle its own ESC
                if (QuestInteractionScreenController.Instance != null && QuestInteractionScreenController.Instance.IsVisible)
                    return;

                // If dressing room is visible, close it and return to main
                if (_dressingRoomScreenController != null && _dressingRoomScreenController.IsVisible)
                {
                    Debug.Log("[NpcMainPanelController] Escape key pressed - returning from dressing room");
                    ReturnFromDressingRoom();
                    return;
                }

                // If dialogue editor is visible, close it and return to main
                if (_dialogueEditorScreenController != null && _dialogueEditorScreenController.IsVisible)
                {
                    Debug.Log("[NpcMainPanelController] Escape key pressed - returning from dialogue editor");
                    _dialogueEditorScreenController.Hide();
                    GoToMainScreen();
                    return;
                }

                // If on a tab screen, go back to main. If on main, close.
                if (_selectedNpcType.HasValue)
       {
   Debug.Log("[NpcMainPanelController] Escape key pressed - returning to main screen");
   GoToMainScreen();
          }
      else
     {
        Debug.Log("[NpcMainPanelController] Escape key pressed - hiding panel");
     Hide();
  }
    }
   }

        #region Screen Navigation

public void GoToMainScreen()
        {
            Debug.Log("[NpcMainPanelController] Navigating to main screen");
        _selectedNpcType = null;
            _currentPage = 0;
            _selectedIndex = -1;
 RefreshUI();
        }

        public void GoToTypeScreen(NpcType npcType)
        {
            Debug.Log($"[NpcMainPanelController] Navigating to {npcType} screen");
     _selectedNpcType = npcType;
  _currentPage = 0;
      _selectedIndex = -1;
            RefreshUI();
    }

     public bool IsOnMainScreen => !_selectedNpcType.HasValue;

        #endregion

        #region Drag Handling

      private void SetupDragHandler()
        {
            // Primary drag target: the Header bar (the entire title area).
            // This is the most intuitive target ï¿½ users expect to drag from the title bar.
            Transform headerT = transform.Find("Header");
            if (headerT != null)
            {
                var headerDragGO = headerT.gameObject;
                // Ensure the Header Image has raycastTarget so it receives drag events
                var headerImg = headerDragGO.GetComponent<Image>();
                if (headerImg != null) headerImg.raycastTarget = true;
                _dragHandler = headerDragGO.GetComponent<PanelDragHandler>();
                if (_dragHandler == null)
                    _dragHandler = headerDragGO.AddComponent<PanelDragHandler>();
                _dragHandler.Initialize(this);
                Debug.Log("[NpcMainPanelController] Drag handler attached to Header");
            }
            else
            {
                // Fallback: SizeButton (nested inside TabsBar)
                Transform sizeT = transform.Find("TabsBar/SizeButton") ?? transform.Find("SizeButton");
                if (sizeT != null)
                {
                    var dragTargetGO = sizeT.gameObject;
                    _dragHandler = dragTargetGO.GetComponent<PanelDragHandler>();
                    if (_dragHandler == null)
                        _dragHandler = dragTargetGO.AddComponent<PanelDragHandler>();
                    _dragHandler.Initialize(this);
                    Debug.Log("[NpcMainPanelController] Drag handler attached to SizeButton (fallback)");
                }
                else
                {
                    Debug.LogWarning("[NpcMainPanelController] Neither Header nor SizeButton found ï¿½ drag disabled");
                }
            }

            // Also wire the close button directly in case WireMainPanel missed it.
            if (closeButton == null)
            {
                var closeBtnT = FindDeep(transform, "CloseButton");
                if (closeBtnT != null)
                {
                    var btn = closeBtnT.GetComponent<Button>();
                    if (btn != null)
                    {
                        closeButton = btn;
                        closeButton.onClick.RemoveAllListeners();
                        closeButton.onClick.AddListener(OnCloseButtonClicked);
                        Debug.Log("[NpcMainPanelController] Close button wired via fallback in SetupDragHandler");
                    }

                    // Kill _ClickOverlay raycast ï¿½ it sits on top of the button and eats clicks
                    var overlay = closeBtnT.Find("_ClickOverlay");
                    if (overlay != null)
                    {
                        var img = overlay.GetComponent<Image>();
                        if (img != null) img.raycastTarget = false;
                    }
                }
            }
            else
            {
                // Close button already wired ï¿½ still kill the overlay
                var overlay = closeButton.transform.Find("_ClickOverlay");
                if (overlay != null)
                {
                    var img = overlay.GetComponent<Image>();
                    if (img != null) img.raycastTarget = false;
                }
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public void OnDragStart(Vector2 mousePosition)
        {
  if (_panelLockedConfig?.Value == true) return;

            _isDragging = true;
            var rectTransform = GetComponent<RectTransform>();
   if (rectTransform != null)
       {
         _dragOffset = (Vector2)rectTransform.position - mousePosition;
            }
        }

        public void OnDrag(Vector2 mousePosition)
      {
            if (!_isDragging || _panelLockedConfig?.Value == true) return;

        var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
      {
   rectTransform.position = mousePosition + _dragOffset;
    }
        }

        public void OnDragEnd()
        {
            if (!_isDragging) return;

       _isDragging = false;
            SavePanelPosition();
        }

        private void SavePanelPosition()
        {
            var rectTransform = GetComponent<RectTransform>();
       if (rectTransform == null) return;

  if (_panelPosXConfig != null && _panelPosYConfig != null)
            {
       _panelPosXConfig.Value = rectTransform.anchoredPosition.x;
           _panelPosYConfig.Value = rectTransform.anchoredPosition.y;
        FiresRPGmaker.Instance?.Config.Save();
                Debug.Log($"[NpcMainPanelController] Panel position saved: ({_panelPosXConfig.Value}, {_panelPosYConfig.Value})");
   }
   }

        private void LoadPanelPosition()
        {
            var rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null) return;

      if (_panelPosXConfig != null && _panelPosYConfig != null)
          {
   // Always load saved position - even if 0,0 (which is a valid center position)
             // Only skip if this is the very first time (config just created with defaults)
  var savedPos = new Vector2(_panelPosXConfig.Value, _panelPosYConfig.Value);
        rectTransform.anchoredPosition = savedPos;
      Debug.Log($"[NpcMainPanelController] Panel position loaded: ({_panelPosXConfig.Value}, {_panelPosYConfig.Value})");
        }
        }

     public void ToggleLock()
    {
            if (_panelLockedConfig != null)
  {
     _panelLockedConfig.Value = !_panelLockedConfig.Value;
        FiresRPGmaker.Instance?.Config.Save();

           string status = _panelLockedConfig.Value ? "locked" : "unlocked";
           MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Panel position {status}");
          Debug.Log($"[NpcMainPanelController] Panel position {status}");
 }
        }

    public void ResetPosition()
        {
var rectTransform = GetComponent<RectTransform>();
      if (rectTransform != null)
  {
         rectTransform.anchoredPosition = Vector2.zero;
   SavePanelPosition();
   MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Panel position reset");
            }
 }

        #endregion

      #region Tab Highlight System

 private void CreateTabHighlights()
      {
            CreateTabHighlight(questNpcTabButton, 4f);
            CreateTabHighlight(infoNpcTabButton, 4f);
            CreateTabHighlight(dialogueNpcTabButton, 4f);
            CreateTabHighlight(traderTabButton, 4f);

            Debug.Log($"[NpcMainPanelController] Created {_tabHighlights.Count} tab highlights");
 }

        private void CreateTabHighlight(Button tabButton, float extraLeftExpansion = 4f)
    {
          if (tabButton == null) return;

            if (_tabHighlights.ContainsKey(tabButton)) return;

       var highlightGO = new GameObject($"{tabButton.name}_Highlight");
      highlightGO.transform.SetParent(tabButton.transform, false);

var highlightRect = highlightGO.AddComponent<RectTransform>();
          highlightRect.anchorMin = Vector2.zero;
      highlightRect.anchorMax = Vector2.one;
            highlightRect.offsetMin = new Vector2(-extraLeftExpansion, -3);
          highlightRect.offsetMax = new Vector2(3, 3);

            var outlineImage = highlightGO.AddComponent<Image>();
            outlineImage.color = InactiveBorderColor;
            outlineImage.raycastTarget = false;

   outlineImage.type = Image.Type.Sliced;
        outlineImage.fillCenter = false;

    highlightGO.transform.SetAsFirstSibling();

            _tabHighlights[tabButton] = highlightGO;
        }

        private void UpdateTabHighlights()
      {
     bool onMain = IsOnMainScreen;

  SetTabHighlightActive(questNpcTabButton, !onMain && _selectedNpcType == NpcType.QuestNpc, 4f);
            SetTabHighlightActive(infoNpcTabButton, !onMain && _selectedNpcType == NpcType.InfoNpc, 4f);
            SetTabHighlightActive(dialogueNpcTabButton, !onMain && _selectedNpcType == NpcType.DialogueNpc, 4f);
    SetTabHighlightActive(traderTabButton, !onMain && _selectedNpcType == NpcType.Trader, 4f);
        }

   private void SetTabHighlightActive(Button tab, bool active, float baseLeftExpansion = 4f)
        {
       if (tab == null) return;

   if (_tabHighlights.TryGetValue(tab, out var highlightGO) && highlightGO != null)
            {
     var image = highlightGO.GetComponent<Image>();
       if (image != null)
     {
    image.color = active ? HighlightBorderColor : InactiveBorderColor;

        var rect = highlightGO.GetComponent<RectTransform>();
          if (rect != null)
          {
  float expansion = active ? 5f : 2f;
      float leftExpansion = baseLeftExpansion + (active ? 2f : 0f);
           rect.offsetMin = new Vector2(-leftExpansion, -expansion);
           rect.offsetMax = new Vector2(expansion, expansion);
            }
      }
            }

            var tabText = tab.GetComponentInChildren<TMP_Text>();
  if (tabText != null)
   {
       // Use warm gold/orange colors for tab text
                tabText.color = active ? ActiveTabTextColor : InactiveTabTextColor;
   tabText.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
 }
        }

        #endregion

    #region Public API

        public static void ShowForNpc(NpcController npc)
        {
            if (Instance == null)
            {
                Debug.LogWarning("[NpcMainPanelController] Instance is null - UI not initialized");
                return;
            }
            if (npc == null)
            {
                Debug.LogWarning("[NpcMainPanelController] Cannot show panel for null NPC");
                return;
            }

            Debug.Log($"[NpcMainPanelController] ShowForNpc called for: {npc.npcProfileName}");

            if (!Instance._initialized)
            {
                Instance.Initialize();
            }

            // IMPORTANT: Hide any quest interaction screen elements first
            // This ensures they don't appear when admin opens the config panel
            QuestInteractionScreenController.HideQuestScreenIfExists();

            Instance._currentNpc = npc;

            // Hide this NPC's world-space hover HUD (floating nameplate + health bar) while interacting.
            NpcHoverHudSuppressor.Clear();
            NpcHoverHudSuppressor.Suppress(npc.GetComponent<Character>() ?? npc.GetComponentInChildren<Character>());
            // SuppressAll so nearby NPC / Companion / wild-monster nameplates don't bleed through the panel.
            NpcHoverHudSuppressor.SuppressAll(true);

            Instance._selectedNpcType = null;
            Instance._currentPage = 0;
            Instance._searchValue = "";
            Instance._selectedIndex = -1;

            if (Instance.searchInput != null)
                Instance.searchInput.text = "";

            // Create dressing room button if it doesn't exist
            Instance.CreateDressingRoomButton();

            // Create idle wander toggle if it doesn't exist
            Instance.CreateIdleWanderToggle();

            // PERF: Configure admin mode BEFORE activating the panel so
            // PanelElementManager hides all quest/info/dialogue sub-trees first.
            // This prevents Unity from activating ~400 unnecessary elements.
            PanelElementManager.ConfigureForMode(Instance.transform, PanelElementManager.ScreenMode.AdminConfig, npc?.GetDisplayName() ?? "NPC");

            Instance.RefreshUI();

            if (!Instance.gameObject.activeSelf)
                Instance.gameObject.SetActive(true);

            // Apply fonts only once ï¿½ they don't change between interactions
            if (!Instance._fontsApplied)
            {
                Instance._fontsApplied = true;
                Instance.ApplyGameFontsToNonHeaders();
            }

            // Canvas parenting and visibility only need to be verified once
            if (!Instance._canvasVerified)
            {
                Instance._canvasVerified = true;
                Instance.EnsureCanvasSetup();
                Instance.EnsurePanelVisibility();
            }
            Instance.LoadPanelPosition();

            GUIManager.BlockInput(true);

            Debug.Log($"[NpcMainPanelController] Panel activated, active={Instance.gameObject.activeSelf}, " +
                $"position={Instance.transform.position}, localPosition={Instance.transform.localPosition}");
        }

        public void Hide()
        {
       Debug.Log("[NpcMainPanelController] Hide() called");
     
   // Hide any type-specific screens
  HideAllTypeScreens();
            
     // Close player inventory if it's open (dressing room may have opened it)
     try
          {
             if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
      InventoryGui.instance.Hide();
   Debug.Log("[NpcMainPanelController] Closed player inventory on panel hide");
           }
  }
    catch { }
      
 gameObject.SetActive(false);
            _currentNpc = null;
            NpcHoverHudSuppressor.Clear(); // restore the NPC's world hover HUD
  _selectedNpcType = null;
      _selectedIndex = -1;
  _isDragging = false;
      ClearContent();
        GUIManager.BlockInput(false);
}

        private void HideAllTypeScreens()
        {
     _infoScreenController?.Hide();
       _questConfigScreenController?.Hide();
   _dressingRoomScreenController?.Hide();
   _dialogueEditorScreenController?.Hide();
   _traderConfigScreenController?.Hide();
   _bankerConfigScreenController?.Hide();
   foreach (var screen in _moduleScreens.Values) screen?.Hide();

            // Also clean up quest interaction screen elements
            QuestInteractionScreenController.HideQuestScreenIfExists();
  }

      public void RefreshProfiles()
        {
      // No longer needed with text input, but keep for compatibility
      }

        public void SetLoading(bool loading)
  {
      if (loadingIndicator != null)
loadingIndicator.SetActive(loading);
        }

        /// <summary>
      /// Gets the current NPC being configured.
        /// </summary>
        public NpcController CurrentNpc => _currentNpc;

        #endregion

        #region Button Listener Wiring

        public void RewireButtonListeners()
   {
       Debug.Log($"[NpcMainPanelController] RewireButtonListeners - closeButton: {closeButton != null}, " +
     $"prevPageButton: {prevPageButton != null}, applyButton: {applyButton != null}");

        // Remove existing listeners
   closeButton?.onClick.RemoveAllListeners();
            applyButton?.onClick.RemoveAllListeners();
    questNpcTabButton?.onClick.RemoveAllListeners();
  infoNpcTabButton?.onClick.RemoveAllListeners();
            dialogueNpcTabButton?.onClick.RemoveAllListeners();
            traderTabButton?.onClick.RemoveAllListeners();
         prevPageButton?.onClick.RemoveAllListeners();
    nextPageButton?.onClick.RemoveAllListeners();
            sizeButton?.onClick.RemoveAllListeners();

         if (closeButton != null)
      closeButton.onClick.AddListener(OnCloseButtonClicked);

    if (applyButton != null)
   applyButton.onClick.AddListener(OnApplyButtonClicked);

  if (questNpcTabButton != null)
      questNpcTabButton.onClick.AddListener(() => OnTabClicked(NpcType.QuestNpc));

            if (infoNpcTabButton != null)
                infoNpcTabButton.onClick.AddListener(() => OnTabClicked(NpcType.InfoNpc));

       if (dialogueNpcTabButton != null)
       dialogueNpcTabButton.onClick.AddListener(() => OnTabClicked(NpcType.DialogueNpc));

    if (traderTabButton != null)
                traderTabButton.onClick.AddListener(() => OnTabClicked(NpcType.Trader));

            if (prevPageButton != null)
           prevPageButton.onClick.AddListener(OnPrevButtonClicked);

      if (nextPageButton != null)
      nextPageButton.onClick.AddListener(() => PageIncrementor(1));

            if (sizeButton != null)
         sizeButton.onClick.AddListener(OnSizeButtonClicked);

            if (searchInput != null)
  {
       searchInput.onValueChanged.RemoveAllListeners();
          searchInput.onValueChanged.AddListener(OnSearchChanged);
       }

            Debug.Log("[NpcMainPanelController] Button listeners rewired");
 }

        private void OnCloseButtonClicked()
     {
      Debug.Log("[NpcMainPanelController] Close button clicked!");
            Hide();
        }

private void OnPrevButtonClicked()
        {
   // If dressing room is visible, return from it
     if (_dressingRoomScreenController != null && _dressingRoomScreenController.IsVisible)
       {
  Debug.Log("[NpcMainPanelController] Prev button clicked - returning from dressing room");
      ReturnFromDressingRoom();
   return;
  }

       if (!IsOnMainScreen)
     {
     Debug.Log("[NpcMainPanelController] Prev button clicked - returning to main screen");
        GoToMainScreen();
   }
       else
            {
  Debug.Log("[NpcMainPanelController] Prev button clicked - paginating");
PageIncrementor(-1);
   }
        }

        private void OnApplyButtonClicked()
        {
            Debug.Log("[NpcMainPanelController] Apply button clicked!");
            ApplyNpcChanges();
        }

        private void OnTabClicked(NpcType type)
        {
     Debug.Log($"[NpcMainPanelController] Tab clicked: {type}");
       GoToTypeScreen(type);
        }

      private void OnSizeButtonClicked()
    {
  Debug.Log("[NpcMainPanelController] Size button clicked!");
       ChangeSize(true);
        }

        #endregion

        #region Size Control

     private void ChangeSize(bool cycle)
        {
  if (cycle && _panelSizeConfig != null)
            {
     var oldSize = _panelSizeConfig.Value;
     _panelSizeConfig.Value = _panelSizeConfig.Value switch
                {
        PanelSize.Small => PanelSize.Medium,
         PanelSize.Medium => PanelSize.Large,
          PanelSize.Large => PanelSize.Small,
          _ => PanelSize.Large
             };
                Debug.Log($"[NpcMainPanelController] Size changed: {oldSize} -> {_panelSizeConfig.Value}");
         }
      ApplySize(true);
 }

   private void ApplySize(bool save)
        {
          if (_panelSizeConfig == null) return;

            float scale = _panelSizeConfig.Value switch
     {
             PanelSize.Small => 0.75f,
      PanelSize.Medium => 0.9f,
                PanelSize.Large => 1f,
      _ => 1f
         };

            transform.localScale = new Vector3(scale, scale, 1f);

            if (sizeText != null)
       {
          sizeText.text = _panelSizeConfig.Value switch
       {
    PanelSize.Small => "S",
       PanelSize.Medium => "M",
          PanelSize.Large => "L",
            _ => "L"
   };
            }

            if (save)
 {
      FiresRPGmaker.Instance?.Config.Save();
       }
        }

 #endregion

        #region Font Loading

        /// <summary>
        /// Applies configured fonts to non-header text elements.
        /// Uses the Body font category from UIFontConfig for labels, inputs, and body text.
        /// This preserves UIFontConfig styling on headers while ensuring other text uses configured fonts.
        /// </summary>
        private void ApplyGameFontsToNonHeaders()
        {
            try
            {
                // Get the configured Body font for non-header text
                var bodyFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Body);
                
                // Fallback to Primary if Body isn't available
                if (bodyFont == null)
                    bodyFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Primary);
                
                // Last resort fallback to game font
                if (bodyFont == null)
                {
                    var gameTmp = Hud.instance?.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (gameTmp?.font != null)
                        bodyFont = gameTmp.font;
                }
                
                if (bodyFont == null) return;
                
                // Apply to all text elements EXCEPT the header
                foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                {
                    // Skip the header text - it uses UIFontConfig styling (Primary/Decorative category)
                    if (text == headerText) continue;
                    
                    // Skip any text that's part of the Header transform
                    if (text.transform.parent != null && 
                        (text.transform.parent.name == "Header" || 
                         text.transform.parent.parent?.name == "Header"))
                        continue;
                    
                    // Apply configured Body font to everything else
                    text.font = bodyFont;
                }
            }
            catch { }
        }

        /// <summary>
        /// Copies configured fonts to ALL text elements (legacy method - use ApplyGameFontsToNonHeaders instead).
        /// Uses the Primary font category from UIFontConfig.
        /// </summary>
        private void CopyGameFonts()
        {
            try
            {
                var font = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Primary);
                
                // Fallback to game font if config not available
                if (font == null)
                {
                    var gameTmp = Hud.instance?.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (gameTmp != null && gameTmp.font != null)
                        font = gameTmp.font;
                }
                
                if (font != null)
                {
                    foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                    {
                        text.font = font;
                    }
                }
            }
            catch { }
        }

    #endregion

        #region Search

        private void OnSearchChanged(string value)
        {
            _searchValue = value;
            _currentPage = 0;
    _selectedIndex = -1;
        }

        #endregion

#region UI Refresh

        private void RefreshUI()
        {
            ClearContent();

  // Determine current screen mode and configure elements accordingly
   string npcName = _currentNpc?.npcProfileName ?? "NPC";
     
     if (IsOnMainScreen)
  {
      // Admin main config screen
    PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.AdminConfig, npcName);
        LoadMainScreenSettings();
            }
       else if (_selectedNpcType.HasValue)
        {
      // Type-specific admin screens
   switch (_selectedNpcType.Value)
      {
       case NpcType.QuestNpc:
     PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.QuestConfig, npcName);
                   _questConfigScreenController?.Show(_currentNpc);
     break;
     
           case NpcType.InfoNpc:
   PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.InfoConfig, npcName);
  _infoScreenController?.Show(_currentNpc);
   break;
  
  case NpcType.DialogueNpc:
       PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.DialogueEditor, npcName);
       _dialogueEditorScreenController?.Show(_currentNpc);
       break;

       case NpcType.Trader:
                   PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.TraderConfig, npcName);
                   _traderConfigScreenController?.Show(_currentNpc);
                   break;

       case NpcType.Banker:
                   PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.BankerConfig, npcName);
                   _bankerConfigScreenController?.Show(_currentNpc);
                   break;

           default:
                   // Generic Marketplace-module stubs (Gambler/Marketplace/Achievement/Leaderboard/Mail/Transmog)
                   if (_moduleScreens.TryGetValue(_selectedNpcType.Value, out var moduleScreen))
                   {
                       PanelElementManager.ConfigureForMode(transform, MapNpcTypeToScreenMode(_selectedNpcType.Value), npcName);
                       moduleScreen?.Show(_currentNpc);
                   }
                   else
                   {
                       // Truly unhandled type — show standard admin panel
                       PanelElementManager.ConfigureForMode(transform, PanelElementManager.ScreenMode.AdminConfig, npcName);
                       LoadTypeScreenSettings();
                   }
                   break;
   }
            }

 // Update header based on current screen
    UpdateHeader();

            UpdatePagination();
    UpdateTabHighlights();
          ForceLayoutUpdate();
   }

    private void UpdateHeader()
        {
 if (headerText == null) return;

       string npcName = _currentNpc?.npcProfileName ?? "NPC";

  if (IsOnMainScreen)
      {
  headerText.text = $"Configure NPC: {npcName}";
     UIFontConfig.ApplyStyle(headerText, UIFontConfig.AdminPanelHeader);
            }
   else
 {
    string typeName = GetNpcTypeDisplayName(_selectedNpcType.Value);
        headerText.text = $"Setup {typeName}: {npcName}";
   UIFontConfig.ApplyStyle(headerText, UIFontConfig.AdminPanelHeader);
       }
        }

        private void UpdateBackButtonVisibility()
    {
       if (backButton != null)
       {
    backButton.gameObject.SetActive(true);
   }
        }

        private void ShowMainScreenElements()
        {
  // Show the standard detail panel with name/model/profile inputs
   if (detailPanel != null)
      {
detailPanel.SetActive(true);
            }

   // Show standard input fields
       SetStandardInputsVisible(true);
            
            // Show tabs and pagination
         ShowTabsAndPagination();
        }

        private void ShowTypeScreenElements()
{
            if (!_selectedNpcType.HasValue || _currentNpc == null) return;

          switch (_selectedNpcType.Value)
     {
     case NpcType.InfoNpc:
       // Hide standard inputs and show Info screen
          if (detailPanel != null)
       detailPanel.SetActive(true);
        SetStandardInputsVisible(false);
           _infoScreenController?.Show(_currentNpc);
    break;

                case NpcType.QuestNpc:
   // Hide detail panel content, show Quest Config screen
          // Keep: Header, CloseButton, SizeButton, Background, drag handling
          // ALSO KEEP: Tabs and pagination for navigation
        if (detailPanel != null)
      detailPanel.SetActive(false);
   SetStandardInputsVisible(false);
   
 // IMPORTANT: Hide TabContent when in Quest Config mode
    var tabContent = transform.Find("TabContent");
  if (tabContent != null)
  {
    tabContent.gameObject.SetActive(false);
 Debug.Log("[NpcMainPanelController] Hidden TabContent for Quest Config screen");
        }
        
     _questConfigScreenController?.Show(_currentNpc);

   // Update header for quest config
   if (headerText != null)
       headerText.text = "Setup Quest Cfg";
          break;

  case NpcType.DialogueNpc:
     // Handled by PanelElementManager.DialogueEditor mode in RefreshUI
     _dialogueEditorScreenController?.Show(_currentNpc);
     break;

 case NpcType.Trader:
           default:
      // For other types, show standard detail panel for now
         if (detailPanel != null)
         detailPanel.SetActive(true);
 SetStandardInputsVisible(true);
      break;
    }
        }

        /// <summary>
  /// Hides tabs and pagination controls for screens that don't need them.
        /// </summary>
        private void HideTabsAndPagination()
        {
         // Hide tab buttons
            questNpcTabButton?.gameObject.SetActive(false);
     infoNpcTabButton?.gameObject.SetActive(false);
dialogueNpcTabButton?.gameObject.SetActive(false);
 traderTabButton?.gameObject.SetActive(false);
         
      // Hide pagination
 if (prevPageButton != null)
        prevPageButton.gameObject.SetActive(false);
            if (nextPageButton != null)
    nextPageButton.gameObject.SetActive(false);
            if (pageText != null)
         pageText.gameObject.SetActive(false);
   
            // Hide search
     if (searchInput != null)
           searchInput.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows tabs and pagination controls.
     /// </summary>
        private void ShowTabsAndPagination()
  {
      // Show tab buttons
            questNpcTabButton?.gameObject.SetActive(true);
    infoNpcTabButton?.gameObject.SetActive(true);
    dialogueNpcTabButton?.gameObject.SetActive(true);
traderTabButton?.gameObject.SetActive(true);
            
       // Show pagination
            if (prevPageButton != null)
            prevPageButton.gameObject.SetActive(true);
            if (nextPageButton != null)
       nextPageButton.gameObject.SetActive(true);
        if (pageText != null)
            pageText.gameObject.SetActive(true);
        
            // Show search
            if (searchInput != null)
                searchInput.gameObject.SetActive(true);
        }

     /// <summary>
    /// Shows or hides the standard input fields (name, model, profile).
 /// </summary>
        private void SetStandardInputsVisible(bool visible)
        {
   // Hide/show the input field containers
         if (nameOverrideInput != null)
   {
         var parent = nameOverrideInput.transform.parent?.gameObject ?? nameOverrideInput.gameObject;
                parent.SetActive(visible);
            }

            if (modelOverrideInput != null)
            {
       var parent = modelOverrideInput.transform.parent?.gameObject ?? modelOverrideInput.gameObject;
    parent.SetActive(visible);
      }

     if (profileInput != null)
      {
        var parent = profileInput.transform.parent?.gameObject ?? profileInput.gameObject;
   parent.SetActive(visible);
  }
        }

        private void LoadMainScreenSettings()
        {
            if (_currentNpc == null) return;

      if (nameOverrideInput != null)
         nameOverrideInput.text = _currentNpc.npcProfileName ?? "";

  if (modelOverrideInput != null)
         modelOverrideInput.text = _currentNpc.modelOverride ?? "";

      if (profileInput != null)
            {
       string currentProfile = GetCurrentProfileForType(_currentNpc.npcType);
        profileInput.text = currentProfile;
     }

            LoadIdleWanderState();
        }

        private void LoadTypeScreenSettings()
 {
            if (_currentNpc == null || !_selectedNpcType.HasValue) return;

   // For non-Info NPC types, load standard settings
if (_selectedNpcType.Value != NpcType.InfoNpc)
            {
           if (nameOverrideInput != null)
          nameOverrideInput.text = _currentNpc.npcProfileName ?? "";

  if (modelOverrideInput != null)
   modelOverrideInput.text = _currentNpc.modelOverride ?? "";

           if (profileInput != null)
       {
        string currentProfile = GetCurrentProfileForType(_selectedNpcType.Value);
       profileInput.text = currentProfile;
        }
            }
     // Info NPC screen handles its own loading
        }

        private string GetNpcTypeDisplayName(NpcType type)
        {
    return type switch
         {
     NpcType.QuestNpc => "Quest NPC",
       NpcType.InfoNpc => "Info NPC",
    NpcType.DialogueNpc => "Dialogue NPC",
         NpcType.Trader => "Trader",
         NpcType.Banker => "Banker",
         NpcType.Gambler => "Gambler",
         NpcType.Marketplace => "Marketplace",
         NpcType.Achievement => "Achievement",
         NpcType.Leaderboard => "Leaderboard",
         NpcType.Mail => "Mail",
         NpcType.Transmog => "Transmog",
         _ => "NPC"
            };
     }

        private void ClearContent()
        {
  foreach (var go in _currentGameObjects)
         {
   if (go != null)
      Destroy(go);
          }
            _currentGameObjects.Clear();
 }

        private void ForceLayoutUpdate()
        {
            // Use targeted LayoutRebuilder instead of toggling all ContentSizeFitters.
            // Toggling off/on forces a double layout pass across the entire 600+ node hierarchy.
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        #endregion

        #region NPC Settings

        private string GetCurrentProfileForType(NpcType type)
      {
      if (_currentNpc == null) return "";

            return type switch
     {
        NpcType.QuestNpc => _currentNpc.questProfile ?? "",
        NpcType.InfoNpc => _currentNpc.infoProfile ?? "",
             NpcType.DialogueNpc => _currentNpc.dialogueProfile ?? "",
 NpcType.Trader => _currentNpc.traderProfile ?? "",
             NpcType.Banker => _currentNpc.bankerProfile ?? "",
             NpcType.Gambler => _currentNpc.gamblerProfile ?? "",
             NpcType.Marketplace => _currentNpc.marketplaceProfile ?? "",
             NpcType.Achievement => _currentNpc.achievementProfile ?? "",
             NpcType.Leaderboard => _currentNpc.leaderboardProfile ?? "",
             NpcType.Mail => _currentNpc.mailProfile ?? "",
             NpcType.Transmog => _currentNpc.transmogProfile ?? "",
             _ => ""
      };
}

        private void ApplyNpcChanges()
        {
            if (_currentNpc == null)
         {
      Debug.LogWarning("[NpcMainPanelController] Cannot apply changes - no NPC selected");
            return;
            }

    var zNetView = _currentNpc.GetComponent<ZNetView>();
       if (zNetView == null || !zNetView.IsValid())
    {
           MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Cannot save - NPC has no valid ZNetView");
   return;
      }

            // Check if we're on the Info NPC screen - it handles its own apply
            if (_selectedNpcType == NpcType.InfoNpc && _infoScreenController != null && _infoScreenController.IsVisible)
            {
  _infoScreenController.ApplyChanges();
            return;
   }

    // Check if we're on the Quest Config screen - it handles its own apply
      if (_selectedNpcType == NpcType.QuestNpc && _questConfigScreenController != null && _questConfigScreenController.IsVisible)
      {
            _questConfigScreenController.ApplyChanges();
     return;
      }

            // Read the name the user entered
            string pendingName = nameOverrideInput?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(pendingName))
                pendingName = _currentNpc.npcProfileName;

            // Template overwrite check: if a template already exists with this name
            // AND the NPC hasn't been saved under this template name before, prompt.
            string templateKey = SavedNpcManager.MakeTemplateName(pendingName);
            if (!SavedNpcManager.IsDefaultNpcName(pendingName) && SavedNpcManager.TemplateExists(templateKey))
            {
                // Check if this NPC already owns this template (same position-based ID)
                var existingTemplate = SavedNpcManager.GetTemplate(templateKey);
                string thisNpcId = SavedNpcManager.GenerateNpcId(_currentNpc.transform.position);
                bool isSameNpc = existingTemplate != null &&
                    string.Equals(existingTemplate.NpcId, "template_" + templateKey, StringComparison.OrdinalIgnoreCase);

                // Also consider it "same NPC" if the existing template's profile name matches
                // what this NPC already has (i.e. updating its own template)
                bool isOwnTemplate = string.Equals(_currentNpc.npcProfileName, pendingName, StringComparison.OrdinalIgnoreCase);

                if (!isSameNpc && !isOwnTemplate)
                {
                    OverwriteConfirmDialog.Show(pendingName, () =>
                    {
                        PerformApplyNpcChanges();
                    });
                    return;
                }
            }

            PerformApplyNpcChanges();
        }

        private void PerformApplyNpcChanges()
        {
            // Capture current fashion JSON before any model changes so we can restore it
  string currentFashionJson = null;
         try
            {
                currentFashionJson = NpcFashionManager.ExtractFashionJsonFromNpc(_currentNpc);
   }
            catch (Exception ex)
       {
             Debug.LogWarning($"[NpcMainPanelController] Failed to capture fashion JSON: {ex.Message}");
           }

         // If we couldn't extract fashion JSON, use pending fashion JSON if available
   if (string.IsNullOrEmpty(currentFashionJson))
       {
 currentFashionJson = _currentNpc.GetPendingFashionJson();
     }

          // Store the fashion JSON so it persists through model reset
       if (!string.IsNullOrEmpty(currentFashionJson))
          {
     _currentNpc.SetPendingFashionJson(currentFashionJson);
     Debug.Log($"[NpcMainPanelController] Stored fashion JSON for persistence: {currentFashionJson.Length} chars");
            }

            // Apply name override
     if (nameOverrideInput != null && !string.IsNullOrEmpty(nameOverrideInput.text))
            {
       _currentNpc.npcProfileName = nameOverrideInput.text;
              // Also update displayNameOverride so GetDisplayName(), GetHoverName(),
              // and the billboard health bar all reflect the new name.
              _currentNpc.displayNameOverride = nameOverrideInput.text;

                // Propagate the new name to the companion component (EnemyHud billboard), if any.
                FiresCore.Bridge.NpcCompanionBridge.RaiseSetDisplayName(_currentNpc.gameObject, nameOverrideInput.text);
         }

            // Track if model was changed/reset so we know if we need to schedule reapplication
   bool modelWasReset = false;

       // Apply model override
            if (modelOverrideInput != null)
            {
            string newModel = modelOverrideInput.text?.Trim() ?? "";
   string oldModel = _currentNpc.modelOverride ?? "";

     bool wantsReset = string.IsNullOrEmpty(newModel) ||
       newModel.Equals("none", StringComparison.OrdinalIgnoreCase);

    if (wantsReset)
    {
   _currentNpc.modelOverride = "";
   _currentNpc.ResetModelToDefault();
     modelOverrideInput.text = "";
  modelWasReset = true;
            // ResetModelToDefault handles scheduling ApplyVisualEquipment and ApplyPendingFashion
   }
           else if (newModel != oldModel)
    {
       _currentNpc.modelOverride = newModel;
           _currentNpc.ApplyModelOverride(newModel);
         modelWasReset = true;
       // ApplyModelOverride handles scheduling ApplyVisualEquipment
              }
            }

       // If model wasn't changed, we still need to ensure equipment and fashion are visible
            // This handles the case where user sets up fashion/equipment in dressing room, 
       // then goes back to main screen and clicks Apply
       if (!modelWasReset)
            {
       // Re-apply equipment if any exists
        if (_currentNpc.HasAnyEquipment())
     {
          _currentNpc.ApplyVisualEquipment();
       }

           // Re-apply fashion if any exists
  if (!string.IsNullOrEmpty(currentFashionJson))
   {
             try
          {
     NpcFashionManager.ApplyFashionJsonToNpc(currentFashionJson, _currentNpc);
   Debug.Log("[NpcMainPanelController] Re-applied fashion JSON to NPC");
         }
        catch (Exception ex)
    {
           Debug.LogWarning($"[NpcMainPanelController] Failed to re-apply fashion: {ex.Message}");
     }
           }
        }

   // Apply profile based on current screen
            if (profileInput != null)
            {
    string profile = profileInput.text?.Trim() ?? "";

 if (IsOnMainScreen)
                {
            // On main screen, try to detect the profile type and set NPC type accordingly
            NpcType detectedType = DetectProfileType(profile);
              ApplyProfileToType(detectedType, profile);
        _currentNpc.npcType = detectedType;
           Debug.Log($"[NpcMainPanelController] Main screen - detected profile type: {detectedType} for profile: '{profile}'");
        }
           else if (_selectedNpcType.HasValue)
       {
     ApplyProfileToType(_selectedNpcType.Value, profile);
       _currentNpc.npcType = _selectedNpcType.Value;
    }
            }

       // Apply idle wander toggle to CompanionNpcModule if present
            ApplyIdleWanderState();

       _currentNpc.SaveToZDO();

           string screenName = IsOnMainScreen ? "NPC" : GetNpcTypeDisplayName(_selectedNpcType.Value);
   string typeName = GetNpcTypeDisplayName(_currentNpc.npcType);
       MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"{screenName} settings saved! Type: {typeName}");
            Debug.Log($"[NpcMainPanelController] Applied changes - Name: {_currentNpc.npcProfileName}, Type: {_currentNpc.npcType}, Model: {_currentNpc.modelOverride}");
        }
    /// <summary>
    /// Detects the NPC type based on the profile name by checking if it exists in profile managers.
    /// </summary>
    private NpcType DetectProfileType(string profile)
    {
        if (string.IsNullOrEmpty(profile))
         return _currentNpc?.npcType ?? NpcType.QuestNpc;

        // Check if it's a quest profile
        var questProfile = QuestProfileManager.GetProfile(profile);
   if (questProfile != null)
        {
 Debug.Log($"[NpcMainPanelController] Profile '{profile}' found as Quest profile");
 return NpcType.QuestNpc;
        }

    // Check if it's an info profile
    string infoText = InfoNpcScreenController.GetInfoTextForProfile(profile);
        if (!string.IsNullOrEmpty(infoText))
        {
            Debug.Log($"[NpcMainPanelController] Profile '{profile}' found as Info profile");
  return NpcType.InfoNpc;
   }

        // Check if it's a dialogue profile
        var dialogueProfile = DialogueManager.GetProfile(profile);
        if (dialogueProfile != null)
     {
  Debug.Log($"[NpcMainPanelController] Profile '{profile}' found as Dialogue profile");
            return NpcType.DialogueNpc;
        }

        // Default to current type or Quest
      Debug.Log($"[NpcMainPanelController] Profile '{profile}' not found in any manager, using current type: {_currentNpc?.npcType ?? NpcType.QuestNpc}");
    return _currentNpc?.npcType ?? NpcType.QuestNpc;
    }

  private void ApplyProfileToType(NpcType type, string profile)
        {
        switch (type)
      {
  case NpcType.QuestNpc:
        _currentNpc.questProfile = profile;
             break;
    case NpcType.InfoNpc:
             _currentNpc.infoProfile = profile;
            break;
           case NpcType.DialogueNpc:
         _currentNpc.dialogueProfile = profile;
          break;
       case NpcType.Trader:
       _currentNpc.traderProfile = profile;
    break;
       case NpcType.Banker:
       _currentNpc.bankerProfile = profile;
    break;
       case NpcType.Gambler:      _currentNpc.gamblerProfile = profile;     break;
       case NpcType.Marketplace:  _currentNpc.marketplaceProfile = profile; break;
       case NpcType.Achievement:  _currentNpc.achievementProfile = profile; break;
       case NpcType.Leaderboard:  _currentNpc.leaderboardProfile = profile; break;
       case NpcType.Mail:         _currentNpc.mailProfile = profile;        break;
       case NpcType.Transmog:     _currentNpc.transmogProfile = profile;    break;
            }
        }

  #endregion

        #region Pagination

        private void UpdatePagination()
        {
  if (prevPageButton != null)
      prevPageButton.interactable = _currentPage > 0 || !IsOnMainScreen;

            if (nextPageButton != null)
        nextPageButton.interactable = _currentPage < _currentMaxPage - 1;

            if (pageText != null)
                pageText.text = $"{_currentPage + 1} / {_currentMaxPage}";
        }

  private void PageIncrementor(int value)
        {
        int newPage = _currentPage + value;
    if (newPage >= 0 && newPage < _currentMaxPage)
         {
   _currentPage = newPage;
    _selectedIndex = -1;
   UpdatePagination();
 }
   }

        #endregion

  #region Canvas Setup

        private void EnsureCanvasSetup()
        {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null) return;

            // Panel is hosted under the standalone canvas created by UIInitializer.
            // We deliberately do NOT fall back to reparenting under Hud's canvas here:
            // doing so would re-introduce inventory-grid dirty cascades on every show.
            var parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogWarning("[NpcMainPanelController] No parent Canvas found; UIInitializer should have created the standalone canvas.");
            }

            // DO NOT reset anchoredPosition here - let LoadPanelPosition() handle it
        }

        private void EnsurePanelVisibility()
        {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                Debug.LogError("[NpcMainPanelController] No RectTransform on panel!");
                return;
            }

            // Panel lives under the standalone canvas created by UIInitializer; we
            // intentionally do not reparent into the Hud canvas here (that would
            // re-introduce inventory-grid dirty cascades on every show).
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[NpcMainPanelController] Panel not under a Canvas! UIInitializer should have created the standalone canvas.");
            }

    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
       rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
      rectTransform.localScale = Vector3.one;
      rectTransform.localRotation = Quaternion.identity;

      // DO NOT reset anchoredPosition here - let LoadPanelPosition() handle it
         // rectTransform.anchoredPosition = Vector2.zero;

 if (rectTransform.sizeDelta.x < 100 || rectTransform.sizeDelta.y < 100)
         {
     Debug.LogWarning($"[NpcMainPanelController] Panel size too small: {rectTransform.sizeDelta}, setting default");
           rectTransform.sizeDelta = new Vector2(500, 600);
 }

    var canvasGroup = GetComponent<CanvasGroup>();
    if (canvasGroup == null)
     {
      canvasGroup = gameObject.AddComponent<CanvasGroup>();
      }
     canvasGroup.alpha = 1f;
      canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        transform.SetAsLastSibling();
          ApplySize(false);

         Debug.Log($"[NpcMainPanelController] Panel visibility check - Canvas: {canvas?.name ?? "null"}, " +
  $"Size: {rectTransform.sizeDelta}, Scale: {transform.localScale}, " +
 $"AnchoredPos: {rectTransform.anchoredPosition}, CanvasGroup alpha: {canvasGroup.alpha}");
    }

        #endregion

        #region Dressing Room

   /// <summary>
        /// Wires up the Dressing Room button from the Unity prefab placeholder.
        /// Expects a GameObject named "DressingRoom" in the DetailPanel.
        /// </summary>
        #region Idle Wander Toggle

             /// <summary>
             /// Creates the idle wander checkbox below the Apply button in the DetailPanel.
             /// Same visual style as the quest config screen checkbox.
             /// </summary>
             private void CreateIdleWanderToggle()
             {
                 if (_idleWanderToggle != null) return;
                 if (detailPanel == null) return;

                 var containerGO = new GameObject("IdleWanderCheckbox");
                 containerGO.transform.SetParent(detailPanel.transform, false);
                 _idleWanderToggleGO = containerGO;

                 var containerRect = containerGO.AddComponent<RectTransform>();
                 containerRect.anchorMin = new Vector2(0.25f, 0f);
                 containerRect.anchorMax = new Vector2(0.75f, 0f);
                 containerRect.pivot = new Vector2(0.5f, 0f);
                 containerRect.anchoredPosition = new Vector2(0f, 8f);
                 containerRect.sizeDelta = new Vector2(0f, 22f);

                 // LayoutElement so it participates in the DetailPanel VLG
                 var le = containerGO.AddComponent<LayoutElement>();
                 le.minHeight = 22f;
                 le.preferredHeight = 22f;
                 le.minWidth = 250f;

                 // Toggle box
                 var toggleGO = new GameObject("Toggle");
                 toggleGO.transform.SetParent(containerGO.transform, false);
                 var toggleRect = toggleGO.AddComponent<RectTransform>();
                 toggleRect.anchorMin = new Vector2(0f, 0.5f);
                 toggleRect.anchorMax = new Vector2(0f, 0.5f);
                 toggleRect.pivot = new Vector2(0f, 0.5f);
                 toggleRect.anchoredPosition = new Vector2(4f, 0f);
                 toggleRect.sizeDelta = new Vector2(16f, 16f);

                 var toggleBg = toggleGO.AddComponent<Image>();
                 toggleBg.color = new Color(0.08f, 0.08f, 0.1f, 0.6f);
                 toggleBg.raycastTarget = true;

                 // Checkmark image
                 var checkGO = new GameObject("Checkmark");
                 checkGO.transform.SetParent(toggleGO.transform, false);
                 var checkRect = checkGO.AddComponent<RectTransform>();
                 checkRect.anchorMin = new Vector2(0.15f, 0.15f);
                 checkRect.anchorMax = new Vector2(0.85f, 0.85f);
                 checkRect.offsetMin = Vector2.zero;
                 checkRect.offsetMax = Vector2.zero;

                 var checkImg = checkGO.AddComponent<Image>();
                 checkImg.color = new Color(1f, 0.7f, 0.3f);

                 // Wire Toggle component
                 _idleWanderToggle = toggleGO.AddComponent<Toggle>();
                 _idleWanderToggle.targetGraphic = toggleBg;
                 _idleWanderToggle.graphic = checkImg;
                 _idleWanderToggle.isOn = false;

                 // Label
                 var labelGO = new GameObject("Label");
                 labelGO.transform.SetParent(containerGO.transform, false);
                 var labelRect = labelGO.AddComponent<RectTransform>();
                 labelRect.anchorMin = new Vector2(0f, 0f);
                 labelRect.anchorMax = new Vector2(1f, 1f);
                 labelRect.offsetMin = new Vector2(24f, 0f);
                 labelRect.offsetMax = Vector2.zero;

                 var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
                 labelTmp.text = "Allow Idle Wandering";
                 labelTmp.fontSize = 12f;
                 labelTmp.color = new Color(0.9f, 0.75f, 0.45f);
                 labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
                 labelTmp.raycastTarget = false;
                 UIBuilderHelper.ApplyBodyFont(labelTmp);

                 Debug.Log("[NpcMainPanelController] Created idle wander toggle");
             }

             /// <summary>
             /// Reads the current allowIdleWandering state from the CompanionNpcModule
             /// and syncs the toggle. Hides the toggle if no CompanionNpcModule is present.
             /// </summary>
             private void LoadIdleWanderState()
             {
                 if (_currentNpc == null) return;

                 if (_idleWanderToggleGO != null)
                     _idleWanderToggleGO.SetActive(true);

                 if (_idleWanderToggle != null)
                 {
                     // Companion module state if a companion mod manages this NPC, else NpcController flag.
                     _idleWanderToggle.isOn = FiresCore.Bridge.NpcCompanionBridge.GetIdleWander(_currentNpc.gameObject)
                         ?? _currentNpc.allowIdleWandering;
                 }
             }

             /// <summary>
             /// Applies the toggle state to the CompanionNpcModule.
             /// Uses ToggleIdleWandering() only when the state actually changed.
             /// </summary>
             private void ApplyIdleWanderState()
             {
                 if (_currentNpc == null || _idleWanderToggle == null) return;

                 bool wantWander = _idleWanderToggle.isOn;

                 // Persist on NpcController (ZDO) for both static and companion NPCs.
                 _currentNpc.allowIdleWandering = wantWander;

                 // Let the companion mod construct/toggle the actual wandering behavior (no-op without it).
                 FiresCore.Bridge.NpcCompanionBridge.RaiseApplyIdleWander(_currentNpc.gameObject, wantWander);
             }

             #endregion

        private void CreateDressingRoomButton()
         {
            if (_dressingRoomButton != null) return;
            if (detailPanel == null) return;

            // Look for existing placeholder from Unity prefab
  var dressingRoomGO = detailPanel.transform.Find("DressingRoom");
 if (dressingRoomGO == null)
            {
              Debug.LogWarning("[NpcMainPanelController] DressingRoom placeholder not found in DetailPanel - creating programmatically");
   CreateDressingRoomButtonProgrammatically();
      return;
            }

            // Add Button component if not present
     _dressingRoomButton = dressingRoomGO.GetComponent<Button>();
       if (_dressingRoomButton == null)
        {
            _dressingRoomButton = dressingRoomGO.gameObject.AddComponent<Button>();
  }

        // Add Image component for button background if not present
   var buttonImage = dressingRoomGO.GetComponent<Image>();
     if (buttonImage == null)
        {
           buttonImage = dressingRoomGO.gameObject.AddComponent<Image>();
     }
            buttonImage.color = new Color(0.12f, 0.10f, 0.08f, 0.9f);
         buttonImage.raycastTarget = true;
         _dressingRoomButton.targetGraphic = buttonImage;

     // Style button colors
            var colors = _dressingRoomButton.colors;
  colors.normalColor = new Color(0.12f, 0.10f, 0.08f, 0.9f);
            colors.highlightedColor = new Color(0.4f, 0.3f, 0.15f, 1f);
colors.pressedColor = new Color(0.6f, 0.4f, 0.2f, 1f);
            colors.selectedColor = new Color(0.12f, 0.10f, 0.08f, 0.9f);
          _dressingRoomButton.colors = colors;

     // Find or create text child
 var buttonText = dressingRoomGO.GetComponentInChildren<TMP_Text>();
            if (buttonText == null)
    {
        var textGO = new GameObject("Text");
  textGO.transform.SetParent(dressingRoomGO, false);
     
         var textRect = textGO.AddComponent<RectTransform>();
    textRect.anchorMin = Vector2.zero;
       textRect.anchorMax = Vector2.one;
              textRect.offsetMin = Vector2.zero;
     textRect.offsetMax = new Vector2(0, -2);
     
                buttonText = textGO.AddComponent<TextMeshProUGUI>();
    }

    // Style the text
            buttonText.text = "Dressing Room";
            buttonText.color = UIFontConfig.Colors.Gold;
            buttonText.fontSize = 16;
   buttonText.alignment = TextAlignmentOptions.Center;
       UIBuilderHelper.ApplyValheimFont(buttonText);

            // Wire click handler
    _dressingRoomButton.onClick.RemoveAllListeners();
    _dressingRoomButton.onClick.AddListener(OnDressingRoomButtonClicked);

     Debug.Log("[NpcMainPanelController] Wired DressingRoom button from Unity prefab placeholder");
        }

        /// <summary>
   /// Fallback: Creates the Dressing Room button programmatically if no placeholder exists.
   /// </summary>
        private void CreateDressingRoomButtonProgrammatically()
  {
         // Create button container with semi-transparent background
            var buttonContainerGO = new GameObject("DressingRoom");
         buttonContainerGO.transform.SetParent(detailPanel.transform, false);

  var containerRect = buttonContainerGO.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.15f, 0.25f);
containerRect.anchorMax = new Vector2(0.85f, 0.38f);
   containerRect.offsetMin = Vector2.zero;
  containerRect.offsetMax = Vector2.zero;

     // Add semi-transparent background
    var containerBg = buttonContainerGO.AddComponent<Image>();
        containerBg.color = new Color(0.12f, 0.10f, 0.08f, 0.9f);
          containerBg.raycastTarget = true;

  // Add Button component
    _dressingRoomButton = buttonContainerGO.AddComponent<Button>();
            _dressingRoomButton.targetGraphic = containerBg;

          // Style button colors
     var colors = _dressingRoomButton.colors;
   colors.normalColor = new Color(0.12f, 0.10f, 0.08f, 0.9f);
  colors.highlightedColor = new Color(0.4f, 0.3f, 0.15f, 1f);
       colors.pressedColor = new Color(0.6f, 0.4f, 0.2f, 1f);
      colors.selectedColor = new Color(0.12f, 0.10f, 0.08f, 0.9f);
         _dressingRoomButton.colors = colors;

            // Create text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonContainerGO.transform, false);

    var textRect = textGO.AddComponent<RectTransform>();
      textRect.anchorMin = Vector2.zero;
   textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
    textRect.offsetMax = new Vector2(0, -2);

            var buttonText = textGO.AddComponent<TextMeshProUGUI>();
       buttonText.text = "Dressing Room";
            buttonText.color = UIFontConfig.Colors.Gold;
   buttonText.fontSize = 16;
  buttonText.alignment = TextAlignmentOptions.Center;
            UIBuilderHelper.ApplyValheimFont(buttonText);

         // Wire click handler
     _dressingRoomButton.onClick.AddListener(OnDressingRoomButtonClicked);

            Debug.Log("[NpcMainPanelController] Created Dressing Room button programmatically (fallback)");
        }

     private void OnDressingRoomButtonClicked()
        {
         Debug.Log("[NpcMainPanelController] Dressing Room button clicked!");

            if (_currentNpc == null)
            {
          Debug.LogWarning("[NpcMainPanelController] No NPC selected for dressing room!");
 MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "No NPC selected");
              return;
   }

     if (_dressingRoomScreenController == null)
            {
      Debug.LogError("[NpcMainPanelController] DressingRoomScreenController is null!");
     return;
 }

            // Show dressing room screen - it will call PanelElementManager.ConfigureForMode internally
     Debug.Log($"[NpcMainPanelController] Showing dressing room for: {_currentNpc.GetDisplayName()}");
            _dressingRoomScreenController.Show(_currentNpc);
        }

/// <summary>
 /// Called when the user wants to go back from dressing room to main screen.
        /// This is triggered by pressing Escape or clicking the back/prev button.
        /// </summary>
        private void ReturnFromDressingRoom()
      {
            if (_dressingRoomScreenController != null && _dressingRoomScreenController.IsVisible)
            {
         _dressingRoomScreenController.Hide();
      // Return to main admin config screen
              GoToMainScreen();
            }
        }

     #endregion
  }

    /// <summary>
    /// Helper component for handling panel drag events.
    /// </summary>
    public class PanelDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private NpcMainPanelController _controller;
        private float _lastClickTime;
        private const float DoubleClickTime = 0.3f;

        public void Initialize(NpcMainPanelController controller)
        {
            _controller = controller;
      }

public void OnBeginDrag(PointerEventData eventData)
        {
   if (_controller != null)
 {
          _controller.OnDragStart(eventData.position);
      }
        }

   public void OnDrag(PointerEventData eventData)
        {
            if (_controller != null)
         {
     _controller.OnDrag(eventData.position);
         }
   }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_controller != null)
            {
     _controller.OnDragEnd();
            }
    }

        public void OnPointerClick(PointerEventData eventData)
        {
         if (Time.unscaledTime - _lastClickTime < DoubleClickTime)
          {
 _controller?.ToggleLock();
         _lastClickTime = 0f;
            }
            else
            {
        _lastClickTime = Time.unscaledTime;
}
        }
    }
}
