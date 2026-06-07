# Unity UI Decompiled Reference — Part 2: Selectables, Raycasting & Masking

Decompiled from `UnityEngine.UI.dll` (Version=1.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\UnityEngine.UI.dll`

Part 2 covers the interactive component hierarchy (Selectable ? Button/Slider),
canvas scaling, raycasting, and masking — all critical for understanding how our
UI Builder editor's click routing, interactable states, and canvas setup work.

**See also:** `UNITY_UI_DECOMPILED_REFERENCE.md` — Part 1 (LayoutRebuilder, LayoutGroup, ScrollRect)

---

## Table of Contents

1. [Selectable](#selectable) — Base class for all interactive UI elements
2. [Button](#button) — Click handler, surprisingly minimal
3. [Slider](#slider) — Value drag, visual update pattern
4. [CanvasScaler](#canvasscaler) — How ScaleWithScreenSize actually works
5. [GraphicRaycaster](#graphicraycaster) — Click detection pipeline
6. [Mask](#mask) — Stencil-based masking
7. [RectMask2D](#rectmask2d) — Rect-based clipping (no stencil)

---

## Selectable

**Key insights for our UI Builder:**

- **`colors` property** uses `SetPropertyUtility.SetStruct` which compares the full `ColorBlock` struct for equality. If the value hasn't changed, `OnSetProperty()` is NOT called. This means our `ApplyButtonDeep()` setting `btn.colors = colors` only triggers a visual update if the color actually changed — it's safe to call repeatedly.
- **`interactable` setter** has a side effect: if setting to `false` and this object is the currently selected EventSystem object, it calls `EventSystem.current.SetSelectedGameObject(null)`. This is why disabling interactable deselects the element in the EventSystem.
- **`OnSetProperty()`** calls `DoStateTransition(currentSelectionState, false)` — this is how color/sprite changes animate. Setting `colors` triggers this automatically. No manual transition call needed from our code.
- **`DoStateTransition()`** calls `StartColorTween()` which calls `m_TargetGraphic.CrossFadeColor()` — this is the actual animated color transition. The `fadeDuration` from `ColorBlock` controls the tween speed. Our `ApplyButtonDeep()` correctly sets `fadeDuration` in the `ColorBlock`.
- **`OnCanvasGroupChanged()`** walks up the parent chain checking `CanvasGroup.interactable`. If any parent `CanvasGroup` has `interactable=false`, the selectable becomes non-interactive. This is why our opacity `CanvasGroup` might accidentally block interaction — we need to ensure `CanvasGroup.interactable` stays `true` when we only want alpha control.
- **`s_Selectables` static array** — Unity maintains a flat array of ALL active selectables. The `OnEnable`/`OnDisable` pattern uses swap-remove (moves last element to the removed slot). This is O(1) but means the array is unordered. Navigation searches iterate this array.
- **`IsInteractable()`** checks BOTH `m_Interactable` AND `m_GroupsAllowInteraction` (from parent CanvasGroups). Both must be true.
- **`targetGraphic`** is the `Graphic` that receives color tints. `Button` auto-finds it via `GetComponent<Graphic>()` in `Awake()` if not set. For our builder, the background `Image` on the button GO is the target graphic.

```csharp
// Type: UnityEngine.UI.Selectable

[AddComponentMenu("UI/Selectable", 35)]
[ExecuteAlways]
[SelectionBase]
[DisallowMultipleComponent]
public class Selectable :
  UIBehaviour,
  IMoveHandler, IEventSystemHandler,
  IPointerDownHandler, IPointerUpHandler,
  IPointerEnterHandler, IPointerExitHandler,
  ISelectHandler, IDeselectHandler
{
  protected static Selectable[] s_Selectables = new Selectable[10];
  protected static int s_SelectableCount = 0;
  private bool m_EnableCalled;

  [SerializeField] private Navigation m_Navigation = Navigation.defaultNavigation;
  [SerializeField] private Transition m_Transition = Transition.ColorTint;
  [SerializeField] private ColorBlock m_Colors = ColorBlock.defaultColorBlock;
  [SerializeField] private SpriteState m_SpriteState;
  [SerializeField] private AnimationTriggers m_AnimationTriggers = new AnimationTriggers();
  [SerializeField] private bool m_Interactable = true;
  [SerializeField] private Graphic m_TargetGraphic;
  private bool m_GroupsAllowInteraction = true;
  protected int m_CurrentIndex = -1;
  private readonly List<CanvasGroup> m_CanvasGroupCache = new List<CanvasGroup>();

  // --- Properties that trigger visual state transition on change ---

  public Navigation navigation
  {
    get => m_Navigation;
    set { if (SetPropertyUtility.SetStruct(ref m_Navigation, value)) OnSetProperty(); }
  }

  public Transition transition
  {
    get => m_Transition;
    set { if (SetPropertyUtility.SetStruct(ref m_Transition, value)) OnSetProperty(); }
  }

  /// <summary>
  /// Setting colors only triggers OnSetProperty if the ColorBlock actually changed.
  /// Safe to call repeatedly from ApplyButtonDeep — no-ops if unchanged.
  /// </summary>
  public ColorBlock colors
  {
    get => m_Colors;
    set { if (SetPropertyUtility.SetStruct(ref m_Colors, value)) OnSetProperty(); }
  }

  public SpriteState spriteState
  {
    get => m_SpriteState;
    set { if (SetPropertyUtility.SetStruct(ref m_SpriteState, value)) OnSetProperty(); }
  }

  public Graphic targetGraphic
  {
    get => m_TargetGraphic;
    set { if (SetPropertyUtility.SetClass(ref m_TargetGraphic, value)) OnSetProperty(); }
  }

  /// <summary>
  /// SIDE EFFECT: If setting to false and this GO is EventSystem.current.currentSelectedGameObject,
  /// calls EventSystem.SetSelectedGameObject(null). This deselects the element.
  /// Our editor should be aware of this when toggling interactable in the inspector.
  /// </summary>
  public bool interactable
  {
    get => m_Interactable;
    set
    {
      if (!SetPropertyUtility.SetStruct(ref m_Interactable, value)) return;
      if (!m_Interactable && EventSystem.current != null &&
          EventSystem.current.currentSelectedGameObject == gameObject)
        EventSystem.current.SetSelectedGameObject(null);
      OnSetProperty();
    }
  }

  public Image image
  {
    get => m_TargetGraphic as Image;
    set => m_TargetGraphic = value;
  }

  protected override void Awake()
  {
    if (m_TargetGraphic == null)
      m_TargetGraphic = GetComponent<Graphic>();
  }

  /// <summary>
  /// Walks parent chain checking CanvasGroup.interactable.
  /// IMPORTANT: If ANY parent CanvasGroup has interactable=false, this returns false.
  /// Our opacity CanvasGroup must keep interactable=true to avoid accidentally blocking input.
  /// </summary>
  protected override void OnCanvasGroupChanged()
  {
    bool flag = ParentGroupAllowsInteraction();
    if (flag == m_GroupsAllowInteraction) return;
    m_GroupsAllowInteraction = flag;
    OnSetProperty();
  }

  private bool ParentGroupAllowsInteraction()
  {
    for (Transform t = transform; t != null; t = t.parent)
    {
      t.GetComponents(m_CanvasGroupCache);
      for (int i = 0; i < m_CanvasGroupCache.Count; i++)
      {
        if (m_CanvasGroupCache[i].enabled && !m_CanvasGroupCache[i].interactable)
          return false;
        if (m_CanvasGroupCache[i].ignoreParentGroups)
          return true;
      }
    }
    return true;
  }

  /// <summary>
  /// Both m_Interactable AND m_GroupsAllowInteraction must be true.
  /// This is checked by Button.Press(), Slider drag, Toggle click, etc.
  /// </summary>
  public virtual bool IsInteractable() => m_GroupsAllowInteraction && m_Interactable;

  /// <summary>
  /// Triggers visual state transition. Called by all property setters that affect visuals.
  /// </summary>
  private void OnSetProperty() => DoStateTransition(currentSelectionState, false);

  // --- State machine ---

  protected SelectionState currentSelectionState
  {
    get
    {
      if (!IsInteractable()) return SelectionState.Disabled;
      if (isPointerDown) return SelectionState.Pressed;
      if (hasSelection) return SelectionState.Selected;
      return isPointerInside ? SelectionState.Highlighted : SelectionState.Normal;
    }
  }

  /// <summary>
  /// Maps SelectionState ? Color/Sprite/AnimTrigger, then applies via the active transition mode.
  /// For ColorTint: calls StartColorTween which calls m_TargetGraphic.CrossFadeColor().
  /// </summary>
  protected virtual void DoStateTransition(SelectionState state, bool instant)
  {
    if (!gameObject.activeInHierarchy) return;
    Color color;
    Sprite newSprite;
    string triggername;
    switch (state)
    {
      case SelectionState.Normal:
        color = m_Colors.normalColor; newSprite = null;
        triggername = m_AnimationTriggers.normalTrigger; break;
      case SelectionState.Highlighted:
        color = m_Colors.highlightedColor; newSprite = m_SpriteState.highlightedSprite;
        triggername = m_AnimationTriggers.highlightedTrigger; break;
      case SelectionState.Pressed:
        color = m_Colors.pressedColor; newSprite = m_SpriteState.pressedSprite;
        triggername = m_AnimationTriggers.pressedTrigger; break;
      case SelectionState.Selected:
        color = m_Colors.selectedColor; newSprite = m_SpriteState.selectedSprite;
        triggername = m_AnimationTriggers.selectedTrigger; break;
      case SelectionState.Disabled:
        color = m_Colors.disabledColor; newSprite = m_SpriteState.disabledSprite;
        triggername = m_AnimationTriggers.disabledTrigger; break;
      default:
        color = Color.black; newSprite = null; triggername = string.Empty; break;
    }
    switch (m_Transition)
    {
      case Transition.ColorTint:
        StartColorTween(color * m_Colors.colorMultiplier, instant); break;
      case Transition.SpriteSwap:
        DoSpriteSwap(newSprite); break;
      case Transition.Animation:
        TriggerAnimation(triggername); break;
    }
  }

  /// <summary>
  /// Uses Graphic.CrossFadeColor for smooth color transitions.
  /// fadeDuration from ColorBlock controls the tween speed.
  /// instant=true ? immediate color change (used on enable/disable).
  /// </summary>
  private void StartColorTween(Color targetColor, bool instant)
  {
    if (m_TargetGraphic == null) return;
    m_TargetGraphic.CrossFadeColor(targetColor, instant ? 0f : m_Colors.fadeDuration, true, true);
  }

  // --- Enable/Disable: swap-remove pattern for O(1) removal ---

  protected override void OnEnable()
  {
    if (m_EnableCalled) return;
    base.OnEnable();
    // Grow array if needed
    if (s_SelectableCount == s_Selectables.Length)
    {
      var newArr = new Selectable[s_Selectables.Length * 2];
      Array.Copy(s_Selectables, newArr, s_Selectables.Length);
      s_Selectables = newArr;
    }
    m_CurrentIndex = s_SelectableCount;
    s_Selectables[m_CurrentIndex] = this;
    s_SelectableCount++;
    m_GroupsAllowInteraction = ParentGroupAllowsInteraction();
    DoStateTransition(currentSelectionState, true);
    m_EnableCalled = true;
  }

  protected override void OnDisable()
  {
    if (!m_EnableCalled) return;
    // Swap-remove: move last to this slot
    s_SelectableCount--;
    s_Selectables[s_SelectableCount].m_CurrentIndex = m_CurrentIndex;
    s_Selectables[m_CurrentIndex] = s_Selectables[s_SelectableCount];
    s_Selectables[s_SelectableCount] = null;
    InstantClearState();
    base.OnDisable();
    m_EnableCalled = false;
  }

  // --- Pointer handlers ---

  public virtual void OnPointerDown(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left) return;
    if (IsInteractable() && navigation.mode != Navigation.Mode.None && EventSystem.current != null)
      EventSystem.current.SetSelectedGameObject(gameObject, eventData);
    isPointerDown = true;
    EvaluateAndTransitionToSelectionState();
  }

  public virtual void OnPointerUp(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left) return;
    isPointerDown = false;
    EvaluateAndTransitionToSelectionState();
  }

  public virtual void OnPointerEnter(PointerEventData eventData)
  {
    isPointerInside = true;
    EvaluateAndTransitionToSelectionState();
  }

  public virtual void OnPointerExit(PointerEventData eventData)
  {
    isPointerInside = false;
    EvaluateAndTransitionToSelectionState();
  }

  public enum Transition { None, ColorTint, SpriteSwap, Animation }
  protected enum SelectionState { Normal, Highlighted, Pressed, Selected, Disabled }
}
```

---

## Button

**Key insights for our UI Builder:**

- **Extremely minimal** — inherits everything from `Selectable`. `Press()` just calls `m_OnClick.Invoke()` guarded by `IsActive() && IsInteractable()`.
- **`OnPointerClick`** only fires for `InputButton.Left`. Right-click does NOT trigger `onClick`.
- **`OnSubmit`** (keyboard/gamepad Enter) calls `Press()`, then starts a coroutine to animate back from Pressed state using `colors.fadeDuration`. This is the submit bounce animation.
- No layout or visual complexity — all visual state management is in `Selectable.DoStateTransition()`.
- Our `ApplyButtonDeep()` only needs to set `colors` on the `Button` component (which it does via the `Selectable.colors` property) — the rest is handled by the Selectable base class.

```csharp
// Type: UnityEngine.UI.Button

