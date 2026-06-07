using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.Modules.Guilds.Core;

namespace FiresRPGmaker.Modules.Guilds.Companions
{
    /// <summary>
    /// Handles the "guild access" toggle on companions.
    ///
    /// ZDO KEY
    /// -------
    ///   "companion_guild_access"  (bool, default false)
    ///
    /// When true, any player in the same guild as the companion's owner
    /// may:
    ///   - Open the companion's inventory (read + write items)
    ///   - Issue standard work orders (gather, cook, deposit, etc.)
    ///
    /// Ownership-exclusive operations (dismiss, set home, archetype change,
    /// combat stance, name change, transfer ownership) remain locked to the
    /// actual owner and server admins.
    /// </summary>
    public static class CompanionGuildAccess
    {
        // ZDO key â€” matches the key written by GuildSyncManager.Server_SetCompanionGuildAccess.
        public const string ZDO_KEY = "companion_guild_access";

        // â”€â”€ ZDO read helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public static bool IsGuildAccessEnabled(CompanionController companion)
        {
            if (companion == null) return false;
            var nview = companion.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            return nview.GetZDO().GetBool(ZDO_KEY, false);
        }

        public static bool IsGuildAccessEnabled(ZNetView nview)
        {
            if (nview == null || !nview.IsValid()) return false;
            return nview.GetZDO().GetBool(ZDO_KEY, false);
        }

        // â”€â”€ Permission check API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Returns true if <paramref name="player"/> is allowed to interact
        /// with <paramref name="companion"/> at the guild-access level (inventory
        /// viewing, work orders).
        ///
        /// True when any of:
        ///   1. Player is the companion's owner.
        ///   2. Player is a server admin.
        ///   3. Guild access is enabled AND player is in the same guild as
        ///      the companion's owner.
        /// </summary>
        public static bool CanInteract(Player player, CompanionController companion)
        {
            if (player == null || companion == null) return false;

            // Owner always has access.
            if (companion.IsOwner(player)) return true;

            // Server admins always have access.
            if (companion.IsPlayerAdmin(player)) return true;

            // Check guild access flag.
            if (!IsGuildAccessEnabled(companion)) return false;

            // Verify both owner and player are in the same guild.
            long ownerId  = companion.ownerPlayerId;
            long playerId = player.GetPlayerID();
            if (ownerId == 0) return false;

            return GuildAPI.AreGuildmates(ownerId, playerId);
        }

        /// <summary>
        /// Returns true if <paramref name="player"/> may perform owner-exclusive
        /// actions (dismiss, set home, archetype, rename, transfer ownership).
        /// </summary>
        public static bool CanOwnerAction(Player player, CompanionController companion)
        {
            if (player == null || companion == null) return false;
            return companion.IsOwner(player) || companion.IsPlayerAdmin(player);
        }

        // â”€â”€ Toggle command (client-side) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Sends the guild access toggle command for <paramref name="companion"/>
        /// to the server.  The caller must be the companion's owner.
        /// </summary>
        public static void Toggle(CompanionController companion)
        {
            if (companion == null) return;
            var nview = companion.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            bool current = IsGuildAccessEnabled(companion);
            bool newValue = !current;

            GuildAPI.SetCompanionGuildAccess(nview.GetZDO().m_uid, newValue);

            // OPTIMISTIC LOCAL UPDATE: the server-authoritative write goes through
            // ZDO replication and won't reach us within this frame, but the radial
            // menu refreshes immediately. Stamp the ZDO locally so the toggle label
            // ("Guild: On"/"Guild: Off") flips right now. ZDO.Set is a no-op for
            // non-owners, so claim ownership first; the server's reply will reclaim
            // ownership and confirm (or correct) the value via replication.
            if (!nview.IsOwner()) nview.ClaimOwnership();
            nview.GetZDO().Set(ZDO_KEY, newValue);

            var state = newValue ? "enabled" : "disabled";
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"{companion.GetDisplayName()}: Guild access {state}");
        }

        /// <summary>Current toggle state for display in the radial menu.</summary>
        public static bool GetToggleState(CompanionController companion)
            => IsGuildAccessEnabled(companion);
    }
}
