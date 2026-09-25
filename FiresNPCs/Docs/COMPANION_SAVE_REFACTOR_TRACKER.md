# Companion Save System Refactor — Tracker

> **Status:** Planning ? Phase 1 in progress
> **Owner:** Fire (with Copilot)
> **Created:** 2026-04-27

---

## 1. Problem

The current companion save system uses **`VaultOfKnowledge` JSON files** as the source of truth, with the live ZDO acting as a runtime mirror that gets read/written back to JSON on a flush schedule.

This architecture has three concrete failure modes that are biting in actual play:

1. **Crash during respawn timer wipes companions.**
   When a companion dies, `CompanionDeathHandler` saves a death snapshot to the JSON, schedules a respawn timer in `CompanionRespawnManager`, and destroys the live ZDO. If the game crashes between the timer scheduling and the next vault flush — or between vault flushes generally — the in-memory respawn state is lost AND the ZDO is gone, so on next login the companion just doesn't exist anywhere. The player loses the companion permanently.

2. **Respawn from vault produces wrong name / wrong scale.**
   When the respawn timer fires, `CompanionRespawnManager.RestoreCompanionFromVault` reads from JSON. If the JSON read returns partial data (vault not fully loaded yet, key missing, race with another writer), the restored companion comes back with a default name like `"CompanionNpc"` and `localScale=(1,1,1)` instead of its real values. The companion is technically restored but cosmetically and functionally broken until manually re-equipped.

3. **JSON ? ZDO sync drift.**
   Many code paths write to one and not the other (or write to both in different orders), and the "winner" depends on which writer ran most recently. Bugs in this layer have been chased multiple times and keep returning under different shapes.

The root cause is shared across all three: **two sources of truth that are written to on independent schedules and can't be made atomic**.

---

## 2. Goals

- **Companion ZDO is authoritative for live runtime data** (name, scale, level, archetype, kills, equipment, follow target, etc.).
- **Player `m_customData` is authoritative for companion ownership and inactive-companion state** (the roster: which companions a player owns, who is following/staying/stationed, and the death snapshot for any companion currently on a respawn timer).
- **Vault JSON files become a read-only debug mirror** flushed periodically from the authoritative sources. They are not consulted for restore.
- **Server binding** so a character file moved into a different world doesn't drag companions with it.
- **Crash-safe respawn timers** — a companion on respawn cooldown survives a crash because the death snapshot lives in the player's character file (already saved by vanilla on every world save tick) and the respawn timer is recomputed from a wall-clock timestamp on next login.
- **Bug-fix as a side effect:** wrong-name / wrong-scale on respawn becomes structurally impossible because there is no fallback default — either the player's `m_customData` has a complete snapshot or no respawn happens at all.

## 3. Non-Goals

- Not changing the live combat/AI/idle behavior systems. Those already operate on `CompanionController` + ZDO and don't need to know the storage layer changed underneath.
- Not removing `VaultOfKnowledge` outright — it stays as the JSON debug view layer. Only its **role as the authority** changes.
- Not changing the wire protocol for multiplayer companion sync. ZDOs already handle that.
- No new external dependencies.

---

## 4. Architecture

### Data ownership matrix

| Data field                          | Old location           | New location                  |
| ----------------------------------- | ---------------------- | ----------------------------- |
| Companion name                      | JSON + ZDO mirror      | **ZDO** (authoritative)       |
| Companion scale                     | JSON + ZDO mirror      | **ZDO**                       |
| Equipment slots                     | JSON + ZDO mirror      | **ZDO**                       |
| Archetype, level, attributes        | JSON + ZDO mirror      | **ZDO**                       |
| Kill / death counters               | JSON + ZDO mirror      | **ZDO**                       |
| Storage inventory                   | JSON + ZDO mirror      | **ZDO**                       |
| Follow / stay / stationed state     | JSON                   | **Player `m_customData`**     |
| Companion-owned-by-player mapping   | JSON                   | **Player `m_customData`**     |
| Originating-server world UID        | (none)                 | **Player `m_customData`**     |
| Death snapshot (for pending respawn)| JSON                   | **Player `m_customData`**     |
| Respawn deadline (wall-clock UTC)   | In-memory only         | **Player `m_customData`**     |
| Read-only flat dump for debug       | JSON (authoritative)   | JSON (mirror only)            |

### Key insight

