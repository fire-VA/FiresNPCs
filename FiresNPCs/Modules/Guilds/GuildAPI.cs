using System;
using System.Collections.Generic;
using FiresRPGmaker.Modules.Guilds.Core;
using FiresRPGmaker.Modules.Guilds.Network;

namespace FiresRPGmaker.Modules.Guilds
{
    /// <summary>
    /// Public facade for the guild system.
    ///
    /// EVENTS
    /// ------
    /// Subscribe to these to react to guild state changes without
    /// coupling to the network internals.  All events are fired on the
    /// main thread after the local cache is already updated.
    ///
    /// COMMANDS
    /// --------
    /// All mutation commands send an RPC to the server.  They are
    /// fire-and-forget; the resulting state arrives via a FullSync
    /// which fires <see cref="OnGuildsUpdated"/>.
    /// </summary>
    public static class GuildAPI
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired whenever the local guild cache is replaced by a server sync.</summary>
        public static event Action<IReadOnlyList<GuildData>> OnGuildsUpdated;

        /// <summary>A guild was created (server-side event; fires everywhere via sync).</summary>
        public static event Action<GuildData> OnGuildCreated;

        /// <summary>A guild was permanently dissolved.</summary>
        public static event Action<string> OnGuildDeleted;   // guildId

        /// <summary>A player joined a guild.</summary>
        public static event Action<GuildData, long> OnGuildJoined;  // guild, playerId

        /// <summary>A player left or was kicked from a guild.</summary>
        public static event Action<GuildData, long> OnGuildLeft;    // guild, playerId

        /// <summary>A guild chat message was received.</summary>
        public static event Action<string, string, string> OnGuildChatReceived; // senderName, guildName, text

        /// <summary>
        /// The persisted chat backlog arrived from the server (in response
        /// to RequestSync on join). Replaces any previously cached history
        /// for the named guild — UI should redraw the Chat tab from scratch.
        /// </summary>
        public static event Action<string, List<global::FiresRPGmaker.Modules.Guilds.Storage.GuildStorage.ChatMessage>> OnGuildChatHistoryReceived; // guildName, messages

        // ── Event dispatch (called by GuildSyncManager) ────────────────────────

        internal static void FireGuildsUpdated(List<GuildData> guilds)
            => OnGuildsUpdated?.Invoke(guilds);

        internal static void FireGuildCreated(GuildData guild)
            => OnGuildCreated?.Invoke(guild);

        internal static void FireGuildDeleted(string guildId)
            => OnGuildDeleted?.Invoke(guildId);

        internal static void FireGuildJoined(GuildData guild, long playerId)
            => OnGuildJoined?.Invoke(guild, playerId);

        internal static void FireGuildLeft(GuildData guild, long playerId)
            => OnGuildLeft?.Invoke(guild, playerId);

        internal static void FireGuildChatReceived(string sender, string guildName, string text)
            => OnGuildChatReceived?.Invoke(sender, guildName, text);

        internal static void FireGuildChatHistoryReceived(string guildName, List<global::FiresRPGmaker.Modules.Guilds.Storage.GuildStorage.ChatMessage> messages)
            => OnGuildChatHistoryReceived?.Invoke(guildName, messages);

        // ── Queries ────────────────────────────────────────────────────────────

        /// <summary>Returns all guilds in the current world (read-only view).</summary>
        public static IReadOnlyList<GuildData> GetAllGuilds()
            => GuildSyncManager.GetAllGuilds();

        /// <summary>Returns the guild the given player belongs to, or null. When an external owner
        /// (VikingLands.Core) holds guilds, answers from it: the local player's guild becomes a TDV-backed
        /// proxy, and any player TDV reports as a guildmate maps to that same proxy.</summary>
        public static GuildData GetPlayerGuild(long playerId)
        {
            if (global::FiresRPGmaker.RpgMakerApi.GuildsOwnedExternally)
            {
                var local = Player.m_localPlayer;
                long localId = local != null ? local.GetPlayerID() : 0L;
                if (playerId == localId || (localId != 0L && global::FiresRPGmaker.RpgMakerApi.AreGuilded(localId, playerId)))
                    return BuildExternalGuildProxy();
                return null; // a non-guildmate's own guild isn't fully known to us through TDV
            }
            return GuildSyncManager.GetPlayerGuild(playerId);
        }

