using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Server-authoritative, GLOBAL auction house over Core's LiteDB <see cref="MarketplaceRepository"/>.
    /// All players share one market (MP's model) instead of the old per-profile in-memory pools.
    ///
    /// The server owns listing state. Sell consumes the item on the seller's client, then the server records
    /// the listing. Buy consumes currency on the buyer's client, then the server validates the listing still
    /// has stock, decrements it, PAYS THE SELLER by mail (price − tax, VIP-reduced via VipManager) — the fix
    /// for the long-standing "buyer pays, seller gets nothing" gap — and replies telling the buyer to grant the
    /// item (or refund the currency if the listing was already gone). Cancel returns the goods, owner-checked
    /// server-side. Every mutation re-broadcasts the full list so all open panels stay live.
    /// </summary>
    public static class MarketplaceNetwork
    {
        public const string LogPrefix = "[MarketNet]";

        private const string RpcReq = "FiresRPGmaker_MarketReq";
        private const string RpcRes = "FiresRPGmaker_MarketRes";

        private enum Op  : byte { GetListings = 1, List = 2, Buy = 3, Cancel = 4 }
        private enum Res : byte { Listings = 1, Grant = 2, Status = 3 }

        // Client-side hooks (open MarketplacePlayerPanel subscribes).
        public static Action OnListings;         // fired after the live-listing cache refreshes
        public static Action<string> OnResult;   // status line (buy/sell/cancel result)

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;

        // Server-side: profiles whose sample .cfg listings we've already seeded this session.
        private static readonly HashSet<string> _seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_registered && _registeredOn == current) return;

            current.Register<ZPackage>(RpcReq, new Action<long, ZPackage>(RPC_MarketReq));
            current.Register<ZPackage>(RpcRes, new Action<long, ZPackage>(RPC_MarketRes));
            _registered   = true;
            _registeredOn = current;
            MarketplaceListingManager.ClearLive();
            FiresLogger.LogVerbose($"{LogPrefix} Registered on ZRoutedRpc {current.GetHashCode()}.");
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        private static bool IsDedicated => ZNet.instance != null && ZNet.instance.IsDedicated();

        // ── Client → server sends ──────────────────────────────────────────────────────────────────────────
        public static void RequestListings(string profile)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.GetListings);
            inner.Write(profile ?? "");
            Send(inner);
        }

        public static void RequestList(string currency, int price, string prefab, int qty)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.List);
            inner.Write(currency ?? "Coins");
            inner.Write(price);
            inner.Write(prefab ?? "");
            inner.Write(qty);
            Send(inner);
        }

        public static void RequestBuy(int listingId, int qty, string currency, int totalPaid)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.Buy);
            inner.Write(listingId);
            inner.Write(qty);
            inner.Write(currency ?? "Coins");
            inner.Write(totalPaid);
            Send(inner);
        }

        public static void RequestCancel(int listingId)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.Cancel);
            inner.Write(listingId);
            Send(inner);
        }

        private static void Send(ZPackage inner)
        {
            if (ZRoutedRpc.instance == null) return;
            var local = Player.m_localPlayer;
            long playerId = local != null ? local.GetPlayerID() : 0L;
            if (playerId == 0L) return;

            var wrapped = new ZPackage();
            wrapped.Write(playerId);
            wrapped.Write(local != null ? (local.GetPlayerName() ?? "") : "");
            wrapped.Write(inner.GetArray());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcReq, wrapped);
        }

        // ── Server handler ─────────────────────────────────────────────────────────────────────────────────
        private static void RPC_MarketReq(long senderId, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            try
            {
                long playerId    = pkg.ReadLong();
                string playerName = pkg.ReadString();
                var inner = new ZPackage(pkg.ReadByteArray());
                // kg-parity owner key: platform user id, server-derived from the sender's socket
                // (account-scoped like kg — a migrated DB.db resolves without remapping; unspoofable).
                string userId = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(senderId, playerId);

                var op = (Op)inner.ReadByte();
                switch (op)
                {
                    case Op.GetListings:
                    {
                        string profile = inner.ReadString();
                        SeedSamples(profile);
                        ReplyListings(senderId);
                        break;
                    }
                    case Op.List:
                    {
                        string currency = inner.ReadString();
                        int price = inner.ReadInt();
                        string prefab = inner.ReadString();
                        int qty = inner.ReadInt();
                        ServerList(senderId, userId, playerName, currency, price, prefab, qty);
                        break;
                    }
                    case Op.Buy:
                    {
                        int listingId = inner.ReadInt();
                        int qty = inner.ReadInt();
                        string currency = inner.ReadString();
                        int totalPaid = inner.ReadInt();
                        ServerBuy(senderId, userId, playerName, listingId, qty, currency, totalPaid);
                        break;
                    }
                    case Op.Cancel:
                        ServerCancel(senderId, userId, inner.ReadInt());
                        break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_MarketReq error: {ex.Message}"); }
        }

        private static void ServerList(long senderId, string userId, string playerName, string currency, int price, string prefab, int qty)
        {
            if (qty <= 0 || price <= 0 || string.IsNullOrEmpty(prefab))
            { ReplyStatus(senderId, "Invalid listing."); return; }

            // Make the seller mail-addressable so their payout can reach them even if they never opened mail.
            try { MailRepository.UpsertUser(new MailUser { Name = playerName ?? "", UserID = userId, IsOnline = true }); } catch { }

            int id = MarketplaceRepository.AddListing(new MarketListing
            {
                ItemPrefab = prefab,
                Count = qty,
                Price = price,
                Currency = currency ?? "Coins",
                SellerName = playerName ?? "",
                SellerUserID = userId,
            });
            if (id == 0) { ReplyGrant(senderId, prefab, qty, "Listing failed — item returned."); return; }

            ReplyStatus(senderId, $"Listed {qty}x {prefab} for {price} {currency}.");
            BroadcastListings();
        }

        private static void ServerBuy(long senderId, string userId, string buyerName, int listingId, int qty, string currency, int totalPaid)
        {
            var listing = MarketplaceRepository.GetListing(listingId);
            int serverTotal = listing != null ? qty * listing.Price : 0;

            // Reject (and refund the buyer's consumed currency) if the listing is gone, understocked, or the
            // price no longer matches what the buyer paid from a stale cache.
            if (listing == null || qty <= 0 || qty > listing.Count || totalPaid != serverTotal)
            { ReplyGrant(senderId, currency, totalPaid, "Listing changed — currency refunded."); return; }

            // Decrement or clear the listing.
            if (qty >= listing.Count) MarketplaceRepository.RemoveListing(listingId);
            else { listing.Count -= qty; MarketplaceRepository.UpdateListing(listing); }

            // Pay the seller by mail: full price minus tax (VIP sellers pay the reduced rate). A seller we can't
            // resolve (admin/NPC sample listing with no UserID) simply gets no payout — the coins are a sink.
            bool sellerVip = VipManager.IsVip(listing.SellerUserID, listing.SellerName);
            float taxPct = VipManager.MarketTaxPercent(sellerVip);
            int sellerGets = Mathf.FloorToInt(serverTotal * (1f - taxPct / 100f));
            if (sellerGets > 0 && !string.IsNullOrEmpty(listing.SellerName))
            {
                MailNetwork.DeliverSystemMail(
                    listing.SellerName, "Marketplace", "Item sold",
                    $"Your listing of {qty}x {listing.ItemPrefab} sold to {buyerName} for {serverTotal} {listing.Currency}. " +
                    $"After {taxPct:0.#}% tax you received {sellerGets} {listing.Currency}.",
                    new List<StoredItem> { new StoredItem { ItemPrefab = listing.Currency, Count = sellerGets } });
            }

            ReplyGrant(senderId, listing.ItemPrefab, qty, $"Bought {qty}x {listing.ItemPrefab}.");
            BroadcastListings();
        }

        private static void ServerCancel(long senderId, string userId, int listingId)
        {
            var listing = MarketplaceRepository.GetListing(listingId);
            if (listing == null) { ReplyStatus(senderId, "Listing no longer exists."); return; }
            if (!string.Equals(listing.SellerUserID, userId, StringComparison.Ordinal))
            { ReplyStatus(senderId, "That isn't your listing."); return; }

            MarketplaceRepository.RemoveListing(listingId);
            ReplyGrant(senderId, listing.ItemPrefab, listing.Count, $"Cancelled — returned {listing.Count}x {listing.ItemPrefab}.");
            BroadcastListings();
        }

        // Seed a profile's sample .cfg listings into the global market once per session, deduped against what's
        // already there so reopening the same marketplace NPC never stacks duplicates.
        private static void SeedSamples(string profile)
        {
            if (string.IsNullOrEmpty(profile) || _seeded.Contains(profile)) return;
            _seeded.Add(profile);

            var samples = MarketplaceListingManager.GetProfileSamples(profile);
            if (samples == null || samples.Count == 0) return;
            var existing = MarketplaceRepository.GetListings();
            foreach (var s in samples)
            {
                bool dup = existing.Any(e =>
                    string.Equals(e.ItemPrefab, s.ItemPrefab, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.SellerName, s.SellerName, StringComparison.OrdinalIgnoreCase) &&
                    e.Price == s.Price);
                if (dup) continue;
                MarketplaceRepository.AddListing(new MarketListing
                {
                    ItemPrefab = s.ItemPrefab,
                    Count = s.Count,
                    Price = s.Price,
                    Currency = s.Currency ?? "Coins",
                    SellerName = s.SellerName ?? "",
                    SellerUserID = "", // sample/admin seller — no payout target
                });
            }
        }

        // ── Server → client replies ────────────────────────────────────────────────────────────────────────
        private static ZPackage BuildListingsPkg()
        {
            var all = MarketplaceRepository.GetListings();
            var pkg = new ZPackage();
            pkg.Write((byte)Res.Listings);
            pkg.Write(all.Count);
            foreach (var l in all)
            {
                pkg.Write(l._id);
                pkg.Write(l.ItemPrefab ?? "");
                pkg.Write(l.Count);
                pkg.Write(l.Price);
                pkg.Write(l.Currency ?? "Coins");
                pkg.Write(l.SellerName ?? "");
                pkg.Write(ParseId(l.SellerUserID));
            }
            return pkg;
        }

        private static void ReplyListings(long senderId) =>
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, BuildListingsPkg());

        private static void BroadcastListings()
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null) return;
            var peers = ZNet.instance.GetPeers();
            if (peers != null)
                foreach (var peer in peers)
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcRes, BuildListingsPkg());
            // The listen-server host isn't in the peer list — apply directly. Consume the leading Res tag first
            // so the package is positioned exactly where the RPC path leaves it (at the count).
            if (!IsDedicated)
            {
                var hp = BuildListingsPkg();
                hp.ReadByte();
                ApplyListingsLocal(hp);
            }
        }

        private static void ReplyGrant(long senderId, string prefab, int count, string status)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.Grant);
            pkg.Write(prefab ?? "");
            pkg.Write(count);
            pkg.Write(status ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        private static void ReplyStatus(long senderId, string status)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.Status);
            pkg.Write(status ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        // ── Client handler ─────────────────────────────────────────────────────────────────────────────────
        private static void RPC_MarketRes(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var res = (Res)pkg.ReadByte();
                switch (res)
                {
                    case Res.Listings:
                        ApplyListingsLocal(pkg);
                        break;
                    case Res.Grant:
                    {
                        string prefab = pkg.ReadString();
                        int count = pkg.ReadInt();
                        string status = pkg.ReadString();
                        GrantToInventory(prefab, count);
                        OnResult?.Invoke(status);
                        break;
                    }
                    case Res.Status:
                        OnResult?.Invoke(pkg.ReadString());
                        break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_MarketRes error: {ex.Message}"); }
        }

        // Replaces the client cache from a Listings package positioned at the count (the leading Res.Listings
        // byte is consumed by both callers before this runs).
        private static void ApplyListingsLocal(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            var list = new List<MarketplaceListing>(count);
            for (int i = 0; i < count; i++)
            {
                int id = pkg.ReadInt();
                string prefab = pkg.ReadString();
                int c = pkg.ReadInt();
                int price = pkg.ReadInt();
                string currency = pkg.ReadString();
                string seller = pkg.ReadString();
                long sellerId = pkg.ReadLong();
                list.Add(new MarketplaceListing
                {
                    Id = id, ItemPrefab = prefab, Count = c, Price = price,
                    Currency = currency, SellerName = seller, SellerId = sellerId,
                });
            }
            MarketplaceListingManager.ReplaceLive(list);
            OnListings?.Invoke();
        }

        private static int GrantToInventory(string prefab, int count)
        {
            var player = Player.m_localPlayer;
            if (player == null || string.IsNullOrEmpty(prefab) || count <= 0) return 0;
            var inv = player.GetInventory();
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop == null) return 0;
            if (inv.CanAddItem(drop.m_itemData, count)) inv.AddItem(prefab, count, 1, 0, 0L, "");
            else ItemDrop.DropItem(drop.m_itemData, count, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
            return count;
        }

        private static long ParseId(string s) => long.TryParse(s, out long v) ? v : 0L;
    }
}
