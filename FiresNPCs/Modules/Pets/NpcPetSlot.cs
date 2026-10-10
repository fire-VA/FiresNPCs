using System;
using System.Collections.Generic;
using FiresRPGmaker.UI;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// 0.0.559 (Fire 10-09, COMPANION_PET_SLOT_DESIGN §3): an RPGmaker NPC's cosmetic pet. NpcController.equipPet holds the pet
    /// item or creature name ("" = none); the ZDO owner writes it through PetApi.SetPet ("fires_pet" = creature), which the
    /// outfit redress path already reaches on the owner. Stored in the fashion JSON (Pet) so presets/saved NPCs carry it.
    /// No buffs, no drop: the pet is part of the look.
    /// </summary>
    public static class NpcPetSlot
    {
        /// <summary>Owner side: push npc.equipPet into "fires_pet" if it differs. Safe to call every redress.</summary>
        public static void WriteOnOwner(NpcController npc, string why)
        {
            if (npc == null || npc.equipPet == null) return;   // null = never set on this peer: leave "fires_pet" alone
            var ch = npc.GetComponent<Character>();
            if (ch == null) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcPet] {why}: '{npc.name}' has no Character, skipped"); return; }
            string want = npc.equipPet;
            string wantCreature = string.IsNullOrEmpty(want) ? "" : (PetCatalog.CreatureOfPrefabName(want) ?? want);
            string have = PetApi.GetPet(ch) ?? "";
            if (string.Equals(have, wantCreature, StringComparison.Ordinal)) return;
            bool ok = PetApi.SetPet(ch, want, out string reason);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcPet] {why}: '{npc.GetDisplayName()}' pet '{have}' -> '{want}' {(ok ? "set" : "FAILED: " + reason)}");
        }

        /// <summary>Every registered pet item prefab name (for the dressing-room picker), sorted by display name.</summary>
        public static List<string> AllPetItems()
        {
            PetCatalog.EnsureRegistered();
            var list = new List<string>();
            if (ObjectDB.instance == null) return list;
            foreach (var go in ObjectDB.instance.m_items)
                if (go != null && PetCatalog.CreatureOfPrefabName(go.name) != null) list.Add(go.name);
            list.Sort((a, b) => string.Compare(DisplayName(a), DisplayName(b), StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static string DisplayName(string prefab)
        {
            var go = ObjectDB.instance?.GetItemPrefab(prefab);
            string n = go?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name ?? prefab;
            return n.StartsWith("$") ? (Localization.instance?.Localize(n) ?? n) : n;
        }
    }
}
