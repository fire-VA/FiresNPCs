using HarmonyLib;
using UnityEngine;

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

        // Holds the camera still while a custom panel is open — replaces an older prefix that returned false to
        // SKIP GameCamera.UpdateCamera. Skipping UpdateCamera stops vanilla from repositioning the camera each
        // frame, so a camera mod that applies a RELATIVE offset in GameCamera.LateUpdate (e.g. FiresValcast)
        // accumulates its offset every frame and flies the camera "way out". Instead we let the camera update
        // normally, then pin its transform to wherever it was when the panel opened — overriding both vanilla
        // follow and any later camera-mod offset (lowest priority => runs last). Player.TakeInput is already
        // blocked, so the player can't move under the held camera, and the pin doubles as the look-drift guard
        // the old prefix provided.
        private static bool _camPinned;
        private static Vector3 _camPinPos;
        private static Quaternion _camPinRot;

        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        [HarmonyPostfix]
        [HarmonyPriority(int.MinValue)]
        private static void GameCamera_LateUpdate_Postfix(GameCamera __instance)
        {
            if (!GUIManager.ShouldBlockGameInput()) { _camPinned = false; return; }
            var t = __instance.transform;
            if (_camPinned) { t.position = _camPinPos; t.rotation = _camPinRot; }
            else { _camPinPos = t.position; _camPinRot = t.rotation; _camPinned = true; }
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
