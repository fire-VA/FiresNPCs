using HarmonyLib;
using UnityEngine;
using System.Reflection;
using FiresRPGmaker.UI.Screens;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Harmony patches for InventoryGui to intercept item drops when over the NPC Dressing Room slots,
    /// so items aren't dropped on the ground when the player releases them over our UI. (Companion
    /// inventory/storage drag handling was removed in the RPGMaker decouple — companion gear lives in
    /// FiresCompanions now; only the static-NPC Dressing Room remains here.)
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

        /// <summary>Checks if the mouse is currently over a dressing room equipment slot.</summary>
        public static bool IsOverDressingRoomSlot()
        {
            var dressingRoom = DressingRoomScreenController.Instance;
            if (dressingRoom == null || !dressingRoom.IsVisible)
                return false;
            return dressingRoom.GetSlotUnderMouse() != null;
        }

        /// <summary>Checks if dressing room is visible (even if not over a slot).</summary>
        public static bool IsDressingRoomVisible()
        {
            var dressingRoom = DressingRoomScreenController.Instance;
            return dressingRoom != null && dressingRoom.IsVisible;
        }

        /// <summary>Checks if any of our equipment screens are visible.</summary>
        public static bool IsAnyEquipmentScreenVisible() => IsDressingRoomVisible();

        /// <summary>Checks if mouse is over any of our equipment slots.</summary>
        public static bool IsOverAnyEquipmentSlot() => IsOverDressingRoomSlot();

        /// <summary>Gets the prefab name from an item.</summary>
        private static string GetPrefabName(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            if (item.m_dropPrefab != null)
                return item.m_dropPrefab.name.Replace("(Clone)", "").Trim();
            return null;
        }

        /// <summary>Process any pending dressing room drops, after Valheim has cleaned up its state.</summary>
        public static void ProcessPendingDrop()
        {
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
                return null; // Suppress
            return __exception;
        }
    }

    /// <summary>
    /// Patch InventoryGui.OnSelectedItem to handle dressing room drops.
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

            if (InventoryGuiPatches.IsOverDressingRoomSlot())
                return HandleDressingRoomDrop(__instance, dragItem);

            return true;
        }

        // slotOverride: the slot an EventSystem click landed on (SlotClickHandler); null = the slot under the mouse.
        internal static bool HandleDressingRoomDrop(InventoryGui __instance, ItemDrop.ItemData dragItem, DressingRoomScreenController.EquipmentSlotUI slotOverride = null)
        {
            var dressingRoom = DressingRoomScreenController.Instance;
            var slot = slotOverride ?? dressingRoom.GetSlotUnderMouse();

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
                sourceInventory.RemoveItem(dragItem);

            // Apply to dressing room
            dressingRoom.OnItemDropped(slot.SlotType, prefabName);

            // Clear drag state and update UI
            ClearDragAndUpdateUI(__instance);
            return false;
        }

        /// <summary>Clears drag state and updates UI after a successful drop.</summary>
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
                instance.m_playerGrid.UpdateInventory(Player.m_localPlayer.GetInventory(), Player.m_localPlayer, null);

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

            if (InventoryGuiPatches.IsOverDressingRoomSlot())
                return HandleDressingRoomDropOutside(__instance, dragItem);

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
                sourceInventory.RemoveItem(dragItem);

            dressingRoom.OnItemDropped(slot.SlotType, prefabName);
            InventoryGui_OnSelectedItem_Patch.ClearDragAndUpdateUI(__instance);
            return false;
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception is System.NullReferenceException)
                return null;
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
                InventoryGuiPatches.ProcessPendingDrop();
        }
    }
}
