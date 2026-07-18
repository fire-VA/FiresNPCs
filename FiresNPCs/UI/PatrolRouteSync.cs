using System.Collections.Generic;
using UnityEngine;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Saves a recorded patrol route and assigns it to an NPC. On host / single-player the cfg is written
    /// to the local config folder — which is also the server's, so the server-authoritative mover reads it.
    ///
    /// DEDICATED-SERVER TODO: a client recording against a remote server must also push (name + points)
    /// to the server over a routed RPC so the server writes/reads the cfg. host works today; that RPC is
    /// the follow-up to make dedis work (PatrolRouteManager.WriteRawAndReload is the server-side sink).
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
            npc.SyncPatrolAssignment();
        }

        /// <summary>Removes any patrol-route assignment from an NPC (the route cfg itself is left on disk).</summary>
        public static void ClearAssignment(NpcController npc)
        {
            if (npc == null) return;

            npc.patrolRouteName = "";
            npc.patrolSpeedPreset = "";
            WriteRouteKeyOnly(npc, "", "");
            npc.SyncPatrolAssignment();
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
            if (!nview.IsOwner()) nview.ClaimOwnership();   // static NPCs are server-owned; claim so the write sticks (host/SP)
            var zdo = nview.GetZDO();
            if (zdo != null)
            {
                zdo.Set("npc_patrol_route", routeName ?? "");
                zdo.Set("npc_patrol_preset", presetName ?? "");
            }
        }
    }
}
