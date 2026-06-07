using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Diagnostics
{
    // ????????????????????????????????????????????????????????????????????????
    //   TEMPORARY DIAGNOSTIC � REMOVE ONCE LEAK SOURCE IS IDENTIFIED
    // ????????????????????????????????????????????????????????????????????????
    //
    // PURPOSE
    // -------
    // We've been chasing an intermittent NRE flood in vanilla
    // ZNetScene.RemoveObjects:
    //
    //   foreach (ZNetView znetView in this.m_instances.Values)
    //       if ((int) znetView.GetZDO().TempRemoveEarmark != (int) num)
    //           ...
    //
    // The NRE is mechanical: it can ONLY happen if some entry in
    // m_instances has either
    //   (a) a Unity-destroyed ZNetView Component (Component == null returns
    //       true; calling GetZDO() / get_gameObject() throws), OR
    //   (b) a live ZNetView whose m_zdo was reset to null (so GetZDO()
    //       returns null and .TempRemoveEarmark dereferences null).
    //
    // Both states are produced by SOME code path destroying a ZNetView'd
    // GameObject without going through ZNetScene.Destroy(go), which is the
    // only path that atomically clears m_instances. Several confirmed
    // candidate sites have already been migrated through CompanionNetworkHelper
    // / NetworkObjectHelper.SafeDestroy, but the spam recurs in the user's
    // playthrough � meaning at least one source is still unidentified.
    //
    // Static analysis has hit diminishing returns (audited every Object.Destroy
    // call site, can't see what's still leaking), so this diagnostic captures
    // the leak FROM THE OTHER SIDE: it inspects m_instances itself and reports
    // which prefab type is being stranded. With a prefab name in hand we can
    // grep the source for whoever creates/destroys it and fix that path.
    //
    // BEHAVIOUR
    // ---------
    //   1. Runs as a Harmony [HarmonyPrefix] on ZNetScene.RemoveObjects with
    //      Priority.First so we run before any other mod's patches and before
    //      vanilla's loop. We also patch CreateDestroyObjects with the same
    //      logic for belt-and-suspenders coverage of any callers that might
    //      reach RemoveObjects through a different path.
    //   2. Walks the m_instances dictionary once per tick (~30 Hz, cheap �
    //      typical worlds have a few hundred entries).
    //   3. For each entry whose Component is Unity-destroyed OR whose ZDO
    //      is null, we resolve the PREFAB NAME from the dictionary key
    //      (kv.Key is the ZDO; ZDO.GetPrefab() returns the stable hash;
    //      ZNetScene.GetPrefab(hash) gives us the prefab GameObject and
    //      its name).
    //   4. We log ONE warning per unique prefab name per session via the
    //      _reportedPrefabs set, so a persistent leak floods at most
    //      <number-of-distinct-prefab-types> log lines instead of every
    //      ~33 ms forever. Per-prefab counts are accumulated across ticks.
    //   5. AFTER logging, we remove the stale keys from m_instances so
    //      vanilla's RemoveObjects loop runs without throwing. This is
    //      explicitly self-healing: with the diagnostic in place the user's
    //      session is playable while we hunt the source. NOTHING ELSE is
    //      modified � the rest of m_instances is untouched and vanilla
    //      proceeds normally afterwards.
    //   6. Returns true so vanilla's loop still runs after the prefix.
    //
    // SAFETY
    // ------
    //   � Wrapped in try/catch at every level. A defensive sweep MUST NOT
    //     break the game loop � if anything throws, we silently bail and
    //     let vanilla try its iteration (the caller sees no change).
    //   � No mutation other than removing entries we've already determined
    //     to be unusable (Unity-destroyed Component or null-ZDO).
    //   � Uses Harmony field-injection via the `___m_instances` parameter
    //     name convention � works on .NET Framework 4.8 / Harmony 2.x.
    //
    // HOW TO USE
    // ----------
    //   1. Build the mod and reproduce the NRE flood in-game.
    //   2. Look in the BepInEx LogOutput for lines tagged
    //      `[ZNetSceneStaleInstanceDiagnostic]` � each line names a prefab
    //      that was leaking.
    //   3. grep the codebase for the prefab name (or the system that
    //      spawns/destroys it). That's the bug site. Migrate the destroy
    //      to NetworkObjectHelper.SafeDestroy / CompanionNetworkHelper.Destroy.
    //   4. Once the leak is confirmed fixed, DELETE this entire file. The
    //      diagnostic is not meant to ship long-term.
    //
    // ????????????????????????????????????????????????????????????????????????

    [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
    internal static class ZNetSceneStaleInstanceDiagnostic_RemoveObjects
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool Prefix(ZNetScene __instance, Dictionary<ZDO, ZNetView> ___m_instances)
        {
            ZNetSceneStaleInstanceDiagnostic.Sweep(__instance, ___m_instances, "RemoveObjects");
            return true;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
    internal static class ZNetSceneStaleInstanceDiagnostic_CreateDestroyObjects
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool Prefix(ZNetScene __instance, Dictionary<ZDO, ZNetView> ___m_instances)
        {
            // Same sweep, called from the upstream entry point so we catch the
            // bad state one frame earlier than RemoveObjects would.
            ZNetSceneStaleInstanceDiagnostic.Sweep(__instance, ___m_instances, "CreateDestroyObjects");
            return true;
        }
    }

    /// <summary>
    /// Shared sweep logic for both patch sites. Static so the dedup state
    /// (<see cref="_reportedPrefabs"/>) is shared across both call paths.
    /// </summary>
    internal static class ZNetSceneStaleInstanceDiagnostic
    {
        // Reused buffers � avoid allocating per ~33ms tick.
        private static readonly List<ZDO> _staleKeys = new List<ZDO>(16);
        private static readonly Dictionary<string, int> _staleCountsThisTick = new Dictionary<string, int>(8);

        // One log line per distinct prefab name per session, so a persistent
        // leak doesn't flood the log. Cleared on world unload via
        // ResetForNewSession() (call sites: see bottom of class).
        private static readonly HashSet<string> _reportedPrefabs = new HashSet<string>(StringComparer.Ordinal);

        // Cumulative counters across all ticks � useful for confirming the
        // leak rate when we report findings.
        private static readonly Dictionary<string, int> _cumulativeStaleCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        private const string LOG_PREFIX = "[ZNetSceneStaleInstanceDiagnostic]";

        public static void Sweep(ZNetScene scene, Dictionary<ZDO, ZNetView> instances, string callSite)
        {
            if (scene == null || instances == null || instances.Count == 0) return;

            try
            {
                _staleKeys.Clear();
                _staleCountsThisTick.Clear();

                // Pass 1: scan for stale entries. Don't mutate inside the
                // foreach � Dictionary throws on concurrent modification.
                foreach (var kv in instances)
                {
                    var view = kv.Value;
                    bool stale = false;

                    // Unity-destroyed Component: == null returns true while
                    // the C# reference is non-null. This is exactly the case
                    // that NREs on `view.GetZDO()` in the vanilla loop.
                    if ((object)view == null || view == null)
                    {
                        stale = true;
                    }
                    else
                    {
                        // Component alive but its ZDO got reset without the
                        // dict entry being cleaned. `view.GetZDO()` would
                        // return null and `.TempRemoveEarmark` NREs.
                        ZDO viewZdo = null;
                        try { viewZdo = view.GetZDO(); }
                        catch { stale = true; }
                        if (!stale && viewZdo == null) stale = true;
                    }

                    if (stale) _staleKeys.Add(kv.Key);
                }

                if (_staleKeys.Count == 0) return;

                // Pass 2: resolve prefab names from the ZDO keys (we still
                // have the keys regardless of the ZNetView's state) and
                // count per-prefab.
                foreach (var zdo in _staleKeys)
                {
                    string prefabName = ResolvePrefabName(scene, zdo);

                    if (_staleCountsThisTick.TryGetValue(prefabName, out int n))
                        _staleCountsThisTick[prefabName] = n + 1;
                    else
                        _staleCountsThisTick[prefabName] = 1;

                    if (_cumulativeStaleCounts.TryGetValue(prefabName, out int total))
                        _cumulativeStaleCounts[prefabName] = total + 1;
                    else
                        _cumulativeStaleCounts[prefabName] = 1;
                }

                // Pass 3: log each NEW prefab name we've never reported
                // before. After the first sighting we go silent for that
                // prefab � the cumulative counter keeps tracking but doesn't
                // re-log.
                foreach (var kv in _staleCountsThisTick)
                {
                    string prefab = kv.Key;
                    if (_reportedPrefabs.Add(prefab))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"{LOG_PREFIX} STALE m_instances entry detected via {callSite} � " +
                            $"prefab='{prefab}', count_this_tick={kv.Value}. " +
                            $"This means the GameObject was destroyed without going through " +
                            $"ZNetScene.Destroy(). Search the codebase for code that destroys " +
                            $"'{prefab}' (or its container/spawner) and migrate the destroy to " +
                            $"NetworkObjectHelper.SafeDestroy / CompanionNetworkHelper.Destroy. " +
                            $"(Further sightings of this prefab will be silently counted; " +
                            $"see _cumulativeStaleCounts for totals.)");
                    }
                }

                // Pass 4: remove the stale entries so vanilla's loop runs
                // without throwing. We've already captured the diagnostic
                // info we need � keeping these entries would let the NRE
                // flood the log every tick and crowd out the actual signal.
                foreach (var key in _staleKeys)
                {
                    instances.Remove(key);
                }

                _staleKeys.Clear();
                _staleCountsThisTick.Clear();
            }
            catch (Exception ex)
            {
                // Defensive � the diagnostic must never break the game loop.
                // If we throw here, vanilla gets to try its iteration which
                // is the same state we'd be in without this patch anyway.
                try
                {
                    UnityEngine.Debug.LogError($"{LOG_PREFIX} sweep error (callSite={callSite}): {ex.Message}");
                }
                catch { /* truly nothing else we can do */ }
            }
        }

        private static string ResolvePrefabName(ZNetScene scene, ZDO zdo)
        {
            try
            {
                if (zdo == null) return "<null-zdo>";

                int hash = zdo.GetPrefab();
                if (hash == 0) return "<no-prefab-hash>";

                var prefab = scene != null ? scene.GetPrefab(hash) : null;
                if (prefab == null) return $"<unknown-prefab:{hash}>";

                return prefab.name;
            }
            catch
            {
                return "<resolve-failed>";
            }
        }

        /// <summary>
        /// Optional: call from a world-unload hook to reset session state.
        /// Not wired up by default � leak diagnostics are most useful when
        /// they ACCUMULATE across the session, and a fresh world-load
        /// already starts with an empty m_instances (ZNetScene.Shutdown
        /// clears it), so the per-prefab dedup automatically becomes
        /// relevant again only if a new prefab leaks.
        /// </summary>
        public static void ResetForNewSession()
        {
            _reportedPrefabs.Clear();
            _cumulativeStaleCounts.Clear();
        }

        /// <summary>
        /// For ad-hoc inspection from a debug command. Returns a snapshot of
        /// what's been reported this session.
        /// </summary>
        public static IReadOnlyDictionary<string, int> GetCumulativeStaleCounts()
        {
            return _cumulativeStaleCounts;
        }
    }
}
