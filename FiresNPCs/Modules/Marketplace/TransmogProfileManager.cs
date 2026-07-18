using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    public class TransmogOption
    {
        public string Slot;
        public string Prefab;
        public string PricePrefab = "Coins";
        public int PriceAmount;
    }

    /// <summary>
    /// Loads transmog catalogs from <c>BepInEx/config/firesnpcs/transmog/</c>. .cfg format:
    /// <code>
    /// [profilename]
    /// Slot|prefab|pricePrefab|priceAmount
    /// </code>
    /// Slot = Helmet | Chest | Legs | Cape. Real visual swap (ItemDataComponent overriding worn armor
    /// appearance) ships in a follow-up alongside KG's TransmogItem_Component pattern.
    /// </summary>
    public static class TransmogProfileManager
    {
        private static readonly Dictionary<string, List<TransmogOption>> _byProfile = new();
        private static bool _initialized;

        public static IReadOnlyList<TransmogOption> GetAllForProfile(string profile)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<TransmogOption>();
            return _byProfile.TryGetValue(profile.ToLowerInvariant(), out var pool) ? pool : new List<TransmogOption>();
        }

        public static IReadOnlyList<TransmogOption> GetForSlot(string profile, string slot)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<TransmogOption>();
            if (!_byProfile.TryGetValue(profile.ToLowerInvariant(), out var pool)) return new List<TransmogOption>();
            return pool.Where(t => string.Equals(t.Slot, slot, System.StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public static IEnumerable<string> GetLoadedProfileNames() { EnsureLoaded(); return _byProfile.Keys; }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _byProfile.Clear();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder))
                    try { Directory.CreateDirectory(folder); } catch { }
                // Always seed example.cfg if missing — see same fix in BankerProfileManager.
                WriteExampleProfile(folder);
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Transmogs))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[TransmogProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[TransmogProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " transmog(s) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Transmogs);

        private static void LoadOne(string file, string[] lines)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<TransmogOption>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_byProfile.ContainsKey(key)) _byProfile[key] = new List<TransmogOption>();
                    pool = _byProfile[key];
                    continue;
                }
                // My pipe format: Slot|Prefab|Currency|Price
                if (line.Contains("|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 4) continue;
                    int.TryParse(parts[3], out int price);
                    pool.Add(new TransmogOption
                    {
                        Slot = parts[0].Trim(),
                        Prefab = parts[1].Trim(),
                        PricePrefab = parts[2].Trim(),
                        PriceAmount = price,
                    });
                    continue;
                }
                // KG comma format: Prefab, Currency, Price, IgnoreCategory [, optional fields ignored]
                // Auto-classify slot by prefab name prefix (KG didn't store slot — its data was global).
                if (line.Contains(","))
                {
                    var parts = line.Replace(" ", "").Split(',');
                    if (parts.Length < 3) continue;
                    int.TryParse(parts[2], out int kgPrice);
                    pool.Add(new TransmogOption
                    {
                        Slot = GuessSlotFromPrefab(parts[0]),
                        Prefab = parts[0],
                        PricePrefab = parts.Length > 1 ? parts[1] : "Coins",
                        PriceAmount = kgPrice,
                    });
                }
            }
            if (pool.Count > 0 && !_byProfile.ContainsKey(key)) _byProfile[key] = pool;
        }

        /// <summary>Best-effort item-type detection from prefab name (matches KG's ItemType enum strings).
        /// Used when a KG-format .cfg row doesn't carry an explicit slot. Falls back via ObjectDB
        /// lookup if the name doesn't pattern-match.</summary>
        private static string GuessSlotFromPrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "Helmet";
            string p = prefab.ToLowerInvariant();

            // Authoritative source — look up the actual ItemType from ObjectDB when available.
            if (ObjectDB.instance != null)
            {
                var go = ObjectDB.instance.GetItemPrefab(prefab);
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop?.m_itemData?.m_shared != null)
                    return drop.m_itemData.m_shared.m_itemType.ToString();
            }

            // Fall back to name-prefix heuristics for offline / pre-ObjectDB loads.
            if (p.StartsWith("helmet")) return "Helmet";
            if (p.StartsWith("cape")) return "Shoulder";
            if (p.Contains("chest")) return "Chest";
            if (p.Contains("legs")) return "Legs";
            if (p.StartsWith("bow")) return "Bow";
            if (p.StartsWith("shield")) return "Shield";
            if (p.StartsWith("tool") || p.Contains("pickaxe") || p.Contains("hoe") || p.StartsWith("axe") || p.StartsWith("hammer")) return "Tool";
            if (p.Contains("sword") || p.Contains("knife") || p.Contains("club") || p.Contains("mace")) return "OneHandedWeapon";
            if (p.Contains("atgeir") || p.Contains("battleaxe") || p.Contains("sledge") || p.Contains("polearm")) return "TwoHandedWeapon";
            return "Helmet";
        }

        /// <summary>Dumps a profile as Slot|Prefab|Currency|Price lines (one per option).</summary>
        public static string Serialize(IReadOnlyList<TransmogOption> options)
        {
            if (options == null || options.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var o in options)
                sb.AppendLine($"{o.Slot}|{o.Prefab}|{o.PricePrefab}|{o.PriceAmount}");
            return sb.ToString().TrimEnd();
        }

        public static IReadOnlyList<TransmogOption> GetProfileOptions(string profile)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<TransmogOption>();
            return _byProfile.TryGetValue(profile.ToLowerInvariant(), out var pool) ? pool : new List<TransmogOption>();
        }

        public static void SaveProfile(string name, string bodyText)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string content = $"[{name.Trim()}]" + System.Environment.NewLine + (bodyText ?? "").Trim() + System.Environment.NewLine;
                string safe = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");
                string file = Path.Combine(folder, safe + ".cfg");
                File.WriteAllText(file, content);
                _initialized = false;
                EnsureLoaded();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Transmogrifications", safe + ".cfg", content);
                Debug.Log($"[TransmogProfileManager] Saved transmog profile '{name}' -> {file}");
            }
            catch (System.Exception ex) { Debug.LogError("[TransmogProfileManager] SaveProfile failed: " + ex.Message); }
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example transmog catalog. Drop in BepInEx\\config\\firesnpcs\\transmog\\<name>.cfg.",
                "# Format per row: Slot|prefab|pricePrefab|priceAmount",
                "# Slot: Helmet | Chest | Legs | Cape",
                "",
                "[example]",
                "Helmet|HelmetIron|Coins|150",
                "Helmet|HelmetBronze|Coins|80",
                "Helmet|HelmetDverger|Coins|120",
                "Chest|ArmorIronChest|Coins|200",
                "Chest|ArmorBronzeChest|Coins|100",
                "Chest|ArmorLeatherChest|Coins|40",
                "Legs|ArmorIronLegs|Coins|200",
                "Legs|ArmorBronzeLegs|Coins|100",
                "Legs|ArmorLeatherLegs|Coins|40",
                "Cape|CapeOdin|Coins|350",
                "Cape|CapeLox|Coins|180",
                "Cape|CapeLinen|Coins|90",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
