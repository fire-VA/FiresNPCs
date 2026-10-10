using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using FiresCore.Lifecycle;

namespace FiresRPGmaker.Modules.Party
{
    /// <summary>
    /// Per-player party / friend-group state. Persisted on
    /// <see cref="Player.m_customData"/> under
    /// <see cref="PartyStorage.CustomDataKey"/> as JSON, so it travels
    /// with the character file (.fch) and survives logout / crash the
    /// same way the companion roster does (Phase 3 of the companion
    /// save refactor).
    ///
    /// DESIGN
    /// ------
    /// Parties have no central authority. Every member keeps a local
    /// copy and trusts it. Membership changes (invite-accept, disband,
    /// leave) propagate via <c>PartyMsg.Sync</c> to all currently-online
    /// members. Offline members reconcile on their next login by
    /// receiving the next Sync from any online peer (or by remaining on
    /// their last-known state if nobody updates them \u2014 acceptable
    /// for a friend-group system; if they actively try to act on it the
    /// remote side will reject inconsistent state).
    ///
    /// SERVER BINDING
    /// --------------
    /// <see cref="ServerWorldUid"/> is stamped at creation so a party
    /// formed on world A doesn't suddenly appear when the same character
    /// joins world B. Mirrors the companion-roster server-binding rule.
    /// </summary>
    [Serializable]
    public class PartyData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>
        /// Stable identifier for this party. Generated when the very
        /// first invite is accepted and never changes for the lifetime
        /// of the group. Used to discriminate between merge attempts
        /// (two existing parties trying to invite each other) and
        /// straight invitations.
        /// </summary>
        public long PartyId;

        /// <summary>
        /// World UID this party belongs to. Cross-world roaming
        /// characters keep their party data per world; <c>0</c> means
        /// "unknown" and is permissive (matches the companion-roster
        /// convention).
        /// </summary>
        public long ServerWorldUid;

        /// <summary>
        /// Player-id of the leader. Only the leader may
        /// <c>/disband</c>. On leader-leaves, leadership transfers to
        /// the longest-tenured remaining member.
        /// </summary>
        public long LeaderPlayerId;

        public List<PartyMember> Members = new List<PartyMember>();

        /// <summary>
        /// UTC ticks of the last write. Used to break ties when two
        /// members deliver conflicting Sync messages on login.
        /// </summary>
        public long LastUpdatedUtcTicks;

        public bool ContainsPlayer(long playerId)
        {
            if (Members == null) return false;
            for (int i = 0; i < Members.Count; i++)
                if (Members[i] != null && Members[i].PlayerId == playerId)
                    return true;
            return false;
        }

        public PartyMember FindMember(long playerId)
        {
            if (Members == null) return null;
            for (int i = 0; i < Members.Count; i++)
                if (Members[i] != null && Members[i].PlayerId == playerId)
                    return Members[i];
            return null;
        }
    }

    [Serializable]
    public class PartyMember
    {
        public long PlayerId;
        public string PlayerName;
        public long JoinedUtcTicks;
    }

    /// <summary>
    /// Read / write helpers for the per-player party blob on
    /// <see cref="Player.m_customData"/>. Mirrors the API surface of
    /// <c>PlayerCompanionStorage</c> for the companion roster so the
    /// two systems read identically when reviewing.
    /// </summary>
    public static class PartyStorage
    {
        public const string CustomDataKey = "fires.va.party.v1";
        private const string LogPrefix = "[PartyStorage]";

        public static bool VerboseLogging = false;

        // ------------------------------ READ --------------------------

        public static bool TryGetParty(Player player, out PartyData party)
        {
            party = null;
            if (player == null || player.m_customData == null) return false;
            if (!player.m_customData.TryGetValue(CustomDataKey, out string json) || string.IsNullOrEmpty(json))
                return false;

            try
            {
                party = JsonConvert.DeserializeObject<PartyData>(json);
                if (party == null) return false;
                if (party.Members == null) party.Members = new List<PartyMember>();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Failed to parse party JSON for {SafePlayerName(player)}: {ex.Message}");
                return false;
            }
        }

        public static PartyData GetPartyOrNull(Player player)
        {
            return TryGetParty(player, out var p) ? p : null;
        }

        // ------------------------------ WRITE -------------------------

        public static bool SaveParty(Player player, PartyData party)
        {
            if (player == null || party == null) return false;
            party.SchemaVersion = PartyData.CurrentSchemaVersion;
            party.LastUpdatedUtcTicks = DateTime.UtcNow.Ticks;
            if (party.Members == null) party.Members = new List<PartyMember>();

            string json;
            try
            {
                json = JsonConvert.SerializeObject(party);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Serialise failed for {SafePlayerName(player)}: {ex.Message}");
                return false;
            }

            // Defer the write through the spawn gate so we don't mutate m_customData
            // during the player's respawn / teleport teardown window. Return value
            // reflects serialise success only - the actual write may run a frame later.
            string capturedJson = json;
            int memberCount = party.Members.Count;
            string playerName = SafePlayerName(player);
            PlayerSpawnGate.RunWhenReady(player, 30f, p =>
            {
                try
                {
                    if (p.m_customData == null) return;
                    p.m_customData[CustomDataKey] = capturedJson;
                    if (VerboseLogging)
                        Debug.Log($"{LogPrefix} Wrote party ({memberCount} members, {capturedJson.Length} bytes) for {playerName}.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix} m_customData write failed for {playerName}: {ex.Message}");
                }
            });
            return true;
        }

        public static bool ClearParty(Player player)
        {
            if (player == null || player.m_customData == null) return false;
            try
            {
                if (player.m_customData.ContainsKey(CustomDataKey))
                {
                    player.m_customData.Remove(CustomDataKey);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Clear failed: {ex.Message}");
                return false;
            }
        }

        // ------------------------------ HELPERS -----------------------

        public static long GetCurrentServerWorldUid()
        {
            try
            {
                var znet = ZNet.instance;
                return znet == null ? 0 : znet.GetWorldUID();
            }
            catch { return 0; }
        }

        public static bool BelongsToCurrentServer(PartyData party)
        {
            if (party == null) return false;
            long current = GetCurrentServerWorldUid();
            if (current == 0 || party.ServerWorldUid == 0) return true;
            return party.ServerWorldUid == current;
        }

        private static string SafePlayerName(Player p)
        {
            try { return p?.GetPlayerName() ?? "?"; } catch { return "?"; }
        }
    }
}
