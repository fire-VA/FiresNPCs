using HarmonyLib;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Harmony patches to block player input (movement, camera look, attacks)
    /// when custom UI panels are open. Valheim's Player.TakeInput() is the
    /// central gate for all player actions - returning false here stops
    /// movement and combat while our panels are visible.
    /// GameCamera.UpdateCamera is patched separately because camera look
    /// runs independent of Player.TakeInput and must be suppressed directly.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerInputPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
        [HarmonyPrefix]
        private static bool Player_TakeInput_Prefix(ref bool __result)
        {
            if (GUIManager.ShouldBlockGameInput())
            {
                __result = false;
                return false;
            }
            return true;
        }

        // Blocks camera rotation while any custom UI panel has input focus.
        // GameCamera reads raw mouse delta independently of Player.TakeInput,
        // so without this patch the camera drifts whenever the cursor moves
        // over a guild/companion/quest UI element.
        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        [HarmonyPrefix]
        private static bool GameCamera_UpdateCamera_Prefix()
        {
            return !GUIManager.ShouldBlockGameInput();
        }

    }

    /// <summary>
    /// Client-only input gates added 2026-06-07: M key (Minimap) and TAB (Inventory) are not routed
    /// through Player.TakeInput, so typing into a text field while a custom UI panel is open used to
    /// pop the map/inventory underneath. These prefixes short-circuit those Update bodies while a panel
    /// is registered with GUIManager.
    /// </summary>
    [HarmonyPatch]
    public static class ClientInputGatePatches
    {
        [HarmonyPatch(typeof(Minimap), "Update")]
        [HarmonyPrefix]
        private static bool Minimap_Update_Prefix() => !GUIManager.ShouldBlockGameInput();

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPrefix]
        private static bool InventoryGui_Update_Prefix()
        {
            // Don't block when inventory IS the active UI (e.g. opened from admin Dressing Room screen).
            return !GUIManager.ShouldBlockGameInput() || (InventoryGui.instance != null && InventoryGui.IsVisible());
        }
    }
}
