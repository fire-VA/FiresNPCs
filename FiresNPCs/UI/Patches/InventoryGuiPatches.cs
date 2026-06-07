using HarmonyLib;
using UnityEngine;
using System.Reflection;
using FiresRPGmaker.UI.Screens;
using FiresCore.Npc;
using FiresRPGmaker.Modules.Companions;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Harmony patches for InventoryGui to intercept item drops when over NPC dressing room slots,
    /// companion inventory equipment slots, or companion storage slots.
    /// This prevents items from being dropped on the ground when the player releases them over our UI.
    /// </summary>
    public static class InventoryGuiPatches
    {
        // Cached reflection fields for performance
        private static FieldInfo _dragItemField;
  private static FieldInfo _dragGoField;
      private static FieldInfo _dragInvField;
      private static FieldInfo _dragAmountField;
   private static MethodInfo _setupDragItemMethod;
        private static MethodInfo _updateCraftingPanelMethod;

        // Track pending drops to dressing room
        private static bool _pendingDressingRoomDrop;
        private static DressingRoomScreenController.EquipmentSlot _pendingSlot;
    private static string _pendingPrefabName;

      // Track pending drops to companion inventory
        private static bool _pendingCompanionDrop;
        private static CompanionInventoryScreen.EquipmentSlot _pendingCompanionSlot;
        private static string _pendingCompanionPrefabName;

        // Track pending drops to companion storage
   private static bool _pendingStorageDrop;
        private static CompanionInventoryScreen.StorageSlotUI _pendingStorageSlot;
     private static ItemDrop.ItemData _pendingStorageItem;

   static InventoryGuiPatches()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
          var publicFlags = BindingFlags.Public | BindingFlags.Instance;

       _dragItemField = typeof(InventoryGui).GetField("m_dragItem", flags);
_dragGoField = typeof(InventoryGui).GetField("m_dragGo", flags);
      _dragInvField = typeof(InventoryGui).GetField("m_dragInventory", flags);
        _dragAmountField = typeof(InventoryGui).GetField("m_dragAmount", flags);
       _setupDragItemMethod = typeof(InventoryGui).GetMethod("SetupDragItem", flags);
            _updateCraftingPanelMethod = typeof(InventoryGui).GetMethod("UpdateCraftingPanel", flags | publicFlags);
     }

        /// <summary>
      /// Checks if the mouse is currently over a dressing room equipment slot.
   /// </summary>
        public static bool IsOverDressingRoomSlot()
        {
     var dressingRoom = DressingRoomScreenController.Instance;
if (dressingRoom == null || !dressingRoom.IsVisible)
            return false;

return dressingRoom.GetSlotUnderMouse() != null;
        }

  /// <summary>
        /// Checks if the mouse is currently over a companion inventory equipment slot.
        /// </summary>
      public static bool IsOverCompanionSlot()
        {
       var companionScreen = CompanionInventoryScreen.Instance;
      if (companionScreen == null || !CompanionInventoryScreen.IsVisible)
     return false;

            return companionScreen.GetSlotUnderMouse() != null;
        }

    /// <summary>
        /// Checks if the mouse is currently over a companion storage slot.
    /// </summary>
      public static bool IsOverCompanionStorageSlot()
        {
            var companionScreen = CompanionInventoryScreen.Instance;
            if (companionScreen == null || !CompanionInventoryScreen.IsVisible)
       return false;

         return companionScreen.GetStorageSlotUnderMouse() != null;
   }

  /// <summary>
        /// Checks if dressing room is visible (even if not over a slot).
        /// </summary>
        public static bool IsDressingRoomVisible()
        {
            var dressingRoom = DressingRoomScreenController.Instance;
            return dressingRoom != null && dressingRoom.IsVisible;
        }

      /// <summary>
      /// Checks if companion inventory screen is visible (even if not over a slot).
 /// </summary>
        public static bool IsCompanionScreenVisible()
        {
   return CompanionInventoryScreen.IsVisible;
        }

 /// <summary>
        /// Checks if any of our equipment screens are visible.
        /// </summary>
        public static bool IsAnyEquipmentScreenVisible()
        {
            return IsDressingRoomVisible() || IsCompanionScreenVisible();
        }

   /// <summary>
        /// Checks if mouse is over any of our equipment or storage slots.
        /// </summary>
        public static bool IsOverAnyEquipmentSlot()
        {
            return IsOverDressingRoomSlot() || IsOverCompanionSlot() || IsOverCompanionStorageSlot();
        }

 /// <summary>
  /// Gets the prefab name from an item.
        /// </summary>
        private static string GetPrefabName(ItemDrop.ItemData item)
   {
       if (item == null) return null;

            if (item.m_dropPrefab != null)
      {
                return item.m_dropPrefab.name.Replace("(Clone)", "").Trim();
       }

     return null;
        }

        /// <summary>
        /// Process any pending dressing room drops.
        /// Called after Valheim has cleaned up its state.
     /// </summary>
    public static void ProcessPendingDrop()
   {
            // Process dressing room drops
            if (_pendingDressingRoomDrop)
        {
 _pendingDressingRoomDrop = false;

         var dressingRoom = DressingRoomScreenController.Instance;
  if (dressingRoom != null && dressingRoom.IsVisible && !string.IsNullOrEmpty(_pendingPrefabName))
      {
  dressingRoom.OnItemDropped(_pendingSlot, _pendingPrefabName);
           Debug.Log($"[InventoryGuiPatches] Applied pending dressing room drop: {_pendingPrefabName} to {_pendingSlot}");
          }

      _pendingPrefabName = null;
   }

            // Process companion inventory drops
            if (_pendingCompanionDrop)
  {
    _pendingCompanionDrop = false;

var companionScreen = CompanionInventoryScreen.Instance;
        if (companionScreen != null && CompanionInventoryScreen.IsVisible && !string.IsNullOrEmpty(_pendingCompanionPrefabName))
      {
   companionScreen.OnItemDropped(_pendingCompanionSlot, _pendingCompanionPrefabName);
         Debug.Log($"[InventoryGuiPatches] Applied pending companion drop: {_pendingCompanionPrefabName} to {_pendingCompanionSlot}");

         }

        _pendingCompanionPrefabName = null;
          }

            // Process companion storage drops
 if (_pendingStorageDrop)
  {
          _pendingStorageDrop = false;

      var companionScreen = CompanionInventoryScreen.Instance;
      if (companionScreen != null && CompanionInventoryScreen.IsVisible && _pendingStorageSlot != null && _pendingStorageItem != null)
       {
            companionScreen.OnStorageItemDropped(_pendingStorageSlot, _pendingStorageItem);
   Debug.Log($"[InventoryGuiPatches] Applied pending storage drop: {_pendingStorageItem.m_shared.m_name} to slot {_pendingStorageSlot.SlotIndex}");
           }

        _pendingStorageSlot = null;
   _pendingStorageItem = null;
            }
        }
  }

    /// <summary>
    /// Patch InventoryGui.UpdateItemDrag to prevent NRE when drag state is cleared externally.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateItemDrag")]
    public static class InventoryGui_UpdateItemDrag_Patch
    {
        static System.Exception Finalizer(System.Exception __exception)
        {
         if (__exception is System.NullReferenceException)
       {
        return null; // Suppress
     }
            return __exception;
        }
    }

    /// <summary>
    /// Patch InventoryGui.OnSelectedItem to handle dressing room, companion equipment, and storage drops.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    public static class InventoryGui_OnSelectedItem_Patch
    {
        static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
    {
            // Process any pending drops from previous operations
    InventoryGuiPatches.ProcessPendingDrop();

       // If no equipment screen visible, let Valheim handle it
       if (!InventoryGuiPatches.IsAnyEquipmentScreenVisible())
      return true;

    // Get current drag state
     var dragGoField = typeof(InventoryGui).GetField("m_dragGo", BindingFlags.NonPublic | BindingFlags.Instance);
       var dragGo = dragGoField?.GetValue(__instance) as GameObject;

    // If we're not dragging anything, let Valheim handle normal item selection
    if (dragGo == null)
         return true;

       // We have a drag in progress - check if we're over any of our slots
            if (!InventoryGuiPatches.IsOverAnyEquipmentSlot())
              return true; // Not over our slot, let Valheim handle it

  // Get the dragged item
      var dragItemField = typeof(InventoryGui).GetField("m_dragItem", BindingFlags.NonPublic | BindingFlags.Instance);
            var dragItem = dragItemField?.GetValue(__instance) as ItemDrop.ItemData;

         if (dragItem == null)
                return true;

  // Check which screen/slot we're dropping on
            if (InventoryGuiPatches.IsOverDressingRoomSlot())
          {
        return HandleDressingRoomDrop(__instance, dragItem);
       }
  else if (InventoryGuiPatches.IsOverCompanionSlot())
   {
    return HandleCompanionDrop(__instance, dragItem);
    }
     else if (InventoryGuiPatches.IsOverCompanionStorageSlot())
         {
return HandleCompanionStorageDrop(__instance, dragItem);
            }

        return true;
        }

     private static bool HandleDressingRoomDrop(InventoryGui __instance, ItemDrop.ItemData dragItem)
        {
var dressingRoom = DressingRoomScreenController.Instance;
        var slot = dressingRoom.GetSlotUnderMouse();

    if (slot == null)
                return true;

    // Check if item is valid for this slot
            if (!dressingRoom.IsValidItemForSlot(slot.SlotType, dragItem))
            {
   MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item doesn't fit in that slot");
   var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
       setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
      return false;
        }

 // Get prefab name and source inventory
     string prefabName = dragItem.m_dropPrefab?.name?.Replace("(Clone)", "").Trim();
    var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
            var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

  if (string.IsNullOrEmpty(prefabName))
            {
    Debug.LogWarning("[InventoryGuiPatches] Could not get prefab name from dragged item");
    return true;
     }

        Debug.Log($"[InventoryGuiPatches] Dropping {prefabName} on dressing room slot {slot.SlotType}");

        // Remove from inventory
       if (sourceInventory != null)
     {
sourceInventory.RemoveItem(dragItem);
      }

   // Apply to dressing room
   dressingRoom.OnItemDropped(slot.SlotType, prefabName);

  // Clear drag state and update UI
      ClearDragAndUpdateUI(__instance);
  return false;
        }

        private static bool HandleCompanionDrop(InventoryGui __instance, ItemDrop.ItemData dragItem)
        {
            var companionScreen = CompanionInventoryScreen.Instance;
            var slot = companionScreen.GetSlotUnderMouse();

    if (slot == null)
       return true;

   // Check if item is valid for this slot
     if (!companionScreen.IsValidItemForSlot(slot.SlotType, dragItem))
            {
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item doesn't fit in that slot");
  var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
                setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
    return false;
         }

            // Get prefab name and source inventory
     string prefabName = dragItem.m_dropPrefab?.name?.Replace("(Clone)", "").Trim();
            var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
            var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

     if (string.IsNullOrEmpty(prefabName))
            {
   Debug.LogWarning("[InventoryGuiPatches] Could not get prefab name from dragged item");
   return true;
       }

            Debug.Log($"[InventoryGuiPatches] Dropping {prefabName} on companion slot {slot.SlotType}");

            // Remove from inventory
            if (sourceInventory != null)
    {
             sourceInventory.RemoveItem(dragItem);
     }

            // Apply to companion screen
     companionScreen.OnItemDropped(slot.SlotType, prefabName);

            // Clear drag state and update UI
        ClearDragAndUpdateUI(__instance);
            return false;
        }

        private static bool HandleCompanionStorageDrop(InventoryGui __instance, ItemDrop.ItemData dragItem)
        {
 var companionScreen = CompanionInventoryScreen.Instance;
         var storageSlot = companionScreen.GetStorageSlotUnderMouse();

            if (storageSlot == null)
             return true;

        // Check if item is valid for storage
            if (!companionScreen.IsValidItemForStorage(dragItem))
            {
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item can't be stored here");
                var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
 setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
         return false;
            }

         // Get source inventory
          var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
       var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

 Debug.Log($"[InventoryGuiPatches] Dropping {dragItem.m_shared.m_name} on companion storage slot {storageSlot.SlotIndex}");

            // Clone the item before removing (so we have the full data)
     var itemClone = dragItem.Clone();

         // Remove from source inventory
            if (sourceInventory != null)
         {
       sourceInventory.RemoveItem(dragItem);
    }

    // Apply to companion storage
          companionScreen.OnStorageItemDropped(storageSlot, itemClone);

            // Clear drag state and update UI
         ClearDragAndUpdateUI(__instance);
     return false;
        }

    /// <summary>
    /// Clears drag state and updates UI after a successful drop.
    /// </summary>
   public static void ClearDragAndUpdateUI(InventoryGui instance)
        {
            // Clear drag state using Valheim's method
         var setupDragMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
     setupDragMethod?.Invoke(instance, new object[] { null, null, 1 });

       // Update crafting panel to refresh inventory display
         var updateMethod = typeof(InventoryGui).GetMethod("UpdateCraftingPanel", BindingFlags.NonPublic | BindingFlags.Instance);
      updateMethod?.Invoke(instance, new object[] { false });

            // Update the player grid to show the removed item is gone
    if (instance.m_playerGrid != null && Player.m_localPlayer != null)
         {
          instance.m_playerGrid.UpdateInventory(Player.m_localPlayer.GetInventory(), Player.m_localPlayer, null);
        }

  // Play move effect
            var moveEffectsField = typeof(InventoryGui).GetField("m_moveItemEffects", BindingFlags.Public | BindingFlags.Instance);
var moveEffects = moveEffectsField?.GetValue(instance) as EffectList;
            moveEffects?.Create(instance.transform.position, Quaternion.identity);
        }

    static System.Exception Finalizer(System.Exception __exception)
  {
     if (__exception is System.NullReferenceException)
            {
                Debug.Log("[InventoryGuiPatches] Suppressed NRE in OnSelectedItem");
     return null;
  }
            return __exception;
        }
    }

    /// <summary>
    /// Patch InventoryGui.OnDropOutside to prevent drops when over our UI.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "OnDropOutside")]
    public static class InventoryGui_OnDropOutside_Patch
    {
        static bool Prefix(InventoryGui __instance)
        {
            // Check if any equipment screen is visible and we're over a slot
      if (!InventoryGuiPatches.IsOverAnyEquipmentSlot())
    return true; // Let original method run

       // Get the currently dragged item
       var dragGoField = typeof(InventoryGui).GetField("m_dragGo", BindingFlags.NonPublic | BindingFlags.Instance);
       var dragGo = dragGoField?.GetValue(__instance) as GameObject;

            if (dragGo == null)
         return true; // Nothing being dragged

       var dragItemField = typeof(InventoryGui).GetField("m_dragItem", BindingFlags.NonPublic | BindingFlags.Instance);
          var dragItem = dragItemField?.GetValue(__instance) as ItemDrop.ItemData;

            if (dragItem == null)
           return true;

  // Check which screen/slot we're dropping on
       if (InventoryGuiPatches.IsOverDressingRoomSlot())
            {
            return HandleDressingRoomDropOutside(__instance, dragItem);
            }
   else if (InventoryGuiPatches.IsOverCompanionSlot())
      {
         return HandleCompanionDropOutside(__instance, dragItem);
            }
     else if (InventoryGuiPatches.IsOverCompanionStorageSlot())
   {
       return HandleCompanionStorageDropOutside(__instance, dragItem);
      }

            return true;
     }

        private static bool HandleDressingRoomDropOutside(InventoryGui __instance, ItemDrop.ItemData dragItem)
     {
      var dressingRoom = DressingRoomScreenController.Instance;
            var slot = dressingRoom?.GetSlotUnderMouse();

      if (slot == null)
              return true;

    if (!dressingRoom.IsValidItemForSlot(slot.SlotType, dragItem))
            {
MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item doesn't fit in that slot");
  var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
       setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
        return false;
  }

   string prefabName = dragItem.m_dropPrefab?.name?.Replace("(Clone)", "").Trim();
     if (string.IsNullOrEmpty(prefabName))
         return true;

        var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

  Debug.Log($"[InventoryGuiPatches] OnDropOutside - dropping {prefabName} on dressing room slot {slot.SlotType}");

      if (sourceInventory != null && sourceInventory.ContainsItem(dragItem))
          {
         sourceInventory.RemoveItem(dragItem);
      }

            dressingRoom.OnItemDropped(slot.SlotType, prefabName);
            InventoryGui_OnSelectedItem_Patch.ClearDragAndUpdateUI(__instance);
     return false;
  }

        private static bool HandleCompanionDropOutside(InventoryGui __instance, ItemDrop.ItemData dragItem)
        {
         var companionScreen = CompanionInventoryScreen.Instance;
        var slot = companionScreen?.GetSlotUnderMouse();

 if (slot == null)
     return true;

            if (!companionScreen.IsValidItemForSlot(slot.SlotType, dragItem))
   {
      MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item doesn't fit in that slot");
        var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
  setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
  return false;
   }

       string prefabName = dragItem.m_dropPrefab?.name?.Replace("(Clone)", "").Trim();
            if (string.IsNullOrEmpty(prefabName))
            return true;

    var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
        var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

         Debug.Log($"[InventoryGuiPatches] OnDropOutside - dropping {prefabName} on companion slot {slot.SlotType}");

         if (sourceInventory != null && sourceInventory.ContainsItem(dragItem))
            {
   sourceInventory.RemoveItem(dragItem);
      }

            companionScreen.OnItemDropped(slot.SlotType, prefabName);
 InventoryGui_OnSelectedItem_Patch.ClearDragAndUpdateUI(__instance);
 return false;
      }

     private static bool HandleCompanionStorageDropOutside(InventoryGui __instance, ItemDrop.ItemData dragItem)
        {
            var companionScreen = CompanionInventoryScreen.Instance;
 var storageSlot = companionScreen?.GetStorageSlotUnderMouse();

            if (storageSlot == null)
      return true;

            if (!companionScreen.IsValidItemForStorage(dragItem))
            {
      MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This item can't be stored here");
  var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", BindingFlags.NonPublic | BindingFlags.Instance);
      setupMethod?.Invoke(__instance, new object[] { null, null, 1 });
  return false;
            }

 var dragInvField = typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.NonPublic | BindingFlags.Instance);
            var sourceInventory = dragInvField?.GetValue(__instance) as Inventory;

            Debug.Log($"[InventoryGuiPatches] OnDropOutside - dropping {dragItem.m_shared.m_name} on companion storage slot {storageSlot.SlotIndex}");

            // Clone the item before removing
      var itemClone = dragItem.Clone();

            if (sourceInventory != null && sourceInventory.ContainsItem(dragItem))
 {
         sourceInventory.RemoveItem(dragItem);
}

 companionScreen.OnStorageItemDropped(storageSlot, itemClone);
         InventoryGui_OnSelectedItem_Patch.ClearDragAndUpdateUI(__instance);
            return false;
        }

        static System.Exception Finalizer(System.Exception __exception)
      {
            if (__exception is System.NullReferenceException)
            {
   return null;
            }
     return __exception;
        }
    }

    /// <summary>
    /// Patch InventoryGui.SetupDragItem to process pending drops after drag state is cleared.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
    public static class InventoryGui_SetupDragItem_Patch
    {
        static void Postfix(ItemDrop.ItemData item)
        {
   // Process any pending drops after drag state is cleared
    if (item == null)
      {
      InventoryGuiPatches.ProcessPendingDrop();
         }
        }
    }
}
