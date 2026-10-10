using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Remote completed-achievement READ path — LAZY per-row fetch, cloning <see cref="LeaderboardAppearanceBoard"/>.
    /// The board snapshot ships only each player's achievement COUNT (Step 2); the full completed LIST for a remote
    /// player is fetched on demand when their details card opens. Opening a remote card issues one
    /// <c>Fires_Leaderboard_RequestAchievements(name, season)</c>; the server computes that player's completed defs
    /// against the SAME season's row the count was computed from (live / archived / the Overall aggregate) via
    /// <see cref="AchievementCompletion.EvaluateCompleted"/> and ships back just the def IDs
    /// (<c>Fires_Leaderboard_Achievements</c>, targeted to the requesting peer). Client and server load the SAME
    /// .cfg files, so IDs (assigned in parse order) resolve back to defs identically. The client caches the ID list
    /// by (name, season) — season-scoped so the list always matches the header count for the season being viewed —
    /// and raises <see cref="AchievementsReceived"/> so an open details card rebuilds. An empty list = "fetched,
    /// none" (a clean state, never a throw).
    /// </summary>
    public static class LeaderboardAchievementBoard
    {
        public const string RequestRpc = "Fires_Leaderboard_RequestAchievements";
        public const string ReplyRpc = "Fires_Leaderboard_Achievements";

        // Client-side cache keyed by (season, player name) → completed achievement def IDs. Season-scoped so that
        // switching seasons doesn't reuse one season's list under another season's count. Empty list = fetched-none.
        private static readonly Dictionary<string, List<int>> _cache =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private static string CacheKey(string name, int season) => season + "|" + name;

        /// <summary>Raised on the client with the (player name, season) once that player's list for that season arrives.</summary>
        public static event Action<string, int> AchievementsReceived;

        public static void RegisterRpcs(ZRoutedRpc rpc)
        {
            if (rpc == null) return;
            rpc.Register(RequestRpc, new Action<long, ZPackage>(RPC_Request));
            rpc.Register(ReplyRpc, new Action<long, ZPackage>(RPC_Reply));
        }

        /// <summary>Client: ask the server for a player's completed-achievement list for a specific season.</summary>
        public static void RequestAchievements(string name, int season)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(name)) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(name);
                pkg.Write(season);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RequestRpc, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RequestAchievements failed: {ex.Message}"); }
        }

        /// <summary>Client: read a cached completed-id list for (name, season). Returns false if not fetched yet.</summary>
        public static bool TryGet(string name, int season, out List<int> ids)
        {
            ids = null;
            if (string.IsNullOrEmpty(name)) return false;
            return _cache.TryGetValue(CacheKey(name, season), out ids);
        }

        // Server: compute the requested player's completed defs for the requested season and send their IDs back.
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string name = pkg.ReadString();
                if (string.IsNullOrEmpty(name)) return;
                int season = pkg.ReadInt();

                string ownerKey = LeaderboardServerFeed.OwnerKey(name);
                // Resolve the SAME row the snapshot count came from (live / archived / Overall aggregate), so the
                // list and the count can never diverge for the season being viewed.
                LeaderboardBridge.GetSeasonEntries(season).TryGetValue(ownerKey, out var entry);
                string profile = AchievementProfileManager.ResolveActiveProfile();
                var defs = AchievementProfileManager.GetAchievements(profile);
                var completed = AchievementCompletion.EvaluateCompleted(entry, defs); // null entry => empty list

                var outPkg = new ZPackage();
                outPkg.Write(name);              // echo so the client keys the cache even on empty
                outPkg.Write(season);
                outPkg.Write(entry != null);     // present
                outPkg.Write(completed.Count);
                for (int i = 0; i < completed.Count; i++) outPkg.Write(completed[i].Id);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReplyRpc, outPkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Achievements RPC_Request failed: {ex.Message}"); }
        }

        // Client: cache the id list (empty if none/unavailable) under (name, season) and notify any open details card.
        private static void RPC_Reply(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                string name = pkg.ReadString();
                if (string.IsNullOrEmpty(name)) return;
                int season = pkg.ReadInt();

                bool present = pkg.ReadBool();
                var ids = new List<int>();
                if (present)
                {
                    int count = pkg.ReadInt();
                    for (int i = 0; i < count; i++) ids.Add(pkg.ReadInt());
                }
                _cache[CacheKey(name, season)] = ids;
                try { AchievementsReceived?.Invoke(name, season); } catch { }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Achievements RPC_Reply failed: {ex.Message}"); }
        }
    }
}
