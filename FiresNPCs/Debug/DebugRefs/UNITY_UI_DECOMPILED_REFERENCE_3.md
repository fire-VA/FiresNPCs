# Unity UI Decompiled Reference — Part 3: Layout Components, Image, Toggle & Dropdown

Decompiled from `UnityEngine.UI.dll` (Version=1.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\UnityEngine.UI.dll`

Part 3 covers the layout component hierarchy (HorizontalOrVerticalLayoutGroup ? HLG/VLG,
GridLayoutGroup, ContentSizeFitter), the Image component (mesh generation, slicing, tiling),
Toggle, and the legacy Dropdown — all critical for understanding how our UI Builder captures,
serializes, and renders these components.

**See also:**
- `UNITY_UI_DECOMPILED_REFERENCE.md` — Part 1 (LayoutRebuilder, LayoutGroup, ScrollRect)
- `UNITY_UI_DECOMPILED_REFERENCE_2.md` — Part 2 (Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D)

---

## Table of Contents

1. [HorizontalOrVerticalLayoutGroup](#horizontalorverticallayoutgroup) — Shared VLG/HLG logic
2. [HorizontalLayoutGroup](#horizontallayoutgroup) — Thin wrapper (isVertical=false)
3. [GridLayoutGroup](#gridlayoutgroup) — Cell-based grid layout
4. [ContentSizeFitter](#contentsizefitter) — Auto-size to content
5. [Image](#image) — Sprite rendering, slicing, tiling, fill modes
6. [Toggle](#toggle) — Checkmark alpha animation
7. [Dropdown (Legacy)](#dropdown-legacy) — Template-based dropdown list

---

## HorizontalOrVerticalLayoutGroup

**Key insights for our UI Builder:**

- **All properties use `SetProperty<T>()`** (inherited from `LayoutGroup`) which calls `SetDirty()` ? `MarkLayoutForRebuild()` on change. Our `ApplyLayoutGroupDeep()` setting these via property setters automatically triggers a layout rebuild — no manual `MarkLayoutForRebuild()` needed.
- **`childControlWidth`/`childControlHeight`** — when `true`, the layout group controls the size of children based on their `ILayoutElement` values (`LayoutElement.preferredWidth`, etc.). When `false`, children keep their own `sizeDelta` but are still positioned by the group. Our data model's `ChildControlWidth`/`ChildControlHeight` maps directly.
- **`childForceExpandWidth`/`childForceExpandHeight`** — when `true`, forces `flexible` to be at least `1f` for all children, making them expand to fill available space. Our `ChildForceExpandWidth`/`ChildForceExpandHeight` maps directly.
- **`childScaleWidth`/`childScaleHeight`** — accounts for `localScale` when calculating child sizes. Rarely used in typical UIs but important for scaled elements.
- **`reverseArrangement`** — reverses child iteration order. We don't currently expose this in the inspector or data model, but it's captured if present.
- **`CalcAlongAxis(axis, isVertical)`** — the actual layout calculation:
  - When axis matches the layout direction (`isVertical ^ axis == 1` is false): children are stacked, sizes are summed with spacing
  - When axis is perpendicular (cross axis): takes the maximum child size
  - This dual-axis calculation is why VLG and HLG share one implementation — they just flip `isVertical`
- **`SetChildrenAlongAxis(axis, isVertical)`** — the actual positioning:
  - Along the main axis: positions children sequentially with spacing, distributes surplus via flexible sizes
  - On the cross axis: each child is sized/positioned independently with alignment
  - Uses `GetStartOffset()` to apply `childAlignment` for the cross axis
- **`GetChildSizes()`** — when `controlSize` is `false`, uses `child.sizeDelta[axis]` directly instead of `LayoutUtility.GetMinSize/GetPreferredSize/GetFlexibleSize`. This is the fast path for non-controlled children.

```csharp
// Type: UnityEngine.UI.HorizontalOrVerticalLayoutGroup

[ExecuteAlways]
public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
{
  [SerializeField] protected float m_Spacing;
  [SerializeField] protected bool m_ChildForceExpandWidth = true;
  [SerializeField] protected bool m_ChildForceExpandHeight = true;
  [SerializeField] protected bool m_ChildControlWidth = true;
  [SerializeField] protected bool m_ChildControlHeight = true;
  [SerializeField] protected bool m_ChildScaleWidth;
  [SerializeField] protected bool m_ChildScaleHeight;
  [SerializeField] protected bool m_ReverseArrangement;

  // All setters use SetProperty<T> ? SetDirty() ? MarkLayoutForRebuild()
  public float spacing { get => m_Spacing; set => SetProperty(ref m_Spacing, value); }
  public bool childForceExpandWidth { get => m_ChildForceExpandWidth; set => SetProperty(ref m_ChildForceExpandWidth, value); }
  public bool childForceExpandHeight { get => m_ChildForceExpandHeight; set => SetProperty(ref m_ChildForceExpandHeight, value); }
  public bool childControlWidth { get => m_ChildControlWidth; set => SetProperty(ref m_ChildControlWidth, value); }
  public bool childControlHeight { get => m_ChildControlHeight; set => SetProperty(ref m_ChildControlHeight, value); }
  public bool childScaleWidth { get => m_ChildScaleWidth; set => SetProperty(ref m_ChildScaleWidth, value); }
  public bool childScaleHeight { get => m_ChildScaleHeight; set => SetProperty(ref m_ChildScaleHeight, value); }
  public bool reverseArrangement { get => m_ReverseArrangement; set => SetProperty(ref m_ReverseArrangement, value); }

  /// <summary>
  /// Core layout calculation. Called twice per layout pass (once for each axis).
  /// Along main axis: sum children sizes + spacing.
  /// On cross axis: take max child size.
  /// isVertical=true for VLG, false for HLG.
  /// </summary>
  protected void CalcAlongAxis(int axis, bool isVertical)
  {
    float padding = axis == 0 ? (float)this.padding.horizontal : (float)this.padding.vertical;
    bool controlSize = axis == 0 ? m_ChildControlWidth : m_ChildControlHeight;
    bool useScale = axis == 0 ? m_ChildScaleWidth : m_ChildScaleHeight;
    bool forceExpand = axis == 0 ? m_ChildForceExpandWidth : m_ChildForceExpandHeight;

    float totalMin = padding, totalPreferred = padding, totalFlexible = 0f;
    bool isCrossAxis = isVertical ^ axis == 1; // true when this axis is perpendicular to layout dir

    for (int i = 0; i < rectChildren.Count; i++)
    {
      RectTransform child = rectChildren[i];
      float min, preferred, flexible;
      GetChildSizes(child, axis, controlSize, forceExpand, out min, out preferred, out flexible);

      if (useScale)
      {
        float scale = child.localScale[axis];
        min *= scale; preferred *= scale; flexible *= scale;
      }

      if (isCrossAxis)
      {
        // Cross axis: take maximum
        totalMin = Mathf.Max(min + padding, totalMin);
        totalPreferred = Mathf.Max(preferred + padding, totalPreferred);
        totalFlexible = Mathf.Max(flexible, totalFlexible);
      }
      else
      {
        // Main axis: sum with spacing
        totalMin += min + spacing;
        totalPreferred += preferred + spacing;
        totalFlexible += flexible;
      }
    }
    if (!isCrossAxis && rectChildren.Count > 0)
    {
      totalMin -= spacing;
      totalPreferred -= spacing;
    }
    SetLayoutInputForAxis(totalMin, Mathf.Max(totalMin, totalPreferred), totalFlexible, axis);
  }

  /// <summary>
  /// Core child positioning. Handles both main and cross axis.
  /// Main axis: sequential positioning with flexible size distribution.
  /// Cross axis: each child sized independently with alignment offset.
  /// </summary>
  protected void SetChildrenAlongAxis(int axis, bool isVertical)
  {
    float rectSize = rectTransform.rect.size[axis];
    bool controlSize = axis == 0 ? m_ChildControlWidth : m_ChildControlHeight;
    bool useScale = axis == 0 ? m_ChildScaleWidth : m_ChildScaleHeight;
    bool forceExpand = axis == 0 ? m_ChildForceExpandWidth : m_ChildForceExpandHeight;
    float alignment = GetAlignmentOnAxis(axis);

    bool isCrossAxis = isVertical ^ axis == 1;

    if (isCrossAxis)
    {
      // Cross axis: each child positioned independently
      float innerSize = rectSize - (axis == 0 ? padding.horizontal : padding.vertical);
      for (int i = 0; i < rectChildren.Count; i++)
      {
        RectTransform child = rectChildren[i];
        float min, preferred, flexible;
        GetChildSizes(child, axis, controlSize, forceExpand, out min, out preferred, out flexible);
        float scale = useScale ? child.localScale[axis] : 1f;
        float size = Mathf.Clamp(innerSize, min, flexible > 0f ? rectSize : preferred);
        float offset = GetStartOffset(axis, size * scale);
        if (controlSize)
          SetChildAlongAxisWithScale(child, axis, offset, size, scale);
        else
          SetChildAlongAxisWithScale(child, axis, offset + (size - child.sizeDelta[axis]) * alignment, scale);
      }
    }
    else
    {
      // Main axis: sequential stacking with flexible distribution
      float pos = axis == 0 ? padding.left : padding.top;
      float surplusPerFlexible = 0f;
      float surplus = rectSize - GetTotalPreferredSize(axis);
      if (surplus > 0f)
      {
        if (GetTotalFlexibleSize(axis) == 0f)
          pos = GetStartOffset(axis, GetTotalPreferredSize(axis) - (axis == 0 ? padding.horizontal : padding.vertical));
        else if (GetTotalFlexibleSize(axis) > 0f)
          surplusPerFlexible = surplus / GetTotalFlexibleSize(axis);
      }
      // Lerp between min and preferred based on available space
      float minToPreferredT = 0f;
      if (GetTotalMinSize(axis) != GetTotalPreferredSize(axis))
        minToPreferredT = Mathf.Clamp01((rectSize - GetTotalMinSize(axis)) /
                                         (GetTotalPreferredSize(axis) - GetTotalMinSize(axis)));

      for (int i = 0; i < rectChildren.Count; i++)
      {
        RectTransform child = rectChildren[i];
        float min, preferred, flexible;
        GetChildSizes(child, axis, controlSize, forceExpand, out min, out preferred, out flexible);
        float scale = useScale ? child.localScale[axis] : 1f;
        float size = Mathf.Lerp(min, preferred, minToPreferredT) + flexible * surplusPerFlexible;

        if (controlSize)
          SetChildAlongAxisWithScale(child, axis, pos, size, scale);
        else
          SetChildAlongAxisWithScale(child, axis, pos + (size - child.sizeDelta[axis]) * alignment, scale);

        pos += size * scale + spacing;
      }
    }
  }

  /// <summary>
  /// When controlSize=false, uses child.sizeDelta directly (fast path).
  /// When controlSize=true, queries LayoutUtility for min/preferred/flexible.
  /// childForceExpand ensures flexible is at least 1.
  /// </summary>
  private void GetChildSizes(RectTransform child, int axis, bool controlSize,
    bool childForceExpand, out float min, out float preferred, out float flexible)
  {
    if (!controlSize)
    {
      min = child.sizeDelta[axis];
      preferred = min;
      flexible = 0f;
    }
    else
    {
      min = LayoutUtility.GetMinSize(child, axis);
      preferred = LayoutUtility.GetPreferredSize(child, axis);
      flexible = LayoutUtility.GetFlexibleSize(child, axis);
    }
    if (childForceExpand) flexible = Mathf.Max(flexible, 1f);
  }
}
```

---

## HorizontalLayoutGroup

**Key insight:** Literally just calls the base class methods with `isVertical=false`. The entire HLG is 4 one-liner overrides. VLG (not shown) is identical but with `isVertical=true`. This is why switching between VLG and HLG in our builder requires destroying one component and creating the other — they're separate classes that pass different `isVertical` flags.

```csharp
// Type: UnityEngine.UI.HorizontalLayoutGroup

[AddComponentMenu("Layout/Horizontal Layout Group", 150)]
public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
  public override void CalculateLayoutInputHorizontal()
  {
    base.CalculateLayoutInputHorizontal(); // rebuilds rectChildren list
    CalcAlongAxis(0, false);               // axis=0 (horizontal), isVertical=false
  }
  public override void CalculateLayoutInputVertical() => CalcAlongAxis(1, false);
  public override void SetLayoutHorizontal() => SetChildrenAlongAxis(0, false);
  public override void SetLayoutVertical() => SetChildrenAlongAxis(1, false);
}