[AddComponentMenu("UI/Button", 30)]
public class Button : Selectable, IPointerClickHandler, IEventSystemHandler, ISubmitHandler
{
  [SerializeField] private ButtonClickedEvent m_OnClick = new ButtonClickedEvent();

  public ButtonClickedEvent onClick { get => m_OnClick; set => m_OnClick = value; }

  private void Press()
  {
    if (!IsActive() || !IsInteractable()) return;
    UISystemProfilerApi.AddMarker("Button.onClick", this);
    m_OnClick.Invoke();
  }

  /// <summary>
  /// Only fires for left mouse button. Our editor's right-click context menu
  /// doesn't accidentally trigger button clicks because of this guard.
  /// </summary>
  public virtual void OnPointerClick(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left) return;
    Press();
  }

  /// <summary>
  /// Keyboard/gamepad submit. Animates pressed state then transitions back.
  /// Uses colors.fadeDuration for the bounce-back timing.
  /// </summary>
  public virtual void OnSubmit(BaseEventData eventData)
  {
    Press();
    if (!IsActive() || !IsInteractable()) return;
    DoStateTransition(SelectionState.Pressed, false);
    StartCoroutine(OnFinishSubmit());
  }

  private IEnumerator OnFinishSubmit()
  {
    float fadeTime = colors.fadeDuration;
    float elapsed = 0f;
    while (elapsed < fadeTime)
    {
      elapsed += Time.unscaledDeltaTime;
      yield return null;
    }
    DoStateTransition(currentSelectionState, false);
  }

  [Serializable]
  public class ButtonClickedEvent : UnityEvent { }
}
```

---

## Slider

**Key insights for our UI Builder:**

- **`minValue`, `maxValue`, `wholeNumbers`** all call `Set(m_Value)` + `UpdateVisuals()` on change. Setting these via property setters is safe and triggers a visual refresh automatically. Our `ApplySliderDeep()` setting these directly is correct.
- **`value` setter** calls `Set(value)` which clamps, checks for change, updates visuals, and fires `onValueChanged`. `SetValueWithoutNotify()` does the same but skips the callback — use this in our inspector to avoid infinite loops.
- **`UpdateVisuals()`** drives fill rect anchors and handle rect anchors based on `normalizedValue`. It uses `DrivenRectTransformTracker` to mark these as driven. This is why the fill area and handle position update automatically when we change the value.
- **`fillRect`** and **`handleRect`** setters trigger `UpdateCachedReferences()` + `UpdateVisuals()`. These are structural changes — our builder should use full rebuild when changing these references.
- **Drag handling:** `OnDrag` ? `UpdateDrag()` ? calculates normalized position from pointer local position within the handle container rect ? sets `normalizedValue`. The `m_Offset` corrects for where the user grabbed the handle.
- **Direction** enum (LeftToRight, RightToLeft, BottomToTop, TopToBottom) controls axis and reverse. `reverseValue` inverts the normalized position for RTL/TTB.

```csharp
// Type: UnityEngine.UI.Slider

