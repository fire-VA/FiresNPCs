# CustomSpawnData — Marketplace schema

A `CustomSpawnData` file is a **named bag of typed ZDO key/value pairs**. It does NOT spawn anything by
itself — it is a reusable data payload referenced **by key** from dialogue commands/conditions. When a
`*WithData` command spawns a prefab (or gives an item, or writes player data), these key/values are applied to
the target's ZDO. The "spawn" happens in the dialogue command; this file only supplies the ZDO overrides.

Source: `Marketplace/Modules/Dialogues/Dialogues_Main_Server.cs` (`ReadCustomSpawnData`),
`Dialogues/Dialogues_DataTypes.cs` (`CustomSpawnZDO`). Consumers are the `Spawn*WithData` / `GiveItemWithData`
/ `SetPlayerData` commands and the `PlayerHas…CustomDataKeys` conditions (see `Dialogues.md`).

---

## Location / how loaded

- **Folder:** `BepInEx/config/Marketplace/Configs/CustomSpawnData/*.yml`
  (`Market_Paths.DialoguesCustomSpawnDataFolder`), recursive
  (`Directory.GetFiles(folder, "*.yml", SearchOption.AllDirectories)`). **Only `*.yml`** (not `.yaml`).
- **Parser:** YamlDotNet `new DeserializerBuilder().Build()` → `Deserialize<CustomSpawnZDO>(fileText)`.
  One file = one `CustomSpawnZDO`. **The key is the filename without extension, lowercased**
  (`Path.GetFileNameWithoutExtension(path).ToLower()`), stored in
  `CustomSpawnZDOData_Synced` (`Dictionary<string, CustomSpawnZDO>`, a `CustomSyncedValue`).
- Server-only autoload (watch key `"DSCD"` → `OnCustomSpawnDataChange` re-reads on file change).
- **Error handling:** any file that throws aborts the *entire* read (`return;`) — one malformed file drops
  the whole dictionary for that load. (Stricter than the `.cfg` per-line skip.)

---

## Data model / grammar (`CustomSpawnZDO`)

Five optional dictionaries, one per ZDO value type. All may be omitted/null. Keys are the **target ZDO field
names** (raw strings — they become `zdo.Set(key, value)` calls), values are the typed value to write.

| YAML field | C# type                   | written via |
|------------|---------------------------|-------------|
| `Ints`     | `Dictionary<string,int>`  | `zdo.Set(key, int)` |
| `Floats`   | `Dictionary<string,float>`| `zdo.Set(key, float)` |
| `Longs`    | `Dictionary<string,long>` | `zdo.Set(key, long)` |
| `Strings`  | `Dictionary<string,string>`| `zdo.Set(key, string)` |
| `Bools`    | `Dictionary<string,bool>` | `zdo.Set(key, bool)` |

(`ISerializableParameter` wire order, for reference: Ints, Floats, Strings, Bools, Longs — each prefixed by a
presence-bool then a count.)

### Example file `Configs/CustomSpawnData/elite_boss.yml`

```yaml
Ints:
  level: 3
  CustomBossFlag: 1
Floats:
  Health: 5000.0
Strings:
  TamedName: "Old One"
Bools:
  HasFields: true
Longs:
  creator: 0
```

Referenced from a dialogue option as:

```cfg
Text:Summon the boss | Command:SpawnXYZWithData,Skeleton,1,2,100,40,200,5,elite_boss
```

Here `elite_boss` is the filename key (lowercased); the spawned `Skeleton` ZDO gets `level=3`,
`CustomBossFlag=1`, `Health=5000`, etc. set before `Character.SetLevel` / `ItemDrop.SetQuality`.

---

## What it spawns

Nothing on its own. It is consumed by these dialogue commands/conditions (see `Dialogues.md`):

- **`SpawnWithData prefab count quality key`** — spawn near player, then apply all 5 dicts to each spawned ZDO.
- **`SpawnXYZWithData prefab count quality x y z spread key`** — spawn at coords, apply dicts.
- **`GiveItemWithData prefab count level key`** — add item to player, item ZDO gets the dicts.
- **`SetPlayerData key`** — apply the dicts directly to `Player.m_localPlayer.m_nview.m_zdo`.
- **Conditions `PlayerHasAllCustomDataKeys key reason` / `PlayerHasOneOfCustomDataKeys key reason`** — compare
  the dicts against the player's ZDO (floats compared with 0.01 tolerance; "All" = every KV must match,
  "OneOf" = passes as soon as any KV differs).

For Character spawns, `SetLevel(max(1, quality+1))` and (for ItemDrops) `SetQuality(quality)` +
`LoadFromExternalZDO` run **after** the dict overrides on `SpawnWithData`, but `SpawnXYZWithData` applies the
dicts first then SetLevel/SetQuality — minor ordering difference between the two commands.

---

## Linkage

- **Filename (lowercased, no extension) = the lookup key** used by every `*WithData` command and the two
  `…CustomDataKeys` conditions. There is no `[section]` header inside the file.
- The dict **keys** are arbitrary ZDO field names interpreted by whatever prefab/player consumes them — they
  are not validated against any schema. Used for vanilla fields (`level`, `Health`, …) or other mods' ZDO keys.

## Gotchas / risks

- **`.yml` only** — a `.yaml` extension in this folder is ignored (unlike `Marketplace_SavedNPCs`, which reads
  both).
- **All-or-nothing load**: one bad YAML file aborts the whole CustomSpawnData read for that cycle.
- **Lookup is case-insensitive on the key** (filename `.ToLower()`), and dialogue commands also `.ToLower()`
  the referenced key — so `Elite_Boss.yml` is reachable as `elite_boss`. Dict keys/values are NOT lowercased.
- Floats deserialize per the invariant YAML numeric format; comparison in conditions uses a 0.01 epsilon.
- This shares the `Dialogues` module/folder tree but is a **separate YAML system** from the dialogue `.cfg`
  grammar — different parser, different sync value (`CustomSpawnZDOData_Synced`).

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
