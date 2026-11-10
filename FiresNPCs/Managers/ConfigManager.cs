using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Progression;

using FiresCore.Sync;

namespace FiresRPGmaker.Managers
{
    /// <summary>Overhead identity display for static NPCs. HealthBar = the vanilla EnemyHud plate;
    /// HoverName = kg-Marketplace-style raised world-space name text; Hide = nothing.</summary>
    public enum StaticNpcNameplateMode
    {
        HealthBar,
        HoverName,
        Hide,
    }

    /// <summary>
    /// Available font options for UI configuration.
    /// These names are displayed in the BepInEx config dropdown.
    /// Each maps to a specific Valheim TMP font asset.
    /// </summary>
    public enum UIFontOption
    {
        /// <summary>Retro 80s pixel-style font (Valheim default HUD font)</summary>
        Valheim_Prstartk,

        /// <summary>Elegant serif font - clean and readable</summary>
        Valheim_AveriaSerif,

        /// <summary>Viking runic decorative font - great for headers</summary>
        Valheim_Norse,

        /// <summary>Clean sans-serif font - modern look</summary>
        Valheim_AveriaSans,

        /// <summary>Runic symbols font - for special decorative use</summary>
        Valheim_Rune
    }

    public class ConfigManager
    {
        private static ConfigManager _instance;
        public static ConfigManager Instance => _instance ?? (_instance = new ConfigManager());

        public bool _isProcessingSync = false;
        private FileSystemWatcher configWatcher;
        // Stamped on every ConfigEntry write we make (SettingChanged fires on .Value sets and
        // SaveOnConfigSet rewrites the .cfg on the same set) - lets the watcher tell our own
        // file writes from an external/admin edit, so interactive UI writes can't loop us into
        // endless self-reloads (+ synced-entry rebroadcasts).
        private DateTime _lastSelfWriteUtc = DateTime.MinValue;
        private const double SelfWriteSuppressSeconds = 3.0;

        private void SelfWriteStamp(object sender, BepInEx.Configuration.SettingChangedEventArgs e)
            => _lastSelfWriteUtc = DateTime.UtcNow;

        private bool IsRecentSelfWrite()
            => (DateTime.UtcNow - _lastSelfWriteUtc).TotalSeconds < SelfWriteSuppressSeconds;
        private bool _isShuttingDown = false;
        private DateTime _lastConfigUpdate = DateTime.MinValue;
        private Coroutine _reloadCoroutine;

        // ────── General Settings ──────
        public ConfigEntry<bool> configVerboseLogging;
        public ConfigEntry<bool> configServerAuthority;

        // ────── Hotkeys ──────
        // KeyboardShortcut (not KeyCode) so multi-key chords like "J + LeftAlt" are storable —
        // the F8 Set→record→Apply capture writes them and Chord.IsDown reads them. Old saved
        // single-key values ("J") deserialize cleanly as a bare-main-key shortcut.
        public ConfigEntry<KeyboardShortcut> configQuestJournalHotkey;
        public ConfigEntry<float> configQuestBookOpenSpeed;
        public ConfigEntry<float> configBookScale;
        public ConfigEntry<float> configMapRoutePointSpacing;
        public ConfigEntry<bool> configLiveMap;
        public ConfigEntry<int> configLiveMapFps;
        public ConfigEntry<bool> configLiveMinimap;
        public ConfigEntry<int> configLiveMinimapFps;
        public ConfigEntry<float> configLiveMapMaxView;
        public ConfigEntry<bool> configLiveMapAuto;   // 0.0.843
        public ConfigEntry<int> configLiveMapResolution;
        public ConfigEntry<string> configQuestRewardOverflow;
        public ConfigEntry<KeyboardShortcut> configLeaderboardHotkey;
        public ConfigEntry<bool> configLeaderboardBackgroundToggle;
        public ConfigEntry<string> configLeaderboardBackground;
        public ConfigEntry<string> configAchievementProfile;

        // ────── Guilds ────── removed with RPG Maker's guild module (FiresGuilds owns guilds and their settings).

        // ────── Territories ──────
        public ConfigEntry<bool> configAdminBypassTerritory;
        public ConfigEntry<int> configMaxTerritoriesPerPlayer;   // Claim limit, server-synced
        public ConfigEntry<string> configLedgerPlayerFlags;   // Territory Ledger: the flags owners may change, server-synced
        public ConfigEntry<bool> configTerritoryVisualizers;
        public ConfigEntry<bool> configTerritoryOverlay;
        public ConfigEntry<bool> configDeepNorthMapOverlay;
        public ConfigEntry<bool> configCompactMapOverlayBuffers;
        public ConfigEntry<bool> configSpreadMapOverlayPaint;
        public ConfigEntry<bool> configBackgroundTerritoryCacheWrite;
        public ConfigEntry<bool> configBackgroundVaultReceive;
        public ConfigEntry<bool> configRedrawOverlayAfterLoad;
        public ConfigEntry<bool> configDebounceStatSaves;
        public ConfigEntry<bool> configBackgroundVaultWrites;
        public ConfigEntry<bool> configLoadBundlesFromFiles;
        public ConfigEntry<bool> configPerfNameplateSleeps, configPerfPatrolKeyRevision, configPerfHotkeyGate;
        public ConfigEntry<bool> configFrameCostProbe;
        public ConfigEntry<bool> configVisEquipmentProbe;

        // ────── Companions ──────
        public ConfigEntry<bool> configShowCompanionBillboard;
        public ConfigEntry<bool> configShowGroupHud;
        public ConfigEntry<bool> configUseGlobeHud;
        public ConfigEntry<bool> configGlobeFoodRow;
        public ConfigEntry<bool> configGlobeOverfillPulse;
        public ConfigEntry<bool> configGlobeStates;
        public ConfigEntry<float> configGlobeLowThreshold, configGlobeDamageDrop, configGlobeFillingRate, configGlobeFullFloor;
        public ConfigEntry<bool> configGlobeLiquidMotion;
        public ConfigEntry<float> configGlobeSloshStrength;
        public ConfigEntry<bool> configGlobeValuePopups;
        public ConfigEntry<float> configGlobePopupHoldSeconds;
        public ConfigEntry<bool> configGlobeInventoryTooltips;
        public ConfigEntry<bool> configUseBookAdminUI;
        public ConfigEntry<bool> configShowAdminHints;
        public ConfigEntry<string> configFeedbackWebhook;
        public ConfigEntry<float> configExtendedInteractRange;

