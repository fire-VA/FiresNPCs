using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Globe HUD for Health, Eitr, Stamina and Adrenaline: liquid that fills, pours and sloshes with the player's
    /// movement (Custom/FiresGlobeLiquid), real-looking glass lit by the scene's sun or moon (Custom/FiresGlobeGlass),
    /// current/max popups while a value changes, and a breakdown tooltip when hovered with the inventory open.
    ///
    /// PREFAB LAYOUT (HealthBarOverlayVAD in the firesnpcs bundle):
    ///   Root GO      - Image with the ornate frame artwork
    ///   Health/Eitr/Stamina/Adrenaline - liquid Images, each with a *Glass child Image
    ///
    /// RENDER ORDER (back -> front): sort 1 liquid + glass, sort 3 frame, sort 4 resize wireframe, sort 5 popups.
    ///
    /// DRAG and RESIZE (ESC menu open): drag to move; double-click toggles the resize wireframe (corner handles
    /// scale proportionally, side handles stretch one axis). Position and scale persist in PlayerPrefs.
    ///
    /// Vanilla Hud bars are hidden while this overlay is active.
    /// </summary>
    public class GlobeHudController : MonoBehaviour
    {
        #region Constants

        private const string PREFAB_NAME = "HealthBarOverlayVAD";
        private const float MaxFrameSeconds = 0.1f;
        private const float ShaderTimeWrapSeconds = 3600f;
        private const int LiquidSortingOrder = 1;
        private const int FrameSortingOrder = 3;
        private const int WireframeSortingOrder = 4;

        // Scale limits
        private const float MIN_SCALE = 0.3f;
        private const float MAX_SCALE = 2.5f;

        private const float MinVisibleFraction = 0.25f;
        private static readonly Vector2 DefaultPosition = new Vector2(100f, 30f);

        // Wireframe / handle visuals
        private const float HANDLE_SIZE = 14f;
        private const float SIDE_HANDLE_LONG = 20f;
        private const float SIDE_HANDLE_SHORT = 10f;
        private const float OUTLINE_WIDTH = 2f;
        private static readonly Color WireframeColor = new Color(1f, 1f, 1f, 0.7f);
        private static readonly Color HandleColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        private static readonly Color HandleHoverColor = new Color(1f, 0.95f, 0.7f, 1f);

        // PlayerPrefs keys
        private static readonly string PREF_POS_X = "GlobeHud_PosX";
        private static readonly string PREF_POS_Y = "GlobeHud_PosY";
        private static readonly string PREF_SCALE_X = "GlobeHud_ScaleX";
        private static readonly string PREF_SCALE_Y = "GlobeHud_ScaleY";

        #endregion

        #region Fields

        private static GlobeHudController _instance;
        public static GlobeHudController Instance => _instance;

        private GameObject _root;
        private RectTransform _rootRect;

        private readonly List<GlobeView> _views = new List<GlobeView>();
        private readonly List<GlobeValuePopup> _popups = new List<GlobeValuePopup>();
        private readonly GlobeMotionSampler _motion = new GlobeMotionSampler();
        private readonly GlobeHoverTooltip _tooltip = new GlobeHoverTooltip();
        private GlobeFoodRow _foodRow;
        private readonly System.Collections.Generic.Dictionary<GlobeResource, int> _lastPulsePct = new System.Collections.Generic.Dictionary<GlobeResource, int>();

        private Player _localPlayer;
        private bool _hudReady;

        // Vanilla HUD elements
        private RectTransform _vanillaHealthPanel;
        private RectTransform _vanillaHealthBarRoot;
        private RectTransform _vanillaStaminaBar2Root;
        private RectTransform _vanillaEitrBarRoot;
        private RectTransform _vanillaAdrenalineBarRoot;
        private RectTransform _vanillaFoodBarRoot;
        private bool _vanillaHudCaptured;

        // Drag support
        private bool _isDragging;
        private Vector2 _dragOffset;
        private bool _isRepositionMode;
        private Image _rootImage;

        // Resize mode (double-click wireframe)
        private bool _isResizeMode;
        private GameObject _wireframeRoot;

        // Captured at drag-start so the first frame doesn't jump
        private float _resizeDragStartDist;
        private Vector3 _resizeDragStartScale;

        #endregion

        #region Public API

        public static void Initialize()
        {
            if (_instance != null) return;

            if (Hud.instance == null)
            {
                global::FiresRPGmaker.FiresLogger.LogDebug("[GlobeHud] Hud not ready, deferring initialization");
                var tempObj = new GameObject("GlobeHudDeferredInit");
                var init = tempObj.AddComponent<DeferredInit>();
                init.StartCoroutine(init.WaitForHud());
                return;
            }

            CreateInstance();
        }

        private static void CreateInstance()
        {
            if (_instance != null) return;
            if (Hud.instance == null) return;

            var go = new GameObject("GlobeHudController");
            go.transform.SetParent(Hud.instance.transform, false);
            _instance = go.AddComponent<GlobeHudController>();

            global::FiresRPGmaker.FiresLogger.LogDebug("[GlobeHud] Initialized");
        }

        /// <summary>Logout: the Hud and its vanilla bars are being destroyed with us, so there is nothing to restore.</summary>
        public static void Cleanup()
        {
            if (_instance == null) return;
            Destroy(_instance.gameObject);
            _instance = null;
        }

        /// <summary>UseGlobeHud changed mid-session. Turning it off brings back the vanilla bars, since the Hud stays.</summary>
        public static void ApplyEnabled(bool enabled)
        {
            if (Hud.instance == null) return;
            if (enabled)
            {
                Initialize();
                return;
            }
            if (_instance == null) return;
            _instance.RestoreVanillaHud();
            Cleanup();
        }

        private class DeferredInit : MonoBehaviour
        {
            public System.Collections.IEnumerator WaitForHud()
            {
                float elapsed = 0f;
                while (Hud.instance == null && elapsed < 15f)
                {
                    yield return new WaitForSeconds(0.5f);
                    elapsed += 0.5f;
                }
                if (Hud.instance != null)
                    CreateInstance();
                else
                    Debug.LogWarning("[GlobeHud] Hud never became ready");
                Destroy(gameObject);
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
            BuildUI();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            foreach (var view in _views) view.Destroy();
            foreach (var popup in _popups) popup.Destroy();
            _tooltip.Destroy();
            if (_root != null) Destroy(_root);
        }

        private void Update()
        {
            SaveDockSizeIfDue();
            if (!_hudReady) return;

            if (_localPlayer == null || !_localPlayer)
            {
                _localPlayer = Player.m_localPlayer;
                _motion.Reset();
                if (_localPlayer == null)
                {
                    if (_root != null) _root.SetActive(false);
                    HideOverlays();
                    return;
                }
            }

            // Reposition mode = ESC menu open
            bool wasReposition = _isRepositionMode;
            _isRepositionMode = Menu.IsVisible();

            // If we left reposition mode, exit resize mode too
            if (wasReposition && !_isRepositionMode)
                SetResizeMode(false);

            // Enable raycast on the root image only during reposition mode
            if (_rootImage != null && !_isDragging)
                _rootImage.raycastTarget = false;   // Core hit-tests the whole element itself

            // Show/hide wireframe with resize mode
            if (_wireframeRoot != null && _wireframeRoot.activeSelf != _isResizeMode)
                _wireframeRoot.SetActive(_isResizeMode);

            // Hide on full-screen map, build-mode piece selection, or the vanilla hide-HUD toggle (Ctrl+F3).
            bool shouldShow = !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !Hud.IsUserHidden();
            if (_root.activeSelf != shouldShow)
                _root.SetActive(shouldShow);

            if (!shouldShow)
            {
                HideOverlays();
                return;
            }

            // 0.0.799 (lead 10-30: GlobeHudController.Update was RPGM's top per-frame cost, ~55 us a frame): Debug split of where it goes,
            // before any trim (log-prove first). One line every 10 s.
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            HideVanillaHud();
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            AnimateLiquid();
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            _foodRow?.Tick(_localPlayer, ConfigManager.Instance.configGlobeFoodRow.Value);
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            _splitHide += t1 - t0; _splitLiquid += t2 - t1; _splitFood += t3 - t2; _splitFrames++;
            if (Time.unscaledTime >= _splitNext)
            {
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency, n = System.Math.Max(1, _splitFrames);
                if (_splitNext > 0f) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[GlobeHudCost] {_splitFrames} frame(s): liquid {_splitLiquid * f / n * 1000:0.0} us, food row {_splitFood * f / n * 1000:0.0} us, hide vanilla {_splitHide * f / n * 1000:0.0} us per frame");
                _splitNext = Time.unscaledTime + 10f; _splitHide = _splitLiquid = _splitFood = 0; _splitFrames = 0;
            }
        }
        private long _splitHide, _splitLiquid, _splitFood; private int _splitFrames; private float _splitNext;

        #endregion

        #region Liquid, Popups and Tooltip

        private void AnimateLiquid()
        {
            var config = ConfigManager.Instance;
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);
            float now = Time.unscaledTime;
            var camera = Utils.GetMainCamera();
            GlobeSceneLight.Publish(camera);

            var motion = GlobeMotion.Still;
            if (camera != null && config.configGlobeLiquidMotion.Value)
                motion = _motion.Sample(_localPlayer, camera.transform, dt).Scaled(config.configGlobeSloshStrength.Value);
            else
                _motion.Reset();

            float shaderTime = Mathf.Repeat(now, ShaderTimeWrapSeconds);
            float holdSeconds = config.configGlobePopupHoldSeconds.Value;
            bool popups = config.configGlobeValuePopups.Value;
            for (int index = 0; index < _views.Count; index++)
            {
                var view = _views[index];
                view.Read(_localPlayer);
                // globe states (OFF = today's look); FULL's strength scales with the overfill
                view.StatesOn = config.configGlobeStates.Value;
                if (view.StatesOn)
                    view.States.Update(now, dt, view.Current, view.Max, view.Resource == GlobeResource.Adrenaline ? 0f : OverfillStrength(view),
                        new GlobeStateMachine.Settings
                        {
                            LowThreshold = config.configGlobeLowThreshold.Value,
                            DamageDrop = config.configGlobeDamageDrop.Value,
                            FillingRate = config.configGlobeFillingRate.Value,
                            FullFloor = config.configGlobeFullFloor.Value,
                        });
                view.Advance(dt, motion);
                view.Apply(shaderTime);
                view.SetOverfill(config.configGlobeOverfillPulse.Value ? OverfillStrength(view) : 0f);
                _popups[index].Tick(view, now, holdSeconds, popups);
            }
            _tooltip.Tick(_views, _localPlayer, config.configGlobeInventoryTooltips.Value, now);
        }

        // base = max minus what food adds right now (vanilla's own m_foods values), so
        // class, gear and other mods' bonuses count as base. strength = smoothstep(clamp01(log2(max/base) / log2(6))): 2x ~0.39, 5x ~0.90.
        // Eitr only with a base of 1+; Adrenaline never. One Debug line when a globe's strength moves by 5 %+.
        private float OverfillStrength(GlobeView view)
        {
            var p = _localPlayer;
            if (p == null || view.Resource == GlobeResource.Adrenaline) return 0f;
            float max, food = 0f;
            var foods = p.GetFoods();
            switch (view.Resource)
            {
                case GlobeResource.Health: max = p.GetMaxHealth(); if (foods != null) foreach (var f in foods) food += f.m_health; break;
                case GlobeResource.Stamina: max = p.GetMaxStamina(); if (foods != null) foreach (var f in foods) food += f.m_stamina; break;
                case GlobeResource.Eitr: max = p.GetMaxEitr(); if (foods != null) foreach (var f in foods) food += f.m_eitr; break;
                default: return 0f;
            }
            float baseValue = max - food;
            float strength = 0f;
            if (!(baseValue <= 0f || max <= baseValue || (view.Resource == GlobeResource.Eitr && baseValue < 1f)))
            {
                float t = Mathf.Clamp01(Mathf.Log(max / baseValue, 2f) / Mathf.Log(6f, 2f));
                strength = t * t * (3f - 2f * t);
            }
            int pct = Mathf.RoundToInt(strength * 20f) * 5;
            if (!_lastPulsePct.TryGetValue(view.Resource, out int last) || last != pct)
            {
                _lastPulsePct[view.Resource] = pct;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[GlobeHud] overfill {view.Resource}: max {max:0.#}, food {food:0.#}, base {baseValue:0.#}, ratio {(baseValue > 0f ? max / baseValue : 0f):0.00}, pulse {strength:0.00}");
            }
            return strength;
        }

        private void HideOverlays()
        {
            foreach (var popup in _popups) popup.Hide();
            _tooltip.Tick(_views, _localPlayer, false, Time.unscaledTime);
        }

        #endregion

        #region Drag Handling

        /// <summary>Called by GlobeHudInteractionHandler on drag start.</summary>
        public void OnDragStart(Vector2 mousePosition)
        {
            return;   // Core's HUD dock ("rpgmaker.globes") is the only mover; the own drag is retired
            if (!_isRepositionMode || _isResizeMode) return;
            _isDragging = true;
            if (_rootRect != null)
                _dragOffset = (Vector2)_rootRect.position - mousePosition;
        }

        /// <summary>Called by GlobeHudInteractionHandler during drag.</summary>
        public void OnDragMove(Vector2 mousePosition)
        {
            return;   // retired (Core dock)
            if (!_isDragging || !_isRepositionMode) return;
            if (_rootRect == null) return;
            _rootRect.position = mousePosition + _dragOffset;
            _rootRect.anchoredPosition = KeepOnScreen(_rootRect.anchoredPosition);
        }

        /// <summary>Called by GlobeHudInteractionHandler on drag end.</summary>
        public void OnDragEnd()
        {
            if (!_isDragging) return;
            _isDragging = false;
            SavePositionAndScale();
        }

        /// <summary>Called by GlobeHudInteractionHandler on double-click.</summary>
        public void OnDoubleClick()
        {
            return;   // the own resize wireframe is retired (Core dock grip)
            if (!_isRepositionMode) return;
            SetResizeMode(!_isResizeMode);
        }

        #endregion

        #region Resize Mode (wireframe + corner / side handles)

        private void SetResizeMode(bool active)
        {
            _isResizeMode = active;
            if (_wireframeRoot != null)
                _wireframeRoot.SetActive(active);
        }

        /// <summary>
        /// Called by any resize handle on drag start. Captures the current mouse
        /// distance and scale so the first drag frame doesn't cause a jump.
        /// </summary>
        public void OnResizeDragStart(Vector2 mousePosition)
        {
            if (_dockOwned) return;
            if (_rootRect == null) return;
            _resizeDragStartScale = _rootRect.localScale;
            _resizeDragStartDist = Vector2.Distance((Vector2)_rootRect.position, mousePosition);
            if (_resizeDragStartDist < 1f) _resizeDragStartDist = 1f;
        }

        /// <summary>
        /// Called by a corner handle during drag. Applies uniform scale based on
        /// the ratio between the current and initial mouse distance from center.
        /// </summary>
        public void OnCornerDrag(Vector2 currentMouseWorld)
        {
            if (_dockOwned) return;
            if (_rootRect == null) return;

            float dist = Vector2.Distance((Vector2)_rootRect.position, currentMouseWorld);
            float ratio = dist / _resizeDragStartDist;

            float baseScale = Mathf.Max(_resizeDragStartScale.x, _resizeDragStartScale.y);
            float newScale = Mathf.Clamp(baseScale * ratio, MIN_SCALE, MAX_SCALE);
            _rootRect.localScale = new Vector3(newScale, newScale, 1f);
        }

        /// <summary>
        /// Called by a side handle during drag. Adjusts only the relevant axis.
        /// </summary>
        public void OnSideDrag(ResizeAxis axis, Vector2 currentMouseWorld)
        {
            if (_dockOwned) return;
            if (_rootRect == null) return;

            Vector2 center = (Vector2)_rootRect.position;
            float ratio;

            if (axis == ResizeAxis.Horizontal)
            {
                float dx = Mathf.Abs(currentMouseWorld.x - center.x);
                float startDx = Mathf.Max(_resizeDragStartDist * 0.5f, 1f);
                ratio = dx / startDx;
                float newX = Mathf.Clamp(_resizeDragStartScale.x * ratio, MIN_SCALE, MAX_SCALE);
                _rootRect.localScale = new Vector3(newX, _rootRect.localScale.y, 1f);
            }
            else
            {
                float dy = Mathf.Abs(currentMouseWorld.y - center.y);
                float startDy = Mathf.Max(_resizeDragStartDist * 0.5f, 1f);
                ratio = dy / startDy;
                float newY = Mathf.Clamp(_resizeDragStartScale.y * ratio, MIN_SCALE, MAX_SCALE);
                _rootRect.localScale = new Vector3(_rootRect.localScale.x, newY, 1f);
            }
        }

        /// <summary>Called by any resize handle when drag ends.</summary>
        public void OnResizeDragEnd()
        {
            SavePositionAndScale();
        }

        /// <summary>
        /// Creates the wireframe outline, four corner drag handles, and four side
        /// drag handles as children of the root. Hidden by default, shown only
        /// during resize mode.
        /// Rendered at sortingOrder +1 so they appear above the border artwork.
        /// </summary>
        private void BuildWireframe()
        {
            _wireframeRoot = new GameObject("ResizeWireframe");
            _wireframeRoot.transform.SetParent(_root.transform, false);
            var wfRect = _wireframeRoot.AddComponent<RectTransform>();
            StretchFill(wfRect);

            // Render above the border frame
            EnsureCanvas(_wireframeRoot, WireframeSortingOrder);

            // Outline edges
            CreateOutlineEdge("Top", wfRect,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -OUTLINE_WIDTH), Vector2.zero);

            CreateOutlineEdge("Bottom", wfRect,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, OUTLINE_WIDTH));

            CreateOutlineEdge("Left", wfRect,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(OUTLINE_WIDTH, 0f));

            CreateOutlineEdge("Right", wfRect,
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-OUTLINE_WIDTH, 0f), Vector2.zero);

            // Corner handles (TL, TR, BL, BR) - proportional resize
            CreateResizeHandle("HandleTL", wfRect, new Vector2(0f, 1f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleTR", wfRect, new Vector2(1f, 1f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleBL", wfRect, new Vector2(0f, 0f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleBR", wfRect, new Vector2(1f, 0f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);

            // Side handles - single-axis resize
            CreateResizeHandle("HandleTop", wfRect, new Vector2(0.5f, 1f),
                new Vector2(SIDE_HANDLE_LONG, SIDE_HANDLE_SHORT), ResizeAxis.Vertical);
            CreateResizeHandle("HandleBottom", wfRect, new Vector2(0.5f, 0f),
                new Vector2(SIDE_HANDLE_LONG, SIDE_HANDLE_SHORT), ResizeAxis.Vertical);
            CreateResizeHandle("HandleLeft", wfRect, new Vector2(0f, 0.5f),
                new Vector2(SIDE_HANDLE_SHORT, SIDE_HANDLE_LONG), ResizeAxis.Horizontal);
            CreateResizeHandle("HandleRight", wfRect, new Vector2(1f, 0.5f),
                new Vector2(SIDE_HANDLE_SHORT, SIDE_HANDLE_LONG), ResizeAxis.Horizontal);

            _wireframeRoot.SetActive(false);
        }

        private void CreateOutlineEdge(string name, RectTransform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var img = go.AddComponent<Image>();
            img.color = WireframeColor;
            img.raycastTarget = false;
        }

        private void CreateResizeHandle(string name, RectTransform parent,
            Vector2 anchor, Vector2 size, ResizeAxis axis)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = HandleColor;
            img.raycastTarget = true;

            var handle = go.AddComponent<GlobeHudResizeHandle>();
            handle.Initialize(this, img, HandleColor, HandleHoverColor, axis);
        }

        #endregion

        #region Persistence

        public void ResetPositionAndScale()
        {
            // no PlayerPrefs any more (Core dock)
            PlayerPrefs.Save();

            if (_rootRect != null)
            {
                _rootRect.anchoredPosition = DefaultPosition;
                _rootRect.localScale = new Vector3(MIN_SCALE, MIN_SCALE, 1f);
            }
        }

        private void SavePositionAndScale()
        {
            return;   // the dock saves the layout; no PlayerPrefs
            if (_rootRect == null) return;

            var pos = _rootRect.anchoredPosition;
            if (!IsFinite(pos.x) || !IsFinite(pos.y)) return;

            pos = KeepOnScreen(pos);
            _rootRect.anchoredPosition = pos;
            PlayerPrefs.SetFloat(PREF_POS_X, pos.x);
            PlayerPrefs.SetFloat(PREF_POS_Y, pos.y);
            PlayerPrefs.SetFloat(PREF_SCALE_X, _rootRect.localScale.x);
            PlayerPrefs.SetFloat(PREF_SCALE_Y, _rootRect.localScale.y);
            PlayerPrefs.Save();
        }

        // the globe HUD becomes the dockable HUD element
        // "rpgmaker.globes" via FiresShell.RegisterHudElement (reflected: older Cores keep the globes' own drag/resize). Core sets only
        // position + localScale; while it owns them, the globes' own drag / resize / save step aside. Registered once, after the
        // saved position is loaded, so the first default is where the globes are today.
        private bool _dockOwned;
        private void RegisterWithDock()
        {
            if (_dockOwned || _rootRect == null) return;
            // ScalePerAxis = the old OnSideDrag model, done by
            // Core - side grips change ONLY localScale.x or .y of the whole root, corners both (clamp 0.3..2.5 in Core). No re-layout, no new math.
            // A width / height re-layout squashed the left globe and moved the liquids out of their rings, so the WHOLE globe HUD scales proportionally with
            // one uniform scale (Core's grip, no SizeChanged), so every globe, the bar and the food row keep their proportions and never
            // squash. Still a LIVE element (drag any time, grip revealed on double-click). Drag area = the whole drawn element (Core's own
            // hit test), not a thin bar image. The legacy [Globe HUD] Width / Height keys are ignored.
            if (_rootImage != null) _rootImage.raycastTarget = false;
            _dockOwned = CoreUiBridge.RegisterHud("rpgmaker.globes", _rootRect, "Globes", "FiresRPGmaker", resizable: true,
                liveEdit: true, dragHandle: null, revealOnDoubleClick: true, scalePerAxis: true, hitBounds: BuildHitBounds());   // per-axis scale via Core
            if (_dockOwned && _isRepositionMode) SetResizeMode(false);
            if (_dockOwned)   // retire the pre-Core drag handler + 8-handle wireframe, so the
            {                  // EventSystem hands every drag / click on the globes to Core's 4 per-axis grips
                var oldHandler = _root != null ? _root.GetComponent<GlobeHudInteractionHandler>() : null;
                if (oldHandler != null) Destroy(oldHandler);
                if (_wireframeRoot != null) { _wireframeRoot.transform.SetParent(null, false); Destroy(_wireframeRoot); _wireframeRoot = null; }
                global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] old drag handler {(oldHandler != null ? "retired" : "absent")}; wireframe retired; Core grips only");
            }
            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] {(_dockOwned ? "live Core HUD element 'rpgmaker.globes' (drag any time, per-axis scale as 0.0.582: side grips X or Y, corner both; grip on double-click)" : "Core HUD dock not available (older Core)")}; size {_rootRect.sizeDelta.x:0}x{_rootRect.sizeDelta.y:0}, scale {_rootRect.localScale.x:0.00}");
        }

        // (Core 0.2.603 HudElementOptions.HitBounds: a stray transparent child stretched Core's measured bounds to
        // 570x650, so the grip sat far from the art): a graphic-less child rect that wraps ONLY the globe art - the bar (the root's own rect)
        // and the four globes - computed once at build in root-local units, so it scales and moves with the root.
        private RectTransform BuildHitBounds()
        {
            if (_rootRect == null) return null;
            var existing = _rootRect.Find("GlobeHitBounds") as RectTransform;
            if (existing != null) return existing;
            // union only the DRAWN art - enabled Images with
            // visible alpha, skipping full-size layers (>= 90% of the root on both axes), each trimmed by its sprite's transparent padding
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            var c = new Vector3[4]; var used = new List<string>();
            Vector2 rs = _rootRect.rect.size;
            foreach (var im in _rootRect.GetComponentsInChildren<Image>(false))
            {
                if (im == null || !im.enabled || im.color.a < 0.05f || im.sprite == null) continue;
                var irt = im.rectTransform;
                irt.GetWorldCorners(c);
                Vector2 a = _rootRect.InverseTransformPoint(c[0]), b = _rootRect.InverseTransformPoint(c[2]);
                Vector2 lo = Vector2.Min(a, b), hi = Vector2.Max(a, b), sz = hi - lo;
                if (sz.x >= rs.x * 0.9f && sz.y >= rs.y * 0.9f) continue;
                if (sz.x < 2f || sz.y < 2f) continue;
                try
                {
                    var pad = UnityEngine.Sprites.DataUtility.GetPadding(im.sprite);   // l, b, r, t in sprite pixels
                    var sr = im.sprite.rect.size;
                    if (sr.x > 0 && sr.y > 0)
                    {
                        lo += new Vector2(sz.x * pad.x / sr.x, sz.y * pad.y / sr.y);
                        hi -= new Vector2(sz.x * pad.z / sr.x, sz.y * pad.w / sr.y);
                    }
                }
                catch { }
                if (hi.x <= lo.x || hi.y <= lo.y) continue;
                min = Vector2.Min(min, lo); max = Vector2.Max(max, hi);
                used.Add($"{im.name} {hi.x - lo.x:0}x{hi.y - lo.y:0}");
            }
            if (used.Count == 0) { Debug.LogWarning("[GlobeHud] hit bounds: no drawn Images found - using the root"); return null; }
            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] hit bounds images ({used.Count}): {string.Join(", ", used)}");
            var go = new GameObject("GlobeHitBounds", typeof(RectTransform));
            var rt = (RectTransform)go.transform; rt.SetParent(_rootRect, false);
            rt.anchorMin = rt.anchorMax = _rootRect.pivot; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = max - min; rt.anchoredPosition = (min + max) * 0.5f;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] hit bounds (drawn art): {rt.sizeDelta.x:0}x{rt.sizeDelta.y:0} at {rt.anchoredPosition} (root-local; root rect {_rootRect.rect.width:0}x{_rootRect.rect.height:0})");
            return rt;
        }

        // ---- width / height resize ----
        private struct ChildHome { public RectTransform Rt; public bool Right; public float Edge, CenterYFrac; public Vector2 Size; }
        private readonly List<ChildHome> _homes = new List<ChildHome>();
        private Vector2 _nativeSize;
        private static BepInEx.Configuration.ConfigEntry<float> s_width, s_height;
        private Vector2 _sizePending; private float _sizeDue = -1f;

        private void CaptureLayout()
        {
            _homes.Clear();
            _nativeSize = _rootRect.rect.size;
            float w = Mathf.Max(1f, _nativeSize.x), h = Mathf.Max(1f, _nativeSize.y);
            foreach (Transform c in _rootRect)
            {
                if (!(c is RectTransform rt)) continue;
                var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                Vector2 a = _rootRect.InverseTransformPoint(corners[0]), b = _rootRect.InverseTransformPoint(corners[2]);
                // root-local, pivot bottom-left: x from 0..w, y from 0..h
                a -= _rootRect.rect.min; b -= _rootRect.rect.min;
                float cx = (a.x + b.x) * 0.5f;
                bool right = cx > w * 0.5f;
                _homes.Add(new ChildHome { Rt = rt, Right = right, Edge = right ? w - a.x : a.x, CenterYFrac = ((a.y + b.y) * 0.5f) / h, Size = b - a });
            }
            if (s_width == null)
            {
                s_width = global::FiresRPGmaker.FiresRPGmaker.Instance.Config.Bind("Globe HUD", "Width", 0f, "Globe HUD width set by the layout resize grip (0 = the art's own width).");
                s_height = global::FiresRPGmaker.FiresRPGmaker.Instance.Config.Bind("Globe HUD", "Height", 0f, "Globe HUD height set by the layout resize grip (0 = the art's own height).");
                global::FiresRPGmaker.RpgConfigUi.Apply(global::FiresRPGmaker.FiresRPGmaker.Instance.Config);   // late bind: tag it (hidden)
            }
        }

        private void ApplySavedSize()
        {
            float w = s_width != null && s_width.Value > 1f ? s_width.Value : _nativeSize.x;
            float h = s_height != null && s_height.Value > 1f ? s_height.Value : _nativeSize.y;
            RelayoutGlobes(new Vector2(w, h));
        }

        // The globes and the food row keep their native size; each stays the same distance from ITS side (left or right) and at the same
        // height fraction; the bar (the root's Image) fills the new width / height.
        private void RelayoutGlobes(Vector2 size)
        {
            if (_rootRect == null) return;
            _rootRect.sizeDelta = size;
            foreach (var hm in _homes)
            {
                if (hm.Rt == null) continue;
                float x0 = hm.Right ? size.x - hm.Edge : hm.Edge;
                float y0 = hm.CenterYFrac * size.y - hm.Size.y * 0.5f;
                hm.Rt.anchorMin = hm.Rt.anchorMax = Vector2.zero;
                hm.Rt.sizeDelta = hm.Size;
                hm.Rt.anchoredPosition = new Vector2(x0 + hm.Size.x * hm.Rt.pivot.x, y0 + hm.Size.y * hm.Rt.pivot.y);
            }
        }

        private void OnDockSize(Vector2 size)
        {
            RelayoutGlobes(size);
            _sizePending = size; _sizeDue = Time.unscaledTime + 0.5f;   // saved once the drag rests (BepInEx rewrites the cfg per set)
        }

        private void ResetDockSize()
        {
            _sizeDue = -1f;
            if (s_width != null) s_width.Value = 0f;
            if (s_height != null) s_height.Value = 0f;
            RelayoutGlobes(_nativeSize);
            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] size reset to the art's own {_nativeSize.x:0}x{_nativeSize.y:0} (Core layout Reset)");
        }

        private void SaveDockSizeIfDue()
        {
            if (_sizeDue < 0f || Time.unscaledTime < _sizeDue) return;
            _sizeDue = -1f;
            if (s_width != null) s_width.Value = _sizePending.x;
            if (s_height != null) s_height.Value = _sizePending.y;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] size saved {_sizePending.x:0}x{_sizePending.y:0} (Core grip)");
        }

        private void LoadPositionAndScale()
        {
            if (_rootRect == null) return;

            // the default spot only; Core's dock applies the saved layout when it registers
            var pos = DefaultPosition;
            float scaleX = MIN_SCALE, scaleY = MIN_SCALE;

            if (!IsFinite(pos.x) || !IsFinite(pos.y)) pos = DefaultPosition;
            scaleX = IsFinite(scaleX) ? Mathf.Clamp(scaleX, MIN_SCALE, MAX_SCALE) : MIN_SCALE;
            scaleY = IsFinite(scaleY) ? Mathf.Clamp(scaleY, MIN_SCALE, MAX_SCALE) : MIN_SCALE;

            _rootRect.localScale = new Vector3(scaleX, scaleY, 1f);
            _rootRect.anchoredPosition = KeepOnScreen(pos);
        }

        /// <summary>
        /// Keeps at least a quarter of the scaled frame inside the HUD area. The frame art has transparent margins,
        /// so players park its corner past the screen edge; only a frame that would be lost off screen is pulled back.
        /// </summary>
        private Vector2 KeepOnScreen(Vector2 position)
        {
            var area = _rootRect.parent as RectTransform;
            if (area == null) return position;
            Vector2 areaSize = area.rect.size;
            if (areaSize.x <= 0f || areaSize.y <= 0f) return position;

            Vector2 size = Vector2.Scale(_rootRect.rect.size, _rootRect.localScale);
            Vector2 minVisible = size * MinVisibleFraction;
            return new Vector2(
                Mathf.Clamp(position.x, minVisible.x - size.x, areaSize.x - minVisible.x),
                Mathf.Clamp(position.y, minVisible.y - size.y, areaSize.y - minVisible.y));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion

        #region Vanilla HUD Hiding

        private void HideVanillaHud()
        {
            if (!_vanillaHudCaptured)
            {
                _vanillaHudCaptured = true;
                CaptureVanillaHudReferences();
            }

            SetVanillaElementActive(_vanillaHealthPanel, false);
            SetVanillaElementActive(_vanillaHealthBarRoot, false);
            SetVanillaElementActive(_vanillaStaminaBar2Root, false);
            SetVanillaElementActive(_vanillaEitrBarRoot, false);
            SetVanillaElementActive(_vanillaAdrenalineBarRoot, false);
            SetVanillaElementActive(_vanillaFoodBarRoot, false);
        }

        private void RestoreVanillaHud()
        {
            SetVanillaElementActive(_vanillaHealthPanel, true);
            SetVanillaElementActive(_vanillaHealthBarRoot, true);
            SetVanillaElementActive(_vanillaStaminaBar2Root, true);
            SetVanillaElementActive(_vanillaEitrBarRoot, true);
            SetVanillaElementActive(_vanillaAdrenalineBarRoot, true);
            SetVanillaElementActive(_vanillaFoodBarRoot, true);
        }

        private static void SetVanillaElementActive(RectTransform rt, bool active)
        {
            if (rt != null && rt.gameObject.activeSelf != active)
                rt.gameObject.SetActive(active);
        }

        private void CaptureVanillaHudReferences()
        {
            if (Hud.instance == null) return;

            var hud = Hud.instance;
            _vanillaHealthPanel = hud.m_healthPanel;
            _vanillaHealthBarRoot = hud.m_healthBarRoot;
            _vanillaStaminaBar2Root = hud.m_staminaBar2Root;
            _vanillaEitrBarRoot = hud.m_eitrBarRoot;
            _vanillaAdrenalineBarRoot = hud.m_adrenalineBarRoot;
            _vanillaFoodBarRoot = hud.m_foodBarRoot;

            int count = 0;
            if (_vanillaHealthPanel != null) count++;
            if (_vanillaHealthBarRoot != null) count++;
            if (_vanillaStaminaBar2Root != null) count++;
            if (_vanillaEitrBarRoot != null) count++;
            if (_vanillaAdrenalineBarRoot != null) count++;
            if (_vanillaFoodBarRoot != null) count++;

            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] Captured {count}/6 vanilla HUD element references");
        }

        #endregion

        #region UI Construction

        private void BuildUI()
        {
            // From FiresRPGmaker's OWN bundle first. Core's VAMiscAssetManager reads the one shared UIBuilderHost.AssetBundle,
            // which FiresCompanions overwrites with its bundle when it loads after us, so since 09-28 the globe looked in the
            // wrong bundle and never built. The shared path stays as a fallback.
            var prefab = LoadOwnPrefab() ?? VAMiscAssetManager.LoadUIPrefab(PREFAB_NAME);
            if (prefab == null)
            {
                Debug.LogWarning($"[GlobeHud] Prefab '{PREFAB_NAME}' not found in asset bundle.");
                _hudReady = false;
                return;
            }

            _root = Instantiate(prefab, Hud.instance.transform);
            _root.name = "GlobeHudOverlay";

            _rootRect = _root.GetComponent<RectTransform>();
            if (_rootRect == null) _rootRect = _root.AddComponent<RectTransform>();

            _rootRect.anchorMin = new Vector2(0f, 0f);
            _rootRect.anchorMax = new Vector2(0f, 0f);
            _rootRect.pivot = new Vector2(0f, 0f);

            _rootImage = _root.GetComponent<Image>();
            if (_rootImage != null)
                _rootImage.raycastTarget = false;

            var cg = _root.GetComponent<CanvasGroup>();
            if (cg == null) cg = _root.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // The root needs its own Canvas for sort-order control.
            // EnsureCanvas also adds a GraphicRaycaster so the EventSystem
            // can detect clicks/drags on this nested Canvas and its children.
            EnsureCanvas(_root, FrameSortingOrder);

            // Attach interaction handler (drag + double-click)
            var handler = _root.AddComponent<GlobeHudInteractionHandler>();
            handler.Initialize(this);

            bool complete = WireGlobe(GlobeResource.Health, GlobeLiquidSlosh.HealthFrequency)
                          & WireGlobe(GlobeResource.Eitr, GlobeLiquidSlosh.EitrFrequency)
                          & WireGlobe(GlobeResource.Stamina, GlobeLiquidSlosh.StaminaFrequency)
                          & WireGlobe(GlobeResource.Adrenaline, GlobeLiquidSlosh.AdrenalineFrequency);
            if (!complete)
            {
                Debug.LogError("[GlobeHud] The globe prefab is missing a liquid or glass image; the HUD stays off.");
                _hudReady = false;
                return;
            }

            foreach (var view in _views)
                _popups.Add(new GlobeValuePopup(Hud.instance.transform, view));

            _foodRow = new GlobeFoodRow(_root.transform);

            // Build the wireframe overlay (hidden by default)
            // no own resize wireframe (Core dock grip)

            LoadPositionAndScale();
            RegisterWithDock();   // hand the globes to Core's HUD dock (layout mode) when the Core has it

            _root.SetActive(false);
            _hudReady = true;

            global::FiresRPGmaker.FiresLogger.LogDebug($"[GlobeHud] UI built - {_views.Count} liquid globes, shaders {_views[0].Fill.material.shader.name}");
        }

        private static GameObject LoadOwnPrefab()
        {
            var bundle = global::FiresRPGmaker.FiresRPGmaker.assetBundle;
            if (bundle == null) return null;
            foreach (string path in new[] { "Assets/Custom/VAitems/UI/" + PREFAB_NAME + ".prefab", PREFAB_NAME })
            {
                var prefab = bundle.LoadAsset<GameObject>(path);
                if (prefab != null)
                {
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[GlobeHud] Prefab '{PREFAB_NAME}' loaded from FiresRPGmaker's own bundle ({path}).");
                    return prefab;
                }
            }
            return null;
        }

        private bool WireGlobe(GlobeResource resource, float sloshFrequency)
        {
            var fillTransform = _root.transform.Find(resource.ToString());
            var fill = fillTransform != null ? fillTransform.GetComponent<Image>() : null;
            var glassTransform = fillTransform != null ? fillTransform.Find(resource + "Glass") : null;
            var glass = glassTransform != null ? glassTransform.GetComponent<Image>() : null;
            if (fill == null || glass == null)
            {
                Debug.LogWarning($"[GlobeHud] '{resource}' needs a liquid Image with a '{resource}Glass' child Image");
                return false;
            }

            glassTransform.SetAsLastSibling();
            EnsureCanvas(fill.gameObject, LiquidSortingOrder);
            _views.Add(new GlobeView(resource, fill, glass, sloshFrequency));
            return true;
        }

        private static void EnsureCanvas(GameObject go, int sortingOrder)
        {
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null)
                canvas = go.AddComponent<Canvas>();

            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            // Every nested Canvas needs its own GraphicRaycaster or the
            // EventSystem won't detect clicks/drags on its child graphics.
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        #endregion
    }

    /// <summary>
    /// Handles drag-to-reposition and double-click-to-toggle-resize for the Globe HUD.
    /// Attached to the root GameObject. Only active when the ESC menu is open.
    /// </summary>
    public class GlobeHudInteractionHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private GlobeHudController _controller;
        private float _lastClickTime;
        private const float DOUBLE_CLICK_THRESHOLD = 0.3f;

        public void Initialize(GlobeHudController controller)
        {
            _controller = controller;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_controller != null)
                _controller.OnDragStart(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_controller != null)
                _controller.OnDragMove(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_controller != null)
                _controller.OnDragEnd();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_controller == null) return;

            float now = Time.unscaledTime;
            if (now - _lastClickTime < DOUBLE_CLICK_THRESHOLD)
            {
                _controller.OnDoubleClick();
                _lastClickTime = 0f;
            }
            else
            {
                _lastClickTime = now;
            }
        }
    }

    /// <summary>Axis that a resize handle operates on.</summary>
    public enum ResizeAxis
    {
        Proportional,
        Horizontal,
        Vertical
    }

    /// <summary>
    /// A drag handle for the resize wireframe. Corners scale proportionally;
    /// side handles adjust only one axis.
    /// </summary>
    public class GlobeHudResizeHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private GlobeHudController _controller;
        private Image _image;
        private Color _normalColor;
        private Color _hoverColor;
        private ResizeAxis _axis;

        public void Initialize(GlobeHudController controller, Image img,
            Color normal, Color hover, ResizeAxis axis)
        {
            _controller = controller;
            _image = img;
            _normalColor = normal;
            _hoverColor = hover;
            _axis = axis;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_controller != null)
                _controller.OnResizeDragStart(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_controller == null) return;

            if (_axis == ResizeAxis.Proportional)
                _controller.OnCornerDrag(eventData.position);
            else
                _controller.OnSideDrag(_axis, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_controller != null)
                _controller.OnResizeDragEnd();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_image != null) _image.color = _hoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_image != null) _image.color = _normalColor;
        }
    }
}
