using System.Collections.Generic;
using HarmonyLib;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Hides the vanilla EnemyHud floating nameplate + health bar for an NPC while its interaction panel
    /// is open. EnemyHud.TestShow is the single visibility gate the hud uses both to ADD a hud
    /// (EnemyHud.Update) and to KEEP one (EnemyHud.UpdateHuds); forcing it false for a suppressed Character
    /// destroys any existing hud on the next frame and stops it being re-added. Clearing the suppression
    /// (panel close) lets the hud reappear normally.
    /// </summary>
    public static class NpcHoverHudSuppressor
    {
        private static readonly HashSet<Character> _suppressed = new HashSet<Character>();

        public static void Suppress(Character c) { if ((object)c != null) _suppressed.Add(c); }

        public static void Clear() => _suppressed.Clear();

        public static bool IsSuppressed(Character c) => (object)c != null && _suppressed.Contains(c);

        [HarmonyPatch(typeof(EnemyHud), "TestShow", new[] { typeof(Character), typeof(bool) })]
        private static class TestShowPatch
        {
            private static bool Prefix(Character c, ref bool __result)
            {
                if (!IsSuppressed(c)) return true;
                __result = false;
                return false;
            }
        }
    }
}
