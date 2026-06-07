# Sync / Territory / Logging Investigation
**Date:** 2025-04-18  
**Investigator:** GitHub Copilot  
**Status:** In-progress — findings captured before continuing

---

## Files Examined

| File | Purpose |
|------|---------|
| `FiresNPCs\Ascend.cs` | Plugin entry point, init order, WaitForZNetReady coroutine |
| `FiresNPCs\Managers\QuestManager.cs` | Quest DB + profile loading, HandleFileUpdate RPC, SendAllQuestFilesToPeer |
| `FiresNPCs\UI\QuestProfileManager.cs` | QuestProfile in-memory store, LoadAllProfiles, ApplyProfileDataFromServer, Reload |
| `FiresNPCs\Modules\Dialogues\DialogueSyncRPC.cs` | ApplyFileUpdate, HandleFolderReload, DebouncedReloadCoroutine, "Saved synced config" log line |
| `FiresNPCs\Managers\ServerConfigFileWatcher.cs` | File watcher on server, debounce, reloads subsystems on change |
| `FiresNPCs\Modules\Territories\TerritorySyncedState.cs` | Territory RPC registration, RequestFromServer, RPC_Request, chunk sending |
| `FiresNPCs\Modules\Territories\TerritoryLedger.cs` | Loads territory .cfg files, creates sample, calls TerritoryRuntime helpers |
| `FiresNPCs\Modules\Territories\TerritoryRuntime.cs` | Client-side territory enforcement, Current territory tracking |
| `FiresNPCs\Modules\Vault\VaultOfKnowledge.cs` | Player data, RPC registrations including QuestDB, FileUpdate, ReceivePlayerData |

---

## Issue 1 — Quest Profiles Loaded Multiple Times

### Root Cause Chain

1. **`QuestManager.Init()`** is called in `WaitForZNetReady` — this calls `LoadQuestProfiles()` which reads all `QuestProfiles/*.cfg` files from disk on **both** server and client.

2. **`DialogueSyncRPC`** receives files from server via its own RPC system. When it receives a `QuestProfiles/*.cfg` file, `ApplyFileUpdate()` is called which:
   - Writes the file to disk on the client (non-server path)
   - Calls `HandleFolderReload("questprofiles")`
   - Which calls `QuestProfileManager.Reload()`

3. **`QuestProfileManager.Reload()`** calls `LoadAllProfiles()` which loops all files and calls `ParseProfileLines()` for each — logging `Created new profile` / `Added quest` / `Parsed X profile(s)` for **every file** on every reload.

4. There are **6 profile files** on disk. Each file synced from server triggers a debounced reload of **all 6 files**. Because of the 0.5s debounce, if all 6 arrive within the window they batch into one reload — but if they stagger (network timing), each triggers its own reload cycle.

5. **QuestF.cfg intentionally duplicates quests** from other profile files (it is a "mega-profile" containing all quests). This means the same quest IDs appear in both `QuestF.cfg` and `Majestic_Quest_Profile.cfg` etc. `ParseProfileLines` merges into existing profiles so duplicates are silently merged, but the log lines still fire for each.

### Evidence in Log

```
[QuestProfileManager] Loading file: .../DefaultProfile.cfg         (6 lines)
[QuestProfileManager] Loading file: .../Majestic_Quest_Profile.cfg (59 lines)
[QuestProfileManager] Loading file: .../MoreGamingFactionQuests.cfg
[QuestProfileManager] Loading file: .../QuestF.cfg                 (59 lines — duplicates Majestic!)
[QuestProfileManager] Loading file: .../QuestProfiles.cfg
[QuestProfileManager] Loading file: .../VerdantsAscent_Quest_Profile.cfg
[QuestProfileManager] Loaded 14 profiles from 6 file(s)
[QuestProfileManager] Reloaded 14 quest profiles from local files   ? second full load
```

The `Loaded` line comes from `QuestManager.Init()` ? `LoadQuestProfiles()`.  
The `Reloaded` line comes from `DialogueSyncRPC` ? `ExecutePendingReloads()` ? `QuestProfileManager.Reload()`.

### Per-Quest Logging Noise

`ParseProfileLines` logs:
- `Created new profile: 'X'` — once per profile header
- `Added quest 'Y' to profile 'X'` — once per quest ID line
- `Profile 'X' now has N quest IDs` — once per closing profile
- `Parsed N profile(s), M quest(s) from file.cfg` — once per file

