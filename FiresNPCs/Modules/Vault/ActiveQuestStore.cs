using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.Modules.Vault
{
    /// <summary>
    /// kg-parity active-quest store. A player's ACTIVE quests live CLIENT-LOCAL in <c>Player.m_customData</c>,
    /// ONE key per quest (<c>FiresQuest.&lt;id&gt; = "p0,p1,…;startedUnix"</c>). Accepting is a purely local write —
    /// no server round-trip — so it applies instantly (no first-click race) and, because every quest is its own
    /// key, a stale copy can never wipe siblings (no whole-list blob to clobber). This mirrors kg's
    /// <c>AcceptedQuests</c> dictionary + per-UID <c>m_customData</c> persistence.
    ///
    /// The old server-authoritative vault blob (<see cref="VaultOfKnowledge"/> ActiveQuests) is migrated into
    /// per-quest keys once on first Load, then never read for active quests again.
    /// </summary>
    public static class ActiveQuestStore
    {
        public const string KeyPrefix = "FiresQuest.";
        // Completed quests must ALSO live client-local: SavePlayerData no-ops on clients, so a turn-in's
        // AddCompletedQuest would vanish on the next GetPlayerData and break QuestFinished chain gates
        // (SnorriOne → SnorriOne2). One key per completed quest: FiresQuestDone.<id> = "times;completedUnix".
        public const string DoneKeyPrefix = "FiresQuestDone.";

        private static readonly Dictionary<string, ActiveQuestData> _active =
            new Dictionary<string, ActiveQuestData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, KeyValuePair<int, long>> _completed =
            new Dictionary<string, KeyValuePair<int, long>>(StringComparer.OrdinalIgnoreCase);
        private static long _loadedFor = long.MinValue;

        public static string Norm(string id) => (id ?? string.Empty).Trim();

        public static IReadOnlyCollection<ActiveQuestData> Active => _active.Values;
        public static int Count => _active.Count;
        public static bool HasQuest(string questId) => _active.ContainsKey(Norm(questId));
        public static ActiveQuestData Get(string questId) => _active.TryGetValue(Norm(questId), out var q) ? q : null;
        public static bool IsCompleted(string questId) => _completed.ContainsKey(Norm(questId));

        /// <summary>Record a quest completion for the local player — client-local, persisted immediately to
        /// m_customData (SavePlayerData no-ops on clients). This is what makes QuestFinished chain gates work.</summary>
        public static void MarkCompleted(string questId)
        {
            questId = Norm(questId);
            if (string.IsNullOrEmpty(questId)) return;
            long unix = 0; try { unix = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds(); } catch { }
            int times = (_completed.TryGetValue(questId, out var prev) ? prev.Key : 0) + 1;
            _completed[questId] = new KeyValuePair<int, long>(times, unix);
            var p = Player.m_localPlayer;
            if (p?.m_customData != null)
                p.m_customData[DoneKeyPrefix + questId] = times.ToString(CultureInfo.InvariantCulture) + ";" + unix.ToString(CultureInfo.InvariantCulture);
            Debug.Log($"[QuestPipe] MarkCompleted {questId} times={times} totalDone={_completed.Count}");
        }

        /// <summary>Overlay the client-local completions onto a freshly-read vault object (mirrors the ActiveQuests
        /// override in GetPlayerData) so every reader — CheckRequirements, HasCompletedQuest — sees them.</summary>
        public static void ApplyCompletedTo(PlayerVaultData d)
        {
            if (d == null || _completed.Count == 0) return;
            if (d.CompletedQuests == null) d.CompletedQuests = new Dictionary<string, CompletedQuestEntry>();
            foreach (var kv in _completed)
            {
                DateTime at = DateTime.UtcNow;
                try { at = DateTimeOffset.FromUnixTimeSeconds(kv.Value.Value).UtcDateTime; } catch { }
                d.CompletedQuests[kv.Key] = new CompletedQuestEntry(kv.Value.Key, at);
            }
        }

        /// <summary>True once Load has run for the given player this session (so readers know the store is warm).</summary>
        public static bool IsLoadedFor(long playerId) => _loadedFor == playerId;

        /// <summary>Rehydrate the active set from the local player's per-quest m_customData keys. Call on spawn.
        /// Also one-time migrates any quests still living in the old vault blob into per-quest keys.</summary>
        public static void Load(Player player)
        {
            _active.Clear();
            _completed.Clear();
            _loadedFor = long.MinValue; // stay "not loaded" through migration so GetPlayerData still returns the vault blob
            if (player == null) return;
            long pid = player.GetPlayerID();

            var cd = player.m_customData;
            if (cd != null)
            {
                foreach (var kv in cd.Where(k => k.Key != null
                                                 && k.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)
                                                 && !k.Key.StartsWith(DoneKeyPrefix, StringComparison.Ordinal)).ToList())
                {
                    string id = kv.Key.Substring(KeyPrefix.Length);
                    var aq = Parse(id, kv.Value);
                    if (aq != null) _active[id] = aq;
                }
                foreach (var kv in cd.Where(k => k.Key != null && k.Key.StartsWith(DoneKeyPrefix, StringComparison.Ordinal)).ToList())
                {
                    string id = kv.Key.Substring(DoneKeyPrefix.Length);
                    int times = 1; long unix = 0;
                    var semi = (kv.Value ?? string.Empty).Split(';');
                    if (!int.TryParse(semi[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out times) || times < 1) times = 1;
                    if (semi.Length > 1) long.TryParse(semi[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out unix);
                    _completed[id] = new KeyValuePair<int, long>(times, unix);
                }
            }

            MigrateFromVaultBlob(player); // reads the real vault blob (routing still off), copies leftovers into per-quest keys
            _loadedFor = pid;             // NOW authoritative — GetPlayerData routes active quests through the store
            Debug.Log($"[QuestPipe] Load player={pid} active={_active.Count} done={_completed.Count} ids=[{string.Join(",", _active.Keys)}]");
        }

        // One-time: pull any ActiveQuests still in the server vault blob into per-quest keys, so existing players
        // don't lose in-flight quests when we switch storage models.
        private static void MigrateFromVaultBlob(Player player)
        {
            try
            {
                var data = VaultOfKnowledge.Instance?.GetPlayerData(player.GetPlayerID());
                if (data == null) return;
                if (data.ActiveQuests != null)
                {
                    int migrated = 0;
                    foreach (var aq in data.ActiveQuests)
                    {
                        string id = Norm(aq?.QuestId);
                        if (string.IsNullOrEmpty(id) || _active.ContainsKey(id)) continue;
                        _active[id] = aq;
                        Write(player, id, aq);
                        migrated++;
                    }
                    if (migrated > 0) Debug.Log($"[QuestPipe] migrated {migrated} quest(s) from vault blob → per-quest keys");
                }
                // Completed quests likewise move client-local, so existing completions still gate chains after the switch.
                if (data.CompletedQuests != null && player.m_customData != null)
                {
                    int migratedC = 0;
                    foreach (var kv in data.CompletedQuests)
                    {
                        string id = Norm(kv.Key);
                        if (string.IsNullOrEmpty(id) || _completed.ContainsKey(id)) continue;
                        int times = kv.Value?.TimesCompleted ?? 1; if (times < 1) times = 1;
                        long unix = 0;
                        try { unix = new DateTimeOffset(DateTime.SpecifyKind(kv.Value?.LastCompletedAt ?? DateTime.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds(); } catch { }
                        _completed[id] = new KeyValuePair<int, long>(times, unix);
                        player.m_customData[DoneKeyPrefix + id] = times.ToString(CultureInfo.InvariantCulture) + ";" + unix.ToString(CultureInfo.InvariantCulture);
                        migratedC++;
                    }
                    if (migratedC > 0) Debug.Log($"[QuestPipe] migrated {migratedC} completed quest(s) from vault blob → done keys");
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[QuestPipe] blob migrate failed: {ex.Message}"); }
        }

        /// <summary>Accept a quest for the local player — local, immediate, idempotent. Returns false if already active.</summary>
        public static bool Accept(string questId, QuestDefinition def)
        {
            questId = Norm(questId);
            if (string.IsNullOrEmpty(questId)) return false;
            var p = Player.m_localPlayer;
            if (p == null) { Debug.LogWarning("[QuestPipe] Accept: no local player"); return false; }
            if (_active.ContainsKey(questId)) { Debug.Log($"[QuestPipe] Accept skip (already active) {questId}"); return false; }

            int targets = def?.QuestTargets?.Count ?? 0;
            var aq = new ActiveQuestData { QuestId = questId, Progress = 0, StartedAt = DateTime.UtcNow };
            aq.EnsureTargetProgress(targets);
            _active[questId] = aq;
            Write(p, questId, aq);
            Debug.Log($"[QuestPipe] Accept OK {questId} targets={targets} total={_active.Count}");
            return true;
        }

        /// <summary>Abandon / drop a quest from the active set (removes its m_customData key).</summary>
        public static bool Abandon(string questId)
        {
            questId = Norm(questId);
            bool had = _active.Remove(questId);
            var p = Player.m_localPlayer;
            if (p?.m_customData != null) p.m_customData.Remove(KeyPrefix + questId);
            // 0.0.532: also out of the old vault, or Load's migration brings a turned-in quest back after a relog.
            VaultOfKnowledge.DropActiveQuestFromVault(p, questId);
            Debug.Log($"[QuestPipe] {(had ? "Abandon OK" : "Abandon skip (not active)")} {questId} total={_active.Count}");
            return had;
        }

        /// <summary>Re-serialize every active quest's key (call after in-place progress mutations so they persist).</summary>
        public static void PersistAll()
        {
            var p = Player.m_localPlayer;
            if (p?.m_customData == null || _active.Count == 0) return;
            foreach (var kv in _active) p.m_customData[KeyPrefix + kv.Key] = Serialize(kv.Value);
        }

        /// <summary>Persist a quest's per-objective progress and re-save its single key.</summary>
        public static void SetTargetProgress(string questId, int index, int value, int targetCount)
        {
            var aq = Get(questId);
            if (aq == null) return;
            aq.SetTargetProgress(index, value, targetCount);
            var p = Player.m_localPlayer;
            if (p != null) Write(p, questId, aq);
        }

        private static void Write(Player p, string questId, ActiveQuestData aq)
        {
            if (p?.m_customData == null) return;
            p.m_customData[KeyPrefix + questId] = Serialize(aq);
        }

        private static string Serialize(ActiveQuestData aq)
        {
            string prog = (aq.TargetProgress != null && aq.TargetProgress.Count > 0)
                ? string.Join(",", aq.TargetProgress)
                : aq.Progress.ToString(CultureInfo.InvariantCulture);
            long started = 0;
            try { started = new DateTimeOffset(aq.StartedAt == default ? DateTime.UtcNow : DateTime.SpecifyKind(aq.StartedAt, DateTimeKind.Utc)).ToUnixTimeSeconds(); } catch { }
            return prog + ";" + started.ToString(CultureInfo.InvariantCulture);
        }

        private static ActiveQuestData Parse(string questId, string raw)
        {
            var aq = new ActiveQuestData { QuestId = questId, StartedAt = DateTime.UtcNow };
            try
            {
                if (string.IsNullOrEmpty(raw)) return aq;
                var semi = raw.Split(';');
                if (semi.Length > 1 && long.TryParse(semi[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
                {
                    try { aq.StartedAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; } catch { }
                }
                string progPart = semi[0];
                if (!string.IsNullOrEmpty(progPart))
                {
                    var parts = progPart.Split(',');
                    var list = new List<int>(parts.Length);
                    int sum = 0;
                    foreach (var s in parts)
                    {
                        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v);
                        if (v < 0) v = 0;
                        list.Add(v); sum += v;
                    }
                    aq.TargetProgress = list;
                    aq.Progress = sum;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[QuestPipe] parse fail {questId}: {ex.Message}"); }
            return aq;
        }
    }
}