The player's character file (`.fch`) is **already crash-safe** — vanilla saves it on every world autosave and on logout. By piggy-backing roster + death-snapshot data onto `Player.m_customData`, we get atomic saves for free. There is no separate "vault flush" timing window to lose.

The companion ZDO is **also crash-safe** when alive — vanilla saves the world `.db` on autosave intervals, and the ZDO travels with it. When the companion is dead and on a respawn timer, the ZDO doesn't exist; that's exactly when we need the death snapshot to live on the player.

### Lifecycle

```
                    ???????????????????????????????
                    ?  COMPANION ALIVE (in world) ?
                    ?  ZDO holds full data        ?
                    ?  Player roster: alive=true  ?
                    ???????????????????????????????
                                 ? death
                                 ?
                    ???????????????????????????????
                    ?  COMPANION DEAD (timer)     ?
                    ?  ZDO destroyed              ?
                    ?  Player roster:             ?
                    ?    alive=false              ?
                    ?    pendingRespawn=true      ?
                    ?    deathSnapshot={...}      ?
                    ?    respawnDeadlineUtc=...   ?
                    ???????????????????????????????
                                 ? timer expires
                                 ?
                    ???????????????????????????????
                    ?  RESPAWN                    ?
                    ?  Spawn new ZDO              ?
                    ?  Apply deathSnapshot ? ZDO  ?
                    ?  Player roster:             ?
                    ?    alive=true               ?
                    ?    pendingRespawn=false     ?
                    ?    deathSnapshot=null       ?
                    ???????????????????????????????
```

---

## 5. Data model

### `PlayerCompanionRosterEntry` (one per owned companion)

```csharp
public class PlayerCompanionRosterEntry
{
    public string  CompanionId;           // stable across deaths
    public string  ServerWorldUid;        // ZNet.GetWorldUID() when first owned
    public CompanionFollowState FollowState; // Following | Staying | Stationed
    public bool    IsPendingRespawn;
    public long    RespawnDeadlineUtcTicks; // 0 if not pending
    public CompanionDeathSnapshot DeathSnapshot; // null unless IsPendingRespawn
}
```

### `CompanionDeathSnapshot`

A serialised dump of every field we'd otherwise pull from the live ZDO at respawn time. Same fields the current `CompanionData` JSON model has, minus the runtime-mirror duplicates we no longer need:

```csharp
public class CompanionDeathSnapshot
{
    public string PrefabName;        // CompanionNpc | CompanionNpc_Wild
    public string Name;
    public float  Scale;
    public bool   IsGiant, IsDwarf;
    public int    Level;
    public int    UnspentAttributes;
    public AttributeSpends Attributes;
    public string Archetype;
    public int    TotalKills, TotalDeaths;
    public string KillsByPrefab;     // packed "Greydwarf:5;Skeleton:2"
    public string SkillsPacked;
    public Dictionary<string, EquipmentSlotData> Equipment;
    public List<ItemData> StorageInventory;
    public string FactionTagPacked;  // wild origin marker if any
    // ... whatever else the existing vault snapshot stores; mechanical port.
}
```

### Storage on `Player.m_customData`

One single key holds the entire roster as JSON:

```
key:    "fires.va.companions.v1"
value:  {"entries": [...], "schemaVersion": 1}
```

Single key keeps it atomic from `m_customData`'s perspective and dodges the per-key 4 KB cap by storing the whole thing as one larger blob (m_customData values can hold tens of KB; the limit is the overall character file size, well within budget for ? 50 companions per player).

---

## 6. Phased implementation plan

Each phase is independently buildable and shippable. After each phase: `run_build`, smoke-test, then start the next.

### Phase 1 — Data model + storage helpers (no behavior change)

- [x] `PlayerCompanionRosterEntry`, `CompanionFollowState`, `PlayerCompanionRoster` types in `FiresNPCs/Modules/Companions/Vault/PlayerCompanionRoster.cs`.
- [x] `PlayerCompanionStorage` static helper in `FiresNPCs/Modules/Companions/Vault/PlayerCompanionStorage.cs`:
  - `TryGetRoster(Player) ? roster`
  - `GetRosterOrEmpty(Player)`
  - `GetEntry(Player, companionId)`, `FindEntry(roster, companionId)`
  - `SaveRoster(Player, roster)`
  - `UpsertEntry(Player, entry)`
  - `RemoveEntry(Player, companionId)`
  - `GetCurrentServerWorldUid()`, `BelongsToCurrentServer(entry)`
