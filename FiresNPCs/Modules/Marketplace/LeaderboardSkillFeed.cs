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

        /// <summary>Vanilla has ~20 skills; modded skill packs add some. Larger batches are refused.</summary>
        private const int MaxSkillsPerBatch = 64;
        private const int MaxSkillLevel = 100;
        private const int MaxSkillNameLength = 64;

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
                ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcName), RpcName, pkg);
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
                // The row is the routed sender's, never the packet's name.
                if (!global::FiresRPGmaker.Managers.RpcSender.IsServerSelf(sender))
                {
                    string senderName = global::FiresRPGmaker.Managers.RpcSender.SenderName(sender);
                    if (string.IsNullOrEmpty(senderName)) { global::FiresRPGmaker.Managers.RpcSender.Refuse(RpcName, sender, "unknown sender name"); return; }
                    // A mismatched name is dropped, never rewritten.
                    if (!string.Equals(senderName.Trim(), playerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresRPGmaker] {RpcName}: dropped skills for '{playerName}' sent by peer {sender} ('{senderName}')");
                        return;
                    }
                }
                string ownerKey = LeaderboardServerFeed.OwnerKey(playerName);
                if (string.IsNullOrEmpty(ownerKey)) return;

                int n = pkg.ReadInt();
                if (n < 0 || n > MaxSkillsPerBatch) { global::FiresRPGmaker.Managers.RpcSender.Refuse(RpcName, sender, $"skill count {n} outside 0..{MaxSkillsPerBatch}"); return; }
                var levels = new List<KeyValuePair<string, int>>(n);
                for (int i = 0; i < n; i++)
                {
                    string skill = pkg.ReadString();
                    int level = Mathf.Clamp(pkg.ReadInt(), 0, MaxSkillLevel);
                    if (!string.IsNullOrEmpty(skill) && skill.Length <= MaxSkillNameLength)
                        levels.Add(new KeyValuePair<string, int>(skill, level));
                }
                // One vault write for the batch (Core 0.2.115+), not one per skill.
                var swProbe = System.Diagnostics.Stopwatch.StartNew();   // 0.0.796 perf probe (lead 10-29)
                LeaderboardBridge.SetSkillLevels(ownerKey, playerName, levels);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[PerfProbe] RPC_RecordSkill: Core LeaderboardBridge.SetSkillLevels ({levels.Count} skills) {swProbe.Elapsed.TotalMilliseconds:0.00} ms");
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_RecordSkill failed: {ex.Message}"); }
        }
    }
}
