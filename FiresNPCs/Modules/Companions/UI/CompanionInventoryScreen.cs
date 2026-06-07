using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.Managers;
using FiresCore.Npc;
using FiresCore.Npc.Interactions;
using FiresCore.Npc.Movement;

namespace FiresRPGmaker.Modules.Companions
{
    /// <summary>
    /// UI Screen for managing companion equipment and storage inventory.
    /// Redesigned to match DressingRoomScreenController's professional look.
    /// 
    /// LAYOUT:
    /// - Header with companion name (smaller to show background border)
    /// - Tab bar for Equipment/Stats switching
 /// - Equipment slots in a T-shape with Helmet alone on top row
    /// - Storage grids (3x3) flanking the center equipment column
    /// - Footer buttons (smaller to show background border)
    /// 
    /// SLOT LAYOUT (Equipment Tab):
    ///     Row 0:        [Helmet]
    ///     Row 1: [L.Hand][L.Back][Shoulder][R.Back][R.Hand]
    ///     Row 2: [Storage 0-2]   [Chest]   [Storage 6-8]
    ///     Row 3: [Storage 3-5]   [Utility] [Storage 9-11]
    ///     Row 4:      [Legs]
    /// </summary>
    public class CompanionInventoryScreen : MonoBehaviour
    {
        public static CompanionInventoryScreen Instance { get; private set; }

        #region Constants

        private const string LAYOUT_UID = "companionsequipmentscreen";

        #endregion

        #region Constants - Colors

        private static readonly Color SlotEmptyColor = new Color(0.12f, 0.10f, 0.08f, 0.6f);
        private static readonly Color SlotFilledColor = new Color(0.18f, 0.14f, 0.10f, 0.8f);
        private static readonly Color SlotValidDropColor = new Color(0.2f, 0.5f, 0.2f, 0.9f);
        private static readonly Color SlotInvalidDropColor = new Color(0.5f, 0.2f, 0.2f, 0.9f);
        private static readonly Color TabActiveColor = new Color(0.25f, 0.20f, 0.15f, 0.9f);
        private static readonly Color TabInactiveColor = new Color(0.12f, 0.10f, 0.08f, 0.7f);
        private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f);

        #endregion

   #region Fields - UI References

  [Header("UI References")]
        public GameObject screenRoot;
        public TMP_Text instructionsText;
        public TMP_Text companionInfoText;
 public TMP_Text headerText;
        public Button closeButton;

        // Equipment slot UI elements
   public Dictionary<EquipmentSlot, EquipmentSlotUI> Slots => _slots;
     private Dictionary<EquipmentSlot, EquipmentSlotUI> _slots = new Dictionary<EquipmentSlot, EquipmentSlotUI>();

        // Storage slot UI elements
    private List<StorageSlotUI> _storageSlots = new List<StorageSlotUI>();

        // View mode and panels
        private enum ViewMode { Equipment, Stats }
        private ViewMode _currentViewMode = ViewMode.Equipment;
        private GameObject _equipmentPanel;
      private CompanionStatsScreen _statsScreen;
        private Button _equipmentTabButton;
        private Button _statsTabButton;

   // Button RectTransforms for manual hit testing
    private RectTransform _applyButtonRect;
        private RectTransform _clearAllButtonRect;
        private RectTransform _closeButtonRect;
   private RectTransform _headerRect;
        private RectTransform _equipmentTabRect;
        private RectTransform _statsTabRect;

        #endregion

   #region Fields - State

        private CompanionController _currentCompanion;
  private Player _currentPlayer;
      private Button _applyButton;
        private Button _clearAllButton;

      private Dictionary<EquipmentSlot, string> _pendingEquipment = new Dictionary<EquipmentSlot, string>();
        private Dictionary<EquipmentSlot, int> _pendingEquipmentQuality = new Dictionary<EquipmentSlot, int>();
        private ItemDrop.ItemData _lastDraggedItem;
  private EquipmentSlotUI _lastHoveredSlot;
        private StorageSlotUI _lastHoveredStorageSlot;

        private bool _isDragging;
 private Vector2 _dragOffset;

        private bool _isEditingName;
        private TMP_InputField _nameInputField;
 private GameObject _nameEditPanel;
        private RectTransform _headerTextRect;
        
        // Stat bar UI elements
        private Image _healthBarFill;
        private Image _staminaBarFill;
        private Image _eitrBarFill;
        private TMP_Text _healthText;
        private TMP_Text _staminaText;
        private TMP_Text _eitrText;
        private TMP_Text _levelText;
        
        // Food display
        private TMP_Text _foodDisplayText;
        private string _defaultInstructionsText = "Drag items from inventory.\nRight-click to clear.";
        private bool _isHoveringItem;
        
        // Tooltip popup panel
        private GameObject _tooltipPanel;
        private RectTransform _tooltipRect;
        private TMP_Text _tooltipTitle;
        private TMP_Text _tooltipText;
        private Image _tooltipBackground;
        private const float TOOLTIP_WIDTH = 280f;
        private const float TOOLTIP_MAX_HEIGHT = 350f;
        private const float TOOLTIP_PADDING = 8f;
        private const float TOOLTIP_OFFSET_X = 15f;
        private const float TOOLTIP_OFFSET_Y = -15f;

        #endregion

        #region Enums

        /// <summary>
  /// Equipment slot types matching Valheim's equipment system.
      /// </summary>
        public enum EquipmentSlot
        {
       Helmet,
      Chest,
    Legs,
            Shoulder,
            Utility,
      RightHand,
            LeftHand,
            RightBack,
LeftBack
      }

        #endregion

    #region Unity Lifecycle

  private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
         if (!IsVisible) return;

            // Escape to close
            if (Input.GetKeyDown(KeyCode.Escape))
            {
      HideInternal();
                return;
   }

    // Left click handling
            if (Input.GetMouseButtonDown(0))
      {
                HandleManualButtonClick();
            }

  // Right click to clear slots
     if (Input.GetMouseButtonDown(1))
     {
      HandleRightClick();
            }

          // Panel dragging via header
        HandlePanelDrag();

        // Drag visuals only in equipment mode
      if (_currentViewMode == ViewMode.Equipment)
      {
           UpdateDragVisuals();
           
           // Update hover tooltip for slots
           UpdateSlotTooltip();
 }

    // Update companion info display
     UpdateCompanionInfo();
     
