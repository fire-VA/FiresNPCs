# DistancedUI — Marketplace schema

Lets players open selected Marketplace NPC services (trader/banker/quests/etc.) **without an NPC nearby**
— i.e. globally from anywhere — by naming the profiles that should be remotely accessible.

## Location / how loaded

- **Code:** `Modules/DistancedUI/` — `DistanceUI_Main_Server.cs`, `DistancedUI_Main_Client.cs`,
  `DistancedUI_UI.cs`, `DistancedUI_DataType.cs`, `HoverOnButton.cs`.
- **Config:** single file `DistancedUI/DistancedUI.cfg` (path:
  `BepInEx/config/Marketplace/DistancedUI/DistancedUI.cfg`).
- **This is a BepInEx `ConfigFile`, NOT the hand-parsed `[section]` skeleton.** It uses standard
  BepInEx INI sections/keys via `Config.Bind`. All keys live under one section `[DistancedUI]`.
- Server autoload `[Market_Autoload(Server, Normal, "OnInit", new[]{"DistancedUI.cfg"},
  new[]{"OnConfigChange"})]` — watches the cfg; on change re-reads and re-syncs (with a debounce via
  `Utils.DelayReloadConfig`).

## Config keys (all in section `[DistancedUI]`)

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | bool | `false` | master on/off for the whole DistancedUI system. |
| `CanUseMarketplace` | bool | `true` | allow opening the auction house remotely. |
| `CanUseMail` | bool | `true` | allow opening the mail UI remotely. |
| `CanTakeMailAttachments` | bool | `false` | allow claiming mail attachments remotely. |
| `TraderProfiles` | string | `"Profiles here with ,"` | comma list of trader profile names. |
| `BankerProfiles` | string | `"Profiles here with ,"` | comma list of banker profile names. |
| `TeleporterProfiles` | string | `"Profiles here with ,"` | comma list. |
| `GamblerProfiles` | string | `"Profiles here with ,"` | comma list. |
| `BufferProfiles` | string | `"Profiles here with ,"` | comma list. |
| `QuestProfiles` | string | `"Profiles here with ,"` | comma list. |
| `InfoProfiles` | string | `"Profiles here with ,"` | comma list (ServerInfo profiles). |
| `TransmogrificationProfiles` | string | `"Profiles here with ,"` | comma list. |
| `Dialogues` | string | `"Profiles here with ,"` | comma list of dialogue profile names. |

## Grammar of the profile-list strings

Each `*Profiles` string is `Split(',')`, and **every token is `.ToLower().Trim()`** before storing.
So profile names are matched **case-insensitively and lowercased** — consistent with how the
`[section]` headers are lowercased in the other modules. The placeholder default
`"Profiles here with ,"` becomes the single (meaningless) token `profiles here with` plus an empty
token; harmless unless a real profile happens to match.

Example `DistancedUI.cfg`:
```ini
[DistancedUI]
Enabled = true
CanUseMarketplace = true
CanUseMail = true
CanTakeMailAttachments = false
TraderProfiles = bandit_trader, blacksmith
BankerProfiles = central_bank
TeleporterProfiles =
GamblerProfiles =
BufferProfiles =
QuestProfiles = daily_quests
InfoProfiles =
TransmogrificationProfiles =
Dialogues =
```

## Data model (synced to clients)

`DistancedUIData : ISerializableParameter` (`DistancedUI_DataType.cs`), pushed via
`CustomSyncedValue<DistancedUIData>` (sync key `"distancedUIData"`). Fields: the 4 bools above plus 9
`List<string>` (`TraderProfiles, TeleporterProfiles, GamblerProfiles, BufferProfiles, QuestProfiles,
InfoProfiles, BankerProfiles, TransmogrificationProfiles, DialogueProfiles`).

Serialize order: `Enabled, MarketplaceEnabled, MailEnabled, CanTakeMailAttachments`, then each list as
`count` followed by `count` strings, in the order: Trader, Teleporter, Gambler, Buffer, Quest, Info,
Banker, Transmogrification, Dialogue. (Note the **serialize order differs from the cfg-bind order** —
Banker is read 5th from cfg but serialized 7th.)

## Persistence

ServerSync `CustomSyncedValue` only (`"distancedUIData"`). The authoritative store is the cfg file; no
LiteDB, no `customData`.

## NPC linkage

Indirect: the profile names listed here are the same profile names NPCs reference via their ZDO
`Profile` string for each service module. DistancedUI just whitelists which of those profiles can be
opened without a physical NPC.

## Gotchas / risks

- **Plain BepInEx CFG**, not the shared `[name]`/data-line skeleton — parse it as INI.
- Profile names are lowercased+trimmed; match accordingly.
- Cfg-read order ≠ wire-serialize order (Banker). Irrelevant for cfg parsing but matters if
  reimplementing the ServerSync wire format.
- `Enabled=false` (the default) disables everything regardless of the per-service bools/lists.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