// VerticalLayoutGroup (not shown) is identical but passes isVertical=true:
// CalcAlongAxis(0, true), CalcAlongAxis(1, true), SetChildrenAlongAxis(0, true), SetChildrenAlongAxis(1, true)
```

---

## GridLayoutGroup

**Key insights for our UI Builder:**

- **Extends `LayoutGroup` directly** (not `HorizontalOrVerticalLayoutGroup`) — completely different layout algorithm.
- **All properties use `SetProperty<T>()`** ? `SetDirty()` ? `MarkLayoutForRebuild()`. Our `ApplyGridLayoutGroupDeep()` can safely set these via property setters.
- **`cellSize`** — fixed size for ALL cells. Unlike VLG/HLG, grid children do NOT use their own preferred size. Every child gets `cellSize` assigned via `rect.sizeDelta = cellSize` in the horizontal pass.
- **`constraint` + `constraintCount`** — determines grid dimensions:
  - `Flexible`: auto-calculates columns from available width (floor of `(width + spacing) / (cellSize + spacing)`)
  - `FixedColumnCount`: exact column count, rows calculated from child count
  - `FixedRowCount`: exact row count, columns calculated from child count
  - Our `UIGridLayoutGroupDef.Constraint`/`Count` maps directly
- **`startCorner`** — UpperLeft/UpperRight/LowerLeft/LowerRight. Controls which corner children start from. `num1` and `num2` decode this into X/Y flip flags.
- **`startAxis`** — Horizontal/Vertical. Controls whether children fill rows-first or columns-first.
- **`SetCellsAlongAxis(0)`** (horizontal pass) — forces ALL children to `anchorMin = Vector2.up`, `anchorMax = Vector2.up`, `sizeDelta = cellSize`. This is the same top-left anchor forcing as LayoutGroup.
- **`SetCellsAlongAxis(1)`** (vertical pass) — calculates actual positions using `GetStartOffset()` + cell index × (cellSize + spacing).
- **Captured InventoryGrid ? GridLayoutGroup:** Our parser synthesizes a `UIGridLayoutGroupDef` from Valheim's code-computed grid. The cellSize comes from the slot prefab's RectTransform, spacing from `m_elementSpace`, and constraint from `m_width`.

```csharp
// Type: UnityEngine.UI.GridLayoutGroup

