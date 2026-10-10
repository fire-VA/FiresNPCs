using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Newtonsoft.Json;
using FiresRPGmaker.NpcFashion;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Controller for the NPC Dressing Room screen.
    /// Allows admins to drag equipment from their inventory onto NPC equipment slots.
    /// Items are consumed from player inventory via Harmony patches when dropped on slots.
    /// </summary>
    public partial class DressingRoomScreenController : MonoBehaviour
    {
        public static DressingRoomScreenController Instance { get; private set; }

        [Header("UI Elements")]
        public GameObject screenRoot;
        public TMP_Text instructionsText;
        public TMP_Text selectedItemText;

        // Equipment slot UI elements (public so patches can access)
        public Dictionary<EquipmentSlot, EquipmentSlotUI> Slots => _slots;
        private Dictionary<EquipmentSlot, EquipmentSlotUI> _slots = new Dictionary<EquipmentSlot, EquipmentSlotUI>();

        // Reference to current NPC being dressed
        private NpcController _currentNpc;
        private Button _applyButton;
        private Button _clearAllButton;

        // Button RectTransforms for manual hit testing
        private RectTransform _applyButtonRect;
        private RectTransform _clearAllButtonRect;

        // Main panel button RectTransforms for manual hit testing
        private RectTransform _closeButtonRect;
        private RectTransform _prevButtonRect;
        private RectTransform _sizeButtonRect;
        private RectTransform _headerRect;

        // Fashion UI fields (periodic animations, interactions, hair, sounds, etc.)
        private RectTransform _fashionPanelRect;
        private RectTransform _fashionLeftPanelRect;
     private TMP_Dropdown _periodicAnimationDropdown;
        private TMP_InputField _periodicAnimationTimeInput;
        private TMP_Dropdown _periodicSoundDropdown;
  private TMP_InputField _periodicSoundTimeInput;
        private TMP_Dropdown _interactAnimationDropdown;
        private TMP_Dropdown _greetAnimationDropdown;
      private TMP_Dropdown _byeAnimationDropdown;
        private TMP_InputField _greetTextInput;   // speech bubble on player approach (kg GreetText parity)
        private TMP_InputField _byeTextInput;     // speech bubble on player leave (kg ByeText parity)
        private TMP_Dropdown _hairItemDropdown;
        private TMP_Dropdown _beardItemDropdown;
        private TMP_Dropdown _hairColorDropdown;
        private TMP_Dropdown _beardColorDropdown;
      private TMP_Dropdown _skinColorDropdown;
      private TMP_Dropdown _eyeColorDropdown;    // Eye color dropdown
   private Toggle _lookAtPlayerToggle;
        private TMP_InputField _modelScaleInput;
        // Keep list of dropdowns so we can manually open them when Inventory blocks pointer clicks
        private List<TMP_Dropdown> _dropdowns = new List<TMP_Dropdown>();
        // Keep list of input fields for manual click handling
        private List<TMP_InputField> _inputFields = new List<TMP_InputField>();

        // NPC stats scroll strip (health / respawn / nameplate) — manual click/scroll since the inventory blocks events.
        private Toggle _infiniteHealthToggle, _hideNameplateToggle;
        private Slider _maxHealthSlider, _respawnDelaySlider, _nameplateSizeSlider, _nameplateYSlider;
        private readonly List<Toggle> _statToggles = new List<Toggle>();
        private readonly List<Slider> _statSliders = new List<Slider>();
        private RectTransform _statsContentRect, _statsViewportRect;

 // Pending equipment changes (prefab names)
        private Dictionary<EquipmentSlot, string> _pendingEquipment = new Dictionary<EquipmentSlot, string>();

    // Track dragged item state for visual feedback
        private ItemDrop.ItemData _lastDraggedItem;
        private EquipmentSlotUI _lastHoveredSlot;

        // Drag state for panel dragging
        private bool _isDragging;
        private Vector2 _dragOffset;
        private Vector2 _dragStartPos;

        // Drag-to-scroll state for open dropdown lists.
        // The native ScrollRect inside TMP_Dropdown templates is sometimes swallowed by
        // InventoryGui's event intercept (beard dropdown reproduces this reliably).
        // We implement a uniform manual fallback so every dropdown scrolls consistently.
        private bool _isDraggingDropdown;
        private float _dropdownDragStartMouseY;
        private float _dropdownDragStartContentY;
        private RectTransform _dropdownDragContent;

        // Colors - warm orange/gold theme matching main UI
      private static readonly Color SlotEmptyColor = new Color(0.12f, 0.10f, 0.08f, 0.6f);
        private static readonly Color SlotFilledColor = new Color(0.18f, 0.14f, 0.10f, 0.8f);
 private static readonly Color SlotValidDropColor = new Color(0.2f, 0.5f, 0.2f, 0.9f);
    private static readonly Color SlotInvalidDropColor = new Color(0.5f, 0.2f, 0.2f, 0.9f);
        private static readonly Color SlotBorderColor = new Color(0.6f, 0.45f, 0.25f, 0.6f);
   private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f);
    private static readonly Color LabelColor = new Color(0.9f, 0.75f, 0.45f);

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
    LeftBack,
            Pet   // 0.0.559: cosmetic pet (not gear; never in _pendingEquipment)
        }

        #region Lifecycle

        private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
     if (!IsVisible) return;

     // The patrol popup sits on top and owns all clicks; don't also process dressing clicks behind it.
     if (PatrolRoutePopup.IsOpen) return;

     // Handle Escape key to go back
       if (Input.GetKeyDown(KeyCode.Escape))
    {
     Debug.Log("[DressingRoomScreen] Escape key pressed - closing");
                Hide();
       return;
            }

     // CRITICAL: Manual click detection for buttons since InventoryGui blocks IPointerClickHandler
         // This runs every frame to check for clicks on our buttons
   if (Input.GetMouseButtonDown(0))
     {
    HandleManualButtonClick();
     }

            // Handle right-click on slots to clear them (separate from left-click handling)
          if (Input.GetMouseButtonDown(1))
      {
          HandleRightClick();
         }

    // Handle panel dragging
     HandlePanelDrag();

    // Manual drag-to-scroll for open dropdown lists (covers the case where
    // InventoryGui swallows the native ScrollRect drag, e.g. beard dropdown).
    HandleDropdownDrag();

    // Mouse-wheel scroll for the NPC stats strip (native ScrollRect is dead while the inventory is open).
    HandleStatsScroll();

    // Keep the fashion scrollbar visible + its handle in sync (also creates it on first run).
    UpdateFashionScrollbar();

      // Get current drag state for visual feedback only
            var draggedItem = GetDraggedItemFromInventory();
       var slotUnderMouse = GetSlotUnderMouse();

       if (draggedItem != null)
   {
        // Only update visuals when the hovered slot or dragged item actually changed -
        // setting Image.color every frame dirties the canvas even if the value is identical
        if (slotUnderMouse != _lastHoveredSlot || draggedItem != _lastDraggedItem)
        {
                UpdateSlotHoverVisuals(slotUnderMouse, draggedItem);
        }
         _lastDraggedItem = draggedItem;
                _lastHoveredSlot = slotUnderMouse;
   }
       else
 {
          // Reset tracking state when not dragging
        if (_lastDraggedItem != null)
       {
     _lastDraggedItem = null;
            _lastHoveredSlot = null;
           ResetSlotColors();
         }
}
        }

        /// <summary>
      /// Manually checks if any of our buttons were clicked.
        /// This bypasses Unity's event system which may be blocked by InventoryGui.
        /// </summary>
        private int _buttonClickFrame = -1;

        // The manual hit-test and the EventSystem onClick can both see one mouse click; the first one this frame runs it.
        private bool TakeButtonClick()
        {
            if (_buttonClickFrame == Time.frameCount) return false;
            _buttonClickFrame = Time.frameCount;
            return true;
        }

        private void OnApplyEvent() { if (TakeButtonClick()) { Debug.Log("[DressingRoomScreen] Apply button clicked (EventSystem)"); HandleApplyClicked(); } }
        private void OnClearAllEvent() { if (TakeButtonClick()) { Debug.Log("[DressingRoomScreen] Clear All button clicked (EventSystem)"); HandleClearAllClicked(); } }

        private void HandleManualButtonClick()
  {
          Vector2 mousePos = Input.mousePosition;

            // Check Apply button
            if (_applyButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_applyButtonRect, mousePos, null))
            {
 Debug.Log("[DressingRoomScreen] Apply button clicked via manual detection!");
           if (TakeButtonClick()) HandleApplyClicked();
    return;
        }

    // Check Clear All button
    if (_clearAllButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_clearAllButtonRect, mousePos, null))
 {
                Debug.Log("[DressingRoomScreen] Clear All button clicked via manual detection!");
         if (TakeButtonClick()) HandleClearAllClicked();
         return;
  }

   // Check main panel Close button
     if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
     {
      Debug.Log("[DressingRoomScreen] Close button clicked via manual detection!");
         OnCloseButtonClicked();
             return;
       }

            // Check main panel Prev/Back button
          if (_prevButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_prevButtonRect, mousePos, null))
            {
      Debug.Log("[DressingRoomScreen] Back button clicked via manual detection!");
   OnBackClicked();
         return;
 }

            // Check main panel Size button
            if (_sizeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_sizeButtonRect, mousePos, null))
   {
       Debug.Log("[DressingRoomScreen] Size button clicked via manual detection!");
     OnSizeButtonClicked();
          return;
            }

    // Check input fields - focus them when clicked
 if (_inputFields != null)
        {
         foreach (var input in _inputFields)
      {
  if (input == null) continue;
       var rt = input.GetComponent<RectTransform>();
  if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
    {
   try
        {
   Debug.Log($"[DressingRoomScreen] Focusing input field: {input.gameObject.name}");
         EventSystem.current?.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            input.Select();
      }
       catch (Exception ex)
          {
      Debug.LogWarning($"[DressingRoomScreen] Failed to focus input: {ex.Message}");
           }
      return;
             }
    }
      }

            // NPC stats toggles/sliders (scroll strip) — manual click, guarded to the visible viewport so a
            // control scrolled out of view isn't hit through the mask.
            if (_statsViewportRect != null && RectTransformUtility.RectangleContainsScreenPoint(_statsViewportRect, mousePos, null))
            {
                foreach (var tg in _statToggles)
                {
                    if (tg == null) continue;
                    var rt = tg.GetComponent<RectTransform>();
                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                    {
                        tg.isOn = !tg.isOn;
                        return;
                    }
                }
                foreach (var sl in _statSliders)
                {
                    if (sl == null) continue;
                    var rt = sl.GetComponent<RectTransform>();
                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null) &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mousePos, null, out var lp))
                    {
                        float frac = Mathf.Clamp01((lp.x - rt.rect.xMin) / rt.rect.width);
                        sl.value = sl.minValue + frac * (sl.maxValue - sl.minValue);
                        return;
                    }
                }
            }

            // Check our dropdown captions - Inventory may block pointer events so open dropdowns programmatically
            if (_dropdowns != null)
   {
    // First: if any dropdown template is open, check for clicks on its items
           foreach (var dd in _dropdowns)
                {
    if (dd == null) continue;
        var template = dd.template;
         if (template != null && template.gameObject.activeInHierarchy)
           {
   var content = template.Find("Viewport/Content") as RectTransform;
        if (content != null && RectTransformUtility.RectangleContainsScreenPoint(content, mousePos, null))
       {
  // Compute clicked index based on local Y (content pivot is top)
     if (RectTransformUtility.ScreenPointToLocalPointInRectangle(content, mousePos, null, out var localPoint))
        {
                // FIXED: Use 24px item height to match quest config
           float itemHeight = 24f;
            // FIXED: Account for scroll offset (content.anchoredPosition.y changes when scrolled)
        float scrolledY = -localPoint.y - content.anchoredPosition.y;
       int idx = Mathf.FloorToInt(scrolledY / itemHeight);
 idx = Mathf.Clamp(idx, 0, dd.options.Count - 1);

      Debug.Log($"[DressingRoomScreen] Dropdown click: localY={localPoint.y:F1}, scrollY={content.anchoredPosition.y:F1}, calculated idx={idx}, options={dd.options.Count}");

              try
            {
      dd.value = idx;
          dd.RefreshShownValue();
           dd.onValueChanged?.Invoke(idx);
          // Hide template
 dd.Hide();
        Debug.Log($"[DressingRoomScreen] Selected dropdown item {idx}: {dd.options[idx].text}");
    }
        catch (Exception ex)
{
     Debug.LogWarning($"[DressingRoomScreen] Failed to select dropdown item: {ex.Message}");
    }
  return;
       }
     }
            }
  }

   // Next: check clicks on dropdown captions to open them — ONLY inside the visible fashion-scroll
   // viewport AND never over an equipment slot, so a dropdown scrolled up behind the grid can't fire
   // when you drop an inventory item onto a slot (that was the reported bug).
           bool overFashionViewport = _statsViewportRect == null
                || RectTransformUtility.RectangleContainsScreenPoint(_statsViewportRect, mousePos, null);
           if (overFashionViewport && GetSlotUnderMouse() == null)
           foreach (var dd in _dropdowns)
                {
            if (dd == null) continue;
            var rt = dd.gameObject.GetComponent<RectTransform>();
         // The caption area is the dropdown's GameObject; if clicked, open dropdown
 if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
            {
        try
           {
                Debug.Log($"[DressingRoomScreen] Opening dropdown: {dd.gameObject.name}, options count: {dd.options.Count}");
      dd.Show();
    }
         catch (Exception ex)
   {
         Debug.LogWarning($"[DressingRoomScreen] Failed to show dropdown: {ex.Message}");
         }
  return;
        }
       }
    }
      }

        /// <summary>
      /// Handles right-click to clear equipment slots.
        /// This bypasses Unity's event system which may be blocked by InventoryGui.
        /// </summary>
        private void HandleRightClick()
        {
       var slot = GetSlotUnderMouse();
    if (slot != null && (slot.SlotType == EquipmentSlot.Pet || !string.IsNullOrEmpty(slot.CurrentPrefab)))
       {
  Debug.Log($"[DressingRoomScreen] Right-click on slot {slot.SlotType} - clearing and returning item");
     OnSlotCleared(slot.SlotType);
}
   }

        /// <summary>
        /// Handles panel dragging via the header area.
