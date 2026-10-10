using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Territories;
using FiresCore.Npc;
using FiresCore.Npc.Commands;
using FiresCore.Npc.Archetypes;
using FiresRPGmaker.Modules.Compendium;
using FiresRPGmaker.Modules.ChatSystem;

using FiresCore.Sync;
using FiresCore.Lifecycle;

namespace FiresRPGmaker
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.Fire.FiresWayshrines", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.Fire.VAInventory", BepInDependency.DependencyFlags.SoftDependency)]
    public class FiresRPGmaker : FiresMod<FiresRPGmaker>
    {
        public const string PluginGUID = "com.Fire.FiresRPGmaker";
        public const string PluginName = "FiresRPGmaker";
        public const string PluginVersion = "0.0.566";

        public static AssetBundle assetBundle;
        public static AssetBundle uiBundle;
        public static AssetBundle booksBundle;   // book models (props + the animated UI book), embedded alongside firesnpcsui
        public static AssetBundle chessBundle;   // the in-world chess table piece (vaitems_chessboard)
        public static AssetBundle propsBundle;   // mailbox + sign/board props (firesnpcs_props)
        public static AssetBundle leaderboardTabBundle;   // the inventory Leaderboards tab view (firesleaderboardtab, self-contained)
        public static readonly List<GameObject> piecePrefabs = [];
        public static readonly List<GameObject> itemPrefabs = [];
        public static GameObject RootObject;
        public static GameObject PrefabContainer { get; private set; }

        public ConfigSync configSync;

        private readonly System.Action<bool> _refreshConfigLockOnAdminChange =
            _ => ConfigManager.Instance?.OnConfigLockChanged();
        private static ILogHandler _originalLogHandler;
        internal static BepInEx.Logging.ManualLogSource Log;
        private static long _bundleLoadMs;
        private static Modules.Party.Bridge.PartyGroupProvider _groupProvider;

        protected override void Setup()
        {
            Log = Logger;
            try { FiresRPGmakerBanner.PrintBig(); } catch { }

            // Diagnostic - fires before any other init code or log filter install.
            // If this line doesn't appear in the log even though BepInEx says
            // "Loading [FiresRPGmaker X.X.X]", then Awake() never executed
            // (Unity scheduling issue, plugin instantiation failure, etc).
            Debug.Log($"[{PluginName}] Awake() entered - version {PluginVersion}");

            // Consolidate the old scattered config folders (firesnpcs/, FiresNPCs_Sounds/) into FiresRPGmaker/
            // before anything reads them this session.
            Managers.FiresConfigMigration.Run();

            // Install log filter FIRST, before any asset loading that might generate shader warnings
        InstallLogFilter();

            RootObject = new GameObject("_FiresRPGmakerRoot");
            UnityEngine.Object.DontDestroyOnLoad(RootObject);

      PrefabContainer = new GameObject("FiresRPGmaker_Prefabs");
      PrefabContainer.transform.parent = RootObject.transform;
            PrefabContainer.SetActive(false);

            configSync = new ConfigSync(PluginGUID)
         {
        DisplayName = PluginName,
        CurrentVersion = PluginVersion,
          MinimumRequiredVersion = PluginVersion
 };

   configSync.lockedConfigChanged += ConfigManager.Instance.OnConfigLockChanged;
            AdminSyncing.AdminStatusChanged += _refreshConfigLockOnAdminChange;

            ConfigManager.Instance.Initialize(Config);
            try { global::FiresRPGmaker.Modules.Pets.PetVisual.BindConfig(Config); } catch { }
            // 0.0.561: [wishbone] Pets.Progress joins the first config sync
            try { global::FiresRPGmaker.Modules.Pets.Progress.PetProgress.BindConfig(); } catch (Exception ex) { Logger.LogWarning($"PetProgress.BindConfig: {ex.Message}"); }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.TurnSecondsCfg = Config.Bind("Poker", "PlayerTurnSeconds", 60, "Seconds a seated human has to act at the poker table before auto-fold (min 10)."); } catch { }
            try { global::FiresRPGmaker.Modules.Poker.PokerAI.DifficultyCfg = Config.Bind("Poker", "Difficulty", "Medium", new BepInEx.Configuration.ConfigDescription("Poker AI table strength (hot-reloads).", new BepInEx.Configuration.AcceptableValueList<string>("Easy", "Medium", "Hard", "Master"))); } catch { }
            try
            {
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.CurrencyCfg = Config.Bind("Poker", "CashCurrency", "Coins", "Currency item played at open (cash) tables - bank it with the Banker to buy in.");
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.BuyInCfg = Config.Bind("Poker", "CashBuyIn", 500, "Open-table buy-in, withdrawn from your bank when you sit down.");
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.BigBlindCfg = Config.Bind("Poker", "CashBigBlind", 10, "Open-table big blind.");
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.DifficultyCfg = Config.Bind("Chess", "Difficulty", "Medium", new BepInEx.Configuration.ConfigDescription("Chess AI strength (hot-reloads; a firesnpcs_chess depth argument overrides it).", new BepInEx.Configuration.AcceptableValueList<string>("Easy", "Medium", "Hard", "Master"))); } catch { }
            try { global::FiresRPGmaker.UI.Books.FiresBookView.DebugCapture = Config.Bind("Debug", "BookViewCaptures", false, "Dev tool (off by default): when a 3D book view opens, dump render PNGs + a state file to BepInEx/config/FiresRPGmaker/Debug/BookView. Off avoids the login capture stall + [FiresBookView] log spam.").Value; } catch { }
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.UiScaleCfg = Config.Bind("Poker", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Poker window scale (drag the corner grip in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f)));
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.OffsetXCfg = Config.Bind("Poker", "OffsetX", 0f, "Poker window horizontal position (drag the title bar in-game).");
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.OffsetYCfg = Config.Bind("Poker", "OffsetY", 0f, "Poker window vertical position (drag the title bar in-game).");
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.UiScaleCfg = Config.Bind("Chess", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Chess window scale - stretches the whole board UI (hot-reloads).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f))); } catch { }
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.OffsetXCfg = Config.Bind("Chess", "OffsetX", 0f, "Chess window horizontal position (drag the title bar in-game).");
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.OffsetYCfg = Config.Bind("Chess", "OffsetY", 0f, "Chess window vertical position (drag the title bar in-game).");
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.UiScaleCfg = Config.Bind("Fanorona", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Fanorona window scale - stretches the whole board UI (hot-reloads).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f))); } catch { }
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.OffsetXCfg = Config.Bind("Fanorona", "OffsetX", 0f, "Fanorona window horizontal position (drag the title bar in-game).");
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.OffsetYCfg = Config.Bind("Fanorona", "OffsetY", 0f, "Fanorona window vertical position (drag the title bar in-game).");
            }
            catch { }
 BindConfigsToSync();


            // Restore persisted chat channel preference (client-only, not synced).
            ChatChannelManager.InitConfig(Config);

        // Load assets AFTER log filter is installed
            LoadAssets();

            // Wire the Core NPC engine's host-service bridges to RPGMaker implementations.
            Integration.NpcBridgeProviders.RegisterAll();

            // Expose party membership to the rest of the family via the Core bridge.
            _groupProvider = new Modules.Party.Bridge.PartyGroupProvider();
            FiresCore.Bridge.GroupBridge.Register(_groupProvider);

            // Offer the Marketplace achievements to any mod that shows achievements (FiresRPGClasses).
            Modules.Marketplace.AchievementCatalogue.Register();

            // Patrol route editing (client-only): Shift+Right-click a visualized route node to open the baked
            // Patrol Route Editor book - stop times, section speed sliders, node + route (hide/save) actions.
            if (!Application.isBatchMode)
                UI.Screens.Patrol.PatrolEditorDriver.Ensure();

            // Admin map-painting territory editor (client-only): Shift+Alt+Right-click the open map to toggle.
            if (!Application.isBatchMode)
                Modules.Territories.Paint.TerritoryPaintDriver.Ensure();

            // Load custom NPC mp3 sounds from BepInEx/config/FiresRPGmaker/Sounds (KG-style). The coroutine
            // self-guards on a headless/dedicated server (no audio output there).
            try
            {
                string soundsFolder = Path.Combine(FiresCore.Storage.FiresConfigPaths.Sounds);
                Directory.CreateDirectory(soundsFolder);
                StartCoroutine(FiresCore.Audio.FiresCustomAudioStore.LoadAllCoroutine(soundsFolder));
            }
            catch (Exception ex) { Debug.LogWarning($"[{PluginName}] custom sound load init failed: {ex.Message}"); }

            FinishInit();
        }

        private void InstallLogFilter()
        {
            // FiresUnifiedCore installs the one shared rate-limit/suppression log handler now
            // (FiresCore.Logging.RateLimitedLogHandler). This per-mod copy was collapsed into Core so the handler
            // chain stays a single wrapper instead of one-per-mod - intentionally a no-op.
        }

        private void Update()
        {
            // Drain the UILayoutSyncRPC chunked-send queue at a rate-limited pace each frame
            // to avoid flooding Steam's send buffer when syncing large layout files to clients.
            FiresCore.UI.UILayoutSyncRPC.DrainSendQueue();

            // Same rationale for territory chunks. 290 territories spread across 12+ chunks,
            // fired all in one frame, was hitting Steam's k_EResultLimitExceeded and leaving
            // clients stuck in a "Territory RequestFromServer" retry loop.
            global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.DrainSendQueue();
        }


        /// <summary>
        /// Types whose Harmony patches must NOT be processed on a dedicated server.
        /// These classes (and their nested types) reference client-only assemblies/types
        /// (Minimap, Hud, EnemyHud, InventoryGui, FejdStartup, GameCamera, etc.) inside
        /// their patch method bodies.  When Harmony's IL rewriter tries to import those
        /// references on a headless server the Mono runtime crashes natively inside
        /// AssemblyName.get_FullName -> String.FastAllocateString.
        /// </summary>
        private static readonly HashSet<string> _clientOnlyPatchTypes =
        [
            // Territories
            "FiresRPGmaker.Modules.Territories.TerritoryMapOverlay",
            "FiresRPGmaker.Modules.Territories.TerritoryHudOverlay",
            "FiresRPGmaker.Modules.Territories.OutlineSurveyor",
            "FiresRPGmaker.Modules.Territories.Surveyor",
            // UI (inventory, initializer)
            "FiresRPGmaker.UI.UIInitializer",
            "FiresRPGmaker.UI.NpcHoverHudSuppressor+TestShowPatch",
            "FiresRPGmaker.UI.Patches.InventoryGuiPatches",
            "FiresRPGmaker.UI.Patches.UITooltip_StylePatches",
            "FiresRPGmaker.UI.Patches.InventoryGui_UpdateItemDrag_Patch",
            "FiresRPGmaker.UI.Patches.InventoryGui_OnSelectedItem_Patch",
            "FiresRPGmaker.UI.Patches.InventoryGui_OnDropOutside_Patch",
            "FiresRPGmaker.UI.Patches.InventoryGui_SetupDragItem_Patch",
            "FiresCore.UI.UIOverrideInventoryPatches",
            // Factions nameplate (EnemyHud is client UI) + local-player-only restriction patches
            // (InventoryGui client type; all gate the local player's own actions)
            "FiresRPGmaker.Modules.Factions.FactionNameplatePatches",
            "FiresRPGmaker.Modules.Factions.FactionRestrictionPatches",
            "FiresRPGmaker.Modules.PlayersTag.PlayerTagNameplatePatches",
            // Companions (only client-only nested classes, server patches stay active)
            "FiresRPGmaker.Modules.Companions.CompanionPatches+CompanionClientPatches_FejdStartup",
            "FiresRPGmaker.Modules.Companions.CompanionPatches+CompanionClientPatches_EnemyHud",
            "FiresRPGmaker.Modules.Companions.CompanionPatches+ArcheryTarget_OnProjectileHit_Patch",
            // Dialogues
            "FiresRPGmaker.Modules.Dialogues.DialogueUI",
            // Chat
            "FiresRPGmaker.Modules.ChatSystem.ChatPatches",
            // Leaderboard hooks: InventoryGui is client-only; the rest (Character/Piece/Pickable/Player) exist
            // server-side and patch fine. Without this the dedi server crashes natively in PatchAll's IL rewrite
            // (the cause of an ErrorConnectFailed handshake).
            "FiresRPGmaker.Modules.Marketplace.LeaderboardFeedPatches+InventoryGui_DoCrafting",
            // Leaderboard window modal input-capture (PlayerController/GameCamera/ZInput/Minimap/Player placement)
            // - all client-only types; on a headless server these would native-crash Mono's IL rewriter.
            "FiresRPGmaker.UI.Leaderboard.FiresLeaderboardInputPatches",
            "FiresRPGmaker.UI.Leaderboard.LeaderboardTabLateRegister",
            // Marketplace console commands - Terminal exists server-side too, but the patch class registers
            // a player-side flow only; keeping it client-skipped is harmless.
            "FiresRPGmaker.Modules.Marketplace.MarketplaceConsoleCommands",
            // Transmog visual swap - VisEquipment is client-side (visualization), patches NRE on dedi.
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetHelmetItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetChestItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetLegItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetShoulderItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetRightItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetLeftItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetUtilityItem_Patch",
            // Inventory grid marker - InventoryGrid is a client-only UI component.
            "FiresRPGmaker.Modules.Marketplace.InventoryGrid_TransmogMarker_Patch",
            // Territory missing-flag patches - most reference Player.m_localPlayer / Hud / MessageHud.
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_UseStamina_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_UseEitr_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_GetRunSpeedFactor_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_GetJogSpeedFactor_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_TeleportTo_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_HardDeath_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Player_AutoPickup_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Attack_SpawnOnHitTerrain_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+ArmorStand_UseItem_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+SpawnSystem_UpdateSpawning_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+Mister_OnEnable_Patch",
            "FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+SpawnSystem_FindBaseSpawnPoint_Patch",
            // Quest type-extension patches - client-side gameplay event hooks.
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_OnInventoryChanged_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Pickable_RPC_Pick_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+InventoryGui_DoCrafting_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_PlacePiece_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_Update_MoveQuest_Patch",
            // Per-NPC nameplate controls - references EnemyHud (client UI), absent server-side.
            "FiresRPGmaker.UI.Patches.NpcNameplatePatches",
            "FiresRPGmaker.UI.Patches.NpcNameplatePatches+EnemyHud_TestShow_NameplateMode",
            // Long-range board interact - GameCamera/Hud/Minimap/InventoryGui touches, local-player only.
            "FiresRPGmaker.Modules.TableGames.LongRange_Player_Update_Patch",
            "FiresRPGmaker.Modules.TableGames.LongRange_Hud_Crosshair_Patch",
        ];

        /// <summary>Client-only patch classes FiresMod skips when PatchAll runs on a dedicated server
        /// (their bodies reference Hud/Minimap/InventoryGui/FejdStartup/etc. and native-crash Mono's IL
        /// rewriter headless). Replaces the old hand-rolled PatchAll loop.</summary>
        protected override System.Collections.Generic.IReadOnlyCollection<string> DedicatedServerSkipPatchTypes => _clientOnlyPatchTypes;

        // RPGMaker self-gates its world init via the WaitForZNetReady coroutine started in Setup;
        // WorldStart only announces readiness (compact banner + summary box).
        protected override void WorldStart()
        {
            FiresRPGmakerBanner.Print();
            EmitWorldStartSummary();
        }
        protected override void TitleScene(bool isFirstBoot) { }

        private static void EmitWorldStartSummary()
        {
            try
            {
                // 0.0.527: RPG Maker's own guild module is removed (Fire 10-08); guilds come from FiresGuilds or VikingLands.Core.
                string guildOwner = global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildOwner ?? "none (install FiresGuilds)";
                FiresCore.Logging.LoadSummary.For(PluginName).EmitMiniBox("\uD83D\uDCD6 RPG MAKER", new[]
                {
                    $"saved npcs   {SavedNpcManager.GetAllSavedNpcs().Count()}",
                    $"territories  {(ZNet.instance != null && ZNet.instance.IsServer() ? TerritoryLedger.Territories.Count : TerritorySyncedState.ClientTerritories.Count)}",
                    $"vault        {(VaultOfKnowledge.Instance != null ? "ok" : "off")}",
                    $"guilds       {guildOwner}",
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{PluginName}] World-start summary failed: {ex.Message}");
            }
        }

        private void FinishInit()
        {
            // Register sync RPCs.
            // NOTE: "FiresRPGmaker_SyncFiles" is intentionally NOT registered here. It is owned
            // by DialogueSyncRPC.Register (ZNet.Awake postfix), the single canonical registration
            // that binds reliably on every host (client, listen-server, dedicated). Registering it
            // here as well would throw - ZRoutedRpc.Register uses Dictionary.Add, which rejects a
            // duplicate key - and that throw would abort the registrations queued after it.
            try
            {
                if (ZRoutedRpc.instance != null)
                {
                    SavedNpcManager.RegisterRPCs();
                    FiresCore.UI.UILayoutSyncRPC.EnsureRpcsRegistered();

                    // Register Territory RPCs early so request/receive handlers are bound
                    // before WaitForZNetReady fires RequestFromServer on the client.
                    global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.EnsureRpcsRegistered();

                    // NOTE: ManifestDiffSync registration intentionally NOT here. On a
                    // client this whole block runs during plugin Awake when ZRoutedRpc
                    // is null, so the manifest RPCs would never bind and the server's
                    // RequestFromPeer queries would time out. Moved to WaitForZNetReady
                    // alongside the other Init() calls, which is the first moment
                    // ZRoutedRpc is guaranteed to be wired.
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to register SyncFiles RPC: {ex.Message}");
            }

            // 0.0.527 (Fire 10-08): RPG Maker's own guild module is removed. Guilds come from FiresGuilds (or VikingLands.Core);
            // the guild-dependent features here (the Clan chat channel, dialogue guild conditions) read the owner through
            // FiresCore.Bridge.GuildBridge / RpgMakerApi.

            // Banker + Mail + Marketplace are server-authoritative over Core's LiteDB vault. Register in their own
            // guard so a guild throw above can't skip them; idempotent + instance-tracked, re-run by Game.Start.
            try
            {
                if (ZRoutedRpc.instance != null)
                {
                    global::FiresRPGmaker.Modules.Marketplace.BankerNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MailNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MarketplaceNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Poker.PokerLobby.EnsureRegistered();
                    global::FiresRPGmaker.Modules.Factions.FactionChat.EnsureRegistered();
                    global::FiresRPGmaker.Modules.Marketplace.DiscordWebhooks.EnsureRegistered();
                    global::FiresRPGmaker.UI.NpcMapPins.EnsureRegistered();
                }
            }
            catch (Exception ex) { Debug.LogError($"[FiresRPGmaker] Banker/Mail/Market Initialize threw: {ex}"); }


            // RPC registration is centralized in VaultOfKnowledge.EnsureRpcsRegistered to avoid duplicates

            Debug.Log($"{PluginName}: FiresRPGmaker mod loaded!");
            StartCoroutine(WaitForZNetReady());
        }

        /// <summary>
        /// Backup registration hook: runs once on Game.Start (server and client). Game.Start fires after ZNet/ZRoutedRpc are
        /// fully wired, so this guarantees the Banker / Mail / Marketplace / Poker / Faction chat / webhook / map pin RPCs are
        /// registered even if FinishInit ran before ZRoutedRpc was available. Each EnsureRegistered / Initialize is idempotent.
        /// (0.0.527: it no longer registers guilds; RPG Maker's guild module is removed.)
        /// </summary>
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        public static class GameStart_BackupRegistration
        {
            private static void Postfix()
            {
                try
                {
                    global::FiresRPGmaker.Modules.Marketplace.BankerNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MailNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MarketplaceNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Poker.PokerLobby.EnsureRegistered();
                    global::FiresRPGmaker.Modules.Factions.FactionChat.EnsureRegistered();
                    global::FiresRPGmaker.Modules.Marketplace.DiscordWebhooks.EnsureRegistered();
                    global::FiresRPGmaker.UI.NpcMapPins.EnsureRegistered();
                }
                catch (Exception ex) { Debug.LogError($"[FiresRPGmaker] Game.Start backup Banker/Mail/Market Initialize threw: {ex}"); }
            }
        }

        private void BindConfigsToSync()
        {
            var cm = ConfigManager.Instance;
            // Only verbose logging and server authority (locking)
            configSync.AddLockingConfigEntry(cm.configServerAuthority);
            configSync.AddConfigEntry(cm.configVerboseLogging);
            configSync.AddConfigEntry(cm.configStaticNpcInvulnerable);
            configSync.AddConfigEntry(cm.configAdminBypassTerritory);
            configSync.AddConfigEntry(cm.configTerritoryVisualizers);
            configSync.AddConfigEntry(cm.configTerritoryOverlay);

            // Leaderboard - clients must adopt the server's achievement profile so local and synced
            // counts score against the same def set (the server is the single source of truth).
            configSync.AddConfigEntry(cm.configAchievementProfile);

            // Progression (all synced)
            configSync.AddConfigEntry(cm.configUsePrivateKeys);
            configSync.AddConfigEntry(cm.configBlockAllGlobalKeys);
            configSync.AddConfigEntry(cm.configBlockedGlobalKeys);
            configSync.AddConfigEntry(cm.configAllowedGlobalKeys);
            configSync.AddConfigEntry(cm.configEnforcedGlobalKeys);
            configSync.AddConfigEntry(cm.configBlockedPrivateKeys);
            configSync.AddConfigEntry(cm.configAllowedPrivateKeys);
            configSync.AddConfigEntry(cm.configEnforcedPrivateKeys);
            configSync.AddConfigEntry(cm.configUsePrivateRaids);
            configSync.AddConfigEntry(cm.configAdminBypassProgression);
            configSync.AddConfigEntry(cm.configUseBlockedMessage);
            configSync.AddConfigEntry(cm.configBlockedMessage);
            configSync.AddConfigEntry(cm.configUseBlockedEffect);
            configSync.AddConfigEntry(cm.configLockEquipment);
            configSync.AddConfigEntry(cm.configLockEquipmentKey);
            configSync.AddConfigEntry(cm.configLockCrafting);
            configSync.AddConfigEntry(cm.configLockCraftingKey);
            configSync.AddConfigEntry(cm.configLockBuilding);
            configSync.AddConfigEntry(cm.configLockBuildingKey);
            configSync.AddConfigEntry(cm.configLockCooking);
            configSync.AddConfigEntry(cm.configLockCookingKey);
            configSync.AddConfigEntry(cm.configLockEating);
            configSync.AddConfigEntry(cm.configLockEatingKey);
            configSync.AddConfigEntry(cm.configLockTaming);
            configSync.AddConfigEntry(cm.configLockTamingKey);
            configSync.AddConfigEntry(cm.configLockGuardianPower);
            configSync.AddConfigEntry(cm.configLockGuardianPowerKey);
            configSync.AddConfigEntry(cm.configLockBossSummons);
            configSync.AddConfigEntry(cm.configLockBossSummonsKey);

            // Companion persistence (server-authoritative - clients must agree
            // on whether companions persist across logout, otherwise the dedupe
            // sweep on the server fights with a client still expecting fresh
            // spawns from vault).
            configSync.AddConfigEntry(cm.configPersistentCompanions);
            configSync.AddConfigEntry(cm.configKeepZonesLoadedForOfflinePlayers);


            // Heightmap Override (all synced/server-locked)

        }

        protected override void Shutdown()
        {
            FiresCore.Bridge.GroupBridge.Unregister(_groupProvider);

            if (assetBundle != null)
            {
                try
                {
                    assetBundle.Unload(true);
                    Debug.Log($"{PluginName}: AssetBundle unloaded.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{PluginName}: AssetBundle unload error: {ex}");
                }
                finally
                {
                    assetBundle = null;
                }
            }
            if (chessBundle != null)
            {
                try { chessBundle.Unload(true); } catch { }
                chessBundle = null;
            }

            if (RootObject != null)
            {
                UnityEngine.Object.Destroy(RootObject);
                RootObject = null;
            }

            if (configSync != null)
            {
                configSync.lockedConfigChanged -= ConfigManager.Instance.OnConfigLockChanged;
                configSync = null;
            }
            AdminSyncing.AdminStatusChanged -= _refreshConfigLockOnAdminChange;

            // Shutdown config file watcher (prevents FileSystemWatcher thread from blocking process exit)
            try
            {
                ConfigManager.Instance?.Dispose();
            }
            catch { }

            // Shutdown server config file watcher
            try
            {
                ServerConfigFileWatcher.Shutdown();
            }
            catch { }
            
            // Clear ability FX prefab cache
            try
            {
                AbilityFXManager.ClearCache();
            }
            catch { }
        }

        public static bool IsObjectDBValid() => ObjectDB.instance != null;

        /// <summary>
        /// Gets the main asset bundle for this mod.
        /// Used by UIPrefabFactory to load UI prefabs.
        /// </summary>
        public AssetBundle GetAssetBundle()
        {
            return assetBundle;
        }

        private void LoadAssets()
        {
            if (assetBundle != null)
            {
                Debug.Log($"{PluginName}: AssetBundle already loaded.");
                return;
            }

            try
            {
                var bundleTimer = System.Diagnostics.Stopwatch.StartNew();

                // ORDER MATTERS: firesnpcsui declares a Dependency on firesnpcs (per its manifest), so
                // firesnpcs MUST load FIRST or firesnpcsui's cross-bundle refs (Image sprites etc.) don't
                // resolve at deserialize-time -> Image components get stripped from prefabs at runtime
                // (was: panel.GetComponentsInChildren<Image>() returned 0 even though .prefab YAML had 24).
                assetBundle = GetAssetBundleFromResources("firesnpcs");
                if (assetBundle == null) throw new Exception("Bundle not found");

                uiBundle = GetAssetBundleFromResources("firesnpcsui");
                booksBundle = GetAssetBundleFromResources("firesbooks");   // self-contained (ships Custom/FiresPiece); no cross-bundle deps
                leaderboardTabBundle = GetAssetBundleFromResources("firesleaderboardtab");
                chessBundle = GetAssetBundleFromResources("vaitems_chessboard");   // self-contained vanilla-clone chess table

                global::FiresRPGmaker.UI.FiresNpcsUIBuilderHost.Install();
                global::FiresRPGmaker.UI.Leaderboard.LeaderboardInventoryTab.Register();

                VAItemManager.LoadItems("Assets/Custom/NPCs/", this);
                VAPieceManager.LoadPieces("Assets/Custom/NPCs/", PluginName);
                Modules.Chess.ChessTablePiece.Load(chessBundle);   // appends to piecePrefabs -> rides ZNetScene + hammer passes
                propsBundle = GetAssetBundleFromResources("firesnpcs_props");
                Modules.Props.FiresPropsModule.Load(propsBundle);  // mailboxes -> hammer + mail UI; boards -> ZNetScene-only
                _bundleLoadMs = bundleTimer.ElapsedMilliseconds;

     // Companion prefabs (CompanionNpc/BaseNpc/Wild) are owned by FiresCompanions now
     // (they live in the firescompanions bundle and FiresCompanions loads + registers them).
     // RPGMaker keeps only its own StaticNpc prefab (firesnpcs bundle) for static quest NPCs.

                // Schedule the one-time shader/mesh prewarm pass for every
                // loaded NPC prefab. Init() registers a PlayerSpawnGate
                // callback - actual rendering happens AFTER the player is
                // in-world. See VANpcShaderPrewarm.cs for the full design.
                try { VANpcShaderPrewarm.Init(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] VANpcShaderPrewarm.Init failed: {ex.Message}");
                }



           Debug.Log($"{PluginName}: Loaded {itemPrefabs.Count} items, {piecePrefabs.Count} pieces");
   }
            catch (Exception ex)
            {
  Debug.LogError($"{PluginName}: Asset load failed: {ex}");
            }
     }

        private AssetBundle GetAssetBundleFromResources(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Debug.LogError($"{PluginName}: No embedded resource ending with '{name}'.");
                return null;
            }

            // Read the bundle fully into memory: AssetBundle.LoadFromStream reads assets LAZILY, but the `using`
            // disposes the stream on return - under memory pressure Unity re-reads the evicted bundle from that
            // disposed stream -> "ManagedStream must be readable" -> "Size overflow in allocator" native crash.
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            return AssetBundle.LoadFromMemory(ms.ToArray());
        }

        private static bool _prefabsRegisteredThisSession;

        public static void AddPrefabsToZNetScene(ZNetScene zNetScene)
        {
            if (zNetScene == null) return;

            // BEFORE adding anything, purge Unity fake-null entries from the shared
            // collections. On a second world load in the same game session, leftover
            // fake-null entries (from any mod, not just ours) cause downstream
            // iterators (BalrondExtendedAnimals.SpawnerBuilder, Jotunn ItemManager,
            // ExpandWorldData prefab parser, etc.) to NRE during ZNetScene.Awake.
            // Unity's overloaded operator== makes `p == null` return true for
            // destroyed objects, so this catches both real-null and fake-null.
            zNetScene.m_prefabs.RemoveAll(p => p == null);

            Dictionary<int, GameObject> namedPrefabsDict = null;
            try
            {
                var namedField = typeof(ZNetScene).GetField("m_namedPrefabs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (namedField != null)
                {
                    namedPrefabsDict = namedField.GetValue(zNetScene) as Dictionary<int, GameObject>;
                    if (namedPrefabsDict != null)
                    {
                        var dead = namedPrefabsDict.Where(kvp => kvp.Value == null)
                                                   .Select(kvp => kvp.Key).ToList();
                        foreach (var k in dead) namedPrefabsDict.Remove(k);
                        if (dead.Count > 0)
                            Debug.Log($"{PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{PluginName}: Could not scrub ZNetScene.m_namedPrefabs: {ex.Message}");
            }

            // Only register itemPrefabs here - piecePrefabs are registered later
            // in VAPieceManager.AddPiecesToHammer() at the same time they're added to the
            // hammer's PieceTable. This prevents other mods (e.g. MVBP) from scooping up
            // our prefabs before we've claimed them in the hammer.
            foreach (var prefab in itemPrefabs)
            {
                if (prefab == null) continue;

                if (zNetScene.GetPrefab(prefab.name) != null)
                    continue;

                var zNetView = prefab.GetComponent<ZNetView>();
                zNetView ??= prefab.AddComponent<ZNetView>();
                zNetView.m_persistent = true;

                zNetScene.m_prefabs.Add(prefab);
                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Added {prefab.name} to ZNetScene");
            }

            // Repopulate hash lookup for items only.
            if (namedPrefabsDict != null)
            {
                foreach (var prefab in itemPrefabs)
                {
                    if (prefab == null) continue;
                    int hash = prefab.name.GetStableHashCode();
                    if (!namedPrefabsDict.ContainsKey(hash))
                    {
                        namedPrefabsDict[hash] = prefab;
                    }
                }
            }

            _prefabsRegisteredThisSession = true;
        }

        public static void AddItemsToObjectDB()
        {
            if (!IsObjectDBValid()) return;

            // Purge fake-null entries from ObjectDB shared collections before adding.
            // Same rationale as AddPrefabsToZNetScene - leftover dead refs from a
            // previous session crash other mods' Awake patches on the second world load.
            ObjectDB.instance.m_items.RemoveAll(p => p == null);

            Dictionary<int, GameObject> itemByHashDict = null;
            try
            {
                itemByHashDict = (Dictionary<int, GameObject>)typeof(ObjectDB)
                    .GetField("m_itemByHash", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(ObjectDB.instance);
                if (itemByHashDict != null)
                {
                    var dead = itemByHashDict.Where(kvp => kvp.Value == null)
                                             .Select(kvp => kvp.Key).ToList();
                    foreach (var k in dead) itemByHashDict.Remove(k);
                    if (dead.Count > 0)
                        Debug.Log($"{PluginName}: Scrubbed {dead.Count} fake-null entries from ObjectDB.m_itemByHash");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{PluginName}: Could not scrub ObjectDB.m_itemByHash: {ex.Message}");
            }

            // Add items
            foreach (var item in itemPrefabs)
            {
                if (item == null) continue;

                if (ObjectDB.instance.GetItemPrefab(item.name) != null) continue;

                ObjectDB.instance.m_items.Add(item);

                var hashDict = (Dictionary<int, GameObject>)typeof(ObjectDB)
                    .GetField("m_itemByHash", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(ObjectDB.instance);

                if (hashDict != null)
                {
                    int hash = item.name.GetStableHashCode();
                    if (!hashDict.ContainsKey(hash))
                        hashDict[hash] = item;
                }

                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Registered item to ObjectDB: {item.name}");
            }

            // Also ensure piece prefabs are registered for spawning
            // Pieces need to be in ObjectDB's internal lookup for Player.PlacePiece to work
            foreach (var piece in piecePrefabs)
            {
                if (piece == null) continue;

                // Check if it has an ItemDrop (some pieces are also items)
                var itemDrop = piece.GetComponent<ItemDrop>();
                if (itemDrop != null && ObjectDB.instance.GetItemPrefab(piece.name) == null)
                {
                    ObjectDB.instance.m_items.Add(piece);
  
                    var hashDict = (Dictionary<int, GameObject>)typeof(ObjectDB)
     .GetField("m_itemByHash", BindingFlags.Instance | BindingFlags.NonPublic)
   ?.GetValue(ObjectDB.instance);

if (hashDict != null)
  {
            int hash = piece.name.GetStableHashCode();
  if (!hashDict.ContainsKey(hash))
    hashDict[hash] = piece;
    }

     if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Registered piece item to ObjectDB: {piece.name}");
    }
  }
   }

        /// <summary>
        /// Retries territory sync if the initial request was lost or chunks were dropped.
        /// Runs on client only, stops after territories are received or max retries exceeded.
        /// </summary>
        private IEnumerator RetryTerritorySync()
        {
            while (TerritorySyncedState.NeedsRetry)
            {
                yield return new WaitForSeconds(12f);
                if (!TerritorySyncedState.NeedsRetry) yield break;
                if (ZNet.instance == null || ZNet.instance.IsServer()) yield break;
                Debug.Log("[FiresRPGmaker] Territory sync retry - no territories received yet");
                TerritorySyncedState.RequestFromServer();
            }
        }

        /// <summary>
        /// Diagnostic for the guild hand-off (FiresGuilds or VikingLands.Core own guilds; 0.0.527 removed RPG Maker's own module):
        /// polls Core's guild bridge until the owner resolves the local player's guild, then logs it once. The owner resolves it a
        /// few seconds after spawn (a server round-trip), so a one-shot read at spawn would sample before it arrives.
        /// </summary>
        private IEnumerator LogExternalGuildWhenResolved()
        {
            const float TimeoutSeconds = 20f;
            const float PollSeconds = 2f;
            float waited = 0f;
            string owner = global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildOwner ?? "an external guild mod";
            while (waited < TimeoutSeconds)
            {
                string name = null;
                try { name = FiresCore.Bridge.GuildBridge.LocalGuildName(); } catch { }
                if (!string.IsNullOrEmpty(name))
                {
                    Debug.Log($"[FiresRPGmaker] {owner} owns guilds - local guild '{name}' resolved through the guild bridge.");
                    yield break;
                }
                yield return new WaitForSeconds(PollSeconds);
                waited += PollSeconds;
            }
            Debug.Log($"[FiresRPGmaker] {owner} owns guilds - the local player resolves to no guild after 20s. If you ARE in a guild there, its local-guild sync isn't reaching the client.");
        }

        private IEnumerator WaitForZNetReady()
        {
            Debug.Log($"{PluginName}: Waiting for ZNetScene & ObjectDB...");
            while (ZNetScene.instance == null || ObjectDB.instance == null)
                yield return null;

            // Warm the territory ledger on the server as soon as ZNetScene/ObjectDB are ready, so the (large)
            // legacy load is done before clients request territory sync. Server-only; reads cfg, registers nothing.
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                    global::FiresRPGmaker.Modules.Territories.TerritoryLedger.Init();
            }
            catch (System.Exception ex) { Debug.LogWarning($"[FiresRPGmaker] early ledger warm failed: {ex.Message}"); }

            yield return new WaitForEndOfFrame();

            // Each guarded independently: these run one after another on the same frame, and an unguarded throw
            // in any one used to abort the whole chain (e.g. a VaultOfKnowledge.Init failure took quests +
            // dialogues + territory sync down with it, and left the vault unconfigured -> vanishing bank deposits).
            try { VaultOfKnowledge.Init(); } catch (System.Exception ex) { Debug.LogError($"[FiresRPGmaker] VaultOfKnowledge.Init failed: {ex.Message}"); }
            try { QuestManager.Init(); } catch (System.Exception ex) { Debug.LogError($"[FiresRPGmaker] QuestManager.Init failed: {ex.Message}"); }
            try { global::FiresRPGmaker.Modules.Dialogues.DialogueCodex.Init(); } catch (System.Exception ex) { Debug.LogError($"[FiresRPGmaker] DialogueCodex.Init failed: {ex.Message}"); }
            try { TerritorySyncedState.Init(); } catch (System.Exception ex) { Debug.LogError($"[FiresRPGmaker] TerritorySyncedState.Init failed: {ex.Message}"); }

            // Generic manifest-diff transport + folder/blob registrations.
            // Registered here (not in FinishInit) because on a client the plugin
            // Awake fires before ZNet starts - ZRoutedRpc.instance is null at
            // that point, so EnsureRegistered would silently bail and every
            // future RequestFromPeer query from the server would time out
            // against a handler-less client. WaitForZNetReady runs after
            // ZNetScene + ObjectDB are wired (i.e. after the player has
            // joined a server and ZRoutedRpc is alive), which is the correct
            // moment for both sides - server asks, client answers,
            // listen-server does both.
            ManifestDiffSync.EnsureRegistered();
            global::FiresRPGmaker.Managers.ServerConfigFileWatcher.RegisterManifestFolders();
            global::FiresRPGmaker.Managers.SavedNpcManager.RegisterManifestFolder();
            // Quest DB fingerprint is computed live from the in-memory DB (no profile-global
            // .hash that would bleed quests across servers - mirrors the territory fix).
            ManifestDiffSync.RegisterHashProvider(
                global::FiresRPGmaker.Modules.Quest.QuestManager.QuestDbManifestNamespace,
                () => global::FiresRPGmaker.Modules.Quest.QuestManager.GetLiveQuestDbFingerprint());
            // Territory fingerprint is computed live from the per-server client cache
            // (no profile-global .hash that would bleed overlays across servers).
            ManifestDiffSync.RegisterHashProvider(
                global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.TerritoriesManifestNamespace,
                () => global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.GetLiveFingerprint());

                        // Initialize RuntimeSprites sync for quest preview images
                          global::FiresRPGmaker.UI.QuestJournal.RuntimeSpriteSync.Init();
                          global::FiresRPGmaker.Modules.Sounds.NpcSoundSync.Init();   // 0.0.544: synced custom NPC sounds
                          global::FiresRPGmaker.UI.PatrolRouteSync.EnsureRegistered();   // 0.0.544: route key via the server

                           if (ZNet.instance != null && !ZNet.instance.IsServer())
                           {
                        TerritorySyncedState.RequestFromServer();
                               StartCoroutine(RetryTerritorySync());
                               // Request sprite + UI asset sync from server on first login (deferred to ensure connection is ready)
                               StartCoroutine(DeferredSpriteSyncRequest());

                               // 0.0.527: no guild sync of our own (the module is removed). When a guild mod owns
                               // guilds, log what it resolves for the local player (a few seconds after spawn: a
                               // server round-trip), so the hand-off is verifiable in the log.
                               if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildsDeferred)
                                   StartCoroutine(LogExternalGuildWhenResolved());
                             }

            // Register debug commands now that everything is ready
            DebugTools.RegisterConsoleCommands();
            

            AddItemsToObjectDB();
            AddPrefabsToZNetScene(ZNetScene.instance);

            // Companion prefab ZNetScene registration removed - FiresCompanions owns the companion
            // prefabs (firescompanions bundle). RPGMaker no longer registers them, which also resolves
            // the prior double-registration when both mods were installed.

            // Validate quest rewards/targets against ZNetScene/ObjectDB now that all prefabs are registered.
            // This marks unavailable rewards (e.g. from removed mods) so the UI can grey them out.
            try
            {
                QuestManager.Instance?.ValidateQuestDatabase();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{PluginName}] Quest validation failed: {ex.Message}");
            }

            // Initialize companion map marker system
            InitializeCompanionMapMarker();

            // Initialize companion command system (Shift+MMB ping)
            InitializeCompanionCommandSystem();

            // Register companion archetype status effects (Taunt, etc.)
            InitializeArchetypeSystem();

            // Replace custom shaders with Valheim's runtime shaders
            // Must be done after ZNetScene is ready so we can find Valheim's shaders
            FiresCore.Services.VanillaAssetResolver.Warm();
            foreach (var p in piecePrefabs) FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(p, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);
            foreach (var p in itemPrefabs)  FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(p, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);



            VAPieceManager.AddPiecesToHammer();
            VARecipeManager.UpdatePieceRequirements();


            // Mod compatibility - remove conflicting patches from other mods
            BalrondAmazingNatureCompat.Apply();

            // Ensure UI system is initialized (backup in case Harmony patch didn't trigger)
            StartCoroutine(EnsureUIInitialized());

            // Start periodic server-side flush of dirty vault data
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    StartCoroutine(PeriodicSaveAllDirty());
                    Debug.Log($"{PluginName}: Started periodic Vault SaveAllDirty coroutine on server");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{PluginName}] Failed to start periodic SaveAllDirty: {ex.Message}");
            }

            // Initialize server-side config file watcher for auto-sync to clients
            try
            {
                ServerConfigFileWatcher.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{PluginName}] Failed to initialize ServerConfigFileWatcher: {ex.Message}");
            }

            FiresCore.Logging.LoadSummary.For(PluginName).EmitMiniBox("\uD83D\uDCE6 ASSETS", new[]
            {
                $"items      {itemPrefabs.Count}",
                $"pieces     {piecePrefabs.Count}",
                $"npcs       {VAPieceManager.NpcPrefabNames.Count}",
                $"bundle     {_bundleLoadMs} ms",
            });

            Debug.Log($"{PluginName}: All items & pieces fully registered - ready!");
        }

        /// <summary>
        /// Initializes the companion map marker system.
        /// Creates a persistent GameObject that tracks companions on the minimap.
        /// </summary>
        private void InitializeCompanionMapMarker()
        {
            try
            {
                // Create the map marker manager if it doesn't exist
                if (CompanionMapMarker.Instance == null)
                {
                    GameObject markerManager = new("CompanionMapMarker");
                    markerManager.transform.SetParent(RootObject.transform);
                    markerManager.AddComponent<CompanionMapMarker>();
                    if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose("Companion map marker system initialized");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{PluginName}: Failed to initialize companion map marker: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the companion command system.
        /// Allows players to direct companions with Shift + Middle Mouse Button.
        /// </summary>
        private void InitializeCompanionCommandSystem()
        {
            try
            {
                CompanionCommandSystem.EnsureInitialized();
                CompanionCommandCoordinator.EnsureInitialized();
                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose("Companion command system initialized");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{PluginName}: Failed to initialize companion command system: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the companion archetype system.
        /// Registers taunt status effects, ability FX, and RPC handlers for multiplayer sync.
        /// </summary>
        private void InitializeArchetypeSystem()
        {
            try
            {
                // Register taunt status effects with ObjectDB
                TauntManager.RegisterEffects();

                // Register all companion status effects (Fortify, Berserk, etc.)
                FiresCore.Npc.Archetypes.StatusEffects.StatusEffectManager.RegisterAllEffects();

                // Initialize ability RPC manager for multiplayer sync
                // This must be done after ZRoutedRpc is available
                if (ZRoutedRpc.instance != null)
                {
                    AbilityRPCManager.Initialize();
                    // VikingLands.Core (TDV) owns groups - skip our Party when it's present.
                    if (!global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                        global::FiresRPGmaker.Modules.Party.PartyManager.Initialize();
                }
                else
                {
                    // Defer RPC initialization if ZRoutedRpc isn't ready yet
                    StartCoroutine(DeferredRPCInit());
                }

                // Initialize group synergy manager for combo abilities
                GroupSynergyManager.Initialize();

                // Register debug FX enumeration commands (listfx, testfx, listfxcategory)
                DebugFXEnumerator.RegisterCommands();

                // Initialize the compendium system for in-game documentation
                CompendiumManager.Initialize();

                // Factions: player affiliations (membership + nameplate + dialogue verbs/conditions).
                global::FiresRPGmaker.Modules.Factions.FactionModule.Initialize();

                Debug.Log($"{PluginName}: Companion archetype system initialized (Taunt, StatusEffects, FX, Synergies, Compendium)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{PluginName}: Failed to initialize archetype system: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Deferred initialization of RPC handlers if ZRoutedRpc wasn't ready during initial init.
        /// </summary>
        private IEnumerator DeferredRPCInit()
        {
            // Wait for ZRoutedRpc to be available
            float timeout = 30f;
            float elapsed = 0f;
            while (ZRoutedRpc.instance == null && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            if (ZRoutedRpc.instance != null)
            {
                try
                {
                    AbilityRPCManager.Initialize();
                    if (!global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                        global::FiresRPGmaker.Modules.Party.PartyManager.Initialize();
                    Debug.Log($"{PluginName}: Ability RPC manager initialized (deferred)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{PluginName}: Deferred RPC init failed: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"{PluginName}: ZRoutedRpc not available after {timeout}s, ability sync disabled");
            }
        }

        /// <summary>
        /// Deferred sprite sync request. Waits for the server connection to be fully
        /// established before requesting sprites, so the RPC doesn't get dropped.
        /// </summary>
        private IEnumerator DeferredSpriteSyncRequest()
        {
            // Wait for connection to be fully established
            float timeout = 15f;
            float elapsed = 0f;
            while (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            // Extra delay to let the server-side RPC handlers finish registering
            yield return new WaitForSeconds(2f);

            if (ZRoutedRpc.instance != null && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                global::FiresRPGmaker.UI.QuestJournal.RuntimeSpriteSync.RequestSyncFromServer();
                global::FiresRPGmaker.Modules.Sounds.NpcSoundSync.RequestFromServer();
                Debug.Log($"{PluginName}: Deferred sprite sync request sent to server");
            }
        }

        private IEnumerator EnsureUIInitialized()
        {
            // Wait for Hud to be available
            float timeout = 30f;
            float elapsed = 0f;
            while (Hud.instance == null && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (Hud.instance == null)
            {
                Debug.LogWarning($"{PluginName}: Hud not available after {timeout}s, UI initialization may fail");
                yield break;
            }

            // Give Hud a moment to fully initialize
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // Initialize UI system
            try
            {
                global::FiresRPGmaker.UI.UIInitializer.Initialize();
                Debug.Log($"{PluginName}: UI system initialized via backup coroutine");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{PluginName}: UI initialization failed: {ex}");
            }
        }

        private IEnumerator PeriodicSaveAllDirty()
        {
            const float interval = 120f; // seconds
            while (true)
            {
                yield return new WaitForSeconds(interval);
                try
                {
                    if (VaultOfKnowledge.Instance == null) continue;
                    
                    // OPTIMIZATION: Only run periodic saves when players are actually connected
                    // This prevents unnecessary disk writes and log spam when server is empty
                    var allPlayers = Player.GetAllPlayers();
                    if (allPlayers == null || allPlayers.Count == 0)
                    {
                        // No players connected - skip this save cycle entirely
                        // Server data will be saved when players connect or on world save
                        continue;
                    }

                    // If a player is joining, skip this scheduled save and reset timer
                    try
                    {
                        if (VaultOfKnowledge.Instance.ConsumeSkipNextScheduledSave())
                        {
                            if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose("[FiresRPGmaker] Periodic SaveAllDirty skipped due to player join; timer reset");
                            continue;
                        }
                    }
                    catch { }

                    VaultOfKnowledge.Instance.SaveAllDirty();
                    Debug.Log($"[{PluginName}] Periodic SaveAllDirty executed ({allPlayers.Count} players online)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[{PluginName}] Periodic SaveAllDirty failed: {ex.Message}");
                }
            }
        }

    }

    /// <summary>
    /// Harmony patches for re-registering prefabs on re-login (logout to menu and back in).
    /// Without these patches, NPCs disappear because ZNetScene loses the prefab references.
    /// </summary>
    [HarmonyPatch]
    public static class ZNetScenePatches
    {
        /// <summary>
/// Re-register prefabs when ZNetScene.Awake runs (happens on every world load).
        /// </summary>
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
   [HarmonyPostfix]
public static void ZNetScene_Awake_Postfix(ZNetScene __instance)
 {
            try
          {
                // Re-register item prefabs to ZNetScene (pieces are handled in AddPiecesToHammer)
                if (FiresRPGmaker.itemPrefabs.Count > 0)
                {
                    FiresRPGmaker.AddPrefabsToZNetScene(__instance);
                    Debug.Log($"[FiresRPGmaker] Re-registered {FiresRPGmaker.itemPrefabs.Count} item prefabs to ZNetScene on Awake");
                }

                // Companion prefab re-registration removed - FiresCompanions owns companion prefabs.

                // Pre-register piece prefabs (wayshrines etc.) with ZNetScene
                // BEFORE ZDOMan.Load runs and tries to spawn the persistent
                // ZDOs that reference them. PieceTable wiring (Hammer build
                // menu) still happens in AddPiecesToHammer at ObjectDB.Awake
                // because that one needs the Hammer ItemDrop to be ready -
                // but the ZNetScene side has to land here or vanilla's
                // CreateObject runs "Destroyed invalid predab ZDO:..." on
                // every wayshrine in the world.
                try
                {
                    VAPieceManager.RegisterPiecesWithZNetScene();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] VAPieceManager.RegisterPiecesWithZNetScene failed in ZNetScene.Awake postfix: {ex.Message}");
                }
      }
            catch (Exception ex)
         {
   Debug.LogError($"[FiresRPGmaker] ZNetScene_Awake_Postfix error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patches for ObjectDB to re-register items on re-login.
    /// </summary>
    [HarmonyPatch]
    public static class ObjectDBPatches
    {
        /// <summary>
        /// Re-register items and pieces when ObjectDB.Awake runs (happens on every world load).
        /// CRITICAL: Also re-adds piece prefabs to the hammer and ZNetScene so that
        /// Player.PlacePiece can find them after logout-to-menu and re-login.
        /// Without this, placing StaticNpc (or any custom piece) after re-login causes
        /// "The Object you want to instantiate is null" because the new ZNetScene
        /// instance no longer contains our piece prefabs.
        /// </summary>
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        // No __instance parameter - body uses static ObjectDB / ZNetScene state only.
        public static void ObjectDB_Awake_Postfix()
        {
            try
            {
         // Re-register items to ObjectDB
         if (FiresRPGmaker.itemPrefabs.Count > 0)
         {
             FiresRPGmaker.AddItemsToObjectDB();
             Debug.Log($"[FiresRPGmaker] Re-registered items to ObjectDB on Awake");
         }


            // Re-register piece prefabs to hammer and ZNetScene.
            // AddPiecesToHammer internally registers each piece to ZNetScene.m_namedPrefabs
            // and adds it to the hammer's PieceTable - both are required for PlacePiece.
            if (FiresRPGmaker.piecePrefabs.Count > 0 && ZNetScene.instance != null)
            {
                VAPieceManager.AddPiecesToHammer();
                Debug.Log($"[FiresRPGmaker] Re-registered {FiresRPGmaker.piecePrefabs.Count} piece prefabs to hammer on ObjectDB Awake");
            }

            // Check the companion gear table against ObjectDB now, not on the first spawn frame.
            try
            {
                FiresCore.Npc.CompanionGearTable.Prepare();
            }
            catch (Exception cacheEx)
            {
                Debug.LogWarning($"[FiresRPGmaker] CompanionGearTable prewarm failed: {cacheEx.Message}");
            }
         }
            catch (Exception ex)
      {
          Debug.LogError($"[FiresRPGmaker] ObjectDB_Awake_Postfix error: {ex.Message}");
 }
    }
    }
}











