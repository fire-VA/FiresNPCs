using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>Pet levels (Tools\COMPANION_PET_SLOT_DESIGN.md §6, Fire 10-09).
    /// - Level 0 = an un-upgraded pet: visual only, no buff, no skill. Levels 1-5, stars.
    /// - Level and XP live on the pet ITEM (m_customData "fires_pet_level" / "fires_pet_xp"), so they travel with it.
    /// - XP: feeding (taming foods 3x, other food 1x scaled by food tier) and combat (1/s while fighting, capped per day).
    /// - A level needs full XP plus materials: the trophy (else the main drop; the ravens neither) and the tier metal
    ///   (T1 copper, T2 iron, T3 silver, T4 black metal), 1 / 2 / 3 / 5 of each.
    /// - Feed and level-up are server-checked (RPC); the client that owns the inventory pays and writes (vanilla crafting trust).
    /// Every number is server config (ServerSync), section "Pets.Progress".</summary>
    public static class PetProgress
    {
        public const string KeyLevel = "fires_pet_level", KeyXp = "fires_pet_xp", KeyDay = "fires_pet_xpday", KeyDayXp = "fires_pet_daycombat";
        public const int MaxLevel = 5;

        // ── server-synced config ──
        public static ConfigEntry<string> XpToLevel;       // XP for L0→1, 1→2, 2→3, 3→4, 4→5
        public static ConfigEntry<string> MaterialCounts;  // 1 / 2 / 3 / 5 (per level 1→2 .. 4→5; L0→1 uses the first)
        public static ConfigEntry<string> TierMetals;      // T1..T4
        public static ConfigEntry<float> FeedXpBase;       // XP of one food at food tier 1
        public static ConfigEntry<float> TamingFoodMultiplier;
        public static ConfigEntry<float> CombatXpPerSecond;
        public static ConfigEntry<int> CombatXpPerDay;
        public static ConfigEntry<float> BuffScale;        // global multiplier on every buff (admins' balance knob)
        public static ConfigEntry<bool> SkillsEnabled;

        private static bool s_bound;

        /// <summary>BuffScale, clamped 0-2.</summary>
        public static float Scale => Mathf.Clamp(BuffScale?.Value ?? 1f, 0f, 2f);

        /// <summary>Binds + ServerSyncs the "Pets.Progress" entries. Call from the plugin's Awake so the first config sync carries
        /// them ([classes-ui] review #13); PetSkills also calls it at ZNet.Awake as a fallback (no-op once bound).</summary>
        public static void BindConfig()
        {
            if (s_bound) return;
            var plugin = FiresRPGmaker.Instance;
            if (plugin == null) return;
            s_bound = true;
            const string S = "Pets.Progress";
            XpToLevel = Sync(plugin.Config.Bind(S, "XpToLevel", "100,200,350,500,700", "XP needed for each level up: L0→1, 1→2, 2→3, 3→4, 4→5."));
            MaterialCounts = Sync(plugin.Config.Bind(S, "MaterialCounts", "1,1,2,3,5", "How many of the trophy and of the tier metal each level up needs: L0→1, 1→2, 2→3, 3→4, 4→5."));
            TierMetals = Sync(plugin.Config.Bind(S, "TierMetals", "Copper,Iron,Silver,BlackMetal", "The metal a pet of tier 1, 2, 3, 4 needs to level up."));
            FeedXpBase = Sync(plugin.Config.Bind(S, "FeedXpBase", 20f, "XP from one ordinary food of tier 1 (scaled by the food's tier)."));
            TamingFoodMultiplier = Sync(plugin.Config.Bind(S, "TamingFoodMultiplier", 3f, "XP multiplier for the creature's own taming foods."));
            CombatXpPerSecond = Sync(plugin.Config.Bind(S, "CombatXpPerSecond", 1f, "XP per second while the owner fights (dealt or took damage in the last 5 s)."));
            CombatXpPerDay = Sync(plugin.Config.Bind(S, "CombatXpPerDay", 300, "Most combat XP a pet can earn per in-game day."));
            BuffScale = Sync(plugin.Config.Bind(S, "BuffScale", 1f, new ConfigDescription("Multiplier on every pet buff (1 = the design table). Buffs never pass their caps.", new AcceptableValueRange<float>(0f, 2f))));
            SkillsEnabled = Sync(plugin.Config.Bind(S, "SkillsEnabled", true, "Pet passive skills on/off."));
        }

        private static ConfigEntry<T> Sync<T>(ConfigEntry<T> e)
        {
            try { FiresRPGmaker.Instance?.configSync?.AddConfigEntry(e); } catch (Exception ex) { FiresRPGmaker.Log?.LogWarning("[FiresPets] sync " + e.Definition.Key + ": " + ex.Message); }
            return e;
        }

        private static int[] Ints(ConfigEntry<string> e, int[] def)
        {
            try
            {
                var v = (e?.Value ?? "").Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return v.Length >= def.Length ? v : def;
            }
            catch { return def; }
        }

        public static int XpFor(int level) { var a = Ints(XpToLevel, new[] { 100, 200, 350, 500, 700 }); return a[Mathf.Clamp(level, 0, 4)]; }
        public static int MaterialCount(int level) { var a = Ints(MaterialCounts, new[] { 1, 1, 2, 3, 5 }); return a[Mathf.Clamp(level, 0, 4)]; }

        public static string TierMetal(int tier)
        {
            var parts = (TierMetals?.Value ?? "Copper,Iron,Silver,BlackMetal").Split(',');
            return parts[Mathf.Clamp(tier, 1, parts.Length) - 1].Trim();
        }

        // ── the item ──
        public static int Level(ItemDrop.ItemData item) => item != null && item.m_customData != null && item.m_customData.TryGetValue(KeyLevel, out var s) && int.TryParse(s, out int v) ? Mathf.Clamp(v, 0, MaxLevel) : 0;
        public static int Xp(ItemDrop.ItemData item) => item != null && item.m_customData != null && item.m_customData.TryGetValue(KeyXp, out var s) && int.TryParse(s, out int v) ? Mathf.Max(0, v) : 0;

        internal static void Write(ItemDrop.ItemData item, int level, int xp)
        {
            if (item == null) return;
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            item.m_customData[KeyLevel] = Mathf.Clamp(level, 0, MaxLevel).ToString(CultureInfo.InvariantCulture);
            item.m_customData[KeyXp] = Mathf.Max(0, xp).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Adds XP up to the current level's need (XP past it waits; the level-up is a deliberate act with materials).</summary>
        internal static int AddXp(ItemDrop.ItemData item, int amount)
        {
            int level = Level(item);
            if (level >= MaxLevel || amount <= 0) return 0;
            int xp = Xp(item), need = XpFor(level);
            int add = Mathf.Min(amount, Mathf.Max(0, need - xp));
            if (add > 0) Write(item, level, xp + add);
            return add;
        }

        // ── what a level-up needs ──
        public sealed class Need { public string Prefab; public int Count; public string Name; }

        /// <summary>The materials for the next level of this pet (trophy or main drop, plus the tier metal).</summary>
        public static List<Need> NextNeeds(string creature, int level)
        {
            var list = new List<Need>();
            if (level >= MaxLevel) return list;
            int n = MaterialCount(level);
            string signature = SignatureItem(creature);
            if (!string.IsNullOrEmpty(signature)) list.Add(new Need { Prefab = signature, Count = n, Name = ItemName(signature) });
            string metal = TierMetal(PetFacts.Tier(creature));
            list.Add(new Need { Prefab = metal, Count = n, Name = ItemName(metal) });
            return list;
        }

        /// <summary>The creature's trophy, else its main (most likely) drop, else none (the ravens).</summary>
        public static string SignatureItem(string creature)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
            var cd = go != null ? go.GetComponent<CharacterDrop>() : null;
            if (cd == null || cd.m_drops == null) return null;
            var trophy = cd.m_drops.FirstOrDefault(d => d?.m_prefab != null && d.m_prefab.name.StartsWith("Trophy", StringComparison.Ordinal));
            if (trophy != null) return trophy.m_prefab.name;
            var main = cd.m_drops.Where(d => d?.m_prefab != null && d.m_prefab.GetComponent<ItemDrop>() != null).OrderByDescending(d => d.m_chance).FirstOrDefault();
            return main?.m_prefab.name;
        }

        public static string ItemName(string prefab)
        {
            var drop = ObjectDB.instance != null && !string.IsNullOrEmpty(prefab) ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            if (drop == null) return prefab ?? "";
            string n = drop.m_itemData.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(n) : n;
        }

        public static int CountInBag(Inventory inv, string prefab)
        {
            int c = 0;
            if (inv == null) return 0;
            foreach (var it in inv.GetAllItems())
                if (it?.m_dropPrefab != null && string.Equals(it.m_dropPrefab.name, prefab, StringComparison.OrdinalIgnoreCase)) c += it.m_stack;
            return c;
        }

        // ── feeding XP ──
        /// <summary>XP one of this food gives this pet: taming foods × TamingFoodMultiplier, others × 1, scaled by the food's tier.</summary>
        public static int FeedXp(string creature, ItemDrop.ItemData food)
        {
            if (food == null || food.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable || food.m_shared.m_food <= 0f && food.m_shared.m_foodStamina <= 0f) return 0;
            float tier = Mathf.Clamp(FoodTier(food), 1, 7);
            float xp = (FeedXpBase?.Value ?? 20f) * tier;
            if (IsTamingFood(creature, food)) xp *= TamingFoodMultiplier?.Value ?? 3f;
            return Mathf.Max(1, Mathf.RoundToInt(xp));
        }

        public static bool IsTamingFood(string creature, ItemDrop.ItemData food)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
            var ai = go != null ? go.GetComponent<MonsterAI>() : null;
            if (ai?.m_consumeItems == null || food?.m_dropPrefab == null) return false;
            return ai.m_consumeItems.Any(i => i != null && i.gameObject.name == food.m_dropPrefab.name);
        }

        // A food's tier from its health + stamina (vanilla foods run ~25 → ~120 total).
        private static float FoodTier(ItemDrop.ItemData food) => 1f + Mathf.Floor((food.m_shared.m_food + food.m_shared.m_foodStamina) / 40f);
    }

    /// <summary>Facts about a pet creature from its prefab (cached): family, tier, display name.</summary>
    public static class PetFacts
    {
        private static readonly Dictionary<string, (string family, int tier)> s_cache = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);

        public static string Family(string creature) => Get(creature).family;
        public static int Tier(string creature) => Get(creature).tier;

        private static (string family, int tier) Get(string creature)
        {
            if (string.IsNullOrEmpty(creature)) return ("Grazer", 1);
            if (s_cache.TryGetValue(creature, out var v)) return v;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null) return ("Grazer", 1);   // not cached: ZNetScene may not be ready yet
            string fam = creature.StartsWith("FiresRaven", StringComparison.OrdinalIgnoreCase) ? "Flyer" : PetBuffTable.FamilyOf(c);
            v = (fam, PetCatalogTier(c));
            s_cache[creature] = v;
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] family {fam} for {creature} (tier {v.tier})");
            return v;
        }

        // PetCatalog.TierOf is private; the same rule: named bosses by order, everything else by HP (≤60 / ≤250 / ≤900 / more).
        private static int PetCatalogTier(Character c)
        {
            if (c.IsBoss())
            {
                string n = c.gameObject.name;
                if (n.StartsWith("Eikthyr")) return 1;
                if (n.StartsWith("gd_king") || n.StartsWith("Bonemass")) return 2;
                if (n.StartsWith("Dragon") || n.StartsWith("GoblinKing")) return 3;
                return 4;
            }
            float hp = c.m_health;
            return hp <= 60f ? 1 : hp <= 250f ? 2 : hp <= 900f ? 3 : 4;
        }
    }
}
