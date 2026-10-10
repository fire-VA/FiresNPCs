using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace FiresRPGmaker.Modules.Vault
{
    // Structured representation of a stored bank item. This replaces ad-hoc string storage
    [Serializable]
    public class StoredBankItem
    {
        // Type namespace (e.g. "Item", "Coins", etc.)
        public string Type { get; set; } = string.Empty;
        // Prefab or target identifier (e.g. "Iron", "SwordIron")
        public string Prefab { get; set; } = string.Empty;
        public int Amount { get; set; } = 1;
        // Quality/level if applicable (vanilla uses int quality)
        public int Quality { get; set; } = 1;
        // Durability or condition (vanilla uses float)
        public float Durability { get; set; } = -1f;
        // Optional metadata for future expansion
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();

        public StoredBankItem() { }

        public StoredBankItem(string type, string prefab, int amount, int quality = 1, float durability = -1f)
        {
            Type = type ?? string.Empty;
            Prefab = prefab ?? string.Empty;
            Amount = Math.Max(1, amount);
            Quality = quality;
            Durability = durability;
        }

        public override string ToString()
        {
            // compact legacy-compatible string if no advanced metadata
            if (Meta == null || Meta.Count == 0)
                return $"{Type}:{Prefab}:{Amount}:{Quality}:{Durability}";
            return JsonConvert.SerializeObject(this);
        }

        public static bool TryParse(string raw, out StoredBankItem item)
        {
            item = null;
            if (string.IsNullOrEmpty(raw)) return false;
            // Try JSON first
            if ((raw.StartsWith("{") && raw.EndsWith("}")) || raw.Contains("\"Type\""))
            {
                try
                {
                    item = JsonConvert.DeserializeObject<StoredBankItem>(raw);
                    return item != null;
                }
                catch { }
            }

            // Legacy colon-separated: Type:Prefab:Amount[:Quality[:Durability]] or Type,Prefab,Amount
            try
            {
                var parts = raw.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string type = parts[0].Trim();
                    string prefab = parts[1].Trim();
                    int amount = 1;
                    int quality = 1;
                    float durability = -1f;
                    if (parts.Length > 2) int.TryParse(parts[2], out amount);
                    if (parts.Length > 3) int.TryParse(parts[3], out quality);
                    if (parts.Length > 4) float.TryParse(parts[4], out durability);
                    item = new StoredBankItem(type, prefab, amount, quality, durability);
                    return true;
                }

                // fallback: comma separated
                parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string type = parts[0].Trim();
                    string prefab = parts[1].Trim();
                    int amount = 1;
                    int quality = 1;
                    float durability = -1f;
                    if (parts.Length > 2) int.TryParse(parts[2], out amount);
                    if (parts.Length > 3) int.TryParse(parts[3], out quality);
                    if (parts.Length > 4) float.TryParse(parts[4], out durability);
                    item = new StoredBankItem(type, prefab, amount, quality, durability);
                    return true;
                }
            }
            catch { }

            return false;
        }
    }

    public static class BankMerge
    {
        // Merge two stackable items; returns false if incompatible. Durability keeps max non-negative.
        public static bool TryMerge(StoredBankItem a, StoredBankItem b, out StoredBankItem merged)
        {
            merged = null;
            if (a == null || b == null) return false;
            if (!string.Equals(a.Type, b.Type, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Quality != b.Quality) return false;

            float durability = a.Durability >= 0 && b.Durability >= 0 ? Math.Max(a.Durability, b.Durability) : (a.Durability >= 0 ? a.Durability : b.Durability);
            merged = new StoredBankItem(a.Type, a.Prefab, a.Amount + b.Amount, a.Quality, durability);
            return true;
        }

        // Deduplicate a list of StoredBankItem-compatible objects (string or StoredBankItem)
        public static List<StoredBankItem> MergeList(IEnumerable<object> source)
        {
            var result = new List<StoredBankItem>();
            if (source == null) return result;

            foreach (var obj in source)
            {
                StoredBankItem item = null;
                if (obj is StoredBankItem sbi)
                {
                    item = sbi;
                }
                else if (obj is string s)
                {
                    StoredBankItem.TryParse(s, out item);
                }
                else
                {
                    try { item = JsonConvert.DeserializeObject<StoredBankItem>(JsonConvert.SerializeObject(obj)); } catch { item = null; }
                }

                if (item == null) continue;

                var existing = result.FirstOrDefault(x => TryMerge(x, item, out _));
                if (existing == null)
                {
                    result.Add(item);
                }
                else
                {
                    TryMerge(existing, item, out var merged);
                    result[result.IndexOf(existing)] = merged;
                }
            }

            return result;
        }
    }
}
