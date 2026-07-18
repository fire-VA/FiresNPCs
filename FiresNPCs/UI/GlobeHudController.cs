using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Custom globe-style HUD overlay for Health, Eitr, Stamina, and Adrenaline.
    ///
    /// PREFAB LAYOUT:
    ///   Root GO      � Image with ornate border/frame artwork
    ///   Health       � child Image (red oval fill)
    ///   Eitr         � child Image (purple oval fill)
    ///   Stamina      � child Image (olive/yellow oval fill)
    ///   Adrenaline   � child Image (horizontal bar fill)
    ///
    /// RENDER ORDER (back ? front):
    ///    sort 1  Fills         � prefab child Images converted to Image.Type.Filled
    ///           Glass overlays � remain as children of each fill; rendered after
    ///                            the fill Image via normal child ordering, keeping
    ///                            their transparent Standard-shader material intact
    ///    sort 3  Root border   � the ornate frame artwork on the root Image
    ///    sort 4  Wireframe     � resize handles (only visible in resize mode)
    ///
    /// DRAG and RESIZE (reposition mode � ESC menu open):
    ///   - Drag anywhere on the HUD to reposition.
    ///   - Double-click to toggle resize mode: a wireframe outline appears with
    ///     four corner handles and four side handles.
    ///     Corner handles scale the HUD proportionally.
    ///     Side handles adjust only width or height for non-uniform stretching.
    ///     Double-click again or close the ESC menu to exit resize mode.
    ///   - Position and scale are saved to PlayerPrefs.
    ///
    /// Vanilla Hud bars are hidden while this overlay is active.
    /// </summary>
    public class GlobeHudController : MonoBehaviour
    {
        #region Constants

        private const string PREFAB_NAME = "HealthBarOverlayVAD";
        private const float UPDATE_INTERVAL = 0.05f;
        private const float FILL_LERP_SPEED = 5f;

        private const float LOW_THRESHOLD = 0.25f;
        private const float PULSE_SPEED = 3f;
        private const float PULSE_ALPHA_MIN = 0.5f;
        private const float PULSE_ALPHA_MAX = 0.95f;

        // Scale limits
        private const float MIN_SCALE = 0.3f;
        private const float MAX_SCALE = 2.5f;

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

        private GlobeUI _health;
        private GlobeUI _eitr;
        private GlobeUI _stamina;
        private BarUI _adrenaline;

        private Player _localPlayer;
        private float _lastUpdate;
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

        #region Data Classes

        private class GlobeUI
        {
            public GameObject Root;
            public Image FillImage;
            public Color OriginalColor;
            public float CurrentFill;
            public float TargetFill;
            public bool IsLow;
        }

        private class BarUI
        {
            public GameObject Root;
            public Image FillImage;
            public Color OriginalColor;
            public float CurrentFill;
            public float TargetFill;
            public bool IsLow;
        }

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

        public static void Cleanup()
        {
            if (_instance != null)
            {
                _instance.RestoreVanillaHud();
                Destroy(_instance.gameObject);
                _instance = null;
            }
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
            RestoreVanillaHud();
            if (_root != null) Destroy(_root);
        }

        private void Update()
        {
            if (!_hudReady) return;

            if (_localPlayer == null || !_localPlayer)
            {
                _localPlayer = Player.m_localPlayer;
                if (_localPlayer == null)
                {
                    if (_root != null) _root.SetActive(false);
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

            if (!shouldShow) return;

            HideVanillaHud();

            if (Time.time - _lastUpdate < UPDATE_INTERVAL) return;
            _lastUpdate = Time.time;

            UpdateStats();
            AnimateFills();
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
            if (_rootRect != null)
                _rootRect.position = mousePosition + _dragOffset;
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
            EnsureCanvas(_wireframeRoot, 4);

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

            // Corner handles (TL, TR, BL, BR) � proportional resize
            CreateResizeHandle("HandleTL", wfRect, new Vector2(0f, 1f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleTR", wfRect, new Vector2(1f, 1f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleBL", wfRect, new Vector2(0f, 0f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);
            CreateResizeHandle("HandleBR", wfRect, new Vector2(1f, 0f),
                new Vector2(HANDLE_SIZE, HANDLE_SIZE), ResizeAxis.Proportional);

            // Side handles � single-axis resize
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
                _rootRect.anchoredPosition = new Vector2(100f, 30f);
                _rootRect.localScale = new Vector3(MIN_SCALE, MIN_SCALE, 1f);
            }
        }

        private void SavePositionAndScale()
        {
            if (_rootRect == null) return;

            var pos = _rootRect.anchoredPosition;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) ||
                float.IsInfinity(pos.x) || float.IsInfinity(pos.y))
                return;

            PlayerPrefs.SetFloat(PREF_POS_X, pos.x);
            PlayerPrefs.SetFloat(PREF_POS_Y, pos.y);
            PlayerPrefs.SetFloat(PREF_SCALE_X, _rootRect.localScale.x);
            PlayerPrefs.SetFloat(PREF_SCALE_Y, _rootRect.localScale.y);
            PlayerPrefs.Save();
        }

        private void LoadPositionAndScale()
        {
            if (_rootRect == null) return;

            float posX = PlayerPrefs.GetFloat(PREF_POS_X, 100f);
            float posY = PlayerPrefs.GetFloat(PREF_POS_Y, 30f);
            float scaleX = PlayerPrefs.GetFloat(PREF_SCALE_X, MIN_SCALE);
            float scaleY = PlayerPrefs.GetFloat(PREF_SCALE_Y, MIN_SCALE);

            // Sanity: reject NaN/Infinity and also negative positions that
            // would place the bottom-left corner off-screen.
            if (float.IsNaN(posX) || float.IsNaN(posY) ||
                float.IsInfinity(posX) || float.IsInfinity(posY) ||
                posX < 0f || posY < 0f)
            {
                posX = 100f;
                posY = 30f;
            }
            scaleX = Mathf.Clamp(scaleX, MIN_SCALE, MAX_SCALE);
            scaleY = Mathf.Clamp(scaleY, MIN_SCALE, MAX_SCALE);

            _rootRect.anchoredPosition = new Vector2(posX, posY);
            _rootRect.localScale = new Vector3(scaleX, scaleY, 1f);
        }

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
            var prefab = VAMiscAssetManager.LoadUIPrefab(PREFAB_NAME);
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
            EnsureCanvas(_root, 3);

            // Attach interaction handler (drag + double-click)
            var handler = _root.AddComponent<GlobeHudInteractionHandler>();
            handler.Initialize(this);

            _health = WireGlobe("Health");
            _eitr = WireGlobe("Eitr");
            _stamina = WireGlobe("Stamina");
            _adrenaline = WireBar("Adrenaline");

            if (_health == null || _stamina == null)
            {
                Debug.LogError("[GlobeHud] Could not find Health or Stamina in prefab.");
                _hudReady = false;
                return;
            }

            // Build the wireframe overlay (hidden by default)
            BuildWireframe();

            LoadPositionAndScale();

            _root.SetActive(false);
            _hudReady = true;

            Debug.Log($"[GlobeHud] UI built � Health={_health != null}, Eitr={_eitr != null}, " +
                      $"Stamina={_stamina != null}, Adrenaline={_adrenaline != null}");
        }

        private GlobeUI WireGlobe(string childName)
        {
            var child = _root.transform.Find(childName);
            if (child == null)
            {
                Debug.LogWarning($"[GlobeHud] Globe child '{childName}' not found in prefab");
                return null;
            }

            var img = child.GetComponent<Image>();
            if (img == null)
            {
                Debug.LogWarning($"[GlobeHud] Globe child '{childName}' has no Image component");
                return null;
            }

            // Leave glass overlay as a child of the fill. It renders after
            // the fill Image via normal UI child ordering.
            //
            // The prefab assigns a Standard/Transparent 3D material to the
            // glass Image, but Unity's CanvasRenderer ignores 3D shader
            // transparency and renders the clear areas as solid black.
            // Fix: swap to a UI-compatible material so the sprite alpha
            // is honoured correctly.
            string glassName = childName + "Glass";
            var glassTf = child.Find(glassName);
            if (glassTf != null)
            {
                var glassImg = glassTf.GetComponent<Image>();
                if (glassImg != null)
                {
                    glassImg.raycastTarget = false;
                    glassImg.material = GetUIDefaultMaterial();
                }

                // Ensure the glass is the last sibling so it draws on top
                // of the fill Image within this Canvas.
                glassTf.SetAsLastSibling();
                Debug.Log($"[GlobeHud] {glassName} kept as child of {childName}, material swapped to UI/Default");
            }

            // Convert the original prefab Image into the runtime fill
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Vertical;
            img.fillOrigin = (int)Image.OriginVertical.Bottom;
            img.fillAmount = 1f;

            // Fill renders behind glass and border
            EnsureCanvas(child.gameObject, 1);

            var globe = new GlobeUI
            {
                Root = child.gameObject,
                FillImage = img,
                OriginalColor = img.color,
                CurrentFill = 1f,
                TargetFill = 1f
            };

            return globe;
        }

        private BarUI WireBar(string childName)
        {
            var child = _root.transform.Find(childName);
            if (child == null)
            {
                Debug.LogWarning($"[GlobeHud] Bar child '{childName}' not found in prefab");
                return null;
            }

            var img = child.GetComponent<Image>();
            if (img == null)
            {
                Debug.LogWarning($"[GlobeHud] Bar child '{childName}' has no Image component");
                return null;
            }

            // Leave glass overlay as a child (same reasoning as WireGlobe)
            string glassName = childName + "Glass";
            var glassTf = child.Find(glassName);
            if (glassTf != null)
            {
                var glassImg = glassTf.GetComponent<Image>();
                if (glassImg != null)
                {
                    glassImg.raycastTarget = false;
                    glassImg.material = GetUIDefaultMaterial();
                }
                glassTf.SetAsLastSibling();
                Debug.Log($"[GlobeHud] {glassName} kept as child of {childName}, material swapped to UI/Default");
            }

            // Convert the original prefab Image into the runtime fill
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;

            EnsureCanvas(child.gameObject, 1);

            var bar = new BarUI
            {
                Root = child.gameObject,
                FillImage = img,
                OriginalColor = img.color,
                CurrentFill = 0f,
                TargetFill = 0f
            };
            return bar;
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

        /// <summary>
        /// Returns a shared UI-compatible material that honours sprite alpha.
        /// The prefab's glass Images use a Standard/Transparent 3D material
        /// which doesn't render transparency correctly under Unity's Canvas
        /// system.  This swaps it to the built-in UI/Default shader.
        /// </summary>
        private static Material _uiDefaultMat;
        private static Material GetUIDefaultMaterial()
        {
            if (_uiDefaultMat == null)
            {
                _uiDefaultMat = new Material(Shader.Find("UI/Default"));
                _uiDefaultMat.name = "GlobeHud_UIDefault";
            }
            return _uiDefaultMat;
        }

        #endregion

        #region Stat Reading

        private void UpdateStats()
        {
            if (_localPlayer == null) return;

            if (_health != null)
            {
                float maxHp = _localPlayer.GetMaxHealth();
                _health.TargetFill = maxHp > 0f ? Mathf.Clamp01(_localPlayer.GetHealth() / maxHp) : 0f;
                _health.IsLow = _health.TargetFill < LOW_THRESHOLD;
            }

            if (_stamina != null)
            {
                float maxSta = _localPlayer.GetMaxStamina();
                _stamina.TargetFill = maxSta > 0f ? Mathf.Clamp01(_localPlayer.GetStamina() / maxSta) : 0f;
                _stamina.IsLow = _stamina.TargetFill < LOW_THRESHOLD;
            }

            if (_eitr != null)
            {
                float maxEitr = _localPlayer.GetMaxEitr();
                if (maxEitr <= 0f)
                {
                    if (_eitr.Root.activeSelf) _eitr.Root.SetActive(false);
                }
                else
                {
                    if (!_eitr.Root.activeSelf) _eitr.Root.SetActive(true);
                    _eitr.TargetFill = Mathf.Clamp01(_localPlayer.GetEitr() / maxEitr);
                    _eitr.IsLow = _eitr.TargetFill < LOW_THRESHOLD;
                }
            }

            if (_adrenaline != null)
            {
                float adrenalineVal = GetAdrenalineValue();
                _adrenaline.TargetFill = Mathf.Clamp01(adrenalineVal);
                _adrenaline.IsLow = false;
            }
        }

        private float GetAdrenalineValue()
        {
            if (_localPlayer == null) return 0f;

            try
            {
                var field = _adrenalineField;
                if (field == null && !_adrenalineFieldSearched)
                {
                    _adrenalineFieldSearched = true;

                    var playerType = typeof(Player);
                    field = playerType.GetField("m_adrenaline",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (field == null)
                    {
                        _adrenalineProperty = playerType.GetProperty("Adrenaline",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                    }
                    _adrenalineField = field;
                }

                if (_adrenalineField != null)
                {
                    object val = _adrenalineField.GetValue(_localPlayer);
                    if (val is float f) return f;
                }

                if (_adrenalineProperty != null)
                {
                    object val = _adrenalineProperty.GetValue(_localPlayer);
                    if (val is float f) return f;
                }
            }
            catch { }

            return 0f;
        }

        private static System.Reflection.FieldInfo _adrenalineField;
        private static System.Reflection.PropertyInfo _adrenalineProperty;
        private static bool _adrenalineFieldSearched;

        #endregion

        #region Animation

        private void AnimateFills()
        {
            float dt = Time.unscaledDeltaTime;
            float speed = FILL_LERP_SPEED;

            AnimateGlobe(_health, dt, speed);
            AnimateGlobe(_eitr, dt, speed);
            AnimateGlobe(_stamina, dt, speed);
            AnimateBar(_adrenaline, dt, speed);
        }

        private void AnimateGlobe(GlobeUI globe, float dt, float speed)
        {
            if (globe == null || globe.FillImage == null) return;

            float newFill = Mathf.MoveTowards(globe.CurrentFill, globe.TargetFill, dt * speed);

            // Only write to fillAmount when value actually changed � avoids dirtying canvas
            if (!Mathf.Approximately(newFill, globe.CurrentFill))
            {
                globe.CurrentFill = newFill;
                globe.FillImage.fillAmount = newFill;
            }

            if (globe.IsLow && globe.CurrentFill > 0f)
            {
                float pulse = Mathf.Lerp(PULSE_ALPHA_MIN, PULSE_ALPHA_MAX,
                    (Mathf.Sin(Time.time * PULSE_SPEED) + 1f) * 0.5f);
                var c = globe.OriginalColor;
                var newColor = new Color(c.r, c.g, c.b, pulse);
                if (globe.FillImage.color != newColor)
                    globe.FillImage.color = newColor;
            }
            else
            {
                if (globe.FillImage.color != globe.OriginalColor)
                    globe.FillImage.color = globe.OriginalColor;
            }
        }

        private void AnimateBar(BarUI bar, float dt, float speed)
        {
            if (bar == null || bar.FillImage == null) return;

            float newFill = Mathf.MoveTowards(bar.CurrentFill, bar.TargetFill, dt * speed);

            // Only write to fillAmount when value actually changed � avoids dirtying canvas
            if (!Mathf.Approximately(newFill, bar.CurrentFill))
            {
                bar.CurrentFill = newFill;
                bar.FillImage.fillAmount = newFill;
            }

            if (bar.CurrentFill > 0.01f)
            {
                float glow = Mathf.Lerp(0.7f, 1f, bar.CurrentFill);
                var c = bar.OriginalColor;
                var newColor = new Color(
                    Mathf.Min(c.r * glow, 1f),
                    Mathf.Min(c.g * glow, 1f),
                    Mathf.Min(c.b * glow, 1f),
                    c.a);
                if (bar.FillImage.color != newColor)
                    bar.FillImage.color = newColor;
            }
            else
            {
                if (bar.FillImage.color != bar.OriginalColor)
                    bar.FillImage.color = bar.OriginalColor;
            }
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
