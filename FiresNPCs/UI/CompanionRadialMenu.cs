using HarmonyLib;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresCore.Npc;
using FiresCore.Npc.AI;
using FiresCore.Npc.Movement;
using FiresCore.Npc.IdleBehaviors;
using FiresRPGmaker.Modules.Companions;
using FiresRPGmaker.Modules.Companions.NpcMode;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Core;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Companions;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Radial menu for companion commands.
    /// Allows quick access to common companion actions without memorizing hotkeys.
    /// 
    /// USAGE:
    /// - Hold R (configurable) while hovering over companion to open
    /// - Move mouse/stick to select action
    /// - Release R to execute action
    /// 
    /// DESIGN:
    /// - Outer ring: Main actions (Follow, Inventory, Work modes, etc.)
    /// - Inner ring: Combat stances (Balanced, Aggressive, Defensive, etc.)
    /// - Animated open/close with staggered segment reveal
    /// </summary>
    public class CompanionRadialMenu : MonoBehaviour
    {
        #region Action IDs
        
        // Main actions (outer ring)
        private const int ActionFollow = 0;
        private const int ActionGatherResources = 1;
        private const int ActionTendFires = 2;
        private const int ActionOperateSmelters = 3;
        private const int ActionDepositItems = 4;
        private const int ActionInventory = 5;
        private const int ActionStats = 6;
        private const int ActionWander = 7;
        private const int ActionDismiss = 8;
        private const int ActionSetHome = 9;
        private const int ActionCooking = 10;
        private const int ActionOrganizeChests = 11;
        private const int ActionCrafting = 12;
        private const int ActionLootPickup = 13;
        private const int ActionHunt = 14;
        private const int ActionGuildAccess = 15;

        // Archetype selection (inner ring) - based on ArchetypeClass enum values
        private const int ActionArchetypeBase = 20;
        // ActionArchetypeBase + (int)ArchetypeClass gives the action ID
        
        #endregion
        
        #region Colors
        
        private static readonly Color BgColor = new Color(0.14f, 0.11f, 0.08f, 0.82f);
        private static readonly Color HighlightHover = new Color(0.83f, 0.64f, 0.31f, 0.3f);
        private static readonly Color HighlightActive = new Color(0.45f, 0.35f, 0.18f, 0.15f);
        private static readonly Color TextNormal = new Color(0.85f, 0.8f, 0.65f, 1f);
        private static readonly Color TextHover = new Color(1f, 0.95f, 0.8f, 1f);
        private static readonly Color ActiveDot = new Color(0.4f, 0.85f, 0.4f, 1f);
        private static readonly Color InactiveDot = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        
        #endregion
        
        #region Layout Constants
        
        private const float RingRadius = 210f;
        private const float SegSize = 100f;
        private const float HighlightSize = 96f;
        private const float IconSize = 76f;
        private const float DeadZonePx = 30f;
        private const float DeadZoneStick = 0.3f;
        
        private const float InnerRingRadius = 80f;
        private const float InnerSegSize = 72f;
        private const float InnerIconSize = 54f;
        private const float InnerHighlightSize = 68f;
        
        private const float AnimDuration = 0.45f;
        private const float SegStagger = 0.04f;
        private const float BgAnimFraction = 0.4f;
        
        #endregion
        
        #region State
        
        private CompanionController _companion;
        private ZNetView _companionNview;
        private CompanionAI _companionAI;
        private CompanionIdleBehavior _companionIdleBehavior;
        private CompanionCombatMovement _companionCombatMovement;
        
        private bool _visible;
        private bool _built;
        
        private Canvas _canvas;
        private GameObject _root;
        private GameObject _bgCircle;
        private GameObject _innerBgCircle;
        private GameObject _centerContainer;
        private TextMeshProUGUI _centerName;
        private TextMeshProUGUI _centerAction;
        private TextMeshProUGUI _centerState;
        
        private readonly List<Segment> _segments = new List<Segment>();
        private readonly List<GameObject> _segmentGOs = new List<GameObject>();
        private readonly List<Image> _segHighlights = new List<Image>();
        private readonly List<Image> _segIcons = new List<Image>();
        private readonly List<TextMeshProUGUI> _segLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> _segDots = new List<Image>();
        private int _hoveredIndex = -1;
        
        private readonly List<Segment> _innerSegments = new List<Segment>();
        private readonly List<GameObject> _innerSegmentGOs = new List<GameObject>();
        private readonly List<Image> _innerHighlights = new List<Image>();
        private readonly List<Image> _innerIcons = new List<Image>();
        private readonly List<TextMeshProUGUI> _innerLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> _innerDots = new List<Image>();
        private readonly List<CanvasGroup> _innerCanvasGroups = new List<CanvasGroup>();
        private readonly List<RectTransform> _innerRTs = new List<RectTransform>();
        private readonly List<Vector2> _innerFinalPositions = new List<Vector2>();
        private int _hoveredInner = -1;
        
        private bool _useReleasedSinceOpen;
        private AnimState _animState;
        private float _animTimer;
        
        private CanvasGroup _bgCanvasGroup;
        private CanvasGroup _innerBgCanvasGroup;
        private CanvasGroup _centerCanvasGroup;
        private readonly List<CanvasGroup> _segCanvasGroups = new List<CanvasGroup>();
        private readonly List<RectTransform> _segRTs = new List<RectTransform>();
        private readonly List<Vector2> _segFinalPositions = new List<Vector2>();
        
        private static readonly Dictionary<int, Sprite> _iconCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<string, Sprite> _valheimSpriteCache = new Dictionary<string, Sprite>();
        private static Sprite _circleSprite;
        private static Sprite _donutSprite;
        private static bool _spriteCacheInitialized;
        
        // Hybrid archetype selection
        private const int ActionHybridArchetypeBase = 100; // 100+ reserved for hybrid archetypes
        
        // Billboard visibility state - track if we hid it so we can restore it
        private bool _hidBillboard;
        private GameObject _cachedBillboardGui;
        
        #endregion
        
        #region Singleton
        
        public static CompanionRadialMenu Instance { get; private set; }
        
        public bool IsVisible => _visible;
        public CompanionController CurrentCompanion => _companion;
        
        public static bool IsOpenFor(CompanionController companion)
        {
            return Instance != null && 
                   Instance._visible && 
                   Instance._companion != null && 
                   Instance._companion == companion;
        }
        
        /// <summary>
        /// Ensures the radial menu instance exists.
        /// </summary>
        public static void EnsureInstance()
        {
            if (Instance != null) return;
            
            var go = new GameObject("VA_CompanionRadialMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CompanionRadialMenu>();
        }
        
        /// <summary>
        /// Pre-warms the icon cache for faster first open.
        /// </summary>
        public static void WarmCache()
        {
            // Initialize Valheim sprite cache first
            InitializeValheimSpriteCache();
            
            GetCircleSprite();
            GetDonutSprite();
            
            // Pre-cache outer ring icons (work behaviors now live in CompanionIdleBehaviorRadial)
            int[] outerActionIds = {
                ActionFollow, ActionInventory, ActionStats, ActionSetHome, ActionDismiss
            };
            
            foreach (int actionId in outerActionIds)
                GetActionIcon(actionId);
            
            // Pre-cache archetype icons
            for (int i = 0; i <= (int)ArchetypeClass.Monk; i++)
                GetActionIcon(ActionArchetypeBase + i);
        }
        
        /// <summary>
        /// Initializes the Valheim sprite cache by scanning all loaded sprites.
        /// </summary>
        private static void InitializeValheimSpriteCache()
        {
            if (_spriteCacheInitialized) return;
            
            try
            {
                var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                foreach (var sprite in sprites)
                {
                    if (sprite != null && !string.IsNullOrEmpty(sprite.name) && !_valheimSpriteCache.ContainsKey(sprite.name))
                    {
                        _valheimSpriteCache[sprite.name] = sprite;
                    }
                }
                
                _spriteCacheInitialized = true;
                Debug.Log($"[RadialMenu] Cached {_valheimSpriteCache.Count} Valheim sprites");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RadialMenu] Failed to cache Valheim sprites: {e.Message}");
            }
        }
        
        /// <summary>
        /// Gets a Valheim sprite by name from the cache.
        /// </summary>
        private static Sprite GetValheimSprite(string spriteName)
        {
            if (!_spriteCacheInitialized)
                InitializeValheimSpriteCache();
            
            if (_valheimSpriteCache.TryGetValue(spriteName, out var sprite))
                return sprite;
            
            return null;
        }
        
        /// <summary>
        /// Tries multiple sprite names and returns the first one found.
        /// </summary>
        private static Sprite GetValheimSpriteWithFallbacks(params string[] spriteNames)
        {
            foreach (var name in spriteNames)
            {
                var sprite = GetValheimSprite(name);
                if (sprite != null) return sprite;
            }
            return null;
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            Instance = this;
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            
            Teardown();
            _iconCache.Clear();
            _circleSprite = null;
            _donutSprite = null;
        }
        
        private void Update()
        {
            if (!_visible) return;
            
            // Update animation
            if (_animState == AnimState.Opening || _animState == AnimState.Closing)
                UpdateAnimation();
            
            if (_animState == AnimState.Closing)
                return;
            
            // Validate companion still exists
            if (_companion == null)
            {
                Hide();
                return;
            }
            
            // Handle close keys
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetKeyDown(KeyCode.R) || ZInput.GetButtonDown("JoyBack"))
            {
                Hide();
                return;
            }
            
            // Track key release for execution
            if (!_useReleasedSinceOpen && !Input.GetKey(KeyCode.R) && !ZInput.GetButton("JoyUse"))
                _useReleasedSinceOpen = true;
            
            // Update selection based on mouse/stick
            UpdateSelection();
            
            // Execute action on click/release
            if (_animState == AnimState.Open && _useReleasedSinceOpen)
            {
                if (Input.GetMouseButtonDown(0) || ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse"))
                {
                    ExecuteSelectedAction();
                    RefreshSegmentStates();
                }
            }
            
            RefreshVisuals();
        }
        
        #endregion
        
        #region Show/Hide
        
        public void Show(CompanionController companion)
        {
            if (companion == null) return;
            
            _animState = AnimState.Closed;
            _companion = companion;
            _companionNview = companion.GetComponent<ZNetView>();
            _companionAI = companion.GetCompanionAI();
            _companionIdleBehavior = companion.GetComponent<CompanionIdleBehavior>();
            _companionCombatMovement = companion.GetComponent<CompanionCombatMovement>();

            companion.GetComponent<CompanionStateController>()?.ForceStopEmote();

            BuildSegments();
            
            if (!_built)
                BuildUI();
            
            PopulateUI();
            
            _root.SetActive(true);
            _visible = true;
            _hoveredIndex = -1;
            _hoveredInner = -1;
            _useReleasedSinceOpen = false;
            
            _animState = AnimState.Opening;
            _animTimer = 0f;
            InitAnimationState();
            
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // Hide companion's billboard (health bar/name) while menu is open
            SetCompanionBillboardVisible(false);
            
            Debug.Log($"[RadialMenu] Show Ã¯Â¿Â½ companion=\"{companion.companionName}\" segments={_segments.Count}");
        }
        
        public void Hide()
        {
            if (!_visible || _animState == AnimState.Closing)
                return;
            
            _animState = AnimState.Closing;
            _animTimer = 0f;
            _hoveredIndex = -1;
            _hoveredInner = -1;
            
            Debug.Log("[RadialMenu] Hide (closing)");
        }
        
        private void CompleteHide()
        {
            _visible = false;
            _animState = AnimState.Closed;
            
            // Restore companion's billboard visibility before clearing companion reference
            if (_hidBillboard && _cachedBillboardGui != null)
            {
                try
                {
                    _cachedBillboardGui.SetActive(true);
                }
                catch { }
                _hidBillboard = false;
                _cachedBillboardGui = null;
            }
            else if (_companion != null)
            {
                // Fallback: try to restore via the companion reference
                SetCompanionBillboardVisible(true);
            }
            
            if (_root != null)
                _root.SetActive(false);
            
            _companion = null;
            _companionNview = null;
            _companionAI = null;
            _companionIdleBehavior = null;
            _companionCombatMovement = null;
        }
        
        #endregion
        
        #region Selection
        
        private void UpdateSelection()
        {
            if (_segments.Count == 0) return;
            
            bool gamepad = ZInput.IsGamepadActive();
            Vector2 input;
            float deadZone;
            
            if (gamepad)
            {
                Vector2 left = new Vector2(ZInput.GetJoyLeftStickX(), ZInput.GetJoyLeftStickY());
                Vector2 right = new Vector2(ZInput.GetJoyRightStickX(), ZInput.GetJoyRightStickY());
                input = left.sqrMagnitude >= right.sqrMagnitude ? left : right;
                deadZone = DeadZoneStick;
            }
            else
            {
                input = (Vector2)Input.mousePosition - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                deadZone = DeadZonePx;
            }
            
            if (input.magnitude < deadZone)
            {
                _hoveredIndex = -1;
                _hoveredInner = -1;
                return;
            }
            
            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;
            
            float magnitude = input.magnitude;
            float innerThreshold = gamepad ? 0.55f : 116f;
            
            if (_innerSegments.Count > 0 && magnitude < innerThreshold)
            {
                _hoveredInner = ComputeSegmentIndex(angle, _innerSegments.Count);
                _hoveredIndex = -1;
            }
            else
            {
                _hoveredIndex = ComputeSegmentIndex(angle, _segments.Count);
                _hoveredInner = -1;
            }
        }
        
        private static int ComputeSegmentIndex(float angle, int count)
        {
            if (count <= 0) return -1;
            
            float segmentAngle = 360f / count;
            return Mathf.Clamp(
                Mathf.FloorToInt(((90f - angle + 360f + segmentAngle * 0.5f) % 360f) / segmentAngle),
                0, count - 1);
        }
        
        #endregion
        
        #region Action Execution
        
        private void ExecuteSelectedAction()
        {
            if (_companion == null || _companionNview == null) return;
            
            // Inner ring (archetype or hybrid selection)
            if (_hoveredInner >= 0 && _hoveredInner < _innerSegments.Count)
            {
                var segment = _innerSegments[_hoveredInner];
                EnsureOwnership();
                
                // Check if this is a hybrid or base archetype selection
                if (segment.ActionId >= ActionHybridArchetypeBase)
                {
                    // Hybrid archetype selection
                    ArchetypeClass subArchetype = (ArchetypeClass)(segment.ActionId - ActionHybridArchetypeBase);
                    SetHybridArchetype(subArchetype);
                    Debug.Log($"[RadialMenu] Set hybrid sub-archetype={subArchetype} label=\"{segment.Label}\"");
                }
                else if (segment.ActionId >= ActionArchetypeBase)
                {
                    // Base archetype selection
                    ArchetypeClass selectedArchetype = (ArchetypeClass)(segment.ActionId - ActionArchetypeBase);
                    SetArchetypeOverride(selectedArchetype);
                    Debug.Log($"[RadialMenu] Set archetype={selectedArchetype} label=\"{segment.Label}\"");
                }
                return;
            }
            
            // Outer ring (main actions)
            if (_hoveredIndex < 0 || _hoveredIndex >= _segments.Count) return;
            
            var seg = _segments[_hoveredIndex];
            EnsureOwnership();
            
            Debug.Log($"[RadialMenu] Execute action={seg.ActionId} label=\"{seg.Label}\"");
            
            switch (seg.ActionId)
            {
                case ActionFollow:
                    ToggleFollow();
                    break;
                case ActionInventory:
                    OpenInventory();
                    break;
                case ActionStats:
                    OpenStats();
                    break;
                case ActionSetHome:
                    DoSetHome();
                    break;
                case ActionDismiss:
                    DoDismiss();
                    break;
                case ActionGuildAccess:
                    DoToggleGuildAccess();
                    break;
            }
        }

        #endregion

        #region Action Implementations
        
        private void ToggleFollow()
        {
            if (_companionAI == null) return;
            
            var player = Player.m_localPlayer;
            if (player == null) return;
            
            if (_companionAI.ShouldBeFollowing)
            {
                // Currently following -> Stay
                _companion.CommandStay();
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, 
                    $"{_companion.GetDisplayName()}: Staying here");
            }
            else
            {
                // Currently staying -> Follow
                _companion.CommandFollow(player);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, 
                    $"{_companion.GetDisplayName()}: Following you");
            }
        }
        
        private void OpenInventory()
        {
            if (_companion == null) return;
            
            var player = Player.m_localPlayer;
            if (player == null) return;
            
            Hide();
            _companion.OpenInventory(player);
        }
        
        private void OpenStats()
        {
            if (_companion == null) return;
            
            var player = Player.m_localPlayer;
            if (player == null) return;
            
            Hide();
            
            // Open the dedicated CompanionStatsScreen
            // The stats screen is created as part of CompanionInventoryScreen but can be shown standalone
            var existingScreen = UnityEngine.Object.FindFirstObjectByType<CompanionStatsScreen>();
            if (existingScreen != null && existingScreen.gameObject.activeInHierarchy == false)
            {
                existingScreen.Show(_companion);
            }
            else
            {
                // Fallback: open inventory which has stats as a tab/section
                // The inventory screen shows stats in the right panel
                _companion.OpenInventory(player);
            }
        }
        
        private void DoSetHome()
        {
            if (_companionIdleBehavior == null) return;
            
            Vector3 position = _companion.transform.position;
            _companionIdleBehavior.SetHomePosition(position);
            
            if (_companionCombatMovement != null)
                _companionCombatMovement.SetHomePosition(position);
            
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                $"{_companion.GetDisplayName()}: Home set here");
        }
        
        private void DoDismiss()
        {
            if (_companion == null) return;

            Hide();
            _companion.ForceDismiss();
        }

        private void DoToggleGuildAccess()
        {
            if (_companion == null) return;
            var local = Player.m_localPlayer;
            if (local == null || !_companion.IsOwner(local))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    "Only the owner can change guild access.");
                return;
            }
            CompanionGuildAccess.Toggle(_companion);
            RefreshSegmentStates();
        }

        private void SetArchetypeOverride(ArchetypeClass archetype)
        {
            if (_companion == null) return;
            
            var archetypeController = _companion.GetComponent<ArchetypeController>();
            if (archetypeController == null)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                    $"{_companion.GetDisplayName()}: Cannot change archetype");
                return;
            }
            
            // Store override in ZDO
            var zdo = _companionNview?.GetZDO();
            if (zdo != null)
            {
                zdo.Set("companion_archetype_override", (int)archetype);
            }
            
            // Force the archetype change
            archetypeController.SetArchetype(archetype);
            
            string archetypeName = ArchetypeRegistry.GetDefinition(archetype)?.DisplayName ?? archetype.ToString();
            
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                $"{_companion.GetDisplayName()}: Archetype set to {archetypeName}");
        }
        
        private void SetHybridArchetype(ArchetypeClass subArchetype)
        {
            if (_companion == null) return;
            
            var archetypeController = _companion.GetComponent<ArchetypeController>();
            if (archetypeController == null)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                    $"{_companion.GetDisplayName()}: Cannot change hybrid");
                return;
            }
            
            var mainArchetype = archetypeController.CurrentArchetypeClass;
            var hybridDef = HybridArchetypeDefinitions.GetHybrid(mainArchetype, subArchetype);
            
            if (hybridDef == null)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                    $"{_companion.GetDisplayName()}: Invalid hybrid combination");
                return;
            }
            
            // Store sub-archetype override in ZDO for persistence
            var zdo = _companionNview?.GetZDO();
            if (zdo != null)
            {
                zdo.Set("companion_sub_archetype_override", (int)subArchetype);
            }
            
            // Apply the sub-archetype by triggering a re-evaluation
            // The ArchetypeController will pick up the equipment-based sub-archetype,
            // but we can influence it by ensuring the equipment supports this hybrid
            archetypeController.ForceReevaluate();
            
            // Refresh hybrid ability manager
            var hybridManager = _companion.GetComponent<HybridAbilityManager>();
            hybridManager?.RefreshHybridDefinition();
            
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
                $"{_companion.GetDisplayName()}: Now a {hybridDef.DisplayName}!");
        }
        
        private ArchetypeClass GetCurrentArchetype()
        {
            var archetypeController = _companion?.GetComponent<ArchetypeController>();
            return archetypeController?.CurrentArchetypeClass ?? ArchetypeClass.None;
        }
        
        /// <summary>
        /// Gets archetypes that are valid based on the companion's inventory.
        /// </summary>
        private List<ArchetypeClass> GetAvailableArchetypes()
        {
            var available = new List<ArchetypeClass>();
            if (_companion == null) return available;
            
            var inventory = _companion.GetInventory();
            if (inventory == null) return available;
            
            // Check equipped and storage items for equipment that supports each archetype
            var rightHand = inventory.GetEquippedItem(CompanionInventory.EquipmentSlot.RightHand);
            var leftHand = inventory.GetEquippedItem(CompanionInventory.EquipmentSlot.LeftHand);
            var rightBack = inventory.GetEquippedItem(CompanionInventory.EquipmentSlot.RightBack);
            var leftBack = inventory.GetEquippedItem(CompanionInventory.EquipmentSlot.LeftBack);
            var storage = inventory.GetStorageInventory();
            
            // Check for shield (Tank, Paladin)
            bool hasShield = (leftHand?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield) ||
                             (leftBack?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield) ||
                             HasItemTypeInStorage(storage, ItemDrop.ItemData.ItemType.Shield);
            
            // Check for bow (Ranger)
            bool hasBow = (rightHand?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow) ||
                          (leftHand?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow) ||
                          (rightBack?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow) ||
                          (leftBack?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow) ||
                          HasItemTypeInStorage(storage, ItemDrop.ItemData.ItemType.Bow);
            
            // Check for knives (Rogue)
            bool hasKnives = (rightHand?.m_shared?.m_skillType == Skills.SkillType.Knives) ||
                             HasSkillTypeInStorage(storage, Skills.SkillType.Knives);
            
            // Check for melee weapons
            bool hasMelee = (rightHand?.IsWeapon() == true && IsMeleeItem(rightHand)) ||
                            (rightBack?.IsWeapon() == true && IsMeleeItem(rightBack)) ||
                            HasMeleeInStorage(storage);
            
            // Check for staff (Mage, Healer)
            bool hasOffensiveStaff = ArchetypeUtils.IsOffensiveStaff(rightHand) || 
                                     ArchetypeUtils.IsOffensiveStaff(leftHand) ||
                                     HasOffensiveStaffInStorage(storage);
            bool hasSupportStaff = ArchetypeUtils.IsSupportStaff(rightHand) || 
                                   ArchetypeUtils.IsSupportStaff(leftHand) ||
                                   HasSupportStaffInStorage(storage);
            
            // Check for two-handed weapons (Berserker)
            bool hasTwoHanded = (rightHand?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon) ||
                                (rightHand?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft) ||
                                HasTwoHandedInStorage(storage);
            
            // Add available archetypes based on equipment
            if (hasShield && hasMelee)
            {
                available.Add(ArchetypeClass.Tank);
                available.Add(ArchetypeClass.Paladin);
            }
            
            if (hasBow)
            {
                available.Add(ArchetypeClass.Ranger);
            }
            
            if (hasKnives)
            {
                available.Add(ArchetypeClass.Rogue);
            }
            
            if (hasOffensiveStaff)
            {
                available.Add(ArchetypeClass.Mage);
            }
            
            if (hasSupportStaff)
            {
                available.Add(ArchetypeClass.Healer);
            }
            
            if (hasTwoHanded || hasMelee)
            {
                available.Add(ArchetypeClass.Berserker);
            }
            
            // Always allow Monk as fallback (unarmed)
            if (!available.Contains(ArchetypeClass.Monk))
                available.Add(ArchetypeClass.Monk);
            
            return available;
        }
        
        private bool IsMeleeItem(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            var itemType = item.m_shared.m_itemType;
            var skill = item.m_shared.m_skillType;
            if (itemType == ItemDrop.ItemData.ItemType.Shield) return false;
            if (itemType == ItemDrop.ItemData.ItemType.Bow) return false;
            if (itemType == ItemDrop.ItemData.ItemType.Tool) return false;
            if (skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows) return false;
            return true;
        }
        
        private bool HasItemTypeInStorage(Inventory storage, ItemDrop.ItemData.ItemType type)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (item?.m_shared?.m_itemType == type) return true;
            }
            return false;
        }
        
        private bool HasSkillTypeInStorage(Inventory storage, Skills.SkillType skill)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (item?.m_shared?.m_skillType == skill) return true;
            }
            return false;
        }
        
        private bool HasMeleeInStorage(Inventory storage)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (item?.IsWeapon() == true && IsMeleeItem(item)) return true;
            }
            return false;
        }
        
        private bool HasTwoHandedInStorage(Inventory storage)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                    item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
                    return true;
            }
            return false;
        }
        
        private bool HasOffensiveStaffInStorage(Inventory storage)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (ArchetypeUtils.IsOffensiveStaff(item)) return true;
            }
            return false;
        }
        
        private bool HasSupportStaffInStorage(Inventory storage)
        {
            if (storage == null) return false;
            foreach (var item in storage.GetAllItems())
            {
                if (ArchetypeUtils.IsSupportStaff(item)) return true;
            }
            return false;
        }
        
        #endregion
        
        private void EnsureOwnership()
        {
            if (_companionNview == null || _companionNview.GetZDO() == null || _companionNview.IsOwner())
                return;
            
            _companionNview.ClaimOwnership();
        }
        
        /// <summary>
        /// Shows or hides the companion's billboard (health bar and name display).
        /// This prevents the billboard from showing through the radial menu.
        /// Uses the same reflection approach as CompanionPatches.EnemyHud_UpdateHuds_Postfix.
        /// </summary>
        private void SetCompanionBillboardVisible(bool visible)
        {
            if (_companion == null) return;
            
            // If showing and we have the cached reference, use it directly
            if (visible && _hidBillboard && _cachedBillboardGui != null)
            {
                try
                {
                    _cachedBillboardGui.SetActive(true);
                }
                catch { }
                _hidBillboard = false;
                _cachedBillboardGui = null;
                return;
            }
            
            // If showing and we didn't hide it, nothing to do
            if (visible && !_hidBillboard) return;
            
            var enemyHud = EnemyHud.instance;
            if (enemyHud == null)
            {
                if (!visible)
                    Debug.Log("[RadialMenu] EnemyHud.instance is null, cannot hide billboard");
                return;
            }
            
            var character = _companion.GetComponent<Character>();
            if (character == null)
            {
                if (!visible)
                    Debug.Log("[RadialMenu] Character component is null, cannot hide billboard");
                return;
            }
            
            try
            {
                // Access the m_huds dictionary via reflection (it's private)
                var hudsField = typeof(EnemyHud).GetField("m_huds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (hudsField == null)
                {
                    Debug.LogWarning("[RadialMenu] Could not find m_huds field");
                    return;
                }
                
                var hudsObj = hudsField.GetValue(enemyHud);
                if (hudsObj == null)
                {
                    Debug.LogWarning("[RadialMenu] m_huds is null");
                    return;
                }
                
                // Get the nested HudData type (private nested class)
                var hudDataType = typeof(EnemyHud).GetNestedType("HudData", System.Reflection.BindingFlags.NonPublic);
                if (hudDataType == null)
                {
                    Debug.LogWarning("[RadialMenu] Could not find HudData type");
                    return;
                }
                
                // Get m_gui field from HudData (public field within the nested type)
                var guiField = hudDataType.GetField("m_gui");
                if (guiField == null)
                {
                    Debug.LogWarning("[RadialMenu] Could not find m_gui field in HudData");
                    return;
                }
                
                // Cast to IDictionary to access by key
                var hudsDict = hudsObj as System.Collections.IDictionary;
                if (hudsDict == null)
                {
                    Debug.LogWarning("[RadialMenu] m_huds is not IDictionary");
                    return;
                }
                
                // Check if our character is in the dictionary
                if (hudsDict.Contains(character))
                {
                    var hudData = hudsDict[character];
                    if (hudData != null)
                    {
                        var gui = guiField.GetValue(hudData) as GameObject;
                        if (gui != null)
                        {
                            if (!visible)
                            {
                                // Hiding - cache the reference and hide
                                _cachedBillboardGui = gui;
                                _hidBillboard = true;
                                gui.SetActive(false);
                                Debug.Log($"[RadialMenu] Hidden billboard for {_companion.companionName}");
                            }
                            else
                            {
                                // Showing - restore visibility
                                gui.SetActive(true);
                                _hidBillboard = false;
                                _cachedBillboardGui = null;
                                Debug.Log($"[RadialMenu] Restored billboard for {_companion.companionName}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[RadialMenu] m_gui GameObject is null");
                        }
                    }
                }
                else
                {
                    // Character not in huds dictionary - this is normal if billboard isn't showing yet
                    // We'll set a flag to hide it when it appears
                    if (!visible)
                    {
                        Debug.Log($"[RadialMenu] Character {_companion.companionName} not in EnemyHud yet, will hide when it appears");
                        _hidBillboard = true; // Flag that we want it hidden
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RadialMenu] SetCompanionBillboardVisible failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #region Segment Building
        
        private void BuildSegments()
        {
            _segments.Clear();
            
            bool isFollowing = _companionAI?.ShouldBeFollowing ?? false;

            // Follow/Stay toggle
            _segments.Add(new Segment
            {
                Label = isFollowing ? "Stay" : "Follow",
                ActionId = ActionFollow,
                IsToggle = true,
                IsActive = isFollowing,
                IconColor = new Color(0.4f, 0.75f, 0.4f)
            });

            // Inventory
            _segments.Add(new Segment
            {
                Label = "Inventory",
                ActionId = ActionInventory,
                IsToggle = false,
                IsActive = false,
                IconColor = new Color(0.75f, 0.6f, 0.35f)
            });

            // Stats/Attributes
            _segments.Add(new Segment
            {
                Label = "Stats",
                ActionId = ActionStats,
                IsToggle = false,
                IsActive = false,
                IconColor = new Color(0.5f, 0.65f, 0.85f)
            });

            // Set Home
            _segments.Add(new Segment
            {
                Label = "Set Home",
                ActionId = ActionSetHome,
                IsToggle = false,
                IsActive = false,
                IconColor = new Color(0.25f, 0.7f, 0.7f)
            });

            // Dismiss
            _segments.Add(new Segment
            {
                Label = "Dismiss",
                ActionId = ActionDismiss,
                IsToggle = false,
                IsActive = false,
                IconColor = new Color(0.8f, 0.35f, 0.35f)
            });

            // Guild Access toggle Ã¢â‚¬â€ only shown if owner is in a guild.
            if (GuildAPI.GetOwnGuild() != null && _companion != null)
            {
                bool guildOn = CompanionGuildAccess.GetToggleState(_companion);
                _segments.Add(new Segment
                {
                    Label     = guildOn ? "Guild: On" : "Guild: Off",
                    ActionId  = ActionGuildAccess,
                    IsToggle  = true,
                    IsActive  = guildOn,
                    IconColor = guildOn
                        ? new Color(0.55f, 0.75f, 0.95f)
                        : new Color(0.45f, 0.40f, 0.35f),
                });
            }

            // Work behaviors moved to CompanionIdleBehaviorRadial (hold H)

            // Build inner ring (archetype OR hybrid selection based on companion level)
            _innerSegments.Clear();
            
            ArchetypeClass currentArchetype = GetCurrentArchetype();
            var archetypeController = _companion?.GetComponent<ArchetypeController>();
            var progression = _companion?.GetComponent<CompanionProgression>();
            int companionLevel = progression?.Level ?? 1;
            
            // Check if companion is eligible for hybrid archetypes (level 25+)
            // Note: AbilityUnlockSystem.HYBRID_ABILITY_LEVEL may be 30, but we show options at 25
            bool canUseHybrids = companionLevel >= 25;
            var currentSubArchetype = archetypeController?.SubArchetypeClass ?? ArchetypeClass.None;
            
            if (canUseHybrids && currentArchetype != ArchetypeClass.None)
            {
                // Show hybrid options for current main archetype
                var availableHybrids = GetAvailableHybrids(currentArchetype);
                
                if (availableHybrids.Count > 0)
                {
                    foreach (var (subArchetype, hybridDef) in availableHybrids)
                    {
                        _innerSegments.Add(new Segment
                        {
                            Label = hybridDef.DisplayName,
                            ActionId = ActionHybridArchetypeBase + (int)subArchetype,
                            IsMode = true,
                            IsActive = currentSubArchetype == subArchetype,
                            IconColor = hybridDef.IconColor
                        });
                    }
                }
                else
                {
                    // No hybrids available, show base archetypes
                    BuildBaseArchetypeSegments(currentArchetype);
                }
            }
            else
            {
                // Not eligible for hybrids, show base archetype selection
                BuildBaseArchetypeSegments(currentArchetype);
            }
            
            // If still no segments, show a message
            if (_innerSegments.Count == 0)
            {
                _innerSegments.Add(new Segment
                {
                    Label = "No Options",
                    ActionId = -1,
                    IsMode = false,
                    IsActive = false,
                    IconColor = Color.gray
                });
            }
        }
        
        private void BuildBaseArchetypeSegments(ArchetypeClass currentArchetype)
        {
            var availableArchetypes = GetAvailableArchetypes();
            
            foreach (var archetype in availableArchetypes)
            {
                var definition = ArchetypeRegistry.GetDefinition(archetype);
                string label = definition?.DisplayName ?? archetype.ToString();
                Color iconColor = definition?.IconColor ?? Color.gray;
                
                _innerSegments.Add(new Segment
                {
                    Label = label,
                    ActionId = ActionArchetypeBase + (int)archetype,
                    IsMode = true,
                    IsActive = currentArchetype == archetype,
                    IconColor = iconColor
                });
            }
        }
        
        /// <summary>
        /// Gets available hybrid archetypes based on current main archetype and equipment.
        /// </summary>
        private List<(ArchetypeClass sub, HybridArchetypeDefinitions.HybridDefinition def)> GetAvailableHybrids(ArchetypeClass mainArchetype)
        {
            var hybrids = new List<(ArchetypeClass, HybridArchetypeDefinitions.HybridDefinition)>();
            
            if (mainArchetype == ArchetypeClass.None)
                return hybrids;
            
            // Get available base archetypes based on equipment
            var availableArchetypes = GetAvailableArchetypes();
            
            // For each available archetype (except main), check if hybrid exists
            foreach (var subArchetype in availableArchetypes)
            {
                if (subArchetype == mainArchetype) continue;
                if (subArchetype == ArchetypeClass.None) continue;
                
                var hybridDef = HybridArchetypeDefinitions.GetHybrid(mainArchetype, subArchetype);
                if (hybridDef != null)
                {
                    hybrids.Add((subArchetype, hybridDef));
                }
            }
            
            return hybrids;
        }
        
        private void RefreshSegmentStates()
        {
            BuildSegments();
            
            // Update outer ring dots
            for (int i = 0; i < _segments.Count && i < _segDots.Count; i++)
            {
                if (_segDots[i] != null)
                    _segDots[i].color = _segments[i].IsActive ? ActiveDot : InactiveDot;
            }
            
            // Update inner ring dots
            for (int i = 0; i < _innerSegments.Count && i < _innerDots.Count; i++)
            {
                if (_innerDots[i] != null)
                    _innerDots[i].color = _innerSegments[i].IsActive ? ActiveDot : InactiveDot;
            }
        }
        
        #endregion
        
        #region UI Building
        
        private void BuildUI()
        {
            TMP_FontAsset font = GetFont();
            
            // Create canvas
            var canvasGO = new GameObject("VA_RadialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30;
            
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            
            // Create root
            _root = new GameObject("RadialRoot", typeof(RectTransform));
            _root.transform.SetParent(canvasGO.transform, false);
            
            var rootRT = _root.GetComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0.5f, 0.5f);
            rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.sizeDelta = new Vector2(560f, 560f);
            
            // Outer donut background
            _bgCircle = CreateUIElement("BgDonut", _root.transform, new Vector2(540f, 540f));
            var bgImg = _bgCircle.AddComponent<Image>();
            bgImg.sprite = GetDonutSprite();
            bgImg.color = BgColor;
            bgImg.raycastTarget = false;
            _bgCanvasGroup = _bgCircle.AddComponent<CanvasGroup>();
            _bgCanvasGroup.blocksRaycasts = false;
            
            // Inner circle background
            _innerBgCircle = CreateUIElement("BgInnerCircle", _root.transform, new Vector2(240f, 240f));
            var innerBgImg = _innerBgCircle.AddComponent<Image>();
            innerBgImg.sprite = GetCircleSprite();
            innerBgImg.color = BgColor;
            innerBgImg.raycastTarget = false;
            _innerBgCanvasGroup = _innerBgCircle.AddComponent<CanvasGroup>();
            _innerBgCanvasGroup.blocksRaycasts = false;
            
            // Center container
            _centerContainer = CreateUIElement("CenterContainer", _root.transform, new Vector2(160f, 60f));
            _centerCanvasGroup = _centerContainer.AddComponent<CanvasGroup>();
            _centerCanvasGroup.blocksRaycasts = false;
            
            // Center text - use larger fonts for better readability
            _centerName = MakeText(_centerContainer.transform, "CenterName", "", GetConfiguredFont(UIFontConfig.FontCategory.Primary), 18f, 
                new Color(0.83f, 0.64f, 0.31f, 1f), TextAlignmentOptions.Center);
            SetRectTransform(_centerName.GetComponent<RectTransform>(), new Vector2(180f, 24f), new Vector2(0f, 16f));
            
            _centerAction = MakeText(_centerContainer.transform, "CenterAction", "", GetConfiguredFont(UIFontConfig.FontCategory.Body), 14f, 
                TextNormal, TextAlignmentOptions.Center);
            SetRectTransform(_centerAction.GetComponent<RectTransform>(), new Vector2(180f, 20f), new Vector2(0f, -4f));
            
            _centerState = MakeText(_centerContainer.transform, "CenterState", "", GetConfiguredFont(UIFontConfig.FontCategory.Body), 12f, 
                ActiveDot, TextAlignmentOptions.Center);
            SetRectTransform(_centerState.GetComponent<RectTransform>(), new Vector2(100f, 18f), new Vector2(0f, -22f));
            
            _root.SetActive(false);
            _built = true;
        }
        
        private void PopulateUI()
        {
            TMP_FontAsset font = GetFont();
            
            // Clear existing segments
            ClearSegmentObjects();
            
            // Build outer ring segments
            float segAngle = 360f / _segments.Count;
            for (int i = 0; i < _segments.Count; i++)
            {
                var segment = _segments[i];
                float angle = (90f - segAngle * i) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * RingRadius;
                float y = Mathf.Sin(angle) * RingRadius;
                
                _segFinalPositions.Add(new Vector2(x, y));
                
                // Create segment container
                var segGO = new GameObject($"Seg_{i}", typeof(RectTransform), typeof(CanvasGroup));
                segGO.transform.SetParent(_root.transform, false);
                
                var segRT = segGO.GetComponent<RectTransform>();
                segRT.anchorMin = segRT.anchorMax = segRT.pivot = new Vector2(0.5f, 0.5f);
                segRT.sizeDelta = new Vector2(SegSize, SegSize);
                segRT.anchoredPosition = new Vector2(x, y);
                
                var segCG = segGO.GetComponent<CanvasGroup>();
                segCG.blocksRaycasts = false;
                
                // Highlight
                var highlightGO = CreateUIElement("Highlight", segGO.transform, new Vector2(HighlightSize, HighlightSize));
                var highlightImg = highlightGO.AddComponent<Image>();
                highlightImg.sprite = GetCircleSprite();
                highlightImg.color = Color.clear;
                highlightImg.raycastTarget = false;
                
                // Icon background
                var iconBgGO = CreateUIElement("IconBg", segGO.transform, new Vector2(IconSize, IconSize));
                var iconBgImg = iconBgGO.AddComponent<Image>();
                iconBgImg.sprite = GetCircleSprite();
                Color iconBgColor = segment.IconColor * 0.35f;
                iconBgImg.color = new Color(iconBgColor.r, iconBgColor.g, iconBgColor.b, 0.5f);
                iconBgImg.raycastTarget = false;
                
                // Icon
                var iconGO = CreateUIElement("Icon", segGO.transform, new Vector2(IconSize * 0.975f, IconSize * 0.975f));
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = GetActionIcon(segment.ActionId);
                iconImg.color = new Color(0.9f, 0.9f, 0.9f, 0.85f);
                iconImg.raycastTarget = false;
                
                // Label - use configured body font with larger size, positioned within icon circle
                var labelTMP = MakeText(segGO.transform, "Label", segment.Label, GetConfiguredFont(UIFontConfig.FontCategory.Body), 11f, TextNormal, TextAlignmentOptions.Center);
                var labelRT = labelTMP.GetComponent<RectTransform>();
                labelRT.anchorMin = new Vector2(0f, 0f);
                labelRT.anchorMax = new Vector2(1f, 0f);
                labelRT.pivot = new Vector2(0.5f, 1f);
                labelRT.sizeDelta = new Vector2(0f, 16f);
                labelRT.anchoredPosition = new Vector2(0f, 32f);
                
                // Status dot (for toggles) - positioned BELOW the label to avoid overlap
                Image dotImg = null;
                if (segment.IsToggle || segment.IsMode)
                {
                    var dotGO = CreateUIElement("Dot", segGO.transform, new Vector2(8f, 8f));
                    var dotRT = dotGO.GetComponent<RectTransform>();
                    dotRT.anchorMin = dotRT.anchorMax = new Vector2(0.5f, 0f);
                    dotRT.pivot = new Vector2(0.5f, 0.5f);
                    dotRT.anchoredPosition = new Vector2(0f, -2f);  // Moved lower to avoid text overlap
                    
                    dotImg = dotGO.AddComponent<Image>();
                    dotImg.sprite = GetCircleSprite();
                    dotImg.color = segment.IsActive ? ActiveDot : InactiveDot;
                    dotImg.raycastTarget = false;
                }
                
                _segmentGOs.Add(segGO);
                _segHighlights.Add(highlightImg);
                _segIcons.Add(iconImg);
                _segLabels.Add(labelTMP);
                _segDots.Add(dotImg);
                _segCanvasGroups.Add(segCG);
                _segRTs.Add(segRT);
            }
            
            // Build inner ring segments (combat stances)
            if (_innerSegments.Count > 0)
            {
                float innerSegAngle = 360f / _innerSegments.Count;
                for (int i = 0; i < _innerSegments.Count; i++)
                {
                    var segment = _innerSegments[i];
                    float angle = (90f - innerSegAngle * i) * Mathf.Deg2Rad;
                    float x = Mathf.Cos(angle) * InnerRingRadius;
                    float y = Mathf.Sin(angle) * InnerRingRadius;
                    
                    _innerFinalPositions.Add(new Vector2(x, y));
                    
                    var segGO = new GameObject($"InnerSeg_{i}", typeof(RectTransform), typeof(CanvasGroup));
                    segGO.transform.SetParent(_root.transform, false);
                    
                    var segRT = segGO.GetComponent<RectTransform>();
                    segRT.anchorMin = segRT.anchorMax = segRT.pivot = new Vector2(0.5f, 0.5f);
                    segRT.sizeDelta = new Vector2(InnerSegSize, InnerSegSize);
                    segRT.anchoredPosition = new Vector2(x, y);
                    
                    var segCG = segGO.GetComponent<CanvasGroup>();
                    segCG.blocksRaycasts = false;
                    
                    // Highlight
                    var highlightGO = CreateUIElement("Highlight", segGO.transform, new Vector2(InnerHighlightSize, InnerHighlightSize));
                    var highlightImg = highlightGO.AddComponent<Image>();
                    highlightImg.sprite = GetCircleSprite();
                    highlightImg.color = Color.clear;
                    highlightImg.raycastTarget = false;
                    
                    // Icon background
                    var iconBgGO = CreateUIElement("IconBg", segGO.transform, new Vector2(InnerIconSize, InnerIconSize));
                    var iconBgImg = iconBgGO.AddComponent<Image>();
                    iconBgImg.sprite = GetCircleSprite();
                    Color iconBgColor = segment.IconColor * 0.35f;
                    iconBgImg.color = new Color(iconBgColor.r, iconBgColor.g, iconBgColor.b, 0.5f);
                    iconBgImg.raycastTarget = false;
                    
                    // Icon
                    var iconGO = CreateUIElement("Icon", segGO.transform, new Vector2(InnerIconSize * 0.78f, InnerIconSize * 0.78f));
                    var iconImg = iconGO.AddComponent<Image>();
                    iconImg.sprite = GetActionIcon(segment.ActionId);
                    iconImg.color = new Color(0.9f, 0.9f, 0.9f, 0.85f);
                    iconImg.raycastTarget = false;
                    
                    // Label - use configured body font with larger size, positioned within icon circle
                    var labelTMP = MakeText(segGO.transform, "Label", segment.Label, GetConfiguredFont(UIFontConfig.FontCategory.Body), 10f, TextNormal, TextAlignmentOptions.Center);
                    var labelRT = labelTMP.GetComponent<RectTransform>();
                    labelRT.anchorMin = new Vector2(0f, 0f);
                    labelRT.anchorMax = new Vector2(1f, 0f);
                    labelRT.pivot = new Vector2(0.5f, 1f);
                    labelRT.sizeDelta = new Vector2(0f, 14f);
                    labelRT.anchoredPosition = new Vector2(0f, 24f);
                    
                    // Status dot - positioned BELOW the label to avoid overlap
                    var dotGO = CreateUIElement("Dot", segGO.transform, new Vector2(6f, 6f));
                    var dotRT = dotGO.GetComponent<RectTransform>();
                    dotRT.anchorMin = dotRT.anchorMax = new Vector2(0.5f, 0f);
                    dotRT.pivot = new Vector2(0.5f, 0.5f);
                    dotRT.anchoredPosition = new Vector2(0f, -4f);  // Moved lower to avoid text overlap
                    
                    var dotImg = dotGO.AddComponent<Image>();
                    dotImg.sprite = GetCircleSprite();
                    dotImg.color = segment.IsActive ? ActiveDot : InactiveDot;
                    dotImg.raycastTarget = false;
                    
                    _innerSegmentGOs.Add(segGO);
                    _innerHighlights.Add(highlightImg);
                    _innerIcons.Add(iconImg);
                    _innerLabels.Add(labelTMP);
                    _innerDots.Add(dotImg);
                    _innerCanvasGroups.Add(segCG);
                    _innerRTs.Add(segRT);
                }
            }
            
            // Update center name
            if (_centerName != null)
            {
                _centerName.text = _companion?.GetDisplayName() ?? "Companion";
            }
        }
        
        private void ClearSegmentObjects()
        {
            foreach (var go in _segmentGOs)
            {
                if (go != null) Destroy(go);
            }
            _segmentGOs.Clear();
            _segHighlights.Clear();
            _segIcons.Clear();
            _segLabels.Clear();
            _segDots.Clear();
            _segCanvasGroups.Clear();
            _segRTs.Clear();
            _segFinalPositions.Clear();
            
            foreach (var go in _innerSegmentGOs)
            {
                if (go != null) Destroy(go);
            }
            _innerSegmentGOs.Clear();
            _innerHighlights.Clear();
            _innerIcons.Clear();
            _innerLabels.Clear();
            _innerDots.Clear();
            _innerCanvasGroups.Clear();
            _innerRTs.Clear();
            _innerFinalPositions.Clear();
        }
        
        private void RefreshVisuals()
        {
            // Update outer ring highlights
            for (int i = 0; i < _segmentGOs.Count; i++)
            {
                bool hovered = i == _hoveredIndex;
                var segment = _segments[i];
                
                if (_segHighlights[i] != null)
                {
                    Color highlightColor = hovered ? HighlightHover :
                        ((segment.IsActive && (segment.IsToggle || segment.IsMode)) ? HighlightActive : Color.clear);
                    _segHighlights[i].color = highlightColor;
                }
                
                if (_segLabels[i] != null)
                    _segLabels[i].color = hovered ? TextHover : TextNormal;
                
                if (_segIcons[i] != null)
                    _segIcons[i].color = hovered ? Color.white : new Color(0.9f, 0.9f, 0.9f, 0.85f);
            }
            
            // Update inner ring highlights
            for (int i = 0; i < _innerSegmentGOs.Count; i++)
            {
                bool hovered = i == _hoveredInner;
                var segment = _innerSegments[i];
                
                if (_innerHighlights[i] != null)
                {
                    Color highlightColor = hovered ? HighlightHover :
                        (segment.IsActive ? HighlightActive : Color.clear);
                    _innerHighlights[i].color = highlightColor;
                }
                
                if (_innerLabels[i] != null)
                    _innerLabels[i].color = hovered ? TextHover : TextNormal;
                
                if (_innerIcons[i] != null)
                    _innerIcons[i].color = hovered ? Color.white : new Color(0.9f, 0.9f, 0.9f, 0.85f);
            }
            
            // Update center text
            if (_centerAction != null)
            {
                if (_hoveredInner >= 0 && _hoveredInner < _innerSegments.Count)
                {
                    var seg = _innerSegments[_hoveredInner];
                    _centerAction.text = seg.Label;
                    _centerState.text = seg.IsActive ? "[Active]" : "";
                }
                else if (_hoveredIndex >= 0 && _hoveredIndex < _segments.Count)
                {
                    var seg = _segments[_hoveredIndex];
                    _centerAction.text = seg.Label;
                    _centerState.text = (seg.IsToggle || seg.IsMode) ? 
                        (seg.IsActive ? "[On]" : "[Off]") : "";
                }
                else
                {
                    _centerAction.text = "";
                    _centerState.text = "";
                }
            }
        }
        
        #endregion
        
        #region Animation
        
        private void InitAnimationState()
        {
            if (_bgCanvasGroup != null) _bgCanvasGroup.alpha = 0f;
            _bgCircle.GetComponent<RectTransform>().localScale = Vector3.zero;
            
            if (_innerBgCanvasGroup != null) _innerBgCanvasGroup.alpha = 0f;
            if (_innerBgCircle != null)
                _innerBgCircle.GetComponent<RectTransform>().localScale = Vector3.zero;
            
            if (_centerCanvasGroup != null) _centerCanvasGroup.alpha = 0f;
            
            for (int i = 0; i < _segmentGOs.Count; i++)
            {
                if (i < _segRTs.Count && _segRTs[i] != null)
                {
                    _segRTs[i].anchoredPosition = Vector2.zero;
                    _segRTs[i].localScale = Vector3.zero;
                    _segRTs[i].localEulerAngles = new Vector3(0f, 0f, i * 20f - 180f);
                }
                if (i < _segCanvasGroups.Count && _segCanvasGroups[i] != null)
                    _segCanvasGroups[i].alpha = 0f;
            }
            
            for (int i = 0; i < _innerSegmentGOs.Count; i++)
            {
                if (i < _innerRTs.Count && _innerRTs[i] != null)
                {
                    _innerRTs[i].anchoredPosition = Vector2.zero;
                    _innerRTs[i].localScale = Vector3.zero;
                    _innerRTs[i].localEulerAngles = new Vector3(0f, 0f, i * 30f - 120f);
                }
                if (i < _innerCanvasGroups.Count && _innerCanvasGroups[i] != null)
                    _innerCanvasGroups[i].alpha = 0f;
            }
        }
        
        private void UpdateAnimation()
        {
            _animTimer += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_animTimer / AnimDuration);
            bool closing = _animState == AnimState.Closing;
            float t = closing ? 1f - progress : progress;
            
            // Background animation
            float bgT = Mathf.Clamp01(t / BgAnimFraction);
            float bgScale = EaseOutBack(bgT);
            
            _bgCircle.GetComponent<RectTransform>().localScale = Vector3.one * Mathf.Max(0f, bgScale);
            if (_bgCanvasGroup != null)
                _bgCanvasGroup.alpha = Mathf.Clamp01(EaseOutQuad(bgT));
            
            if (_innerBgCircle != null)
            {
                _innerBgCircle.GetComponent<RectTransform>().localScale = Vector3.one * Mathf.Max(0f, bgScale);
                if (_innerBgCanvasGroup != null)
                    _innerBgCanvasGroup.alpha = Mathf.Clamp01(EaseOutQuad(bgT));
            }
            
            if (_centerCanvasGroup != null)
                _centerCanvasGroup.alpha = Mathf.Clamp01((t - 0.3f) / 0.3f);
            
            // Outer segments animation
            int outerCount = _segmentGOs.Count;
            float segDuration = Mathf.Max(0.01f, AnimDuration - outerCount * SegStagger);
            
            for (int i = 0; i < outerCount; i++)
            {
                float delay = (closing ? (outerCount - 1 - i) : i) * SegStagger;
                float segT = closing ? 
                    1f - Mathf.Clamp01((_animTimer - delay) / segDuration) :
                    Mathf.Clamp01((_animTimer - delay) / segDuration);
                
                float scale = closing ? segT * segT : EaseOutBack(segT);
                
                if (i < _segRTs.Count && _segRTs[i] != null)
                {
                    if (i < _segFinalPositions.Count)
                        _segRTs[i].anchoredPosition = _segFinalPositions[i] * scale;
                    _segRTs[i].localScale = Vector3.one * Mathf.Max(0f, scale);
                    
                    float startAngle = i * 30f - 360f;
                    _segRTs[i].localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(startAngle, 0f, Mathf.Clamp01(scale)));
                }
                
                if (i < _segCanvasGroups.Count && _segCanvasGroups[i] != null)
                    _segCanvasGroups[i].alpha = Mathf.Clamp01(EaseOutQuad(segT));
            }
            
            // Inner segments animation
            int innerCount = _innerSegmentGOs.Count;
            float innerDelay = 0.02f;
            
            for (int i = 0; i < innerCount; i++)
            {
                float delay = ((closing ? (innerCount - 1 - i) : i) * SegStagger) + innerDelay;
                float segT = closing ? 
                    1f - Mathf.Clamp01((_animTimer - delay) / segDuration) :
                    Mathf.Clamp01((_animTimer - delay) / segDuration);
                
                float scale = closing ? segT * segT : EaseOutBack(segT);
                
                if (i < _innerRTs.Count && _innerRTs[i] != null)
                {
                    if (i < _innerFinalPositions.Count)
                        _innerRTs[i].anchoredPosition = _innerFinalPositions[i] * scale;
                    _innerRTs[i].localScale = Vector3.one * Mathf.Max(0f, scale);
                    
                    float startAngle = i * 40f - 270f;
                    _innerRTs[i].localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(startAngle, 0f, Mathf.Clamp01(scale)));
                }
                
                if (i < _innerCanvasGroups.Count && _innerCanvasGroups[i] != null)
                    _innerCanvasGroups[i].alpha = Mathf.Clamp01(EaseOutQuad(segT));
            }
            
            // Check animation completion
            if (progress >= 1f)
            {
                if (_animState == AnimState.Opening)
                {
                    _animState = AnimState.Open;
                    FinalizeOpenAnimation();
                }
                else if (_animState == AnimState.Closing)
                {
                    CompleteHide();
                }
            }
        }
        
        private void FinalizeOpenAnimation()
        {
            _bgCircle.GetComponent<RectTransform>().localScale = Vector3.one;
            if (_bgCanvasGroup != null) _bgCanvasGroup.alpha = 1f;
            
            if (_innerBgCircle != null)
            {
                _innerBgCircle.GetComponent<RectTransform>().localScale = Vector3.one;
                if (_innerBgCanvasGroup != null) _innerBgCanvasGroup.alpha = 1f;
            }
            
            if (_centerCanvasGroup != null) _centerCanvasGroup.alpha = 1f;
            
            for (int i = 0; i < _segmentGOs.Count; i++)
            {
                if (i < _segRTs.Count && _segRTs[i] != null)
                {
                    if (i < _segFinalPositions.Count)
                        _segRTs[i].anchoredPosition = _segFinalPositions[i];
                    _segRTs[i].localScale = Vector3.one;
                    _segRTs[i].localEulerAngles = Vector3.zero;
                }
                if (i < _segCanvasGroups.Count && _segCanvasGroups[i] != null)
                    _segCanvasGroups[i].alpha = 1f;
            }
            
            for (int i = 0; i < _innerSegmentGOs.Count; i++)
            {
                if (i < _innerRTs.Count && _innerRTs[i] != null)
                {
                    if (i < _innerFinalPositions.Count)
                        _innerRTs[i].anchoredPosition = _innerFinalPositions[i];
                    _innerRTs[i].localScale = Vector3.one;
                    _innerRTs[i].localEulerAngles = Vector3.zero;
                }
                if (i < _innerCanvasGroups.Count && _innerCanvasGroups[i] != null)
                    _innerCanvasGroups[i].alpha = 1f;
            }
        }
        
        private static float EaseOutBack(float t)
        {
            float c = t - 1f;
            return 1f + 2.4f * c * c * c + 1.4f * c * c;
        }
        
        private static float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
        
        #endregion
        
        #region Icon Style Settings
        
        /// <summary>
        /// Icon style for the radial menu.
        /// Set to true for consistent monochrome procedural icons.
        /// Set to false to use Valheim's native colored sprites.
        /// </summary>
        public static bool UseMonochromeIcons = true;
        
        #endregion
        
        #region Sprite Generation
        
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            
            int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            
            var colors = new Color[size * size];
            float center = (size - 1) * 0.5f;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    
                    if (dist > center + 1.5f)
                        colors[y * size + x] = Color.clear;
                    else if (dist > center)
                        colors[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((center + 1.5f - dist) / 1.5f));
                    else
                        colors[y * size + x] = Color.white;
                }
            }
            
            texture.SetPixels(colors);
            texture.Apply();
            
            _circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }
        
        private static Sprite GetDonutSprite()
        {
            if (_donutSprite != null) return _donutSprite;
            
            int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            
            var colors = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float outerRadius = center;
            float innerRadius = center * 0.563f;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    
                    if (dist > outerRadius + 1.5f)
                        colors[y * size + x] = Color.clear;
                    else if (dist > outerRadius)
                        colors[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((outerRadius + 1.5f - dist) / 1.5f));
                    else if (dist > innerRadius)
                        colors[y * size + x] = Color.white;
                    else if (dist > innerRadius - 1.5f)
                        colors[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((dist - innerRadius + 1.5f) / 1.5f));
                    else
                        colors[y * size + x] = Color.clear;
                }
            }
            
            texture.SetPixels(colors);
            texture.Apply();
            
            _donutSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _donutSprite;
        }
        
        private static Sprite GetActionIcon(int actionId)
        {
            if (_iconCache.TryGetValue(actionId, out var cached))
                return cached;
            
            Sprite sprite = null;
            
            if (UseMonochromeIcons)
            {
                // Always use procedural monochrome icons for consistency
                sprite = GetProceduralIcon(actionId);
            }
            else
            {
                // Try to get Valheim sprite first, fallback to procedural
                sprite = GetValheimSpriteForAction(actionId);
                if (sprite == null)
                    sprite = GetProceduralIcon(actionId);
            }
            
            _iconCache[actionId] = sprite;
            return sprite;
        }
        
        /// <summary>
        /// Maps action IDs to best matching Valheim sprites.
        /// Uses sprite names from VALHEIM_SPRITES_REFERENCE.txt
        /// </summary>
        private static Sprite GetValheimSpriteForAction(int actionId)
        {
            // Initialize cache if needed
            if (!_spriteCacheInitialized)
                InitializeValheimSpriteCache();
            
            switch (actionId)
            {
                // === OUTER RING ACTIONS ===
                
                case ActionFollow:
                    // Follow/Stay - use wave emote (distinctive hand gesture)
                    return GetValheimSpriteWithFallbacks("T_emote_wave", "mapicon_player_32", "skill_run");
                    
                case ActionGatherResources:
                    // Gather - use axe icon
                    return GetValheimSpriteWithFallbacks("axe_iron", "axe_bronze", "axe_stone", "pickaxe_iron");
                    
                case ActionTendFires:
                    // Fire tending - use campfire or torch
                    return GetValheimSpriteWithFallbacks("Campfire", "torch", "brazierfloor01", "firepit_base");
                    
                case ActionOperateSmelters:
                    // Smelters - use smelter icon
                    return GetValheimSpriteWithFallbacks("smelter", "blastfurnace", "charcoal_kiln", "forge");
                    
                case ActionDepositItems:
                    // Deposit - use chest icon
                    return GetValheimSpriteWithFallbacks("chest_wood", "chest_iron", "chest_blackmetal", "chest_private");
                    
                case ActionInventory:
                    // Inventory - use chest/bag icon for inventory
                    return GetValheimSpriteWithFallbacks("chest_private", "chest_wood", "item_background", "trophies");
                    
                case ActionStats:
                    // Stats - use skills icon or bar chart type icon
                    return GetValheimSpriteWithFallbacks("skills", "texts_icon", "trophies");
                    
                case ActionWander:
                    // Wander - use compass/explore icon to distinguish from Follow
                    return GetValheimSpriteWithFallbacks("mapicon_boss", "mapicon_ping", "T_emote_point", "skill_run");
                    
                case ActionSetHome:
                    // Set Home - use house map icon
                    return GetValheimSpriteWithFallbacks("mapicon_house", "mapicon_bed", "mapicon_fire", "mapicon_pin");
                    
                case ActionDismiss:
                    // Dismiss - use X or trash icon
                    return GetValheimSpriteWithFallbacks("trash_icon", "mapicon_death");
                    
                default:
                    // Check for archetype or hybrid icons
                    if (actionId >= ActionHybridArchetypeBase)
                    {
                        // Hybrid archetype - get icon based on sub-archetype
                        return GetArchetypeSprite((ArchetypeClass)(actionId - ActionHybridArchetypeBase));
                    }
                    else if (actionId >= ActionArchetypeBase)
                    {
                        // Base archetype
                        return GetArchetypeSprite((ArchetypeClass)(actionId - ActionArchetypeBase));
                    }
                    break;
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the best Valheim sprite for an archetype class.
        /// </summary>
        private static Sprite GetArchetypeSprite(ArchetypeClass archetype)
        {
            switch (archetype)
            {
                case ArchetypeClass.Tank:
                    // Tank/Guardian - shield icon
                    return GetValheimSpriteWithFallbacks(
                        "shield_serpentscale", "shield_banded3", "shield_ironbuckler", 
                        "shield_bronzebuckler", "shield_wood3", "skill_block");
                    
                case ArchetypeClass.Paladin:
                    // Paladin - shield with holy theme
                    return GetValheimSpriteWithFallbacks(
                        "SwordSilver", "shield_banded2", "shield_ironbuckler", 
                        "skill_block", "DivineProtection");
                    
                case ArchetypeClass.Berserker:
                    // Berserker - two-handed weapon or aggressive icon
                    return GetValheimSpriteWithFallbacks(
                        "AtgeirBlackmetal", "BattleaxeCrystal", "AtgeirIron", 
                        "SledgeStagbreaker", "axe_blackmetal");
                    
                case ArchetypeClass.Rogue:
                    // Rogue - knives/daggers
                    return GetValheimSpriteWithFallbacks(
                        "KnifeBlackMetal", "KnifeSilver", "KnifeCopper", 
                        "KnifeFlint", "skill_sneak");
                    
                case ArchetypeClass.Ranger:
                    // Ranger - bow
                    return GetValheimSpriteWithFallbacks(
                        "bow_draugrfang", "bow_huntsman", "bow_finewood", 
                        "bow", "skill_bows");
                    
                case ArchetypeClass.Mage:
                    // Mage - staff or magic icon
                    return GetValheimSpriteWithFallbacks(
                        "StaffFireball", "StaffIceShards", "StaffLightning", 
                        "StaffSkeleton", "eitr");
                    
                case ArchetypeClass.Healer:
                    // Healer/Cleric - support staff or healing icon
                    return GetValheimSpriteWithFallbacks(
                        "StaffShield", "StaffGreenRoots", "Healthy", 
                        "rested", "health");
                    
                case ArchetypeClass.Monk:
                    // Monk - fists or unarmed icon
                    return GetValheimSpriteWithFallbacks(
                        "FistsFenrirClaw", "Fists", "skill_unarmed", 
                        "T_emote_flex");
                    
                default:
                    return null;
            }
        }
        
        private static Sprite GetProceduralIcon(int actionId)
        {
            int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;
            
            Color white = new Color(1f, 1f, 1f, 0.95f);
            float c = (size - 1) * 0.5f;
            
            // Draw detailed icons based on action
            switch (actionId)
            {
                case ActionFollow: // Person walking with motion lines
                    // Head
                    DrawCircle(pixels, size, c, c + 28f, 10f, white);
                    // Body
                    DrawLine(pixels, size, c, c + 18f, c, c - 8f, white, 5f);
                    // Arms swinging
                    DrawLine(pixels, size, c, c + 8f, c - 14f, c + 2f, white, 4f);
                    DrawLine(pixels, size, c, c + 8f, c + 10f, c + 14f, white, 4f);
                    // Legs walking
                    DrawLine(pixels, size, c, c - 8f, c - 12f, c - 28f, white, 4f);
                    DrawLine(pixels, size, c, c - 8f, c + 8f, c - 28f, white, 4f);
                    // Motion lines
                    DrawLine(pixels, size, c - 28f, c + 20f, c - 22f, c + 20f, white, 2f);
                    DrawLine(pixels, size, c - 30f, c + 8f, c - 24f, c + 8f, white, 2f);
                    DrawLine(pixels, size, c - 28f, c - 4f, c - 22f, c - 4f, white, 2f);
                    break;
                    
                case ActionInventory: // Backpack/bag with straps
                    // Main bag body
                    DrawLine(pixels, size, c - 20f, c + 20f, c + 20f, c + 20f, white, 4f);
                    DrawLine(pixels, size, c - 20f, c + 20f, c - 20f, c - 18f, white, 4f);
                    DrawLine(pixels, size, c + 20f, c + 20f, c + 20f, c - 18f, white, 4f);
                    DrawLine(pixels, size, c - 20f, c - 18f, c + 20f, c - 18f, white, 4f);
                    // Flap
                    DrawLine(pixels, size, c - 16f, c + 20f, c - 16f, c + 28f, white, 3f);
                    DrawLine(pixels, size, c + 16f, c + 20f, c + 16f, c + 28f, white, 3f);
                    DrawArc(pixels, size, c, c + 28f, 16f, 0f, 180f, white, 3f);
                    // Buckle
                    DrawCircle(pixels, size, c, c + 12f, 4f, white);
                    // Pocket
                    DrawLine(pixels, size, c - 10f, c - 2f, c + 10f, c - 2f, white, 2f);
                    DrawLine(pixels, size, c - 10f, c - 2f, c - 10f, c - 14f, white, 2f);
                    DrawLine(pixels, size, c + 10f, c - 2f, c + 10f, c - 14f, white, 2f);
                    DrawLine(pixels, size, c - 10f, c - 14f, c + 10f, c - 14f, white, 2f);
                    break;
                    
                case ActionStats: // Notepad/clipboard icon
                    // Clipboard body
                    DrawLine(pixels, size, c - 18f, c + 24f, c + 18f, c + 24f, white, 4f);  // Top
                    DrawLine(pixels, size, c - 18f, c + 24f, c - 18f, c - 28f, white, 4f);  // Left
                    DrawLine(pixels, size, c + 18f, c + 24f, c + 18f, c - 28f, white, 4f);  // Right
                    DrawLine(pixels, size, c - 18f, c - 28f, c + 18f, c - 28f, white, 4f);  // Bottom
                    // Clipboard clip at top
                    DrawLine(pixels, size, c - 10f, c + 24f, c - 10f, c + 32f, white, 3f);
                    DrawLine(pixels, size, c + 10f, c + 24f, c + 10f, c + 32f, white, 3f);
                    DrawLine(pixels, size, c - 10f, c + 32f, c + 10f, c + 32f, white, 3f);
                    DrawCircle(pixels, size, c, c + 28f, 4f, white);  // Clip hole
                    // Text lines on notepad
                    DrawLine(pixels, size, c - 12f, c + 14f, c + 12f, c + 14f, white, 2f);
                    DrawLine(pixels, size, c - 12f, c + 6f, c + 12f, c + 6f, white, 2f);
                    DrawLine(pixels, size, c - 12f, c - 2f, c + 8f, c - 2f, white, 2f);
                    DrawLine(pixels, size, c - 12f, c - 10f, c + 12f, c - 10f, white, 2f);
                    DrawLine(pixels, size, c - 12f, c - 18f, c + 4f, c - 18f, white, 2f);
                    break;
                    
                case ActionGatherResources: // Clean axe icon
                    // Axe handle (diagonal)
                    DrawLine(pixels, size, c - 20f, c - 28f, c + 8f, c + 20f, white, 5f);
                    // Axe head - curved blade shape
                    DrawLine(pixels, size, c - 4f, c + 8f, c + 20f, c + 20f, white, 5f);
                    DrawLine(pixels, size, c + 20f, c + 20f, c + 28f, c + 6f, white, 5f);
                    DrawLine(pixels, size, c + 28f, c + 6f, c + 18f, c - 6f, white, 5f);
                    DrawLine(pixels, size, c + 18f, c - 6f, c - 4f, c + 8f, white, 5f);
                    // Fill the axe head
                    DrawLine(pixels, size, c + 6f, c + 10f, c + 22f, c + 14f, white, 4f);
                    DrawLine(pixels, size, c + 10f, c + 6f, c + 24f, c + 10f, white, 4f);
                    DrawLine(pixels, size, c + 14f, c + 2f, c + 24f, c + 6f, white, 4f);
                    // Small wood chips flying off (shows action)
                    DrawCircle(pixels, size, c - 28f, c + 16f, 3f, white);
                    DrawCircle(pixels, size, c - 24f, c + 24f, 2f, white);
                    DrawCircle(pixels, size, c - 32f, c + 8f, 2f, white);
                    break;
                    
                case ActionTendFires: // Clean campfire icon
                    // Log base (crossed logs)
                    DrawLine(pixels, size, c - 24f, c - 18f, c + 24f, c - 18f, white, 4f);
                    DrawLine(pixels, size, c - 18f, c - 24f, c + 18f, c - 12f, white, 4f);
                    DrawLine(pixels, size, c + 18f, c - 24f, c - 18f, c - 12f, white, 4f);
                    // Main flame (center, tallest)
                    DrawLine(pixels, size, c, c - 14f, c - 4f, c + 6f, white, 4f);
                    DrawLine(pixels, size, c - 4f, c + 6f, c, c + 28f, white, 4f);
                    DrawLine(pixels, size, c, c + 28f, c + 4f, c + 6f, white, 4f);
                    DrawLine(pixels, size, c + 4f, c + 6f, c, c - 14f, white, 4f);
                    // Left flame
                    DrawLine(pixels, size, c - 12f, c - 14f, c - 16f, c + 4f, white, 3f);
                    DrawLine(pixels, size, c - 16f, c + 4f, c - 10f, c + 18f, white, 3f);
                    DrawLine(pixels, size, c - 10f, c + 18f, c - 6f, c + 2f, white, 3f);
                    DrawLine(pixels, size, c - 6f, c + 2f, c - 12f, c - 14f, white, 3f);
                    // Right flame
                    DrawLine(pixels, size, c + 12f, c - 14f, c + 16f, c + 4f, white, 3f);
                    DrawLine(pixels, size, c + 16f, c + 4f, c + 10f, c + 18f, white, 3f);
                    DrawLine(pixels, size, c + 10f, c + 18f, c + 6f, c + 2f, white, 3f);
                    DrawLine(pixels, size, c + 6f, c + 2f, c + 12f, c - 14f, white, 3f);
                    // Inner glow detail
                    DrawCircle(pixels, size, c, c - 4f, 5f, white);
                    break;
                    
                case ActionOperateSmelters: // Tall smelter/furnace with glow
                    // Furnace body - wider at bottom, narrower at top (proper taper)
                    DrawLine(pixels, size, c - 18f, c - 24f, c + 18f, c - 24f, white, 4f); // Bottom (wide)
                    DrawLine(pixels, size, c - 18f, c - 24f, c - 12f, c + 8f, white, 4f);  // Left wall (tapers in going up)
                    DrawLine(pixels, size, c + 18f, c - 24f, c + 12f, c + 8f, white, 4f);  // Right wall (tapers in going up)
                    DrawLine(pixels, size, c - 12f, c + 8f, c + 12f, c + 8f, white, 4f);   // Top of main body (narrower)
                    // Chimney/stack on top
                    DrawLine(pixels, size, c - 8f, c + 8f, c - 8f, c + 24f, white, 4f);    // Left chimney
                    DrawLine(pixels, size, c + 8f, c + 8f, c + 8f, c + 24f, white, 4f);    // Right chimney
                    DrawLine(pixels, size, c - 8f, c + 24f, c + 8f, c + 24f, white, 4f);   // Chimney top
                    // Furnace opening/mouth (arched) - at bottom
                    DrawLine(pixels, size, c - 8f, c - 24f, c - 8f, c - 10f, white, 3f);
                    DrawLine(pixels, size, c + 8f, c - 24f, c + 8f, c - 10f, white, 3f);
                    DrawArc(pixels, size, c, c - 10f, 8f, 0f, 180f, white, 3f);
                    // Glowing interior (dots to suggest heat)
                    DrawCircle(pixels, size, c, c - 18f, 4f, white);
                    DrawCircle(pixels, size, c - 4f, c - 14f, 2f, white);
                    DrawCircle(pixels, size, c + 4f, c - 14f, 2f, white);
                    // Smoke wisps from chimney
                    DrawCircle(pixels, size, c - 2f, c + 30f, 3f, white);
                    DrawCircle(pixels, size, c + 4f, c + 36f, 4f, white);
                    break;
                    
                case ActionDepositItems: // Chest with arrow going in
                    // Chest body
                    DrawLine(pixels, size, c - 22f, c + 8f, c + 22f, c + 8f, white, 4f);
                    DrawLine(pixels, size, c - 22f, c + 8f, c - 22f, c - 16f, white, 4f);
                    DrawLine(pixels, size, c + 22f, c + 8f, c + 22f, c - 16f, white, 4f);
                    DrawLine(pixels, size, c - 22f, c - 16f, c + 22f, c - 16f, white, 4f);
                    // Chest lid
                    DrawLine(pixels, size, c - 22f, c + 8f, c - 22f, c + 14f, white, 4f);
                    DrawLine(pixels, size, c + 22f, c + 8f, c + 22f, c + 14f, white, 4f);
                    DrawArc(pixels, size, c, c + 14f, 22f, 0f, 180f, white, 4f);
                    // Lock/latch
                    DrawCircle(pixels, size, c, c + 4f, 4f, white);
                    // Arrow pointing down into chest
                    DrawLine(pixels, size, c, c + 36f, c, c + 20f, white, 4f);
                    DrawLine(pixels, size, c - 8f, c + 28f, c, c + 20f, white, 4f);
                    DrawLine(pixels, size, c + 8f, c + 28f, c, c + 20f, white, 4f);
                    break;
                    
                case ActionWander: // Compass/exploration
                    // Compass circle
                    DrawArc(pixels, size, c, c, 26f, 0f, 360f, white, 3f);
                    // Cardinal points
                    DrawLine(pixels, size, c, c + 20f, c, c + 30f, white, 4f); // N arrow
                    DrawLine(pixels, size, c - 5f, c + 22f, c, c + 30f, white, 3f);
                    DrawLine(pixels, size, c + 5f, c + 22f, c, c + 30f, white, 3f);
                    DrawCircle(pixels, size, c, c - 26f, 3f, white); // S dot
                    DrawCircle(pixels, size, c - 26f, c, 3f, white); // W dot
                    DrawCircle(pixels, size, c + 26f, c, 3f, white); // E dot
                    // Center
                    DrawCircle(pixels, size, c, c, 4f, white);
                    // Needle
                    DrawLine(pixels, size, c, c, c + 8f, c + 16f, white, 3f);
                    DrawLine(pixels, size, c, c, c - 6f, c - 12f, white, 2f);
                    break;
                    
                case ActionSetHome: // House with heart/pin
                    // Roof
                    DrawLine(pixels, size, c - 26f, c + 4f, c, c + 28f, white, 5f);
                    DrawLine(pixels, size, c + 26f, c + 4f, c, c + 28f, white, 5f);
                    // Walls
                    DrawLine(pixels, size, c - 20f, c + 4f, c - 20f, c - 22f, white, 4f);
                    DrawLine(pixels, size, c + 20f, c + 4f, c + 20f, c - 22f, white, 4f);
                    // Floor
                    DrawLine(pixels, size, c - 20f, c - 22f, c + 20f, c - 22f, white, 4f);
                    // Door
                    DrawLine(pixels, size, c - 6f, c - 22f, c - 6f, c - 6f, white, 3f);
                    DrawLine(pixels, size, c + 6f, c - 22f, c + 6f, c - 6f, white, 3f);
                    DrawLine(pixels, size, c - 6f, c - 6f, c + 6f, c - 6f, white, 3f);
                    // Chimney
                    DrawLine(pixels, size, c + 10f, c + 16f, c + 10f, c + 24f, white, 4f);
                    DrawLine(pixels, size, c + 16f, c + 12f, c + 16f, c + 24f, white, 4f);
                    DrawLine(pixels, size, c + 10f, c + 24f, c + 16f, c + 24f, white, 4f);
                    break;
                    
                case ActionDismiss: // Person walking away through door/portal
                    // Door frame / portal arch
                    DrawLine(pixels, size, c - 20f, c - 28f, c - 20f, c + 16f, white, 4f);  // Left frame
                    DrawLine(pixels, size, c + 20f, c - 28f, c + 20f, c + 16f, white, 4f);  // Right frame
                    DrawArc(pixels, size, c, c + 16f, 20f, 0f, 180f, white, 4f);            // Arch top
                    // Person silhouette walking into the door (smaller, centered)
                    DrawCircle(pixels, size, c, c + 6f, 7f, white);                          // Head
                    DrawLine(pixels, size, c, c - 1f, c, c - 14f, white, 4f);                // Body
                    DrawLine(pixels, size, c, c - 6f, c - 8f, c - 2f, white, 3f);            // Left arm
                    DrawLine(pixels, size, c, c - 6f, c + 8f, c - 10f, white, 3f);           // Right arm (forward)
                    DrawLine(pixels, size, c, c - 14f, c - 6f, c - 26f, white, 3f);          // Left leg
                    DrawLine(pixels, size, c, c - 14f, c + 6f, c - 26f, white, 3f);          // Right leg
                    // Arrow pointing into door (dismissing)
                    DrawLine(pixels, size, c, c + 28f, c, c + 38f, white, 3f);
                    DrawLine(pixels, size, c - 5f, c + 33f, c, c + 28f, white, 3f);
                    DrawLine(pixels, size, c + 5f, c + 33f, c, c + 28f, white, 3f);
                    break;
                    
                default:
                    // Check if this is an archetype action
                    if (actionId >= ActionHybridArchetypeBase)
                    {
                        var archetype = (ArchetypeClass)(actionId - ActionHybridArchetypeBase);
                        DrawArchetypeIcon(pixels, size, c, white, archetype);
                    }
                    else if (actionId >= ActionArchetypeBase && actionId <= ActionArchetypeBase + 10)
                    {
                        var archetype = (ArchetypeClass)(actionId - ActionArchetypeBase);
                        DrawArchetypeIcon(pixels, size, c, white, archetype);
                    }
                    else
                    {
                        // Default question mark for unknown actions
                        DrawArc(pixels, size, c, c + 5f, 14f, 180f, 480f, white, 5f);
                        DrawLine(pixels, size, c, c - 6f, c, c - 14f, white, 5f);
                        DrawCircle(pixels, size, c, c - 22f, 4f, white);
                    }
                    break;
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
        
        private static void DrawLine(Color[] pixels, int size, float x0, float y0, float x1, float y1, Color color, float thickness)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2f));
            float halfThickness = thickness * 0.5f;
            
            for (int step = 0; step <= steps; step++)
            {
                float t = (float)step / steps;
                float px = Mathf.Lerp(x0, x1, t);
                float py = Mathf.Lerp(y0, y1, t);
                
                int minX = Mathf.Max(0, Mathf.FloorToInt(px - halfThickness - 1));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + halfThickness + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(py - halfThickness - 1));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + halfThickness + 1));
                
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dist = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (dist <= halfThickness - 0.5f)
                            pixels[y * size + x] = color;
                        else if (dist <= halfThickness + 0.5f)
                        {
                            float blend = Mathf.Clamp01(halfThickness + 0.5f - dist);
                            pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, blend);
                        }
                    }
                }
            }
        }
        
        private static void DrawCircle(Color[] pixels, int size, float cx, float cy, float radius, Color color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius + 1));
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist <= radius - 0.5f)
                        pixels[y * size + x] = color;
                    else if (dist <= radius + 0.5f)
                    {
                        float blend = Mathf.Clamp01(radius + 0.5f - dist);
                        pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, blend);
                    }
                }
            }
        }
        
        private static void DrawArchetypeIcon(Color[] pixels, int size, float c, Color white, ArchetypeClass archetype)
        {
            switch (archetype)
            {
                case ArchetypeClass.Tank: // Detailed shield
                    // Shield outline
                    DrawLine(pixels, size, c - 22f, c + 26f, c + 22f, c + 26f, white, 5f);
                    DrawLine(pixels, size, c - 22f, c + 26f, c - 22f, c - 6f, white, 5f);
                    DrawLine(pixels, size, c + 22f, c + 26f, c + 22f, c - 6f, white, 5f);
                    DrawArc(pixels, size, c, c - 6f, 22f, 180f, 360f, white, 5f);
                    // Shield boss (center circle)
                    DrawCircle(pixels, size, c, c + 8f, 8f, white);
                    // Rivets
                    DrawCircle(pixels, size, c - 14f, c + 18f, 3f, white);
                    DrawCircle(pixels, size, c + 14f, c + 18f, 3f, white);
                    break;
                    
                case ArchetypeClass.Berserker: // Two-handed axe
                    // Long handle
                    DrawLine(pixels, size, c - 24f, c + 28f, c + 20f, c - 26f, white, 5f);
                    // Double axe head
                    DrawArc(pixels, size, c + 8f, c - 14f, 18f, -30f, 90f, white, 5f);
                    DrawArc(pixels, size, c + 14f, c - 8f, 14f, 150f, 270f, white, 5f);
                    break;
                    
                case ArchetypeClass.Rogue: // Crossed daggers
                    // First dagger
                    DrawLine(pixels, size, c - 20f, c + 20f, c + 12f, c - 20f, white, 4f);
                    DrawLine(pixels, size, c - 12f, c + 8f, c - 4f, c + 12f, white, 5f); // Guard
                    // Second dagger
                    DrawLine(pixels, size, c + 20f, c + 20f, c - 12f, c - 20f, white, 4f);
                    DrawLine(pixels, size, c + 12f, c + 8f, c + 4f, c + 12f, white, 5f); // Guard
                    break;
                    
                case ArchetypeClass.Ranger: // Bow with arrow
                    // Bow body
                    DrawArc(pixels, size, c - 10f, c, 28f, 270f, 450f, white, 5f);
                    // Bowstring
                    DrawLine(pixels, size, c - 10f, c + 28f, c - 10f, c - 28f, white, 3f);
                    // Arrow
                    DrawLine(pixels, size, c - 6f, c, c + 28f, c, white, 3f);
                    DrawLine(pixels, size, c + 20f, c - 6f, c + 28f, c, white, 3f);
                    DrawLine(pixels, size, c + 20f, c + 6f, c + 28f, c, white, 3f);
                    // Fletching
                    DrawLine(pixels, size, c - 6f, c, c - 2f, c + 6f, white, 2f);
                    DrawLine(pixels, size, c - 6f, c, c - 2f, c - 6f, white, 2f);
                    break;
                    
                case ArchetypeClass.Mage: // Staff with magic orb
                    // Staff
                    DrawLine(pixels, size, c, c - 32f, c, c + 24f, white, 5f);
                    // Orb at top
                    DrawCircle(pixels, size, c, c - 24f, 10f, white);
                    // Magic sparkles around orb
                    DrawCircle(pixels, size, c - 14f, c - 20f, 3f, white);
                    DrawCircle(pixels, size, c + 14f, c - 20f, 3f, white);
                    DrawCircle(pixels, size, c - 10f, c - 32f, 2f, white);
                    DrawCircle(pixels, size, c + 10f, c - 32f, 2f, white);
                    // Staff detail
                    DrawLine(pixels, size, c - 6f, c + 16f, c + 6f, c + 16f, white, 3f);
                    break;
                    
                case ArchetypeClass.Healer: // Staff with healing cross
                    // Staff
                    DrawLine(pixels, size, c, c - 32f, c, c + 24f, white, 5f);
                    // Cross at top
                    DrawLine(pixels, size, c - 12f, c - 20f, c + 12f, c - 20f, white, 5f);
                    DrawLine(pixels, size, c, c - 32f, c, c - 8f, white, 5f);
                    // Healing aura
                    DrawArc(pixels, size, c, c - 20f, 18f, 0f, 360f, white, 2f);
                    // Staff detail
                    DrawLine(pixels, size, c - 6f, c + 16f, c + 6f, c + 16f, white, 3f);
                    break;
                    
                case ArchetypeClass.Paladin: // Shield with sword
                    // Shield
                    DrawLine(pixels, size, c - 18f, c + 22f, c + 4f, c + 22f, white, 4f);
                    DrawLine(pixels, size, c - 18f, c + 22f, c - 18f, c - 2f, white, 4f);
                    DrawLine(pixels, size, c + 4f, c + 22f, c + 4f, c - 2f, white, 4f);
                    DrawArc(pixels, size, c - 7f, c - 2f, 11f, 180f, 360f, white, 4f);
                    // Cross on shield
                    DrawLine(pixels, size, c - 12f, c + 10f, c - 2f, c + 10f, white, 3f);
                    DrawLine(pixels, size, c - 7f, c + 16f, c - 7f, c + 4f, white, 3f);
                    // Sword behind
                    DrawLine(pixels, size, c + 10f, c + 28f, c + 10f, c - 28f, white, 4f);
                    DrawLine(pixels, size, c + 4f, c + 20f, c + 16f, c + 20f, white, 4f); // Guard
                    break;
                    
                case ArchetypeClass.Monk: // Fist with energy
                    // Fist outline
                    DrawCircle(pixels, size, c, c, 18f, white);
                    // Knuckles
                    DrawLine(pixels, size, c - 10f, c + 10f, c - 10f, c - 6f, white, 5f);
                    DrawLine(pixels, size, c - 2f, c + 12f, c - 2f, c - 4f, white, 5f);
                    DrawLine(pixels, size, c + 6f, c + 10f, c + 6f, c - 6f, white, 5f);
                    // Thumb
                    DrawLine(pixels, size, c - 14f, c + 4f, c - 14f, c - 8f, white, 4f);
                    // Energy lines
                    DrawLine(pixels, size, c - 28f, c + 14f, c - 22f, c + 10f, white, 3f);
                    DrawLine(pixels, size, c - 30f, c, c - 22f, c, white, 3f);
                    DrawLine(pixels, size, c - 28f, c - 14f, c - 22f, c - 10f, white, 3f);
                    DrawLine(pixels, size, c + 22f, c + 14f, c + 28f, c + 10f, white, 3f);
                    DrawLine(pixels, size, c + 22f, c, c + 30f, c, white, 3f);
                    DrawLine(pixels, size, c + 22f, c - 14f, c + 28f, c - 10f, white, 3f);
                    break;
                    
                default: // Generic star for unknown archetype
                    DrawCircle(pixels, size, c, c, 22f, white);
                    DrawCircle(pixels, size, c, c, 10f, white);
                    break;
            }
        }
        
        private static void DrawArc(Color[] pixels, int size, float cx, float cy, float radius, float startDeg, float endDeg, Color color, float thickness)
        {
            float halfThickness = thickness * 0.5f;
            int steps = Mathf.Max(16, Mathf.CeilToInt(Mathf.Abs(endDeg - startDeg) * radius * 0.08f));
            
            for (int step = 0; step <= steps; step++)
            {
                float t = (float)step / steps;
                float angle = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                float px = cx + Mathf.Cos(angle) * radius;
                float py = cy + Mathf.Sin(angle) * radius;
                
                int minX = Mathf.Max(0, Mathf.FloorToInt(px - halfThickness - 1));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + halfThickness + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(py - halfThickness - 1));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + halfThickness + 1));
                
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dist = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        if (dist <= halfThickness - 0.5f)
                            pixels[y * size + x] = color;
                        else if (dist <= halfThickness + 0.5f)
                        {
                            float blend = Mathf.Clamp01(halfThickness + 0.5f - dist);
                            pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color, blend);
                        }
                    }
                }
            }
        }
        
        #endregion
        
        #region UI Helpers
        
        private static GameObject CreateUIElement(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            
            return go;
        }
        
        private static void SetRectTransform(RectTransform rt, Vector2 size, Vector2 position)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }
        
        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, TMP_FontAsset font, 
            float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = text;
            
            return tmp;
        }
        
        /// <summary>
        /// Gets the configured font for a given category, respecting user config settings.
        /// </summary>
        private static TMP_FontAsset GetConfiguredFont(UIFontConfig.FontCategory category)
        {
            return UIFontConfig.GetFontForCategory(category);
        }
        
        /// <summary>
        /// Legacy font getter - falls back to Primary font category.
        /// </summary>
        private static TMP_FontAsset GetFont()
        {
            // Use the configured primary font
            var font = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Primary);
            if (font != null) return font;
            
            // Fallback: Try to find any Valheim font
            foreach (var tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
            {
                if (tmp.font != null)
                    return tmp.font;
            }
            return null;
        }
        
        private void Teardown()
        {
            ClearSegmentObjects();
            _built = false;
        }
        
        #endregion
        
        #region Enums and Structs
        
        private enum AnimState
        {
            Closed,
            Opening,
            Open,
            Closing
        }
        
        private struct Segment
        {
            public string Label;
            public int ActionId;
            public bool IsToggle;
            public bool IsMode;
            public bool IsActive;
            public Color IconColor;
        }
        
        #endregion
        
        #region HUD Integration Patches
        
        /// <summary>
        /// Patch Hud.InRadial to report our radial menu as active.
        /// This prevents other UI from interfering.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "InRadial")]
        private static class HudInRadial_Patch
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result && Instance != null && Instance._visible)
                    __result = true;
            }
        }
        
        /// <summary>
        /// Patch Hud.UpdateCrosshair to hide crosshair when radial is open.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        private static class HudCrosshair_Patch
        {
            private static void Postfix(Hud __instance)
            {
                if (Instance == null || !Instance._visible) return;
                
                __instance.m_crosshair.gameObject.SetActive(false);
                __instance.m_hoverName.text = "";
                
                if (__instance.m_pieceHealthRoot != null)
                    __instance.m_pieceHealthRoot.gameObject.SetActive(false);
                
                if (__instance.m_crosshairBow != null)
                    __instance.m_crosshairBow.gameObject.SetActive(false);
            }
        }
        
        /// <summary>
        /// Patch EnemyHud.UpdateHuds to hide the companion's billboard while radial menu is open.
        /// This is more reliable than trying to find and hide it via reflection at Show() time.
        /// </summary>
        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static class EnemyHud_UpdateHuds_HideCompanionBillboard_Patch
        {
            private static void Postfix(EnemyHud __instance)
            {
                if (Instance == null || !Instance._visible || Instance._companion == null) return;
                
                var character = Instance._companion.GetComponent<Character>();
                if (character == null) return;
                
                try
                {
                    // Access the m_huds dictionary via reflection
                    var hudsField = typeof(EnemyHud).GetField("m_huds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (hudsField == null) return;
                    
                    var hudsObj = hudsField.GetValue(__instance);
                    if (hudsObj == null) return;
                    
                    var hudDataType = typeof(EnemyHud).GetNestedType("HudData", System.Reflection.BindingFlags.NonPublic);
                    if (hudDataType == null) return;
                    
                    var guiField = hudDataType.GetField("m_gui");
                    if (guiField == null) return;
                    
                    var hudsDict = hudsObj as System.Collections.IDictionary;
                    if (hudsDict == null) return;
                    
                    if (hudsDict.Contains(character))
                    {
                        var hudData = hudsDict[character];
                        if (hudData != null)
                        {
                            var gui = guiField.GetValue(hudData) as GameObject;
                            if (gui != null && gui.activeSelf)
                            {
                                gui.SetActive(false);
                                // Cache reference for restoration
                                if (Instance._cachedBillboardGui == null)
                                {
                                    Instance._cachedBillboardGui = gui;
                                    Instance._hidBillboard = true;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
        
        #endregion
    }
}
