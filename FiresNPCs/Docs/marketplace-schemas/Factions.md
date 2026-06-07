# Factions — Marketplace config schema

The **only top-level YAML** config in Marketplace. Decompiled from
`Marketplace/Modules/Factions/Factions_Server.cs`, `Factions_DataTypes.cs`, `Factions_Client.cs`
(kg.Marketplace v9.8.1.0).

## Config location
- **File:** `BepInEx/config/Marketplace/Configs/Factions.yml` (`Market_Paths.FactionsFile`).
  Format: **YAML** via YamlDotNet (`new DeserializerBuilder().Build()`). Single file, not a folder
  merge.
- **Autoload / watch:** `[Market_Autoload(Server, Last, "OnInit", {"Factions.yml"}, {"OnChange"})]`.
  Watch key is the literal filename `"Factions.yml"`; `OnChange()` → `Load()` re-reads and re-syncs.
- Empty/whitespace file → factions cleared (empty dict). Any deserialize exception → cleared +
  logged (`Error while loading factions data: ...`). **Whole-file failure**, not per-entry.

## Section header / structure
Top-level YAML shape is a **map** `Dictionary<string, FactionYaml>` — each key is a faction **id**.
The server **lowercases the key** to form the runtime dictionary key
(`dictionary2[key.ToLower()] = ...`). A faction whose `Name` is null/whitespace is skipped with a
log. The key is what a player stores as their faction membership (see Persistence).

## YAML schema (`FactionYaml` → runtime `Faction`)
Per faction value (field names are PascalCase, matching the class fields; YamlDotNet default naming):

```yaml
bandits:                       # <- map key = faction id (lowercased at load)
  Name: "The Bandits"          # required (string); skip if empty
  Icon: "bandit_icon.png"      # optional (string, nullable); cached client image name
  SpecificUseItems:            # list of prefab NAMES (strings)
    - SwordIron
    - ShieldWood
  BlockPickupItems:            # prefab names the faction cannot pick up
    - Coins
  SpecificBuildings:           # prefab names this faction is allowed/able to build
    - wood_wall
  SpecificCraftItems:          # prefab names this faction may craft
    - ArmorBronzeChest
  FriendlyMonsters:            # prefab names of monsters that won't aggro this faction
    - Greyling
  BonusSkills:                 # map of SkillType -> int bonus
    Swords: 10
    Blocking: 5
  Buff: "RegenBuff"            # optional buff UniqueName (lowercased at load); applied as status effect
  CanDamageSameFaction: true   # default true; false = friendly fire off within faction
```

### Field types and transforms at load
- `Name` (string, **required**), `Icon` (string?, nullable).
- `SpecificUseItems`, `BlockPickupItems`, `SpecificBuildings`, `SpecificCraftItems`,
  `FriendlyMonsters`: YAML `HashSet<string>` of **prefab names**. At load each is converted to a
  `HashSet<int>` of **stable hashes** (`s.GetStableHashCode()`), dropping null/whitespace entries.
  → A drop-in reader must hash the same way to match the runtime `Faction` (which stores ints).
- `BonusSkills`: `Dictionary<Skills.SkillType, int>`. Keys are the **`Skills.SkillType` enum names**
  (e.g. `Swords`, `Bows`, `Blocking`, `Run`, `Jump`...). YamlDotNet parses the enum by name. Copied
  through unchanged.
- `Buff` (string?): a Buffer module `UniqueName`; **lowercased** at load and matched against
  `SyncedBufferBuffs`. Applied to the member as a `MPASN_FactionBuff` status effect.
- `CanDamageSameFaction` (bool, **default `true`** in `FactionYaml`): when false, same-faction PvP
  damage is blocked.

## Domain object — `Factions_DataTypes.Faction` (implements `ISerializableParameter`)
Runtime form (ints, not strings):
`Name(string), Icon(string?), SpecificUseItems/BlockPickupItems/SpecificBuildings/SpecificCraftItems/
FriendlyMonsters (HashSet<int> stable-hash), BonusSkills(Dictionary<Skills.SkillType,int>),
Buff(string?), CanDamageSameFaction(bool)`.
Distributed via `CustomSyncedValue<Dictionary<string, Faction>> SyncedFactions` (ServerSync key
`"SyncedFactions"`), keyed by the **lowercased faction id**.

## Persistence
- **Faction definitions:** the `Factions.yml` file only. No LiteDB. Rebuilt into `SyncedFactions`
  on every load and ServerSync-pushed to clients.
- **Per-player membership:** stored on the **player's saved character `customData`** under key
  **`"MPASN_Faction"`**, whose value is the **(lowercased) faction id string** (the map key, not the
  Name). It is also mirrored to the player's **ZDO** as an int **index** into the client-side
  `Factions` list under the same key `"MPASN_Faction"` (used by other clients for HUD/PvP checks via
  `GetInt("MPASN_Faction", -1)`). `Factions_Client.IsMyFaction(faction)` compares the customData
  string to the queried id; `FindMyFaction` resolves the customData id against `SyncedFactions` and
  applies the faction Buff. So a player's faction is **not** in the LiteDB DB.db — it rides on
  character customData (server-persisted with the character) + a transient ZDO index.

## NPC linkage
NPCs/characters carry the same **ZDO `MPASN_Faction` int index**; the EnemyHud and friendly-fire /
aggro logic read it to color the name plate and decide damage (`FriendlyMonsters`,
`CanDamageSameFaction`). A faction id also appears in TerritorySystem as a territory's
`OnlyForFaction` (lowercased) gate.

## Gotchas / risks for the parser
- Map keys are **lowercased** at load and that lowercased id is the membership value stored on the
  player. Match case-insensitively.
- Item/building/monster/craft lists are **prefab names in YAML but stable-hash ints at runtime** —
  hash with Valheim's `GetStableHashCode` to compare against synced data.
- `BonusSkills` keys are `Skills.SkillType` **enum names** (parsed by YamlDotNet); an unknown skill
  name throws and fails the **whole file** (load is wrapped in one try/catch, not per-faction).
- `Buff` and `OnlyForFaction` references are lowercased; the buff must exist in the Buffer module.
- `CanDamageSameFaction` defaults to **true** when omitted (set in `FactionYaml`), unlike a bare
  C# bool default.
- YamlDotNet default (no naming convention) → **PascalCase** field names in the YAML, exactly as the
  class fields. Do not assume camelCase/hyphenated keys.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