[AddComponentMenu("Layout/Grid Layout Group", 152)]
public class GridLayoutGroup : LayoutGroup
{
  [SerializeField] protected Corner m_StartCorner;
  [SerializeField] protected Axis m_StartAxis;
  [SerializeField] protected Vector2 m_CellSize = new Vector2(100f, 100f);
  [SerializeField] protected Vector2 m_Spacing = Vector2.zero;
  [SerializeField] protected Constraint m_Constraint;
  [SerializeField] protected int m_ConstraintCount = 2;

  // All setters ? SetProperty ? SetDirty ? MarkLayoutForRebuild
  public Corner startCorner { get => m_StartCorner; set => SetProperty(ref m_StartCorner, value); }
  public Axis startAxis { get => m_StartAxis; set => SetProperty(ref m_StartAxis, value); }
  public Vector2 cellSize { get => m_CellSize; set => SetProperty(ref m_CellSize, value); }
  public Vector2 spacing { get => m_Spacing; set => SetProperty(ref m_Spacing, value); }
  public Constraint constraint { get => m_Constraint; set => SetProperty(ref m_Constraint, value); }
  public int constraintCount { get => m_ConstraintCount; set => SetProperty(ref m_ConstraintCount, Mathf.Max(1, value)); }

  /// <summary>
  /// Horizontal input: calculates min/preferred width based on constraint mode.
  /// FixedColumnCount: uses constraintCount columns.
  /// FixedRowCount: calculates columns from childCount / rows.
  /// Flexible: min=1 column, preferred=sqrt(childCount) columns.
  /// </summary>
  public override void CalculateLayoutInputHorizontal()
  {
    base.CalculateLayoutInputHorizontal(); // rebuild rectChildren
    int minColumns, preferredColumns;
    if (m_Constraint == Constraint.FixedColumnCount)
      minColumns = preferredColumns = m_ConstraintCount;
    else if (m_Constraint == Constraint.FixedRowCount)
      minColumns = preferredColumns = Mathf.CeilToInt((float)rectChildren.Count / m_ConstraintCount);
    else
    {
      minColumns = 1;
      preferredColumns = Mathf.CeilToInt(Mathf.Sqrt(rectChildren.Count));
    }
    SetLayoutInputForAxis(
      padding.horizontal + (cellSize.x + spacing.x) * minColumns - spacing.x,
      padding.horizontal + (cellSize.x + spacing.x) * preferredColumns - spacing.x,
      -1f, 0);
  }