/// </summary>
        private void HandlePanelDrag()
        {
 if (_headerRect == null) return;

         Vector2 mousePos = Input.mousePosition;

  // Start drag on mouse down over header (but not over buttons)
            if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
    // Check if mouse is over header but not over any buttons
                if (RectTransformUtility.RectangleContainsScreenPoint(_headerRect, mousePos, null))
   {
          // Make sure we're not over the close button
        if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
          return;

            // Start dragging
  _isDragging = true;
          }
            }


            // End drag
      if (_isDragging && Input.GetMouseButtonUp(0))
  {
       _isDragging = false;
       }
      }

     /// <summary>
        /// Handles Size button click - cycles through panel sizes.
        /// </summary>
     private void OnSizeButtonClicked()
        {
        }

        // Mouse-wheel scrolls the NPC stats strip content while the cursor is over its viewport. The native
        // ScrollRect can't receive wheel events while the inventory holds the event system.
        private void HandleStatsScroll()
        {
            if (_statsContentRect == null || _statsViewportRect == null) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_statsViewportRect, Input.mousePosition, null)) return;

            float max = Mathf.Max(0f, _statsContentRect.rect.height - _statsViewportRect.rect.height);
            var pos = _statsContentRect.anchoredPosition;
            pos.y = Mathf.Clamp(pos.y - wheel * 24f, 0f, max);
            _statsContentRect.anchoredPosition = pos;
        }

        // Visible scrollbar for the fashion/appearance scroll area, so it's obvious the list scrolls. The scroll
        // itself is manual (HandleStatsScroll) because InventoryGui swallows the native ScrollRect, so this is a
        // lightweight indicator we position ourselves. raycastTarget off — it's purely visual (wheel-driven).
        private RectTransform _fashionScrollTrack, _fashionScrollHandle;
        private void EnsureFashionScrollbar()
        {
            if (_fashionScrollTrack != null || _statsViewportRect == null) return;

            var trackGo = new GameObject("FashionScrollTrack", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            trackGo.transform.SetParent(_statsViewportRect, false);
            _fashionScrollTrack = (RectTransform)trackGo.transform;
            _fashionScrollTrack.anchorMin = new Vector2(1f, 0f); _fashionScrollTrack.anchorMax = new Vector2(1f, 1f);
            _fashionScrollTrack.pivot = new Vector2(1f, 1f);
            _fashionScrollTrack.sizeDelta = new Vector2(7f, -4f);
            // Sit in the narrow gap between the appearance fields and the right anim column: -1 overlapped the
            // field/slider ends, +10 overshot into the right column — +4 lands in the gap, clear of both.
            _fashionScrollTrack.anchoredPosition = new Vector2(4f, -2f);
            var timg = trackGo.GetComponent<UnityEngine.UI.Image>();
            timg.color = new Color(0.20f, 0.14f, 0.07f, 0.45f); timg.raycastTarget = false;

            var handleGo = new GameObject("FashionScrollHandle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            handleGo.transform.SetParent(_fashionScrollTrack, false);
            _fashionScrollHandle = (RectTransform)handleGo.transform;
            _fashionScrollHandle.anchorMin = new Vector2(0f, 1f); _fashionScrollHandle.anchorMax = new Vector2(1f, 1f);
            _fashionScrollHandle.pivot = new Vector2(0.5f, 1f);
            _fashionScrollHandle.sizeDelta = new Vector2(0f, 40f);
            var himg = handleGo.GetComponent<UnityEngine.UI.Image>();
            himg.color = new Color(0.58f, 0.44f, 0.20f, 0.95f); himg.raycastTarget = false;
        }

        private void UpdateFashionScrollbar()
        {
            EnsureFashionScrollbar();
            if (_fashionScrollTrack == null || _statsViewportRect == null || _statsContentRect == null) return;

            float vpH = _statsViewportRect.rect.height;
            float contentH = _statsContentRect.rect.height;
            if (contentH <= vpH + 1f) { if (_fashionScrollTrack.gameObject.activeSelf) _fashionScrollTrack.gameObject.SetActive(false); return; }
            if (!_fashionScrollTrack.gameObject.activeSelf) _fashionScrollTrack.gameObject.SetActive(true);

            float trackH = _fashionScrollTrack.rect.height;
            float handleH = Mathf.Clamp(trackH * (vpH / contentH), 24f, trackH);
            _fashionScrollHandle.sizeDelta = new Vector2(0f, handleH);

            float maxScroll = Mathf.Max(1f, contentH - vpH);
            float frac = Mathf.Clamp01(_statsContentRect.anchoredPosition.y / maxScroll); // 0 = top
            _fashionScrollHandle.anchoredPosition = new Vector2(0f, -frac * (trackH - handleH));
        }

        /// <summary>
        /// Manually scrolls the currently open dropdown list by tracking mouse drag.
        /// Runs every frame; all state is local so it is safe to call even when no
        /// dropdown is open.
        /// </summary>
        private void HandleDropdownDrag()
        {
            if (_dropdowns == null) return;

            // Locate the one open dropdown template, and its Viewport/Content.
            RectTransform openContent = null;
            RectTransform openViewport = null;
            foreach (var dd in _dropdowns)
            {
                if (dd == null || dd.template == null) continue;
                if (!dd.template.gameObject.activeInHierarchy) continue;
                var vp = dd.template.Find("Viewport") as RectTransform;
                var ct = dd.template.Find("Viewport/Content") as RectTransform;
                if (ct == null) continue;
                openViewport = vp;
                openContent  = ct;
                break;
            }

            // No dropdown open - cancel any in-progress drag.
            if (openContent == null)
            {
                _isDraggingDropdown = false;
                _dropdownDragContent = null;
                return;
            }

            Vector2 mousePos = Input.mousePosition;

            // Begin drag when the mouse button goes down inside the viewport.
            if (Input.GetMouseButtonDown(0) && !_isDraggingDropdown)
            {
                if (openViewport != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(openViewport, mousePos, null))
                {
                    _isDraggingDropdown       = true;
                    _dropdownDragStartMouseY  = mousePos.y;
                    _dropdownDragStartContentY = openContent.anchoredPosition.y;
                    _dropdownDragContent      = openContent;
                }
            }

            // Continue drag - clamp so content never scrolls past its ends.
            if (_isDraggingDropdown && Input.GetMouseButton(0) && _dropdownDragContent == openContent)
            {
                float viewportH = openViewport != null ? openViewport.rect.height : 0f;
                float contentH  = openContent.rect.height;
                float maxScroll = Mathf.Max(0f, contentH - viewportH);

                // Dragging the mouse UP moves content down (positive anchoredPosition.y).
                float delta = mousePos.y - _dropdownDragStartMouseY;
                float newY  = Mathf.Clamp(_dropdownDragStartContentY - delta, 0f, maxScroll);
                openContent.anchoredPosition = new Vector2(openContent.anchoredPosition.x, newY);
            }

            // End drag.
            if (_isDraggingDropdown && Input.GetMouseButtonUp(0))
            {
                _isDraggingDropdown  = false;
                _dropdownDragContent = null;
            }
        }

    /// <summary>
        /// Updates the hover visual feedback on slots based on dragged item.
        /// </summary>
        private void UpdateSlotHoverVisuals(EquipmentSlotUI slotUnderMouse, ItemDrop.ItemData draggedItem)
        {
    foreach (var kvp in _slots)
            {
     var slot = kvp.Value;
           if (slot == slotUnderMouse)
          {
       bool isValid = IsValidItemForSlot(slot.SlotType, draggedItem);
             slot.Background.color = isValid ? SlotValidDropColor : SlotInvalidDropColor;
}
          else
      {
      // Reset to appropriate color
             slot.Background.color = string.IsNullOrEmpty(slot.CurrentPrefab)
        ? SlotEmptyColor
          : SlotFilledColor;
             }
            }
        }

        #endregion

        #region Factory

        /// <summary>
        /// Wires this controller to pre-built elements in the codegen hierarchy.
        /// Called by CodegenUIWiring.WireDressingRoomScreen().
        /// </summary>
        public void WireFromCodegen(GameObject screenGo)
        {
            screenRoot = screenGo;
            var t = screenGo.transform;

            // Instructions text
            instructionsText = CodegenUIWiring.FindTMP(t, "Instructions");

            // Buttons
            _clearAllButton = CodegenUIWiring.FindButton(t, "ClearAllButton");
            if (_clearAllButton != null)
                _clearAllButtonRect = _clearAllButton.GetComponent<RectTransform>();

            _applyButton = CodegenUIWiring.FindButton(t, "ApplyEquipmentButton");
            if (_applyButton != null)
                _applyButtonRect = _applyButton.GetComponent<RectTransform>();

            // EventSystem clicks too (a click that reaches the button: mouse when nothing swallows it, a gamepad submit, a test);
            // the manual hit-test above stays for the mouse while the InventoryGui swallows events. One run per frame either way.
            if (_clearAllButton != null) { _clearAllButton.onClick.RemoveListener(OnClearAllEvent); _clearAllButton.onClick.AddListener(OnClearAllEvent); }
            if (_applyButton != null) { _applyButton.onClick.RemoveListener(OnApplyEvent); _applyButton.onClick.AddListener(OnApplyEvent); }

            // Equipment grid and slots
            var gridTransform = CodegenUIWiring.FindChild(t, "EquipmentGrid");
            if (gridTransform != null)
            {
                WireEquipmentSlot(gridTransform, "Slot_Helmet", EquipmentSlot.Helmet);
                WireEquipmentSlot(gridTransform, "Slot_Chest", EquipmentSlot.Chest);
                WireEquipmentSlot(gridTransform, "Slot_Legs", EquipmentSlot.Legs);
                WireEquipmentSlot(gridTransform, "Slot_Shoulder", EquipmentSlot.Shoulder);
                WireEquipmentSlot(gridTransform, "Slot_Utility", EquipmentSlot.Utility);
                WireEquipmentSlot(gridTransform, "Slot_RightHand", EquipmentSlot.RightHand);
                WireEquipmentSlot(gridTransform, "Slot_LeftHand", EquipmentSlot.LeftHand);
                WireEquipmentSlot(gridTransform, "Slot_RightBack", EquipmentSlot.RightBack);
                WireEquipmentSlot(gridTransform, "Slot_LeftBack", EquipmentSlot.LeftBack);
                WirePetSlot(gridTransform);   // 0.0.559
            }

            // Fashion Panel Right (animation/sounds)
            var fashionRight = CodegenUIWiring.FindChild(t, "FashionPanel_Right");
            if (fashionRight != null)
            {
                _fashionPanelRect = fashionRight.GetComponent<RectTransform>();

                _periodicAnimationDropdown = FindDropdownInContainer(fashionRight, "Periodic Anim_Container");
                _periodicAnimationTimeInput = FindInputInContainer(fashionRight, "Interval(seconds)_Container", 0);
                _interactAnimationDropdown = FindDropdownInContainer(fashionRight, "Interact Anim_Container");
                _greetAnimationDropdown = FindDropdownInContainer(fashionRight, "Greet Anim_Container");
                _byeAnimationDropdown = FindDropdownInContainer(fashionRight, "Bye Anim_Container");
                _periodicSoundDropdown = FindDropdownInContainer(fashionRight, "Periodic Sound_Container");
                _periodicSoundTimeInput = FindInputInContainer(fashionRight, "Interval(seconds)_Container", 1);

                // Track dropdowns for manual click handling
                if (_periodicAnimationDropdown != null) _dropdowns.Add(_periodicAnimationDropdown);
                if (_interactAnimationDropdown != null) _dropdowns.Add(_interactAnimationDropdown);
                if (_greetAnimationDropdown != null) _dropdowns.Add(_greetAnimationDropdown);
                if (_byeAnimationDropdown != null) _dropdowns.Add(_byeAnimationDropdown);
                if (_periodicSoundDropdown != null) _dropdowns.Add(_periodicSoundDropdown);

                // Greet/Bye TEXT rows (kg speech-bubble parity). Runtime addition — the baked panel is never
                // rebaked; we retile the existing anchor-stacked rows a little tighter and clone the interval
                // input container twice for the two free-text fields.
                EnsureGreetByeTextRows(fashionRight);
                if (_greetTextInput != null) _inputFields.Add(_greetTextInput);
                if (_byeTextInput != null) _inputFields.Add(_byeTextInput);
            }

            // Fashion Panel Left (appearance)
            var fashionLeft = CodegenUIWiring.FindChild(t, "FashionPanel_Left");
            if (fashionLeft != null)
            {
                _fashionLeftPanelRect = fashionLeft.GetComponent<RectTransform>();

                _hairItemDropdown = FindDropdownInContainer(fashionLeft, "Hair_Container");
                _hairColorDropdown = FindDropdownInContainer(fashionLeft, "Hair Color_Container");
                _beardItemDropdown = FindDropdownInContainer(fashionLeft, "Beard_Container");
                _beardColorDropdown = FindDropdownInContainer(fashionLeft, "Beard Color_Container");
                _skinColorDropdown = FindDropdownInContainer(fashionLeft, "Skin Tone_Container");
                _eyeColorDropdown = FindDropdownInContainer(fashionLeft, "Eye Color_Container");
                _modelScaleInput = FindInputInContainer(fashionLeft, "ModelScale_Container", 0);

                if (_hairItemDropdown != null) _dropdowns.Add(_hairItemDropdown);
                if (_hairColorDropdown != null) _dropdowns.Add(_hairColorDropdown);
                if (_beardItemDropdown != null) _dropdowns.Add(_beardItemDropdown);
                if (_beardColorDropdown != null) _dropdowns.Add(_beardColorDropdown);
                if (_skinColorDropdown != null) _dropdowns.Add(_skinColorDropdown);
                if (_eyeColorDropdown != null) _dropdowns.Add(_eyeColorDropdown);
            }

            // NPC stats controls live at the bottom of the unified fashion scroll content.
            var statsContent = CodegenUIWiring.FindChild(t, "FashionContent");
            if (statsContent != null)
            {
                _statsContentRect = statsContent.GetComponent<RectTransform>();
                _statsViewportRect = statsContent.parent as RectTransform;

                _infiniteHealthToggle = FindToggleInContainer(statsContent, "InfiniteHealth_Container");
                _hideNameplateToggle = FindToggleInContainer(statsContent, "HideNameplate_Container");
                _maxHealthSlider = FindSliderInContainer(statsContent, "MaxHealth_Container");
                _respawnDelaySlider = FindSliderInContainer(statsContent, "RespawnDelay_Container");
                _nameplateSizeSlider = FindSliderInContainer(statsContent, "NameplateSize_Container");
                _nameplateYSlider = FindSliderInContainer(statsContent, "NameplateHeight_Container");

                if (_infiniteHealthToggle != null) _statToggles.Add(_infiniteHealthToggle);
                if (_hideNameplateToggle != null) _statToggles.Add(_hideNameplateToggle);
                HookStatSlider(_maxHealthSlider, "MaxHealth_Container", statsContent, true);
                HookStatSlider(_respawnDelaySlider, "RespawnDelay_Container", statsContent, true);
                HookStatSlider(_nameplateSizeSlider, "NameplateSize_Container", statsContent, false);
                HookStatSlider(_nameplateYSlider, "NameplateHeight_Container", statsContent, false);
            }

            // Track input fields for manual click handling
            if (_periodicAnimationTimeInput != null) _inputFields.Add(_periodicAnimationTimeInput);
            if (_periodicSoundTimeInput != null) _inputFields.Add(_periodicSoundTimeInput);
            if (_modelScaleInput != null) _inputFields.Add(_modelScaleInput);

            // Populate color dropdowns
            PopulateColorDropdown(_hairColorDropdown);
            PopulateColorDropdown(_beardColorDropdown);
            PopulateSkinColorDropdown(_skinColorDropdown);
            PopulateEyeColorDropdown(_eyeColorDropdown);

            screenGo.SetActive(false);

            Debug.Log($"[DressingRoomScreenController] WireFromCodegen - " +
                      $"Instructions={instructionsText != null}, " +
                      $"ClearAll={_clearAllButton != null}, Apply={_applyButton != null}, " +
                      $"Slots={_slots.Count}, Dropdowns={_dropdowns.Count}, Inputs={_inputFields.Count}, " +
                      $"FashionRight={_fashionPanelRect != null}, FashionLeft={_fashionLeftPanelRect != null}");
        }

        /// <summary>
        /// Wires a single equipment slot from the codegen hierarchy.
        /// </summary>
        private void WireEquipmentSlot(Transform gridTransform, string slotName, EquipmentSlot slotType)
        {
            var slotTransform = gridTransform.Find(slotName);
            if (slotTransform == null) return;

            var slotGO = slotTransform.gameObject;
            var slotRect = slotGO.GetComponent<RectTransform>();
            var slotBg = slotGO.GetComponent<Image>();
            var iconImage = slotTransform.Find("Icon")?.GetComponent<Image>();
            var labelText = slotTransform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var itemNameText = slotTransform.Find("ItemName")?.GetComponent<TextMeshProUGUI>();

            var slotUI = new EquipmentSlotUI
            {
                SlotType = slotType,
                Root = slotGO,
                RectTransform = slotRect,
                Background = slotBg,
                Icon = iconImage,
                Label = labelText,
                ItemName = itemNameText,
                CurrentPrefab = null
            };

            var clickHandler = slotGO.AddComponent<SlotClickHandler>();
            clickHandler.Initialize(this, slotUI);

            _slots[slotType] = slotUI;
        }

        /// <summary>
        /// Finds a TMP_Dropdown within a named container child.
        /// </summary>
        private static TMP_Dropdown FindDropdownInContainer(Transform parent, string containerName)
        {
            var container = parent.Find(containerName);
            return container?.GetComponentInChildren<TMP_Dropdown>(true);
        }

        /// <summary>
        /// Finds a TMP_InputField within a named container child.
        /// nth parameter handles duplicate container names (e.g. two "Interval(seconds)_Container").
        /// </summary>
        private static TMP_InputField FindInputInContainer(Transform parent, string containerName, int nth)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == containerName)
                {
                    if (count == nth)
                        return child.GetComponentInChildren<TMP_InputField>(true);
                    count++;
                }
            }
            return null;
        }

        // Finds the nth direct child with the given name (containers can share names, e.g. the two
        // "Interval(seconds)_Container" rows).
        private static Transform FindNthChild(Transform parent, string childName, int nth)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != childName) continue;
                if (count == nth) return child;
                count++;
            }
            return null;
        }

        /// <summary>
        /// Adds the Greet Text / Bye Text input rows to the baked FashionPanel_Right at runtime (never
        /// rebake). The panel's rows are anchor-stacked (no layout group), so we retile the 7 baked rows
        /// plus the 2 clones into a 9-row grid, keeping the original top/bottom span. The clones are copies
        /// of the interval input container with the label retitled and the input switched to free text.
        /// </summary>
        private void EnsureGreetByeTextRows(Transform fashionRight)
        {
            try
            {
                if (fashionRight == null || fashionRight.Find("Greet Text_Container") != null) return;

                var intervalDonor = FindNthChild(fashionRight, "Interval(seconds)_Container", 0);
                if (intervalDonor == null) return;

                _greetTextInput = CloneTextRow(fashionRight, intervalDonor, "Greet Text_Container", "Greet Text", "Shown when a player walks up");
                _byeTextInput = CloneTextRow(fashionRight, intervalDonor, "Bye Text_Container", "Bye Text", "Shown when a player leaves");

                // Row order top→bottom, greet/bye text grouped under their animation rows.
                var rows = new List<Transform>
                {
                    fashionRight.Find("Periodic Anim_Container"),
                    FindNthChild(fashionRight, "Interval(seconds)_Container", 0),
                    fashionRight.Find("Interact Anim_Container"),
                    fashionRight.Find("Greet Anim_Container"),
                    fashionRight.Find("Greet Text_Container"),
                    fashionRight.Find("Bye Anim_Container"),
                    fashionRight.Find("Bye Text_Container"),
                    fashionRight.Find("Periodic Sound_Container"),
                    FindNthChild(fashionRight, "Interval(seconds)_Container", 1),
                };

                const float top = 0.96f, bottom = 0.02f;
                float step = (top - bottom) / rows.Count;
                float rowH = step * 0.9f;
                for (int i = 0; i < rows.Count; i++)
                {
                    var rect = rows[i] != null ? rows[i].GetComponent<RectTransform>() : null;
                    if (rect == null) continue;
                    float yMax = top - i * step;
                    rect.anchorMin = new Vector2(rect.anchorMin.x, yMax - rowH);
                    rect.anchorMax = new Vector2(rect.anchorMax.x, yMax);
                    rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
                    rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DressingRoomScreen] Greet/Bye text row setup failed: {ex.Message}");
            }
        }

        // Clone an input container row, retitle its label, and convert the input to free text.
        private static TMP_InputField CloneTextRow(Transform parent, Transform donor, string containerName, string label, string placeholder)
        {
            var clone = UnityEngine.Object.Instantiate(donor.gameObject, parent, false).transform;
            clone.name = containerName;

            // Retitle: the row label is the TMP that is NOT part of the input field's own hierarchy.
            var input = clone.GetComponentInChildren<TMP_InputField>(true);
            foreach (var tmp in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (input != null && tmp.transform.IsChildOf(input.transform)) continue;
                tmp.text = label;
                break;
            }

            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Standard;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.characterLimit = 120;
                input.text = "";
                if (input.placeholder is TextMeshProUGUI ph) ph.text = placeholder;
            }
            return input;
        }

        private static Toggle FindToggleInContainer(Transform parent, string containerName)
        {
            var container = parent.Find(containerName);
            return container?.GetComponentInChildren<Toggle>(true);
        }

        private static Slider FindSliderInContainer(Transform parent, string containerName)
        {
            var container = parent.Find(containerName);
            return container?.GetComponentInChildren<Slider>(true);
        }

        // Registers a stats slider for manual handling and keeps its right-side value label in sync.
        private void HookStatSlider(Slider slider, string containerName, Transform statsContent, bool whole)
        {
            if (slider == null) return;
            _statSliders.Add(slider);
            var valText = statsContent.Find(containerName)?.Find("Val")?.GetComponent<TextMeshProUGUI>();
            if (valText != null)
            {
                slider.onValueChanged.AddListener(v => valText.text = whole ? ((int)v).ToString() : v.ToString("0.0"));
                valText.text = whole ? ((int)slider.value).ToString() : slider.value.ToString("0.0");
            }
        }

        public static DressingRoomScreenController Create(Transform parent)
        {
            var rootGO = new GameObject("DressingRoomScreen");
  rootGO.transform.SetParent(parent, false);

            var rootRect = rootGO.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0, 0.02f);
      rootRect.anchorMax = new Vector2(1, 0.98f);
            rootRect.offsetMin = new Vector2(12, 8);
         rootRect.offsetMax = new Vector2(-12, -8);

            var bgImage = rootGO.AddComponent<Image>();
 bgImage.color = UIFontConfig.Colors.FormSectionBackground;
  bgImage.raycastTarget = true;

            var clip = rootGO.AddComponent<RectMask2D>();

        var canvasGroup = rootGO.AddComponent<CanvasGroup>();
       canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
   canvasGroup.alpha = 1f;

         var controller = rootGO.AddComponent<DressingRoomScreenController>();
  controller.screenRoot = rootGO;

      controller.CreateEquipmentSlots(rootGO.transform);
            controller.CreateInstructions(rootGO.transform);
 controller.CreateButtons(rootGO.transform);
        controller.CreateFashionPanel(rootGO.transform);

  rootGO.SetActive(false);

         Debug.Log($"[DressingRoomScreen] Created screen with Apply button: {controller._applyButton != null}");
         return controller;
 }

      private void CreateEquipmentSlots(Transform parent)
        {
      var gridGO = new GameObject("EquipmentGrid");
            gridGO.transform.SetParent(parent, false);

          var gridRect = gridGO.AddComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.06f, 0.36f);
            gridRect.anchorMax = new Vector2(0.94f, 0.96f);
    gridRect.offsetMin = Vector2.zero;
            gridRect.offsetMax = Vector2.zero;

     float slotSize = 0.16f;
         float shiftDown = 1.5f * slotSize;
      float centerX = 0.5f;
         float[] centers = new float[] { 0.18f, 0.34f, centerX, 0.66f, 0.82f };

        CreateSlot(gridRect, EquipmentSlot.Helmet, "Helmet", centerX - slotSize / 2f, 0.94f - shiftDown, slotSize);

           float middleY = 0.78f - shiftDown;
     CreateSlot(gridRect, EquipmentSlot.LeftHand, "L. Hand", centers[0] - slotSize / 2f, middleY, slotSize);
    CreateSlot(gridRect, EquipmentSlot.LeftBack, "L. Back", centers[1] - slotSize / 2f, middleY, slotSize);
     CreateSlot(gridRect, EquipmentSlot.Shoulder, "Cape", centers[2] - slotSize / 2f, middleY, slotSize);
    CreateSlot(gridRect, EquipmentSlot.RightBack, "R. Back", centers[3] - slotSize / 2f, middleY, slotSize);
             CreateSlot(gridRect, EquipmentSlot.RightHand, "R. Hand", centers[4] - slotSize / 2f, middleY, slotSize);

        // Calculate vertical positions so slots touch edge-to-edge (no gaps)
             // Cape/Shoulder row bottom edge is at: middleY
 // So Chest top should be at middleY, meaning chestY = middleY - slotSize
             float chestY = middleY - slotSize;
     float utilityY = chestY - slotSize;
           float legsY = utilityY - slotSize;

             CreateSlot(gridRect, EquipmentSlot.Chest, "Chest", centerX - slotSize / 2f, chestY, slotSize);
             CreateSlot(gridRect, EquipmentSlot.Utility, "Utility", centerX - slotSize / 2f, utilityY, slotSize);
     CreateSlot(gridRect, EquipmentSlot.Legs, "Legs", centerX - slotSize / 2f, legsY, slotSize);
      }

        private void CreateSlot(RectTransform parent, EquipmentSlot slot, String label, float xPos, float yPos, float size)
        {
         var slotGO = new GameObject($"Slot_{slot}");
   slotGO.transform.SetParent(parent, false);

       var slotRect = slotGO.AddComponent<RectTransform>();
      slotRect.anchorMin = new Vector2(xPos, yPos);
            slotRect.anchorMax = new Vector2(xPos + size, yPos + size);
     slotRect.offsetMin = Vector2.zero;
            slotRect.offsetMax = Vector2.zero;

          var slotBg = slotGO.AddComponent<Image>();
        slotBg.color = SlotEmptyColor;
            slotBg.raycastTarget = true;

     var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(slotGO.transform, false);
            var borderRect = borderGO.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
      borderRect.offsetMin = new Vector2(-2, -2);
            borderRect.offsetMax = new Vector2(2, 2);
       var borderImage = borderGO.AddComponent<Image>();
   borderImage.color = SlotBorderColor;
       borderImage.raycastTarget = false;
      borderImage.type = Image.Type.Sliced;
         borderImage.fillCenter = false;
   borderGO.transform.SetAsFirstSibling();

     var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
  iconRect.anchorMin = new Vector2(0.1f, 0.25f);
   iconRect.anchorMax = new Vector2(0.9f, 0.95f);
           iconRect.offsetMin = Vector2.zero;
      iconRect.offsetMax = Vector2.zero;
            var iconImage = iconGO.AddComponent<Image>();
           iconImage.color = new Color(1, 1, 1, 0.3f);
    iconImage.preserveAspect = true;
    iconImage.raycastTarget = false;

     var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(slotGO.transform, false);
    var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 0.25f);
         labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
     labelText.fontSize = 10;
            labelText.color = LabelColor;
            labelText.alignment = TextAlignmentOptions.Center;
            UIBuilderHelper.ApplyValheimFont(labelText);

             var itemNameGO = new GameObject("ItemName");
   itemNameGO.transform.SetParent(slotGO.transform, false);
     var itemNameRect = itemNameGO.AddComponent<RectTransform>();
 itemNameRect.anchorMin = new Vector2(0, 0);
           itemNameRect.anchorMax = new Vector2(1, 0.22f);
      itemNameRect.offsetMin = Vector2.zero;
       itemNameRect.offsetMax = Vector2.zero;
       var itemNameText = itemNameGO.AddComponent<TextMeshProUGUI>();
      itemNameText.text = "";
         itemNameText.fontSize = 8;
            itemNameText.color = TextColor;
  itemNameText.alignment = TextAlignmentOptions.Center;
        itemNameText.textWrappingMode = TextWrappingModes.NoWrap;
      itemNameText.overflowMode = TextOverflowModes.Ellipsis;
       UIBuilderHelper.ApplyValheimFont(itemNameText);
            itemNameGO.SetActive(false);

             var slotUI = new EquipmentSlotUI
      {
        SlotType = slot,
             Root = slotGO,
          RectTransform = slotRect,
  Background = slotBg,
        Icon = iconImage,
                 Label = labelText,
              ItemName = itemNameText,
       CurrentPrefab = null
            };

      var clickHandler = slotGO.AddComponent<SlotClickHandler>();
         clickHandler.Initialize(this, slotUI);

             _slots[slot] = slotUI;
         }

        private void CreateInstructions(Transform parent)
        {
       var instructionsGO = new GameObject("Instructions");
           instructionsGO.transform.SetParent(parent, false);

            var instructionsRect = instructionsGO.AddComponent<RectTransform>();
            instructionsRect.anchorMin = new Vector2(0, 0.12f);
 instructionsRect.anchorMax = new Vector2(1, 0.20f);
         instructionsRect.offsetMin = new Vector2(14, 6);
    instructionsRect.offsetMax = new Vector2(-14, 0);

       instructionsText = instructionsGO.AddComponent<TextMeshProUGUI>();
 instructionsText.text = "Drag items from inventory to slots. Right-click slot to clear.\nItems are consumed from inventory when equipped.";
      instructionsText.fontSize = 10;
    instructionsText.color = UIFontConfig.Colors.MutedGold;
      instructionsText.alignment = TextAlignmentOptions.Center;
       instructionsText.textWrappingMode = TextWrappingModes.Normal;
       instructionsText.overflowMode = TextOverflowModes.Ellipsis;
            instructionsText.margin = new Vector4(6, 2, 6, 2);
  UIBuilderHelper.ApplyValheimFont(instructionsText);
     }

        private void CreateButtons(Transform parent)
    {
      Debug.Log($"[DressingRoomScreen] CreateButtons called, parent: {parent?.name}");

             _clearAllButton = CreateDressingRoomButton(parent, "ClearAllButton", "Clear All",
           new Vector2(0.12f, 0.01f), new Vector2(0.38f, 0.06f));
             _clearAllButtonRect = _clearAllButton.GetComponent<RectTransform>();

           _applyButton = CreateDressingRoomButton(parent, "ApplyEquipmentButton", "Apply Changes",
        new Vector2(0.62f, 0.01f), new Vector2(0.88f, 0.06f));
   _applyButtonRect = _applyButton.GetComponent<RectTransform>();

             Debug.Log($"[DressingRoomScreen] CreateButtons complete - Apply: {_applyButton != null}, Clear: {_clearAllButton != null}");
         }

        private Button CreateDressingRoomButton(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax)
        {
     var buttonGO = new GameObject(name);
            buttonGO.transform.SetParent(parent, false);

      var buttonRect = buttonGO.AddComponent<RectTransform>();
            buttonRect.anchorMin = anchorMin;
          buttonRect.anchorMax = anchorMax;
     buttonRect.offsetMin = Vector2.zero;
          buttonRect.offsetMax = Vector2.zero;

        var buttonImg = buttonGO.AddComponent<Image>();
         buttonImg.color = UIFontConfig.Colors.ButtonNormal;
            buttonImg.raycastTarget = true;

   var button = buttonGO.AddComponent<Button>();
         button.targetGraphic = buttonImg;
  button.interactable = true;

            var colors = button.colors;
            colors.normalColor = UIFontConfig.Colors.ButtonNormal;
 colors.highlightedColor = UIFontConfig.Colors.ButtonHover;
            colors.pressedColor = UIFontConfig.Colors.ButtonPressed;
          colors.selectedColor = UIFontConfig.Colors.ButtonNormal;
     button.colors = colors;

    var hoverHandler = buttonGO.AddComponent<DressingRoomButtonHoverHandler>();
          hoverHandler.Initialize(buttonImg);

        var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);

    var textRect = textGO.AddComponent<RectTransform>();
          textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
   textRect.offsetMin = Vector2.zero;
          textRect.offsetMax = Vector2.zero;

 var buttonText = textGO.AddComponent<TextMeshProUGUI>();
            buttonText.text = text;
   buttonText.alignment = TextAlignmentOptions.Center;
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.raycastTarget = false;
        UIFontConfig.ApplyStyle(buttonText, UIFontConfig.ButtonText);

            buttonImg.color = new Color(UIFontConfig.Colors.ButtonNormal.r, UIFontConfig.Colors.ButtonNormal.g, UIFontConfig.Colors.ButtonNormal.b, 0.95f);

            return button;
     }

        private void CreateFashionPanel(Transform parent)
        {
            // Right fashion panel (animation / sounds) - MOVED DOWN 1 more pixel
            var panelGO = new GameObject("FashionPanel_Right");
            panelGO.transform.SetParent(parent, false);

            var rect = panelGO.AddComponent<RectTransform>();
            // Right fashion panel - moved down 1 pixel (from 0.21-0.69 to 0.20-0.68)
            rect.anchorMin = new Vector2(0.62f, 0.20f);
            rect.anchorMax = new Vector2(0.96f, 0.68f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var bg = panelGO.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.07f, 0.06f, 0.6f);
            bg.raycastTarget = true;
            _fashionPanelRect = rect;

            // Left fashion panel - MOVED DOWN 1px and RIGHT 1px
            var leftPanelGO = new GameObject("FashionPanel_Left");
            leftPanelGO.transform.SetParent(parent, false);
            var leftRect = leftPanelGO.AddComponent<RectTransform>();
            // Left panel: moved down 1px (0.21->0.20, 0.69->0.68), moved right 1px (0.02->0.03)
            leftRect.anchorMin = new Vector2(0.03f, 0.20f);
            leftRect.anchorMax = new Vector2(0.37f, 0.68f);
            leftRect.offsetMin = Vector2.zero;
            leftRect.offsetMax = Vector2.zero;
            var leftBg = leftPanelGO.AddComponent<Image>();
            leftBg.color = new Color(0.08f, 0.07f, 0.06f, 0.5f);
            leftBg.raycastTarget = true;
            _fashionLeftPanelRect = leftRect;

            float yRight = 0.96f;
         float yLeft = 0.96f;
    float lineH = 0.08f;  // Increased line height for better spacing (was 0.065f)

            // Helper to create labeled dropdown - INCREASED FONT SIZE by 2 points and INCREASED HEIGHT
            TMP_Dropdown CreateLabeledDropdown(Transform parentTransform, string labelText, ref float yPos, float lineHeight)
     {
 var container = new GameObject(labelText + "_Container");
    container.transform.SetParent(parentTransform, false);
 var r = container.AddComponent<RectTransform>();
        // Increased height for more spacing
 r.anchorMin = new Vector2(0.02f, yPos - lineHeight * 1.5f);
   r.anchorMax = new Vector2(0.98f, yPos);
     r.offsetMin = Vector2.zero;
    r.offsetMax = Vector2.zero;

    var lblGO = new GameObject("Label");
 lblGO.transform.SetParent(container.transform, false);
          var lblRect = lblGO.AddComponent<RectTransform>();
  // Label in upper portion - more space above dropdown
     lblRect.anchorMin = new Vector2(0, 0.55f);
     lblRect.anchorMax = new Vector2(1, 1);
    lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
           var lbl = lblGO.AddComponent<TextMeshProUGUI>();
       lbl.text = labelText;
  lbl.fontSize = 10;
     lbl.color = LabelColor;
       lbl.alignment = TextAlignmentOptions.BottomLeft;  // Align to bottom of label area
    UIBuilderHelper.ApplyValheimFont(lbl);

   var ddGO = new GameObject("Dropdown");
        ddGO.transform.SetParent(container.transform, false);
    var ddRect = ddGO.AddComponent<RectTransform>();
 // Dropdown in lower portion with gap from label
      ddRect.anchorMin = new Vector2(0, 0);
                ddRect.anchorMax = new Vector2(1, 0.48f);  // Leave gap between label and dropdown
            ddRect.offsetMin = Vector2.zero;
 ddRect.offsetMax = Vector2.zero;
 var bgImg = ddGO.AddComponent<Image>();
    bgImg.color = new Color(0.1f, 0.08f, 0.06f, 0.7f);
   bgImg.raycastTarget = true;

    // Caption text - MUST be named "Label" and be direct child of dropdown (matching QuestConfigScreenController)
     var captionGO = new GameObject("Label");
           captionGO.transform.SetParent(ddGO.transform, false);
      var captionRect = captionGO.AddComponent<RectTransform>();
       captionRect.anchorMin = Vector2.zero;
    captionRect.anchorMax = Vector2.one;
    captionRect.offsetMin = new Vector2(6, 0);
         captionRect.offsetMax = new Vector2(-20, 0);

    var caption = captionGO.AddComponent<TextMeshProUGUI>();
    caption.fontSize = 11;
         caption.color = TextColor;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
       caption.raycastTarget = false;
  caption.textWrappingMode = TextWrappingModes.NoWrap;
     caption.overflowMode = TextOverflowModes.Ellipsis;
     UIBuilderHelper.ApplyValheimFont(caption);

    var arrowGO = new GameObject("Arrow");
      arrowGO.transform.SetParent(ddGO.transform, false);
        var arrowRect = arrowGO.AddComponent<RectTransform>();
 arrowRect.anchorMin = new Vector2(1, 0);
 arrowRect.anchorMax = new Vector2(1, 1);
     arrowRect.pivot = new Vector2(1, 0.5f);
        arrowRect.sizeDelta = new Vector2(18, 0);
        arrowRect.anchoredPosition = new Vector2(-2, 0);
 var arrow = arrowGO.AddComponent<TextMeshProUGUI>();
     arrow.text = "v";
       arrow.fontSize = 10;
arrow.color = LabelColor;
  arrow.alignment = TextAlignmentOptions.Center;
        arrow.raycastTarget = false;
       UIBuilderHelper.ApplyValheimFont(arrow);

        // Template setup (matching QuestConfigScreenController structure)
    var templateGO = new GameObject("Template");
    templateGO.transform.SetParent(ddGO.transform, false);
    var templateRect = templateGO.AddComponent<RectTransform>();
   templateRect.anchorMin = new Vector2(0, 0);
          templateRect.anchorMax = new Vector2(1, 0);
  templateRect.pivot = new Vector2(0.5f, 1);
templateRect.anchoredPosition = new Vector2(0, 0);
         templateRect.sizeDelta = new Vector2(0, 140);
   var templateBg = templateGO.AddComponent<Image>();
     templateBg.color = new Color(0.05f, 0.05f, 0.07f, 0.98f);

    var viewportGO = new GameObject("Viewport");
         viewportGO.transform.SetParent(templateGO.transform, false);
     var viewportRect = viewportGO.AddComponent<RectTransform>();
       viewportRect.anchorMin = Vector2.zero;
     viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(2, 4);
      viewportRect.offsetMax = new Vector2(-12, -4);
  viewportGO.AddComponent<RectMask2D>();

              var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRect = contentGO.AddComponent<RectTransform>();
contentRect.anchorMin = new Vector2(0, 1);
      contentRect.anchorMax = new Vector2(1, 1);
     contentRect.pivot = new Vector2(0, 1);
      contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 24);

   // Scrollbar
var scrollbarGO = new GameObject("Scrollbar");
   scrollbarGO.transform.SetParent(templateGO.transform, false);
 var scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
     scrollbarRect.anchorMin = new Vector2(1, 0);
       scrollbarRect.anchorMax = new Vector2(1, 1);
          scrollbarRect.pivot = new Vector2(1, 0.5f);
  scrollbarRect.anchoredPosition = new Vector2(-2, 0);
  scrollbarRect.sizeDelta = new Vector2(8, -8);
     var scrollbarBg = scrollbarGO.AddComponent<Image>();
        scrollbarBg.color = new Color(0.15f, 0.15f, 0.18f, 0.8f);
      var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
    scrollbar.direction = Scrollbar.Direction.BottomToTop;

     var slidingGO = new GameObject("SlidingArea");
 slidingGO.transform.SetParent(scrollbarGO.transform, false);
        var slidingRect = slidingGO.AddComponent<RectTransform>();
       slidingRect.anchorMin = Vector2.zero;
  slidingRect.anchorMax = Vector2.one;
 slidingRect.offsetMin = Vector2.zero;
         slidingRect.offsetMax = Vector2.zero;

    var handleGO = new GameObject("Handle");
          handleGO.transform.SetParent(slidingGO.transform, false);
      var handleRect = handleGO.AddComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0, 0);
  handleRect.anchorMax = new Vector2(1, 0.3f);
      handleRect.offsetMin = Vector2.zero;
   handleRect.offsetMax = Vector2.zero;
        var handleImg = handleGO.AddComponent<Image>();
   handleImg.color = new Color(0.6f, 0.45f, 0.25f, 0.9f);

 scrollbar.handleRect = handleRect;
             scrollbar.targetGraphic = handleImg;

  var scroll = templateGO.AddComponent<ScrollRect>();
   scroll.horizontal = false;
      scroll.vertical = true;
         scroll.movementType = ScrollRect.MovementType.Clamped;
