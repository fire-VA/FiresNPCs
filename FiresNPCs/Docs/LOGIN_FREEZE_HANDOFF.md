# Login Freeze Investigation — Handoff

**Status:** Open. Hard client freeze on login for existing characters. Brand-new characters log in cleanly. User has been burned by symptom-fixes; do NOT speculatively change companion / vault code without diagnostic evidence pointing at it.

**Date:** 2026-05-10
**Last touched build:** Has all instrumentation + Ghetto Networking deferrals + the wrong-ID bug fix described below. Build is clean.

---

## Symptom

- **Old character (`VaHypeweek`, 2 companions in vault, saved at heavily-built location ~`(4219.99, 31.36, 2099.19)`, in territory "Raven's Rest"):** hard freezes ~3 seconds after `Spawned after 8.019993`.
- **Brand-new character (`testnumberseven`, no companions, no saved location):** logs in normally and plays.
- **Crash signature:** `Player.log` ends abruptly with no managed exception. Last line in repros so far: `Starting music swamp`. File truncates mid-write — pattern of a native crash or main-thread freeze that the OS eventually kills.
- **Server side:** doesn't notice — connection stays "alive" until ZRpc heartbeat times out at 90s.
- **User-confirmed scope:** the world (7.5M+ ZDOs, heavily built map) runs fine without our mod loaded. The freeze is in our code or our code's interaction with the engine, **not** ZDO count, **not** Ghetto Networking AutoTune, **not** vanilla zone-stream pressure.

---

## What's been verified (don't re-confirm)

### Server-side ✅ working
- `[CompanionRestoreService] RPC_AnnouncePlayerInfo postfix fired (state=-99955842, pending=0, restored=0)` — postfix fires correctly, valid state.
- All four heavy server-initiated pushes are now wrapped in `GhettoNetworkingCoexistence.ScheduleOrInvoke` with a 45s defer when Ghetto Networking is loaded:
  - `Managers/VaultPatches.cs:RPC_PeerInfo` — `SendAllQuestFilesToPeer` + `BroadcastQuestDatabaseToPeer`
  - `Modules/Companions/CompanionPatches.cs:RPC_CharacterID` — `ServerConfigFileWatcher.SendAllConfigsToClient`
  - `Modules/Companions/Vault/CompanionRestoreService.cs:RPC_AnnouncePlayerInfo` — `RestoreForPlayerServerSide`
  - `Modules/Vault/VaultOfKnowledge.cs:RPC_RequestPlayerData` — second `SendAllQuestFilesToPeer`
- Server log confirms all four defer 45s as designed when Ghetto Networking is detected. The wire is quiet during the AutoTune probe.
- `[CompanionTeleportService] Reconcile complete: notFollowing=2` — both companions are stay-mode.

### Client-side ✅ verified harmless
- `RPC_ReceivePlayerData wrote vault data (3067 bytes) into local player customData for -99955842` — vault data lands successfully. **3KB JSON. NOT the crash cause.** activeQuests=0, completedQuests=0, companions=2, bankItems=0.
- Wayshrine list (3 entries) received from server.
- Reconcile request sent.
- Inventory load completed (helmet, mage chest visible).
- All standard Player.OnSpawned postfixes from other modules ran (CompendiumManager, FrozenWaterController, CustomWaterPatches, RuntimeSpriteSync, SavedNpcManager, GlobeHud).

### Ruled out
- ❌ Quest files / QuestDB push (deferred, not in crash window).
- ❌ ServerConfigFileWatcher push (deferred, not in crash window).
- ❌ Server-side companion ZDO scan / adopt (deferred, not in crash window).
- ❌ Vault data deserialization / customData write (succeeds, tiny payload).
- ❌ 7.5M ZDO count / zone-stream pressure (world runs fine without our mod).
- ❌ Ghetto Networking AutoTune probe directly (it runs and completes; client survives the 30s probe window).
- ❌ Recursion in `LoadPlayerData → SavePlayerData` (server-only path, gated by `IsServer()` check at `VaultOfKnowledge.cs:1096`).

---

## What I added for diagnostics (already in the build)

