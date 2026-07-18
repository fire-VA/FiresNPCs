using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FiresRPGmaker.Modules.PlayersTag
{
    /// <summary>
    /// Renders a player's cosmetic title above its EnemyHud nameplate, read live from the
    /// Fires_PlayerTag ZDO string. Client-only (EnemyHud) — in Ascend's dedi exclusion set.
    /// </summary>
    internal static class PlayerTagNameplatePatches
    {
        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        private static class EnemyHud_ShowHud_Tag
        {
            private static void Postfix(EnemyHud __instance, Character c)
            {
                try
                {
                    if (c == null || __instance == null || __instance.m_huds == null) return;
                    if (!__instance.m_huds.TryGetValue(c, out var hud) || hud == null || hud.m_gui == null) return;

                    var label = EnsureLabel(hud.m_gui.transform);
                    if (label == null) return;

                    string tag = PlayerTagManager.TagOf(c);
                    if (string.IsNullOrEmpty(tag)) { label.gameObject.SetActive(false); return; }
                    label.gameObject.SetActive(true);
                    label.text = $"<color=#C9A24B>[{tag}]</color>";
                }
                catch { }
            }
        }

        private static TMP_Text EnsureLabel(Transform gui)
        {
            var existing = gui.Find("Fires_TagLabel");
            if (existing != null) return existing.GetComponent<TMP_Text>();

            var nameText = gui.GetComponentInChildren<TMP_Text>(true);
            if (nameText == null) return null;
            var go = Object.Instantiate(nameText.gameObject, gui);
            go.name = "Fires_TagLabel";
            var t = go.GetComponent<TMP_Text>();
            t.fontSize = Mathf.Max(8f, nameText.fontSize * 0.7f);
            var rt = (RectTransform)go.transform;
            var nrt = (RectTransform)nameText.transform;
            rt.anchoredPosition = nrt.anchoredPosition + new Vector2(0f, 14f);   // above the name
            return t;
        }
    }

    /// <summary>Write the local tag to the ZDO on spawn so other clients see it immediately.</summary>
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class Player_OnSpawned_TagRefresh
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            try { PlayerTagManager.RefreshLocal(); } catch { }
        }
    }
}
