# QuestProfiles — Marketplace config schema

## Config location
- Folder: `Configs/QuestProfiles/*.cfg` (recursive merge — `Directory.GetFiles(QuestsProfilesFolder, "*.cfg", AllDirectories)`, each via `File.ReadAllLines`)
- `QuestsProfilesFolder` = `Configs/QuestProfiles` (`Market_Paths.cs`)
- Autoload watch key / on-change handler: same `[Market_Autoload(...)]` on `Quests_Main_Server` — watch key **`QP`** → handler `OnQuestsProfilesFileChange` → `ReadQuestProfiles()` → `SyncedQuestProfiles.Update()`. (No debounce, unlike the Quests `QD` key.)

## Section header
- Parsed in `ProcessQuestProfiles`. A line starting with `[` sets the current **profile key** = `line.Replace("[","").Replace("]","").Replace(" ","").ToLower()`. No `=bool` form is parsed for this folder — an `=` in the header is left literally in the key (not special-cased). Lines before any header default to profile key **`"default"`**.
- The profile key is what an NPC's ZDO `KGnpcProfile` string maps to (lowercased match).

## Data-line grammar
Each non-comment, non-header line is a **comma-separated list of quest names**:
```
questname1, questname2, questname3
```
Processing (per line): `line.Replace(" ","").Split(',')` → `.Distinct()`. For each token: `int hash = token.ToLower().GetStableHashCode()` and `hash` is **appended** to `SyncedQuestProfiles[profileKey]` (the list is created on first use). So:
- All spaces are stripped from the whole line first.
- Tokens are de-duplicated **within a single line** (via `Distinct()`), but **NOT across lines** — multiple lines under the same `[profile]` accumulate (and the same name on two different lines would be added twice).
- Each token is lowercased before hashing. These hashes must equal the quest-definition hashes (`Quests.md`: quest UID = lowercased `[name]` header hash) for the link to resolve.
- A profile may span multiple lines and/or multiple files (recursive merge); the lists concatenate.

### Example
```cfg
# blacksmith offers the early combat quests
[blacksmith]
wolf_cull, boar_hunt
deer_pelts

[default]
intro_quest
```
(`blacksmith` profile → quests {wolf_cull, boar_hunt, deer_pelts}. An NPC with no profile set falls back to `default`.)

## Domain object
There is **no dedicated class** — a QuestProfile is just an entry in:
`CustomSyncedValue<Dictionary<string, List<int>>> SyncedQuestProfiles` (ServerSync key `"questProfiles"`), where:
- **key** = profile name (lowercased string)
- **value** = `List<int>` of quest-name stable hashes (each → a `Quest` in `SyncedQuestData`/`AllQuests`)

Cleared and rebuilt wholesale on every `ReadQuestProfiles()`.

## Persistence
None of its own. QuestProfiles are pure server config synced to clients via `SyncedQuestProfiles`. **No per-player state** — which quests a player has accepted/finished lives in player `customData` (see `Quests.md` Persistence). The profile only defines the *menu* of quests an NPC offers.

## NPC linkage
1. A Quests NPC stores its profile name in its ZDO key **`"KGnpcProfile"`** (`GET_NPC_Profile()` defaults to `"default"`).
2. `Quest.HasQuests(npc)` / the quest UI does `SyncedQuestProfiles.Value.TryGetValue(npc.GET_NPC_Profile(), out List<int> questHashes)`.
   - NOTE: the NPC's stored profile string is matched **verbatim** against the dictionary key. Profile keys in this file were lowercased+space-stripped at parse time, so the NPC's `KGnpcProfile` value must already be in that normalized form to match (the NPC editor stores what the user typed).
3. Each hash → `AllQuests[hash]`; the NPC offers a quest only if `CanTake` (conditions pass) AND not already accepted AND not on cooldown.

## Gotchas / risks for the parser
- **No `=bool` semantics here** — don't apply the shared section-flag rule; an `=` stays in the key.
- Tokens are **lowercased then `GetStableHashCode()`** — the importer must reproduce Valheim's `GetStableHashCode` exactly and lowercase first, or links break silently (a missing hash just means "NPC offers nothing").
- `Distinct()` is **per-line only**; cross-line/cross-file duplicates accumulate. Faithful behavior = append, dedupe within the line only.
- Default profile key is `"default"` (also the NPC ZDO default), so headerless lines are not dropped — they attach to `default`.
- The list is the *order quests appear*; preserve insertion order.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