        /// <summary>Returns the local player's guild, or null.</summary>
        public static GuildData GetOwnGuild()
        {
            if (global::FiresRPGmaker.RpgMakerApi.GuildsOwnedExternally)
                return BuildExternalGuildProxy();
            var local = Player.m_localPlayer;
            return local == null ? null : GetPlayerGuild(local.GetPlayerID());
        }

        /// <summary>Returns true if the two players are in the same guild.</summary>
        public static bool AreGuildmates(long playerA, long playerB)
        {
            if (global::FiresRPGmaker.RpgMakerApi.GuildsOwnedExternally)
                return global::FiresRPGmaker.RpgMakerApi.AreGuilded(playerA, playerB);
            var guildA = GetPlayerGuild(playerA);
            if (guildA == null) return false;
            return guildA.IsMember(playerB);
        }

        /// <summary>Builds a lightweight <see cref="GuildData"/> for the local player's guild from the
        /// external owner's data (VikingLands.Core), so RPG Maker's guild queries return the real guild +
        /// members instead of 0. Returns null when the local player isn't in an external guild.</summary>
        private static GuildData BuildExternalGuildProxy()
        {
            string name = global::FiresRPGmaker.RpgMakerApi.LocalGuildName();
            if (string.IsNullOrEmpty(name)) return null;

            long leaderId = global::FiresRPGmaker.RpgMakerApi.LocalGuildLeaderId();
            var guild = new GuildData { GuildId = name, Name = name, LeaderPlayerId = leaderId };

            var ids = global::FiresRPGmaker.RpgMakerApi.LocalGuildMemberIds();
            var names = global::FiresRPGmaker.RpgMakerApi.LocalGuildMemberNames();
            for (int i = 0; i < ids.Length; i++)
            {
                string mn = (names != null && i < names.Length) ? names[i] : "";
                guild.Members.Add(new GuildMember
                {
                    PlayerId = ids[i],
                    PlayerName = mn,
                    Rank = ids[i] == leaderId ? GuildRank.Leader : GuildRank.Member,
                });
            }
            return guild;
        }

        /// <summary>
        /// Returns whether a player (by id) is currently online.
        /// Uses the ZNet player list.
        /// </summary>
        public static bool IsPlayerOnline(long playerId)
        {
            try
            {
                var list = ZNet.instance?.GetPlayerList();
                if (list == null) return false;
                foreach (var entry in list)
                    if (entry.m_characterID.UserID == playerId) return true;
                return false;
            }
            catch { return false; }
        }

        // ── Guild commands ─────────────────────────────────────────────────────

        public static void CreateGuild(string name, string description)
            => GuildSyncManager.CmdCreateGuild(name, description);

        public static void DeleteGuild()
            => GuildSyncManager.CmdDeleteGuild();

        public static void RenameGuild(string newName)
            => GuildSyncManager.CmdRename(newName);

        public static void SetDescription(string description)
            => GuildSyncManager.CmdSetDescription(description);

        public static void SetColor(byte r, byte g, byte b)
            => GuildSyncManager.CmdSetColor(r, g, b);

        public static void SetOpen(bool open)
            => GuildSyncManager.CmdSetOpen(open);

        public static void InvitePlayer(long targetPlayerId, string targetName)
            => GuildSyncManager.CmdInvite(targetPlayerId, targetName);

        public static void KickMember(long targetPlayerId)
            => GuildSyncManager.CmdKick(targetPlayerId);

        public static void PromoteMember(long targetPlayerId)
            => GuildSyncManager.CmdPromote(targetPlayerId);

        public static void DemoteMember(long targetPlayerId)
            => GuildSyncManager.CmdDemote(targetPlayerId);

        public static void LeaveGuild()
            => GuildSyncManager.CmdLeave();

        public static void TransferLeadership(long newLeaderId)
            => GuildSyncManager.CmdTransferLeadership(newLeaderId);

        public static void ApplyToGuild(string guildId, string message)
            => GuildSyncManager.CmdApplyToGuild(guildId, message);

        public static void HandleApplication(long applicantId, bool accept)
            => GuildSyncManager.CmdHandleApplication(applicantId, accept);

        public static void SendChatMessage(string text)
            => GuildSyncManager.CmdChatMessage(text);

        // ── Companion commands ─────────────────────────────────────────────────

        public static void SetCompanionGuildAccess(ZDOID companionId, bool allow)
            => GuildSyncManager.CmdSetCompanionGuildAccess(companionId, allow);

        public static void TransferCompanionOwnership(ZDOID companionId, long newOwnerId)
            => GuildSyncManager.CmdTransferCompanionOwnership(companionId, newOwnerId);
    }
}
