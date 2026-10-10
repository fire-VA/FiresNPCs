using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Territories
{
    [HarmonyPatch]
    internal static partial class TerritoryRuntime
    {
        private static readonly HashSet<int> _loggedSpawnerlessTerritories = new();
        private static float _nextSpawnerDebugTime;
        private static readonly Dictionary<int, List<CustomSpawnerRuntime>> _customSpawnerRuntime = new();
        private static readonly Dictionary<string, List<CustomSpawnerRuntime>> _customSpawnerRuntimeByName = new(StringComparer.OrdinalIgnoreCase);
        private static bool _territoryEntryRpcRegistered;

        internal static double GetNow() => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        private static string BuildSpawnerSignature(CustomSpawnDef def)
        {
            if (def == null) return string.Empty;
            return string.Join("|", def.Prefab ?? string.Empty, def.DataName ?? string.Empty, def.Amount, def.Level, def.Interval,
                def.HasPosition, def.Position.x, def.Position.y, def.Position.z);
        }

        private static void TryRunCustomSpawners(Territory terr, Player player, bool force = false, bool logEntry = false, Vector3? fallbackAnchor = null)
        {
            if (terr == null)
            {
                if (Time.time >= _nextSpawnerDebugTime)
                {
                    Debug.Log("[FiresRPGmaker] CustomSpawner skipped: territory null");
                    _nextSpawnerDebugTime = Time.time + 5f;
                }
                return;
            }
            // player can be null when we only know a territory entry occurred; fall back to territory center
            Vector3 anchor = fallbackAnchor ?? (terr.X != null && terr.X.Length > 0 && terr.Y != null && terr.Y.Length > 0
                ? new Vector3(terr.X[0], 0f, terr.Y[0])
                : Vector3.zero);
            if (player != null)
            {
                anchor = player.transform.position;
            }

            if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.EWPSpawner))
            {
                if (!_loggedSpawnerlessTerritories.Contains(terr.UID))
                {
                    Debug.Log($"[FiresRPGmaker] CustomSpawner skipped: territory '{terr.Name}' (UID {terr.UID}) missing EWPSpawner flag; flags={terr.AdditionalFlags}");
                    _loggedSpawnerlessTerritories.Add(terr.UID);
                }
                return;
            }

            if (terr.CustomSpawners == null || terr.CustomSpawners.Count == 0)
            {
                Debug.Log($"[FiresRPGmaker] CustomSpawner skipped: no spawners configured for territory '{terr.Name}' (UID {terr.UID})");
                return;
            }
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                Debug.Log("[FiresRPGmaker] CustomSpawner skipped: not running on server");
                return;
            }

            // Rebuild runtime cache if the territory definition changed (reload) or didn't exist yet
            if (!_customSpawnerRuntime.TryGetValue(terr.UID, out var list))
            {
                if (!string.IsNullOrEmpty(terr.Name) && _customSpawnerRuntimeByName.TryGetValue(terr.Name, out var byName))
                {
                    list = byName;
                    _customSpawnerRuntime[terr.UID] = list;
                    Debug.Log($"[FiresRPGmaker] CustomSpawner reused runtime by name for territory '{terr.Name}' (uid {terr.UID})");
                }
            }

            bool needsRebuild = list == null || list.Count != terr.CustomSpawners.Count;
            if (!needsRebuild)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var sig = BuildSpawnerSignature(terr.CustomSpawners[i]);
                    if (!string.Equals(list[i].Signature, sig, StringComparison.Ordinal))
                    {
                        needsRebuild = true;
                        break;
                    }
                }
            }
            if (needsRebuild)
            {
                list = terr.CustomSpawners.Select(def => new CustomSpawnerRuntime
                {
                    Def = def,
                    NextTime = 0d,
                    Signature = BuildSpawnerSignature(def),
                    ActiveCount = 0,
                    TerritoryKey = terr.Name ?? terr.UID.ToString()
                }).ToList();
                _customSpawnerRuntime[terr.UID] = list;
                if (!string.IsNullOrEmpty(terr.Name))
                {
                    _customSpawnerRuntimeByName[terr.Name] = list;
                }
            }
            else if (!string.IsNullOrEmpty(terr.Name) && list != null)
            {
                // ensure name lookup is wired even when not rebuilding
                _customSpawnerRuntimeByName[terr.Name] = list;
            }

            if (logEntry)
            {
                Debug.Log($"[FiresRPGmaker] CustomSpawner entering '{terr.Name}' (UID {terr.UID}) defs={terr.CustomSpawners.Count} player={(player != null ? player.GetPlayerName() : "(no player)")} force={force}");
            }

            double now = GetNow();
            foreach (var runtime in list)
            {
                SyncExistingInstances(terr, runtime);
                RefreshActiveCount(runtime);
                string key = GetSpawnerKey(runtime);
                if (runtime.NextTime == 0d || runtime.NextTime == double.MaxValue)
                {
                    // initialize to "now" so first fire is immediate unless forced otherwise
                    runtime.NextTime = 0d;
                }
                if (!force && runtime.ActiveCount > 0)
                {
                    Debug.Log($"[FiresRPGmaker] CustomSpawner gated by active instances: terr='{terr.Name}' key='{key}' active={runtime.ActiveCount}");
                    continue; // wait until current wave dies
                }

                double gate = runtime.NextTime;
                if (!force && now < gate)
                {
                    Debug.Log($"[FiresRPGmaker] CustomSpawner on cooldown: terr='{terr.Name}' key='{key}' now={now:F1} gate={gate:F1}");
                    continue;
                }

                Debug.Log($"[FiresRPGmaker] CustomSpawner firing in territory '{terr.Name}' (UID {terr.UID}) -> prefab={runtime.Def.Prefab}, amt={runtime.Def.Amount}, data={runtime.Def.DataName}, interval={runtime.Def.Interval}");
                var spawned = SpawnCustom(runtime.Def, player, terr, anchor, runtime);
                runtime.ActiveCount += spawned;
                runtime.NextTime = double.MaxValue; // will be set when last spawn dies
                Debug.Log($"[FiresRPGmaker] CustomSpawner spawned {spawned} -> active={runtime.ActiveCount} nextTime=hold key='{key}'");
            }
        }

        private static int SpawnCustom(CustomSpawnDef def, Player player, Territory terr, Vector3 anchor, CustomSpawnerRuntime runtime)
        {
            if (def == null) return 0;
            var prefab = ZNetScene.instance?.GetPrefab(def.Prefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[FiresRPGmaker] EWPSpawner: prefab '{def.Prefab}' not found");
                return 0;
            }

            var center = def.HasPosition
                ? def.Position
                : new Vector3(
                    terr.X != null && terr.X.Length > 0 ? terr.X[0] : anchor.x,
                    anchor.y,
                    terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : anchor.z);
            try { Heightmap.GetHeight(center, out var h); center.y = h + 0.5f; } catch { }

            int count = Math.Max(1, def.Amount);
            float radius = terr.Radius > 0 ? terr.Radius * 0.5f : 20f;
            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                var pos = def.HasPosition
                    ? center + UnityEngine.Random.insideUnitSphere * 1f
                    : center + UnityEngine.Random.insideUnitSphere * Math.Max(1f, radius);
                try { Heightmap.GetHeight(pos, out var h); pos.y = h + 0.5f; } catch { pos.y = center.y; }

                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
                go.name = prefab.name;

                var character = go.GetComponent<Character>();
                if (character != null)
                {
                    if (def.Level > 1) character.SetLevel(def.Level);
                }
                else
                {
                    var drop = go.GetComponent<ItemDrop>();
                    if (drop != null && def.Level > 1)
                    {
                        drop.SetQuality(def.Level);
                    }
                }

                if (!string.IsNullOrEmpty(def.DataName))
                {
                    var data = PrefabDataFactory.LoadCustomSpawnerData(def.DataName);
                    if (!string.IsNullOrEmpty(data))
                    {
                        PrefabDataFactory.ApplyCustomData(go, data);
                    }
                    else
                    {
                        Debug.LogWarning($"[FiresRPGmaker] CustomSpawner data '{def.DataName}' not found for prefab {prefab.name}");
                    }
                }
                try
                {
                    var nview = go.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid())
                    {
                        nview.GetZDO().Set(SpawnerDataNameZdoKey, def.DataName ?? string.Empty);
                    }
                }
                catch { }

                // Attach watcher to track deaths and start interval
                if (runtime != null)
                {
                    runtime.Instances ??= new List<WeakReference<GameObject>>();
                    runtime.Instances.Add(new WeakReference<GameObject>(go));
                    var watcher = go.AddComponent<TerritorySpawnWatcher>();
                    watcher.SetRuntime(runtime, def.Interval > 0f ? def.Interval : 300f);
                }

                spawned++;
            }

            return spawned;
        }

        internal class CustomSpawnerRuntime
        {
            public CustomSpawnDef Def;
            public double NextTime;
            public string Signature;
            public int ActiveCount;
            public List<WeakReference<GameObject>> Instances;
            public string TerritoryKey;
        }

        private const string SpawnerDataNameZdoKey = "FiresRPGmaker_SpawnDataName";

        internal static void OnSpawnedDestroyed(CustomSpawnerRuntime runtime, double intervalSeconds, GameObject instance = null)
        {
            if (runtime == null) return;
            if (instance != null && runtime.Instances != null)
            {
                for (int i = runtime.Instances.Count - 1; i >= 0; i--)
                {
                    if (runtime.Instances[i] != null && runtime.Instances[i].TryGetTarget(out var go) && go == instance)
                    {
                        runtime.Instances.RemoveAt(i);
                        break;
                    }
                }
            }
            RefreshActiveCount(runtime);
            if (runtime.ActiveCount <= 0)
            {
                runtime.ActiveCount = 0;
                runtime.NextTime = GetNow() + (intervalSeconds > 0 ? intervalSeconds : 300d);
                Debug.Log($"[FiresRPGmaker] CustomSpawner all dead; scheduling next spawn at {runtime.NextTime:F1} key='{GetSpawnerKey(runtime)}' prefab='{runtime.Def?.Prefab}' data='{runtime.Def?.DataName}'");
            }
            else
            {
                Debug.Log($"[FiresRPGmaker] CustomSpawner instance destroyed but {runtime.ActiveCount} remain key='{GetSpawnerKey(runtime)}' prefab='{runtime.Def?.Prefab}' data='{runtime.Def?.DataName}'");
            }
        }

        private static int RefreshActiveCount(CustomSpawnerRuntime runtime)
        {
            if (runtime == null) return 0;
            int alive = 0;
            if (runtime.Instances != null)
            {
                for (int i = runtime.Instances.Count - 1; i >= 0; i--)
                {
                    var wr = runtime.Instances[i];
                    if (wr == null || !wr.TryGetTarget(out var go) || go == null)
                    {
                        runtime.Instances.RemoveAt(i);
                    }
                    else
                    {
                        alive++;
                    }
                }
            }
            runtime.ActiveCount = alive;
            return alive;
        }

        private static void SyncExistingInstances(Territory terr, CustomSpawnerRuntime runtime)
        {
            if (terr == null || runtime == null || runtime.Def == null) return;
            runtime.Instances ??= new List<WeakReference<GameObject>>();

            // Clean first
            RefreshActiveCount(runtime);

            var allChars = Character.GetAllCharacters();
            if (allChars == null) return;

            foreach (var ch in allChars)
            {
                if (ch == null) continue;
                var go = ch.gameObject;
                if (go == null) continue;
                var pos = go.transform.position;
                if (!terr.IsInside(pos)) continue;

                var prefabName = Utils.GetPrefabName(go);
                if (!string.Equals(prefabName, runtime.Def.Prefab, StringComparison.OrdinalIgnoreCase)) continue;

                if (!DataNameMatches(runtime.Def, go)) continue;

                bool alreadyTracked = runtime.Instances.Any(wr => wr != null && wr.TryGetTarget(out var existing) && existing == go);
                if (alreadyTracked) continue;

                runtime.Instances.Add(new WeakReference<GameObject>(go));
                var watcher = go.GetComponent<TerritorySpawnWatcher>() ?? go.AddComponent<TerritorySpawnWatcher>();
                watcher.SetRuntime(runtime, runtime.Def.Interval > 0f ? runtime.Def.Interval : 300f);
                Debug.Log($"[FiresRPGmaker] CustomSpawner tracked existing instance '{prefabName}' in terr='{terr.Name}' key='{GetSpawnerKey(runtime)}'");
            }

            RefreshActiveCount(runtime);
        }

        private static bool DataNameMatches(CustomSpawnDef def, GameObject go)
        {
            if (def == null || go == null) return false;
            if (string.IsNullOrEmpty(def.DataName))
            {
                try
                {
                    var nview = go.GetComponent<ZNetView>();
                    var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                    var marker = zdo != null ? zdo.GetString(SpawnerDataNameZdoKey) : string.Empty;
                    return string.IsNullOrEmpty(marker);
                }
                catch { return true; }
            }

            try
            {
                var nview = go.GetComponent<ZNetView>();
                var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                if (zdo == null) return false;
                var marker = zdo.GetString(SpawnerDataNameZdoKey) ?? string.Empty;
                return string.Equals(marker, def.DataName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string GetSpawnerKey(CustomSpawnerRuntime runtime)
        {
            if (runtime == null) return string.Empty;
            var terrKey = runtime.TerritoryKey ?? string.Empty;
            var sig = runtime.Signature ?? runtime.Def?.Prefab ?? string.Empty;
            return string.Join("|", terrKey, sig);
        }

        private static void EnsureTerritoryEntryRpc()
        {
            if (_territoryEntryRpcRegistered) return;
            if (ZRoutedRpc.instance == null) return;
            try
            {
                ZRoutedRpc.instance.Register<long, string, string, string, int, string>("FiresRPGmaker_Territory_ClientEntered", RPC_TerritoryClientEntered);
                _territoryEntryRpcRegistered = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory entry RPC register failed: {ex.Message}");
            }
        }

        private static void RPC_TerritoryClientEntered(long sender, long playerId, string playerName, string platformId, string platform, int territoryUid, string territoryName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                var terr = TerritoryLedger.Territories.FirstOrDefault(t => t.UID == territoryUid)
                           ?? TerritoryLedger.Territories.FirstOrDefault(t => string.Equals(t.Name, territoryName, StringComparison.OrdinalIgnoreCase));
                if (terr == null)
                {
                    Debug.Log($"[FiresRPGmaker] Territory entry RPC: territory not found (uid {territoryUid}, name '{territoryName}') from {playerName} ({playerId})");
                    return;
                }

                Debug.Log($"[FiresRPGmaker] Territory entry RPC: spawning for '{terr.Name}' (UID {terr.UID}) from {playerName} ({playerId}) platformId={platformId}");

                try
                {
                    TryRunCustomSpawners(terr, null, force: false, logEntry: true, fallbackAnchor: terr.Pos3D());
                }
                catch (Exception exSpawn)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Territory entry RPC immediate spawn failed: {exSpawn.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory entry RPC handler failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        private static void ZNet_Awake_Postfix()
        {
            try { EnsureTerritoryEntryRpc(); } catch { }
        }
    }
}
