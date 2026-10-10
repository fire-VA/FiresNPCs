using UnityEngine;
using FiresRPGmaker.UI.Screens.PlayerPanels;

namespace FiresRPGmaker.Modules.Props
{
    // Attached mod-side (by name) to the FiresMailbox_* pieces at registration — the firesnpcs_props bundle is built
    // in the Unity project which has no reference to this mod's types, so the interactable is added at load time.
    // Interact (Use) toggles the local player's mail UI open/closed.
    public class MailboxPiece : MonoBehaviour, Hoverable, Interactable
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            MailPlayerPanel.Toggle("", "Mailbox");   // "" profile = the local player's own inbox
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText() =>
            Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] Open mail");

        public string GetHoverName() => "Mailbox";
        // Valheim 1.0 added GetHoverOffset to the Hoverable interface; it is ADDED to
        // Player.m_maxInteractDistance, so 0 preserves the previous interact range exactly.
        public float GetHoverOffset() => 0f;
    }
}
