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
        public static void AssignExisting(string name, NpcController npc)
        {
            if (npc == null || string.IsNullOrEmpty(name)) return;
            if (PatrolRouteManager.GetRoute(name) == null) return;

            npc.patrolRouteName = name;
            npc.SaveToZDO();
            npc.SyncPatrolAssignment();
        }

        /// <summary>Removes any patrol-route assignment from an NPC (the route cfg itself is left on disk).</summary>
        public static void ClearAssignment(NpcController npc)
        {
            if (npc == null) return;

            npc.patrolRouteName = "";
            npc.SaveToZDO();
            npc.SyncPatrolAssignment();
        }
    }
}
