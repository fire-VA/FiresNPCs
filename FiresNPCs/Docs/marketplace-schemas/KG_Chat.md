# KG_Chat — Marketplace schema

A drop-in replacement for Valheim's vanilla chat window: a custom resizable/draggable chat UI with send
mode buttons (Say/Shout/Whisper/Group/Faction), emoji sprites, transparency control, a per-message-type
filter, and optional typing sounds. **Purely client-side.**

## Location / how loaded

- **Code:** `Modules/KG_Chat/KG_Chat.cs` (single file).
- **No `.cfg` file of its own.** All settings are bound into the **main plugin config**
  (`Marketplace._thistype.Config`, i.e. the BepInEx config for the `kg.Marketplace` plugin —
  `kg.Marketplace.cfg`, NOT `MarketPlace.cfg`), under section `[KG Chat]`.
- Client autoload `[Market_Autoload(Client, Last, "OnInit", null, null)]`.
- **Master enable comes from `MarketPlace.cfg` → `EnableKGChat`** (Global_Configs `_enableKGChat`, see
  `Global_Options.md`). `ApplyKGChat()` early-returns if `_enableKGChat` is false. So the on/off switch
  is server-synced; the cosmetic prefs below are local.

## Config keys (section `[KG Chat]`, in the plugin's own BepInEx cfg)

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Font Size` | int | `18` | chat font size. |
| `Use Type Sound` | bool | `false` | play a keystroke sound while typing (also toggled in-UI). |
| `Hide Floating Text` | bool | `true` | suppress in-world floating chat text (except pings). |
| `Chat Filter` | enum `ChatFilter` (`[Flags]`) | `All` | which message types to show: `None=0, Normal=1, Shout=2, Whisper=4, All=7`. |
| `Transparency` | enum `Transparency` | `Two` | background alpha: `None=0.0, One=0.2, Two=0.4, Three=0.6, Four=0.8, Five=0.95`. |
| `UI_sizeX` | float | `1` | chat window X scale (set by drag-resize). |
| `UI_sizeY` | float | `1` | chat window Y scale. |
| `UI_posX` | float | `-15` | chat window X anchored position (set by drag-move). |
| `UI_posY` | float | `60` | chat window Y anchored position. |

These are runtime-tunable: dragging/resizing the window and clicking the transparency / mute buttons
write back to the cfg via `Config.Save()`.

## Runtime behavior / what it does

- On `Player.OnSpawned` (and on `SyncedGlobalOptions` change) `ApplyKGChat()` destroys the vanilla
  `Chat` component and instantiates the `Marketplace_KGChat_SelfCanvas` asset (sorting order 599),
  wiring up `ChatController` (mode buttons), `ResizeUI`, `DragUI`.
- **Send-mode slash shortcuts:** typing exactly `/say`, `/shout`, `/whisper`, `/faction`, or
  `/group`|`/party` (the last only if the Guilds `API.IsLoaded()`) switches mode instead of sending.
  Outgoing text is rewritten to `/say`,`/s`,`/w`,`/p`,`/f` prefixes by mode.
- **Emojis:** `:moji0:`…`:moji24:` map to TMP `<sprite=0..24>`. Loaded from
  `BepInEx/config/Marketplace_KGChat_Emojis/spritesheet.png` if present (else dumps the original to
  `spritesheet_original.png`). Emoji picker tab in the UI inserts the `:mojiN:` token.
- **Blocked chat users:** `Chat.InputText` prefix returns false (blocks sending) if
  `Global_Configs._localUserID` is in `_blockedChatUsers` (from `MarketPlace.cfg` → `BlockedChatUsers`).
- **Console command** `chatfilter <flag>` toggles a `ChatFilter` flag at runtime.
- Several Harmony transpilers strip vanilla key handling (keys 323/324) and case-forcing in
  `Terminal.AddString`.

## Persistence

Local BepInEx cfg (`[KG Chat]` section) only. Master toggle + blocked-users list ride on the
server-synced `Global_Configs.Container` (see `Global_Options.md`). No LiteDB, no `customData`.

## NPC linkage

None.

## Gotchas / risks

- **Two different config files are involved:** the cosmetic `[KG Chat]` keys live in the plugin's own
  `kg.Marketplace.cfg`, while the master `EnableKGChat` + `BlockedChatUsers` live in `MarketPlace.cfg`.
  Don't conflate them.
- Entirely client-side; nothing to import server-side beyond honoring `EnableKGChat`/`BlockedChatUsers`.
- Emoji spritesheet override is a side file under `BepInEx/config/Marketplace_KGChat_Emojis/`, outside
  the `Marketplace/` config tree.
- It hard-replaces vanilla `Chat` (DestroyImmediate); any other mod expecting the vanilla chat object
  may conflict when KG Chat is enabled.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
