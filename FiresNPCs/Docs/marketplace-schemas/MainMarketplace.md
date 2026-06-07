# MainMarketplace — Marketplace schema

The player-to-player auction house ("Tradepost"). Sellers list inventory items at a fixed total `Price`
in a chosen `Currency`; buyers buy the whole slot or a partial quantity. Proceeds are mailed to the
seller via CMS (see `CMS.md`), minus a configurable tax.

## Location / how loaded

- **Code:** `Modules/MainMarketplace/` — `Marketplace_Main_Server.cs` (RPC handlers),
  `Marketplace_Main_Client.cs`, `Marketplace_UI.cs`, `Marketplace_DataTypes.cs`.
- **No `.cfg` of its own.** All tunables live in `MarketPlace.cfg` (see `Global_Options.md`):
  `ItemMarketLimit`, `MarketTaxes`, `VIPplayersTaxes`, `VIPplayersList`, `MarketSellBlockedPrefabs`,
  `BlockedPlayers`, `PossibleCurrencies`.
- **Server autoload:** `[Market_Autoload(Server, Normal, "OnInit", null, null)]` — `OnInit` is empty;
  the DB is the source of truth, populated at runtime by RPCs. No file watcher.
- **Listings persist** in LiteDB collection `Marketplace` (`SavedData/DB.db`).

### ⚠️ Lootboxes (`Configs/Lootboxes/*.cfg`) — NOT parsed in this build
The README layout lists `Configs/Lootboxes/`, and `Marketplace.cs` registers that folder with watch-key
**`"LB"`** (`FoldersToFiles.Add(Market_Paths.LootboxesFolder, "LB")`); `Market_Paths` creates the folder
on init. There is also a Discord webhook category `Webhooks.Lootboxes` ("opened {1} and got: {2}").
**However, no module in this decompiled source reads or parses `Configs/Lootboxes/*.cfg`.** No
`[Market_Autoload(... "LB" ...)]` watcher exists, and no class references `LootboxesFolder` for reading.
The closest live feature is the **Gambler** (`Modules/Gambler/`, watch-key `"GP"`,
`Configs/Gamblers/*.cfg`) which has the roll-table grammar one would expect of a lootbox
(`Prefab,Min-Max` pairs, `[profile=MAXROLLS]` header). Treat Lootbox `.cfg` grammar as **undocumented /
not implemented in 9.8.1.0** — the folder is reserved and watched but inert. If a real Lootbox config
surfaces in-game, it is most likely handled by an external/companion assembly not present in this dump.

## Data-line grammar / config keys / data model

There is **no hand-parsed data file** here. The domain object is built from live client inventory and
persisted to LiteDB.

### `MarketSlot` (LiteDB `Marketplace`, the auction listing) — from `DB/DB.cs`
| Field | Type | Notes |
|---|---|---|
| `_id` | `int` | LiteDB id (auto). Used as the slot handle in buy/remove RPCs. |
| `ItemPrefab` | `string` | prefab name of the listed item. Indexed (`EnsureIndex(x => x.ItemPrefab)`). |
| `Count` | `int` | quantity in the slot. |
| `Price` | `int` | **price per unit** (NOT total — buy multiplies `Count * Price`). |
| `SellerName` | `string` | display name. |
| `SellerUserID` | `string` | platform user id (host name). |
| `ItemCategory` | enum int | `ItemData_ItemCategory { ALL=0, WEAPONS, ARMOR, CONSUMABLE, TOOLS, RESOURCES }`. |
| `Quality` | `int` | item quality/level. |
| `Variant` | `int` | item variant. |
| `CrafterName` | `string` | default `""`. |
| `CrafterID` | `long` | crafter platform id. |
| `CUSTOMdata` | `string` | fastJSON of `Dictionary<string,string>` item custom data; default `"{}"`. |
| `DurabilityPercent` | `byte` | 0–100. |
| `TimeStamp` | `uint` | `(uint)ZNet.m_netTime` at listing. |
| `Currency` | `string` | default `"Coins"`. Validated against `_possibleCurrencies` (falls back to `_possibleCurrencies[0]`). |

ServerSync wire order (`Serialize`/`Deserialize`): `_id, ItemPrefab, Count, Price, SellerName,
SellerUserID, (int)ItemCategory, Quality, Variant, CrafterName, CrafterID, CUSTOMdata,
DurabilityPercent, TimeStamp, Currency`. **Note:** `ToClient()` drops `TimeStamp` and `Currency`
(the client `ClientMarketSendData` has no `TimeStamp`).

