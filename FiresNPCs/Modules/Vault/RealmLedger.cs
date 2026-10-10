using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace FiresRPGmaker.Modules.Vault
{
    [Serializable]
    public class PlayerVaultData
    {
        // Player Info Section
        public long PlayerId { get; set; }
        public string PlayerName { get; set; } = "Unknown";
        public string PlatformId { get; set; } = "";
        public string Platform { get; set; } = "";
        public bool IsAdmin { get; set; } = false;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        // Quest Section
        public List<ActiveQuestData> ActiveQuests { get; set; } = new();
        public Dictionary<string, CompletedQuestEntry> CompletedQuests { get; set; } = new();
        public List<string> FailedQuests { get; set; } = new();

        // Stats Section
        public Dictionary<string, int> Stats { get; set; } = new();

        // Bank Section
        public List<object> BankItems { get; set; } = new();

        // Other Sections
        public List<object> Companions { get; set; } = new();
        public List<object> OwnedTerritories { get; set; } = new();

        public bool IsDirty { get; set; } = false;

        public PlayerVaultData() { }

        public PlayerVaultData(long playerId)
        {
            PlayerId = playerId;
            LastSeen = DateTime.UtcNow;
        }

        /// <summary>
        /// Serializes this vault data to a ZPackage.
        /// </summary>
        /// <param name="pkg">The package to write to.</param>
        /// <param name="excludeCompanions">If true, companions are not serialized. Used for m_customData sync to prevent cross-server companion pollution.</param>
        public void Serialize(ZPackage pkg, bool excludeCompanions = false)
        {
            pkg.Write(PlayerId);
            pkg.Write(PlayerName);
            pkg.Write(PlatformId ?? "");
            pkg.Write(Platform ?? "");
            pkg.Write(IsAdmin);
            pkg.Write(LastSeen.ToString("o"));
            pkg.Write(Stats.Count);
            foreach (var kv in Stats)
            {
                pkg.Write(kv.Key);
                pkg.Write(kv.Value);
            }
            pkg.Write(ActiveQuests.Count);
            foreach (var q in ActiveQuests)
            {
                pkg.Write(q.QuestId);
                pkg.Write(q.Progress);
                pkg.Write(q.StartedAt.ToString("o"));
            }
            // write distinct completed quest count and per-quest completion count
            pkg.Write(CompletedQuests.Count);
            foreach (var kv in CompletedQuests)
            {
                pkg.Write(kv.Key ?? string.Empty);
                pkg.Write(kv.Value.TimesCompleted);
                pkg.Write(kv.Value.LastCompletedAt.ToString("o"));
            }
            // failed quests
            pkg.Write(FailedQuests?.Count ?? 0);
            if (FailedQuests != null)
            {
                foreach (var fq in FailedQuests) pkg.Write(fq ?? string.Empty);
            }
            // Companions - serialize as JSON string to preserve complex structure
            // IMPORTANT: If excludeCompanions is true, write empty string to prevent cross-server companion pollution.
            // Companions are server-specific and should only be stored in the server's JSON files.
            try
            {
                string companionsJson = "";
                if (!excludeCompanions && Companions != null && Companions.Count > 0)
                {
                    companionsJson = JsonConvert.SerializeObject(Companions);
                }
                pkg.Write(companionsJson);
            }
            catch
            {
                pkg.Write("");
            }
            // BankItems - serialize as JSON string
            try
            {
                string bankJson = BankItems != null && BankItems.Count > 0 
                    ? JsonConvert.SerializeObject(BankItems) 
                    : "";
                pkg.Write(bankJson);
            }
            catch
            {
                pkg.Write("");
            }

            // Extended trailing block: per-objective quest progress. Appended at the very END so any reader
            // that predates it simply stops after BankItems and never desyncs; a new reader detects it via
            // GetPos() < Size(). Keyed by QuestId so it survives list reordering. (Wire-only path; disk +
            // m_customData are JSON, where TargetProgress serializes as a normal property.)
            pkg.Write(ActiveQuests.Count);
            foreach (var q in ActiveQuests)
            {
                pkg.Write(q.QuestId ?? string.Empty);
                var tp = q.TargetProgress;
                pkg.Write(tp?.Count ?? 0);
                if (tp != null) foreach (var v in tp) pkg.Write(v);
            }
        }

        public static PlayerVaultData Deserialize(ZPackage pkg)
        {
            var data = new PlayerVaultData();
            data.PlayerId = pkg.ReadLong();
            data.PlayerName = pkg.ReadString();
            data.PlatformId = pkg.ReadString();
            data.Platform = pkg.ReadString();
            data.IsAdmin = pkg.ReadBool();
            data.LastSeen = DateTime.Parse(pkg.ReadString());
            int statCount = pkg.ReadInt();
            for (int i = 0; i < statCount; i++)
            {
                data.Stats[pkg.ReadString()] = pkg.ReadInt();
            }
            int activeCount = pkg.ReadInt();
            for (int i = 0; i < activeCount; i++)
            {
                data.ActiveQuests.Add(new ActiveQuestData
                {
                    QuestId = pkg.ReadString(),
                    Progress = pkg.ReadInt(),
                    StartedAt = DateTime.Parse(pkg.ReadString())
                });
            }
            int completedCount = pkg.ReadInt();
            for (int i = 0; i < completedCount; i++)
            {
                var id = pkg.ReadString();
                int times = pkg.ReadInt();
                var completedAt = DateTime.Parse(pkg.ReadString());
                data.CompletedQuests[id] = new CompletedQuestEntry(times, completedAt);
            }
            // failed quests
            try
            {
                int failedCount = pkg.ReadInt();
                for (int i = 0; i < failedCount; i++)
                {
                    try { var fq = pkg.ReadString(); if (!string.IsNullOrEmpty(fq)) data.FailedQuests.Add(fq); } catch { }
                }
            }
            catch { }
            // Companions - deserialize from JSON string
            try
            {
                string companionsJson = pkg.ReadString();
                if (!string.IsNullOrEmpty(companionsJson))
                {
                    data.Companions = JsonConvert.DeserializeObject<List<object>>(companionsJson) ?? new List<object>();
                }
            }
            catch { }
            // BankItems - deserialize from JSON string
            try
            {
                string bankJson = pkg.ReadString();
                if (!string.IsNullOrEmpty(bankJson))
                {
                    data.BankItems = JsonConvert.DeserializeObject<List<object>>(bankJson) ?? new List<object>();
                }
            }
            catch { }

            // Extended trailing block (see Serialize): only present on newer senders. GetPos() < Size()
            // guards against older packets that end after BankItems.
            try
            {
                if (pkg.GetPos() < pkg.Size())
                {
                    int n = pkg.ReadInt();
                    for (int i = 0; i < n; i++)
                    {
                        string qid = pkg.ReadString();
                        int c = pkg.ReadInt();
                        List<int> list = c > 0 ? new List<int>(c) : null;
                        for (int j = 0; j < c; j++)
                        {
                            int v = pkg.ReadInt();
                            list?.Add(v);
                        }
                        if (list == null) continue;
                        foreach (var aq in data.ActiveQuests)
                        {
                            if (string.Equals(aq.QuestId, qid, StringComparison.Ordinal)) { aq.TargetProgress = list; break; }
                        }
                    }
                }
            }
            catch { }
            return data;
        }

        // Helper: record a completed quest (increment count)
        public void AddCompletedQuest(string questId, int count = 1)
        {
            if (string.IsNullOrEmpty(questId) || count <= 0) return;
            if (!CompletedQuests.ContainsKey(questId)) CompletedQuests[questId] = new CompletedQuestEntry(count, DateTime.UtcNow);
            else
            {
                CompletedQuests[questId].TimesCompleted += count;
                CompletedQuests[questId].LastCompletedAt = DateTime.UtcNow;
            }
        }

        // Add a bank item with stacking: combines amounts for identical Type:Target:Quality keys.
        public void AddBankItem(string type, string target, int amount)
        {
            AddBankItem(type, target, amount, 1, -1f);
        }

        // New overload with quality/durability
        public void AddBankItem(string type, string target, int amount, int quality = 1, float durability = -1f)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(target) || amount <= 0) return;
            try
            {
                string typeNorm = type.Trim();
                string targetNorm = target.Trim();
                // Try to find existing structured item to stack into
                for (int i = 0; i < BankItems.Count; i++)
                {
                    var obj = BankItems[i];
                    if (obj == null) continue;
                    StoredBankItem sb = null;
                    if (obj is StoredBankItem sbi) sb = sbi;
                    else if (obj is string s)
                    {
                        if (!StoredBankItem.TryParse(s, out sb)) sb = null;
                    }
                    else
                    {
                        // attempt to convert via JSON
                        try { sb = JsonConvert.DeserializeObject<StoredBankItem>(JsonConvert.SerializeObject(obj)); } catch { sb = null; }
                    }

                    if (sb == null) continue;
                    if (string.Equals(sb.Type, typeNorm, StringComparison.OrdinalIgnoreCase) && string.Equals(sb.Prefab, targetNorm, StringComparison.OrdinalIgnoreCase) && sb.Quality == quality)
                    {
                        sb.Amount += amount;
                        // replace stored object with structured item
                        BankItems[i] = sb;
                        return;
                    }
                }

                // not found -> add new structured item
                var newItem = new StoredBankItem(typeNorm, targetNorm, amount, quality, durability);
                BankItems.Add(newItem);
            }
            catch { }
        }

        // Parse a raw bank item string and add it via AddBankItem
        public void AddBankItemFromString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                if (StoredBankItem.TryParse(raw, out var parsed))
                {
                    AddBankItem(parsed.Type, parsed.Prefab, parsed.Amount, parsed.Quality, parsed.Durability);
                    return;
                }

                var parts = raw.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                if (parts.Length >= 2)
                {
                    string type = parts[0];
                    string target = parts[1];
                    int amount = 1;
                    if (parts.Length > 2 && int.TryParse(parts[2], out int v)) amount = v;
                    int quality = 1;
                    if (parts.Length > 3 && int.TryParse(parts[3], out int q)) quality = q;
                    float durability = -1f;
                    if (parts.Length > 4) float.TryParse(parts[4], out durability);
                    AddBankItem(type, target, amount, quality, durability);
                }
            }
            catch { }
        }

        // Remove up to `amount` of the specified bank item (type+prefab+quality) and return a StoredBankItem representing what was removed.
        // Returns null if nothing could be taken.
        public StoredBankItem TakeBankItem(string type, string prefab, int amount, int quality = 1)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(prefab) || amount <= 0) return null;
            try
            {
                string typeNorm = type.Trim();
                string prefabNorm = prefab.Trim();
                int remaining = amount;
                float durability = -1f;
                var indicesToUpdate = new List<(int idx, StoredBankItem sb)>();

                for (int i = 0; i < BankItems.Count && remaining > 0; i++)
                {
                    var obj = BankItems[i];
                    if (obj == null) continue;
                    StoredBankItem sb = null;
                    if (obj is StoredBankItem sbi) sb = sbi;
                    else if (obj is string s)
                    {
                        if (!StoredBankItem.TryParse(s, out sb)) sb = null;
                    }
                    else
                    {
                        try { sb = JsonConvert.DeserializeObject<StoredBankItem>(JsonConvert.SerializeObject(obj)); } catch { sb = null; }
                    }

                    if (sb == null) continue;
                    if (!string.Equals(sb.Type, typeNorm, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(sb.Prefab, prefabNorm, StringComparison.OrdinalIgnoreCase)) continue;
                    if (sb.Quality != quality) continue;

                    int take = Math.Min(remaining, sb.Amount);
                    remaining -= take;
                    durability = (durability < 0 && sb.Durability >= 0) ? sb.Durability : durability;

                    // record updated amount for this slot
                    var updated = new StoredBankItem(sb.Type, sb.Prefab, sb.Amount - take, sb.Quality, sb.Durability);
                    indicesToUpdate.Add((i, updated));
                }

                if (remaining == amount) return null; // nothing taken

                // Apply updates: replace slots or remove if amount ==0
                foreach (var tup in indicesToUpdate.OrderByDescending(x => x.idx))
                {
                    if (tup.sb.Amount <= 0)
                    {
                        BankItems.RemoveAt(tup.idx);
                    }
                    else
                    {
                        BankItems[tup.idx] = tup.sb;
                    }
                }

                int takenTotal = amount - remaining;
                var result = new StoredBankItem(typeNorm, prefabNorm, takenTotal, quality, durability);
                return result;
            }
            catch { return null; }
        }

        public void RemoveCompletedQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return;
            if (CompletedQuests.ContainsKey(questId)) CompletedQuests.Remove(questId);
        }

        // Deduplicate and merge current bank items using BankMerge rules
        public void MergeBankItems()
        {
            try
            {
                BankItems = BankMerge.MergeList(BankItems).Cast<object>().ToList();
            }
            catch { }
        }
    }
}