        // Group HUD layout / position
        public ConfigEntry<int>   configGroupHudMaxRowsPerColumn;
        public ConfigEntry<int>   configGroupHudColumns;
        public ConfigEntry<float> configGroupHudDefaultPosX;
        public ConfigEntry<float> configGroupHudDefaultPosY;

        // Quest tracker HUD (client-local personal HUD)
        public ConfigEntry<bool>  configShowQuestTracker;
        public ConfigEntry<float> configQuestTrackerPosX;
        public ConfigEntry<float> configQuestTrackerPosY;
        public ConfigEntry<bool>  configQuestTrackerCollapsed;
        public ConfigEntry<bool>  configQuestTrackerUserPositioned;

        // Territory HUD box position (client-local; drag with the pause menu open to reposition)
        public ConfigEntry<float> configTerritoryHudPosX;
        public ConfigEntry<float> configTerritoryHudPosY;

        // Companion Behavior Settings
        public ConfigEntry<float> configCompanionIdleWanderRadius;
        public ConfigEntry<float> configCompanionReturnHomeRadius;
        public ConfigEntry<float> configCompanionChestSearchRadius;
        public ConfigEntry<float> configCompanionStayModeWorkRadius;
        public ConfigEntry<float> configChestAutoSortRadius;
        public ConfigEntry<float> configCompanionStationedDefendRange;
        public ConfigEntry<float> configCompanionStationedLeashRange;
        public ConfigEntry<float> configCompanionStationedCombatHold;

        // Companion Persistence (Synced) — server-authoritative model where companions
        // live in the world like vanilla tames instead of being destroyed and respawned
        // on every logout/login.
        public ConfigEntry<bool> configPersistentCompanions;
        public ConfigEntry<bool> configKeepZonesLoadedForOfflinePlayers;

        // Master switch (default true): when true, hammer-placed static NPCs cannot be killed (immune to all
        // damage) AND both resurrection paths are off (no death-respawn, no reconciler auto-revive) so an admin
        // delete is permanent. When false, they are killable + respawn at their placed position after 60s.
        // Server-authoritative.
        public ConfigEntry<bool> configStaticNpcInvulnerable;

        // Overhead display style for static NPCs: vanilla plate, kg-style hover name, or nothing.
        public ConfigEntry<StaticNpcNameplateMode> configStaticNpcNameplateMode;

        // Client-side background opacities for the dialogue + info NPC panels.
        public ConfigEntry<float> configDialogueBgOpacity;
        public ConfigEntry<float> configInfoBgOpacity;

        // ────── UI Fonts ──────
        // These allow swapping the font used for different UI text categories
        public ConfigEntry<UIFontOption> configUIPrimaryFont;
        public ConfigEntry<UIFontOption> configUIDecorativeFont;
        public ConfigEntry<UIFontOption> configUIBodyFont;

        // ────── Progression (Synced) ──────
        public ConfigEntry<bool> configUsePrivateKeys;
        public ConfigEntry<bool> configBlockAllGlobalKeys;
        public ConfigEntry<string> configBlockedGlobalKeys;
        public ConfigEntry<string> configAllowedGlobalKeys;
        public ConfigEntry<string> configEnforcedGlobalKeys;
        public ConfigEntry<string> configBlockedPrivateKeys;
        public ConfigEntry<string> configAllowedPrivateKeys;
        public ConfigEntry<string> configEnforcedPrivateKeys;
        public ConfigEntry<bool> configUsePrivateRaids;
        public ConfigEntry<bool> configAdminBypassProgression;
        public ConfigEntry<bool> configUseBlockedMessage;
        public ConfigEntry<string> configBlockedMessage;
        public ConfigEntry<bool> configUseBlockedEffect;
        public ConfigEntry<bool> configLockEquipment;
        public ConfigEntry<string> configLockEquipmentKey;
        public ConfigEntry<bool> configLockCrafting;
        public ConfigEntry<string> configLockCraftingKey;
        public ConfigEntry<bool> configLockBuilding;
        public ConfigEntry<string> configLockBuildingKey;
        public ConfigEntry<bool> configLockCooking;
        public ConfigEntry<string> configLockCookingKey;
        public ConfigEntry<bool> configLockEating;
        public ConfigEntry<string> configLockEatingKey;
        public ConfigEntry<bool> configLockTaming;
        public ConfigEntry<string> configLockTamingKey;
        public ConfigEntry<bool> configLockGuardianPower;
        public ConfigEntry<string> configLockGuardianPowerKey;
        public ConfigEntry<bool> configLockBossSummons;
        public ConfigEntry<string> configLockBossSummonsKey;

        // ────── Piece Recipes (Synced) ──────
        // Default cost for every piece we add: a single cheat-sword and nothing
        // else. Only an admin can produce one (`genitem SwordCheat`), so our
        // content is admin-only to build by construction rather than by a UI
        // check that can fail open. VARecipeManager additionally forces the
        // gate onto whatever recipe is configured, so editing this string
        // customises the EXTRA cost — it can't remove the gate.
        public const string DefaultPieceRecipe = "SwordCheat,1";

        // The default every cfg written before the gate landed still carries.
        // BepInEx never rewrites an existing key, so without this those pieces
        // would keep the old build cost stacked UNDER the gate. A piece still
        // sitting on this exact string never had a deliberate recipe, so it is
        // safe to migrate; anything customised is left untouched.
        private const string LegacyDefaultPieceRecipe =
            "ElderBark,40,Blackwood,40,TrollHide,20,BronzeNails,144,Iron,10";

        public readonly Dictionary<string, ConfigEntry<string>> configPerPieceRecipes = new();
        public readonly Dictionary<string, ConfigEntry<bool>> configPerPieceEnabled = new();