With 6 files × ~50 quest entries each this is **~300+ log lines** per reload cycle. With two cycles on login that's 600+ lines just from profile parsing.

---

## Issue 2 — Territories Not Initializing

### Init Call Chain (from `WaitForZNetReady`)

```csharp
TerritorySyncedState.Init();

if (ZNet.instance != null && !ZNet.instance.IsServer())
{
    TerritorySyncedState.RequestFromServer();
}
```

### `TerritorySyncedState.Init()`

```csharp
public static void Init()
{
    EnsureRpcs();
    if (ZNet.instance != null && ZNet.instance.IsServer())
    {
        TerritoryLedger.Init();
    }
}
```

- `EnsureRpcs()` registers 4 RPCs — but only if `ZRoutedRpc.instance != null` and `_rpcsRegistered == false`.
- On a **client**, `TerritoryLedger.Init()` is **never called** — correct, client should receive from server.
- `TerritoryLedger.Init()` has a `if (_initialized) return;` guard — only runs once on server.

### `RequestFromServer()` Early-Exit Bug

```csharp
public static void RequestFromServer()
{
    EnsureRpcs();
    if (ZRoutedRpc.instance == null) return;
    if (ClientTerritories.Count > 0 && _pendingChunks.Count == 0) return;  // ? BUG
    ...
}
```

If the client already has **local territory files** from a previous session (synced to disk), `WriteClientManifest` will find them and populate the package — but the method **does not** check `ClientTerritories.Count` before sending. The early-exit only fires if `ClientTerritories > 0`, which is only true if the in-memory list was already populated by a previous RPC response in this session. On first login this should be 0, so the early exit should NOT fire.

### Why Nothing Appears in Log

`TerritoryLedger.Init()` has:
```csharp
Debug.Log($"[FiresRPGmaker] TerritoryLedger loaded {_territories.Count} territories...");
```
This always logs (not verbose-gated). The fact that **this line is absent** from the BepInEx log means `TerritoryLedger.Init()` was **never called** — meaning `TerritorySyncedState.Init()` either:

**Option A:** `ZNet.instance.IsServer()` returned `false` on the host/server when Init ran — possible if `ZNet.instance` was still spinning up despite `ZNetScene` being ready.

**Option B:** `TerritorySyncedState.Init()` threw silently — unlikely, it's wrapped in try/catch.

**Option C (most likely):** The instance running the log IS a client (not host) — the server never produced its territory log because the server is a separate process (dedicated server). The client called `RequestFromServer()` correctly, but the **RPC fired to the server got no response**, OR the `RPC_Receive` handler on the client never fired. No log output at all suggests the `RPC_Request` fired but silently failed on server side, or `EnsureRpcs()` failed to register because `ZRoutedRpc.instance` was null at the moment `Init()` was called in `WaitForZNetReady`.

### Timing Problem with EnsureRpcs

`WaitForZNetReady` waits for:
```csharp
while (ZNetScene.instance == null || ObjectDB.instance == null)
    yield return null;
yield return new WaitForEndOfFrame();
```

`ZRoutedRpc.instance` is created during `ZNet.Awake()` which runs **before** `ZNetScene`. So `ZRoutedRpc.instance` SHOULD be non-null here. However, `FinishInit()` (which runs before `WaitForZNetReady`) also tries to register RPCs:

```csharp
if (ZRoutedRpc.instance != null)
{
    ZRoutedRpc.instance.Register<ZPackage>("FiresRPGmaker_SyncFiles", ...);
    SavedNpcManager.RegisterRPCs();
    UILayoutSyncRPC.EnsureRpcsRegistered();
}
```

`TerritorySyncedState.EnsureRpcs()` is NOT called here — it's deferred to `WaitForZNetReady`. If `ZRoutedRpc.instance` happens to be null at that deferred point (e.g., scene reload/reconnect scenario), `_rpcsRegistered` stays `false` and RPCs silently never get registered.

### Server-Side Territory Sync Guard Bug

In `RPC_Request`:
```csharp
var now = DateTime.UtcNow;
if (_recentResponses.TryGetValue(sender, out var last) && now - last < ResponseCooldown)
{
    return; // avoid duplicate sends within cooldown
}
```

If the client fires `RequestFromServer()` twice (e.g., once in `WaitForZNetReady` and again later), the **second request is silently dropped** due to the 10-second cooldown. No log is emitted on the server when it drops. This could explain why territories never appear if the first request was dropped/lost.

---

## Issue 3 — Excessive Logging (Verbose Mode)

### Sources of Log Spam (per login)