  /// <summary>
  /// Actual cell positioning. Called twice (axis=0 then axis=1).
  /// Axis 0: forces all children to top-left anchors + cellSize.
  /// Axis 1: calculates grid positions using corner/axis/constraint config.
  /// </summary>
  private void SetCellsAlongAxis(int axis)
  {
    if (axis == 0)
    {
      // Horizontal pass: force anchors and size on ALL children
      for (int i = 0; i < rectChildren.Count; i++)
      {
        RectTransform child = rectChildren[i];
        m_Tracker.Add(this, child,
          DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition |
          DrivenTransformProperties.SizeDelta);
        child.anchorMin = Vector2.up;   // (0, 1) — top-left
        child.anchorMax = Vector2.up;
        child.sizeDelta = cellSize;
      }
      return;
    }

    // Vertical pass: calculate actual positions
    // ... determines cellsPerMainAxis, actualCellCountX, actualCellCountY
    // based on constraint mode, startAxis, available width ...

    // Position each child:
    // col/row determined by: index % cellsPerMainAxis, index / cellsPerMainAxis
    // Flipped by startCorner (UpperRight flips X, LowerLeft flips Y, etc.)
    // Final position: startOffset + col * (cellSize + spacing), row * (cellSize + spacing)
    for (int i = 0; i < rectChildren.Count; i++)
    {
      // ... col/row calculation with corner flips ...
      SetChildAlongAxis(rectChildren[i], 0, startX + (cellSize.x + spacing.x) * col, cellSize.x);
      SetChildAlongAxis(rectChildren[i], 1, startY + (cellSize.y + spacing.y) * row, cellSize.y);
    }
  }

  public override void SetLayoutHorizontal() => SetCellsAlongAxis(0);
  public override void SetLayoutVertical() => SetCellsAlongAxis(1);

  public enum Corner { UpperLeft, UpperRight, LowerLeft, LowerRight }
  public enum Axis { Horizontal, Vertical }
  public enum Constraint { Flexible, FixedColumnCount, FixedRowCount }
}
```

---

## ContentSizeFitter

**Key insights for our UI Builder:**

- **Implements `ILayoutSelfController`** — runs BEFORE `ILayoutController` (LayoutGroup) in the layout pass. This means ContentSizeFitter resizes the element before its parent LayoutGroup positions it. This is the correct order for "auto-size text container" patterns.
- **`horizontalFit`/`verticalFit`** use `SetPropertyUtility.SetStruct` + `SetDirty()`. Safe for `ApplyContentFitterDeep()`.
- **`HandleSelfFittingAlongAxis()`** — the core logic:
  - `Unconstrained`: does nothing (element keeps manual size)
  - `MinSize`: sets `sizeDelta` to `LayoutUtility.GetMinSize()` — uses the minimum size reported by child `ILayoutElement` components
  - `PreferredSize`: sets `sizeDelta` to `LayoutUtility.GetPreferredSize()` — uses the preferred size (e.g., TMP_Text's calculated text bounds)
- Uses `SetSizeWithCurrentAnchors()` instead of direct `sizeDelta` assignment — this correctly handles stretched anchors.
- **`DrivenRectTransformTracker`** marks the controlled axis as driven, preventing manual edits in the Unity inspector (not relevant for our builder but explains the behavior).
- **`OnRectTransformDimensionsChange()`** calls `SetDirty()` — if the parent resizes, the fitter recalculates. This can cause cascading rebuilds but is necessary for correct behavior.
- Common pattern in our builder: Text element with ContentSizeFitter (PreferredSize) ? text container auto-sizes to fit the text content.

```csharp
// Type: UnityEngine.UI.ContentSizeFitter