### `ClientMarketSendData` (wire/mail payload) — `Marketplace_DataTypes.cs`
JSON-serialized via fastJSON. Fields: `ItemPrefab, Count, Price, SellerName, ItemCategory(enum),
Quality, Variant, CUSTOMdata("{}"), CrafterName(""), CrafterID(long), DurabilityPercent(byte),
Currency`. `ItemName`/`ItemPrefab` are `[BsonIgnore]`. This is the type used for **mail attachments and
item-links** too (see `CMS.md`).

## Lifecycle (list / buy / cancel)

All server-side in `Marketplace_Main_Server.cs`. RPC names are load-bearing for compat.

**LIST** — client → `"KGmarket ReceiveItem"` (JSON `ClientMarketSendData`):
- Server rejects `Count <= 0`. Builds `MarketSlot(other, hostName)` (stamps `SellerUserID`, `TimeStamp`,
  re-validates `Currency`), inserts via `Marketplace_AddItem`, then `SaveMarketAndSendToClients()`,
  logs, and fires a Discord webhook.
- **Client-side gates** (in `Marketplace_UI.cs`, server does NOT re-check these): sell blocked if seller
  already owns `>= _itemMarketLimit` slots; prefab must not be in `_blockedPrefabsServer`; currency must
  exist in `_possibleCurrencies` and resolve to a prefab.

**BUY** — client → `"KGmarket RequestBuyItem"` (`ZPackage`: `int slotId, int totalCost, int wantCount,
string currencyPrefab`):
- `GetSlotByID(slotId)`. If `null` → refund-style answer (server returns the paid currency back via
  `"KGmarket BuyItemAnswer"`) and resyncs.
- If `wantCount >= slot.Count`: whole slot removed (`Marketplace_RemoveItem`); buyer gets the items;
  any overpay (`wantCount - Count` units × Price) is refunded as currency.
- Else: partial — `slot.Count -= wantCount`, `ModifyExisting(slot)`.
- **Self-buy = cancel:** if `buyer.hostName == slot.SellerUserID`, the items are mailed back as a
  "SlotCancelled" CMS mail (no tax, no payout).
- **Normal buy:** tax = `_vipmarketTaxes` if seller in `_vipPlayerList` else `_marketTaxes` (clamped
  0–100). Buyer gets a "SlotBought" mail with the item attachment; seller gets an "Income" mail with
  `floor(totalCost * (1 - tax/100))` of the currency prefab as attachment, plus the item as a *link*.
  Mail topics/messages are localization keys (`$mpasn_CMS_TradePost*`) embedding
  `<link=kg.CMSLINK:N>` markup (see `CMS.md`).

**CANCEL/REMOVE (admin)** — client → `"KGmarket RemoveItemAdmin"` (`int id`): only honored if sender's
host name is in `ZNet.m_adminList`. Deletes the slot and resyncs. (Player self-cancel is the self-buy
path above.)

**SYNC** — `SaveMarketAndSendToClients()` serializes all slots into a compressed `ZPackage` and pushes
`"KGmarket ReceiveTradepostData"` to every ready peer (and to self in singleplayer). Clients request a
full refresh via `"KGmarket ReceiveTradepostData_s"`.

## Persistence

- **LiteDB `Marketplace`** collection (`SavedData/DB.db`), `MarketSlot` rows, index on `ItemPrefab`.
  `Marketplace_AddItem` / `Marketplace_RemoveItem(int _id)` / `Marketplace_GetItems` /
  `GetSlotByID(int)` / `ModifyExisting`.
- Sale payouts flow into the **`Mails`** collection (CMS), not back into Marketplace.
- No ServerSync persistence of listings; the `CustomSyncedValue` path is only the live push to clients.

## NPC linkage

The marketplace UI is opened by an NPC of type **Marketplace** (a.k.a. Tradepost) — NPCs reference it by
NPC `Type`, not by a named profile (unlike Trader/Banker). No per-profile `.cfg`. Also reachable via the
**DistancedUI** "Marketplace" toggle without an NPC (see `DistancedUI.md`).

## Gotchas / risks

- **`Price` is per-unit**, despite the field name reading like a total; buy cost = `Count * Price`.
- **Sell-side validation is client-only.** A non-compliant client can list blocked prefabs / exceed the
  slot limit. Our importer/host must re-enforce server-side if security matters.
- `ToClient()` silently drops `TimeStamp`/`Currency`; mail attachments therefore carry no currency tag.
- **Lootboxes are unimplemented here** — do not invent a grammar; the folder is reserved + watched only.
- Currencies are validated/normalized against live prefabs at `ZNetScene.Awake` (see
  `Global_Options.md`); a currency naming a missing prefab is dropped from `_possibleCurrencies`.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
