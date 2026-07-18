using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.UI;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Per-NPC nameplate controls for static NPCs, driven by <see cref="NpcController"/> values: hide the
    /// floating name, scale the whole plate, and raise/lower it on the Y axis. EnemyHud pools/recreates huds
    /// and rewrites the name every frame, so these are re-applied each frame in a UpdateHuds postfix. Only
    /// characters that carry an NpcController are touched, so vanilla creatures are never affected (a hud
    /// that gets recreated simply starts at vanilla defaults and this re-applies our values next frame).
    ///
    /// The Companions.StaticNpcs/NameplateStyle config picks the whole display: HealthBar keeps this vanilla
    /// plate; HoverName / Hide suppress it entirely via the TestShow gate below (TestShow is the single gate
    /// EnemyHud uses both to ADD a hud and to KEEP one, so forcing it false also destroys existing plates on
    /// the next frame — flipping the config applies live). HoverName's raised world-space label is drawn by
    /// <see cref="NpcHoverNameplate"/> on the NPC itself.
    /// </summary>
    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    internal static class NpcNameplatePatches
    {
        [HarmonyPatch(typeof(EnemyHud), "TestShow", new[] { typeof(Character), typeof(bool) })]
        private static class EnemyHud_TestShow_NameplateMode
        {
            private static void Postfix(Character c, ref bool __result)
            {
                if (!__result || c == null) return;
                var mode = ConfigManager.Instance?.configStaticNpcNameplateMode?.Value ?? StaticNpcNameplateMode.HealthBar;
                if (mode == StaticNpcNameplateMode.HealthBar) return;
                if (c.GetComponent<NpcController>() != null) __result = false;
            }
        }

        private static void Postfix(EnemyHud __instance)
        {
            if (__instance == null || __instance.m_huds == null) return;

            Camera cam = Utils.GetMainCamera();
            try
            {
                foreach (var kvp in __instance.m_huds)
                {
                    var ch = kvp.Key;
                    if (ch == null) continue;
                    var npc = ch.GetComponent<NpcController>();
                    if (npc == null) continue;
                    var hud = kvp.Value;
                    if (hud == null || hud.m_gui == null) continue;

                    // Hide the name label (the health bar, if any, stays).
                    if (hud.m_name != null && hud.m_name.gameObject.activeSelf == npc.hideNameplate)
                        hud.m_name.gameObject.SetActive(!npc.hideNameplate);

                    // Scale the whole plate.
                    float s = Mathf.Max(0.1f, npc.nameplateScale);
                    if (hud.m_gui.transform.localScale.x != s)
                        hud.m_gui.transform.localScale = new Vector3(s, s, s);

                    // Raise/lower on Y, matching how vanilla world-anchors the plate (GetTopPoint).
                    if (cam != null && hud.m_gui.activeSelf && Mathf.Abs(npc.nameplateYOffset) > 0.001f)
                    {
                        Vector3 world = ch.GetTopPoint() + Vector3.up * npc.nameplateYOffset;
                        hud.m_gui.transform.position = cam.WorldToScreenPointScaled(world);
                    }
                }
            }
            catch { }
        }
    }
}