[AddComponentMenu("Layout/Content Size Fitter", 141)]
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class ContentSizeFitter : UIBehaviour, ILayoutSelfController, ILayoutController
{
  [SerializeField] protected FitMode m_HorizontalFit;
  [SerializeField] protected FitMode m_VerticalFit;
  [NonSerialized] private RectTransform m_Rect;
  private DrivenRectTransformTracker m_Tracker;

  public FitMode horizontalFit
  {
    get => m_HorizontalFit;
    set { if (SetPropertyUtility.SetStruct(ref m_HorizontalFit, value)) SetDirty(); }
  }

  public FitMode verticalFit
  {
    get => m_VerticalFit;
    set { if (SetPropertyUtility.SetStruct(ref m_VerticalFit, value)) SetDirty(); }
  }

  protected override void OnEnable() { base.OnEnable(); SetDirty(); }

  protected override void OnDisable()
  {
    m_Tracker.Clear();
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
    base.OnDisable();
  }

  protected override void OnRectTransformDimensionsChange() => SetDirty();

  /// <summary>
  /// Core sizing logic. For each axis:
  /// - Unconstrained: no-op
  /// - MinSize: resize to LayoutUtility.GetMinSize (minimum reported by ILayoutElement children)
  /// - PreferredSize: resize to LayoutUtility.GetPreferredSize (e.g., TMP text bounds)
  /// Uses SetSizeWithCurrentAnchors for correct behavior with stretched anchors.
  /// </summary>
  private void HandleSelfFittingAlongAxis(int axis)
  {
    FitMode fitMode = axis == 0 ? horizontalFit : verticalFit;
    if (fitMode == FitMode.Unconstrained)
    {
      m_Tracker.Add(this, rectTransform, DrivenTransformProperties.None);
      return;
    }
    m_Tracker.Add(this, rectTransform,
      axis == 0 ? DrivenTransformProperties.SizeDeltaX : DrivenTransformProperties.SizeDeltaY);

    if (fitMode == FitMode.MinSize)
      rectTransform.SetSizeWithCurrentAnchors((RectTransform.Axis)axis,
        LayoutUtility.GetMinSize(m_Rect, axis));
    else // PreferredSize
      rectTransform.SetSizeWithCurrentAnchors((RectTransform.Axis)axis,
        LayoutUtility.GetPreferredSize(m_Rect, axis));
  }

  /// <summary>
  /// ILayoutSelfController: runs BEFORE LayoutGroup in the layout pass.
  /// Horizontal first, then vertical (called separately by LayoutRebuilder).
  /// </summary>
  public virtual void SetLayoutHorizontal()
  {
    m_Tracker.Clear();
    HandleSelfFittingAlongAxis(0);
  }
  public virtual void SetLayoutVertical() => HandleSelfFittingAlongAxis(1);

  protected void SetDirty()
  {
    if (!IsActive()) return;
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
  }

  public enum FitMode { Unconstrained, MinSize, PreferredSize }
}
```

---

## Image

**Key insights for our UI Builder:**

- **`sprite` setter** — complex! Compares old vs new sprite, skips layout update if size unchanged (`m_SkipLayoutUpdate`), skips material update if texture unchanged (`m_SkipMaterialUpdate`). Then calls `SetAllDirty()` + `TrackSprite()`. Our builder should use full rebuild when changing sprites (not ApplyDeep).
- **`type` setter** — uses `SetPropertyUtility.SetStruct`, calls `SetVerticesDirty()` only (not layout dirty). Switching between Simple/Sliced/Tiled/Filled only regenerates mesh vertices, not layout. Our `ApplyImageDeep()` could potentially handle this in-place, but we currently use full rebuild.
- **`preserveAspect`, `fillCenter`, `fillAmount`, `fillClockwise`, `useSpriteMesh`** — all use `SetPropertyUtility.SetStruct` ? `SetVerticesDirty()`. Safe for in-place property updates.
- **`pixelsPerUnitMultiplier`** — clamped to `>= 0.01`. Calls `SetVerticesDirty()`. Affects how Sliced/Tiled images scale their borders. Our `UIImageDef.PixelsPerUnit` maps to this.
- **`color`** (inherited from `Graphic`) — used directly in `OnPopulateMesh()` as vertex color. Changing color triggers mesh regeneration. Our `ApplyImageDeep()` sets `img.color` which calls `SetVerticesDirty()`.
- **Image.Type rendering modes:**
  - `Simple`: 4 vertices, 2 triangles. Fastest. Optional `useSpriteMesh` for tight mesh.
  - `Sliced`: 9-slice using sprite border. `fillCenter` controls whether the center patch renders. `GetAdjustedBorders()` scales borders proportionally if the image is smaller than the border sum.
  - `Tiled`: Repeats the inner region. Has a 65,000 vertex safety limit — reduces tile count if exceeded. Performance-sensitive for large areas.
  - `Filled`: Horizontal, Vertical, Radial90/180/360 fill modes. `fillAmount` (0–1) controls how much is visible. Uses `RadialCut()` for radial modes.
- **`IsRaycastLocationValid()`** — when `alphaHitTestMinimumThreshold > 0`, reads the sprite texture pixel at the click point and only returns true if alpha >= threshold. Requires readable texture (not crunched). We don't use this in the builder but captured UIs might have it.
- **Layout interface:** `preferredWidth`/`preferredHeight` return sprite size / pixelsPerUnit. This is how ContentSizeFitter + Image auto-sizes to the sprite's native resolution.
- **`overrideSprite`** — used by Selectable (Button) for sprite-swap transitions. `activeSprite` returns `overrideSprite ?? sprite`. Our builder doesn't need to worry about this — it's handled by the Button's state machine.

```csharp
// Type: UnityEngine.UI.Image (key members only — full source is ~800 lines)

