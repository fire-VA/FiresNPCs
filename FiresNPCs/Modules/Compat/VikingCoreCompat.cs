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
    /// Detection mirrors <c>BalrondAmazingNatureCompat</c>: a Chainloader presence check on TDV's GUID.
    /// PluginInfos is populated for every discovered plugin before any plugin's Awake runs, so this is
    /// reliable even though TDV loads after FiresRPGmaker.
    /// </summary>
    internal static class VikingCoreCompat
    {
        private const string VikingCoreGUID = "terresdeviking.plugin";

        private static bool _checked;
        private static bool _present;

        public static bool Present
        {
            get
            {
                if (_checked) return _present;
                _checked = true;
                try { _present = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(VikingCoreGUID); }
                catch { _present = false; }
                Debug.Log(_present
                    ? "[VikingCoreCompat] VikingLands.Core detected — RPG Maker defers groups/guilds/leaderboards/achievements to it."
                    : "[VikingCoreCompat] VikingLands.Core not present — RPG Maker runs its own social systems.");
                return _present;
            }
        }
    }
}
