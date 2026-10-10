using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using BepInEx;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Territories;
using FiresCore.Lifecycle;

using FiresCore.Sync;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Runtime client-side territory enforcement (flags, movement, restrictions).
    /// This is a lightweight subset of Marketplace behavior to get core effects working.
    /// </summary>
    [HarmonyPatch]
    internal static partial class TerritoryRuntime
    {
        public static Territory Current { get; private set; }
        private static Territory _last;
        private static float _tick;
        private static float _nextPeriodicTime;
        private static PhysicsMaterial _origPhysMat;
        private static PhysicsMaterial _icePhysMat;
        private static CapsuleCollider _lastCollider;
        private static float? _origDrag;
        private static float? _origAngularDrag;
        private static float _baseWalkSpeed;
        private static float _baseRunSpeed;
        private static float _baseSwimSpeed;
        private static float _nextTerritoryCheck;
        private static Vector3 _lastPos;
        private static float _nextIceCheck;
        private static SE_Stats _iceStatus;
        private static readonly int IceStatusHash = "DeepNorth_IceSlip".GetStableHashCode();

        private static bool IsPermitted(Territory t, Player player)
        {
            if (t == null || player == null) return true;
            if (AdminBypass(player)) return true;
            var pid = player.GetPlayerName() ?? string.Empty;
            if (!string.IsNullOrEmpty(t.Owner) && string.Equals(t.Owner, pid, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(t.Permitted))
            {
                if (t.Permitted.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(p => string.Equals(p.Trim(), pid, StringComparison.OrdinalIgnoreCase))) return true;
            }
            // KG parity: OnlyForGuild / OnlyForFaction grant access to anyone whose guild/faction matches.
            // OnlyForGuild — uses our GuildAPI (mirrors KG's API.GetOwnGuild()).
            if (t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.OnlyForGuild) && !string.IsNullOrEmpty(t.OnlyForGuild))
            {
                try
                {
                    string ownGuildName = FiresCore.Bridge.GuildBridge.LocalGuildName();
                    if (ownGuildName != null && string.Equals(ownGuildName, t.OnlyForGuild, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* guild source not loaded — skip */ }
            }
            // OnlyForFaction: members of the named faction (its id or display name) are permitted, the way OnlyForGuild
            // permits guild members. This was a placeholder from before the Faction module shipped, so the flag parsed
            // and did nothing.
            if (t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.OnlyForFaction) && !string.IsNullOrEmpty(t.OnlyForFaction))
            {
                try
                {
                    string factionId = player == Player.m_localPlayer
                        ? global::FiresRPGmaker.Modules.Factions.FactionMembership.LocalFactionId()
                        : global::FiresRPGmaker.Modules.Factions.FactionMembership.FactionIdOf(player);
                    var faction = global::FiresRPGmaker.Modules.Factions.FactionManager.Get(factionId);
                    string wanted = t.OnlyForFaction.Trim();
                    if (!string.IsNullOrEmpty(factionId)
                        && (string.Equals(factionId, wanted, StringComparison.OrdinalIgnoreCase)
                            || (faction != null && string.Equals(faction.Name, wanted, StringComparison.OrdinalIgnoreCase))))
                        return true;
                }
                catch { /* faction data not loaded: skip */ }
            }

            // Steam-ID territory OWNERSHIP (server-synced). Owning this territory's NAME grants build rights on
            // every area sharing that name. Keyed by Steam ID so it survives renames, and granted/sold via the
            // GiveTerritory / RemoveTerritory dialogue commands.
            try
            {
                if (TerritoryOwnership.IsOwner(t.Name, global::FiresRPGmaker.Modules.Dialogues.EwpDataBridge.GetLocalSteamId()))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool AdminBypass(Player player)
        {
            try
            {
                if (player == null) return false;
                if (ConfigManager.Instance?.configAdminBypassTerritory?.Value != true) return false;

                // Check per-player override stored on server
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    if (_adminBypassOverrides.TryGetValue(player.GetPlayerID(), out var enable) && !enable)
                    {
                        return false;
                    }
                }

                // PER-PLAYER admin check — deliberately NOT AdminSyncing.IsLocalAdmin(). That is a LOCAL-player
                // gate that short-circuits true on IsServer(); using it here would grant EVERY player admin
                // bypass on a dedicated server. On the server the authority is the per-player IsAdmin(playerId);
                // on a client the local-admin flags apply to the local player. Left exactly as it shipped.
                if (FiresRPGmaker.Instance?.configSync?.IsAdmin == true) return true;
                if (ConfigSync.lockExempt) return true;

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    return AdminSyncing.IsAdmin(player.GetPlayerID());
                }
            }
            catch { }
            return false;
        }

        private static List<string> GetUniqueFlagNames(Territory terr)
        {
            var names = new List<string>();
            if (terr == null) return names;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TerritoryFlags flag in Enum.GetValues(typeof(TerritoryFlags)))
            {
                if (flag == TerritoryFlags.None) continue;
                if (!terr.Flags.HasFlag(flag)) continue;
                var name = flag.ToString();
                if (seen.Add(name)) names.Add(name);
            }

            foreach (AdditionalTerritoryFlags flag in Enum.GetValues(typeof(AdditionalTerritoryFlags)))
            {
                if (flag == AdditionalTerritoryFlags.None) continue;
                if (flag == AdditionalTerritoryFlags.CustomJereSpawner) continue; // skip legacy alias
                if (!terr.AdditionalFlags.HasFlag(flag)) continue;
                var name = flag.ToString();
                if (seen.Add(name)) names.Add(name);
            }

            return names;
        }

        private static string FormatFlagsMultiLine(Territory terr)
        {
            if (terr == null) return string.Empty;
            var list = GetUniqueFlagNames(terr);
            if (list.Count == 0) return string.Empty;

            var lines = new List<string>();
            for (int i = 0; i < list.Count; i += 2)
            {
                lines.Add(string.Join(", ", list.Skip(i).Take(2)));
            }
            return string.Join("\n", lines);
        }

        private static readonly HashSet<int> _paintedTerritories = new();
        private static readonly HashSet<int> _mapRevealedTerritories = new();
        private static readonly Dictionary<long, bool> _adminBypassOverrides = new();
        private static bool _adminBypassRpcRegistered;
        private static bool _lastBypassSent;
        private static bool _lastBypassSentInitialized;
        private static long _lastBypassPlayerId;
        private static bool _adminBypassConfigHooked;
        private static readonly AccessTools.FieldRef<Fireplace, bool> FireplaceInfiniteFuelRef = AccessTools.FieldRefAccess<Fireplace, bool>("m_infiniteFuel");

        private static void EnsureAdminBypassRpc()
        {
            if (_adminBypassRpcRegistered) return;
            if (ZRoutedRpc.instance == null) return;

            // Server handler: record per-player preference
            ZRoutedRpc.instance.Register<long, bool>("FiresRPGmaker_Territory_AdminBypass", (sender, playerId, enabled) =>
             {
                 try
                 {
                     if (!ZNet.instance || !ZNet.instance.IsServer()) return;
                     if (!AdminSyncing.IsAdmin(sender)) return;
                     if (enabled)
                     {
                         _adminBypassOverrides.Remove(playerId); // clearing restores default bypass behavior
                     }
                     else
                     {
                         _adminBypassOverrides[playerId] = false; // explicit opt-out
                     }
                 }
                 catch { }
             });

            _adminBypassRpcRegistered = true;
        }

        private static void HookAdminBypassConfig()
        {
            if (_adminBypassConfigHooked) return;
            var entry = ConfigManager.Instance?.configAdminBypassTerritory;
            if (entry == null) return;
            entry.SettingChanged += (s, e) => SendAdminBypassPreference(Player.m_localPlayer, force: true);
            _adminBypassConfigHooked = true;
        }

        private static void SendAdminBypassPreference(Player player, bool force = false)
        {
            if (player == null) return;
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            if (ZRoutedRpc.instance == null) return;
            var pid = player.GetPlayerID();
            if (!_lastBypassSentInitialized || _lastBypassPlayerId != pid)
            {
                _lastBypassSentInitialized = false; // force send for new player/session
            }
            bool desired = ConfigManager.Instance?.configAdminBypassTerritory?.Value == true;
            if (!force && _lastBypassSentInitialized && desired == _lastBypassSent) return;
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_Territory_AdminBypass", player.GetPlayerID(), desired);
                _lastBypassSent = desired;
                _lastBypassSentInitialized = true;
                _lastBypassPlayerId = pid;
            }
            catch { }
        }

        private static bool HasRestrictionBypass(Territory terr, Player player)
        {
            if (terr == null) return true;
            if (AdminBypass(player)) return true;
            return IsPermitted(terr, player);
        }

        // Public wrappers for cross-file patch callers (see TerritoryRuntime.MissingFlagPatches.cs).
        public static bool HasRestrictionBypassPublic(Territory terr, Player player) => HasRestrictionBypass(terr, player);
        public static Territory GetTerritory(Vector3 pos) => GetTerritoryInternal(pos);

        private static void ApplyMoveSpeedMultiplier(Player player)
        {
            if (player == null) return;
            if (_baseWalkSpeed <= 0f)
            {
                _baseWalkSpeed = player.m_walkSpeed;
                _baseRunSpeed = player.m_runSpeed;
                _baseSwimSpeed = player.m_swimSpeed;
            }
            float mul = Current != null ? Current.MoveSpeedMultiplier : 1f;
            if (mul <= 0f) mul = 1f;
            player.m_walkSpeed = _baseWalkSpeed * mul;
            player.m_runSpeed = _baseRunSpeed * mul;
            player.m_swimSpeed = _baseSwimSpeed * mul;
        }

        private static bool IsWithinHeight(Territory terr, float y)
        {
            if (terr == null) return false;
            var bounds = terr.HeightBounds;
            int min = bounds.Item1;
            int max = bounds.Item2;
            bool hasCustom = !(min == -100000 && max == 100000);
            if (!hasCustom)
            {
                max = 600; // default ceiling when not explicitly configured
            }
            return y >= min && y <= max;
        }

        private static Territory GetTerritoryInternal(Vector3 pos)
        {
            var terr = TerritorySyncedState.GetCurrent(pos);
            if (terr == null) return null;
            return IsWithinHeight(terr, pos.y) ? terr : null;
        }
        private static Territory GetTerritory(Player player) => player == null ? null : GetTerritoryInternal(player.transform.position);

        // ── Vedr season / weather while inside (0.0.533, Fire 10-08) ──────────────────────────────────────────────────
        // ForceSeason / ForceVedrWeather go through Vedr's public API (Vedr.VedrApi, API 7+), found by name: no hard
        // reference. Client-local only (Vedr refuses on a dedicated server); players outside keep the world season.
        // One owner tag per territory UID; Clear on exit falls back to the next active override, then the world.
        private static Type s_vedrApi;
        private static bool s_vedrResolved, s_vedrWarned;

        private static Type VedrApi()
        {
            if (s_vedrResolved) return s_vedrApi;
            s_vedrResolved = true;
            try
            {
                var t = Type.GetType("Vedr.VedrApi, Norger.Vedr");
                var ver = t?.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                if (t != null && ver != null && Convert.ToInt32(ver.GetValue(null)) >= 7) s_vedrApi = t;
            }
            catch { }
            return s_vedrApi;
        }

        private static void CallVedr(string method, params object[] args)
        {
            try { VedrApi()?.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, args); }
            catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] Vedr {method} failed: {ex.Message}"); }
        }

        // The tag currently applied. Cleared on exit, logout and world unload (lead 10-08: never rely on Vedr resetting);
        // a territory removed while inside drops Current on the next tick, which comes through ApplyVedr as an exit.
        private static string s_vedrActiveTag;

        private static void ClearVedrActive()
        {
            if (s_vedrActiveTag == null || VedrApi() == null) { s_vedrActiveTag = null; return; }
            CallVedr("ClearLocalSeasonOverride", s_vedrActiveTag);
            CallVedr("ClearLocalWeatherOverride", s_vedrActiveTag);
            s_vedrActiveTag = null;
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
        [HarmonyPrefix]
        private static void Vedr_Logout() => ClearVedrActive();

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPrefix]
        private static void Vedr_WorldUnload() => ClearVedrActive();

        private static bool HasVedrFlags(Territory t) => t != null && (!string.IsNullOrEmpty(t.ForceSeason) || !string.IsNullOrEmpty(t.ForceVedrWeather));

        private static void ApplyVedr(Territory oldTerr, Territory newTerr)
        {
            if (!HasVedrFlags(oldTerr) && !HasVedrFlags(newTerr)) return;
            if (VedrApi() == null)
            {
                if (!s_vedrWarned && HasVedrFlags(newTerr))
                {
                    s_vedrWarned = true;
                    Debug.LogWarning($"[FiresRPGmaker] Territory '{newTerr.Name}': ForceSeason / ForceVedrWeather need Vedr (API 7+); ignoring them.");
                }
                return;
            }
            if (HasVedrFlags(oldTerr) || s_vedrActiveTag != null)
            {
                ClearVedrActive();
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{oldTerr?.Name}' Vedr overrides cleared");
            }
            if (HasVedrFlags(newTerr))
            {
                string tag = "RPGmaker:" + newTerr.UID;
                s_vedrActiveTag = tag;
                if (!string.IsNullOrEmpty(newTerr.ForceSeason)) CallVedr("SetLocalSeasonOverride", newTerr.ForceSeason, tag);
                if (!string.IsNullOrEmpty(newTerr.ForceVedrWeather)) CallVedr("SetLocalWeatherOverride", newTerr.ForceVedrWeather, tag);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{newTerr.Name}' Vedr season '{newTerr.ForceSeason}' weather '{newTerr.ForceVedrWeather}'");
            }
        }
        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        private static void PlayerUpdate_Postfix(Player __instance)
        {
            if (__instance == null) return;
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            bool isOwner = __instance.IsOwner();

            // CRITICAL FIX: Only run territory tracking for the LOCAL player on clients
            // The server should NOT track territory changes using static state because
            // all players share the same static variables, causing oscillation
            // Server-side territory enforcement (NoBuild, NoPortals, etc.) works by
            // querying GetTerritory() directly in their patches, not via Current
            bool isLocalPlayer = __instance == Player.m_localPlayer;
            if (!isLocalPlayer) return;

            EnsureAdminBypassRpc();
            HookAdminBypassConfig();
            EnsureTerritoryEntryRpc();

            // push local admin bypass preference to server when it changes
            if (Player.m_localPlayer == __instance && ZNet.instance != null && !ZNet.instance.IsServer() && ZRoutedRpc.instance != null)
            {
                var pid = __instance.GetPlayerID();
                if (!_lastBypassSentInitialized || _lastBypassPlayerId != pid)
                {
                    _lastBypassSentInitialized = false; // reset to force send for new session
                }
                bool desired = ConfigManager.Instance?.configAdminBypassTerritory?.Value == true;
                if (!_lastBypassSentInitialized || desired != _lastBypassSent)
                {
                    SendAdminBypassPreference(__instance);
                }
            }
            if (!ZNet.instance.IsServer())
            {
                // INTENTIONALLY EMPTY.
                //
                // We used to call TerritorySyncedState.RequestFromServer() here whenever
                // ClientTerritories was empty. That fired EVERY FRAME on a client whose
                // server happens to have zero territories defined (or whose initial sync
                // got dropped) - each call ran Directory.GetFiles + MD5-hashed every
                // .cfg in the Territories folder to build the manifest, which produced
                // visible per-frame stalls and the "attempt 51, have 0 in cache" log
                // spam users were reporting.
                //
                // The retry path is fully owned by Ascend.RetryTerritorySync(): one
                // request on WaitForZNetReady + a 12 s coroutine that gives up at
                // MaxRequestRetries. If the user later wants a manual refresh, that's
                // what the explicit /territory or admin commands are for.
            }

            var pos = __instance.transform.position;
            bool shouldCheck = Time.time >= _nextTerritoryCheck || (pos - _lastPos).sqrMagnitude > 1f;
            if (shouldCheck)
            {
                _lastPos = pos;
                _nextTerritoryCheck = Time.time + 0.2f; // throttle territory lookup to ~5 Hz
                var newTerr = GetTerritory(__instance);

                // Check if territory actually changed by comparing both reference and UID
                bool territoryChanged = false;
                if (!ReferenceEquals(newTerr, Current))
                {
                    int currentUID = Current?.UID ?? 0;
                    int newUID = newTerr?.UID ?? 0;
                    territoryChanged = currentUID != newUID;
                }

                if (territoryChanged)
                {
                    var old = Current;
                    Current = newTerr;
                    OnTerritoryChanged(old, Current, __instance);
                    if (Current != null && Current.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.RevealOnMap))
                    {
                        TryRevealOnMap(Current);
                    }
                    _last = Current;
                    _nextPeriodicTime = 0f;
                }
            }

            Surveyor.Tick(__instance);
            OutlineSurveyor.Tick(__instance);
            ApplyTerritoryEffects(__instance);

            // queue removal: no pending queue processing
            // if (isServer)
            // {
            // TickPendingEntries();
            // }
        }

        private static readonly string[] IcePrefabs = new[] { "ice_rock1", "ice1" };
        private const string IcePrefab = "ice_rock1";
        private const float IceCheckInterval = 2f;
        private const int IceMaxNearby = 20;
        private const float DeepWaterThreshold = 8f;
        private const float RockDeepScalePerMeter = 0.08f;

        private static void TrySpawnIceFlows(Player player)
        {
            if (player == null) return;
            if (Time.time < _nextIceCheck) return;
            _nextIceCheck = Time.time + IceCheckInterval;

            var pos = player.transform.position;
            if (!IsInDeepNorthOceanBand(pos)) return;

            float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            float ground = pos.y;
            try { Heightmap.GetHeight(pos, out ground); } catch { }
            float depth = waterLevel - ground;
            if (depth < 2f) return; // need ocean-ish

            // choose available ice prefab (filter rock in deep water so floating ice spawns)
            var availablePrefabs = IcePrefabs
                .Select(name => ZNetScene.instance?.GetPrefab(name))
                .Where(p => p != null)
                .ToList();
            if (availablePrefabs.Count == 0) return;

            float radius = 120f;
            var hits = Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore);
            int existing = 0;
            if (hits != null)
            {
                foreach (var h in hits)
                {
                    if (h == null) continue;
                    var go = h.attachedRigidbody ? h.attachedRigidbody.gameObject : h.gameObject;
                    if (go != null && IcePrefabs.Any(n => go.name.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing++;
                        if (existing >= IceMaxNearby) break;
                    }
                }
            }
            if (existing >= IceMaxNearby) return;

            foreach (var prefab in availablePrefabs)
            {
                for (int i = 0; i < 8 && existing < IceMaxNearby; i++)
                {
                    var spawnPos = pos + UnityEngine.Random.insideUnitSphere * radius;
                    spawnPos.y = waterLevel;

                    // ensure reasonably deep water
                    float g = spawnPos.y;
                    try { Heightmap.GetHeight(spawnPos, out g); } catch { }
                    var spawnDepth = waterLevel - g;
                    if (spawnDepth < 1.5f) continue;

                    // choose prefab: in deep water,10% rock, otherwise ice1 preferred
                    GameObject chosen = prefab;
                    if (spawnDepth >= DeepWaterThreshold)
                    {
                        bool rockRoll = UnityEngine.Random.value <= 0.10f;
                        chosen = rockRoll ? prefab : availablePrefabs.FirstOrDefault(p => string.Equals(p.name, "ice1", StringComparison.OrdinalIgnoreCase)) ?? prefab;
                    }

                    var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                    var go = UnityEngine.Object.Instantiate(chosen, spawnPos, rot);
                    go.name = chosen.name;
                    if (string.Equals(chosen.name, "ice1", StringComparison.OrdinalIgnoreCase))
                    {
                        float scale = UnityEngine.Random.Range(0.6f, 2.2f);
                        go.transform.localScale = new Vector3(scale, scale, scale);
                    }
                    else if (string.Equals(chosen.name, "ice_rock1", StringComparison.OrdinalIgnoreCase))
                    {
                        float scale = 1f + Mathf.Clamp((spawnDepth - DeepWaterThreshold) * RockDeepScalePerMeter, 0f, 3f);
                        go.transform.localScale = new Vector3(scale, scale, scale);
                    }
                    existing++;
                    if (existing >= IceMaxNearby) break;
                }
                if (existing >= IceMaxNearby) break;
            }
        }

        private static bool IsInDeepNorthArea(Vector3 pos)
        {
            try
            {
                if (WorldGenerator.IsDeepnorth(pos.x, pos.z)) return true;
                Heightmap.Biome biome = Heightmap.Biome.None;
                try { biome = Heightmap.FindBiome(pos); } catch { }
                return biome == Heightmap.Biome.DeepNorth;
            }
            catch { return false; }
        }

        private static bool IsInDeepNorthOceanBand(Vector3 pos)
        {
            if (IsInDeepNorthArea(pos)) return true;
            return pos.z >= 7000f;
        }

        [HarmonyPatch(typeof(Fish), "Awake")]
        [HarmonyPostfix]
        private static void Fish_Awake_Postfix(Fish __instance)
        {
            try
            {
                if (__instance == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                var pos = __instance.transform.position;
                if (!IsInDeepNorthOceanBand(pos)) return;
                if (UnityEngine.Random.value > 0.2f) return;

                var availablePrefabs = IcePrefabs
                    .Select(name => ZNetScene.instance?.GetPrefab(name))
                    .Where(p => p != null)
                    .ToList();
                if (availablePrefabs.Count == 0) return;

                var waterLevelNow = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                var spawnDepth = waterLevelNow - __instance.transform.position.y;
                var deep = spawnDepth >= DeepWaterThreshold;
                GameObject prefab;
                if (deep && UnityEngine.Random.value <= 0.10f)
                {
                    prefab = availablePrefabs.FirstOrDefault(p => string.Equals(p.name, "ice_rock1", StringComparison.OrdinalIgnoreCase))
                             ?? availablePrefabs[UnityEngine.Random.Range(0, availablePrefabs.Count)];
                }
                else
                {
                    prefab = availablePrefabs.FirstOrDefault(p => string.Equals(p.name, "ice1", StringComparison.OrdinalIgnoreCase))
                             ?? availablePrefabs[UnityEngine.Random.Range(0, availablePrefabs.Count)];
                }

                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
                go.name = prefab.name;
                if (string.Equals(prefab.name, "ice1", StringComparison.OrdinalIgnoreCase))
                {
                    float scale = UnityEngine.Random.Range(0.6f, 2.2f);
                    go.transform.localScale = new Vector3(scale, scale, scale);
                }
                else if (string.Equals(prefab.name, "ice_rock1", StringComparison.OrdinalIgnoreCase))
                {
                    float scale = 1f + Mathf.Clamp((spawnDepth - DeepWaterThreshold) * RockDeepScalePerMeter, 0f, 3f);
                    go.transform.localScale = new Vector3(scale, scale, scale);
                }

                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(__instance.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        [HarmonyPrefix]
        private static bool NoBuild(Player __instance, ref bool __result)
        {
            var terr = GetTerritory(__instance);
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoBuild) && !HasRestrictionBypass(terr, __instance))
            {
                __result = false;
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Building not allowed here");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
        [HarmonyPrefix]
        private static bool NoInteract(Player __instance, GameObject go, bool hold, bool alt)
        {
            var terr = GetTerritory(__instance);
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteract) && !HasRestrictionBypass(terr, __instance))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Interaction blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractItemStands(ItemStand __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractItemStands) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Item stands blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractCrafting(CraftingStation __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractCraftingStation) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Crafting blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractPortals(TeleportWorld __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractPortals) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Portal use blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractItems(ItemDrop __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractItems) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Items blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractChests(Container __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractChests) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Chests blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.TakeAll))]
        [HarmonyPrefix]
        private static bool NoInteractChests_TakeAll(Container __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractChests) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Chests blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
        [HarmonyPrefix]
        private static bool NoInteractDoors(Door __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : Current;
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoInteractDoors) && !HasRestrictionBypass(terr, Player.m_localPlayer))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Doors blocked in this territory");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        [HarmonyPrefix]
        private static bool NoAttack(Humanoid character)
        {
            var player = character as Player;
            var terr = player != null ? GetTerritory(player) : GetTerritory(character != null ? character.transform.position : Vector3.zero);
            if (terr == null || player != Player.m_localPlayer && !ZNet.instance.IsServer()) return true;

            ItemDrop.ItemData rightItem = null;
            try { rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(player) as ItemDrop.ItemData; } catch { }
            if (ShouldBlockToolUse(terr, player, rightItem, checkPickaxe: true)) return false;

            if (rightItem != null && rightItem.m_shared != null && rightItem.m_shared.m_skillType == Skills.SkillType.Pickaxes)
            {
                if (terr.Flags.HasFlag(TerritoryFlags.NoPickaxe) && !HasRestrictionBypass(terr, player))
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Mining not allowed here");
                    return false;
                }
            }

            if (terr.Flags.HasFlag(TerritoryFlags.NoAttack) && !HasRestrictionBypass(terr, player))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Combat blocked in this territory");
                return false;
            }
            return true;
        }

        private static bool IsTool(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null) return false;
            var shared = item.m_shared;
            return shared.m_toolTier > 0
                || shared.m_skillType == Skills.SkillType.Pickaxes
                || shared.m_skillType == Skills.SkillType.WoodCutting
                || shared.m_itemType == ItemDrop.ItemData.ItemType.Tool;
        }

        private static bool ShouldBlockToolUse(Territory terr, Player player, ItemDrop.ItemData item, bool checkPickaxe)
        {
            if (terr == null) return false;
            if (HasRestrictionBypass(terr, player)) return false;
            if (terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoTool) && IsTool(item))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Tools blocked in this territory");
                return true;
            }
            if (checkPickaxe && terr.Flags.HasFlag(TerritoryFlags.NoPickaxe) && item != null && item.m_shared != null && item.m_shared.m_skillType == Skills.SkillType.Pickaxes)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Mining not allowed here");
                return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.ApplyOperation))]
        [HarmonyPrefix]
        private static bool TerrainComp_ApplyOperation_Prefix(TerrainComp __instance, TerrainOp modifier)
        {
            if (Player.m_localPlayer == null) return true;
            var terr = GetTerritory(Player.m_localPlayer);
            if (terr == null) return true;
            if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoTool)) return true;
            if (HasRestrictionBypass(terr, Player.m_localPlayer)) return true;

            ItemDrop.ItemData rightItem = null;
            try { rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(Player.m_localPlayer) as ItemDrop.ItemData; } catch { }
            if (rightItem != null && IsTool(rightItem))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Tools blocked in this territory");
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        [HarmonyPrefix]
        private static bool Player_UpdatePlacement_Prefix(Player __instance, bool takeInput, float dt)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return true;
            if (!takeInput) return true;

            var terr = GetTerritory(__instance);
            if (terr == null) return true;
            if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoTool)) return true;
            if (HasRestrictionBypass(terr, __instance)) return true;

            ItemDrop.ItemData rightItem = null;
            try { rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(__instance) as ItemDrop.ItemData; } catch { }

            if (rightItem != null && IsTool(rightItem))
            {
                bool leftClick = ZInput.GetButton("Attack") || ZInput.GetButtonDown("Attack");
                bool rightClick = ZInput.GetButton("Block") || ZInput.GetButtonDown("Block") || ZInput.GetButton("AltPlace") || ZInput.GetButtonDown("AltPlace");
                if (leftClick || rightClick)
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Tools blocked in this territory");
                    return false;
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(TerrainOp), "Awake")]
        [HarmonyPrefix]
        private static bool TerrainOp_Awake_Prefix(TerrainOp __instance)
        {
            if (__instance == null) return true;
            if (Player.m_localPlayer == null) return true;

            var terr = GetTerritory(__instance.transform.position);
            if (terr == null) return true;
            if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoTool)) return true;
            if (HasRestrictionBypass(terr, Player.m_localPlayer)) return true;

            ItemDrop.ItemData rightItem = null;
            try { rightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(Player.m_localPlayer) as ItemDrop.ItemData; } catch { }

            if (rightItem != null && IsTool(rightItem))
            {
                // TerrainOp carries a ZNetView; route through ZNetScene so we don't
                // leave a stale m_instances entry that would NRE in
                // ZNetScene.RemoveObjects on the next zone tick.
                NetworkObjectHelper.SafeDestroy(__instance.gameObject);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Tools blocked in this territory");
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyPrefix]
        private static bool IncreasedDamage_Prefix(Character __instance, HitData hit)
        {
            if (hit == null) return true;
            var attacker = hit.GetAttacker();
            if (attacker == null) return true;

            var terr = GetTerritory(__instance.transform.position);
            if (terr == null) return true;

            bool attackerIsPlayer = attacker is Player;
            bool targetIsPlayer = __instance is Player;

            if (targetIsPlayer && terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.GodMode) && !AdminBypass(attacker as Player))
            {
                hit.m_damage = new HitData.DamageTypes();
                return false;
            }

            if (attackerIsPlayer && terr.Flags.HasFlag(TerritoryFlags.IncreasedPlayerDamage))
            {
                float mul = terr.IncreasedPlayerDamageValue > 0f ? terr.IncreasedPlayerDamageValue : 1.5f;
                hit.m_damage.Modify(mul);
            }

            if (!attackerIsPlayer && targetIsPlayer && terr.Flags.HasFlag(TerritoryFlags.IncreasedMonsterDamage))
            {
                float mul = terr.IncreasedMonsterDamageValue > 0f ? terr.IncreasedMonsterDamageValue : 1.5f;
                hit.m_damage.Modify(mul);
            }

            return true;
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPostfix]
        private static void DropMultiplier_Postfix(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {
            if (__result == null || __result.Count == 0) return;
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : null;
            if (terr == null) return;
            if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.DropMultiplier)) return;

            float mul = terr.DropMultiplier > 0f ? terr.DropMultiplier : 1f;
            if (Mathf.Approximately(mul, 1f)) return;

            for (int i = 0; i < __result.Count; i++)
            {
                var kvp = __result[i];
                int newAmount = Mathf.Max(1, Mathf.RoundToInt(kvp.Value * mul));
                __result[i] = new KeyValuePair<GameObject, int>(kvp.Key, newAmount);
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        [HarmonyPostfix]
        private static void Character_Awake_Postfix(Character __instance)
        {
            try
            {
                if (__instance == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (__instance is Player) return;

                var terr = GetTerritory(__instance.transform.position);
                if (terr == null || !terr.Flags.HasFlag(TerritoryFlags.MonstersAddStars)) return;

                int add = terr.AddMonsterLevel > 0 ? terr.AddMonsterLevel : 1;
                int target = Mathf.Max(1, __instance.GetLevel() + add);
                try { __instance.SetLevel(target); } catch { }
            }
            catch { }
        }

        // Valheim 1.0 dropped Spawn's trailing "minLevelOverride"/"levelUpMultiplier" parameters -
        // the level is now derived inside Spawn from critter.m_minLevel and GetLevelUpChance. Harmony
        // binds patch parameters BY NAME, so leaving them declared made this ENTIRE class fail to
        // attach (all 25 patches, not just this one).
        //
        // minLevelOverride used to seed the starting minLevel before vanilla's random level-up loop,
        // so bumping critter.m_minLevel for the duration of the call reproduces it exactly. Spawn is
        // synchronous and single-threaded, and the finalizer restores the field even if Spawn throws,
        // so the shared SpawnData is never left mutated.
        private static SpawnSystem.SpawnData _levelBumpTarget;
        private static int _levelBumpSaved;

        [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
        [HarmonyPrefix]
        private static bool SpawnSystem_Spawn_Prefix(SpawnSystem.SpawnData critter, Vector3 spawnPoint, bool eventSpawner)
        {
            var terr = GetTerritory(spawnPoint);
            if (terr == null) return true;
            if (terr.Flags.HasFlag(TerritoryFlags.NoMonsters))
                return false;

            if (critter != null && terr.Flags.HasFlag(TerritoryFlags.MonstersAddStars))
            {
                int add = terr.AddMonsterLevel > 0 ? terr.AddMonsterLevel : 1;
                _levelBumpTarget = critter;
                _levelBumpSaved = critter.m_minLevel;
                critter.m_minLevel = critter.m_minLevel + add;
            }
            return true;
        }

        [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
        [HarmonyFinalizer]
        private static void SpawnSystem_Spawn_Finalizer()
        {
            if (_levelBumpTarget == null) return;
            _levelBumpTarget.m_minLevel = _levelBumpSaved;
            _levelBumpTarget = null;
        }

        [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
        [HarmonyPrefix]
        private static void InfiniteFuel_Prefix(Fireplace __instance)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : null;
            if (terr != null && terr.Flags.HasFlag(TerritoryFlags.InfiniteFuel))
            {
                // Use cached field ref to set infinite fuel
                try { FireplaceInfiniteFuelRef(__instance) = true; }
                catch { }
            }
        }

        /// <summary>Called from <see cref="Modules.RpgVanillaHooks.Player_OnDeath"/> (prefix): flags a protected
        /// death before vanilla drains skills.</summary>
        internal static void OnPlayerDeathPrefix(Player __instance)
        {
            var terr = GetTerritory(__instance);
            if (terr == null) return;

            // NoDeathPenalty or NoItemLoss - we'll handle the actual prevention in the skill drain and inventory patches
            // Store a flag we can check in subsequent patches
            if (terr.Flags.HasFlag(TerritoryFlags.NoDeathPenalty) || terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoItemLoss))
            {
                _lastDeathWasProtected = true;
            }
            else
            {
                _lastDeathWasProtected = false;
            }
        }

        private static bool _lastDeathWasProtected;

        [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
        [HarmonyPrefix]
        private static bool NoDeathPenalty_SkillLoss(Skills __instance)
        {
            // If the death was in a NoDeathPenalty territory, skip skill loss
            if (_lastDeathWasProtected)
            {
                return false; // Skip original - no skill loss
            }
            return true;
        }

        // NoItemLoss (Fire 10-08): a death inside keeps the whole inventory and leaves no tombstone. Vanilla's
        // DeathKeepInventory world modifier does exactly this: Player.CreateTombStone returns before it unequips or
        // moves anything. Skills are kept as well (OnPlayerDeathPrefix marks the death protected). Before, the flag
        // only kept skills; its patch sat on CreateDeathEffects and never touched the grave.
        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        [HarmonyPrefix]
        private static bool NoItemLoss_KeepInventory(Player __instance)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return true;
            var terr = GetTerritory(__instance);
            if (terr == null || !terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoItemLoss)) return true;
            int items = __instance.GetInventory()?.NrOfItems() ?? 0;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{terr.Name}' NoItemLoss: {__instance.GetPlayerName()} keeps {items} item(s), no tombstone");
            return false;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        [HarmonyPrefix]
        private static bool NoPortals_Prefix(TeleportWorld __instance, Player player)
        {
            var terr = __instance != null ? GetTerritory(__instance.transform.position) : null;
            if (terr != null && terr.Flags.HasFlag(TerritoryFlags.NoPortals) && !IsPermitted(terr, player))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Portals are disabled in this territory");
                return false;
            }
            return true;
        }

        private static void OnTerritoryChanged(Territory oldTerritory, Territory newTerritory, Player player)
        {
            if (player == null) return;
            if (ReferenceEquals(oldTerritory, newTerritory)) return;

            string oldName = oldTerritory?.Name ?? string.Empty;
            string newName = newTerritory?.Name ?? string.Empty;

            // Check both UID and name to prevent spam from different territory instances with same properties
            int oldUID = oldTerritory?.UID ?? 0;
            int newUID = newTerritory?.UID ?? 0;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && oldUID == newUID) return;

            bool sameName = !string.IsNullOrEmpty(newName) && string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (ZNet.instance == null || ZNet.instance.IsServer() || player == Player.m_localPlayer)
            {
                // 0.0.499: disk-to-disk hops inside one named territory (a road) are routine: Debug, not Info
                if (sameName) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"Territory hop for {player.GetPlayerName()} inside '{newName}' (UID {oldUID} -> {newUID})");
                else Debug.Log($"[FiresRPGmaker] Territory change for {player.GetPlayerName()} name '{oldName}' -> '{newName}' (UID {oldUID} -> {newUID})");
            }
            if (player == Player.m_localPlayer) TerritoryEntryBanner.NoteChange(oldName, newName);
            if (player == Player.m_localPlayer) ApplyVedr(oldTerritory, newTerritory);

            if (newTerritory != null)
            {
                TerritoryEntryReporter.SendEntryToServer(newTerritory, player);
            }

            // Entry announcement in Valheim's native location-discovery style (braided plaque) — enter only,
            // local player only. Leaving shows nothing (kg parity). The banner shows once per territory NAME (0.0.499).
            if (newTerritory != null && player == Player.m_localPlayer)
            {
                TerritoryEntryBanner.Show(newName);
            }

            if (newTerritory != null && ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (newTerritory.Flags.HasFlag(TerritoryFlags.CustomPaint))
                {
                    TryApplyTerritoryPaint(newTerritory, player.transform.position);
                }
                TryRunCustomSpawners(newTerritory, player, force: false, logEntry: true);
            }
        }

        // Environment/biome/effects helpers are implemented in TerritoryRuntime.Environment.cs
    }
}