scroll.scrollSensitivity = 120f;  // Higher value = faster scrolling
             scroll.inertia = false;
           scroll.viewport = viewportRect;
   scroll.content = contentRect;
       scroll.verticalScrollbar = scrollbar;
          scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

         // Item template
    var itemGO = new GameObject("Item");
     itemGO.transform.SetParent(contentGO.transform, false);
            var itemRect = itemGO.AddComponent<RectTransform>();
     itemRect.anchorMin = new Vector2(0, 0.5f);
          itemRect.anchorMax = new Vector2(1, 0.5f);
 itemRect.pivot = new Vector2(0.5f, 0.5f);
          itemRect.sizeDelta = new Vector2(0, 24);
      itemRect.anchoredPosition = Vector2.zero;

    var itemBgGO = new GameObject("Item Background");
         itemBgGO.transform.SetParent(itemGO.transform, false);
     var itemBgRect = itemBgGO.AddComponent<RectTransform>();
         itemBgRect.anchorMin = Vector2.zero;
   itemBgRect.anchorMax = Vector2.one;
       itemBgRect.offsetMin = Vector2.zero;
itemBgRect.offsetMax = Vector2.zero;
        var itemBg = itemBgGO.AddComponent<Image>();
    itemBg.color = new Color(0.12f, 0.10f, 0.08f, 0.8f);

     var toggle = itemGO.AddComponent<Toggle>();
  toggle.targetGraphic = itemBg;

        var checkGO = new GameObject("Item Checkmark");
     checkGO.transform.SetParent(itemGO.transform, false);
     var checkRect = checkGO.AddComponent<RectTransform>();
   checkRect.anchorMin = new Vector2(0, 0.5f);
       checkRect.anchorMax = new Vector2(0, 0.5f);
        checkRect.pivot = new Vector2(0, 0.5f);
       checkRect.anchoredPosition = new Vector2(4, 0);
        checkRect.sizeDelta = new Vector2(12, 12);
      var checkImg = checkGO.AddComponent<Image>();
 checkImg.color = new Color(1f, 0.7f, 0.3f);
           toggle.graphic = checkImg;
          toggle.isOn = true;

    // Item Label - this is what TMP_Dropdown uses for item text
             var itemLabelGO = new GameObject("Item Label");
             itemLabelGO.transform.SetParent(itemGO.transform, false);
  var itemLabelRect = itemLabelGO.AddComponent<RectTransform>();
   itemLabelRect.anchorMin = Vector2.zero;
           itemLabelRect.anchorMax = Vector2.one;
      itemLabelRect.pivot = new Vector2(0.5f, 0.5f);
  itemLabelRect.offsetMin = new Vector2(22, 2);
       itemLabelRect.offsetMax = new Vector2(-6, -2);
          var itemLabel = itemLabelGO.AddComponent<TextMeshProUGUI>();
        itemLabel.fontSize = 11;
   itemLabel.color = TextColor;
   itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
