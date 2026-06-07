# Teleporter — Marketplace config schema

## Config location
- Folder: `Configs/Teleporters/*.cfg` (recursive merge — `SearchOption.AllDirectories`, all files merged). Path constant `Market_Paths.TeleportHubProfilesFolder`.
- Autoload watch key / on-change handler: `[Market_Autoload(Server, Normal, "OnInit", new[]{"TE"}, new[]{"OnTeleporterProfilesChange"})]` — watch key `TE`.

## Section header
- `[name]` means: teleporter-hub **profile name** (the section key). Stored stripped of `[`, `]`, all spaces, **lowercased**. Default key before any header is `"default"`.
- `[name=bool]` flag means: **NOT SUPPORTED.** Like Banker, the header parser does NOT split on `=` — it strips brackets/spaces and lowercases the whole token. `[hub=true]` becomes the literal key `hub=true`. No per-section boolean.

## Data-line grammar
Two data-line forms, decided by prefix.

### Form A — import another profile's pins: line starts with `@from:`
- `fromProfile = line.Replace("@from:","").Replace(" ","").ToLower()`.
- Produces a `TeleporterData` with only `fromProfile` set (name/coords empty/0). Appended to the current section.
- At display time the client pulls in **all pins from the referenced profile** (`SyncedTeleporterData.Value[fromProfile]`) in addition to this profile's own pins. Lets one hub reuse another hub's destination set.

### Form B — a teleport destination pin: any other line (`.Split(',')`, NOT space-stripped first)
Fields by position:
- `tokens[0]` = **name** (the pin label). MAY embed an optional inline speed tag `<speed=N>`:
  - The parser finds `"<speed="`, reads up to the next `>`, `Convert.ToInt32` the inner value into `speed` (default 0 if tag absent or empty), then strips the whole `<speed=...>` rich-text tag from the name via `Utils.RemoveRichTextDynamicTag(input,"speed")`. The cleaned string becomes `name`.
  - `speed` drives travel-animation duration on the client; `speed <= 0` means instant teleport.
- `tokens[1]` = `x` = `int.Parse(tokens[1].Replace(" ",""))`
- `tokens[2]` = `y` = `int.Parse(tokens[2].Replace(" ",""))`
- `tokens[3]` = `z` = `int.Parse(tokens[3].Replace(" ",""))`
- `tokens[4]` (optional, **5 tokens**) = `sprite` = `tokens[4].Replace(" ","")`; if only **4 tokens**, `sprite = "none"`.
- Coordinates are **ints** (world position). `int.Parse` (not TryParse) → bad coord throws, caught per-line, logged `Can't add pins in {file}, line: {index}`, and the rest of that file is **`break`-skipped**.

NOTE: in Form B the line is **NOT** `.Replace(" ","")`-ed as a whole (unlike every other module); only the individual coord/sprite tokens are space-stripped. So `name` (token 0) MAY contain spaces.

### Example lines
```cfg
[spawn_hub]
Black Forest Portal,1500,30,-2200,PortalIcon
Swamp Entrance <speed=8>,-800,25,3400
@from:meadows_hub
```

## Domain object
- Synced container: `CustomSyncedValue<Dictionary<string, List<Teleporter_DataTypes.TeleporterData>>> SyncedTeleporterData` (ServerSync key `"teleporterData"`). Key = profile name; value = list of pins (and `@from` import markers).
- `TeleporterData` (implements `ISerializableParameter`):
  - `string name`, `string sprite`
  - `int x, y, z` (world coords)
  - `int speed`
  - `string fromProfile` (set only for `@from:` lines; empty otherwise)
  - Wire order: `name, sprite, x, y, z, speed, fromProfile`.

## Persistence
- Destinations: **config-file + ServerSync only** (`SyncedTeleporterData`). Not in LiteDB.
- No per-player Teleporter state persists — teleporting moves the live player; no cooldown/unlock data is read from the cfg or DB.

## NPC linkage
- Opened by `NPCType.Teleporter`. `OpenUIForType` reads `GET_NPC_Profile()` ZDO string, **lowercases** it, calls `Teleporter_Main_Client.ShowTeleporterUI(profile)` → opens the large map and creates a pin per `SyncedTeleporterData.Value[profile]` entry, plus pins from any `@from` referenced profiles.

## Gotchas / risks for the parser
- **Form B is the only module data-line that is NOT whole-line space-stripped** — `name` (token 0) can contain spaces. Only x/y/z/sprite tokens are individually `.Replace(" ","")`-ed. Do not blanket-strip spaces or you corrupt pin names.
- **No `=bool` flag** (like Banker): bracket contents become the literal key, `=` and all.
- `<speed=N>` is parsed out of token 0 and removed from the display name via `RemoveRichTextDynamicTag`. Handle the tag before treating token 0 as the name. Absent/empty tag → speed 0.
- Coords are `int.Parse` (not TryParse) → throws on non-int; the catch does `break`, skipping the rest of that file (note the log uses `line: {index}` — 0-based, unlike Trader/Gambler which use `index+1`).
- `@from:` value is lowercased and space-stripped; resolution is **lazy at display time** and silently ignored if the referenced profile doesn't exist (`...Where(... ContainsKey(x.fromProfile))`).
- Section keys lowercased; NPC lookup lowercased — match case-insensitively.
- Sprite `"none"` is the sentinel for "use default portal icon" (`AssetStorage.PortalIconDefault` via `Utils.TryFindIcon`).

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
