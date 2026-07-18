using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresCore.Bridge;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>One ranked row of the cross-player leaderboard, flattened from a LeaderboardEntry for display.</summary>
    public sealed class LeaderboardRow
    {
        public string PlayerName;
        public int Monsters;
        public int PlayersKilled;
        public int Structures;
        public int Crafted;
        public int Deaths;
        public float MapExplored;   // 0..1
        public int Score;
        // Server-computed count of completed achievements under the active profile (Step 2). v4 snapshot field.
        public int AchievementCount;

        // Favorite weapon — the highest-count weapon_kills entry, resolved server-side in BuildRows. The id is
        // the stored weapon prefab name (server stores name==id); the pretty name is re-derived client-side.
        public string PreferredWeaponId = string.Empty;
        public string PreferredWeaponName = string.Empty;

        // Top skills (level>0, descending), up to TopSkillsCount. Used by the player-details Skills panel for
        // remote players; the local player prefers their live skill list for freshness.
        public List<LeaderboardSkillEntry> TopSkills = new List<LeaderboardSkillEntry>();

        // Achieved Adventure-milestone ids (server-computed via LeaderboardMilestones). Drives the Progression
        // timeline. v5 snapshot field.
        public List<string> Milestones = new List<string>();
    }

    /// <summary>
    /// Board READ path. Clients can't open the server's per-world LiteDB vault, so the client asks for a
    /// snapshot (<c>Fires_Leaderboard_Request</c>) and the server ships back the ranked rows
    /// (<c>Fires_Leaderboard_Snapshot</c>, targeted to that peer). The client caches them in <see cref="Rows"/>
    /// and raises <see cref="LeaderboardBridge"/>.EntryPublished so an open panel refreshes. Pairs with
    /// <see cref="LeaderboardServerFeed"/> (the write path).
    /// </summary>
    public static class LeaderboardBoard
    {
        public const string RequestRpc = "Fires_Leaderboard_Request";
        public const string SnapshotRpc = "Fires_Leaderboard_Snapshot";
        public const string RollSeasonRpc = "Fires_Leaderboard_RollSeason";
        public const string WipeRpc = "Fires_Leaderboard_Wipe";
        // Sentinel "season" for the all-time aggregate view. Sits at the front of the season selector.
        public const int OverallSeason = -1;
        private const int MaxRows = 100;
        private const int TopSkillsCount = 4;

        // Snapshot payload version. v1 = the original 8 scalar fields (no leading version int). v2 = a leading
        // version int, then the 8 scalars, then PreferredWeaponId/Name and the TopSkills list per row. The reader
        // peeks the first int: a value < the row count it would imply is treated as a version tag (v2+), otherwise
        // the package is read as legacy v1 (the first int IS the row count). Writing the version keeps old clients
        // fail-safe — a v1 reader would misread, but every shipped client reads via the version-aware path below.
        // v3 adds a season header (current season, the season these rows represent, and the list of available
        // seasons) ahead of the row count, so the client can populate the season selector. Reader stays
        // version-aware. v4 adds one int per row (AchievementCount) right after Score. Client and server always run
        // the same plugin version, so the newest version is always taken in practice.
        private const int SnapshotPayloadVersion = 5;

        /// <summary>Client-side cache of the most recent snapshot (already ranked by score, highest first).</summary>
        public static List<LeaderboardRow> Rows { get; private set; } = new List<LeaderboardRow>();

        // Season state, refreshed from every snapshot. ViewedSeason = the season the current Rows represent;
        // CurrentSeason = the live season; AvailableSeasons = every season the server can serve (1..current).
        public static int CurrentSeason { get; private set; } = 1;
        public static int ViewedSeason { get; private set; } = 1;
        public static List<int> AvailableSeasons { get; private set; } = new List<int> { OverallSeason, 1 };
        private static int _requestSeason; // 0 = "give me the current/live season"

        public static void RegisterRpcs(ZRoutedRpc rpc)
        {
            if (rpc == null) return;
            rpc.Register(RequestRpc, new Action<long, ZPackage>(RPC_Request));
            rpc.Register(SnapshotRpc, new Action<long, ZPackage>(RPC_Snapshot));
            rpc.Register(RollSeasonRpc, new Action<long, ZPackage>(RPC_RollSeason));
            rpc.Register(WipeRpc, new Action<long, ZPackage>(RPC_Wipe));
        }

        /// <summary>Admin action: roll the current season (archive the live board, wipe it, advance the season).
        /// Runs the roll directly on the server; from a client it sends an admin-gated RPC. The next snapshot
        /// (auto-refresh) reflects the wiped board and the newly available archived season.</summary>
        public static void RequestRollSeason()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                LeaderboardBridge.RollSeason();
                return;
            }
            if (ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(0L, RollSeasonRpc, new ZPackage()); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard RequestRollSeason failed: {ex.Message}"); }
        }

        // Server: an admin asked to roll the season. Gate on admin, then archive + wipe + advance.
        private static void RPC_RollSeason(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!AdminSyncing.IsAdmin(sender))
            {
                Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} tried to roll a leaderboard season — rejected.");
                return;
            }
            int archived = LeaderboardBridge.RollSeason();
            Debug.Log($"[FiresRPGmaker] Leaderboard season rolled by admin {sender} (archived season {archived}).");
        }

        /// <summary>Admin action: HARD-WIPE the entire leaderboard — scores, skills, deaths, appearance, the season
        /// archive, and the season counter — for a fresh start at season 1. Runs directly on the server; from a
        /// client it sends an admin-gated RPC. Afterwards every connected client is pushed an empty snapshot so open
        /// boards clear without a relog. Achievement DEFINITIONS are kept (progress derives from the board).</summary>
        public static void RequestWipe()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ServerWipe(); return; }
            if (ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(0L, WipeRpc, new ZPackage()); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard RequestWipe failed: {ex.Message}"); }
        }

        // Server: an admin asked to wipe everything. Gate on admin, then drop all data + reset + push to clients.
        private static void RPC_Wipe(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!AdminSyncing.IsAdmin(sender))
            {
                Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} tried to wipe the leaderboard — rejected.");
                return;
            }
            ServerWipe();
            Debug.Log($"[FiresRPGmaker] Leaderboard fully wiped by admin {sender} (fresh season 1).");
        }

        // Server: drop all leaderboard/appearance/season data, reset local season state, refresh the host's own open
        // panel, and push an empty board to every connected client.
        private static void ServerWipe()
        {
            LeaderboardBridge.WipeAllData();
            Rows = new List<LeaderboardRow>();
            CurrentSeason = 1;
            ViewedSeason = 1;
            AvailableSeasons = new List<int> { OverallSeason, 1 };
            _requestSeason = 0;
            LeaderboardBridge.RaiseEntryPublished(null);
            BroadcastBoardInvalidated();
        }

        // Push a fresh (post-wipe, empty) snapshot to every connected client so their cached board clears live.
        private static void BroadcastBoardInvalidated()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null) return;
            foreach (var peer in ZNet.instance.GetConnectedPeers())
            {
                if (peer == null) continue;
                SendSnapshot(peer.m_uid, 0);
            }
        }

        /// <summary>Client: ask the server for the board (call when opening the panel / on refresh). Re-requests
        /// whichever season is currently being viewed.</summary>
        public static void RequestBoard() => RequestBoard(_requestSeason);

        /// <summary>Client: ask for a specific season (0 = current/live; the server resolves it).</summary>
        public static void RequestBoard(int season)
        {
            if (ZRoutedRpc.instance == null) return;
            _requestSeason = season;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(season);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RequestRpc, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard RequestBoard failed: {ex.Message}"); }
        }

        // Server: build the ranked snapshot and send it back to the requesting peer only.
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            int requested = 0;
            try { if (pkg != null && pkg.Size() > 0) requested = pkg.ReadInt(); } catch { requested = 0; }
            // 0 = "give me the live season"; negative = the all-time Overall aggregate; positive = that season.
            SendSnapshot(sender, requested);
        }

        // Build the ranked snapshot for a season (0 = live) and ship it to one peer. Shared by RPC_Request (reply to
        // a requester) and BroadcastBoardInvalidated (push to every peer after a wipe).
        private static void SendSnapshot(long targetPeer, int requested)
        {
            if (ZRoutedRpc.instance == null) return;
            try
            {
                int current = LeaderboardBridge.GetCurrentSeason();
                int viewed = requested == 0 ? current : requested;

                var seasons = LeaderboardBridge.GetArchivedSeasons();
                if (!seasons.Contains(current)) seasons.Add(current);
                seasons.Sort();
                seasons.Insert(0, OverallSeason); // Overall view at the front of the selector

                var rows = BuildRows(viewed);
                var outPkg = new ZPackage();
                // v3 layout: version tag, current season, viewed season, season list, then row count + each row.
                outPkg.Write(SnapshotPayloadVersion);
                outPkg.Write(current);
                outPkg.Write(viewed);
                outPkg.Write(seasons.Count);
                foreach (var s in seasons) outPkg.Write(s);
                outPkg.Write(rows.Count);
                foreach (var r in rows)
                {
                    outPkg.Write(r.PlayerName ?? string.Empty);
                    outPkg.Write(r.Monsters);
                    outPkg.Write(r.PlayersKilled);
                    outPkg.Write(r.Structures);
                    outPkg.Write(r.Crafted);
                    outPkg.Write(r.Deaths);
                    outPkg.Write(r.MapExplored);
                    outPkg.Write(r.Score);
                    outPkg.Write(r.AchievementCount); // v4
                    outPkg.Write(r.PreferredWeaponId ?? string.Empty);
                    outPkg.Write(r.PreferredWeaponName ?? string.Empty);
                    var skills = r.TopSkills ?? new List<LeaderboardSkillEntry>();
                    outPkg.Write(skills.Count);
                    for (int i = 0; i < skills.Count; i++)
                    {
                        outPkg.Write(skills[i].Skill ?? string.Empty);
                        outPkg.Write(skills[i].Level);
                    }
                    var ms = r.Milestones ?? new List<string>(); // v5 — achieved Adventure-milestone ids
                    outPkg.Write(ms.Count);
                    for (int i = 0; i < ms.Count; i++) outPkg.Write(ms[i] ?? string.Empty);
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer, SnapshotRpc, outPkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard SendSnapshot failed: {ex.Message}"); }
        }

        // Client: cache the snapshot and notify any open UI.
        private static void RPC_Snapshot(long sender, ZPackage pkg)
        {
            try
            {
                var rows = new List<LeaderboardRow>();
                // Version-aware read: the first int is the version tag (2 or 3), or — legacy v1 — the row count.
                int first = pkg.ReadInt();
                int version = (first >= 2 && first <= 5) ? first : 1;
                int n;
                if (version >= 3)
                {
                    CurrentSeason = pkg.ReadInt();
                    ViewedSeason = pkg.ReadInt();
                    int sc = pkg.ReadInt();
                    var seasons = new List<int>();
                    for (int s = 0; s < sc; s++) seasons.Add(pkg.ReadInt());
                    AvailableSeasons = seasons.Count > 0 ? seasons : new List<int> { CurrentSeason };
                    n = pkg.ReadInt();
                }
                else
                {
                    n = version >= 2 ? pkg.ReadInt() : first;
                }
                for (int i = 0; i < n; i++)
                {
                    var row = new LeaderboardRow
                    {
                        PlayerName = pkg.ReadString(),
                        Monsters = pkg.ReadInt(),
                        PlayersKilled = pkg.ReadInt(),
                        Structures = pkg.ReadInt(),
                        Crafted = pkg.ReadInt(),
                        Deaths = pkg.ReadInt(),
                        MapExplored = pkg.ReadSingle(),
                        Score = pkg.ReadInt(),
                    };
                    if (version >= 4) row.AchievementCount = pkg.ReadInt();
                    if (version >= 2)
                    {
                        row.PreferredWeaponId = pkg.ReadString();
                        row.PreferredWeaponName = pkg.ReadString();
                        int sk = pkg.ReadInt();
                        for (int s = 0; s < sk; s++)
                        {
                            string skill = pkg.ReadString();
                            int level = pkg.ReadInt();
                            row.TopSkills.Add(new LeaderboardSkillEntry(skill, level));
                        }
                    }
                    if (version >= 5)
                    {
                        int mc = pkg.ReadInt();
                        for (int m = 0; m < mc; m++) row.Milestones.Add(pkg.ReadString());
                    }
                    rows.Add(row);
                }
                Rows = rows;
                LeaderboardBridge.RaiseEntryPublished(null);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard RPC_Snapshot failed: {ex.Message}"); }
        }

        private static List<LeaderboardRow> BuildRows(int season)
        {
            var rows = new List<LeaderboardRow>();
            // Resolve the achievement profile + defs once for the whole snapshot (EnsureLoaded runs inside
            // GetAchievements; works headless since the loader is pure file I/O). The per-row count is computed
            // server-side from each stored entry so every player — not just the local one — gets a real count.
            string achProfile = AchievementProfileManager.ResolveActiveProfile();
            var achDefs = AchievementProfileManager.GetAchievements(achProfile);
            foreach (var entry in LeaderboardBridge.GetSeasonEntries(season).Values)
            {
                if (entry == null) continue;
                int playersKilled = entry.KilledPlayers;          // KilledCreatures["Player"]
                int monsters = 0;
                if (entry.KilledCreatures != null)
                    foreach (var kv in entry.KilledCreatures)
                        if (!string.Equals(kv.Key, "Player", StringComparison.OrdinalIgnoreCase)) monsters += kv.Value;
                int structures = SumValues(entry.BuiltStructures);
                int crafted = SumValues(entry.ItemsCrafted);
                int deaths = entry.DeathAmount;
                float map = Mathf.Clamp01(entry.MapExplored);
                // Placeholder score weighting (tunable later; TDV used a config-driven reputation formula).
                int score = monsters + playersKilled * 10 + structures + crafted + Mathf.RoundToInt(map * 500f) - deaths * 2;
                string preferredWeapon = ResolvePreferredWeapon(entry.WeaponKills);
                rows.Add(new LeaderboardRow
                {
                    PlayerName = entry.PlayerName,
                    Monsters = monsters,
                    PlayersKilled = playersKilled,
                    Structures = structures,
                    Crafted = crafted,
                    Deaths = deaths,
                    MapExplored = map,
                    Score = score,
                    AchievementCount = AchievementCompletion.CountCompleted(entry, achDefs),
                    // Server stores name==id; the client re-derives the pretty name from the prefab on display.
                    PreferredWeaponId = preferredWeapon,
                    PreferredWeaponName = preferredWeapon,
                    TopSkills = ResolveTopSkills(entry.Skills),
                    Milestones = LeaderboardMilestones.Achieved(entry),
                });
            }
            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (rows.Count > MaxRows) rows.RemoveRange(MaxRows, rows.Count - MaxRows);
            return rows;
        }

        private static int SumValues(Dictionary<string, int> d)
        {
            if (d == null) return 0;
            int s = 0;
            foreach (var v in d.Values) s += v;
            return s;
        }

        // Highest-count weapon prefab (tie-break by key for determinism). Empty when no weapon kills recorded.
        private static string ResolvePreferredWeapon(Dictionary<string, int> weaponKills)
        {
            if (weaponKills == null || weaponKills.Count == 0) return string.Empty;
            string best = string.Empty;
            int bestCount = -1;
            foreach (var kv in weaponKills)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (kv.Value > bestCount || (kv.Value == bestCount && string.CompareOrdinal(kv.Key, best) < 0))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }
            }
            return best;
        }

        // Up to TopSkillsCount skills with level>0, descending by level (tie-break by name).
        private static List<LeaderboardSkillEntry> ResolveTopSkills(Dictionary<string, int> skills)
        {
            var result = new List<LeaderboardSkillEntry>();
            if (skills == null || skills.Count == 0) return result;
            foreach (var kv in skills
                         .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Value > 0)
                         .OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(TopSkillsCount))
            {
                result.Add(new LeaderboardSkillEntry(kv.Key, kv.Value));
            }
            return result;
        }
    }
}