[AddComponentMenu("UI/Slider", 34)]
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class Slider : Selectable, IDragHandler, IEventSystemHandler, IInitializePotentialDragHandler, ICanvasElement
{
  [SerializeField] private RectTransform m_FillRect;
  [SerializeField] private RectTransform m_HandleRect;
  [SerializeField] private Direction m_Direction;
  [SerializeField] private float m_MinValue;
  [SerializeField] private float m_MaxValue = 1f;
  [SerializeField] private bool m_WholeNumbers;
  [SerializeField] protected float m_Value;
  [SerializeField] private SliderEvent m_OnValueChanged = new SliderEvent();
  private Image m_FillImage;
  private RectTransform m_FillContainerRect;
  private RectTransform m_HandleContainerRect;
  private Vector2 m_Offset = Vector2.zero;
  private DrivenRectTransformTracker m_Tracker;
  private bool m_DelayedUpdateVisuals;

  // --- Value properties (safe for ApplyDeep — auto-update visuals) ---

  public float minValue
  {
    get => m_MinValue;
    set { if (SetPropertyUtility.SetStruct(ref m_MinValue, value)) { Set(m_Value); UpdateVisuals(); } }
  }

  public float maxValue
  {
    get => m_MaxValue;
    set { if (SetPropertyUtility.SetStruct(ref m_MaxValue, value)) { Set(m_Value); UpdateVisuals(); } }
  }

  public bool wholeNumbers
  {
    get => m_WholeNumbers;
    set { if (SetPropertyUtility.SetStruct(ref m_WholeNumbers, value)) { Set(m_Value); UpdateVisuals(); } }
  }

  /// <summary>
  /// Calls Set() which clamps, updates visuals, and fires onValueChanged.
  /// Use SetValueWithoutNotify() from inspector code to avoid callback loops.
  /// </summary>
  public virtual float value
  {
    get => wholeNumbers ? Mathf.Round(m_Value) : m_Value;
    set => Set(value);
  }

  public virtual void SetValueWithoutNotify(float input) => Set(input, false);

  public float normalizedValue
  {
    get => Mathf.Approximately(minValue, maxValue) ? 0f
         : Mathf.InverseLerp(minValue, maxValue, value);
    set => this.value = Mathf.Lerp(minValue, maxValue, value);
  }

  // --- Core set + visual update ---

  private float ClampValue(float input)
  {
    float f = Mathf.Clamp(input, minValue, maxValue);
    if (wholeNumbers) f = Mathf.Round(f);
    return f;
  }

  protected virtual void Set(float input, bool sendCallback = true)
  {
    float num = ClampValue(input);
    if (m_Value == num) return;
    m_Value = num;
    UpdateVisuals();
    if (sendCallback) m_OnValueChanged.Invoke(num);
  }

  /// <summary>
  /// Drives fill rect anchors (for filled mode) and handle rect anchors.
  /// Uses DrivenRectTransformTracker so these are marked as driven/non-editable.
  /// </summary>
  private void UpdateVisuals()
  {
    m_Tracker.Clear();
    if (m_FillContainerRect != null)
    {
      m_Tracker.Add(this, m_FillRect, DrivenTransformProperties.Anchors);
      Vector2 min = Vector2.zero, max = Vector2.one;
      if (m_FillImage != null && m_FillImage.type == Image.Type.Filled)
        m_FillImage.fillAmount = normalizedValue;
      else if (reverseValue)
        min[(int)axis] = 1f - normalizedValue;
      else
        max[(int)axis] = normalizedValue;
      m_FillRect.anchorMin = min;
      m_FillRect.anchorMax = max;
    }
    if (m_HandleContainerRect != null)
    {
      m_Tracker.Add(this, m_HandleRect, DrivenTransformProperties.Anchors);
      Vector2 min = Vector2.zero, max = Vector2.one;
      min[(int)axis] = max[(int)axis] = reverseValue ? 1f - normalizedValue : normalizedValue;
      m_HandleRect.anchorMin = min;
      m_HandleRect.anchorMax = max;
    }
  }

  // --- Drag handling ---

  private void UpdateDrag(PointerEventData eventData, Camera cam)
  {
    RectTransform containerRect = m_HandleContainerRect ?? m_FillContainerRect;
    if (containerRect == null || containerRect.rect.size[(int)axis] <= 0f) return;
    Vector2 localPoint;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
      containerRect, eventData.position, cam, out localPoint)) return;
    float val = (localPoint - containerRect.rect.position - m_Offset)[(int)axis]
                / containerRect.rect.size[(int)axis];
    normalizedValue = reverseValue ? 1f - Mathf.Clamp01(val) : Mathf.Clamp01(val);
  }

  public virtual void OnDrag(PointerEventData eventData)
  {
    if (!IsActive() || !IsInteractable() || eventData.button != PointerEventData.InputButton.Left) return;
    UpdateDrag(eventData, eventData.pressEventCamera);
  }

  // --- Enums ---

  private Axis axis => (m_Direction == Direction.LeftToRight || m_Direction == Direction.RightToLeft)
    ? Axis.Horizontal : Axis.Vertical;
  private bool reverseValue => m_Direction == Direction.RightToLeft || m_Direction == Direction.TopToBottom;

  public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
  [Serializable] public class SliderEvent : UnityEvent<float> { }
  private enum Axis { Horizontal, Vertical }
}
```

---

## CanvasScaler

**Key insights for our UI Builder:**

- **`ScaleWithScreenSize` mode** (our mode) — `HandleScaleWithScreenSize()` runs every frame via `Canvas.preWillRenderCanvases`. It reads `m_Canvas.renderingDisplaySize` for the actual screen size.
- **`MatchWidthOrHeight` formula:** `scaleFactor = Pow(2, Lerp(Log2(screenW/refW), Log2(screenH/refH), matchWidthOrHeight))`. At `matchWidthOrHeight = 0.5f`, it's the geometric mean of width and height ratios.
- **`SetScaleFactor()`** has a **dead zone:** won't update if `|new - old| < 0.000005f`. So very tiny changes don't trigger canvas rescaling.
- **`referenceResolution` setter** clamps X and Y to never be exactly zero (uses `1E-05f` minimum). Our code setting `CanvasSize.ToVector2()` as the reference resolution is safe as long as canvas size is non-zero.
- **Canvas.preWillRenderCanvases** — the scaler hooks this event to run `Handle()` every frame. This means if we change `referenceResolution` or `matchWidthOrHeight` at runtime, it takes effect on the next render frame automatically.
- **OnDisable** resets to scaleFactor=1 and referencePixelsPerUnit=100. So destroying a canvas with a scaler doesn't leave stale scale values.

```csharp
// Type: UnityEngine.UI.CanvasScaler

