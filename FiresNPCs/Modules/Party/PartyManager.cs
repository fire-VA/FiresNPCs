using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Party
{
    /// <summary>
    /// Coordinator for the friend-group ("party") system.
    ///
    /// SCOPE
    /// -----
    /// First-pass implementation of an inter-player grouping concept
    /// that will eventually grow into a full guild system. For now it
    /// gives players:
    ///
    ///   <c>/invite &lt;name&gt;</c> \u2014 send a YesNo invite popup to a
    ///                              named online player.
    ///   <c>/disband</c>           \u2014 leader-only; tears down the party
    ///                              for every member.
    ///   <c>/leave</c>             \u2014 removes self from the party; if
    ///                              the leaver was leader, leadership
    ///                              transfers to the longest-tenured
    ///                              remaining member.
    ///
    /// All party state lives on each member's
    /// <see cref="Player.m_customData"/> blob (see
    /// <see cref="PartyStorage"/>) so it survives logout and behaves
    /// the same way the companion roster does.
    ///
    /// NETWORKING
    /// ----------
    /// Two routed RPCs:
    ///
    ///   <c>FiresRPGmaker_PartyRoute</c>  \u2014 client \u2192 server. Carries a
    ///       sub-message and a target player NAME. The server resolves
    ///       the name to a peer and forwards the inner message via the
    ///       second RPC. We use a server-side router because clients
    ///       can't enumerate each other's <c>ZNetPeer.m_uid</c> values
    ///       directly.
    ///
    ///   <c>FiresRPGmaker_PartyMsg</c>    \u2014 server \u2192 target peer. Carries
    ///       the actual sub-message that the recipient acts on
    ///       (Invite / Accept / Sync / Disband / Leave).
    ///
    /// SECURITY / SANITY
    /// -----------------
    /// The router enforces "the sender's reported playerId must match
    /// some currently-connected player whose m_uid matches the RPC
    /// sender", so a malicious client can't spoof an invite from
    /// somebody else. Beyond that, a member who receives a Sync that
    /// doesn't include themselves is treated as "I just got kicked"
    /// and clears local state.
    /// </summary>
    public static class PartyManager
    {
        public const string LogPrefix = "[PartyManager]";

        // RPC names
        private const string RpcRoute = "FiresRPGmaker_PartyRoute";
        private const string RpcMsg = "FiresRPGmaker_PartyMsg";

        // Sub-message types serialised as bytes inside ZPackages.
        private enum MsgType : byte
        {
            Invite = 1,
            InviteAccept = 2,
            InviteDecline = 3,
            Sync = 4,
            Disband = 5,
            Leave = 6,
        }

        public static bool VerboseLogging = false;
        private static ZRoutedRpc s_regOn;

        // Every ZNet.Awake builds a new ZRoutedRpc; bind the party RPCs on it.
        // Ascend still calls Initialize once at plugin init (now a no-op when already on this instance).
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterOnZNetAwake
        {
            [HarmonyPostfix]
            private static void Postfix() => Initialize();   // (VikingCoreCompat.Present is a constant false since FiresGuilds)
        }

        // Pending-invite tracking on the receiving side: only one
        // outstanding YesNoPopup at a time per local client. Re-invites
        // from the same inviter while a popup is already up are
        // silently dropped.
        private static long _pendingInviterPlayerId;
        private static long _pendingPartyId;

        // ----------------------------- INIT ---------------------------

        /// <summary>
        /// Register the routed RPCs. Call once after
        /// <see cref="ZRoutedRpc.instance"/> is available.
        /// </summary>
        public static void Initialize()
        {
            if (ZRoutedRpc.instance == null)
            {
                Debug.LogWarning($"{LogPrefix} ZRoutedRpc not ready, deferring registration.");
                return;
            }
            // Guard by the ZRoutedRpc instance: a leave + rejoin builds a new one, and without re-registering PartyMsg has no
            // handler and every accept is dropped before it reaches the inviter.
            if (!global::FiresRPGmaker.Managers.RpcRegistration.Needed(s_regOn)) return;
            s_regOn = ZRoutedRpc.instance;

            try
            {
                const string M = "Party";
                int n = 0;
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcRoute, () => ZRoutedRpc.instance.Register<ZPackage>(RpcRoute, new Action<long, ZPackage>(RPC_PartyRoute)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcMsg, () => ZRoutedRpc.instance.Register<ZPackage>(RpcMsg, new Action<long, ZPackage>(RPC_PartyMsg)));
                global::FiresRPGmaker.Managers.RpcRegistration.Logged(M, n);
                global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} Registered party RPCs.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} RPC registration failed: {ex.Message}");
            }
        }

        // ----------------------------- PUBLIC API ---------------------

        /// <summary>
        /// Returns the local player's current party, or null if they
        /// are not in one or the stored party belongs to a different
        /// world.
        /// </summary>
        public static PartyData GetLocalParty()
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;
            var party = PartyStorage.GetPartyOrNull(player);
            if (party == null) return null;
            if (!PartyStorage.BelongsToCurrentServer(party)) return null;
            return party;
        }

        public static bool IsLocalLeader()
        {
            var party = GetLocalParty();
            var local = Player.m_localPlayer;
            if (party == null || local == null) return false;
            return party.LeaderPlayerId == local.GetPlayerID();
        }

        /// <summary>
        /// True if the given player id is currently visible in the
        /// world's <see cref="ZNet.GetPlayerList"/>. Used by the HUD
        /// to render "(offline)" badges.
        /// </summary>
        public static bool IsPlayerOnline(long playerId)
        {
            try
            {
                var list = ZNet.instance?.GetPlayerList();
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].m_characterID.UserID == playerId)
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Lookup a live <see cref="Player"/> instance by player id, or
        /// null if no instance is currently loaded locally (peer is
        /// online but out-of-zone).
        /// </summary>
        public static Player FindLocalPlayerInstance(long playerId)
        {
            try
            {
                var all = Player.GetAllPlayers();
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p != null && p.GetPlayerID() == playerId) return p;
                }
                return null;
            }
            catch { return null; }
        }

        // ----------------------------- COMMANDS -----------------------

        public static void HandleInviteCommand(string targetName)
        {
            var local = Player.m_localPlayer;
            if (local == null)
            {
                Notify("You must be in-world to use /invite.");
                return;
            }
            if (string.IsNullOrEmpty(targetName))
            {
                Notify("Usage: /invite <playername>");
                return;
            }
            if (string.Equals(targetName, local.GetPlayerName(), StringComparison.OrdinalIgnoreCase))
            {
                Notify("You cannot invite yourself.");
                return;
            }
            if (ResolvePlayerIdByName(targetName, out long targetPlayerId, out string canonicalName) == false)
            {
                Notify($"Player '{targetName}' is not online.");
                return;
            }

            var party = PartyStorage.GetPartyOrNull(local) ?? CreateNewPartyAround(local);
            if (party.ContainsPlayer(targetPlayerId))
            {
                Notify($"{canonicalName} is already in your party.");
                return;
            }

            // Pack invite payload
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Invite);
            pkg.Write(local.GetPlayerID());
            pkg.Write(local.GetPlayerName() ?? "");
            pkg.Write(party.PartyId);
            pkg.Write(party.LeaderPlayerId);
            pkg.Write(party.Members.Count);
            foreach (var m in party.Members)
            {
                pkg.Write(m?.PlayerId ?? 0L);
                pkg.Write(m?.PlayerName ?? "");
                pkg.Write(m?.JoinedUtcTicks ?? 0L);
            }

            SendRouted(canonicalName, pkg, targetPlayerId);
            _outgoingInvites[(canonicalName ?? "").Trim().ToLowerInvariant()] = DateTime.UtcNow;   // the Accept must answer this
            Notify($"Invite sent to {canonicalName}.");

            // Persist the (possibly new) party locally so the inviter
            // appears in their own HUD even before the accept comes
            // back. The remote member shows up only on accept.
            PartyStorage.SaveParty(local, party);
        }

        public static void HandleDisbandCommand()
        {
            var local = Player.m_localPlayer;
            if (local == null) return;

            var party = GetLocalParty();
            if (party == null)
            {
                Notify("You are not in a party.");
                return;
            }
            if (party.LeaderPlayerId != local.GetPlayerID())
            {
                Notify("Only the party leader can /disband. Use /leave instead.");
                return;
            }

            BroadcastToMembers(party, BuildDisbandMsg(party.PartyId), excludeSelf: true);
            PartyStorage.ClearParty(local);
            Notify("Party disbanded.");
        }

        public static void HandleLeaveCommand()
        {
            var local = Player.m_localPlayer;
            if (local == null) return;

            var party = GetLocalParty();
            if (party == null)
            {
                Notify("You are not in a party.");
                return;
            }

            long localId = local.GetPlayerID();
            BroadcastToMembers(party, BuildLeaveMsg(localId), excludeSelf: true);
            PartyStorage.ClearParty(local);
            Notify("You left the party.");
        }

        // ----------------------------- RPC HANDLERS -------------------

        // Client -> server router. Body:
        //   string targetName   (case-insensitive, trimmed)
        //   ZPackage innerMsg   (full PartyMsg payload, opaque to us)
        private static void RPC_PartyRoute(long senderId, ZPackage pkg)
        {
            // Only the server forwards. Defence in depth: if a client
            // somehow has this registered and receives it, ignore.
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (pkg == null) return;

                string targetName = pkg.ReadString();
                var inner = pkg.ReadPackage();
                // Optional trailing target id (absent from older clients).
                long targetId = 0L;
                try { if (pkg.GetPos() + 8 <= pkg.Size()) targetId = pkg.ReadLong(); } catch { targetId = 0L; }

                if ((string.IsNullOrEmpty(targetName) && targetId == 0L) || inner == null)
                {
                    if (VerboseLogging) Debug.LogWarning($"{LogPrefix} Route from {senderId} with empty target/payload \u2014 dropping.");
                    TestLastRouteResolution = $"dropped:empty sender={senderId}";
                    return;
                }

                string how = "id";
                long targetPeerUid = targetId != 0L ? ResolvePeerUidById(targetId) : 0L;
                if (targetPeerUid == 0)
                {
                    how = "name";
                    targetPeerUid = ResolvePeerUidByName(targetName);
                }
                if (targetPeerUid == 0)
                {
                    if (VerboseLogging) global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} Route target '{targetName}' (id {targetId}) not online \u2014 dropping.");
                    TestLastRouteResolution = $"unresolved name={targetName} id={targetId}";
                    return;
                }
                TestLastRouteResolution = $"{how} name={targetName} id={targetId} peer={targetPeerUid}";
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Party] route to {targetName} resolved by {how} ({targetPeerUid})");

                // Stamp the REAL sender (server's view of the routed sender) after the inner message; the recipient
                // trusts only this stamp, never the ids/names inside the message.
                long stampId = global::FiresRPGmaker.Managers.RpcSender.SenderPlayerId(senderId);
                string stampName = global::FiresRPGmaker.Managers.RpcSender.SenderName(senderId);
                if (stampId == 0L || string.IsNullOrEmpty(stampName))
                {
                    global::FiresRPGmaker.Managers.RpcSender.Refuse(RpcRoute, senderId, "sender has no player character to stamp");
                    return;
                }
                var stamped = new ZPackage(inner.GetArray());
                stamped.SetPos(stamped.Size());
                stamped.Write(stampId);
                stamped.Write(stampName);
                ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerUid, RpcMsg, stamped);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} RPC_PartyRoute failed: {ex.Message}");
            }
        }

        // Server -> target peer. Body:
        //   byte MsgType
        //   <type-specific payload>
        private static void RPC_PartyMsg(long senderId, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                // Only the server's forward counts: it carries the stamp of the real sender.
                if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.IsFromServer(senderId)) { global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.RefuseNotServer(RpcMsg, senderId); return; }
                MsgType type = (MsgType)pkg.ReadByte();
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Party] PartyMsg {type} received from server");

                switch (type)
                {
                    case MsgType.Invite:        HandleIncomingInvite(pkg); break;
                    case MsgType.InviteAccept:  HandleIncomingAccept(pkg); break;
                    case MsgType.InviteDecline: HandleIncomingDecline(pkg); break;
                    case MsgType.Sync:          HandleIncomingSync(pkg); break;
                    case MsgType.Disband:       HandleIncomingDisband(pkg); break;
                    case MsgType.Leave:         HandleIncomingLeave(pkg); break;
                    default:
                        if (VerboseLogging) Debug.LogWarning($"{LogPrefix} Unknown MsgType {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} RPC_PartyMsg failed: {ex.Message}");
            }
        }

        /// <summary>Largest party a message may describe (allocation guard).</summary>
        private const int MaxPartyMembers = 32;
        /// <summary>How long an outgoing invite stays acceptable.</summary>
        private const double OutgoingInviteTtlSeconds = 300;
        // Lower-cased names this client invited -> when; an Accept is honoured only from one of them.
        private static readonly Dictionary<string, DateTime> _outgoingInvites = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> _refusedParty = new HashSet<string>();

        /// <summary>Reads the server's sender stamp appended after the inner message (see RPC_PartyRoute).</summary>
        private static bool TryReadStamp(ZPackage pkg, MsgType type, out long senderPlayerId, out string senderName)
        {
            senderPlayerId = 0L; senderName = null;
            try
            {
                if (pkg.GetPos() < pkg.Size()) { senderPlayerId = pkg.ReadLong(); senderName = pkg.ReadString(); }
            }
            catch { senderPlayerId = 0L; }
            if (senderPlayerId != 0L && !string.IsNullOrEmpty(senderName)) return true;
            RefuseParty(type, "no server sender stamp");
            return false;
        }

        private static void RefuseParty(MsgType type, string reason)
        {
            if (_refusedParty.Count > 1024) _refusedParty.Clear();
            if (_refusedParty.Add(type + "|" + reason)) Debug.LogWarning($"{LogPrefix} {type} refused: {reason}");
        }

        private static bool ValidMemberCount(int n, MsgType type)
        {
            if (n >= 0 && n <= MaxPartyMembers) return true;
            RefuseParty(type, $"member count {n} outside 0..{MaxPartyMembers}");
            return false;
        }

        private static void HandleIncomingInvite(ZPackage pkg)
        {
            long inviterId = pkg.ReadLong();
            string inviterName = pkg.ReadString();
            long partyId = pkg.ReadLong();
            long leaderId = pkg.ReadLong();
            int memberCount = pkg.ReadInt();
            if (!ValidMemberCount(memberCount, MsgType.Invite)) return;
            var members = new List<PartyMember>(memberCount);
            for (int i = 0; i < memberCount; i++)
            {
                var m = new PartyMember
                {
                    PlayerId = pkg.ReadLong(),
                    PlayerName = pkg.ReadString(),
                    JoinedUtcTicks = pkg.ReadLong(),
                };
                members.Add(m);
            }
            if (!TryReadStamp(pkg, MsgType.Invite, out long stampId, out string stampName)) return;
            // The inviter is whoever the server stamped, and must be in the party it offers.
            if (inviterId != stampId) { RefuseParty(MsgType.Invite, $"inviter id {inviterId} is not the stamped sender {stampId}"); return; }
            inviterName = stampName;
            bool inviterListed = false;
            foreach (var m in members) if (m != null && m.PlayerId == stampId) { inviterListed = true; break; }
            if (!inviterListed) { RefuseParty(MsgType.Invite, "the inviter is not in the offered party"); return; }

            var local = Player.m_localPlayer;
            if (local == null) return;

            // Already in a party? Reject with an auto-decline so the
            // inviter sees a clear "they're busy" rather than a silent
            // timeout.
            var existing = GetLocalParty();
            if (existing != null && existing.PartyId != partyId)
            {
                SendDeclineTo(inviterName, local.GetPlayerID(), local.GetPlayerName(), reason: "already in a party", targetId: inviterId);
                return;
            }

            // Suppress duplicate popups while one is up.
            if (_pendingInviterPlayerId != 0 && _pendingInviterPlayerId == inviterId)
                return;
            _pendingInviterPlayerId = inviterId;
            _pendingPartyId = partyId;

            string header = "Party Invite";
            string text = $"<b>{inviterName}</b> has invited you to join their party.\n\nAccept?";
            try
            {
                UnifiedPopup.Push(new YesNoPopup(header, text,
                    yesCallback: () => OnInviteYes(inviterId, inviterName, partyId, leaderId, members),
                    noCallback: () => OnInviteNo(inviterId, inviterName),
                    localizeText: false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Failed to show invite popup: {ex.Message}");
                _pendingInviterPlayerId = 0;
                _pendingPartyId = 0;
            }
        }

        private static void OnInviteYes(long inviterId, string inviterName, long partyId, long leaderId, List<PartyMember> remoteMembers)
        {
            _pendingInviterPlayerId = 0;
            _pendingPartyId = 0;

            var local = Player.m_localPlayer;
            if (local == null) return;

            // Build the accepted party = remoteMembers + self.
            var party = new PartyData
            {
                PartyId = partyId,
                ServerWorldUid = PartyStorage.GetCurrentServerWorldUid(),
                LeaderPlayerId = leaderId,
                Members = new List<PartyMember>(remoteMembers ?? new List<PartyMember>()),
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks,
            };

            long selfId = local.GetPlayerID();
            string selfName = local.GetPlayerName() ?? "?";

            if (!party.ContainsPlayer(selfId))
            {
                party.Members.Add(new PartyMember
                {
                    PlayerId = selfId,
                    PlayerName = selfName,
                    JoinedUtcTicks = DateTime.UtcNow.Ticks,
                });
            }

            PartyStorage.SaveParty(local, party);

            // Notify inviter.
            var ack = new ZPackage();
            ack.Write((byte)MsgType.InviteAccept);
            ack.Write(selfId);
            ack.Write(selfName);
            SendRouted(inviterName, ack, inviterId);

            Notify($"You joined {inviterName}'s party.");
        }

        private static void OnInviteNo(long inviterId, string inviterName)
        {
            _pendingInviterPlayerId = 0;
            _pendingPartyId = 0;

            var local = Player.m_localPlayer;
            if (local == null) return;
            SendDeclineTo(inviterName, local.GetPlayerID(), local.GetPlayerName(), reason: "declined", targetId: inviterId);
        }

        private static void HandleIncomingAccept(ZPackage pkg)
        {
            long acceptedById = pkg.ReadLong();
            string acceptedByName = pkg.ReadString();
            if (!TryReadStamp(pkg, MsgType.InviteAccept, out long stampId, out string stampName)) return;
            // Only the stamped sender can accept for itself, and only an invite this client sent.
            if (acceptedById != stampId) { RefuseParty(MsgType.InviteAccept, $"accepter id {acceptedById} is not the stamped sender {stampId}"); return; }
            acceptedByName = stampName;
            string inviteKey = stampName.Trim().ToLowerInvariant();
            if (!_outgoingInvites.TryGetValue(inviteKey, out DateTime sentAt) || (DateTime.UtcNow - sentAt).TotalSeconds > OutgoingInviteTtlSeconds)
            { RefuseParty(MsgType.InviteAccept, $"no open invite to '{stampName}'"); return; }
            _outgoingInvites.Remove(inviteKey);

            var local = Player.m_localPlayer;
            if (local == null) return;

            var party = PartyStorage.GetPartyOrNull(local) ?? CreateNewPartyAround(local);
            if (!party.ContainsPlayer(acceptedById))
            {
                party.Members.Add(new PartyMember
                {
                    PlayerId = acceptedById,
                    PlayerName = acceptedByName,
                    JoinedUtcTicks = DateTime.UtcNow.Ticks,
                });
            }
            PartyStorage.SaveParty(local, party);

            // Sync the new state to every member except the accepter
            // (they already have it because they just accepted with
            // self in the list) and except self.
            BroadcastSyncToMembers(party, excludePlayerIds: new HashSet<long> { acceptedById, local.GetPlayerID() });

            Notify($"{acceptedByName} joined your party.");
        }

        private static void HandleIncomingDecline(ZPackage pkg)
        {
            long declinerId = pkg.ReadLong();
            string declinerName = pkg.ReadString();
            string reason = pkg.ReadString();
            if (!TryReadStamp(pkg, MsgType.InviteDecline, out _, out string stampName)) return;
            declinerName = stampName;
            if (reason != null && reason.Length > 64) reason = reason.Substring(0, 64);
            Notify($"{declinerName} declined the invite{(string.IsNullOrEmpty(reason) ? "" : $" ({reason})")}.");
        }

        private static void HandleIncomingSync(ZPackage pkg)
        {
            long partyId = pkg.ReadLong();
            long leaderId = pkg.ReadLong();
            int memberCount = pkg.ReadInt();
            if (!ValidMemberCount(memberCount, MsgType.Sync)) return;
            var members = new List<PartyMember>(memberCount);
            for (int i = 0; i < memberCount; i++)
            {
                members.Add(new PartyMember
                {
                    PlayerId = pkg.ReadLong(),
                    PlayerName = pkg.ReadString(),
                    JoinedUtcTicks = pkg.ReadLong(),
                });
            }
            if (!TryReadStamp(pkg, MsgType.Sync, out long stampId, out _)) return;

            var local = Player.m_localPlayer;
            if (local == null) return;
            long selfId = local.GetPlayerID();

            // A sync is only from a member of MY current party (same party id); changing the leader or
            // removing me (a kick) is the current leader's right only.
            var current = PartyStorage.GetPartyOrNull(local);
            if (current == null || current.PartyId != partyId) { RefuseParty(MsgType.Sync, "sync for a party this player is not in"); return; }
            if (!current.ContainsPlayer(stampId)) { RefuseParty(MsgType.Sync, $"sender {stampId} is not a member"); return; }
            if (leaderId != current.LeaderPlayerId && stampId != current.LeaderPlayerId) { RefuseParty(MsgType.Sync, "only the leader may change the leader"); return; }
            bool includesSelf = false;
            foreach (var m in members) if (m != null && m.PlayerId == selfId) { includesSelf = true; break; }
            if (!includesSelf && stampId != current.LeaderPlayerId) { RefuseParty(MsgType.Sync, "only the leader may remove a member"); return; }

            // If the incoming sync doesn't include us, treat it as a
            // remote kick / leader-disband-equivalent and clear state.
            bool stillIncluded = false;
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null && members[i].PlayerId == selfId) { stillIncluded = true; break; }

            if (!stillIncluded)
            {
                PartyStorage.ClearParty(local);
                Notify("You were removed from the party.");
                return;
            }

            var party = new PartyData
            {
                PartyId = partyId,
                ServerWorldUid = PartyStorage.GetCurrentServerWorldUid(),
                LeaderPlayerId = leaderId,
                Members = members,
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks,
            };
            PartyStorage.SaveParty(local, party);
        }

        private static void HandleIncomingDisband(ZPackage pkg)
        {
            long partyId = pkg.ReadLong();
            if (!TryReadStamp(pkg, MsgType.Disband, out long stampId, out _)) return;
            var local = Player.m_localPlayer;
            if (local == null) return;
            var party = PartyStorage.GetPartyOrNull(local);
            if (party == null || party.PartyId != partyId) return;
            if (stampId != party.LeaderPlayerId) { RefuseParty(MsgType.Disband, $"sender {stampId} is not the leader"); return; }
            PartyStorage.ClearParty(local);
            Notify("Your party was disbanded by the leader.");
        }

        private static void HandleIncomingLeave(ZPackage pkg)
        {
            long leaverId = pkg.ReadLong();
            if (!TryReadStamp(pkg, MsgType.Leave, out long stampId, out _)) return;
            // A player can only announce its OWN leave.
            if (leaverId != stampId) { RefuseParty(MsgType.Leave, $"leaver id {leaverId} is not the stamped sender {stampId}"); return; }
            var local = Player.m_localPlayer;
            if (local == null) return;
            var party = PartyStorage.GetPartyOrNull(local);
            if (party == null) return;

            var member = party.FindMember(leaverId);
            if (member == null) return;

            string leaverName = member.PlayerName ?? "Someone";
            party.Members.RemoveAll(m => m == null || m.PlayerId == leaverId);

            // Promote a new leader if needed: oldest by JoinedUtcTicks.
            if (party.LeaderPlayerId == leaverId && party.Members.Count > 0)
            {
                PartyMember oldest = null;
                foreach (var m in party.Members)
                {
                    if (m == null) continue;
                    if (oldest == null || m.JoinedUtcTicks < oldest.JoinedUtcTicks) oldest = m;
                }
                if (oldest != null) party.LeaderPlayerId = oldest.PlayerId;
            }

            // If the local player is left alone, clear instead of
            // saving a one-member "party of self".
            if (party.Members.Count <= 1)
            {
                PartyStorage.ClearParty(local);
                Notify($"{leaverName} left the party. The party has been dissolved.");
                return;
            }

            PartyStorage.SaveParty(local, party);
            Notify($"{leaverName} left the party.");
        }

        // ----------------------------- INTERNAL -----------------------

        private static PartyData CreateNewPartyAround(Player local)
        {
            return new PartyData
            {
                PartyId = GeneratePartyId(),
                ServerWorldUid = PartyStorage.GetCurrentServerWorldUid(),
                LeaderPlayerId = local.GetPlayerID(),
                Members = new List<PartyMember>
                {
                    new PartyMember
                    {
                        PlayerId = local.GetPlayerID(),
                        PlayerName = local.GetPlayerName() ?? "?",
                        JoinedUtcTicks = DateTime.UtcNow.Ticks,
                    }
                },
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks,
            };
        }

        private static long GeneratePartyId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return BitConverter.ToInt64(bytes, 0);
        }

        private static void SendDeclineTo(string inviterName, long selfId, string selfName, string reason, long targetId = 0L)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.InviteDecline);
            pkg.Write(selfId);
            pkg.Write(selfName ?? "");
            pkg.Write(reason ?? "");
            SendRouted(inviterName, pkg, targetId);
        }

        private static ZPackage BuildSyncMsg(PartyData party)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Sync);
            pkg.Write(party.PartyId);
            pkg.Write(party.LeaderPlayerId);
            pkg.Write(party.Members.Count);
            foreach (var m in party.Members)
            {
                pkg.Write(m?.PlayerId ?? 0L);
                pkg.Write(m?.PlayerName ?? "");
                pkg.Write(m?.JoinedUtcTicks ?? 0L);
            }
            return pkg;
        }

        private static ZPackage BuildDisbandMsg(long partyId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Disband);
            pkg.Write(partyId);
            return pkg;
        }

        private static ZPackage BuildLeaveMsg(long leaverId)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.Leave);
            pkg.Write(leaverId);
            return pkg;
        }

        private static void BroadcastSyncToMembers(PartyData party, HashSet<long> excludePlayerIds = null)
        {
            var msg = BuildSyncMsg(party);
            foreach (var m in party.Members)
            {
                if (m == null) continue;
                if (excludePlayerIds != null && excludePlayerIds.Contains(m.PlayerId)) continue;
                if (string.IsNullOrEmpty(m.PlayerName) && m.PlayerId == 0L) continue;
                SendRouted(m.PlayerName, msg, m.PlayerId);
            }
        }

        private static void BroadcastToMembers(PartyData party, ZPackage msg, bool excludeSelf)
        {
            long selfId = Player.m_localPlayer?.GetPlayerID() ?? 0L;
            foreach (var m in party.Members)
            {
                if (m == null) continue;
                if (excludeSelf && m.PlayerId == selfId) continue;
                if (string.IsNullOrEmpty(m.PlayerName) && m.PlayerId == 0L) continue;
                SendRouted(m.PlayerName, msg, m.PlayerId);
            }
        }

        /// <summary>
        /// Push the inner payload to the server as a Route message; the
        /// server forwards it to the named target's peer.
        /// </summary>
        private static void SendRouted(string targetName, ZPackage inner, long targetId = 0L)
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(targetName ?? "");
                pkg.Write(inner ?? new ZPackage());
                // Add-only trailing field - the target's player id / character UserID / peer uid.
                // An older server reads name + inner and ignores it; a newer server reads it only when bytes remain.
                pkg.Write(targetId);
                ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcRoute), RpcRoute, pkg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} SendRouted to {targetName} failed: {ex.Message}");
            }
        }

        private static bool ResolvePlayerIdByName(string name, out long playerId, out string canonicalName)
        {
            playerId = 0; canonicalName = name;
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                var list = ZNet.instance?.GetPlayerList();
                if (list == null) return false;
                string lower = name.Trim().ToLowerInvariant();
                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];
                    if (info.m_name == null) continue;
                    if (info.m_name.Trim().ToLowerInvariant() == lower)
                    {
                        playerId = info.m_characterID.UserID;
                        canonicalName = info.m_name;
                        return playerId != 0;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>Test hook - the server's last PartyRoute resolution
        /// ("id|name name=.. id=.. peer=..", "unresolved ...", "dropped:empty ...").</summary>
        public static string TestLastRouteResolution = "";

        /// <summary>
        /// Server-side resolve an id to a peer uid. The id may be a peer uid, a character
        /// ZDOID UserID (what ZNet.GetPlayerList gives the inviter) or a Player.GetPlayerID() (party member ids).
        /// </summary>
        private static long ResolvePeerUidById(long id)
        {
            try
            {
                if (id == 0L || ZNet.instance == null) return 0L;
                var peers = ZNet.instance.GetPeers();
                if (peers != null)
                {
                    for (int i = 0; i < peers.Count; i++)
                    {
                        var p = peers[i];
                        if (p == null) continue;
                        if (p.m_uid == id || p.m_characterID.UserID == id) return p.m_uid;
                    }
                }
                long self = ZRoutedRpc.instance != null ? ZRoutedRpc.instance.GetServerPeerID() : 0L;
                if (ZNet.instance.IsServer() && self != 0L && self == id) return self;
                var all = Player.GetAllPlayers();
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var pl = all[i];
                        if (pl == null || pl.GetPlayerID() != id) continue;
                        if (ZNet.instance.IsServer() && pl == Player.m_localPlayer) return self;   // listen host's own player
                        var zdo = pl.GetComponent<ZNetView>()?.GetZDO();
                        long owner = zdo != null ? zdo.GetOwner() : 0L;
                        if (owner != 0L) return owner;
                    }
                }
                return 0L;
            }
            catch { return 0L; }
        }

        /// <summary>
        /// Server-side: resolve a player NAME to the network <c>m_uid</c>
        /// of the connected peer hosting that player.
        /// </summary>
        private static long ResolvePeerUidByName(string name)
        {
            try
            {
                if (ZNet.instance == null) return 0;
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogWarning($"{LogPrefix} [Party] route by name: empty name and no id — cannot resolve.");
                    return 0;
                }
                string lower = name.Trim().ToLowerInvariant();

                // Count matches so a duplicate name is reported, not silently first-wins.
                long found = 0; int matches = 0; int emptyNames = 0;
                var peers = ZNet.instance.GetPeers();
                if (peers != null)
                {
                    for (int i = 0; i < peers.Count; i++)
                    {
                        var p = peers[i];
                        if (p == null) continue;
                        if (string.IsNullOrEmpty(p.m_playerName)) { emptyNames++; continue; }
                        if (p.m_playerName.Trim().ToLowerInvariant() == lower)
                        {
                            if (matches == 0) found = p.m_uid;
                            matches++;
                        }
                    }
                }
                if (matches > 1)
                    Debug.LogWarning($"{LogPrefix} [Party] route by name '{name}' is ambiguous ({matches} peers) — using peer {found}.");
                if (matches > 0) return found;
                if (emptyNames > 0)
                    Debug.LogWarning($"{LogPrefix} [Party] route by name '{name}': no match, {emptyNames} peer(s) have no name yet (just joined?).");

                // Server's own local player (the host on a listen-server)
                // never appears in the peers list. Detect this case.
                if (ZNet.instance.IsServer())
                {
                    var localOnHost = Player.m_localPlayer;
                    if (localOnHost != null && string.Equals(localOnHost.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Route to ourselves: 0L would mean "send to
                        // server", which IS us; ZRoutedRpc handles a
                        // self-targeted RPC by invoking the local
                        // handler directly.
                        return ZRoutedRpc.instance != null ? ZRoutedRpc.instance.GetServerPeerID() : 0L;
                    }
                }

                return 0;
            }
            catch { return 0; }
        }

        private static void Notify(string text)
        {
            try
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);
            }
            catch { }
            global::FiresRPGmaker.FiresLogger.LogDebug($"{LogPrefix} {text}");
        }

        // ----------------------------- CONSOLE COMMANDS ---------------

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPriority(Priority.Low)]
        public static class Patch_Terminal_InitTerminal
        {
            private static bool _added;

            static void Postfix()
            {
                if (_added) return;
                _added = true;

                new Terminal.ConsoleCommand("invite",
                    "Invite a player to your party. Usage: /invite <playername>",
                    args =>
                    {
                        if (args.Length < 2) { args.Context.AddString("Usage: /invite <playername>"); return; }
                        // Re-join trailing words into the name (Valheim names can have spaces).
                        string name = string.Join(" ", args.Args, 1, args.Length - 1);
                        HandleInviteCommand(name);
                    },
                    isCheat: false, isNetwork: false, onlyServer: false);

                new Terminal.ConsoleCommand("disband",
                    "Disband your party (leader only).",
                    args => HandleDisbandCommand(),
                    isCheat: false, isNetwork: false, onlyServer: false);

                new Terminal.ConsoleCommand("leave",
                    "Leave your current party.",
                    args => HandleLeaveCommand(),
                    isCheat: false, isNetwork: false, onlyServer: false);
            }
        }
    }
}
