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
        private bool _isShuttingDown = false;
        private DateTime _lastConfigUpdate = DateTime.MinValue;
        private Coroutine _reloadCoroutine;

        // ────── General Settings ──────
        public ConfigEntry<bool> configVerboseLogging;
        public ConfigEntry<bool> configServerAuthority;

        // ────── Hotkeys ──────
        public ConfigEntry<KeyCode> configQuestJournalHotkey;
        public ConfigEntry<KeyCode> configGuildHotkey;

        // ────── Guilds ──────
        public ConfigEntry<bool> configGuildsEnabled;
        public ConfigEntry<bool> configAllowGuildCreation;
        public ConfigEntry<int>  configMaxGuildMembers;
        public ConfigEntry<int>  configMaxGuildsPerWorld;

        // ────── Territories ──────
        public ConfigEntry<bool> configAdminBypassTerritory;
        public ConfigEntry<bool> configTerritoryVisualizers;
        public ConfigEntry<bool> configTerritoryOverlay;
        public ConfigEntry<bool> configDeepNorthMapOverlay;

        // ────── Companions ──────
        public ConfigEntry<bool> configShowCompanionBillboard;
        public ConfigEntry<bool> configShowGroupHud;
        public ConfigEntry<bool> configUseGlobeHud;
        public ConfigEntry<bool> configUseBookAdminUI;

        // Group HUD layout / position
        public ConfigEntry<int>   configGroupHudMaxRowsPerColumn;
        public ConfigEntry<int>   configGroupHudColumns;
        public ConfigEntry<float> configGroupHudDefaultPosX;
        public ConfigEntry<float> configGroupHudDefaultPosY;

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

        // When true, hammer-placed static NPCs cannot be killed (immune to all damage). When false,
        // they are killable and respawn at their placed position after 60s. Server-authoritative.
        public ConfigEntry<bool> configStaticNpcInvulnerable;



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
            configQuestJournalHotkey = config.Bind("Hotkeys", "QuestJournalKey", KeyCode.J,
                "Hotkey to open the Quest Journal. Set to None to disable.");
            configGuildHotkey = config.Bind("Hotkeys", "GuildScreenKey", KeyCode.G,
                "Hotkey to open the Guild screen. Set to None to disable.");

            // ────── Guilds ──────
            configGuildsEnabled = config.Bind("Guilds", "GuildsEnabled", true,
                "Enable the guild system. When false, all guild commands and UI are disabled. [Synced with Server]");
            configAllowGuildCreation = config.Bind("Guilds", "AllowGuildCreation", true,
                "Allow players to create new guilds. Set to false to lock the world to existing guilds only. [Synced with Server]");
            configMaxGuildMembers = config.Bind("Guilds", "MaxGuildMembers", 20,
                new ConfigDescription(
                    "Maximum number of members a single guild can have. Range: 1-200. [Synced with Server]",
                    new AcceptableValueRange<int>(1, 200)));
            configMaxGuildsPerWorld = config.Bind("Guilds", "MaxGuildsPerWorld", 100,
                new ConfigDescription(
                    "Maximum total guilds allowed on the server at once. Range: 1-1000. [Synced with Server]",
                    new AcceptableValueRange<int>(1, 1000)));

            // ────── Territories ──────
            configAdminBypassTerritory = config.Bind("Territories", "AdminBypassTerritoryRestrictions", true,
                "Allow admins to bypass territory restrictions such as NoBuild, NoTool, and pushback.");
            configTerritoryVisualizers = config.Bind("Territories", "EnableWorldVisualizers", false,
                "Draw 3D world debug visualizers for territories (for overlap/height debugging).");
            configTerritoryOverlay = config.Bind("Territories", "EnableMapOverlay", true,
                "Draw the territory overlay on the minimap. Disable to test/avoid overlay-related stutter.");
            configDeepNorthMapOverlay = config.Bind("Territories", "EnableDeepNorthMapOverlay", true,
                "Draw the Deep North icy-blue tint and water line on the minimap. " +
                "Disable if using Better Continents or another mod that provides its own map visuals for the Deep North.");

            // ────── Companions ──────
            configShowCompanionBillboard = config.Bind("Companions", "ShowBillboardHealthBar", true,
                "Show the health bar and name floating above companions' heads.");
            configShowGroupHud = config.Bind("Companions", "ShowGroupHud", true,
                "Show the group HUD panel on the right side of the screen with companion health/stamina bars.");
            configUseGlobeHud = config.Bind("UI", "UseGlobeHud", true,
                "Enable the custom globe-style Health/Stamina/Eitr/Adrenaline HUD overlay. " +
                "Set to false to hide the globe entirely and use the vanilla Valheim HUD bars instead.");
            configUseBookAdminUI = config.Bind("UI", "UseBookAdminPanel", true,
                "Use the new two-page parchment 'book' NPC admin config panel (structured per-module forms). " +
                "When disabled, the legacy completecompanionsui admin panel is used instead.");

            // Group HUD layout — panel stretches to fit the actual group up to MaxRowsPerColumn,
            // then becomes scrollable. Columns 1 = single list, 2 = side-by-side.
            configGroupHudMaxRowsPerColumn = config.Bind("UI.GroupHud", "MaxRowsPerColumn", 8,
                "Maximum rows visible per column before the group HUD panel scrolls. " +
                "The panel auto-shrinks to fit smaller groups; this value is just the upper cap. " +
                "Range: 1-20.");
            configGroupHudColumns = config.Bind("UI.GroupHud", "Columns", 1,
                "Number of columns in the group HUD. 1 = single list, 2 = side-by-side. Range: 1-2.");
            configGroupHudDefaultPosX = config.Bind("UI.GroupHud", "DefaultPosX", -10f,
                "Default panel X offset from the screen's top-right anchor (negative = left of anchor). " +
                "Used when the player has not yet dragged the panel, and when ResetPosition is invoked.");
            configGroupHudDefaultPosY = config.Bind("UI.GroupHud", "DefaultPosY", -200f,
                "Default panel Y offset from the screen's top-right anchor (negative = below anchor). " +
                "Used when the player has not yet dragged the panel, and when ResetPosition is invoked.");

            // Companion Behavior Settings
            configCompanionIdleWanderRadius = config.Bind("Companions.Behavior", "IdleWanderRadius", 20f,
                "Default radius (in meters) that companions will wander from their home position when in Stay mode. " +
                "Range: 5-100. Individual NPCs can override this via their ZDO settings.");
            configCompanionReturnHomeRadius = config.Bind("Companions.Behavior", "ReturnHomeRadius", 25f,
                "Radius (in meters) beyond which companions will automatically return to their home position. " +
                "This is the single source of truth for all return-home logic. Range: 10-100.");
            configCompanionChestSearchRadius = config.Bind("Companions.Behavior", "ChestSearchRadius", 50f,
                "Radius (in meters) to search for chests when companions operate smelters, kilns, or deposit items. " +
                "Range: 5-100.");
            configCompanionStayModeWorkRadius = config.Bind("Companions.Behavior", "StayModeWorkRadius", 50f,
                "Radius (in meters) that staying companions search for work opportunities (smelters, fires, resources, etc.). " +
                "Range: 10-100.");
            configChestAutoSortRadius = config.Bind("Companions.Behavior", "ChestAutoSortRadius", 10f,
                "Radius (in meters) for auto-sorting items across multiple chests when interacting with a single chest. " +
                "Range: 5-30.");
            configCompanionStationedDefendRange = config.Bind("Companions.Behavior", "StationedDefendRange", 14f,
                "Aggro radius (in meters) around a stationed/patrol NPC's post within which it engages hostile enemies. " +
                "Range: 4-40.");
            configCompanionStationedLeashRange = config.Bind("Companions.Behavior", "StationedLeashRange", 24f,
                "Distance (in meters) from its post past which a stationed/patrol NPC gives up pursuit and returns to guard. " +
                "Keep this larger than StationedDefendRange. Range: 6-60.");
            configCompanionStationedCombatHold = config.Bind("Companions.Behavior", "StationedCombatHold", 10f,
                "How long (in seconds) a stationed/patrol NPC stays engaged/alert after the last threat clears before " +
                "resuming patrol/idle. Range: 0-60.");

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
            configPersistentCompanions = config.Bind("Companions.Persistence", "PersistentCompanions", true,
                "When enabled, companions persist in the world across logouts (like vanilla tames) " +
                "instead of being destroyed and re-spawned. Server-authoritative — set on the host. " +
                "Set to false to revert to legacy destroy-on-logout behavior on low-spec servers. " +
                "[Synced with Server]");
            configKeepZonesLoadedForOfflinePlayers = config.Bind("Companions.Persistence", "KeepZonesLoadedForOfflinePlayers", true,
                "When enabled (and PersistentCompanions is true), the server keeps zones loaded " +
                "around every tamed companion in the world — including those belonging to offline " +
                "players. Required for companions to behave as living entities (combat, idle behaviors, " +
                "wandering) while their owner is logged out. Costs server memory and CPU proportional " +
                "to total tamed-companion count. Disable for low-spec servers; companions still " +
                "persist as dormant ZDOs and reanimate when somebody is nearby. [Synced with Server]");

            configStaticNpcInvulnerable = config.Bind("Companions.StaticNpcs", "StaticNpcInvulnerable", false,
                "When enabled, hammer-placed static NPCs are invulnerable — they cannot be killed by " +
                "combat, the K/killall debug commands, or anything else. When disabled (default), static " +
                "NPCs are killable and automatically respawn at their placed position after 60 seconds. " +
                "Admin-controlled, server-authoritative. [Synced with Server]");

            // ────── UI Fonts ──────
            // Font options appear as dropdowns in config managers
            configUIPrimaryFont = config.Bind("UI.Fonts", "PrimaryFont", UIFontOption.Valheim_AveriaSans,
                "Primary font used for headers, titles, and main UI text.\n" +
                "Valheim_AveriaSans = Clean sans-serif (default)\n" +
                "Valheim_Prstartk = Retro 80s pixel-style\n" +
                "Valheim_AveriaSerif = Elegant serif font\n" +
                "Valheim_Norse = Viking runic decorative\n" +
                "Valheim_Rune = Runic symbols");
            configUIDecorativeFont = config.Bind("UI.Fonts", "DecorativeFont", UIFontOption.Valheim_Norse,
                "Decorative font used for quest titles, section headers, and Viking-themed text.\n" +
                "Valheim_Norse = Viking runic decorative (default)");
            configUIBodyFont = config.Bind("UI.Fonts", "BodyFont", UIFontOption.Valheim_AveriaSerif,
                "Body font used for descriptions, buttons, and general readable text.\n" +
                "Valheim_AveriaSerif = Elegant serif font (default)");

            // ────── Progression (Synced) ──────
            configUsePrivateKeys = config.Bind("Progression.Keys", "UsePrivateKeys", true,
                "Use per-player private keys for boss progression instead of global server-wide keys. [Synced with Server]");
            configBlockAllGlobalKeys = config.Bind("Progression.Keys", "BlockAllGlobalKeys", true,
                "Block all boss keys from the global list (redirected to private keys instead). [Synced with Server]");
            configBlockedGlobalKeys = config.Bind("Progression.Keys", "BlockedGlobalKeys", "",
                "Comma-separated list of specific global keys to block (when BlockAllGlobalKeys is false). [Synced with Server]");
            configAllowedGlobalKeys = config.Bind("Progression.Keys", "AllowedGlobalKeys", "",
                "Comma-separated list of global keys to allow through (when BlockAllGlobalKeys is true). [Synced with Server]");
            configEnforcedGlobalKeys = config.Bind("Progression.Keys", "EnforcedGlobalKeys", "",
                "Comma-separated list of global keys to always add on server startup. [Synced with Server]");
            configBlockedPrivateKeys = config.Bind("Progression.Keys", "BlockedPrivateKeys", "",
                "Comma-separated list of private keys that players cannot receive. [Synced with Server]");
            configAllowedPrivateKeys = config.Bind("Progression.Keys", "AllowedPrivateKeys", "",
                "Comma-separated list of private keys allowed (if set, all others are blocked). [Synced with Server]");
            configEnforcedPrivateKeys = config.Bind("Progression.Keys", "EnforcedPrivateKeys", "",
                "Comma-separated list of private keys to always give players on login. [Synced with Server]");
            configUsePrivateRaids = config.Bind("Progression.Raids", "UsePrivateRaids", true,
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
                "Key name required to equip items (global, private, custom player, or EWP key). Only used when LockEquipment is true. [Synced with Server]");
            configLockCrafting = config.Bind("Progression.Locking", "LockCrafting", false,
                "Lock crafting behind a required key. OFF by default. [Synced with Server]");
            configLockCraftingKey = config.Bind("Progression.Locking", "LockCraftingKey", "",
                "Key name required to craft items. Only used when LockCrafting is true. [Synced with Server]");
            configLockBuilding = config.Bind("Progression.Locking", "LockBuilding", false,
                "Lock building behind a required key. OFF by default. [Synced with Server]");
            configLockBuildingKey = config.Bind("Progression.Locking", "LockBuildingKey", "",
                "Key name required to build. Only used when LockBuilding is true. [Synced with Server]");
            configLockCooking = config.Bind("Progression.Locking", "LockCooking", false,
                "Lock cooking behind a required key. OFF by default. [Synced with Server]");
            configLockCookingKey = config.Bind("Progression.Locking", "LockCookingKey", "",
                "Key name required to cook. Only used when LockCooking is true. [Synced with Server]");
            configLockEating = config.Bind("Progression.Locking", "LockEating", false,
                "Lock eating behind a required key. OFF by default. [Synced with Server]");
            configLockEatingKey = config.Bind("Progression.Locking", "LockEatingKey", "",
                "Key name required to eat. Only used when LockEating is true. [Synced with Server]");
            configLockTaming = config.Bind("Progression.Locking", "LockTaming", false,
                "Lock taming creatures behind a required key. OFF by default. [Synced with Server]");
            configLockTamingKey = config.Bind("Progression.Locking", "LockTamingKey", "",
                "Key name required to tame. Only used when LockTaming is true. [Synced with Server]");
            configLockGuardianPower = config.Bind("Progression.Locking", "LockGuardianPower", false,
                "Lock guardian power usage behind a required key. OFF by default. [Synced with Server]");
            configLockGuardianPowerKey = config.Bind("Progression.Locking", "LockGuardianPowerKey", "",
                "Key name required to use guardian powers. Only used when LockGuardianPower is true. [Synced with Server]");
            configLockBossSummons = config.Bind("Progression.Locking", "LockBossSummons", false,
                "Lock boss summoning behind a required key. OFF by default. [Synced with Server]");
            configLockBossSummonsKey = config.Bind("Progression.Locking", "LockBossSummonsKey", "",
                "Key name required to summon bosses. Only used when LockBossSummons is true. [Synced with Server]");

            ApplyProgressionConfig();

            // ────── Heightmap Override ──────



            // ────── Piece Recipes (Synced) ──────
            foreach (var pieceName in VAPieceManager.PieceNames)
            {
                string section = $"Pieces.{pieceName}";
                configPerPieceRecipes[pieceName] = config.Bind(section, "Recipe",
                    "ElderBark,40,Blackwood,40,TrollHide,20,BronzeNails,144,Iron,10",
                    $"Recipe string for {pieceName} (name,amount,perLevel,…).");
                configPerPieceEnabled[pieceName] = config.Bind(section, "Enabled", true,
                    $"Enable piece {pieceName}.");
            }

            ValidateConfigs();
            SetupConfigWatcher(config);
        }

        private void SetupConfigWatcher(ConfigFile config)
        {
            if (configWatcher != null) return;

            string configPath = config.ConfigFilePath;
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;

            string directory = Path.GetDirectoryName(configPath);
            string fileName = Path.GetFileName(configPath);

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
                if (_reloadCoroutine != null) return;
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(ReloadConfigAfterDelay());
            };

            configWatcher.Renamed += (sender, e) =>
            {
                if (_isShuttingDown || ConfigSync.ProcessingServerUpdate) return;
                if (_reloadCoroutine != null) return;
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(ReloadConfigAfterDelay());
            };
        }

        private IEnumerator ReloadConfigAfterDelay()
        {
            yield return new WaitForSeconds(2f);

            if (_isShuttingDown || ConfigSync.ProcessingServerUpdate)
            {
                _reloadCoroutine = null;
                yield break;
            }

            if ((DateTime.Now - _lastConfigUpdate).TotalMilliseconds < 2000)
            {
                _reloadCoroutine = null;
                yield break;
            }

            _lastConfigUpdate = DateTime.Now;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Config file changed, reloading..." +
                (isServer ? " (server — will broadcast all)" : " (client — ConfigSync handles broadcast)"));

            var configFile = FiresRPGmaker.Instance.Config;
            bool saveOnSet = configFile.SaveOnConfigSet;
            configFile.SaveOnConfigSet = false;

            FiresRPGmaker.Instance.Config.Reload();

            ValidateConfigs();

            // On the server: explicitly broadcast all synced configs to clients.
            // On clients: Config.Reload() already set each entry's Value, which fires
            // SettingChanged, which ConfigSync's per-entry handler picks up and broadcasts
            // to the server automatically — no bulk loop needed.
            if (isServer)
            {
                foreach (var kvp in FiresRPGmaker.Instance.Config)
                {
                    var entry = kvp.Value;
                    var synced = ConfigSync.configData(entry);
                    if (synced?.SynchronizedConfig == true)
                    {
                        FiresRPGmaker.Instance.configSync.Broadcast(ZRoutedRpc.Everybody, entry);
                    }
                }
            }

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
                Debug.Log($"{FiresRPGmaker.PluginName}: Config reloaded — recipes preserved.");

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
                Debug.Log($"{FiresRPGmaker.PluginName}: Config lock changed.");
        }

        public void Dispose()
        {
            _isShuttingDown = true;
            configWatcher?.Dispose();
        }
    }
}