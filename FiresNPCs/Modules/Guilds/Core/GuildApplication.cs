using System;

namespace FiresRPGmaker.Modules.Guilds.Core
{
    [Serializable]
    public class GuildApplication
    {
        public long   PlayerId;
        public string PlayerName;
        public string Message;
        public long   AppliedUtcTicks;
    }
}
