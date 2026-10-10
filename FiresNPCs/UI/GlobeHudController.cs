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
                Debug.Log("[GlobeHud] Hud not ready, deferring initialization");
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

            Debug.Log("[GlobeHud] Initialized");
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
                _rootImage.raycastTarget = _isRepositionMode;

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

            HideVanillaHud();
            AnimateLiquid();
        }

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
                view.Advance(dt, motion);
                view.Apply(shaderTime);
                _popups[index].Tick(view, now, holdSeconds, popups);
            }
            _tooltip.Tick(_views, _localPlayer, config.configGlobeInventoryTooltips.Value, now);
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
            if (!_isRepositionMode || _isResizeMode) return;
            _isDragging = true;
            if (_rootRect != null)
                _dragOffset = (Vector2)_rootRect.position - mousePosition;
        }

        /// <summary>Called by GlobeHudInteractionHandler during drag.</summary>
        public void OnDragMove(Vector2 mousePosition)
        {
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
            PlayerPrefs.DeleteKey(PREF_POS_X);
            PlayerPrefs.DeleteKey(PREF_POS_Y);
            PlayerPrefs.DeleteKey(PREF_SCALE_X);
            PlayerPrefs.DeleteKey(PREF_SCALE_Y);
            PlayerPrefs.Save();

            if (_rootRect != null)
            {
                _rootRect.anchoredPosition = DefaultPosition;
                _rootRect.localScale = new Vector3(MIN_SCALE, MIN_SCALE, 1f);
            }
        }

        private void SavePositionAndScale()
        {
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

        private void LoadPositionAndScale()
        {
            if (_rootRect == null) return;

            var pos = new Vector2(PlayerPrefs.GetFloat(PREF_POS_X, DefaultPosition.x), PlayerPrefs.GetFloat(PREF_POS_Y, DefaultPosition.y));
            float scaleX = PlayerPrefs.GetFloat(PREF_SCALE_X, MIN_SCALE);
            float scaleY = PlayerPrefs.GetFloat(PREF_SCALE_Y, MIN_SCALE);

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

            Debug.Log($"[GlobeHud] Captured {count}/6 vanilla HUD element references");
        }

        #endregion

        #region UI Construction

        private void BuildUI()
        {
            // From FiresRPGmaker's OWN bundle first. Core's VAMiscAssetManager reads the one shared UIBuilderHost.AssetBundle,
            // which FiresCompanions overwrites with its bundle when it loads after us, so since 09-28 the globe looked in the
            // wrong bundle and never built ("Prefab ... not found"; Fire, hand test 4, 10-08). The shared path stays as a fallback.
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

            // Build the wireframe overlay (hidden by default)
            BuildWireframe();

            LoadPositionAndScale();

            _root.SetActive(false);
            _hudReady = true;

            Debug.Log($"[GlobeHud] UI built - {_views.Count} liquid globes, shaders {_views[0].Fill.material.shader.name}");
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
