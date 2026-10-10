using System;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI
{
    // Wires the shared FiresCore UIBuilder SDK to FiresRPGmaker's host specifics: the mod's asset bundle and
    // root object, a coroutine host, where its bundled-layout JSON resources live, input blocking, the prefab
    // factory's background sprite, the config file-watcher admin-sync, and the mod's font-config choice.
    // Called once during startup after the asset bundle is loaded.
    internal static class FiresNpcsUIBuilderHost
    {
        public static void Install()
        {
            UIBuilderHost.AssetBundle = FiresRPGmaker.assetBundle;
            UIBuilderHost.RootObject = FiresRPGmaker.RootObject;
            UIBuilderHost.CoroutineHost = FiresRPGmaker.Instance;
            UIBuilderHost.LayoutResourceAssembly = typeof(FiresRPGmaker).Assembly;
            UIBuilderHost.LayoutResourcePrefix = "FiresRPGmaker.BundledLayouts.";
            UIBuilderHost.ModAssemblyName = "FiresRPGmaker.dll";
            UIBuilderHost.ModName = "FiresRPGmaker";
            UIBuilderHost.InputBlocker = GUIManager.BlockInput;
            UIBuilderHost.BackgroundSpriteProvider = UIPrefabFactory.GetBackgroundSprite;
            UIBuilderHost.MarkFileAdminSynced = ServerConfigFileWatcher.MarkFileAsAdminSynced;
            UIBuilderHost.ConfiguredFontProvider = ResolveConfiguredFont;
            // Core 0.2.330+: the fields above are one global that FiresCompanions overwrites when it loads after us (that hid
            // the globe HUD's prefab from 09-28 to 10-08). Register() keeps our bundle, layouts, name and admin-sync marker
            // in Core's lists so its lookups still find them.
            UIBuilderHost.Register();
        }

        // Maps the mod's font-config choice (UIFontOption, owned by ConfigManager) to the SDK's FontStyle.
        // The SDK keeps no knowledge of the mod's config enum; the mapping lives here.
        private static UIFontConfig.FontStyle? ResolveConfiguredFont(UIFontConfig.FontCategory category)
        {
            var cm = ConfigManager.Instance;
            if (cm == null) return null;

            UIFontOption? option = category switch
            {
                UIFontConfig.FontCategory.Primary => cm.configUIPrimaryFont?.Value,
                UIFontConfig.FontCategory.Decorative => cm.configUIDecorativeFont?.Value,
                UIFontConfig.FontCategory.Body => cm.configUIBodyFont?.Value,
                _ => null
            };
            if (option == null) return null;

            return option.Value switch
            {
                UIFontOption.Valheim_Prstartk => UIFontConfig.FontStyle.Valheim,
                UIFontOption.Valheim_AveriaSerif => UIFontConfig.FontStyle.AveriaSerif,
                UIFontOption.Valheim_Norse => UIFontConfig.FontStyle.Norse,
                UIFontOption.Valheim_AveriaSans => UIFontConfig.FontStyle.AveriaLibre,
                UIFontOption.Valheim_Rune => UIFontConfig.FontStyle.Rune,
                _ => UIFontConfig.FontStyle.Valheim
            };
        }
    }
}
