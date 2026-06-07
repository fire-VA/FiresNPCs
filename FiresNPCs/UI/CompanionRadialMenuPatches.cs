using HarmonyLib;
using UnityEngine;
using FiresCore.Npc;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Harmony patches to integrate CompanionRadialMenu with companion interaction.
    /// 
    /// TRIGGER BEHAVIOR:
    /// - Hold R while looking at a companion to open the radial menu
    /// - Works with both mouse and gamepad
    /// </summary>
    [HarmonyPatch]
    public static class CompanionRadialMenuPatches
    {
        private static CompanionController _hoveredCompanion;
        private static float _keyHoldTime;
        private static bool _menuOpenedThisHold;

        private static CompanionController _hoveredCompanionH;
        private static float _hKeyHoldTime;
        private static bool _hMenuOpenedThisHold;

        private const float HoldThreshold = 0.15f;
        private const KeyCode RadialKey = KeyCode.R;
        private const KeyCode IdleBehaviorKey = KeyCode.H;

        /// <summary>
        /// Updates every frame to check for radial menu trigger.
        /// </summary>
        public static void UpdateRadialMenuInput()
        {
            // Don't process if UI is already open
            if (Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible() ||
                InventoryGui.IsVisible() || StoreGui.IsVisible())
            {
                if (CompanionRadialMenu.Instance?.IsVisible == true)
                    CompanionRadialMenu.Instance.Hide();
                if (CompanionIdleBehaviorRadial.Instance?.IsVisible == true)
                    CompanionIdleBehaviorRadial.Instance.Hide();
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null) return;

            // --- R key: main command radial ---
            bool rHeld = Input.GetKey(RadialKey);
            if (rHeld)
            {
                if (!_menuOpenedThisHold)
                {
                    _keyHoldTime += Time.deltaTime;
                    var companion = GetCompanionUnderCrosshair(player);
                    if (companion != null && _keyHoldTime >= HoldThreshold)
                    {
                        CompanionRadialMenu.EnsureInstance();
                        CompanionRadialMenu.Instance?.Show(companion);
                        _menuOpenedThisHold = true;
                        _hoveredCompanion = companion;
                    }
                }
            }
            else
            {
                _keyHoldTime = 0f;
                _menuOpenedThisHold = false;
                _hoveredCompanion = null;
            }

            // --- H key: idle behavior radial ---
            bool hHeld = Input.GetKey(IdleBehaviorKey);
            if (hHeld)
            {
                if (!_hMenuOpenedThisHold)
                {
                    _hKeyHoldTime += Time.deltaTime;
                    var companion = GetCompanionUnderCrosshair(player);
                    if (companion != null && _hKeyHoldTime >= HoldThreshold)
                    {
                        CompanionIdleBehaviorRadial.EnsureInstance();
                        CompanionIdleBehaviorRadial.Instance?.Show(companion);
                        _hMenuOpenedThisHold = true;
                        _hoveredCompanionH = companion;
                    }
                }
            }
            else
            {
                _hKeyHoldTime = 0f;
                _hMenuOpenedThisHold = false;
                _hoveredCompanionH = null;
            }
        }
        
        /// <summary>
        /// Finds a companion the player is looking at.
        /// </summary>
        private static CompanionController GetCompanionUnderCrosshair(Player player)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            
            Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
            RaycastHit hit;
            
            int layerMask = LayerMask.GetMask("character", "Default");
            if (layerMask == 0) layerMask = ~0;
            
            if (Physics.Raycast(ray, out hit, 10f, layerMask))
            {
                var companion = hit.collider.GetComponent<CompanionController>() ??
                                hit.collider.GetComponentInParent<CompanionController>();
                
                if (companion != null && companion.isTamed && companion.IsOwner(player))
                {
                    return companion;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Patch Player.Update to inject our input processing.
        /// </summary>
        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        private static void Player_Update_Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            
            UpdateRadialMenuInput();
        }
        
        /// <summary>
        /// Prevents normal R key behavior when radial menu is about to open or is open.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        [HarmonyPrefix]
        private static bool Player_UpdatePlacement_Prefix(Player __instance)
        {
            if ((Input.GetKey(RadialKey) && _keyHoldTime > 0.1f) ||
                (Input.GetKey(IdleBehaviorKey) && _hKeyHoldTime > 0.1f))
            {
                var companion = GetCompanionUnderCrosshair(__instance);
                if (companion != null)
                    return false;
            }

            if (CompanionRadialMenu.Instance?.IsVisible == true ||
                CompanionIdleBehaviorRadial.Instance?.IsVisible == true)
                return false;

            return true;
        }

        [HarmonyPatch(typeof(Player), "SetControls")]
        [HarmonyPrefix]
        private static bool Player_SetControls_Prefix(Player __instance)
        {
            if (CompanionRadialMenu.Instance?.IsVisible == true ||
                CompanionIdleBehaviorRadial.Instance?.IsVisible == true)
            {
                __instance.SetMoveDir(Vector3.zero);
                return false;
            }
            return true;
        }
    }
}
