# Global_Options (MarketPlace.cfg) — Marketplace schema

The server's global BepInEx config. **Many other modules read these keys**, so this is a cross-module
dependency hub. All keys bind under a single section `[Main]`. The bound values are distributed to
clients via a single `CustomSyncedValue<Container>` (sync key `"Global_Values"`).

## Location / how loaded

- **Code:** `Modules/Global_Options/Global_Configs.cs`.
- **Config file:** `BepInEx/config/Marketplace/MarketPlace.cfg` (`Market_Paths.MainConfig`).
- A **plain BepInEx `ConfigFile`** (INI), NOT the hand-parsed `[section]`/data-line skeleton. Every key
  is bound via `SearchOption<T>(name, default, desc)` → `_config.Bind<T>("Main", name, default, desc)`.
  So all keys are under `[Main]`.
- Server autoload `[Market_Autoload(Server, **First**, "OnInit", new[]{"MarketPlace.cfg"},
  new[]{"OnChange"})]` — priority **First** so other modules can rely on these values being loaded.
  Watched; `OnChange` re-reads (debounced) and re-syncs to all clients.

## Config keys (all under `[Main]`)

### Stored in static fields (server-local, NOT in the synced Container)
| Key | Type | Default | Field | Consumer |
|---|---|---|---|---|
| `EnableTransmogLog` | bool | `false` | `EnableTransmogLog` | Transmogrification logging. |
| `EnableTraderLog` | bool | `false` | `EnableTraderLog` | Trader logging. |
| `BankerIncomeTime` | int | `1` | `BankerIncomeTime` | Banker interest period, **hours**. |
| `BankerIncomeMultiplier` | float | `0.0` | `BankerIncomeMultiplier` | Banker interest rate per period. |
| `FeedbackWebhookLink` | string | `"webhook link"` | `WebHookLink` | Feedback module Discord webhook. |
| `BankerInterestItems` | string | `"All"` | `BankerInterestItems` | which banked prefabs earn interest (`.Replace(" ","")`; `"All"` = every item). |
| `BankerVIPIncomeMultiplier` | float | `0.0` | `BankerVIPIncomeMultiplier` | VIP banker interest rate. |
| `OrConditionSeparator` | string | `"||"` | `OrConditionSeparator` | OR token used in Dialogue condition parsing. |

### Stored in the synced `Container` (`SyncedGlobalOptions.Value`, pushed to clients)
| Key | Type | Default | Container field | Consumer(s) |
|---|---|---|---|---|
| `ItemMarketLimit` | int | `15` | `_itemMarketLimit` | **MainMarketplace** max slots/seller. |
| `BlockedPlayers` | string | `"User IDs"` | `_blockedPlayerList` | Marketplace blocked players. |
| `MarketTaxes` | int (clamp 0–100) | `0` | `_marketTaxes` | **MainMarketplace** sale tax %. |
| `CanTeleportWithOre` | bool | `true` | `_canTeleportWithOre` | **Teleporter** ore restriction. |
| `MarketSellBlockedPrefabs` | string | `"Coins, SwordCheat"` | `_blockedPrefabsServer` | comma list of prefabs that can't be listed. |
| `GamblerEnableWinNotifications` | bool | `false` | `_gamblerEnableNotifications` | **Gambler** win broadcast. |
| `AllowMultipleQuestsScore` | bool | `false` | `_allowMultipleQuestScore` | **Quests** scoring. |
| `MaxAcceptedQuests` | int | `7` | `_maxAcceptedQuests` | **Quests** accepted cap. |
| `HideOtherQuestRequirementQuests` | bool | `false` | `_hideOtherQuestRequirementQuests` | **Quests** UI gating. |
| `AllowKillQuestsInParty` | bool | `true` | `_allowKillQuestsInParty` | **Quests** party kill credit. |
| `EnableKGChat` | bool | `true` | `_enableKGChat` | **KG_Chat** master enable. |
| `MailPostRecipe` | string | `"SwordCheat,1"` | `_mailPostRecipe` | **CMS/MailPost** crafting recipe (`prefab,count`). |
| `MailPostWaitTime` | int | `5` | `_mailPostWaitTime` | **CMS/MailPost** cooldown, **minutes**. |
| `MailPostExcludeItems` | string | `"Items Here"` | `_mailPostExcludeItems` | **CMS/MailPost** non-mailable items (comma). |
| `PieceSaverRecipe` | string | `"SwordCheat,1"` | `_pieceSaverRecipe` | Piece Saver Crystal recipe (`prefab,count`). |
| `UseLeaderboard` | bool | `false` | `_useLeaderboard` | **Leaderboard** enable. |
| `OverrideDebug` | string | `"Steam IDs here"` | `_overrideDebug` (HashSet) | admin/debug UserIDs; split on `,`, kept only if numeric (long-parseable), trimmed. |
| `BlockedChatUsers` | string | `"Steam IDs here"` | `_blockedChatUsers` (HashSet) | **KG_Chat** mute list; numeric UserIDs only, same parse as above. |
| `VIPplayersTaxes` | int (clamp 0–100) | `0` | `_vipmarketTaxes` | **MainMarketplace** VIP sale tax %. |
| `VIPplayersList` | string | `"User IDs"` | `_vipPlayerList` | **MainMarketplace/Banker** VIP list (substring `.Contains` test on UserID). |
| `PossibleCurrencies` | string | `"Coins"` | `_possibleCurrencies` (string[]) | **MainMarketplace** allowed currencies; split `,`, trimmed. |
| `CanLeaveGuild` | bool | `true` | `_canLeaveGuild` | Blaxxun Guilds — if false, re-adds player on leave. |
| `FactionNameplateOffset` | Vector2 | `(0,0)` | `_factionNameplateOffset` | **Factions** nameplate offset (X=horiz, Y=vert). |