itemLabel.raycastTarget = false;
     itemLabel.textWrappingMode = TextWrappingModes.NoWrap;
 itemLabel.overflowMode = TextOverflowModes.Ellipsis;
         UIBuilderHelper.ApplyValheimFont(itemLabel);

      templateGO.SetActive(false);

   var dropdown = ddGO.AddComponent<TMP_Dropdown>();
 dropdown.template = templateRect;
      dropdown.captionText = caption;
    dropdown.itemText = itemLabel;
       dropdown.ClearOptions();
       dropdown.AddOptions(new List<string> { "(none)" });
 dropdown.value = 0;
        dropdown.RefreshShownValue();

  _dropdowns.Add(dropdown);

   yPos -= lineHeight * 1.6f + 0.01f;  // Increased spacing between elements
 return dropdown;
   }

        // Helper to create input field BELOW hint text (stacked layout) - INCREASED HEIGHT
        TMP_InputField CreateStackedInputField(Transform parentTransform, string hintText, ref float yPos, float lineHeight, bool freeText = false)
{
         var container = new GameObject(hintText.Replace(" ", "") + "_Container");
       container.transform.SetParent(parentTransform, false);
            var r = container.AddComponent<RectTransform>();
          r.anchorMin = new Vector2(0.02f, yPos - lineHeight * 1.5f);
            r.anchorMax = new Vector2(0.98f, yPos);
            r.offsetMin = Vector2.zero;
 r.offsetMax = Vector2.zero;

  var lblGO = new GameObject("HintLabel");
   lblGO.transform.SetParent(container.transform, false);
    var lblRect = lblGO.AddComponent<RectTransform>();
   lblRect.anchorMin = new Vector2(0, 0.55f);  // More space for label
     lblRect.anchorMax = new Vector2(1, 1);
  lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
    var lbl = lblGO.AddComponent<TextMeshProUGUI>();
  lbl.text = hintText;
    lbl.fontSize = 9;
     lbl.color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.7f);
     lbl.alignment = TextAlignmentOptions.BottomLeft;  // Align to bottom
   lbl.fontStyle = FontStyles.Italic;
            UIBuilderHelper.ApplyValheimFont(lbl);

     var inputGO = new GameObject("Input");
  inputGO.transform.SetParent(container.transform, false);
        var inputRect = inputGO.AddComponent<RectTransform>();
         inputRect.anchorMin = new Vector2(0, 0);
       inputRect.anchorMax = new Vector2(1, 0.48f);  // Leave gap between label and input
     inputRect.offsetMin = Vector2.zero;
    inputRect.offsetMax = Vector2.zero;
       var img = inputGO.AddComponent<Image>();
     img.color = new Color(0.1f, 0.08f, 0.06f, 0.7f);
   img.raycastTarget = true;

    var viewportGO = new GameObject("Viewport");
   viewportGO.transform.SetParent(inputGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
    viewportRect.anchorMin = Vector2.zero;
       viewportRect.anchorMax = Vector2.one;
    viewportRect.offsetMin = new Vector2(6, 2);
    viewportRect.offsetMax = new Vector2(-6, -2);
            viewportGO.AddComponent<RectMask2D>();

         var textGO = new GameObject("Text");
          textGO.transform.SetParent(viewportGO.transform, false);
  var textRect = textGO.AddComponent<RectTransform>();
    textRect.anchorMin = Vector2.zero;
       textRect.anchorMax = Vector2.one;
      textRect.offsetMin = Vector2.zero;
       textRect.offsetMax = Vector2.zero;
          var text = textGO.AddComponent<TextMeshProUGUI>();
     text.fontSize = 11;
   text.color = TextColor;
         text.alignment = TextAlignmentOptions.Center;
          text.raycastTarget = false;
    UIBuilderHelper.ApplyValheimFont(text);

       var placeholderGO = new GameObject("Placeholder");
  placeholderGO.transform.SetParent(viewportGO.transform, false);
   var placeholderRect = placeholderGO.AddComponent<RectTransform>();
     placeholderRect.anchorMin = Vector2.zero;
    placeholderRect.anchorMax = Vector2.one;
   placeholderRect.offsetMin = Vector2.zero;
  placeholderRect.offsetMax = new Vector2(-6, -2);
            var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
      placeholder.text = "0";
     placeholder.fontSize = 11;
    placeholder.color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.5f);
            placeholder.alignment = TextAlignmentOptions.Center;
