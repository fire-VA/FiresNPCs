using System.Collections.Generic;
using UnityEngine;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Saves a recorded patrol route and assigns it to an NPC. On host / single-player the cfg is written
    /// to the local config folder — which is also the server's, so the server-authoritative mover reads it.
    ///
    /// Dedicated servers: the route cfg reaches the server through Core's PatrolRouteManager.PushToServer (FiresRPGmaker_SyncFiles,
    /// folder PatrolRoutes; the server watcher reloads it), and a client that doesn't own the NPC writes the route key
    /// through the FiresRPGmaker_SetPatrolKey server RPC (admin only) instead of claiming ownership.
    /// </summary>
    public static class PatrolRouteSync
    {
        public static void SaveAndAssign(string name, IList<Vector3> points, NpcController npc)
        {
            if (string.IsNullOrEmpty(name) || points == null || points.Count < 2) return;

            PatrolRouteManager.SaveRoute(name, points);
            AssignExisting(name, npc);
        }

        /// <summary>Assigns an already-saved route (by name) to an NPC without re-recording any points.</summary>
        public static void AssignExisting(string name, NpcController npc) => AssignExisting(name, npc.patrolSpeedPreset, npc);

        /// <summary>Assigns a saved route + a DBSM speed preset (empty = Default flat walk) to an NPC.</summary>
        public static void AssignExisting(string name, string preset, NpcController npc)
        {
            if (npc == null || string.IsNullOrEmpty(name)) return;
            if (PatrolRouteManager.GetRoute(name) == null) return;

            npc.patrolRouteName = name;
            npc.patrolSpeedPreset = preset ?? "";
            WriteRouteKeyOnly(npc, name, npc.patrolSpeedPreset);
            npc.ApplyRouteChange();
        }

        /// <summary>Removes any patrol-route assignment from an NPC (the route cfg itself is left on disk).</summary>
        public static void ClearAssignment(NpcController npc)
        {
            if (npc == null) return;

            npc.patrolRouteName = "";
            npc.patrolSpeedPreset = "";
            WriteRouteKeyOnly(npc, "", "");
            npc.ApplyRouteChange();
        }

        /// <summary>
        /// Persists ONLY the patrol-route ZDO key. A route is independent of the NPC's saved profile, so this
        /// must NEVER call npc.SaveToZDO() — that rewrites name/profile/equipment from the controller's
        /// (possibly stale/default) fields and was clobbering the NPC's applied name back to "StaticNpc".
        /// </summary>
        private static void WriteRouteKeyOnly(NpcController npc, string routeName, string presetName)
        {
            var nview = npc.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            if (zdo == null) return;
            if (nview.IsOwner() || ZNet.instance == null || ZNet.instance.IsServer())
            {
                zdo.Set("npc_patrol_route", routeName ?? "");
                zdo.Set("npc_patrol_preset", presetName ?? "");
                return;
            }
            EnsureRegistered();
            var pkg = new ZPackage();
            pkg.Write(zdo.m_uid); pkg.Write(routeName ?? ""); pkg.Write(presetName ?? "");
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcSetKey, pkg);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Patrol] route key '{routeName}' for {zdo.m_uid} sent to the server (no ownership claim)");
        }

        // server-side, the route an admin last assigned per NPC this session (cleared routes are kept as "")
        private static readonly Dictionary<ZDOID, (string route, string preset)> s_serverAssigned = new Dictionary<ZDOID, (string, string)>();
        internal static bool TryGetServerAssigned(ZDOID id, out string route, out string preset)
        {
            if (s_serverAssigned.TryGetValue(id, out var v)) { route = v.route; preset = v.preset; return true; }
            route = preset = null; return false;
        }

        private const string RpcSetKey = "FiresRPGmaker_SetPatrolKey";
        // (re-register RPCs per connection): by instance + bound from ZNet.Awake. Ascend called this once per process
        // (WaitForZNetReady); after a rejoin it rebound only lazily, on the next route send.
        private static ZRoutedRpc s_regOn;
        public static void ResetForLogout() => s_regOn = null;
        public static void EnsureRegistered()
        {
            if (!global::FiresRPGmaker.Managers.RpcRegistration.Needed(s_regOn)) return;
            s_regOn = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(RpcSetKey, RPC_SetKey);
            global::FiresRPGmaker.Managers.RpcRegistration.Logged("PatrolRouteSync", 1);
        }

        [HarmonyLib.HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterOnZNetAwake
        {
            [HarmonyLib.HarmonyPostfix] private static void Postfix() { try { EnsureRegistered(); } catch { } }
        }

        // Server: an admin client assigned a route to an NPC it doesn't own; write the key on the server's copy.
        private static void RPC_SetKey(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!FiresCore.Sync.AdminSyncing.IsAdmin(sender)) { Debug.LogWarning($"[Patrol] route key from non-admin peer {sender} ignored"); return; }
            ZDOID id = pkg.ReadZDOID(); string route = pkg.ReadString(), preset = pkg.ReadString();
            var zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null) return;
            if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set("npc_patrol_route", route);
            s_serverAssigned[id] = (route ?? "", preset ?? "");   // the owner's watcher re-asserts this against stale writes
            zdo.Set("npc_patrol_preset", preset);
            // the server owns the NPC and drives the patrol, but only its ZDO
            // key changed; nothing re-read it into the live controller, so its PatrolAssignment stayed empty. Apply it here.
            bool live = false;
            var inst = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            var lnpc = inst != null ? inst.GetComponent<NpcController>() : null;
            if (lnpc != null) { lnpc.patrolRouteName = route; lnpc.patrolSpeedPreset = preset; lnpc.ApplyRouteChange(); live = true; }
            global::FiresRPGmaker.FiresLogger.LogDebug($"[Patrol] server applied route '{route}' to the live NPC {id}: {(live ? "yes" : "no live body here")}, route resolves on the server={(PatrolRouteManager.GetRoute(route) != null)}");
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Patrol] server set route '{route}' preset '{preset}' on {id} (from peer {sender})");
        }
    }
}
