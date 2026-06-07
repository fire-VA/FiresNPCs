using System.Collections.Generic;
using UnityEngine;
using FiresCore.Npc;

namespace FiresRPGmaker.Modules.Guilds.Companions
{
    /// <summary>
    /// Handles companion ownership transfer between players.
    ///
    /// FLOW
    /// ----
    ///   1. Owner opens a "Transfer Ownership" dialog (in GuildMainScreen or
    ///      companion radial context menu) and picks a guild member target.
    ///   2. TransferTo() sends a server RPC.
    ///   3. Server validates (sender is owner, target is online), writes the
    ///      new owner id to the companion's ZDO ("companion_owner"), and
    ///      broadcasts a FullSync so all clients see the updated owner.
    ///   4. On the next ZDO tick the companion reloads from ZDO and the
    ///      ownership change becomes visible to all.
    ///
    /// RESTRICTIONS
    /// ------------
    ///   - Caller must be the current companion owner.
    ///   - Target must be online.
    ///   - Target must be in the same guild (enforced by the UI; server does
    ///     NOT enforce guild membership so admins can also use this).
    ///   - Guild access is cleared on the companion upon transfer.
    /// </summary>
    public static class CompanionOwnershipTransfer
    {
        /// <summary>
        /// Initiates an ownership transfer for <paramref name="companion"/> to
        /// <paramref name="newOwner"/>.  Validates locally before sending the RPC.
        /// </summary>
        public static bool TransferTo(CompanionController companion, Player newOwner)
        {
            if (companion == null || newOwner == null)
            {
                ShowError("Invalid companion or player.");
                return false;
            }

            var local = Player.m_localPlayer;
            if (local == null || !companion.IsOwner(local))
            {
                ShowError("You are not this companion's owner.");
                return false;
            }

            if (newOwner.GetPlayerID() == local.GetPlayerID())
            {
                ShowError("You already own this companion.");
                return false;
            }

            var nview = companion.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                ShowError("Companion is not in a valid state.");
                return false;
            }

            GuildAPI.TransferCompanionOwnership(nview.GetZDO().m_uid, newOwner.GetPlayerID());

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"{companion.GetDisplayName()} ownership transferred to {newOwner.GetPlayerName()}.");

            return true;
        }

        /// <summary>
        /// Returns a list of online guild members (excluding the local player) who
        /// are valid transfer targets â€” for populating the UI picker.
        /// </summary>
        public static List<(long PlayerId, string PlayerName)> GetValidTargets(CompanionController companion)
        {
            var result = new List<(long, string)>();
            if (companion == null) return result;

            var local = Player.m_localPlayer;
            if (local == null || !companion.IsOwner(local)) return result;

            var guild = GuildAPI.GetOwnGuild();
            if (guild == null) return result;

            long localId = local.GetPlayerID();
            foreach (var member in guild.Members)
            {
                if (member.PlayerId == localId) continue;
                if (!GuildAPI.IsPlayerOnline(member.PlayerId)) continue;
                result.Add((member.PlayerId, member.PlayerName));
            }

            return result;
        }

        private static void ShowError(string msg)
            => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"[Transfer] {msg}");
    }
}
