# Valheim UI Decompiled Reference — InventoryGui, InventoryGrid

Decompiled from `assembly_valheim.dll` (Version=0.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll`

This file covers Valheim-specific UI components that our UI Builder parser captures
and interacts with. Understanding these internals lets us make smarter decisions about
how to capture, serialize, and render Valheim UI structures.

**See also:**
- `UNITY_UI_DECOMPILED_REFERENCE.md` — Part 1 (LayoutRebuilder, LayoutGroup, ScrollRect)
- `UNITY_UI_DECOMPILED_REFERENCE_2.md` — Part 2 (Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D)
- `UNITY_UI_DECOMPILED_REFERENCE_3.md` — Part 3 (HorizontalOrVerticalLayoutGroup, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown)
- `TMPRO_DECOMPILED_REFERENCE.md` — TMP_Text, TMP_InputField, TMP_Dropdown
- `VALHEIM_COMPONENT_REFERENCE.md` — ZRoutedRpc, ZNet/ZDO patterns, Player/Game UI hooks

### Reference File Index

| File | Contents | Status |
|------|----------|--------|
| `UNITY_UI_DECOMPILED_REFERENCE.md` | LayoutRebuilder, LayoutGroup, ScrollRect | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_2.md` | Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_3.md` | HorizontalOrVerticalLayoutGroup, HLG/VLG, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown (Legacy) | ? Complete |
| `TMPRO_DECOMPILED_REFERENCE.md` | TMP_Text, TMP_InputField, TMP_Dropdown — text rendering, input handling, TMP dropdown vs legacy | ? Complete |
| `VALHEIM_UI_REFERENCE.md` | InventoryGui, InventoryGrid — Valheim-specific UI components our parser captures | ? Complete |
| `VALHEIM_COMPONENT_REFERENCE.md` | ZRoutedRpc (for UILayoutSyncRPC), ZNet/ZDO patterns, Player/Game UI hooks our integration uses | ? Complete |

---

## Table of Contents

1. [InventoryGui](#inventorygui) — Main inventory/crafting UI controller
2. [InventoryGrid](#inventorygrid) — Grid-based item display component

---

## InventoryGui

**Key insights for our UI Builder:**

- **Singleton pattern** via `m_instance` — always accessed through `InventoryGui.instance`. Our parser can safely reference this to discover the inventory UI hierarchy.
- **`m_player`, `m_crafting`, `m_info`, `m_container`** are all `RectTransform` references — these are the major UI panels our parser captures as layout roots.
- **`m_playerGrid` / `m_containerGrid`** are `InventoryGrid` components found via `GetComponentInChildren<InventoryGrid>()` — our parser captures these grid structures.
- **`UIGroupHandler m_inventoryGroup` / `m_uiGroups[]`** — gamepad navigation groups. `SetActiveGroup()` cycles through these for gamepad tab navigation. Our parser should capture these for accessibility.
- **`m_recipeListRoot`** (`RectTransform`) — the recipe list container. Recipes are instantiated from `m_recipeElementPrefab` and positioned via `anchoredPosition`. This is a manual layout (not LayoutGroup) — our parser captures the prefab structure.
- **`m_recipeListSpace`** (30f) — manual spacing between recipe elements. Each recipe is positioned at `y = index * -m_recipeListSpace`. Our parser can synthesize a VLG equivalent.
- **`m_hiddenFrames`** — used by `IsVisible()` to detect if the inventory is actually shown (not just the animator bool). Returns true only if `m_hiddenFrames <= 1`. Our UI hooks check this.
- **`UpdateCraftingPanel()`** — rebuilds the entire recipe list each call. Destroys all existing recipe GOs, creates new ones, sorts them, positions them. This is why capturing the recipe list requires snapshotting during active display.
- **`SetupDragItem()`** — creates a drag ghost from `m_dragItemPrefab`. The ghost follows the mouse in `UpdateItemDrag()`. Our parser doesn't capture drag ghosts.
- **`Show(Container)`** / `Hide()` — the main visibility toggle. `Show()` calls `SetupCrafting()` which calls `UpdateCraftingPanel()`. `Hide()` destroys drag items, closes container, plays effects.
- **`RecipeDataPair`** struct — holds `Recipe`, `ItemDrop.ItemData`, `GameObject` (UI element), and `CanCraft` bool. Sorting is done on this struct array.
- **Recipe sorting** (`UpdateRecipeList`) — supports Original/Name/Type/Weight sort methods. Stored in player's unique keys as `sortcraft`. The sort comparators use `byCraftable` first (craftable items on top), then `bySortWeight`, then the selected method, then `byLevel`.
- **`m_splitPanel`** — the stack split dialog. Uses a `Slider` for amount selection with keyboard number input support (`UpdateSplitDialog()`). Our parser captures this panel structure.
- **Container interaction** — `UpdateContainer()` handles auto-close by distance (`m_autoCloseDistance = 4f`), hold-to-stack-all (`m_containerHoldPlaceStackDelay = 0.5f`), and hold-to-exit behavior.
- **`OnSelectedItem()`** — complex item interaction handler. Handles: drag start, drag drop, swap, split, move-to-container, and drop-on-ground. Different behavior based on `InventoryGrid.Modifier` (Select, Split, Move, Drop).

```csharp
// Decompiled with JetBrains decompiler
// Type: InventoryGui
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#nullable disable
public class InventoryGui : MonoBehaviour
{
  private List<Recipe> m_tempRecipes = new List<Recipe>();
  private List<ItemDrop.ItemData> m_tempItemList = new List<ItemDrop.ItemData>();
  private List<ItemDrop.ItemData> m_tempWornItems = new List<ItemDrop.ItemData>();
  private static InventoryGui m_instance;
  [Header("Gamepad")]
  public UIGroupHandler m_inventoryGroup;
  public UIGroupHandler[] m_uiGroups = new UIGroupHandler[0];
  private int m_activeGroup = 1;
  [SerializeField]
  private bool m_inventoryGroupCycling;
  [Header("Other")]
  public Transform m_inventoryRoot;
  public RectTransform m_player;
  public RectTransform m_crafting;
  public RectTransform m_info;
  public RectTransform m_container;
  public GameObject m_dragItemPrefab;
  public TMP_Text m_containerName;
  public Button m_dropButton;
  public Button m_takeAllButton;
  public Button m_stackAllButton;
  public float m_autoCloseDistance = 4f;
  [Header("Crafting dialog")]
  public Button m_tabCraft;
  public Button m_tabUpgrade;
  public GameObject m_recipeElementPrefab;
  public RectTransform m_recipeListRoot;
  public Scrollbar m_recipeListScroll;
  public float m_recipeListSpace = 30f;
  public float m_craftBonusChance = 0.25f;
  public int m_craftBonusAmount = 1;
  public EffectList m_craftBonusEffect;
  public float m_craftDurationSkillMaxDecrease = 0.6f;
  public float m_craftDuration = 2f;
  public int m_multiCraftAmount = 5;
  public float m_multiCraftDuration = 6f;
  public TMP_Text m_craftingStationName;
  public Image m_craftingStationIcon;
  public RectTransform m_craftingStationLevelRoot;
  public TMP_Text m_craftingStationLevel;
  public TMP_Text m_recipeName;
  public TMP_Text m_recipeDecription;
  public Image m_recipeIcon;
  public GameObject[] m_recipeRequirementList = new GameObject[0];
  public Button m_variantButton;
  public Button m_craftButton;
  public Button m_craftCancelButton;
  public Transform m_craftProgressPanel;
  public GuiBar m_craftProgressBar;
  [Header("Repair")]
  public Button m_repairButton;
  public Transform m_repairPanel;
  public Image m_repairButtonGlow;
  public Transform m_repairPanelSelection;
  [Header("Upgrade")]
  public Image m_upgradeItemIcon;
  public GuiBar m_upgradeItemDurability;
  public TMP_Text m_upgradeItemName;
  public TMP_Text m_upgradeItemQuality;
  public GameObject m_upgradeItemQualityArrow;
  public TMP_Text m_upgradeItemNextQuality;
  public TMP_Text m_upgradeItemIndex;
  public TMP_Text m_itemCraftType;
  public RectTransform m_qualityPanel;
  public Button m_qualityLevelDown;
  public Button m_qualityLevelUp;
  public TMP_Text m_qualityLevel;
  public Image m_minStationLevelIcon;
  private Color m_minStationLevelBasecolor;
  public TMP_Text m_minStationLevelText;
  public ScrollRectEnsureVisible m_recipeEnsureVisible;
  [Header("Variants dialog")]
  public VariantDialog m_variantDialog;
  [Header("Skills dialog")]
  public SkillsDialog m_skillsDialog;
  [Header("Texts dialog")]
  public TextsDialog m_textsDialog;
  [Header("Split dialog")]
  public Transform m_splitPanel;
  public Slider m_splitSlider;
  public TMP_Text m_splitAmount;
  public Button m_splitCancelButton;
  public Button m_splitOkButton;
  public Image m_splitIcon;
  public TMP_Text m_splitIconName;
  [Header("Character stats")]
  public Transform m_infoPanel;
  public TMP_Text m_playerName;
  public TMP_Text m_armor;
  public TMP_Text m_weight;
  public TMP_Text m_containerWeight;
  public Toggle m_pvp;
  [Header("Trophies")]
  public GameObject m_trophiesPanel;
  public RectTransform m_trophieListRoot;
  public float m_trophieListSpace = 30f;
  public GameObject m_trophieElementPrefab;
  public Scrollbar m_trophyListScroll;
  [Header("Effects")]
  public EffectList m_moveItemEffects = new EffectList();
  public EffectList m_craftItemEffects = new EffectList();
  public EffectList m_craftItemDoneEffects = new EffectList();
  public EffectList m_openInventoryEffects = new EffectList();
  public EffectList m_closeInventoryEffects = new EffectList();
  public EffectList m_setActiveGroupEffects = new EffectList();
  [HideInInspector]
  public InventoryGrid m_playerGrid;
  private InventoryGrid m_containerGrid;
  private Animator m_animator;
  private Container m_currentContainer;
  private bool m_firstContainerUpdate = true;
  private float m_containerHoldTime;
  private float m_containerHoldPlaceStackDelay = 0.5f;
  private float m_containerHoldExitDelay = 0.5f;
  private int m_containerHoldState;
  private InventoryGui.RecipeDataPair m_selectedRecipe;
  private List<ItemDrop.ItemData> m_upgradeItems = new List<ItemDrop.ItemData>();
  private List<Piece.Requirement> m_reqList = new List<Piece.Requirement>();
  private int m_selectedVariant;
  private Recipe m_craftRecipe;
  private ItemDrop.ItemData m_craftUpgradeItem;
  private int m_craftVariant;
  private List<InventoryGui.RecipeDataPair> m_availableRecipes = new List<InventoryGui.RecipeDataPair>();
  private GameObject m_dragGo;
  private ItemDrop.ItemData m_dragItem;
  private Inventory m_dragInventory;
  private int m_dragAmount = 1;
  private ItemDrop.ItemData m_splitItem;
  private Inventory m_splitInventory;
  private float m_craftTimer = -1f;
  private bool m_multiCrafting;
  private float m_recipeListBaseSize;
  private int m_hiddenFrames = 9999;
  private string m_splitInput = "";
  private DateTime m_lastSplitInput;
  public float m_splitNumInputTimeoutSec = 0.5f;
  private List<GameObject> m_trophyList = new List<GameObject>();
  private float m_trophieListBaseSize;

  public static InventoryGui instance => InventoryGui.m_instance;

  private void Awake()
  {
    InventoryGui.m_instance = this;
    this.m_animator = this.GetComponent<Animator>();
    this.m_inventoryRoot.gameObject.SetActive(true);
    this.m_player.gameObject.SetActive(true);
    this.m_crafting.gameObject.SetActive(true);
    this.m_info.gameObject.SetActive(true);
    this.m_container.gameObject.SetActive(false);
    this.m_splitPanel.gameObject.SetActive(false);
    this.m_trophiesPanel.SetActive(false);
    this.m_variantDialog.gameObject.SetActive(false);
    this.m_skillsDialog.gameObject.SetActive(false);
    this.m_textsDialog.gameObject.SetActive(false);
    this.m_playerGrid = this.m_player.GetComponentInChildren<InventoryGrid>();
    this.m_containerGrid = this.m_container.GetComponentInChildren<InventoryGrid>();
    this.m_playerGrid.m_onSelected += new Action<InventoryGrid, ItemDrop.ItemData, Vector2i, InventoryGrid.Modifier>(this.OnSelectedItem);
    this.m_playerGrid.m_onRightClick += new Action<InventoryGrid, ItemDrop.ItemData, Vector2i>(this.OnRightClickItem);
    this.m_containerGrid.m_onSelected += new Action<InventoryGrid, ItemDrop.ItemData, Vector2i, InventoryGrid.Modifier>(this.OnSelectedItem);
    this.m_containerGrid.m_onRightClick += new Action<InventoryGrid, ItemDrop.ItemData, Vector2i>(this.OnRightClickItem);
    this.m_playerGrid.OnMoveToLowerInventoryGrid += new Action<Vector2i>(this.MoveToLowerInventoryGrid);
    this.m_containerGrid.OnMoveToUpperInventoryGrid += new Action<Vector2i>(this.MoveToUpperInventoryGrid);
    this.m_craftButton.onClick.AddListener(new UnityAction(this.OnCraftPressed));
    this.m_craftCancelButton.onClick.AddListener(new UnityAction(this.OnCraftCancelPressed));
    this.m_dropButton.onClick.AddListener(new UnityAction(this.OnDropOutside));
    this.m_takeAllButton.onClick.AddListener(new UnityAction(this.OnTakeAll));
    this.m_stackAllButton.onClick.AddListener(new UnityAction(this.OnStackAll));
    this.m_repairButton.onClick.AddListener(new UnityAction(this.OnRepairPressed));
    this.m_splitSlider.onValueChanged.AddListener(new UnityAction<float>(this.OnSplitSliderChanged));
    this.m_splitCancelButton.onClick.AddListener(new UnityAction(this.OnSplitCancel));
    this.m_splitOkButton.onClick.AddListener(new UnityAction(this.OnSplitOk));
    this.m_variantDialog.m_selected += new Action<int>(this.OnVariantSelected);
    this.m_recipeListBaseSize = this.m_recipeListRoot.rect.height;
    this.m_trophieListBaseSize = this.m_trophieListRoot.rect.height;
    this.m_minStationLevelBasecolor = this.m_minStationLevelText.color;
    this.m_tabCraft.interactable = false;
    this.m_tabUpgrade.interactable = true;
  }

  private void MoveToLowerInventoryGrid(Vector2i previousGridPosition)
  {
    if (!this.m_inventoryGroup.IsActive || !this.IsContainerOpen())
      return;
    int num = (int) Math.Ceiling((double) (this.m_playerGrid.GridWidth - this.m_containerGrid.GridWidth) / 2.0);
    Vector2i selectionGridPosition = this.m_containerGrid.SelectionGridPosition;
    int a = Mathf.Max(0, previousGridPosition.x - num);
    selectionGridPosition.x = Mathf.Min(a, this.m_containerGrid.GridWidth - 1);
    this.m_containerGrid.SetSelection(selectionGridPosition);
    this.SetActiveGroup(this.m_activeGroup - 1);
  }

  private void MoveToUpperInventoryGrid(Vector2i previousGridPosition)
  {
    if (!this.m_inventoryGroup.IsActive)
      return;
    int num = (int) Math.Ceiling((double) (this.m_playerGrid.GridWidth - this.m_containerGrid.GridWidth) / 2.0);
    Vector2i selectionGridPosition = this.m_playerGrid.SelectionGridPosition;
    int a = Mathf.Max(0, previousGridPosition.x + num);
    int b = Mathf.Min(this.m_playerGrid.GridWidth - 1, previousGridPosition.x);
    selectionGridPosition.x = Mathf.Max(a, b);
    this.m_playerGrid.SetSelection(selectionGridPosition);
    this.SetActiveGroup(this.m_activeGroup + 1);
  }

  private void OnDestroy() => InventoryGui.m_instance = (InventoryGui) null;

  public static bool IsVisible()
  {
    return (bool) (UnityEngine.Object) InventoryGui.m_instance && InventoryGui.m_instance.m_hiddenFrames <= 1;
  }

  public bool IsContainerOpen() => (UnityEngine.Object) this.m_currentContainer != (UnityEngine.Object) null;

  public void Show(Container container, int activeGroup = 1)
  {
    Hud.HidePieceSelection();
    this.m_animator.SetBool("visible", true);
    this.SetActiveGroup(activeGroup, false);
    Player localPlayer = Player.m_localPlayer;
    if ((bool) (UnityEngine.Object) localPlayer)
      this.SetupCrafting();
    this.m_currentContainer = container;
    this.m_hiddenFrames = 0;
    if ((bool) (UnityEngine.Object) localPlayer)
      this.m_openInventoryEffects.Create(localPlayer.transform.position, Quaternion.identity);
    Gogan.LogEvent("Screen", "Enter", "Inventory", 0L);
  }

  public void Hide()
  {
    if (!this.m_animator.GetBool("visible"))
      return;
    this.m_craftTimer = -1f;
    this.m_animator.SetBool("visible", false);
    this.m_trophiesPanel.SetActive(false);
    this.m_variantDialog.gameObject.SetActive(false);
    this.m_skillsDialog.gameObject.SetActive(false);
    this.m_textsDialog.gameObject.SetActive(false);
    this.m_splitPanel.gameObject.SetActive(false);
    this.SetupDragItem((ItemDrop.ItemData) null, (Inventory) null, 1);
    if ((bool) (UnityEngine.Object) this.m_currentContainer)
    {
      this.m_currentContainer.SetInUse(false);
      this.m_currentContainer = (Container) null;
    }
    if ((bool) (UnityEngine.Object) Player.m_localPlayer)
      this.m_closeInventoryEffects.Create(Player.m_localPlayer.transform.position, Quaternion.identity);
    this.m_containerHoldTime = 0.0f;
    this.m_containerHoldState = 0;
    Gogan.LogEvent("Screen", "Exit", "Inventory", 0L);
  }

  private void SetActiveGroup(int index, bool playSound = true)
  {
    if (!this.m_inventoryGroupCycling)
    {
      index = Mathf.Clamp(index, 0, this.m_uiGroups.Length - 1);
    }
    else
    {
      if (index == 0 && !this.IsContainerOpen())
        index = this.m_uiGroups.Length - 1;
      index = (index + this.m_uiGroups.Length) % this.m_uiGroups.Length;
    }
    this.m_activeGroup = index;
    for (int index1 = 0; index1 < this.m_uiGroups.Length; ++index1)
      this.m_uiGroups[index1].SetActive(index1 == this.m_activeGroup);
    if (!((bool) (UnityEngine.Object) Player.m_localPlayer & playSound))
      return;
    this.m_setActiveGroupEffects?.Create(Player.m_localPlayer.transform.position, Quaternion.identity);
  }

  private void UpdateRecipeList(List<Recipe> recipes)
  {
    Player localPlayer = Player.m_localPlayer;
    foreach (InventoryGui.RecipeDataPair availableRecipe in this.m_availableRecipes)
      UnityEngine.Object.Destroy((UnityEngine.Object) availableRecipe.InterfaceElement);
    this.m_availableRecipes.Clear();
    bool globalKey = ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
    if (this.InCraftTab())
    {
      foreach (Recipe recipe in recipes)
        this.AddRecipeToList(localPlayer, recipe, (ItemDrop.ItemData) null, localPlayer.HaveRequirements(recipe, false, 1) | globalKey);
    }
    else
    {
      for (int index = 0; index < recipes.Count; ++index)
      {
        Recipe recipe = recipes[index];
        if (recipe.m_item.m_itemData.m_shared.m_maxQuality > 1)
        {
          this.m_tempItemList.Clear();
          localPlayer.GetInventory().GetAllItems(recipe.m_item.m_itemData.m_shared.m_name, this.m_tempItemList);
          foreach (ItemDrop.ItemData tempItem in this.m_tempItemList)
          {
            bool canCraft = tempItem.m_quality < tempItem.m_shared.m_maxQuality && localPlayer.HaveRequirements(recipe, false, tempItem.m_quality + 1) | globalKey;
            this.AddRecipeToList(localPlayer, recipe, tempItem, canCraft);
          }
        }
      }
    }
    this.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(this.m_recipeListBaseSize, (float) this.m_availableRecipes.Count * this.m_recipeListSpace));
    // ... sorting logic omitted for brevity (see full source in workspace summary) ...
    for (int index = 0; index < this.m_availableRecipes.Count; ++index)
      (this.m_availableRecipes[index].InterfaceElement.transform as RectTransform).anchoredPosition = new Vector2(0.0f, (float) index * -this.m_recipeListSpace);
  }

  private void AddRecipeToList(Player player, Recipe recipe, ItemDrop.ItemData item, bool canCraft)
  {
    int count = this.m_availableRecipes.Count;
    GameObject element = UnityEngine.Object.Instantiate<GameObject>(this.m_recipeElementPrefab, (Transform) this.m_recipeListRoot);
    element.SetActive(true);
    (element.transform as RectTransform).anchoredPosition = new Vector2(0.0f, (float) count * -this.m_recipeListSpace);
    Image component1 = element.transform.Find("icon").GetComponent<Image>();
    component1.sprite = recipe.m_item.m_itemData.GetIcon();
    component1.color = canCraft ? Color.white : new Color(1f, 0.0f, 1f, 0.0f);
    TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
    string str = Localization.instance.Localize(recipe.m_item.m_itemData.m_shared.m_name);
    if (recipe.m_amount > 1)
      str += $" x{recipe.m_amount}";
    component2.text = str;
    component2.color = canCraft ? Color.white : new Color(0.66f, 0.66f, 0.66f, 1f);
    GuiBar component3 = element.transform.Find("Durability").GetComponent<GuiBar>();
    if (item != null && item.m_shared.m_useDurability && (double) item.m_durability < (double) item.GetMaxDurability())
    {
      component3.gameObject.SetActive(true);
      component3.SetValue(item.GetDurabilityPercentage());
    }
    else
      component3.gameObject.SetActive(false);
    TMP_Text component4 = element.transform.Find("QualityLevel").GetComponent<TMP_Text>();
    if (item != null)
    {
      component4.gameObject.SetActive(true);
      component4.text = item.m_quality.ToString();
    }
    else
      component4.gameObject.SetActive(false);
    element.GetComponent<Button>().onClick.AddListener((UnityAction) (() => this.OnSelectedRecipe(element)));
    this.m_availableRecipes.Add(new InventoryGui.RecipeDataPair(recipe, item, element, canCraft));
  }

  public static bool SetupRequirement(
    Transform elementRoot,
    Piece.Requirement req,
    Player player,
    bool craft,
    int quality,
    int craftMultiplier = 1)
  {
    Image component1 = elementRoot.transform.Find("res_icon").GetComponent<Image>();
    TMP_Text component2 = elementRoot.transform.Find("res_name").GetComponent<TMP_Text>();
    TMP_Text component3 = elementRoot.transform.Find("res_amount").GetComponent<TMP_Text>();
    UITooltip component4 = elementRoot.GetComponent<UITooltip>();
    if ((UnityEngine.Object) req.m_resItem != (UnityEngine.Object) null)
    {
      component1.gameObject.SetActive(true);
      component2.gameObject.SetActive(true);
      component3.gameObject.SetActive(true);
      component1.sprite = req.m_resItem.m_itemData.GetIcon();
      component1.color = Color.white;
      component4.m_text = Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name);
      component2.text = Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name);
      int num1 = player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name);
      int num2 = req.GetAmount(quality) * craftMultiplier;
      if (num2 <= 0)
      {
        InventoryGui.HideRequirement(elementRoot);
        return false;
      }
      component3.text = num2.ToString();
      if (num1 < num2 && (!craft && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost) || craft && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)))
        component3.color = (double) Mathf.Sin(Time.time * 10f) > 0.0 ? Color.red : Color.white;
      else
        component3.color = Color.white;
    }
    return true;
  }

  public static void HideRequirement(Transform elementRoot)
  {
    Image component1 = elementRoot.transform.Find("res_icon").GetComponent<Image>();
    TMP_Text component2 = elementRoot.transform.Find("res_name").GetComponent<TMP_Text>();
    TMP_Text component3 = elementRoot.transform.Find("res_amount").GetComponent<TMP_Text>();
    elementRoot.GetComponent<UITooltip>().m_text = "";
    component1.gameObject.SetActive(false);
    component2.gameObject.SetActive(false);
    component3.gameObject.SetActive(false);
  }

  private void SetupDragItem(ItemDrop.ItemData item, Inventory inventory, int amount)
  {
    if ((bool) (UnityEngine.Object) this.m_dragGo)
    {
      UnityEngine.Object.Destroy((UnityEngine.Object) this.m_dragGo);
      this.m_dragGo = (GameObject) null;
      this.m_dragItem = (ItemDrop.ItemData) null;
      this.m_dragInventory = (Inventory) null;
      this.m_dragAmount = 0;
    }
    if (item == null)
      return;
    this.m_dragGo = UnityEngine.Object.Instantiate<GameObject>(this.m_dragItemPrefab, this.transform);
    this.m_dragItem = item;
    this.m_dragInventory = inventory;
    this.m_dragAmount = amount;
    this.m_moveItemEffects.Create(this.transform.position, Quaternion.identity);
    UITooltip.HideTooltip();
  }

  public int ActiveGroup => this.m_activeGroup;
  public bool IsSkillsPanelOpen => this.m_skillsDialog.gameObject.activeInHierarchy;
  public bool IsTextPanelOpen => this.m_textsDialog.gameObject.activeInHierarchy;
  public bool IsTrophisPanelOpen => this.m_trophiesPanel.activeInHierarchy;
  public InventoryGrid ContainerGrid => this.m_containerGrid;
  public bool InUpradeTab() => !this.m_tabUpgrade.interactable;
  public bool InCraftTab() => !this.m_tabCraft.interactable;

  public enum SortMethod
  {
    Original,
    Name,
    Type,
    Weight,
    Count,
  }

  private struct RecipeDataPair(
    Recipe recipe,
    ItemDrop.ItemData data,
    GameObject element,
    bool canCraft)
  {
    public Recipe Recipe { get; private set; } = recipe;
    public ItemDrop.ItemData ItemData { get; private set; } = data;
    public GameObject InterfaceElement { get; private set; } = element;
    public bool CanCraft { get; private set; } = canCraft;
  }
}
```

---

## InventoryGrid

**Key insights for our UI Builder:**

- **`m_elementPrefab`** — the slot prefab instantiated for each grid cell. This is what our parser captures for grid cell structure. Each element contains: `icon` (Image), `amount` (TMP_Text), `quality` (TMP_Text), `equiped` (Image), `queued` (Image), `noteleport` (Image), `foodicon` (Image), `selected` (GameObject), `durability` (GuiBar), and a `UITooltip`.
- **`m_gridRoot`** (`RectTransform`) — the container all grid elements are parented to. Sized via `SetSizeWithCurrentAnchors` to `height * m_elementSpace`.
- **`m_elementSpace`** (10f) — grid spacing. Elements are positioned at `(x * elementSpace, y * -elementSpace)`. Our parser synthesizes a `GridLayoutGroup` equivalent from this.
- **Manual grid layout** — NOT using Unity's GridLayoutGroup! Elements are positioned via `anchoredPosition = offset + (x * spacing, y * -spacing)`. The offset centers the grid horizontally: `(containerWidth / 2) - (widgetSize.x / 2)`.
- **`m_width` / `m_height`** — grid dimensions from `m_inventory.GetWidth()` / `GetHeight()`. Grid is rebuilt (all elements destroyed and recreated) when dimensions change.
- **`Element` class** — private inner class holding references to all slot sub-components. Each element tracks its grid position (`m_pos`) and whether it's currently used (`m_used`). Unused elements are hidden.
- **`UpdateGui()`** — called every frame when visible. Iterates all inventory items, maps them to grid elements by position, and updates visual state (icon, amount, durability, equipped status, food type color, etc.). The "selected" child is only active for the gamepad-selected slot.
- **Gamepad navigation** — `UpdateGamepad()` handles D-pad/joystick movement through the grid. When moving past the top/bottom edge, it fires `OnMoveToUpperInventoryGrid` / `OnMoveToLowerInventoryGrid` events to switch between player/container grids. The `jumpToNextContainer` flag prevents rapid switching.
- **`GetHoveredElement()`** — iterates all elements and uses `InverseTransformPoint` + `rect.Contains()` to find the element under the mouse cursor. This is a linear search — no spatial index.
- **`DropItem()`** — handles drag-drop onto a grid slot. If the target slot has an item with a different name/quality, performs a swap (removes source, moves target to source position, then moves source to target).
- **`ResetView()`** — resets scroll position. If grid fits in viewport, centers it (`pivot.y = 0.5`); otherwise, pins to top (`pivot.y = 1`).
- **Food coloring** — slots show colored food icons based on the dominant stat: `m_foodEitrColor` (blue) if eitr dominates, `m_foodHealthColor` (red) if health dominates, `m_foodStaminaColor` (yellow) if stamina dominates, white if balanced.

```csharp
// Decompiled with JetBrains decompiler
// Type: InventoryGrid
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable disable
public class InventoryGrid : MonoBehaviour
{
  public Action<InventoryGrid, ItemDrop.ItemData, Vector2i, InventoryGrid.Modifier> m_onSelected;
  public Action<InventoryGrid, ItemDrop.ItemData, Vector2i> m_onRightClick;
  public RectTransform m_tooltipAnchor;
  public Action<Vector2i> OnMoveToUpperInventoryGrid;
  public Action<Vector2i> OnMoveToLowerInventoryGrid;
  public GameObject m_elementPrefab;
  public RectTransform m_gridRoot;
  public Scrollbar m_scrollbar;
  public UIGroupHandler m_uiGroup;
  public float m_elementSpace = 10f;
  private int m_width = 4;
  private int m_height = 4;
  private Vector2i m_selected = new Vector2i(0, 0);
  private Inventory m_inventory;
  private List<InventoryGrid.Element> m_elements = new List<InventoryGrid.Element>();
  private bool jumpToNextContainer;
  private readonly Color m_foodEitrColor = new Color(0.6f, 0.6f, 1f, 1f);
  private readonly Color m_foodHealthColor = new Color(1f, 0.5f, 0.5f, 1f);
  private readonly Color m_foodStaminaColor = new Color(1f, 1f, 0.5f, 1f);