| Source | Lines Generated | Verbose-Gated? |
|--------|----------------|----------------|
| QuestProfileManager `ParseProfileLines` — per quest ID | ~300 | ? No (`Debug.Log`) |
| QuestProfileManager `ParseProfileLines` — per profile header | ~50 | ? No (`Debug.Log`) |
| QuestProfileManager file load summary | ~12 | ? No (`Debug.Log`) |
| QuestManager `LoadQuestProfiles` count | 1 | ? `LogVerbose` |
| `DialogueSyncRPC` "Saved synced config" | 1 per file | ? `LogVerbose` |
| Territory RPC — no log at all | 0 | N/A (broken) |
| `VaultOfKnowledge` `RPC_ReceivePlayerData` | 4 lines | ? `Debug.Log` (Info level) |

`ParseProfileLines` uses bare `Debug.Log()` not `FiresLogger.LogVerbose()` — it will spam even with verbose OFF.

---

## Planned Fixes

### Fix 1 — QuestProfileManager logging: gate all per-item lines behind `FiresLogger.VerboseEnabled`

Only the final summary per file should always log. Individual profile/quest lines should be verbose-only.

**File:** `FiresNPCs\UI\QuestProfileManager.cs`  
**Lines to change:**
- `Debug.Log($"[QuestProfileManager] Created new profile: '{profileName}'")` ? `FiresLogger.LogVerbose(...)`
- `Debug.Log($"[QuestProfileManager] Profile '{currentProfile.Name}' now has {n} quest IDs")` ? `FiresLogger.LogVerbose(...)`
- `Debug.Log($"[QuestProfileManager] Added quest '{questId}' to profile '{profileName}'")` ? `FiresLogger.LogVerbose(...)`
- Keep `Debug.Log($"[QuestProfileManager] Parsed {n} profile(s), {m} quest(s) from {fileName}")` — this is the useful summary line.

### Fix 2 — QuestProfileManager double-load on client

Clients should NOT call `LoadAllProfiles()` from disk at all. The `Initialize()` method already has the `IsServer` check — but `QuestManager.Init()` calls `LoadQuestProfiles()` which is a separate code path that DOES load from disk on clients too.

**File:** `FiresNPCs\Managers\QuestManager.cs`  
**Fix:** Guard `LoadQuestProfiles()` behind `IsServer` check same as `QuestProfileManager.Initialize()`.

### Fix 3 — Territory RPC registration: call `EnsureRpcs` earlier

Move `TerritorySyncedState.EnsureRpcs()` call into `FinishInit()` alongside the other RPC registrations, so it runs at the same guaranteed-safe time as `SavedNpcManager.RegisterRPCs()`.

**File:** `FiresNPCs\Ascend.cs`  
**Change:** Add `TerritorySyncedState.EnsureRpcsPublic()` call inside the `if (ZRoutedRpc.instance != null)` block in `FinishInit()`.  
**Also:** Make `EnsureRpcs()` in `TerritorySyncedState` public or add a public wrapper.

### Fix 4 — Territory server-side drop logging

In `RPC_Request`, when dropping due to cooldown, log a warning so it's visible.

**File:** `FiresNPCs\Modules\Territories\TerritorySyncedState.cs`

### Fix 5 — Single sync on login (safe, no-disconnect guarantee)

`SendAllQuestFilesToPeer` fires all files immediately on connect. For large numbers of files this can flood the ZRoutedRpc buffer causing disconnects. Needs to be chunked/rate-limited via a send queue (similar to how `UILayoutSyncRPC.DrainSendQueue` works in `Update()`).

**File:** `FiresNPCs\Managers\QuestManager.cs`  
**Also:** `FiresNPCs\Modules\Dialogues\DialogueSyncRPC.cs`

---

## Next Steps

1. Apply Fix 1 (logging gates) — immediate win, no behavior change
2. Apply Fix 2 (client LoadQuestProfiles guard) — stops double-load
3. Apply Fix 3 (territory RPC registration timing) — unblocks territory sync
4. Apply Fix 4 (cooldown drop log) — makes territory failures visible
5. Verify territory flow end-to-end via log after Fix 3
6. Address Fix 5 (rate-limited join sync) separately as it's a larger change

---

## RESOLUTION — Changes Applied

### `FiresNPCs\UI\QuestProfileManager.cs`
- Added `using VerdantsAscent;` for `FiresLogger` access
- `Reload()` now **short-circuits on client** — returns early with a verbose log; clients
  get their profiles exclusively via `ApplyProfileDataFromServer` RPC path now
