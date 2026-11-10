using FiresCore.Logging;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker
{
    // FiresRPGmaker's tagged logger. Thin wrapper over the shared FiresCore FiresLog engine so
    // logging behavior lives in one place; only the mod tag and verbose source are mod-specific.
    public static class FiresLogger
    {
        private static readonly FiresLog Log = new FiresLog(
            "FiresRPGmaker",
            () => ConfigManager.Instance?.configVerboseLogging?.Value ?? false);

        public static bool VerboseEnabled => Log.VerboseEnabled;

        public static void LogInfo(string message) => Log.Info(message);

        public static void LogVerbose(string message) => Log.Verbose(message);

        /// <summary>Fire 11-03: recurring and proof lines. Real BepInEx Debug, through Core's engine; default players see warnings and errors.</summary>
        public static void LogDebug(string message) => Log.Debug(message);

        public static void LogWarning(string message) => Log.Warning(message);

        public static void LogError(string message) => Log.Error(message);
    }
}
