using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Npc.Vault;
using FiresRPGmaker.Modules.Party;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Group HUD Controller - displays health, stamina, and eitr bars for the player's
    /// following companions on the main gameplay HUD.
    /// 
    /// FEATURES:
    /// - Shows only when inventory/other UIs are closed (but stays visible when Menu is open for repositioning)
    /// - Positioned in top-right, below the minimap (~30% down from top) by default
    /// - Draggable - hold and drag to reposition, position is saved
    /// - Displays companion name with distance in meters
    /// - Shows health, stamina, eitr bars at half size of inventory UI
    /// - Vertical layout for multiple companions (future: group members too)
    /// 
    /// DRAG TO REPOSITION:
    /// - Press Escape to open menu, then drag the HUD panel to reposition
    /// - Position is saved automatically when you release the mouse
    /// </summary>
    public class GroupHudController : MonoBehaviour
    {
        #region Constants
        
        // Panel dimensions
        private const float PANEL_WIDTH = 180f;
        private const float MEMBER_HEIGHT = 50f;
        private const float BAR_HEIGHT = 6f;
        private const float BAR_SPACING = 2f;
        private const float NAME_HEIGHT = 14f;
        private const float STATUS_EFFECT_ROW_HEIGHT = 16f;
        private const float STATUS_EFFECT_ICON_SIZE = 14f;
        private const int MAX_EFFECTS_PER_ROW = 5;  // Max icons per row before wrapping
        
        // Code-level default position (used when neither BepInEx config nor PlayerPrefs
        // has a value). 30% down from top, right side of screen.
        // Note: configGroupHudDefaultPosX / configGroupHudDefaultPosY in ConfigManager
        // override these â€” they exist so the original default can be tuned per-install.
        private const float DEFAULT_POS_X = -10f;
        private const float DEFAULT_POS_Y = -200f;

        /// <summary>Resolves the configured default panel position (BepInEx config â†’ fallback to const).</summary>
        private static Vector2 GetConfiguredDefaultPosition()
        {
            var cm = ConfigManager.Instance;
            float x = cm?.configGroupHudDefaultPosX?.Value ?? DEFAULT_POS_X;
            float y = cm?.configGroupHudDefaultPosY?.Value ?? DEFAULT_POS_Y;
            return new Vector2(x, y);
        }
        
        // PlayerPrefs keys for saving position
        private const string PREF_POS_X = "GroupHud_PosX";
        private const string PREF_POS_Y = "GroupHud_PosY";
        
        // Colors matching companion UI theme
        private static readonly Color HealthBarColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color StaminaBarColor = new Color(0.9f, 0.8f, 0.2f, 1f);
        private static readonly Color EitrBarColor = new Color(0.3f, 0.5f, 0.9f, 1f);
        private static readonly Color BarBackgroundColor = new Color(0.1f, 0.08f, 0.06f, 0.8f);
        private static readonly Color PanelBackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.6f);
        private static readonly Color PanelDraggingColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
        private static readonly Color NameColor = new Color(1f, 0.85f, 0.5f, 1f);
        private static readonly Color DistanceColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        private static readonly Color DeadNameColor = new Color(0.5f, 0.4f, 0.35f, 1f);
        private static readonly Color DeadBarColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        private static readonly Color RespawnTimerColor = new Color(1f, 0.5f, 0.3f, 1f);
        
        private const int COLUMN_MAX_ITEMS    = 20;  // hard upper limit per column (physical capacity)
        private const float COLUMN_INNER_WIDTH = PANEL_WIDTH - 8f;

        // ?? Configurable display size ????????????????????????????????????????
        // How many rows are visible before the panel requires scrolling, and
        // how many columns are shown.  Both are persisted in PlayerPrefs so
        // the player can tune them without restarting.
        private const string PREF_VISIBLE_ROWS = "GroupHud_VisibleRows";
        private const string PREF_VISIBLE_COLS = "GroupHud_VisibleCols";
        private const int DEFAULT_VISIBLE_ROWS = 8;
        private const int DEFAULT_VISIBLE_COLS = 1;
        private const int MIN_VISIBLE_ROWS = 1;
        private const int MAX_VISIBLE_ROWS = 20;

        /// <summary>
        /// Rows visible per column before the panel scrolls. Range [1-20].
        /// Reads from BepInEx config (configGroupHudMaxRowsPerColumn) when available
        /// so the value can be tuned via cfg file / Configuration Manager.
        /// PlayerPrefs is kept as a backup for in-session changes pre-config.
        /// </summary>
        public static int VisibleRows
        {
            get
            {
                int cfgVal = ConfigManager.Instance?.configGroupHudMaxRowsPerColumn?.Value
                             ?? PlayerPrefs.GetInt(PREF_VISIBLE_ROWS, DEFAULT_VISIBLE_ROWS);
                return Mathf.Clamp(cfgVal, MIN_VISIBLE_ROWS, MAX_VISIBLE_ROWS);
            }
            set
            {
                int v = Mathf.Clamp(value, MIN_VISIBLE_ROWS, MAX_VISIBLE_ROWS);
                PlayerPrefs.SetInt(PREF_VISIBLE_ROWS, v);
                PlayerPrefs.Save();
                var cfg = ConfigManager.Instance?.configGroupHudMaxRowsPerColumn;
                if (cfg != null) cfg.Value = v;
                _instance?.RebuildLayout();
            }
        }

        /// <summary>Number of columns shown. 1 = single list, 2 = side-by-side lists.</summary>
        public static int VisibleColumns
        {
            get
            {
                int cfgVal = ConfigManager.Instance?.configGroupHudColumns?.Value
                             ?? PlayerPrefs.GetInt(PREF_VISIBLE_COLS, DEFAULT_VISIBLE_COLS);
                return Mathf.Clamp(cfgVal, 1, 2);
            }
            set
            {
                int v = Mathf.Clamp(value, 1, 2);
                PlayerPrefs.SetInt(PREF_VISIBLE_COLS, v);
                PlayerPrefs.Save();
                var cfg = ConfigManager.Instance?.configGroupHudColumns;
                if (cfg != null) cfg.Value = v;
                _instance?.RebuildLayout();
            }
        }

        // Update intervals
        private const float UPDATE_INTERVAL = 0.1f;
        private const float COMPANION_SCAN_INTERVAL = 1f;

        #endregion
        
        #region Fields
        
        private static GroupHudController _instance;
        public static GroupHudController Instance => _instance;
        
        private GameObject _root;
        private RectTransform _rootRect;
        private ScrollRect _scrollRect;
        private GameObject _column1Container;
        private GameObject _column2Container;
        private Image _backgroundImage;
        
        // Tracking data
        private List<CompanionController> _trackedCompanions = new List<CompanionController>();
        private Dictionary<CompanionController, MemberUI> _memberUIs = new Dictionary<CompanionController, MemberUI>();
        
        // UI for pending respawn companions (not in world)
        private Dictionary<string, MemberUI> _pendingRespawnUIs = new Dictionary<string, MemberUI>();

        // UI for other party members (player-id -> row).
        // Holds rows for online + offline members; the row content is
        // refreshed every UPDATE_INTERVAL by UpdatePartyMemberBars().
        private Dictionary<long, MemberUI> _partyMemberUIs = new Dictionary<long, MemberUI>();
        private float _lastPartyScan;
        private const float PARTY_SCAN_INTERVAL = 1f;
        
        // Timing
        private float _lastUpdate;
        private float _lastCompanionScan;
        
        // Cache
        private Player _localPlayer;
        
        // Drag handling
        private bool _isDragging;
        private Vector2 _dragOffset;
        private bool _isRepositionMode;  // True when menu is open and we allow dragging
        private GroupHudDragHandler _dragHandler;
        
        #endregion
        
        #region Member UI Data
        
        private class MemberUI
        {
            public GameObject Root;
            public TMP_Text NameText;
            public TMP_Text DistanceText;
            public TMP_Text RespawnTimerText;
            public Image HealthBarFill;
            public Image StaminaBarFill;
            public Image EitrBarFill;
            public GameObject EitrBarRoot;
            public Image MemberBackground;
            public bool IsDead;
            
            // Status effect display
            public Transform StatusEffectContainer;
            public List<StatusEffectIconUI> StatusEffectIcons = new List<StatusEffectIconUI>();
        }
        
        /// <summary>
        /// UI for a single status effect icon with timer.
        /// </summary>
        private class StatusEffectIconUI
        {
            public GameObject Root;
            public Image Icon;
            public TMP_Text TimerText;
            public string EffectName;
        }
        
        // Track pending respawns (companion ID -> respawn time)
        private Dictionary<string, PendingRespawnInfo> _pendingRespawns = new Dictionary<string, PendingRespawnInfo>();
        
        private class PendingRespawnInfo
        {
            public string CompanionId;
            public string CompanionName;
            public float RespawnTime;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Initializes the Group HUD. Called from UIInitializer or similar.
        /// Creates a standalone HUD element positioned 30% down from top on the right side of the screen.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null) return;
            
            // Wait for Hud to be ready
            if (Hud.instance == null)
            {
                Debug.Log("[GroupHudController] Hud not ready, scheduling deferred initialization");
                var tempObj = new GameObject("GroupHudControllerInitializer");
                var initializer = tempObj.AddComponent<DeferredInitializer>();
                initializer.StartCoroutine(initializer.WaitForHudAndInit());
                return;
            }
            
            CreateInstance();
        }
        
        private static void CreateInstance()
        {
            if (_instance != null) return;
            if (Hud.instance == null) return;
            
            // Find or get the main HUD canvas
            var hudRoot = Hud.instance.transform;
            
            var go = new GameObject("GroupHudController");
            go.transform.SetParent(hudRoot, false);
            _instance = go.AddComponent<GroupHudController>();
            
            Debug.Log("[GroupHudController] Initialized as standalone HUD element");
        }
        
        /// <summary>
        /// Helper class for deferred initialization when Hud isn't ready yet.
        /// </summary>
        private class DeferredInitializer : MonoBehaviour
        {
            public System.Collections.IEnumerator WaitForHudAndInit()
            {
                float timeout = 10f;
                float elapsed = 0f;
                
                while (Hud.instance == null && elapsed < timeout)
                {
                    yield return new WaitForSeconds(0.5f);
                    elapsed += 0.5f;
                }
                
                if (Hud.instance != null)
                {
                    CreateInstance();
                }
                else
                {
                    Debug.LogWarning("[GroupHudController] Hud never became ready, skipping HUD initialization");
                }
                
                Destroy(gameObject);
            }
        }
        
        /// <summary>
        /// Cleans up the Group HUD.
        /// </summary>
        public static void Cleanup()
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            CreateUI();

            // Live-refresh layout / position when the BepInEx configs change
            // (e.g. user edits via Configuration Manager mod).
            var cm = ConfigManager.Instance;
            if (cm != null)
            {
                if (cm.configGroupHudMaxRowsPerColumn != null)
                    cm.configGroupHudMaxRowsPerColumn.SettingChanged += OnLayoutConfigChanged;
                if (cm.configGroupHudColumns != null)
                    cm.configGroupHudColumns.SettingChanged += OnLayoutConfigChanged;
                if (cm.configGroupHudDefaultPosX != null)
                    cm.configGroupHudDefaultPosX.SettingChanged += OnDefaultPosConfigChanged;
                if (cm.configGroupHudDefaultPosY != null)
                    cm.configGroupHudDefaultPosY.SettingChanged += OnDefaultPosConfigChanged;
            }
        }

        private void OnDestroy()
        {
            // Detach config listeners so we don't leak callbacks across scene reloads.
            var cm = ConfigManager.Instance;
            if (cm != null)
            {
                if (cm.configGroupHudMaxRowsPerColumn != null)
                    cm.configGroupHudMaxRowsPerColumn.SettingChanged -= OnLayoutConfigChanged;
                if (cm.configGroupHudColumns != null)
                    cm.configGroupHudColumns.SettingChanged -= OnLayoutConfigChanged;
                if (cm.configGroupHudDefaultPosX != null)
                    cm.configGroupHudDefaultPosX.SettingChanged -= OnDefaultPosConfigChanged;
                if (cm.configGroupHudDefaultPosY != null)
                    cm.configGroupHudDefaultPosY.SettingChanged -= OnDefaultPosConfigChanged;
            }

            if (_instance == this)
                _instance = null;
        }

        private void OnLayoutConfigChanged(object sender, System.EventArgs e)
        {
            try { RebuildLayout(); }
            catch (System.Exception ex) { Debug.LogWarning($"[GroupHudController] Live layout refresh failed: {ex.Message}"); }
        }

        /// <summary>
        /// Live-refresh the panel position only when the player has NOT yet dragged it
        /// (no PlayerPrefs override). Otherwise the user's saved position wins â€” we
        /// don't yank a deliberately-placed panel back to a config default.
        /// </summary>
        private void OnDefaultPosConfigChanged(object sender, System.EventArgs e)
        {
            if (_rootRect == null) return;
            bool hasUserPosition = PlayerPrefs.HasKey(PREF_POS_X) && PlayerPrefs.HasKey(PREF_POS_Y);
            if (hasUserPosition) return;
            _rootRect.anchoredPosition = GetConfiguredDefaultPosition();
        }
        
        private void Update()
        {
            // Cache local player
            if (_localPlayer == null || !_localPlayer)
            {
                _localPlayer = Player.m_localPlayer;
            }
            
            // ALWAYS scan for companions, even when hidden
            // This is critical - otherwise we never find companions to show!
            if (Time.time - _lastCompanionScan >= COMPANION_SCAN_INTERVAL)
            {
                _lastCompanionScan = Time.time;
                ScanForCompanions();
            }

            // Party-member roster scan runs on its own cadence; cheap
            // (one m_customData read + a HashSet diff against the
            // existing rows).
            if (Time.time - _lastPartyScan >= PARTY_SCAN_INTERVAL)
            {
                _lastPartyScan = Time.time;
                ScanForPartyMembers();
            }
            
            // Check if we're in reposition mode (Menu is open)
            _isRepositionMode = Menu.IsVisible();
            
            // Update raycast target based on reposition mode
            if (_backgroundImage != null && !_isDragging)
            {
                _backgroundImage.raycastTarget = _isRepositionMode;
            }
            
            // Check visibility (this now properly checks companion count)
            bool shouldShow = ShouldShowHud();
            if (_root != null)
            {
                _root.SetActive(shouldShow);
                
                // Update background color based on drag state
                if (_backgroundImage != null)
                {
                    _backgroundImage.color = _isDragging ? PanelDraggingColor : PanelBackgroundColor;
                }
            }
            
            if (!shouldShow) return;
            
            // Update bars periodically
            if (Time.time - _lastUpdate >= UPDATE_INTERVAL)
            {
                _lastUpdate = Time.time;
                UpdateMemberBars();
            }
        }
        
        #endregion
        
        #region Drag Handling
        
        /// <summary>
        /// Called by GroupHudDragHandler when drag starts.
        /// </summary>
        public void OnDragStart(Vector2 mousePosition)
        {
            if (!_isRepositionMode) return;
            
            _isDragging = true;
            if (_rootRect != null)
            {
                _dragOffset = (Vector2)_rootRect.position - mousePosition;
            }
        }
        
        /// <summary>
        /// Called by GroupHudDragHandler during drag.
        /// </summary>
        public void OnDrag(Vector2 mousePosition)
        {
            if (!_isDragging || !_isRepositionMode) return;
            
            if (_rootRect != null)
            {
                _rootRect.position = mousePosition + _dragOffset;
            }
        }
        
        /// <summary>
        /// Called by GroupHudDragHandler when drag ends.
        /// </summary>
        public void OnDragEnd()
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            SavePosition();
        }
        
        private void SavePosition()
        {
            if (_rootRect == null) return;
            
            // Save the anchored position directly
            // This is resolution-independent since it's relative to the anchor point (top-right)
            Vector2 anchoredPos = _rootRect.anchoredPosition;
            
            // Only save if position is valid (not NaN or infinity)
            if (float.IsNaN(anchoredPos.x) || float.IsNaN(anchoredPos.y) ||
                float.IsInfinity(anchoredPos.x) || float.IsInfinity(anchoredPos.y))
            {
                Debug.LogWarning($"[GroupHudController] Invalid position, not saving: {anchoredPos}");
                return;
            }
            
            PlayerPrefs.SetFloat(PREF_POS_X, anchoredPos.x);
            PlayerPrefs.SetFloat(PREF_POS_Y, anchoredPos.y);
            PlayerPrefs.Save();
            
            Debug.Log($"[GroupHudController] Saved anchored position: {anchoredPos.x:F1}, {anchoredPos.y:F1}");
        }
        
        private void LoadPosition()
        {
            if (_rootRect == null) return;

            // Default comes from BepInEx config (with const fallback), so installs can
            // dial in their preferred starting spot without needing to drag the panel
            // and without code changes.
            Vector2 def = GetConfiguredDefaultPosition();
            float defaultX = def.x;
            float defaultY = def.y;

            float posX = PlayerPrefs.GetFloat(PREF_POS_X, defaultX);
            float posY = PlayerPrefs.GetFloat(PREF_POS_Y, defaultY);
            
            // Sanity check - only reject truly invalid values (NaN, infinity)
            // Since we're anchored to top-right:
            // - X can be POSITIVE (left of anchor) up to screen width, or negative (right of anchor, which is off-screen)
            // - Y can be NEGATIVE (below anchor) or positive (above anchor, which is off-screen)
            // Don't restrict position based on value - let the user place it anywhere they want
            bool hasValidSavedPosition = PlayerPrefs.HasKey(PREF_POS_X) && PlayerPrefs.HasKey(PREF_POS_Y);
            
            // Only reject NaN and infinity - any numeric value is valid
            if (float.IsNaN(posX) || float.IsNaN(posY) || 
                float.IsInfinity(posX) || float.IsInfinity(posY))
            {
                Debug.Log($"[GroupHudController] Saved position invalid ({posX}, {posY}), using default");
                posX = defaultX;
                posY = defaultY;
            }
            
            _rootRect.anchoredPosition = new Vector2(posX, posY);
            
            if (hasValidSavedPosition)
            {
                Debug.Log($"[GroupHudController] Loaded saved anchored position: {posX:F1}, {posY:F1}");
            }
            else
            {
                Debug.Log($"[GroupHudController] Using default position: {posX:F1}, {posY:F1}");
            }
        }
        
        /// <summary>
        /// Resets the HUD position to default (top-right, about 200px from top).
        /// </summary>
        public void ResetPosition()
        {
            PlayerPrefs.DeleteKey(PREF_POS_X);
            PlayerPrefs.DeleteKey(PREF_POS_Y);
            PlayerPrefs.Save();

            if (_rootRect != null)
            {
                _rootRect.anchoredPosition = GetConfiguredDefaultPosition();
            }

            Debug.Log($"[GroupHudController] Position reset to configured default {GetConfiguredDefaultPosition()}");
        }
        
        #endregion
        
        #region Visibility Check
        
        private bool ShouldShowHud()
        {
            // Check config setting - default to true if config is not available
            try
            {
                if (ConfigManager.Instance?.configShowGroupHud?.Value == false)
                    return false;
            }
            catch { /* Config not ready, default to showing */ }
            
            // Must have local player
            if (_localPlayer == null) return false;
            
            // Hide if inventory is open
            if (InventoryGui.IsVisible()) return false;
            
            // ALLOW when menu is open - this lets users reposition the HUD!
            // The menu being open is our "reposition mode"
            // if (Menu.IsVisible()) return false;
            
            // Hide if minimap is in large mode (full map open)
            if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large) return false;
            
            // Hide if any custom panels are open (but not during menu/reposition mode)
            if (!_isRepositionMode && GUIManager.IsCustomPanelOpen()) return false;
            
            // Hide if console is open
            if (Console.IsVisible()) return false;
            
            // Hide if text input is active
            if (TextInput.IsVisible()) return false;
            
            // Hide if chat is open
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            
            // Must have companions to show (unless we're in reposition mode with saved position)
            // Also show if there are pending respawns or party members
            if (_trackedCompanions.Count == 0 && _pendingRespawns.Count == 0 && _partyMemberUIs.Count == 0 && !_isRepositionMode) return false;
            
            return true;
        }
        
        #endregion
        
        #region UI Creation
        
        private void CreateUI()
        {
            _root = new GameObject("GroupHudPanel");
            _root.transform.SetParent(transform, false);

            _rootRect = _root.AddComponent<RectTransform>();
            _rootRect.anchorMin = new Vector2(1f, 1f);
            _rootRect.anchorMax = new Vector2(1f, 1f);
            _rootRect.pivot    = new Vector2(1f, 1f);
            _rootRect.sizeDelta = new Vector2(PANEL_WIDTH, 30f);

            _backgroundImage = _root.AddComponent<Image>();
            _backgroundImage.color = PanelBackgroundColor;
            _backgroundImage.raycastTarget = false;

            // ?? Viewport (clipping mask) ????????????????????????????????????
            var viewportGO   = new GameObject("Viewport");
            viewportGO.transform.SetParent(_root.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            var viewportImg  = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.01f);
            var viewportMask = viewportGO.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            // ?? Content container (HorizontalLayoutGroup holds both columns) ?
            var contentGO   = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot     = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;

            var contentHL = contentGO.AddComponent<HorizontalLayoutGroup>();
            contentHL.spacing            = 4f;
            contentHL.padding            = new RectOffset(4, 4, 4, 4);
            contentHL.childAlignment     = TextAnchor.UpperLeft;
            contentHL.childControlWidth  = false;
            contentHL.childControlHeight = true;
            contentHL.childForceExpandWidth  = false;
            contentHL.childForceExpandHeight = false;

            var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ?? Column 1 ????????????????????????????????????????????????????
            _column1Container = CreateColumnContainer(contentGO.transform, "Column1");

            // ?? Column 2 (hidden until items > COLUMN_MAX_ITEMS) ????????????
            _column2Container = CreateColumnContainer(contentGO.transform, "Column2");
            _column2Container.SetActive(false);

            // ?? ScrollRect (wraps the viewport+content) ??????????????????????
            _scrollRect = _root.AddComponent<ScrollRect>();
            _scrollRect.viewport     = viewportRect;
            _scrollRect.content      = contentRect;
            _scrollRect.horizontal   = false;
            _scrollRect.vertical     = true;
            _scrollRect.scrollSensitivity = 400f;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scrollRect.enabled = false; // enabled only when > 2 columns worth of items

            // Minimum size so panel is always draggable even when empty
            var rootLE = _root.AddComponent<LayoutElement>();
            rootLE.minHeight = 30f;
            rootLE.minWidth  = PANEL_WIDTH;

            _dragHandler = _root.AddComponent<GroupHudDragHandler>();
            _dragHandler.Initialize(this);

            LoadPosition();
            _root.SetActive(false);

            Debug.Log($"[GroupHudController] UI created at anchoredPosition: {_rootRect.anchoredPosition}");
        }

        private GameObject CreateColumnContainer(Transform parent, string name)
        {
            var go   = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(COLUMN_INNER_WIDTH, 0f);

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.spacing            = 4f;
            vl.childControlWidth  = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = COLUMN_INNER_WIDTH;
            le.minWidth       = COLUMN_INNER_WIDTH;

            return go;
        }

        /// <summary>
        /// Redistributes all live member rows across the two columns and resizes
        /// the root panel. Called whenever the set of displayed rows changes.
        /// </summary>
        private void RebuildLayout()
        {
            // Collect all live row roots in display order:
            // tracked companions ? pending respawns ? party members
            var allRoots = new List<GameObject>();
            foreach (var c in _trackedCompanions)
                if (_memberUIs.TryGetValue(c, out var ui) && ui.Root != null)
                    allRoots.Add(ui.Root);
            foreach (var kvp in _pendingRespawnUIs)
                if (kvp.Value?.Root != null) allRoots.Add(kvp.Value.Root);
            foreach (var kvp in _partyMemberUIs)
                if (kvp.Value?.Root != null) allRoots.Add(kvp.Value.Root);

            int  total        = allRoots.Count;
            int  visRows      = VisibleRows;    // rows visible before scroll
            int  visCols      = VisibleColumns; // 1 or 2
            bool needsCol2    = visCols >= 2;
            // Scroll is needed when total entries exceed the visible window.
            bool needsScroll  = total > visRows * visCols;

            if (_column2Container != null)
                _column2Container.SetActive(needsCol2);

            // Split point: items 0..(visRows-1) go in column 1,
            //              items visRows..end go in column 2 (if active).
            // When only 1 column is configured every item lands in column 1.
            int splitAt = needsCol2 ? visRows : int.MaxValue;

            for (int i = 0; i < allRoots.Count; i++)
            {
                bool   inCol2     = needsCol2 && i >= splitAt;
                var    targetCol  = inCol2 ? _column2Container : _column1Container;
                if (targetCol == null) continue;
                var    row        = allRoots[i];
                if (row.transform.parent != targetCol.transform)
                    row.transform.SetParent(targetCol.transform, false);
                row.transform.SetSiblingIndex(inCol2 ? i - splitAt : i);
            }

            // Panel width: 1 column = PANEL_WIDTH, 2 columns = two column widths + gap.
            float colW   = COLUMN_INNER_WIDTH;
            float gap    = 4f;
            float totalW = needsCol2 ? colW * 2 + gap + 8f : PANEL_WIDTH;

            // Viewport height = actual group size, capped at visRows (the user-configured
            // upper bound). Panel auto-shrinks for small groups and only stops growing
            // once it hits the configured cap; beyond that, the scroll bar takes over.
            float rowH      = MEMBER_HEIGHT + STATUS_EFFECT_ROW_HEIGHT * 2 + 4f;
            int   visibleRowsActual = Mathf.Clamp(total, 1, visRows);
            float viewportH = Mathf.Max(30f, visibleRowsActual * rowH + 8f);

            if (_rootRect != null)
                _rootRect.sizeDelta = new Vector2(totalW, viewportH);

            if (_scrollRect != null)
                _scrollRect.enabled = needsScroll;
        }
        
        private MemberUI CreateMemberUI(CompanionController companion)
        {
            var memberUI = new MemberUI();
            
            // Member container
            var memberGO = new GameObject($"Member_{companion.GetDisplayName()}");
            memberGO.transform.SetParent(_column1Container.transform, false);
            memberUI.Root = memberGO;
            
            var memberRect = memberGO.AddComponent<RectTransform>();
            
            // Layout element for height - base height plus space for status effects (up to 2 rows)
            var layoutElement = memberGO.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = MEMBER_HEIGHT + STATUS_EFFECT_ROW_HEIGHT * 2;
            layoutElement.minHeight = MEMBER_HEIGHT + STATUS_EFFECT_ROW_HEIGHT;
            
            // Background
            var memberBg = memberGO.AddComponent<Image>();
            memberBg.color = new Color(0.05f, 0.05f, 0.08f, 0.5f);
            memberBg.raycastTarget = false;
            
            // Name row (name + distance)
            CreateNameRow(memberGO.transform, memberUI);
            
            // Stat bars
            CreateStatBars(memberGO.transform, memberUI);
            
            // Status effect row
            CreateStatusEffectRow(memberGO.transform, memberUI);
            
            return memberUI;
        }
        
        private void CreateNameRow(Transform parent, MemberUI memberUI)
        {
            var nameRowGO = new GameObject("NameRow");
            nameRowGO.transform.SetParent(parent, false);
            
            var nameRowRect = nameRowGO.AddComponent<RectTransform>();
            nameRowRect.anchorMin = new Vector2(0, 0.70f);
            nameRowRect.anchorMax = new Vector2(1, 1);
            nameRowRect.offsetMin = new Vector2(4, 0);
            nameRowRect.offsetMax = new Vector2(-4, -2);
            
            // Horizontal layout for name + distance
            var hLayout = nameRowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            
            // Name text
            var nameTextGO = new GameObject("Name");
            nameTextGO.transform.SetParent(nameRowGO.transform, false);
            
            memberUI.NameText = nameTextGO.AddComponent<TextMeshProUGUI>();
            memberUI.NameText.text = "Companion";
            memberUI.NameText.fontSize = 11f;
            memberUI.NameText.fontStyle = FontStyles.Bold;
            memberUI.NameText.color = NameColor;
            memberUI.NameText.alignment = TextAlignmentOptions.MidlineLeft;
            memberUI.NameText.textWrappingMode = TextWrappingModes.NoWrap;
            memberUI.NameText.overflowMode = TextOverflowModes.Ellipsis;
            memberUI.NameText.raycastTarget = false;
            
            // Apply font using GUIManager.ApplyGameFont - the same way all other working UI does it
            GUIManager.ApplyGameFont(memberUI.NameText);
            
            // Force mesh update to ensure text renders
            memberUI.NameText.ForceMeshUpdate();
            
            var nameLayout = nameTextGO.AddComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1f;
            
            // Respawn timer text (hidden by default)
            var timerTextGO = new GameObject("RespawnTimer");
            timerTextGO.transform.SetParent(nameRowGO.transform, false);
            
            memberUI.RespawnTimerText = timerTextGO.AddComponent<TextMeshProUGUI>();
            memberUI.RespawnTimerText.text = "";
            memberUI.RespawnTimerText.fontSize = 9f;
            memberUI.RespawnTimerText.color = RespawnTimerColor;
            memberUI.RespawnTimerText.alignment = TextAlignmentOptions.MidlineRight;
            memberUI.RespawnTimerText.textWrappingMode = TextWrappingModes.NoWrap;
            memberUI.RespawnTimerText.raycastTarget = false;
            
            // Apply font using GUIManager.ApplyGameFont - the same way all other working UI does it
            GUIManager.ApplyGameFont(memberUI.RespawnTimerText);
                
            timerTextGO.SetActive(false);
            
            var timerLayout = timerTextGO.AddComponent<LayoutElement>();
            timerLayout.minWidth = 40f;
            
            // Distance text
            var distTextGO = new GameObject("Distance");
            distTextGO.transform.SetParent(nameRowGO.transform, false);
            
            memberUI.DistanceText = distTextGO.AddComponent<TextMeshProUGUI>();
            memberUI.DistanceText.text = "(0m)";
            memberUI.DistanceText.fontSize = 9f;
            memberUI.DistanceText.color = DistanceColor;
            memberUI.DistanceText.alignment = TextAlignmentOptions.MidlineRight;
            memberUI.DistanceText.textWrappingMode = TextWrappingModes.NoWrap;
            memberUI.DistanceText.raycastTarget = false;
            
            // Apply font using GUIManager.ApplyGameFont - the same way all other working UI does it
            GUIManager.ApplyGameFont(memberUI.DistanceText);
            
            var distLayout = distTextGO.AddComponent<LayoutElement>();
            distLayout.minWidth = 35f;
        }
        
        private void CreateStatBars(Transform parent, MemberUI memberUI)
        {
            var barsGO = new GameObject("StatBars");
            barsGO.transform.SetParent(parent, false);
            
            var barsRect = barsGO.AddComponent<RectTransform>();
            barsRect.anchorMin = new Vector2(0, 0);
            barsRect.anchorMax = new Vector2(1, 0.68f);
            barsRect.offsetMin = new Vector2(4, 2);
            barsRect.offsetMax = new Vector2(-4, 0);
            
            // Vertical layout for bars
            var vLayout = barsGO.AddComponent<VerticalLayoutGroup>();
            vLayout.spacing = BAR_SPACING;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
            
            // Health bar
            memberUI.HealthBarFill = CreateStatBar(barsGO.transform, "Health", HealthBarColor);
            
            // Stamina bar
            memberUI.StaminaBarFill = CreateStatBar(barsGO.transform, "Stamina", StaminaBarColor);
            
            // Eitr bar (can be hidden if companion has no eitr)
            var eitrBar = CreateStatBar(barsGO.transform, "Eitr", EitrBarColor);
            memberUI.EitrBarFill = eitrBar;
            memberUI.EitrBarRoot = eitrBar.transform.parent.gameObject;
        }
        
        private Image CreateStatBar(Transform parent, string name, Color fillColor)
        {
            var barGO = new GameObject($"{name}Bar");
            barGO.transform.SetParent(parent, false);
            
            var barRect = barGO.AddComponent<RectTransform>();
            
            // Layout element for height
            var layoutElement = barGO.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = BAR_HEIGHT;
            layoutElement.minHeight = BAR_HEIGHT;
            
            // Background
            var barBg = barGO.AddComponent<Image>();
            barBg.color = BarBackgroundColor;
            barBg.raycastTarget = false;
            
            // Inner container with padding - this represents the "max width" for the fill
            // The fill bar inside will use anchors to represent percentage of this space
            var innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(barGO.transform, false);
            var innerRect = innerGO.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(1, 1);  // 1px padding
            innerRect.offsetMax = new Vector2(-1, -1);
            innerRect.pivot = new Vector2(0, 0.5f);   // Pivot on left side
            
            // Fill bar - uses anchorMax.x for dynamic width based on percentage
            // CRITICAL: anchorMax.x = percentage (0.0 to 1.0)
            // No sizeDelta or offsets - pure anchor-based sizing
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(innerGO.transform, false);
            
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;         // Bottom-left corner
            fillRect.anchorMax = new Vector2(1f, 1f);  // Start at full - will be updated by SetBarFill
            fillRect.offsetMin = Vector2.zero;         // No offsets
            fillRect.offsetMax = Vector2.zero;         // No offsets
            fillRect.pivot = new Vector2(0, 0.5f);     // Pivot on left side for proper fill direction
            
            var fill = fillGO.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;
            
            return fill;
        }
        
        /// <summary>
        /// Creates the status effect icon row for a companion.
        /// Uses a GridLayoutGroup to allow wrapping to multiple rows when many effects are active.
        /// </summary>
        private void CreateStatusEffectRow(Transform parent, MemberUI memberUI)
        {
            var effectsGO = new GameObject("StatusEffects");
            effectsGO.transform.SetParent(parent, false);
            
            var effectsRect = effectsGO.AddComponent<RectTransform>();
            // Position at the bottom of the member UI
            effectsRect.anchorMin = new Vector2(0, 0);
            effectsRect.anchorMax = new Vector2(1, 0);
            effectsRect.pivot = new Vector2(0, 0);
            effectsRect.offsetMin = new Vector2(4, 2);
            effectsRect.offsetMax = new Vector2(-4, 2 + STATUS_EFFECT_ROW_HEIGHT * 2); // Allow for 2 rows
            
            // Use GridLayoutGroup for wrapping to multiple rows
            var gridLayout = effectsGO.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(STATUS_EFFECT_ICON_SIZE + 16f, STATUS_EFFECT_ROW_HEIGHT);
            gridLayout.spacing = new Vector2(2f, 2f);
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = MAX_EFFECTS_PER_ROW;
            
            // Add ContentSizeFitter to auto-size based on content
            var fitter = effectsGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            memberUI.StatusEffectContainer = effectsGO.transform;
        }
        
        /// <summary>
        /// Creates or gets a status effect icon UI element.
        /// </summary>
        private StatusEffectIconUI CreateStatusEffectIcon(Transform parent)
        {
            var iconUI = new StatusEffectIconUI();
            
            iconUI.Root = new GameObject("EffectIcon");
            iconUI.Root.transform.SetParent(parent, false);
            
            var rootRect = iconUI.Root.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(STATUS_EFFECT_ICON_SIZE + 16f, STATUS_EFFECT_ROW_HEIGHT);
            
            var layout = iconUI.Root.AddComponent<LayoutElement>();
            layout.preferredWidth = STATUS_EFFECT_ICON_SIZE + 16f;
            layout.preferredHeight = STATUS_EFFECT_ROW_HEIGHT;
            
            // Icon (colored square)
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(iconUI.Root.transform, false);
            
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(STATUS_EFFECT_ICON_SIZE, STATUS_EFFECT_ICON_SIZE);
            
            iconUI.Icon = iconGO.AddComponent<Image>();
            iconUI.Icon.color = Color.white;
            iconUI.Icon.raycastTarget = false;
            
            // Timer text
            var timerGO = new GameObject("Timer");
            timerGO.transform.SetParent(iconUI.Root.transform, false);
            
            var timerRect = timerGO.AddComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0, 0);
            timerRect.anchorMax = new Vector2(1, 1);
            timerRect.offsetMin = new Vector2(STATUS_EFFECT_ICON_SIZE + 1, 0);
            timerRect.offsetMax = Vector2.zero;
            
            iconUI.TimerText = timerGO.AddComponent<TextMeshProUGUI>();
            iconUI.TimerText.fontSize = 8f;
            iconUI.TimerText.color = Color.white;
            iconUI.TimerText.alignment = TextAlignmentOptions.MidlineLeft;
            iconUI.TimerText.raycastTarget = false;
            
            GUIManager.ApplyGameFont(iconUI.TimerText);
            
            return iconUI;
        }
        
        #endregion
        
        #region Companion Tracking
        
        private void ScanForCompanions()
        {
            if (_localPlayer == null) return;
            
            long playerId = _localPlayer.GetPlayerID();
            
            // Find all companions following this player (alive)
            var newTracked = new List<CompanionController>();
            
            foreach (var companion in CompanionController.AllCompanions)
            {
                if (companion == null) continue;
                // CHANGED: Don't skip defeated companions - we'll show them grayed out
                // if (companion.isDefeated) continue;
                if (!companion.isTamed) continue;
                if (companion.ownerPlayerId != playerId) continue;
                
                // Only show following companions (or defeated ones that were following)
                if (!companion.ShouldBeFollowing) continue;
                
                newTracked.Add(companion);
            }
            
            // Also check vault for companions pending respawn that aren't in the world
            UpdatePendingRespawns(playerId);
            
            // Sort by name for consistent ordering
            newTracked.Sort((a, b) => string.Compare(a.GetDisplayName(), b.GetDisplayName()));
            
            // Check if list changed
            bool changed = newTracked.Count != _trackedCompanions.Count;
            if (!changed)
            {
                for (int i = 0; i < newTracked.Count; i++)
                {
                    if (newTracked[i] != _trackedCompanions[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }
            
            if (changed)
            {
                UpdateTrackedCompanions(newTracked);
            }
            
            // Always update pending respawn UIs (for dead companions not in world)
            UpdatePendingRespawnUIs();
        }
        
        /// <summary>
        /// Updates the list of companions pending respawn from the vault.
        /// </summary>
        private void UpdatePendingRespawns(long playerId)
        {
            try
            {
                // Phase 6 (save refactor): read from the resolver instead
                // of poking the vault directly. The resolver prefers the
                // per-player roster on m_customData which is updated
                // synchronously on death (CompanionRosterWriter.OnDeathWithSnapshot)
                // ï¿½ vault is now only a periodic debug mirror and would
                // be up to 30 s stale, which used to cause the HUD entry
                // for a just-killed companion to disappear until the
                // next flush.
                var companions = CompanionSavedDataResolver.ResolveAllForRosterUI(playerId);
                if (companions == null) return;
                
                // Clear old pending respawns that might have completed
                var toRemove = new List<string>();
                foreach (var kvp in _pendingRespawns)
                {
                    // Check if this companion exists in the world now
                    bool existsInWorld = false;
                    foreach (var c in CompanionController.AllCompanions)
                    {
                        if (c != null && c.companionId == kvp.Key && !c.isDefeated)
                        {
                            existsInWorld = true;
                            break;
                        }
                    }
                    
                    if (existsInWorld || Time.time > kvp.Value.RespawnTime)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var id in toRemove)
                {
                    _pendingRespawns.Remove(id);
                }
                
                // Add new pending respawns from vault
                foreach (var data in companions)
                {
                    // INCLUSIVE FILTER: show every companion that is currently
                    // pending respawn UNLESS it was a stationed-as-NPC (i.e.
                    // intentionally placed at a fixed station ï¿½ not a follow
                    // companion).  We deliberately do NOT additionally require
                    // data.IsFollowing here.
                    //
                    // Reason: at the moment the companion's death-state was
                    // saved, BuildSaveData read CompanionController.GetPersistentFollowIntent,
                    // which historically returned false whenever the owner-side
                    // PlayerFollowingRegistry was empty ï¿½ and that registry is
                    // wiped on every player death+respawn (it lives on the
                    // player ZDO, which is replaced).  So a companion who dies
                    // SHORTLY AFTER the player respawned would get IsFollowing=false
                    // saved to vault even though the owner never told them to
                    // stop following.  Those companions then dropped out of the
                    // group HUD entirely, which is the "they just disappear when
                    // they die" symptom.
                    //
                    // The companion-side ZDO read in GetPersistentFollowIntent
                    // has been fixed to fall through and check the surviving
                    // companion ZDO flag, so future death saves should record
                    // IsFollowing correctly ï¿½ but the HUD filter staying strict
                    // would still hide ANY existing vault entries that were
                    // saved during the broken window.  Drop the filter.
                    bool isStationed = data.IsStationedAsNpc;
                        if (data.IsPendingRespawn && !isStationed)
                        {
                            // Only show companions that were following the player.
                            // Use the registry as the authoritative source (registry
                            // reliability issues are now fixed). Fall back to the
                            // vault flag for legacy entries written before the fix.
                            bool wasFollowing = (_localPlayer != null &&
                                                PlayerFollowingRegistry.IsFollowing(_localPlayer, data.CompanionId))
                                               || data.IsFollowing;
                            if (!wasFollowing) continue;

                            // Phase 6: the resolver projects the roster's
                        // wall-clock RespawnDeadlineUtcTicks into
                        // data.RespawnTimeRemaining so it already IS the
                        // live remaining time. We must NOT subtract
                        // (now - DeathTimestamp) again \u2014 that's the
                        // legacy vault calculation and would zero out
                        // every entry post-Phase-6.
                        float remainingTime = data.RespawnTimeRemaining;

                        if (remainingTime > 0 && !_pendingRespawns.ContainsKey(data.CompanionId))
                        {
                            _pendingRespawns[data.CompanionId] = new PendingRespawnInfo
                            {
                                CompanionId = data.CompanionId,
                                CompanionName = data.CompanionName ?? data.DisplayNameOverride ?? "Companion",
                                RespawnTime = Time.time + remainingTime
                            };
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GroupHudController] Error updating pending respawns: {ex.Message}");
            }
        }
        
        private void UpdateTrackedCompanions(List<CompanionController> newCompanions)
        {
            // Remove UIs for companions no longer tracked
            var toRemove = new List<CompanionController>();
            foreach (var kvp in _memberUIs)
            {
                if (!newCompanions.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var companion in toRemove)
            {
                if (_memberUIs.TryGetValue(companion, out var ui))
                {
                    if (ui.Root != null)
                    {
                        Destroy(ui.Root);
                    }
                    _memberUIs.Remove(companion);
                }
            }
            
            // Add UIs for new companions
            foreach (var companion in newCompanions)
            {
                if (!_memberUIs.ContainsKey(companion))
                {
                    var ui = CreateMemberUI(companion);
                    _memberUIs[companion] = ui;
                }
            }
            
            // Reorder UI elements to match list order
            for (int i = 0; i < newCompanions.Count; i++)
            {
                if (_memberUIs.TryGetValue(newCompanions[i], out var ui))
                {
                    ui.Root.transform.SetSiblingIndex(i);
                }
            }

            _trackedCompanions = newCompanions;
            RebuildLayout();
        }
        
        /// <summary>
        /// Updates UI elements for companions that are pending respawn but not in the world.
        /// These show as greyed out entries with respawn timers.
        /// </summary>
        private void UpdatePendingRespawnUIs()
        {
            // Build set of companion IDs that are in the world
            var worldCompanionIds = new HashSet<string>();
            foreach (var companion in _trackedCompanions)
            {
                if (companion != null && !string.IsNullOrEmpty(companion.companionId))
                {
                    worldCompanionIds.Add(companion.companionId);
                }
            }
            
            // Remove UIs for pending respawns that are now in the world or no longer pending
            var toRemove = new List<string>();
            foreach (var kvp in _pendingRespawnUIs)
            {
                // Remove if companion is now in world or no longer in pending respawns
                if (worldCompanionIds.Contains(kvp.Key) || !_pendingRespawns.ContainsKey(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var id in toRemove)
            {
                if (_pendingRespawnUIs.TryGetValue(id, out var ui))
                {
                    if (ui.Root != null)
                    {
                        Destroy(ui.Root);
                    }
                    _pendingRespawnUIs.Remove(id);
                }
            }
            
            // Create UIs for pending respawns not in world and not already having UI
            foreach (var kvp in _pendingRespawns)
            {
                // Skip if this companion is in the world (has a live CompanionController)
                if (worldCompanionIds.Contains(kvp.Key)) continue;

                // Skip if we already have UI for this pending respawn
                if (_pendingRespawnUIs.ContainsKey(kvp.Key)) continue;

                // Create UI for pending respawn
                var ui = CreatePendingRespawnUI(kvp.Value);
                _pendingRespawnUIs[kvp.Key] = ui;
            }
            RebuildLayout();
        }
        
        /// <summary>
        /// Creates a member UI for a companion that is pending respawn (not in world).
        /// </summary>
        private MemberUI CreatePendingRespawnUI(PendingRespawnInfo info)
        {
            var memberUI = new MemberUI();
            
            // Member container
            var memberGO = new GameObject($"Member_Pending_{info.CompanionName}");
            memberGO.transform.SetParent(_column1Container.transform, false);
            memberUI.Root = memberGO;
            
            var memberRect = memberGO.AddComponent<RectTransform>();
            
            // Layout element for height
            var layoutElement = memberGO.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = MEMBER_HEIGHT + STATUS_EFFECT_ROW_HEIGHT;
            layoutElement.minHeight = MEMBER_HEIGHT + STATUS_EFFECT_ROW_HEIGHT;
            
            // Background
            var memberBg = memberGO.AddComponent<Image>();
            memberBg.color = new Color(0.05f, 0.05f, 0.08f, 0.5f);
            memberBg.raycastTarget = false;
            
            // Name row (name + respawn timer)
            CreateNameRow(memberGO.transform, memberUI);
            
            // Stat bars
            CreateStatBars(memberGO.transform, memberUI);
            
            // Status effect row (empty for pending respawns)
            CreateStatusEffectRow(memberGO.transform, memberUI);
            
            // Set as dead/pending state immediately
            memberUI.IsDead = true;
            memberUI.NameText.text = info.CompanionName;
            memberUI.NameText.color = DeadNameColor;
            memberUI.DistanceText.gameObject.SetActive(false);
            memberUI.RespawnTimerText.gameObject.SetActive(true);
            
            // Gray out bars
            SetBarFill(memberUI.HealthBarFill, 0);
            SetBarFill(memberUI.StaminaBarFill, 0);
            memberUI.EitrBarRoot.SetActive(false);
            memberUI.HealthBarFill.color = DeadBarColor;
            memberUI.StaminaBarFill.color = DeadBarColor;
            
            return memberUI;
        }

        #endregion

        #region Party Members

        /// <summary>
        /// Reconciles <see cref="_partyMemberUIs"/> with the local
        /// player's current <see cref="PartyData"/>. Adds rows for new
        /// members and removes rows for members no longer in the
        /// party. Self is intentionally excluded \u2014 the player has
        /// the vanilla HUD for their own bars.
        /// </summary>
        private void ScanForPartyMembers()
        {
            if (_localPlayer == null) return;

            var party = PartyManager.GetLocalParty();
            long selfId = _localPlayer.GetPlayerID();

            var desired = new HashSet<long>();
            if (party != null && party.Members != null)
            {
                foreach (var m in party.Members)
                {
                    if (m == null) continue;
                    if (m.PlayerId == 0 || m.PlayerId == selfId) continue;
                    desired.Add(m.PlayerId);
                }
            }

            // Drop rows for ex-members.
            var toRemove = new List<long>();
            foreach (var kvp in _partyMemberUIs)
            {
                if (!desired.Contains(kvp.Key)) toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                if (_partyMemberUIs.TryGetValue(id, out var ui) && ui.Root != null)
                    Destroy(ui.Root);
                _partyMemberUIs.Remove(id);
            }

            // Create rows for new members.
            if (party != null && party.Members != null)
            {
                foreach (var m in party.Members)
                {
                    if (m == null || m.PlayerId == 0 || m.PlayerId == selfId) continue;
                    if (_partyMemberUIs.ContainsKey(m.PlayerId)) continue;
                    _partyMemberUIs[m.PlayerId] = CreatePartyMemberUI(m);
                }
            }
            RebuildLayout();
        }

        /// <summary>
        /// Per-tick refresh of every party-member row: name colour,
        /// online / offline tag, distance, and HP/Stamina/Eitr bars
        /// when the remote player's <see cref="Player"/> instance is
        /// loaded locally (i.e. they're inside our active-zone radius).
        /// </summary>
        private void UpdatePartyMemberBars()
        {
            if (_localPlayer == null) return;
            Vector3 myPos = _localPlayer.transform.position;

            var party = PartyManager.GetLocalParty();
            if (party == null) return;

            foreach (var kvp in _partyMemberUIs)
            {
                long memberId = kvp.Key;
                var ui = kvp.Value;
                if (ui == null || ui.Root == null) continue;

                var memberInfo = party.FindMember(memberId);
                string memberName = memberInfo?.PlayerName ?? "?";
                bool isLeader = memberId == party.LeaderPlayerId;
                bool isOnline = PartyManager.IsPlayerOnline(memberId);
                var liveInstance = isOnline ? PartyManager.FindLocalPlayerInstance(memberId) : null;

                // Name + leader badge + (offline) suffix.
                string display = isLeader ? $"\u2605 {memberName}" : memberName;
                if (!isOnline) display += "  <size=70%>(offline)</size>";
                ui.NameText.text = display;
                ui.NameText.color = isOnline ? NameColor : DeadNameColor;

                // Distance only when we can resolve a live position.
                if (liveInstance != null)
                {
                    float dist = Vector3.Distance(myPos, liveInstance.transform.position);
                    ui.DistanceText.text = $"{dist:F0}m";
                    ui.DistanceText.gameObject.SetActive(true);
                }
                else
                {
                    ui.DistanceText.gameObject.SetActive(false);
                }

                // Hide the respawn-timer label \u2014 it's repurposed from the
                // companion row template; party rows don't use it.
                if (ui.RespawnTimerText != null)
                    ui.RespawnTimerText.gameObject.SetActive(false);

                // Bars: only render when we have a live local Player
                // instance for this member (i.e. they're in our active
                // zone). Otherwise gray everything out.
                if (liveInstance != null)
                {
                    float hpPct = liveInstance.GetMaxHealth() > 0f ? liveInstance.GetHealth() / liveInstance.GetMaxHealth() : 0f;
                    float stPct = liveInstance.GetMaxStamina() > 0f ? liveInstance.GetStamina() / liveInstance.GetMaxStamina() : 0f;
                    SetBarFill(ui.HealthBarFill, hpPct);
                    SetBarFill(ui.StaminaBarFill, stPct);
                    ui.HealthBarFill.color = HealthBarColor;
                    ui.StaminaBarFill.color = StaminaBarColor;

                    float maxEitr = liveInstance.GetMaxEitr();
                    if (maxEitr > 0f)
                    {
                        ui.EitrBarRoot.SetActive(true);
                        SetBarFill(ui.EitrBarFill, liveInstance.GetEitr() / maxEitr);
                        ui.EitrBarFill.color = EitrBarColor;
                    }
                    else
                    {
                        ui.EitrBarRoot.SetActive(false);
                    }
                }
                else
                {
                    SetBarFill(ui.HealthBarFill, 0);
                    SetBarFill(ui.StaminaBarFill, 0);
                    ui.HealthBarFill.color = DeadBarColor;
                    ui.StaminaBarFill.color = DeadBarColor;
                    ui.EitrBarRoot.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Creates a single party-member row. Mirrors the layout of
        /// <see cref="CreatePendingRespawnUI"/> so that the visual
        /// hierarchy of the HUD is consistent across companion rows
        /// and player rows.
        /// </summary>
        private MemberUI CreatePartyMemberUI(PartyMember member)
        {
            var memberUI = new MemberUI();

            var memberGO = new GameObject($"Member_Party_{member.PlayerId}");
            memberGO.transform.SetParent(_column1Container.transform, false);
            memberUI.Root = memberGO;

            memberGO.AddComponent<RectTransform>();

            var layoutElement = memberGO.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = MEMBER_HEIGHT;
            layoutElement.minHeight = MEMBER_HEIGHT;

            var memberBg = memberGO.AddComponent<Image>();
            memberBg.color = new Color(0.05f, 0.08f, 0.05f, 0.5f);
            memberBg.raycastTarget = false;
            memberUI.MemberBackground = memberBg;

            CreateNameRow(memberGO.transform, memberUI);
            CreateStatBars(memberGO.transform, memberUI);

            memberUI.NameText.text = member?.PlayerName ?? "?";
            memberUI.NameText.color = NameColor;
            memberUI.RespawnTimerText.gameObject.SetActive(false);

            return memberUI;
        }

        #endregion

        #region UI Updates

        private void UpdateMemberBars()
        {
            if (_localPlayer == null) return;

            Vector3 playerPos = _localPlayer.transform.position;
            
            // Update UIs for live companions
            foreach (var kvp in _memberUIs)
            {
                var companion = kvp.Key;
                var ui = kvp.Value;
                
                if (companion == null || ui.Root == null) continue;
                
                // Check if companion is dead/defeated
                bool isDead = companion.isDefeated || (companion.GetCharacter()?.IsDead() ?? false);
                ui.IsDead = isDead;
                
                // Update name
                ui.NameText.text = companion.GetDisplayName();
                ui.NameText.color = isDead ? DeadNameColor : NameColor;
                
                if (isDead)
                {
                    // Show respawn timer instead of distance
                    ui.DistanceText.gameObject.SetActive(false);
                    
                    // Check for respawn time
                    float respawnTime = CompanionDeathHandler.GetRespawnTime(companion.companionId);
                    if (respawnTime > 0)
                    {
                        float remaining = respawnTime - Time.time;
                        if (remaining > 0)
                        {
                            ui.RespawnTimerText.text = $"({remaining:F0}s)";
                            ui.RespawnTimerText.gameObject.SetActive(true);
                        }
                        else
                        {
                            ui.RespawnTimerText.text = "(Respawning...)";
                            ui.RespawnTimerText.gameObject.SetActive(true);
                        }
                    }
                    else
                    {
                        ui.RespawnTimerText.text = "(Dead)";
                        ui.RespawnTimerText.gameObject.SetActive(true);
                    }
                    
                    // Gray out bars for dead companions
                    SetBarFill(ui.HealthBarFill, 0);
                    SetBarFill(ui.StaminaBarFill, 0);
                    ui.EitrBarRoot.SetActive(false);
                    
                    // Darken the health bar color to indicate death
                    ui.HealthBarFill.color = DeadBarColor;
                    ui.StaminaBarFill.color = DeadBarColor;
                }
                else
                {
                    // Show distance for alive companions
                    ui.DistanceText.gameObject.SetActive(true);
                    ui.RespawnTimerText.gameObject.SetActive(false);
                    
                    float distance = Vector3.Distance(playerPos, companion.transform.position);
                    ui.DistanceText.text = $"({distance:F0}m)";
                    
                    // Restore bar colors
                    ui.HealthBarFill.color = HealthBarColor;
                    ui.StaminaBarFill.color = StaminaBarColor;
                    
                    // CRITICAL: Get stats directly from CompanionStats for most accurate values
                    // This bypasses the calculator and gets the authoritative current/max values
                    var stats = companion.GetStats();
                    
                    if (stats != null)
                    {
                        // Health - use CompanionStats values directly
                        float currentHealth = stats.CurrentHealth;
                        float maxHealth = stats.MaxHealth;
                        float healthPercent = maxHealth > 0 ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
                        SetBarFill(ui.HealthBarFill, healthPercent);
                        
                        // Stamina - use CompanionStats values directly
                        float currentStamina = stats.CurrentStamina;
                        float maxStamina = stats.MaxStamina;
                        float staminaPercent = maxStamina > 0 ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
                        SetBarFill(ui.StaminaBarFill, staminaPercent);
                        
                        // Eitr (show/hide based on max eitr)
                        float maxEitr = stats.MaxEitr;
                        if (maxEitr > 0)
                        {
                            ui.EitrBarRoot.SetActive(true);
                            float currentEitr = stats.CurrentEitr;
                            float eitrPercent = Mathf.Clamp01(currentEitr / maxEitr);
                            SetBarFill(ui.EitrBarFill, eitrPercent);
                        }
                        else
                        {
                            ui.EitrBarRoot.SetActive(false);
                        }
                    }
                    else
                    {
                        // Fallback to calculator if CompanionStats not available
                        var calc = new CompanionStatCalculator(companion);
                        if (calc.IsValid)
                        {
                            SetBarFill(ui.HealthBarFill, calc.HealthPercentage);
                            SetBarFill(ui.StaminaBarFill, calc.StaminaPercentage);
                            
                            if (calc.MaxEitr > 0)
                            {
                                ui.EitrBarRoot.SetActive(true);
                                SetBarFill(ui.EitrBarFill, calc.EitrPercentage);
                            }
                            else
                            {
                                ui.EitrBarRoot.SetActive(false);
                            }
                        }
                        else
                        {
                            SetBarFill(ui.HealthBarFill, 0);
                            SetBarFill(ui.StaminaBarFill, 0);
                            ui.EitrBarRoot.SetActive(false);
                        }
                    }
                    
                    // Update status effects
                    UpdateStatusEffects(companion, ui);
                }
            }
            
            // Update UIs for pending respawn companions (not in world)
            foreach (var kvp in _pendingRespawnUIs)
            {
                var ui = kvp.Value;
                if (ui.Root == null) continue;
                
                // Get the pending respawn info
                if (_pendingRespawns.TryGetValue(kvp.Key, out var info))
                {
                    // Update respawn timer
                    float remaining = info.RespawnTime - Time.time;
                    if (remaining > 0)
                    {
                        ui.RespawnTimerText.text = $"({remaining:F0}s)";
                    }
                    else
                    {
                        ui.RespawnTimerText.text = "(Respawning...)";
                    }
                }
            }

            UpdatePartyMemberBars();
        }

        /// <summary>
        /// Sets a stat bar's fill percentage using anchor-based sizing.
        /// The fill bar stretches from the left edge (anchorMin.x = 0) to the percentage point (anchorMax.x = percent).
        /// This approach ensures the bar dynamically scales with its container - no hardcoded widths.
        /// </summary>
        private void SetBarFill(Image fillImage, float percent)
        {
            if (fillImage == null) return;
            
            // CRITICAL: Use anchors for percentage-based fill
            // anchorMin = (0, 0) = bottom-left of parent
            // anchorMax = (percent, 1) = percent across, full height
            // No offsets - anchors alone determine size
            percent = Mathf.Clamp01(percent);
            var rect = fillImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(percent, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        
        #endregion
        
        #region Helpers
        
        /// <summary>
        /// Updates the status effect icons for a companion.
        /// </summary>
        private void UpdateStatusEffects(CompanionController companion, MemberUI ui)
        {
            if (companion == null || ui == null || ui.StatusEffectContainer == null) return;
            
            var character = companion.GetCharacter();
            if (character == null) return;
            
            var seman = character.GetSEMan();
            if (seman == null) return;
            
            // Get active companion status effects
            var activeEffects = new List<StatusEffect>();
            foreach (var effect in seman.GetStatusEffects())
            {
                if (effect == null) continue;
                
                // Check if it's one of our companion effects
                if (IsCompanionEffect(effect.name))
                {
                    activeEffects.Add(effect);
                }
            }
            
            // Ensure we have enough effect icons
            while (ui.StatusEffectIcons.Count < activeEffects.Count)
            {
                ui.StatusEffectIcons.Add(CreateStatusEffectIcon(ui.StatusEffectContainer));
            }
            
            // Update or hide icons
            for (int i = 0; i < ui.StatusEffectIcons.Count; i++)
            {
                var icon = ui.StatusEffectIcons[i];
                
                if (i < activeEffects.Count)
                {
                    var effect = activeEffects[i];
                    icon.Root.SetActive(true);
                    
                    // Update icon color based on effect
                    icon.Icon.color = GetEffectColor(effect.name);
                    icon.EffectName = effect.name;
                    
                    // Update timer
                    float remaining = effect.GetRemaningTime();
                    if (remaining > 0)
                    {
                        icon.TimerText.text = remaining > 60 ? $"{remaining / 60:F0}m" : $"{remaining:F0}";
                    }
                    else
                    {
                        icon.TimerText.text = "?";
                    }
                }
                else
                {
                    icon.Root.SetActive(false);
                }
            }
        }
        
        /// <summary>
        /// Checks if an effect is a companion-related status effect.
        /// </summary>
        private bool IsCompanionEffect(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) return false;
            
            // Check for our custom effects
            return effectName.StartsWith("Companion") ||
                   effectName == StatusEffectManager.EFFECT_FORTIFY ||
                   effectName == StatusEffectManager.EFFECT_DIVINE_PROTECTION ||
                   effectName == StatusEffectManager.EFFECT_HOLY_SMITE ||
                   effectName == StatusEffectManager.EFFECT_BERSERK_RAGE ||
                   effectName == StatusEffectManager.EFFECT_WARCRY ||
                   effectName == StatusEffectManager.EFFECT_STEALTH ||
                   effectName == StatusEffectManager.EFFECT_HUNTERS_MARK ||
                   effectName == StatusEffectManager.EFFECT_EAGLE_EYE ||
                   effectName == StatusEffectManager.EFFECT_ELEMENTAL_INFUSION ||
                   effectName == StatusEffectManager.EFFECT_ARCANE_SHIELD ||
                   effectName == StatusEffectManager.EFFECT_PURIFY ||
                   effectName == StatusEffectManager.EFFECT_SANCTUARY ||
                   effectName == StatusEffectManager.EFFECT_PURIFYING_CIRCLE ||
                   effectName == StatusEffectManager.EFFECT_CHI_STRIKE ||
                   effectName == StatusEffectManager.EFFECT_INNER_PEACE ||
                   effectName == "CompanionTaunting";
        }
        
        /// <summary>
        /// Gets the display color for a status effect.
        /// </summary>
        private Color GetEffectColor(string effectName)
        {
            switch (effectName)
            {
                case StatusEffectManager.EFFECT_FORTIFY: return new Color(0.3f, 0.3f, 0.8f);
                case StatusEffectManager.EFFECT_DIVINE_PROTECTION: return new Color(1f, 0.85f, 0.4f);
                case StatusEffectManager.EFFECT_HOLY_SMITE: return new Color(1f, 1f, 0.6f);
                case StatusEffectManager.EFFECT_BERSERK_RAGE: return new Color(0.8f, 0.2f, 0.2f);
                case StatusEffectManager.EFFECT_WARCRY: return new Color(0.9f, 0.3f, 0.1f);
                case StatusEffectManager.EFFECT_STEALTH: return new Color(0.3f, 0.3f, 0.4f);
                case StatusEffectManager.EFFECT_HUNTERS_MARK: return new Color(0.8f, 0.2f, 0.2f);
                case StatusEffectManager.EFFECT_EAGLE_EYE: return new Color(0.4f, 0.7f, 0.3f);
                case StatusEffectManager.EFFECT_ELEMENTAL_INFUSION: return new Color(0.5f, 0.3f, 0.8f);
                case StatusEffectManager.EFFECT_ARCANE_SHIELD: return new Color(0.3f, 0.5f, 0.9f);
                case StatusEffectManager.EFFECT_PURIFY: return new Color(0.8f, 0.8f, 0.2f);
                case StatusEffectManager.EFFECT_SANCTUARY: return new Color(0.8f, 0.8f, 1f);
                case StatusEffectManager.EFFECT_PURIFYING_CIRCLE: return new Color(0.9f, 0.9f, 0.5f);
                case StatusEffectManager.EFFECT_CHI_STRIKE: return new Color(0.2f, 0.8f, 0.9f);
                case StatusEffectManager.EFFECT_INNER_PEACE: return new Color(0.3f, 0.9f, 0.5f);
                case "CompanionTaunting": return new Color(1f, 0.6f, 0.2f);
                default: return Color.white;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Helper component for handling Group HUD drag events using Unity's event system.
    /// This is the same pattern used by NpcMainPanelController's PanelDragHandler.
    /// </summary>
    public class GroupHudDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GroupHudController _controller;
        
        public void Initialize(GroupHudController controller)
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
    }
}
