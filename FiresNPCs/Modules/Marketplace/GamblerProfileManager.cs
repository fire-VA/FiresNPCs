using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Item entry — currency or reward. Min/Max are stack range (Min==Max => fixed stack).
    /// </summary>
    public class GamblerItemEntry
    {
        public string Prefab;
        public int Min;
        public int Max;
    }

    /// <summary>
    /// One gambler profile = one currency to pay + N reward items + max rolls per spin.
    /// Mirrors KG's <c>Gambler_DataTypes.BigData</c>.
    /// </summary>
    /// <summary>The gambling mini-games a profile can offer. Keys match the player-panel tabs.</summary>
    public static class GamblerGames
    {
        public const string Cards = "cards";
        public const string Bones = "bones";
        public const string Runes = "runes";
        public const string Wheel = "wheel";
        public const string Duel = "duel";
        public static readonly string[] All = { Cards, Bones, Runes, Wheel, Duel };
        public static bool IsValid(string g) => System.Array.IndexOf(All, g) >= 0;
    }

    public class GamblerProfile
    {
        public string Name;
        public int MaxRolls = 1;
        public GamblerItemEntry RequiredItem = new GamblerItemEntry();
        public List<GamblerItemEntry> Rewards = new List<GamblerItemEntry>();
        // Games this profile offers (player-panel tabs). Absent "Games:" line => cards-only (vanilla compat).
        public List<string> Games = new List<string> { GamblerGames.Cards };
    }

    /// <summary>
    /// Loads gambler profiles from <c>BepInEx/config/firesnpcs/gambler/*.cfg</c>. KG's file format:
    /// <code>
    /// [profilename]                 # max_rolls defaults to 1
    /// [profilename=N]               # N = max rolls per spin
    /// Coins,10,YagluthDrop,1-3      # first item = required currency, rest = rewards (Min[-Max])
    /// </code>
    /// </summary>
    public static class GamblerProfileManager
    {
        private static readonly Dictionary<string, GamblerProfile> _profiles = new();
        private static bool _initialized;

        // Auto-load on read so the saved-profiles dropdown sees the on-disk profiles the first time
        // a config screen opens (without forcing every caller to remember EnsureLoaded).
        public static IReadOnlyCollection<string> AllProfileNames { get { EnsureLoaded(); return _profiles.Keys; } }

        public static GamblerProfile GetProfile(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            _profiles.TryGetValue(name.ToLowerInvariant(), out var p);
            return p;
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
                // Always seed example.cfg if missing — the previous gate only fired when the folder didn't
                // exist, leaving empty pre-created folders unseeded (the same bug the Banker manager hit).
                WriteExampleProfile(folder);
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Gamblers))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[GamblerProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[GamblerProfileManager] Loaded " + _profiles.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Gamblers);

        private static void LoadOne(string file, string[] lines)
        {
            // Filename basename = default profile key, so a user who types the filename as the NPC's
            // profile name gets something to load (covers stale [basic]/[premium] example.cfg without
            // forcing a manual delete).
            string fileBasename = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            string firstHeader = null;
            string key = fileBasename; int maxRolls = 1; List<string> currentGames = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("["))
                {
                    var inside = line.Replace("[", "").Replace("]", "").Replace(" ", "");
                    var eq = inside.Split('=');
                    key = eq[0].ToLowerInvariant();
                    if (firstHeader == null) firstHeader = key;
                    maxRolls = eq.Length > 1 && int.TryParse(eq[1], out var r) ? r : 1;
                    currentGames = null;
                    continue;
                }

                // Fires extension: "Games: cards, bones, wheel" picks which mini-games this profile offers.
                var directive = line.Replace(" ", "").ToLowerInvariant();
                if (directive.StartsWith("games:") || directive.StartsWith("games="))
                {
                    currentGames = ParseGames(line);
                    if (_profiles.TryGetValue(key, out var existingForGames)) existingForGames.Games = currentGames;
                    continue;
                }

                var parts = line.Replace(" ", "").Split(',');
                if (parts.Length % 2 != 0)
                {
                    Debug.LogWarning("[GamblerProfileManager] " + file + ": odd-element line, skipping: " + line);
                    continue;
                }
                var items = new List<GamblerItemEntry>();
                for (int i = 0; i < parts.Length; i += 2)
                {
                    var entry = new GamblerItemEntry { Prefab = parts[i] };
                    var range = parts[i + 1].Split('-');
                    int.TryParse(range[0], out entry.Min);
                    entry.Max = range.Length > 1 && int.TryParse(range[1], out var mx) ? mx : entry.Min;
                    items.Add(entry);
                }
                if (items.Count == 0) continue;
                var required = items[0]; items.RemoveAt(0);
                _profiles[key] = new GamblerProfile
                {
                    Name = key, MaxRolls = maxRolls,
                    RequiredItem = required, Rewards = items,
                    Games = currentGames ?? new List<string> { GamblerGames.Cards },
                };
            }

            // Filename-basename alias: if the .cfg used custom headers like [basic]/[premium] and the user
            // typed the filename (e.g. "example") as the profile on the NPC, point that name at the FIRST
            // header so the panel still loads something.
            if (firstHeader != null && !_profiles.ContainsKey(fileBasename) && _profiles.TryGetValue(firstHeader, out var firstProfile))
                _profiles[fileBasename] = firstProfile;
        }

        /// <summary>Dumps a profile to KG-format header + entries body. Round-trips through LoadOne.</summary>
        public static string Serialize(GamblerProfile profile)
        {
            if (profile == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append(profile.RequiredItem.Prefab).Append(',').Append(SerializeRange(profile.RequiredItem));
            foreach (var r in profile.Rewards) sb.Append(',').Append(r.Prefab).Append(',').Append(SerializeRange(r));
            return sb.ToString();
        }
        private static string SerializeRange(GamblerItemEntry e) => e.Min == e.Max ? e.Min.ToString() : $"{e.Min}-{e.Max}";

        /// <summary>Parses a "Games: cards, bones, wheel" line into the validated game-key list (cards-only if empty/invalid).</summary>
        private static List<string> ParseGames(string line)
        {
            int sep = line.IndexOfAny(new[] { ':', '=' });
            string val = sep >= 0 ? line.Substring(sep + 1) : "";
            var games = new List<string>();
            foreach (var tok in val.Split(','))
            {
                var g = tok.Trim().ToLowerInvariant();
                if (GamblerGames.IsValid(g) && !games.Contains(g)) games.Add(g);
            }
            if (games.Count == 0) games.Add(GamblerGames.Cards);
            return games;
        }

        /// <summary>Normalizes a game-key list to the canonical "cards,bones,..." cfg value (cards-only fallback).</summary>
        public static string SerializeGames(IEnumerable<string> games)
        {
            var valid = new List<string>();
            if (games != null)
                foreach (var g in games)
                {
                    var k = (g ?? "").ToLowerInvariant();
                    if (GamblerGames.IsValid(k) && !valid.Contains(k)) valid.Add(k);
                }
            if (valid.Count == 0) valid.Add(GamblerGames.Cards);
            return string.Join(",", valid);
        }

        public static void SaveProfile(string name, int maxRolls, string bodyText, IEnumerable<string> games = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string header = maxRolls > 1 ? $"[{name.Trim()}={maxRolls}]" : $"[{name.Trim()}]";
                string content = header + System.Environment.NewLine + (bodyText ?? "").Trim() + System.Environment.NewLine;
                if (games != null) content += "Games: " + SerializeGames(games) + System.Environment.NewLine;
                string safe = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");
                string file = Path.Combine(folder, safe + ".cfg");
                File.WriteAllText(file, content);
                _initialized = false;
                EnsureLoaded();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Gamblers", safe + ".cfg", content);
                Debug.Log($"[GamblerProfileManager] Saved gambler profile '{name}' (maxRolls={maxRolls}) -> {file}");
            }
            catch (System.Exception ex) { Debug.LogError("[GamblerProfileManager] SaveProfile failed: " + ex.Message); }
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example gambler profile — drop in BepInEx\\config\\firesnpcs\\gambler\\<name>.cfg.",
                "# Format: [profilename] header (optionally =MAXROLLS), then comma-separated PREFAB,MIN-MAX pairs.",
                "# First pair on each line is the REQUIRED currency to pay; the rest are possible rewards.",
                "",
                "[example]",
                "Coins,10,Wood,5-15,Stone,5-15,Resin,2-6,Feathers,1-4,Flint,2-8",
                "",
                "[premium=3]",
                "Coins,50,SilverNecklace,1,Ruby,1-2,Amber,2-4,SurtlingCore,1-2,YagluthDrop,1",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
