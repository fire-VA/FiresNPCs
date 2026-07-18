using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Owner-side capture triggers for the skills feed (see <see cref="LeaderboardSkillFeed"/>). Mirrors
    /// <see cref="LeaderboardAppearanceCapturePatches"/>: every hook is gated to the LOCAL player's
    /// <see cref="Skills"/> instance (so it auto-no-ops on a headless server, where m_localPlayer is null) and
    /// only ever SENDS when the skill signature actually changed, on a debounce, never per-frame. Triggers:
    /// <list type="bullet">
    /// <item>Full snapshot on <c>Player.OnSpawned</c> — the authoritative initial state.</item>
    /// <item>Level changes — postfixes on <c>Skills.RaiseSkill</c> / <c>Skills.CheatRaiseSkill</c> /
    /// <c>Skills.ResetSkill</c> (the three vanilla mutation points), each requesting a debounced flush.</item>
    /// <item>Debounced flush — a ~2 s throttled <c>Player.Update</c> tick that coalesces a burst of RaiseSkill
    /// calls into a single batch send and re-checks the signature.</item>
    /// </list>
    /// All targets (Skills/Player) exist server-side, so this class does NOT need the dedi skip-set — the
    /// m_localPlayer gate already makes every path a no-op on a dedicated server.
    /// </summary>
    public static class LeaderboardSkillCapturePatches
    {
        private const float FlushDebounceSeconds = 2f;

        private static string _lastSentSignature;
        private static bool _hasSent;
        private static bool _flushPending;
        private static float _flushTimer;

        // True only when this Skills instance belongs to the local player (the single owner-gate for all hooks).
        private static bool IsLocalSkills(Skills skills)
        {
            var local = Player.m_localPlayer;
            return skills != null && local != null && skills == local.GetSkills();
        }

        private static List<LeaderboardSkillEntry> BuildLocalSkillSnapshot()
        {
            var result = new List<LeaderboardSkillEntry>();
            var local = Player.m_localPlayer;
            var skills = local != null ? local.GetSkills() : null;
            if (skills == null) return result;

            var list = skills.GetSkillList();
            if (list == null) return result;
            foreach (var s in list)
            {
                if (s == null || s.m_info == null) continue;
                int level = Mathf.RoundToInt(s.m_level);
                result.Add(new LeaderboardSkillEntry(s.m_info.m_skill.ToString(), level));
            }
            return result;
        }

        private static string Signature(List<LeaderboardSkillEntry> snapshot)
        {
            // Order is stable per-dictionary within a session; level changes (the thing we react to) always move
            // the joined string, so a plain join is a sufficient change key.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < snapshot.Count; i++)
            {
                sb.Append(snapshot[i].Skill).Append('~').Append(snapshot[i].Level).Append('|');
            }
            return sb.ToString();
        }

        private static void FlushIfChanged()
        {
            var local = Player.m_localPlayer;
            if (local == null) return;

            var snapshot = BuildLocalSkillSnapshot();
            if (snapshot.Count == 0) return;

            string sig = Signature(snapshot);
            if (_hasSent && sig == _lastSentSignature) return;

            LeaderboardSkillFeed.Send(local.GetPlayerName(), snapshot);
            _lastSentSignature = sig;
            _hasSent = true;
        }

        private static void RequestDebouncedFlush()
        {
            _flushPending = true;
            _flushTimer = 0f;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        public static class Player_OnSpawned
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                // Authoritative full snapshot once the player spawns in — flush immediately (no debounce).
                FlushIfChanged();
            }
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        public static class Skills_RaiseSkill
        {
            [HarmonyPostfix]
            private static void Postfix(Skills __instance)
            {
                if (!IsLocalSkills(__instance)) return;
                RequestDebouncedFlush();
            }
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.CheatRaiseSkill))]
        public static class Skills_CheatRaiseSkill
        {
            [HarmonyPostfix]
            private static void Postfix(Skills __instance)
            {
                if (!IsLocalSkills(__instance)) return;
                RequestDebouncedFlush();
            }
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.ResetSkill))]
        public static class Skills_ResetSkill
        {
            [HarmonyPostfix]
            private static void Postfix(Skills __instance)
            {
                if (!IsLocalSkills(__instance)) return;
                RequestDebouncedFlush();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public static class Player_Update
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance == null || __instance != Player.m_localPlayer || !_flushPending) return;
                _flushTimer += Time.deltaTime;
                if (_flushTimer < FlushDebounceSeconds) return;
                _flushPending = false;
                _flushTimer = 0f;
                FlushIfChanged();
            }
        }
    }
}