[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/Image", 11)]
public class Image : MaskableGraphic, ISerializationCallbackReceiver, ILayoutElement, ICanvasRaycastFilter
{
  [SerializeField] private Sprite m_Sprite;
  [NonSerialized] private Sprite m_OverrideSprite;
  [SerializeField] private Type m_Type;
  [SerializeField] private bool m_PreserveAspect;
  [SerializeField] private bool m_FillCenter = true;
  [SerializeField] private FillMethod m_FillMethod = FillMethod.Radial360;
  [SerializeField] private float m_FillAmount = 1f;
  [SerializeField] private bool m_FillClockwise = true;
  [SerializeField] private int m_FillOrigin;
  [SerializeField] private bool m_UseSpriteMesh;
  [SerializeField] private float m_PixelsPerUnitMultiplier = 1f;
  private float m_AlphaHitTestMinimumThreshold;

  /// <summary>
  /// COMPLEX setter — compares sprite size and texture, skips unnecessary dirty calls.
  /// Calls SetAllDirty() + TrackSprite(). Use full rebuild in our builder when changing sprites.
  /// </summary>
  public Sprite sprite
  {
    get => m_Sprite;
    set
    {
      if (m_Sprite != null)
      {
        if (m_Sprite == value) return;
        m_SkipLayoutUpdate = m_Sprite.rect.size.Equals(value ? value.rect.size : Vector2.zero);
        m_SkipMaterialUpdate = m_Sprite.texture == (value ? value.texture : null);
      }
      else if (value == null) return;
      m_Sprite = value;
      SetAllDirty();
      TrackSprite();
    }
  }

  // Used by Selectable for sprite-swap transitions
  public Sprite overrideSprite { get => activeSprite; set { SetPropertyUtility.SetClass(ref m_OverrideSprite, value); SetAllDirty(); } }
  private Sprite activeSprite => m_OverrideSprite != null ? m_OverrideSprite : sprite;

  // These all use SetPropertyUtility ? SetVerticesDirty() (vertices only, not layout)
  public Type type { get => m_Type; set { if (SetPropertyUtility.SetStruct(ref m_Type, value)) SetVerticesDirty(); } }
  public bool preserveAspect { get => m_PreserveAspect; set { if (SetPropertyUtility.SetStruct(ref m_PreserveAspect, value)) SetVerticesDirty(); } }
  public bool fillCenter { get => m_FillCenter; set { if (SetPropertyUtility.SetStruct(ref m_FillCenter, value)) SetVerticesDirty(); } }
  public float fillAmount { get => m_FillAmount; set { if (SetPropertyUtility.SetStruct(ref m_FillAmount, Mathf.Clamp01(value))) SetVerticesDirty(); } }
  public float pixelsPerUnitMultiplier { get => m_PixelsPerUnitMultiplier; set { m_PixelsPerUnitMultiplier = Mathf.Max(0.01f, value); SetVerticesDirty(); } }

  // pixelsPerUnit: sprite.pixelsPerUnit / canvas.referencePixelsPerUnit
  // multipliedPixelsPerUnit: pixelsPerUnit * m_PixelsPerUnitMultiplier
  // Used by Sliced/Tiled for border scaling

  /// <summary>
  /// Routes to the correct mesh generation method based on Image.Type.
  /// </summary>
  protected override void OnPopulateMesh(VertexHelper toFill)
  {
    if (activeSprite == null) { base.OnPopulateMesh(toFill); return; }
    switch (type)
    {
      case Type.Simple: GenerateSimpleSprite(toFill, m_PreserveAspect); break;
      case Type.Sliced: GenerateSlicedSprite(toFill); break;
      case Type.Tiled:  GenerateTiledSprite(toFill); break;
      case Type.Filled: GenerateFilledSprite(toFill, m_PreserveAspect); break;
    }
  }

  // Layout interface: auto-size based on sprite dimensions
  public virtual float preferredWidth =>
    activeSprite == null ? 0f
    : (type == Type.Sliced || type == Type.Tiled)
      ? DataUtility.GetMinSize(activeSprite).x / pixelsPerUnit
      : activeSprite.rect.size.x / pixelsPerUnit;

  public virtual float preferredHeight =>
    activeSprite == null ? 0f
    : (type == Type.Sliced || type == Type.Tiled)
      ? DataUtility.GetMinSize(activeSprite).y / pixelsPerUnit
      : activeSprite.rect.size.y / pixelsPerUnit;

  public enum Type { Simple, Sliced, Tiled, Filled }
  public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
}
```

---

## Toggle

**Key insights for our UI Builder:**

- **`graphic` field** is the checkmark visual (assigned in editor/code, not auto-found). `PlayEffect()` cross-fades this graphic's alpha between 0 (off) and 1 (on) with a 0.1s duration. Our builder sets the checkmark Image reference during instantiation.
- **`isOn` property** calls `Set(value)` which:
  1. Checks for actual change (`m_IsOn == value`)
  2. If in a `ToggleGroup`, enforces group rules (at least one on if `!allowSwitchOff`)
  3. Calls `PlayEffect()` — animates the checkmark
  4. Fires `onValueChanged.Invoke(m_IsOn)`
- **`SetIsOnWithoutNotify()`** — same as `Set(value, false)` — skips callback. Use in our inspector to avoid callback loops.
- **`InternalToggle()`** — the actual click handler. Guarded by `IsActive() && IsInteractable()`. Just flips `isOn`.
- **`PlayEffect(instant)`** — `graphic.CrossFadeAlpha(isOn ? 1f : 0f, instant ? 0f : 0.1f, true)`. The 0.1f duration is hardcoded. Our `ApplyToggleDeep()` sets `toggle.isOn` which triggers this animation.
- **ToggleGroup** support — optional. When set, toggles coordinate to ensure mutual exclusivity. Our builder doesn't currently create toggle groups, but captured UIs might have them.
- **`OnPointerClick`** only fires for left mouse button (inherited pattern from Selectable).
- **Key difference from Slider:** Toggle doesn't have `UpdateVisuals()` — the visual state is entirely managed by the checkmark graphic alpha crossfade. No driven properties, no tracker.

```csharp
// Type: UnityEngine.UI.Toggle

[AddComponentMenu("UI/Toggle", 30)]
[RequireComponent(typeof(RectTransform))]
public class Toggle : Selectable, IPointerClickHandler, IEventSystemHandler, ISubmitHandler, ICanvasElement
{
  public ToggleTransition toggleTransition = ToggleTransition.Fade;
  public Graphic graphic; // The checkmark visual
  [SerializeField] private ToggleGroup m_Group;
  public ToggleEvent onValueChanged = new ToggleEvent();
  [SerializeField] private bool m_IsOn;

  public ToggleGroup group
  {
    get => m_Group;
    set { SetToggleGroup(value, true); PlayEffect(true); }
  }

  /// <summary>
  /// Calls Set() which checks for change, enforces group rules, animates checkmark.
  /// </summary>
  public bool isOn { get => m_IsOn; set => Set(value); }

  /// <summary>
  /// Same as Set(value, false) — skips onValueChanged callback.
  /// Use from inspector code to avoid infinite loops.
  /// </summary>
  public void SetIsOnWithoutNotify(bool value) => Set(value, false);

  private void Set(bool value, bool sendCallback = true)
  {
    if (m_IsOn == value) return;
    m_IsOn = value;

    // ToggleGroup enforcement
    if (m_Group != null && m_Group.isActiveAndEnabled && IsActive())
    {
      if (m_IsOn || (!m_Group.AnyTogglesOn() && !m_Group.allowSwitchOff))
      {
        m_IsOn = true;
        m_Group.NotifyToggleOn(this, sendCallback);
      }
    }

    PlayEffect(toggleTransition == ToggleTransition.None);
    if (sendCallback) onValueChanged.Invoke(m_IsOn);
  }

  /// <summary>
  /// Animates the checkmark graphic alpha. 0.1f fade duration is HARDCODED.
  /// instant=true on Start(), group change, and ToggleTransition.None.
  /// </summary>
  private void PlayEffect(bool instant)
  {
    if (graphic == null) return;
    graphic.CrossFadeAlpha(m_IsOn ? 1f : 0f, instant ? 0f : 0.1f, true);
  }

  protected override void Start() => PlayEffect(true); // Instant set on start

  private void InternalToggle()
  {
    if (!IsActive() || !IsInteractable()) return;
    isOn = !isOn;
  }

  public virtual void OnPointerClick(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left) return;
    InternalToggle();
  }

  public virtual void OnSubmit(BaseEventData eventData) => InternalToggle();

  public enum ToggleTransition { None, Fade }
  [Serializable] public class ToggleEvent : UnityEvent<bool> { }
}
```

---

## Dropdown (Legacy)

**Key insights for our UI Builder:**

- **This is `UnityEngine.UI.Dropdown`** (legacy Text-based), NOT `TMP_Dropdown`. Valheim uses both. Our builder mostly encounters `TMP_Dropdown` but the architecture is identical.
- **Template-based:** The dropdown list is created by instantiating `m_Template` (a child RectTransform that's normally inactive). Each item is instantiated from a Toggle child inside the template. This is why dropdown structure changes require full rebuild — the template defines the visual structure.
- **`value` setter** calls `Set()` which clamps, calls `RefreshShownValue()` (updates caption text/image), and fires `onValueChanged`. `SetValueWithoutNotify()` skips callback.
- **`options`** setter calls `RefreshShownValue()`. Our `ApplyDropdownDeep()` can safely set options and value.
- **`Show()`** — complex instantiation:
  1. Finds root canvas (walks up parent chain)
  2. Validates and sets up the template (adds Canvas with `sortingOrder = 30000`, GraphicRaycaster, CanvasGroup)
  3. Instantiates template ? creates dropdown list GO
  4. Creates a Toggle item for each option
  5. Sets up navigation between items
  6. Sizes the content container
  7. Flips the dropdown direction if it would go off-screen (`RectTransformUtility.FlipLayoutOnAxis`)
  8. Creates a full-screen transparent blocker (click-outside-to-close)
  9. Alpha-fades the list in
- **Blocker pattern:** Creates a full-screen transparent `Image` with a `Button` whose `onClick` calls `Hide()`. This is the "click outside to close" behavior. The blocker has `sortingOrder = dropdown.sortingOrder - 1` and `CanvasGroup.ignoreParentGroups = true`.
- **`Hide()`** — alpha-fades out, then destroys via `DelayedDestroyDropdownList()` coroutine after fade duration.
- **`OnSelectItem(toggle)`** — finds item index by checking `toggle.transform` position in parent's child list (`index - 1` to skip the template item). Sets `value` and calls `Hide()`.
- **Performance note:** `Show()` does a LOT of work — template validation, instantiation, navigation setup, bounds checking, blocker creation. This all happens in one frame. For captured UIs with dropdowns, we should not try to `ApplyDeep` option/template changes — always full rebuild.
- **Not relevant for our data model:** `captionText`, `captionImage`, `itemText`, `itemImage` are Unity `Text`/`Image` references internal to the template structure. Our `UIDropdownDef` stores options as strings and renders via TMP_Dropdown at instantiation time.

```csharp
// Type: UnityEngine.UI.Dropdown (key members — full source is ~500 lines)

