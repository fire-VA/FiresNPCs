using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresCore.Npc;
using FiresCore.Npc.IdleBehaviors;
using FiresCore.Npc.Movement;
using FiresCore.Npc.NpcMode;
using FiresRPGmaker.Modules.Companions.NpcMode;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Work behavior toggle radial. Hold H while looking at a companion to open.
    /// Controls all idle/work sub-behavior toggles: gathering, fires, smelters, etc.
    /// Each button has a unique procedural runtime icon.
    /// </summary>
    public class CompanionIdleBehaviorRadial : MonoBehaviour
    {
        #region Action IDs

        private const int ActionGather   = 0;
        private const int ActionFires    = 1;
        private const int ActionSmelters = 2;
        private const int ActionCooking  = 3;
        private const int ActionOrganize = 4;
        private const int ActionCrafting = 5;
        private const int ActionLoot     = 6;
        private const int ActionHunt     = 7;
        private const int ActionWander   = 8;
        private const int ActionFishing  = 9;
        private const int ActionRepair   = 10;
        private const int ActionDeposit  = 11;

        #endregion

        #region Colors

        private static readonly Color BgColor         = new Color(0.10f, 0.08f, 0.14f, 0.85f);
        private static readonly Color HighlightHover  = new Color(0.55f, 0.40f, 0.85f, 0.30f);
        private static readonly Color HighlightActive = new Color(0.35f, 0.25f, 0.55f, 0.15f);
        private static readonly Color TextNormal       = new Color(0.85f, 0.80f, 0.95f, 1f);
        private static readonly Color TextHover        = new Color(1f, 0.95f, 1f, 1f);
        private static readonly Color ActiveDot        = new Color(0.5f, 0.9f, 0.5f, 1f);
        private static readonly Color InactiveDot      = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        #endregion

        #region Layout

        private const float RingRadius     = 200f;
        private const float SegSize        = 96f;
        private const float HighlightSize  = 92f;
        private const float IconSize       = 72f;
        private const float DeadZonePx    = 30f;
        private const float DeadZoneStick = 0.3f;
        private const float AnimDuration   = 0.40f;
        private const float SegStagger     = 0.030f;
        private const float BgAnimFraction = 0.4f;

        #endregion

        #region State

        private CompanionController    _companion;
        private ZNetView               _companionNview;
        private CompanionIdleBehavior  _companionIdleBehavior;
        private bool                   _visible;
        private bool                   _hidBillboard;
        private GameObject             _cachedBillboardGui;
        private bool                   _built;
        private int                    _hoveredIndex = -1;
        private bool                   _useReleasedSinceOpen;
        private AnimState              _animState;
        private float                  _animTimer;

        #endregion

        #region UI

        private Canvas               _canvas;
        private GameObject           _root;
        private GameObject           _bgCircle;
        private GameObject           _centerContainer;
        private TextMeshProUGUI      _centerTitle;
        private TextMeshProUGUI      _centerHint;
        private CanvasGroup          _bgCanvasGroup;
        private CanvasGroup          _centerCanvasGroup;

        private readonly List<Segment>            _segments        = new List<Segment>();
        private readonly List<GameObject>         _segmentGOs      = new List<GameObject>();
        private readonly List<Image>              _segHighlights   = new List<Image>();
        private readonly List<Image>              _segIcons        = new List<Image>();
        private readonly List<TextMeshProUGUI>    _segLabels       = new List<TextMeshProUGUI>();
        private readonly List<Image>              _segDots         = new List<Image>();
        private readonly List<CanvasGroup>        _segCanvasGroups = new List<CanvasGroup>();
        private readonly List<RectTransform>      _segRTs          = new List<RectTransform>();
        private readonly List<Vector2>            _segFinalPositions = new List<Vector2>();

        private static readonly Dictionary<int, Sprite> _iconCache = new Dictionary<int, Sprite>();
        private static Sprite _circleSprite;
        private static Sprite _donutSprite;

        #endregion

        #region Singleton

        public static CompanionIdleBehaviorRadial Instance { get; private set; }
        public bool IsVisible => _visible;

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("VA_IdleBehaviorRadial");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CompanionIdleBehaviorRadial>();
        }

        #endregion

        #region Unity Lifecycle

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _built = false;
            _iconCache.Clear();
            _circleSprite = null;
            _donutSprite = null;
        }

        private void Update()
        {
            if (!_visible) return;

            if (_animState == AnimState.Opening || _animState == AnimState.Closing)
                UpdateAnimation();

            if (_animState == AnimState.Closing) return;

            if (_companion == null) { Hide(); return; }

            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetKeyDown(KeyCode.H) || ZInput.GetButtonDown("JoyBack"))
            {
                Hide();
                return;
            }

            if (!_useReleasedSinceOpen && !Input.GetKey(KeyCode.H))
                _useReleasedSinceOpen = true;

            UpdateSelection();

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

        #region Show / Hide

        public void Show(CompanionController companion)
        {
            if (companion == null) return;

            _companion            = companion;
            _companionNview       = companion.GetComponent<ZNetView>();
            _companionIdleBehavior = companion.GetComponent<CompanionIdleBehavior>();

            companion.GetComponent<CompanionStateController>()?.ForceStopEmote();
            SetCompanionBillboardVisible(false);

            BuildSegments();
            if (!_built) BuildUI();
            PopulateUI();

            _root.SetActive(true);
            _visible              = true;
            _hoveredIndex         = -1;
            _useReleasedSinceOpen = false;
            _animState            = AnimState.Opening;
            _animTimer            = 0f;
            InitAnimationState();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        public void Hide()
        {
            if (!_visible || _animState == AnimState.Closing) return;
            _animState  = AnimState.Closing;
            _animTimer  = 0f;
            _hoveredIndex = -1;
        }

        private void CompleteHide()
        {
            _visible   = false;
            _animState = AnimState.Closed;

            if (_hidBillboard && _cachedBillboardGui != null)
            {
                try { _cachedBillboardGui.SetActive(true); } catch { }
                _hidBillboard      = false;
                _cachedBillboardGui = null;
            }
            else if (_companion != null)
            {
                SetCompanionBillboardVisible(true);
            }

            if (_root != null) _root.SetActive(false);
            _companion             = null;
            _companionNview        = null;
            _companionIdleBehavior = null;
        }

        #endregion

        #region Selection

        private void UpdateSelection()
        {
            if (_segments.Count == 0) return;

            bool    gamepad = ZInput.IsGamepadActive();
            Vector2 input;
            float   deadZone;

            if (gamepad)
            {
                Vector2 left  = new Vector2(ZInput.GetJoyLeftStickX(),  ZInput.GetJoyLeftStickY());
                Vector2 right = new Vector2(ZInput.GetJoyRightStickX(), ZInput.GetJoyRightStickY());
                input    = left.sqrMagnitude >= right.sqrMagnitude ? left : right;
                deadZone = DeadZoneStick;
            }
            else
            {
                input    = (Vector2)Input.mousePosition - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                deadZone = DeadZonePx;
            }

            if (input.magnitude < deadZone) { _hoveredIndex = -1; return; }

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            float segmentAngle = 360f / _segments.Count;
            _hoveredIndex = Mathf.Clamp(
                Mathf.FloorToInt(((90f - angle + 360f + segmentAngle * 0.5f) % 360f) / segmentAngle),
                0, _segments.Count - 1);
        }

        #endregion

        #region Action Execution

        private void ExecuteSelectedAction()
        {
            if (_companion == null || _companionNview == null) return;
            if (_hoveredIndex < 0 || _hoveredIndex >= _segments.Count) return;

            var seg = _segments[_hoveredIndex];
            EnsureOwnership();

            switch (seg.ActionId)
            {
                case ActionGather:   ToggleBehavior(CompanionBehaviorToggles.KEY_GATHER,   "Gathering");          break;
                case ActionFires:    ToggleBehavior(CompanionBehaviorToggles.KEY_FIRES,    "Fire tending");       break;
                case ActionSmelters: ToggleBehavior(CompanionBehaviorToggles.KEY_SMELTER,  "Smelter operation"); break;
                case ActionCooking:  ToggleBehavior(CompanionBehaviorToggles.KEY_COOKING,  "Cooking");            break;
                case ActionOrganize: ToggleBehavior(CompanionBehaviorToggles.KEY_ORGANIZE, "Chest organization"); break;
                case ActionCrafting: ToggleBehavior(CompanionBehaviorToggles.KEY_CRAFTING, "Crafting upgrades");  break;
                case ActionLoot:     ToggleBehavior(CompanionBehaviorToggles.KEY_LOOT,     "Loot pickup");        break;
                case ActionHunt:     ToggleBehavior(CompanionBehaviorToggles.KEY_HUNTING,  "Hunting wildlife");   break;
                case ActionWander:   ToggleWander(); break;
                case ActionFishing:  ToggleBehavior(CompanionBehaviorToggles.KEY_FISHING,  "Fishing");            break;
                case ActionRepair:   ToggleBehavior(CompanionBehaviorToggles.KEY_REPAIR,   "Building repair");    break;
                case ActionDeposit:  DoDepositItems(); break;
            }
        }

        private void ToggleBehavior(string key, string label)
        {
            if (CompanionPatches.AreCompanionTeleportsSuppressed()) return;
            var nview = _companion?.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            var zdo = nview.GetZDO();
            if (zdo == null) return;
            bool current = zdo.GetBool(key, true);
            zdo.Set(key, !current);
            string state = !current ? "enabled" : "disabled";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                $"{_companion.GetDisplayName()}: {label} {state}");
        }

        private void ToggleWander()
        {
            var zdo = _companionNview?.GetZDO();
            if (zdo == null) return;
            bool current = zdo.GetBool("companion_wander_enabled", true);
            zdo.Set("companion_wander_enabled", !current);

            var npcModule = _companion?.GetComponent<CompanionNpcModule>();
            if (npcModule != null && npcModule.IsStationedAsNpc)
                npcModule.ToggleIdleWandering();

            string state = !current ? "enabled" : "disabled";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                $"{_companion.GetDisplayName()}: Wandering {state}");
        }

        private void DoDepositItems()
        {
            if (_companion == null || _companionIdleBehavior == null) return;

            var nearbyChests = ChestHelper.FindNearbyChests(
                _companion.transform.position, CompanionSettings.ChestSearchRadius);

            if (nearbyChests == null || nearbyChests.Count == 0)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                    $"{_companion.GetDisplayName()}: No chests within {CompanionSettings.ChestSearchRadius}m");
                return;
            }

            var depositV2 = _companionIdleBehavior.GetSubBehavior<ChestDepositBehaviorV2>();
            if (depositV2 != null)
            {
                Container nearest = nearbyChests[0];
                float nearestDist = Vector3.Distance(_companion.transform.position, nearest.transform.position);
                foreach (var chest in nearbyChests)
                {
                    float d = Vector3.Distance(_companion.transform.position, chest.transform.position);
                    if (d < nearestDist) { nearestDist = d; nearest = chest; }
                }
                depositV2.SetCommandedTarget(nearest.gameObject);
                if (_companionIdleBehavior.TryStartSubBehavior<ChestDepositBehaviorV2>())
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                        $"{_companion.GetDisplayName()}: Depositing items ({nearbyChests.Count} chests found)");
                    Hide();
                    return;
                }
            }

            var deposit = _companionIdleBehavior.GetSubBehavior<ChestDepositBehavior>();
            if (deposit != null && _companionIdleBehavior.TryStartSubBehavior<ChestDepositBehavior>())
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                    $"{_companion.GetDisplayName()}: Depositing items");
                Hide();
                return;
            }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                $"{_companion.GetDisplayName()}: Cannot start deposit");
        }

        private void EnsureOwnership()
        {
            if (_companionNview == null || _companionNview.GetZDO() == null || _companionNview.IsOwner()) return;
            _companionNview.ClaimOwnership();
        }

        private void SetCompanionBillboardVisible(bool visible)
        {
            if (_companion == null) return;

            if (visible && _hidBillboard && _cachedBillboardGui != null)
            {
                try { _cachedBillboardGui.SetActive(true); } catch { }
                _hidBillboard      = false;
                _cachedBillboardGui = null;
                return;
            }
            if (visible && !_hidBillboard) return;

            var enemyHud = EnemyHud.instance;
            if (enemyHud == null) return;

            var character = _companion.GetComponent<Character>();
            if (character == null) return;

            try
            {
                var hudsField = typeof(EnemyHud).GetField("m_huds",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (hudsField == null) return;

                var hudsObj = hudsField.GetValue(enemyHud);
                if (hudsObj == null) return;

                var hudDataType = typeof(EnemyHud).GetNestedType("HudData",
                    System.Reflection.BindingFlags.NonPublic);
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
                        if (gui != null)
                        {
                            gui.SetActive(visible);
                            if (!visible) { _cachedBillboardGui = gui; _hidBillboard = true; }
                            else          { _cachedBillboardGui = null; _hidBillboard = false; }
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Segment Building

        private void BuildSegments()
        {
            _segments.Clear();

            var  zdo           = _companionNview?.GetZDO();
            bool wanderEnabled = zdo?.GetBool("companion_wander_enabled", true) ?? true;

            var npcModule = _companion?.GetComponent<CompanionNpcModule>();
            if (npcModule != null && npcModule.IsStationedAsNpc)
                wanderEnabled = npcModule.allowIdleWandering;

            bool gatherEnabled  = zdo?.GetBool(CompanionBehaviorToggles.KEY_GATHER,   true) ?? true;
            bool firesEnabled   = zdo?.GetBool(CompanionBehaviorToggles.KEY_FIRES,    true) ?? true;
            bool smelterEnabled = zdo?.GetBool(CompanionBehaviorToggles.KEY_SMELTER,  true) ?? true;
            bool cookingEnabled = zdo?.GetBool(CompanionBehaviorToggles.KEY_COOKING,  true) ?? true;
            bool organizeEnabled= zdo?.GetBool(CompanionBehaviorToggles.KEY_ORGANIZE, true) ?? true;
            bool craftingEnabled= zdo?.GetBool(CompanionBehaviorToggles.KEY_CRAFTING, true) ?? true;
            bool lootEnabled    = zdo?.GetBool(CompanionBehaviorToggles.KEY_LOOT,     true) ?? true;
            bool huntEnabled    = zdo?.GetBool(CompanionBehaviorToggles.KEY_HUNTING,  true) ?? true;
            bool fishingEnabled = zdo?.GetBool(CompanionBehaviorToggles.KEY_FISHING,  true) ?? true;
            bool repairEnabled  = zdo?.GetBool(CompanionBehaviorToggles.KEY_REPAIR,   true) ?? true;

            Add("Gather",   ActionGather,   true,  gatherEnabled,  new Color(0.65f, 0.45f, 0.25f));
            Add("Fires",    ActionFires,    true,  firesEnabled,   new Color(0.90f, 0.40f, 0.10f));
            Add("Smelters", ActionSmelters, true,  smelterEnabled, new Color(0.80f, 0.55f, 0.10f));
            Add("Cooking",  ActionCooking,  true,  cookingEnabled, new Color(0.85f, 0.45f, 0.30f));
            Add("Organize", ActionOrganize, true,  organizeEnabled,new Color(0.45f, 0.55f, 0.85f));
            Add("Crafting", ActionCrafting, true,  craftingEnabled,new Color(0.70f, 0.40f, 0.70f));
            Add("Loot",     ActionLoot,     true,  lootEnabled,    new Color(0.85f, 0.75f, 0.20f));
            Add("Hunt",     ActionHunt,     true,  huntEnabled,    new Color(0.75f, 0.30f, 0.25f));
            Add("Wander",   ActionWander,   true,  wanderEnabled,  new Color(0.45f, 0.75f, 0.35f));
            Add("Fishing",  ActionFishing,  true,  fishingEnabled, new Color(0.35f, 0.55f, 0.85f));
            Add("Repair",   ActionRepair,   true,  repairEnabled,  new Color(0.55f, 0.55f, 0.55f));
            Add("Deposit",  ActionDeposit,  false, false,          new Color(0.60f, 0.55f, 0.45f));
        }

        private void Add(string label, int actionId, bool isToggle, bool isActive, Color iconColor)
        {
            _segments.Add(new Segment
            {
                Label = label, ActionId = actionId,
                IsToggle = isToggle, IsActive = isActive, IconColor = iconColor
            });
        }

        private void RefreshSegmentStates()
        {
            BuildSegments();
            for (int i = 0; i < _segments.Count && i < _segDots.Count; i++)
                if (_segDots[i] != null)
                    _segDots[i].color = _segments[i].IsActive ? ActiveDot : InactiveDot;
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            var canvasGO = new GameObject("VA_IBR_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 31;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            _root = new GameObject("IBR_Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGO.transform, false);
            var rootRT = _root.GetComponent<RectTransform>();
            rootRT.anchorMin = rootRT.anchorMax = rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.sizeDelta = new Vector2(540f, 540f);

            _bgCircle = CreateUIElement("BgDonut", _root.transform, new Vector2(520f, 520f));
            var bgImg = _bgCircle.AddComponent<Image>();
            bgImg.sprite        = GetDonutSprite();
            bgImg.color         = BgColor;
            bgImg.raycastTarget = false;
            _bgCanvasGroup = _bgCircle.AddComponent<CanvasGroup>();
            _bgCanvasGroup.blocksRaycasts = false;

            _centerContainer = CreateUIElement("Center", _root.transform, new Vector2(150f, 54f));
            _centerCanvasGroup = _centerContainer.AddComponent<CanvasGroup>();
            _centerCanvasGroup.blocksRaycasts = false;

            _centerTitle = MakeText(_centerContainer.transform, "Title", "Work Orders",
                GetFont(), 15f, new Color(0.80f, 0.65f, 1f, 1f), TextAlignmentOptions.Center);
            SetRectTransform(_centerTitle.GetComponent<RectTransform>(), new Vector2(190f, 22f), new Vector2(0f, 10f));

            _centerHint = MakeText(_centerContainer.transform, "Hint", "",
                GetFont(), 10f, TextNormal, TextAlignmentOptions.Center);
            SetRectTransform(_centerHint.GetComponent<RectTransform>(), new Vector2(190f, 16f), new Vector2(0f, -8f));

            _root.SetActive(false);
            _built = true;
        }

        private void PopulateUI()
        {
            ClearSegmentObjects();

            float segAngle = 360f / _segments.Count;
            for (int i = 0; i < _segments.Count; i++)
            {
                var   segment = _segments[i];
                float angle   = (90f - segAngle * i) * Mathf.Deg2Rad;
                float x       = Mathf.Cos(angle) * RingRadius;
                float y       = Mathf.Sin(angle) * RingRadius;

                _segFinalPositions.Add(new Vector2(x, y));

                var segGO = new GameObject($"IBR_Seg_{i}", typeof(RectTransform), typeof(CanvasGroup));
                segGO.transform.SetParent(_root.transform, false);

                var segRT = segGO.GetComponent<RectTransform>();
                segRT.anchorMin = segRT.anchorMax = segRT.pivot = new Vector2(0.5f, 0.5f);
                segRT.sizeDelta = new Vector2(SegSize, SegSize);
                segRT.anchoredPosition = new Vector2(x, y);

                var segCG = segGO.GetComponent<CanvasGroup>();
                segCG.blocksRaycasts = false;

                var hlGO  = CreateUIElement("Highlight", segGO.transform, new Vector2(HighlightSize, HighlightSize));
                var hlImg = hlGO.AddComponent<Image>();
                hlImg.sprite = GetCircleSprite(); hlImg.color = Color.clear; hlImg.raycastTarget = false;

                var bgGO  = CreateUIElement("IconBg", segGO.transform, new Vector2(IconSize, IconSize));
                var bgImg = bgGO.AddComponent<Image>();
                bgImg.sprite = GetCircleSprite();
                Color bg = segment.IconColor * 0.35f;
                bgImg.color = new Color(bg.r, bg.g, bg.b, 0.5f);
                bgImg.raycastTarget = false;

                var iconGO  = CreateUIElement("Icon", segGO.transform, new Vector2(IconSize * 0.92f, IconSize * 0.92f));
                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = GetActionIcon(segment.ActionId);
                iconImg.color  = new Color(0.92f, 0.92f, 0.92f, 0.90f);
                iconImg.raycastTarget = false;

                var lbl   = MakeText(segGO.transform, "Label", segment.Label, GetFont(), 10f, TextNormal, TextAlignmentOptions.Center);
                var lblRT = lbl.GetComponent<RectTransform>();
                lblRT.anchorMin = new Vector2(0f, 0f);
                lblRT.anchorMax = new Vector2(1f, 0f);
                lblRT.pivot     = new Vector2(0.5f, 1f);
                lblRT.sizeDelta = new Vector2(0f, 14f);
                lblRT.anchoredPosition = new Vector2(0f, 30f);

                Image dotImg = null;
                if (segment.IsToggle)
                {
                    var dotGO = CreateUIElement("Dot", segGO.transform, new Vector2(8f, 8f));
                    var dotRT = dotGO.GetComponent<RectTransform>();
                    dotRT.anchorMin = dotRT.anchorMax = new Vector2(0.5f, 0f);
                    dotRT.pivot     = new Vector2(0.5f, 0.5f);
                    dotRT.anchoredPosition = new Vector2(0f, -2f);
                    dotImg = dotGO.AddComponent<Image>();
                    dotImg.sprite = GetCircleSprite();
                    dotImg.color  = segment.IsActive ? ActiveDot : InactiveDot;
                    dotImg.raycastTarget = false;
                }

                _segmentGOs.Add(segGO);
                _segHighlights.Add(hlImg);
                _segIcons.Add(iconImg);
                _segLabels.Add(lbl);
                _segDots.Add(dotImg);
                _segCanvasGroups.Add(segCG);
                _segRTs.Add(segRT);
            }

            if (_companion != null && _centerHint != null)
                _centerHint.text = _companion.GetDisplayName();
        }

        private void ClearSegmentObjects()
        {
            foreach (var go in _segmentGOs)
                if (go != null) UnityEngine.Object.Destroy(go);

            _segmentGOs.Clear(); _segHighlights.Clear(); _segIcons.Clear();
            _segLabels.Clear();  _segDots.Clear();  _segCanvasGroups.Clear();
            _segRTs.Clear();     _segFinalPositions.Clear();
        }

        #endregion

        #region Visuals / Animation

        private void RefreshVisuals()
        {
            for (int i = 0; i < _segments.Count && i < _segmentGOs.Count; i++)
            {
                bool hovered = i == _hoveredIndex;
                if (_segHighlights[i] != null)
                    _segHighlights[i].color = hovered ? HighlightHover
                                                      : (_segments[i].IsActive ? HighlightActive : Color.clear);
                if (_segLabels[i] != null)
                    _segLabels[i].color = hovered ? TextHover : TextNormal;
            }

            if (_centerHint != null)
                _centerHint.text = (_hoveredIndex >= 0 && _hoveredIndex < _segments.Count)
                    ? _segments[_hoveredIndex].Label
                    : (_companion != null ? _companion.GetDisplayName() : "");
        }

        private void InitAnimationState()
        {
            if (_bgCanvasGroup != null)     _bgCanvasGroup.alpha = 0f;
            if (_centerCanvasGroup != null) _centerCanvasGroup.alpha = 0f;
            for (int i = 0; i < _segCanvasGroups.Count; i++)
            {
                _segCanvasGroups[i].alpha = 0f;
                _segRTs[i].anchoredPosition = Vector2.zero;
            }
        }

        private void UpdateAnimation()
        {
            _animTimer += Time.deltaTime;
            bool  closing      = _animState == AnimState.Closing;
            float totalDur     = AnimDuration + _segments.Count * SegStagger;
            float bgT          = Mathf.Clamp01(_animTimer / (totalDur * BgAnimFraction));
            if (closing) bgT   = 1f - bgT;

            if (_bgCanvasGroup != null)     _bgCanvasGroup.alpha     = bgT;
            if (_centerCanvasGroup != null) _centerCanvasGroup.alpha = bgT;

            for (int i = 0; i < _segCanvasGroups.Count && i < _segFinalPositions.Count; i++)
            {
                float delay = i * SegStagger;
                float raw   = Mathf.Clamp01((_animTimer - delay) / AnimDuration);
                if (closing) raw = 1f - Mathf.Clamp01((_animTimer - delay) / AnimDuration);
                float ease  = raw < 0.5f ? 2f * raw * raw : -1f + (4f - 2f * raw) * raw;

                _segCanvasGroups[i].alpha          = ease;
                _segRTs[i].anchoredPosition        = Vector2.Lerp(Vector2.zero, _segFinalPositions[i], ease);
            }

            if (_animTimer >= totalDur)
            {
                if (closing) CompleteHide();
                else         _animState = AnimState.Open;
            }
        }

        #endregion

        #region Icons

        private static Sprite GetActionIcon(int actionId)
        {
            if (_iconCache.TryGetValue(actionId, out var cached)) return cached;
            var sprite = BuildProceduralIcon(actionId);
            if (sprite != null) _iconCache[actionId] = sprite;
            return sprite;
        }

        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            int size = 64;
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float r = size * 0.5f - 1f, cx = size * 0.5f - 0.5f, cy = size * 0.5f - 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                pixels[y * size + x] = d <= r ? Color.white : Color.clear;
            }
            tex.SetPixels(pixels); tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        private static Sprite GetDonutSprite()
        {
            if (_donutSprite != null) return _donutSprite;
            int size = 256;
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float outer = size * 0.5f - 4f, inner = size * 0.22f;
            float cx = size * 0.5f - 0.5f, cy = size * 0.5f - 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                pixels[y * size + x] = (d <= outer && d >= inner) ? Color.white : Color.clear;
            }
            tex.SetPixels(pixels); tex.Apply();
            _donutSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _donutSprite;
        }

        // Each icon is drawn on a 128x128 texture; c = center = 63.5
        private static Sprite BuildProceduralIcon(int actionId)
        {
            int size    = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
            Color w = new Color(1f, 1f, 1f, 0.95f);
            float c = (size - 1) * 0.5f;

            switch (actionId)
            {
                // Ã¢â€â‚¬Ã¢â€â‚¬ GATHER Ã¢â€â‚¬Ã¢â€â‚¬ Axe head striking stone chips
                case ActionGather:
                    // Handle (NW to SE)
                    DrawLine(pixels, size, c - 20f, c - 26f, c + 10f, c + 16f, w, 5f);
                    // Blade outline
                    DrawLine(pixels, size, c - 2f, c + 6f,  c + 18f, c + 18f, w, 5f);
                    DrawLine(pixels, size, c + 18f, c + 18f, c + 26f, c + 4f,  w, 5f);
                    DrawLine(pixels, size, c + 26f, c + 4f,  c + 16f, c - 8f,  w, 5f);
                    DrawLine(pixels, size, c + 16f, c - 8f,  c - 2f,  c + 6f,  w, 5f);
                    // Wood chips flying
                    DrawCircle(pixels, size, c - 26f, c + 14f, 3f, w);
                    DrawCircle(pixels, size, c - 22f, c + 22f, 2f, w);
                    DrawCircle(pixels, size, c - 30f, c + 6f,  2f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ FIRES Ã¢â€â‚¬Ã¢â€â‚¬ Torch: vertical handle + teardrop flame
                case ActionFires:
                    // Handle
                    DrawLine(pixels, size, c, c - 32f, c, c + 4f, w, 5f);
                    // Handle wrap band
                    DrawLine(pixels, size, c - 5f, c + 2f, c + 5f, c + 2f, w, 6f);
                    // Flame edges
                    DrawLine(pixels, size, c - 7f, c + 4f, c, c + 30f, w, 4f);
                    DrawLine(pixels, size, c + 7f, c + 4f, c, c + 30f, w, 4f);
                    // Inner flame glow
                    DrawLine(pixels, size, c - 4f, c + 10f, c, c + 24f, w, 3f);
                    DrawLine(pixels, size, c + 4f, c + 10f, c, c + 24f, w, 3f);
                    DrawCircle(pixels, size, c, c + 10f, 4f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ SMELTERS Ã¢â€â‚¬Ã¢â€â‚¬ Furnace with chimney + glowing interior
                case ActionSmelters:
                    // Body (tapers toward top)
                    DrawLine(pixels, size, c - 18f, c - 24f, c + 18f, c - 24f, w, 4f);
                    DrawLine(pixels, size, c - 18f, c - 24f, c - 12f, c + 8f,  w, 4f);
                    DrawLine(pixels, size, c + 18f, c - 24f, c + 12f, c + 8f,  w, 4f);
                    DrawLine(pixels, size, c - 12f, c + 8f,  c + 12f, c + 8f,  w, 4f);
                    // Chimney
                    DrawLine(pixels, size, c - 8f, c + 8f,  c - 8f, c + 24f, w, 4f);
                    DrawLine(pixels, size, c + 8f, c + 8f,  c + 8f, c + 24f, w, 4f);
                    DrawLine(pixels, size, c - 8f, c + 24f, c + 8f, c + 24f, w, 4f);
                    // Door arch
                    DrawLine(pixels, size, c - 8f, c - 24f, c - 8f, c - 10f, w, 3f);
                    DrawLine(pixels, size, c + 8f, c - 24f, c + 8f, c - 10f, w, 3f);
                    DrawArc (pixels, size, c, c - 10f, 8f, 0f, 180f, w, 3f);
                    // Glow dot
                    DrawCircle(pixels, size, c, c - 18f, 4f, w);
                    DrawCircle(pixels, size, c - 3f, c + 30f, 3f, w);
                    DrawCircle(pixels, size, c + 4f, c + 36f, 3f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ COOKING Ã¢â€â‚¬Ã¢â€â‚¬ Round cauldron with steam wisps
                case ActionCooking:
                    // Pot bowl (big bottom arc)
                    DrawArc(pixels, size, c, c - 8f, 22f, 180f, 360f, w, 4f);
                    // Pot rim (top opening)
                    DrawLine(pixels, size, c - 22f, c - 8f, c + 22f, c - 8f, w, 4f);
                    // Side handles
                    DrawArc(pixels, size, c - 27f, c - 8f,  7f, 270f, 450f, w, 3f);
                    DrawArc(pixels, size, c + 27f, c - 8f,  7f,  90f, 270f, w, 3f);
                    // Legs
                    DrawLine(pixels, size, c - 14f, c - 30f, c - 14f, c - 24f, w, 3f);
                    DrawLine(pixels, size, c,       c - 34f, c,       c - 28f, w, 3f);
                    DrawLine(pixels, size, c + 14f, c - 30f, c + 14f, c - 24f, w, 3f);
                    // Steam curls (left)
                    DrawArc(pixels, size, c - 8f, c + 6f,  5f, 90f, 270f, w, 2f);
                    DrawArc(pixels, size, c - 8f, c + 16f, 5f, 270f,450f, w, 2f);
                    DrawLine(pixels, size, c - 8f, c + 21f, c - 8f, c + 28f, w, 2f);
                    // Steam curls (right)
                    DrawArc(pixels, size, c + 8f, c + 8f,  5f, 270f, 450f, w, 2f);
                    DrawArc(pixels, size, c + 8f, c + 18f, 5f,  90f, 270f, w, 2f);
                    DrawLine(pixels, size, c + 8f, c + 23f, c + 8f, c + 30f, w, 2f);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ ORGANIZE Ã¢â€â‚¬Ã¢â€â‚¬ Chest with sorted bars + upward sort arrow
                case ActionOrganize:
                    // Chest body
                    DrawLine(pixels, size, c - 22f, c + 4f,  c + 22f, c + 4f,  w, 4f);
                    DrawLine(pixels, size, c - 22f, c + 4f,  c - 22f, c - 18f, w, 4f);
                    DrawLine(pixels, size, c + 22f, c + 4f,  c + 22f, c - 18f, w, 4f);
                    DrawLine(pixels, size, c - 22f, c - 18f, c + 22f, c - 18f, w, 4f);
                    // Lid arch
                    DrawLine(pixels, size, c - 22f, c + 4f,  c - 22f, c + 10f, w, 4f);
                    DrawLine(pixels, size, c + 22f, c + 4f,  c + 22f, c + 10f, w, 4f);
                    DrawArc (pixels, size, c, c + 10f, 22f, 0f, 180f, w, 4f);
                    // Sorted bars (long, medium, short = sorted descending)
                    DrawLine(pixels, size, c - 14f, c - 3f,  c + 12f, c - 3f,  w, 3f);
                    DrawLine(pixels, size, c - 14f, c - 9f,  c + 6f,  c - 9f,  w, 3f);
                    DrawLine(pixels, size, c - 14f, c - 15f, c + 0f,  c - 15f, w, 3f);
                    // Sort arrow (up) on right
                    DrawLine(pixels, size, c + 16f, c - 15f, c + 16f, c - 1f, w, 2f);
                    DrawLine(pixels, size, c + 10f, c - 8f,  c + 16f, c - 1f, w, 2f);
                    DrawLine(pixels, size, c + 22f, c - 8f,  c + 16f, c - 1f, w, 2f);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ CRAFTING Ã¢â€â‚¬Ã¢â€â‚¬ Hammer striking an anvil
                case ActionCrafting:
                    // Anvil
                    DrawLine(pixels, size, c - 22f, c - 10f, c + 22f, c - 10f, w, 4f); // base
                    DrawLine(pixels, size, c - 14f, c - 10f, c - 14f, c - 4f,  w, 4f); // left rise
                    DrawLine(pixels, size, c - 14f, c - 4f,  c + 8f,  c - 4f,  w, 4f); // top flat
                    DrawLine(pixels, size, c + 8f,  c - 4f,  c + 22f, c + 4f,  w, 4f); // horn
                    DrawLine(pixels, size, c + 22f, c - 10f, c + 22f, c - 4f,  w, 3f); // right side
                    // Hammer handle (angled, upper-left to lower-right of head)
                    DrawLine(pixels, size, c - 20f, c + 28f, c + 4f, c + 6f, w, 4f);
                    // Hammer head (thick cross)
                    DrawLine(pixels, size, c - 2f, c + 14f, c + 12f, c + 14f, w, 7f); // horizontal
                    DrawLine(pixels, size, c - 2f, c + 8f,  c - 2f,  c + 20f, w, 7f); // vertical face
                    // Impact sparks
                    DrawCircle(pixels, size, c - 6f, c + 2f,  2f, w);
                    DrawCircle(pixels, size, c + 2f, c - 2f,  2f, w);
                    DrawCircle(pixels, size, c - 10f, c,      2f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ LOOT Ã¢â€â‚¬Ã¢â€â‚¬ Stack of three coins
                case ActionLoot:
                    // Coin stack (three overlapping ovals rendered as arcs)
                    DrawArc(pixels, size, c, c - 14f, 17f, 0f, 360f, w, 3f); // bottom coin
                    DrawArc(pixels, size, c, c - 3f,  17f, 0f, 360f, w, 3f); // middle coin
                    DrawArc(pixels, size, c, c + 8f,  17f, 0f, 360f, w, 3f); // top coin
                    // Side fill connecting coins
                    DrawLine(pixels, size, c - 17f, c - 14f, c - 17f, c + 8f, w, 2f);
                    DrawLine(pixels, size, c + 17f, c - 14f, c + 17f, c + 8f, w, 2f);
                    // Shine lines on each coin
                    DrawLine(pixels, size, c - 9f, c - 14f, c + 9f, c - 14f, w, 2f);
                    DrawLine(pixels, size, c - 9f, c - 3f,  c + 9f, c - 3f,  w, 2f);
                    DrawLine(pixels, size, c - 9f, c + 8f,  c + 9f, c + 8f,  w, 2f);
                    // Glint on top coin
                    DrawCircle(pixels, size, c + 9f, c + 14f, 3f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ HUNT Ã¢â€â‚¬Ã¢â€â‚¬ Drawn bow with arrow aimed at deer silhouette
                case ActionHunt:
                    // Bow (arc on right side)
                    DrawArc(pixels, size, c + 6f, c, 22f, 120f, 240f, w, 4f);
                    // Bowstring
                    DrawLine(pixels, size, c + 6f - 11f, c + 19f, c + 6f - 11f, c - 19f, w, 2f);
                    // Arrow shaft + head (pointing right)
                    DrawLine(pixels, size, c - 22f, c, c + 14f, c, w, 3f);
                    DrawLine(pixels, size, c + 14f, c, c + 8f, c + 5f, w, 3f);
                    DrawLine(pixels, size, c + 14f, c, c + 8f, c - 5f, w, 3f);
                    // Deer (simplified, left side Ã¢â‚¬â€œ body, head, antlers, legs)
                    DrawCircle(pixels, size, c - 30f, c + 6f,  5f, w); // body
                    DrawCircle(pixels, size, c - 30f, c + 16f, 3f, w); // head
                    DrawLine(pixels, size, c - 30f, c + 19f, c - 27f, c + 25f, w, 2f); // antler L
                    DrawLine(pixels, size, c - 30f, c + 19f, c - 33f, c + 25f, w, 2f); // antler R
                    DrawLine(pixels, size, c - 34f, c + 2f,  c - 34f, c - 5f,  w, 2f); // front leg
                    DrawLine(pixels, size, c - 26f, c + 2f,  c - 26f, c - 5f,  w, 2f); // back leg
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ WANDER Ã¢â€â‚¬Ã¢â€â‚¬ Two footprints offset diagonally
                case ActionWander:
                    // Left footprint (upper-left: sole + 4 toes)
                    DrawCircle(pixels, size, c - 12f, c + 16f, 8f, w);
                    DrawCircle(pixels, size, c - 18f, c + 23f, 3f, w);
                    DrawCircle(pixels, size, c - 14f, c + 26f, 2f, w);
                    DrawCircle(pixels, size, c - 10f, c + 26f, 2f, w);
                    DrawCircle(pixels, size, c - 6f,  c + 25f, 2f, w);
                    // Right footprint (lower-right: sole + 4 toes)
                    DrawCircle(pixels, size, c + 12f, c - 8f,  8f, w);
                    DrawCircle(pixels, size, c + 6f,  c - 15f, 3f, w);
                    DrawCircle(pixels, size, c + 10f, c - 18f, 2f, w);
                    DrawCircle(pixels, size, c + 14f, c - 18f, 2f, w);
                    DrawCircle(pixels, size, c + 18f, c - 17f, 2f, w);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ FISHING Ã¢â€â‚¬Ã¢â€â‚¬ Rod + line + bobber + ripple
                case ActionFishing:
                    // Rod (thick lower handle Ã¢â€ â€™ thin tip)
                    DrawLine(pixels, size, c - 26f, c - 26f, c + 6f,  c + 12f, w, 5f);  // handle
                    DrawLine(pixels, size, c + 6f,  c + 12f, c + 22f, c + 26f, w, 2f);  // thin tip
                    // Line from tip down to bobber
                    DrawLine(pixels, size, c + 22f, c + 26f, c + 6f, c - 16f, w, 2f);
                    // Bobber
                    DrawCircle(pixels, size, c + 6f, c - 18f, 4f, w);
                    // Water ripples
                    DrawArc(pixels, size, c,      c - 28f, 8f,  180f, 360f, w, 2f);
                    DrawArc(pixels, size, c - 2f, c - 28f, 14f, 180f, 360f, w, 1f);
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ REPAIR Ã¢â€â‚¬Ã¢â€â‚¬ Hammer above cracked plank
                case ActionRepair:
                    // Plank rectangle
                    DrawLine(pixels, size, c - 26f, c - 8f,  c + 26f, c - 8f,  w, 4f); // top
                    DrawLine(pixels, size, c - 26f, c - 18f, c + 26f, c - 18f, w, 4f); // bottom
                    DrawLine(pixels, size, c - 26f, c - 8f,  c - 26f, c - 18f, w, 4f); // left
                    DrawLine(pixels, size, c + 26f, c - 8f,  c + 26f, c - 18f, w, 4f); // right
                    // Crack (lightning-bolt zigzag through plank)
                    DrawLine(pixels, size, c - 4f, c - 8f,  c + 2f, c - 12f, w, 2f);
                    DrawLine(pixels, size, c + 2f, c - 12f, c - 2f, c - 14f, w, 2f);
                    DrawLine(pixels, size, c - 2f, c - 14f, c + 4f, c - 18f, w, 2f);
                    // Hammer (handle diagonal, head horizontal above plank)
                    DrawLine(pixels, size, c - 14f, c + 22f, c + 4f, c + 4f,  w, 3f); // handle
                    DrawLine(pixels, size, c - 4f,  c + 12f, c + 10f, c + 12f, w, 7f); // head h
                    DrawLine(pixels, size, c - 4f,  c + 6f,  c - 4f,  c + 18f, w, 7f); // head v
                    break;

                // Ã¢â€â‚¬Ã¢â€â‚¬ DEPOSIT Ã¢â€â‚¬Ã¢â€â‚¬ Chest lid open + arrow pointing in from above
                case ActionDeposit:
                    // Chest body
                    DrawLine(pixels, size, c - 22f, c + 2f,  c + 22f, c + 2f,  w, 4f);
                    DrawLine(pixels, size, c - 22f, c + 2f,  c - 22f, c - 18f, w, 4f);
                    DrawLine(pixels, size, c + 22f, c + 2f,  c + 22f, c - 18f, w, 4f);
                    DrawLine(pixels, size, c - 22f, c - 18f, c + 22f, c - 18f, w, 4f);
                    // Lid (arch top)
                    DrawLine(pixels, size, c - 22f, c + 2f,  c - 22f, c + 8f,  w, 4f);
                    DrawLine(pixels, size, c + 22f, c + 2f,  c + 22f, c + 8f,  w, 4f);
                    DrawArc (pixels, size, c, c + 8f, 22f, 0f, 180f, w, 4f);
                    DrawCircle(pixels, size, c, c - 4f, 4f, w); // lock
                    // Arrow from above going down into chest
                    DrawLine(pixels, size, c, c + 32f, c, c + 14f, w, 4f); // stem
                    DrawLine(pixels, size, c - 8f, c + 24f, c, c + 14f, w, 4f); // left wing
                    DrawLine(pixels, size, c + 8f, c + 24f, c, c + 14f, w, 4f); // right wing
                    break;

                default:
                    DrawArc(pixels, size, c, c + 5f, 14f, 180f, 480f, w, 5f);
                    DrawLine(pixels, size, c, c - 6f, c, c - 14f, w, 5f);
                    DrawCircle(pixels, size, c, c - 22f, 4f, w);
                    break;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        #endregion

        #region Drawing Helpers

        private static void DrawLine(Color[] pixels, int size,
            float x0, float y0, float x1, float y1, Color color, float thickness)
        {
            int   steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(
                (x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2f));
            float halfT = thickness * 0.5f;

            for (int step = 0; step <= steps; step++)
            {
                float t  = (float)step / steps;
                float px = Mathf.Lerp(x0, x1, t);
                float py = Mathf.Lerp(y0, y1, t);
                int minX = Mathf.Max(0, Mathf.FloorToInt(px - halfT - 1));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + halfT + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(py - halfT - 1));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + halfT + 1));
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                    if (d <= halfT - 0.5f)
                        pixels[y * size + x] = color;
                    else if (d <= halfT + 0.5f)
                        pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color,
                            Mathf.Clamp01(halfT + 0.5f - d));
                }
            }
        }

        private static void DrawCircle(Color[] pixels, int size,
            float cx, float cy, float radius, Color color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius + 1));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= radius - 0.5f)
                    pixels[y * size + x] = color;
                else if (d <= radius + 0.5f)
                    pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color,
                        Mathf.Clamp01(radius + 0.5f - d));
            }
        }

        private static void DrawArc(Color[] pixels, int size,
            float cx, float cy, float radius, float startDeg, float endDeg, Color color, float thickness)
        {
            float halfT = thickness * 0.5f;
            int   steps = Mathf.Max(16, Mathf.CeilToInt(Mathf.Abs(endDeg - startDeg) * radius * 0.08f));
            for (int step = 0; step <= steps; step++)
            {
                float t     = (float)step / steps;
                float angle = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                float px    = cx + Mathf.Cos(angle) * radius;
                float py    = cy + Mathf.Sin(angle) * radius;
                int minX = Mathf.Max(0, Mathf.FloorToInt(px - halfT - 1));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(px + halfT + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(py - halfT - 1));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(py + halfT + 1));
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                    if (d <= halfT - 0.5f)
                        pixels[y * size + x] = color;
                    else if (d <= halfT + 0.5f)
                        pixels[y * size + x] = Color.Lerp(pixels[y * size + x], color,
                            Mathf.Clamp01(halfT + 0.5f - d));
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

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text,
            TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize       = fontSize;
            tmp.color          = color;
            tmp.alignment      = alignment;
            tmp.raycastTarget  = false;
            tmp.overflowMode   = TextOverflowModes.Overflow;
            tmp.text           = text;
            return tmp;
        }

        private static TMP_FontAsset GetFont()
        {
            var font = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Primary);
            if (font != null) return font;
            foreach (var tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
                if (tmp.font != null) return tmp.font;
            return null;
        }

        #endregion

        #region HUD Integration Patches

        [HarmonyPatch(typeof(Hud), "InRadial")]
        private static class HudInRadial_Patch
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result && Instance != null && Instance._visible)
                    __result = true;
            }
        }

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
                    var hudsField = typeof(EnemyHud).GetField("m_huds",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (hudsField == null) return;

                    var hudsObj = hudsField.GetValue(__instance);
                    if (hudsObj == null) return;

                    var hudDataType = typeof(EnemyHud).GetNestedType("HudData",
                        System.Reflection.BindingFlags.NonPublic);
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

        #region Enums / Structs

        private enum AnimState { Closed, Opening, Open, Closing }

        private struct Segment
        {
            public string Label;
            public int    ActionId;
            public bool   IsToggle;
            public bool   IsActive;
            public Color  IconColor;
        }

        #endregion
    }
}
