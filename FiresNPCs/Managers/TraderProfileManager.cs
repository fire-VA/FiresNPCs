using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// A single item stack in a trade (either given or received).
    /// </summary>
    public class TradeItemStack
    {
        public string PrefabName { get; set; }
        public int Amount { get; set; } = 1;
        public int Level { get; set; } = 0; // 0 = no level / default quality
    }

    /// <summary>
    /// A single exchange entry: give all CostItems, receive all RewardItems.
    /// Stock and Restock are optional per-entry overrides; -1 means "use profile default".
    /// </summary>
    public class TradeEntry
    {
        public List<TradeItemStack> CostItems { get; set; } = new();
        public List<TradeItemStack> RewardItems { get; set; } = new();

        /// <summary>Current available stock. -1 = unlimited.</summary>
        public int Stock { get; set; } = -1;

        /// <summary>Restock interval in seconds. 0 = no restock.</summary>
        public int RestockSeconds { get; set; } = 0;

        /// <summary>Whether Stock was explicitly set on this entry (vs inherited from profile).</summary>
        public bool HasExplicitStock { get; set; }

        /// <summary>Whether RestockSeconds was explicitly set on this entry (vs inherited from profile).</summary>
        public bool HasExplicitRestock { get; set; }
    }

    /// <summary>
    /// A named trader profile containing a list of available trades.
    /// </summary>
    public class TraderProfile
    {
        public string Name { get; set; }
        public string SourceFile { get; set; }
        public List<TradeEntry> Trades { get; set; } = new();

        /// <summary>Default stock for all trades in this profile. -1 = unlimited.</summary>
        public int DefaultStock { get; set; } = -1;

        /// <summary>Default restock interval in seconds for all trades in this profile. 0 = no restock.</summary>
        public int DefaultRestockSeconds { get; set; } = 0;

        /// <summary>Per-NPC trader gold pool cap. -1 = unlimited (trader can always pay out). Header
        /// key: <c>MaxGold=</c>. Current pool is tracked per-NPC on its ZDO so two NPCs sharing the
        /// same profile each have their own balance.</summary>
        public int MaxGold { get; set; } = -1;

        /// <summary>Seconds between trader-gold restocks (refills the pool back to MaxGold). 0 = no
        /// auto-restock. Header key: <c>GoldRestock=</c>.</summary>
        public int GoldRestockSeconds { get; set; } = 0;

        /// <summary>kg `[name=NeedToKnow]` flag: trades are hidden until the player KNOWS every item on
        /// both sides (Player.m_knownMaterial by shared name — kg Trader_UI gate). Also accepted as a
        /// bare <c>NeedToKnow</c> token in the Fires header comma list.</summary>
        public bool NeedToKnow { get; set; }
    }

    /// <summary>
    /// Manages trader profiles loaded from config files using the Marketplace exchange format.
    ///
    /// Supported formats:
    ///
    /// Legacy (4-5 values):
    ///   ItemA, AmountA, ItemB, AmountB[, LevelB]
    ///   Example: Coins, 100, SwordIron, 1, 2
    ///   -> Give 100 Coins, receive 1 SwordIron level 2
    ///
    /// New multi-item (= separator):
    ///   CostItem1, Amount1[, Level1], ... = RewardItem1, Amount1[, Level1], ...
    ///   Example: BlackMetal, 1, AxeBlackMetal, 1, 9, Coins, 25 = AxeBlackMetal, 1, 10, Wood, 123
    ///
    /// Stock &amp; Restock (optional):
    ///   Profile-wide defaults in the section header:
    ///     [craftingMeadows,Stock=25,Restock=600]
    ///   Per-entry overrides appended to the trade line:
    ///     Stone, 25, Coins, 250, Stock=30, Restock=400
    ///   Per-entry values take priority over profile defaults.
    ///   Stock = available trade count (-1 = unlimited)
    ///   Restock = seconds until stock refills (0 = no restock)
    ///
    /// Config files are loaded from BepInEx/config/FiresRPGmaker/Traders/*.cfg
    /// </summary>
    public static class TraderProfileManager
    {
        private static readonly Dictionary<string, TraderProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        private static string TraderProfilesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.Traders);
        private static bool IsServer => ZNet.instance?.IsServer() == true;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
                EnsureFolderExists();

            if (IsServer)
            {
                LoadAllProfiles();
                Debug.Log($"[TraderProfileManager] Server loaded {_profiles.Count} trader profiles");
            }
            else
            {
                // Client also loads local files - server data overrides via ApplyServerData
                LoadAllProfiles();
                Debug.Log($"[TraderProfileManager] Client loaded {_profiles.Count} local trader profiles");
            }

            // Drop-in: layer Marketplace's own Trader configs (config/Marketplace/Configs/Traders) on top when present.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
                LoadMarketplaceProfiles();
        }

        public static void Reset()
        {
            _profiles.Clear();
            _initialized = false;
        }

        public static TraderProfile GetProfile(string name)
        {
            if (!_initialized) Initialize();
            return _profiles.TryGetValue(name, out var p) ? p : null;
        }

        public static IEnumerable<string> GetAllProfileNames()
        {
            if (!_initialized) Initialize();
            return _profiles.Keys;
        }

        /// <summary>
        /// Called on the client when the server sends trader profile data via RPC.
        /// </summary>
        public static void ApplyServerData(string fileName, string fileContent)
        {
            if (string.IsNullOrEmpty(fileContent)) return;
            var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            ParseProfileLines(lines, fileName);
            Debug.Log($"[TraderProfileManager] Applied server data {fileName}; profiles={_profiles.Count}");
        }

        /// <summary>
        /// Returns all profile file contents for sending to clients.
        /// </summary>
        public static Dictionary<string, string> GetAllFileContents()
        {
            var result = new Dictionary<string, string>();
            if (!Directory.Exists(TraderProfilesFolder)) return result;
            foreach (var file in Directory.GetFiles(TraderProfilesFolder, "*.cfg"))
            {
                try { result[Path.GetFileName(file)] = File.ReadAllText(file); }
                catch (Exception ex) { Debug.LogWarning($"[TraderProfileManager] Error reading {file}: {ex.Message}"); }
            }
            return result;
        }

        private static void EnsureFolderExists()
        {
            if (!Directory.Exists(TraderProfilesFolder))
            {
                Directory.CreateDirectory(TraderProfilesFolder);
                CreateExampleProfile();
                Debug.Log($"[TraderProfileManager] Created Traders folder at {TraderProfilesFolder}");
            }
        }

        private static void LoadAllProfiles()
        {
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("Traders"))
            {
                try
                {
                    ParseProfileLines(kv.Value, kv.Key);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TraderProfileManager] Error loading {kv.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Drop-in: also load Marketplace's own Trader configs (config/Marketplace/Configs/Traders/) when
        /// that tree is present. Uses the robust FiresCore.Compat.Cfg parser — tolerant of Marketplace's
        /// missing-']' headers and its [name=NeedToKnow] flag (section.Key strips the flag) — and reuses
        /// ParseTradeLine for the exchange format. A same-named profile is taken over (drop-in source wins).
        /// </summary>
        private static void LoadMarketplaceProfiles()
        {
            string folder = FiresCore.Compat.MarketplacePaths.TradersFolder;
            if (!Directory.Exists(folder)) return;

            var doc = FiresCore.Compat.Cfg.CfgParser.ParseFolder(folder);
            int profileCount = 0, tradeCount = 0;
            foreach (var section in doc.Sections)
            {
                string name = section.Key; // bracket-/space-stripped, lowercased, pre-'=' (handles [name=NeedToKnow])
                if (string.IsNullOrEmpty(name) || name == "default") continue;

                if (!_profiles.TryGetValue(name, out var profile))
                {
                    profile = new TraderProfile { Name = name, SourceFile = "Marketplace" };
                    _profiles[name] = profile;
                }
                else
                {
                    profile.Trades.Clear();
                }
                // kg `[name=NeedToKnow]` — the CfgParser preserves the raw =flag on the section.
                if (string.Equals(section.EqFlag, "NeedToKnow", StringComparison.OrdinalIgnoreCase))
                    profile.NeedToKnow = true;
                profileCount++;

                foreach (var cfgLine in section.Lines)
                {
                    var entry = ParseTradeLine(cfgLine.Text);
                    if (entry == null) continue;
                    if (!entry.HasExplicitStock && profile.DefaultStock != -1) entry.Stock = profile.DefaultStock;
                    if (!entry.HasExplicitRestock && profile.DefaultRestockSeconds != 0) entry.RestockSeconds = profile.DefaultRestockSeconds;
                    profile.Trades.Add(entry);
                    tradeCount++;
                }
            }
            if (profileCount > 0)
                Debug.Log($"[TraderProfileManager] Marketplace drop-in: loaded {profileCount} trader profiles ({tradeCount} trades) from {folder}");
        }

        // ── Editing (admin Trader config screen) ─────────────────────────────────

        /// <summary>Serializes a profile's trades to .cfg text (one line per trade), round-trip-parseable by
        /// ParseTradeLine: legacy "cost,amt,reward,amt[,lvl]" for a single cost+reward (cost level 0), else
        /// the multi-item "cost,amt[,lvl],... = reward,amt[,lvl],..." form; trailing Stock=/Restock= for
        /// entries with explicit overrides.</summary>
        public static string SerializeTrades(TraderProfile profile)
        {
            if (profile == null) return "";
            return string.Join("\n", profile.Trades.Select(SerializeTradeEntry));
        }

        private static string SerializeTradeEntry(TradeEntry t)
        {
            string body;
            bool simple = t.CostItems.Count == 1 && t.RewardItems.Count == 1 && t.CostItems[0].Level == 0;
            if (simple)
            {
                var c = t.CostItems[0]; var r = t.RewardItems[0];
                body = r.Level > 0
                    ? $"{c.PrefabName}, {c.Amount}, {r.PrefabName}, {r.Amount}, {r.Level}"
                    : $"{c.PrefabName}, {c.Amount}, {r.PrefabName}, {r.Amount}";
            }
            else
            {
                string costs = string.Join(", ", t.CostItems.Select(SerializeItem));
                string rewards = string.Join(", ", t.RewardItems.Select(SerializeItem));
                body = $"{costs} = {rewards}";
            }
            if (t.HasExplicitStock) body += $", Stock={t.Stock}";
            if (t.HasExplicitRestock) body += $", Restock={t.RestockSeconds}";
            return body;
        }

        private static string SerializeItem(TradeItemStack s)
            => s.Level > 0 ? $"{s.PrefabName}, {s.Amount}, {s.Level}" : $"{s.PrefabName}, {s.Amount}";

        /// <summary>Admin save: writes a profile to FiresRPGmaker/Traders/&lt;name&gt;.cfg (header keeps the
        /// profile's existing Stock/Restock defaults) with the given trade lines, then reloads all profiles.
        /// Local write + reload — dedicated-server propagation rides on the existing config file watcher.</summary>
        public static void SaveProfile(string name, string tradesText)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                EnsureFolderExists();
                var existing = GetProfile(name);
                string header = name.Trim();
                if (existing != null)
                {
                    if (existing.NeedToKnow) header += "=NeedToKnow";   // kg's flag form — round-trips both parsers
                    if (existing.DefaultStock != -1) header += $",Stock={existing.DefaultStock}";
                    if (existing.DefaultRestockSeconds != 0) header += $",Restock={existing.DefaultRestockSeconds}";
                    if (existing.MaxGold != -1) header += $",MaxGold={existing.MaxGold}";
                    if (existing.GoldRestockSeconds != 0) header += $",GoldRestock={existing.GoldRestockSeconds}";
                }
                string content = $"[{header}]\n{(tradesText ?? "").Trim()}\n";
                string file = Path.Combine(TraderProfilesFolder, MakeSafeFileName(name) + ".cfg");
                File.WriteAllText(file, content);
                Reset();
                Initialize();
                Debug.Log($"[TraderProfileManager] Saved trader profile '{name}' -> {file}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraderProfileManager] SaveProfile '{name}' failed: {ex}");
            }
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name.Trim()) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "Trader";
        }

        private static void ParseProfileLines(string[] lines, string sourceName)
        {
            TraderProfile currentProfile = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                // Section header [ProfileName] or [ProfileName,Stock=25,Restock=600]
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string inner = line.Substring(1, line.Length - 2).Trim();
                    if (string.IsNullOrEmpty(inner)) continue;

                    // Split on comma to separate name from optional key=value pairs
                    var headerParts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    string profileName = headerParts[0].Trim();
                    // MP-style `=flag` suffix on the name (e.g. `[armorer=NeedToKnow]`): capture the kg
                    // NeedToKnow flag, then strip whatever the flag was from the profile key.
                    bool needToKnow = false;
                    int nameEq = profileName.IndexOf('=');
                    if (nameEq >= 0)
                    {
                        needToKnow = profileName.Substring(nameEq + 1).Trim().Equals("NeedToKnow", StringComparison.OrdinalIgnoreCase);
                        profileName = profileName.Substring(0, nameEq).Trim();
                    }

                    int defaultStock = -1;
                    int defaultRestock = 0;
                    int maxGold = -1;
                    int goldRestock = 0;

                    // Parse optional key=value pairs from the header (a bare NeedToKnow token also counts)
                    for (int i = 1; i < headerParts.Length; i++)
                    {
                        string tok = headerParts[i].Trim();
                        if (tok.Equals("NeedToKnow", StringComparison.OrdinalIgnoreCase)) { needToKnow = true; continue; }
                        ParseHeaderKeyValue(tok, ref defaultStock, ref defaultRestock, ref maxGold, ref goldRestock);
                    }

                    if (_profiles.TryGetValue(profileName, out var existing))
                    {
                        currentProfile = existing;
                        // Clear existing trades when re-parsing (prevents duplication
                        // when server data is applied over locally-loaded profiles)
                        currentProfile.Trades.Clear();
                        // Update defaults if re-declared
                        if (defaultStock != -1) currentProfile.DefaultStock = defaultStock;
                        if (defaultRestock != 0) currentProfile.DefaultRestockSeconds = defaultRestock;
                        if (maxGold != -1) currentProfile.MaxGold = maxGold;
                        if (goldRestock != 0) currentProfile.GoldRestockSeconds = goldRestock;
                        if (needToKnow) currentProfile.NeedToKnow = true;
                    }
                    else
                    {
                        currentProfile = new TraderProfile
                        {
                            Name = profileName,
                            SourceFile = sourceName,
                            DefaultStock = defaultStock,
                            DefaultRestockSeconds = defaultRestock,
                            MaxGold = maxGold,
                            GoldRestockSeconds = goldRestock,
                            NeedToKnow = needToKnow,
                        };
                        _profiles[profileName] = currentProfile;
                    }
                    continue;
                }

                // Trade entry line
                if (currentProfile != null)
                {
                    var entry = ParseTradeLine(line);
                    if (entry != null)
                    {
                        // Apply profile defaults where entry doesn't have explicit overrides
                        if (!entry.HasExplicitStock && currentProfile.DefaultStock != -1)
                            entry.Stock = currentProfile.DefaultStock;
                        if (!entry.HasExplicitRestock && currentProfile.DefaultRestockSeconds != 0)
                            entry.RestockSeconds = currentProfile.DefaultRestockSeconds;

                        currentProfile.Trades.Add(entry);
                    }
                }
            }
        }

        /// <summary>
        /// Parses a Key=Value token (e.g. "Stock=25" or "Restock=600").
        /// </summary>
        private static void ParseKeyValue(string token, ref int stock, ref int restock)
        {
            int dummyMaxGold = -1, dummyGoldRestock = 0;
            ParseHeaderKeyValue(token, ref stock, ref restock, ref dummyMaxGold, ref dummyGoldRestock);
        }

        /// <summary>Header variant — also recognises <c>MaxGold=</c> and <c>GoldRestock=</c>, which only
        /// make sense at the profile header (per-trade rows don't carry their own gold pool).</summary>
        private static void ParseHeaderKeyValue(string token, ref int stock, ref int restock, ref int maxGold, ref int goldRestock)
        {
            if (string.IsNullOrEmpty(token)) return;
            var eqIdx = token.IndexOf('=');
            if (eqIdx <= 0) return;
            string key = token.Substring(0, eqIdx).Trim();
            string val = token.Substring(eqIdx + 1).Trim();

            if (key.Equals("Stock", StringComparison.OrdinalIgnoreCase))
            { if (int.TryParse(val, out int s)) stock = s; }
            else if (key.Equals("Restock", StringComparison.OrdinalIgnoreCase))
            { if (int.TryParse(val, out int r)) restock = r; }
            else if (key.Equals("MaxGold", StringComparison.OrdinalIgnoreCase))
            { if (int.TryParse(val, out int g)) maxGold = g; }
            else if (key.Equals("GoldRestock", StringComparison.OrdinalIgnoreCase))
            { if (int.TryParse(val, out int gr)) goldRestock = gr; }
        }

        /// <summary>
        /// Parses a single trade line. Supports both legacy and new multi-item formats.
        /// Also strips optional trailing Stock=X, Restock=Y key=value pairs.
        /// </summary>
        private static TradeEntry ParseTradeLine(string line)
        {
            // Extract and strip any key=value overrides (Stock=X, Restock=Y) from the line.
            // These can appear as trailing comma-separated tokens in either format.
            int entryStock = -1;
            int entryRestock = 0;
            bool hasExplicitStock = false;
            bool hasExplicitRestock = false;
            string cleanLine = StripEntryKeyValues(line, ref entryStock, ref entryRestock,
                ref hasExplicitStock, ref hasExplicitRestock);

            TradeEntry entry;
            // Check for new format with = separator (only in the clean item portion)
            if (cleanLine.Contains("="))
                entry = ParseNewFormatLine(cleanLine);
            else
                entry = ParseLegacyFormatLine(cleanLine);

            if (entry != null)
            {
                entry.Stock = entryStock;
                entry.RestockSeconds = entryRestock;
                entry.HasExplicitStock = hasExplicitStock;
                entry.HasExplicitRestock = hasExplicitRestock;
            }

            return entry;
        }

        /// <summary>
        /// Strips trailing Key=Value tokens (Stock=X, Restock=Y) from a trade line,
        /// returning the cleaned item-only portion.
        /// Example: "Coins, 100, SwordIron, 1, Stock=30, Restock=400"
        ///   -> returns "Coins, 100, SwordIron, 1" and sets stock=30, restock=400
        /// </summary>
        private static string StripEntryKeyValues(string line, ref int stock, ref int restock,
            ref bool hasStock, ref bool hasRestock)
        {
            // Split by comma, walk backwards to find Key=Value tokens at the end
            var parts = line.Split(new[] { ',' });
            int lastItemIndex = parts.Length - 1;

            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var token = parts[i].Trim();
                if (token.IndexOf('=') > 0)
                {
                    string key = token.Substring(0, token.IndexOf('=')).Trim();
                    string val = token.Substring(token.IndexOf('=') + 1).Trim();

                    if (key.Equals("Stock", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int s))
                    {
                        stock = s;
                        hasStock = true;
                        lastItemIndex = i - 1;
                    }
                    else if (key.Equals("Restock", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int r))
                    {
                        restock = r;
                        hasRestock = true;
                        lastItemIndex = i - 1;
                    }
                    else
                    {
                        // Not a recognized key=value, stop stripping
                        break;
                    }
                }
                else
                {
                    // Not a key=value token, stop stripping
                    break;
                }
            }

            if (lastItemIndex < parts.Length - 1)
            {
                return string.Join(",", parts, 0, lastItemIndex + 1);
            }
            return line;
        }

        /// <summary>
        /// Parses legacy format: ItemA, AmountA, ItemB, AmountB[, LevelB]
        /// </summary>
        private static TradeEntry ParseLegacyFormatLine(string line)
        {
            var parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).ToArray();

            if (parts.Length < 4) return null;

            // Parts: ItemA, AmountA, ItemB, AmountB[, LevelB]
            if (!int.TryParse(parts[1], out int amountA)) return null;
            if (!int.TryParse(parts[3], out int amountB)) return null;

            int levelB = 0;
            if (parts.Length >= 5)
                int.TryParse(parts[4], out levelB);

            var entry = new TradeEntry();
            entry.CostItems.Add(new TradeItemStack
            {
                PrefabName = parts[0],
                Amount = amountA
            });
            entry.RewardItems.Add(new TradeItemStack
            {
                PrefabName = parts[2],
                Amount = amountB,
                Level = levelB
            });

            return entry;
        }

        /// <summary>
        /// Parses new multi-item format:
        /// CostItem1, Amount1[, Level1], ... = RewardItem1, Amount1[, Level1], ...
        ///
        /// Detection: a token is a "level" if it's a number AND the previous token was also
        /// a number (the amount). Otherwise a number is an amount and the previous token was
        /// a prefab name.
        /// </summary>
        private static TradeEntry ParseNewFormatLine(string line)
        {
            var halves = line.Split(new[] { '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (halves.Length != 2) return null;

            var costItems = ParseItemStackList(halves[0]);
            var rewardItems = ParseItemStackList(halves[1]);

            if (costItems == null || costItems.Count == 0) return null;
            if (rewardItems == null || rewardItems.Count == 0) return null;

            return new TradeEntry
            {
                CostItems = costItems,
                RewardItems = rewardItems
            };
        }

        /// <summary>
        /// Parses a comma-separated list of item stacks.
        /// Format: PrefabName, Amount[, Level], PrefabName2, Amount2[, Level2], ...
        ///
        /// Logic: read tokens left to right. A non-numeric token starts a new item.
        /// The next numeric token is the amount. If another numeric token follows
        /// before the next non-numeric token, it's the level.
        /// </summary>
        private static List<TradeItemStack> ParseItemStackList(string segment)
        {
            var parts = segment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).ToArray();

            var result = new List<TradeItemStack>();
            TradeItemStack current = null;
            bool hasAmount = false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int numValue))
                {
                    if (current == null) return null; // number without a prefab name

                    if (!hasAmount)
                    {
                        current.Amount = numValue;
                        hasAmount = true;
                    }
                    else
                    {
                        // Already have amount, this is the level
                        current.Level = numValue;
                    }
                }
                else
                {
                    // Non-numeric: this is a prefab name, finalize previous item
                    if (current != null)
                        result.Add(current);

                    current = new TradeItemStack { PrefabName = parts[i] };
                    hasAmount = false;
                }
            }

            // Finalize last item
            if (current != null)
                result.Add(current);

            return result;
        }

        private static void CreateExampleProfile()
        {
            var examplePath = Path.Combine(TraderProfilesFolder, "ExampleTrader.cfg");
            if (File.Exists(examplePath)) return;

            var content = @"# Trader Profile Configuration
# 
# Format (legacy): ItemA, AmountA, ItemB, AmountB[, LevelB]
#   Give AmountA of ItemA, receive AmountB of ItemB at optional level
#
# Format (multi-item): CostItem, Amount[, Level], ... = RewardItem, Amount[, Level], ...
#   Give all left-side items, receive all right-side items
#
# Stock & Restock (optional):
#   Set defaults for the whole profile in the section header:
#     [MyTrader,Stock=25,Restock=600]
#   Override per-entry by appending to the line:
#     Wood, 10, Coins, 50, Stock=50, Restock=300
#   Per-entry values take priority over profile defaults.
#   Stock = number of times this trade can be used (-1 = unlimited)
#   Restock = seconds until stock refills (0 = no restock)
#
# Lines starting with # are comments
# Profiles are sections in [brackets]

[ExampleTrader,Stock=25,Restock=600]
# All trades default to Stock=25, Restock=600
# Sell wood for coins
Wood, 10, Coins, 50
# Buy iron sword for coins (level 2) - override stock for this entry only
Coins, 100, SwordIron, 1, 2, Stock=5, Restock=1200
# Multi-item trade: give blackmetal + coins for upgraded weapon
BlackMetal, 5, Coins, 100 = SwordIron, 1, 3
";
            try { File.WriteAllText(examplePath, content); }
            catch (Exception ex) { Debug.LogWarning($"[TraderProfileManager] Failed to create example: {ex.Message}"); }
        }
    }
}
