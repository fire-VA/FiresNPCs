using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// kg territory-purchase flow (TerritorySystem_Main_Client PatchClickIconMinimap): left-clicking a
    /// ForSale territory on the LARGE map pops a buy confirmation; clicking one you own pops a release
    /// confirmation. Ownership routes through the existing server-authoritative
    /// <see cref="TerritoryOwnership.RequestGrant"/> (ForSale-gated for non-admins).
    ///
    /// One honest upgrade over kg: kg's popup NAMES a Coins price but its server never takes any money —
    /// we actually consume <c>SellPrice</c> Coins on confirm (0 = free, like kg). Release refunds
    /// nothing (kg parity).
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick")]
    internal static class TerritoryPurchase
    {
        private const string CurrencyPrefab = "Coins";

        // 0.0.533: 	erritory_buy <name> runs the same map-click path at the plot's centre, so a test (or an admin) can
        // open the Buy / Release popup without a real mouse on the large map ([visual] docs shots).
        private static Vector3? s_forcedPoint;

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class Command
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("territory_buy", "<territory name> - open the Buy/Release popup of a ForSale territory", args =>
                {
                    string name = args.Length > 1 ? string.Join(" ", args.Args, 1, args.Args.Length - 1) : "";
                    var terr = TerritorySyncedState.ClientTerritories?.FirstOrDefault(x => x != null && x.Type == TerritoryType.ForSale
                        && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (terr == null)
                    {
                        var list = TerritorySyncedState.ClientTerritories;
                        var any = list?.FirstOrDefault(x => x != null && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                        args.Context?.AddString(any != null
                            ? $"[territory_buy] '{name}' is here but not ForSale (type {any.Type}, price {any.SellPrice})"
                            : $"[territory_buy] no territory named '{name}' on this client ({list?.Count ?? 0} known)");
                        return;
                    }
                    var c = terr.Center2D();
                    s_forcedPoint = new Vector3(c.x, 0f, c.y);
                    try { if (Prefix()) args.Context?.AddString("[territory_buy] nothing to buy there"); }
                    finally { s_forcedPoint = null; }
                });
            }
        }

        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null || Minimap.instance == null) return true;
                Vector3 pos = s_forcedPoint ?? Minimap.instance.ScreenToWorldPoint(Input.mousePosition);
                var territory = TerritorySyncedState.ClientTerritories?
                    .FirstOrDefault(t => t != null && t.Type == TerritoryType.ForSale && t.IsInside2D(pos.x, pos.z));
                if (territory == null) return true;

                string myId = LocalSteamId();
                bool mine = !string.IsNullOrEmpty(myId) && TerritoryOwnership.IsOwner(territory.Name, myId);

                if (mine)
                {
                    UnifiedPopup.Push(new YesNoPopup("Territory ownership",
                        $"Release your ownership of <color=yellow>{territory.Name}</color>?",
                        () => { UnifiedPopup.Pop(); TerritoryOwnership.RequestGrant(territory.Name, false); },
                        () => UnifiedPopup.Pop()));
                    return false;
                }

                // A plot sells once: check before any Coins are taken, since the server would refuse the grant.
                if (TerritoryOwnership.OwnersOf(territory.Name).Count > 0)
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{territory.Name} is already owned.");
                    return false;
                }

                int price = Mathf.Max(0, territory.SellPrice);
                string priceText = price > 0 ? $" for <color=yellow>{price} {CurrencyPrefab}</color>" : "";
                UnifiedPopup.Push(new YesNoPopup("Territory purchase",
                    $"Buy <color=yellow>{territory.Name}</color>{priceText}?",
                    () =>
                    {
                        UnifiedPopup.Pop();
                        if (price > 0 && !TryConsumeCoins(player, price))
                        {
                            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"You need {price} {CurrencyPrefab}.");
                            return;
                        }
                        TerritoryOwnership.RequestGrant(territory.Name, true);
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Claimed {territory.Name}!");
                    },
                    () => UnifiedPopup.Pop()));
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TerritoryPurchase] map click failed: {ex.Message}");
                return true;
            }
        }

        private static string LocalSteamId()
        {
            try { return global::FiresRPGmaker.Modules.Dialogues.EwpDataBridge.GetLocalSteamId(); }
            catch { return null; }
        }

        private static bool TryConsumeCoins(Player player, int amount)
        {
            var inv = player.GetInventory();
            int held = 0;
            foreach (var it in inv.GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == CurrencyPrefab) held += it.m_stack;
            if (held < amount) return false;
            int remaining = amount;
            foreach (var it in inv.GetAllItems().ToList())
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != CurrencyPrefab) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
            return remaining == 0;
        }
    }
}
