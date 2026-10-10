using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresCore.UI.ContextMenu;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI.ContextMenu
{
    /// <summary>
    /// Hold-Alt + right-click a patrol-route node sphere (shown by "Visualize Route") to reshape the route in
    /// place — move/insert/delete a node — OR author its DBSM speed model: mark a section span (start on one
    /// node, end on another), flag a stop point (+ tune its pause), and open the slider editor. Structural
    /// edits (node move/insert/delete) fix up section/stop indices so they keep pointing at the same physical
    /// nodes. Speed edits target the current editing preset (PatrolEditSession) and re-color the preview live.
    /// </summary>
    public sealed class PatrolNodeContextProvider : IContextMenuProvider
    {
        private const float DefaultStopDuration = 2f;

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

            var preset = PatrolEditSession.GetEditingPreset(route0);
            bool isStop = preset != null && preset.StopAt(idx) != null;
            bool pendingStart = PatrolEditSession.PendingSectionStart >= 0
                                && string.Equals(PatrolEditSession.RouteName, route, StringComparison.OrdinalIgnoreCase);

            var items = new List<ContextMenuItem>
            {
                ContextMenuItem.Row($"Node {idx + 1} of {(route0 != null ? route0.Points.Count : 0)}"
                    + (preset != null ? $"  ·  preset '{preset.Name}'" : ""), null, enabled: false),
                ContextMenuItem.Sep(),
                ContextMenuItem.Row("Grab node (move on hammer)", () => PatrolNodeGrabController.Begin(route, idx)),
                ContextMenuItem.Row("Move node to me", () => EditNodes(route, (pts, ps) => { if (Valid(pts, idx)) pts[idx] = PlayerPos(pts[idx]); })),
                ContextMenuItem.Row("Insert node after", () => EditNodes(route, (pts, ps) => InsertAfter(pts, ps, idx))),
                ContextMenuItem.Row("Delete node", () => EditNodes(route, (pts, ps) => DeleteAt(pts, ps, idx)), canDelete),
                ContextMenuItem.Sep(),
            };

            // ── DBSM speed authoring ──────────────────────────────────────────
            if (pendingStart)
            {
                int start = PatrolEditSession.PendingSectionStart;
                items.Add(ContextMenuItem.Row($"Mark section END here (from node {start + 1})", () => MarkSectionEnd(route, idx)));
                items.Add(ContextMenuItem.Row("Cancel section marking", () => { PatrolEditSession.PendingSectionStart = -1; Msg("Section marking cancelled"); }));
            }
            else
            {
                items.Add(ContextMenuItem.Row("Mark section START here", () => MarkSectionStart(route, idx)));
            }
            items.Add(ContextMenuItem.Row(isStop ? "Remove stop point" : "Add stop point", () => ToggleStop(route, idx)));
            if (isStop)
            {
                items.Add(ContextMenuItem.Row("Stop pause +1s", () => AdjustStop(route, idx, +1f)));
                items.Add(ContextMenuItem.Row("Stop pause -1s", () => AdjustStop(route, idx, -1f)));
            }
            items.Add(ContextMenuItem.Sep());
            items.Add(ContextMenuItem.Row(PatrolRouteVisualizer.IsPreviewingSpeed ? "Hide speed colors" : "Show speed colors", () => ToggleSpeedColors(route)));
            items.Add(ContextMenuItem.Row("Edit speed presets…", () => PatrolPresetPopup.Open(route)));

            return items;
        }

        // ── speed authoring actions ───────────────────────────────────────────
        private static void MarkSectionStart(string route, int idx)
        {
            PatrolEditSession.RouteName = route;
            PatrolEditSession.PendingSectionStart = idx;
            Msg($"Section start = node {idx + 1}. Now right-click the END node.");
        }

        private static void MarkSectionEnd(string route, int endIdx)
        {
            int start = PatrolEditSession.PendingSectionStart;
            PatrolEditSession.PendingSectionStart = -1;
            if (start < 0) return;
            int lo = Mathf.Min(start, endIdx), hi = Mathf.Max(start, endIdx);

            PatrolEditSession.Mutate(route, (r, preset) =>
            {
                // Non-overlapping: clip any existing section that intersects the new span, then add the new one.
                preset.Sections.RemoveAll(s => s.Low <= hi && s.High >= lo);
                preset.Sections.Add(new Section
                {
                    StartIndex = lo, EndIndex = hi,
                    Peak = 1.5f, Floor = 0.6f, Shape = SpeedShape.EaseInOut, ShapeK = 1f,
                });
                preset.Sections.Sort((a, b) => a.Low.CompareTo(b.Low));
            });
            PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.PresetName);
            Msg($"Section {lo + 1}–{hi + 1} added to '{PatrolEditSession.PresetName}'. Tune it in Edit speed presets…");
        }

        private static void ToggleStop(string route, int idx)
        {
            PatrolEditSession.Mutate(route, (r, preset) =>
            {
                var existing = preset.StopAt(idx);
                if (existing != null) preset.StopPoints.Remove(existing);
                else preset.StopPoints.Add(new StopPoint { Index = idx, Duration = DefaultStopDuration });
            });
            PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.PresetName);
            var p = PatrolRouteManager.GetRoute(route)?.GetPreset(PatrolEditSession.PresetName);
            Msg(p?.StopAt(idx) != null ? $"Stop point added at node {idx + 1} ({DefaultStopDuration:0.#}s)" : $"Stop point removed at node {idx + 1}");
        }

        private static void AdjustStop(string route, int idx, float delta)
        {
            PatrolEditSession.Mutate(route, (r, preset) =>
            {
                var st = preset.StopAt(idx);
                if (st != null) st.Duration = Mathf.Clamp(st.Duration + delta, 0f, 120f);
            });
            var st2 = PatrolRouteManager.GetRoute(route)?.GetPreset(PatrolEditSession.PresetName)?.StopAt(idx);
            if (st2 != null) Msg($"Stop pause = {st2.Duration:0.#}s");
        }

        private static void ToggleSpeedColors(string route)
        {
            if (PatrolRouteVisualizer.IsPreviewingSpeed) PatrolRouteVisualizer.SetSpeedPreview(null);
            else PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.GetEditingPreset(PatrolRouteManager.GetRoute(route))?.Name);
        }

        // ── node structural edits (with preset index fix-up) ──────────────────
        private static bool Valid(List<Vector3> pts, int idx) => idx >= 0 && idx < pts.Count;

        private static Vector3 PlayerPos(Vector3 fallback)
            => Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : fallback;

        private static void InsertAfter(List<Vector3> pts, List<SpeedPreset> presets, int idx)
        {
            if (!Valid(pts, idx)) return;
            int next = (idx + 1) % pts.Count;                 // wrap for loops so the last node can spawn one too
            Vector3 mid = (pts[idx] + pts[next]) * 0.5f;
            int newIndex = idx + 1;
            pts.Insert(newIndex, mid);
            foreach (var p in presets) p.ShiftForInsert(newIndex);
        }

        private static void DeleteAt(List<Vector3> pts, List<SpeedPreset> presets, int idx)
        {
            if (pts.Count <= 2 || !Valid(pts, idx)) return;
            pts.RemoveAt(idx);
            foreach (var p in presets) p.ShiftForDelete(idx);
        }

        // Load → mutate points (and fix-up preset indices) → persist → redraw. Presets are cloned so a
        // currently-patrolling NPC's cached route object isn't mutated mid-frame (it re-reads on restart).
        private static void EditNodes(string route, Action<List<Vector3>, List<SpeedPreset>> mutate)
        {
            var r = PatrolRouteManager.GetRoute(route);
            if (r == null) return;
            var pts = new List<Vector3>(r.Points);
            var presets = r.Presets.Select(p => p.Clone()).ToList();
            mutate(pts, presets);
            if (pts.Count < 2)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "A route needs at least 2 nodes");
                return;
            }
            foreach (var p in presets) p.ClampToRoute(pts.Count);
            PatrolRouteManager.SaveRoute(route, pts, presets);
            if (PatrolRouteVisualizer.IsShowing(route))
            {
                PatrolRouteVisualizer.Hide();
                PatrolRouteVisualizer.Show(route);   // node count may have changed → full redraw (keeps any speed preview)
            }
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Route '{route}' updated ({pts.Count} nodes)");
        }

        private static void Msg(string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, text);
    }
}
