using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Faction effect enforcement (the mechanics behind membership), ported from the Marketplace
    /// Factions_Client patches:
    ///   • Skill bonus  — SEMan.ModifySkillLevel postfix adds the member's BonusSkills entry.
    ///   • Friendly monsters — BaseAI.IsEnemy prefix makes listed monsters ignore members.
    ///   • Same-faction PvP — Character.RPC_Damage prefix zeroes damage between same-faction players
    ///     when the faction has CanDamageSameFaction=false.
    /// All three reference vanilla types only (SEMan/BaseAI/Character), so they run on the dedicated
    /// server too — friendly-monster aggro and PvP damage resolve server-side, so they MUST.
    /// </summary>
    internal static class FactionEnforcementPatches
    {
        // ── skill bonus (local player only, additive — matches MP) ──────────────
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifySkillLevel))]
        private static class SEMan_ModifySkillLevel_FactionBonus
        {
            private static void Postfix(SEMan __instance, Skills.SkillType skill, ref float level)
            {
                try
                {
                    if (__instance.m_character == null || __instance.m_character != Player.m_localPlayer) return;
                    var f = FactionMembership.LocalFaction();
                    if (f != null && f.BonusSkills.TryGetValue(skill, out int bonus)) level += bonus;
                }
                catch { }
            }
        }

        // ── friendly monsters (member is invisible to those monsters) ───────────
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
        private static class BaseAI_IsEnemy_Friendly
        {
            // a = the AI's character (usually the monster), b = the potential target.
            private static bool Prefix(Character a, Character b, ref bool __result)
            {
                try
                {
                    // Whichever side is a faction player, test the OTHER side's prefab against that
                    // faction's FriendlyMonsters set. Symmetric so aggro is suppressed either direction.
                    if (IsFriendly(b, a) || IsFriendly(a, b)) { __result = false; return false; }
                }
                catch { }
                return true;   // fall through to vanilla
            }

            private static bool IsFriendly(Character player, Character monster)
            {
                if (player == null || monster == null || !player.IsPlayer()) return false;
                var f = FactionMembership.FactionOf(player);
                if (f == null || f.FriendlyMonsters.Count == 0) return false;
                int monsterHash = monster.name.Replace("(Clone)", "").GetStableHashCode();
                return f.FriendlyMonsters.Contains(monsterHash);
            }
        }

        // ── same-faction PvP damage block ───────────────────────────────────────
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Character_RPC_Damage_FactionPvp
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                try
                {
                    if (__instance == null || hit == null || !__instance.IsPlayer()) return;
                    var attacker = hit.GetAttacker();
                    if (attacker == null || !attacker.IsPlayer() || attacker == __instance) return;
                    if (!FactionMembership.SameFaction(__instance, attacker)) return;

                    var f = FactionMembership.FactionOf(__instance);
                    if (f != null && !f.CanDamageSameFaction) hit.m_damage.Modify(0f);
                }
                catch { }
            }
        }
    }
}
