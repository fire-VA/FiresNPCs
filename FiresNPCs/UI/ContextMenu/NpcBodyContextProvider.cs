using System.Collections.Generic;
using UnityEngine;
using FiresCore.UI.ContextMenu;

namespace FiresRPGmaker.UI.ContextMenu
{
    /// <summary>
    /// Hold Alt+Shift + right-click a placed static NPC's BODY (the context driver already resolves
    /// Character roots under the cursor) for the admin quick menu:
    ///   • Pick up NPC — the anchor grab mode: NPC ghost + orb on the hammer, place to re-home BOTH
    ///     the NPC and its anchor (the move purges the old home, so no duplicates).
    ///   • Show anchor — the golden orb (same viz the book button opens).
    ///   • Show patrol route — only when a route is assigned.
    ///   • Edit NPC — the Shift+E admin book, without walking up to them.
    /// Rows are admin-only; every mutating action is independently server-gated.
    /// </summary>
    public sealed class NpcBodyContextProvider : IContextMenuProvider
    {
        public string TitleFor(ContextTarget target)
        {
            var npc = ResolveStaticNpc(target);
            if (npc == null) return null;
            string name = npc.GetDisplayName();
            return string.IsNullOrEmpty(name) ? "Static NPC" : name;
        }

        public IEnumerable<ContextMenuItem> GetItems(ContextTarget target)
        {
            var npc = ResolveStaticNpc(target);
            if (npc == null) return null;

            bool isAdmin = FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false;
            if (!isAdmin) return null;

            var nview = npc.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            string anchorGuid = zdo != null
                ? zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "")
                : "";
            string route = npc.patrolRouteName;

            var items = new List<ContextMenuItem>();

            if (!string.IsNullOrEmpty(anchorGuid))
                items.Add(ContextMenuItem.Row("Pick up NPC (move with hammer)", () => NpcAnchorGrabController.Begin(anchorGuid)));
            else
                items.Add(ContextMenuItem.Row("Pick up NPC (no anchor yet)", null, enabled: false));

            items.Add(ContextMenuItem.Row("Show anchor", () => NpcAnchorOrbViz.Show(npc)));

            if (!string.IsNullOrEmpty(route))
                items.Add(ContextMenuItem.Row($"Show patrol route ({route})", () =>
                    global::FiresCore.Npc.Patrol.PatrolRouteVisualizer.Show(route)));

            items.Add(ContextMenuItem.Row(npc.mapPinned ? "Map pin: ON (click to hide)" : "Map pin: OFF (click to show)", () =>
            {
                npc.mapPinned = !npc.mapPinned;
                npc.SaveToZDO();
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                    npc.mapPinned ? $"{npc.GetDisplayName()} pinned to the map (updates within ~30s)."
                                  : $"{npc.GetDisplayName()} unpinned from the map.");
            }));
            items.Add(ContextMenuItem.Sep());
            items.Add(ContextMenuItem.Row("Edit NPC", () => NpcBookController.ShowForNpc(npc)));

            // Presets are OPT-IN: this is the only path that saves an NPC to the hammer tab.
            bool nameable = !global::FiresRPGmaker.Managers.SavedNpcManager.IsDefaultNpcName(npc.GetDisplayName());
            if (nameable)
                items.Add(ContextMenuItem.Row("Save to hammer (preset)", () =>
                {
                    string saved = global::FiresRPGmaker.Managers.SavedNpcManager.SaveNpcAsTemplate(npc, out string reason);
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                        saved != null ? $"Saved '{saved}' to the hammer (Fires NPCs tab)" : reason);
                }));
            else
                items.Add(ContextMenuItem.Row("Save to hammer (name the NPC first)", null, enabled: false));

            return items;
        }

        /// <summary>The clicked object as a placed STATIC NPC (never wild/companion followers).</summary>
        private static NpcController ResolveStaticNpc(ContextTarget target)
        {
            var go = target?.GameObject;
            if (go == null) return null;
            var npc = go.GetComponent<NpcController>();
            if (npc == null) return null;
            var module = go.GetComponent<FiresCore.Npc.NpcMode.CompanionNpcModule>();
            var nview = go.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            bool isStatic = (module != null && module.isStaticPlacement)
                            || (zdo != null && zdo.GetBool("npc_static_placement", false));
            return isStatic ? npc : null;
        }
    }
}
