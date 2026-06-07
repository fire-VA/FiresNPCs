using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace FiresRPGmaker.Modules.Companions
{
    /// <summary>
    /// Harmony patches for companion inventory interaction with Valheim's InventoryGui.
    /// Handles drag & drop from player inventory to companion equipment slots.
    /// Also includes safety patches to prevent crashes from invalid item data.
    /// </summary>
    [HarmonyPatch]
    public static class CompanionInventoryPatches
    {
        // Reflection for InventoryGui drag fields
        private static readonly FieldInfo fi_dragItem = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly FieldInfo fi_dragInventory = AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
        private static readonly FieldInfo fi_dragIcon = AccessTools.Field(typeof(InventoryGui), "m_dragGo");
        
        #region Drag State Helpers
        
        /// <summary>
        /// Gets the currently dragged item from InventoryGui.
        /// </summary>
        public static ItemDrop.ItemData GetDraggedItem()
        {
            if (InventoryGui.instance == null) return null;
            return fi_dragItem?.GetValue(InventoryGui.instance) as ItemDrop.ItemData;
        }
        
        /// <summary>
        /// Gets the inventory the dragged item came from.
        /// </summary>
        public static Inventory GetDragInventory()
        {
            if (InventoryGui.instance == null) return null;
            return fi_dragInventory?.GetValue(InventoryGui.instance) as Inventory;
        }
        
        /// <summary>
        /// Clears the drag state in InventoryGui.
        /// </summary>
        public static void ClearDragState()
        {
            try
            {
                if (InventoryGui.instance == null) return;
                
                fi_dragItem?.SetValue(InventoryGui.instance, null);
                fi_dragInventory?.SetValue(InventoryGui.instance, null);
                
                var dragGo = fi_dragIcon?.GetValue(InventoryGui.instance) as GameObject;
                if (dragGo != null)
                {
                    UnityEngine.Object.Destroy(dragGo);
                    fi_dragIcon?.SetValue(InventoryGui.instance, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryPatches] Failed to clear drag state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Sets up the drag state in InventoryGui (for dragging from companion to player).
        /// </summary>
        public static void SetDragItem(Inventory inventory, ItemDrop.ItemData item, int amount)
        {
            try
            {
                if (InventoryGui.instance == null) return;
                
                // Use reflection to call SetupDragItem
                var setupMethod = typeof(InventoryGui).GetMethod("SetupDragItem", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                    
                if (setupMethod != null)
                {
                    setupMethod.Invoke(InventoryGui.instance, new object[] { item, inventory, amount });
                }
                else
                {
                    // Fallback: set fields directly
                    fi_dragItem?.SetValue(InventoryGui.instance, item);
                    fi_dragInventory?.SetValue(InventoryGui.instance, inventory);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionInventoryPatches] Failed to set drag item: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if the companion inventory screen is currently open.
        /// </summary>
        public static bool IsCompanionInventoryOpen()
        {
            return CompanionInventoryScreen.IsOpen;
        }
        
        #endregion
        
        #region Safety Patches - Prevent Crashes from Invalid Item Data
        
        /// <summary>
        /// Harmony patch to prevent IndexOutOfRangeException in ItemDrop.ItemData.GetIcon().
        /// This occurs when m_variant is out of bounds for the m_icons array.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon))]
        private static class ItemData_GetIcon_SafetyPatch
        {
            private static bool Prefix(ItemDrop.ItemData __instance, ref Sprite __result)
            {
                try
                {
                    // Check for null or empty icons array
                    if (__instance.m_shared?.m_icons == null || __instance.m_shared.m_icons.Length == 0)
                    {
                        __result = null;
                        return false; // Skip original method
                    }
                    
                    // Clamp m_variant to valid range to prevent IndexOutOfRangeException
                    if (__instance.m_variant < 0 || __instance.m_variant >= __instance.m_shared.m_icons.Length)
                    {
                        __instance.m_variant = Mathf.Clamp(__instance.m_variant, 0, __instance.m_shared.m_icons.Length - 1);
                    }
                    
                    // Let original method run with corrected variant
                    return true;
                }
                catch (Exception)
                {
                    __result = null;
                    return false; // Skip original on any error
                }
            }
        }
        
        #endregion
        
        #region InventoryGui Patches
        
        /// <summary>
        /// Harmony patch for InventoryGui.UpdateItemDrag to allow dropping on companion slots.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateItemDrag")]
        private static class InventoryGui_UpdateItemDrag_CompanionIntegration
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!IsCompanionInventoryOpen()) return;
                
                var dragItem = GetDraggedItem();
                if (dragItem == null) return;
                
                // Check if mouse is over companion inventory panel
                // The actual drop handling is done by the slot click handlers
            }
        }
        
        /// <summary>
        /// Harmony patch to prevent InventoryGui from closing when companion inventory is open.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        private static class InventoryGui_Hide_PreventWhenCompanionOpen
        {
            private static bool Prefix()
            {
                // Allow hiding if companion inventory is also being closed
                if (IsCompanionInventoryOpen() && !CompanionInventoryScreen.IsClosing)
                {
                    // Close companion inventory first
                    CompanionInventoryScreen.Close();
                }
                return true;
            }
        }
        
        /// <summary>
        /// Harmony patch to sync companion inventory with player inventory updates.
        /// NOTE: We no longer call RefreshIfOpen() every frame here because it was 
        /// causing the UI to glitch by rebuilding equipment visuals every frame.
        /// Equipment visual updates are now handled on-demand when items are actually
        /// added/removed via the slot click handlers.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateInventory")]
        private static class InventoryGui_UpdateInventory_RefreshCompanion
        {
            private static void Postfix(InventoryGui __instance)
            {
                // Intentionally empty - we no longer refresh every frame
                // The companion inventory screen handles its own updates when
                // equipment changes are made through the UI
            }
        }
        
        #endregion
    }
}
