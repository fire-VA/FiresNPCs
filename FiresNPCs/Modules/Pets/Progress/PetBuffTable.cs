using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>What a pet can add to its owner (Tools\COMPANION_PET_SLOT_DESIGN.md §6-7, Fire 10-09). Percent kinds are
    /// stored as fractions (0.02 = 2%); flat kinds in their own unit.</summary>
    public enum PetStat
    {
        None,
        RunSpeed,          // +% move speed (running)
        SprintSpeedNight,  // +% move speed at night
        Jump,              // +% jump height
        SwimSpeed,         // +% swim speed
        StaminaRegen,      // +% stamina regen
        StaminaRegenCaves, // +% stamina regen in caves / crypts (dungeon interiors)
        HealthRegen,       // +HP/s
        HealthRegenWater,  // +HP/s while swimming / wet
        EitrRegen,         // +eitr/s
        EitrRegenPct,      // +% eitr regen
        Carry,             // +carry weight
        Armor,             // +armour
        Damage,            // +% all damage dealt
        ElementalDamage,   // +% fire / frost / lightning / poison / spirit dealt
        FireDamage,        // +% fire damage dealt
        WoodDamage,        // +% chop damage
        ResistFire, ResistFrost, ResistPoison, ResistSpirit, ResistPhysical, ResistBlunt, ResistPierce, // −% damage taken
        Noise,             // −% noise
        NoiseNight,        // −% noise at night
        RunStaminaUse,     // −% run stamina
        SwimStaminaUse,    // −% swim stamina
        FallDamage,        // −% fall damage
        Stagger,           // −% stagger taken
        SkillXp,           // +% skill gain
        Luck,              // +% rare-drop chance (chance × (1 + Luck), rare = under 25%)
        DropChance,        // +% chance of +1 of the listed drops (gather or kill)
        FoodDuration,      // +% food duration (phase 2: shown as coming soon)
        ExploreRadius,     // +m map reveal radius (phase 2)
        AttackSpeed,       // +% attack speed (phase 2)
        SlowResist,        // −% slow (phase 2)
    }

    /// <summary>One buff line: PerLevel at each level 1-5 (L4→L5 adds half a step), never past Cap.</summary>
    public sealed class PetBuffLine
    {
        public PetStat Stat;
        public float PerLevel, Cap;
        public string[] Items;   // DropChance: the drop prefabs it applies to
        public string Label;     // shown text, e.g. "run speed"

        /// <summary>The value at a level × the server's BuffScale, clamped to Cap AFTER scaling ([classes-ui] review #12: a scale
        /// must never lift a buff past its cap).</summary>
        public float At(int level, float scale = 1f)
        {
            if (level <= 0) return 0f;
            float steps = Mathf.Min(level, 4) + (level >= 5 ? 0.5f : 0f);
            return Mathf.Min(Cap, PerLevel * steps * Mathf.Max(0f, scale));
        }
    }

    /// <summary>A passive skill. Phase 1 skills are live; phase 2 show as "coming soon" (Fire 10-09: built later, not cut).</summary>
    public sealed class PetSkill
    {
        public string Id, Name, Text;
        public int Phase = 1;
        public int MinLevel = 1;
        public float Chance, PerLevel, Cap;   // meaning per skill (see PetSkills)
        public string Item;                   // the item an extra-drop skill gives / reacts to
        public float At(int level) => level < MinLevel ? 0f : Mathf.Min(Cap > 0f ? Cap : float.MaxValue, Chance + PerLevel * (level - 1));
    }

    public sealed class PetDef
    {
        public string Key;        // creature prefab, or "family:<Family>"
        public string Family;
        public List<PetBuffLine> Buffs = new List<PetBuffLine>();
        public PetSkill Skill;
    }

    /// <summary>The §7 table: every vanilla creature row, the Fires ravens, and the family fallback for anything else
    /// (modded creatures included). Server config may override a row (PetProgressConfig.Overrides).</summary>
    public static class PetBuffTable
    {
        private static readonly Dictionary<string, PetDef> s_rows = new Dictionary<string, PetDef>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PetDef> s_family = new Dictionary<string, PetDef>(StringComparer.OrdinalIgnoreCase);
        private static bool s_built;

        public static IEnumerable<PetDef> Rows { get { Build(); return s_rows.Values; } }

        /// <summary>The row for a creature prefab, else its family's row. Never null.</summary>
        public static PetDef For(string creature, string family)
        {
            Build();
            if (!string.IsNullOrEmpty(creature) && s_rows.TryGetValue(creature, out var d)) return d;
            if (!string.IsNullOrEmpty(family) && s_family.TryGetValue(family, out var f)) return f;
            return s_family["Grazer"];
        }

        internal static void Replace(PetDef def)
        {
            Build();
            if (def.Key.StartsWith("family:", StringComparison.OrdinalIgnoreCase)) s_family[def.Key.Substring(7)] = def;
            else s_rows[def.Key] = def;
        }

        // ── table ──
        private static PetBuffLine B(PetStat s, float per, float cap, string label, params string[] items)
            => new PetBuffLine { Stat = s, PerLevel = per, Cap = cap, Label = label, Items = items.Length > 0 ? items : null };
        private static PetSkill S(string id, string name, string text, int phase = 1, float chance = 0f, float perLevel = 0f, float cap = 0f, string item = null, int minLevel = 1)
            => new PetSkill { Id = id, Name = name, Text = text, Phase = phase, Chance = chance, PerLevel = perLevel, Cap = cap, Item = item, MinLevel = minLevel };

        private static void Row(string family, PetSkill skill, PetBuffLine[] buffs, params string[] creatures)
        {
            foreach (var c in creatures) s_rows[c] = new PetDef { Key = c, Family = family, Skill = skill, Buffs = new List<PetBuffLine>(buffs) };
        }

        private static void Build()
        {
            if (s_built) return;
            s_built = true;
            const float P = 0.01f;   // one percent

            // Meadows
            Row("Grazer", S("truffle_snout", "Truffle snout", "{0} chance a picked mushroom or berry gives +1", chance: 3 * P, perLevel: 0.5f * P, cap: 5 * P),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "meat drop chance", "RawMeat") }, "Boar", "Boar_piggy");
            Row("Grazer", S("light_step", "Light step", "{0} less fall damage", chance: 5 * P, perLevel: 5 * P, cap: 20 * P),
                new[] { B(PetStat.RunSpeed, 2 * P, 9 * P, "run speed") }, "Deer");
            Row("Swimmer", S("water_born", "Water-born", "Wet lasts half as long"),
                new[] { B(PetStat.SwimStaminaUse, 5 * P, 20 * P, "swim stamina use") }, "Neck");
            Row("Predator", S("pocket_stone", "Pocket stone", "{0} chance of a free stone on a pickaxe hit", chance: 2 * P, perLevel: 0.5f * P, cap: 4 * P, item: "Stone"),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "wood drop chance", "Wood") }, "Greyling");
            Row("Grazer", S("egg_day", "Egg day", "Once a day lays an egg in your bag (L3+)", phase: 2, minLevel: 3),
                new[] { B(PetStat.FoodDuration, 2 * P, 9 * P, "food duration"), B(PetStat.Luck, 0.5f * P, 2.5f * P, "Luck") }, "Hen", "Chicken");
            Row("Boss", S("thunder_hop", "Thunder hop", "A small double jump (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.RunSpeed, 2 * P, 9 * P, "run speed"), B(PetStat.Jump, 1 * P, 4 * P, "jump") }, "Eikthyr");

            // Black Forest
            Row("Predator", S("pinecone", "Pinecone", "{0} chance felling a tree drops a pinecone", chance: 2 * P, perLevel: 0.5f * P, cap: 4 * P, item: "PineCone"),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "resin / eye drop chance", "Resin", "GreydwarfEye") }, "Greydwarf");
            Row("Predator", S("thick_bark", "Thick bark", "{0} less blunt damage taken", chance: 3 * P, perLevel: 3 * P, cap: 12 * P),
                new[] { B(PetStat.Armor, 1, 4, "armour") }, "Greydwarf_Elite");
            Row("Predator", S("mend", "Mend", "On a kill, heal {0} of max HP", chance: 1 * P, perLevel: 0.25f * P, cap: 2 * P),
                new[] { B(PetStat.HealthRegen, 0.5f, 2, "health regen /s") }, "Greydwarf_Shaman");
            Row("Predator", S("big_hands", "Big hands", "+1 stack size on logs and stone (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.Carry, 5, 20, "carry weight") }, "Troll");
            Row("Undead", S("rattle", "Rattle", "Undead notice you from 5% less far per level", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "bone fragment drop chance", "BoneFragments") }, "Skeleton", "Skeleton_NoArcher", "Skeleton_Poison");
            Row("Undead", S("glimmer", "Glimmer", "A faint light around you at night", phase: 2),
                new[] { B(PetStat.Noise, 3 * P, 12 * P, "noise") }, "Ghost");
            Row("Predator", S("honey_nose", "Honey nose", "{0} chance of +1 honey from a beehive", chance: 10 * P, perLevel: 1 * P, cap: 15 * P, item: "Honey"),
                new[] { B(PetStat.Armor, 1, 4, "armour"), B(PetStat.Carry, 3, 12, "carry weight") }, "Bjorn");
            Row("Boss", S("root_grip", "Root grip", "No slide on steep wet slopes", phase: 2),
                new[] { B(PetStat.WoodDamage, 2 * P, 9 * P, "wood damage") }, "gd_king");

            // Swamp
            Row("Undead", S("grave_sense", "Grave sense", "Crypt chests show on the minimap within 20 m", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "iron scrap drop chance", "IronScrap") }, "Draugr", "Draugr_Ranged");
            Row("Undead", S("stubborn", "Stubborn", "{0} less stagger taken", chance: 3 * P, perLevel: 3 * P, cap: 12 * P),
                new[] { B(PetStat.Armor, 1, 4, "armour") }, "Draugr_Elite");
            Row("Undead", S("squish", "Squish", "{0} less fall damage", chance: 5 * P, perLevel: 5 * P, cap: 20 * P),
                new[] { B(PetStat.ResistPoison, 5 * P, 20 * P, "poison resistance") }, "Blob", "BlobElite");
            Row("Predator", S("bloodletter", "Bloodletter", "2% lifesteal on melee hits (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.HealthRegenWater, 0.5f, 2, "health regen in water /s") }, "Leech");
            Row("Demon", S("ember", "Ember", "Torches burn {0} longer", chance: 20 * P, cap: 20 * P),
                new[] { B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance") }, "Surtling");
            Row("Undead", S("unseen", "Unseen", "5% chance enemies lose track of you for 1 s when hit", phase: 2),
                new[] { B(PetStat.NoiseNight, 3 * P, 12 * P, "noise at night") }, "Wraith");
            Row("Predator", S("mossy", "Mossy", "{0} chance of +1 guck from a guck sack", chance: 10 * P, perLevel: 1 * P, cap: 15 * P, item: "Guck"),
                new[] { B(PetStat.Carry, 5, 20, "carry weight") }, "Abomination");
            Row("Boss", S("rot_ward", "Rot ward", "Poison ends {0} sooner", chance: 10 * P, cap: 10 * P),
                new[] { B(PetStat.ResistPhysical, 3 * P, 12 * P, "physical resistance") }, "Bonemass");

            // Mountain
            Row("Predator", S("pack_sense", "Pack sense", "Wolves are 10% less likely to aggro", phase: 2),
                new[] { B(PetStat.RunStaminaUse, 2 * P, 9 * P, "run stamina use") }, "Wolf", "Wolf_cub");
            Row("Predator", S("moon_howl", "Moon howl", "On a kill at night, +{0} stamina", chance: 5, perLevel: 0, cap: 5),
                new[] { B(PetStat.SprintSpeedNight, 2 * P, 9 * P, "speed at night") }, "Fenring");
            Row("Predator", S("shadow", "Shadow", "Seen from 5% less far in the dark", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "wolf pelt drop chance", "WolfPelt") }, "Ulv");
            Row("Flyer", S("glide", "Glide", "Slower fall while holding Jump in the air (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.ResistFrost, 5 * P, 20 * P, "frost resistance") }, "Hatchling");
            Row("Predator", S("gemcutter", "Gemcutter", "{0} chance of +1 crystal from silver or crystal deposits", chance: 5 * P, perLevel: 0.5f * P, cap: 7 * P, item: "Crystal"),
                new[] { B(PetStat.Armor, 1, 4, "armour") }, "StoneGolem");
            Row("Flyer", S("echo", "Echo", "Cave and crypt entrances show on the minimap within 30 m", phase: 2),
                new[] { B(PetStat.StaminaRegenCaves, 2 * P, 9 * P, "stamina regen in caves") }, "Bat");
            Row("Predator", S("cold_fire", "Cold fire", "No Freezing from wet + cold together"),
                new[] { B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance") }, "Fenring_Cultist");
            Row("Boss", S("wind_ward", "Wind ward", "Less wind drag sailing into the wind", phase: 2),
                new[] { B(PetStat.StaminaRegen, 2 * P, 9 * P, "stamina regen") }, "Dragon");

            // Plains
            Row("Predator", S("scrapper", "Scrapper", "{0} chance of +1 black metal scrap from Fulings", chance: 5 * P, perLevel: 0.5f * P, cap: 7 * P, item: "BlackMetalScrap"),
                new[] { B(PetStat.Luck, 1 * P, 5 * P, "Luck") }, "Goblin", "GoblinArcher");
            Row("Predator", S("battle_cry", "Battle cry", "On a kill, +{0} damage for 5 s", chance: 3 * P, cap: 3 * P),
                new[] { B(PetStat.Damage, 1 * P, 5 * P, "damage") }, "GoblinBrute");
            Row("Predator", S("ward", "Ward", "5% chance to shrug off a spell's status effect", phase: 2),
                new[] { B(PetStat.EitrRegen, 0.5f, 2, "eitr regen /s") }, "GoblinShaman");
            Row("Predator", S("sting", "Sting", "3% chance of a small bleed on hit", phase: 2),
                new[] { B(PetStat.AttackSpeed, 1 * P, 5 * P, "attack speed") }, "Deathsquito");
            Row("Grazer", S("gentle_giant", "Gentle giant", "Nearby tames get hungry 10% slower", phase: 2),
                new[] { B(PetStat.Carry, 5, 20, "carry weight") }, "Lox", "Lox_Calf");
            Row("Undead", S("sticky", "Sticky", "{0} chance of +1 tar from tar pits", chance: 10 * P, perLevel: 1 * P, cap: 15 * P, item: "Tar"),
                new[] { B(PetStat.SlowResist, 5 * P, 20 * P, "slow resistance") }, "BlobTar");
            Row("Predator", S("forager", "Forager", "{0} chance of +1 barley or flax from a harvest", chance: 5 * P, perLevel: 0.5f * P, cap: 7 * P, item: "Barley|Flax"),
                new[] { B(PetStat.Armor, 1, 4, "armour"), B(PetStat.Carry, 3, 12, "carry weight") }, "Unbjorn");
            Row("Boss", S("ashen_will", "Ashen will", "Burning on you ends {0} sooner", chance: 10 * P, cap: 10 * P),
                new[] { B(PetStat.ElementalDamage, 2 * P, 9 * P, "elemental damage") }, "GoblinKing");

            // Mistlands
            Row("Predator", S("mist_sight", "Mist sight", "Thinner mist around you", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "carapace drop chance", "Carapace") }, "Seeker");
            Row("Predator", S("chitin", "Chitin", "{0} less pierce damage taken", chance: 3 * P, perLevel: 3 * P, cap: 12 * P),
                new[] { B(PetStat.Armor, 1, 4, "armour") }, "SeekerBrute");
            Row("Predator", S("swarm", "Swarm", "5% chance a hit stuns a small creature", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "royal jelly drop chance", "RoyalJelly") }, "SeekerBrood");
            Row("Predator", S("latch", "Latch", "2% lifesteal on hits (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.HealthRegen, 0.5f, 2, "health regen /s") }, "Tick");
            Row("Flyer", S("bloat", "Bloat", "No knockback from small explosions", phase: 2),
                new[] { B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance") }, "Gjall");
            Row("Predator", S("lantern", "Lantern", "A small personal light in the mist", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "copper / iron from mining", "CopperOre", "IronOre", "Copper", "Iron") }, "Dverger", "DvergerMage", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport");
            Row("Grazer", S("hop", "Hop", "+{0} jump height", chance: 5 * P, perLevel: 5 * P, cap: 20 * P),
                new[] { B(PetStat.Luck, 1 * P, 5 * P, "Luck") }, "Hare");
            Row("Boss", S("hive_mind", "Hive mind", "Seekers 10% less likely to aggro", phase: 2),
                new[] { B(PetStat.EitrRegenPct, 2 * P, 9 * P, "eitr regen") }, "SeekerQueen");

            // Ashlands and the sea
            Row("Undead", S("bone_dry", "Bone-dry", "{0} chance of +1 charred bone", chance: 5 * P, perLevel: 0.5f * P, cap: 7 * P, item: "CharredBone"),
                new[] { B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance") }, "Charred_Melee", "Charred_Archer", "Charred_Mage", "Charred_Twitcher", "Charred_Melee_Dyrnwyn", "Charred_Melee_Fader");
            Row("Demon", S("hulk", "Hulk", "{0} less stagger taken", chance: 3 * P, perLevel: 3 * P, cap: 12 * P),
                new[] { B(PetStat.Armor, 1, 4, "armour"), B(PetStat.Damage, 1 * P, 5 * P, "damage") }, "Morgen", "Morgen_NonSleeping");
            Row("Flyer", S("wings", "Wings", "A small double jump (L5)", phase: 2, minLevel: 5),
                new[] { B(PetStat.StaminaRegen, 2 * P, 9 * P, "stamina regen") }, "FallenValkyrie");
            Row("Predator", S("lava_born", "Lava-born", "{0} less damage from lava", chance: 10 * P, cap: 10 * P),
                new[] { B(PetStat.RunSpeed, 2 * P, 9 * P, "run speed") }, "Asksvin", "Asksvin_hatchling");
            Row("Flyer", S("circling", "Circling", "Dead creatures within 30 m pinged on the minimap", phase: 2),
                new[] { B(PetStat.DropChance, 1 * P, 5 * P, "feather drop chance", "Feathers") }, "Volture");
            Row("Demon", S("cinder", "Cinder", "On a kill, {0} chance of a coal", chance: 5 * P, perLevel: 0.5f * P, cap: 7 * P, item: "Coal"),
                new[] { B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance") }, "BlobLava");
            Row("Swimmer", S("deep_breath", "Deep breath", "{0} less swim stamina", chance: 5 * P, perLevel: 5 * P, cap: 20 * P),
                new[] { B(PetStat.SwimSpeed, 1 * P, 4 * P, "swim speed") }, "Serpent", "BonemawSerpent");
            Row("Boss", S("flameheart", "Flameheart", "Warmth outdoors, no Cold (L3+)", minLevel: 3),
                new[] { B(PetStat.FireDamage, 2 * P, 9 * P, "fire damage") }, "Fader");

            // Fires ravens
            Row("Flyer", S("scout", "Scout", "Every 60 s pings the nearest point of interest within 100 m", phase: 2),
                new[] { B(PetStat.ExploreRadius, 5, 20, "map reveal radius (m)") }, "FiresRaven_Hugin");
            Row("Flyer", S("memory", "Memory", "{0} less skill loss on death", chance: 5 * P, perLevel: 5 * P, cap: 20 * P),
                new[] { B(PetStat.SkillXp, 2 * P, 9 * P, "skill XP") }, "FiresRaven_Munin");

            // Family fallback
            void Fam(string fam, PetSkill skill, params PetBuffLine[] buffs) => s_family[fam] = new PetDef { Key = "family:" + fam, Family = fam, Skill = skill, Buffs = new List<PetBuffLine>(buffs) };
            Fam("Boss", S("forsaken_echo", "Forsaken echo", "A weak copy of its Forsaken power (L5)", phase: 2, minLevel: 5), B(PetStat.Damage, 2 * P, 9 * P, "damage"));
            Fam("Flyer", S("glide", "Glide", "Slower fall while holding Jump (L5)", phase: 2, minLevel: 5), B(PetStat.StaminaRegen, 2 * P, 9 * P, "stamina regen"));
            Fam("Swimmer", S("deep_breath", "Deep breath", "{0} less swim stamina", chance: 5 * P, perLevel: 5 * P, cap: 20 * P), B(PetStat.SwimSpeed, 1 * P, 4 * P, "swim speed"));
            Fam("Undead", S("rattle", "Rattle", "Undead notice you from 5% less far per level", phase: 2), B(PetStat.ResistPoison, 5 * P, 20 * P, "poison resistance"));
            Fam("Demon", S("warmth", "Warmth", "Warmth outdoors, no Cold (L3+)", minLevel: 3), B(PetStat.ResistFire, 5 * P, 20 * P, "fire resistance"));
            Fam("Predator", S("battle_cry", "Battle cry", "On a kill, +{0} damage for 5 s", chance: 3 * P, cap: 3 * P), B(PetStat.Damage, 1 * P, 5 * P, "damage"));
            Fam("Grazer", S("forage", "Forage", "{0} chance of +1 of its main drop", chance: 1 * P, perLevel: 1 * P, cap: 5 * P), B(PetStat.Carry, 5, 20, "carry weight"));
        }

        /// <summary>The family of a creature, in the design's order: Boss → Undead → Demon → Swimmer → Flyer → Predator → Grazer.</summary>
        public static string FamilyOf(Character c)
        {
            if (c == null) return "Grazer";
            if (c.IsBoss()) return "Boss";
            switch (c.m_faction)
            {
                case Character.Faction.Undead: return "Undead";
                case Character.Faction.Demon: return "Demon";
            }
            // Swim-only: the sea monsters faction (serpents, the leviathan's kin).
            if (c.m_faction == Character.Faction.SeaMonsters) return "Swimmer";
            if (c.m_flying) return "Flyer";
            if (c.m_faction == Character.Faction.AnimalsVeg) return "Grazer";
            var humanoid = c as Humanoid;
            bool attacks = humanoid != null && (humanoid.m_defaultItems?.Length ?? 0) > 0;
            return attacks ? "Predator" : "Grazer";
        }
    }
}
