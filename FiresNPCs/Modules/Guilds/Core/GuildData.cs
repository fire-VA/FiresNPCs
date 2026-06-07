using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Guilds.Core
{
    [Serializable]
    public class GuildData
    {
        public string GuildId;
        public string Name;
        public string Description;

        // Color stored as RGBA bytes so Newtonsoft.Json round-trips cleanly.
        public byte ColorR = 200, ColorG = 170, ColorB = 80, ColorA = 255;

        public long LeaderPlayerId;

        // When true: any player can apply without needing an invite.
        public bool IsOpen;

        public long CreatedUtcTicks;

        public List<GuildMember>      Members      = new List<GuildMember>();
        public List<GuildApplication> Applications = new List<GuildApplication>();

        // ── Companion access ──────────────────────────────────────────────────

        // ZDOIDs (as strings "m_id:m_userID") of companions whose owners have
        // toggled guild-access ON.  Stored per-guild so guild members can verify
        // access without having the companion in-zone.
        public List<string> GuildAccessCompanionIds = new List<string>();

        // ── Colour property ───────────────────────────────────────────────────

        [Newtonsoft.Json.JsonIgnore]
        public Color GuildColor
        {
            get => new Color32(ColorR, ColorG, ColorB, ColorA);
            set { var c = (Color32)value; ColorR = c.r; ColorG = c.g; ColorB = c.b; ColorA = c.a; }
        }

        // ── Member helpers ────────────────────────────────────────────────────

        public GuildMember GetMember(long playerId)
        {
            for (int i = 0; i < Members.Count; i++)
                if (Members[i].PlayerId == playerId) return Members[i];
            return null;
        }

        public bool IsMember(long playerId) => GetMember(playerId) != null;

        public GuildRank GetRank(long playerId) => GetMember(playerId)?.Rank ?? GuildRank.Member;

        // ── Rank-gated permission helpers ─────────────────────────────────────

        public bool CanKick(long actorId, long targetId)
        {
            var actor  = GetMember(actorId);
            var target = GetMember(targetId);
            if (actor == null || target == null || actorId == targetId) return false;
            return actor.Rank > target.Rank;
        }

        public bool CanPromote(long actorId, long targetId)
        {
            var actor  = GetMember(actorId);
            var target = GetMember(targetId);
            if (actor == null || target == null || actorId == targetId) return false;
            var promoted = (GuildRank)((int)target.Rank + 1);
            // Can only promote someone to a rank strictly below your own.
            return actor.Rank > promoted;
        }

        public bool CanDemote(long actorId, long targetId)
        {
            var actor  = GetMember(actorId);
            var target = GetMember(targetId);
            if (actor == null || target == null || actorId == targetId) return false;
            return actor.Rank > target.Rank && target.Rank > GuildRank.Member;
        }

        public bool CanManageApplications(long actorId)
        {
            var m = GetMember(actorId);
            return m != null && m.IsOfficer;
        }

        public bool CanEditGuild(long actorId)
        {
            var m = GetMember(actorId);
            return m != null && m.IsColeader;
        }

        public bool IsLeader(long playerId) => LeaderPlayerId == playerId;

        // ── Application helpers ───────────────────────────────────────────────

        public GuildApplication GetApplication(long playerId)
        {
            for (int i = 0; i < Applications.Count; i++)
                if (Applications[i].PlayerId == playerId) return Applications[i];
            return null;
        }

        public bool HasApplied(long playerId) => GetApplication(playerId) != null;
    }
}
