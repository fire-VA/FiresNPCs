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
        public float InterestPerMinute; // % of current balance, e.g. 0.1 = 0.1% per minute
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
            var folder = GetProfileFolder();
            if (!Directory.Exists(folder))
                try { Directory.CreateDirectory(folder); } catch { }
            // Always seed example.cfg if missing — the previous gate fired ONLY when the folder didn't
            // exist, leaving empty pre-created folders unseeded (the bug the user hit on first banker open).
            WriteExampleProfile(folder);
            foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                try { LoadOne(file); }
                catch (System.Exception ex) { Debug.LogError("[BankerProfileManager] " + file + ": " + ex.Message); }
            }
            Debug.Log("[BankerProfileManager] Loaded " + _profiles.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Bankers);

        private static void LoadOne(string file)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            BankerProfile profile = null;
            foreach (var raw in File.ReadAllLines(file))
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
    /// Accrues passive interest on banker balances at the configured per-minute rate.
    /// Driven by a single MonoBehaviour spawned in Setup; ticks every 60s for the local player.
    /// </summary>
    public static class BankerInterestAccrual
    {
        private static float _accumulator;

        public static void Tick(float deltaSeconds, string profileName)
        {
            _accumulator += deltaSeconds;
            if (_accumulator < 60f) return;
            float minutes = _accumulator / 60f; _accumulator = 0f;

            var profile = BankerProfileManager.GetProfile(profileName);
            if (profile == null || profile.InterestPerMinute <= 0f) return;
            var p = Player.m_localPlayer; if (p == null) return;
            string name = p.GetPlayerName();
            foreach (var kv in profile.CurrencyLimits)
            {
                int balance = BankerBalanceStore.Get(name, kv.Key);
                if (balance <= 0) continue;
                int gain = Mathf.RoundToInt(balance * (profile.InterestPerMinute / 100f) * minutes);
                if (gain <= 0) continue;
                int newBalance = Mathf.Min(balance + gain, kv.Value);
                if (newBalance > balance) BankerBalanceStore.Set(name, kv.Key, newBalance);
            }
        }
    }

    /// <summary>
    /// Per-player banker balances kept in BepInEx config as a tiny CSV. Persistence will move to LiteDB
    /// alongside the rest of the Marketplace stores; this is the v1 dev-friendly storage so test runs
    /// survive game restarts.
    /// </summary>
    public static class BankerBalanceStore
    {
        private static readonly Dictionary<string, int> _balances = new();
        private static bool _loaded;

        public static int Get(string playerName, string currency)
        {
            EnsureLoaded();
            _balances.TryGetValue(Key(playerName, currency), out var v);
            return v;
        }

        public static void Set(string playerName, string currency, int amount)
        {
            EnsureLoaded();
            _balances[Key(playerName, currency)] = Mathf.Max(0, amount);
            Save();
        }

        public static IEnumerable<KeyValuePair<string, int>> ForPlayer(string playerName)
        {
            EnsureLoaded();
            string prefix = (playerName ?? "") + "::";
            foreach (var kv in _balances)
            {
                if (kv.Key.StartsWith(prefix))
                    yield return new KeyValuePair<string, int>(kv.Key.Substring(prefix.Length), kv.Value);
            }
        }

        private static string Key(string player, string currency) => (player ?? "") + "::" + (currency ?? "");

        private static string StorePath => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "banker", "_balances.csv");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var path = StorePath;
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var parts = line.Split(',');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[2], out int v)) continue;
                _balances[Key(parts[0], parts[1])] = v;
            }
        }

        private static void Save()
        {
            try
            {
                var path = StorePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# player,currency,amount" };
                foreach (var kv in _balances)
                {
                    var sep = kv.Key.IndexOf("::", System.StringComparison.Ordinal);
                    if (sep < 0) continue;
                    var player = kv.Key.Substring(0, sep);
                    var currency = kv.Key.Substring(sep + 2);
                    lines.Add(player + "," + currency + "," + kv.Value);
                }
                File.WriteAllLines(path, lines);
            }
            catch (System.Exception ex) { Debug.LogError("[BankerBalanceStore] save failed: " + ex.Message); }
        }
    }
}
