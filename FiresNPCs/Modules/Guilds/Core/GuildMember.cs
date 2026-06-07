using System;

namespace FiresRPGmaker.Modules.Guilds.Core
{
    [Serializable]
    public class GuildMember
    {
        public long      PlayerId;
        public string    PlayerName;
        public GuildRank Rank;
        public long      JoinedUtcTicks;
        public long      LastOnlineUtcTicks;

        public bool IsLeader   => Rank == GuildRank.Leader;
        public bool IsColeader => Rank >= GuildRank.Coleader;
        public bool IsOfficer  => Rank >= GuildRank.Officer;

        public string RankLabel() => Rank switch
        {
            GuildRank.Leader   => "Leader",
            GuildRank.Coleader => "Co-leader",
            GuildRank.Officer  => "Officer",
            _                  => "Member",
        };
    }
}