These are non-verbose `Debug.Log` calls that fire unconditionally so the next repro log will pinpoint the freeze:

| Site | File:line | Purpose |
|---|---|---|
| `CompanionController.LoadFromZDO` ENTER/EXIT | `Modules/Companions/CompanionController.cs:2275`, `:2451` | If ENTER appears with no EXIT for a zdoUid → freeze is inside companion init. |
| `CompanionController.TryRestoreEquipmentFromVault` ENTER/EXIT | `Modules/Companions/CompanionController.cs:2510`, `:2615` | If ENTER appears with no EXIT → freeze is in equipment-restore path. |
| `RPC_ReceivePlayerData` schedule + write | `Modules/Vault/VaultOfKnowledge.cs:2280`, `:2298` | Logs counts (active/completed quests, companions, bank items) and JSON byte size. |
| `CompanionRestoreService.RPC_AnnouncePlayerInfo_Postfix` | `Modules/Companions/Vault/CompanionRestoreService.cs:104` | Logs whether postfix fires + dedup state (pending/restored counts). |
| `GhettoNetworkingCoexistence.ScheduleOrInvoke` + `DeferredInvoke` | `Modules/Utilities/GhettoNetworkingCoexistence.cs` | Logs each deferral schedule + 45s elapsed event. |

Bug fix already applied: `CompanionRestoreService` was passing `playerId` to the helper as `peerUid`, causing a false-positive "peer disconnected during deferral" log. Helper now bypasses the `GetPeer(uid)` check when caller passes `0`. The site now passes `0` because it's keyed on playerId, not peer UID.

---

## What's needed from the user — the next test repro

Have the user repro the freeze, then attach **the complete `LogOutput.log` file** (NOT pasted text — pasted text gets truncated and we lose the crash window).

Likely paths:
- r2modman: `C:\Users\<user>\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\<profile>\BepInEx\LogOutput.log`
- Vanilla: `C:\Users\<user>\AppData\LocalLow\IronGate\Valheim\Player.log`

Also useful: the new server log alongside it.

**Ask the user to attach the file directly via the file path, not paste it inline.** The freeze happens past the point users tend to truncate when pasting.

---

## Decision tree for the next agent

When the next log arrives, search for these markers in the section between `[05/...] Spawned after` and end-of-file:

### Branch A — `[CompanionController.LoadFromZDO] ENTER zdoUid=…` appears, no matching `EXIT` for that zdoUid
- **Diagnosis:** freeze is inside `CompanionController.LoadFromZDO` for that ZDO.
- **Next step:** read the full method body at `Modules/Companions/CompanionController.cs:2270-2455`. Identify suspect branches based on the ZDO's owner / following state from the ENTER log line. Likely candidates: `_inventory.LoadFromZDO()`, `TryRestoreEquipmentFromVault()` if equipment is empty, the wakeup-state read.
- **Add finer-grained checkpoints** within `LoadFromZDO` between the major sections (consumables → inventory → skills → AI configure → follow restore) to bisect.

### Branch B — `[CompanionController.TryRestoreEquipmentFromVault] ENTER` appears, no matching `EXIT`
- **Diagnosis:** freeze is in `TryRestoreEquipmentFromVault` (`Modules/Companions/CompanionController.cs:2503-2615`).
- **Suspect calls:** `CompanionSavedDataResolver.ResolveByCompanionId`, `_inventory.RestoreEquipmentFromVault` (per slot), `new ZPackage(savedData.StorageInventoryData)` + `storageInv.Load(pkg)`, `_skills.RestoreSkillsFromVault`, etc.
- **Next step:** add checkpoints around each of those calls.

