using System;
using System.Reflection;
using FiresCore.Variants;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// The pet contract with VAInventory (FIRES_PETS_PLAN §10b). VAInventory calls IsPet to validate its Pet slot and may
    /// call OnPetSlotChanged as a hint; FiresNPCs still pulls the slot itself (Slots.GetPetSlotItem / Slots.PetSlotActive),
    /// so a missed hint changes nothing. Without VAInventory, using a pet item from the inventory summons / dismisses it.
    /// </summary>
    public static class PetApi
    {
        public static bool IsPet(ItemDrop.ItemData item) => PetCatalog.CreatureOf(item) != null;

        // ================= owner API (Tools\COMPANION_PET_SLOT_DESIGN.md, frozen 10-09) =================
        // Any Character with a ZNetView can own a pet: players, companions, NPCs. The owner's ZDO keeps the existing keys
        // ("fires_pet" = creature prefab, "fires_pet_variant"); every client's PetManager reads them and builds the visual.

        /// <summary>Raised on the client that set it: (owner, creature or "").</summary>
        public static event Action<Character, string> PetChanged;

        /// <summary>
        /// Give an owner a pet (a pet ITEM prefab like "FiresPetItem_Boar", or a creature prefab like "Boar"), or clear it
        /// with null / "". Must run on the client that owns the owner's ZDO (companions: their owner's client; NPCs: through
        /// the outfit RPC). True on success, else false with a reason.
        /// </summary>
        public static bool SetPet(Character owner, string itemOrCreature, out string reason)
        {
            reason = null;
            ZNetView nview = owner != null ? owner.m_nview : null;
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) { reason = "no owner ZDO"; return false; }
            if (!nview.IsOwner()) { reason = "not the owner of that character's ZDO"; return false; }
            string creature = ResolveCreature(itemOrCreature, out reason);
            if (creature == null) return false;
            if (zdo.GetString(ZdoKeyPet) != creature) zdo.Set(ZdoKeyPet, creature);
            if (creature.Length == 0 && zdo.GetString(VariantTable.KeyPetVariantHash).Length > 0) zdo.Set(VariantTable.KeyPetVariantHash, "");
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] SetPet {owner.m_name}: '{(creature.Length > 0 ? creature : "none")}'");
            PetManager.RequestRefresh();
            PetChanged?.Invoke(owner, creature);
            return true;
        }

        /// <summary>The owner's pet creature prefab, or "" (read from its ZDO, so it works on every client).</summary>
        public static string GetPet(Character owner)
        {
            ZDO zdo = owner != null && owner.m_nview != null ? owner.m_nview.GetZDO() : null;
            return zdo?.GetString(ZdoKeyPet) ?? "";
        }

        /// <summary>Hide the owner's pet visual on THIS client only (the ZDO is untouched; it shows again when the pet changes).</summary>
        public static void Dismiss(Character owner, string why) => PetManager.DismissLocal(owner, why);

        internal const string ZdoKeyPet = "fires_pet";
        private const string ItemPrefix = "FiresPetItem_";

        private static string ResolveCreature(string itemOrCreature, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(itemOrCreature)) return "";
            string creature = itemOrCreature.StartsWith(ItemPrefix, StringComparison.Ordinal) ? itemOrCreature.Substring(ItemPrefix.Length) : itemOrCreature;
            if (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(ItemPrefix + creature) == null)
            {
                reason = $"'{itemOrCreature}' is not a pet";
                return null;
            }
            return creature;
        }

        /// <summary>
        /// Core's PetBridge (Core 0.2.38x) lets mods without a reference to RPGmaker (FiresCompanions) call SetPet / GetPet.
        /// Filled by reflection, so this builds and runs against an older Core too (then there is simply no bridge).
        /// </summary>
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterBridge
        {
            private static void Postfix()
            {
                Type bridge = AccessTools.TypeByName("FiresCore.Bridge.PetBridge");
                if (bridge == null) return;
                Func<Character, string, string> set = (owner, pet) => SetPet(owner, pet, out string why) ? null : why;
                Func<Character, string> get = GetPet;
                Func<ItemDrop.ItemData, bool> isPet = IsPet;   // the companion Pet cell validates drops with it
                bool ok = Assign(bridge, "SetPet", set) & Assign(bridge, "GetPet", get) & Assign(bridge, "IsPetItem", isPet);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] Core PetBridge {(ok ? "registered" : "found but not assignable")}");
            }

            private static bool Assign(Type t, string name, object value)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType.IsInstanceOfType(value)) { f.SetValue(null, value); return true; }
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value)) { p.SetValue(null, value); return true; }
                return false;
            }
        }

        public static void OnPetSlotChanged(Player player, ItemDrop.ItemData item)
        {
            if (player != null && player == Player.m_localPlayer) PetManager.RequestRefresh();
        }

        private static bool _probed;
        private static MethodInfo _getPetSlotItem;
        private static PropertyInfo _petSlotActiveProp;
        private static FieldInfo _petSlotActiveField;

        /// <summary>True when VAInventory's Pet slot exists; then the slot, not the inventory "use", decides.</summary>
        internal static bool HasPetSlot
        {
            get { Probe(); return _getPetSlotItem != null; }
        }

        /// <summary>The creature in the local player's Pet slot, or null (empty, inactive or no VAInventory).</summary>
        internal static string SlotCreature()
        {
            Probe();
            if (_getPetSlotItem == null) return null;
            try
            {
                object active = _petSlotActiveProp != null ? _petSlotActiveProp.GetValue(null)
                    : _petSlotActiveField != null ? _petSlotActiveField.GetValue(null) : true;
                if (active is bool b && !b) return null;
                return PetCatalog.CreatureOf(_getPetSlotItem.Invoke(null, null) as ItemDrop.ItemData);
            }
            catch { return null; }
        }

        /// <summary>The item in the local player's Pet slot (null when empty or without VAInventory).</summary>
        internal static ItemDrop.ItemData SlotItem()
        {
            Probe();
            if (_getPetSlotItem == null) return null;
            try { return _getPetSlotItem.Invoke(null, null) as ItemDrop.ItemData; }
            catch { return null; }
        }

        /// <summary>
        /// Using an essence (Fire 10-08) recolours the pet you have out: its ITEM carries the variant, so it stays through the
        /// item stand, trade, death and relog. Aimed at something (a mount, a nest) it's left to vanilla, so FiresMounts' own
        /// use-on-mount path gets it. Consumes one essence; Clarity removes the variant.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class EssenceUse
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
            {
                if (!(__instance is Player player) || player != Player.m_localPlayer) return true;
                if (!FiresCore.Variants.VariantEssences.IsEssence(item, out FiresCore.Variants.VariantDef def)) return true;
                GameObject hover = player.GetHoverObject();
                if (!fromInventoryGui && hover != null && hover.GetComponentInParent<Interactable>() != null) return true;

                ItemDrop.ItemData pet = PetManager.LocalPetItem(player);
                if (pet == null)
                {
                    player.Message(MessageHud.MessageType.Center, "Take out a pet first, or use it on your mount");
                    return false;
                }
                string current = FiresCore.Variants.VariantTable.OfItem(pet);
                string next = def.IsClear ? "" : def.Id;
                string petName = Localization.instance != null ? Localization.instance.Localize(pet.m_shared.m_name) : pet.m_shared.m_name;
                if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
                {
                    player.Message(MessageHud.MessageType.Center, def.IsClear ? petName + " has no essence" : petName + " is already " + def.DisplayName);
                    return false;
                }
                FiresCore.Variants.VariantTable.SetOnItem(pet, next);
                player.GetInventory().RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, def.IsClear ? petName + " is itself again" : petName + " is now " + def.DisplayName);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] essence {item.m_dropPrefab?.name} on {pet.m_dropPrefab?.name}: "
                    + $"variant '{(current.Length > 0 ? current : "none")}' -> '{(next.Length > 0 ? next : "none")}'");
                PetManager.RequestRefresh();
                return false;
            }
        }

        /// <summary>The tooltip names a pet item's essence ("Essence: Verdant").</summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class VariantTooltip
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (item == null || PetCatalog.CreatureOf(item) == null) return;
                if (!FiresCore.Variants.VariantTable.TryGet(FiresCore.Variants.VariantTable.OfItem(item), out FiresCore.Variants.VariantDef def)) return;
                __result += "\nEssence: <color=orange>" + def.DisplayName + "</color>";
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            Type slots = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((slots = asm.GetType("VerdantsAscent.Slots", false)) != null) break;
            if (slots == null) return;
            // Plain reflection: AccessTools logs a warning for every member it can't find, and VAInventory may not have them yet.
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            _getPetSlotItem = slots.GetMethod("GetPetSlotItem", Any, null, Type.EmptyTypes, null);
            _petSlotActiveProp = slots.GetProperty("PetSlotActive", Any);
            if (_petSlotActiveProp == null) _petSlotActiveField = slots.GetField("PetSlotActive", Any);
            _tryEquipPet = slots.GetMethod("TryEquipPet", Any, null, new[] { typeof(ItemDrop.ItemData) }, null);
        }

        private static MethodInfo _tryEquipPet;

        /// <summary>
        /// VAInventory moves this item from the bag into its Pet slot (swapping any pet already there back to the bag).
        /// Null when this VAInventory has no such method yet; false when it refused (no room).
        /// </summary>
        private static bool? TryEquipIntoSlot(ItemDrop.ItemData item)
        {
            Probe();
            if (_tryEquipPet == null) return null;
            try { return _tryEquipPet.Invoke(null, new object[] { item }) is bool ok && ok; }
            catch (Exception e)
            {
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] VAInventory TryEquipPet threw: {e.InnerException?.Message ?? e.Message}");
                return false;
            }
        }

        internal const string CustomDataKey = "fires_pet";

        /// <summary>
        /// Using a pet item equips it (Fire 10-07: "we should be able to equip from inventory"):
        /// - with VAInventory's Pet slot, it moves INTO the slot (VAInventory's TryEquipPet), which is "equipped";
        /// - without VAInventory, it's marked equipped in the bag (vanilla's m_equipped, the equipped tint) and follows; using it
        ///   again unequips it and it goes home. One pet at a time.
        /// A hotbar use while aiming at an item stand stays vanilla's: it hangs the item.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class UseItemFallback
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
            {
                string creature = PetCatalog.CreatureOf(item);
                if (creature == null || !(__instance is Player player) || player != Player.m_localPlayer) return true;
                GameObject hover = player.GetHoverObject();
                bool aimed = hover != null && hover.GetComponentInParent<Interactable>() != null;
                bool inBag = player.GetInventory().ContainsItem(item);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] use {item.m_dropPrefab?.name}: fromInventoryGui {fromInventoryGui}, "
                    + $"hover '{(hover != null ? hover.name : "none")}' interactable {aimed}, in the player's inventory {inBag}");
                if (!fromInventoryGui && aimed) return true;

                if (HasPetSlot)
                {
                    // An item already in the Pet slot isn't in the bag: VAInventory's own right-click returns it.
                    if (!inBag) return true;
                    bool? moved = TryEquipIntoSlot(item);
                    if (moved == true)
                    {
                        player.Message(MessageHud.MessageType.Center, item.m_shared.m_name + " follows you");
                        PetManager.RequestRefresh();
                    }
                    else player.Message(MessageHud.MessageType.Center, moved == null ? "Put it in your Pet slot" : "Couldn't put it in your Pet slot");
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] equip {item.m_dropPrefab?.name} into the Pet slot: "
                        + (moved == null ? "VAInventory has no TryEquipPet" : moved == true ? "moved" : "refused"));
                    return false;
                }

                bool unequip = item.m_equipped;
                foreach (ItemDrop.ItemData other in player.GetInventory().GetAllItems())
                    if (other.m_equipped && PetCatalog.CreatureOf(other) != null) other.m_equipped = false;
                if (unequip) player.m_customData.Remove(CustomDataKey);
                else
                {
                    item.m_equipped = true;
                    player.m_customData[CustomDataKey] = creature;
                }
                player.GetInventory().Changed();
                player.Message(MessageHud.MessageType.Center, unequip ? item.m_shared.m_name + " goes home" : item.m_shared.m_name + " follows you");
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {(unequip ? "unequipped" : "equipped")} {item.m_dropPrefab?.name} in the bag (no Pet slot)");
                PetManager.RequestRefresh();
                return false;
            }
        }

        /// <summary>
        /// An equipped pet item (no-VAInventory path) must go to the gravestone like everything else: vanilla's
        /// MoveInventoryToGrave keeps m_equipped items on the player, and UnequipAllItems only clears real gear.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static class PetGoesToTheGrave
        {
            private static void Prefix(Player __instance)
            {
                Inventory bag = __instance != null ? __instance.GetInventory() : null;
                if (bag == null) return;
                foreach (ItemDrop.ItemData item in bag.GetAllItems())
                    if (item.m_equipped && PetCatalog.CreatureOf(item) != null) item.m_equipped = false;
            }
        }

        /// <summary>
        /// Evidence for taking a pet item back off a stand (round 15: E on the stand left it there, no exception): whether
        /// vanilla's ItemStand.Interact ran, with which hold / alt, and whether the stand still holds the item afterwards.
        /// Only for stands holding a pet item.
        /// </summary>
        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
        private static class StandInteractLog
        {
            private static void Prefix(ItemStand __instance, out int __state) =>
                __state = __instance != null && __instance.HaveAttachment() ? __instance.GetAttachedItem() : 0;

            private static void Postfix(ItemStand __instance, bool hold, bool alt, bool __result, int __state)
            {
                if (__state == 0) return;
                string item = ObjectDB.instance?.GetItemPrefab(__state)?.name;
                if (PetCatalog.CreatureOfPrefabName(item) == null) return;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand '{__instance.name}' Interact {item}: hold {hold}, alt {alt}, "
                    + $"returned {__result}, can be removed {__instance.m_canBeRemoved}, owner {__instance.m_nview?.IsOwner()}, still holds it {__instance.HaveAttachment()}");
            }
        }

        /// <summary>Evidence for the stand path: whether vanilla's ItemStand took a pet item.</summary>
        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
        private static class StandUseItemLog
        {
            private static void Postfix(ItemStand __instance, ItemDrop.ItemData item, bool __result)
            {
                if (PetCatalog.CreatureOf(item) == null) return;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand '{__instance.name}' UseItem {item.m_dropPrefab?.name}: "
                    + $"handled {__result}, had an item {__instance.HaveAttachment()}, attach child {ItemStand.GetAttachPrefab(item.m_dropPrefab) != null}");
            }
        }
    }
}