[RequireComponent(typeof(Canvas))]
[ExecuteAlways]
[AddComponentMenu("Layout/Canvas Scaler", 101)]
[DisallowMultipleComponent]
public class CanvasScaler : UIBehaviour
{
  [SerializeField] private ScaleMode m_UiScaleMode;
  [SerializeField] protected float m_ReferencePixelsPerUnit = 100f;
  [SerializeField] protected float m_ScaleFactor = 1f;
  [SerializeField] protected Vector2 m_ReferenceResolution = new Vector2(800f, 600f);
  [SerializeField] protected ScreenMatchMode m_ScreenMatchMode;
  [Range(0f, 1f)]
  [SerializeField] protected float m_MatchWidthOrHeight;
  [SerializeField] protected float m_DynamicPixelsPerUnit = 1f;
  private Canvas m_Canvas;
  private float m_PrevScaleFactor = 1f;
  private float m_PrevReferencePixelsPerUnit = 100f;

  // All properties are simple setters — no side effects, no rebuild triggers.
  public ScaleMode uiScaleMode { get => m_UiScaleMode; set => m_UiScaleMode = value; }
  public float referencePixelsPerUnit { get => m_ReferencePixelsPerUnit; set => m_ReferencePixelsPerUnit = value; }
  public float scaleFactor { get => m_ScaleFactor; set => m_ScaleFactor = Mathf.Max(0.01f, value); }
  public float matchWidthOrHeight { get => m_MatchWidthOrHeight; set => m_MatchWidthOrHeight = value; }

