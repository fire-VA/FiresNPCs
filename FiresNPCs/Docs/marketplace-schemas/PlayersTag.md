# PlayersTag — Marketplace schema

Prepends a custom prefix/tag to a player's name in the server player list (the in-game player list /
nameplates fed from `ZNet.m_players`). E.g. a `[VIP]` or `[Admin]` badge in front of the name.

## Location / how loaded

- **Code:** `Modules/PlayersTag/` — `PlayerTags_Main_Server.cs`, `PlayerTag_Main_Client.cs`,
  `PlayersTag_DataTypes.cs`.
- **Config:** single file `PlayerTags/PlayerTags.cfg` (path:
  `BepInEx/config/Marketplace/PlayerTags/PlayerTags.cfg`). Created empty on init if missing
  (`File.Create(...).Dispose()`).
- Server autoload `[Market_Autoload(Server, Last, "OnInit", new[]{"PlayerTags.cfg"},
  new[]{"OnPlayerTagsChange"})]` — watches the cfg; on change re-reads and re-syncs.

## Data-line grammar

**Bespoke, NOT the shared `[section]` skeleton and NOT BepInEx INI.** The whole file is read with
`File.ReadAllLines` and each line is `Split(':')`:

- A line is accepted **only if it splits into exactly 2 parts** on the first `:` semantics — actually
  `string.Split(':')` with **exactly length 2** (so a value containing another `:` is rejected).
- `key = parts[0].Trim(' ')` = the player's **platform UserID**.
- `value = parts[1].Trim(' ')` = the **tag string**.
- Stored as `dict[userID] = tag`.
- No comment handling, no `[headers]`, no blank-line special case (a blank line just fails the
  length-2 check and is skipped).

Example `PlayerTags.cfg`:
```
76561198000000000 : [VIP]
76561198000000001 : <color=red>[Admin]</color>
```
Result: `{ "76561198000000000": "[VIP]", "76561198000000001": "<color=red>[Admin]</color>" }`.

The tag value may contain Unity rich-text markup (e.g. `<color=...>`), since it is concatenated raw
into the name.

## Data model

`PlayersTag_DataTypes.SyncedPlayersTagData` — `CustomSyncedValue<Dictionary<string,string>>`
(sync key `"kgPlayersTagData"`), mapping `UserID → tag`.

## Application

Harmony postfix on `ZNet.UpdatePlayerList` (server-only): for each entry in `ZNet.m_players`, if its
`m_userInfo.m_id.m_userID` has a tag, rewrites `m_name = $"{tag} {name}"` (tag, space, then original
name). It mutates the struct in the list in place.

## Persistence

ServerSync `CustomSyncedValue` only (`"kgPlayersTagData"`). Authoritative store is the cfg file. No
LiteDB, no `customData`.

## NPC linkage

None. Player-only feature, keyed by platform UserID.

## Gotchas / risks

- **Delimiter is `:` and only the first split matters via `Length == 2`** — a tag string that itself
  contains `:` (e.g. a timestamp or another `<...:...>` tag) will fail the length-2 check and be
  silently dropped. Keep tags colon-free.
- No comments / no headers — don't apply the shared skeleton parser here.
- Tag is prepended with a trailing space before the name; preserve that to match display exactly.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