[AddComponentMenu("UI/Legacy/Dropdown", 102)]
[RequireComponent(typeof(RectTransform))]
public class Dropdown : Selectable, IPointerClickHandler, IEventSystemHandler, ISubmitHandler, ICancelHandler
{
  [SerializeField] private RectTransform m_Template;
  [SerializeField] private Text m_CaptionText;
  [SerializeField] private Image m_CaptionImage;
  [SerializeField] private Text m_ItemText;
  [SerializeField] private Image m_ItemImage;
  [SerializeField] private int m_Value;
  [SerializeField] private OptionDataList m_Options = new OptionDataList();
  [SerializeField] private DropdownEvent m_OnValueChanged = new DropdownEvent();
  [SerializeField] private float m_AlphaFadeSpeed = 0.15f;
  private GameObject m_Dropdown;
  private GameObject m_Blocker;
  private List<DropdownItem> m_Items = new List<DropdownItem>();

  public List<OptionData> options
  {
    get => m_Options.options;
    set { m_Options.options = value; RefreshShownValue(); }
  }

  public int value
  {
    get => m_Value;
    set => Set(value);
  }

  public void SetValueWithoutNotify(int input) => Set(input, false);

  private void Set(int value, bool sendCallback = true)
  {
    if (Application.isPlaying && (value == m_Value || options.Count == 0)) return;
    m_Value = Mathf.Clamp(value, 0, options.Count - 1);
    RefreshShownValue();
    if (sendCallback) m_OnValueChanged.Invoke(m_Value);
  }

