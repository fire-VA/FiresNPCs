using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.UI.ContextMenu;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI.ContextMenu
{
    /// <summary>
    /// Hold-Alt + right-click a patrol-route node sphere (shown by "Visualize Route") to reshape the route in
    /// place — move a node to your feet, nudge it on any axis, insert a node after it, or delete it. Each edit
    /// rewrites the route cfg via PatrolRouteManager.SaveRoute (same host/SP path the recorder uses) and redraws
    /// the markers so the next pick lands on the moved node.
    /// </summary>
    public sealed class PatrolNodeContextProvider : IContextMenuProvider
    {
        public string TitleFor(ContextTarget target)
            => target != null && target.MarkerKind == "PatrolNode" ? "Patrol Node" : null;

        public IEnumerable<ContextMenuItem> GetItems(ContextTarget target)
        {
            if (target == null || target.MarkerKind != "PatrolNode") return null;
            if (!(target.Marker is PatrolNodeMarker marker)) return null;

            string route = marker.RouteName;
            int idx = marker.Index;

            var route0 = PatrolRouteManager.GetRoute(route);
            bool canDelete = route0 != null && route0.Points.Count > 2; // SaveRoute requires >= 2

            return new List<ContextMenuItem>
            {
                ContextMenuItem.Row($"Node {idx + 1} of {(route0 != null ? route0.Points.Count : 0)}", null, enabled: false),
                ContextMenuItem.Sep(),
                ContextMenuItem.Row("Grab node (move on hammer)", () => PatrolNodeGrabController.Begin(route, idx)),
                ContextMenuItem.Row("Move node to me", () => Edit(route, pts => { if (Valid(pts, idx)) pts[idx] = PlayerPos(pts[idx]); })),
                ContextMenuItem.Sep(),
                ContextMenuItem.Row("Insert node after", () => Edit(route, pts => InsertAfter(pts, idx))),
                ContextMenuItem.Row("Delete node", () => Edit(route, pts => { if (pts.Count > 2 && Valid(pts, idx)) pts.RemoveAt(idx); }), canDelete),
            };
        }

        private static bool Valid(List<Vector3> pts, int idx) => idx >= 0 && idx < pts.Count;

        private static Vector3 PlayerPos(Vector3 fallback)
            => Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : fallback;

        private static void InsertAfter(List<Vector3> pts, int idx)
        {
            if (!Valid(pts, idx)) return;
            int next = (idx + 1) % pts.Count;                 // wrap for loops so the last node can spawn one too
            Vector3 mid = (pts[idx] + pts[next]) * 0.5f;
            pts.Insert(idx + 1, mid);
        }

        // Load → mutate → persist → redraw. Mirrors the recorder's host/SP SaveRoute path.
        private static void Edit(string route, Action<List<Vector3>> mutate)
        {
            var r = PatrolRouteManager.GetRoute(route);
            if (r == null) return;
            var pts = new List<Vector3>(r.Points);
            mutate(pts);
            if (pts.Count < 2)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "A route needs at least 2 nodes");
                return;
            }
            PatrolRouteManager.SaveRoute(route, pts);
            if (PatrolRouteVisualizer.IsShowing(route))
            {
                PatrolRouteVisualizer.Hide();
                PatrolRouteVisualizer.Show(route);
            }
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Route '{route}' updated ({pts.Count} nodes)");
        }
    }
}
