using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Shows a character's faction name (gold) under its EnemyHud nameplate, read live from the
    /// character's <c>MPASN_FactionId</c> ZDO string → the synced faction table. When the faction defines an
    /// Icon that resolves to an item sprite (see <see cref="FactionIcons"/>), that crest renders just left of
    /// the name. Client-only (EnemyHud is client UI — this type is in Ascend's dedi patch-exclusion set).
    /// </summary>
    internal static class FactionNameplatePatches
    {
        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        private static class EnemyHud_ShowHud_Faction
        {
            private static void Postfix(EnemyHud __instance, Character c)
            {
                try
                {
                    if (c == null || __instance == null || __instance.m_huds == null) return;
                    if (!__instance.m_huds.TryGetValue(c, out var hud) || hud == null || hud.m_gui == null) return;

                    var label = EnsureLabel(hud.m_gui.transform);
                    if (label == null) return;
                    var icon = EnsureIcon(hud.m_gui.transform, label);

                    var f = FactionMembership.FactionOf(c);
                    if (f == null)
                    {
                        label.gameObject.SetActive(false);
                        if (icon != null) icon.enabled = false;
                        return;
                    }
                    label.gameObject.SetActive(true);
                    label.text = $"<color=#FFD966>{f.Name}</color>";

                    var sprite = FactionIcons.Resolve(f.Icon);
                    if (icon != null)
                    {
                        if (sprite != null)
                        {
                            icon.enabled = true;
                            icon.sprite = sprite;
                            // Park the crest just past the name's left edge (preferredWidth is text-dependent).
                            label.ForceMeshUpdate();
                            var lrt = (RectTransform)label.transform;
                            var irt = (RectTransform)icon.transform;
                            irt.anchoredPosition = new Vector2(lrt.anchoredPosition.x - label.preferredWidth * 0.5f - 9f, lrt.anchoredPosition.y);
                        }
                        else icon.enabled = false;
                    }
                }
                catch { }
            }
        }

        private static TMP_Text EnsureLabel(Transform gui)
        {
            var existing = gui.Find("MPASN_FactionLabel");
            if (existing != null) return existing.GetComponent<TMP_Text>();

            // Clone the nameplate's own name text so the faction line matches its font/outline.
            var nameText = gui.GetComponentInChildren<TMP_Text>(true);
            if (nameText == null) return null;
            var go = Object.Instantiate(nameText.gameObject, gui);
            go.name = "MPASN_FactionLabel";
            var t = go.GetComponent<TMP_Text>();
            t.fontSize = Mathf.Max(8f, nameText.fontSize * 0.72f);
            var rt = (RectTransform)go.transform;
            var nrt = (RectTransform)nameText.transform;
            // kg FactionNameplateOffset (MarketPlace.cfg "x,y") shifts the faction line; the icon anchors
            // off this label, so the pair moves together.
            rt.anchoredPosition = nrt.anchoredPosition + new Vector2(0f, -12f)
                + global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.FactionNameplateOffset;
            return t;
        }

        private static Image EnsureIcon(Transform gui, TMP_Text label)
        {
            var existing = gui.Find("MPASN_FactionIcon");
            if (existing != null) return existing.GetComponent<Image>();

            var go = new GameObject("MPASN_FactionIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(gui, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = (RectTransform)go.transform;
            var lrt = (RectTransform)label.transform;
            rt.anchorMin = lrt.anchorMin; rt.anchorMax = lrt.anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(14f, 14f);
            return img;
        }
    }

    /// <summary>Re-resolve the local player's faction onto its ZDO + buff whenever it spawns/loads.</summary>
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class Player_OnSpawned_FactionRefresh
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            try { FactionMembership.RefreshLocal(false); } catch { }
        }
    }
}
