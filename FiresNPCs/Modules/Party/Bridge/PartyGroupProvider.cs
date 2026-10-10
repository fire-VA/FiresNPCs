using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Party.Bridge
{
    /// <summary>
    /// The single <see cref="IGroupProvider"/> FiresRPGmaker registers with the Core <see cref="GroupBridge"/>,
    /// so consumers (ability ring visibility, companion allegiance, territory/PvP) resolve party state without
    /// referencing FiresRPGmaker directly.
    ///
    /// An external social mod answers only while it has actually wired a group resolver into
    /// <see cref="RpgMakerApi"/>; otherwise the local player's <see cref="PartyManager"/> party answers. The party
    /// layer is client-local (state lives on each member's own <c>m_customData</c>), so membership questions about
    /// players outside the local player's party are unanswerable and report as "not grouped".
    /// </summary>
    public sealed class PartyGroupProvider : IGroupProvider
    {
        private const string LogPrefix = "[PartyGroupProvider]";

        private static bool _reportedExternalRosterGap;
        private static bool _reportedUnwiredExternalOwner;

        public bool AreGrouped(long playerA, long playerB)
        {
            if (ExternalResolverWired) return RpgMakerApi.AreGrouped(playerA, playerB);
            ReportUnwiredExternalOwnerOnce();

            var party = PartyManager.GetLocalParty();
            return party != null && party.ContainsPlayer(playerA) && party.ContainsPlayer(playerB);
        }

        public IReadOnlyList<long> GetGroupMembers(long playerId)
        {
            var party = LocalPartyContaining(playerId);
            if (party == null) return Array.Empty<long>();

            var ids = new List<long>(party.Members.Count);
            for (int i = 0; i < party.Members.Count; i++)
            {
                var member = party.Members[i];
                if (member != null && member.PlayerId != 0L) ids.Add(member.PlayerId);
            }
            return ids;
        }

        public long GetGroupLeader(long playerId)
        {
            return LocalPartyContaining(playerId)?.LeaderPlayerId ?? 0L;
        }

        public bool HasGroup(long playerId)
        {
            return LocalPartyContaining(playerId) != null;
        }

        private static bool ExternalResolverWired => RpgMakerApi.GroupsOwnedExternally;

        private static bool ExternalOwnerClaimsGroups => Modules.Compat.VikingCoreCompat.Present;

        private static PartyData LocalPartyContaining(long playerId)
        {
            if (ExternalResolverWired)
            {
                ReportExternalRosterGapOnce();
                return null;
            }
            ReportUnwiredExternalOwnerOnce();
            if (playerId == 0L) return null;

            var party = PartyManager.GetLocalParty();
            return party != null && party.ContainsPlayer(playerId) ? party : null;
        }

        private static void ReportExternalRosterGapOnce()
        {
            if (_reportedExternalRosterGap) return;
            _reportedExternalRosterGap = true;
            Debug.LogWarning($"{LogPrefix} An external mod owns groups through RpgMakerApi, which exposes only " +
                             "AreGrouped - no member roster or leader. GroupBridge.GetGroupMembers/GetGroupLeader/" +
                             "HasGroup answer empty for the whole session. Add roster + leader resolvers to " +
                             "RpgMakerApi and register them from the owning mod to close this.");
        }

        private static void ReportUnwiredExternalOwnerOnce()
        {
            if (!ExternalOwnerClaimsGroups) return;
            if (_reportedUnwiredExternalOwner) return;
            if (Player.m_localPlayer == null) return;
            _reportedUnwiredExternalOwner = true;
            Debug.LogError($"{LogPrefix} VikingLands.Core is loaded and owns groups, but no group resolver was " +
                           "wired into RpgMakerApi, so every external party query would answer \"not grouped\". " +
                           "Answering from the local PartyManager party instead. Check the SetGroupResolver " +
                           "registration in FiresRpgMakerSocialInterop.");
        }
    }
}
