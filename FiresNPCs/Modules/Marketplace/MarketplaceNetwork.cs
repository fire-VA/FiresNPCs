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
        private enum Res : byte { Listings = 1, Grant = 2, Status = 3, Settle = 4, PayFromBag = 5 }

        // Security (audit 10-23): the server never holds the seller's item or the buyer's coins (Valheim inventories
        // are client-side), so it must never tell a client to "give back" an amount the CLIENT reported. Each List/Buy
        // carries a client request id (trailing field; absent from old clients = 0); a refusal answers Res.Settle with
        // that id and the client returns exactly what IT consumed for that request, from its own ledger below.
        private static int _nextReqId;
        private sealed class PendingList
        {
            public string Prefab, CrafterName, CustomData; public int Qty, Quality, Variant; public byte DurabilityPct; public long CrafterId;
            public float At = Time.realtimeSinceStartup;
        }
        private sealed class PendingBuy { public string Currency; public int Paid; public int ListingId, Qty; public bool BagTaken = true; public float At = Time.realtimeSinceStartup; }
        private static readonly Dictionary<int, PendingList> _pendingLists = new Dictionary<int, PendingList>();
        private static readonly Dictionary<int, PendingBuy> _pendingBuys = new Dictionary<int, PendingBuy>();

        // Buy payment (Fire 10-23): bank first, else bag. PayState rides the Buy packet after the request id.
        private const byte PayStateBagPaid = 0;   // the client already took the coins from its bag (old clients, today's panel)
        private const byte PayStateNotPaid = 1;   // the client took nothing yet: bank first, else the server asks for the bag
        private const float BuyMinIntervalSec = 0.5f;   // per-sender buy rate limit
        private static readonly Dictionary<long, float> _lastBuyAt = new Dictionary<long, float>();
        private const int MaxBagBuysPerHour = 50;          // per-sender cap on bag-paid (unverifiable) buys
        private const float BagBuyWindowSec = 3600f;       // one real hour
        private static readonly Dictionary<long, Queue<float>> _bagBuys = new Dictionary<long, Queue<float>>();
        private const float PendingMaxAgeSec = 60f;        // client: an unanswered record that holds nothing (bank-first buy) is dropped after this
        private const float PendingHeldMaxAgeSec = 1800f;  // a record still holding a listed item or bag coins waits much longer for its answer

        // Server: drop per-sender entries that no longer limit anything (age-based, so disconnected senders vanish too).
        private static void PruneBuyMaps(float now)
        {
            foreach (var k in _lastBuyAt.Where(kv => now - kv.Value >= BuyMinIntervalSec).Select(kv => kv.Key).ToList()) _lastBuyAt.Remove(k);
            foreach (var k in _bagBuys.Where(kv => kv.Value.Count == 0 || now - kv.Value.Last() >= BagBuyWindowSec).Select(kv => kv.Key).ToList()) _bagBuys.Remove(k);
        }

        // Client: drop pending records the server never answered. A listed item or bag coins are only returned through the record,
        // so a record that holds them waits PendingHeldMaxAgeSec (and its drop is logged); a bank-first buy holds nothing.
        private static void PrunePending()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var kv in _pendingLists.Where(kv => now - kv.Value.At > PendingHeldMaxAgeSec).ToList())
            {
                Debug.LogWarning($"[Marketplace] no answer for listing {kv.Value.Prefab} x{kv.Value.Qty} after {PendingHeldMaxAgeSec:0} s; its return record is dropped");
                _pendingLists.Remove(kv.Key);
            }
            foreach (var kv in _pendingBuys.Where(kv => now - kv.Value.At > (kv.Value.BagTaken ? PendingHeldMaxAgeSec : PendingMaxAgeSec)).ToList())
            {
                if (kv.Value.BagTaken) Debug.LogWarning($"[Marketplace] no answer for buy {kv.Value.ListingId} ({kv.Value.Paid} {kv.Value.Currency} from the bag) after {PendingHeldMaxAgeSec:0} s; its refund record is dropped");
                _pendingBuys.Remove(kv.Key);
            }
        }

        // Client: Send() bails without a spawned local player; don't record a pending entry then.
        private static bool CanSend => ZRoutedRpc.instance != null && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() != 0L;

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

        public static void RequestList(string currency, int price, string prefab, int qty,
            int quality = 1, int variant = 0, byte durabilityPct = 100, string crafterName = "", long crafterId = 0L, string customData = "{}")
        {
            if (!CanSend) return;   // Send() would bail: record nothing
            PrunePending();
            int reqId = ++_nextReqId;
            _pendingLists[reqId] = new PendingList
            {
                Prefab = prefab ?? "", Qty = qty, Quality = quality, Variant = variant, DurabilityPct = durabilityPct,
                CrafterName = crafterName ?? "", CrafterId = crafterId, CustomData = customData ?? "{}",
            };
            var inner = new ZPackage();
            inner.Write((byte)Op.List);
            inner.Write(currency ?? "Coins");
            inner.Write(price);
            inner.Write(prefab ?? "");
            inner.Write(qty);
            inner.Write(quality);
            inner.Write(variant);
            inner.Write(durabilityPct);
            inner.Write(crafterName ?? "");
            inner.Write(crafterId);
            inner.Write(customData ?? "{}");
            inner.Write(reqId);   // trailing: the server echoes it in Res.Settle
            Send(inner);
        }

        public static void RequestBuy(int listingId, int qty, string currency, int totalPaid)
        {
            if (!CanSend) return;   // Send() would bail: record nothing
            PrunePending();
            int reqId = ++_nextReqId;
            _pendingBuys[reqId] = new PendingBuy { Currency = currency ?? "Coins", Paid = totalPaid, ListingId = listingId, Qty = qty };
            var inner = new ZPackage();
            inner.Write((byte)Op.Buy);
            inner.Write(listingId);
            inner.Write(qty);
            inner.Write(currency ?? "Coins");
            inner.Write(totalPaid);
            inner.Write(reqId);   // trailing: the server echoes it in Res.Settle
            Send(inner);
        }

        /// <summary>Bank-first buy: takes NOTHING from the bag up front. The server charges the buyer's bank; if the bank
        /// can't cover it the server answers PayFromBag and this client pays from its bag then (RequestBuy path).</summary>
        public static void RequestBuyBankFirst(int listingId, int qty, string currency, int totalCost)
        {
            if (!CanSend) return;   // Send() would bail: record nothing
            PrunePending();
            int reqId = ++_nextReqId;
            _pendingBuys[reqId] = new PendingBuy { Currency = currency ?? "Coins", Paid = totalCost, ListingId = listingId, Qty = qty, BagTaken = false };
            var inner = new ZPackage();
            inner.Write((byte)Op.Buy);
            inner.Write(listingId);
            inner.Write(qty);
            inner.Write(currency ?? "Coins");
            inner.Write(totalCost);
            inner.Write(reqId);
            inner.Write(PayStateNotPaid);
            Send(inner);
        }

        // Bag payment for a PayFromBag answer: all or nothing, from this client's own inventory.
        private static bool TakeFromBag(string prefab, int amount)
        {
            var player = Player.m_localPlayer;
            if (player == null || amount <= 0) return false;
            var inv = player.GetInventory();
            int held = 0;
            foreach (var it in inv.GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == prefab) held += it.m_stack;
            if (held < amount) return false;
            int remaining = amount;
            foreach (var it in inv.GetAllItems().ToList())
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != prefab) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
            return remaining == 0;
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
            ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcReq), RpcReq, wrapped);
        }

        // ── Server handler ─────────────────────────────────────────────────────────────────────────────────
        private static void RPC_MarketReq(long senderId, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            // Same guarantee as the banker: the shared Core vault must be configured before any listing read/write,
            // even if an earlier server-init step threw before VaultOfKnowledge configured it (idempotent no-op).
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.EnsureVaultConfigured();
            try { ServerExpireTick(); } catch (Exception ex) { Debug.LogWarning($"{LogPrefix} expiry check failed: {ex.Message}"); }   // escrow expiry, on access (throttled)
            try
            {
                long playerId    = pkg.ReadLong();
                pkg.ReadString();   // client-sent name: ignored (spoofable); the name comes from the sender's peer
                string playerName = EconomySenderGuard.SenderName(senderId);
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
                        int quality = inner.ReadInt();
                        int variant = inner.ReadInt();
                        byte durabilityPct = inner.ReadByte();
                        string crafterName = inner.ReadString();
                        long crafterId = inner.ReadLong();
                        string customData = inner.ReadString();
                        int reqId = ReadOptionalInt(inner);
                        ServerList(senderId, reqId, userId, playerName, currency, price, prefab, qty,
                            quality, variant, durabilityPct, crafterName, crafterId, customData);
                        break;
                    }
                    case Op.Buy:
                    {
                        int listingId = inner.ReadInt();
                        int qty = inner.ReadInt();
                        string currency = inner.ReadString();
                        int totalPaid = inner.ReadInt();
                        int reqId = ReadOptionalInt(inner);
                        // Trailing pay state (absent = the bag was already paid, as old clients do).
                        byte payState = inner.GetPos() < inner.Size() ? inner.ReadByte() : PayStateBagPaid;
                        ServerBuy(senderId, reqId, payState, userId, playerName, listingId, qty, currency, totalPaid);
                        break;
                    }
                    case Op.Cancel:
                        ServerCancel(senderId, userId, inner.ReadInt());
                        break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_MarketReq error: {ex.Message}"); }
        }

        private static void ServerList(long senderId, int reqId, string userId, string playerName, string currency, int price, string prefab, int qty,
            int quality, int variant, byte durabilityPct, string crafterName, long crafterId, string customData)
        {
            // The client consumed the item before sending; the server cannot see that inventory. So a listing is
            // only recorded when every field is something a real item could carry (no impossible stacks, qualities
            // or payloads), and a refusal never grants anything from the packet: Res.Settle tells the client to
            // return what IT consumed for this request id (quality/durability intact, from its own ledger).
            string bad = ValidateListing(prefab, qty, price, quality, variant, durabilityPct, crafterName, customData);
            if (bad != null)
            { RefuseList(senderId, reqId, prefab, $"Invalid listing ({bad}) — item returned.", bad); return; }

            // MarketPlace.cfg enforcement (the admin tab's knobs, kg key names).
            if (MarketplaceGlobals.IsBlockedPlayer(userId, playerName))
            { RefuseList(senderId, reqId, prefab, "You are blocked from the marketplace — item returned.", "blocked player"); return; }
            if (MarketplaceGlobals.IsBlockedPrefab(prefab))
            { RefuseList(senderId, reqId, prefab, $"{prefab} cannot be sold on the marketplace — item returned.", "blocked prefab"); return; }
            int slotLimit = MarketplaceGlobals.ItemMarketLimit;
            if (slotLimit > 0)
            {
                int mine = 0;
                try { foreach (var l in MarketplaceRepository.GetListings()) if (string.Equals(l.SellerUserID, userId, StringComparison.Ordinal)) mine++; } catch { }
                if (mine >= slotLimit)
                { RefuseList(senderId, reqId, prefab, $"Listing limit reached ({mine}/{slotLimit}) — item returned.", "listing limit"); return; }
            }
            // kg currency rule: an off-list currency is coerced to the first allowed one, never rejected.
            currency = MarketplaceGlobals.CoerceCurrency(currency);

            // Make the seller mail-addressable so their payout can reach them even if they never opened mail.
            try { MailRepository.UpsertUser(new MailUser { Name = playerName ?? "", UserID = userId, IsOnline = true }); } catch { }

            int id = MarketplaceRepository.AddListing(new MarketListing
            {
                ItemPrefab = prefab,
                Count = qty,
                Price = price,
                Currency = currency,
                SellerName = playerName ?? "",
                SellerUserID = userId,
                Quality = Mathf.Max(1, quality),
                Variant = variant,
                DurabilityPercent = durabilityPct,
                CrafterName = crafterName ?? "",
                CrafterID = crafterId,
                CUSTOMdata = string.IsNullOrEmpty(customData) ? "{}" : customData,
                TimeStamp = NowUnix(),   // escrow: listed-at, drives expiry
            });
            if (id == 0) { RefuseList(senderId, reqId, prefab, "Listing failed — item returned.", "storage write failed"); return; }
            global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} LIST listing {id}: {qty}x {prefab} (q{quality} v{variant} d{durabilityPct}%) seller {userId} for {price} {currency} — held in escrow");

            // kg fires the marketplace Discord webhook on LIST (not on sale) — DiscordSettings.cfg template.
            DiscordWebhooks.PostMarketplaceList(playerName ?? "", qty, prefab, price, currency);

            ReplySettle(senderId, reqId, false, $"Listed {qty}x {prefab} for {price} {currency}.");
            BroadcastListings();
        }

        // Null = acceptable; otherwise the reason. Bounds come from the item's own ObjectDB data.
        private static string ValidateListing(string prefab, int qty, int price, int quality, int variant, byte durabilityPct, string crafterName, string customData)
        {
            if (qty <= 0 || price <= 0 || string.IsNullOrEmpty(prefab)) return "qty/price/prefab";
            var shared = EconomySenderGuard.ItemShared(prefab);
            if (shared == null) return "unknown prefab";
            int maxQty = Mathf.Max(1, shared.m_maxStackSize) * EconomySenderGuard.MaxListingStacks;
            if (qty > maxQty) return $"qty {qty} > {maxQty}";
            if (quality < 1 || quality > Mathf.Max(1, shared.m_maxQuality)) return $"quality {quality}";
            if (variant < 0 || variant > Mathf.Max(0, shared.m_variants - 1)) return $"variant {variant}";
            if (durabilityPct > EconomySenderGuard.MaxDurabilityPct) return $"durability {durabilityPct}";
            if ((crafterName ?? "").Length > EconomySenderGuard.MaxCrafterNameChars) return "crafter name length";
            if (!string.IsNullOrEmpty(customData) && customData != "{}")
            {
                if (customData.Length > EconomySenderGuard.MaxCustomDataChars) return "customData length";
                try { if (Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(customData) == null) return "customData"; }
                catch { return "customData"; }
            }
            return null;
        }

        private static void RefuseList(long senderId, int reqId, string prefab, string status, string reason)
        {
            Debug.LogWarning($"{LogPrefix} List refused for sender {senderId}: {reason} (prefab '{prefab}', req {reqId})");
            ReplySettle(senderId, reqId, true, status);
        }

        private static void RefuseBuy(long senderId, int reqId, int listingId, string status, string reason)
        {
            Debug.LogWarning($"{LogPrefix} Buy refused for sender {senderId}: {reason} (listing {listingId}, req {reqId})");
            ReplySettle(senderId, reqId, true, status);
        }

        // Trailing optional field: old clients don't send it (0 = no request id, nothing to settle).
        private static int ReadOptionalInt(ZPackage p)
        {
            try { return p.GetPos() < p.Size() ? p.ReadInt() : 0; } catch { return 0; }
        }

        private static void ServerBuy(long senderId, int reqId, byte payState, string userId, string buyerName, int listingId, int qty, string currency, int totalPaid)
        {
            // The client consumed the currency before sending; the server never sees it. A refusal therefore never
            // grants the packet's totalPaid (that minted coins): Res.Settle tells the client to return what IT
            // consumed for this request id. Blocked players are shut out of the whole market (kg semantics).
            if (MarketplaceGlobals.IsBlockedPlayer(userId, buyerName))
            { RefuseBuy(senderId, reqId, listingId, "You are blocked from the marketplace — currency refunded.", "blocked player"); return; }

            // Rate limit per sender on COMPLETED buys (bag payments are unverifiable, so bound how fast they can land).
            // Stamped only when a buy completes, so a bank-short request + its bag resend aren't refused as "too fast".
            float now = Time.realtimeSinceStartup;
            if (_lastBuyAt.TryGetValue(senderId, out float last) && now - last < BuyMinIntervalSec)
            { RefuseBuy(senderId, reqId, listingId, "Too fast — currency refunded.", "rate limited"); return; }

            var listing = MarketplaceRepository.GetListing(listingId);
            long serverTotalL = listing != null ? (long)qty * listing.Price : 0L;   // price ALWAYS from the server's listing
            if (serverTotalL > int.MaxValue || serverTotalL < 0)
            { RefuseBuy(senderId, reqId, listingId, "Listing changed — currency refunded.", "total out of range"); return; }
            int serverTotal = (int)serverTotalL;

            // Reject if the listing is gone, understocked, or the price/currency no longer matches the buyer's cache.
            if (listing == null || qty <= 0 || qty > listing.Count || totalPaid != serverTotal
                || !string.Equals(currency ?? "", listing.Currency ?? "", StringComparison.Ordinal))
            { RefuseBuy(senderId, reqId, listingId, "Listing changed — currency refunded.", "listing changed / price mismatch"); return; }

            // Payment: BANK first (server-held, all or nothing, the Banker's account key = userId), else BAG.
            // Old clients (reqId 0) can't be told about a bank charge, so they stay on the bag path as before.
            bool paidByBank = false;
            if (reqId != 0 && serverTotal > 0)
            {
                int hash = listing.Currency.GetStableHashCode();
                int got = BankRepository.Withdraw(userId, hash, serverTotal);
                if (got >= serverTotal) paidByBank = true;
                else if (got > 0) BankRepository.Deposit(userId, hash, got);   // partial = put it back
            }
            if (!paidByBank && payState == PayStateNotPaid)
            {
                // The client hasn't touched its bag yet: ask it to pay from the bag and resend (price = the server's).
                ReplyPayFromBag(senderId, reqId, serverTotal);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"{LogPrefix} buy listing {listingId} x{qty} by sender {senderId}: bank short, asked bag for {serverTotal} {listing.Currency}");
                return;
            }
            // Bag-paid buys are unverifiable (the seller's payout is minted from serverTotal until escrow): cap them per
            // sender per real hour, server-side.
            if (!paidByBank)
            {
                if (!_bagBuys.TryGetValue(senderId, out var times)) { times = new Queue<float>(); _bagBuys[senderId] = times; }
                while (times.Count > 0 && now - times.Peek() >= BagBuyWindowSec) times.Dequeue();
                if (times.Count >= MaxBagBuysPerHour)
                { RefuseBuy(senderId, reqId, listingId, "Too many bag purchases this hour — bank your coins with the Banker.", $"bag-buy cap {MaxBagBuysPerHour}/h"); return; }
                times.Enqueue(now);
            }
            PruneBuyMaps(now);
            _lastBuyAt[senderId] = now;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"{LogPrefix} buy listing {listingId} x{qty} for {serverTotal} {listing.Currency} by sender {senderId} path={(paidByBank ? "bank" : "bag")}");

            // Escrow (0.0.763): the sold part leaves escrow in ONE guarded server step (re-read + decrement/remove).
            // If the listing vanished meanwhile (cancel/expiry), undo the bank charge and refuse — never sold AND returned.
            if (!TryTakeFromEscrow(listingId, qty, out listing))
            {
                if (paidByBank) BankRepository.Deposit(userId, currency.GetStableHashCode(), serverTotal);
                RefuseBuy(senderId, reqId, listingId, "Listing changed — currency refunded.", "escrow gone before sale");
                return;
            }
            global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} SALE listing {listingId}: {qty}x {listing.ItemPrefab} (q{listing.Quality}) seller {listing.SellerUserID} -> sender {senderId} for {serverTotal} {listing.Currency} path={(paidByBank ? "bank" : "bag")}");

            // Pay the seller by mail: full price minus tax (VIP sellers pay the reduced rate). A seller we can't
            // resolve (admin/NPC sample listing with no UserID) simply gets no payout — the coins are a sink.
            bool sellerVip = VipManager.IsVip(listing.SellerUserID, listing.SellerName);
            float taxPct = VipManager.MarketTaxPercent(sellerVip);
            int sellerGets = Mathf.FloorToInt(serverTotal * (1f - taxPct / 100f));
            // Paid by the seller's server-recorded owner key, never by name (anyone can name a character after a seller).
            if (sellerGets > 0)
            {
                string body = $"Your listing of {qty}x {listing.ItemPrefab} sold to {buyerName} for {serverTotal} {listing.Currency}. " +
                              $"After {taxPct:0.#}% tax you received {sellerGets} {listing.Currency}.";
                var pay = new List<StoredItem> { new StoredItem { ItemPrefab = listing.Currency, Count = sellerGets } };
                if (!string.IsNullOrEmpty(listing.SellerUserID))
                    MailNetwork.DeliverSystemMailToOwner(listing.SellerUserID, "Marketplace", "Item sold", body, pay);
                else if (!string.IsNullOrEmpty(listing.SellerName))
                    // Older/migrated rows carry no owner key: fall back to the name (ambiguous names deliver to nobody).
                    MailNetwork.DeliverSystemMail(listing.SellerName, "Marketplace", "Item sold", body, pay);
            }

            // Bank path: the client gets back the bag coins it pre-paid (if any) — no double payment. Bag path: kept.
            ReplySettle(senderId, reqId, paidByBank && payState == PayStateBagPaid, "", paidByBank);
            ReplyGrant(senderId, listing.ItemPrefab, qty, $"Bought {qty}x {listing.ItemPrefab}{(paidByBank ? " (paid from your bank)" : "")}.",
                listing.Quality, listing.Variant, listing.DurabilityPercent, listing.CrafterName, listing.CrafterID, listing.CUSTOMdata);
            BroadcastListings();
        }

        private static void ServerCancel(long senderId, string userId, int listingId)
        {
            var listing = MarketplaceRepository.GetListing(listingId);
            if (listing == null) { ReplyStatus(senderId, "Listing no longer exists."); return; }
            if (!string.Equals(listing.SellerUserID, userId, StringComparison.Ordinal))
            {
                // kg RemoveItemAdmin parity: an admin may remove ANY listing. Hard delete — the goods are
                // NOT granted to the admin and NOT mailed back (kg's exact cleanup semantics; the point is
                // pulling exploited/junk listings, so returning duped items would defeat it).
                if (global::FiresCore.Sync.AdminSyncing.IsAdmin(senderId))
                {
                    MarketplaceRepository.RemoveListing(listingId);
                    ReplyStatus(senderId, $"Removed {listing.SellerName}'s listing of {listing.Count}x {listing.ItemPrefab} (admin).");
                    BroadcastListings();
                    return;
                }
                ReplyStatus(senderId, "That isn't your listing.");
                return;
            }

            // Escrow (0.0.763): the server is the only holder; the item goes back by MAIL to the seller's account,
            // never granted from anything the client says. One guarded take, so it can't also be sold.
            if (!TryTakeFromEscrow(listingId, int.MaxValue, out var taken))
            { Debug.LogWarning($"{LogPrefix} Cancel refused for sender {senderId}: listing {listingId} already gone"); ReplyStatus(senderId, "Listing no longer exists."); return; }
            bool mailed;
            try { mailed = ReturnEscrowByMail(taken, "Listing cancelled", "You cancelled your marketplace listing; the item is returned here."); }
            catch (Exception ex) { mailed = false; Debug.LogWarning($"{LogPrefix} cancel mail for listing {listingId} threw: {ex.Message}"); }
            if (!mailed)
            {
                // The mail never landed: the item goes back into escrow, so it is never lost
                PutBackInEscrow(listingId, taken);
                Debug.LogWarning($"{LogPrefix} CANCEL listing {listingId} undone: the return mail failed, {taken.Count}x {taken.ItemPrefab} stays listed");
                ReplyStatus(senderId, "Couldn't cancel right now (mail failed); the listing is unchanged.");
                return;
            }
            global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} CANCEL listing {listingId}: {taken.Count}x {taken.ItemPrefab} (q{taken.Quality}) seller {taken.SellerUserID} mailed");
            ReplyStatus(senderId, $"Cancelled — {taken.Count}x {taken.ItemPrefab} sent to your mailbox.");
            BroadcastListings();
        }

        // Server tick for expiry when nobody opens the market (ServerExpireTick throttles itself to ExpiryCheckIntervalSec).
        [HarmonyLib.HarmonyPatch(typeof(ZNet), "Update")]
        private static class ZNet_Update_Expiry
        {
            private static void Postfix()
            {
                if (!IsServer) return;
                try { ServerExpireTick(); }
                catch (Exception ex) { Debug.LogWarning($"{LogPrefix} expiry tick failed: {ex.Message}"); }
            }
        }

        // ── Escrow helpers (0.0.763) ─────────────────────────────────────────────────────────────────────────
        // The listing ROW is the escrow record: it already persists every ItemData field (prefab, Count, Quality,
        // Variant, DurabilityPercent, CrafterName/ID, CUSTOMdata) in Core's vault; TimeStamp = listed-at (unix s, UTC).

        /// <summary>Listing lifetime in real days (server-side; bound by the plugin: [Marketplace] ListingLifetimeDays). ≤0 = never expire.</summary>
        public static BepInEx.Configuration.ConfigEntry<int> ListingLifetimeDaysCfg;
        private const int DefaultListingLifetimeDays = 7;
        private const float ExpiryCheckIntervalSec = 60f;
        private const long SecondsPerDay = 86400L;
        private static float _nextExpiryCheck;

        private static int ListingLifetimeDays => ListingLifetimeDaysCfg != null ? ListingLifetimeDaysCfg.Value : DefaultListingLifetimeDays;
        private static uint NowUnix() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // THE single state change for an escrowed listing: re-read, then decrement (partial sale) or remove. Runs on the
        // server main thread (RPC + tick), so sale / cancel / expiry can never both take the same units.
        private static bool TryTakeFromEscrow(int listingId, int qty, out MarketListing taken)
        {
            taken = null;
            var fresh = MarketplaceRepository.GetListing(listingId);
            if (fresh == null || fresh.Count <= 0) return false;
            int take = Mathf.Min(qty, fresh.Count);
            if (qty != int.MaxValue && qty > fresh.Count) return false;   // a sale must not take more than is held
            if (take >= fresh.Count) MarketplaceRepository.RemoveListing(listingId);
            else { fresh.Count -= take; MarketplaceRepository.UpdateListing(fresh); }
            taken = fresh;
            taken.Count = take;   // the snapshot describes exactly what left escrow
            return true;
        }

        // Undo a take whose follow-up failed: the units go back into the listing (or the row comes back), never lost.
        private static void PutBackInEscrow(int listingId, MarketListing taken)
        {
            if (taken == null || taken.Count <= 0) return;
            var still = MarketplaceRepository.GetListing(listingId);
            if (still != null) { still.Count += taken.Count; MarketplaceRepository.UpdateListing(still); }
            else MarketplaceRepository.AddListing(taken);
        }

        // Mail the escrowed item back to its seller (account key; legacy rows without one: by unique name).
        private static bool ReturnEscrowByMail(MarketListing l, string subject, string body)
        {
            var item = new StoredItem
            {
                ItemPrefab = l.ItemPrefab, Count = l.Count, Quality = Mathf.Max(1, l.Quality), Variant = l.Variant,
                DurabilityPercent = l.DurabilityPercent, CrafterName = l.CrafterName ?? "", CrafterID = l.CrafterID,
                CUSTOMdata = string.IsNullOrEmpty(l.CUSTOMdata) ? "{}" : l.CUSTOMdata,
            };
            var att = new List<StoredItem> { item };
            if (!string.IsNullOrEmpty(l.SellerUserID)) return MailNetwork.DeliverSystemMailToOwner(l.SellerUserID, "Marketplace", subject, body, att);
            if (!string.IsNullOrEmpty(l.SellerName)) return MailNetwork.DeliverSystemMail(l.SellerName, "Marketplace", subject, body, att);
            return false;
        }

        // Server: expire listings older than the lifetime (throttled; called on every market request and the server tick).
        // Rows with no seller account (admin/NPC samples, pre-account legacy rows) never expire. A row with TimeStamp 0
        // (listed before 0.0.763) starts its clock now instead of expiring at once.
        internal static void ServerExpireTick()
        {
            if (!IsServer) return;
            float t = Time.realtimeSinceStartup;
            if (t < _nextExpiryCheck) return;
            _nextExpiryCheck = t + ExpiryCheckIntervalSec;
            int days = ListingLifetimeDays;
            if (days <= 0) return;
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.EnsureVaultConfigured();   // idempotent; the vault must be open
            uint now = NowUnix();
            long lifetime = days * SecondsPerDay;
            bool changed = false;
            List<MarketListing> all;
            try { all = MarketplaceRepository.GetListings(); } catch { return; }
            foreach (var l in all)
            {
                try   // one bad row never stops the rest of the tick
                {
                    if (l == null || string.IsNullOrEmpty(l.SellerUserID)) continue;
                    if (l.TimeStamp == 0) { l.TimeStamp = now; MarketplaceRepository.UpdateListing(l); continue; }
                    if ((long)now - l.TimeStamp < lifetime) continue;
                    if (!TryTakeFromEscrow(l._id, int.MaxValue, out var taken)) continue;
                    bool mailed;
                    try { mailed = ReturnEscrowByMail(taken, "Listing expired", $"Your marketplace listing expired after {days} day(s); the item is returned here."); }
                    catch (Exception ex) { mailed = false; Debug.LogWarning($"{LogPrefix} expiry mail for listing {l._id} threw: {ex.Message}"); }
                    if (!mailed)
                    {
                        PutBackInEscrow(l._id, taken);   // retried on a later tick; the item is never lost
                        Debug.LogWarning($"{LogPrefix} EXPIRE listing {l._id} undone: the return mail failed, it stays listed");
                        continue;
                    }
                    global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} EXPIRE listing {l._id}: {taken.Count}x {taken.ItemPrefab} (q{taken.Quality}) seller {taken.SellerUserID} mailed");
                    changed = true;
                }
                catch (Exception ex) { Debug.LogWarning($"{LogPrefix} expiry of listing {l?._id} failed: {ex.Message}"); }
            }
            if (changed) BroadcastListings();
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
                pkg.Write(Mathf.Max(1, l.Quality));
                pkg.Write(l.Variant);
                pkg.Write(l.DurabilityPercent);
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

        // Grant with full item fidelity. Plain refunds (currency) use the defaults = fresh quality-1 stack.
        private static void ReplyGrant(long senderId, string prefab, int count, string status,
            int quality = 1, int variant = 0, byte durabilityPct = 100, string crafterName = "", long crafterId = 0L, string customData = "{}")
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.Grant);
            pkg.Write(prefab ?? "");
            pkg.Write(count);
            pkg.Write(status ?? "");
            pkg.Write(quality);
            pkg.Write(variant);
            pkg.Write(durabilityPct);
            pkg.Write(crafterName ?? "");
            pkg.Write(crafterId);
            pkg.Write(customData ?? "{}");
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        private static void ReplySettle(long senderId, int reqId, bool giveBack, string status, bool paidByBank = false)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.Settle);
            pkg.Write(reqId);
            pkg.Write(giveBack);
            pkg.Write(status ?? "");
            pkg.Write(paidByBank);   // trailing: true = the server charged the bank (the client keeps/returns its bag coins)
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        // Bank couldn't cover a PayStateNotPaid buy: the client pays `total` (the server's price) from its bag and resends.
        private static void ReplyPayFromBag(long senderId, int reqId, int total)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.PayFromBag);
            pkg.Write(reqId);
            pkg.Write(total);
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
                        if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.IsFromServer(senderId))
                        { Debug.LogWarning($"{LogPrefix} Grant ignored: sender {senderId} is not the server"); break; }
                        string prefab = pkg.ReadString();
                        int count = pkg.ReadInt();
                        string status = pkg.ReadString();
                        int quality = pkg.ReadInt();
                        int variant = pkg.ReadInt();
                        byte durPct = pkg.ReadByte();
                        string crafterName = pkg.ReadString();
                        long crafterId = pkg.ReadLong();
                        string customData = pkg.ReadString();
                        GrantToInventory(prefab, count, quality, variant, durPct, crafterName, crafterId, customData);
                        OnResult?.Invoke(status);
                        break;
                    }
                    case Res.Status:
                        OnResult?.Invoke(pkg.ReadString());
                        break;
                    case Res.Settle:
                    {
                        // Only the server may settle (any peer can route to us).
                        if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.IsFromServer(senderId))
                        { Debug.LogWarning($"{LogPrefix} Settle ignored: sender {senderId} is not the server"); break; }
                        int reqId = pkg.ReadInt();
                        bool giveBack = pkg.ReadBool();
                        string status = pkg.ReadString();
                        // Return exactly what THIS client consumed for that request (its own ledger), never a server amount.
                        if (_pendingLists.TryGetValue(reqId, out var pl))
                        {
                            _pendingLists.Remove(reqId);
                            if (giveBack) GrantToInventory(pl.Prefab, pl.Qty, pl.Quality, pl.Variant, pl.DurabilityPct, pl.CrafterName, pl.CrafterId, pl.CustomData);
                        }
                        bool paidByBank = pkg.GetPos() < pkg.Size() && pkg.ReadBool();   // trailing, informational
                        if (_pendingBuys.TryGetValue(reqId, out var pb))
                        {
                            _pendingBuys.Remove(reqId);
                            // giveBack on a bank-paid buy = the bag coins this client pre-paid come back (no double pay).
                            if (giveBack && pb.BagTaken) GrantToInventory(pb.Currency, pb.Paid);
                        }
                        if (paidByBank) FiresLogger.LogVerbose($"{LogPrefix} buy req {reqId} paid from the bank");
                        if (!string.IsNullOrEmpty(status)) OnResult?.Invoke(status);
                        break;
                    }
                    case Res.PayFromBag:
                    {
                        if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.IsFromServer(senderId))
                        { Debug.LogWarning($"{LogPrefix} PayFromBag ignored: sender {senderId} is not the server"); break; }
                        int reqId = pkg.ReadInt();
                        int total = pkg.ReadInt();
                        if (!_pendingBuys.TryGetValue(reqId, out var pb) || pb.BagTaken) break;   // not ours / already paid
                        _pendingBuys.Remove(reqId);
                        // Pay only the price this client agreed to (server total must match it), from its own bag.
                        if (total != pb.Paid || !TakeFromBag(pb.Currency, total))
                        { OnResult?.Invoke($"Need {total} {pb.Currency} in your bank or bag."); break; }
                        RequestBuy(pb.ListingId, pb.Qty, pb.Currency, total);   // bag-paid resend (normal bag path)
                        break;
                    }
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
                int quality = pkg.ReadInt();
                int variant = pkg.ReadInt();
                byte durPct = pkg.ReadByte();
                list.Add(new MarketplaceListing
                {
                    Id = id, ItemPrefab = prefab, Count = c, Price = price,
                    Currency = currency, SellerName = seller, SellerId = sellerId,
                    Quality = quality, Variant = variant, DurabilityPercent = durPct,
                });
            }
            MarketplaceListingManager.ReplaceLive(list);
            OnListings?.Invoke();
        }

        private static int GrantToInventory(string prefab, int count, int quality = 1, int variant = 0,
            byte durabilityPct = 100, string crafterName = "", long crafterId = 0L, string customData = "{}")
        {
            var player = Player.m_localPlayer;
            if (player == null || string.IsNullOrEmpty(prefab) || count <= 0) return 0;
            var inv = player.GetInventory();
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop == null) return 0;
            quality = Mathf.Max(1, quality);
            if (inv.CanAddItem(drop.m_itemData, count))
            {
                var added = inv.AddItem(prefab, count, quality, variant, crafterId, crafterName ?? "", false);
                // Durability + customData only make sense on a single item (equipment never stacks);
                // stackable materials carry neither, so a multi-count grant skips this on purpose.
                if (added != null && count == 1)
                {
                    try
                    {
                        if (added.m_shared != null && added.m_shared.m_useDurability)
                            added.m_durability = Mathf.Clamp01(durabilityPct / 100f) * added.GetMaxDurability(quality);
                        if (!string.IsNullOrEmpty(customData) && customData != "{}")
                        {
                            var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(customData);
                            if (dict != null && dict.Count > 0)
                                foreach (var kv in dict) added.m_customData[kv.Key] = kv.Value;
                        }
                    }
                    catch { }
                }
            }
            else ItemDrop.DropItem(drop.m_itemData, count, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
            return count;
        }

        private static long ParseId(string s) => long.TryParse(s, out long v) ? v : 0L;
    }
}