- [x] All read/write goes through `Player.m_customData["FiresRPGmaker_CompanionRoster_v1"]`.
- [x] Reuses existing `CompanionSaveData` shape as the death snapshot — no parallel data model, migration is a copy.
- [x] Schema versioning via `PlayerCompanionRoster.SchemaVersion`; unknown versions are refused on read with a warning, leaving the stored blob intact for forensic inspection.
- [x] Pending-respawn entries with null snapshots are refused at the `UpsertEntry` boundary — the contract that makes the wrong-name/wrong-scale bug structurally impossible.
- [x] Build clean. **No call sites yet** — dead code, ships safely alongside existing vault flow.

### Phase 2 — ZDO snapshot helpers

- [x] `CompanionZdoSnapshot.Capture(CompanionController) ? CompanionSaveData` in `FiresNPCs/Modules/Companions/Vault/CompanionZdoSnapshot.cs`.
- [x] `CompanionZdoSnapshot.Apply(CompanionController, CompanionSaveData) ? bool`.
- [x] `CompanionZdoSnapshot.StoreDeathSnapshotOnPlayer(...)` convenience for the death flow (capture + upsert in one call).
- [x] `CompanionZdoSnapshot.ComputeRemainingRespawnSeconds(deadlineTicks)` for crash-safe timer resume on Phase 4 login.
- [x] **Thin facade** over the existing `CompanionVault.BuildSaveData` / `CompanionVault.RestoreCompanion` pure-function pair. Phase 6 cleanup can rewrite the field-copy logic without touching call sites.
- [x] **Refuses to Apply with a null/no-id snapshot** — the contract that prevents the wrong-name/wrong-scale bug.
- [x] Build clean. **No call sites yet.**

### Phase 3 — Roster writers cover the full companion lifecycle (vault still mirrored)

**Intent:** make the roster authoritative for *all* owned companions, not just dying ones. The roster must hold an entry for every companion the player owns, in any state, so that Phase 4 can flip reads over without falling back to vault.

The roster is **only being written to** in this phase. Reads still go through the existing JSON vault. Both layers run side-by-side.

#### Data-model addition

- [x] **`CompanionFollowState.Dismissed`** added. Companions in this state are OUT of the world (live ZDO destroyed) but the roster entry is preserved with a complete snapshot for recall. Crucially distinguishes Stay (in world) from Dismiss (out of world but recallable) from Remove (wiped — only the roster screen Remove button does this).

#### Writer chokepoint