  protected void Awake() { }

  public void ResetView()
  {
    RectTransform transform = this.transform as RectTransform;
    Rect rect = this.m_gridRoot.rect;
    double height1 = (double) rect.height;
    rect = transform.rect;
    double height2 = (double) rect.height;
    this.m_gridRoot.pivot = height1 <= height2 ? new Vector2(this.m_gridRoot.pivot.x, 0.5f) : new Vector2(this.m_gridRoot.pivot.x, 1f);
    this.m_gridRoot.anchoredPosition = new Vector2(0.0f, 0.0f);
  }

  public void UpdateInventory(Inventory inventory, Player player, ItemDrop.ItemData dragItem)
  {
    this.m_inventory = inventory;
    this.UpdateGamepad();
    this.UpdateGui(player, dragItem);
  }

  private void UpdateGui(Player player, ItemDrop.ItemData dragItem)
  {
    RectTransform transform = this.transform as RectTransform;
    int width = this.m_inventory.GetWidth();
    int height = this.m_inventory.GetHeight();
    if (this.m_selected.x >= width - 1)
      this.m_selected.x = width - 1;
    if (this.m_selected.y >= height - 1)
      this.m_selected.y = height - 1;
    if (this.m_width != width || this.m_height != height)
    {
      this.m_width = width;
      this.m_height = height;
      foreach (InventoryGrid.Element element in this.m_elements)
        UnityEngine.Object.Destroy((UnityEngine.Object) element.m_go);
      this.m_elements.Clear();
      Vector2 widgetSize = this.GetWidgetSize();
      Vector2 vector2_1 = new Vector2(transform.rect.width / 2f, 0.0f) - new Vector2(widgetSize.x, 0.0f) * 0.5f;
      for (int _y = 0; _y < height; ++_y)
      {
        for (int _x = 0; _x < width; ++_x)
        {
          Vector2 vector2_2 = (Vector2) new Vector3((float) _x * this.m_elementSpace, (float) _y * -this.m_elementSpace);
          GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(this.m_elementPrefab, (Transform) this.m_gridRoot);
          (gameObject.transform as RectTransform).anchoredPosition = vector2_1 + vector2_2;
          UIInputHandler componentInChildren = gameObject.GetComponentInChildren<UIInputHandler>();
          componentInChildren.m_onRightDown += new Action<UIInputHandler>(this.OnRightClick);
          componentInChildren.m_onLeftDown += new Action<UIInputHandler>(this.OnLeftClick);
          TMP_Text component = gameObject.transform.Find("binding").GetComponent<TMP_Text>();
          if ((bool) (UnityEngine.Object) player && _y == 0)
            component.text = (_x + 1).ToString();
          else
            component.enabled = false;
          this.m_elements.Add(new InventoryGrid.Element()
          {
            m_pos = new Vector2i(_x, _y),
            m_go = gameObject,
            m_icon = gameObject.transform.Find("icon").GetComponent<Image>(),
            m_amount = gameObject.transform.Find("amount").GetComponent<TMP_Text>(),
            m_quality = gameObject.transform.Find("quality").GetComponent<TMP_Text>(),
            m_equiped = gameObject.transform.Find("equiped").GetComponent<Image>(),
            m_queued = gameObject.transform.Find("queued").GetComponent<Image>(),
            m_noteleport = gameObject.transform.Find("noteleport").GetComponent<Image>(),
            m_food = gameObject.transform.Find("foodicon").GetComponent<Image>(),
            m_selected = gameObject.transform.Find("selected").gameObject,
            m_tooltip = gameObject.GetComponent<UITooltip>(),
            m_durability = gameObject.transform.Find("durability").GetComponent<GuiBar>()
          });
        }
      }
    }
    // ... item display update logic (sets icons, amounts, durability bars, etc.) ...
    this.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float) height * this.m_elementSpace);
  }

  public Vector2 GetWidgetSize()
  {
    return new Vector2((float) this.m_width * this.m_elementSpace, (float) this.m_height * this.m_elementSpace);
  }

  private InventoryGrid.Element GetElement(int x, int y, int width)
  {
    return this.m_elements[y * width + x];
  }

  private InventoryGrid.Element GetHoveredElement()
  {
    foreach (InventoryGrid.Element element in this.m_elements)
    {
      RectTransform transform = element.m_go.transform as RectTransform;
      if (transform.rect.Contains((Vector2) transform.InverseTransformPoint(ZInput.mousePosition)))
        return element;
    }
    return (InventoryGrid.Element) null;
  }

  public bool DropItem(Inventory fromInventory, ItemDrop.ItemData item, int amount, Vector2i pos)
  {
    ItemDrop.ItemData itemAt = this.m_inventory.GetItemAt(pos.x, pos.y);
    if (itemAt == item)
      return true;
    if (itemAt == null || !(itemAt.m_shared.m_name != item.m_shared.m_name) && (item.m_shared.m_maxQuality <= 1 || itemAt.m_quality == item.m_quality) && itemAt.m_shared.m_maxStackSize != 1 || item.m_stack != amount)
      return this.m_inventory.MoveItemToThis(fromInventory, item, amount, pos.x, pos.y);
    fromInventory.RemoveItem(item);
    fromInventory.MoveItemToThis(this.m_inventory, itemAt, itemAt.m_stack, item.m_gridPos.x, item.m_gridPos.y);
    this.m_inventory.MoveItemToThis(fromInventory, item, amount, pos.x, pos.y);
    return true;
  }

  public Inventory GetInventory() => this.m_inventory;
  public void SetSelection(Vector2i pos) => this.m_selected = pos;

  public ItemDrop.ItemData GetGamepadSelectedItem()
  {
    if (!this.m_uiGroup.IsActive)
      return (ItemDrop.ItemData) null;
    return this.m_inventory == null ? (ItemDrop.ItemData) null : this.m_inventory.GetItemAt(this.m_selected.x, this.m_selected.y);
  }

  public RectTransform GetGamepadSelectedElement()
  {
    if (!this.m_uiGroup.IsActive)
      return (RectTransform) null;
    return this.m_selected.x < 0 || this.m_selected.x >= this.m_width || this.m_selected.y < 0 || this.m_selected.y >= this.m_height ? (RectTransform) null : this.GetElement(this.m_selected.x, this.m_selected.y, this.m_width).m_go.transform as RectTransform;
  }

  internal int GridWidth => this.m_width;
  internal Vector2i SelectionGridPosition => this.m_selected;

  private class Element
  {
    public Vector2i m_pos;
    public GameObject m_go;
    public Image m_icon;
    public TMP_Text m_amount;
    public TMP_Text m_quality;
    public Image m_equiped;
    public Image m_queued;
    public GameObject m_selected;
    public Image m_noteleport;
    public Image m_food;
    public UITooltip m_tooltip;
    public GuiBar m_durability;
    public bool m_used;
  }

  public enum Modifier
  {
    Select,
    Split,
    Move,
    Drop,
  }
}
```

---

## Actionable Takeaways for UI Builder

### 1. InventoryGrid uses manual layout, not GridLayoutGroup
Elements are positioned via `anchoredPosition = offset + (col * spacing, row * -spacing)`. Our parser synthesizes a `UIGridLayoutGroupDef` with `cellSize` from the element prefab and `spacing` from `m_elementSpace` to create an equivalent layout.

### 2. Recipe list uses manual positioning too
Recipes in `m_recipeListRoot` are positioned at `y = index * -m_recipeListSpace`. Our parser can synthesize a VLG with spacing=30. The root is resized via `SetSizeWithCurrentAnchors` to fit all recipes.

### 3. Grid elements have a consistent child structure
Every slot prefab follows the pattern: `icon`, `amount`, `quality`, `equiped`, `queued`, `noteleport`, `foodicon`, `selected`, `durability`, `binding`. Our parser knows these child names for targeted capture.

### 4. Visibility check uses hidden frames, not animator state
`IsVisible()` checks `m_hiddenFrames <= 1`, not the animator bool directly. This accounts for the frame delay between the animator transition and actual visibility. Our UI hooks should use this pattern.

### 5. Container grid navigation coordinates with player grid
`MoveToLowerInventoryGrid` / `MoveToUpperInventoryGrid` handle the column offset calculation when grids have different widths. The offset is `ceil((playerWidth - containerWidth) / 2)`.

### 6. Item drag is a separate overlay system
The drag ghost is a separate instantiated prefab (`m_dragItemPrefab`) that follows mouse/gamepad position. It's parented to the InventoryGui transform, not any grid. Our parser ignores these transient drag objects.