     // Update food display
     UpdateFoodDisplay();
        }
        
        
        /// <summary>
        /// Updates item tooltip display when hovering over slots.
        /// Shows a floating tooltip panel near the mouse cursor, similar to Valheim's native tooltips.
        /// </summary>
        private void UpdateSlotTooltip()
        {
            Vector2 mousePos = Input.mousePosition;
            ItemDrop.ItemData hoveredItem = null;
            int hoveredItemQuality = 1;
            
            // Don't show tooltip if we're dragging an item
            var draggedItem = GetDraggedItemFromInventory();
            if (draggedItem != null)
            {
                HideTooltipPanel();
                _isHoveringItem = false;
                return;
            }
            
            // Check storage slots first
            foreach (var storageSlot in _storageSlots)
            {
                if (storageSlot.RectTransform != null && 
                    RectTransformUtility.RectangleContainsScreenPoint(storageSlot.RectTransform, mousePos, null))
                {
                    if (storageSlot.CurrentItem != null)
                    {
                        hoveredItem = storageSlot.CurrentItem;
                        hoveredItemQuality = storageSlot.CurrentItem.m_quality;
                    }
                    break;
                }
            }
            
            // Check equipment slots if no storage slot was hovered
            if (hoveredItem == null)
            {
                foreach (var kvp in _slots)
                {
                    var slot = kvp.Value;
                    if (slot.RectTransform != null && 
                        RectTransformUtility.RectangleContainsScreenPoint(slot.RectTransform, mousePos, null))
                    {
                        if (!string.IsNullOrEmpty(slot.CurrentPrefab))
                        {
                            // CRITICAL FIX: Get the actual equipped ItemData from the companion's inventory
                            // NOT from the prefab (which always has quality 1)!
                            // The companion's _equippedItems dictionary has the actual item with correct quality
                            if (_currentCompanion != null)
                            {
                                var compInv = _currentCompanion.GetInventory();
                                if (compInv != null)
                                {
                                    var compSlot = MapUISlotToCompanionSlot(slot.SlotType);
                                    var equippedItem = compInv.GetEquippedItem(compSlot);
                                    if (equippedItem != null)
                                    {
                                        // Use the actual equipped item - it has the correct quality
                                        hoveredItem = equippedItem;
                                        hoveredItemQuality = equippedItem.m_quality;
                                    }
                                    else
                                    {
                                        // Fallback: Get from prefab if equipped item not found
                                        // (shouldn't happen but just in case)
                                        var prefab = ZNetScene.instance?.GetPrefab(slot.CurrentPrefab);
                                        if (prefab != null)
                                        {
                                            var itemDrop = prefab.GetComponent<ItemDrop>();
                                            if (itemDrop?.m_itemData != null)
                                            {
                                                hoveredItem = itemDrop.m_itemData;
                                                hoveredItemQuality = compInv.GetEquipmentQualityPublic(compSlot);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // No companion - fall back to prefab data
                                var prefab = ZNetScene.instance?.GetPrefab(slot.CurrentPrefab);
                                if (prefab != null)
                                {
                                    var itemDrop = prefab.GetComponent<ItemDrop>();
                                    if (itemDrop?.m_itemData != null)
                                    {
                                        hoveredItem = itemDrop.m_itemData;
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }
            
            // Display floating tooltip panel near mouse
            if (hoveredItem != null)
            {
                ShowTooltipPanel(hoveredItem, hoveredItemQuality, mousePos);
                _isHoveringItem = true;
            }
            else if (_isHoveringItem)
            {
                HideTooltipPanel();
                _isHoveringItem = false;
            }
        }
        
        /// <summary>
        /// Creates the floating tooltip panel if it doesn't exist.
        /// </summary>
        private void CreateTooltipPanel()
        {
            if (_tooltipPanel != null) return;
            
            // Create tooltip panel as child of canvas (not screen root) so it can overflow panel bounds
            _tooltipPanel = new GameObject("TooltipPanel");
            _tooltipPanel.transform.SetParent(screenRoot.transform.parent, false);
            
            _tooltipRect = _tooltipPanel.AddComponent<RectTransform>();
            _tooltipRect.pivot = new Vector2(0, 1); // Top-left pivot for positioning
            
            // Background
            _tooltipBackground = _tooltipPanel.AddComponent<Image>();
            _tooltipBackground.color = new Color(0.08f, 0.06f, 0.04f, 0.95f);
            _tooltipBackground.raycastTarget = false;
            
            // Border effect using outline
            var outline = _tooltipPanel.AddComponent<Outline>();
            outline.effectColor = new Color(0.5f, 0.4f, 0.25f, 0.8f);
            outline.effectDistance = new Vector2(2, -2);
            
            // Add VerticalLayoutGroup for automatic sizing
            var layoutGroup = _tooltipPanel.AddComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(
                (int)TOOLTIP_PADDING, 
                (int)TOOLTIP_PADDING, 
                (int)TOOLTIP_PADDING, 
                (int)TOOLTIP_PADDING
            );
            layoutGroup.spacing = 4f;
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            
            // Add ContentSizeFitter for dynamic height
            var sizeFitter = _tooltipPanel.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            // Set fixed width
            _tooltipRect.sizeDelta = new Vector2(TOOLTIP_WIDTH, 0); // Height will be auto-calculated
            
            // Title text (item name) - child of panel for layout
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(_tooltipPanel.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            
            _tooltipTitle = UIBuilderHelper.AddTextMeshProUGUI(titleGO);
            _tooltipTitle.fontSize = 14;
            _tooltipTitle.fontStyle = FontStyles.Bold;
            _tooltipTitle.color = new Color(1f, 0.85f, 0.4f); // Gold
            _tooltipTitle.alignment = TextAlignmentOptions.TopLeft;
            _tooltipTitle.textWrappingMode = TextWrappingModes.Normal;
            _tooltipTitle.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(_tooltipTitle);
            
            // Add LayoutElement to title for proper sizing
            var titleLayout = titleGO.AddComponent<LayoutElement>();
            titleLayout.minHeight = 20f;
            titleLayout.preferredWidth = TOOLTIP_WIDTH - TOOLTIP_PADDING * 2;
            
            // Body text (tooltip details) - child of panel for layout
            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(_tooltipPanel.transform, false);
            var bodyRect = bodyGO.AddComponent<RectTransform>();
            
            _tooltipText = UIBuilderHelper.AddTextMeshProUGUI(bodyGO);
            _tooltipText.fontSize = 11;
            _tooltipText.color = new Color(0.9f, 0.85f, 0.75f);
            _tooltipText.alignment = TextAlignmentOptions.TopLeft;
            _tooltipText.textWrappingMode = TextWrappingModes.Normal;
            _tooltipText.overflowMode = TextOverflowModes.Overflow; // Allow text to determine size
            _tooltipText.raycastTarget = false;
            _tooltipText.lineSpacing = 15f;  // Increased line spacing for better readability
            UIBuilderHelper.ApplyValheimFont(_tooltipText);
            
            // Add LayoutElement to body for proper sizing
            var bodyLayout = bodyGO.AddComponent<LayoutElement>();
            bodyLayout.preferredWidth = TOOLTIP_WIDTH - TOOLTIP_PADDING * 2;
            bodyLayout.flexibleHeight = 1f; // Allow body to grow
            
            _tooltipPanel.SetActive(false);
        }
        
        /// <summary>
        /// Shows the tooltip panel with item information at the specified position.
        /// </summary>
        private void ShowTooltipPanel(ItemDrop.ItemData item, int quality, Vector2 mousePos)
        {
            if (item == null) return;
            
            // Create tooltip panel if needed
            CreateTooltipPanel();
            if (_tooltipPanel == null) return;
            
            // Get localized item name and tooltip
            string itemName = item.m_shared.m_name;
            string localizedName = Localization.instance?.Localize(itemName) ?? itemName;
            
            // Add quality indicator to name if quality > 1
            if (quality > 1)
            {
                localizedName = $"{localizedName} <color=#AAAAAA>[{quality}]</color>";
            }
            
            // Use Valheim's native GetTooltip() for full item details
            string tooltipText = item.GetTooltip();
            string localizedTooltip = Localization.instance?.Localize(tooltipText) ?? tooltipText;
            
            // Set text content
            _tooltipTitle.text = localizedName;
            _tooltipText.text = localizedTooltip;
            
            // Force layout rebuild to calculate sizes with new content
            _tooltipTitle.ForceMeshUpdate();
            _tooltipText.ForceMeshUpdate();
            
            // Force rebuild the layout to get accurate size
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tooltipRect);
            
            // Get the calculated height from the ContentSizeFitter
            float totalHeight = _tooltipRect.sizeDelta.y;
            
            // Clamp to max height if needed
            if (totalHeight > TOOLTIP_MAX_HEIGHT)
            {
                _tooltipRect.sizeDelta = new Vector2(TOOLTIP_WIDTH, TOOLTIP_MAX_HEIGHT);
                totalHeight = TOOLTIP_MAX_HEIGHT;
                
                // Enable truncation when clamped
                _tooltipText.overflowMode = TextOverflowModes.Ellipsis;
            }
            else
            {
                _tooltipText.overflowMode = TextOverflowModes.Overflow;
            }
            
            // Position tooltip near mouse, offset to bottom-right
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            
            float tooltipX = mousePos.x + TOOLTIP_OFFSET_X;
            float tooltipY = mousePos.y + TOOLTIP_OFFSET_Y;
            
            // Keep tooltip on screen horizontally
            if (tooltipX + TOOLTIP_WIDTH > screenWidth - 10)
            {
                tooltipX = mousePos.x - TOOLTIP_WIDTH - TOOLTIP_OFFSET_X;
            }
            
            // Keep tooltip on screen vertically (accounting for pivot at top)
            if (tooltipY - totalHeight < 10)
            {
                tooltipY = mousePos.y + totalHeight + Mathf.Abs(TOOLTIP_OFFSET_Y);
            }
            
            // Also check if tooltip goes off top of screen
            if (tooltipY > screenHeight - 10)
            {
                tooltipY = screenHeight - 10;
            }
            
            _tooltipRect.position = new Vector3(tooltipX, tooltipY, 0);
            
            // Show panel and bring to front
            _tooltipPanel.SetActive(true);
            _tooltipPanel.transform.SetAsLastSibling();
        }
        
        /// <summary>
        /// Hides the tooltip panel.
        /// </summary>
        private void HideTooltipPanel()
        {
            if (_tooltipPanel != null)
            {
                _tooltipPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// Maps UI equipment slot to CompanionInventory equipment slot.
        /// </summary>
        private CompanionInventory.EquipmentSlot MapUISlotToCompanionSlot(EquipmentSlot uiSlot)
        {
            return uiSlot switch
            {
                EquipmentSlot.Helmet => CompanionInventory.EquipmentSlot.Helmet,
                EquipmentSlot.Chest => CompanionInventory.EquipmentSlot.Chest,
                EquipmentSlot.Legs => CompanionInventory.EquipmentSlot.Legs,
                EquipmentSlot.Shoulder => CompanionInventory.EquipmentSlot.Shoulder,
                EquipmentSlot.Utility => CompanionInventory.EquipmentSlot.Utility,
                EquipmentSlot.RightHand => CompanionInventory.EquipmentSlot.RightHand,
                EquipmentSlot.LeftHand => CompanionInventory.EquipmentSlot.LeftHand,
                EquipmentSlot.RightBack => CompanionInventory.EquipmentSlot.RightBack,
                EquipmentSlot.LeftBack => CompanionInventory.EquipmentSlot.LeftBack,
                _ => CompanionInventory.EquipmentSlot.Helmet
            };
        }
        
        /// <summary>
        /// Updates the food display showing active food buffs and remaining time.
        /// Uses localized names and multi-line layout: first 2 foods on line 1, 3rd on line 2.
        /// </summary>
        private void UpdateFoodDisplay()
        {
            if (_foodDisplayText == null || _currentCompanion == null) return;

            var consumables = _currentCompanion.GetComponent<CompanionConsumables>();
            if (consumables == null)
            {
                _foodDisplayText.text = "";
                return;
            }

            var foodEffects = consumables.GetActiveFoodEffects();
            if (foodEffects == null || foodEffects.Count == 0)
            {
                _foodDisplayText.text = "<color=#888>No active food buffs</color>";
                return;
            }

            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < foodEffects.Count && i < 3; i++)
            {
                var effect = foodEffects[i];
                if (i > 0) sb.Append("\n");

                // Format time remaining as M:SS
                int minutes = Mathf.FloorToInt(effect.TimeRemaining / 60f);
                int seconds = Mathf.FloorToInt(effect.TimeRemaining % 60f);

                // Color code based on time remaining
                string timeColor = effect.TimeRemaining > 300f ? "#aaffaa" : (effect.TimeRemaining > 60f ? "#ffff88" : "#ff8888");

                // LOCALIZE the food name
                string localizedName = Localization.instance?.Localize(effect.FoodName) ?? effect.FoodName;
                if (localizedName.StartsWith("$"))
                    localizedName = localizedName.Substring(1).Replace("_", " ");

                // Build buff summary: +HP / +Sta / +Eitr
                var buffs = new System.Text.StringBuilder();
                if (effect.HealthBonus > 0)
                    buffs.Append($" <color=#ff8888>+{effect.HealthBonus:F0}HP</color>");
                if (effect.StaminaBonus > 0)
                    buffs.Append($" <color=#ffff88>+{effect.StaminaBonus:F0}Sta</color>");
                if (effect.EitrBonus > 0)
                    buffs.Append($" <color=#8888ff>+{effect.EitrBonus:F0}Eitr</color>");
                if (effect.HealthRegen > 0)
                    buffs.Append($" <color=#88ff88>+{effect.HealthRegen:F1}/t</color>");

                sb.Append($"<color={timeColor}>{localizedName}</color>{buffs} <color=#888>({minutes}:{seconds:D2})</color>");
            }

            _foodDisplayText.text = sb.ToString();
        }
        
        /// <summary>
        /// Gets a formatted tooltip string for an item.
        /// </summary>
        private string GetItemTooltipText(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return null;
            
            var sb = new System.Text.StringBuilder();
            
            // Item name with quality stars if applicable
            string name = Localization.instance?.Localize(item.m_shared.m_name) ?? item.m_shared.m_name;
            sb.AppendLine($"<color=yellow>{name}</color>");
            
            // Quality display (stars)
            if (item.m_quality > 1 && item.m_shared.m_maxQuality > 1)
            {
                sb.AppendLine($"Quality: {item.m_quality}/{item.m_shared.m_maxQuality}");
            }
            
            // Stack info
            if (item.m_shared.m_maxStackSize > 1)
            {
                sb.AppendLine($"Stack: {item.m_stack}/{item.m_shared.m_maxStackSize}");
            }
            
            // Description
            string desc = Localization.instance?.Localize(item.m_shared.m_description) ?? item.m_shared.m_description;
            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine($"<color=#888888>{desc}</color>");
            }
            
            // Weight
            float weight = item.GetWeight();
            sb.AppendLine($"Weight: {weight:F1}");
            
            // Durability
            if (item.m_shared.m_useDurability)
            {
                float durPercent = item.GetDurabilityPercentage() * 100f;
                sb.AppendLine($"Durability: {durPercent:F0}%");
            }
            
            // Armor
            if (item.m_shared.m_armor > 0)
            {
                float armor = item.GetArmor();
                sb.AppendLine($"Armor: {armor:F0}");
            }
            
            // Damage (for weapons)
            if (item.IsWeapon())
            {
                var damage = item.GetDamage();
                if (damage.m_damage > 0) sb.AppendLine($"Damage: {damage.m_damage:F0}");
                if (damage.m_slash > 0) sb.AppendLine($"Slash: {damage.m_slash:F0}");
                if (damage.m_pierce > 0) sb.AppendLine($"Pierce: {damage.m_pierce:F0}");
                if (damage.m_blunt > 0) sb.AppendLine($"Blunt: {damage.m_blunt:F0}");
                if (damage.m_fire > 0) sb.AppendLine($"Fire: {damage.m_fire:F0}");
                if (damage.m_frost > 0) sb.AppendLine($"Frost: {damage.m_frost:F0}");
                if (damage.m_lightning > 0) sb.AppendLine($"Lightning: {damage.m_lightning:F0}");
                if (damage.m_poison > 0) sb.AppendLine($"Poison: {damage.m_poison:F0}");
                if (damage.m_spirit > 0) sb.AppendLine($"Spirit: {damage.m_spirit:F0}");
            }
            
            // Block power (for shields)
            if (item.m_shared.m_blockPower > 0)
            {
                sb.AppendLine($"Block: {item.GetBlockPower(item.m_quality):F0}");
            }
            
            return sb.ToString();
        }

        #endregion

        #region Input Handling

        private void HandleManualButtonClick()
        {
     Vector2 mousePos = Input.mousePosition;

    // If editing name, check for click outside
            if (_isEditingName)
            {
     if (_nameEditPanel != null)
     {
              var panelRect = _nameEditPanel.GetComponent<RectTransform>();
       if (panelRect != null && !RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePos, null))
   {
       CancelNameEdit();
}
      }
                return;
      }

            // Check tab buttons
      if (_equipmentTabRect != null && RectTransformUtility.RectangleContainsScreenPoint(_equipmentTabRect, mousePos, null))
            {
       SwitchToEquipmentView();
           return;
      }

       if (_statsTabRect != null && RectTransformUtility.RectangleContainsScreenPoint(_statsTabRect, mousePos, null))
     {
 SwitchToStatsView();
     return;
          }

    // Header text for name editing
            if (_headerTextRect != null && RectTransformUtility.RectangleContainsScreenPoint(_headerTextRect, mousePos, null))
            {
    StartNameEdit();
       return;
            }

      // Apply button
          if (_applyButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_applyButtonRect, mousePos, null))
      {
                HandleApplyClicked();
   return;
  }

         // Clear All button
            if (_clearAllButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_clearAllButtonRect, mousePos, null))
     {
     HandleClearAllClicked();
   return;
            }

            // Close button
            if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
     {
             HideInternal();
         return;
   }
   
            // MANUAL SLOT CLICK HANDLING - InventoryGui blocks IPointerClickHandler
            // Check equipment slots for left-click
            if (_currentViewMode == ViewMode.Equipment)
            {
                HandleManualSlotClick(mousePos);
            }
        }
        
        /// <summary>
        /// Handles left-click on equipment and storage slots manually.
        /// This is needed because InventoryGui blocks Unity's event system.
        /// </summary>
        private void HandleManualSlotClick(Vector2 mousePos)
        {
            // Check if we're dragging from player inventory
            var draggedItem = GetDraggedItemFromInventory();
            
            // Check equipment slots
            foreach (var kvp in _slots)
            {
                var slot = kvp.Value;
                var hitRect = slot.ClickableRect ?? slot.RectTransform;
                if (hitRect == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(hitRect, mousePos, null))
                {
                    if (draggedItem != null)
                    {
                        // Dropping item from player inventory
                        if (IsValidItemForSlot(slot.SlotType, draggedItem))
                        {
                            string prefabName = draggedItem.m_dropPrefab?.name ?? draggedItem.m_shared?.m_name;
                            if (!string.IsNullOrEmpty(prefabName))
                            {
                                prefabName = prefabName.Replace("(Clone)", "").Trim();
                                
                                // Remove from player inventory
                                var player = Player.m_localPlayer;
                                if (player != null)
                                {
                                    player.GetInventory()?.RemoveItem(draggedItem, 1);
                                }
                                
                                // CRITICAL: Pass the item's quality so upgraded items retain their level
                                OnItemDropped(slot.SlotType, prefabName, draggedItem.m_quality);
                                ClearInventoryGuiDrag();
                                Debug.Log($"[CompanionInventoryScreen] Manual drop: {prefabName} to {slot.SlotType}");
                            }
                        }
                            else
                                {
                                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item doesn't fit in that slot");
                                }
                            }
                            else if (!string.IsNullOrEmpty(slot.CurrentPrefab))
                            {
                                // Left-click pickup: pick up the equipped item and start a Valheim drag
                                PickupEquipmentItemManual(slot);
                            }
                            return;
                        }
            }
            
            // Check storage slots
            foreach (var storageSlot in _storageSlots)
            {
                if (storageSlot.RectTransform == null) continue;
                
                if (RectTransformUtility.RectangleContainsScreenPoint(storageSlot.RectTransform, mousePos, null))
                {
                    if (draggedItem != null)
                    {
                        // Dropping item from player inventory
                        if (IsValidItemForStorageSlot(storageSlot, draggedItem))
                        {
                            // Clone the item to preserve all properties (quality, stack, etc.)
                            var itemClone = draggedItem.Clone();
                            
                            // Remove the ENTIRE item from player inventory (not just 1)
                            var player = Player.m_localPlayer;
                            if (player != null)
                            {
                                player.GetInventory()?.RemoveItem(draggedItem);
                            }
                            
                            OnStorageItemDropped(storageSlot, itemClone);
                            ClearInventoryGuiDrag();
                            Debug.Log($"[CompanionInventoryScreen] Manual storage drop to slot {storageSlot.SlotIndex}, stack: {itemClone.m_stack}, quality: {itemClone.m_quality}");
                        }
                        else
                        {
                            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item can't be stored here");
                        }
                    }
                    else if (storageSlot.CurrentItem != null)
                    {
                        // LEFT-CLICK PICKUP: No item being dragged, slot has item - pick it up!
                        // This matches vanilla behavior: single click picks up the item and starts dragging
                        PickupStorageItemManual(storageSlot);
                    }
                    return;
                }
            }
        }
        
        /// <summary>
        /// Picks up an item from a storage slot via manual click detection.
        /// Called from HandleManualSlotClick when InventoryGui blocks Unity events.
        /// Matches vanilla InventoryGui.OnSelectedItem behavior: single click picks up item.
        /// </summary>
        /// <summary>
        /// Picks up an item from an equipment slot via manual click detection.
        /// Creates a Valheim drag so the item can be dropped back into player inventory
        /// or into another slot.
        /// </summary>
        private void PickupEquipmentItemManual(EquipmentSlotUI slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.CurrentPrefab)) return;

            var itemPrefab = ObjectDB.instance?.GetItemPrefab(slot.CurrentPrefab);
            if (itemPrefab == null) return;
            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop?.m_itemData == null) return;

            var player = Player.m_localPlayer;
            if (player == null) return;
            var playerInv = player.GetInventory();
            if (playerInv == null) return;

            int storedQuality = _pendingEquipmentQuality.TryGetValue(slot.SlotType, out int q) ? q : 1;
            string prefabName = slot.CurrentPrefab;

            ClearSlotVisual(slot);
            _pendingEquipment.Remove(slot.SlotType);
            _pendingEquipmentQuality.Remove(slot.SlotType);
            ApplyEquipmentToCompanion();

            // Add the item into the player inventory so Valheim's drag system
            // has a real source inventory for MoveItemToThis / DropItem.
            var addedItem = playerInv.AddItem(prefabName, 1, storedQuality,
                itemDrop.m_itemData.m_variant, 0, "");
            if (addedItem == null)
            {
                // Inventory full ÃƒÂ¯Ã‚Â¿Ã‚Â½ drop on ground
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Inventory full!");
                DropItemOnGround(prefabName, storedQuality);
                return;
            }

            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                if (setupDragMethod != null && InventoryGui.instance != null)
                {
                    setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, 1 });
                    InventoryGui.instance.m_moveItemEffects?.Create(transform.position, Quaternion.identity);
                    Debug.Log($"[CompanionInventoryScreen] Manual pickup equipment: {prefabName} (quality {storedQuality})");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] Failed to pickup equipment: {ex.Message}");
            }
        }

        private void PickupStorageItemManual(StorageSlotUI slot)
        {
            if (slot?.CurrentItem == null) return;

            var player = Player.m_localPlayer;
            if (player == null) return;
            var playerInv = player.GetInventory();
            if (playerInv == null) return;

            var originalItem = slot.CurrentItem;
            string prefabName = originalItem.m_dropPrefab?.name ?? originalItem.m_shared?.m_name;
            if (string.IsNullOrEmpty(prefabName)) return;
            prefabName = prefabName.Replace("(Clone)", "").Trim();
            int stack = originalItem.m_stack;
            int quality = originalItem.m_quality;
            int variant = originalItem.m_variant;

            // Remove from companion storage
            if (_currentCompanion != null)
            {
                var compInv = _currentCompanion.GetInventory();
                compInv?.GetStorageInventory()?.RemoveItem(originalItem);
            }

            ClearStorageSlotVisual(slot);
            SaveCompanionData();

            // Add to player inventory using the overload that returns the ItemData reference
            var addedItem = playerInv.AddItem(prefabName, stack, quality, variant, 0, "");
            if (addedItem == null)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Inventory full!");
                if (_currentCompanion != null)
                {
                    var compInv = _currentCompanion.GetInventory();
                    compInv?.GetStorageInventory()?.AddItem(originalItem);
                    SetStorageSlotItem(slot, originalItem);
                    SaveCompanionData();
                }
                return;
            }

            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                if (setupDragMethod != null && InventoryGui.instance != null)
                {
                    setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, stack });
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] Failed to pickup item: {ex.Message}");
            }
        }

        private void HandleRightClick()
        {
  if (_currentViewMode != ViewMode.Equipment) return;
  
            Vector2 mousePos = Input.mousePosition;

            // Check equipment slots for right-click (clear slot)
            foreach (var kvp in _slots)
            {
                var slot = kvp.Value;
                var hitRect = slot.ClickableRect ?? slot.RectTransform;
                if (hitRect == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(hitRect, mousePos, null))
                {
                    if (!string.IsNullOrEmpty(slot.CurrentPrefab))
                    {
                        OnSlotCleared(slot.SlotType);
                        Debug.Log($"[CompanionInventoryScreen] Right-click cleared {slot.SlotType}");
                    }
                    return;
                }
            }
            
            // Check storage slots for right-click (clear slot)
            foreach (var storageSlot in _storageSlots)
            {
                if (storageSlot.RectTransform == null) continue;
                
                if (RectTransformUtility.RectangleContainsScreenPoint(storageSlot.RectTransform, mousePos, null))
                {
                    if (storageSlot.CurrentItem != null)
                    {
                        OnStorageSlotCleared(storageSlot);
                        Debug.Log($"[CompanionInventoryScreen] Right-click cleared storage slot {storageSlot.SlotIndex}");
                    }
                    return;
                }
            }
        }

        private void HandlePanelDrag()
   {
            if (_headerRect == null) return;

        Vector2 mousePos = Input.mousePosition;

       if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
           if (RectTransformUtility.RectangleContainsScreenPoint(_headerRect, mousePos, null))
   {
    if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
                 return;

     _isDragging = true;
    var panelRect = screenRoot?.GetComponent<RectTransform>();
        if (panelRect != null)
      {
         _dragOffset = (Vector2)panelRect.position - mousePos;
       }
                }
       }

            if (_isDragging && Input.GetMouseButton(0))
            {
      var panelRect = screenRoot?.GetComponent<RectTransform>();
      if (panelRect != null)
     {
 panelRect.position = mousePos + _dragOffset;
                }
   }

                 if (_isDragging && Input.GetMouseButtonUp(0))
          {
              _isDragging = false;
              SaveScreenPosition();
              }
              }

              private const string PREF_POS_X = "CompanionInvScreen_PosX";
              private const string PREF_POS_Y = "CompanionInvScreen_PosY";

              private void SaveScreenPosition()
              {
                  var panelRect = screenRoot?.GetComponent<RectTransform>();
                  if (panelRect == null) return;
                  PlayerPrefs.SetFloat(PREF_POS_X, panelRect.anchoredPosition.x);
                  PlayerPrefs.SetFloat(PREF_POS_Y, panelRect.anchoredPosition.y);
                  PlayerPrefs.Save();
              }

              private void LoadScreenPosition()
              {
                  if (!PlayerPrefs.HasKey(PREF_POS_X)) return;
                  var panelRect = screenRoot?.GetComponent<RectTransform>();
                  if (panelRect == null) return;
                  float x = PlayerPrefs.GetFloat(PREF_POS_X);
                  float y = PlayerPrefs.GetFloat(PREF_POS_Y);
                  panelRect.anchoredPosition = new Vector2(x, y);
              }

 private void UpdateDragVisuals()
        {
     var draggedItem = GetDraggedItemFromInventory();
   var slotUnderMouse = GetSlotUnderMouse();
            var storageSlotUnderMouse = GetStorageSlotUnderMouse();

            if (draggedItem != null)
            {
      UpdateSlotHoverVisuals(slotUnderMouse, storageSlotUnderMouse, draggedItem);
          _lastDraggedItem = draggedItem;
     _lastHoveredSlot = slotUnderMouse;
   _lastHoveredStorageSlot = storageSlotUnderMouse;
            }
            else if (_lastDraggedItem != null)
            {
    _lastDraggedItem = null;
     _lastHoveredSlot = null;
        _lastHoveredStorageSlot = null;
            ResetSlotColors();
     }
        }

        private void UpdateSlotHoverVisuals(EquipmentSlotUI slotUnderMouse, StorageSlotUI storageSlotUnderMouse, ItemDrop.ItemData draggedItem)
   {
        foreach (var kvp in _slots)
            {
        var slot = kvp.Value;
          if (slot == slotUnderMouse)
     {
   bool isValid = IsValidItemForSlot(slot.SlotType, draggedItem);
              if (slot.Background != null)
                  slot.Background.color = isValid ? SlotValidDropColor : SlotInvalidDropColor;
               }
               else
                  {
         if (slot.Background != null)
             slot.Background.color = string.IsNullOrEmpty(slot.CurrentPrefab) ? SlotEmptyColor : SlotFilledColor;
           }
           }

            foreach (var storageSlot in _storageSlots)
             {
          if (storageSlot == storageSlotUnderMouse)
                  {
                   bool isValid = IsValidItemForStorageSlot(storageSlot, draggedItem);
       if (storageSlot.Background != null)
           storageSlot.Background.color = isValid ? SlotValidDropColor : SlotInvalidDropColor;
         }
            else
                 {
            if (storageSlot.Background != null)
                storageSlot.Background.color = storageSlot.CurrentItem != null ? SlotFilledColor : SlotEmptyColor;
              }
            }
     }

   #endregion

  #region View Switching

 private void SwitchToEquipmentView()
        {
            _currentViewMode = ViewMode.Equipment;
            _equipmentPanel?.SetActive(true);
            _statsScreen?.Hide();
    UpdateTabVisuals();
        }

        private void SwitchToStatsView()
        {
            _currentViewMode = ViewMode.Stats;
     _equipmentPanel?.SetActive(false);
            _statsScreen?.Show(_currentCompanion);
        UpdateTabVisuals();
        }

        private void UpdateTabVisuals()
        {
       if (_equipmentTabButton != null)
     {
    var img = _equipmentTabButton.GetComponent<Image>();
    if (img != null) img.color = _currentViewMode == ViewMode.Equipment ? TabActiveColor : TabInactiveColor;
          }
         if (_statsTabButton != null)
   {
   var img = _statsTabButton.GetComponent<Image>();
                if (img != null) img.color = _currentViewMode == ViewMode.Stats ? TabActiveColor : TabInactiveColor;
            }
        }

        #endregion

        #region Static API

        public static void Show(CompanionController companion, Player player)
     {
     if (companion == null || player == null) return;

            if (Instance == null)
            {
         CreateScreen();
   }

            Instance?.ShowInternal(companion, player);
        }

        public static void Hide()
  {
 Instance?.HideInternal();
        }

        public static bool IsVisible => Instance?.screenRoot?.activeSelf ?? false;
        
        /// <summary>
        /// Alias for IsVisible for patch compatibility.
        /// </summary>
        public static bool IsOpen => IsVisible;
        
        /// <summary>
        /// Flag indicating if the screen is currently being closed (to prevent recursion).
        /// </summary>
        public static bool IsClosing { get; private set; }
        
        /// <summary>
        /// Returns true if the inventory screen is open for the specified companion.
        /// </summary>
        public static bool IsOpenForCompanion(CompanionController companion)
        {
            if (Instance == null || !IsVisible) return false;
            if (companion == null) return false;
            return Instance._currentCompanion == companion;
        }
        
        /// <summary>
        /// Closes the companion inventory screen.
        /// </summary>
        public static void Close()
        {
            if (Instance != null && !IsClosing)
            {
                IsClosing = true;
                try
                {
                    Hide();
                }
                finally
                {
                    IsClosing = false;
                }
            }
        }
        
        /// <summary>
        /// Refreshes the companion inventory display if the screen is open.
        /// </summary>
        public static void RefreshIfOpen()
        {
            if (Instance != null && IsVisible)
            {
                Instance.RefreshCompanionVisuals();
            }
        }

  #endregion

        #region Screen Creation ÃƒÂ¯Ã‚Â¿Ã‚Â½ SDK Layout

        private static void CreateScreen()
        {
            // Ensure the codex has scanned disk layouts
            UILayoutCodex.Init();

            var layout = UILayoutCodex.Get(LAYOUT_UID);
            if (layout == null)
            {
                // Layout may have been added to disk after the codex initialized.
                // Force a reload and try once more before giving up.
                Debug.Log($"[CompanionInventoryScreen] Layout '{LAYOUT_UID}' not in cache, forcing codex reload...");
                UILayoutCodex.Reload();
                layout = UILayoutCodex.Get(LAYOUT_UID);
            }
            if (layout == null)
            {
                Debug.LogError($"[CompanionInventoryScreen] Layout '{LAYOUT_UID}' not found in UILayoutCodex. " +
                    "Ensure companionsequipmentscreen.json is in Config/FiresRPGmaker/UILayouts/");
                return;
            }

            // Find or create canvas
            Canvas canvas = null;
            if (Hud.instance != null)
                canvas = Hud.instance.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("CompanionInventoryCanvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Instantiate the entire UI hierarchy from the layout JSON
            var root = UICanvasRenderer.Instantiate(layout, canvas.transform);
            if (root == null)
            {
                Debug.LogError("[CompanionInventoryScreen] UICanvasRenderer.Instantiate returned null");
                return;
            }

            // The Root element is a transparent wrapper ÃƒÂ¯Ã‚Â¿Ã‚Â½ disable its raycast so it
            // doesn't block clicks on the game world when the screen is hidden.
            var rootImage = root.GetComponent<Image>();
            if (rootImage != null)
                rootImage.raycastTarget = false;

            // The Root element is a wrapper ÃƒÂ¯Ã‚Â¿Ã‚Â½ find CompanionInventoryPanel inside it
            var panelTransform = root.transform.Find("CompanionInventoryPanel");
            if (panelTransform == null)
            {
                Debug.LogError("[CompanionInventoryScreen] Could not find CompanionInventoryPanel in instantiated layout");
                UnityEngine.Object.Destroy(root);
                return;
            }
            var panelGO = panelTransform.gameObject;

            // Add CanvasGroup for input blocking
            var canvasGroup = panelGO.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = panelGO.AddComponent<CanvasGroup>();
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;

            // Add the controller component
            var controller = panelGO.AddComponent<CompanionInventoryScreen>();
            controller.screenRoot = panelGO;

            // Wire all UI references from the instantiated hierarchy
            controller.WireUIReferences(panelGO);

            panelGO.SetActive(false);
            Debug.Log("[CompanionInventoryScreen] Created screen from SDK layout");
        }

        /// <summary>
        /// Finds all named elements in the instantiated layout hierarchy and wires
        /// them to the controller's fields. Adds runtime click handlers to equipment
        /// and storage slots.
        /// </summary>
        private void WireUIReferences(GameObject root)
        {
            // --- Header ---
            var headerGO = FindDeep(root.transform, "Header");
            _headerRect = headerGO?.GetComponent<RectTransform>();

            var headerTextGO = FindDeep(root.transform, "HeaderText");
            headerText = headerTextGO?.GetComponent<TMP_Text>();

            var headerTextContainerGO = FindDeep(root.transform, "HeaderTextContainer");
            _headerTextRect = headerTextContainerGO?.GetComponent<RectTransform>();

            var nameEditPanelGO = FindDeep(root.transform, "NameEditPanel");
            _nameEditPanel = nameEditPanelGO;
            if (nameEditPanelGO != null)
            {
                var nameInputGO = FindDeep(nameEditPanelGO.transform, "NameInput");
                _nameInputField = nameInputGO?.GetComponent<TMP_InputField>();
                if (_nameInputField != null)
                    _nameInputField.onEndEdit.AddListener(OnNameEditComplete);

                var confirmBtnGO = FindDeep(nameEditPanelGO.transform, "ConfirmButton");
                var confirmBtn = confirmBtnGO?.GetComponent<Button>();
                if (confirmBtn != null)
                    confirmBtn.onClick.AddListener(ConfirmNameEdit);

                nameEditPanelGO.SetActive(false);
            }

            var closeBtnGO = FindDeep(root.transform, "Btn_X");
            closeButton = closeBtnGO?.GetComponent<Button>();
            _closeButtonRect = closeBtnGO?.GetComponent<RectTransform>();

            // --- Tabs ---
            var equipTabGO = FindDeep(root.transform, "Tab_Equipment");
            _equipmentTabButton = equipTabGO?.GetComponent<Button>();
            _equipmentTabRect = equipTabGO?.GetComponent<RectTransform>();

            var statsTabGO = FindDeep(root.transform, "Tab_Stats");
            _statsTabButton = statsTabGO?.GetComponent<Button>();
            _statsTabRect = statsTabGO?.GetComponent<RectTransform>();

            UpdateTabVisuals();

            // --- Content Area ---
            var contentAreaGO = FindDeep(root.transform, "ContentArea");
            var equipmentPanelGO = FindDeep(root.transform, "EquipmentPanel");
            _equipmentPanel = equipmentPanelGO;

            // Stats screen (create fresh since it's a separate component)
            if (contentAreaGO != null)
            {
                var contentRect = contentAreaGO.GetComponent<RectTransform>();
                // Look for existing StatsScreen in the layout
                var statsScreenGO = FindDeep(contentAreaGO.transform, "StatsScreen");
                if (statsScreenGO != null)
                {
                    _statsScreen = CompanionStatsScreen.Create(contentAreaGO.transform, contentRect);
                }
                else
                {
                    _statsScreen = CompanionStatsScreen.Create(contentAreaGO.transform, contentRect);
                }
            }

            // --- Companion Info ---
            var companionInfoGO = FindDeep(root.transform, "CompanionInfo");
            companionInfoText = companionInfoGO?.GetComponent<TMP_Text>();

            // --- Stat Bars ---
            WireStatBar(root.transform, "HealthBar", out _healthBarFill, out _healthText);
            WireStatBar(root.transform, "StaminaBar", out _staminaBarFill, out _staminaText);
            WireStatBar(root.transform, "EitrBar", out _eitrBarFill, out _eitrText);

            var levelTextGO = FindDeep(root.transform, "LevelText");
            _levelText = levelTextGO?.GetComponent<TMP_Text>();

            // --- Food Display ---
            var foodDisplayGO = FindDeep(root.transform, "FoodDisplay");
            _foodDisplayText = foodDisplayGO?.GetComponent<TMP_Text>();
            if (foodDisplayGO != null)
            {
                // Push the food display down 20px so it doesn't overlap the grid
                var foodRect = foodDisplayGO.GetComponent<RectTransform>();
                foodRect.anchoredPosition += new Vector2(0f, -20f);

                // Add "Active Food Buffs" header just above the food text
                var headerLabelGO = new GameObject("FoodBuffsHeader");
                headerLabelGO.transform.SetParent(foodDisplayGO.transform.parent, false);
                var headerRect = headerLabelGO.AddComponent<RectTransform>();
                headerRect.anchorMin = foodRect.anchorMin;
                headerRect.anchorMax = new Vector2(foodRect.anchorMax.x, foodRect.anchorMin.y);
                headerRect.pivot = new Vector2(0.5f, 0f);
                headerRect.anchoredPosition = new Vector2(foodRect.anchoredPosition.x, foodRect.anchoredPosition.y + foodRect.sizeDelta.y + 2f);
                headerRect.sizeDelta = new Vector2(foodRect.sizeDelta.x, 14f);
                var headerTmp = headerLabelGO.AddComponent<TextMeshProUGUI>();
                headerTmp.text = "Active Food Buffs";
                headerTmp.fontSize = 10;
                headerTmp.color = new Color(0.9f, 0.75f, 0.45f, 0.8f);
                headerTmp.alignment = TextAlignmentOptions.Center;
                headerTmp.textWrappingMode = TextWrappingModes.NoWrap;
                headerTmp.raycastTarget = false;
                UIBuilderHelper.ApplyValheimFont(headerTmp);
                headerLabelGO.transform.SetSiblingIndex(foodDisplayGO.transform.GetSiblingIndex());
            }

            // --- Instructions ---
            var instructionsGO = FindDeep(root.transform, "Instructions");
            instructionsText = instructionsGO?.GetComponent<TMP_Text>();

            // --- Footer Buttons ---
            var clearAllGO = FindDeep(root.transform, "Btn_ClearAll");
            _clearAllButton = clearAllGO?.GetComponent<Button>();
            _clearAllButtonRect = clearAllGO?.GetComponent<RectTransform>();

            var applyGO = FindDeep(root.transform, "Btn_Apply&Save");
            _applyButton = applyGO?.GetComponent<Button>();
            _applyButtonRect = applyGO?.GetComponent<RectTransform>();

            // --- Equipment Slots ---
            WireEquipmentSlots(root.transform);

            // --- Storage Slots ---
            WireStorageSlots(root.transform);
        }

        /// <summary>
        /// Wires a stat bar's fill image and value text from the hierarchy.
        /// Expected structure: BarGO > Inner > Fill, BarGO > Value
        /// </summary>
        private void WireStatBar(Transform root, string barName, out Image fillImage, out TMP_Text valueText)
        {
            fillImage = null;
            valueText = null;

            var barGO = FindDeep(root, barName);
            if (barGO == null) return;

            var innerGO = FindDeep(barGO.transform, "Inner");
            if (innerGO != null)
            {
                var fillGO = FindDeep(innerGO.transform, "Fill");
                fillImage = fillGO?.GetComponent<Image>();
            }

            var valueGO = FindDeep(barGO.transform, "Value");
            valueText = valueGO?.GetComponent<TMP_Text>();
        }

        /// <summary>
        /// Finds all 9 equipment slots by name (Slot_Helmet, Slot_LeftHand, etc.)
        /// and wires them as EquipmentSlotUI with click handlers.
        /// </summary>
        private void WireEquipmentSlots(Transform root)
        {
            var slotMap = new Dictionary<string, EquipmentSlot>
            {
                { "Slot_Helmet", EquipmentSlot.Helmet },
                { "Slot_LeftHand", EquipmentSlot.LeftHand },
                { "Slot_LeftBack", EquipmentSlot.LeftBack },
                { "Slot_Shoulder", EquipmentSlot.Shoulder },
                { "Slot_RightBack", EquipmentSlot.RightBack },
                { "Slot_RightHand", EquipmentSlot.RightHand },
                { "Slot_Chest", EquipmentSlot.Chest },
                { "Slot_Utility", EquipmentSlot.Utility },
                { "Slot_Legs", EquipmentSlot.Legs }
            };

            foreach (var kvp in slotMap)
            {
                var slotGO = FindDeep(root, kvp.Key);
                if (slotGO == null)
                {
                    Debug.LogWarning($"[CompanionInventoryScreen] Equipment slot '{kvp.Key}' not found in layout");
                    continue;
                }

                var slotRect = slotGO.GetComponent<RectTransform>();
                var slotBg = slotGO.GetComponent<Image>();

                // Equipment slot label ("Helmet", "Chest", etc.) and item name
                var labelGO = FindDeep(slotGO.transform, "Label");
                var labelText = labelGO?.GetComponent<TextMeshProUGUI>();

                var itemNameGO = FindDeep(slotGO.transform, "ItemName");
                var itemNameText = itemNameGO?.GetComponent<TextMeshProUGUI>();
                if (itemNameGO != null) itemNameGO.SetActive(false);

                // Find the InventoryElement(Clone) child inside this slot
                // It contains all the Valheim item display children
                GameObject invElem = null;
                for (int i = 0; i < slotGO.transform.childCount; i++)
                {
                    var child = slotGO.transform.GetChild(i);
                    if (child.name.Contains("InventoryElement"))
                    {
                        invElem = child.gameObject;
                        break;
                    }
                }

                // Wire InventoryElement children ÃƒÂ¯Ã‚Â¿Ã‚Â½ same pattern as WireStorageSlots
                Image iconImage = null;
                TMP_Text amountText = null;
                TMP_Text qualityText = null;
                Image equippedImg = null;
                Image queuedImg = null;
                GameObject selectedGO = null;
                Image noTeleImg = null;
                Image foodImg = null;
                GameObject durGO = null;
                RectTransform durBarRect = null;

                if (invElem != null)
                {
                    iconImage = FindDeep(invElem.transform, "icon")?.GetComponent<Image>();
                    amountText = EnsureTMPText(FindDeep(invElem.transform, "amount"), 14, TextAlignmentOptions.BottomRight);
                    qualityText = EnsureTMPText(FindDeep(invElem.transform, "quality"), 10, TextAlignmentOptions.Center);
                    equippedImg = FindDeep(invElem.transform, "equiped")?.GetComponent<Image>();
                    queuedImg = FindDeep(invElem.transform, "queued")?.GetComponent<Image>();
                    selectedGO = FindDeep(invElem.transform, "selected");
                    noTeleImg = FindDeep(invElem.transform, "noteleport")?.GetComponent<Image>();

                    var foodGO = FindDeep(invElem.transform, "foodicon");
                    foodImg = foodGO?.GetComponent<Image>();
                    if (foodImg != null && foodImg.sprite == null)
                        foodImg.sprite = UICanvasRenderer.FindLoadedSpriteByName("food_icon");

                    durGO = FindDeep(invElem.transform, "durability");
                    if (durGO != null)
                    {
                        var barGO = FindDeep(durGO.transform, "bar");
                        durBarRect = barGO?.GetComponent<RectTransform>();
                    }

                    // Initial state: hide all dynamic children
                    if (iconImage != null) iconImage.enabled = false;
                    if (amountText != null) amountText.gameObject.SetActive(false);
                    if (qualityText != null) qualityText.gameObject.SetActive(false);
                    if (equippedImg != null) equippedImg.enabled = false;
                    if (queuedImg != null) queuedImg.enabled = false;
                    if (selectedGO != null) selectedGO.SetActive(false);
                    if (noTeleImg != null) noTeleImg.enabled = false;
                    if (foodImg != null) foodImg.enabled = false;
                    if (durGO != null) durGO.SetActive(false);
                }
                else
                {
                    // Fallback: use the parent slot's own Icon child
                    var iconGO = FindDeep(slotGO.transform, "Icon");
                    iconImage = iconGO?.GetComponent<Image>();
                }

                var slotUI = new EquipmentSlotUI
                {
                    SlotType = kvp.Value,
                    Root = slotGO,
                    RectTransform = slotRect,
                    ClickableRect = invElem != null ? invElem.GetComponent<RectTransform>() : slotRect,
                    Background = slotBg,
                    Label = labelText,
                    ItemName = itemNameText,
                    CurrentPrefab = null,
                    Icon = iconImage,
                    AmountText = amountText,
                    QualityText = qualityText,
                    EquippedOverlay = equippedImg,
                    QueuedOverlay = queuedImg,
                    SelectedFrame = selectedGO,
                    NoTeleportIcon = noTeleImg,
                    FoodIcon = foodImg,
                    DurabilityRoot = durGO,
                    DurabilityBarRect = durBarRect,
                };

                // Put click handler on the InventoryElement(Clone) that receives
                // pointer events. Keep the Button enabled ÃƒÂ¯Ã‚Â¿Ã‚Â½ disabling it breaks
                // the child rendering (color block, transitions). Our handler's
                // IPointerClickHandler fires alongside the Button without conflict.
                var clickTarget = invElem != null ? invElem : slotGO;

                var clickHandler = clickTarget.AddComponent<SlotClickHandler>();
                clickHandler.Initialize(this, slotUI);
                _slots[kvp.Value] = slotUI;
            }
        }

        /// <summary>
        /// Finds all storage slots in the layout. These are tagged with "inventory_slot"
        /// or have metadata "slot_template"="true". Looks in both the right-side Panel
        /// (inventory grid) and the bottom Panel Copy (food/ammo grid).
        /// </summary>
        private void WireStorageSlots(Transform root)
        {
            // Storage slots live inside "Panel" (inventory grid) and "Panel Copy" (food/ammo grid)
            // NOT inside the equipment slots. We must scope the search to avoid
            // wiring the InventoryElement(Clone) children inside Slot_Helmet etc.
            var slotGOs = new List<GameObject>();
            int storageSlotCount = 0;

            var inventoryGridGO = FindDeep(root, "Panel");
            if (inventoryGridGO != null)
            {
                var gridSlots = UICanvasRenderer.FindAllByTag(inventoryGridGO, "inventory_slot");
                if (gridSlots != null)
                {
                    slotGOs.AddRange(gridSlots);
                    storageSlotCount = gridSlots.Count;
                }
            }

            var foodAmmoGridGO = FindDeep(root, "Panel Copy");
            int foodAmmoStartIndex = slotGOs.Count;
            if (foodAmmoGridGO != null)
            {
                var foodSlots = UICanvasRenderer.FindAllByTag(foodAmmoGridGO, "inventory_slot");
                if (foodSlots != null) slotGOs.AddRange(foodSlots);
            }

            // Fallback: search by name if no tagged slots found in the grids
            if (slotGOs.Count == 0)
            {
                if (inventoryGridGO != null)
                    FindAllByNameContains(inventoryGridGO.transform, "InventoryElement", slotGOs);
                storageSlotCount = slotGOs.Count;
                foodAmmoStartIndex = slotGOs.Count;
                if (foodAmmoGridGO != null)
                    FindAllByNameContains(foodAmmoGridGO.transform, "InventoryElement", slotGOs);
            }

            // Panel Copy grid is 3 columns: first row (3 slots) = Food, remaining = Ammo
            int foodAmmoColumns = 3;

            int slotIndex = 0;
            foreach (var slotGO in slotGOs)
            {
                if (slotGO == null) continue;

                var slotRect = slotGO.GetComponent<RectTransform>();
                var slotBg = slotGO.GetComponent<Image>();

                // --- icon (Image, ComponentEnabled=false at rest) ---
                var iconGO = FindDeep(slotGO.transform, "icon");
                var iconImage = iconGO?.GetComponent<Image>();

                // --- amount (Panel?swap to TMP_Text for stack count display) ---
                var amountText = EnsureTMPText(FindDeep(slotGO.transform, "amount"), 14,
                    TextAlignmentOptions.BottomRight);

                // --- quality (Panel?swap to TMP_Text for quality star display) ---
                var qualityText = EnsureTMPText(FindDeep(slotGO.transform, "quality"), 10,
                    TextAlignmentOptions.Center);

                // --- equiped overlay (Image) ---
                var equippedGO = FindDeep(slotGO.transform, "equiped");
                var equippedImg = equippedGO?.GetComponent<Image>();

                // --- queued overlay (Image) ---
                var queuedGO = FindDeep(slotGO.transform, "queued");
                var queuedImg = queuedGO?.GetComponent<Image>();

                // --- selected frame (GameObject, toggled via SetActive) ---
                var selectedGO = FindDeep(slotGO.transform, "selected");

                // --- noteleport icon (Image) ---
                var noTeleGO = FindDeep(slotGO.transform, "noteleport");
                var noTeleImg = noTeleGO?.GetComponent<Image>();

                // --- foodicon (Image ÃƒÂ¯Ã‚Â¿Ã‚Â½ vanilla sprite, resolve by name if build missed it) ---
                var foodGO = FindDeep(slotGO.transform, "foodicon");
                var foodImg = foodGO?.GetComponent<Image>();
                if (foodImg != null && foodImg.sprite == null)
                {
                    foodImg.sprite = UICanvasRenderer.FindLoadedSpriteByName("food_icon");
                }

                // --- durability bar (Panel > bar child) ---
                var durGO = FindDeep(slotGO.transform, "durability");
                RectTransform durBarRect = null;
                if (durGO != null)
                {
                    var barGO = FindDeep(durGO.transform, "bar");
                    durBarRect = barGO?.GetComponent<RectTransform>();
                }

                // --- initial state: hide dynamic children ---
                if (iconImage != null) iconImage.enabled = false;
                if (amountText != null) amountText.gameObject.SetActive(false);
                if (qualityText != null) qualityText.gameObject.SetActive(false);
                if (equippedImg != null) equippedImg.enabled = false;
                if (queuedImg != null) queuedImg.enabled = false;
                if (selectedGO != null) selectedGO.SetActive(false);
                if (noTeleImg != null) noTeleImg.enabled = false;
                if (foodImg != null) foodImg.enabled = false;
                if (durGO != null) durGO.SetActive(false);

                // Determine slot restriction for food/ammo grid
                StorageSlotRestriction restriction = StorageSlotRestriction.None;
                if (slotIndex >= foodAmmoStartIndex)
                {
                    int indexInGrid = slotIndex - foodAmmoStartIndex;
                    restriction = indexInGrid < foodAmmoColumns
                        ? StorageSlotRestriction.Food
                        : StorageSlotRestriction.Ammo;
                }

                var storageSlot = new StorageSlotUI
                {
                    SlotIndex = slotIndex,
                    Restriction = restriction,
                    Root = slotGO,
                    RectTransform = slotRect,
                    Background = slotBg,
                    Icon = iconImage,
                    AmountText = amountText,
                    QualityText = qualityText,
                    EquippedOverlay = equippedImg,
                    QueuedOverlay = queuedImg,
                    SelectedFrame = selectedGO,
                    NoTeleportIcon = noTeleImg,
                    FoodIcon = foodImg,
                    DurabilityRoot = durGO,
                    DurabilityBarRect = durBarRect,
                    CurrentItem = null
                };

                var clickHandler = slotGO.AddComponent<StorageSlotClickHandler>();
                clickHandler.Initialize(this, storageSlot);
                _storageSlots.Add(storageSlot);

                // Add Food/Ammo hint labels to Panel Copy slots
                if (slotIndex >= foodAmmoStartIndex)
                {
                    int indexInGrid = slotIndex - foodAmmoStartIndex;
                    bool isFood = indexInGrid < foodAmmoColumns;
                    string hintText = isFood ? "Food" : "Ammo";
                    AddSlotHintLabel(slotGO, hintText);
                }

                slotIndex++;
            }

            Debug.Log($"[CompanionInventoryScreen] Wired {_storageSlots.Count} storage slots");
        }

        /// <summary>
        /// Adds a small hint label at the bottom of a slot, matching the style
        /// of equipment slot labels ("Helmet", "Chest", etc.).
        /// </summary>
        private static void AddSlotHintLabel(GameObject slotGO, string text)
        {
            var labelGO = new GameObject("HintLabel");
            labelGO.transform.SetParent(slotGO.transform, false);
            var rect = labelGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0.18f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 8;
            tmp.color = new Color(0.9f, 0.75f, 0.45f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(tmp);
        }

        /// <summary>
        /// Ensures a child GO has a TMP_Text component. The UI builder creates
        /// amount/quality/binding as Panel (which adds an Image). Since Unity
        /// only allows one Graphic per GO, we destroy the Image first, then add
        /// TMP_Text. If TMP_Text already exists we just return it.
        /// </summary>
        private static TMP_Text EnsureTMPText(GameObject go, float fontSize, TextAlignmentOptions alignment)
        {
            if (go == null) return null;

            var existing = go.GetComponent<TMP_Text>();
            if (existing != null) return existing;

            // Remove the Image that BuildPanel added ÃƒÂ¯Ã‚Â¿Ã‚Â½ frees the Graphic slot
            var img = go.GetComponent<Image>();
            if (img != null) DestroyImmediate(img);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(tmp);
            return tmp;
        }

        /// <summary>
        /// Recursive deep search for a child by exact name. 
        /// Searches the entire hierarchy depth-first.
        /// </summary>
        private static GameObject FindDeep(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child.gameObject;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Finds all GameObjects whose name contains the given substring.
        /// </summary>
        private static void FindAllByNameContains(Transform parent, string substring, List<GameObject> results)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.Contains(substring))
                    results.Add(child.gameObject);
                FindAllByNameContains(child, substring, results);
            }
        }

        #endregion

        #region Show/Hide

        private void ShowInternal(CompanionController companion, Player player)
        {
            _currentCompanion = companion;
            _currentPlayer = player;
            _pendingEquipment.Clear();
            _pendingEquipmentQuality.Clear();
            _lastDraggedItem = null;
            _lastHoveredSlot = null;
            _lastHoveredStorageSlot = null;
            _isDragging = false;
            _currentViewMode = ViewMode.Equipment;
            
            // Ensure stats are recalculated with latest food bonuses before showing
            companion.GetStats()?.RecalculateMaxStats();

            if (headerText != null)
            {
                headerText.text = $"{companion.GetDisplayName()}";
            }

            // CRITICAL: Lock companion movement while UI is open
            LockCompanionForInteraction(companion, player);

            LoadEquipmentFromCompanion(companion);
            LoadStorageFromCompanion(companion);

            SwitchToEquipmentView();

            screenRoot.SetActive(true);
            screenRoot.transform.SetAsLastSibling();
            LoadScreenPosition();
            OpenPlayerInventory();

            GUIManager.BlockInput(true);

            Debug.Log($"[CompanionInventoryScreen] Opened for companion: {companion?.GetDisplayName()}");
        }

        private void HideInternal()
        {
            CancelNameEdit();
            
            // Hide tooltip panel
            HideTooltipPanel();

            // CRITICAL: Unlock companion movement when UI closes
            UnlockCompanionFromInteraction();

            screenRoot?.SetActive(false);
            
            // Clear references after unlocking
            _currentCompanion = null;
            _currentPlayer = null;
            _pendingEquipment.Clear();
            _pendingEquipmentQuality.Clear();
            _lastDraggedItem = null;
            _lastHoveredSlot = null;
            _lastHoveredStorageSlot = null;
            _isDragging = false;

            foreach (var slot in _slots.Values)
            {
                ClearSlotVisual(slot);
            }

            foreach (var storageSlot in _storageSlots)
            {
                ClearStorageSlotVisual(storageSlot);
            }

            _statsScreen?.Hide();

            ClosePlayerInventory();
            GUIManager.BlockInput(false);

            Debug.Log("[CompanionInventoryScreen] Hidden");
        }

        private void OpenPlayerInventory()
        {
 try
   {
  if (InventoryGui.instance != null && !InventoryGui.IsVisible())
    {
         InventoryGui.instance.Show(null);
       }
       }
 catch (Exception ex)
            {
         Debug.LogWarning($"[CompanionInventoryScreen] Could not open inventory: {ex.Message}");
    }
        }

        private void ClosePlayerInventory()
        {
            try
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] Could not close inventory: {ex.Message}");
            }
        }

        /// <summary>
        /// Locks the companion in place for UI interaction.
        /// Stops movement, faces the player, and locks all AI movement.
        /// CRITICAL: Uses CompanionStateController for authoritative freeze that blocks movement at Harmony level.
        /// </summary>
        private void LockCompanionForInteraction(CompanionController companion, Player player)
        {
            if (companion == null || player == null) return;

            try
            {
                // CRITICAL: Enter UIInteraction state in state controller FIRST
                // This is the authoritative freeze - Harmony patches will block ALL movement
                var stateController = companion.GetStateController();
                if (stateController != null)
                {
                    stateController.TryEnterState(CompanionStateController.CompanionState.UIInteraction, 999f, "InventoryScreen");
                }
                
                // Zero rigidbody velocity - only if NOT kinematic (Unity 6 doesn't allow setting velocity on kinematic bodies)
                var rigidbody = companion.GetComponent<Rigidbody>();
                if (rigidbody != null && !rigidbody.isKinematic)
                {
                    rigidbody.linearVelocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }

                // Cancel any active idle behaviors
                var idleBehavior = companion.GetComponent<CompanionIdleBehavior>();
                if (idleBehavior != null)
                {
                    idleBehavior.FreezeForInteraction();
                }

                // Force detach if attached to something (sitting)
                var interactionBehavior = companion.GetComponent<CompanionInteractionBehavior>();
                interactionBehavior?.ForceDetach();

                // Clear any command/idle destination
                var companionAI = companion.GetCompanionAI();
                companionAI?.ClearIdleDestination();
                companionAI?.ClearCommandDestination();

                // Lock movement via CompanionCombatMovement as backup
                var combatMovement = companion.GetComponent<CompanionCombatMovement>();
                if (combatMovement != null)
                {
                    combatMovement.LockMovement("UIInteraction", 999f);
                }

                // Face the player
                Vector3 toPlayer = (player.transform.position - companion.transform.position);
                toPlayer.y = 0;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    companion.transform.rotation = Quaternion.LookRotation(toPlayer);
                }

                Debug.Log($"[CompanionInventoryScreen] Locked {companion.companionName} for UI interaction via StateController");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] LockCompanionForInteraction error: {ex.Message}");
            }
        }

        /// <summary>
        /// Unlocks the companion after UI interaction is complete.
        /// CRITICAL: Exits state controller UIInteraction state to resume normal movement.
        /// </summary>
        private void UnlockCompanionFromInteraction()
        {
            if (_currentCompanion == null) return;

            try
            {
                // CRITICAL: Exit UIInteraction state in state controller
                // This allows the Harmony patches to let movement through again
                var stateController = _currentCompanion.GetStateController();
                if (stateController != null)
                {
                    stateController.ExitState(CompanionStateController.CompanionState.Idle);
                }
                
                // Also unfreeze via idle behavior
                var idleBehavior = _currentCompanion.GetComponent<CompanionIdleBehavior>();
                if (idleBehavior != null)
                {
                    idleBehavior.UnfreezeAfterInteraction();
                }
                
                // Unlock movement via combat movement as backup
                var combatMovement = _currentCompanion.GetComponent<CompanionCombatMovement>();
                if (combatMovement != null)
                {
                    combatMovement.UnlockMovement();
                }

                Debug.Log($"[CompanionInventoryScreen] Unlocked {_currentCompanion.companionName} from UI interaction via StateController");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] UnlockCompanionFromInteraction error: {ex.Message}");
            }
        }

        #endregion

   #region Inventory Drag Integration

        private ItemDrop.ItemData GetDraggedItemFromInventory()
        {
  try
       {
if (InventoryGui.instance == null) return null;

         var dragItemField = typeof(InventoryGui).GetField("m_dragItem",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (dragItemField != null)
                {
               return dragItemField.GetValue(InventoryGui.instance) as ItemDrop.ItemData;
      }
       }
            catch { }
            return null;
}

        /// <summary>
        /// Clears the current drag item from InventoryGui.
        /// This is CRITICAL for proper left-click handling - without clearing,
        /// the player cannot interact with other items until closing inventory.
        /// </summary>
        private void ClearInventoryGuiDrag()
        {
            try
            {
                if (InventoryGui.instance == null) return;
                
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                
                // Set m_dragItem to null
                var dragItemField = typeof(InventoryGui).GetField("m_dragItem", flags);
                if (dragItemField != null)
                {
                    dragItemField.SetValue(InventoryGui.instance, null);
                }
                
                // Set m_dragInventory to null
                var dragInventoryField = typeof(InventoryGui).GetField("m_dragInventory", flags);
                if (dragInventoryField != null)
                {
                    dragInventoryField.SetValue(InventoryGui.instance, null);
                }
                
                // Set m_dragAmount to 0
                var dragAmountField = typeof(InventoryGui).GetField("m_dragAmount", flags);
                if (dragAmountField != null)
                {
                    dragAmountField.SetValue(InventoryGui.instance, 0);
                }
                
                // Hide the drag icon - CRITICAL: must hide the visual drag indicator
                var dragIconField = typeof(InventoryGui).GetField("m_dragGo", flags);
                if (dragIconField != null)
                {
                    var dragGo = dragIconField.GetValue(InventoryGui.instance) as GameObject;
                    if (dragGo != null)
                    {
                        dragGo.SetActive(false);
                    }
                }
                
                // CRITICAL: Also clear the split panel if it was open
                var splitPanelField = typeof(InventoryGui).GetField("m_splitPanel", flags);
                if (splitPanelField != null)
                {
                    var splitPanel = splitPanelField.GetValue(InventoryGui.instance) as GameObject;
                    if (splitPanel != null && splitPanel.activeSelf)
                    {
                        splitPanel.SetActive(false);
                    }
                }
                
                // CRITICAL: Clear any selected item state
                var selectedItemField = typeof(InventoryGui).GetField("m_selectedItem", flags);
                if (selectedItemField != null)
                {
                    selectedItemField.SetValue(InventoryGui.instance, null);
                }
                
                // Update the inventory grid to refresh slot visuals
                InventoryGui.instance.m_playerGrid?.UpdateInventory(
                    Player.m_localPlayer?.GetInventory(), 
                    Player.m_localPlayer, 
                    null);
                
                // Force cursor update by calling UpdateCursor via reflection
                var updateCursorMethod = typeof(InventoryGui).GetMethod("UpdateCursor", flags);
                if (updateCursorMethod != null)
                {
                    updateCursorMethod.Invoke(InventoryGui.instance, new object[] { Time.deltaTime });
                }
                
                // Also call SetupDragItem with null to properly clear internal state
                var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                if (setupDragMethod != null)
                {
                    try
                    {
                        setupDragMethod.Invoke(InventoryGui.instance, new object[] { null, null, 0 });
                    }
                    catch { /* Some versions may have different signatures */ }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] ClearInventoryGuiDrag error: {ex.Message}");
            }
        }

        public EquipmentSlotUI GetSlotUnderMouse()
        {
            Vector2 mousePos = Input.mousePosition;

            foreach (var kvp in _slots)
            {
                var slot = kvp.Value;
                var hitRect = slot.ClickableRect ?? slot.RectTransform;
                if (hitRect == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(hitRect, mousePos, null))
                {
                    return slot;
                }
            }

            return null;
        }

        public StorageSlotUI GetStorageSlotUnderMouse()
        {
 Vector2 mousePos = Input.mousePosition;

          foreach (var storageSlot in _storageSlots)
    {
         if (storageSlot.RectTransform == null) continue;

     if (RectTransformUtility.RectangleContainsScreenPoint(storageSlot.RectTransform, mousePos, null))
     {
      return storageSlot;
                }
            }

          return null;
      }

        private void ResetSlotColors()
        {
  foreach (var slot in _slots.Values)
     {
    if (slot.Background != null)
        slot.Background.color = string.IsNullOrEmpty(slot.CurrentPrefab) ? SlotEmptyColor : SlotFilledColor;
     }

   foreach (var storageSlot in _storageSlots)
          {
       if (storageSlot.Background != null)
           storageSlot.Background.color = storageSlot.CurrentItem != null ? SlotFilledColor : SlotEmptyColor;
            }
        }

    #endregion

        #region Storage Management

        private void LoadStorageFromCompanion(CompanionController companion)
        {
            if (companion == null) return;

            foreach (var storageSlot in _storageSlots)
            {
                ClearStorageSlotVisual(storageSlot);
            }

            var inventory = companion.GetInventory();
            if (inventory == null) return;

            var storageInventory = inventory.GetStorageInventory();
            if (storageInventory == null) return;

            var items = storageInventory.GetAllItems();

            // Separate items by type so they go into the correct restricted slots
            var foodItems = new Queue<ItemDrop.ItemData>();
            var ammoItems = new Queue<ItemDrop.ItemData>();
            var generalItems = new Queue<ItemDrop.ItemData>();

            foreach (var item in items)
            {
                if (item.m_shared != null && (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0))
                    foodItems.Enqueue(item);
                else if (item.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Ammo)
                    ammoItems.Enqueue(item);
                else
                    generalItems.Enqueue(item);
            }

            foreach (var slot in _storageSlots)
            {
                ItemDrop.ItemData itemToPlace = null;
                switch (slot.Restriction)
                {
                    case StorageSlotRestriction.Food:
                        if (foodItems.Count > 0) itemToPlace = foodItems.Dequeue();
                        break;
                    case StorageSlotRestriction.Ammo:
                        if (ammoItems.Count > 0) itemToPlace = ammoItems.Dequeue();
                        break;
                    default:
                        if (generalItems.Count > 0) itemToPlace = generalItems.Dequeue();
                        else if (foodItems.Count > 0) itemToPlace = foodItems.Dequeue();
                        else if (ammoItems.Count > 0) itemToPlace = ammoItems.Dequeue();
                        break;
                }
                if (itemToPlace != null)
                    SetStorageSlotItem(slot, itemToPlace);
            }
        }

      public void SetStorageSlotItem(StorageSlotUI slot, ItemDrop.ItemData item)
      {
          if (slot == null) return;

          slot.CurrentItem = item;

          if (item == null)
          {
              ClearStorageSlotVisual(slot);
              return;
          }

          // Icon
          if (slot.Icon != null)
          {
              if (item.m_shared?.m_icons != null && item.m_shared.m_icons.Length > 0)
              {
                  slot.Icon.sprite = item.m_shared.m_icons[0];
                  slot.Icon.color = Color.white;
              }
              slot.Icon.enabled = item.m_shared?.m_icons != null && item.m_shared.m_icons.Length > 0;
          }

          // Stack amount
          if (slot.AmountText != null)
          {
              bool show = item.m_stack > 1;
              slot.AmountText.gameObject.SetActive(show);
              if (show) slot.AmountText.text = item.m_stack.ToString();
          }

          // Quality
          if (slot.QualityText != null)
          {
              bool show = item.m_quality > 1;
              slot.QualityText.gameObject.SetActive(show);
              if (show) slot.QualityText.text = item.m_quality.ToString();
          }

          // Equipped overlay
          if (slot.EquippedOverlay != null)
              slot.EquippedOverlay.enabled = item.m_equipped;

          // Queued overlay (not used in companion storage, keep off)
          if (slot.QueuedOverlay != null)
              slot.QueuedOverlay.enabled = false;

          // No-teleport icon
          if (slot.NoTeleportIcon != null)
              slot.NoTeleportIcon.enabled = (item.m_shared?.m_teleportable == false);

          // Food icon with stat-type coloring
          if (slot.FoodIcon != null)
          {
              bool isFood = item.m_shared != null &&
                  (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0);
              slot.FoodIcon.enabled = isFood;
              if (isFood)
              {
                  if (item.m_shared.m_foodEitr > 0)
                      slot.FoodIcon.color = new Color(0.6f, 0.6f, 1f, 1f);
                  else if (item.m_shared.m_food > item.m_shared.m_foodStamina)
                      slot.FoodIcon.color = new Color(1f, 0.5f, 0.5f, 1f);
                  else
                      slot.FoodIcon.color = new Color(1f, 1f, 0.5f, 1f);
              }
          }

          // Durability bar
          if (slot.DurabilityRoot != null)
          {
              float maxDur = item.GetMaxDurability();
              bool hasDur = maxDur > 0 && item.m_shared?.m_useDurability == true;
              slot.DurabilityRoot.SetActive(hasDur);
              if (hasDur && slot.DurabilityBarRect != null)
              {
                  float ratio = Mathf.Clamp01(item.m_durability / maxDur);
                  var parentRT = slot.DurabilityRoot.GetComponent<RectTransform>();
                  float fullW = parentRT != null ? parentRT.sizeDelta.x : 54f;
                  slot.DurabilityBarRect.sizeDelta = new Vector2(fullW * ratio, slot.DurabilityBarRect.sizeDelta.y);

                  var barImg = slot.DurabilityBarRect.GetComponent<Image>();
                  if (barImg != null)
                  {
                      barImg.enabled = true;
                      if (ratio > 0.75f) barImg.color = new Color(0.94f, 0.94f, 0.94f, 1f);
                      else if (ratio > 0.50f) barImg.color = new Color(1f, 0.8f, 0.2f, 1f);
                      else if (ratio > 0.25f) barImg.color = new Color(1f, 0.5f, 0.1f, 1f);
                      else barImg.color = new Color(1f, 0.2f, 0.2f, 1f);
                  }
              }
          }

          // Selection frame stays off (toggled by hover/click elsewhere)
          if (slot.SelectedFrame != null)
              slot.SelectedFrame.SetActive(false);
      }

   public void ClearStorageSlotVisual(StorageSlotUI slot)
    {
        if (slot == null) return;

        slot.CurrentItem = null;

        if (slot.Icon != null)
        {
            slot.Icon.sprite = null;
            slot.Icon.enabled = false;
        }
        if (slot.AmountText != null)
            slot.AmountText.gameObject.SetActive(false);
        if (slot.QualityText != null)
            slot.QualityText.gameObject.SetActive(false);
        if (slot.EquippedOverlay != null)
            slot.EquippedOverlay.enabled = false;
        if (slot.QueuedOverlay != null)
            slot.QueuedOverlay.enabled = false;
        if (slot.SelectedFrame != null)
            slot.SelectedFrame.SetActive(false);
        if (slot.NoTeleportIcon != null)
            slot.NoTeleportIcon.enabled = false;
        if (slot.FoodIcon != null)
            slot.FoodIcon.enabled = false;
        if (slot.DurabilityRoot != null)
            slot.DurabilityRoot.SetActive(false);
    }

    public void OnStorageSlotCleared(StorageSlotUI slot)
    {
            if (slot?.CurrentItem == null) return;

            var player = Player.m_localPlayer;
            if (player != null)
            {
           var playerInv = player.GetInventory();
                if (playerInv != null && playerInv.AddItem(slot.CurrentItem))
       {
      MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
          $"Returned {Localization.instance?.Localize(slot.CurrentItem.m_shared.m_name) ?? slot.CurrentItem.m_shared.m_name}");
      }
     else
          {
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Inventory full!");
   return;
   }
            }

            if (_currentCompanion != null)
            {
  var compInv = _currentCompanion.GetInventory();
     compInv?.GetStorageInventory()?.RemoveItem(slot.CurrentItem);
            }

            ClearStorageSlotVisual(slot);
 SaveCompanionData();
        }

        public void OnStorageItemDropped(StorageSlotUI slot, ItemDrop.ItemData item)
    {
         if (slot == null || item == null || _currentCompanion == null) return;

         // Enforce slot restrictions - reject items that don't belong
         if (!IsValidItemForStorageSlot(slot, item))
         {
             MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item can't be stored here");
             // Return item to player inventory
             var p = Player.m_localPlayer;
             p?.GetInventory()?.AddItem(item);
             return;
         }

  var compInv = _currentCompanion.GetInventory();
    if (compInv == null)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] No companion inventory; returning {item.m_shared?.m_name} to player");
                Player.m_localPlayer?.GetInventory()?.AddItem(item);
                return;
            }

  var storageInv = compInv.GetStorageInventory();
            if (storageInv == null)
            {
                Debug.LogWarning($"[CompanionInventoryScreen] No storage inventory; returning {item.m_shared?.m_name} to player");
                Player.m_localPlayer?.GetInventory()?.AddItem(item);
                return;
            }

            // SWAP CASE
            // The slot already has an item.  Move that one back to the player so
            // we can drop the new one in.  We do this BEFORE the AddItem attempt
            // so the swapped-out item frees up a backing position; without this,
            // a "full" backing inventory would have no room for the new item even
            // though the slot we're targeting visually has one.
            ItemDrop.ItemData previousSlotItem = slot.CurrentItem;
            if (previousSlotItem != null)
            {
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    var playerInv = player.GetInventory();
                    if (playerInv != null)
                    {
                        playerInv.AddItem(previousSlotItem);
                    }
                }
                storageInv.RemoveItem(previousSlotItem);
            }

            // CRITICAL: capture AddItem's return value.
            // The companion's underlying Inventory has fixed dimensions
            // (CompanionInventory.inventoryWidth ÃƒÂ¯Ã‚Â¿Ã‚Â½ inventoryHeight) which can be
            // smaller than the UI grid displays ÃƒÂ¯Ã‚Â¿Ã‚Â½ the trailing UI cells then
            // appear "empty" but have no backing slot, causing AddItem to return
            // false silently.  If we ignored that return (the previous behavior),
            // the player's item ÃƒÂ¯Ã‚Â¿Ã‚Â½ already removed from their inventory by the
            // caller ÃƒÂ¯Ã‚Â¿Ã‚Â½ would simply VANISH.
            if (!storageInv.AddItem(item))
            {
                Debug.LogWarning($"[CompanionInventoryScreen] Companion storage full; returning {item.m_shared?.m_name} to player");
                Player.m_localPlayer?.GetInventory()?.AddItem(item);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Companion storage is full");
                // Refresh visuals so the swapped-out (now-returned) item still
                // shows correctly if it landed back in storage.
                LoadStorageFromCompanion(_currentCompanion);
                return;
            }

            SetStorageSlotItem(slot, item);
            SaveCompanionData();
      }

   public bool IsValidItemForStorage(ItemDrop.ItemData item)
   {
       if (item == null) return false;
       var itemType = item.m_shared.m_itemType;
       return itemType == ItemDrop.ItemData.ItemType.Consumable ||
           itemType == ItemDrop.ItemData.ItemType.Material ||
           itemType == ItemDrop.ItemData.ItemType.Misc ||
           itemType == ItemDrop.ItemData.ItemType.Trophy ||
           itemType == ItemDrop.ItemData.ItemType.Fish ||
           itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
           itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
           itemType == ItemDrop.ItemData.ItemType.Bow ||
           itemType == ItemDrop.ItemData.ItemType.Shield ||
           itemType == ItemDrop.ItemData.ItemType.Helmet ||
           itemType == ItemDrop.ItemData.ItemType.Chest ||
           itemType == ItemDrop.ItemData.ItemType.Legs ||
           itemType == ItemDrop.ItemData.ItemType.Shoulder ||
           itemType == ItemDrop.ItemData.ItemType.Utility ||
           itemType == ItemDrop.ItemData.ItemType.Tool ||
           itemType == ItemDrop.ItemData.ItemType.Torch ||
           itemType == ItemDrop.ItemData.ItemType.Ammo;
   }

   public bool IsValidItemForStorageSlot(StorageSlotUI slot, ItemDrop.ItemData item)
   {
       if (item == null || slot == null) return false;

       switch (slot.Restriction)
       {
           case StorageSlotRestriction.Food:
               return item.m_shared != null &&
                   (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0);
           case StorageSlotRestriction.Ammo:
               return item.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Ammo;
           default:
               return IsValidItemForStorage(item);
       }
   }

  #endregion

        #region Equipment Management

        private void LoadEquipmentFromCompanion(CompanionController companion)
        {
   if (companion == null) return;

        foreach (var slot in _slots.Values)
            {
        ClearSlotVisual(slot);
          }
            _pendingEquipment.Clear();
            _pendingEquipmentQuality.Clear();

            var inventory = companion.GetInventory();
            if (inventory == null) return;

   LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.Helmet, EquipmentSlot.Helmet);
   LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.Chest, EquipmentSlot.Chest);
            LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.Legs, EquipmentSlot.Legs);
   LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.Shoulder, EquipmentSlot.Shoulder);
            LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.Utility, EquipmentSlot.Utility);
        LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.RightHand, EquipmentSlot.RightHand);
            LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.LeftHand, EquipmentSlot.LeftHand);
            LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.RightBack, EquipmentSlot.RightBack);
            LoadSlotFromPrefabName(inventory, CompanionInventory.EquipmentSlot.LeftBack, EquipmentSlot.LeftBack);
        }

   private void LoadSlotFromPrefabName(CompanionInventory inventory, CompanionInventory.EquipmentSlot companionSlot, EquipmentSlot uiSlot)
 {
        string prefabName = inventory.GetEquipmentPrefabNamePublic(companionSlot);
     if (!string.IsNullOrEmpty(prefabName))
            {
       if (_slots.TryGetValue(uiSlot, out var slotUI))
     {
           SetSlotItem(slotUI, prefabName);
   _pendingEquipment[uiSlot] = prefabName;
                    // CRITICAL: Also load the quality from the companion's equipped item
                    int quality = inventory.GetEquipmentQualityPublic(companionSlot);
                    _pendingEquipmentQuality[uiSlot] = quality;
                }
            }
      }

        public void SetSlotItem(EquipmentSlotUI slot, string prefabName)
        {
            if (slot == null) return;

            slot.CurrentPrefab = prefabName;

            if (string.IsNullOrEmpty(prefabName))
            {
                ClearSlotVisual(slot);
                return;
            }

            var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (itemPrefab == null)
            {
                ClearSlotVisual(slot);
                return;
            }

            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop?.m_itemData == null)
            {
                ClearSlotVisual(slot);
                return;
            }

            var item = itemDrop.m_itemData;
            int quality = _pendingEquipmentQuality.TryGetValue(slot.SlotType, out int q) ? q : item.m_quality;

            // Hide label, show item name
            if (slot.Label != null)
                slot.Label.gameObject.SetActive(false);

            string displayName = item.m_shared?.m_name ?? prefabName;
            if (displayName.StartsWith("$"))
                displayName = Localization.instance?.Localize(displayName) ?? displayName;

            if (slot.ItemName != null)
            {
                slot.ItemName.text = displayName;
                slot.ItemName.gameObject.SetActive(true);
            }

            // Icon
            if (slot.Icon != null)
            {
                if (item.m_shared?.m_icons != null && item.m_shared.m_icons.Length > 0)
                {
                    slot.Icon.sprite = item.m_shared.m_icons[0];
                    slot.Icon.color = Color.white;
                    slot.Icon.enabled = true;
                }
                else
                {
                    slot.Icon.enabled = false;
                }
            }

            // Quality
            if (slot.QualityText != null)
            {
                bool show = quality > 1;
                slot.QualityText.gameObject.SetActive(show);
                if (show) slot.QualityText.text = quality.ToString();
            }

            // Equipped overlay (always true for equipment slots)
            if (slot.EquippedOverlay != null)
                slot.EquippedOverlay.enabled = true;

            // No-teleport icon
            if (slot.NoTeleportIcon != null)
                slot.NoTeleportIcon.enabled = (item.m_shared?.m_teleportable == false);

            // Food icon
            if (slot.FoodIcon != null)
            {
                bool isFood = item.m_shared != null &&
                    (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0);
                slot.FoodIcon.enabled = isFood;
                if (isFood)
                {
                    if (item.m_shared.m_foodEitr > 0)
                        slot.FoodIcon.color = new Color(0.6f, 0.6f, 1f, 1f);
                    else if (item.m_shared.m_food > item.m_shared.m_foodStamina)
                        slot.FoodIcon.color = new Color(1f, 0.5f, 0.5f, 1f);
                    else
                        slot.FoodIcon.color = new Color(1f, 1f, 0.5f, 1f);
                }
            }

            // Durability bar
            if (slot.DurabilityRoot != null)
            {
                float maxDur = item.GetMaxDurability();
                bool hasDur = maxDur > 0 && item.m_shared?.m_useDurability == true;
                slot.DurabilityRoot.SetActive(hasDur);
                if (hasDur && slot.DurabilityBarRect != null)
                {
                    float ratio = Mathf.Clamp01(item.m_durability / maxDur);
                    var parentRT = slot.DurabilityRoot.GetComponent<RectTransform>();
                    float fullW = parentRT != null ? parentRT.sizeDelta.x : 54f;
                    slot.DurabilityBarRect.sizeDelta = new Vector2(fullW * ratio, slot.DurabilityBarRect.sizeDelta.y);

                    var barImg = slot.DurabilityBarRect.GetComponent<Image>();
                    if (barImg != null)
                    {
                        barImg.enabled = true;
                        if (ratio > 0.75f) barImg.color = new Color(0.94f, 0.94f, 0.94f, 1f);
                        else if (ratio > 0.50f) barImg.color = new Color(1f, 0.8f, 0.2f, 1f);
                        else if (ratio > 0.25f) barImg.color = new Color(1f, 0.5f, 0.1f, 1f);
                        else barImg.color = new Color(1f, 0.2f, 0.2f, 1f);
                    }
                }
            }

            // Selection frame off, queued off, amount off
            if (slot.SelectedFrame != null) slot.SelectedFrame.SetActive(false);
            if (slot.QueuedOverlay != null) slot.QueuedOverlay.enabled = false;
            if (slot.AmountText != null) slot.AmountText.gameObject.SetActive(false);
        }

        public void ClearSlotVisual(EquipmentSlotUI slot)
        {
            if (slot == null) return;

            slot.CurrentPrefab = null;

            // Show label, hide item name
            if (slot.Label != null)
                slot.Label.gameObject.SetActive(true);
            if (slot.ItemName != null)
            {
                slot.ItemName.text = "";
                slot.ItemName.gameObject.SetActive(false);
            }

            // Icon
            if (slot.Icon != null)
            {
                slot.Icon.sprite = null;
                slot.Icon.enabled = false;
            }

            // All overlays off
            if (slot.AmountText != null) slot.AmountText.gameObject.SetActive(false);
            if (slot.QualityText != null) slot.QualityText.gameObject.SetActive(false);
            if (slot.EquippedOverlay != null) slot.EquippedOverlay.enabled = false;
            if (slot.QueuedOverlay != null) slot.QueuedOverlay.enabled = false;
            if (slot.SelectedFrame != null) slot.SelectedFrame.SetActive(false);
            if (slot.NoTeleportIcon != null) slot.NoTeleportIcon.enabled = false;
            if (slot.FoodIcon != null) slot.FoodIcon.enabled = false;
            if (slot.DurabilityRoot != null) slot.DurabilityRoot.SetActive(false);
        }

        public void OnSlotCleared(EquipmentSlot slotType)
        {
if (_slots.TryGetValue(slotType, out var slot))
            {
                // Get quality before clearing
                int quality = _pendingEquipmentQuality.TryGetValue(slotType, out int q) ? q : 1;
ReturnItemToInventory(slot.CurrentPrefab, quality);
                ClearSlotVisual(slot);
      _pendingEquipment.Remove(slotType);
                _pendingEquipmentQuality.Remove(slotType);

    ApplyEquipmentToCompanion();
      }
     }

        public void ReturnItemToInventory(string prefabName, int quality = 1)
        {
            if (string.IsNullOrEmpty(prefabName)) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            var inventory = player.GetInventory();
            if (inventory == null) return;

            var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (itemPrefab == null) return;

            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null) return;

            // CRITICAL FIX: Use the passed quality instead of prefab default
            int itemQuality = quality > 0 ? quality : itemDrop.m_itemData.m_quality;
            var addedItem = inventory.AddItem(prefabName, 1, itemQuality, itemDrop.m_itemData.m_variant, 0, "");

            if (addedItem != null)
            {
                string qualityStr = itemQuality > 1 ? $" [{itemQuality}]" : "";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Returned {GetItemDisplayName(prefabName)}{qualityStr} to inventory");
            }
            else
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Inventory full! Item dropped on ground.");
                DropItemOnGround(prefabName, itemQuality);
            }
        }

        private void DropItemOnGround(string prefabName, int quality = 1)
        {
     var player = Player.m_localPlayer;
       if (player == null) return;

        var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
         if (itemPrefab == null) return;

            var dropPos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 0.5f;
            var droppedGO = CompanionNetworkHelper.Spawn(itemPrefab, dropPos, Quaternion.identity);
            
            // Set the quality on the dropped item
            var droppedItem = droppedGO.GetComponent<ItemDrop>();
            if (droppedItem != null && quality > 1)
            {
                droppedItem.m_itemData.m_quality = quality;
                droppedItem.SetQuality(quality);
            }
        }

      private string GetItemDisplayName(string prefabName)
      {
         var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
          if (itemPrefab == null) return prefabName;

        var itemDrop = itemPrefab.GetComponent<ItemDrop>();
        if (itemDrop == null) return prefabName;

            string displayName = itemDrop.m_itemData?.m_shared?.m_name ?? prefabName;
            if (displayName.StartsWith("$"))
        displayName = Localization.instance?.Localize(displayName) ?? displayName;

      return displayName;
        }

   public void OnItemDropped(EquipmentSlot slotType, string prefabName, int quality = 1)
     {
       if (_slots.TryGetValue(slotType, out var slot))
            {
     if (!string.IsNullOrEmpty(slot.CurrentPrefab))
      {
                // Get quality of existing item before replacing
                int existingQuality = _pendingEquipmentQuality.TryGetValue(slotType, out int q) ? q : 1;
  ReturnItemToInventory(slot.CurrentPrefab, existingQuality);
   }

         SetSlotItem(slot, prefabName);
         _pendingEquipment[slotType] = prefabName;
            _pendingEquipmentQuality[slotType] = quality;
            Debug.Log($"[CompanionInventoryScreen] OnItemDropped: {prefabName} with quality {quality}");

 ApplyEquipmentToCompanion();
          }
        }

        private void HandleApplyClicked()
        {
          ApplyEquipmentToCompanion();
         SaveCompanionData();

            int equippedCount = _pendingEquipment.Count(kvp => !string.IsNullOrEmpty(kvp.Value));

        string message = equippedCount > 0
        ? $"Companion equipment saved! ({equippedCount} items)"
        : "Companion equipment cleared!";

      MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, message);
        }

        public void ApplyEquipmentToCompanion()
        {
            if (_currentCompanion == null) return;

  var inventory = _currentCompanion.GetInventory();
   if (inventory == null) return;

            var allSlots = new[]
            {
                CompanionInventory.EquipmentSlot.Helmet,
                CompanionInventory.EquipmentSlot.Chest,
  CompanionInventory.EquipmentSlot.Legs,
    CompanionInventory.EquipmentSlot.Shoulder,
                CompanionInventory.EquipmentSlot.Utility,
   CompanionInventory.EquipmentSlot.RightHand,
     CompanionInventory.EquipmentSlot.LeftHand,
          CompanionInventory.EquipmentSlot.RightBack,
 CompanionInventory.EquipmentSlot.LeftBack
        };

   foreach (var slot in allSlots)
      {
      string currentPrefab = inventory.GetEquipmentPrefabNamePublic(slot);
    EquipmentSlot uiSlot = MapCompanionSlotToUISlot(slot);
    string pendingPrefab = _pendingEquipment.TryGetValue(uiSlot, out var p) ? p : "";

   currentPrefab = string.IsNullOrEmpty(currentPrefab) ? "" : currentPrefab;
                pendingPrefab = string.IsNullOrEmpty(pendingPrefab) ? "" : pendingPrefab;

     if (currentPrefab == pendingPrefab)
   continue;

    if (!string.IsNullOrEmpty(currentPrefab))
     {
    inventory.UnequipSlotSilent(slot);
        }

      if (!string.IsNullOrEmpty(pendingPrefab))
            {
        var itemPrefab = ObjectDB.instance?.GetItemPrefab(pendingPrefab);
          if (itemPrefab != null)
          {
                  var itemDrop = itemPrefab.GetComponent<ItemDrop>();
        if (itemDrop != null)
     {
          var itemData = itemDrop.m_itemData.Clone();
         itemData.m_dropPrefab = itemPrefab;
                    // CRITICAL FIX: Use the stored quality instead of default prefab quality
                    if (_pendingEquipmentQuality.TryGetValue(uiSlot, out int storedQuality) && storedQuality > 0)
                    {
                        itemData.m_quality = storedQuality;
                        Debug.Log($"[CompanionInventoryScreen] ApplyEquipment: {pendingPrefab} with quality {storedQuality}");
                    }
            inventory.EquipItemSilent(slot, itemData);
             }
      }
    }
            }

      inventory.RecalculateEquipmentBonusesPublic();
            RefreshCompanionVisuals();
        inventory.TriggerSaveToZDO();
        }

        private EquipmentSlot MapCompanionSlotToUISlot(CompanionInventory.EquipmentSlot slot)
        {
     return slot switch
            {
      CompanionInventory.EquipmentSlot.Helmet => EquipmentSlot.Helmet,
      CompanionInventory.EquipmentSlot.Chest => EquipmentSlot.Chest,
          CompanionInventory.EquipmentSlot.Legs => EquipmentSlot.Legs,
       CompanionInventory.EquipmentSlot.Shoulder => EquipmentSlot.Shoulder,
       CompanionInventory.EquipmentSlot.Utility => EquipmentSlot.Utility,
 CompanionInventory.EquipmentSlot.RightHand => EquipmentSlot.RightHand,
    CompanionInventory.EquipmentSlot.LeftHand => EquipmentSlot.LeftHand,
    CompanionInventory.EquipmentSlot.RightBack => EquipmentSlot.RightBack,
              CompanionInventory.EquipmentSlot.LeftBack => EquipmentSlot.LeftBack,
         _ => EquipmentSlot.Helmet
      };
        }

        private void RefreshCompanionVisuals()
        {
            if (_currentCompanion == null) return;

            var visEquip = _currentCompanion.GetComponent<NpcVisEquipment>();
            if (visEquip == null)
            {
                visEquip = _currentCompanion.gameObject.AddComponent<NpcVisEquipment>();
                // Only force reinitialize when we first add the component
                visEquip.ForceReinitialize();
            }

            // Build equipment dictionary including ALL slots (even empty ones)
            // This ensures cleared slots get their visuals removed
            var equipment = new Dictionary<DressingRoomScreenController.EquipmentSlot, string>();
            
            // Initialize all slots to empty
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                var dressingSlot = MapCompanionSlotToDressingSlot(slot);
                equipment[dressingSlot] = "";
            }
            
            // Now fill in the slots that have equipment
            foreach (var kvp in _pendingEquipment)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    var dressingSlot = MapCompanionSlotToDressingSlot(kvp.Key);
                    equipment[dressingSlot] = kvp.Value;
                }
            }

            visEquip.ApplyEquipment(equipment.ToDictionary(kv => (CompanionInventory.EquipmentSlot)(int)kv.Key, kv => kv.Value));
        }

  public void SaveCompanionData()
        {
            if (_currentCompanion == null) return;

            _currentCompanion.SaveToZDO();
            _currentCompanion.SaveCompanionToVault();

    var inventory = _currentCompanion.GetInventory();
  inventory?.SaveToZDO();
        }

        private DressingRoomScreenController.EquipmentSlot MapCompanionSlotToDressingSlot(EquipmentSlot slot)
        {
 return slot switch
         {
 EquipmentSlot.Helmet => DressingRoomScreenController.EquipmentSlot.Helmet,
      EquipmentSlot.Chest => DressingRoomScreenController.EquipmentSlot.Chest,
       EquipmentSlot.Legs => DressingRoomScreenController.EquipmentSlot.Legs,
           EquipmentSlot.Shoulder => DressingRoomScreenController.EquipmentSlot.Shoulder,
     EquipmentSlot.Utility => DressingRoomScreenController.EquipmentSlot.Utility,
     EquipmentSlot.RightHand => DressingRoomScreenController.EquipmentSlot.RightHand,
          EquipmentSlot.LeftHand => DressingRoomScreenController.EquipmentSlot.LeftHand,
                EquipmentSlot.RightBack => DressingRoomScreenController.EquipmentSlot.RightBack,
     EquipmentSlot.LeftBack => DressingRoomScreenController.EquipmentSlot.LeftBack,
   _ => DressingRoomScreenController.EquipmentSlot.Helmet
   };
  }

      private void HandleClearAllClicked()
      {
      foreach (var slot in _slots.Values)
   {
           if (!string.IsNullOrEmpty(slot.CurrentPrefab))
   {
                // Get quality before returning
                int quality = _pendingEquipmentQuality.TryGetValue(slot.SlotType, out int q) ? q : 1;
          ReturnItemToInventory(slot.CurrentPrefab, quality);
       }
                ClearSlotVisual(slot);
          }

 _pendingEquipment.Clear();
            _pendingEquipmentQuality.Clear();
            ApplyEquipmentToCompanion();
            SaveCompanionData();
 }

        private void UpdateCompanionInfo()
        {
            if (_currentCompanion == null) return;

            var character = _currentCompanion.GetCharacter();
            var stats = _currentCompanion.GetStats();
            var progression = _currentCompanion.GetProgression();
            
            // Update header info text
            if (companionInfoText != null && character != null)
            {
                string followStatus = _currentCompanion.IsFollowing ? "Following" : "Staying";
                int level = progression?.Level ?? 0;
                companionInfoText.text = $"Level {level} | {followStatus}";
            }
            
            // Update level indicator
            if (_levelText != null && progression != null)
            {
                _levelText.text = $"Lvl\n{progression.Level}";
            }
            
            // Use centralized stat calculator for consistent values
            var calc = new CompanionStatCalculator(_currentCompanion);
            
            // Update health bar - dynamically sized based on current/max ratio
            // The fill bar's anchorMax.x determines how much of the inner container is filled
            // anchorMax.x = 0.0 means empty, 1.0 means full
            if (_healthBarFill != null && calc.IsValid)
            {
                float healthPercent = Mathf.Clamp01(calc.HealthPercentage);
                SetBarFillPercent(_healthBarFill, healthPercent);
                
                if (_healthText != null)
                {
                    _healthText.text = $"{calc.CurrentHealth:F0} / {calc.MaxHealth:F0}";
                }
            }
            
            // Update stamina bar
            if (_staminaBarFill != null && calc.IsValid)
            {
                float staminaPercent = Mathf.Clamp01(calc.StaminaPercentage);
                SetBarFillPercent(_staminaBarFill, staminaPercent);
                
                if (_staminaText != null)
                {
                    _staminaText.text = $"{calc.CurrentStamina:F0} / {calc.MaxStamina:F0}";
                }
            }
            
            // Update eitr bar
            if (_eitrBarFill != null && calc.IsValid)
            {
                float eitrPercent = Mathf.Clamp01(calc.EitrPercentage);
                SetBarFillPercent(_eitrBarFill, eitrPercent);
                
                if (_eitrText != null)
                {
                    _eitrText.text = $"{calc.CurrentEitr:F0} / {calc.MaxEitr:F0}";
                }
            }
        }
        
        /// <summary>
        /// Sets a stat bar's fill percentage using anchor-based sizing.
        /// The fill bar stretches from the left edge (anchorMin.x = 0) to the percentage point (anchorMax.x = percent).
        /// This approach ensures the bar dynamically scales with its container - no hardcoded widths.
        /// </summary>
        private void SetBarFillPercent(Image fillImage, float percent)
        {
            if (fillImage == null) return;
            
            var rect = fillImage.rectTransform;
            
            // CRITICAL: Use anchors for percentage-based fill
            // anchorMin = (0, 0) = bottom-left of parent
            // anchorMax = (percent, 1) = percent across, full height
            // No offsets - anchors alone determine size
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(percent, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        #endregion

        #region Name Editing

        private void StartNameEdit()
  {
    if (_currentCompanion == null || _nameEditPanel == null || _nameInputField == null) return;

            _isEditingName = true;
   _nameInputField.text = _currentCompanion.GetDisplayName();
     _nameEditPanel.SetActive(true);
        headerText.gameObject.SetActive(false);

            _nameInputField.Select();
    _nameInputField.ActivateInputField();
        }

        private void OnNameEditComplete(string newName)
        {
   if (!_isEditingName) return;
       ConfirmNameEdit();
        }

        private void ConfirmNameEdit()
        {
        if (_currentCompanion == null || _nameInputField == null) return;

    string newName = _nameInputField.text?.Trim();

    if (!string.IsNullOrEmpty(newName) && newName.Length >= 1)
    {
             _currentCompanion.SetDisplayName(newName);
    _currentCompanion.SaveCompanionToVault();

            if (headerText != null)
    {
                headerText.text = newName;
            }

    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Companion renamed to: {newName}");
    }

     CancelNameEdit();
        }

        private void CancelNameEdit()
        {
_isEditingName = false;

            if (_nameEditPanel != null)
         _nameEditPanel.SetActive(false);

            if (headerText != null)
        headerText.gameObject.SetActive(true);
        }

        #endregion

        #region Item Validation

     public bool IsValidItemForSlot(EquipmentSlot slot, ItemDrop.ItemData item)
        {
         if (item == null) return false;

    var itemType = item.m_shared.m_itemType;

            return slot switch
    {
            EquipmentSlot.Helmet => itemType == ItemDrop.ItemData.ItemType.Helmet,
     EquipmentSlot.Chest => itemType == ItemDrop.ItemData.ItemType.Chest,
     EquipmentSlot.Legs => itemType == ItemDrop.ItemData.ItemType.Legs,
    EquipmentSlot.Shoulder => itemType == ItemDrop.ItemData.ItemType.Shoulder,
       EquipmentSlot.Utility => itemType == ItemDrop.ItemData.ItemType.Utility,
        EquipmentSlot.RightHand => itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
  itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
          itemType == ItemDrop.ItemData.ItemType.Tool ||
            itemType == ItemDrop.ItemData.ItemType.Torch,
    EquipmentSlot.LeftHand => itemType == ItemDrop.ItemData.ItemType.Shield ||
                  itemType == ItemDrop.ItemData.ItemType.Torch ||
     itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
        itemType == ItemDrop.ItemData.ItemType.Bow ||
          itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
                EquipmentSlot.RightBack => itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
    itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
           itemType == ItemDrop.ItemData.ItemType.Bow ||
      itemType == ItemDrop.ItemData.ItemType.Shield ||
    itemType == ItemDrop.ItemData.ItemType.Tool,
        EquipmentSlot.LeftBack => itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
        itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
 itemType == ItemDrop.ItemData.ItemType.Bow ||
        itemType == ItemDrop.ItemData.ItemType.Shield ||
  itemType == ItemDrop.ItemData.ItemType.Tool,
              _ => false
  };
    }

        #endregion

        #region Helper Types

        public class EquipmentSlotUI
        {
            public EquipmentSlot SlotType;
            public GameObject Root;
            public RectTransform RectTransform;
            public RectTransform ClickableRect; // InventoryElement(Clone) rect for accurate hit testing
            public Image Background;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI ItemName;
            public string CurrentPrefab;
            // InventoryElement(Clone) children ÃƒÂ¯Ã‚Â¿Ã‚Â½ same as StorageSlotUI
            public Image Icon;
            public TMP_Text AmountText;
            public TMP_Text QualityText;
            public Image EquippedOverlay;
            public Image QueuedOverlay;
            public GameObject SelectedFrame;
            public Image NoTeleportIcon;
            public Image FoodIcon;
            public GameObject DurabilityRoot;
            public RectTransform DurabilityBarRect;
        }

        public enum StorageSlotRestriction { None, Food, Ammo }

        public class StorageSlotUI
        {
            public int SlotIndex;
            public StorageSlotRestriction Restriction;
            public GameObject Root;
            public RectTransform RectTransform;
            public Image Background;
            public Image Icon;
            public TMP_Text AmountText;
            public TMP_Text QualityText;
            public Image EquippedOverlay;
            public Image QueuedOverlay;
            public GameObject SelectedFrame;
            public Image NoTeleportIcon;
            public Image FoodIcon;
            public GameObject DurabilityRoot;
            public RectTransform DurabilityBarRect;
            public ItemDrop.ItemData CurrentItem;
        }

        private class SlotClickHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private CompanionInventoryScreen _controller;
            private EquipmentSlotUI _slot;
            private bool _isDragging;
            private bool _isPointerDown;
            private Vector2 _pointerDownPosition;
            private const float DRAG_THRESHOLD = 5f;  // Pixels of movement before drag starts

            public void Initialize(CompanionInventoryScreen controller, EquipmentSlotUI slot)
            {
                _controller = controller;
                _slot = slot;
            }
            
            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    _isPointerDown = true;
                    _pointerDownPosition = eventData.position;
                }
            }
            
            public void OnPointerUp(PointerEventData eventData)
            {
                _isPointerDown = false;
            }
            
            private void Update()
            {
                // Manual drag detection when pointer is down and hasn't started dragging
                if (_isPointerDown && !_isDragging && !string.IsNullOrEmpty(_slot?.CurrentPrefab))
                {
                    Vector2 currentPos = Input.mousePosition;
                    float distance = Vector2.Distance(_pointerDownPosition, currentPos);
                    
                    if (distance > DRAG_THRESHOLD)
                    {
                        // Start dragging using Valheim's system
                        StartDragManual();
                    }
                }
                
                // End drag detection when mouse is released
                if (_isDragging && !Input.GetMouseButton(0))
                {
                    EndDragManual();
                }
            }
            
            private void StartDragManual()
            {
                if (_slot?.CurrentPrefab == null || string.IsNullOrEmpty(_slot.CurrentPrefab)) return;

                _isDragging = true;
                _isPointerDown = false;

                var player = Player.m_localPlayer;
                if (player == null) return;
                var playerInv = player.GetInventory();
                if (playerInv == null) return;

                var itemPrefab = ObjectDB.instance?.GetItemPrefab(_slot.CurrentPrefab);
                if (itemPrefab == null) return;
                var itemDrop = itemPrefab.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData == null) return;

                int storedQuality = _controller._pendingEquipmentQuality.TryGetValue(_slot.SlotType, out int q) ? q : 1;
                string prefabName = _slot.CurrentPrefab;

                _controller.ClearSlotVisual(_slot);
                _controller._pendingEquipment.Remove(_slot.SlotType);
                _controller._pendingEquipmentQuality.Remove(_slot.SlotType);
                _controller.ApplyEquipmentToCompanion();

                var addedItem = playerInv.AddItem(prefabName, 1, storedQuality,
                    itemDrop.m_itemData.m_variant, 0, "");
                if (addedItem == null)
                {
                    _controller.DropItemOnGround(prefabName, storedQuality);
                    return;
                }

                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                    if (setupDragMethod != null && InventoryGui.instance != null)
                    {
                        setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, 1 });
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CompanionInventoryScreen] Failed to setup drag: {ex.Message}");
                }
            }
            
            private void EndDragManual()
            {
                // Drag is now handled by Valheim's system, so we just reset our state
                if (!_isDragging) return;
                _isDragging = false;
                _isPointerDown = false;
                
                // The actual drop handling is done by InventoryGui patches
                // If the item wasn't dropped on a valid target, Valheim will handle it
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_controller == null || _slot == null) return;
                
                // Don't process click if we just finished dragging
                if (_isDragging) return;
                
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    // Right-click: Immediately return item to player inventory
                    if (!string.IsNullOrEmpty(_slot.CurrentPrefab))
                    {
                        _controller.OnSlotCleared(_slot.SlotType);
                    }
                }
                else if (eventData.button == PointerEventData.InputButton.Left)
                {
                    var draggedItem = _controller.GetDraggedItemFromInventory();
                    
                    if (draggedItem != null)
                    {
                        // Player is dragging an item from their inventory - drop it in this slot
                        if (_controller.IsValidItemForSlot(_slot.SlotType, draggedItem))
                        {
                            string prefabName = draggedItem.m_dropPrefab?.name ?? draggedItem.m_shared?.m_name;
                            if (!string.IsNullOrEmpty(prefabName))
                            {
                                // CRITICAL: Pass the item's quality so upgraded items retain their level
                                _controller.OnItemDropped(_slot.SlotType, prefabName, draggedItem.m_quality);
                                
                                // Remove item from player inventory
                                var player = Player.m_localPlayer;
                                if (player != null)
                                {
                                    player.GetInventory()?.RemoveItem(draggedItem, 1);
                                }
                                
                                // Clear the drag from InventoryGui
                                _controller.ClearInventoryGuiDrag();
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(_slot.CurrentPrefab))
                    {
                        // Left-click pickup: pick up item and start Valheim drag
                        PickupItemFromSlot();
                    }
                }
            }

            private void PickupItemFromSlot()
            {
                if (string.IsNullOrEmpty(_slot?.CurrentPrefab)) return;

                var player = Player.m_localPlayer;
                if (player == null) return;
                var playerInv = player.GetInventory();
                if (playerInv == null) return;

                var itemPrefab = ObjectDB.instance?.GetItemPrefab(_slot.CurrentPrefab);
                if (itemPrefab == null) return;
                var itemDrop = itemPrefab.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData == null) return;

                int storedQuality = _controller._pendingEquipmentQuality.TryGetValue(_slot.SlotType, out int q) ? q : 1;
                string prefabName = _slot.CurrentPrefab;

                _controller.ClearSlotVisual(_slot);
                _controller._pendingEquipment.Remove(_slot.SlotType);
                _controller._pendingEquipmentQuality.Remove(_slot.SlotType);
                _controller.ApplyEquipmentToCompanion();

                var addedItem = playerInv.AddItem(prefabName, 1, storedQuality,
                    itemDrop.m_itemData.m_variant, 0, "");
                if (addedItem == null)
                {
                    _controller.DropItemOnGround(prefabName, storedQuality);
                    return;
                }

                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                    if (setupDragMethod != null && InventoryGui.instance != null)
                    {
                        setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, 1 });
                        InventoryGui.instance.m_moveItemEffects?.Create(_controller.transform.position, Quaternion.identity);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CompanionInventoryScreen] Failed to pickup equipment: {ex.Message}");
                }
            }

            // Keep IBeginDragHandler/etc for fallback compatibility
            public void OnBeginDrag(PointerEventData eventData)
            {
                // Handled by Update() now due to InventoryGui blocking these events
            }
            
            public void OnDrag(PointerEventData eventData)
            {
                // Handled by Update()
            }
            
            public void OnEndDrag(PointerEventData eventData)
            {
                // Handled by Update()
            }
        }

        private class StorageSlotClickHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private CompanionInventoryScreen _controller;
            private StorageSlotUI _slot;
            private bool _isDragging;
            private bool _isPointerDown;
            private Vector2 _pointerDownPosition;
            private const float DRAG_THRESHOLD = 5f;  // Pixels of movement before drag starts

            public void Initialize(CompanionInventoryScreen controller, StorageSlotUI slot)
            {
                _controller = controller;
                _slot = slot;
            }
            
            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    _isPointerDown = true;
                    _pointerDownPosition = eventData.position;
                }
            }
            
            public void OnPointerUp(PointerEventData eventData)
            {
                _isPointerDown = false;
            }
            
            private void Update()
            {
                // Manual drag detection when pointer is down and hasn't started dragging
                if (_isPointerDown && !_isDragging && _slot?.CurrentItem != null)
                {
                    Vector2 currentPos = Input.mousePosition;
                    float distance = Vector2.Distance(_pointerDownPosition, currentPos);
                    
                    if (distance > DRAG_THRESHOLD)
                    {
                        // Start dragging using Valheim's system
                        StartDragManual();
                    }
                }
                
                // End drag detection when mouse is released
                if (_isDragging && !Input.GetMouseButton(0))
                {
                    EndDragManual();
                }
            }
            
            private void StartDragManual()
            {
                if (_slot?.CurrentItem == null) return;

                _isDragging = true;
                _isPointerDown = false;

                var player = Player.m_localPlayer;
                if (player == null) return;
                var playerInv = player.GetInventory();
                if (playerInv == null) return;

                var originalItem = _slot.CurrentItem;
                string prefabName = originalItem.m_dropPrefab?.name ?? originalItem.m_shared?.m_name;
                if (string.IsNullOrEmpty(prefabName)) return;
                prefabName = prefabName.Replace("(Clone)", "").Trim();
                int stack = originalItem.m_stack;
                int quality = originalItem.m_quality;
                int variant = originalItem.m_variant;

                if (_controller._currentCompanion != null)
                {
                    var compInv = _controller._currentCompanion.GetInventory();
                    compInv?.GetStorageInventory()?.RemoveItem(_slot.CurrentItem);
                }

                _controller.ClearStorageSlotVisual(_slot);
                _controller.SaveCompanionData();

                // Use the string overload that returns a fresh ItemData reference.
                // The bool overload can merge into an existing stack, leaving the
                // original reference orphaned and breaking SetupDragItem.
                var addedItem = playerInv.AddItem(prefabName, stack, quality, variant, 0, "");
                if (addedItem == null)
                {
                    // Give back to companion
                    if (_controller._currentCompanion != null)
                    {
                        var compInv = _controller._currentCompanion.GetInventory();
                        compInv?.GetStorageInventory()?.AddItem(originalItem);
                        _controller.SetStorageSlotItem(_slot, originalItem);
                        _controller.SaveCompanionData();
                    }
                    return;
                }

                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                    if (setupDragMethod != null && InventoryGui.instance != null)
                    {
                        setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, stack });
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CompanionInventoryScreen] Failed to setup storage drag: {ex.Message}");
                }
            }
            
            private void EndDragManual()
            {
                // Drag is now handled by Valheim's system, so we just reset our state
                if (!_isDragging) return;
                _isDragging = false;
                _isPointerDown = false;
                
                // The actual drop handling is done by InventoryGui
                // If the item wasn't dropped on a valid target, Valheim will handle it
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_controller == null || _slot == null) return;
                
                // Don't process click if we just finished dragging
                if (_isDragging) return;
                
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    // Right-click: Immediately return item to player inventory
                    if (_slot.CurrentItem != null)
                    {
                        _controller.OnStorageSlotCleared(_slot);
                    }
                }
                else if (eventData.button == PointerEventData.InputButton.Left)
                {
                    var draggedItem = _controller.GetDraggedItemFromInventory();
                    
                    if (draggedItem != null)
                    {
                        // Player is dragging an item from their inventory - drop it in this slot
                        if (_controller.IsValidItemForStorageSlot(_slot, draggedItem))
                        {
                            // Clone the item to preserve all properties
                            var itemClone = draggedItem.Clone();
                            _controller.OnStorageItemDropped(_slot, itemClone);

                            // Remove the ENTIRE item from player inventory (not just 1)
                            var player = Player.m_localPlayer;
                            if (player != null)
                            {
                                player.GetInventory()?.RemoveItem(draggedItem);
                            }

                            // Clear the drag from InventoryGui
                            _controller.ClearInventoryGuiDrag();
                        }
                    }
                    else if (_slot.CurrentItem != null)
                    {
                        // LEFT-CLICK PICKUP: No item being dragged, slot has item - pick it up!
                        // This matches vanilla InventoryGrid.OnLeftClick behavior
                        PickupItemFromSlot();
                    }
                }
            }
            
            /// <summary>
            /// Picks up the item from this storage slot and starts a Valheim drag.
            /// Matches vanilla InventoryGrid.OnLeftClick behavior.
            /// </summary>
            private void PickupItemFromSlot()
            {
                if (_slot?.CurrentItem == null) return;

                var player = Player.m_localPlayer;
                if (player == null) return;
                var playerInv = player.GetInventory();
                if (playerInv == null) return;

                var originalItem = _slot.CurrentItem;
                string prefabName = originalItem.m_dropPrefab?.name ?? originalItem.m_shared?.m_name;
                if (string.IsNullOrEmpty(prefabName)) return;
                prefabName = prefabName.Replace("(Clone)", "").Trim();
                int stack = originalItem.m_stack;
                int quality = originalItem.m_quality;
                int variant = originalItem.m_variant;

                if (_controller._currentCompanion != null)
                {
                    var compInv = _controller._currentCompanion.GetInventory();
                    compInv?.GetStorageInventory()?.RemoveItem(_slot.CurrentItem);
                }

                _controller.ClearStorageSlotVisual(_slot);
                _controller.SaveCompanionData();

                // Use the string overload that returns a fresh ItemData reference.
                // The bool overload can merge into an existing stack, leaving the
                // original reference orphaned and breaking SetupDragItem.
                var addedItem = playerInv.AddItem(prefabName, stack, quality, variant, 0, "");
                if (addedItem == null)
                {
                    if (_controller._currentCompanion != null)
                    {
                        var compInv = _controller._currentCompanion.GetInventory();
                        compInv?.GetStorageInventory()?.AddItem(originalItem);
                        _controller.SetStorageSlotItem(_slot, originalItem);
                        _controller.SaveCompanionData();
                    }
                    return;
                }

                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
                    if (setupDragMethod != null && InventoryGui.instance != null)
                    {
                        setupDragMethod.Invoke(InventoryGui.instance, new object[] { addedItem, playerInv, stack });
                        InventoryGui.instance.m_moveItemEffects?.Create(_controller.transform.position, Quaternion.identity);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CompanionInventoryScreen] Failed to pickup item: {ex.Message}");
                }
            }
            
            // Keep IBeginDragHandler/etc for fallback compatibility
            public void OnBeginDrag(PointerEventData eventData)
            {
                // Handled by Update() now due to InventoryGui blocking these events
            }
            
            public void OnDrag(PointerEventData eventData)
            {
                // Handled by Update()
            }
            
            public void OnEndDrag(PointerEventData eventData)
            {
                // Handled by Update()
            }
        }

  private class ButtonHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private Image _img;
         public void Initialize(Image img) => _img = img;
            public void OnPointerEnter(PointerEventData eventData)
            {
 if (_img != null) _img.color = UIFontConfig.Colors.ButtonHover;
            }
   public void OnPointerExit(PointerEventData eventData)
            {
  if (_img != null) _img.color = UIFontConfig.Colors.ButtonNormal;
   }
        }

        #endregion
    }
}
