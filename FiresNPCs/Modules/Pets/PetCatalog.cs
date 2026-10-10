using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// Fire's pocket pets (Tools\FIRES_PETS_PLAN.md): every creature in the game gets a craftable "Pocket &lt;creature&gt;" item.
    /// Creatures are found at runtime, never listed by hand: any ZNetScene prefab with a Character that isn't a player or a
    /// Fires NPC. The recipe uses the trophy its own CharacterDrop drops; a creature with no trophy uses 10 of its most common
    /// drop (Fire, P2). The station follows the creature's toughness (Fire, P1: by biome tier).
    ///
    /// The pet item is a clone of the trophy item, keeping its icon, "attach" model and type, so it hangs on any trophy stand
    /// (vanilla and FAP); m_dropPrefab is set because vanilla's ItemStand.CanAttach throws without it.
    /// </summary>
    internal static class PetCatalog
    {
        internal const string ItemPrefix = "FiresPetItem_";

        private sealed class Entry
        {
            public string Creature;      // the creature's prefab name
            public GameObject Item;      // FiresPetItem_<creature>
            public GameObject Ingredient;
            public int IngredientAmount;
            public int Tier;
            public (string prefab, int amount)[] Recipe;   // an explicit recipe (the ravens), instead of ingredient + tier extras
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static GameObject _holder;

        /// <summary>The creature a pet item stands for, or null when the item isn't a pet.</summary>
        internal static string CreatureOf(ItemDrop.ItemData item)
        {
            string name = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            return CreatureOfPrefabName(name);
        }

        internal static string CreatureOfPrefabName(string prefabName) =>
            !string.IsNullOrEmpty(prefabName) && prefabName.StartsWith(ItemPrefix, StringComparison.Ordinal)
                ? prefabName.Substring(ItemPrefix.Length)
                : null;

        /// <summary>Builds the pet items and recipes once both ZNetScene and ObjectDB exist (game scene only).</summary>
        internal static void EnsureRegistered()
        {
            if (ZNetScene.instance == null || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0) return;
            try
            {
                if (Entries.Count == 0) Discover();
                foreach (var entry in Entries.Values) Register(entry);
            }
            catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogWarning("[FiresPets] registration failed: " + ex); }
        }

        private static void Discover()
        {
            if (_holder == null)
            {
                _holder = new GameObject("FiresPets_Prefabs");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }

            // A boss with phases ("FrozenKing", "FrozenKing_p2", "FrozenKing_p3") is ONE pet: the first phase's model, with
            // drops pooled from every phase (the Deep North boss drops its trophy only from the last one).
            List<GameObject> creatures = ZNetScene.instance.m_prefabs
                .Where(p => p != null && p.GetComponent<Character>() != null && p.GetComponent<Player>() == null && !IsFiresNpc(p))
                .ToList();
            var families = creatures.GroupBy(p => PhaseBase(p.name)).ToDictionary(g => g.Key, g => g.OrderBy(p => PhaseNumber(p.name)).ToList());

            int built = 0, skipped = 0, merged = 0;
            foreach (GameObject prefab in creatures)
            {
                List<GameObject> family = families[PhaseBase(prefab.name)];
                if (family[0] != prefab) { merged++; continue; }
                var character = prefab.GetComponent<Character>();
                if (prefab.GetComponentInChildren<Renderer>(true) == null) { skipped++; continue; }

                var allDrops = family.Select(p => p.GetComponent<CharacterDrop>()).Where(d => d != null).SelectMany(d => d.m_drops).ToList();
                GameObject trophy = allDrops.Where(d => d.m_prefab != null && d.m_prefab.name.StartsWith("Trophy", StringComparison.Ordinal))
                    .Select(d => d.m_prefab).FirstOrDefault();
                GameObject common = allDrops.Where(d => d.m_prefab != null && d.m_prefab.GetComponent<ItemDrop>() != null)
                    .OrderByDescending(d => d.m_chance).Select(d => d.m_prefab).FirstOrDefault();
                GameObject template = trophy ?? common;
                if (template == null || template.GetComponent<ItemDrop>() == null) { skipped++; continue; }

                var entry = new Entry
                {
                    Creature = prefab.name,
                    Ingredient = template,
                    IngredientAmount = trophy != null ? 1 : 10,
                    Tier = TierOf(character),
                };
                entry.Item = BuildItem(entry, template, character);
                Entries[entry.Creature] = entry;
                built++;
            }
            built += DiscoverRavens();
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {built} pocket pet(s) built from the creatures in ZNetScene ({skipped} skipped: no model or no drop to craft from; {merged} later boss phases merged into their first phase).");
        }

        /// <summary>
        /// Hugin and Munin (Fire 10-08): Core's networked ravens drop nothing, so they get their own entries and Fire's recipes at
        /// the workbench; the item is a clone of Feathers.
        /// </summary>
        private static int DiscoverRavens()
        {
            var ravens = new[]
            {
                (prefab: FiresCore.Ravens.RavenPrefabs.Hugin, recipe: new[] { ("Feathers", 10), ("Coal", 5), ("Resin", 5) }),
                (prefab: FiresCore.Ravens.RavenPrefabs.Munin, recipe: new[] { ("Feathers", 10), ("Coal", 5), ("Honey", 5) }),
            };
            GameObject feathers = ObjectDB.instance.GetItemPrefab("Feathers");
            int built = 0;
            foreach (var raven in ravens)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(raven.prefab);
                Character character = prefab != null ? prefab.GetComponent<Character>() : null;
                if (character == null || feathers == null || Entries.ContainsKey(raven.prefab)) continue;
                var entry = new Entry { Creature = raven.prefab, Ingredient = feathers, IngredientAmount = 10, Tier = 1, Recipe = raven.recipe };
                entry.Item = BuildItem(entry, feathers, character);
                Entries[entry.Creature] = entry;
                built++;
            }
            return built;
        }

        private static readonly System.Text.RegularExpressions.Regex Phase = new System.Text.RegularExpressions.Regex(@"^(.+)_p(\d+)$");

        /// <summary>"FrozenKing_p2" -> "FrozenKing"; a name with no phase suffix is its own base.</summary>
        private static string PhaseBase(string name)
        {
            var m = Phase.Match(name);
            return m.Success ? m.Groups[1].Value : name;
        }

        /// <summary>"FrozenKing" -> 1 (the unsuffixed prefab is the first phase), "FrozenKing_p2" -> 2.</summary>
        private static int PhaseNumber(string name)
        {
            var m = Phase.Match(name);
            return m.Success && int.TryParse(m.Groups[2].Value, out int n) ? n : 1;
        }

        private static bool IsFiresNpc(GameObject prefab)
        {
            foreach (var c in prefab.GetComponents<MonoBehaviour>())
            {
                string ns = c != null ? c.GetType().Namespace ?? "" : "";
                if (ns.StartsWith("FiresCore", StringComparison.Ordinal) || ns.StartsWith("FiresRPGmaker", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>1 Workbench, 2 Forge, 3 Artisan table, 4 Black forge; bosses by their place in the game.</summary>
        private static int TierOf(Character character)
        {
            if (character.IsBoss())
            {
                switch (character.name)
                {
                    case "Eikthyr": return 1;
                    case "gd_king": case "Bonemass": return 2;
                    case "Dragon": case "GoblinKing": return 3;
                    default: return 4;
                }
            }
            float hp = character.m_health;
            return hp <= 60f ? 1 : hp <= 250f ? 2 : hp <= 900f ? 3 : 4;
        }

        private const float StandModelScale = 0.35f;

        private static void AddAttach(GameObject item, string creature)
        {
            Renderer body = null;
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && (body == null || r.bounds.size.sqrMagnitude > body.bounds.size.sqrMagnitude)) body = r;
            if (body == null)
            {
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] pet item for {creature}: no model to hang on a stand");
                return;
            }
            Mesh mesh = body is SkinnedMeshRenderer smr ? smr.sharedMesh : body.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            // a fresh object with just the mesh and its materials: copying the renderer's object could copy the whole item
            var attach = new GameObject("attach");
            attach.transform.SetParent(item.transform, false);
            attach.transform.localRotation = Quaternion.Inverse(item.transform.rotation) * body.transform.rotation;
            attach.transform.localScale = body.transform.lossyScale;
            attach.AddComponent<MeshFilter>().sharedMesh = mesh;
            attach.AddComponent<MeshRenderer>().sharedMaterials = body.sharedMaterials;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] pet item for {creature}: no trophy 'attach' model, made one from '{body.name}' so it hangs on a stand");
        }   // the item's "attach" model on an item stand

        private static GameObject BuildItem(Entry entry, GameObject template, Character character)
        {
            var item = UnityEngine.Object.Instantiate(template, _holder.transform, false);
            item.name = ItemPrefix + entry.Creature;
            var drop = item.GetComponent<ItemDrop>();
            var shared = drop.m_itemData.m_shared;
            string creatureName = CreatureName(character);
            shared.m_name = "Pocket " + creatureName;
            shared.m_description = "A pocket-sized " + creatureName + ". Put it in your Pet slot (or use it) and it follows you; "
                + "hang it on an item stand and it stays there. It can't fight, and it can't be hurt.";
            shared.m_maxStackSize = 1;
            shared.m_weight = 0.2f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            // Every pet item is a Trophy-type item, so trophy item stands accept it (vanilla ItemStand.CanAttach checks
            // m_supportedTypes). A creature with no trophy cloned a plain drop (the ravens: Feathers = Material), so the stand
            // said "can't attach" even with an attach model (batch 10-09: "handled True, attach child True", no attachment).
            shared.m_itemType = ItemDrop.ItemData.ItemType.Trophy;
            drop.m_itemData.m_dropPrefab = item;
            // A creature with no trophy (the ravens, 10-09: "attach child False") clones a plain drop that has no "attach"
            // model, so vanilla's ItemStand can't hang it and the item stays in the bag. Give such an item one: a copy of its
            // own visible model (renderers + meshes only), which the stand then shows.
            if (item.transform.Find("attach") == null) AddAttach(item, entry.Creature);
            // Fire 10-08 (A): a pet item hangs on a stand at 35 % of the trophy's size; real trophies stay vanilla.
            foreach (Transform child in item.transform)
                if (child.name.StartsWith("attach", StringComparison.Ordinal)) child.localScale *= StandModelScale;
            return item;
        }

        /// <summary>
        /// The creature's shown name. A name key the game has no text for comes back as "[key]" (TheHive's read
        /// "Pocket [enemy_hive]", round 7); then the prefab name in words ("TheHive" -> "The Hive").
        /// </summary>
        private static string CreatureName(Character character)
        {
            string name = Localization.instance != null ? Localization.instance.Localize(character.m_name) : character.m_name;
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("[", StringComparison.Ordinal) && !name.StartsWith("$", StringComparison.Ordinal))
                return name;
            string raw = character.name.Replace("(Clone)", "").Replace('_', ' ').Trim();
            var words = new System.Text.StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(raw[i - 1])) words.Append(' ');
                words.Append(i == 0 || raw[i - 1] == ' ' ? char.ToUpperInvariant(c) : c);
            }
            return words.ToString();
        }

        /// <summary>Pets only an admin can make: their recipe needs <see cref="AdminIngredient"/>, which can't be got in play.</summary>
        private static readonly HashSet<string> AdminOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TheHive" };

        /// <summary>Vanilla's dev sword: no recipe, no drop, only `spawn SwordCheat` with devcommands.</summary>
        private const string AdminIngredient = "SwordCheat";

        private static readonly string[] StationByTier ={ "piece_workbench", "forge", "piece_artisanstation", "blackforge" };

        private static readonly (string prefab, int amount)[][] ExtrasByTier =
        {
            new[] { ("Wood", 5), ("LeatherScraps", 2) },
            new[] { ("Bronze", 1), ("DeerHide", 2) },
            new[] { ("Silver", 1), ("LoxPelt", 1) },
            new[] { ("BlackMetal", 1), ("Eitr", 1) },
        };

        private static void Register(Entry entry)
        {
            var db = ObjectDB.instance;
            if (db.GetItemPrefab(entry.Item.name) == null)
            {
                db.m_items.Add(entry.Item);
                db.m_itemByHash[entry.Item.name.GetStableHashCode()] = entry.Item;
            }
            var scene = ZNetScene.instance;
            int hash = entry.Item.name.GetStableHashCode();
            if (!scene.m_namedPrefabs.ContainsKey(hash))
            {
                scene.m_prefabs.Add(entry.Item);
                scene.m_namedPrefabs[hash] = entry.Item;
            }

            string recipeName = "Recipe_" + entry.Item.name;
            if (db.m_recipes.Any(r => r != null && r.name == recipeName)) return;

            var station = scene.GetPrefab(StationByTier[entry.Tier - 1])?.GetComponent<CraftingStation>();
            var resources = new List<Piece.Requirement>
            {
                new Piece.Requirement { m_resItem = entry.Ingredient.GetComponent<ItemDrop>(), m_amount = entry.IngredientAmount, m_recover = true },
            };
            if (entry.Recipe != null)
            {
                resources.Clear();
                foreach (var (prefab, amount) in entry.Recipe)
                {
                    var part = db.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
                    if (part != null) resources.Add(new Piece.Requirement { m_resItem = part, m_amount = amount, m_recover = true });
                }
            }
            else foreach (var (prefab, amount) in ExtrasByTier[entry.Tier - 1])
            {
                var extra = db.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
                if (extra != null) resources.Add(new Piece.Requirement { m_resItem = extra, m_amount = amount, m_recover = true });
            }
            // Admin-only pets (Fire 10-08: "the hive doesnt actually need to be a pet ... make its crafting recipe require a
            // cheat item so its only ever spawnable by admins"): the only ingredient is vanilla's SwordCheat, which nothing
            // crafts or drops (spawn it with devcommands). Without it in ObjectDB, the recipe is left disabled.
            bool adminOnly = AdminOnly.Contains(entry.Creature);
            if (adminOnly)
            {
                var cheat = db.GetItemPrefab(AdminIngredient)?.GetComponent<ItemDrop>();
                resources.Clear();
                if (cheat != null) resources.Add(new Piece.Requirement { m_resItem = cheat, m_amount = 1, m_recover = false });
            }
            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = recipeName;
            recipe.m_item = entry.Item.GetComponent<ItemDrop>();
            recipe.m_amount = 1;
            recipe.m_craftingStation = station;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = resources.ToArray();
            recipe.m_enabled = !adminOnly || resources.Count > 0;
            db.m_recipes.Add(recipe);
            if (adminOnly)
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {entry.Item.name}: admin-only recipe, "
                    + (resources.Count > 0 ? $"needs 1 {AdminIngredient}" : $"no {AdminIngredient} in ObjectDB, recipe disabled"));
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDBAwake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => EnsureRegistered();
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetSceneAwake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => EnsureRegistered();
        }
    }
}
