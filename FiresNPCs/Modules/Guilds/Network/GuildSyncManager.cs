using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds.Core;
using FiresRPGmaker.Modules.Guilds.Storage;

namespace FiresRPGmaker.Modules.Guilds.Network
{
    /// <summary>
    /// Server-authoritative guild networking.
    ///
    /// PROTOCOL
    /// --------
    ///   FiresRPGmaker_GuildRoute  — client → server.
    ///       Carries (byte msgType, ZPackage payload).
    ///       Server validates the sender, mutates state, saves to disk,
    ///       then broadcasts FullSync to all connected peers.
    ///
    ///   FiresRPGmaker_GuildMsg    — server → target peer(s).
    ///       Carries (byte msgType, ZPackage payload).
    ///       Clients update their local cache and fire GuildAPI events.
    ///
    ///   FiresRPGmaker_GuildInvite — server → single target peer.
    ///       Invite popup payload.
    ///
    /// SERVER STATE
    /// ------------
    ///   _guilds — in-memory List<GuildData>, loaded from disk on world start.
    ///   All mutations happen on the server; clients get a FullSync JSON blob
    ///   after every change and on initial connect.
    ///
    /// CLIENT STATE
    /// ------------
    ///   _guilds — in-memory cache received from server.  Read-only outside
    ///   of RPC_GuildMsg.
    /// </summary>
    public static class GuildSyncManager
    {
        public const string LogPrefix = "[GuildSync]";

        private const string RpcRoute  = "FiresRPGmaker_GuildRoute";
        private const string RpcMsg    = "FiresRPGmaker_GuildMsg";
        private const string RpcInvite = "FiresRPGmaker_GuildInvite";

        private enum MsgType : byte
        {
            FullSync                  = 1,
            CreateGuild               = 2,
            DeleteGuild               = 3,
            Invite                    = 4,
            InviteAccept              = 5,
            InviteDecline             = 6,
            Kick                      = 7,
            Promote                   = 8,
            Demote                    = 9,
            LeaveGuild                = 10,
            SetDescription            = 11,
            SetColor                  = 12,
            SetOpen                   = 13,
            ApplyToGuild              = 14,
            HandleApplication         = 15,  // accept=true or reject=false
            SetCompanionGuildAccess   = 16,
            TransferCompanionOwnership= 17,
            ChatMessage               = 18,
            RenameGuild               = 19,
            TransferLeadership        = 20,
            // Client → server. Sent once after the client finishes registering
            // its RPC handlers, so the server can re-push the FullSync that
            // was likely dropped when the OnNewConnection-triggered FullSync
            // raced ahead of the client's handler registration.
            RequestSync               = 21,
            // Server → client. The persisted guild chat backlog. Sent in
            // response to RequestSync (and could also be sent on demand via
            // a future request-history button) so members see messages from
            // sessions they weren't online for.
            ChatHistory               = 22,
        }

        private static bool _registered;
        private static List<GuildData> _guilds = new List<GuildData>();

        // Client-side: have we already asked the server to (re)push state on
        // this ZRoutedRpc instance? Reset whenever we re-register against a
        // new instance (world change / reconnect).
        private static bool _clientSyncRequested;

        // Pending invite state on the client side.
        private static string _pendingInviteGuildId;
        private static long   _pendingInviterPlayerId;

        // ── Init ──────────────────────────────────────────────────────────────

        // Track which ZRoutedRpc instance we registered against. Valheim destroys
        // and recreates ZRoutedRpc.instance on every world load / server change,
        // and the new instance starts with an empty handler dictionary. Without
        // this check we'd skip re-registration after the first session and the
        // server's FullSync RPC would silently drop on the client because no
        // handler is bound on the current ZRoutedRpc.
        private static ZRoutedRpc _registeredOn;

        public static void Initialize()
        {
            var current = ZRoutedRpc.instance;
            Debug.Log($"{LogPrefix} Initialize() entered — _registered={_registered}, sameInstanceAsLast={(current == _registeredOn && current != null)}, ZRoutedRpc.instance={(current != null ? "ready" : "NULL")}");

            if (current == null)
            {
                Debug.LogWarning($"{LogPrefix} ZRoutedRpc not ready — deferring registration.");
                return;
            }

            // Only short-circuit if we already registered on THIS exact instance.
            // After a world change / server reconnect ZRoutedRpc.instance is a
            // different object and our handlers don't carry over — we must
            // re-register on the new instance.
            if (_registered && _registeredOn == current) return;

            current.Register<ZPackage>(RpcRoute,  new Action<long, ZPackage>(RPC_GuildRoute));
            current.Register<ZPackage>(RpcMsg,    new Action<long, ZPackage>(RPC_GuildMsg));
            current.Register<ZPackage>(RpcInvite, new Action<long, ZPackage>(RPC_GuildInvite));
            _registered    = true;
            _registeredOn  = current;
            _clientSyncRequested = false;

            // If this is the server (listen-server or dedicated), load from disk.
            // Clients DON'T request sync from here — Game.Start fires before
            // ZNetScene/ObjectDB are ready and the route to the server peer
            // isn't fully wired, so a broadcast at this point silently drops.
            // The Ascend.WaitForZNetReady coroutine calls GuildSyncManager.RequestSync
            // at the right moment (same gate that TerritorySyncedState uses).
            if (ZNet.instance != null && ZNet.instance.IsServer())
                ServerLoad();

            Debug.Log($"{LogPrefix} Initialized — registered handlers for '{RpcRoute}', '{RpcMsg}', '{RpcInvite}' on ZRoutedRpc instance {current.GetHashCode()}.");
        }