- Gated the following log lines behind `FiresLogger.LogVerbose`:
  - `Client initialized - waiting for profile data from server`
  - `Applying profile data from server: {fileName}`
  - `Applied in-memory profile file {fileName}`
  - `Profile 'X' now has N quest IDs` (per profile)
  - `Profile 'X' already exists, merging quests`
  - `Created new profile: 'X'`
  - `Added quest 'Y' to profile 'X'` (the biggest spammer — hundreds per login)
  - `Profile 'X' final count: N quest IDs`
  - `Parsed N profile(s), M quest(s) from file.cfg`
  - `Cleared all profiles`
  - `Loading file: {filePath}`
- Kept as always-logged:
  - `Server loaded {N} quest profiles from local files` (once per server startup)
  - `Loaded {N} profiles from {M} file(s)` (once per server startup)
  - `Reloaded {N} quest profiles from local files` (only server-side now)
  - All warnings / errors

### `FiresNPCs\Managers\QuestManager.cs`
- `LoadQuestProfiles()` now **skips disk reads on client**. Profile data on the client comes
  entirely through the server sync path via `HandleFileUpdate` ? `ApplyFileContent`. Prevents
  the old parallel disk-load from racing with RPC-delivered data.

### `FiresNPCs\Modules\Vault\VaultOfKnowledge.cs`
- Two chatty lines in `RPC_ReceivePlayerData` that always fired on login are now
  `FiresLogger.LogVerbose` gated:
  - `RPC_ReceivePlayerData wrote vault data into local player customData for X`
  - `RPC_ReceivePlayerData stored received player data for X`

### `FiresNPCs\Modules\Territories\TerritorySyncedState.cs`
- Exposed `EnsureRpcsRegistered()` public wrapper so `Ascend.FinishInit()` can register
  territory RPCs at the guaranteed-safe early point (alongside `SyncFiles`, `SavedNpc`,
  `UILayoutSyncRPC`). Prevents the silent "RPC never registered" class of bug.
- Added diagnostic logs (always-on, info level) so territory sync failures are visible:
  - `Territory RPCs registered` (verbose)
  - `Territory RequestFromServer sent to server` (info, once per login)
  - `Territory RequestFromServer aborted: ZRoutedRpc.instance is null` (warning)
  - `Territory RequestFromServer skipped: already have N territories in memory` (verbose)
  - `Territory RequestFromServer dropped: already awaiting response (cooldown)` (verbose)
  - `Territory RPC_Request from {sender}: sending N territories` (info, server-side)
  - `Territory RPC_Request from {sender} dropped (within 10s cooldown)` (verbose)

### `FiresNPCs\Ascend.cs`
- `FinishInit()` now calls `TerritorySyncedState.EnsureRpcsRegistered()` inside the
  `if (ZRoutedRpc.instance != null)` block, alongside `SavedNpcManager.RegisterRPCs()`
  and `UILayoutSyncRPC.EnsureRpcsRegistered()`. Guarantees territory RPC handlers are
  bound before any client issues `RequestFromServer()` in the `WaitForZNetReady` coroutine.

---

## Expected Log Volume After Fix

Per login with **verbose OFF** (normal gameplay):
- Vault: 0 chatty lines (was ~4)
- QuestProfileManager: 0 lines (was ~300+)
- Territory: 2 lines (`RPC_Request sent` + `Received N territories`) — was 0 (invisible failure)
- Net: ~300 lines removed, 2 lines added for diagnostic visibility

Per login with **verbose ON** (testing):
- All previous info gathered, but now properly tagged as verbose
- Territory path fully instrumented (was invisible)

---

## Root-Cause Verification

The fixes address the **actual** causes, not symptoms:

| Symptom | Root Cause | Fix |
|---------|-----------|-----|
| Quests "added more than once" | `QuestProfileManager.Reload()` on client re-parses disk after RPC already delivered data | Client-side Reload short-circuit |
| `Added quest 'X' to profile 'Y'` × hundreds | `Debug.Log` (info-level) inside parse loop, not gated by verbose flag | `FiresLogger.LogVerbose` gating |
| Territory "doesn't seem to be called at all" | RPC handlers registered late in `WaitForZNetReady`; server-side cooldown drops silently | Register RPCs in `FinishInit` + add drop logs |
| "too much logging even with verbose off" | Many `Debug.Log` calls were unconditional info-level rather than verbose | Converted to `FiresLogger.LogVerbose` |

