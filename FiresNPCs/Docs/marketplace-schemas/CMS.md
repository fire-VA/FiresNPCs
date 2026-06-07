# CMS (Centralized Mail System) — Marketplace schema

The server-wide mailbox. Mails carry text plus optional **attachments** (claimable items) and **links**
(read-only item previews). Used by the Marketplace for sale receipts/payouts and by admins for
broadcasts. Players can also send plain text-only mail to each other.

## Location / how loaded

- **Code:** `Modules/CMS/` — `CentralizedMailSystem_Server.cs`, `_Client.cs`, `_UI.cs`,
  `_DataTypes.cs`, `CMS_Links.cs`.
- **No `.cfg`.** Server autoload `[Market_Autoload(Server, Last, "OnInit", null, null)]`. `OnInit`
  loads all `Users` from LiteDB into `SyncedCMSUsers` and registers a 60s `FixedUpdate` tick.
- Tunables come from `MarketPlace.cfg` (`MailPostRecipe`, `MailPostWaitTime`, `MailPostExcludeItems`) —
  see `Global_Options.md`. The default admin-send expiry is set in the UI (`"168"` hours).

## Data model

### `CMS_User` (LiteDB `Users`) — `DB/DB.cs`
| Field | Type | Notes |
|---|---|---|
| `_id` | `int` | LiteDB id. |
| `Name` | `string` | last-seen display name. |
| `UserID` | `string` | platform user id. Indexed (`EnsureIndex(x => x.UserID)`). |
| `LastOnline` | `long` | `DateTime.Now.Ticks`. |
| `IsOnline` | `bool` | recomputed each tick; **synced but not a stable DB fact** (set false then re-set). |

ServerSync wire order: `Name, UserID, LastOnline, IsOnline` (NOTE: `_id` is **not** serialized for
`CMS_User`, unlike `CMS_Entry`). The full user list is pushed to clients via `SyncedCMSUsers`
(`CustomSyncedValue<List<CMS_User>>`, sync key `"SyncedCMSUSers"` — note the capital-S typo).

### `CMS_Entry` (LiteDB `Mails`) — `DB/DB.cs`
| Field | Type | Notes |
|---|---|---|
| `_id` | `int` | LiteDB id; the mail handle in read/delete/withdraw RPCs. |
| `Owner` | `string` | recipient platform user id. Indexed (`EnsureIndex(x => x.Owner)`). |
| `Created` | `long` | `DateTime.Now.Ticks` at send. |
| `Sender` | `string` | sender name or a localization key (e.g. `$mpasn_CMS_TradePost`). |
| `Topic` | `string` | subject (often a loc key). |
| `Message` | `string` | body; may contain `<link=kg.CMSLINK:N>...</link>` markup. |
| `Attachments` | `List<ClientMarketSendData>` | claimable items; `null` when none. |
| `Links` | `List<ClientMarketSendData>` | read-only item previews; `null` when none. |
| `ExpireAfterMinutes` | `int` | default `10800` (7.5 days). `AddMail` default param is `10080` (7 days). `0` = no expiry display. |
| `WasRead` | `bool` | |
| `AttachmentsTaken` | `bool` | once true, attachments can't be re-claimed. |

ServerSync wire order: `_id, Owner, Created, Sender, Topic, Message`, then `bool hasAttachments` +
(if true) `JSON.ToJSON(Attachments)`, then `bool hasLinks` + (if true) `JSON.ToJSON(Links)`, then
`ExpireAfterMinutes, WasRead, AttachmentsTaken`. **Attachments/Links are persisted as fastJSON strings
embedded in the ZPackage**, not as nested binary — match this exactly for wire compat.

### Attachment / link / item-preview model (`CMS_Links.cs`)
- Link markup key is `"kg.CMSLINK:"` followed by an int index. Convention from the Marketplace
  sale-mail builder: **Links** start at index `0`, **Attachments** start at index `100`
  (`SetItemLinks` assigns links `0..` and attachments `100..`). The hover tooltip rebuilds a full
  `ItemDrop.ItemData` from the `ClientMarketSendData` (stack/quality/variant/crafter/durability/customData).
- Withdrawing attachments (`"KGmarket CMS WithdrawMailAttachments"`) sends each attachment to the client
  via `"KGmarket BuyItemAnswer"` (same delivery RPC the Marketplace buy uses) and flips
  `AttachmentsTaken`.

## RPCs (server handlers, names are load-bearing)

| RPC | Args | Action |
|---|---|---|
| `KGmarket CMS WithdrawMailAttachments` | `int mailUID` | claim attachments if not already taken. |
| `KGmarket CMS DeleteMail` | `int mailUID` | delete one mail owned by sender. |
| `KGmarket CMS ReadMail` | `int mailUID` | set `WasRead`. |
| `KGmarket CMS RequestMailAmount` | — | reply `CMS_ReceiveMailAmount` (total). |
| `KGmarket CMS RequestMailData` | `int page, int revision` | reply `CMS_ReceiveMailData` (7/page, newest first). |
| `KGmarket CMS SendMail_User` | `ZPackage{userID,topic,message}` | player→player, text only. |
| `KGmarket CMS SendMail_Admin` | `ZPackage{sender,userID,topic,message,minutes,attachments[]}` | admin send; `userID=="all"` (case-insensitive) broadcasts to every known `CMS_User`. |

`AddMail(sender, topic, message, userID, attachments?, links?, minutesDuration=10080)` is the internal
entry point (Marketplace payouts call it). `userID=="0"` is ignored. On send, if the recipient is online,
pushes `CMS_ReceiveUnreadMailAmount`.

## Persistence

- **LiteDB `Mails`** (`CMS_Entry`, index `Owner`) and **`Users`** (`CMS_User`, index `UserID`) in
  `SavedData/DB.db`.
- `BsonMapper.Global.IncludeFields = true` etc. (see README) — required for these field-backed types.
- ServerSync `SyncedCMSUsers` (online presence) and `SyncedServerTime` (`long` ticks, sync key
  `SyncedServerTime`) are runtime-only, not persisted.

## Expiry — IMPORTANT GOTCHA

`ExpireAfterMinutes` + `Created` are used **only for client-side display** of remaining time
(`SyncedServerTime - Created` vs `ExpireAfterMinutes.MinutesToTicks()`, where
`MinutesToTicks = minutes*60*1e7`). **There is NO server-side purge of expired mail in this build** —
the periodic `FixedUpdate`/`PeriodicCheck` only refreshes user online-status and unread counts. Expired
mail keeps living in the DB until the owner manually deletes it. A drop-in importer should not assume
expired rows are gone.

## NPC linkage

Opened via a **Mail/Mailbox** NPC interaction or the DistancedUI "Mail" toggle (`MailEnabled`,
`CanTakeMailAttachments`). No per-profile `.cfg`; not keyed by NPC `Profile` string.

## Gotchas / risks

- `CMS_User.Serialize` omits `_id`; `CMS_Entry.Serialize` includes it. Don't symmetrize blindly.
- ServerSync key for users is `"SyncedCMSUSers"` (typo with capital S) — must match for wire compat.
- Attachments/Links serialize as **fastJSON strings inside the ZPackage**; a naive binary
  re-implementation will desync clients.
- Link index convention (links 0+, attachments 100+) is implicit in `CMS_Links.SetItemLinks`; mail
  builders must place markup indices accordingly.
- No expiry GC (see above).

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
