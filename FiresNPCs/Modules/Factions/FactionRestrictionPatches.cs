using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Faction item/build/craft restrictions (whitelist for build+craft, blacklist for pickup).
    /// These gate the LOCAL player's own actions, so the whole type is client-only (in Ascend's dedi
    /// exclusion set — one branch touches InventoryGui). Each check fast-paths on the union set, so
    /// unrestricted items/pieces (the overwhelming majority) pay only a hash-set lookup.
    /// </summary>
    internal static class FactionRestrictionPatches
    {
        private static FactionData Local() => FactionMembership.LocalFaction();

        private static void Deny(string msg)
        {
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg); } catch { }
        }

        // Blacklist: your faction forbids picking this item up.
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.CanPickup))]
        private static class ItemDrop_CanPickup_Faction
        {
            private static bool Prefix(ItemDrop __instance, ref bool __result)
            {
                try
                {
                    var prefab = __instance != null ? __instance.m_itemData?.m_dropPrefab : null;
                    if (prefab == null) return true;
                    if (FactionManager.PickupBlocked(prefab.name.GetStableHashCode(), Local()))
                    {
                        __result = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // Whitelist: only members of a faction that lists this piece may build it.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Player_TryPlacePiece_Faction
        {
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                try
                {
                    if (__instance != Player.m_localPlayer || piece == null) return true;
                    int hash = piece.gameObject.name.Replace("(Clone)", "").GetStableHashCode();
                    if (!FactionManager.BuildAllowed(hash, Local()))
                    {
                        Deny("Your faction can't build that.");
                        __result = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // Whitelist: only members of a faction that lists this recipe's item may craft it.
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting_Faction
        {
            private static bool Prefix(InventoryGui __instance)
            {
                try
                {
                    var item = __instance != null && __instance.m_craftRecipe != null ? __instance.m_craftRecipe.m_item : null;
                    if (item == null) return true;
                    int hash = item.gameObject.name.Replace("(Clone)", "").GetStableHashCode();
                    if (!FactionManager.CraftAllowed(hash, Local()))
                    {
                        Deny("Your faction can't craft that.");
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // Whitelist: only members of a faction that lists this item may equip/use it (MP SpecificUseItems).
        // EquipItem is the single choke for weapons/armor/tools/shields — the core of MP's use-gating.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Humanoid_EquipItem_Faction
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
            {
                try
                {
                    if (__instance != Player.m_localPlayer || item?.m_dropPrefab == null) return true;
                    if (!FactionManager.UseAllowed(item.m_dropPrefab.name.GetStableHashCode(), Local()))
                    {
                        Deny("Your faction can't use that.");
                        __result = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }
    }
}
