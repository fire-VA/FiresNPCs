# FiresRPGmaker Performance Review

> **Date:** 2025-05-04  
> **Scope:** Full mod review — logging, async patterns, initialization, per-frame costs  
> **Goal:** Identify concrete changes to reduce startup time, frame hitches, and log spam

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [CRITICAL — Verbose Logging Defaults to ON](#2-critical--verbose-logging-defaults-to-on)
3. [HIGH — Excessive `Debug.Log` During Startup (Non-Verbose)](#3-high--excessive-debuglog-during-startup-non-verbose)
4. [HIGH — Per-Combiner Log Spam During Water/Terrain Restoration](#4-high--per-combiner-log-spam-during-waterterrain-restoration)
5. [HIGH — `Resources.FindObjectsOfTypeAll` Called At Runtime](#5-high--resourcesfindobjectsoftypeall-called-at-runtime)
6. [MEDIUM — Duplicate Budget Systems (MainThreadDispatcher + FrameBudgetScheduler)](#6-medium--duplicate-budget-systems-mainthreaddispatcher--framebudgetscheduler)
7. [MEDIUM — DirtFloorDeferredInit Has Independent 8ms Budget](#7-medium--dirtfloordeferredinit-has-independent-8ms-budget)
8. [MEDIUM — Font Enumeration Dumps 53 Fonts to Console](#8-medium--font-enumeration-dumps-53-fonts-to-console)
9. [MEDIUM — ServerConfigFileWatcher Creates 7+ FileSystemWatchers Synchronously](#9-medium--serverconfigfilewatcher-creates-7-filesystemwatchers-synchronously)
10. [MEDIUM — Reflection Lookups in Hot Paths](#10-medium--reflection-lookups-in-hot-paths)
11. [LOW — SnowClutterConfig Shader Property Dump](#11-low--snowclutterconfig-shader-property-dump)
12. [LOW — TerrainCombinerAnchor Iterates AllCombiners in Start()](#12-low--terraincombineranchor-iterates-allcombiners-in-start)
13. [LOW — WaterCombinerCache Uses LINQ FirstOrDefault in Update Path](#13-low--watercombinercache-uses-linq-firstordefault-in-update-path)
14. [GOOD — Patterns Already Done Well](#14-good--patterns-already-done-well)
15. [Action Plan — Priority Order](#15-action-plan--priority-order)

---

## 1. Executive Summary

The mod is **architecturally sound** — it already has a `FrameBudgetScheduler`, `BackgroundWorker<T>`, deferred initialization, and component caching in `CompanionAI`. The main performance issues are:

| Category | Issue | Impact |
|----------|-------|--------|
| **Critical** | `configVerboseLogging` defaults to `true` | Every `LogVerbose()` call hits disk on live servers |
| **High** | ~120+ unconditional `Debug.Log()` calls during startup | Blocks main thread, ~200ms+ of string formatting + I/O |
| **High** | Per-combiner restoration logs (35 water + 7 terrain = 42 log lines × ~3 lines each) | ~126 log lines just for restore |
| **High** | `Resources.FindObjectsOfTypeAll<Sprite>()` in `CodegenUIWiring` | Scans every loaded asset — very expensive |
| **Medium** | `MainThreadDispatcher` (4ms) + `FrameBudgetScheduler` (5ms) = potential 9ms/frame | Two budgets not coordinated |
| **Medium** | `DirtFloorDeferredInit` has independent 8ms budget | Third budget source |
| **Medium** | Font enumeration dumps 53 font names to log | Pure waste during init |

**Estimated improvement:** Cutting log calls alone should reduce startup wall-clock time by **150–400ms** and significantly reduce per-frame log I/O during steady-state.

---

## 2. CRITICAL — Verbose Logging Defaults to ON

**File:** `FiresNPCs\Managers\ConfigManager.cs` line 95

```csharp
configVerboseLogging = config.Bind("General", "VerboseLogging", true, ...);
//                                                                ^^^^
```

**Problem:** The default is `true`. Every server/client that hasn't manually edited their config will have verbose logging enabled. This means every `FiresLogger.LogVerbose()` call in the entire codebase writes to disk.

**Fix:** Change default to `false`:
```csharp
configVerboseLogging = config.Bind("General", "VerboseLogging", false, ...);
```

**Impact:** Immediate reduction of potentially hundreds of log lines per session.

---

## 3. HIGH — Excessive `Debug.Log` During Startup (Non-Verbose)

**Files:** `Ascend.cs`, `VaultOfKnowledge.cs`, `PlayerFileStorage.cs`, `WaterPersistence.cs`, `WaterOperationRPC.cs`, `CompanionPrefabManager.cs`, and many more.

**Problem:** The startup sequence in `WaitForZNetReady()` (Ascend.cs lines 463-570) calls ~30 initialization methods, almost every one of which logs 1-5 lines unconditionally via `Debug.Log()`. Looking at the server log output provided, the mod produces **~300+ log lines** between "ZNET START" and "ready!".

Each `Debug.Log()` call:
1. Allocates a formatted string (GC pressure)
2. Writes to Unity's log file (disk I/O on main thread)
3. Triggers BepInEx's log handler chain
4. On dedicated servers, gets duplicated to console output

**Examples of unconditional startup logs that should be verbose-only:**

| File | Log Message | Should Be |
|------|-------------|-----------|
| `Ascend.cs:325` | `Loaded {count} items, {count} pieces` | Verbose |
| `Ascend.cs:374` | `Added {name} to ZNetScene` (per-prefab!) | Verbose |
| `Ascend.cs:432` | `Registered item to ObjectDB: {name}` (per-item!) | Verbose |
| `Ascend.cs:458` | `Registered piece item to ObjectDB: {name}` (per-piece!) | Verbose |
| `Ascend.cs:586` | `Companion map marker system initialized` | Verbose |
| `Ascend.cs:605` | `Companion command system initialized` | Verbose |
| `Ascend.cs:632,642,649,651` | Multiple archetype init logs | Verbose — keep ONE summary |
| `VaultOfKnowledge.cs:45-47` | `About to call...` / `completed` | Remove entirely |
| `PlayerFileStorage.cs:54,64` | `VERSION: ...` / `PlayersDir: ...` | Verbose |
| `WaterPersistence.cs:46` | `Initialized v5` | Verbose |
| `WaterPersistence.cs:90,117,125,154,158` | Multiple restore status lines | Consolidate to ONE |
| `ServerConfigFileWatcher.cs:81` | `initialized - watching` | Verbose |
| Per-watcher creation (×7) | `Created file watcher for X at Y` | Verbose |

**Fix Pattern:**
```csharp
// BEFORE (unconditional):
Debug.Log($"[FiresRPGmaker] Added {prefab.name} to ZNetScene");

// AFTER (verbose-gated):
if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Added {prefab.name} to ZNetScene");
```

For initialization summaries, log ONE line with counts:
```csharp
// BEFORE: 30+ individual log lines
// AFTER: One summary
Debug.Log($"[FiresRPGmaker] Init complete: {itemCount} items, {pieceCount} pieces, " +
          $"{companionCount} companions, {waterCombiners} water, {terrainCombiners} terrain");
```

**Estimated savings:** 200+ `Debug.Log` calls eliminated, ~150-300ms startup time reduction.

---

## 4. HIGH — Per-Combiner Log Spam During Water/Terrain Restoration

**Files:** `WaterMeshCombinerAnchor.cs`, `WaterMeshCombiner.cs`, `WaterCombinerCache.cs`, `TerrainCombinerAnchor.cs`, `DirtFloorMeshCombiner.*.cs`, `DirtFloorTerrainManager.cs`, `DirtFloorZoneRegistry.cs`

**Problem:** Looking at the server log, restoring 35 water combiners produces **~105 log lines** (3 per combiner: `RestoreFromAnchorData`, `Set gridOrigin`, `Restored combiner from cache`). Terrain produces similar per-combiner logging.

The `DirtFloorTerrainManager` also logs per-disk creation:
```
[DirtFloorTerrainManager] Created terrain at (-10827, 38, 1460), radius=10 (absorbed into combiner with 1 disks)
[FindOrCreateCombinerForPosition] isIsolated=False, groupId=0, diskType=Normal, position=(...)
[FindOrCreateCombinerForPosition] No combiner found for group 0, type Normal - will create new combiner
[DirtFloorMeshCombiner] Set pending isolation state: isolated=False, groupId=0
[DirtFloorMeshCombiner] Applied pending isolation state: isolated=False, groupId=0
[DirtFloorZoneRegistry] Registered combiner combiner_-169068 at (...), total count now: 1
[TerrainCombinerAnchor] Found prefab: Piece_DirtFloor_vad
[TerrainCombinerAnchor] Stripped to invisible ZDO container at (...)
[TerrainCombinerAnchor] Created invisible anchor at Y=0 for terrain at (...)
[TerrainCombinerAnchor] Linked to combiner DirtFloorCombiner_...
[DirtFloorTerrainManager] Created combiner DirtFloorCombiner_... with anchor at (...)
```

That's **~11 log lines per terrain disk**. With 7 disks, that's ~77 lines just for terrain.

**Fix:** Replace per-item logs with a batch summary:
```csharp
// In WaterPersistence.LoadWaterData():
// Remove per-combiner logging, keep ONE summary at end
Debug.Log($"[WaterPersistence] Restored {restoredCount}/{cache.Groups.Count} water combiners from cache");

// In DirtFloorTerrainManager / TerrainCombinerAnchor:
// Gate ALL per-disk logs behind verbose
if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[TerrainCombinerAnchor] ...");
```

**Estimated savings:** ~180 log lines eliminated during startup.

---

## 5. HIGH — `Resources.FindObjectsOfTypeAll` Called At Runtime

**File:** `FiresNPCs\UI\CodegenUIWiring.cs` lines 154-169

```csharp
// Fallback: search all loaded sprites
if (_itemBackgroundSprite == null || _selectionFrameSprite == null || _noTeleportSprite == null)
{
    var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
    foreach (var sprite in allSprites) { ... }
}
```

**Problem:** `Resources.FindObjectsOfTypeAll<Sprite>()` is **extremely expensive** — it iterates every loaded asset in memory. With Valheim's large asset pool, this can take 50-200ms and cause a visible hitch. It also allocates a large temporary array (GC pressure).

**Fix:** The primary path (lines 132-151) already checks `InventoryGui.instance` directly. The fallback should:
1. Only run once and cache the result
2. Use a more targeted search (e.g., specific atlas or bundle)
3. Or just accept the missing sprites gracefully — the UI will work without them

```csharp
// Already cached via _spritesCached flag, but the expensive search still runs
// if InventoryGui didn't have all sprites. Better: skip the fallback entirely
// and let the missing sprite be null (cosmetic-only impact).
```

---

## 6. MEDIUM — Duplicate Budget Systems (MainThreadDispatcher + FrameBudgetScheduler)

**Files:** `MainThreadDispatcher.cs` (4ms budget), `FrameBudgetScheduler.cs` (5ms budget)

**Problem:** Both systems run in `Update()` independently. In the worst case, one frame could consume:
- `FrameBudgetScheduler`: up to 5ms
- `MainThreadDispatcher`: up to 4ms  
- **Total: 9ms** — over half a 60fps frame

The `BackgroundWorker` already routes callbacks through `FrameBudgetScheduler` (line 200), which is correct. But `MainThreadDispatcher.Enqueue()` is still available as a public API and may be called directly by other systems.

**Fix:** 
1. **Audit all `MainThreadDispatcher.Enqueue()` callers** — migrate them to `FrameBudgetScheduler.Schedule()`
2. **Deprecate** `MainThreadDispatcher` or have it delegate to `FrameBudgetScheduler` internally
3. If both must coexist, share a single budget:
```csharp
// In FrameBudgetScheduler.Update():
float usedByDispatcher = MainThreadDispatcher.LastFrameMs;
float budgetMs = MaxBudgetMs - usedByDispatcher;
```

---

## 7. MEDIUM — DirtFloorDeferredInit Has Independent 8ms Budget

**File:** `FiresNPCs\Modules\CustomTerrain\Utilities\DirtFloorDeferredInit.cs` line 62

```csharp
public float MaxFrameTimeMs = 8f;
```

**Problem:** This is a **third** independent per-frame budget that stacks on top of `FrameBudgetScheduler` (5ms) and `MainThreadDispatcher` (4ms). In a frame where all three are active: **5 + 4 + 8 = 17ms** — exceeding a 60fps frame entirely.

**Fix:** Route `DirtFloorDeferredInit` work through `FrameBudgetScheduler`:
```csharp
// Instead of its own Update() loop with independent timing:
FrameBudgetScheduler.Schedule(WorkPriority.Normal, () => ProcessPhysicsBake(combiner));
FrameBudgetScheduler.Schedule(WorkPriority.Low, () => ProcessGrassSpawn(combiner));
```

This ensures ALL mod work shares ONE budget.

---

## 8. MEDIUM — Font Enumeration Dumps 53 Fonts to Console

**File:** `UIFontConfig.cs` or the initialization code that logs:
```
[UIFontConfig] Available TMP Fonts (53):
  - LiberationSans SDF - Fallback
  - AveriaSansLibre-Bold SDF
  ... (53 lines)
```

**Problem:** 53 unconditional `Debug.Log()` calls during UI initialization. This is diagnostic information that should never appear in production.

**Fix:** Gate behind verbose logging, or remove entirely:
```csharp
if (FiresLogger.VerboseEnabled)
{
    FiresLogger.LogVerbose($"[UIFontConfig] Available TMP Fonts ({fonts.Length})");
    foreach (var font in fonts)
        FiresLogger.LogVerbose($"  - {font.name}");
}
```

---

## 9. MEDIUM — ServerConfigFileWatcher Creates 7+ FileSystemWatchers Synchronously

**File:** `FiresNPCs\Managers\ServerConfigFileWatcher.cs` lines 96-121

**Problem:** `SetupWatchers()` creates directories and `FileSystemWatcher` instances for 7 folders synchronously during `WaitForZNetReady()`. Each creates file system handles and registers OS-level callbacks. All 7 log their creation.

With two mods (FiresRPGmaker + FiresSteamyDumps), this doubles to **14 watchers**.

**Fix:** 
1. Create watchers in a coroutine, one per frame
2. Consolidate to a single watcher on the parent directory with `IncludeSubdirectories = true`
3. Gate per-watcher creation logs behind verbose

---

## 10. MEDIUM — Reflection Lookups in Hot Paths

**File:** `Ascend.cs` lines 382-398, 421-429, 447-456

```csharp
var hashField = typeof(ZNetScene).GetField("m_namedPrefabs", 
    BindingFlags.NonPublic | BindingFlags.Instance);
```

**Problem:** These reflection lookups run during `AddPrefabsToZNetScene()` and `AddItemsToObjectDB()`. While they're not per-frame, they run during initialization and could be cached:

```csharp
// Cache once as static readonly:
private static readonly FieldInfo s_namedPrefabsField = 
    typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);

private static readonly FieldInfo s_itemByHashField = 
    typeof(ObjectDB).GetField("m_itemByHash", BindingFlags.Instance | BindingFlags.NonPublic);
```

This also eliminates repeated null checks and reduces allocations.

---

## 11. LOW — SnowClutterConfig Shader Property Dump

**File:** `SnowClutterConfig.cs` (lines visible in log output)

```
[SnowClutterConfig] Shader 'Custom/Grass' has 21 properties:
  [Color] _Color = RGBA(0.000, 0.000, 0.000, 0.000)
  [Texture] _MainTex = forest_groundcover_snow1
  ... (21 lines)
```

**Problem:** Dumps all 21 shader properties to log unconditionally. This is debug-only information.

**Fix:** Gate behind verbose logging.

---

## 12. LOW — TerrainCombinerAnchor Iterates AllCombiners in Start()

**File:** `TerrainCombinerAnchor.cs` lines 194-200

```csharp
foreach (var combiner in DirtFloorMeshCombiner.AllCombiners)
{
    if (combiner == null || combiner.Anchor == null) continue;
    ...
}
```

**Problem:** Each anchor's `Start()` iterates ALL existing combiners to check for duplicates. With N anchors loading simultaneously, this is O(N²). For 7 terrain disks this is negligible, but if the count grows to 50+, it could become noticeable.

**Fix:** Use a dictionary lookup by position hash:
```csharp
private static Dictionary<long, DirtFloorMeshCombiner> s_combinersByPosHash = new();

static long HashPosition(Vector3 pos) => 
    ((long)(pos.x * 10)) * 1000000L + (long)(pos.z * 10);
```

---

## 13. LOW — WaterCombinerCache Uses LINQ FirstOrDefault in Update Path

**File:** `WaterCombinerCache.cs` line 258

```csharp
groupData = _currentCache.Groups.FirstOrDefault(g => g.GroupId == groupId);
```

**Problem:** LINQ `FirstOrDefault` with a lambda allocates a delegate and enumerator each call. In the update/save path this adds GC pressure.

**Fix:** Use a `Dictionary<int, WaterCombinerGroupData>` for O(1) lookup:
```csharp
private static Dictionary<int, WaterCombinerGroupData> _groupLookup = new();
```

---

## 14. GOOD — Patterns Already Done Well

These patterns are already well-implemented and should be preserved:

| Pattern | Where | Notes |
|---------|-------|-------|
| **Component caching** | `CompanionAI.Awake()` | All frequently-used components cached in fields — no per-frame `GetComponent()` |
| **Background worker** | `BackgroundWorker<T>` | Proper Thread + Monitor pattern, no Task/async, callbacks via FrameBudgetScheduler |
| **Frame budget** | `FrameBudgetScheduler` | Priority lanes, guaranteed forward progress, time-budgeted |
| **Rate-limited logging** | `RateLimitedLogHandler` | Suppresses shader warnings, kinematic velocity spam, Valheim NREs |
| **Static character list** | `CompanionAI._tempCharacterList` | Reused list for target scanning — no allocation per scan |
| **Deferred terrain init** | `DirtFloorDeferredInit` | Physics bakes spread across frames (just needs budget integration) |
| **Raycast optimization** | `CodegenUIWiring.DisableUnnecessaryRaycasts()` | Disables raycasting on non-interactive UI elements |
| **Nested canvas** | `CodegenUIWiring.AddNestedCanvas()` | Isolates NPC panel from HUD canvas rebuild |
| **Dirty tracking** | `WaterCombinerCache._dirtyGroupIds` | Only saves changed groups |
| **Periodic save guard** | `PeriodicSaveAllDirty()` | Skips when no players connected |
| **Hash-based ZDO keys** | All anchor classes | Pre-computed `GetStableHashCode()` as static readonly |

---

## 15. Action Plan — Priority Order

### Phase 1 — Quick Wins (< 1 hour, biggest impact)

| # | Change | Files | Est. Savings |
|---|--------|-------|-------------|
| 1 | Change `configVerboseLogging` default to `false` | `ConfigManager.cs` | Hundreds of log lines |
| 2 | Gate per-item init logs behind verbose | `Ascend.cs` (×~15 calls) | ~50 log lines |
| 3 | Gate per-combiner restore logs behind verbose | `WaterPersistence.cs`, `WaterCombinerCache.cs`, `WaterMeshCombinerAnchor.cs`, `TerrainCombinerAnchor.cs`, `DirtFloorTerrainManager.cs`, `DirtFloorZoneRegistry.cs`, `DirtFloorMeshCombiner.*.cs` | ~180 log lines |
| 4 | Gate font enumeration behind verbose | `UIFontConfig.cs` or caller | 53 log lines |
| 5 | Gate shader property dump behind verbose | `SnowClutterConfig.cs` | 22 log lines |
| 6 | Remove `PlayerFileStorage.LogVersion()` ceremony | `VaultOfKnowledge.cs`, `PlayerFileStorage.cs` | 4 log lines |
| 7 | Consolidate archetype init to ONE summary log | `Ascend.cs` lines 617-657 | 5 log lines |

### Phase 2 — Budget Unification (2-3 hours)

| # | Change | Files | Impact |
|---|--------|-------|--------|
| 8 | Route `DirtFloorDeferredInit` work through `FrameBudgetScheduler` | `DirtFloorDeferredInit.cs` | Eliminates 8ms independent budget |
| 9 | Audit `MainThreadDispatcher.Enqueue()` callers, migrate to `FrameBudgetScheduler` | Various | Eliminates 4ms independent budget |
| 10 | Deprecate or thin `MainThreadDispatcher` | `MainThreadDispatcher.cs` | Single unified budget |

### Phase 3 — Optimization (1-2 hours)

| # | Change | Files | Impact |
|---|--------|-------|--------|
| 11 | Remove `Resources.FindObjectsOfTypeAll<Sprite>()` fallback | `CodegenUIWiring.cs` | 50-200ms hitch eliminated |
| 12 | Cache reflection field lookups as static readonly | `Ascend.cs` | Minor GC reduction |
| 13 | Replace LINQ `FirstOrDefault` with dictionary in WaterCombinerCache | `WaterCombinerCache.cs` | GC reduction in save path |
| 14 | Consolidate FileSystemWatchers to one parent watcher | `ServerConfigFileWatcher.cs` | 6 fewer OS handles |

### Phase 4 — Future Considerations

| # | Change | Notes |
|---|--------|-------|
| 15 | Add a `[ConditionalAttribute("DEBUG")]` style compile-time gate for verbose logs | Eliminates string formatting cost entirely when verbose is off |
| 16 | Consider a ring-buffer log sink instead of `Debug.Log` for high-frequency diagnostics | Avoids disk I/O for diagnostic data |
| 17 | Profile `CompanionAI.UpdateAI()` with 10+ companions active | May reveal per-frame costs in targeting/pathfinding |

---

## Summary

The mod's architecture is solid — `FrameBudgetScheduler`, `BackgroundWorker`, and deferred init show good engineering. The main issues are **operational** rather than architectural:

1. **Too many unconditional log calls** — the single biggest source of startup lag and I/O overhead
2. **Three independent frame budgets** — should be unified under `FrameBudgetScheduler`
3. **One expensive fallback** (`Resources.FindObjectsOfTypeAll`) that should be removed
4. **Verbose logging defaulting to ON** — turns diagnostic logging into production overhead

Implementing Phase 1 alone (mostly search-and-replace of `Debug.Log` ? verbose-gated) should cut startup log output by **~80%** and measurably improve server load time.
