using UnityEngine;

namespace FiresRPGmaker.Modules.Compat
{
    /// <summary>
    /// Detects VikingLands.Core (TDV). When present, TDV owns the authoritative groups, guilds,
    /// leaderboards, and achievements — so RPG Maker stands down its own (skips Party + Guild init,
    /// hides its leaderboard/achievement UI) and drives its remaining guild-dependent features
    /// (chat / dialogue conditions / territory) off TDV via <see cref="RpgMakerApi"/>.
    ///
    /// TDV still consumes RPG Maker's quest/customValue events (RpgMakerApi) to feed its leaderboard,
    /// and registers its group/guild query methods into RpgMakerApi at its own startup.
    ///
    /// Detection is a Chainloader presence check on TDV's GUID. IMPORTANT: BepInEx 5 populates
    /// PluginInfos INCREMENTALLY as each plugin loads — and VikingLands.Core loads AFTER FiresRPGmaker —
    /// so a check during our Awake would miss it. We therefore re-evaluate until found and cache ONLY the
    /// positive result. Every gate that depends on this (Guild/Party init, leaderboard/achievement UI)
    /// runs at Game.Start / after ZNet-ready, by which point all plugins (incl. TDV) have loaded.
    /// </summary>
    internal static class VikingCoreCompat
    {
        private const string VikingCoreGUID = "terresdeviking.plugin";

        private static bool _present;
        private static bool _logged;

        public static bool Present
        {
            get
            {
                if (_present) return true; // once detected, stays detected (and PluginInfos only grows)
                try { _present = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(VikingCoreGUID); }
                catch { _present = false; }
                if (_present && !_logged)
                {
                    _logged = true;
                    Debug.Log("[VikingCoreCompat] VikingLands.Core detected — RPG Maker defers groups/guilds/leaderboards/achievements to it.");
                }
                return _present;
            }
        }

        // The standalone FiresGuilds mod also owns guilds when present. Detected the same way
        // (Chainloader presence, cached-once positive). Kept SEPARATE from Present because FiresGuilds owns
        // ONLY guilds — groups/parties/leaderboards/achievements still defer to TDV (Present) alone.
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

        // True when an external mod owns the GUILD system specifically (TDV or FiresGuilds). RPG Maker then
        // stands its guild down — RPCs, UI, G hotkey, and the /g chat command — so the two never duplicate.
        public static bool GuildsDeferred => Present || FiresGuildsPresent;

        // Which mod actually took guilds. Either one defers us, so a message that names only one of them sends
        // whoever reads the log hunting for a mod that is not installed.
        public static string GuildOwner => Present ? "VikingLands.Core" : FiresGuildsPresent ? "FiresGuilds" : null;
    }
}
