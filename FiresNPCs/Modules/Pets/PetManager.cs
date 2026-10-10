using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using FiresCore.Variants;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// Owns every pet on this client (FIRES_PETS_PLAN §9b / §9c). The local player writes its pet to its own player ZDO
    /// ("fires_pet" = creature, "fires_pet_emote" = counter * 16 + emote); every client reads those keys for every player,
    /// and reads item stands directly, then builds or removes local visual copies. Nothing else is networked.
    /// </summary>
    internal sealed class PetManager : MonoBehaviour
    {
        private const string ZdoPet = "fires_pet";
        private const string ZdoEmote = "fires_pet_emote";
        private const float CullDistance = 40f;
        private const int DrawCap = 16;

        private static PetManager _instance;
        private static readonly List<ItemStand> Stands = new List<ItemStand>();
        private readonly HashSet<int> _unknownStandItems = new HashSet<int>();   // stands already logged as holding an item ObjectDB doesn't know

        private readonly Dictionary<Object, PetVisual> _pets = new Dictionary<Object, PetVisual>();
        private float _nextRefresh;
        private float _nextCombatEmote;
        private int _emoteCounter;

        // owners whose pet visual this client hid (PetApi.Dismiss), with the creature hidden
        private readonly Dictionary<Character, string> _dismissed = new Dictionary<Character, string>();

        internal static void DismissLocal(Character owner, string why)
        {
            if (owner == null) return;
            Ensure();
            string creature = PetApi.GetPet(owner);
            if (creature.Length == 0) return;
            _instance._dismissed[owner] = creature;
            _instance.Remove(owner);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {owner.m_name}'s pet {creature} dismissed here ({why})");
        }

        internal static void RequestRefresh()
        {
            if (_instance != null) _instance._nextRefresh = 0f;
        }

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresPets_Manager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PetManager>();
        }

        private void Update()
        {
            Player local = Player.m_localPlayer;
            if (local == null || ZNetScene.instance == null) { Clear(); return; }

            UpdateCombatEmote(local);
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + 0.5f;

            WriteLocal(local);
            Refresh(local.transform.position);
        }

        /// <summary>What the local player has out: the VAInventory Pet slot, else the "used" item while it's still carried.</summary>
        private static string LocalCreature(Player local)
        {
            if (PetApi.HasPetSlot) return PetApi.SlotCreature();
            // Without VAInventory: the pet item marked equipped in the bag. Vanilla's EquipInventoryItems clears m_equipped on
            // load (a trophy-type item can't be "equipped" its way), so the creature kept in m_customData re-marks it.
            ItemDrop.ItemData equipped = local.GetInventory().GetAllItems().FirstOrDefault(i => i.m_equipped && PetCatalog.CreatureOf(i) != null);
            if (equipped != null) return PetCatalog.CreatureOf(equipped);
            if (!local.m_customData.TryGetValue(PetApi.CustomDataKey, out string creature)) return null;
            ItemDrop.ItemData carried = local.GetInventory().GetAllItems().FirstOrDefault(i => PetCatalog.CreatureOf(i) == creature);
            if (carried == null)
            {
                local.m_customData.Remove(PetApi.CustomDataKey);
                return null;
            }
            carried.m_equipped = true;
            local.GetInventory().Changed();
            return creature;
        }

        private static void WriteLocal(Player local)
        {
            ZDO zdo = local.m_nview != null ? local.m_nview.GetZDO() : null;
            if (zdo == null || !local.m_nview.IsOwner()) return;
            string creature = LocalCreature(local) ?? "";
            if (zdo.GetString(ZdoPet) != creature) zdo.Set(ZdoPet, creature);
            string variant = creature.Length > 0 ? VariantTable.OfItem(LocalPetItem(local)) : "";
            if (zdo.GetString(VariantTable.KeyPetVariantHash) != variant) zdo.Set(VariantTable.KeyPetVariantHash, variant);
        }

        /// <summary>The ITEM of the pet the local player has out (the Pet slot's, else the bag pet marked equipped), or null.</summary>
        internal static ItemDrop.ItemData LocalPetItem(Player local)
        {
            if (local == null) return null;
            if (PetApi.HasPetSlot)
            {
                ItemDrop.ItemData slot = PetApi.SlotItem();
                return PetCatalog.CreatureOf(slot) != null ? slot : null;
            }
            return local.GetInventory().GetAllItems().FirstOrDefault(i => i.m_equipped && PetCatalog.CreatureOf(i) != null);
        }

        private readonly Dictionary<ItemStand, (uint revision, string variant)> _standVariants = new Dictionary<ItemStand, (uint, string)>();

        /// <summary>
        /// The variant on a stand's pet item. Vanilla's ItemStand saves the whole item into its ZDO (UpdateAttach ->
        /// ItemDrop.SaveToZDO(item, zdo, -1)), custom data included, so it is read back the same way; cached per ZDO revision.
        /// </summary>
        private string StandVariant(ItemStand stand, GameObject itemPrefab)
        {
            ZDO zdo = stand.m_nview != null ? stand.m_nview.GetZDO() : null;
            ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (zdo == null || drop == null) return "";
            if (_standVariants.TryGetValue(stand, out var cached) && cached.revision == zdo.DataRevision) return cached.variant;
            string variant = "";
            try
            {
                ItemDrop.ItemData copy = drop.m_itemData.Clone();
                copy.m_customData.Clear();
                ItemDrop.LoadFromZDO(copy, zdo, -1);
                variant = VariantTable.OfItem(copy);
            }
            catch (System.Exception e)
            {
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand '{stand.name}': reading its item's variant failed: {e.Message}");
            }
            _standVariants[stand] = (zdo.DataRevision, variant);
            return variant;
        }

        /// <summary>The last moment the owner was attacking, so a swing between two checks still counts as fighting.</summary>
        private float _lastOwnerAttack = float.NegativeInfinity;

        private const float FightMemorySeconds = 2f;
        private const float NewFightGapSeconds = 10f;

        /// <summary>
        /// While the owner fights, its pet "joins in" every 5-9 s (harmless; it only animates). "Fighting" means attacking
        /// within the last 2 s: a swing is short, and gating on InAttack() at the exact frame the cooldown ended missed most
        /// of them (pad round 4, The Elder). The first swing of a new fight plays one at once.
        /// </summary>
        private void UpdateCombatEmote(Player local)
        {
            if (local.InAttack())
            {
                if (Time.time - _lastOwnerAttack > NewFightGapSeconds) _nextCombatEmote = 0f;
                _lastOwnerAttack = Time.time;
            }
            if (Time.time < _nextCombatEmote || Time.time - _lastOwnerAttack > FightMemorySeconds) return;
            ZDO zdo = local.m_nview != null ? local.m_nview.GetZDO() : null;
            if (zdo == null || !local.m_nview.IsOwner() || string.IsNullOrEmpty(zdo.GetString(ZdoPet))) return;
            _nextCombatEmote = Time.time + Random.Range(5f, 9f);
            _emoteCounter = (_emoteCounter + 1) & 0x7FFFFFF;
            zdo.Set(ZdoEmote, _emoteCounter * 16 + Random.Range(0, 16));
        }

        private void Refresh(Vector3 center)
        {
            var wanted = new List<(Object key, Transform anchor, string creature, bool leashed, float dist, string variant)>();
            // Any Character with a ZNetView owns a pet through its ZDO: players, companions and NPCs (pet-slot design, 10-09).
            // The DrawCap / CullDistance below still keep only the nearest few.
            foreach (Character c in Character.GetAllCharacters())
            {
                ZDO zdo = c != null && c.m_nview != null ? c.m_nview.GetZDO() : null;
                string creature = zdo?.GetString(ZdoPet);
                if (string.IsNullOrEmpty(creature)) continue;
                if (_dismissed.TryGetValue(c, out string hidden))
                {
                    if (hidden == creature) continue;   // dismissed here; shows again once the pet changes
                    _dismissed.Remove(c);
                }
                wanted.Add((c, c.transform, creature, false, Vector3.Distance(center, c.transform.position),
                    zdo.GetString(VariantTable.KeyPetVariantHash)));
            }
            Stands.RemoveAll(s => s == null);
            foreach (ItemStand stand in Stands)
            {
                if (!stand.HaveAttachment()) continue;
                GameObject itemPrefab = ObjectDB.instance?.GetItemPrefab(stand.GetAttachedItem());
                string creature = PetCatalog.CreatureOfPrefabName(itemPrefab?.name);
                if (creature == null)
                {
                    if (itemPrefab == null && _unknownStandItems.Add(stand.GetInstanceID()))
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand '{stand.name}' holds item {stand.GetAttachedItem()}: no ObjectDB prefab, no pet");
                    continue;
                }
                float dist = Vector3.Distance(center, stand.transform.position);
                wanted.Add((stand, stand.transform, creature, true, dist, dist <= CullDistance ? StandVariant(stand, itemPrefab) : ""));
            }
            foreach (ItemStand gone in _standVariants.Keys.Where(s => s == null).ToList()) _standVariants.Remove(gone);

            var keep = wanted.Where(w => w.dist <= CullDistance).OrderBy(w => w.dist).Take(DrawCap).ToList();
            var keepKeys = new HashSet<Object>(keep.Select(w => w.key));
            foreach (var key in _pets.Keys.ToList())
            {
                PetVisual pet = _pets[key];
                if (key == null || pet == null || !keepKeys.Contains(key)) { Remove(key); continue; }
            }
            foreach (var w in keep)
            {
                if (_pets.TryGetValue(w.key, out PetVisual pet) && pet != null && pet.Creature == w.creature)
                {
                    pet.SetVariant(w.variant);
                    if (w.key is Player p) PlayOwnerEmote(p, pet);
                    continue;
                }
                Remove(w.key);
                PetVisual built = PetVisual.Build(w.creature, w.anchor, w.leashed);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug(built == null
                    ? $"[FiresPets] FiresPet_{w.creature}: build FAILED ({(w.leashed ? "stand" : "follower")} of '{w.anchor.name}')"
                    : $"[FiresPets] {built.name} built: {(w.leashed ? "stand" : "follower")} of '{w.anchor.name}' at {w.anchor.position}, pet at {built.transform.position}");
                if (built != null)
                {
                    built.SetVariant(w.variant);
                    _pets[w.key] = built;
                    if (w.key is Player p) built.LastEmoteCounter = p.m_nview.GetZDO().GetInt(ZdoEmote) / 16;
                }
            }
        }

        private static void PlayOwnerEmote(Player owner, PetVisual pet)
        {
            int packed = owner.m_nview.GetZDO().GetInt(ZdoEmote);
            int counter = packed / 16;
            if (counter == pet.LastEmoteCounter) return;
            pet.LastEmoteCounter = counter;
            pet.PlayEmote(packed % 16, combat: true, owner.transform.position + owner.GetLookDir() * 4f);
        }

        private void Remove(Object key)
        {
            if (_pets.TryGetValue(key, out PetVisual pet) && pet != null)
            {
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {pet.name} removed ({(pet.Leashed ? "stand" : "follower")} of '{(key != null ? key.name : "gone")}')");
                Destroy(pet.gameObject);
            }
            _pets.Remove(key);
        }

        private void Clear()
        {
            if (_pets.Count == 0) return;
            foreach (var pet in _pets.Values) if (pet != null) Destroy(pet.gameObject);
            _pets.Clear();
        }

        [HarmonyPatch(typeof(ItemStand), "Awake")]
        private static class ItemStandAwake
        {
            private static void Postfix(ItemStand __instance) => Stands.Add(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawned
        {
            private static void Postfix() => Ensure();
        }
    }
}
