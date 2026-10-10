using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>KG-aligned category enum so the Marketplace UI can filter listings.</summary>
    public enum MarketCategory { All, Weapons, Armor, Consumable, Tools, Resources }

    /// <summary>Single auction-house listing row (browse/sell/own).</summary>
    public class MarketplaceListing
    {
        /// <summary>Server-side LiteDB listing id (0 for inventory sell-prompts not yet listed). Buy/Cancel
        /// reference this so the server knows exactly which row to mutate.</summary>
        public int Id;
        public string ItemPrefab;
        public int Count;
        public int Price;
        public string Currency = "Coins";
        public string SellerName;
        public long SellerId;

        // kg MarketSlot item-fidelity fields: an upgraded/damaged/crafted item survives the round trip
        // (captured on sell, carried on the wire, restored on buy). Defaults = a fresh quality-1 item.
        public int Quality = 1;
        public int Variant;
        public byte DurabilityPercent = 100;
        public string CrafterName = "";
        public long CrafterID;
        public string CustomData = "{}";

        /// <summary>Cached category for filtering — derived from the ItemDrop's ItemType the first
        /// time we look at it (vanilla Armor/Weapon/Bow/Shield → KG bucket). All=passes everything.</summary>
        public MarketCategory Category = MarketCategory.All;
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

        // The live GLOBAL market, replaced wholesale by MarketplaceNetwork replies/broadcasts. The panel reads
        // it through GetListings; the server never reads this (it owns the LiteDB repo directly).
        private static readonly List<MarketplaceListing> _live = new List<MarketplaceListing>();

        public static void ReplaceLive(List<MarketplaceListing> listings)
        {
            _live.Clear();
            if (listings != null) _live.AddRange(listings);
        }

        public static void ClearLive() => _live.Clear();

        public static IReadOnlyList<MarketplaceListing> GetListings(bool myListingsOnly, string profileName, string filterLower)
            => GetListings(myListingsOnly, profileName, filterLower, MarketCategory.All);

        // profileName is retained for call-site compatibility but ignored — the market is global now.
        /// <summary>The key the server files the local player's listings under: the platform user id (Core OwnerKeys,
        /// kg parity), parsed the way the listing rows are sent (MarketplaceNetwork ParseId). 0.0.532: the UI compared
        /// rows with Player.GetPlayerID() (the save-file id), so My Listings was empty for every non-admin and nobody
        /// could cancel their own listing. Falls back to the player id when the platform id isn't numeric.</summary>
        internal static long LocalSellerKey()
        {
            try
            {
                string local = Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.m_userID;
                if (long.TryParse(local, out long key) && key != 0L) return key;
            }
            catch { }
            return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
        }

        public static IReadOnlyList<MarketplaceListing> GetListings(bool myListingsOnly, string profileName, string filterLower, MarketCategory category)
        {
            long myId = LocalSellerKey();
            IEnumerable<MarketplaceListing> q = _live;
            // Admins see EVERY listing on the My Listings tab (kg's admin-remove surface): the existing
            // Cancel flow then routes non-owned removals through the server's admin branch.
            if (myListingsOnly && !global::FiresCore.Sync.AdminSyncing.IsLocalAdmin())
                q = q.Where(l => myId != 0L && l.SellerId == myId);
            if (!string.IsNullOrEmpty(filterLower))
                q = q.Where(l => (l.ItemPrefab ?? "").ToLowerInvariant().Contains(filterLower)
                              || (l.SellerName ?? "").ToLowerInvariant().Contains(filterLower));
            if (category != MarketCategory.All)
                q = q.Where(l => ClassifyCategory(l) == category);
            return q.ToList();
        }

        /// <summary>A profile's sample .cfg listings — read server-side to seed the global market once.</summary>
        public static IReadOnlyList<MarketplaceListing> GetProfileSamples(string profile)
        {
            EnsureLoaded();
            return _listingsByProfile.TryGetValue((profile ?? "").ToLowerInvariant(), out var pool)
                ? pool : new List<MarketplaceListing>();
        }

        /// <summary>Maps a listing's item to a KG MarketCategory, by ObjectDB lookup the first time.</summary>
        public static MarketCategory ClassifyCategory(MarketplaceListing l)
        {
            if (l.Category != MarketCategory.All) return l.Category;
            if (ObjectDB.instance == null) return MarketCategory.All;
            var go = ObjectDB.instance.GetItemPrefab(l.ItemPrefab);
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData?.m_shared == null) return MarketCategory.All;
            var t = drop.m_itemData.m_shared.m_itemType;
            l.Category = t switch
            {
                ItemDrop.ItemData.ItemType.OneHandedWeapon
              or ItemDrop.ItemData.ItemType.TwoHandedWeapon
              or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
              or ItemDrop.ItemData.ItemType.Bow => MarketCategory.Weapons,
                ItemDrop.ItemData.ItemType.Helmet
              or ItemDrop.ItemData.ItemType.Chest
              or ItemDrop.ItemData.ItemType.Legs
              or ItemDrop.ItemData.ItemType.Shoulder
              or ItemDrop.ItemData.ItemType.Shield => MarketCategory.Armor,
                ItemDrop.ItemData.ItemType.Consumable => MarketCategory.Consumable,
                ItemDrop.ItemData.ItemType.Tool => MarketCategory.Tools,
                ItemDrop.ItemData.ItemType.Material
              or ItemDrop.ItemData.ItemType.Trophy
              or ItemDrop.ItemData.ItemType.Misc
              or ItemDrop.ItemData.ItemType.Ammo => MarketCategory.Resources,
                _ => MarketCategory.All,
            };
            return l.Category;
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
                // kg parity: MarketSellBlockedPrefabs never appear in the sell list (the server would
                // reject-and-refund anyway; hiding them here avoids the confusing round-trip).
                if (MarketplaceGlobals.IsBlockedPrefab(nm)) continue;
                byte durPct = 100;
                try
                {
                    if (item.m_shared != null && item.m_shared.m_useDurability)
                    {
                        float max = item.GetMaxDurability();
                        if (max > 0f) durPct = (byte)Mathf.Clamp(Mathf.RoundToInt(item.m_durability / max * 100f), 0, 100);
                    }
                }
                catch { }
                string custom = "{}";
                try { if (item.m_customData != null && item.m_customData.Count > 0) custom = Newtonsoft.Json.JsonConvert.SerializeObject(item.m_customData); } catch { }
                result.Add(new MarketplaceListing
                {
                    ItemPrefab = nm, Count = item.m_stack, Price = 10, Currency = "Coins",
                    SellerName = p.GetPlayerName(), SellerId = p.GetPlayerID(),
                    Quality = item.m_quality, Variant = item.m_variant, DurabilityPercent = durPct,
                    CrafterName = item.m_crafterName ?? "", CrafterID = item.m_crafterID, CustomData = custom,
                });
            }
            return result;
        }

        public static IEnumerable<string> GetLoadedProfileNames() { EnsureLoaded(); return _listingsByProfile.Keys; }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _listingsByProfile.Clear();
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
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Marketplace))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[MarketplaceListingManager] " + kv.Key + ": " + ex.Message); }
            }
            int totalRows = _listingsByProfile.Values.Sum(v => v.Count);
            Debug.Log("[MarketplaceListingManager] Loaded " + totalRows + " sample listing(s) across " + _listingsByProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Marketplace);

        private static void LoadOne(string file, string[] lines)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<MarketplaceListing>();
            foreach (var raw in lines)
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
