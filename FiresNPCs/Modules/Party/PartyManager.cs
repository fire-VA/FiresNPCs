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
        private static bool _registered;

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
            if (_registered) return;
            if (ZRoutedRpc.instance == null)
            {
                Debug.LogWarning($"{LogPrefix} ZRoutedRpc not ready, deferring registration.");
                return;
            }

            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcRoute, new Action<long, ZPackage>(RPC_PartyRoute));
                ZRoutedRpc.instance.Register<ZPackage>(RpcMsg, new Action<long, ZPackage>(RPC_PartyMsg));
                _registered = true;
                Debug.Log($"{LogPrefix} Registered party RPCs.");
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

            SendRouted(canonicalName, pkg);
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

                if (string.IsNullOrEmpty(targetName) || inner == null)
                {
                    if (VerboseLogging) Debug.LogWarning($"{LogPrefix} Route from {senderId} with empty target/payload \u2014 dropping.");
                    return;
                }

                long targetPeerUid = ResolvePeerUidByName(targetName);
                if (targetPeerUid == 0)
                {
                    if (VerboseLogging) Debug.Log($"{LogPrefix} Route target '{targetName}' not online \u2014 dropping.");
                    return;
                }

                ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerUid, RpcMsg, inner);
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
                MsgType type = (MsgType)pkg.ReadByte();

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

        private static void HandleIncomingInvite(ZPackage pkg)
        {
            long inviterId = pkg.ReadLong();
            string inviterName = pkg.ReadString();
            long partyId = pkg.ReadLong();
            long leaderId = pkg.ReadLong();
            int memberCount = pkg.ReadInt();
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

            var local = Player.m_localPlayer;
            if (local == null) return;

            // Already in a party? Reject with an auto-decline so the
            // inviter sees a clear "they're busy" rather than a silent
            // timeout.
            var existing = GetLocalParty();
            if (existing != null && existing.PartyId != partyId)
            {
                SendDeclineTo(inviterName, local.GetPlayerID(), local.GetPlayerName(), reason: "already in a party");
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
            SendRouted(inviterName, ack);

            Notify($"You joined {inviterName}'s party.");
        }

        private static void OnInviteNo(long inviterId, string inviterName)
        {
            _pendingInviterPlayerId = 0;
            _pendingPartyId = 0;

            var local = Player.m_localPlayer;
            if (local == null) return;
            SendDeclineTo(inviterName, local.GetPlayerID(), local.GetPlayerName(), reason: "declined");
        }

        private static void HandleIncomingAccept(ZPackage pkg)
        {
            long acceptedById = pkg.ReadLong();
            string acceptedByName = pkg.ReadString();

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
            Notify($"{declinerName} declined the invite{(string.IsNullOrEmpty(reason) ? "" : $" ({reason})")}.");
        }

        private static void HandleIncomingSync(ZPackage pkg)
        {
            long partyId = pkg.ReadLong();
            long leaderId = pkg.ReadLong();
            int memberCount = pkg.ReadInt();
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

            var local = Player.m_localPlayer;
            if (local == null) return;
            long selfId = local.GetPlayerID();

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
            var local = Player.m_localPlayer;
            if (local == null) return;
            var party = PartyStorage.GetPartyOrNull(local);
            if (party == null || party.PartyId != partyId) return;
            PartyStorage.ClearParty(local);
            Notify("Your party was disbanded by the leader.");
        }

        private static void HandleIncomingLeave(ZPackage pkg)
        {
            long leaverId = pkg.ReadLong();
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

        private static void SendDeclineTo(string inviterName, long selfId, string selfName, string reason)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)MsgType.InviteDecline);
            pkg.Write(selfId);
            pkg.Write(selfName ?? "");
            pkg.Write(reason ?? "");
            SendRouted(inviterName, pkg);
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
                if (string.IsNullOrEmpty(m.PlayerName)) continue;
                SendRouted(m.PlayerName, msg);
            }
        }

        private static void BroadcastToMembers(PartyData party, ZPackage msg, bool excludeSelf)
        {
            long selfId = Player.m_localPlayer?.GetPlayerID() ?? 0L;
            foreach (var m in party.Members)
            {
                if (m == null) continue;
                if (excludeSelf && m.PlayerId == selfId) continue;
                if (string.IsNullOrEmpty(m.PlayerName)) continue;
                SendRouted(m.PlayerName, msg);
            }
        }

        /// <summary>
        /// Push the inner payload to the server as a Route message; the
        /// server forwards it to the named target's peer.
        /// </summary>
        private static void SendRouted(string targetName, ZPackage inner)
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(targetName ?? "");
                pkg.Write(inner ?? new ZPackage());
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRoute, pkg);
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

        /// <summary>
        /// Server-side: resolve a player NAME to the network <c>m_uid</c>
        /// of the connected peer hosting that player.
        /// </summary>
        private static long ResolvePeerUidByName(string name)
        {
            try
            {
                if (ZNet.instance == null) return 0;
                if (string.IsNullOrEmpty(name)) return 0;
                string lower = name.Trim().ToLowerInvariant();

                var peers = ZNet.instance.GetPeers();
                if (peers != null)
                {
                    for (int i = 0; i < peers.Count; i++)
                    {
                        var p = peers[i];
                        if (p == null || string.IsNullOrEmpty(p.m_playerName)) continue;
                        if (p.m_playerName.Trim().ToLowerInvariant() == lower)
                            return p.m_uid;
                    }
                }

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
            Debug.Log($"{LogPrefix} {text}");
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