  public Vector2 referenceResolution
  {
    get => m_ReferenceResolution;
    set
    {
      m_ReferenceResolution = value;
      // Clamp to never be exactly zero
      if (Mathf.Abs(m_ReferenceResolution.x) < 1E-05f)
        m_ReferenceResolution.x = 1E-05f * Mathf.Sign(m_ReferenceResolution.x);
      if (Mathf.Abs(m_ReferenceResolution.y) < 1E-05f)
        m_ReferenceResolution.y = 1E-05f * Mathf.Sign(m_ReferenceResolution.y);
    }
  }

  protected override void OnEnable()
  {
    base.OnEnable();
    m_Canvas = GetComponent<Canvas>();
    Handle();
    Canvas.preWillRenderCanvases += Canvas_preWillRenderCanvases;
  }

  private void Canvas_preWillRenderCanvases() => Handle();

  protected override void OnDisable()
  {
    SetScaleFactor(1f);
    SetReferencePixelsPerUnit(100f);
    Canvas.preWillRenderCanvases -= Canvas_preWillRenderCanvases;
    base.OnDisable();
  }

  protected virtual void Handle()
  {
    if (m_Canvas == null || !m_Canvas.isRootCanvas) return;
    if (m_Canvas.renderMode == RenderMode.WorldSpace) { HandleWorldCanvas(); return; }
    switch (m_UiScaleMode)
    {
      case ScaleMode.ConstantPixelSize: HandleConstantPixelSize(); break;
      case ScaleMode.ScaleWithScreenSize: HandleScaleWithScreenSize(); break;
      case ScaleMode.ConstantPhysicalSize: HandleConstantPhysicalSize(); break;
    }
  }