        public void Initialize(ConfigFile config)
        {
            // ────── General Settings ──────
            configVerboseLogging = config.Bind("General", "VerboseLogging", false,
                "Enable detailed debug logs.");
            configServerAuthority = config.Bind("General", "ServerAuthority", false,
                "When enabled, config changes are locked to server admin only (synced).");

            // ────── Hotkeys ──────
            configQuestJournalHotkey = config.Bind("Hotkeys", "QuestJournalKey", new KeyboardShortcut(KeyCode.J),
                "Hotkey to open the Quest Journal. Supports chords (e.g. J + LeftAlt). Set to None to disable.");
            configQuestBookOpenSpeed = config.Bind("UI", "QuestBookOpenSpeed", 1f, new BepInEx.Configuration.ConfigDescription("Speed multiplier for the quest book's opening animation (1 = normal, 2 = twice as fast). The UI reveal delay scales with it automatically.", new BepInEx.Configuration.AcceptableValueRange<float>(0.1f, 5f)));
            configMapRoutePointSpacing = config.Bind("Dressing", "Map route point spacing", 6f,
                new BepInEx.Configuration.ConfigDescription("Patrol map editor (Set Patrol Route > Draw on map): metres between the points a drag drops. Walk-recorded routes keep 1.5 m.",
                    new BepInEx.Configuration.AcceptableValueRange<float>(1f, 50f)));
            // The large map (M) can show a live top-down miniature of the loaded area when zoomed in.
            configLiveMinimap = config.Bind("Live map", "Replace minimap", false, "Show the live map in the small minimap instead of the painted map (off until its cost is measured).");
            configLiveMinimapFps = config.Bind("Live map", "Minimap frames per second", 10, new BepInEx.Configuration.ConfigDescription("How often the live minimap redraws.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 30)));
            configLiveMap = config.Bind("Live map", "Enabled", false, "Large map (M): when zoomed in, show a LIVE top-down render of the loaded area (players, creatures, NPCs, ships, day/night) over the painted map. The header at the top of the map cycles Auto / Live map / Default. Costs one extra camera render at the capped rate below while the map is open.");
            // 0.0.843 (Fire 11-08: "properly gated behind a config toggle ... if live map is toggled off it only zooms in to the default level"): read live
            configLiveMapAuto = config.Bind("Live map", "Auto live map when zoomed in", true, "Large map, header on Auto (the default): zooming in past the closest painted zoom crossfades into the live view of the loaded area; zooming back out returns to the painted map. Off (or Enabled off): the map only zooms in to the game's default closest level.");
            configLiveMapFps = config.Bind("Live map", "Frames per second", 15, new BepInEx.Configuration.ConfigDescription("How often the live map re-renders while it is shown.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 60)));
            configLiveMapMaxView = config.Bind("Live map", "Max view width", 400f, new BepInEx.Configuration.ConfigDescription("Metres across the map view below which the live render replaces the painted map (zoom in past this).", new BepInEx.Configuration.AcceptableValueRange<float>(50f, 2000f)));
            configLiveMapResolution = config.Bind("Live map", "Resolution", 1024, new BepInEx.Configuration.ConfigDescription("Longest side of the live render in pixels.", new BepInEx.Configuration.AcceptableValueRange<int>(256, 2048)));
            configBookScale = config.Bind("UI", "BookScale", 1f,
                new BepInEx.Configuration.ConfigDescription(
                    "Overall scale of the NPC config book UI. Scales the whole book evenly — text, fields, buttons, everything — so you can size it to your liking. 1 = normal, 0.5 = half, 2 = double. Applies live and on next open.",
                    new BepInEx.Configuration.AcceptableValueRange<float>(0.5f, 2f)));
            configQuestRewardOverflow = config.Bind("General", "QuestRewardOverflowDelivery", "Mail",
                new BepInEx.Configuration.ConfigDescription(
                    "Where quest reward items go when your inventory is FULL. Mail = mailed to you with attachments (check a mailbox), Bank = queued into your bank items (visit a banker), Ground = dropped at your feet (kg Marketplace behavior). Rewards that fit in your inventory always go there first.",
                    new BepInEx.Configuration.AcceptableValueList<string>("Mail", "Bank", "Ground")));
            configLeaderboardHotkey = config.Bind("Hotkeys", "LeaderboardKey", KeyboardShortcut.Empty,   // Unbound by default (was L); the hub has a Leaderboard tab
                "Hotkey to open the Leaderboard. Supports chords (e.g. L + LeftAlt). Set to None to disable.");
            configLeaderboardBackgroundToggle = config.Bind("UI", "LeaderboardBackgroundToggle", false,
                "If true, the Leaderboard panel uses a PNG/JPG you drop into BepInEx/config/FiresUIBackgrounds/ as its background instead of the built-in art.");
            configLeaderboardBackground = config.Bind("UI", "LeaderboardBackground", "",
                "Filename (e.g. parchment.png) inside BepInEx/config/FiresUIBackgrounds/. Only used when LeaderboardBackgroundToggle is on.");
            configAchievementProfile = config.Bind("General", "LeaderboardAchievementProfile", "server",
                "Name of the achievement profile (.cfg under BepInEx/config/FiresRPGmaker/LeaderboardAchievements/) the " +
                "leaderboard scores against. The generator writes to 'server' by default. If the named profile is empty, " +
                "the first non-empty loaded profile is used (else 'example'). [Synced with Server]");


            // ────── Territories ──────
            configAdminBypassTerritory = config.Bind("Territories", "AdminBypassTerritoryRestrictions", true,
                "Allow admins to bypass territory restrictions such as NoBuild, NoTool, and pushback.");
            // Checked by the server when a player buys or is given a territory.
            configMaxTerritoriesPerPlayer = config.Bind("Territories", "Max territories per player", 0,
                new ConfigDescription("How many territories one player may own; 0 = no limit. [Synced with Server]",
                    new AcceptableValueRange<int>(0, 1000)));
            configLedgerPlayerFlags = config.Bind("Territory Ledger", "Player flags", Modules.Territories.TerritoryLedgerEdit.DefaultPlayerFlags,
                "The territory flags an owner may turn on or off at a Territory Ledger, comma separated. Admin-only flags (Force Biome, height, spawners, god mode and the like) are refused even when listed here. [Synced with Server]");
            configTerritoryVisualizers = config.Bind("Territories", "EnableWorldVisualizers", false,
                "Draw 3D world debug visualizers for territories (for overlap/height debugging).");
            configTerritoryOverlay = config.Bind("Territories", "EnableMapOverlay", true,
                "Draw the territory overlay on the minimap. Disable to test/avoid overlay-related stutter.");
            configDeepNorthMapOverlay = config.Bind("Territories", "EnableDeepNorthMapOverlay", true,
                "Draw the Deep North icy-blue tint and water line on the minimap. " +
                "Disable if using Better Continents or another mod that provides its own map visuals for the Deep North.");
            configVisEquipmentProbe = config.Bind("Debug", "VisEquipmentProbe", false,
                "Measuring rounds only (a per-frame probe costs fps): logs (Debug, every 10 s) VisEquipment.CustomUpdate and Character.FixedUpdate cost per kind of body (player / companion / RPGM NPC / creature), the top 5 bodies, and which equipment slots re-equipped. Needs a restart.");
            configFrameCostProbe = config.Bind("Debug", "FrameCostProbe", false,
                "Logs (Debug, every 10 s) how much frame time RPGM\x27s own per-frame components cost, top 10 by ms/s. For the standing-fps sweep; turn off when done. Needs a restart.");
            configPerfNameplateSleeps = config.Bind("Performance", "NPC hover nameplate sleeps", true,
                "Perf fix: an NPC's hover nameplate does no per-frame work unless the nameplate style is HoverName. Off = it checks every frame. Needs a restart.");
            configPerfPatrolKeyRevision = config.Bind("Performance", "Patrol key reads only on change", true,
                "Perf fix: on a client an NPC re-reads its patrol route only when its network data changed. Off = once a second regardless.");
            configPerfHotkeyGate = config.Bind("Performance", "Hotkey checks only on key press", true,
                "Perf fix: the quest journal and leaderboard hotkeys check the game state only in the frame their key goes down. Off = every frame.");
            configLoadBundlesFromFiles = config.Bind("General", "LoadBundlesFromFiles", true,
                "RAM fix: load the asset bundles from the files next to the DLL (no in-memory copies). Off = the bundles embedded in the DLL (about 130 MB more RAM). Needs a restart.");
            configBackgroundVaultWrites = config.Bind("Vault", "BackgroundVaultWrites", true,
                "Perf fix: the debounced vault save writes its file on a background thread (the snapshot stays on the main thread; world save, logout and quit still wait for it). Off = write on the main thread.");
            configDebounceStatSaves = config.Bind("Vault", "DebounceStatSaves", true,
                "Perf fix: a kill updates the stat at once but writes the vault file at most every 5 s per player (and always on world save, logout and quit). Off = write the file on every kill.");
            configRedrawOverlayAfterLoad = config.Bind("Territories", "RedrawMapOverlayAfterLoad", true,
                "Fix: redraw the territory map overlay once the territory files have loaded. Without it a host whose map drew before the load showed no territories until something else changed them.");
            // 0.0.806 (Fire 10-31: "spread between frames and budgeted"; off until its A/B proves it)
            configSpreadMapOverlayPaint = config.Bind("Territories", "Spread map overlay paint", false,
                "Paint the territory overlay onto the map over several frames (about 2 ms each) instead of in one frame. " +
                "The old overlay stays on screen until the new one is complete; the map shows the change a fraction of a second later.");
            configBackgroundTerritoryCacheWrite = config.Bind("Territories", "Write territory cache in the background", false,
                "Write the local copy of the server's territories to disk on a background thread after a sync, instead of in the game frame.");
            configBackgroundVaultReceive = config.Bind("Vault", "Serialize received vault data in the background", false,
                "Prepare your quest / vault data received from the server on a background thread instead of in the game frame (a frame or two later in your save).");
            configCompactMapOverlayBuffers = config.Bind("Territories", "CompactMapOverlayBuffers", true,
                "Keep the map overlay's copies of the minimap in their own pixel formats instead of full colour. " +
                "On a 2048 map that is about 50 MB instead of about 270 MB, with identical output. " +
                "Turn it off to compare the two; the overlay logs which one it used and how much it holds.");

            // ────── Companions ──────
            // 0.0.804: the dead [Companions] ShowGroupHud copy is gone (nothing read it); the real switch is [UI.Hud] GroupHud Show (GroupHudOwned)
            configShowGroupHud = config.Bind("UI.Hud", "GroupHud Show", true,
                "Show the group HUD: the panel under the minimap listing your companions and party with name, distance and health / stamina / eitr bars.");
            configGlobeFoodRow = config.Bind("UI", "Food row", false,
                "Shows your three active foods under the globe HUD: icon, time left (m:ss) and a radial fill of the time left; a food blinks in its last minute. Follows the globe HUD's position and scale.");
            configGlobeOverfillPulse = config.Bind("UI", "Overfill pulse", false,
                "The Health / Stamina / Eitr globes pulse when food lifts your maximum above its no-food base; the bigger the boost, the stronger the pulse (2x about 40 %, 5x about 90 %).");
            configGlobeStates = config.Bind("UI", "Globe states", false,
                "The globes show what is happening: FULL breathes (stronger the more food lifts your max), FILLING beats while the value rises, DAMAGE flashes on a sharp drop, LOW throbs as a warning. Priority DAMAGE > LOW > FILLING > FULL. Off = today's look (the old low pulse).");
            configGlobeLowThreshold = config.Bind("UI", "Globe low threshold", 0.25f,
                new BepInEx.Configuration.ConfigDescription("Globe states: LOW below this fraction of max.", new BepInEx.Configuration.AcceptableValueRange<float>(0.05f, 0.6f)));
            configGlobeDamageDrop = config.Bind("UI", "Globe damage drop", 0.08f,
                new BepInEx.Configuration.ConfigDescription("Globe states: DAMAGE when the value falls by this fraction of max within 0.3 s (shown 0.6 s).", new BepInEx.Configuration.AcceptableValueRange<float>(0.01f, 0.5f)));
            configGlobeFillingRate = config.Bind("UI", "Globe filling rate", 0.02f,
                new BepInEx.Configuration.ConfigDescription("Globe states: FILLING while the value rises at least this fraction of max per second.", new BepInEx.Configuration.AcceptableValueRange<float>(0.005f, 0.5f)));
            configGlobeFullFloor = config.Bind("UI", "Globe full breath floor", 0.15f,
                new BepInEx.Configuration.ConfigDescription("Globe states: FULL breath strength with no food overfill (0 = none); it rises to 1 as food lifts your max.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));
            configUseGlobeHud = config.Bind("UI", "UseGlobeHud", true,
                "Enable the custom globe-style Health/Stamina/Eitr/Adrenaline HUD overlay. " +
                "Set to false to hide the globe entirely and use the vanilla Valheim HUD bars instead.");
            configUseGlobeHud.SettingChanged += (_, __) =>
                global::FiresRPGmaker.UI.GlobeHudController.ApplyEnabled(configUseGlobeHud.Value);
            configGlobeLiquidMotion = config.Bind("UI.Hud", "Globe LiquidMotion", true,
                "Slosh the liquid in the globes when you start moving, stop, strafe, jump, land or turn the camera.");
            configGlobeSloshStrength = config.Bind("UI.Hud", "Globe SloshStrength", 1f,
                new BepInEx.Configuration.ConfigDescription("How hard movement throws the globe liquid around. 1 = default, 0 = still.",
                    new BepInEx.Configuration.AcceptableValueRange<float>(0f, 2f)));
            configGlobeValuePopups = config.Bind("UI.Hud", "Globe ValuePopups", true,
                "Show current / max above a globe while its value is changing (sprinting, casting, taking a hit, healing).");
            configGlobePopupHoldSeconds = config.Bind("UI.Hud", "Globe PopupHoldSeconds", 1.5f,
                new BepInEx.Configuration.ConfigDescription("How long the current / max text stays after the value stops changing, before it fades.",
                    new BepInEx.Configuration.AcceptableValueRange<float>(0.3f, 6f)));
            configGlobeInventoryTooltips = config.Bind("UI.Hud", "Globe InventoryTooltips", true,
                "With the inventory open, hovering a globe explains its maximum: base, each food, trinkets, and what " +
                "buffs or other mods add beyond vanilla.");
            configUseBookAdminUI = config.Bind("UI", "UseBookAdminPanel", true,
                "Use the new two-page parchment 'book' NPC admin config panel (structured per-module forms). " +
                "When disabled, the legacy completecompanionsui admin panel is used instead.");
            configShowAdminHints = config.Bind("UI", "ShowAdminHints", true,
                "Show hover-help tooltips over admin config fields (dialogue editor, trader/banker/gambler/" +
                "transmog/quest forms). Turn off if you already know the formats and find them distracting.");
            configShowAdminHints.SettingChanged += (_, __) =>
                FiresCore.UI.FiresHoverTip.Enabled = configShowAdminHints.Value;
            FiresCore.UI.FiresHoverTip.Enabled = configShowAdminHints.Value;
            configFeedbackWebhook = config.Bind("General", "FeedbackWebhookUrl", "",
                "Discord webhook URL that /feedback posts to (with or without the https:// prefix). Empty = disabled.");
            configExtendedInteractRange = config.Bind("UI", "ExtendedInteractRange", 0f, new BepInEx.Configuration.ConfigDescription("DistancedUI: if > 0, sets the player's max interaction distance (meters) so NPCs/objects can be " +
                "used from further away. 0 = vanilla (~5m).", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 20f)));

            // Group HUD layout — panel stretches to fit the actual group up to MaxRowsPerColumn,
            // then becomes scrollable. Columns 1 = single list, 2 = side-by-side.
            configGroupHudMaxRowsPerColumn = config.Bind("UI.Hud", "GroupHud MaxRowsPerColumn", 8, new BepInEx.Configuration.ConfigDescription("Maximum rows visible per column before the group HUD panel scrolls. " +
                "The panel auto-shrinks to fit smaller groups; this value is just the upper cap. " +
                "Range: 1-20.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 30)));
            configGroupHudColumns = config.Bind("UI.Hud", "GroupHud Columns", 1, new BepInEx.Configuration.ConfigDescription("Number of columns in the group HUD. 1 = single list, 2 = side-by-side. Range: 1-2.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 6)));
            configGroupHudDefaultPosX = config.Bind("UI.Hud", "GroupHud DefaultPosX", -10f, new BepInEx.Configuration.ConfigDescription("Default panel X offset from the screen's top-right anchor (negative = left of anchor). " +
                "Used when the player has not yet dragged the panel, and when ResetPosition is invoked.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            configGroupHudDefaultPosY = config.Bind("UI.Hud", "GroupHud DefaultPosY", -200f, new BepInEx.Configuration.ConfigDescription("Default panel Y offset from the screen's top-right anchor (negative = below anchor). " +
                "Used when the player has not yet dragged the panel, and when ResetPosition is invoked.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));

            // Quest tracker HUD — right side, under the territory box. Client-local personal HUD.
            configShowQuestTracker = config.Bind("UI.Hud", "QuestTracker Show", true,
                "Show the on-screen quest tracker (right side, under the territory box). Hides when you have no accepted quests and on the vanilla Ctrl+F3 hide-HUD toggle.");
            configQuestTrackerPosX = config.Bind("UI.Hud", "QuestTracker PosX", -12f, new BepInEx.Configuration.ConfigDescription("Quest tracker X offset from the screen's top-right anchor. Set by dragging the panel with the pause menu open.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            configQuestTrackerPosY = config.Bind("UI.Hud", "QuestTracker PosY", -410f, new BepInEx.Configuration.ConfigDescription("Quest tracker Y offset from the screen's top-right anchor (negative = below anchor). Set by dragging the panel with the pause menu open.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            configQuestTrackerCollapsed = config.Bind("UI.Hud", "QuestTracker Collapsed", false,
                "When true the tracker starts collapsed to just the book icon; click the icon to expand. Toggling in-game persists here.");
            configQuestTrackerUserPositioned = config.Bind("UI.Hud", "QuestTracker UserPositioned", false,
                "Once you drag the tracker it stops auto-stacking under the group HUD and stays where you dropped it (PosX/PosY). " +
                "Set back to false to resume auto-stacking below the group HUD.");

            // Territory HUD box — drag to reposition with the pause menu open.
            configTerritoryHudPosX = config.Bind("UI.Hud", "Territory PosX", -8f, new BepInEx.Configuration.ConfigDescription("Territory box X offset from the minimap's bottom-right anchor. Set by dragging with the pause menu open.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            configTerritoryHudPosY = config.Bind("UI.Hud", "Territory PosY", -8f, new BepInEx.Configuration.ConfigDescription("Territory box Y offset from the minimap's bottom-right anchor. Set by dragging with the pause menu open.", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));

            // Companion Behavior Settings

            // ────── Companion Persistence (Synced) ──────
            // True (default): companions live in the world like vanilla tames. Logout
            // releases ZDO ownership instead of destroying the companion; login adopts
            // the existing ZDOs and teleports followers. Vault spawn-from-prefab is
            // the fallback for first-tame, dismissed-recall, and migration of saves
            // made under the old destroy-on-logout flow.
            //
            // False (legacy): destroy all companions on logout, save snapshot to vault,
            // re-spawn fresh from vault on login. Smaller server memory/CPU footprint
            // (zones for offline players' companions don't stay loaded), but companions
            // can't be persistent participants in the world (e.g. die to a graydwarf
            // attack while their owner is offline). Recommended only for low-spec
            // dedicated hosts.

            configStaticNpcInvulnerable = config.Bind("Companions.StaticNpcs", "StaticNpcInvulnerable", true,
                // The default for NPCs whose Invincible toggle was never set in the NPC book.
                "Default for placed NPCs whose Invincible toggle was never set in the NPC book. On (default): they can't be killed " +
                "and aren't auto-revived, so a hammer-delete stays permanent. Off: they can be killed and come back at their placed " +
                "spot after 60 seconds. An NPC's own Invincible toggle overrides this. [Synced with Server]");

            configStaticNpcNameplateMode = config.Bind("Companions.StaticNpcs", "NameplateStyle", StaticNpcNameplateMode.HealthBar,
                "How static NPCs display their overhead identity. HealthBar = the vanilla floating plate " +
                "(name + health bar; each NPC's hide/TextSize/TextHeight settings still apply). HoverName = " +
                "the Marketplace-style raised world-space name text above the head (the vanilla plate is fully " +
                "hidden; each NPC's TextSize/TextHeight tune the label exactly like kg's). Hide = no overhead " +
                "display at all. Applies live, no restart needed. [Synced with Server]");

            configDialogueBgOpacity = config.Bind("UI", "DialogueBackgroundOpacity", 1.0f, new BepInEx.Configuration.ConfigDescription("Background opacity of the NPC dialogue panel (0.1 = nearly transparent, 1.0 = solid). " +
                "Per-player visual preference; applies the next time a dialogue opens.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));
            configInfoBgOpacity = config.Bind("UI", "InfoBackgroundOpacity", 1.0f, new BepInEx.Configuration.ConfigDescription("Background opacity of the Info NPC panel (0.1 = nearly transparent, 1.0 = solid). " +
                "Per-player visual preference; applies the next time the panel opens.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));

            // ────── UI Fonts ──────
            // Font options appear as dropdowns in config managers
            configUIPrimaryFont = config.Bind("UI", "PrimaryFont", UIFontOption.Valheim_AveriaSans,
                "Primary font used for headers, titles, and main UI text.\n" +
                "Valheim_AveriaSans = Clean sans-serif (default)\n" +
                "Valheim_Prstartk = Retro 80s pixel-style\n" +
                "Valheim_AveriaSerif = Elegant serif font\n" +
                "Valheim_Norse = Viking runic decorative\n" +
                "Valheim_Rune = Runic symbols");
            configUIDecorativeFont = config.Bind("UI", "DecorativeFont", UIFontOption.Valheim_Norse,
                "Decorative font used for quest titles, section headers, and Viking-themed text.\n" +
                "Valheim_Norse = Viking runic decorative (default)");
            configUIBodyFont = config.Bind("UI", "BodyFont", UIFontOption.Valheim_AveriaSerif,
                "Body font used for descriptions, buttons, and general readable text.\n" +
                "Valheim_AveriaSerif = Elegant serif font (default)");

            // ────── Progression (Synced) ──────
            configUsePrivateKeys = config.Bind("Progression.Keys", "UsePrivateKeys", true,
                "Use per-player private keys for boss progression instead of global server-wide keys. [Synced with Server]");
            configBlockAllGlobalKeys = config.Bind("Progression.Keys", "BlockAllGlobalKeys", true,
                "Block all boss keys from the global list (redirected to private keys instead). [Synced with Server]");
            configBlockedGlobalKeys = config.Bind("Progression.Keys", "BlockedGlobalKeys", "",
                "NOT keyboard keys: comma-separated Valheim progress flags (global keys such as defeated_eikthyr) to block (when BlockAllGlobalKeys is false). [Synced with Server]");
            configAllowedGlobalKeys = config.Bind("Progression.Keys", "AllowedGlobalKeys", "",
                "NOT keyboard keys: comma-separated Valheim progress flags (global keys such as defeated_eikthyr) to allow through (when BlockAllGlobalKeys is true). [Synced with Server]");
            configEnforcedGlobalKeys = config.Bind("Progression.Keys", "EnforcedGlobalKeys", "",
                "NOT keyboard keys: comma-separated Valheim progress flags (global keys such as defeated_eikthyr) to always add on server startup. [Synced with Server]");
            configBlockedPrivateKeys = config.Bind("Progression.Keys", "BlockedPrivateKeys", "",
                "NOT keyboard keys: comma-separated private progress flags (per-player keys such as defeated_eikthyr) that players cannot receive. [Synced with Server]");
            configAllowedPrivateKeys = config.Bind("Progression.Keys", "AllowedPrivateKeys", "",
                "NOT keyboard keys: comma-separated private progress flags (per-player keys such as defeated_eikthyr) allowed (if set, all others are blocked). [Synced with Server]");
            configEnforcedPrivateKeys = config.Bind("Progression.Keys", "EnforcedPrivateKeys", "",
                "NOT keyboard keys: comma-separated private progress flags (per-player keys such as defeated_eikthyr) to always give players on login. [Synced with Server]");
            configUsePrivateRaids = config.Bind("Progression.Keys", "UsePrivateRaids", true,
                "Use per-player private keys for raid events instead of global server keys. [Synced with Server]");
            configAdminBypassProgression = config.Bind("Progression.Locking", "AdminBypass", true,
                "Allow admins to bypass all progression locks. [Synced with Server]");
            configUseBlockedMessage = config.Bind("Progression.Locking", "UseBlockedMessage", true,
                "Show a message when an action is blocked by progression. [Synced with Server]");
            configBlockedMessage = config.Bind("Progression.Locking", "BlockedMessage", "You are not ready for this yet.",
                "Message displayed when an action is blocked. [Synced with Server]");
            configUseBlockedEffect = config.Bind("Progression.Locking", "UseBlockedEffect", false,
                "Apply a burning effect when an action is blocked (off by default). [Synced with Server]");
            configLockEquipment = config.Bind("Progression.Locking", "LockEquipment", false,
                "Lock equipping items behind a required key. OFF by default — only cheat items are blocked. [Synced with Server]");
            configLockEquipmentKey = config.Bind("Progression.Locking", "LockEquipmentKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to equip items (global, private, custom player, or EWP key). Only used when LockEquipment is true. [Synced with Server]");
            configLockCrafting = config.Bind("Progression.Locking", "LockCrafting", false,
                "Lock crafting behind a required key. OFF by default. [Synced with Server]");
            configLockCraftingKey = config.Bind("Progression.Locking", "LockCraftingKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to craft items. Only used when LockCrafting is true. [Synced with Server]");
            configLockBuilding = config.Bind("Progression.Locking", "LockBuilding", false,
                "Lock building behind a required key. OFF by default. [Synced with Server]");
            configLockBuildingKey = config.Bind("Progression.Locking", "LockBuildingKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to build. Only used when LockBuilding is true. [Synced with Server]");
            configLockCooking = config.Bind("Progression.Locking", "LockCooking", false,
                "Lock cooking behind a required key. OFF by default. [Synced with Server]");
            configLockCookingKey = config.Bind("Progression.Locking", "LockCookingKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to cook. Only used when LockCooking is true. [Synced with Server]");
            configLockEating = config.Bind("Progression.Locking", "LockEating", false,
                "Lock eating behind a required key. OFF by default. [Synced with Server]");
            configLockEatingKey = config.Bind("Progression.Locking", "LockEatingKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to eat. Only used when LockEating is true. [Synced with Server]");
            configLockTaming = config.Bind("Progression.Locking", "LockTaming", false,
                "Lock taming creatures behind a required key. OFF by default. [Synced with Server]");
            configLockTamingKey = config.Bind("Progression.Locking", "LockTamingKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to tame. Only used when LockTaming is true. [Synced with Server]");
            configLockGuardianPower = config.Bind("Progression.Locking", "LockGuardianPower", false,
                "Lock guardian power usage behind a required key. OFF by default. [Synced with Server]");
            configLockGuardianPowerKey = config.Bind("Progression.Locking", "LockGuardianPowerKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to use guardian powers. Only used when LockGuardianPower is true. [Synced with Server]");
            configLockBossSummons = config.Bind("Progression.Locking", "LockBossSummons", false,
                "Lock boss summoning behind a required key. OFF by default. [Synced with Server]");
            configLockBossSummonsKey = config.Bind("Progression.Locking", "LockBossSummonsKey", "",
                "NOT a keyboard key: the progress flag (a Valheim global key such as defeated_eikthyr, a private key, a custom player key or an EWP key) required to summon bosses. Only used when LockBossSummons is true. [Synced with Server]");

            ApplyProgressionConfig();

            // ────── Heightmap Override ──────



            // ────── Piece Recipes (Synced) ──────
            foreach (var pieceName in VAPieceManager.PieceNames)
            {
                string section = $"Pieces.{pieceName}";
                configPerPieceRecipes[pieceName] = config.Bind(section, "Recipe",
                    DefaultPieceRecipe,
                    $"Recipe string for {pieceName} (name,amount,perLevel,…). A " +
                    $"{FiresCore.Items.AdminRecipeGate.GateItemName} is always required on top of " +
                    "whatever is listed here — it cannot be removed by editing this value.");
                configPerPieceEnabled[pieceName] = config.Bind(section, "Enabled", true,
                    $"Enable piece {pieceName}.");

                // One-time migration off the pre-gate default (see above).
                if (configPerPieceRecipes[pieceName].Value == LegacyDefaultPieceRecipe)
                    configPerPieceRecipes[pieceName].Value = DefaultPieceRecipe;
            }

            ValidateConfigs();
            RemoveRetiredEntries(config);
            ConfigMoves.Apply(config);   // Merged sections keep their values
            GroupHudOwned.Bind(this, config);   // 0.0.804: Core's group HUD keys are owned here now
            SetupConfigWatcher(config);
        }

        // Entries this mod no longer binds stay in the .cfg forever and read like live
        // settings - the [Guilds] block and [Hotkeys] GuildScreenKey (guilds moved to FiresGuilds, which owns G now). Removed once, logged.
        // Pure dead keys: nothing reads them, so there is nothing to migrate.
        private static void RemoveRetiredEntries(ConfigFile config)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config, null) as Dictionary<ConfigDefinition, string>;
                if (orphans == null) return;
                var dead = orphans.Keys.Where(d => d.Section == "Guilds" || (d.Section == "Hotkeys" && d.Key == "GuildScreenKey")
                    || (d.Section == "Companions" && d.Key == "ShowGroupHud")).ToList();   // 0.0.804: the dead group HUD copy
                if (dead.Count == 0) return;
                foreach (var d in dead) orphans.Remove(d);
                config.Save();
                global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] config: removed {dead.Count} retired entr{(dead.Count == 1 ? "y" : "ies")} (guilds moved to FiresGuilds): {string.Join(", ", dead.Select(d => $"[{d.Section}] {d.Key}").ToArray())}");
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] config: retired-entry cleanup skipped: {ex.Message}"); }
        }

        private void SetupConfigWatcher(ConfigFile config)
        {
            if (configWatcher != null) return;

            string configPath = config.ConfigFilePath;
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;

            string directory = Path.GetDirectoryName(configPath);
            string fileName = Path.GetFileName(configPath);

            config.SettingChanged -= SelfWriteStamp;
            config.SettingChanged += SelfWriteStamp;

            configWatcher = new FileSystemWatcher
            {
                Path = directory,
                Filter = fileName,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            configWatcher.Changed += (sender, e) =>
            {
                if (_isShuttingDown || ConfigSync.ProcessingServerUpdate) return;
                if (IsRecentSelfWrite()) return;
                if (_reloadCoroutine != null) return;
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(ReloadConfigAfterDelay());
            };

            configWatcher.Renamed += (sender, e) =>
            {
                if (_isShuttingDown || ConfigSync.ProcessingServerUpdate) return;
                if (IsRecentSelfWrite()) return;
                if (_reloadCoroutine != null) return;
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(ReloadConfigAfterDelay());
            };
        }

        private const float ReloadDelaySeconds = 2f;
        private const int MinReloadIntervalMs = 2000;

        private IEnumerator ReloadConfigAfterDelay()
        {
            yield return new WaitForSeconds(ReloadDelaySeconds);

            if (_isShuttingDown || ConfigSync.ProcessingServerUpdate)
            {
                _reloadCoroutine = null;
                yield break;
            }

            if (IsRecentSelfWrite()) { _reloadCoroutine = null; yield break; }
            if ((DateTime.Now - _lastConfigUpdate).TotalMilliseconds < MinReloadIntervalMs)
            {
                _reloadCoroutine = null;
                yield break;
            }

            _lastConfigUpdate = DateTime.Now;

            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Config file changed, reloading...");

            var configFile = FiresRPGmaker.Instance.Config;
            bool saveOnSet = configFile.SaveOnConfigSet;
            configFile.SaveOnConfigSet = false;

            FiresRPGmaker.Instance.Config.Reload();

            ValidateConfigs();

            configFile.SaveOnConfigSet = saveOnSet;

            // Refresh quest-related systems after config reload
            try
            {
                VARecipeManager.UpdatePieceRequirements();

                // Clear UI font cache so new font settings take effect
                FiresCore.UI.UIFontConfig.ClearCache();
                FiresCore.UI.UIBuilderHelper.ClearFontCache();

                // Optional: reload quest definitions if they depend on config (rare, but safe)
                if (QuestManager.Instance != null)
                {
                    QuestManager.Instance.LoadQuestDatabase();
                    QuestManager.Instance.LoadQuestProfiles();
                }

                // Optional: notify Vault if needed (e.g. if quests affect stats)
                if (VaultOfKnowledge.Instance != null)
                {
                    VaultOfKnowledge.Instance.MarkServerDirty();
                    VaultOfKnowledge.Instance.SaveAllDirty();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Error refreshing systems after config reload: {ex.Message}");
            }

            if (configVerboseLogging.Value)
                global::FiresRPGmaker.FiresLogger.LogDebug($"{FiresRPGmaker.PluginName}: Config reloaded — recipes preserved.");

            _reloadCoroutine = null;
        }

        public void ValidateConfigs()
        {
            foreach (var kv in configPerPieceRecipes)
            {
                var parts = kv.Value.Value.Split(',').Select(s => s.Trim()).ToArray();
                bool valid = (parts.Length % 3 == 0 && parts.All(p => !string.IsNullOrEmpty(p))) ||
                             (parts.Length % 2 == 0 && parts.All(p => !string.IsNullOrEmpty(p)));
                if (!valid)
                    kv.Value.Value = (string)kv.Value.DefaultValue;
            }

            // Re-apply bubble config after validation

            // Re-apply auto water config after validation

            // Re-apply progression config after validation
            ApplyProgressionConfig();
        }



        /// <summary>
        /// Applies progression config entries to the runtime ProgressionConfig singleton.
        /// </summary>
        private void ApplyProgressionConfig()
        {
            try
            {
                var cfg = ProgressionConfig.Instance;
                cfg.UsePrivateKeys       = configUsePrivateKeys?.Value ?? true;
                cfg.BlockAllGlobalKeys   = configBlockAllGlobalKeys?.Value ?? true;
                cfg.UsePrivateRaids      = configUsePrivateRaids?.Value ?? true;
                cfg.AdminBypass          = configAdminBypassProgression?.Value ?? true;
                cfg.UseBlockedMessage    = configUseBlockedMessage?.Value ?? true;
                cfg.BlockedMessage       = configBlockedMessage?.Value ?? "You are not ready for this yet.";
                cfg.UseBlockedEffect     = configUseBlockedEffect?.Value ?? false;
                cfg.LockEquipment        = configLockEquipment?.Value ?? false;
                cfg.LockEquipmentKey     = configLockEquipmentKey?.Value ?? "";
                cfg.LockCrafting         = configLockCrafting?.Value ?? false;
                cfg.LockCraftingKey      = configLockCraftingKey?.Value ?? "";
                cfg.LockBuilding         = configLockBuilding?.Value ?? false;
                cfg.LockBuildingKey      = configLockBuildingKey?.Value ?? "";
                cfg.LockCooking          = configLockCooking?.Value ?? false;
                cfg.LockCookingKey       = configLockCookingKey?.Value ?? "";
                cfg.LockEating           = configLockEating?.Value ?? false;
                cfg.LockEatingKey        = configLockEatingKey?.Value ?? "";
                cfg.LockTaming           = configLockTaming?.Value ?? false;
                cfg.LockTamingKey        = configLockTamingKey?.Value ?? "";
                cfg.LockGuardianPower    = configLockGuardianPower?.Value ?? false;
                cfg.LockGuardianPowerKey = configLockGuardianPowerKey?.Value ?? "";
                cfg.LockBossSummons      = configLockBossSummons?.Value ?? false;
                cfg.LockBossSummonsKey   = configLockBossSummonsKey?.Value ?? "";

                cfg.UpdateFromStrings(
                    configBlockedGlobalKeys?.Value ?? "",
                    configAllowedGlobalKeys?.Value ?? "",
                    configEnforcedGlobalKeys?.Value ?? "",
                    configBlockedPrivateKeys?.Value ?? "",
                    configAllowedPrivateKeys?.Value ?? "",
                    configEnforcedPrivateKeys?.Value ?? ""
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConfigManager] Failed to apply progression config: {ex.Message}");
            }
        }

        public void OnConfigLockChanged()
        {
            ValidateConfigs();
            VARecipeManager.UpdatePieceRequirements();

            if (configVerboseLogging.Value)
                global::FiresRPGmaker.FiresLogger.LogDebug($"{FiresRPGmaker.PluginName}: Config lock changed.");
        }

        public void Dispose()
        {
            _isShuttingDown = true;
            configWatcher?.Dispose();
        }
    }
}