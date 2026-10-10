using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Stamps a small "T" badge on every Valheim inventory slot whose item has a transmog override
    /// applied for the local player. Mirrors the pattern FAP uses for baked-prefab indicators —
    /// at-a-glance visual cue that the item's appearance is being overridden.
    ///
    /// Patched onto <c>InventoryGrid.UpdateGui</c> so the badge follows the item across all inventory
    /// contexts (player inventory, crafting station, chests). Client-only — skipped on dedi.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    public static class InventoryGrid_TransmogMarker_Patch
    {
        private const string MarkerName = "FiresTransmogMarker";
        private static readonly AccessTools.FieldRef<InventoryGrid, Inventory> _invRef =
            AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> _elementsRef =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        [HarmonyPostfix]
        private static void Postfix(InventoryGrid __instance)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null) return;
            string playerName = localPlayer.GetPlayerName();
            if (string.IsNullOrEmpty(playerName)) return;

            var inventory = _invRef(__instance);
            if (inventory == null) return;
            var elements = _elementsRef(__instance);
            if (elements == null || elements.Count == 0) return;

            // First sweep: hide every marker. Items that should keep it get re-enabled in the next loop.
            // Cheap because Find returns null after the first miss; we only iterate slots that already
            // got a marker stamped previously.
            for (int i = 0; i < elements.Count; i++)
            {
                var go = elements[i].gameObject;
                if (go == null) continue;
                var existing = go.transform.Find(MarkerName);
                if (existing != null) existing.gameObject.SetActive(false);
            }

            // Second sweep: for each item, ensure its slot's marker exists + is active.
            int width = inventory.GetWidth();
            foreach (var item in inventory.GetAllItems())
            {
                string prefab = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (string.IsNullOrEmpty(prefab)) continue;
                if (!TransmogState.HasTransmog(playerName, prefab)) continue;

                int idx = item.m_gridPos.y * width + item.m_gridPos.x;
                if (idx < 0 || idx >= elements.Count) continue;
                EnsureMarker(elements[idx].gameObject);
            }
        }

        /// <summary>Ensures the marker exists on <paramref name="slotGo"/> and is active. Cheap repeat-call:
        /// reuses the existing child if present, only constructs the badge on first stamp.</summary>
        private static void EnsureMarker(GameObject slotGo)
        {
            if (slotGo == null) return;
            var existing = slotGo.transform.Find(MarkerName);
            if (existing != null) { existing.gameObject.SetActive(true); return; }

            // Cyan-tinted square in the top-right corner of the slot with a bold "T" glyph.
            var go = new GameObject(MarkerName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(slotGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.62f, 0.62f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.55f, 0.95f, 0.85f);
            img.raycastTarget = false;

            var textGo = new GameObject("T", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.text = "T";
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            // Font size scales with slot — InventoryGrid slot is ~64px so 14f reads clearly at the corner.
            t.fontSize = 14f;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(t);
        }
    }
}
