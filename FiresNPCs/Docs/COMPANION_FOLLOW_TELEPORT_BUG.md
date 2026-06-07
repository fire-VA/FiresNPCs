# Companion Follow / Teleport Regression — Investigation Notes

**Status:** Diagnosis only. No fix attempted. Awaiting more repro data from the user.

**Date opened:** 2026-05-10
**Reporter:** travi (via interactive session)

---

## Reported Behaviour

Symptoms observed in a recent build:

- Companions in **Follow** state will sometimes "plain not follow" — they stand still and do nothing.
- When the player walks far enough away, the companion is **NOT** auto-teleported to catch up. Instead the companion is just **removed from the Group HUD**.
- Re-entering the same area re-adds them to the Group HUD (i.e. they're still alive and ZDO-loaded once the zone re-streams in).
- Sometimes self-heals if the player **re-issues Follow**.
- Sometimes self-heals on **logout / login** — companions teleport to the player and follow correctly again.
- User describes this as "a very recent issue" — likely a regression, not historical.

### What is NOT yet known

- Whether the hover-text state on the stranded companion still says `(Following)` (player is 100m+ away when it happens, hard to read it).
- Whether the bug fires on:
  - Every zone transition (walking across boundary)?
  - Fast-travel / portal teleports specifically?
  - Player death + respawn?
  - All of the above?
- Whether the companion is internally still in `Following` movement intent or has flipped to `Stay`.
- Whether this correlates with any specific server/client topology (single-player vs dedicated, etc.).

---

## Recent Context — UI Standalone Canvas Refactor

Immediately before this report, the NPC interaction UIs were refactored to use **standalone top-level Canvases** (`renderMode=ScreenSpaceOverlay`) instead of being parented under Hud's child canvas. The refactor fixed a 150fps → 4fps regression caused by inventory-grid dirty cascades.

Files touched:

- [UIInitializer.cs](../UI/UIInitializer.cs) — `CreatePanelInstance` now creates `FiresNPCs_NpcPanelCanvas` (sortingOrder 200) and parents the codegen Root under it. `ResetInitialization` explicitly destroys `_panelInstance` (the canvas). Calls `TraderUIController.Cleanup()` and `InfoNpcPlayerPanel.Cleanup()`.
- [NpcMainPanelController.cs](../UI/NpcMainPanelController.cs) — `EnsureCanvasSetup` / `EnsurePanelVisibility` no longer reparent into Hud canvas.
- [TraderUIController.cs](../UI/Screens/TraderUIController.cs) — own `TraderUI_Canvas` (sortingOrder 210), `_standaloneCanvas` field, OnDestroy + static Cleanup.
- [InfoNpcPlayerPanel.cs](../UI/Screens/InfoNpcPlayerPanel.cs) — own `InfoNpcPlayerPanel_Canvas` (sortingOrder 210).
- [CompanionRosterScreen.cs](../Modules/Companions/UI/CompanionRosterScreen.cs) — `PopulateCompanionList` now clears ALL children of `_contentParent` (was only clearing `_rows`-tracked rows, leaving the empty-message lingering).

**Open question:** the user reports the companion bug is "very recent". Is it correlated with the UI refactor, or with something else committed around the same time? The UI refactor shouldn't logically affect movement/AI state — but if it does, the most likely vector is a side effect of `NpcMainPanel`/`TraderUI` cleanup running at an unexpected time. Worth ruling out by checking git log / blame on the suspect files below.

---

## Behaviour ↔ Suspect Mapping

The user's observations map most cleanly onto **Suspect #3** below (teleport suppression / vault-write skip), with a secondary contribution possible from a stuck movement/AI state.

| Observation | Most likely cause |
|---|---|
| Companion stands still in follow state | AI skip path fires (`ShouldSkipAIUpdate` true), or movement authority blocked |
| No auto-teleport when player walks away | `AreCompanionTeleportsSuppressed` returns true → `CheckFollowTeleport` early-outs |
| Companion drops off Group HUD at distance | Normal Valheim zone unload — companion should have teleported BEFORE this happened |
| Re-appears in Group HUD on return | Normal zone re-stream — they were never wrong, just stranded |
| Re-issuing Follow heals it | Manually re-running the follow setter clears the stuck state OR the suppression window has elapsed |
| Logout/login heals it | `LoadFromZDO` re-runs cleanly with current ZDO state, AND any in-memory suppression flag is reset |

---

## Suspect Sites (Top 3)

These were surfaced by an exploratory pass; **none are confirmed root causes**. They are the highest-likelihood places to instrument with logging / breakpoints.

### 1. `LoadFromZDO()` re-entry — HIGH

**File:** [Modules/Companions/CompanionController.cs:2270–2441](../Modules/Companions/CompanionController.cs#L2270)

Called from `Start()` at line 251. The concern: if anything re-triggers `LoadFromZDO()` mid-session (ZDO sync from another peer, respawn, zone reload), it re-evaluates `companion_wasfollowing` from ZDO. The `else` branch at line 2406-2409 unconditionally calls `SetShouldFollow(false)` if the ZDO flag is false. A stale or mis-timed ZDO read clobbers live runtime follow state.

**To verify:** add a log line at the top of `LoadFromZDO()` printing companion id + caller stack frame. See if it fires more than once per session per companion, and at what timestamp relative to the bug repro.

### 2. `TryActivateDeferredFollow()` polling — MEDIUM-HIGH

**File:** [Modules/Companions/CompanionController.cs:291–310](../Modules/Companions/CompanionController.cs#L291)

The `_pendingDeferredFollowRestore` flag is set in `LoadFromZDO()` at line 2404 when the owner Player isn't yet present. `Update()` polls every 0.5s and fires `SetShouldFollow(true)` once the owner appears. Flag is cleared at line 308.

The concern: if `GetOwner()` momentarily returns null after the owner was already present (zone transition, player respawn, brief disconnection), the watcher could re-arm and overwrite live state. Or the flag isn't cleared cleanly in some branch.

**To verify:** log every entry into `TryActivateDeferredFollow` and every clear/set of `_pendingDeferredFollowRestore`. If the flag is ever set during normal play, that's the bug.

### 3. Teleport suppression skipping vault writes — HIGH (best fit for repro)

**File:** [Modules/Companions/CompanionPatches.cs:3118–3171](../Modules/Companions/CompanionPatches.cs#L3118) (`AreCompanionTeleportsSuppressed`)

`SuppressCompanionTeleportsUntil` is set on player death, long-jump teleport, etc. (lines 120, 165, 208, 224 in CompanionPatches). Default suppression window for long-jump = 6 seconds (`SUPPRESS_DURATION_LONG_JUMP`).

While suppressed, the following early-out:

- `CompanionController.OnDestroy` ([line 326](../Modules/Companions/CompanionController.cs#L326)) — vault save skipped
- `CompanionController.SaveFollowStateToVault` ([line 2203](../Modules/Companions/CompanionController.cs#L2203))
- `CompanionVault.Tick` (`Modules/Companions/Vault/CompanionVault.cs:86`)
- `CompanionController.CheckFollowTeleport` ([line 713](../Modules/Companions/CompanionController.cs#L713)) — auto-teleport-on-distance disabled

**This is the strongest fit for the repro:** while the suppression window is open, the companion can't teleport-to-catch-up, so it stands still until it gets unloaded by the zone system at distance. Re-login resets the in-memory suppression. Re-issuing follow may bypass it depending on which path `SetShouldFollow(true)` takes.

**To verify:** log every entry/exit of `AreCompanionTeleportsSuppressed`. Specifically log when the gate stays closed for longer than the configured `SUPPRESS_DURATION_*` constant — that would indicate a stuck flag (timestamp not advancing, or set repeatedly).

Memory-note from prior investigations:
> Two gates: PlayerSpawnGate (player customData) + AreCompanionTeleportsSuppressed (companion ZDO/RPC); writes during the closed window deadlock the zone-stream

The "writes during the closed window deadlock" warning is consistent with the theory that something is hammering the gate or writing to ZDO while it's closed.

---

## Secondary Suspects (lower confidence)

### 4. Glitch recovery forcing stay mode

**File:** [Modules/Companions/CompanionController.cs:1030–1041](../Modules/Companions/CompanionController.cs#L1030)

If owner lookup fails, glitch recovery calls `SetShouldFollow(false)` and marks the companion defeated. Spurious firing would explain the bug, but only triggers on detected corruption — unlikely to fire constantly.

### 5. Vault fallback default

**File:** [Modules/Companions/CompanionRespawnManager.cs:1702](../Modules/Companions/CompanionRespawnManager.cs#L1702)

When respawning, `savedData?.IsFollowing ?? true` defaults to `true` if vault lookup fails. If vault returns null in normal play (rather than only on corruption), follow state could be flipped incorrectly.

### 6. Stuck `UIInteraction` movement state

**Hypothesis:** if our UI refactor caused `CompanionStateController` to enter `UIInteraction` state and not exit cleanly, AI updates would skip and the companion would stand still. This was previously documented as the "freeze" path used by companion inventory/stats screens.

**To verify:** when a companion is stuck, dump `CompanionStateController.CurrentState` and check if it's `UIInteraction`. If yes, find the path that set it and didn't clear it.

---

## Diagnostic Plan (next time the bug repros)

In rough priority order:

1. **Capture state** — when a companion is observed standing still in follow:
   - Approach to within hover-text range. Confirm whether hover says `(Following)` or `(Staying)`.
   - Get the companion ID. Open admin console / debug tools to query:
     - `CompanionStateController.CurrentState`
     - `_companionAI.GetShouldFollow()` value
     - The ZDO `companion_wasfollowing` value
     - `AreCompanionTeleportsSuppressed(companion)` return value
     - `_pendingDeferredFollowRestore` value
2. **Log instrumentation** — temporarily add `Debug.Log` at:
   - Top of `LoadFromZDO()` (with stack trace via `Environment.StackTrace`)
   - Every set of `SuppressCompanionTeleportsUntil`
   - Every entry of `CheckFollowTeleport()` showing the suppress-gate result
   - Every set of `_pendingDeferredFollowRestore`
3. **Repro matrix** — try to find the trigger:
   - Walk far away → does it always happen, or only after a specific event?
   - Player death + respawn → does it happen on the next companion respawn?
   - Portal travel → does it happen on the receiving end?
   - Zone boundary cross-walk vs fast-travel → different behaviour?
4. **Git bisect** — if the user can identify when it started, `git bisect` between a known-good and known-bad commit to find the offending change.

---

## What we explicitly are NOT doing yet

- **No code changes** to any companion / movement / teleport file. The user has been burned by symptom-fixes on timing / respawn bugs (per workflow memory). Diagnosis must come first.
- **No speculative gate-clearing** ("just clear the suppression flag on every X"). That's exactly the kind of symptom fix that papers over a real deadlock.

When fix work begins, it should be informed by the actual instrumentation / repro data captured during the diagnostic plan above.

---

## File / Line Reference Index

| Topic | Location |
|---|---|
| Follow flag setter | `Modules/Companions/CompanionController.cs` — `SetShouldFollow` (search the file) |
| Follow ZDO load | `Modules/Companions/CompanionController.cs:2270` — `LoadFromZDO` |
| Deferred follow restore | `Modules/Companions/CompanionController.cs:291` — `TryActivateDeferredFollow` |
| Auto-teleport on distance | `Modules/Companions/CompanionController.cs:713` — `CheckFollowTeleport` |
| Vault save (follow state) | `Modules/Companions/CompanionController.cs:2203` — `SaveFollowStateToVault` |
| Teleport suppression gate | `Modules/Companions/CompanionPatches.cs:3118` — `AreCompanionTeleportsSuppressed` |
| Suppression set sites | `Modules/Companions/CompanionPatches.cs:120, 165, 208, 224` |
| Reconcile on arrival | `Modules/Companions/CompanionPatches.cs:236` — `ReconcileFollowersAfterArrival` |
| Server-side reconcile RPC | `Modules/Companions/CompanionTeleportService.cs:135–159` |
| Glitch recovery forced stay | `Modules/Companions/CompanionController.cs:1030` |
| Vault fallback default | `Modules/Companions/CompanionRespawnManager.cs:1702` |
| State controller (UIInteraction etc) | `Modules/Companions/Movement/CompanionStateController.cs` |
| Group HUD scan / cleanup | `UI/GroupHudController.cs` |
