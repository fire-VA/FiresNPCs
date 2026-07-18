using System;
using System.Collections.Generic;
using UnityEngine;
// NB: do NOT `using FiresCore.Storage` — this namespace already defines a different LeaderboardEntry
// (LeaderboardProfileManager.cs), which would shadow the Core vault row. The Core type is fully qualified below.

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Shared achievement-completion logic. The completion predicate is identical for everyone —
    /// <c>value &gt;= def.MinAmount</c>; only the <i>stat source</i> differs.
    ///
    /// STEP 1 (this file): the LOCAL client-side path only. It reads the live
    /// <see cref="LeaderboardTracker"/> for the local player (the exact pattern used in
    /// <c>BuildCanvasAchievementsView</c>) — fresh, no server involvement.
    ///
    /// STEP 2 SEAM (not implemented yet): a server-side
    /// <c>int ComputeProgress(LeaderboardEntry e, AchievementDef def)</c> that maps each
    /// <c>def.TriggerType</c> to the authoritative value on the synced <c>LeaderboardEntry</c>, so the
    /// remote count/list can be computed server-side against the SAME defs. When that lands, both paths
    /// must funnel the trigger→value mapping through one place so they cannot drift. See the design doc's
    /// "Compute: LOCAL vs REMOTE" table for the mapping.
    /// </summary>
    public static class AchievementCompletion
    {
        // ---- LOCAL (client-side, live) ------------------------------------------------------------------

        /// <summary>
        /// The completed achievements for the LOCAL player under <paramref name="profile"/>, evaluated
        /// live against <see cref="LeaderboardTracker"/> (mirrors BuildCanvasAchievementsView's predicate).
        /// </summary>
        public static List<AchievementDef> CompletedForLocalPlayer(string profile)
        {
            var result = new List<AchievementDef>();
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
            var defs = AchievementProfileManager.GetAchievements(profile);
            if (defs == null) return result;
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def == null) continue;
                int progress = LeaderboardTracker.GetProgress(localName, def.TriggerType, def.Prefab);
                if (progress >= def.MinAmount) result.Add(def);
            }
            return result;
        }

        /// <summary>Count of completed achievements for the LOCAL player under <paramref name="profile"/>.</summary>
        public static int CountForLocalPlayer(string profile)
        {
            int count = 0;
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
            var defs = AchievementProfileManager.GetAchievements(profile);
            if (defs == null) return 0;
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def == null) continue;
                int progress = LeaderboardTracker.GetProgress(localName, def.TriggerType, def.Prefab);
                if (progress >= def.MinAmount) count++;
            }
            return count;
        }

        // ---- STEP 2: server-side compute from a stored LeaderboardEntry -------------------------------------
        // Mirrors LeaderboardTracker.GetProgress's trigger→value switch (the LOCAL path) so the two cannot drift.
        // The server has no LeaderboardTracker; it reads the vault row's dicts/scalars directly. Lookups are
        // case-insensitive belt-and-suspenders — the MonstersKilled write key was lowercased historically (now
        // raw prefab name, see QuestProgressPatches), and CI matching tolerates either.

        /// <summary>Authoritative progress for a def against a stored vault row.</summary>
        public static int ComputeProgress(FiresCore.Storage.LeaderboardEntry e, AchievementDef def)
        {
            if (e == null || def == null) return 0;
            string prefab = def.Prefab;
            switch (def.TriggerType)
            {
                case "MonstersKilled":
                    return string.IsNullOrEmpty(prefab)
                        ? SumExcept(e.KilledCreatures, "Player")
                        : LookupCI(e.KilledCreatures, prefab);
                case "ItemsCrafted":
                    return string.IsNullOrEmpty(prefab) ? SumValues(e.ItemsCrafted) : LookupCI(e.ItemsCrafted, prefab);
                case "StructuresBuilt":
                    return string.IsNullOrEmpty(prefab) ? SumValues(e.BuiltStructures) : LookupCI(e.BuiltStructures, prefab);
                case "SkillLevel":    return string.IsNullOrEmpty(prefab) ? 0 : LookupCI(e.Skills, prefab);
                case "Terraformed":   return e.Terraformed;
                case "Cultivated":    return e.Cultivated;
                case "Died":          return e.DeathAmount;
                case "KilledBy":      return string.IsNullOrEmpty(prefab) ? 0 : LookupCI(e.KilledBy, prefab);
                case "Explored":      return Mathf.RoundToInt(e.MapExplored * 100f);
                case "Harvested":     return string.IsNullOrEmpty(prefab) ? 0 : LookupCI(e.Harvested, prefab);
                case "PlayersKilled": return e.KilledPlayers;
                default: return 0;
            }
        }

        /// <summary>The completed defs (progress &gt;= MinAmount) for a stored vault row. Shared by the inline count
        /// and the Step-3 remote-list RPC so both evaluate identically.</summary>
        public static List<AchievementDef> EvaluateCompleted(FiresCore.Storage.LeaderboardEntry e, IReadOnlyList<AchievementDef> defs)
        {
            var result = new List<AchievementDef>();
            if (defs == null) return result;
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def != null && ComputeProgress(e, def) >= def.MinAmount) result.Add(def);
            }
            return result;
        }

        /// <summary>Count of completed achievements for a stored vault row under a profile's defs.</summary>
        public static int CountCompleted(FiresCore.Storage.LeaderboardEntry e, IReadOnlyList<AchievementDef> defs)
        {
            int n = 0;
            if (defs == null) return 0;
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def != null && ComputeProgress(e, def) >= def.MinAmount) n++;
            }
            return n;
        }

        private static int LookupCI(Dictionary<string, int> d, string key)
        {
            if (d == null || string.IsNullOrEmpty(key)) return 0;
            if (d.TryGetValue(key, out int v)) return v;
            foreach (var kv in d) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return 0;
        }

        private static int SumValues(Dictionary<string, int> d)
        {
            if (d == null) return 0;
            int s = 0;
            foreach (var v in d.Values) s += v;
            return s;
        }

        private static int SumExcept(Dictionary<string, int> d, string skipKey)
        {
            if (d == null) return 0;
            int s = 0;
            foreach (var kv in d) if (!string.Equals(kv.Key, skipKey, StringComparison.OrdinalIgnoreCase)) s += kv.Value;
            return s;
        }
    }
}