- [x] **`CompanionRosterWriter`** static facade in `FiresNPCs/Modules/Companions/Vault/CompanionRosterWriter.cs`. Every lifecycle event routes through one of its named methods. Hooks the policy in one file: ServerWorldUid stamping, FollowState preservation rules, snapshot capture timing, etc.
  - `OnFirstOwned`, `OnFollowCommand`, `OnStayCommand`, `OnStationedAsNpc`
  - `OnDismissed`, `OnRecalled`, `OnRecallIntent` (no-companion-handle variant for async respawn)
  - `OnDeath` (auto-capture) + `OnDeathWithSnapshot` (caller-mutated snapshot, used by death handler that clears equipment dropped to tombstone)
  - `OnRespawned` (preserves prior FollowState so a Staying-and-pending companion comes back as Staying)
  - `RefreshLiveSnapshot` (logout / periodic flush — doesn't touch FollowState or IsPendingRespawn)
  - `OnRosterRemove` — the ONLY method that calls `RemoveEntry`

#### Wired sites

| Lifecycle event                        | File hooked                                                          | Method called                                |
| -------------------------------------- | -------------------------------------------------------------------- | -------------------------------------------- |
| Companion first owned (taming)         | `CompanionController.TameCompanion`                                  | `OnFirstOwned`                               |
| `CommandFollow` (controller-level)     | `CompanionController.CommandFollow`                                  | `OnFollowCommand`                            |
| `CommandStay` (controller-level)       | `CompanionController.CommandStay`                                    | `OnStayCommand`                              |
| Stay command (radial / ping system)    | `CompanionCommandSystem.CommandCompanionStay`                        | `OnStayCommand`                              |
| Follow command (radial / ping system)  | `CompanionCommandSystem.CommandCompanionFollow`                      | `OnFollowCommand`                            |
| `ForceDismiss`                         | `CompanionController.ForceDismiss`                                   | `OnDismissed` (before destroying live ZDO)   |
| Stationed as NPC                       | `CompanionNpcModule.StationAtPosition`                               | `OnStationedAsNpc`                           |
| Unstationed (return to companion)      | `CompanionNpcModule.Unstation`                                       | `OnFollowCommand` (state ? Following)        |
| Death                                  | `CompanionDeathHandler.SaveDeathState`                               | `OnDeathWithSnapshot` (post-mutation snap)   |
| Respawn success                        | `CompanionRespawnManager` (post-restore vault save)                  | `OnRespawned` (preserves FollowState)        |
| Recall (intent before async spawn)     | `CompanionRosterScreen.RecallCompanion` start                        | `OnRecallIntent` (Dismissed ? Following)     |
| Recall (live-existing companion)       | `CompanionRosterScreen.RecallCompanion` alive-existing branch        | `OnRecalled`                                 |
| Roster screen Remove                   | `CompanionRosterScreen.RemoveCompanion`                              | `OnRosterRemove` — ONLY Remove site          |
| Logout                                 | `CompanionPatches.DestroyPlayerCompanionsOnLogout` per-companion     | `RefreshLiveSnapshot`                        |

#### Notes & deferred items

- **Periodic in-world flush** is NOT wired in this phase. `CompanionVault.Tick()` exists but is dead-code in bootstrap (TBD whether to revive it for the periodic refresh or add a new tick host — not needed for crash-safety because logout already flushes, and Phase 4 reads are robust to slightly-stale snapshots while alive).
- **Stale-by-design while alive.** The live ZDO is the freshest copy of a live companion's state. The roster snapshot is a backup refreshed at transition points + (eventually) the periodic tick. The snapshot becomes authoritative only when `IsPendingRespawn=true`, when `FollowState=Dismissed`, or post-logout.
- **Vault is still the read source** until Phase 4 — if a roster write fails for any reason, the existing vault entry is still there as a fallback. We can ship Phase 3 standalone without breaking the existing flow.
- **Build clean** after every wired site.

### Phase 4 — Read-side switch: roster becomes the load source

**Intent:** flip the read paths over to the roster. Vault is no longer consulted for restore decisions; it's only being kept current as a debug-mirror writer until Phase 6 retires it.

#### Resolver layer

- [x] **`CompanionSavedDataResolver`** static helper in `FiresNPCs/Modules/Companions/Vault/CompanionSavedDataResolver.cs`. Single point of contact for "where do I read companion save data from?". Two methods:
  - `ResolveAllForPlayer(ownerPlayerId)` — used by login restore. Excludes `Dismissed` entries (they don't auto-spawn). Filters by current server world UID. Falls back to vault if roster is empty/unavailable.
  - `ResolveByCompanionId(ownerPlayerId, companionId)` — used by per-companion lookups. Includes `Dismissed` entries (recall flow needs them). Same server-binding filter and vault fallback.
  - Both methods translate the roster's wall-clock `RespawnDeadlineUtcTicks` into the snapshot's `RespawnTimeRemaining` so downstream code sees consistent fields regardless of source.

#### Auto-promote rule (cleanup task)

- [x] `OnRespawned` now auto-promotes `CompanionFollowState.Dismissed` ? `Following` when a respawn fires for a Dismissed entry. Recall is just a respawn from the player's perspective; the writer encodes the rule. **Removed:** `OnRecallIntent` (no longer needed) and the pre-spawn flip in `CompanionRosterScreen.RecallCompanion`.

#### Wired read sites

| Read site                                                       | Source                                                              |
| --------------------------------------------------------------- | ------------------------------------------------------------------- |
| `CompanionPatches.RestoreCompanionsFromVault` (login restore)   | `ResolveAllForPlayer`                                               |
| `CompanionRespawnManager.RespawnCompanion` (death respawn)      | `ResolveByCompanionId`                                              |
| `CompanionRespawnManager.RestoreCompanionFromVaultData`         | `ResolveByCompanionId` (initial + retry)                            |
| `CompanionRespawnManager.RespawnCompanionAtPosition` (resurrect)| `ResolveByCompanionId`                                              |
| `CompanionRespawnManager.TryRespawnFromVault`                   | `ResolveByCompanionId` (per player)                                 |
| `CompanionController` glitch-recovery follow check              | `ResolveByCompanionId`                                              |
| `CompanionController.RestoreFromVault` equipment restore        | `ResolveByCompanionId`                                              |

#### Crash-safe pending respawn

- [x] `CompanionRespawnManager.RestoreCompanionsForPlayer` pending-respawn branch now uses `RespawnTimeRemaining` (which the resolver computed from the wall-clock deadline). Three behaviours:
  - `remaining > 0.5s` ? `ScheduleRespawn(remaining)`. **This is the actual crash-safety payoff:** a companion with 90s left on its timer when the game crashed comes back ~90s after login, not instantly with full HP.
  - `remaining ? 0.5s` ? spawn immediately (legacy behaviour for expired timers).
  - No `RespawnTimeRemaining` field (vault-fallback source from before Phase 3) ? spawn immediately, same as legacy.

#### Server binding (was Phase 5, folded into Phase 4)

- [x] `PlayerCompanionStorage.BelongsToCurrentServer` filter is active on every roster read in the resolver. Companions stamped with another server's world UID are silently skipped — character files moved between worlds do not drag companions along. Permissive when either side's UID is unknown (so single-player and pre-network-ready spawns aren't denied).

#### Notes

- **Vault still being WRITTEN** in the side-by-side window. Phase 6 retires the vault writers; until then, both layers stay current.
- **`ClearRespawnStateInVault` and the logout-safety vault read in `CompanionPatches`** intentionally still touch the vault directly — they're maintaining the legacy mirror, not making restore decisions. Both retire in Phase 6.
- **Build clean** after every wired site.

### Phase 5 — Server binding

- [ ] On `UpsertEntry`, stamp `ServerWorldUid = ZNet.GetWorldUID()` if the entry is new.
- [ ] On respawn / restore, if `entry.ServerWorldUid != current world UID`, skip restore and log a one-line warning. The companion stays in the player's home-server roster; visiting a different server does not duplicate or corrupt it.

### Phase 6 — Vault becomes read-only debug mirror

**Intent:** retire the vault as a runtime authority. The vault JSON is now
refreshed *exclusively* by a periodic flush from the authoritative sources
(live ZDOs + per-player roster). No hot-path code writes to the vault on
death / respawn / login / logout / level-up / follow events; no hot-path
code reads the vault outside the resolver.

#### Periodic flush

- [x] `CompanionVault.FlushDebugMirror()` — walks every live `CompanionController` (authoritative ZDO state), writes a fresh vault entry per companion, then walks the local player's roster for Dismissed / IsPendingRespawn entries and mirrors their stored `Snapshot` (with `RespawnTimeRemaining` recomputed from the wall-clock deadline) for any companion not covered by the live walk.
- [x] `CompanionVault.FlushPlayerToVault(Player)` — same logic scoped to one player. Wired into the logout flow so the on-disk debug mirror is current the moment before a player's companions despawn (the periodic flush won't tick again for a logged-out player).
- [x] `CompanionVault.Tick()` repurposed — was `SaveAllDirty`-based, now drives `FlushDebugMirror` on the existing 30 s `DIRTY_FLUSH_INTERVAL`.

#### Retired writers (per-event vault saves)

| Site                                                                     | What it called                          | Replacement                                                                 |
| ------------------------------------------------------------------------ | --------------------------------------- | --------------------------------------------------------------------------- |
| `CompanionController.SaveCompanionToVault`                               | `CompanionVault.SaveCompanion`          | No-op (kept compiling for back-compat). Periodic flush handles the mirror. |
| `CompanionController.SaveFollowStateToVault`                             | `CompanionVault.SaveCompanion`          | No-op. Persistent intent already lives on `PlayerFollowingRegistry`.       |
| `CompanionDeathHandler.SaveDeathState`                                   | `CompanionVault.SaveCompanionData`      | Roster mirror via `OnDeathWithSnapshot` (already wired in Phase 3).        |
| `CompanionPatches.DestroyPlayerCompanionsOnLogout` per-companion save    | `CompanionVault.SaveCompanion`          | Single `FlushPlayerToVault` call before the destroy loop.                  |
| `CompanionPatches` logout-safety vault read                              | `VaultOfKnowledge.Instance.GetCompanions` | Removed. Roster on `m_customData` is authoritative; the loop is now a no-op when there are no live companions to destroy. |
| `CompanionRespawnManager.RestoreCompanionFromVaultData` post-restore save| `CompanionVault.SaveCompanion`          | Removed. `OnRespawned` roster write is the authoritative signal.           |
| `CompanionRespawnManager.RespawnCompanion` post-respawn save             | `CompanionVault.SaveCompanion`          | Removed. Same as above.                                                    |
| `CompanionRespawnManager.ClearRespawnStateInVault`                       | `GetCompanion` + `SaveCompanionData`    | Method **deleted**. Roster's `IsPendingRespawn=false` is set by `OnRespawned`; the periodic flush rebuilds the vault entry without the pending fields. |
| `CompanionAttributeScreen.OnAttributeUpClicked / OnResetClicked`         | `CompanionVault.SaveCompanion`          | Removed. Live ZDO is authoritative for progression; periodic flush mirrors it. |
| `CompanionRosterScreen.RecallCompanion` post-recall vault clear          | `CompanionVault.SaveCompanionData`      | Replaced with row-local field reset; roster authority is updated by `OnRespawned`. |

#### Retired readers (vault as restore source)

- [x] `CompanionRespawnManager.RestoreCompanionsForPlayer` (the legacy `PlayerFollowingRegistry` migration / self-heal path) now reads via `CompanionSavedDataResolver.ResolveAllForPlayer` (roster first, vault fallback). Was the last hot-path direct vault read.
- [x] `CompanionRosterScreen.PopulateCompanionList` reads via the new `CompanionSavedDataResolver.ResolveAllForRosterUI` — like `ResolveAllForPlayer` but **includes Dismissed entries** so the UI can show recall-eligible companions. Vault fallback retained for migration safety.

#### Marked `[Obsolete]` on `CompanionVault`

Public APIs that wrote / read the vault outside the flush:

- `SaveCompanion`, `SaveCompanionData`, `MarkDirty`, `SaveAllDirty`
- `GetCompanions`, `GetCompanion`
- `RemoveCompanion`

Bodies still compile and function (so any not-yet-migrated caller doesn't break), but every call site generates a `CS0618` warning pointing the caller at the resolver / writer / flush facades. Internal cross-references inside the class itself are suppressed via `#pragma warning disable CS0618`.

The pure helpers `BuildSaveData` and `RestoreCompanion` are **not** obsolete — they're reused by `CompanionZdoSnapshot` (Phase 2 facade) and by the periodic flush itself.

#### Build clean

After every wired site.

### Phase 7 — Migration

**Intent:** seamless one-shot import for players who already had companions in the legacy JSON vault before the refactor shipped. After Phase 7 every player's roster is the authoritative source on disk; the vault stays as a debug mirror written by Phase 6's flush.

#### Migrator

- [x] `PlayerCompanionMigrator.MigrateFromVaultIfNeeded(Player)` in `FiresNPCs/Modules/Companions/Vault/PlayerCompanionMigrator.cs`.
- [x] Gate is **roster empty AND vault non-empty** — no separate "migrated" flag is stored, the natural state of the roster is the gate. Once any entries are written the migrator self-skips on the next login.
- [x] Per-session de-dup (`_migratedThisSession` HashSet of player ids) so a single login that triggers the restore coroutine through multiple paths only runs the import once.

#### Mapping rules (vault ? roster)

| Legacy vault field                 | Roster entry field                                                |
| ---------------------------------- | ----------------------------------------------------------------- |
| `CompanionId`                      | `CompanionId` (verbatim)                                          |
| —                                  | `ServerWorldUid` ? current `ZNet.GetWorldUID()` (best we have)    |
| `IsStationedAsNpc=true`             | `FollowState = Stationed`                                         |
| `IsFollowing=true` (and not stationed) | `FollowState = Following`                                       |
| else                               | `FollowState = Staying` (safe "in-world but parked" default)      |
| `IsPendingRespawn`                 | `IsPendingRespawn` (verbatim)                                     |
| `RespawnTimeRemaining` (seconds)   | `RespawnDeadlineUtcTicks = UtcNow.Ticks + remaining` (lossy at the migration boundary — accepted) |
| Whole `CompanionSaveData`          | `Snapshot` (re-used directly; the contract that pending entries must carry a snapshot is satisfied by construction) |
| —                                  | `LastUpdatedUtcTicks = UtcNow.Ticks`                              |

There is no Dismissed equivalent in the legacy schema, so no entry is created in that state during migration. Players can still `Dismiss` post-migration via the radial / roster screen and the writer will flip the entry over.

#### Wired site

- [x] `CompanionPatches.RestoreCompanionsFromVault` runs `MigrateFromVaultIfNeeded(player)` immediately before `CompanionSavedDataResolver.ResolveAllForPlayer`. The migration completes synchronously so the resolver sees the fresh roster on the same login that performed the import.

#### Failure handling

- All exceptions caught and logged; on failure the per-session de-dup is rolled back so the next login re-attempts.
- A failed `SaveRoster` write also rolls back the de-dup. The vault remains the resolver's fallback during the retry window so the player is never dead-in-the-water.

#### Build clean.

### Phase 8 — Cleanup

**Intent:** delete the dead vault-as-authority code that the side-by-side phases left compiling, fix two regressions surfaced by play-testing the new flow, and confirm `WILD_COMPANION_SPAWN_PLAN.md` doesn't describe stale architecture.

#### Deletions in `CompanionVault`

- [x] `SaveCompanion`, `SaveCompanionData`, `MarkDirty`, `SaveAllDirty`, `GetCompanions`, `GetCompanion`, `RemoveCompanion` — all of the public `[Obsolete]` API surface from Phase 6 is gone now that no production callers remain.
- [x] Private helpers backing them: `ShouldSave`, `FindCompanionById`.
- [x] Backing state: `_lastSaveTime`, `_dirtyCompanions`, `_saveLock`, `MIN_SAVE_INTERVAL`, `MAX_COMPANIONS_PER_PLAYER`.
- [x] `SaveReason` enum (no external referencers after the public API went away).
- [x] `#pragma warning disable CS0618` blocks (no longer needed without the obsolete cross-references).
- [x] Stray `using System.Linq` (only used by the deleted `SaveAllDirty`).

What stays: `BuildSaveData` + `RestoreCompanion` (used by `CompanionZdoSnapshot` and the periodic flush), `ValidateSaveData` / `GetPrefabName` helpers, the entire `FlushDebugMirror` / `FlushPlayerToVault` / `Tick` machinery, and all the `Build*Data` / `Restore*` private helpers that drive the snapshot.

#### Bug fixes from play-testing the new flow

##### GroupHud disappears when an only-companion dies until respawn

- [x] `GroupHudController.UpdatePendingRespawns` was reading from `VaultOfKnowledge.Instance.GetCompanions(playerId)` directly. With Phase 6 the vault is only refreshed every `DIRTY_FLUSH_INTERVAL` (30 s) — so for up to 30 s after a death, the vault has no `IsPendingRespawn=true` entry yet and the HUD entry vanished.
- [x] Re-routed through `CompanionSavedDataResolver.ResolveAllForRosterUI(playerId)`, which prefers the per-player roster (updated synchronously on death by `CompanionRosterWriter.OnDeathWithSnapshot`).
- [x] Also fixed the `RespawnTimeRemaining` calculation: the resolver already projects the wall-clock `RespawnDeadlineUtcTicks` into seconds-remaining, so the legacy `RespawnTimeRemaining - (now - DeathTimestamp)` subtraction was double-counting and zeroing every entry. Now the HUD just uses `data.RespawnTimeRemaining` directly.

##### `ShouldFlee` spam + Combat?Fleeing flap at 100 % HP / 100 % stamina post-respawn

- [x] Added `MIN_FLEE_REENTRY_DELAY = 1.5s` cooldown — after exiting flee, the AI can't re-enter via the `UpdateCombatState` path until the cooldown expires. `OnDamaged` deliberately bypasses the gate so a real new damage event still triggers immediate flee.
- [x] Stamped `_lastFleeExitTime = Time.time` on every flee-exit branch (`no active threats`, `health/stamina recovered`, `flee timeout`).
- [x] Rate-limited the `transitioning to Fleeing!` log to once per second via `FLEE_LOG_INTERVAL` so a stuck threat-analyzer no longer floods BepInEx output at 60 Hz.
- [x] Replaced the bare `ShouldFlee=true` log with a structured reason string (`GetFleeReason()` + private `_lastFleeReason` field). Each branch in `ShouldFlee` now records *why* it returned true (`critical-stamina-recovery`, `stamina-emergency`, `health-below-flee-threshold`, `threat-analyzer-retreat`, `boss-low-hp`, `multi-dangerous-low-hp`). Next play-test session will surface the actual underlying cause of the wobble.

##### Misleading `[CompanionDeathHandler] SaveDeathState result: False` log

- [x] Removed the leftover Phase 6 placeholder log line. The `saved` boolean now captures the roster mirror's return from `OnDeathWithSnapshot` and is only printed inside the existing `VerboseLogging` gate.

#### Docs

- [x] `WILD_COMPANION_SPAWN_PLAN.md` audit — no stale vault-as-source references; nothing to change.

#### Out of scope

- `ZNetSceneStaleInstanceDiagnostic.cs` retained. The original NRE flood it was hunting is a separate concern from the save-flow refactor; without a play-test confirming the spam has stopped post-Phase-6 we don't have evidence to delete it. Tagged for separate investigation.
- 2026-09-17: `ZNetSceneStaleInstanceDiagnostic.cs` and its companion `CharacterDeathCascadeDiagnostic.cs` (`va_orphan_stats`) deleted in FiresRPGmaker 0.0.443. No STALE line appeared in any current log, and the sweep walked every ZNetScene instance twice per CreateDestroyObjects pass (see Toolsull_modstack_run_20260917.md).
- `CompanionPatches.ClearRespawnStateInVault` and `CompanionRosterScreen` direct vault Remove call retained. Vault is a debug mirror; the next periodic flush rebuilds the JSON from authoritative state, so these immediate-clear sites are at most "make the on-disk dump current right now" optimisations. Low-risk to leave; can be retired in a follow-up if desired.

#### Build clean.

---

## 7. Risks & open questions

- **`Player.m_customData` blob size.** A roster with full death snapshots could push 50–100 KB on a player with many dead companions. Vanilla survives this (`m_customData` is part of the `.fch` file which already holds whole inventories), but worth measuring after Phase 3 to confirm save times stay reasonable.
- **Multiplayer ownership transfers.** Phase 5's server-binding is the right default but we should make sure it doesn't accidentally lock a companion to a server when the host migrates the world (e.g. dedicated ? P2P or vice versa). May need a `companionId ?` allowlist override.
- **Existing in-flight `IsPendingRespawn` in `VaultOfKnowledge`.** During the migration window, a player who logs in mid-Phase-3 deployment with a pending respawn in the OLD vault must not lose it. Phase 7's migration covers this, but the order of phase rollout matters — Phase 7 can ship before Phase 6 to be safe.
- **`Player.m_customData` namespace collisions.** Other mods write to it. We use a single dotted-prefixed key (`fires.va.companions.v1`) to minimise overlap risk. If a future schema change is needed, bump the suffix to `v2` and migrate.

---

## 8. Status log

| Date       | Phase | Status                                                                                                  |
| ---------- | ----- | ------------------------------------------------------------------------------------------------------- |
| 2026-04-27 | —     | Plan drafted                                                                                            |
| 2026-04-27 | 1     | Done — data model + storage helper, build clean                                                        |
| 2026-04-27 | 2     | Done — ZDO snapshot facade, build clean                                                                |
| 2026-04-27 | 3     | Done — Dismissed state added, RosterWriter facade, all 13 lifecycle sites wired, build clean           |
| 2026-04-27 | 4     | Done — resolver, 7 read sites wired, crash-safe respawn timer, server binding, auto-promote, build clean |
| 2026-04-27 | 5     | **Folded into Phase 4** — server binding live in resolver. No separate work needed.                    |
| 2026-04-27 | 6     | Done — periodic FlushDebugMirror + FlushPlayerToVault, all per-event vault writes retired, vault reads routed through resolver, legacy `CompanionVault.Save*/Get*/Remove*` marked `[Obsolete]`, build clean |
| 2026-04-27 | 7     | Done — `PlayerCompanionMigrator.MigrateFromVaultIfNeeded` wired into login restore, gate is "roster empty AND vault non-empty", per-session dedup, build clean |
| 2026-04-27 | 8     | Done — obsolete `CompanionVault.Save*/Get*/Remove*` API + backing dirty-tracking deleted; GroupHud now reads roster (no 30s blackout); ShouldFlee re-entry cooldown + reason diagnostic; misleading death-handler log removed; build clean |