  /// <summary>
  /// Updates caption text/image from current selected option.
  /// </summary>
  public void RefreshShownValue()
  {
    OptionData data = options.Count > 0
      ? options[Mathf.Clamp(m_Value, 0, options.Count - 1)]
      : s_NoOptionData;
    if (m_CaptionText) m_CaptionText.text = data?.text ?? "";
    if (m_CaptionImage) { m_CaptionImage.sprite = data?.image; m_CaptionImage.enabled = m_CaptionImage.sprite != null; }
  }

  /// <summary>
  /// Instantiates dropdown list from template. Creates blocker. Sets up item toggles.
  /// Heavy operation — avoid calling from ApplyDeep. Use full rebuild for option/template changes.
  /// </summary>
  public void Show()
  {
    if (!IsActive() || !IsInteractable() || m_Dropdown != null) return;
    // 1. Find root canvas
    // 2. Setup/validate template (adds Canvas sortingOrder=30000, GraphicRaycaster, CanvasGroup)
    // 3. Instantiate template ? m_Dropdown
    // 4. Create Toggle items for each option, wire up navigation
    // 5. Size content container
    // 6. Flip direction if off-screen
    // 7. Alpha fade in
    // 8. Create full-screen blocker (click-outside-to-close)
  }

  /// <summary>
  /// Full-screen transparent Image + Button(onClick=Hide) + CanvasGroup(ignoreParentGroups=true).
  /// sortingOrder = dropdown.sortingOrder - 1.
  /// </summary>
  protected virtual GameObject CreateBlocker(Canvas rootCanvas) { /* ... */ }

  public void Hide()
  {
    if (m_Dropdown != null)
    {
      AlphaFadeList(m_AlphaFadeSpeed, 0f);
      StartCoroutine(DelayedDestroyDropdownList(m_AlphaFadeSpeed));
    }
    if (m_Blocker != null) DestroyBlocker(m_Blocker);
    m_Blocker = null;
    Select(); // Re-select the dropdown itself
  }

  /// <summary>
  /// Finds item index by child position in parent (index - 1 to skip template).
  /// </summary>
  private void OnSelectItem(Toggle toggle)
  {
    if (!toggle.isOn) toggle.isOn = true;
    int index = -1;
    Transform parent = toggle.transform.parent;
    for (int i = 0; i < parent.childCount; i++)
      if (parent.GetChild(i) == toggle.transform) { index = i - 1; break; }
    if (index < 0) return;
    value = index;
    Hide();
  }

  // Click/Submit ? Show()
  public virtual void OnPointerClick(PointerEventData eventData) => Show();
  public virtual void OnSubmit(BaseEventData eventData) => Show();
  public virtual void OnCancel(BaseEventData eventData) => Hide();

  [Serializable] public class OptionData { public string text; public Sprite image; }
  [Serializable] public class OptionDataList { public List<OptionData> options = new List<OptionData>(); }
  [Serializable] public class DropdownEvent : UnityEvent<int> { }
}
```

---

## Actionable Takeaways for UI Builder

### 1. HLG/VLG switch requires component destroy+create
`HorizontalLayoutGroup` and `VerticalLayoutGroup` are separate classes that just pass different `isVertical` flags. Cannot switch in-place — must destroy one and create the other. This is why direction change is a full rebuild in our inspector.

### 2. All layout group property setters auto-trigger rebuild
`spacing`, `padding`, `childAlignment`, `childControlWidth/Height`, `childForceExpandWidth/Height` all use `SetProperty<T>()` ? `SetDirty()` ? `MarkLayoutForRebuild()`. Our `ApplyLayoutGroupDeep()` setting these via property setters is correct — no manual rebuild trigger needed.

### 3. GridLayoutGroup forces cellSize on ALL children
`SetCellsAlongAxis(0)` sets `sizeDelta = cellSize` on every child. Children in a grid layout cannot have individual sizes. Our captured InventoryGrid correctly synthesizes a GridLayoutGroup with cellSize from the slot prefab.

### 4. ContentSizeFitter runs before LayoutGroup
`ILayoutSelfController` priority means ContentSizeFitter resizes the element BEFORE the parent LayoutGroup positions it. This is the correct order for auto-sizing text containers.

### 5. Image sprite changes need full rebuild
The `sprite` setter has complex optimization logic (`m_SkipLayoutUpdate`, `m_SkipMaterialUpdate`). Our sprite resolution pipeline (`ApplySprite()`) does asset lookup ? assignment, which should always go through full rebuild to be safe.

### 6. Image property changes (non-sprite) are vertex-only
`type`, `preserveAspect`, `fillCenter`, `fillAmount`, `pixelsPerUnitMultiplier` all only call `SetVerticesDirty()`. These could theoretically be moved to `ApplyDeep` if we verify our overlay handles the visual update correctly.

### 7. Toggle checkmark animation is hardcoded 0.1s
`PlayEffect()` uses `CrossFadeAlpha` with a hardcoded `0.1f` duration. We can't change this without subclassing Toggle. Not an issue for our builder — just a behavioral note.

### 8. Dropdown Show() is heavy — always full rebuild for option changes
Template instantiation, item creation, navigation wiring, bounds checking, blocker creation — all in one frame. Option/template changes in our inspector should always use full rebuild.

### 9. Dropdown blocker pattern is useful reference
The full-screen transparent `Image` + `Button(onClick=Hide)` + `CanvasGroup(ignoreParentGroups=true)` pattern is how Unity implements "click outside to close". Our context menus could use a similar pattern if needed.
