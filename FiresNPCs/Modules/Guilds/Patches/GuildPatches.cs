using HarmonyLib;
using TMPro;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;

namespace FiresRPGmaker.Modules.Guilds.Patches
{
    /// <summary>
    /// Harmony patches that integrate the guild system into vanilla systems:
    ///
    ///   1. Friendly fire — guild members cannot deal damage to each other
    ///      (configurable; default enabled).
    ///
    ///   2. Player nameplates — shows "[GuildTag]" above player names on the
    ///      EnemyHud billboard, coloured with the guild colour.
    ///
    ///   3. HUD — guild tag shown in the player badge under health bar.
    /// </summary>
    public static class GuildPatches
    {
        public static bool FriendlyFireEnabled = false; // false = guild members cannot hurt each other

        // ── 1. Friendly fire ─────────────────────────────────────────────────

        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class Character_RPC_Damage_Patch
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (FriendlyFireEnabled) return true;
                if (hit == null || hit.m_attacker == ZDOID.None) return true;

                // Resolve attacker character.
                var attackerGO = ZNetScene.instance?.FindInstance(hit.m_attacker);
                if (attackerGO == null) return true;
                var attackerPlayer = attackerGO.GetComponent<Player>();
                if (attackerPlayer == null) return true;

                var targetPlayer = __instance as Player;
                if (targetPlayer == null) return true;

                // Skip if attacking self.
                if (attackerPlayer == targetPlayer) return true;

                long attackerId = attackerPlayer.GetPlayerID();
                long targetId   = targetPlayer.GetPlayerID();

                if (GuildAPI.AreGuildmates(attackerId, targetId))
                    return false; // suppress damage

                return true;
            }
        }

        // ── 2. Player nameplates ──────────────────────────────────────────────

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static class EnemyHud_UpdateHuds_Patch
        {
            private static void Postfix(EnemyHud __instance)
            {
                if (__instance == null) return;

                var local = Player.m_localPlayer;
                if (local == null) return;

                var ownGuild = GuildAPI.GetPlayerGuild(local.GetPlayerID());

                // Iterate all tracked huds looking for players.
                try
                {
                    foreach (var kvp in __instance.m_huds)
                    {
                        if (kvp.Key == null) continue;
                        var targetPlayer = kvp.Key.GetComponent<Player>();
                        if (targetPlayer == null || targetPlayer == local) continue;

                        long targetId = targetPlayer.GetPlayerID();

                        // Find the name text element.
                        var hudData = kvp.Value;
                        if (hudData?.m_name == null) continue;

                        var targetGuild = GuildAPI.GetPlayerGuild(targetId);
                        if (targetGuild == null) continue;

                        // Prepend or append guild tag without overwriting the player name.
                        string baseName = targetPlayer.GetPlayerName();
                        string colorHex = ColorUtility.ToHtmlStringRGB(targetGuild.GuildColor);
                        string tag = $"<color=#{colorHex}>[{Abbreviate(targetGuild.Name)}]</color> ";

                        // Only update when the tag would change to avoid thrashing.
                        string desired = tag + baseName;
                        if (hudData.m_name.text != desired)
                            hudData.m_name.text = desired;
                    }
                }
                catch { }
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static string Abbreviate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // Up to 4 characters: try first letters of each word, otherwise truncate.
            var words = name.Split(' ');
            if (words.Length >= 2)
            {
                string abbr = "";
                foreach (var w in words) if (!string.IsNullOrEmpty(w)) abbr += w[0];
                return abbr.ToUpper().Substring(0, Mathf.Min(abbr.Length, 4));
            }
            return name.Substring(0, Mathf.Min(name.Length, 4)).ToUpper();
        }
    }
}
