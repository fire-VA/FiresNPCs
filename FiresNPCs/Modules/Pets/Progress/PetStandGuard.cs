using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>[wishbone] 10-09 (FDT 583, both ravens): hanging a pet item on an item stand left it in the bag too (a dupe on
    /// take-back). Vanilla's ItemStand.UpdateAttach writes the stand's ZDO and then calls RemoveOneItem(m_queuedItem); the bag
    /// count stayed the same (21 → 21; VAInventory's validation only ran at the later pick-up). Around UpdateAttach this logs the
    /// queued item's bag position and count, and if the stand took a PET item while the bag count didn't drop, it removes one
    /// itself. Pet items only; every other item stays vanilla.</summary>
    [HarmonyPatch(typeof(ItemStand), "UpdateAttach")]
    internal static class PetStandGuard
    {
        private static readonly AccessTools.FieldRef<ItemStand, ItemDrop.ItemData> s_queued = AccessTools.FieldRefAccess<ItemStand, ItemDrop.ItemData>("m_queuedItem");

        private struct State { public ItemDrop.ItemData Item; public string Prefab; public int Count; public bool Had; public bool Contained; }

        [HarmonyPrefix]
        private static void Prefix(ItemStand __instance, out State __state)
        {
            __state = default;
            var item = s_queued(__instance);
            var player = Player.m_localPlayer;
            if (item?.m_dropPrefab == null || player == null || PetCatalog.CreatureOf(item) == null) return;
            var inv = player.GetInventory();
            __state = new State { Item = item, Prefab = item.m_dropPrefab.name, Count = PetProgress.CountInBag(inv, item.m_dropPrefab.name), Had = __instance.HaveAttachment(), Contained = inv.ContainsItem(item) };
        }

        [HarmonyPostfix]
        private static void Postfix(ItemStand __instance, State __state)
        {
            if (__state.Item == null) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            var inv = player.GetInventory();
            bool hung = !__state.Had && __instance.HaveAttachment();
            if (!hung && __instance.m_nview != null && !__instance.m_nview.IsOwner()) return;   // still waiting for ownership: no log spam
            int after = PetProgress.CountInBag(inv, __state.Prefab);
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand attach {__state.Prefab}: owner {__instance.m_nview?.IsOwner()}, in bag before {__state.Contained} at {__state.Item.m_gridPos.x},{__state.Item.m_gridPos.y} "
                + $"(stack {__state.Item.m_stack}, equipped {__state.Item.m_equipped}), hung {hung}, bag count {__state.Count} -> {after}");
            if (!hung || after < __state.Count || after == 0) return;
            // Vanilla's RemoveOneItem didn't take: take the queued item (or another of the same prefab) out ourselves.
            if (inv.ContainsItem(__state.Item)) inv.RemoveOneItem(__state.Item);
            else
                foreach (var it in inv.GetAllItems())
                    if (it?.m_dropPrefab != null && it.m_dropPrefab.name == __state.Prefab) { inv.RemoveOneItem(it); break; }
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] stand attach {__state.Prefab}: vanilla left it in the bag; removed it (bag count now {PetProgress.CountInBag(inv, __state.Prefab)})");
        }
    }
}
