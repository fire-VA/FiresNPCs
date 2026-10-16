using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.TableGames
{
    /// <summary>
    /// Opt-in long-range [E] interaction for game boards — put this on a piece prefab and the local player can hover
    /// and use it from up to <see cref="Range"/> metres away (line of sight required; anything solid blocks the ray).
    /// Vanilla owns close-range interaction untouched: the driver only engages when vanilla has no hover target.
    /// Chess boards use it for arena-scale play; any other Fires game interactable can add the component too.
    /// </summary>
    public sealed class LongRangeInteract : MonoBehaviour
    {
        public float Range = 60f;
    }

    internal static class LongRangeInteractDriver
    {
        internal static LongRangeInteract Target;    // this frame's long-range hover (local client only)
        internal static float TargetDist;
        private static int _mask;
        private static readonly RaycastHit[] _hits = new RaycastHit[24];

        private static bool UiBusy()
        {
            if (global::FiresCore.UI.Minigames.FiresMinigameOverlay.Current != null) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            if (global::Console.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen()) return true;
            if (InventoryGui.IsVisible() || StoreGui.IsVisible() || Menu.IsVisible()) return true;
            if (Hud.IsPieceSelectionVisible()) return true;
            return false;
        }

        internal static void Scan(Player player)
        {
            Target = null;
            var cam = GameCamera.instance;
            if (cam == null || player == null || player != Player.m_localPlayer) return;
            if (player.IsDead() || player.IsTeleporting() || player.InCutscene() || player.InPlaceMode()) return;
            if (UiBusy()) return;
            if (player.GetHoverObject() != null) return;   // vanilla range — its flow owns the interaction

            if (_mask == 0)
                _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                    "terrain", "vehicle", "character", "character_net");

            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, 90f, _mask);
            int first = -1;
            float firstDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null || col.isTrigger) continue;
                if (col.GetComponentInParent<Player>() == player) continue;   // the camera ray clips the local body
                if (_hits[i].distance < firstDist) { firstDist = _hits[i].distance; first = i; }
            }
            // 0.0.565 (FDT 582 long_range "no popup at 20 m"): say what the ray hit when E is pressed and nothing engages.
            bool usePressed = ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse");
            if (first < 0) { if (usePressed) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LongRange] E: the ray hit nothing within 90 m ({n} non-solid/own hit(s))"); return; }

            var lri = _hits[first].collider.GetComponentInParent<LongRangeInteract>();
            if (lri == null) { if (usePressed) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LongRange] E: first hit '{_hits[first].collider.name}' (root '{_hits[first].collider.transform.root.name}') at {firstDist:F1} m has no LongRangeInteract"); return; }

            var eye = player.m_eye != null ? player.m_eye.position : player.transform.position + Vector3.up * 1.6f;
            float dist = Vector3.Distance(eye, _hits[first].point);
            if (dist > lri.Range) { if (usePressed) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LongRange] E: '{lri.name}' at {dist:F1} m is past its range {lri.Range:F0} m"); return; }

            Target = lri;
            TargetDist = dist;

            if ((ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) && !player.InAttack() && !player.InDodge()
                && !FiresCore.Input.FiresInputBlock.IsTyping()) // 0.0.550 typing gate (Fire 10-09)
            {
                var interactable = lri.GetComponentInParent<Interactable>();
                bool used = interactable != null && interactable.Interact(player, false, false);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LongRange] E: '{lri.name}' at {dist:F1} m -> {(interactable == null ? "no Interactable" : used ? "used" : "Interact returned false")}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class LongRange_Player_Update_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            try { LongRangeInteractDriver.Scan(__instance); }
            catch { LongRangeInteractDriver.Target = null; }
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
    internal static class LongRange_Hud_Crosshair_Patch
    {
        private static void Postfix(Hud __instance, Player player)
        {
            var t = LongRangeInteractDriver.Target;
            if (t == null || player != Player.m_localPlayer) return;
            if (__instance.m_hoverName == null || !string.IsNullOrEmpty(__instance.m_hoverName.text)) return;

            var hoverable = t.GetComponentInParent<Hoverable>();
            if (hoverable == null) return;
            string txt = hoverable.GetHoverText();
            if (LongRangeInteractDriver.TargetDist > 8f)
                txt += "\n<color=#9a9a9a>" + Mathf.RoundToInt(LongRangeInteractDriver.TargetDist) + " m</color>";
            __instance.m_hoverName.text = txt;
            if (__instance.m_crosshair != null) __instance.m_crosshair.color = Color.yellow;
        }
    }
}
