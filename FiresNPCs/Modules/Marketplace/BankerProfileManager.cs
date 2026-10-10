using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Defines the banker rules for a profile: which currencies are accepted, the max deposit per currency,
    /// and the interest rate (% per real-world minute, paid out passively while logged in).
    /// </summary>
    public class BankerProfile
    {
        public string Name;
        /// <summary>currency prefab name -> max balance the bank will hold for the player.</summary>
        public Dictionary<string, int> CurrencyLimits = new Dictionary<string, int>();
        // 0.0.557 (Fire/audit: "the mockup says weekly, the code does per-minute"): `interest=` is the % per PERIOD and
        // `interestperiod=` picks the period (minute | hour | day | week; default minute, so existing cfgs keep their meaning).
        public float InterestPerMinute; // % per PERIOD (name kept for compatibility); see InterestPeriod
        public string InterestPeriod = "minute";
        public long PeriodTicks => InterestPeriod == "week" ? System.TimeSpan.TicksPerDay * 7 : InterestPeriod == "day" ? System.TimeSpan.TicksPerDay : InterestPeriod == "hour" ? System.TimeSpan.TicksPerHour : System.TimeSpan.TicksPerMinute;
        public string PeriodLabel => InterestPeriod == "week" ? "weekly" : InterestPeriod == "day" ? "daily" : InterestPeriod == "hour" ? "hourly" : "per minute";
    }

    /// <summary>
    /// Loads banker profiles from <c>BepInEx/config/firesnpcs/banker/</c>. .cfg format:
    /// <code>
    /// [profilename]
    /// interest=0.10
    /// Coins:1000000
    /// SilverNecklace:200
    /// Ruby:50
    /// </code>
    /// </summary>
    public static class BankerProfileManager
    {
        private static readonly Dictionary<string, BankerProfile> _profiles = new();
        private static bool _initialized;

        public static BankerProfile GetProfile(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            _profiles.TryGetValue(name.ToLowerInvariant(), out var p);
            return p;
        }

        // Auto-load on read so the saved-profiles dropdown sees the on-disk profiles the first time
        // a config screen opens (without forcing every caller to remember EnsureLoaded).
        public static IReadOnlyCollection<string> AllProfileNames { get { EnsureLoaded(); return _profiles.Keys; } }

        /// <summary>Union of every currency name accepted by ANY loaded Banker profile. Used as the
        /// authoritative "possible currencies" list for the Marketplace Sell-tab currency dropdown
        /// (matches KG's <c>_possibleCurrencies</c> global config). Always includes "Coins" as a baseline.</summary>
        public static IReadOnlyList<string> GetAllAcceptedCurrencies()
        {
            EnsureLoaded();
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Coins" };
            foreach (var p in _profiles.Values)
                foreach (var c in p.CurrencyLimits.Keys) set.Add(c);
            var list = new System.Collections.Generic.List<string>(set);
            list.Sort(System.StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _profiles.Clear();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder))
                    try { Directory.CreateDirectory(folder); } catch { }
                // Always seed example.cfg if missing — the previous gate fired ONLY when the folder didn't
                // exist, leaving empty pre-created folders unseeded (the bug the user hit on first banker open).
                WriteExampleProfile(folder);
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Bankers))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[BankerProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[BankerProfileManager] Loaded " + _profiles.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Bankers);

        private static void LoadOne(string file, string[] lines)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            BankerProfile profile = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    profile = new BankerProfile { Name = key };
                    _profiles[key] = profile;
                    continue;
                }
                if (profile == null) { profile = new BankerProfile { Name = key }; _profiles[key] = profile; }
                if (line.StartsWith("interestperiod=", System.StringComparison.OrdinalIgnoreCase))
                {
                    var per = line.Substring(15).Trim().ToLowerInvariant();
                    if (per == "minute" || per == "hour" || per == "day" || per == "week") profile.InterestPeriod = per;
                    continue;
                }
                if (line.StartsWith("interest=", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = line.Substring(9);
                    if (float.TryParse(s, out var v)) profile.InterestPerMinute = v;
                    continue;
                }
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[1].Trim(), out int limit))
                        profile.CurrencyLimits[parts[0].Trim()] = limit;
                    continue;
                }
                // KG-format compat: a bare currency name (no colon) is just "accept this currency"
                // with no per-bank limit. Default to a large cap so the UX still works.
                if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0]))
                    profile.CurrencyLimits[parts[0].Trim()] = 1_000_000;
            }
        }

        /// <summary>Dumps a profile to the line-format the config UI editor consumes — `interest=` line
        /// followed by `CurrencyPrefab:maxBalance` lines. Round-trips through <see cref="LoadOne"/>.</summary>
        public static string Serialize(BankerProfile profile)
        {
            if (profile == null) return "";
            var sb = new System.Text.StringBuilder();
            if (profile.InterestPerMinute != 0f) sb.AppendLine($"interest={profile.InterestPerMinute}");
            if (profile.InterestPeriod != "minute") sb.AppendLine($"interestperiod={profile.InterestPeriod}");
            foreach (var kv in profile.CurrencyLimits) sb.AppendLine($"{kv.Key}:{kv.Value}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Admin save: writes a profile to BepInEx/config/FiresRPGmaker/Bankers/&lt;name&gt;.cfg with
        /// the given body text, then reloads all profiles so the new content is live.</summary>
        public static void SaveProfile(string name, string bodyText)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var folder = GetProfileFolder();
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                string content = $"[{name.Trim()}]" + System.Environment.NewLine + (bodyText ?? "").Trim() + System.Environment.NewLine;
                string safe = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");
                string file = System.IO.Path.Combine(folder, safe + ".cfg");
                System.IO.File.WriteAllText(file, content);
                _initialized = false;
                EnsureLoaded();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Bankers", safe + ".cfg", content);
                Debug.Log($"[BankerProfileManager] Saved banker profile '{name}' -> {file}");
            }
            catch (System.Exception ex) { Debug.LogError("[BankerProfileManager] SaveProfile failed: " + ex.Message); }
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example banker profile. Drop in BepInEx\\config\\firesnpcs\\banker\\<name>.cfg.",
                "# Header [profile], then 'interest=PCT_PER_MIN' and 'CurrencyPrefab:maxBalance' lines.",
                "",
                "[example]",
                "interest=0.05",
                "Coins:1000000",
                "SilverNecklace:200",
                "Ruby:50",
                "Amber:500",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }

    /// <summary>
    /// Client-side DISPLAY cache of the local player's banked balances, keyed by currency prefab name.
    /// Authoritative balances live server-side in the per-world vault (<see cref="FiresCore.Storage.BankRepository"/>);
    /// this cache is filled only by <see cref="BankerNetwork"/> replies. No local file persistence — the server
    /// is the single source of truth, which also ends the old per-client CSV that never synced across players.
    /// </summary>
    public static class BankerBalanceStore
    {
        private static readonly Dictionary<string, int> _cache = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary><paramref name="playerName"/> is ignored — the cache only ever holds the local player.</summary>
        public static int Get(string playerName, string currency)
        {
            _cache.TryGetValue(currency ?? "", out var v);
            return v;
        }

        /// <summary>Overwrites the whole cache from a server BalanceFull sync.</summary>
        public static void ReplaceCache(IReadOnlyDictionary<string, int> map)
        {
            _cache.Clear();
            if (map != null) foreach (var kv in map) _cache[kv.Key] = kv.Value;
        }

        /// <summary>Updates a single currency from a server BalanceOne reply.</summary>
        public static void SetCached(string currency, int balance)
        {
            if (!string.IsNullOrEmpty(currency)) _cache[currency] = Mathf.Max(0, balance);
        }

        /// <summary>Dropped on world change so one server's balances never bleed onto another.</summary>
        public static void ClearCache() => _cache.Clear();
    }
}
