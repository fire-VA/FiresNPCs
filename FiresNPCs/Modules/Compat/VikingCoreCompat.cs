using UnityEngine;

namespace FiresRPGmaker.Modules.Compat
{
    /// <summary>
    /// Guild hand-off detection. 0.0.533 (Fire 10-08): the old reference mod VikingLands.Core (TDV, "terresdeviking.plugin")
    /// is no longer probed or logged. Guilds come from FiresGuilds only; groups, leaderboards and achievements are RPG Maker's
    /// own. <see cref="Present"/> stays as a constant false so the old call sites compile and never stand down.
    /// </summary>
    internal static class VikingCoreCompat
    {
        public const bool Present = false;
        // The standalone FiresGuilds mod also owns guilds when present. Detected the same way
        // (Chainloader presence, cached-once positive). Kept SEPARATE from Present because FiresGuilds owns
        // ONLY guilds; groups/parties/leaderboards/achievements are RPG Maker's own.
        private const string FiresGuildsGUID = "com.Fire.FiresGuilds";
        private static bool _firesGuilds;

        public static bool FiresGuildsPresent
        {
            get
            {
                if (_firesGuilds) return true;
                try { _firesGuilds = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(FiresGuildsGUID); }
                catch { _firesGuilds = false; }
                return _firesGuilds;
            }
        }

        // True when an external mod owns the GUILD system specifically (FiresGuilds). RPG Maker then
        // stands its guild down — RPCs, UI, G hotkey, and the /g chat command — so the two never duplicate.
        public static bool GuildsDeferred => FiresGuildsPresent;

        // Which mod actually took guilds. Either one defers us, so a message that names only one of them sends
        // whoever reads the log hunting for a mod that is not installed.
        public static string GuildOwner => FiresGuildsPresent ? "FiresGuilds" : null;

        // 0.0.525 ([ghost], Fire 10-08: RPG Maker's guilds are moving out to FiresGuilds): FiresGuilds 1.0.10+ has its own guild
        // friendly fire, guild-mate map pin colours and Shift+MMB guild ping, so RPG Maker's copies (which reach FiresGuilds' data
        // through the RpgMakerApi bridge) stand down; an older FiresGuilds keeps getting them from us.
        private static int _firesGuildsPorts = -1;

        public static bool FiresGuildsHasPorts
        {
            get
            {
                if (_firesGuildsPorts >= 0) return _firesGuildsPorts == 1;
                if (!FiresGuildsPresent) return false;   // not loaded yet, or not installed: ask again later
                try
                {
                    var v = BepInEx.Bootstrap.Chainloader.PluginInfos[FiresGuildsGUID].Metadata.Version;
                    _firesGuildsPorts = v >= new System.Version(1, 0, 10) ? 1 : 0;
                }
                catch { _firesGuildsPorts = 0; }
                return _firesGuildsPorts == 1;
            }
        }
    }
}
