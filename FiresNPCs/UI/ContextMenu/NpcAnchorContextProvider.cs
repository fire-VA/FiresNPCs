using System.Collections.Generic;
using UnityEngine;
using FiresCore.UI.ContextMenu;

namespace FiresRPGmaker.UI.ContextMenu
{
    /// <summary>
    /// Hold-Alt + right-click the golden anchor orb (shown by the NPC book's "Show Anchor" button) to
    /// manage the NPC's durable record: move the anchor (to the admin's position) or delete the NPC
    /// permanently (record + registry + anchor + body — the one true delete path). Rows are admin-only;
    /// the server independently validates admin on both RPCs.
    /// </summary>
    public sealed class NpcAnchorContextProvider : IContextMenuProvider
    {
        public string TitleFor(ContextTarget target)
            => target != null && target.MarkerKind == "NpcAnchor" ? "NPC Anchor" : null;

        public IEnumerable<ContextMenuItem> GetItems(ContextTarget target)
        {
            if (target == null || target.MarkerKind != "NpcAnchor") return null;
            if (!(target.Marker is NpcAnchorMarker marker)) return null;

            bool isAdmin = FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false;
            if (!isAdmin) return null;

            return new List<ContextMenuItem>
            {
                ContextMenuItem.Row(string.IsNullOrEmpty(marker.NpcName) ? "Static NPC" : marker.NpcName, null, enabled: false),
                ContextMenuItem.Sep(),
                ContextMenuItem.Row("Move anchor (place on hammer)", () => NpcAnchorGrabController.Begin(marker.Guid)),
                ContextMenuItem.Row("Move anchor to me", () =>
                {
                    var p = Player.m_localPlayer;
                    if (p == null) return;
                    Managers.SavedNpcManager.RequestAnchorMove(marker.Guid, p.transform.position);
                    NpcAnchorOrbViz.Hide();
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Anchor moved to your position.");
                }),
                ContextMenuItem.Row("Remove NPC + anchor (permanent)", () =>
                {
                    Managers.SavedNpcManager.AdminDeletePlacedNpc(marker.AnchorPos, marker.Guid);
                    NpcAnchorOrbViz.Hide();
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "NPC and anchor removed permanently.");
                }),
                ContextMenuItem.Sep(),
                ContextMenuItem.Row("Hide anchor orb", NpcAnchorOrbViz.Hide),
            };
        }
    }
}