placeholder.raycastTarget = false;
  UIBuilderHelper.ApplyValheimFont(placeholder);

       var input = inputGO.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
   input.textComponent = text;
   input.placeholder = placeholder;
      input.fontAsset = text.font;
          input.pointSize = 11;
            input.lineType = TMP_InputField.LineType.SingleLine;
     input.contentType = freeText ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.DecimalNumber;
     if (freeText) input.characterLimit = 120;
     input.caretColor = new Color(1f, 0.9f, 0.6f);
    input.selectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);
input.interactable = true;

_inputFields.Add(input);

  yPos -= lineHeight * 1.6f + 0.01f;  // Increased spacing between elements
    return input;
      }

        // RIGHT PANEL - Periodic animations/sounds + Greet/Bye animations
 _periodicAnimationDropdown = CreateLabeledDropdown(panelGO.transform, "Periodic Anim", ref yRight, lineH);
        _periodicAnimationTimeInput = CreateStackedInputField(panelGO.transform, "Interval (seconds)", ref yRight, lineH);
        _interactAnimationDropdown = CreateLabeledDropdown(panelGO.transform, "Interact Anim", ref yRight, lineH);
        _greetAnimationDropdown = CreateLabeledDropdown(panelGO.transform, "Greet Anim", ref yRight, lineH);
        _byeAnimationDropdown = CreateLabeledDropdown(panelGO.transform, "Bye Anim", ref yRight, lineH);
        _greetTextInput = CreateStackedInputField(panelGO.transform, "Greet Text", ref yRight, lineH, freeText: true);
        _byeTextInput = CreateStackedInputField(panelGO.transform, "Bye Text", ref yRight, lineH, freeText: true);
     _periodicSoundDropdown = CreateLabeledDropdown(panelGO.transform, "Periodic Sound", ref yRight, lineH);
     _periodicSoundTimeInput = CreateStackedInputField(panelGO.transform, "Interval (seconds)", ref yRight, lineH);

  // LEFT PANEL - Appearance (Hair, Beard, Skin, Eyes, Scale)
     _hairItemDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Hair", ref yLeft, lineH);
        _hairColorDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Hair Color", ref yLeft, lineH);
      _beardItemDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Beard", ref yLeft, lineH);
      _beardColorDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Beard Color", ref yLeft, lineH);
 _skinColorDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Skin Tone", ref yLeft, lineH);
        _eyeColorDropdown = CreateLabeledDropdown(leftPanelGO.transform, "Eye Color", ref yLeft, lineH);
        _modelScaleInput = CreateStackedInputField(leftPanelGO.transform, "Model Scale", ref yLeft, lineH);

      // Populate color dropdowns with preset colors
        PopulateColorDropdown(_hairColorDropdown);
   PopulateColorDropdown(_beardColorDropdown);
   PopulateSkinColorDropdown(_skinColorDropdown);
        PopulateEyeColorDropdown(_eyeColorDropdown);

   Debug.Log("[DressingRoomScreen] Created fashion panels with inputs/dropdowns");
        }

        /// <summary>
        /// Fills the periodic-sound dropdown with the admin's custom sound clips
        /// (BepInEx/config/FiresRPGmaker/Sounds mp3s via FiresCustomAudioStore — the kg-style custom
        /// audio folder). Refreshed on every screen open since clips load async at startup.
        /// </summary>
        private static void PopulateSoundDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null) return;
            var options = new List<string> { "(none)" };
            try
            {
                var keys = new List<string>(FiresCore.Audio.FiresCustomAudioStore.Clips.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                options.AddRange(keys);
            }
            catch { }
            RepopulateKeepingSelection(dropdown, options);
        }

        // Swap a dropdown's option list and reset to "(none)". Runs at screen open, BEFORE the per-NPC
        // selection restore — resetting here means an NPC with no saved fashion shows clean defaults
        // instead of leaking the previously-edited NPC's picks.
        private static void RepopulateKeepingSelection(TMP_Dropdown dropdown, List<string> options)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        /// <summary>Fills the periodic sound list and clears the greet/bye text; the animation dropdowns are filled per
        /// NPC by <see cref="PopulateDropdowns"/>.</summary>
        private void PopulateFashionBehaviorDropdowns()
        {
            PopulateSoundDropdown(_periodicSoundDropdown);
            if (_greetTextInput != null) _greetTextInput.text = "";
            if (_byeTextInput != null) _byeTextInput.text = "";
        }

        /// <summary>
        /// Populates a color dropdown with preset hair/beard colors.
        /// </summary>
      private void PopulateColorDropdown(TMP_Dropdown dropdown)
      {
   if (dropdown == null) return;
      dropdown.ClearOptions();
   dropdown.AddOptions(HairBeardColors.GetColorNames());
     dropdown.value = 0;
      dropdown.RefreshShownValue();
        }

        /// <summary>
  /// Populates a dropdown with preset skin tone colors.
 /// </summary>
        private void PopulateSkinColorDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null) return;
  dropdown.ClearOptions();
 dropdown.AddOptions(SkinToneColors.GetColorNames());
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        /// <summary>
    /// Populates a dropdown with preset eye colors.
     /// </summary>
        private void PopulateEyeColorDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null) return;
        dropdown.ClearOptions();
            dropdown.AddOptions(EyeColors.GetColorNames());
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        #endregion

        #region Show/Hide

        public void Show(NpcController npc)
     {
     _currentNpc = npc;
            _pendingEquipment.Clear();
         _lastDraggedItem = null;
            _lastHoveredSlot = null;
            _isDragging = false;

            if (npc != null) npc.LoadFromZDO();   // ensure worn-gear fields are populated before reading
            LoadEquipmentFromNpc(npc);
  PopulateDropdowns(npc);
            PopulateFashionBehaviorDropdowns();   // emote + custom-sound lists (before selections restore)
    RestoreFashionSelectionsFromNpc(npc);
            RestoreStatControlsFromNpc(npc);

   screenRoot.SetActive(true);
   screenRoot.transform.SetAsLastSibling();
 OpenPlayerInventory();

        Debug.Log($"[DressingRoomScreen] Opened for NPC: {npc?.GetDisplayName()}");
        }

        // Pushes the NPC's stats values onto the scroll-strip controls when the dressing room opens.
        private void RestoreStatControlsFromNpc(NpcController npc)
        {
            if (npc == null) return;
            if (_infiniteHealthToggle != null) _infiniteHealthToggle.isOn = npc.infiniteHealth;
            if (_hideNameplateToggle != null) _hideNameplateToggle.isOn = npc.hideNameplate;
            if (_maxHealthSlider != null)
            {
                // Unset (0) → show the NPC's actual current max health so the slider reflects reality.
                float mh = npc.maxHealth;
                if (mh < _maxHealthSlider.minValue)
                {
                    var hum = npc.GetComponent<Humanoid>();
                    mh = hum != null ? hum.GetMaxHealth() : 100f;
                }
                _maxHealthSlider.value = Mathf.Clamp(mh, _maxHealthSlider.minValue, _maxHealthSlider.maxValue);
            }
            if (_respawnDelaySlider != null) _respawnDelaySlider.value = Mathf.Clamp(npc.respawnDelay, _respawnDelaySlider.minValue, _respawnDelaySlider.maxValue);
            if (_nameplateSizeSlider != null) _nameplateSizeSlider.value = Mathf.Clamp(npc.nameplateScale <= 0f ? 1f : npc.nameplateScale, _nameplateSizeSlider.minValue, _nameplateSizeSlider.maxValue);
            if (_nameplateYSlider != null) _nameplateYSlider.value = Mathf.Clamp(npc.nameplateYOffset, _nameplateYSlider.minValue, _nameplateYSlider.maxValue);
        }

        // Reads the scroll-strip controls back onto the NPC on Apply.
        private void ApplyStatControlsToNpc()
        {
            if (_currentNpc == null) return;
            if (_infiniteHealthToggle != null) _currentNpc.infiniteHealth = _infiniteHealthToggle.isOn;
            if (_maxHealthSlider != null) _currentNpc.maxHealth = _maxHealthSlider.value;
            if (_respawnDelaySlider != null) _currentNpc.respawnDelay = _respawnDelaySlider.value;
            if (_hideNameplateToggle != null) _currentNpc.hideNameplate = _hideNameplateToggle.isOn;
            if (_nameplateSizeSlider != null) _currentNpc.nameplateScale = _nameplateSizeSlider.value;
            if (_nameplateYSlider != null) _currentNpc.nameplateYOffset = _nameplateYSlider.value;
            _currentNpc.ApplyMaxHealth();
        }

        private const string NoneOptionText = "(none)";

        private static void SelectDropdownOption(TMP_Dropdown dropdown, string optionText)
        {
            if (dropdown == null || string.IsNullOrEmpty(optionText)) return;
            int index = dropdown.options.FindIndex(o => string.Equals(o.text, optionText, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            dropdown.value = index;
            dropdown.RefreshShownValue();
        }

        /// <summary>The dropdowns use "(none)" as their empty placeholder; the ZDO stores an empty string.</summary>
        private static string NormalizeDropdownValue(string value)
            => string.IsNullOrEmpty(value) || value == NoneOptionText ? "" : value;

        /// <summary>Appearance comes from the ZDO keys; the fashion JSON only fills fields they do not carry.
        /// Reading hair and beard from the JSON alone left the dropdowns on "(none)" for every NPC seeded
        /// straight onto the ZDO, and an Apply then wrote that blank back over a perfectly good style.</summary>
        private void RestoreFashionSelectionsFromNpc(NpcController npc)
        {
            if (npc == null) return;

            SelectDropdownOption(_hairItemDropdown, npc.hairItem);
            SelectDropdownOption(_hairColorDropdown, npc.hairColor);
            SelectDropdownOption(_beardItemDropdown, npc.beardItem);
            SelectDropdownOption(_beardColorDropdown, npc.beardColor);

            string fashionJson = npc.GetPendingFashionJson();
            if (string.IsNullOrEmpty(fashionJson)) return;

 try
       {
  var fashion = JsonConvert.DeserializeObject<NPCFashionData>(fashionJson);
         if (fashion == null) return;

     // Restore hair dropdown selection
             if (_hairItemDropdown != null && string.IsNullOrEmpty(npc.hairItem) && !string.IsNullOrEmpty(fashion.HairItem))
   {
  int hairIndex = _hairItemDropdown.options.FindIndex(o =>
            string.Equals(o.text, fashion.HairItem, StringComparison.OrdinalIgnoreCase));
            if (hairIndex >= 0)
        {
     _hairItemDropdown.value = hairIndex;
        _hairItemDropdown.RefreshShownValue();
           }
            }

 // Restore beard dropdown selection
            if (_beardItemDropdown != null && string.IsNullOrEmpty(npc.beardItem) && !string.IsNullOrEmpty(fashion.BeardItem))
   {
         int beardIndex = _beardItemDropdown.options.FindIndex(o =>
     string.Equals(o.text, fashion.BeardItem, StringComparison.OrdinalIgnoreCase));
     if (beardIndex >= 0)
          {
           _beardItemDropdown.value = beardIndex;
      _beardItemDropdown.RefreshShownValue();
                    }
          }

    // Restore color dropdowns
       if (_hairColorDropdown != null && string.IsNullOrEmpty(npc.hairColor) && !string.IsNullOrEmpty(fashion.HairColor))
     {
        int colorIndex = _hairColorDropdown.options.FindIndex(o =>
  string.Equals(o.text, fashion.HairColor, StringComparison.OrdinalIgnoreCase));
                    if (colorIndex >= 0)
          {
            _hairColorDropdown.value = colorIndex;
   _hairColorDropdown.RefreshShownValue();
             }
     }

       if (_beardColorDropdown != null && string.IsNullOrEmpty(npc.beardColor) && !string.IsNullOrEmpty(fashion.BeardColor))
       {
     int colorIndex = _beardColorDropdown.options.FindIndex(o =>
          string.Equals(o.text, fashion.BeardColor, StringComparison.OrdinalIgnoreCase));
      if (colorIndex >= 0)
           {
               _beardColorDropdown.value = colorIndex;
  _beardColorDropdown.RefreshShownValue();
          }
  }

       if (_skinColorDropdown != null && !string.IsNullOrEmpty(fashion.SkinColor))
    {
     int colorIndex = _skinColorDropdown.options.FindIndex(o =>
       string.Equals(o.text, fashion.SkinColor, StringComparison.OrdinalIgnoreCase));
         if (colorIndex >= 0)
          {
           _skinColorDropdown.value = colorIndex;
        _skinColorDropdown.RefreshShownValue();
               }
            }

     if (_eyeColorDropdown != null && !string.IsNullOrEmpty(fashion.EyeColor))
    {
     int colorIndex = _eyeColorDropdown.options.FindIndex(o =>
   string.Equals(o.text, fashion.EyeColor, StringComparison.OrdinalIgnoreCase));
     if (colorIndex >= 0)
         {
        _eyeColorDropdown.value = colorIndex;
     _eyeColorDropdown.RefreshShownValue();
  }
            }

     // Restore model scale
              if (_modelScaleInput != null && !string.IsNullOrEmpty(fashion.ModelScale))
          _modelScaleInput.text = fashion.ModelScale;

                // Restore greet/bye speech text
                if (_greetTextInput != null) _greetTextInput.text = fashion.GreetText ?? "";
                if (_byeTextInput != null) _byeTextInput.text = fashion.ByeText ?? "";

                // Restore animation dropdowns
                SelectAnimationOption(_periodicAnimationDropdown, fashion.PeriodicAnimation);
                SelectAnimationOption(_interactAnimationDropdown, fashion.InteractAnimation);
                SelectAnimationOption(_greetAnimationDropdown, fashion.GreetAnimation);
                SelectAnimationOption(_byeAnimationDropdown, fashion.ByeAnimation);

                // Restore time inputs
      if (_periodicAnimationTimeInput != null && !string.IsNullOrEmpty(fashion.PeriodicAnimationTime))
  _periodicAnimationTimeInput.text = fashion.PeriodicAnimationTime;

            if (_periodicSoundTimeInput != null && !string.IsNullOrEmpty(fashion.PeriodicSoundTime))
     _periodicSoundTimeInput.text = fashion.PeriodicSoundTime;

         Debug.Log($"[DressingRoomScreen] Restored fashion selections from NPC - Hair: {fashion.HairItem}, Beard: {fashion.BeardItem}");
    }
            catch (Exception ex)
         {
          Debug.LogWarning($"[DressingRoomScreen] Failed to restore fashion selections: {ex.Message}");
     }
        }

        private void CacheMainPanelButtonRects()
   {
  }

        private void WireMainPanelButtons()
{
        }

  public void Hide()
        {
     screenRoot.SetActive(false);
            _currentNpc = null;
        _pendingEquipment.Clear();
          _lastDraggedItem = null;
         _lastHoveredSlot = null;
            _isDragging = false;

_closeButtonRect = null;
            _prevButtonRect = null;
_sizeButtonRect = null;
  _headerRect = null;

       foreach (var slot in _slots.Values)
       {
      ClearSlotVisual(slot);
            }

     ClosePlayerInventory();
            RestoreMainPanelButtons();

      Debug.Log("[DressingRoomScreen] Hidden");
  }

        private void RestoreMainPanelButtons()
        {
        }

        public bool IsVisible => screenRoot != null && screenRoot.activeSelf;

        private void OpenPlayerInventory()
  {
          try
        {
                if (InventoryGui.instance != null && !InventoryGui.IsVisible())
       {
         InventoryGui.instance.Show(null);
                    SetInventoryDropButton(false);   // 0.0.545: its full-screen drop button (canvas 600) ate every click on the book (210)
           Debug.Log("[DressingRoomScreen] Opened player inventory");
           }
         }
    catch (Exception ex)
            {
        Debug.LogWarning($"[DressingRoomScreen] Could not open inventory: {ex.Message}");
     }
        }

        // 0.0.545 ([visual]'s npc_looks: "Apply ... hit IngameGui/Inventory_screen/root/dropButton"): the inventory's root drop
        // button stretches over the whole screen above the NPC book, so no click reached the dressing room while the
        // inventory was open. It is switched off while the dressing room uses the inventory, and back on when it closes.
        private static GameObject _droppedButton;
        private static void SetInventoryDropButton(bool on)
        {
            try
            {
                if (!on)
                {
                    if (InventoryGui.instance == null) return;
                    foreach (var t in InventoryGui.instance.GetComponentsInChildren<Transform>(true))
                        if (t.name == "dropButton" && t.gameObject.activeSelf) { _droppedButton = t.gameObject; _droppedButton.SetActive(false); break; }
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoomScreen] inventory drop button {(_droppedButton != null ? "hidden" : "not found")} while the dressing room is open");
                }
                else if (_droppedButton != null) { _droppedButton.SetActive(true); _droppedButton = null; }
            }
            catch (Exception ex) { Debug.LogWarning($"[DressingRoomScreen] drop button toggle failed: {ex.Message}"); }
        }

        private void ClosePlayerInventory()
      {
    try
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
  {
        SetInventoryDropButton(true);
                    InventoryGui.instance.Hide();
         Debug.Log("[DressingRoomScreen] Closed player inventory");
          }
         }
            catch (Exception ex)
            {
  Debug.LogWarning($"[DressingRoomScreen] Could not close inventory: {ex.Message}");
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

    public EquipmentSlotUI GetSlotUnderMouse()
        {
    Vector2 mousePos = Input.mousePosition;

  foreach (var kvp in _slots)
          {
         var slot = kvp.Value;
   if (slot.RectTransform == null) continue;

        if (RectTransformUtility.RectangleContainsScreenPoint(slot.RectTransform, mousePos, null))
  {
        return slot;
        }
      }

 return null;
        }

     private void ResetSlotColors()
        {
            foreach (var slot in _slots.Values)
    {
   slot.Background.color = string.IsNullOrEmpty(slot.CurrentPrefab)
      ? SlotEmptyColor
          : SlotFilledColor;
    }
        }

        #endregion

 #region Equipment Management

        private void LoadEquipmentFromNpc(NpcController npc)
        {
            if (npc == null) return;

            var equipment = npc.GetEquipmentData();
            if (equipment == null) return;

            // Our NpcController.equip* strings are the editable store, but on clients they
            // can come up empty even while the NPC is visibly wearing the gear. The vanilla
            // VisEquipment ZDO (RightItem/ChestItem/...) is the authoritative, network-synced
            // record of what the NPC ACTUALLY has equipped. For any slot our strings don't
            // cover, fall back to that synced state so the grid always reflects the real
            // loadout (weapon + back slots included) — exactly like a tamed companion's grid.
            var zdo = npc.GetComponent<ZNetView>()?.GetZDO();

            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (slot == EquipmentSlot.Pet) { LoadPetSlot(npc); continue; }   // 0.0.559
                string fieldVal = equipment.TryGetValue(slot, out var v) ? (v ?? "") : "";
                string liveVal = (zdo != null) ? GetVanillaEquipName(zdo, slot) : "";

                string used = fieldVal;
                string source = "field";
                if (string.IsNullOrEmpty(used) && !string.IsNullOrEmpty(liveVal))
                {
                    used = liveVal;
                    source = "visEquip";
                }

                if (_slots.TryGetValue(slot, out var slotUI))
                    SetSlotItem(slotUI, used);
                _pendingEquipment[slot] = used;

                string zdoStr = (zdo != null) ? zdo.GetString(ZdoEquipStringKey(slot), "<none>") : "<no-zdo>";
                Debug.Log($"[WeaponDiag] {slot}: field='{fieldVal}' zdoStr='{zdoStr}' visEquip='{liveVal}' -> '{used}' ({source})");
            }

            Debug.Log($"[WeaponDiag] owner={(zdo != null && zdo.IsOwner())} " +
                      $"server={(ZNet.instance != null && ZNet.instance.IsServer())} npc={npc.GetDisplayName()}");
        }

        /// <summary>
        /// Reads the item prefab name actually equipped in a given slot from the NPC's vanilla
        /// VisEquipment ZDO (the network-synced source of truth). Returns "" if the slot is empty.
        /// </summary>
        private static string GetVanillaEquipName(ZDO zdo, EquipmentSlot slot)
        {
            int hash;
            switch (slot)
            {
                case EquipmentSlot.Helmet:    hash = zdo.GetInt(ZDOVars.s_helmetItem); break;
                case EquipmentSlot.Chest:     hash = zdo.GetInt(ZDOVars.s_chestItem); break;
                case EquipmentSlot.Legs:      hash = zdo.GetInt(ZDOVars.s_legItem); break;
                case EquipmentSlot.Shoulder:  hash = zdo.GetInt(ZDOVars.s_shoulderItem); break;
                case EquipmentSlot.Utility:   hash = zdo.GetInt(ZDOVars.s_utilityItem); break;
                case EquipmentSlot.RightHand: hash = zdo.GetInt(ZDOVars.s_rightItem); break;
                case EquipmentSlot.LeftHand:  hash = zdo.GetInt(ZDOVars.s_leftItem); break;
                case EquipmentSlot.RightBack: hash = zdo.GetInt(ZDOVars.s_rightBackItem); break;
                case EquipmentSlot.LeftBack:  hash = zdo.GetInt(ZDOVars.s_leftBackItem); break;
                default: return "";
            }
            if (hash == 0) return "";
            var prefab = ObjectDB.instance?.GetItemPrefab(hash);
            return prefab != null ? prefab.name : "";
        }

        private static string ZdoEquipStringKey(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Helmet:    return "npc_equip_helmet";
                case EquipmentSlot.Chest:     return "npc_equip_chest";
                case EquipmentSlot.Legs:      return "npc_equip_legs";
                case EquipmentSlot.Shoulder:  return "npc_equip_shoulder";
                case EquipmentSlot.Utility:   return "npc_equip_utility";
                case EquipmentSlot.RightHand: return "npc_equip_righthand";
                case EquipmentSlot.LeftHand:  return "npc_equip_lefthand";
                case EquipmentSlot.RightBack: return "npc_equip_rightback";
                case EquipmentSlot.LeftBack:  return "npc_equip_leftback";
                default: return "";
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
   Debug.LogWarning($"[DressingRoomScreen] Item prefab not found: {prefabName}");
       ClearSlotVisual(slot);
        return;
   }

            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
if (itemDrop == null)
            {
     ClearSlotVisual(slot);
            return;
       }

            slot.Background.color = SlotFilledColor;

        if (itemDrop.m_itemData?.m_shared?.m_icons != null &&
   itemDrop.m_itemData.m_shared.m_icons.Length > 0)
        {
       slot.Icon.sprite = itemDrop.m_itemData.m_shared.m_icons[0];
      slot.Icon.color = Color.white;
          }

      if (slot.Label != null)
            slot.Label.gameObject.SetActive(false);

   string displayName = itemDrop.m_itemData?.m_shared?.m_name ?? prefabName;
            if (displayName.StartsWith("$"))
   displayName = Localization.instance?.Localize(displayName) ?? displayName;

            slot.ItemName.text = displayName;
   slot.ItemName.gameObject.SetActive(true);

            Debug.Log($"[DressingRoomScreen] Set slot {slot.SlotType} to {prefabName}");
        }

        public void ClearSlotVisual(EquipmentSlotUI slot)
        {
         if (slot == null) return;

     slot.CurrentPrefab = null;
         slot.Background.color = SlotEmptyColor;
 slot.Icon.sprite = null;
    slot.Icon.color = new Color(1, 1, 1, 0.3f);

      if (slot.Label != null)
    slot.Label.gameObject.SetActive(true);

            slot.ItemName.text = "";
         slot.ItemName.gameObject.SetActive(false);
   }

        public void OnSlotCleared(EquipmentSlot slotType)
 {
            if (slotType == EquipmentSlot.Pet) { OpenPetPicker(); return; }   // 0.0.559: right-click on the Pet slot picks
            if (_slots.TryGetValue(slotType, out var slot))
          {
          ReturnItemToInventory(slot.CurrentPrefab);
     ClearSlotVisual(slot);
 _pendingEquipment.Remove(slotType);
       Debug.Log($"[DressingRoomScreen] Cleared slot {slotType}");

           if (_currentNpc != null)
                {
            try
     {
     _currentNpc.SetEquipmentData(_pendingEquipment);
          _currentNpc.ApplyVisualEquipment();
     _currentNpc.Invoke("ApplyVisualEquipment", 0.05f);
        }
        catch (Exception ex)
         {
    Debug.LogWarning($"[DressingRoomScreen] Failed to apply visuals on clear: {ex.Message}");
         }
            }
            }
        }

        private void ReturnItemToInventory(string prefabName)
        {
    if (string.IsNullOrEmpty(prefabName)) return;

       var player = Player.m_localPlayer;
            if (player == null) return;

    var inventory = player.GetInventory();
        if (inventory == null) return;

  var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
    if (itemPrefab == null)
            {
                Debug.LogWarning($"[DressingRoomScreen] Cannot return item - prefab not found: {prefabName}");
     return;
            }

            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
       if (itemDrop == null)
   {
             Debug.LogWarning($"[DressingRoomScreen] Cannot return item - no ItemDrop component: {prefabName}");
              return;
     }

     var addedItem = inventory.AddItem(prefabName, 1, itemDrop.m_itemData.m_quality, itemDrop.m_itemData.m_variant, 0, "", false);

   if (addedItem != null)
   {
                Debug.Log($"[DressingRoomScreen] Returned {prefabName} to player inventory");
          MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Returned {GetItemDisplayName(prefabName)} to inventory");
            }
            else
     {
    Debug.LogWarning($"[DressingRoomScreen] Failed to return {prefabName} to inventory - inventory might be full");
     MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Inventory full! Item dropped on ground.");
   DropItemOnGround(prefabName);
 }
        }

        private void DropItemOnGround(string prefabName)
    {
            var player = Player.m_localPlayer;
     if (player == null) return;

  var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (itemPrefab == null) return;

            var dropPos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 0.5f;
            UnityEngine.Object.Instantiate(itemPrefab, dropPos, Quaternion.identity);

       Debug.Log($"[DressingRoomScreen] Dropped {prefabName} on ground at {dropPos}");
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

        public void OnItemDropped(EquipmentSlot slotType, string prefabName)
        {
            if (slotType == EquipmentSlot.Pet) { OnPetDropped(prefabName); return; }   // 0.0.559
 if (_slots.TryGetValue(slotType, out var slot))
            {
     if (!string.IsNullOrEmpty(slot.CurrentPrefab))
            {
         ReturnItemToInventory(slot.CurrentPrefab);
        }

  SetSlotItem(slot, prefabName);
  _pendingEquipment[slotType] = prefabName;
 Debug.Log($"[DressingRoomScreen] OnItemDropped - slot {slotType} = {prefabName}, pending count: {_pendingEquipment.Count}");

     if (_currentNpc != null)
          {
         try
             {
          _currentNpc.SetEquipmentData(_pendingEquipment);

          // Full-fidelity combat stats: for a hand weapon, persist the REAL dragged item (quality/upgrades/
          // crafter/customData/worldLevel) so the static NPC fights with the exact weapon, not a quality-1
          // prefab clone. Visuals still come from the prefab-name fields, so no double-paint.
          if (slotType == EquipmentSlot.RightHand || slotType == EquipmentSlot.LeftHand)
          {
              string itemBlob = "";
              if (_lastDraggedItem != null && _lastDraggedItem.m_dropPrefab != null
                  && _lastDraggedItem.m_dropPrefab.name == prefabName)
                  itemBlob = global::FiresCore.Npc.CompanionInventory.SerializeItemData(_lastDraggedItem);
              _currentNpc.SetEquipmentItemData(slotType, itemBlob);
          }

           _currentNpc.ApplyVisualEquipment();
    _currentNpc.Invoke("ApplyVisualEquipment", 0.05f);
    _currentNpc.SaveToZDO();   // persist the drop immediately so it survives close/reopen
        }
     catch (Exception ex)
      {
    Debug.LogWarning($"[DressingRoomScreen] Failed to apply visuals immediately: {ex.Message}");
          }
        }
}
        }

        public void HandleApplyClicked()
        {
            Debug.Log("[DressingRoomScreen] Apply button clicked!");

 if (_currentNpc == null)
        {
            Debug.LogWarning("[DressingRoomScreen] Cannot apply - no NPC selected");
       MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "No NPC selected");
                return;
 }

   Debug.Log($"[DressingRoomScreen] Applying {_pendingEquipment.Count} equipment items to NPC: {_currentNpc.GetDisplayName()}");

        foreach (var kvp in _pendingEquipment)
   {
     Debug.Log($"[DressingRoomScreen] Pending: {kvp.Key} = {kvp.Value}");
            }

    _currentNpc.SetEquipmentData(_pendingEquipment);
            Debug.Log("[DressingRoomScreen] Called SetEquipmentData");

    // Collect fashion settings - now using dropdowns for colors
  string fashionJson = null;
    try
       {
    // The UI has no controls for interact sound, crafting-anim lock, or the look-at toggle —
    // carry the NPC's current values through instead of blanking them (blanking broke migrated
    // kg NPCs: crafting pose lost + LookAtPlayer forced true on every admin Apply).
    var existingComp = _currentNpc.GetComponent<NpcFashionComponent>();
    var fashion = new NPCFashionData
  {
      PeriodicAnimation = SelectedAnimationValue(_periodicAnimationDropdown),
  PeriodicAnimationTime = _periodicAnimationTimeInput?.text,
       PeriodicSound = _periodicSoundDropdown?.options[_periodicSoundDropdown.value].text,
 PeriodicSoundTime = _periodicSoundTimeInput?.text,
 InteractAnimation = SelectedAnimationValue(_interactAnimationDropdown),
      GreetAnimation = SelectedAnimationValue(_greetAnimationDropdown),
     ByeAnimation = SelectedAnimationValue(_byeAnimationDropdown),
      GreetText = _greetTextInput?.text,
      ByeText = _byeTextInput?.text,
        HairItem = _hairItemDropdown?.options[_hairItemDropdown.value].text,
   HairColor = _hairColorDropdown?.options[_hairColorDropdown.value].text,
    BeardItem = _beardItemDropdown?.options[_beardItemDropdown.value].text,
    BeardColor = _beardColorDropdown?.options[_beardColorDropdown.value].text,
   SkinColor = _skinColorDropdown?.options[_skinColorDropdown.value].text,
      EyeColor = _eyeColorDropdown?.options[_eyeColorDropdown.value].text,
       ModelScale = _modelScaleInput?.text,
       InteractAudioClip = existingComp != null ? existingComp.InteractSound : null,
       CraftingAnimation = existingComp != null ? existingComp.CraftingAnimation.ToString() : null,
       LookAtPlayer = existingComp != null ? (existingComp.LookAtPlayer ? "true" : "false") : "true"
    };

            _currentNpc.hairItem = NormalizeDropdownValue(fashion.HairItem);
            _currentNpc.hairColor = NormalizeDropdownValue(fashion.HairColor);
            _currentNpc.beardItem = NormalizeDropdownValue(fashion.BeardItem);
            _currentNpc.beardColor = NormalizeDropdownValue(fashion.BeardColor);

      fashionJson = JsonConvert.SerializeObject(fashion);
   NpcFashionManager.ApplyFashionJsonToNpc(fashionJson, _currentNpc);
            Debug.Log("[DressingRoomScreen] Applied fashion JSON to NPC from UI");

                _currentNpc.SetPendingFashionJson(fashionJson);
 Debug.Log("[DressingRoomScreen] Stored fashion JSON in NPC for persistence");
       }
      catch (Exception ex)
        {
       Debug.LogWarning($"[DressingRoomScreen] Failed to apply fashion from UI: {ex.Message}");
    }

            _currentNpc.ApplyVisualEquipment();
 Debug.Log("[DressingRoomScreen] Called ApplyVisualEquipment");

            ApplyStatControlsToNpc();

            _currentNpc.SaveToZDO();
            global::FiresRPGmaker.Managers.SavedNpcManager.RedressNow(_currentNpc, "dressing Apply");   // 0.0.508: the owner writes the look now
 Debug.Log("[DressingRoomScreen] Called SaveToZDO");

          int equippedCount = 0;
  foreach (var kvp in _pendingEquipment)
            {
          if (!string.IsNullOrEmpty(kvp.Value))
   equippedCount++;
            }

            string message = equippedCount > 0
      ? $"NPC equipment saved! ({equippedCount} items)"
    : "NPC equipment cleared!";

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, message);
            Debug.Log($"[DressingRoomScreen] Applied {equippedCount} equipment items to NPC: {_currentNpc.GetDisplayName()}");
        }

 public void HandleClearAllClicked()
        {
      Debug.Log("[DressingRoomScreen] Clear All button clicked");

            foreach (var slot in _slots.Values)
     {
                if (slot.SlotType == EquipmentSlot.Pet) { SetNpcPet("", "clear all"); continue; }   // 0.0.559
        if (!string.IsNullOrEmpty(slot.CurrentPrefab))
    {
                  ReturnItemToInventory(slot.CurrentPrefab);
        }
      ClearSlotVisual(slot);
            }

        _pendingEquipment.Clear();
  Debug.Log("[DressingRoomScreen] Cleared all equipment slots and returned items to inventory");

      if (_currentNpc != null)
            {
            try
       {
   _currentNpc.SetEquipmentData(_pendingEquipment);
                _currentNpc.ApplyVisualEquipment();
              _currentNpc.Invoke("ApplyVisualEquipment", 0.05f);
            }
  catch (Exception ex)
     {
                    Debug.LogWarning($"[DressingRoomScreen] Failed to apply visuals on clear all: {ex.Message}");
             }
     }
   }

        private void OnBackClicked()
        {
            Debug.Log("[DressingRoomScreen] Back button clicked");
   Hide();

        }

        private void OnCloseButtonClicked()
        {
Debug.Log("[DressingRoomScreen] Close button clicked");
     Hide();

      }

      #endregion

   #region Helper Types

 public class EquipmentSlotUI
        {
            public DressingRoomScreenController.EquipmentSlot SlotType;
   public GameObject Root;
 public RectTransform RectTransform;
            public Image Background;
    public Image Icon;
    public TextMeshProUGUI Label;
      public TextMeshProUGUI ItemName;
            public string CurrentPrefab;
 }

   private class SlotClickHandler : MonoBehaviour, IPointerClickHandler
        {
       private DressingRoomScreenController _controller;
            private EquipmentSlotUI _slot;

            public void Initialize(DressingRoomScreenController controller, EquipmentSlotUI slot)
            {
        _controller = controller;
       _slot = slot;
  }

            public void OnPointerClick(PointerEventData eventData)
      {
    if (_controller == null || _slot == null) return;
    if (eventData.button == PointerEventData.InputButton.Right)
        {
      _controller.OnSlotCleared(_slot.SlotType);
      }
                // A left click that reaches the slot through the EventSystem drops the item being dragged onto THIS slot
                // (the same drop the InventoryGui patches make for the mouse; once dropped the drag is cleared, so it runs once).
                else if (eventData.button == PointerEventData.InputButton.Left && InventoryGui.instance != null)
                {
                    var dragItem = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_dragItem")?.GetValue(InventoryGui.instance) as ItemDrop.ItemData;
                    if (dragItem != null) global::FiresRPGmaker.UI.Patches.InventoryGui_OnSelectedItem_Patch.HandleDressingRoomDrop(InventoryGui.instance, dragItem, _slot);
                }
         }
        }

  private class DressingRoomButtonHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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
        itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
        itemType == ItemDrop.ItemData.ItemType.Bow ||
        itemType == ItemDrop.ItemData.ItemType.Tool ||
        itemType == ItemDrop.ItemData.ItemType.Torch,
     EquipmentSlot.LeftHand => itemType == ItemDrop.ItemData.ItemType.Shield ||
           itemType == ItemDrop.ItemData.ItemType.Torch ||
     itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon,
  EquipmentSlot.RightBack => itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
        itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
            itemType == ItemDrop.ItemData.ItemType.Bow ||
         itemType == ItemDrop.ItemData.ItemType.Shield ||
      itemType == ItemDrop.ItemData.ItemType.Tool,
        EquipmentSlot.Pet => global::FiresRPGmaker.Modules.Pets.PetApi.IsPet(item),
        EquipmentSlot.LeftBack => itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
        itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
       itemType == ItemDrop.ItemData.ItemType.Bow ||
      itemType == ItemDrop.ItemData.ItemType.Shield ||
          itemType == ItemDrop.ItemData.ItemType.Tool,
                _ => false
      };
        }
    }
}
