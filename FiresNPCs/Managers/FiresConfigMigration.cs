namespace FiresRPGmaker.Managers
{
    /// <summary>Kept only for the existing Ascend call-site. The real family-wide config consolidation + grouping
    /// now lives in Core's <see cref="FiresCore.Storage.FiresConfigPaths.Migrate"/> (which runs at Core startup,
    /// before this mod's Awake), so this just delegates — idempotent, so double-invoking is harmless.</summary>
    public static class FiresConfigMigration
    {
        public static void Run() => FiresCore.Storage.FiresConfigPaths.Migrate();
    }
}