  /// <summary>
  /// THE key method for our UI Builder canvases.
  /// scaleFactor = Pow(2, Lerp(Log2(screenW/refW), Log2(screenH/refH), match))
  /// At match=0.5: geometric mean of width and height ratios.
  /// </summary>
  protected virtual void HandleScaleWithScreenSize()
  {
    Vector2 screenSize = m_Canvas.renderingDisplaySize;
    int targetDisplay = m_Canvas.targetDisplay;
    if (targetDisplay > 0 && targetDisplay < Display.displays.Length)
    {
      Display d = Display.displays[targetDisplay];
      screenSize = new Vector2(d.renderingWidth, d.renderingHeight);
    }
    float scaleFactor = 0f;
    switch (m_ScreenMatchMode)
    {
      case ScreenMatchMode.MatchWidthOrHeight:
        float logW = Mathf.Log(screenSize.x / m_ReferenceResolution.x, 2f);
        float logH = Mathf.Log(screenSize.y / m_ReferenceResolution.y, 2f);
        scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logW, logH, m_MatchWidthOrHeight));
        break;
      case ScreenMatchMode.Expand:
        scaleFactor = Mathf.Min(screenSize.x / m_ReferenceResolution.x,
                                screenSize.y / m_ReferenceResolution.y);
        break;
      case ScreenMatchMode.Shrink:
        scaleFactor = Mathf.Max(screenSize.x / m_ReferenceResolution.x,
                                screenSize.y / m_ReferenceResolution.y);
        break;
    }
    SetScaleFactor(scaleFactor);
    SetReferencePixelsPerUnit(m_ReferencePixelsPerUnit);
  }

  /// <summary>
  /// Dead zone: won't update canvas.scaleFactor if change < 0.000005.
  /// Prevents micro-jitter from triggering canvas rebuilds every frame.
  /// </summary>
  protected void SetScaleFactor(float scaleFactor)
  {
    if (Mathf.Abs(scaleFactor - m_PrevScaleFactor) < 5E-06f) return;
    m_Canvas.scaleFactor = scaleFactor;
    m_PrevScaleFactor = scaleFactor;
  }

  public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
  public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
}
```

---

## GraphicRaycaster

**Key insights for our UI Builder:**

- **`sortOrderPriority`** — For `ScreenSpaceOverlay` canvases, returns `canvas.sortingOrder`. This is how our 3-canvas architecture works: Panels (920) > Overlay (910) > Workspace (900). Higher sorting order = higher raycast priority.
- **`Raycast()` inner loop** — iterates `GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas)` and checks:
  1. `graphic.raycastTarget` must be true
  2. `graphic.canvasRenderer.cull` must be false (not culled by RectMask2D)
  3. `graphic.depth != -1` (not disabled)
  4. `RectTransformUtility.RectangleContainsScreenPoint()` with `raycastPadding`
  5. `graphic.Raycast()` — additional check (walks up checking Mask/RectMask2D)
- Results are **sorted by depth** (highest first = front-most), then appended to the result list.
- For `ScreenSpaceOverlay`, `eventCamera` returns `null` — positions are in screen pixels directly.
- **Blocking objects** — `ThreeD`/`TwoD`/`All` can block UI raycasts if a 3D/2D collider is in front. Not relevant for our overlay canvases but important for WorldSpace UIs.
- Our `IsMouseOverEditorUI()` raycasts against the panels raycaster — this works correctly because panels canvas (920) is always above workspace (900).

```csharp
// Type: UnityEngine.UI.GraphicRaycaster

[AddComponentMenu("Event/Graphic Raycaster")]
[RequireComponent(typeof(Canvas))]
public class GraphicRaycaster : BaseRaycaster
{
  [SerializeField] private bool m_IgnoreReversedGraphics = true;
  [SerializeField] private BlockingObjects m_BlockingObjects;
  [SerializeField] protected LayerMask m_BlockingMask = -1;
  private Canvas m_Canvas;
  private List<Graphic> m_RaycastResults = new List<Graphic>();
  private static readonly List<Graphic> s_SortedGraphics = new List<Graphic>();

  /// <summary>
  /// For ScreenSpaceOverlay: returns canvas.sortingOrder.
  /// This is why our 3-canvas arch works: Panels(920) > Overlay(910) > Workspace(900).
  /// EventSystem processes raycasters in sortOrderPriority order (highest first).
  /// </summary>
  public override int sortOrderPriority =>
    canvas.renderMode == RenderMode.ScreenSpaceOverlay
      ? canvas.sortingOrder : base.sortOrderPriority;

  /// <summary>
  /// For ScreenSpaceOverlay: returns null (screen-space positions, no camera needed).
  /// </summary>
  public override Camera eventCamera
  {
    get
    {
      if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
      if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null) return null;
      return canvas.worldCamera ?? Camera.main;
    }
  }

  public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
  {
    if (canvas == null) return;
    IList<Graphic> graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
    if (graphics == null || graphics.Count == 0) return;

    Camera cam = eventCamera;
    // ... viewport bounds check ...

    m_RaycastResults.Clear();
    Raycast(canvas, cam, eventData.position, graphics, m_RaycastResults);

    // Build results with depth, distance, sorting info
    for (int i = 0; i < m_RaycastResults.Count; i++)
    {
      // For ScreenSpaceOverlay: distance = 0 (no depth sorting by distance)
      // sortingLayer and sortingOrder come from the canvas
      resultAppendList.Add(new RaycastResult
      {
        gameObject = m_RaycastResults[i].gameObject,
        module = this,
        distance = 0f, // ScreenSpaceOverlay
        depth = m_RaycastResults[i].depth,
        sortingLayer = canvas.sortingLayerID,
        sortingOrder = canvas.sortingOrder
      });
    }
  }

  /// <summary>
  /// Core raycast: checks raycastTarget, cull state, depth, screen point containment.
  /// Results sorted by depth descending (front-most first).
  /// </summary>
  private static void Raycast(Canvas canvas, Camera cam, Vector2 pos,
    IList<Graphic> foundGraphics, List<Graphic> results)
  {
    for (int i = 0; i < foundGraphics.Count; i++)
    {
      Graphic g = foundGraphics[i];
      if (g.raycastTarget && !g.canvasRenderer.cull && g.depth != -1 &&
          RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pos, cam, g.raycastPadding) &&
          g.Raycast(pos, cam))
        s_SortedGraphics.Add(g);
    }
    s_SortedGraphics.Sort((g1, g2) => g2.depth.CompareTo(g1.depth));
    results.AddRange(s_SortedGraphics);
    s_SortedGraphics.Clear();
  }

  public enum BlockingObjects { None, TwoD, ThreeD, All }
}
```

---

## Mask

**Key insights for our UI Builder:**

- **Stencil-based** — uses the GPU stencil buffer to clip children. More expensive than `RectMask2D` but supports non-rectangular shapes (any `Graphic` shape).
- **`MaskEnabled()`** requires both `IsActive()` AND a non-null `graphic`. A Mask without a Graphic component does nothing.
- **`GetModifiedMaterial()`** wraps the base material with stencil operations. `StencilOp.Replace` writes the mask value; children compare against it. Depth is limited to 8 nested masks.
- **`showMaskGraphic`** controls whether the mask graphic itself is visible or just used as a stencil template. `ColorWriteMask.All` vs `0`.
- **`OnEnable`** sets `canvasRenderer.hasPopInstruction = true` and marks graphic dirty. `OnDisable` cleans up stencil materials and resets pop instruction.
- **`IsRaycastLocationValid()`** — raycasts only hit within the mask's `RectTransform` bounds. Children outside the mask rect don't receive clicks.
- Our builder uses `HasMask` on `UIElementNode` — when toggled, we add/remove the `Mask` component + ensure an `Image` component exists for the graphic. Full rebuild needed.

```csharp
// Type: UnityEngine.UI.Mask

