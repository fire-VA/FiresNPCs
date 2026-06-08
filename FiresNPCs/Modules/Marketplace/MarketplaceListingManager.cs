using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>Single auction-house listing row (browse/sell/own).</summary>
    public class MarketplaceListing
    {
        public string ItemPrefab;
        public int Count;
        public int Price;
        public string Currency = "Coins";
        public string SellerName;
        public long SellerId;
    }

    /// <summary>
    /// V1 in-memory marketplace listing store. Sample listings load from
    /// <c>BepInEx/config/firesnpcs/marketplace/&lt;profile&gt;.cfg</c>; real persistence (LiteDB MarketplaceRepository)
    /// + cross-player listings + buyout flow land in a follow-up.
    ///
    /// Sample-listing .cfg format:
    /// <code>
    /// [profilename]
    /// Coins,100,SwordIron,1,AdminMcHelpful
    /// Coins,5,Wood,50,FirewoodPete
    /// </code>
    /// Layout per line: Currency,Price,ItemPrefab,Count,SellerName
    /// </summary>
    public static class MarketplaceListingManager
    {
        private static readonly Dictionary<string, List<MarketplaceListing>> _listingsByProfile = new();
        private static bool _initialized;

        public static IReadOnlyList<MarketplaceListing> GetListings(bool myListingsOnly, string profileName, string filterLower)
        {
            EnsureLoaded();
            if (!_listingsByProfile.TryGetValue((profileName ?? "").ToLowerInvariant(), out var pool))
                return new List<MarketplaceListing>();

            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            IEnumerable<MarketplaceListing> q = pool;
            if (myListingsOnly && !string.IsNullOrEmpty(playerName))
                q = q.Where(l => string.Equals(l.SellerName, playerName, System.StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(filterLower))
                q = q.Where(l => l.ItemPrefab.ToLowerInvariant().Contains(filterLower)
                              || l.SellerName.ToLowerInvariant().Contains(filterLower));
            return q.ToList();
        }

        /// <summary>Items the local player could plausibly list (their inventory slots, not yet wired).</summary>
        public static IReadOnlyList<MarketplaceListing> GetSellPrompts(string profileName, string filterLower)
        {
            EnsureLoaded();
            var p = Player.m_localPlayer; if (p == null) return new List<MarketplaceListing>();
            var inv = p.GetInventory(); if (inv == null) return new List<MarketplaceListing>();
            var result = new List<MarketplaceListing>();
            foreach (var item in inv.GetAllItems())
            {
                string nm = item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared?.m_name ?? "?";
                if (!string.IsNullOrEmpty(filterLower) && !nm.ToLowerInvariant().Contains(filterLower)) continue;
                result.Add(new MarketplaceListing
                {
                    ItemPrefab = nm, Count = item.m_stack, Price = 10, Currency = "Coins",
                    SellerName = p.GetPlayerName(), SellerId = p.GetPlayerID(),
                });
            }
            return result;
        }

        public static IEnumerable<string> GetLoadedProfileNames() { EnsureLoaded(); return _listingsByProfile.Keys; }

        public static void AddListing(string profile, MarketplaceListing l)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return;
            string key = profile.ToLowerInvariant();
            if (!_listingsByProfile.TryGetValue(key, out var pool))
            {
                pool = new List<MarketplaceListing>();
                _listingsByProfile[key] = pool;
            }
            pool.Add(l);
        }

        public static void RemoveListing(string profile, MarketplaceListing l)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return;
            if (_listingsByProfile.TryGetValue(profile.ToLowerInvariant(), out var pool))
                pool.Remove(l);
        }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _listingsByProfile.Clear();
            var folder = GetProfileFolder();
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); } catch { }
                WriteExampleProfile(folder);
            }
            foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                try { LoadOne(file); }
                catch (System.Exception ex) { Debug.LogError("[MarketplaceListingManager] " + file + ": " + ex.Message); }
            }
            int totalRows = _listingsByProfile.Values.Sum(v => v.Count);
            Debug.Log("[MarketplaceListingManager] Loaded " + totalRows + " sample listing(s) across " + _listingsByProfile.Count + " profile(s) from " + folder);
        }

        public static string GetProfileFolder() => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "marketplace");

        private static void LoadOne(string file)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<MarketplaceListing>();
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_listingsByProfile.ContainsKey(key)) _listingsByProfile[key] = new List<MarketplaceListing>();
                    pool = _listingsByProfile[key];
                    continue;
                }
                var parts = line.Split(',');
                if (parts.Length < 5) continue;
                int.TryParse(parts[1], out int price);
                int.TryParse(parts[3], out int count);
                pool.Add(new MarketplaceListing
                {
                    Currency = parts[0], Price = price,
                    ItemPrefab = parts[2], Count = count,
                    SellerName = parts[4],
                });
            }
            if (pool.Count > 0 && !_listingsByProfile.ContainsKey(key)) _listingsByProfile[key] = pool;
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example marketplace sample-listing file — drop in BepInEx\\config\\firesnpcs\\marketplace\\<name>.cfg.",
                "# Format per line: Currency,Price,ItemPrefab,Count,SellerName.",
                "# Header [profile] line(s) group listings under that profile name; filename is the default profile key.",
                "",
                "[example]",
                "Coins,250,SwordIron,1,AdminMcHelpful",
                "Coins,80,BowFineWood,1,FletcherGertrude",
                "Coins,5,Wood,50,FirewoodPete",
                "Coins,15,Resin,20,SapMaster",
                "Coins,40,Feathers,30,RookeryRune",
                "Coins,500,YagluthDrop,1,RaidBossCorpsey",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
