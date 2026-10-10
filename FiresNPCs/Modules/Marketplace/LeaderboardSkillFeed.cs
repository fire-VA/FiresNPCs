using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>One skill level entry — a <see cref="Skills.SkillType"/> name and its integer level (0..100).</summary>
    public struct LeaderboardSkillEntry
    {
        public string Skill;   // Skills.SkillType name, e.g. "Swords"
        public int Level;      // 0..100

        public LeaderboardSkillEntry(string skill, int level) { Skill = skill; Level = level; }
    }

    /// <summary>
    /// Server-authoritative SKILLS write path for the leaderboard player-details panel. The owner client
    /// snapshots its live skill list and sends a debounced batch via the <c>Fires_Leaderboard_RecordSkill</c>
    /// routed RPC; the server upserts each skill level (latest-wins) into Core's authoritative vault via
    /// <see cref="LeaderboardBridge.SetSkillLevel"/>, keyed with <see cref="LeaderboardServerFeed.OwnerKey"/>
    /// (the SAME key the board uses). Clone of <see cref="LeaderboardServerFeed"/>'s Send/RPC shape.
    /// Read-back: the server attaches each player's top skills onto their <see cref="LeaderboardRow"/> at
    /// snapshot time (see <see cref="LeaderboardBoard"/>); the local panel prefers the live skill list.
    /// </summary>
    public static class LeaderboardSkillFeed
    {
        public const string RpcName = "Fires_Leaderboard_RecordSkill";

        public static void RegisterRpc(ZRoutedRpc rpc)
        {
            rpc?.Register(RpcName, new Action<long, ZPackage>(RPC_RecordSkill));
        }

        // ── Client → server ─────────────────────────────────────────────────────────────────────
        public static void Send(string playerName, IReadOnlyList<LeaderboardSkillEntry> skills)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(playerName) || skills == null || skills.Count == 0) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(playerName);
                pkg.Write(skills.Count);
                for (int i = 0; i < skills.Count; i++)
                {
                    pkg.Write(skills[i].Skill ?? string.Empty);
                    pkg.Write(skills[i].Level);
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcName, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Skill Send failed: {ex.Message}"); }
        }

        // ── Server: upsert each skill (latest-wins) into Core's authoritative repo ───────────────
        private static void RPC_RecordSkill(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string playerName = pkg.ReadString();
                if (string.IsNullOrEmpty(playerName)) return;
                string ownerKey = LeaderboardServerFeed.OwnerKey(playerName);
                if (string.IsNullOrEmpty(ownerKey)) return;

                int n = pkg.ReadInt();
                var levels = new List<KeyValuePair<string, int>>(n);
                for (int i = 0; i < n; i++)
                {
                    string skill = pkg.ReadString();
                    int level = pkg.ReadInt();
                    if (!string.IsNullOrEmpty(skill))
                        levels.Add(new KeyValuePair<string, int>(skill, level));
                }
                // One vault write for the batch (Core 0.2.115+), not one per skill.
                LeaderboardBridge.SetSkillLevels(ownerKey, playerName, levels);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_RecordSkill failed: {ex.Message}"); }
        }
    }
}