[AddComponentMenu("UI/Mask", 13)]
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class Mask : UIBehaviour, ICanvasRaycastFilter, IMaterialModifier
{
  [NonSerialized] private RectTransform m_RectTransform;
  [SerializeField] private bool m_ShowMaskGraphic = true;
  [NonSerialized] private Graphic m_Graphic;
  [NonSerialized] private Material m_MaskMaterial;
  [NonSerialized] private Material m_UnmaskMaterial;

  public RectTransform rectTransform => m_RectTransform ?? (m_RectTransform = GetComponent<RectTransform>());
  public Graphic graphic => m_Graphic ?? (m_Graphic = GetComponent<Graphic>());

  public bool showMaskGraphic
  {
    get => m_ShowMaskGraphic;
    set { if (m_ShowMaskGraphic == value) return; m_ShowMaskGraphic = value; graphic?.SetMaterialDirty(); }
  }

  public virtual bool MaskEnabled() => IsActive() && graphic != null;

  protected override void OnEnable()
  {
    base.OnEnable();
    if (graphic != null)
    {
      graphic.canvasRenderer.hasPopInstruction = true;
      graphic.SetMaterialDirty();
      if (graphic is MaskableGraphic mg) mg.isMaskingGraphic = true;
    }
    MaskUtilities.NotifyStencilStateChanged(this);
  }

  protected override void OnDisable()
  {
    base.OnDisable();
    if (graphic != null)
    {
      graphic.SetMaterialDirty();
      graphic.canvasRenderer.hasPopInstruction = false;
      graphic.canvasRenderer.popMaterialCount = 0;
      if (graphic is MaskableGraphic mg) mg.isMaskingGraphic = false;
    }
    StencilMaterial.Remove(m_MaskMaterial); m_MaskMaterial = null;
    StencilMaterial.Remove(m_UnmaskMaterial); m_UnmaskMaterial = null;
    MaskUtilities.NotifyStencilStateChanged(this);
  }

  /// <summary>
  /// Raycasts only hit within the mask's RectTransform.
  /// Children visually outside the mask also don't receive clicks.
  /// </summary>
  public virtual bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
  {
    return !isActiveAndEnabled ||
           RectTransformUtility.RectangleContainsScreenPoint(rectTransform, sp, eventCamera);
  }

  /// <summary>
  /// Wraps material with stencil operations. Max depth = 8 nested masks.
  /// showMaskGraphic controls ColorWriteMask (visible vs invisible mask).
  /// </summary>
  public virtual Material GetModifiedMaterial(Material baseMaterial)
  {
    if (!MaskEnabled()) return baseMaterial;
    int depth = MaskUtilities.GetStencilDepth(transform,
      MaskUtilities.FindRootSortOverrideCanvas(transform));
    if (depth >= 8) { Debug.LogWarning("Stencil mask depth > 8"); return baseMaterial; }
    int bit = 1 << depth;
    // ... stencil material creation with StencilOp.Replace ...
    return m_MaskMaterial;
  }
}
```

---

## RectMask2D

**Key insights for our UI Builder:**

- **Rect-based clipping** — cheaper than `Mask` (no stencil buffer), but only clips to a rectangle. Good for scroll views, panels, etc.
- **`padding`** allows expanding/shrinking the clip rect beyond the RectTransform bounds. Useful for anti-aliasing edges.
- **`softness`** enables soft edges on the clip boundary (feathered edges). Values are clamped to `>= 0`.
- **`PerformClipping()`** is called by `ClipperRegistry` each frame. It:
  1. Finds all ancestor `RectMask2D` components to compute the intersection rect
  2. Calls `SetClipRect()` on all registered `MaskableGraphic` targets
  3. Calls `Cull()` to hide completely-outside graphics
- **`AddClippable`/`RemoveClippable`** — clippable children register/unregister themselves. `MaskableGraphic` (Image, Text, etc.) auto-registers with the nearest parent `RectMask2D`.
- **`IsRaycastLocationValid()`** respects `padding` — raycasts can be valid even slightly outside the RectTransform if padding extends the rect.
- **`OnTransformParentChanged()`** nulls the canvas cache and recalculates clip rects. This is why reparenting inside a RectMask2D auto-updates clipping.
- Our builder uses `HasRectMask2D` on `UIElementNode` — when toggled, add/remove `RectMask2D` component. Full rebuild needed for structural change.

```csharp
// Type: UnityEngine.UI.RectMask2D