## `Container` (synced) wire format — `ISerializableParameter`

Serialize order (clients must match): `_blockedPrefabsServer, _blockedPlayerList, _itemMarketLimit,
_marketTaxes, _canTeleportWithOre, _gamblerEnableNotifications, _allowMultipleQuestScore,
_maxAcceptedQuests, _hideOtherQuestRequirementQuests, _allowKillQuestsInParty, _enableKGChat,
_mailPostRecipe, _mailPostWaitTime, _mailPostExcludeItems, _pieceSaverRecipe, _useLeaderboard`,
then `_overrideDebug` (count+strings), `_blockedChatUsers` (count+strings), `_vipmarketTaxes`,
`_vipPlayerList`, `_possibleCurrencies` (count+strings; **emits `1,"Coins"` if empty/null**),
`_canLeaveGuild`, `_factionNameplateOffset` (written as a `Vector3`, read back as `Vector2`).

## Currency normalization (important cross-module fact)

`[ServerOnlyPatch]` on `ZNetScene.Awake`: after scene load, each entry in `_possibleCurrencies` is
resolved to a real prefab via `ZNetScene.GetPrefab`. Missing prefabs are logged and **dropped**;
survivors are replaced by their canonical `prefab.name` (deduped via HashSet) and re-synced.
Consequence: `_possibleCurrencies` at runtime contains only valid, canonically-named prefabs, and
`MarketSlot` falls back to `_possibleCurrencies[0]` for any invalid currency.

## Other cross-module reads
- `Global_Configs._localUserID` is the client's own platform UserID (set in `ZNet.Start`/`FejdStartup`
  patches); used by KG_Chat block check and Marketplace VIP/limit UI.
- VIP / blocked lists are tested with **`string.Contains`** on the raw config string (substring match),
  not split-and-equals — a UserID list is one string and membership is a substring test. Be careful:
  short IDs could false-positive as substrings of longer IDs.

## Persistence

`MarketPlace.cfg` is the source of truth (BepInEx INI). The synced subset rides on one
`CustomSyncedValue<Container>` (`"Global_Values"`). No LiteDB, no `customData`.

## NPC linkage

None directly, but virtually every NPC service module reads one or more of these keys at runtime.

## Gotchas / risks

- **Plain INI under `[Main]`** — parse as BepInEx config, not the shared skeleton.
- Split between server-local static fields and the synced `Container`: Banker interest settings,
  trader/transmog log flags, feedback webhook, and `OrConditionSeparator` are **server-local** (not
  synced to clients).
- `_possibleCurrencies` is mutated/normalized at scene load — don't treat the raw cfg string as final.
- VIP / blocked-player membership is a substring `Contains` test on a single string field.
- `OverrideDebug`/`BlockedChatUsers` keep only numeric (long-parseable) tokens — non-Steam-style IDs are
  discarded.
- `FactionNameplateOffset` is a `Vector2` config but travels as a `Vector3` on the wire.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