### Branch C — Neither marker appears anywhere; log ends right after `Starting music swamp`
- **Diagnosis:** freeze is NOT in companion init code. No companion ZDOs had spawned yet at the freeze point.
- **Next investigation candidates** (in priority order):
  1. **Other `Player.OnSpawned` postfixes** that read existing customData (only old chars have it populated). Specifically:
     - `Modules/CustomWater/CustomWaterPatches.cs:351` — `Player_OnSpawned_Postfix`
     - `UI/QuestJournal/RuntimeSpriteSync.cs:1177` — `Player_OnSpawned_Postfix`
     - `Modules/Compendium/CompendiumManager.cs:511` — `Player_OnSpawned_Postfix`
     - `Modules/Wayshrines/Patches/PlayerSpawnedPinRebuildPatch.cs:20` — minimap pin rebuild via `WayshrineSyncRPC.OnLocalPlayerSpawned`
     - `Managers/VaultPatches.cs:238` — `Player_OnSpawned_LoadVaultData` (already deferred via PlayerSpawnGate, but verify)
  2. **CompanionTeleportService client-side reply handler** — server's reconcile reply lands on client and might trigger heavy work.
  3. **Wayshrine pin rebuild** — `WayshrineSyncRPC.OnLocalPlayerSpawned` runs after a one-frame defer; with 3 shrines the work itself is small but it does map-pin manipulation.
  4. **ClientLogRelay push** — sends 553KB synchronously. Fresh char survives this (~410KB push) so it's not solely responsible, but it is heavy main-thread work.
- **Add ENTER/EXIT checkpoints** to each of those Player.OnSpawned postfixes and re-test.

### Cross-cutting clue regardless of branch
- The user described the crash as "frozen" — main-thread hang, not graceful native crash. This points at:
  - Tight infinite loops in our code triggered by old-char data state
  - Or pathological synchronous work blocking long enough that Windows kills the process

---

## Hard rules from the user (workflow memory)

> "diagnose before fixing on timing/respawn bugs; user has been burned by symptom-fixes"

Do NOT:
- Add speculative deferrals or guards in companion / vault code "just in case."
- Disable or skip companion restoration paths to "smoke-test" without specifying *exactly* which line of evidence justifies it.
- Re-investigate ground we've already covered (ZDO count, AutoTune timing, server-side deferrals — those are settled).

Do:
- Read the new log file the user attaches.
- Pick the branch above based on which markers appear / don't appear.
- Add ONE round of more granular checkpoints to bisect the crash to a specific line.
- Build, ask user to repro again, then make the actual targeted fix.

---

## File:line index for the most likely suspects

| What | Where |
|---|---|
| Helper that defers 45s under Ghetto Networking | `Modules/Utilities/GhettoNetworkingCoexistence.cs` |
| Client-side companion init | `Modules/Companions/CompanionController.cs` — `Awake:232`, `Start:246`, `Update:255`, `LoadFromZDO:2270`, `TryRestoreEquipmentFromVault:2503` |
| Vault data RPC reception (client) | `Modules/Vault/VaultOfKnowledge.cs:RPC_ReceivePlayerData:2252` |
| Server-side companion adopt + restore | `Modules/Companions/Vault/CompanionRestoreService.cs:RestoreForPlayerServerSide:166`, `AdoptOwnedCompanionZdos:221` |
| Roster read on local player | `Modules/Companions/Vault/CompanionSavedDataResolver.cs:ResolveByCompanionId:182` |
| Player.OnSpawned postfixes (multiple) | grep for `[HarmonyPatch(typeof(Player), .OnSpawned.)]` |

---

## Memory notes for the next session

The user has these in their persistent memory; the next agent should remember them:

- `[Companion follow/teleport regression](project_companion_follow_bug.md)` — separate open bug, diagnosis in `Docs/COMPANION_FOLLOW_TELEPORT_BUG.md`. Don't conflate with this freeze.
- `[Standalone canvas for NPC UIs](reference_standalone_canvas.md)` — recent UI refactor pattern; not implicated in this freeze.
- `[Ghetto Networking coexistence](reference_ghetto_networking_coexistence.md)` — the deferral system used here.
- `[Spawn gates reference](reference_spawn_gate.md)` — `PlayerSpawnGate` is what `Player_OnSpawned_LoadVaultData` defers through. Writes during the closed window have been observed to deadlock the zone-stream historically — relevant prior art.
- `[csproj reference](reference_csproj.md)` — every new .cs file must be added to `FiresRPGmaker.csproj` `<Compile Include>` entries. `GhettoNetworkingCoexistence.cs` is already added.
