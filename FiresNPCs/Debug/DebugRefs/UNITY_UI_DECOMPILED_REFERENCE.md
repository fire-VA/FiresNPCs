# Unity UI Decompiled Reference — Part 1: Layout & Scrolling

Decompiled from `UnityEngine.UI.dll` (Version=1.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\UnityEngine.UI.dll`

This file exists as a quick-reference for the UI Builder implementation.
Understanding how these Unity internals work lets us make smarter decisions
about when to call `MarkLayoutForRebuild` vs `ForceRebuildLayoutImmediate`,
when property changes are safe to do in-place, and how the layout pipeline
actually processes our elements.

### Reference File Index

| File | Contents | Status |
|------|----------|--------|
| `UNITY_UI_DECOMPILED_REFERENCE.md` | LayoutRebuilder, LayoutGroup, ScrollRect | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_2.md` | Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_3.md` | HorizontalOrVerticalLayoutGroup, HLG/VLG, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown (Legacy) | ? Complete |
| `TMPRO_DECOMPILED_REFERENCE.md` | TMP_Text, TMP_InputField, TMP_Dropdown — text rendering, input handling, TMP dropdown vs legacy | ?? Pending |
| `VALHEIM_UI_REFERENCE.md` | InventoryGui, InventoryGrid, UITooltip, UIGroupHandler — Valheim-specific UI components our parser captures | ?? Pending |
| `VALHEIM_COMPONENT_REFERENCE.md` | ZRoutedRpc (for UILayoutSyncRPC), ZNet/ZDO patterns, Player/Game UI hooks our integration uses | ?? Pending |

---

## Table of Contents

1. [LayoutRebuilder](#layoutrebuilder) — Layout rebuild queue & execution
2. [LayoutGroup (abstract)](#layoutgroup) — Base class for VLG/HLG
3. [ScrollRect](#scrollrect) — Scroll view internals

---

## LayoutRebuilder

**Key insights for our UI Builder:**

- `MarkLayoutForRebuild(rect)` walks **up** the parent chain to find the highest ancestor with an active `ILayoutGroup`, then registers that root for rebuild via `CanvasUpdateRegistry.TryRegisterCanvasElementForLayoutRebuild()`. This means calling it on a deep child is fine — it bubbles up automatically.
- Multiple calls to `MarkLayoutForRebuild()` in the same frame are **deduplicated** by `CanvasUpdateRegistry` (it checks hash). So calling it after every property change in `ApplyNodeDeep()` is cheap — only one actual rebuild happens per frame.
- `ForceRebuildLayoutImmediate(rect)` creates a temporary `LayoutRebuilder`, runs the full layout pass **synchronously** (horizontal calc ? horizontal control ? vertical calc ? vertical control), then releases back to the pool. Use this when you need bounds to be correct **immediately** (e.g., before reading `RectTransform` bounds for selection overlay positioning).
- The rebuild order is: `CalculateLayoutInputHorizontal` (bottom-up), `SetLayoutHorizontal` (top-down, `ILayoutSelfController` first), then same for vertical. `ILayoutSelfController` (ContentSizeFitter) runs before `ILayoutController` (LayoutGroup) within each axis.
- `LayoutComplete()` returns the rebuilder to the pool — no manual cleanup needed.
- `RectTransform.reapplyDrivenProperties` static event triggers `MarkLayoutForRebuild` — this is how driven properties (from LayoutGroups) auto-trigger rebuilds.

```csharp
// Decompiled with JetBrains decompiler
// Type: UnityEngine.UI.LayoutRebuilder
// Assembly: UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null

using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.Pool;

namespace UnityEngine.UI;

public class LayoutRebuilder : ICanvasElement
{
  private RectTransform m_ToRebuild;
  private int m_CachedHashFromTransform;
  private static ObjectPool<LayoutRebuilder> s_Rebuilders = new ObjectPool<LayoutRebuilder>(
    () => new LayoutRebuilder(),
    actionOnRelease: x => x.Clear());

  private void Initialize(RectTransform controller)
  {
    this.m_ToRebuild = controller;
    this.m_CachedHashFromTransform = controller.GetHashCode();
  }

  private void Clear()
  {
    this.m_ToRebuild = null;
    this.m_CachedHashFromTransform = 0;
  }

  static LayoutRebuilder()
  {
    RectTransform.reapplyDrivenProperties += ReapplyDrivenProperties;
  }

  private static void ReapplyDrivenProperties(RectTransform driven)
  {
    MarkLayoutForRebuild(driven);
  }

  public Transform transform => (Transform) this.m_ToRebuild;
  public bool IsDestroyed() => this.m_ToRebuild == null;

  private static void StripDisabledBehavioursFromList(List<Component> components)
  {
    components.RemoveAll(e => e is Behaviour && !((Behaviour) e).isActiveAndEnabled);
  }

  /// <summary>
  /// SYNCHRONOUS immediate layout rebuild. Creates a temp rebuilder, runs full
  /// horizontal+vertical layout pass, then releases. Use when you need correct
  /// bounds IMMEDIATELY (e.g., before reading RectTransform world corners).
  /// </summary>
  public static void ForceRebuildLayoutImmediate(RectTransform layoutRoot)
  {
    LayoutRebuilder element = s_Rebuilders.Get();
    element.Initialize(layoutRoot);
    element.Rebuild(CanvasUpdate.Layout);
    s_Rebuilders.Release(element);
  }

  /// <summary>
  /// Called by CanvasUpdateRegistry during the layout phase.
  /// Order: HorizontalCalc ? HorizontalControl ? VerticalCalc ? VerticalControl
  /// </summary>
  public void Rebuild(CanvasUpdate executing)
  {
    if (executing != CanvasUpdate.Layout) return;
    this.PerformLayoutCalculation(this.m_ToRebuild,
      e => (e as ILayoutElement).CalculateLayoutInputHorizontal());
    this.PerformLayoutControl(this.m_ToRebuild,
      e => (e as ILayoutController).SetLayoutHorizontal());
    this.PerformLayoutCalculation(this.m_ToRebuild,
      e => (e as ILayoutElement).CalculateLayoutInputVertical());
    this.PerformLayoutControl(this.m_ToRebuild,
      e => (e as ILayoutController).SetLayoutVertical());
  }

  /// <summary>
  /// Top-down: Applies layout control. ILayoutSelfController runs FIRST (ContentSizeFitter),
  /// then ILayoutController (LayoutGroup). ScrollRect content is skipped to avoid recursion.
  /// Then recurses into children.
  /// </summary>
  private void PerformLayoutControl(RectTransform rect, UnityAction<Component> action)
  {
    if (rect == null) return;
    List<Component> componentList = CollectionPool<List<Component>, Component>.Get();
    rect.GetComponents(typeof(ILayoutController), componentList);
    StripDisabledBehavioursFromList(componentList);
    if (componentList.Count > 0)
    {
      // ILayoutSelfController first (ContentSizeFitter)
      for (int i = 0; i < componentList.Count; i++)
        if (componentList[i] is ILayoutSelfController)
          action(componentList[i]);
      // Then ILayoutController (LayoutGroup) — skip ScrollRect's own content
      for (int i = 0; i < componentList.Count; i++)
        if (!(componentList[i] is ILayoutSelfController))
        {
          if (componentList[i] is ScrollRect sr)
          {
            if (sr.content != rect) action(componentList[i]);
          }
          else action(componentList[i]);
        }
      // Recurse children
      for (int i = 0; i < rect.childCount; i++)
        this.PerformLayoutControl(rect.GetChild(i) as RectTransform, action);
    }
    CollectionPool<List<Component>, Component>.Release(componentList);
  }

  /// <summary>
  /// Bottom-up: Calculates layout input. Recurses into children FIRST, then runs
  /// CalculateLayoutInput on this node. Only processes nodes with ILayoutElement or ILayoutGroup.
  /// </summary>
  private void PerformLayoutCalculation(RectTransform rect, UnityAction<Component> action)
  {
    if (rect == null) return;
    List<Component> componentList = CollectionPool<List<Component>, Component>.Get();
    rect.GetComponents(typeof(ILayoutElement), componentList);
    StripDisabledBehavioursFromList(componentList);
    if (componentList.Count > 0 || rect.TryGetComponent(typeof(ILayoutGroup), out _))
    {
      // Children first (bottom-up)
      for (int i = 0; i < rect.childCount; i++)
        this.PerformLayoutCalculation(rect.GetChild(i) as RectTransform, action);
      // Then this node
      for (int i = 0; i < componentList.Count; i++)
        action(componentList[i]);
    }
    CollectionPool<List<Component>, Component>.Release(componentList);
  }

  /// <summary>
  /// DEFERRED layout mark. Walks UP the parent chain to find the highest ancestor
  /// with an active ILayoutGroup, then registers that root for rebuild.
  /// Multiple calls per frame are deduplicated by CanvasUpdateRegistry (hash check).
  /// This is the method our ApplyNodeDeep() calls — it's cheap to call repeatedly.
  /// </summary>
  public static void MarkLayoutForRebuild(RectTransform rect)
  {
    if (rect == null || rect.gameObject == null) return;
    List<Component> componentList = CollectionPool<List<Component>, Component>.Get();
    bool flag = true;
    RectTransform layoutRoot = rect;
    // Walk up to find highest layout group ancestor
    for (RectTransform parent = layoutRoot.parent as RectTransform;
         flag && parent != null && parent.gameObject != null;
         parent = parent.parent as RectTransform)
    {
      flag = false;
      parent.GetComponents(typeof(ILayoutGroup), componentList);
      for (int i = 0; i < componentList.Count; i++)
      {
        if (componentList[i] != null && componentList[i] is Behaviour b && b.isActiveAndEnabled)
        {
          flag = true;
          layoutRoot = parent;
          break;
        }
      }
    }
    if (layoutRoot == rect && !ValidController(layoutRoot, componentList))
    {
      CollectionPool<List<Component>, Component>.Release(componentList);
      return; // No layout controller found — nothing to rebuild
    }
    MarkLayoutRootForRebuild(layoutRoot);
    CollectionPool<List<Component>, Component>.Release(componentList);
  }

  private static bool ValidController(RectTransform layoutRoot, List<Component> comps)
  {
    if (layoutRoot == null || layoutRoot.gameObject == null) return false;
    layoutRoot.GetComponents(typeof(ILayoutController), comps);
    for (int i = 0; i < comps.Count; i++)
      if (comps[i] != null && comps[i] is Behaviour b && b.isActiveAndEnabled)
        return true;
    return false;
  }

  private static void MarkLayoutRootForRebuild(RectTransform controller)
  {
    if (controller == null) return;
    LayoutRebuilder element = s_Rebuilders.Get();
    element.Initialize(controller);
    if (!CanvasUpdateRegistry.TryRegisterCanvasElementForLayoutRebuild(element))
      s_Rebuilders.Release(element); // Already registered — dedup
  }

  public void LayoutComplete() => s_Rebuilders.Release(this);
  public void GraphicUpdateComplete() { }

  public override int GetHashCode() => this.m_CachedHashFromTransform;
  public override bool Equals(object obj) => obj.GetHashCode() == this.GetHashCode();
  public override string ToString() => "(Layout Rebuilder for) " + this.m_ToRebuild?.ToString();
}
```

---

## LayoutGroup

**Key insights for our UI Builder:**

- `CalculateLayoutInputHorizontal()` rebuilds `m_RectChildren` list each time — it iterates all children and filters out inactive ones and those with `ILayoutIgnorer.ignoreLayout = true`. This is why `LayoutElement.ignoreLayout` works.
- `SetProperty()` calls `SetDirty()` which calls `MarkLayoutForRebuild()`. So setting `padding`, `childAlignment`, `spacing`, etc. via the properties automatically triggers a rebuild. Our `ApplyLayoutGroupDeep()` sets fields directly on the existing component, which also triggers `SetDirty()` through the property setters.
- `SetChildAlongAxisWithScale()` forces children to `anchorMin = Vector2.up` and `anchorMax = Vector2.up` (top-left anchor). This is why layout group children always get their anchors reset — it's by design. Our captured layouts should expect this.
- `OnTransformChildrenChanged()` calls `SetDirty()` — adding/removing children automatically triggers layout recalculation.
- `DelayedSetDirty()` is a coroutine fallback used when `SetDirty()` is called during a layout rebuild (`CanvasUpdateRegistry.IsRebuildingLayout()` returns true). It waits one frame then marks dirty. This prevents infinite rebuild loops.

```csharp
// Decompiled with JetBrains decompiler
// Type: UnityEngine.UI.LayoutGroup
// Assembly: UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.Pool;

namespace UnityEngine.UI;

[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public abstract class LayoutGroup : UIBehaviour, ILayoutElement, ILayoutGroup, ILayoutController
{
  [SerializeField] protected RectOffset m_Padding = new RectOffset();
  [SerializeField] protected TextAnchor m_ChildAlignment;
  [NonSerialized] private RectTransform m_Rect;
  protected DrivenRectTransformTracker m_Tracker;
  private Vector2 m_TotalMinSize = Vector2.zero;
  private Vector2 m_TotalPreferredSize = Vector2.zero;
  private Vector2 m_TotalFlexibleSize = Vector2.zero;
  [NonSerialized] private List<RectTransform> m_RectChildren = new List<RectTransform>();

  public RectOffset padding
  {
    get => this.m_Padding;
    set => this.SetProperty(ref this.m_Padding, value);
  }

  public TextAnchor childAlignment
  {
    get => this.m_ChildAlignment;
    set => this.SetProperty(ref this.m_ChildAlignment, value);
  }

  protected RectTransform rectTransform
  {
    get
    {
      if (this.m_Rect == null) this.m_Rect = this.GetComponent<RectTransform>();
      return this.m_Rect;
    }
  }

  protected List<RectTransform> rectChildren => this.m_RectChildren;

  /// <summary>
  /// Rebuilds the list of active, non-ignored children.
  /// Called at the START of every layout calculation pass.
  /// Children with ILayoutIgnorer.ignoreLayout=true are excluded.
  /// </summary>
  public virtual void CalculateLayoutInputHorizontal()
  {
    this.m_RectChildren.Clear();
    List<Component> componentList = CollectionPool<List<Component>, Component>.Get();
    for (int i = 0; i < this.rectTransform.childCount; i++)
    {
      RectTransform child = this.rectTransform.GetChild(i) as RectTransform;
      if (child == null || !child.gameObject.activeInHierarchy) continue;
      child.GetComponents(typeof(ILayoutIgnorer), componentList);
      if (componentList.Count == 0)
      {
        this.m_RectChildren.Add(child);
      }
      else
      {
        for (int j = 0; j < componentList.Count; j++)
        {
          if (!((ILayoutIgnorer) componentList[j]).ignoreLayout)
          {
            this.m_RectChildren.Add(child);
            break;
          }
        }
      }
    }
    CollectionPool<List<Component>, Component>.Release(componentList);
    this.m_Tracker.Clear();
  }

  public abstract void CalculateLayoutInputVertical();
  public abstract void SetLayoutHorizontal();
  public abstract void SetLayoutVertical();

  // Layout element interface
  public virtual float minWidth => this.GetTotalMinSize(0);
  public virtual float preferredWidth => this.GetTotalPreferredSize(0);
  public virtual float flexibleWidth => this.GetTotalFlexibleSize(0);
  public virtual float minHeight => this.GetTotalMinSize(1);
  public virtual float preferredHeight => this.GetTotalPreferredSize(1);
  public virtual float flexibleHeight => this.GetTotalFlexibleSize(1);
  public virtual int layoutPriority => 0;

  protected LayoutGroup()
  {
    if (this.m_Padding == null) this.m_Padding = new RectOffset();
  }

  protected override void OnEnable()
  {
    base.OnEnable();
    this.rectTransform.sendChildDimensionsChange = true;
    this.SetDirty();
  }

  protected override void OnDisable()
  {
    this.m_Tracker.Clear();
    LayoutRebuilder.MarkLayoutForRebuild(this.rectTransform);
    this.rectTransform.sendChildDimensionsChange = false;
    base.OnDisable();
  }

  protected override void OnDidApplyAnimationProperties() => this.SetDirty();

  protected float GetTotalMinSize(int axis) => this.m_TotalMinSize[axis];
  protected float GetTotalPreferredSize(int axis) => this.m_TotalPreferredSize[axis];
  protected float GetTotalFlexibleSize(int axis) => this.m_TotalFlexibleSize[axis];

  protected float GetStartOffset(int axis, float requiredSpaceWithoutPadding)
  {
    float totalSpace = requiredSpaceWithoutPadding +
      (axis == 0 ? this.padding.horizontal : this.padding.vertical);
    float surplus = this.rectTransform.rect.size[axis] - totalSpace;
    float alignment = this.GetAlignmentOnAxis(axis);
    return (axis == 0 ? this.padding.left : this.padding.top) + surplus * alignment;
  }

  protected float GetAlignmentOnAxis(int axis)
  {
    return axis == 0
      ? ((int) this.childAlignment % 3) * 0.5f
      : ((int) this.childAlignment / 3) * 0.5f;
  }

  protected void SetLayoutInputForAxis(float totalMin, float totalPreferred, float totalFlexible, int axis)
  {
    this.m_TotalMinSize[axis] = totalMin;
    this.m_TotalPreferredSize[axis] = totalPreferred;
    this.m_TotalFlexibleSize[axis] = totalFlexible;
  }

  /// <summary>
  /// IMPORTANT: Forces child anchors to Vector2.up (0,1) — top-left.
  /// This is why layout group children always lose their anchors.
  /// Our captured layouts must account for this behavior.
  /// </summary>
  protected void SetChildAlongAxisWithScale(RectTransform rect, int axis, float pos, float scaleFactor)
  {
    if (rect == null) return;
    this.m_Tracker.Add(this, rect,
      (DrivenTransformProperties)(0x0F00 | (axis == 0 ? 2 : 4)));
    rect.anchorMin = Vector2.up;
    rect.anchorMax = Vector2.up;
    Vector2 anchoredPosition = rect.anchoredPosition;
    if (axis != 0)
      anchoredPosition[axis] = (float)(-(double)pos -
        rect.sizeDelta[axis] * (1.0 - rect.pivot[axis]) * scaleFactor);
    else
      anchoredPosition[axis] = (float)(pos +
        rect.sizeDelta[axis] * rect.pivot[axis] * scaleFactor);
    rect.anchoredPosition = anchoredPosition;
  }

  protected void SetChildAlongAxisWithScale(RectTransform rect, int axis, float pos, float size, float scaleFactor)
  {
    if (rect == null) return;
    this.m_Tracker.Add(this, rect,
      (DrivenTransformProperties)(0x0F00 | (axis == 0 ? 4098 : 8196)));
    rect.anchorMin = Vector2.up;
    rect.anchorMax = Vector2.up;
    Vector2 sizeDelta = rect.sizeDelta;
    sizeDelta[axis] = size;
    rect.sizeDelta = sizeDelta;
    Vector2 anchoredPosition = rect.anchoredPosition;
    anchoredPosition[axis] = axis == 0
      ? pos + size * rect.pivot[axis] * scaleFactor
      : (float)(-(double)pos - size * (1.0 - rect.pivot[axis]) * scaleFactor);
    rect.anchoredPosition = anchoredPosition;
  }

  private bool isRootLayoutGroup =>
    this.transform.parent == null ||
    this.transform.parent.GetComponent(typeof(ILayoutGroup)) == null;

  protected override void OnRectTransformDimensionsChange()
  {
    base.OnRectTransformDimensionsChange();
    if (this.isRootLayoutGroup) this.SetDirty();
  }

  /// <summary>
  /// Called when children are added/removed. Triggers layout rebuild.
  /// This is why adding elements to a layout group auto-repositions them.
  /// </summary>
  protected virtual void OnTransformChildrenChanged() => this.SetDirty();

  protected virtual void OnChildRectTransformDimensionsChange()
  {
    if (CanvasUpdateRegistry.IsRebuildingLayout()) return;
    this.SetDirty();
  }

  /// <summary>
  /// Generic property setter that calls SetDirty() on change.
  /// Used by padding, childAlignment, spacing property setters.
  /// This is why setting properties through the component API auto-triggers rebuild.
  /// </summary>
  protected void SetProperty<T>(ref T currentValue, T newValue)
  {
    if (currentValue == null && newValue == null) return;
    if (currentValue != null && currentValue.Equals(newValue)) return;
    currentValue = newValue;
    this.SetDirty();
  }

  protected void SetDirty()
  {
    if (!this.IsActive()) return;
    if (!CanvasUpdateRegistry.IsRebuildingLayout())
      LayoutRebuilder.MarkLayoutForRebuild(this.rectTransform);
    else
      this.StartCoroutine(this.DelayedSetDirty(this.rectTransform));
  }

  /// <summary>
  /// Fallback: waits one frame then marks dirty. Used when SetDirty() is called
  /// during an active layout rebuild to prevent infinite recursion.
  /// </summary>
  private IEnumerator DelayedSetDirty(RectTransform rectTransform)
  {
    yield return null;
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
  }
}
```

---

## ScrollRect

**Key insights for our UI Builder:**

- `movementType`, `horizontal`, `vertical`, `elasticity`, `scrollSensitivity` are all simple property setters with **no side effects**. They can be changed at runtime freely. Our `ApplyScrollViewDeep()` setting these is completely safe — no rebuild needed from ScrollRect itself (though we still call `MarkLayoutForRebuild` for any parent layout groups).
- `movementType` is just an enum stored in `m_MovementType` — changing it at runtime is perfectly safe. We can move this from "full rebuild" to "ApplyDeep" if desired.
- `SetDirty()` calls `MarkLayoutForRebuild()` — same pattern as LayoutGroup.
- `SetDirtyCaching()` additionally calls `CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild()` and nulls `m_ViewRect`. This is used when viewport/scrollbar references change (not something we do in ApplyDeep).
- `LateUpdate()` handles inertia, elastic bounce, and clamped movement. Uses `Time.unscaledDeltaTime` so it works during pause.
- `EnsureLayoutHasRebuilt()` calls `Canvas.ForceUpdateCanvases()` if the layout hasn't been rebuilt yet. This is why scrolling works correctly even if you interact before the first layout pass.
- ScrollRect implements `ILayoutGroup` and `ILayoutController` — it participates in the layout system to auto-resize the viewport when scrollbars are in `AutoHideAndExpandViewport` mode.
- `UpdateBounds()` uses `GetWorldCorners()` ? `worldToLocalMatrix.MultiplyPoint3x4()` to compute content bounds relative to the viewport. This is the same pattern our `GetGroupScreenBounds()` uses.
- Content clamping in `UpdateBounds()` uses `AdjustBounds()` which accounts for content pivot position — important for understanding why content position behaves differently with different pivot settings.

```csharp
// Decompiled with JetBrains decompiler
// Type: UnityEngine.UI.ScrollRect
// Assembly: UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null

using System;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UnityEngine.UI;

[AddComponentMenu("UI/Scroll Rect", 37)]
[SelectionBase]
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScrollRect :
  UIBehaviour,
  IInitializePotentialDragHandler, IEventSystemHandler,
  IBeginDragHandler, IEndDragHandler, IDragHandler,
  IScrollHandler, ICanvasElement,
  ILayoutElement, ILayoutGroup, ILayoutController
{
  [SerializeField] private RectTransform m_Content;
  [SerializeField] private bool m_Horizontal = true;
  [SerializeField] private bool m_Vertical = true;
  [SerializeField] private MovementType m_MovementType = MovementType.Elastic;
  [SerializeField] private float m_Elasticity = 0.1f;
  [SerializeField] private bool m_Inertia = true;
  [SerializeField] private float m_DecelerationRate = 0.135f;
  [SerializeField] private float m_ScrollSensitivity = 1f;
  [SerializeField] private RectTransform m_Viewport;
  [SerializeField] private Scrollbar m_HorizontalScrollbar;
  [SerializeField] private Scrollbar m_VerticalScrollbar;
  [SerializeField] private ScrollbarVisibility m_HorizontalScrollbarVisibility;
  [SerializeField] private ScrollbarVisibility m_VerticalScrollbarVisibility;
  [SerializeField] private float m_HorizontalScrollbarSpacing;
  [SerializeField] private float m_VerticalScrollbarSpacing;
  [SerializeField] private ScrollRectEvent m_OnValueChanged = new ScrollRectEvent();

  private Vector2 m_PointerStartLocalCursor = Vector2.zero;
  protected Vector2 m_ContentStartPosition = Vector2.zero;
  private RectTransform m_ViewRect;
  protected Bounds m_ContentBounds;
  private Bounds m_ViewBounds;
  private Vector2 m_Velocity;
  private bool m_Dragging;
  private bool m_Scrolling;
  private Vector2 m_PrevPosition = Vector2.zero;
  private Bounds m_PrevContentBounds;
  private Bounds m_PrevViewBounds;
  [NonSerialized] private bool m_HasRebuiltLayout;
  private bool m_HSliderExpand;
  private bool m_VSliderExpand;
  private float m_HSliderHeight;
  private float m_VSliderWidth;
  [NonSerialized] private RectTransform m_Rect;
  private RectTransform m_HorizontalScrollbarRect;
  private RectTransform m_VerticalScrollbarRect;
  private DrivenRectTransformTracker m_Tracker;
  private readonly Vector3[] m_Corners = new Vector3[4];

  // --- Simple property setters (NO side effects — safe for ApplyDeep) ---

  public RectTransform content { get => m_Content; set => m_Content = value; }
  public bool horizontal { get => m_Horizontal; set => m_Horizontal = value; }
  public bool vertical { get => m_Vertical; set => m_Vertical = value; }
  public MovementType movementType { get => m_MovementType; set => m_MovementType = value; }
  public float elasticity { get => m_Elasticity; set => m_Elasticity = value; }
  public bool inertia { get => m_Inertia; set => m_Inertia = value; }
  public float decelerationRate { get => m_DecelerationRate; set => m_DecelerationRate = value; }
  public float scrollSensitivity { get => m_ScrollSensitivity; set => m_ScrollSensitivity = value; }

  // --- Properties that trigger SetDirtyCaching (reference changes) ---

  public RectTransform viewport
  {
    get => m_Viewport;
    set { m_Viewport = value; SetDirtyCaching(); }
  }

  public Scrollbar horizontalScrollbar
  {
    get => m_HorizontalScrollbar;
    set
    {
      if (m_HorizontalScrollbar)
        m_HorizontalScrollbar.onValueChanged.RemoveListener(SetHorizontalNormalizedPosition);
      m_HorizontalScrollbar = value;
      if (m_Horizontal && m_HorizontalScrollbar)
        m_HorizontalScrollbar.onValueChanged.AddListener(SetHorizontalNormalizedPosition);
      SetDirtyCaching();
    }
  }

  public Scrollbar verticalScrollbar
  {
    get => m_VerticalScrollbar;
    set
    {
      if (m_VerticalScrollbar)
        m_VerticalScrollbar.onValueChanged.RemoveListener(SetVerticalNormalizedPosition);
      m_VerticalScrollbar = value;
      if (m_Vertical && m_VerticalScrollbar)
        m_VerticalScrollbar.onValueChanged.AddListener(SetVerticalNormalizedPosition);
      SetDirtyCaching();
    }
  }

  protected RectTransform viewRect
  {
    get
    {
      if (m_ViewRect == null) m_ViewRect = m_Viewport;
      if (m_ViewRect == null) m_ViewRect = (RectTransform) transform;
      return m_ViewRect;
    }
  }

  public Vector2 velocity { get => m_Velocity; set => m_Velocity = value; }

  private RectTransform rectTransform
  {
    get
    {
      if (m_Rect == null) m_Rect = GetComponent<RectTransform>();
      return m_Rect;
    }
  }

  // --- Lifecycle ---

  protected override void OnEnable()
  {
    base.OnEnable();
    if (m_Horizontal && m_HorizontalScrollbar)
      m_HorizontalScrollbar.onValueChanged.AddListener(SetHorizontalNormalizedPosition);
    if (m_Vertical && m_VerticalScrollbar)
      m_VerticalScrollbar.onValueChanged.AddListener(SetVerticalNormalizedPosition);
    CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
    SetDirty();
  }

  protected override void OnDisable()
  {
    CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);
    if (m_HorizontalScrollbar)
      m_HorizontalScrollbar.onValueChanged.RemoveListener(SetHorizontalNormalizedPosition);
    if (m_VerticalScrollbar)
      m_VerticalScrollbar.onValueChanged.RemoveListener(SetVerticalNormalizedPosition);
    m_Dragging = false;
    m_Scrolling = false;
    m_HasRebuiltLayout = false;
    m_Tracker.Clear();
    m_Velocity = Vector2.zero;
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
    base.OnDisable();
  }

  public override bool IsActive() => base.IsActive() && m_Content != null;

  private void EnsureLayoutHasRebuilt()
  {
    if (m_HasRebuiltLayout || CanvasUpdateRegistry.IsRebuildingLayout()) return;
    Canvas.ForceUpdateCanvases();
  }

  // --- Drag handling ---

  public virtual void OnBeginDrag(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left || !IsActive()) return;
    UpdateBounds();
    m_PointerStartLocalCursor = Vector2.zero;
    RectTransformUtility.ScreenPointToLocalPointInRectangle(
      viewRect, eventData.position, eventData.pressEventCamera, out m_PointerStartLocalCursor);
    m_ContentStartPosition = m_Content.anchoredPosition;
    m_Dragging = true;
  }

  public virtual void OnDrag(PointerEventData eventData)
  {
    if (!m_Dragging || eventData.button != PointerEventData.InputButton.Left || !IsActive()) return;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
      viewRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) return;

    UpdateBounds();
    Vector2 target = m_ContentStartPosition + (localPoint - m_PointerStartLocalCursor);
    Vector2 offset = CalculateOffset(target - m_Content.anchoredPosition);
    Vector2 position = target + offset;
    if (m_MovementType == MovementType.Elastic)
    {
      if (offset.x != 0f) position.x -= RubberDelta(offset.x, m_ViewBounds.size.x);
      if (offset.y != 0f) position.y -= RubberDelta(offset.y, m_ViewBounds.size.y);
    }
    SetContentAnchoredPosition(position);
  }

  public virtual void OnEndDrag(PointerEventData eventData)
  {
    if (eventData.button != PointerEventData.InputButton.Left) return;
    m_Dragging = false;
  }

  // --- Scroll (mouse wheel) ---

  public virtual void OnScroll(PointerEventData data)
  {
    if (!IsActive()) return;
    EnsureLayoutHasRebuilt();
    UpdateBounds();
    Vector2 scrollDelta = data.scrollDelta;
    scrollDelta.y *= -1f;
    // Route X to Y or vice versa for single-axis scrolls
    if (vertical && !horizontal && Mathf.Abs(scrollDelta.x) > Mathf.Abs(scrollDelta.y))
    { scrollDelta.y = scrollDelta.x; scrollDelta.x = 0f; }
    if (horizontal && !vertical && Mathf.Abs(scrollDelta.y) > Mathf.Abs(scrollDelta.x))
    { scrollDelta.x = scrollDelta.y; scrollDelta.y = 0f; }
    if (data.IsScrolling()) m_Scrolling = true;

    Vector2 position = m_Content.anchoredPosition + scrollDelta * m_ScrollSensitivity;
    if (m_MovementType == MovementType.Clamped)
      position += CalculateOffset(position - m_Content.anchoredPosition);
    SetContentAnchoredPosition(position);
    UpdateBounds();
  }

  // --- Inertia & elastic bounce (LateUpdate) ---

  protected virtual void LateUpdate()
  {
    if (!m_Content) return;
    EnsureLayoutHasRebuilt();
    UpdateBounds();
    float dt = Time.unscaledDeltaTime;
    Vector2 offset = CalculateOffset(Vector2.zero);
    if (dt > 0f)
    {
      if (!m_Dragging && (offset != Vector2.zero || m_Velocity != Vector2.zero))
      {
        Vector2 pos = m_Content.anchoredPosition;
        for (int axis = 0; axis < 2; axis++)
        {
          if (m_MovementType == MovementType.Elastic && offset[axis] != 0f)
          {
            float vel = m_Velocity[axis];
            float e = m_Scrolling ? m_Elasticity * 3f : m_Elasticity;
            pos[axis] = Mathf.SmoothDamp(
              m_Content.anchoredPosition[axis],
              m_Content.anchoredPosition[axis] + offset[axis],
              ref vel, e, float.PositiveInfinity, dt);
            if (Mathf.Abs(vel) < 1f) vel = 0f;
            m_Velocity[axis] = vel;
          }
          else if (m_Inertia)
          {
            m_Velocity[axis] *= Mathf.Pow(m_DecelerationRate, dt);
            if (Mathf.Abs(m_Velocity[axis]) < 1f) m_Velocity[axis] = 0f;
            pos[axis] += m_Velocity[axis] * dt;
          }
          else m_Velocity[axis] = 0f;
        }
        if (m_MovementType == MovementType.Clamped)
        {
          offset = CalculateOffset(pos - m_Content.anchoredPosition);
          pos += offset;
        }
        SetContentAnchoredPosition(pos);
      }
      if (m_Dragging && m_Inertia)
        m_Velocity = Vector3.Lerp(m_Velocity,
          (m_Content.anchoredPosition - m_PrevPosition) / dt, dt * 10f);
    }
    if (m_ViewBounds != m_PrevViewBounds || m_ContentBounds != m_PrevContentBounds ||
        m_Content.anchoredPosition != m_PrevPosition)
    {
      UpdateScrollbars(offset);
      m_OnValueChanged.Invoke(normalizedPosition);
      UpdatePrevData();
    }
    UpdateScrollbarVisibility();
    m_Scrolling = false;
  }

  // --- Bounds calculation ---

  protected void UpdateBounds()
  {
    m_ViewBounds = new Bounds(viewRect.rect.center, viewRect.rect.size);
    m_ContentBounds = GetBounds();
    if (m_Content == null) return;

    Vector3 size = m_ContentBounds.size;
    Vector3 center = m_ContentBounds.center;
    Vector2 pivot = m_Content.pivot;
    AdjustBounds(ref m_ViewBounds, ref pivot, ref size, ref center);
    m_ContentBounds.size = size;
    m_ContentBounds.center = center;

    if (movementType != MovementType.Clamped) return;
    // Clamp content to view bounds
    Vector2 clampOffset = Vector2.zero;
    if (m_ViewBounds.max.x > m_ContentBounds.max.x)
      clampOffset.x = Math.Min(m_ViewBounds.min.x - m_ContentBounds.min.x,
                               m_ViewBounds.max.x - m_ContentBounds.max.x);
    else if (m_ViewBounds.min.x < m_ContentBounds.min.x)
      clampOffset.x = Math.Max(m_ViewBounds.min.x - m_ContentBounds.min.x,
                               m_ViewBounds.max.x - m_ContentBounds.max.x);
    if (m_ViewBounds.min.y < m_ContentBounds.min.y)
      clampOffset.y = Math.Max(m_ViewBounds.min.y - m_ContentBounds.min.y,
                               m_ViewBounds.max.y - m_ContentBounds.max.y);
    else if (m_ViewBounds.max.y > m_ContentBounds.max.y)
      clampOffset.y = Math.Min(m_ViewBounds.min.y - m_ContentBounds.min.y,
                               m_ViewBounds.max.y - m_ContentBounds.max.y);

    if (clampOffset.sqrMagnitude > float.Epsilon)
    {
      center = m_Content.anchoredPosition + clampOffset;
      if (!m_Horizontal) center.x = m_Content.anchoredPosition.x;
      if (!m_Vertical) center.y = m_Content.anchoredPosition.y;
      AdjustBounds(ref m_ViewBounds, ref pivot, ref size, ref center);
    }
  }

  /// <summary>
  /// Adjusts content bounds so content is never smaller than the view.
  /// Accounts for pivot offset — explains why different pivots cause
  /// different scroll behavior.
  /// </summary>
  internal static void AdjustBounds(
    ref Bounds viewBounds, ref Vector2 contentPivot,
    ref Vector3 contentSize, ref Vector3 contentPos)
  {
    Vector3 excess = viewBounds.size - contentSize;
    if (excess.x > 0f)
    {
      contentPos.x -= excess.x * (contentPivot.x - 0.5f);
      contentSize.x = viewBounds.size.x;
    }
    if (excess.y > 0f)
    {
      contentPos.y -= excess.y * (contentPivot.y - 0.5f);
      contentSize.y = viewBounds.size.y;
    }
  }

  private Bounds GetBounds()
  {
    if (m_Content == null) return new Bounds();
    m_Content.GetWorldCorners(m_Corners);
    Matrix4x4 w2l = viewRect.worldToLocalMatrix;
    return InternalGetBounds(m_Corners, ref w2l);
  }

  internal static Bounds InternalGetBounds(Vector3[] corners, ref Matrix4x4 viewWorldToLocalMatrix)
  {
    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    for (int i = 0; i < 4; i++)
    {
      Vector3 p = viewWorldToLocalMatrix.MultiplyPoint3x4(corners[i]);
      min = Vector3.Min(p, min);
      max = Vector3.Max(p, max);
    }
    Bounds b = new Bounds(min, Vector3.zero);
    b.Encapsulate(max);
    return b;
  }

  private Vector2 CalculateOffset(Vector2 delta)
  {
    return InternalCalculateOffset(ref m_ViewBounds, ref m_ContentBounds,
      m_Horizontal, m_Vertical, m_MovementType, ref delta);
  }

  internal static Vector2 InternalCalculateOffset(
    ref Bounds viewBounds, ref Bounds contentBounds,
    bool horizontal, bool vertical, MovementType movementType, ref Vector2 delta)
  {
    Vector2 offset = Vector2.zero;
    if (movementType == MovementType.Unrestricted) return offset;
    Vector2 cMin = contentBounds.min; Vector2 cMax = contentBounds.max;
    if (horizontal)
    {
      cMin.x += delta.x; cMax.x += delta.x;
      float d1 = viewBounds.min.x - cMin.x;
      float d2 = viewBounds.max.x - cMax.x;
      if (d1 < -0.001f) offset.x = d1;
      else if (d2 > 0.001f) offset.x = d2;
    }
    if (vertical)
    {
      cMin.y += delta.y; cMax.y += delta.y;
      float d1 = viewBounds.max.y - cMax.y;
      float d2 = viewBounds.min.y - cMin.y;
      if (d1 > 0.001f) offset.y = d1;
      else if (d2 < -0.001f) offset.y = d2;
    }
    return offset;
  }

  private static float RubberDelta(float overStretching, float viewSize)
  {
    return (1f - 1f / (Mathf.Abs(overStretching) * 0.55f / viewSize + 1f))
           * viewSize * Mathf.Sign(overStretching);
  }

  // --- Layout interface (for auto-expand viewport mode) ---

  public virtual void SetLayoutHorizontal()
  {
    m_Tracker.Clear();
    UpdateCachedData();
    if (m_HSliderExpand || m_VSliderExpand)
    {
      m_Tracker.Add(this, viewRect,
        DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition |
        DrivenTransformProperties.SizeDelta);
      viewRect.anchorMin = Vector2.zero;
      viewRect.anchorMax = Vector2.one;
      viewRect.sizeDelta = Vector2.zero;
      viewRect.anchoredPosition = Vector2.zero;
      LayoutRebuilder.ForceRebuildLayoutImmediate(content);
      m_ViewBounds = new Bounds(viewRect.rect.center, viewRect.rect.size);
      m_ContentBounds = GetBounds();
    }
    if (m_VSliderExpand && vScrollingNeeded)
    {
      viewRect.sizeDelta = new Vector2(-(m_VSliderWidth + m_VerticalScrollbarSpacing), viewRect.sizeDelta.y);
      LayoutRebuilder.ForceRebuildLayoutImmediate(content);
      m_ViewBounds = new Bounds(viewRect.rect.center, viewRect.rect.size);
      m_ContentBounds = GetBounds();
    }
    if (m_HSliderExpand && hScrollingNeeded)
    {
      viewRect.sizeDelta = new Vector2(viewRect.sizeDelta.x, -(m_HSliderHeight + m_HorizontalScrollbarSpacing));
      m_ViewBounds = new Bounds(viewRect.rect.center, viewRect.rect.size);
      m_ContentBounds = GetBounds();
    }
    if (m_VSliderExpand && vScrollingNeeded && viewRect.sizeDelta.x == 0f && viewRect.sizeDelta.y < 0f)
      viewRect.sizeDelta = new Vector2(-(m_VSliderWidth + m_VerticalScrollbarSpacing), viewRect.sizeDelta.y);
  }

  // --- Dirty marking ---

  protected void SetDirty()
  {
    if (!IsActive()) return;
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
  }

  protected void SetDirtyCaching()
  {
    if (!IsActive()) return;
    CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
    LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
    m_ViewRect = null; // Force re-resolve on next access
  }

  // --- Enums ---

  public enum MovementType { Unrestricted, Elastic, Clamped }
  public enum ScrollbarVisibility { Permanent, AutoHide, AutoHideAndExpandViewport }

  [Serializable]
  public class ScrollRectEvent : UnityEvent<Vector2> { }
}
```

---

## Actionable Takeaways for UI Builder

### 1. MarkLayoutForRebuild is cheap — call it freely
Multiple calls per frame are deduplicated via `CanvasUpdateRegistry` hash check. Our `ApplyNodeDeep()` calling it at the end is correct and costs nothing extra even if it was already called by a property setter.

### 2. ForceRebuildLayoutImmediate for immediate bounds
When we need accurate bounds right after a change (e.g., for selection overlay positioning), call `ForceRebuildLayoutImmediate()` on the layout root, not `MarkLayoutForRebuild()`. The deferred version won't have updated bounds until end of frame.

### 3. ScrollRect properties are safe for in-place update
`movementType`, `horizontal`, `vertical`, `elasticity`, `scrollSensitivity`, `inertia`, `decelerationRate` are all simple field setters with no rebuild side effects. They can all safely go through `ApplyDeep` instead of requiring full rebuild.

### 4. LayoutGroup forces child anchors to top-left
`SetChildAlongAxisWithScale()` always sets `anchorMin = Vector2.up` and `anchorMax = Vector2.up` on children. Captured layouts with children inside layout groups will always have their anchors overridden. This is expected Unity behavior.

### 5. Layout calculation is bottom-up, control is top-down
`CalculateLayoutInput*` recurses children first (bottom-up), then processes the current node. `SetLayout*` processes the current node first (`ILayoutSelfController` before `ILayoutController`), then recurses into children (top-down). ContentSizeFitter always runs before LayoutGroup on the same node.

### 6. DelayedSetDirty prevents infinite loops
If `SetDirty()` is called during `CanvasUpdateRegistry.IsRebuildingLayout()`, it defers via a coroutine. Our code should be careful about calling `MarkLayoutForRebuild` from within callbacks that fire during layout — but the system handles it gracefully.
