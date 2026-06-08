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
    public class GamblerProfile
    {
        public string Name;
        public int MaxRolls = 1;
        public GamblerItemEntry RequiredItem = new GamblerItemEntry();
        public List<GamblerItemEntry> Rewards = new List<GamblerItemEntry>();
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

        public static IReadOnlyCollection<string> AllProfileNames => _profiles.Keys;

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
            var folder = GetProfileFolder();
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); } catch { }
                WriteExampleProfile(folder);
            }
            foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                try { LoadOne(file); }
                catch (System.Exception ex) { Debug.LogError("[GamblerProfileManager] " + file + ": " + ex.Message); }
            }
            Debug.Log("[GamblerProfileManager] Loaded " + _profiles.Count + " profile(s) from " + folder);
        }

        public static string GetProfileFolder()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "gambler");
        }

        private static void LoadOne(string file)
        {
            // Filename basename = default profile key, so a user who types the filename as the NPC's
            // profile name gets something to load (covers stale [basic]/[premium] example.cfg without
            // forcing a manual delete).
            string fileBasename = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            string firstHeader = null;
            string key = fileBasename; int maxRolls = 1;
            foreach (var raw in File.ReadAllLines(file))
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
                };
            }

            // Filename-basename alias: if the .cfg used custom headers like [basic]/[premium] and the user
            // typed the filename (e.g. "example") as the profile on the NPC, point that name at the FIRST
            // header so the panel still loads something.
            if (firstHeader != null && !_profiles.ContainsKey(fileBasename) && _profiles.TryGetValue(firstHeader, out var firstProfile))
                _profiles[fileBasename] = firstProfile;
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