        /// <summary>
        /// Public client-side entry point: ask the server to push current
        /// guild state. Called from Ascend's WaitForZNetReady coroutine
        /// (same place as TerritorySyncedState.RequestFromServer) and from
        /// the retry watchdog if our cache is still empty.
        ///
        /// Hooking from Game.Start postfix turned out to be too early on
        /// dedicated clients — ZRoutedRpc can be "ready" but the routing
        /// table to the server peer isn't, so the broadcast goes nowhere.
        /// By the time ZNetScene + ObjectDB exist (the WaitForZNetReady
        /// gate), the server peer is reliably reachable.
        /// </summary>
        public static void RequestSync()
        {
            if (ZRoutedRpc.instance == null)
            {
                Debug.LogWarning($"{LogPrefix} RequestSync aborted: ZRoutedRpc null");
                return;
            }
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                // Server runs ServerLoad directly — no RPC needed.
                return;
            }

            // Use SendRoute for the wire format. The server-side unwrap reads
            // (playerId, playerName, innerBytes) — earlier this method wrote
            // its own packet with only (playerId, innerBytes) and the server
            // blew up with EndOfStreamException on every retry, leaving the
            // client cache permanently empty.
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.RequestSync);
            SendRoute(pkg);
            _clientSyncRequested = true;
            long localPlayerId = Player.m_localPlayer != null
                ? Player.m_localPlayer.GetPlayerID()
                : 0L;
            Debug.Log($"{LogPrefix} RequestSync sent to server (have {_guilds.Count} cached, localPlayerId={localPlayerId}).");
        }

        /// <summary>True when the client cache is empty and we should retry.</summary>
        public static bool ClientNeedsSync
        {
            get
            {
                if (ZNet.instance == null || ZNet.instance.IsServer()) return false;
                return _guilds.Count == 0;
            }
        }

        // ── Server disk load ──────────────────────────────────────────────────

        public static void ServerLoad()
        {
            _guilds = GuildStorage.LoadAll();
            _chatHistory = GuildStorage.LoadChatHistory();
            // Seed the player-name cache from persisted member entries so
            // ResolvePlayerName has something to return before peers send any
            // route packets (e.g., chat broadcast to an offline member).
            _playerNameCache.Clear();
            foreach (var g in _guilds)
                foreach (var m in g.Members)
                    if (m.PlayerId != 0
                        && !string.IsNullOrEmpty(m.PlayerName)
                        && !m.PlayerName.StartsWith("Player_"))
                        _playerNameCache[m.PlayerId] = m.PlayerName;
            int chatGuilds = _chatHistory.Count;
            int chatMessages = 0;
            foreach (var kv in _chatHistory) chatMessages += kv.Value?.Count ?? 0;
            Debug.Log($"{LogPrefix} Loaded {_guilds.Count} guild(s) from disk; name cache seeded with {_playerNameCache.Count} entries; chat history: {chatMessages} messages across {chatGuilds} guild(s).");
        }

        public static void ServerSave()
        {
            GuildStorage.SaveAll(_guilds);
        }

        // Called when a new peer connects: push full state to them.
        public static void OnPeerConnected(long peerUid)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SendFullSync(peerUid);
        }

        // ── Public client-side commands ───────────────────────────────────────

        public static List<GuildData> GetAllGuilds() => _guilds;

        public static GuildData GetPlayerGuild(long playerId)
        {
            foreach (var g in _guilds)
                if (g.IsMember(playerId)) return g;
            return null;
        }

        public static void CmdCreateGuild(string name, string description)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.CreateGuild);
            pkg.Write(name ?? "");
            pkg.Write(description ?? "");
            Debug.Log($"{LogPrefix} CmdCreateGuild — sending route: name='{name}', desc.len={(description ?? "").Length}, ZRoutedRpc.instance={(ZRoutedRpc.instance != null ? "ready" : "NULL")}");
            SendRoute(pkg);
        }

        public static void CmdDeleteGuild()
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.DeleteGuild);
            SendRoute(pkg);
        }

        public static void CmdInvite(long targetPlayerId, string targetPlayerName)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Invite);
            pkg.Write(targetPlayerId);
            pkg.Write(targetPlayerName ?? "");
            SendRoute(pkg);
        }

        public static void CmdAcceptInvite(string guildId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.InviteAccept);
            pkg.Write(guildId ?? "");
            SendRoute(pkg);
        }

        public static void CmdDeclineInvite(string guildId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.InviteDecline);
            pkg.Write(guildId ?? "");
            SendRoute(pkg);
        }

        public static void CmdKick(long targetPlayerId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Kick);
            pkg.Write(targetPlayerId);
            SendRoute(pkg);
        }

        public static void CmdPromote(long targetPlayerId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Promote);
            pkg.Write(targetPlayerId);
            SendRoute(pkg);
        }

        public static void CmdDemote(long targetPlayerId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Demote);
            pkg.Write(targetPlayerId);
            SendRoute(pkg);
        }

        public static void CmdLeave()
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.LeaveGuild);
            SendRoute(pkg);
        }

        public static void CmdSetDescription(string desc)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.SetDescription);
            pkg.Write(desc ?? "");
            SendRoute(pkg);
        }

        public static void CmdSetColor(byte r, byte g, byte b)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.SetColor);
            pkg.Write(r); pkg.Write(g); pkg.Write(b);
            SendRoute(pkg);
        }

        public static void CmdSetOpen(bool open)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.SetOpen);
            pkg.Write(open);
            SendRoute(pkg);
        }

        public static void CmdRename(string newName)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.RenameGuild);
            pkg.Write(newName ?? "");
            SendRoute(pkg);
        }

        public static void CmdApplyToGuild(string guildId, string message)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.ApplyToGuild);
            pkg.Write(guildId ?? "");
            pkg.Write(message ?? "");
            SendRoute(pkg);
        }

        public static void CmdHandleApplication(long applicantId, bool accept)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.HandleApplication);
            pkg.Write(applicantId);
            pkg.Write(accept);
            SendRoute(pkg);
        }

        public static void CmdSetCompanionGuildAccess(ZDOID companionId, bool allow)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.SetCompanionGuildAccess);
            pkg.Write(companionId);
            pkg.Write(allow);
            SendRoute(pkg);
        }

        public static void CmdTransferCompanionOwnership(ZDOID companionId, long newOwnerId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.TransferCompanionOwnership);
            pkg.Write(companionId);
            pkg.Write(newOwnerId);
            SendRoute(pkg);
        }

        public static void CmdChatMessage(string text)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.ChatMessage);
            pkg.Write(text ?? "");
            SendRoute(pkg);
        }

        public static void CmdTransferLeadership(long newLeaderId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.TransferLeadership);
            pkg.Write(newLeaderId);
            SendRoute(pkg);
        }

        // ── Transport helpers ─────────────────────────────────────────────────

        private static void SendRoute(ZPackage pkg)
        {
            if (ZRoutedRpc.instance == null) return;

            // Wrap the inner packet with the local player's m_playerID AND
            // their display name. On a dedicated server, peer.m_characterID.UserID
            // is the network UID, NOT the m_playerID stored in the .fch file —
            // they diverge, so the server can't map ID→peer or ID→name reliably
            // from peer info alone. We send the canonical pair from the client
            // and the server stores the name as-given. This mirrors the
            // PartyManager pattern (see PartyManager.SendRouted).
            var local = Player.m_localPlayer;
            long localPlayerId = local != null ? local.GetPlayerID() : 0L;
            string localName   = local != null ? (local.GetPlayerName() ?? "") : "";

            // Bail out instead of churning useless requests when the player isn't
            // ready yet. Player.m_localPlayer can exist but GetPlayerID() returns
            // 0 for several frames after respawn while PlayerProfile is loading;
            // sending playerId=0 makes the server return an empty FullSync (it
            // can't resolve who's asking), the client retries, and the cycle
            // repeats until the ZRpc keep-alive hits its 2-minute timeout and
            // disconnects. RetryGuildSync will keep firing - by the time the
            // player actually has a real ID, the next retry will go through.
            if (localPlayerId == 0L)
            {
                Debug.Log($"{LogPrefix} SendRoute deferred: localPlayerId still 0 (player not fully spawned). Will retry.");
                return;
            }

            var wrapped = new ZPackage();
            wrapped.Write(localPlayerId);
            wrapped.Write(localName);
            wrapped.Write(pkg.GetArray());

            // 0L is the server peer ID — same target territories use.
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRoute, wrapped);
        }

        private static void SendMsg(long peerUid, MsgType type, ZPackage payload)
        {
            if (ZRoutedRpc.instance == null) return;
            var outer = new ZPackage();
            outer.Write((byte)type);
            outer.Write(payload?.GetBase64() ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, RpcMsg, outer);
        }

        private static void BroadcastMsg(MsgType type, ZPackage payload)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null) return;
            var peers = ZNet.instance.GetPeers();
            foreach (var peer in peers)
                SendMsg(peer.m_uid, type, payload);

            // Also deliver to the local listen-server player.
            if (!ZNet.instance.IsDedicated())
                ProcessGuildMsg(type, payload);
        }

        private static void SendFullSync(long peerUid)
        {
            var payload = new ZPackage();
            payload.Write(GuildStorage.Serialize(_guilds));
            Debug.Log($"{LogPrefix} SendFullSync → peer {peerUid}, {_guilds.Count} guild(s).");
            SendMsg(peerUid, MsgType.FullSync, payload);
        }

        private static void BroadcastFullSync()
        {
            var payload = new ZPackage();
            payload.Write(GuildStorage.Serialize(_guilds));
            int peerCount = ZNet.instance?.GetPeers()?.Count ?? 0;
            Debug.Log($"{LogPrefix} BroadcastFullSync → {peerCount} peer(s), {_guilds.Count} guild(s).");
            BroadcastMsg(MsgType.FullSync, payload);
        }

        // ── RPC: client → server router ───────────────────────────────────────

        private static void RPC_GuildRoute(long senderId, ZPackage pkg)
        {
            Debug.Log($"{LogPrefix} RPC_GuildRoute received — senderId={senderId}, pkg={(pkg != null ? "ok" : "NULL")}, isServer={(ZNet.instance != null && ZNet.instance.IsServer())}");
            // Only the server processes routes.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (pkg == null) return;

            try
            {
                // Unwrap: SendRoute prepends the client's m_playerID and their
                // display name so we don't rely on m_characterID.UserID
                // (which is the network UID and diverges from m_playerID on
                // dedicated servers).
                long clientPlayerId   = pkg.ReadLong();
                string clientPlayerName = pkg.ReadString();
                var innerBytes        = pkg.ReadByteArray();
                var inner             = new ZPackage(innerBytes);

                var type = (MsgType)inner.ReadByte();
                long senderPlayerId = clientPlayerId != 0
                    ? clientPlayerId
                    : ResolveSenderPlayerId(senderId);

                Debug.Log($"{LogPrefix} RPC_GuildRoute decoded — type={type}, clientPlayerId={clientPlayerId}, clientName='{clientPlayerName}', resolvedPlayerId={senderPlayerId}");

                if (senderPlayerId == 0
                    && type != MsgType.InviteAccept
                    && type != MsgType.InviteDecline
                    && type != MsgType.RequestSync)
                {
                    Debug.LogWarning($"{LogPrefix} Could not resolve player id for sender {senderId} — dropping {type}.");
                    return;
                }

                // Refresh the cached player name on every route message so
                // stale "Player_<id>" fallbacks from older sessions get
                // overwritten by the canonical client-supplied name. Also
                // saves the world if we changed anything.
                if (!string.IsNullOrEmpty(clientPlayerName))
                    UpsertCachedPlayerName(senderPlayerId, clientPlayerName);

                ServerHandleRoute(type, senderPlayerId, senderId, inner);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} RPC_GuildRoute error: {ex}");
            }
        }

        private static void ServerHandleRoute(MsgType type, long senderPlayerId, long senderUid, ZPackage pkg)
        {
            switch (type)
            {
                case MsgType.CreateGuild:               Server_CreateGuild(senderPlayerId, pkg);               break;
                case MsgType.DeleteGuild:               Server_DeleteGuild(senderPlayerId);                    break;
                case MsgType.Invite:                    Server_Invite(senderPlayerId, pkg);                    break;
                case MsgType.InviteAccept:              Server_InviteAccept(senderPlayerId, pkg);              break;
                case MsgType.InviteDecline:             /* no-op — just informational */                       break;
                case MsgType.Kick:                      Server_Kick(senderPlayerId, pkg);                      break;
                case MsgType.Promote:                   Server_Promote(senderPlayerId, pkg);                   break;
                case MsgType.Demote:                    Server_Demote(senderPlayerId, pkg);                    break;
                case MsgType.LeaveGuild:                Server_Leave(senderPlayerId);                          break;
                case MsgType.SetDescription:            Server_SetDescription(senderPlayerId, pkg);            break;
                case MsgType.SetColor:                  Server_SetColor(senderPlayerId, pkg);                  break;
                case MsgType.SetOpen:                   Server_SetOpen(senderPlayerId, pkg);                   break;
                case MsgType.RenameGuild:               Server_Rename(senderPlayerId, pkg);                    break;
                case MsgType.ApplyToGuild:              Server_Apply(senderPlayerId, pkg);                     break;
                case MsgType.HandleApplication:         Server_HandleApplication(senderPlayerId, pkg);         break;
                case MsgType.SetCompanionGuildAccess:   Server_SetCompanionGuildAccess(senderPlayerId, pkg);   break;
                case MsgType.TransferCompanionOwnership:Server_TransferCompanionOwnership(senderPlayerId, pkg);break;
                case MsgType.ChatMessage:               Server_ChatMessage(senderPlayerId, pkg);               break;
                case MsgType.TransferLeadership:        Server_TransferLeadership(senderPlayerId, pkg);        break;
                case MsgType.RequestSync:
                    SendFullSync(senderUid);
                    // Also ship the persisted chat backlog for this player's
                    // guild so the Chat tab shows previous-session messages.
                    if (senderPlayerId != 0)
                        SendChatHistoryTo(senderUid, senderPlayerId);
                    break;
            }
        }

        // ── RPC: server → client ──────────────────────────────────────────────

        private static void RPC_GuildMsg(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var type    = (MsgType)pkg.ReadByte();
                var b64     = pkg.ReadString();
                var payload = string.IsNullOrEmpty(b64) ? new ZPackage() : new ZPackage(b64);
                Debug.Log($"{LogPrefix} RPC_GuildMsg received — type={type}, payloadBytes={(b64 == null ? 0 : b64.Length)}, senderId={senderId}");
                ProcessGuildMsg(type, payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} RPC_GuildMsg error: {ex}");
            }
        }

        private static void ProcessGuildMsg(MsgType type, ZPackage payload)
        {
            switch (type)
            {
                case MsgType.FullSync:
                {
                    var json = payload.ReadString();
                    _guilds = GuildStorage.Deserialize(json);
                    Debug.Log($"{LogPrefix} FullSync applied — {_guilds.Count} guild(s) in cache.");
                    GuildAPI.FireGuildsUpdated(_guilds);
                    break;
                }
                case MsgType.ChatMessage:
                {
                    var senderName = payload.ReadString();
                    var guildName  = payload.ReadString();
                    var text       = payload.ReadString();
                    GuildAPI.FireGuildChatReceived(senderName, guildName, text);
                    break;
                }
                case MsgType.ChatHistory:
                {
                    var guildName = payload.ReadString();
                    var json      = payload.ReadString();
                    var list      = GuildStorage.DeserializeChatList(json);
                    GuildAPI.FireGuildChatHistoryReceived(guildName, list);
                    break;
                }
            }
        }

        private static void RPC_GuildInvite(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var guildId    = pkg.ReadString();
                var guildName  = pkg.ReadString();
                var inviterName= pkg.ReadString();

                _pendingInviteGuildId   = guildId;
                _pendingInviterPlayerId = senderId;

                var message = $"{inviterName} has invited you to join guild <b>{guildName}</b>.";
                UnifiedPopup.Push(new YesNoPopup("Guild Invitation", message,
                    yesCallback: () => CmdAcceptInvite(guildId),
                    noCallback:  () => CmdDeclineInvite(guildId),
                    localizeText: false));
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} RPC_GuildInvite error: {ex}");
            }
        }

        // ── Server mutation handlers ──────────────────────────────────────────

        private static void Server_CreateGuild(long playerId, ZPackage pkg)
        {
            var name = pkg.ReadString();
            var desc = pkg.ReadString();
            Debug.Log($"{LogPrefix} Server_CreateGuild received — playerId={playerId}, name='{name}', desc.len={(desc ?? "").Length}");

            var cfg = global::FiresRPGmaker.Managers.ConfigManager.Instance;

            if (cfg?.configGuildsEnabled?.Value == false)
            {
                Debug.LogWarning($"{LogPrefix} CreateGuild rejected: guild system disabled.");
                return;
            }

            if (cfg?.configAllowGuildCreation?.Value == false)
            {
                Debug.LogWarning($"{LogPrefix} CreateGuild rejected: guild creation is disabled by server config.");
                return;
            }

            int maxGuilds = cfg?.configMaxGuildsPerWorld?.Value ?? 100;
            if (_guilds.Count >= maxGuilds)
            {
                Debug.LogWarning($"{LogPrefix} CreateGuild rejected: max guilds per world ({maxGuilds}) reached.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning($"{LogPrefix} CreateGuild rejected: empty name from {playerId}.");
                return;
            }

            // Player must not already be in a guild.
            if (GetPlayerGuild(playerId) != null)
            {
                Debug.LogWarning($"{LogPrefix} CreateGuild rejected: {playerId} already in a guild.");
                return;
            }

            var guild = new GuildData
            {
                GuildId        = Guid.NewGuid().ToString(),
                Name           = name.Trim(),
                Description    = (desc ?? "").Trim(),
                LeaderPlayerId = playerId,
                CreatedUtcTicks= DateTime.UtcNow.Ticks,
                IsOpen         = false,
            };

            // Resolve player name.
            string playerName = ResolvePlayerName(playerId);
            guild.Members.Add(new GuildMember
            {
                PlayerId          = playerId,
                PlayerName        = playerName,
                Rank              = GuildRank.Leader,
                JoinedUtcTicks    = DateTime.UtcNow.Ticks,
                LastOnlineUtcTicks= DateTime.UtcNow.Ticks,
            });

            _guilds.Add(guild);
            ServerSave();
            BroadcastFullSync();

            GuildAPI.FireGuildCreated(guild);
            Debug.Log($"{LogPrefix} Guild '{name}' created by {playerName} ({playerId}).");
        }

        private static void Server_DeleteGuild(long playerId)
        {
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (guild.LeaderPlayerId != playerId)
            {
                Debug.LogWarning($"{LogPrefix} DeleteGuild rejected: {playerId} is not leader.");
                return;
            }

            string id = guild.GuildId;
            _guilds.Remove(guild);
            ServerSave();
            BroadcastFullSync();

            GuildAPI.FireGuildDeleted(id);
            Debug.Log($"{LogPrefix} Guild '{guild.Name}' deleted by {playerId}.");
        }

        private static void Server_Invite(long inviterPlayerId, ZPackage pkg)
        {
            long   targetId   = pkg.ReadLong();
            string targetName = pkg.ReadString();

            var guild = GetPlayerGuild(inviterPlayerId);
            if (guild == null) return;
            if (!guild.GetMember(inviterPlayerId)!.IsOfficer) return;
            if (guild.IsMember(targetId)) return;

            // Forward invite popup to the target peer.
            long targetPeerUid = ResolvePeerUid(targetId);
            if (targetPeerUid == 0)
            {
                Debug.LogWarning($"{LogPrefix} Invite: target {targetId} not online.");
                return;
            }

            var invitePkg = new ZPackage();
            invitePkg.Write(guild.GuildId);
            invitePkg.Write(guild.Name);
            invitePkg.Write(ResolvePlayerName(inviterPlayerId));
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerUid, RpcInvite, invitePkg);
        }

        private static void Server_InviteAccept(long playerId, ZPackage pkg)
        {
            var guildId = pkg.ReadString();
            var guild = FindGuildById(guildId);
            if (guild == null) return;
            if (guild.IsMember(playerId)) return;
            if (GetPlayerGuild(playerId) != null) return; // already in another guild

            int maxMembers = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configMaxGuildMembers?.Value ?? 20;
            if (guild.Members.Count >= maxMembers)
            {
                Debug.LogWarning($"{LogPrefix} InviteAccept rejected: guild '{guild.Name}' is full ({maxMembers} members).");
                return;
            }

            guild.Members.Add(new GuildMember
            {
                PlayerId          = playerId,
                PlayerName        = ResolvePlayerName(playerId),
                Rank              = GuildRank.Member,
                JoinedUtcTicks    = DateTime.UtcNow.Ticks,
                LastOnlineUtcTicks= DateTime.UtcNow.Ticks,
            });

            ServerSave();
            BroadcastFullSync();
            GuildAPI.FireGuildJoined(guild, playerId);
        }

        private static void Server_Kick(long actorId, ZPackage pkg)
        {
            long targetId = pkg.ReadLong();
            var guild = GetPlayerGuild(actorId);
            if (guild == null) return;
            if (!guild.CanKick(actorId, targetId)) return;

            guild.Members.RemoveAll(m => m.PlayerId == targetId);
            ServerSave();
            BroadcastFullSync();
            GuildAPI.FireGuildLeft(guild, targetId);
        }

        private static void Server_Promote(long actorId, ZPackage pkg)
        {
            long targetId = pkg.ReadLong();
            var guild = GetPlayerGuild(actorId);
            if (guild == null) return;
            if (!guild.CanPromote(actorId, targetId)) return;

            var target = guild.GetMember(targetId);
            if (target == null) return;
            target.Rank = (GuildRank)((int)target.Rank + 1);
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_Demote(long actorId, ZPackage pkg)
        {
            long targetId = pkg.ReadLong();
            var guild = GetPlayerGuild(actorId);
            if (guild == null) return;
            if (!guild.CanDemote(actorId, targetId)) return;

            var target = guild.GetMember(targetId);
            if (target == null) return;
            target.Rank = (GuildRank)((int)target.Rank - 1);
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_Leave(long playerId)
        {
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;

            bool wasLeader = guild.LeaderPlayerId == playerId;
            guild.Members.RemoveAll(m => m.PlayerId == playerId);

            if (guild.Members.Count == 0)
            {
                // Last member left — dissolve.
                string id = guild.GuildId;
                _guilds.Remove(guild);
                ServerSave();
                BroadcastFullSync();
                GuildAPI.FireGuildDeleted(id);
                return;
            }

            if (wasLeader)
            {
                // Transfer to oldest member.
                GuildMember next = null;
                long oldestTicks = long.MaxValue;
                foreach (var m in guild.Members)
                {
                    if (m.JoinedUtcTicks < oldestTicks) { oldestTicks = m.JoinedUtcTicks; next = m; }
                }
                if (next != null)
                {
                    next.Rank           = GuildRank.Leader;
                    guild.LeaderPlayerId= next.PlayerId;
                }
            }

            ServerSave();
            BroadcastFullSync();
            GuildAPI.FireGuildLeft(guild, playerId);
        }

        private static void Server_SetDescription(long playerId, ZPackage pkg)
        {
            var desc  = pkg.ReadString();
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (!guild.CanEditGuild(playerId)) return;
            guild.Description = (desc ?? "").Trim();
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_SetColor(long playerId, ZPackage pkg)
        {
            byte r = pkg.ReadByte(), g = pkg.ReadByte(), b = pkg.ReadByte();
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (!guild.CanEditGuild(playerId)) return;
            guild.ColorR = r; guild.ColorG = g; guild.ColorB = b;
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_SetOpen(long playerId, ZPackage pkg)
        {
            bool open = pkg.ReadBool();
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (!guild.CanEditGuild(playerId)) return;
            guild.IsOpen = open;
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_Rename(long playerId, ZPackage pkg)
        {
            var name  = pkg.ReadString();
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (guild.LeaderPlayerId != playerId) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            guild.Name = name.Trim();
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_Apply(long playerId, ZPackage pkg)
        {
            var guildId = pkg.ReadString();
            var message = pkg.ReadString();
            var guild   = FindGuildById(guildId);
            if (guild == null) return;
            if (!guild.IsOpen) return;
            if (guild.IsMember(playerId)) return;
            if (GetPlayerGuild(playerId) != null) return;
            if (guild.HasApplied(playerId)) return;

            guild.Applications.Add(new GuildApplication
            {
                PlayerId       = playerId,
                PlayerName     = ResolvePlayerName(playerId),
                Message        = (message ?? "").Trim(),
                AppliedUtcTicks= DateTime.UtcNow.Ticks,
            });
            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_HandleApplication(long actorId, ZPackage pkg)
        {
            long applicantId = pkg.ReadLong();
            bool accept      = pkg.ReadBool();
            var guild = GetPlayerGuild(actorId);
            if (guild == null) return;
            if (!guild.CanManageApplications(actorId)) return;

            guild.Applications.RemoveAll(a => a.PlayerId == applicantId);

            if (accept && !guild.IsMember(applicantId) && GetPlayerGuild(applicantId) == null)
            {
                guild.Members.Add(new GuildMember
                {
                    PlayerId          = applicantId,
                    PlayerName        = ResolvePlayerName(applicantId),
                    Rank              = GuildRank.Member,
                    JoinedUtcTicks    = DateTime.UtcNow.Ticks,
                    LastOnlineUtcTicks= DateTime.UtcNow.Ticks,
                });
                GuildAPI.FireGuildJoined(guild, applicantId);
            }

            ServerSave();
            BroadcastFullSync();
        }

        private static void Server_SetCompanionGuildAccess(long playerId, ZPackage pkg)
        {
            var zdoid = pkg.ReadZDOID();
            bool allow = pkg.ReadBool();

            var guild = GetPlayerGuild(playerId);
            if (guild == null)
            {
                Debug.LogWarning($"{LogPrefix} SetCompanionGuildAccess rejected: playerId {playerId} not in any guild.");
                return;
            }

            // Verify the requesting player actually owns the companion.
            var zdo = ZDOMan.instance?.GetZDO(zdoid);
            if (zdo == null)
            {
                Debug.LogWarning($"{LogPrefix} SetCompanionGuildAccess rejected: ZDO {zdoid} not found.");
                return;
            }
            long ownerInZdo = zdo.GetLong("companion_owner", 0);
            if (ownerInZdo != playerId)
            {
                Debug.LogWarning($"{LogPrefix} SetCompanionGuildAccess rejected: ownerInZdo={ownerInZdo} != playerId={playerId}.");
                return;
            }

            var key = zdoid.ToString();
            if (allow)
            {
                if (!guild.GuildAccessCompanionIds.Contains(key))
                    guild.GuildAccessCompanionIds.Add(key);
            }
            else
            {
                guild.GuildAccessCompanionIds.Remove(key);
            }

            // Stamp companion_guild_access on the ZDO. ZDO.Set is a no-op for
            // non-owners, so we must claim ownership first — the companion's
            // ZDO is typically owned by the client peer that's near it, not
            // by us. This is the same pattern vanilla Tameable uses
            // (ClaimOwnership before writing taming state).
            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set("companion_guild_access", allow);

            ServerSave();
            BroadcastFullSync();
            Debug.Log($"{LogPrefix} Companion guild access {(allow ? "ENABLED" : "DISABLED")} for {zdoid} by player {playerId}.");
        }

        private static void Server_TransferCompanionOwnership(long playerId, ZPackage pkg)
        {
            var zdoid       = pkg.ReadZDOID();
            long newOwnerId = pkg.ReadLong();

            // Validate: sender must be current owner.
            var zdo = ZDOMan.instance?.GetZDO(zdoid);
            if (zdo == null) return;
            long currentOwner = zdo.GetLong("companion_owner", 0);
            if (currentOwner != playerId)
            {
                Debug.LogWarning($"{LogPrefix} TransferOwnership rejected: {playerId} is not owner of companion {zdoid}.");
                return;
            }

            // Validate: new owner must be online and (optionally) in same guild.
            long newOwnerPeerUid = ResolvePeerUid(newOwnerId);
            if (newOwnerPeerUid == 0)
            {
                Debug.LogWarning($"{LogPrefix} TransferOwnership rejected: new owner {newOwnerId} not online.");
                return;
            }

            // Same ZDO ownership trick — claim before writing.
            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set("companion_owner", newOwnerId);
            zdo.Set("companion_guild_access", false);

            BroadcastFullSync(); // Also triggers companions to reload from ZDO on next tick.
            Debug.Log($"{LogPrefix} Companion {zdoid} ownership transferred from {playerId} to {newOwnerId}.");
        }

        private static void Server_ChatMessage(long senderId, ZPackage pkg)
        {
            var text  = pkg.ReadString();
            var guild = GetPlayerGuild(senderId);
            if (guild == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();
            if (text.Length > 512) text = text.Substring(0, 512);

            string senderName = ResolvePlayerName(senderId);

            var payload = new ZPackage();
            payload.Write(senderName);
            payload.Write(guild.Name);
            payload.Write(text);

            // Append to the persisted history (capped at MAX_CHAT_HISTORY) so
            // members can scroll back through messages from previous sessions.
            AppendChatHistory(guild.GuildId, senderName, text);

            // Broadcast only to guild members who are online.
            foreach (var member in guild.Members)
            {
                long peerUid = ResolvePeerUid(member.PlayerId);
                if (peerUid != 0)
                    SendMsg(peerUid, MsgType.ChatMessage, payload);
            }

            // Also deliver to local listen-server player if they're in the guild.
            if (!ZNet.instance.IsDedicated() && guild.IsMember(senderId))
                ProcessGuildMsg(MsgType.ChatMessage, payload);
        }

        // Server-side persistent chat history, keyed by guildId. Loaded once
        // on ServerLoad, mutated on Server_ChatMessage, saved opportunistically.
        private static Dictionary<string, List<GuildStorage.ChatMessage>> _chatHistory
            = new Dictionary<string, List<GuildStorage.ChatMessage>>();

        private static void AppendChatHistory(string guildId, string senderName, string text)
        {
            if (string.IsNullOrEmpty(guildId)) return;
            if (!_chatHistory.TryGetValue(guildId, out var list))
            {
                list = new List<GuildStorage.ChatMessage>();
                _chatHistory[guildId] = list;
            }
            list.Add(new GuildStorage.ChatMessage
            {
                SenderName = senderName,
                Text       = text,
                UtcTicks   = DateTime.UtcNow.Ticks,
            });
            // Cap to last MAX_CHAT_HISTORY entries to keep storage bounded.
            int over = list.Count - GuildStorage.MAX_CHAT_HISTORY;
            if (over > 0) list.RemoveRange(0, over);
            GuildStorage.SaveChatHistory(_chatHistory);
        }

        // Sends the persisted chat backlog to a specific peer. Triggered
        // when the client requests sync; the FullSync handler ships the
        // list separately so we don't bloat the per-mutation broadcast.
        private static void SendChatHistoryTo(long peerUid, long playerId)
        {
            var guild = GetPlayerGuild(playerId);
            if (guild == null) return;
            if (!_chatHistory.TryGetValue(guild.GuildId, out var list)) return;
            if (list == null || list.Count == 0) return;

            var payload = new ZPackage();
            payload.Write(guild.Name);
            payload.Write(GuildStorage.SerializeChatList(list));
            SendMsg(peerUid, MsgType.ChatHistory, payload);
            Debug.Log($"{LogPrefix} SendChatHistoryTo peer {peerUid} → {list.Count} messages.");
        }

        private static void Server_TransferLeadership(long actorId, ZPackage pkg)
        {
            long newLeaderId = pkg.ReadLong();
            var guild = GetPlayerGuild(actorId);
            if (guild == null) return;
            if (guild.LeaderPlayerId != actorId) return;

            var target = guild.GetMember(newLeaderId);
            if (target == null) return;

            // Demote old leader to coleader.
            var oldLeader = guild.GetMember(actorId);
            if (oldLeader != null) oldLeader.Rank = GuildRank.Coleader;

            target.Rank          = GuildRank.Leader;
            guild.LeaderPlayerId = newLeaderId;

            ServerSave();
            BroadcastFullSync();
        }

        // ── Peer/player resolution helpers ────────────────────────────────────

        private static long ResolveSenderPlayerId(long peerUid)
        {
            // On a listen-server the local player's ZRoutedRpc uid matches
            // ZRoutedRpc.instance.GetServerPeerID(), so handle that case first.
            if (ZNet.instance != null && !ZNet.instance.IsDedicated())
            {
                var local = Player.m_localPlayer;
                if (local != null) return local.GetPlayerID();
            }

            // Find the ZNetPeer with matching uid and look up the player list.
            var playerList = ZNet.instance?.GetPlayerList();
            if (playerList == null) return 0;
            foreach (var entry in playerList)
            {
                // ZNet.PlayerInfo.m_characterID.UserID is the player id.
                // We match via peer uid stored in the peer list.
                var peer = ZNet.instance.GetPeer(peerUid);
                if (peer != null && peer.m_characterID.UserID == entry.m_characterID.UserID)
                    return entry.m_characterID.UserID;
            }

            // Simpler fallback: scan all ZNetPeers.
            var peers = ZNet.instance?.GetPeers();
            if (peers == null) return 0;
            foreach (var peer in peers)
                if (peer.m_uid == peerUid) return peer.m_characterID.UserID;

            return 0;
        }

        // Server-side cache of clientPlayerId → canonical PlayerName, kept
        // current by UpsertCachedPlayerName on every inbound route message.
        // Survives across sessions via the persisted member entries in
        // GuildData (we re-seed on ServerLoad), so even peers who haven't
        // sent us a message yet get a real name.
        private static readonly Dictionary<long, string> _playerNameCache
            = new Dictionary<long, string>();

        private static void UpsertCachedPlayerName(long playerId, string name)
        {
            if (playerId == 0 || string.IsNullOrEmpty(name)) return;

            // Update the in-memory cache.
            _playerNameCache[playerId] = name;

            // Also patch any persisted "Player_<id>" entries in the guild
            // member lists. These come from old sessions where the server
            // couldn't resolve a name and stored the fallback. Now that the
            // client told us their real name, fix it everywhere and save.
            bool dirty = false;
            string fallback = $"Player_{playerId}";
            foreach (var g in _guilds)
            {
                foreach (var m in g.Members)
                {
                    if (m.PlayerId != playerId) continue;
                    if (m.PlayerName == name) continue;
                    if (string.IsNullOrEmpty(m.PlayerName) || m.PlayerName == fallback)
                    {
                        m.PlayerName = name;
                        dirty = true;
                    }
                }
                foreach (var a in g.Applications)
                {
                    if (a.PlayerId != playerId) continue;
                    if (a.PlayerName == name) continue;
                    if (string.IsNullOrEmpty(a.PlayerName) || a.PlayerName == fallback)
                    {
                        a.PlayerName = name;
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                ServerSave();
                BroadcastFullSync();
                Debug.Log($"{LogPrefix} Backfilled stale member name → '{name}' for playerId {playerId}.");
            }
        }

        /// <summary>
        /// Server-side: find the network peer UID for a given m_playerID.
        /// Cannot use peer.m_characterID.UserID — that's the network UID, not
        /// the m_playerID, so they diverge on dedicated servers. Instead match
        /// by player NAME (peer.m_playerName == known name from our cache).
        /// </summary>
        private static long ResolvePeerUid(long playerId)
        {
            if (ZNet.instance == null) return 0;

            // Try to look up the player's canonical name first.
            string targetName = ResolvePlayerName(playerId);
            if (string.IsNullOrEmpty(targetName) || targetName.StartsWith("Player_"))
                return 0;

            string lower = targetName.Trim().ToLowerInvariant();
            var peers = ZNet.instance.GetPeers();
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    if (peer == null || string.IsNullOrEmpty(peer.m_playerName)) continue;
                    if (peer.m_playerName.Trim().ToLowerInvariant() == lower)
                        return peer.m_uid;
                }
            }

            // Listen-server's local host doesn't appear in peers list — route
            // via the server peer ID so ZRoutedRpc invokes the local handler.
            if (ZNet.instance.IsServer())
            {
                var localOnHost = Player.m_localPlayer;
                if (localOnHost != null &&
                    string.Equals(localOnHost.GetPlayerName(), targetName, StringComparison.OrdinalIgnoreCase))
                    return ZRoutedRpc.instance != null ? ZRoutedRpc.instance.GetServerPeerID() : 0L;
            }

            return 0;
        }

        private static string ResolvePlayerName(long playerId)
        {
            // 1. Cached canonical name (set from inbound route packets).
            if (_playerNameCache.TryGetValue(playerId, out var cached) && !string.IsNullOrEmpty(cached))
                return cached;

            // 2. Live player instance (only available on listen-server / local host).
            foreach (var p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerID() == playerId) return p.GetPlayerName();

            // 3. Saved guild member entry — note PlayerName here might be
            //    a stale Player_X fallback, so we prefer cached above.
            foreach (var g in _guilds)
            {
                var m = g.GetMember(playerId);
                if (m != null && !string.IsNullOrEmpty(m.PlayerName)) return m.PlayerName;
            }

            // 4. ZNet player list — only matches by m_characterID.UserID
            //    which is network UID; only useful when player ID happens to
            //    coincide with peer UID (listen-server local).
            var list = ZNet.instance?.GetPlayerList();
            if (list != null)
                foreach (var entry in list)
                    if (entry.m_characterID.UserID == playerId) return entry.m_name;

            return $"Player_{playerId}";
        }

        private static GuildData FindGuildById(string id)
        {
            foreach (var g in _guilds) if (g.GuildId == id) return g;
            return null;
        }

        // ── Harmony patch: sync on peer connect ───────────────────────────────

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        private static class ZNet_OnNewConnection_Patch
        {
            private static void Postfix(ZNetPeer peer)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                OnPeerConnected(peer.m_uid);
            }
        }

        // ── Last-online update ────────────────────────────────────────────────

        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        private static class ZNet_Disconnect_Patch
        {
            private static void Prefix(ZNetPeer peer)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                long playerId = peer?.m_characterID.UserID ?? 0;
                if (playerId == 0) return;

                foreach (var guild in _guilds)
                {
                    var m = guild.GetMember(playerId);
                    if (m != null) { m.LastOnlineUtcTicks = DateTime.UtcNow.Ticks; break; }
                }

                ServerSave();
            }
        }
    }
}
