using System.Collections.Generic;
using HarmonyLib;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Hides vanilla EnemyHud floating nameplates + health bars while an NPC interaction panel is open.
    /// EnemyHud.TestShow is the single visibility gate the hud uses both to ADD a hud (EnemyHud.Update)
    /// and to KEEP one (EnemyHud.UpdateHuds); forcing it false destroys any existing hud on the next frame
    /// and stops it being re-added.
    ///
    /// Two modes:
    ///   • Suppress(Character) — targeted, only that NPC's hud is hidden (player UIs use this on the
    ///     specific NPC they're interacting with).
    ///   • SuppressAll(true)   — global, ALL hover huds hidden (admin panel + player panels use this so
    ///     nearby NPC/Companion nameplates don't bleed through the open panel).
    /// </summary>
    public static class NpcHoverHudSuppressor
    {
        private static readonly HashSet<Character> _suppressed = new HashSet<Character>();
        private static bool _suppressAll;

        public static void Suppress(Character c) { if ((object)c != null) _suppressed.Add(c); }

        public static void Clear() { _suppressed.Clear(); _suppressAll = false; }

        public static void SuppressAll(bool on) => _suppressAll = on;

        public static bool IsSuppressed(Character c) => _suppressAll || ((object)c != null && _suppressed.Contains(c));

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