[AddComponentMenu("UI/Rect Mask 2D", 14)]
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class RectMask2D : UIBehaviour, IClipper, ICanvasRaycastFilter
{
  private readonly RectangularVertexClipper m_VertexClipper = new RectangularVertexClipper();
  private RectTransform m_RectTransform;
  private HashSet<MaskableGraphic> m_MaskableTargets = new HashSet<MaskableGraphic>();
  private HashSet<IClippable> m_ClipTargets = new HashSet<IClippable>();
  private bool m_ShouldRecalculateClipRects;
  private List<RectMask2D> m_Clippers = new List<RectMask2D>();
  private Rect m_LastClipRectCanvasSpace;
  private bool m_ForceClip;

  [SerializeField] private Vector4 m_Padding;
  [SerializeField] private Vector2Int m_Softness;
  private Canvas m_Canvas;

  public Vector4 padding
  {
    get => m_Padding;
    set { m_Padding = value; MaskUtilities.Notify2DMaskStateChanged(this); }
  }

  public Vector2Int softness
  {
    get => m_Softness;
    set
    {
      m_Softness.x = Mathf.Max(0, value.x);
      m_Softness.y = Mathf.Max(0, value.y);
      MaskUtilities.Notify2DMaskStateChanged(this);
    }
  }

  public RectTransform rectTransform =>
    m_RectTransform ?? (m_RectTransform = GetComponent<RectTransform>());

  protected override void OnEnable()
  {
    base.OnEnable();
    m_ShouldRecalculateClipRects = true;
    ClipperRegistry.Register(this);
    MaskUtilities.Notify2DMaskStateChanged(this);
  }

  protected override void OnDisable()
  {
    base.OnDisable();
    m_ClipTargets.Clear();
    m_MaskableTargets.Clear();
    m_Clippers.Clear();
    ClipperRegistry.Disable(this);
    MaskUtilities.Notify2DMaskStateChanged(this);
  }

  public virtual bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
  {
    return !isActiveAndEnabled ||
           RectTransformUtility.RectangleContainsScreenPoint(rectTransform, sp, eventCamera, m_Padding);
  }

  /// <summary>
  /// Called each frame by ClipperRegistry. Computes intersection of all ancestor
  /// RectMask2D rects, then applies clip rect to all registered MaskableGraphics.
  /// </summary>
  public virtual void PerformClipping()
  {
    if (Canvas == null) return;
    if (m_ShouldRecalculateClipRects)
    {
      MaskUtilities.GetRectMasksForClip(this, m_Clippers);
      m_ShouldRecalculateClipRects = false;
    }
    bool validRect = true;
    Rect clipRect = Clipping.FindCullAndClipWorldRect(m_Clippers, out validRect);
    // Apply clip rect to all targets, cull fully-outside graphics
    foreach (MaskableGraphic mg in m_MaskableTargets)
    {
      mg.SetClipRect(clipRect, validRect);
      mg.Cull(clipRect, validRect);
    }
    m_LastClipRectCanvasSpace = clipRect;
    UpdateClipSoftness();
  }

  public void AddClippable(IClippable clippable)
  {
    if (clippable == null) return;
    m_ShouldRecalculateClipRects = true;
    if (clippable is MaskableGraphic mg) m_MaskableTargets.Add(mg);
    else m_ClipTargets.Add(clippable);
    m_ForceClip = true;
  }

  public void RemoveClippable(IClippable clippable)
  {
    if (clippable == null) return;
    m_ShouldRecalculateClipRects = true;
    clippable.SetClipRect(new Rect(), false);
    if (clippable is MaskableGraphic mg) m_MaskableTargets.Remove(mg);
    else m_ClipTargets.Remove(clippable);
    m_ForceClip = true;
  }

  protected override void OnTransformParentChanged()
  {
    m_Canvas = null; // Force re-resolve
    base.OnTransformParentChanged();
    m_ShouldRecalculateClipRects = true;
  }
}
```

---

## Actionable Takeaways for UI Builder

### 1. CanvasGroup.interactable must stay true for opacity-only CanvasGroups
`Selectable.ParentGroupAllowsInteraction()` walks up checking `CanvasGroup.interactable`. If we add a `CanvasGroup` for opacity control (node style opacity < 1), we must ensure `cg.interactable = true` and `cg.blocksRaycasts = true` (or whatever the default is). Otherwise ALL child selectables become non-interactive.

### 2. Button.colors setter is no-op if unchanged
`SetPropertyUtility.SetStruct` compares the full `ColorBlock`. Our `ApplyButtonDeep()` can safely set `btn.colors = newColors` every refresh — it only triggers `OnSetProperty()` if something actually changed.

### 3. Slider properties auto-update visuals
`minValue`, `maxValue`, `wholeNumbers`, `value` all call `UpdateVisuals()` internally. Our `ApplySliderDeep()` setting these via the property setters is correct — no manual visual refresh needed.

### 4. GraphicRaycaster.sortOrderPriority enables our 3-canvas architecture
For `ScreenSpaceOverlay`, the sort order comes from `canvas.sortingOrder`. Panels (920) > Overlay (910) > Workspace (900). EventSystem processes higher priority raycasters first, which is why panel clicks always take precedence.

### 5. Mask vs RectMask2D trade-offs
- **Mask**: Stencil-based, supports any shape, max 8 nested, more expensive
- **RectMask2D**: Rect-only, cheaper, supports padding/softness, no nesting limit (they intersect)
- Both block raycasts outside their bounds via `IsRaycastLocationValid()`
- Both require full rebuild when added/removed (structural change)

### 6. CanvasScaler runs every frame
`HandleScaleWithScreenSize()` runs via `Canvas.preWillRenderCanvases` every frame, but `SetScaleFactor()` has a dead zone (< 5E-06) so it only actually updates `canvas.scaleFactor` when there's a meaningful change. Our editor resolution change detection (`_lastScreenWidth != Screen.width`) is complementary but not strictly necessary — the scaler handles it automatically.
