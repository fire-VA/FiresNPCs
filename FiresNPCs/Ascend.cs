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
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Utilities;

using FiresCore.Sync;
using FiresCore.Lifecycle;

namespace FiresRPGmaker
{
    [BepInPlugin("com.Fire.FiresRPGmaker", "FiresRPGmaker", "0.0.167")]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    public class FiresRPGmaker : FiresMod<FiresRPGmaker>
    {
        public const string PluginGUID = "com.Fire.FiresRPGmaker";
        public const string PluginName = "FiresRPGmaker";
        public const string PluginVersion = "0.0.167";

        public static AssetBundle assetBundle;
        public static AssetBundle uiBundle;
        public static readonly List<GameObject> piecePrefabs = [];
        public static readonly List<GameObject> itemPrefabs = [];
        public static GameObject RootObject;
        public static GameObject PrefabContainer { get; private set; }

        public ConfigSync configSync;

        private readonly System.Action<bool> _refreshConfigLockOnAdminChange =
            _ => ConfigManager.Instance?.OnConfigLockChanged();
        private static ILogHandler _originalLogHandler;

        protected override void Setup()
        {
            // Diagnostic Ã¢â‚¬â€ fires before any other init code or log filter install.
            // If this line doesn't appear in the log even though BepInEx says
            // "Loading [FiresRPGmaker X.X.X]", then Awake() never executed
            // (Unity scheduling issue, plugin instantiation failure, etc).
            Debug.Log($"[{PluginName}] Awake() entered Ã¢â‚¬â€ version {PluginVersion}");

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
 BindConfigsToSync();


            // Restore persisted chat channel preference (client-only, not synced).
            ChatChannelManager.InitConfig(Config);

        // Load assets AFTER log filter is installed
            LoadAssets();

            // Wire the Core NPC engine's host-service bridges to RPGMaker implementations.
            Integration.NpcBridgeProviders.RegisterAll();

            // Load custom NPC mp3 sounds from BepInEx/config/FiresNPCs_Sounds (KG-style). The coroutine
            // self-guards on a headless/dedicated server (no audio output there).
            try
            {
                string soundsFolder = Path.Combine(BepInEx.Paths.ConfigPath, "FiresNPCs_Sounds");
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
            // chain stays a single wrapper instead of one-per-mod â€” intentionally a no-op.
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
        /// AssemblyName.get_FullName Ã¢â€ â€™ String.FastAllocateString.
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
            // Companions (only client-only nested classes, server patches stay active)
            "FiresRPGmaker.Modules.Companions.CompanionPatches+CompanionClientPatches_FejdStartup",
            "FiresRPGmaker.Modules.Companions.CompanionPatches+CompanionClientPatches_EnemyHud",
            "FiresRPGmaker.Modules.Companions.CompanionPatches+ArcheryTarget_OnProjectileHit_Patch",
            // Dialogues
            "FiresRPGmaker.Modules.Dialogues.DialogueUI",
            // Chat
            "FiresRPGmaker.Modules.ChatSystem.ChatPatches",
            // Guild (client-only patches: nameplate EnemyHud update, chat intercept, map pings)
            "FiresRPGmaker.Modules.Guilds.Patches.GuildPatches+EnemyHud_UpdateHuds_Patch",
            "FiresRPGmaker.Modules.Guilds.GuildChat.GuildChatChannel+Chat_InputText_Patch",
            "FiresRPGmaker.Modules.Guilds.Map.GuildMapFeatures+Minimap_UpdatePlayerPins_Patch",
            "FiresRPGmaker.Modules.Guilds.Map.GuildMapFeatures+Minimap_OnMapMiddleClick_Patch",
            // Marketplace LeaderboardTracker â€” InventoryGui/Minimap touches don't exist server-side.
            "FiresRPGmaker.Modules.Marketplace.LeaderboardTrackerPatches+InventoryGui_DoCrafting_Patch",
            "FiresRPGmaker.Modules.Marketplace.LeaderboardTrackerPatches+Minimap_Explore_Patch",
            // Marketplace console commands â€” Terminal exists server-side too, but the patch class registers
            // a player-side flow only; keeping it client-skipped is harmless.
            "FiresRPGmaker.Modules.Marketplace.MarketplaceConsoleCommands",
            // Transmog visual swap â€” VisEquipment is client-side (visualization), patches NRE on dedi.
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetHelmetItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetChestItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetLegItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetShoulderItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetRightItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetLeftItem_Patch",
            "FiresRPGmaker.Modules.Marketplace.TransmogVisualPatches+SetUtilityItem_Patch",
            // Inventory grid marker â€” InventoryGrid is a client-only UI component.
            "FiresRPGmaker.Modules.Marketplace.InventoryGrid_TransmogMarker_Patch",
            // Client input gates (M key, TAB) â€” Minimap/InventoryGui don't exist server-side.
            "FiresRPGmaker.UI.Patches.ClientInputGatePatches",
            // Territory missing-flag patches â€” most reference Player.m_localPlayer / Hud / MessageHud.
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
            // Quest type-extension patches â€” client-side gameplay event hooks.
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_OnInventoryChanged_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Pickable_RPC_Pick_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+InventoryGui_DoCrafting_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_PlacePiece_Patch",
            "FiresRPGmaker.Modules.Quest.QuestProgressPatches_NewTypes+Player_Update_MoveQuest_Patch",
            // Quest lifecycle OnDeath hook â€” local-player only, dedi never has m_localPlayer.
            "FiresRPGmaker.Modules.Quest.QuestLifecyclePatches+Player_OnDeath_Patch",
            // Per-NPC nameplate controls â€” references EnemyHud (client UI), absent server-side.
            "FiresRPGmaker.UI.Patches.NpcNameplatePatches",
        ];

        /// <summary>Client-only patch classes FiresMod skips when PatchAll runs on a dedicated server
        /// (their bodies reference Hud/Minimap/InventoryGui/FejdStartup/etc. and native-crash Mono's IL
        /// rewriter headless). Replaces the old hand-rolled PatchAll loop.</summary>
        protected override System.Collections.Generic.IReadOnlyCollection<string> DedicatedServerSkipPatchTypes => _clientOnlyPatchTypes;

        // RPGMaker self-gates its world init via the WaitForZNetReady coroutine started in Setup, so these
        // FiresMod lifecycle hooks are intentionally empty.
        protected override void WorldStart() { }
        protected override void TitleScene(bool isFirstBoot) { }

        private void FinishInit()
        {
            // Register file sync RPC
            try
            {
                if (ZRoutedRpc.instance != null)
                {
                    ZRoutedRpc.instance.Register<ZPackage>("FiresRPGmaker_SyncFiles", new Action<long, ZPackage>(RPC_SyncFiles));
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

            // Guild registration in its OWN try/catch so a throw in the block above
            // can't skip over it. This was the actual root cause of the "guild RPC
            // not registering on the server" bug Ã¢â‚¬â€ TerritorySyncedState or one of
            // the manifest helpers above was throwing silently and the shared catch
            // was swallowing the whole chain. The Harmony backup hook below is a
            // second-chance guarantee in case ZRoutedRpc.instance was still null
            // at FinishInit time.
            try
            {
                if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                {
                    // VikingLands.Core (TDV) owns guilds â€” stand ours down. Our guild-dependent
                    // features (chat / dialogue conditions / territory) read TDV via RpgMakerApi.
                    Debug.Log("[FiresRPGmaker] GuildModule.Initialize SKIPPED â€” VikingLands.Core owns guilds.");
                }
                else if (ZRoutedRpc.instance != null)
                {
                    GuildModule.Initialize();
                }
                else
                {
                    // Expected path on plugin Awake Ã¢â‚¬â€ BepInEx loads us before ZRoutedRpc
                    // is constructed.  The Harmony Game.Start backup below re-runs
                    // Initialize() once ZRoutedRpc is wired, so this is just a
                    // breadcrumb, not a problem.  Demoted from Warning so it doesn't
                    // look alarming in the log.
                    Debug.Log("[FiresRPGmaker] GuildModule.Initialize deferred to Game.Start (ZRoutedRpc not constructed yet Ã¢â‚¬â€ expected during plugin Awake).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] GuildModule.Initialize threw: {ex}");
            }


            // RPC registration is centralized in VaultOfKnowledge.EnsureRpcsRegistered to avoid duplicates

            Debug.Log($"{PluginName}: FiresRPGmaker mod loaded!");
            StartCoroutine(WaitForZNetReady());
        }

        /// <summary>
        /// Backup registration hook: runs once on Game.Start (server and client).
        /// Game.Start fires after ZNet/ZRoutedRpc are fully wired, so this guarantees
        /// guild RPCs are registered even if FinishInit ran before ZRoutedRpc was
        /// available (or threw before reaching the guild registration call). Safe
        /// to call repeatedly Ã¢â‚¬â€ GuildSyncManager.Initialize is idempotent via its
        /// own _registered flag.
        /// </summary>
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        public static class GuildModule_GameStart_BackupRegistration
        {
            private static void Postfix()
            {
                try
                {
                    if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present) return; // TDV owns guilds
                    GuildModule.Initialize();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FiresRPGmaker] Game.Start backup GuildModule.Initialize threw: {ex}");
                }
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

            // Companion persistence (server-authoritative Ã¢â‚¬â€ clients must agree
            // on whether companions persist across logout, otherwise the dedupe
            // sweep on the server fights with a client still expecting fresh
            // spawns from vault).
            configSync.AddConfigEntry(cm.configPersistentCompanions);
            configSync.AddConfigEntry(cm.configKeepZonesLoadedForOfflinePlayers);


            // Heightmap Override (all synced/server-locked)

        }

        protected override void Shutdown()
        {
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
                // ORDER MATTERS: firesnpcsui declares a Dependency on firesnpcs (per its manifest), so
                // firesnpcs MUST load FIRST or firesnpcsui's cross-bundle refs (Image sprites etc.) don't
                // resolve at deserialize-time â†’ Image components get stripped from prefabs at runtime
                // (was: panel.GetComponentsInChildren<Image>() returned 0 even though .prefab YAML had 24).
                assetBundle = GetAssetBundleFromResources("firesnpcs");
                if (assetBundle == null) throw new Exception("Bundle not found");

                uiBundle = GetAssetBundleFromResources("firesnpcsui");

                global::FiresRPGmaker.UI.FiresNpcsUIBuilderHost.Install();

                VAItemManager.LoadItems("Assets/Custom/NPCs/", this);
                VAPieceManager.LoadPieces("Assets/Custom/NPCs/", PluginName);

     // Companion prefabs (CompanionNpc/BaseNpc/Wild) are owned by FiresCompanions now
     // (they live in the firescompanions bundle and FiresCompanions loads + registers them).
     // RPGMaker keeps only its own StaticNpc prefab (firesnpcs bundle) for static quest NPCs.

                // Schedule the one-time shader/mesh prewarm pass for every
                // loaded NPC prefab. Init() registers a PlayerSpawnGate
                // callback Ã¢â‚¬â€ actual rendering happens AFTER the player is
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

            using var stream = assembly.GetManifestResourceStream(resourceName);
            return AssetBundle.LoadFromStream(stream);
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

            // Only register itemPrefabs here Ã¢â‚¬â€ piecePrefabs are registered later
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
            // Same rationale as AddPrefabsToZNetScene Ã¢â‚¬â€ leftover dead refs from a
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
        /// Fallback retry for the rare case where the initial RequestSync RPC
        /// (gated on PlayerSpawnGate.RunWhenLocalReady) was dropped on the wire
        /// during the connect handshake. The "spin until the player is ready"
        /// case is gone now that the FIRST request only fires once the player
        /// has a real ID, so this loop only ever fires when a packet actually
        /// got lost - hence small retry count + short interval.
        /// </summary>
        private IEnumerator RetryGuildSync()
        {
            int attempts = 0;
            const int MaxAttempts = 5;
            const float RetryIntervalSeconds = 6f;

            while (global::FiresRPGmaker.Modules.Guilds.Network.GuildSyncManager.ClientNeedsSync
                   && attempts < MaxAttempts)
            {
                yield return new WaitForSeconds(RetryIntervalSeconds);
                if (!global::FiresRPGmaker.Modules.Guilds.Network.GuildSyncManager.ClientNeedsSync) yield break;
                if (ZNet.instance == null || ZNet.instance.IsServer()) yield break;

                // Skip if player isn't ready yet - that's PlayerSpawnGate's job, not ours.
                var local = Player.m_localPlayer;
                long localPlayerId = local != null ? local.GetPlayerID() : 0L;
                if (local == null || localPlayerId == 0L) continue;

                attempts++;
                Debug.Log($"[FiresRPGmaker] Guild sync retry #{attempts} - cache still empty, re-requesting (likely dropped packet).");
                global::FiresRPGmaker.Modules.Guilds.Network.GuildSyncManager.RequestSync();
            }
        }

        /// <summary>
        /// Diagnostic for the TDV (VikingLands.Core) guild hand-off: polls the GuildAPI proxy until the
        /// external owner resolves the local player's guild, then logs it once. TDV resolves the local
        /// guild a few seconds after spawn (server round-trip), so a one-shot read at spawn would sample
        /// before it arrives. Only started when VikingLands.Core owns guilds.
        /// </summary>
        private IEnumerator LogTdvGuildWhenResolved()
        {
            const float TimeoutSeconds = 20f;
            const float PollSeconds = 2f;
            float waited = 0f;
            while (waited < TimeoutSeconds)
            {
                var g = global::FiresRPGmaker.Modules.Guilds.GuildAPI.GetOwnGuild();
                if (g != null)
                {
                    Debug.Log($"[FiresRPGmaker] TDV owns guilds - local guild '{g.Name}' resolved via TDV with {g.Members.Count} member(s).");
                    yield break;
                }
                yield return new WaitForSeconds(PollSeconds);
                waited += PollSeconds;
            }
            Debug.Log("[FiresRPGmaker] TDV owns guilds - local player still resolves to NO guild after 20s (GetOwnGuild=null). If you ARE in a TDV guild, TDV's local-guild sync isn't reaching the client.");
        }

        private IEnumerator WaitForZNetReady()
        {
            Debug.Log($"{PluginName}: Waiting for ZNetScene & ObjectDB...");
            while (ZNetScene.instance == null || ObjectDB.instance == null)
                yield return null;

            yield return new WaitForEndOfFrame();

            VaultOfKnowledge.Init();
            QuestManager.Init();
            global::FiresRPGmaker.Modules.Dialogues.DialogueCodex.Init();
            TerritorySyncedState.Init();

            // Generic manifest-diff transport + folder/blob registrations.
            // Registered here (not in FinishInit) because on a client the plugin
            // Awake fires before ZNet starts Ã¢â‚¬â€ ZRoutedRpc.instance is null at
            // that point, so EnsureRegistered would silently bail and every
            // future RequestFromPeer query from the server would time out
            // against a handler-less client. WaitForZNetReady runs after
            // ZNetScene + ObjectDB are wired (i.e. after the player has
            // joined a server and ZRoutedRpc is alive), which is the correct
            // moment for both sides Ã¢â‚¬â€ server asks, client answers,
            // listen-server does both.
            ManifestDiffSync.EnsureRegistered();
            global::FiresRPGmaker.Managers.ServerConfigFileWatcher.RegisterManifestFolders();
            global::FiresRPGmaker.Managers.SavedNpcManager.RegisterManifestFolder();
            // Quest DB fingerprint is computed live from the in-memory DB (no profile-global
            // .hash that would bleed quests across servers — mirrors the territory fix).
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

                           if (ZNet.instance != null && !ZNet.instance.IsServer())
                           {
                        TerritorySyncedState.RequestFromServer();
                               StartCoroutine(RetryTerritorySync());
                               // Request sprite + UI asset sync from server on first login (deferred to ensure connection is ready)
                               StartCoroutine(DeferredSpriteSyncRequest());

                               // Guilds: defer the first request until the local player is fully
                               // spawned. Player.m_localPlayer.GetPlayerID() returns 0 for
                               // several frames after respawn while PlayerProfile finishes
                               // loading; firing RequestSync from here (which runs as soon
                               // as ZNetScene + ObjectDB exist) hit that 0-ID window every
                               // time, the server returned an empty FullSync because it
                               // could not resolve the requester, and the retry loop then
                               // span on the same useless request until ZRpc keep-alive
                               // timed out and disconnected the client.  Gating the FIRST
                               // request on PlayerSpawnGate.RunWhenLocalReady lets the
                               // request fire exactly when the ID is real Ã¢â‚¬â€ RetryGuildSync
                               // below is now only a fallback for genuine packet drops, not
                               // a "spin until the player is ready" loop.
                               // Guilds: only sync/retry when RPG Maker owns them. When VikingLands.Core
                               // (TDV) is present it owns guild membership, so requesting our own sync just
                               // produced the misleading "RequestSync ... 0 guild(s)" spam. When deferring,
                               // log what TDV actually resolves so the hand-off is verifiable in the log.
                               if (!global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                               {
                                   FiresCore.Lifecycle.PlayerSpawnGate.RunWhenLocalReady(
                                       60f,
                                       _ => global::FiresRPGmaker.Modules.Guilds.Network.GuildSyncManager.RequestSync());
                                   StartCoroutine(RetryGuildSync());
                               }
                               else
                               {
                                   // TDV resolves the local guild a few seconds after spawn (server round-trip),
                                   // so poll rather than sample one frame too early.
                                   StartCoroutine(LogTdvGuildWhenResolved());
                               }
                             }

            // Register debug commands now that everything is ready
            DebugTools.RegisterConsoleCommands();
            

            AddItemsToObjectDB();
            AddPrefabsToZNetScene(ZNetScene.instance);

            // Companion prefab ZNetScene registration removed â€” FiresCompanions owns the companion
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


            // Mod compatibility Ã¢â‚¬â€ remove conflicting patches from other mods
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

            Debug.Log($"{PluginName}: All items & pieces fully registered Ã¢â‚¬â€ ready!");
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
                    // VikingLands.Core (TDV) owns groups â€” skip our Party when it's present.
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

        private static void RPC_SyncFiles(long sender, ZPackage pkg)
        {
            if (!ZNet.instance.IsServer())
   {
                Debug.LogWarning("[FiresRPGmaker] SyncFiles RPC received on client - ignoring");
                return;
            }

  if (!AdminSyncing.IsAdmin(sender))
       {
        Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} tried to sync file - rejected");
            return;
        }

         string folderName = pkg.ReadString();
      string fileName = pkg.ReadString();
            string content = pkg.ReadString();

            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(content))
    {
                Debug.LogWarning("[FiresRPGmaker] Invalid SyncFiles payload");
   return;
 }

     var allowedFolders = new[] { "Quests", "QuestProfiles", "QuestEvents", "Dialogues", "Traders", "Territories", "ServerInfos", "SavedNPCs" };
    if (!allowedFolders.Contains(folderName))
        {
   Debug.LogWarning($"[FiresRPGmaker] Invalid folder in SyncFiles: {folderName}");
return;
}

  fileName = fileName.Replace("..", "").Replace("/", "").Replace("\\", "");
      if (!fileName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) &&
          !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
      {
          Debug.LogWarning($"[FiresRPGmaker] Invalid file extension in SyncFiles: {fileName}");
          return;
      }

            string baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker");
       string targetDir = Path.Combine(baseDir, folderName);
            string fullPath = Path.Combine(targetDir, fileName);

   try
            {
                // Mark this file as admin-synced BEFORE writing so the FileWatcher knows to exclude this sender
                ServerConfigFileWatcher.MarkFileAsAdminSynced(folderName, fileName, sender);
                
                // Create target directory
                Directory.CreateDirectory(targetDir);
         File.WriteAllText(fullPath, content);
      Debug.Log($"[FiresRPGmaker] Admin {sender} synced file: {fullPath} ({content.Length} bytes)");

                // Handle folder-specific reloads
              if (string.Equals(folderName, "Territories", StringComparison.OrdinalIgnoreCase))
                {
             try
        {
      global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.ScheduleDebouncedReload();
              }
 catch (Exception ex)
     {
      Debug.LogWarning($"[FiresRPGmaker] Territory reload/broadcast failed: {ex.Message}");
       }
      }
     else if (string.Equals(folderName, "ServerInfos", StringComparison.OrdinalIgnoreCase))
             {
             try
               {
     // Reload the info profile cache on the server
            global::FiresRPGmaker.UI.Screens.InfoNpcScreenController.ReloadCache();
               Debug.Log("[FiresRPGmaker] ServerInfos cache reloaded");
           }
    catch (Exception ex)
  {
       Debug.LogWarning($"[FiresRPGmaker] ServerInfos reload failed: {ex.Message}");
    }
      }
      else if (string.Equals(folderName, "SavedNPCs", StringComparison.OrdinalIgnoreCase))
      {
          // Don't call OnServerFileChanged here Ã¢â‚¬â€ the File.WriteAllText above will
          // trigger the FileWatcher, which calls OnServerFileChanged with debouncing.
          // Calling it directly here causes double-processing.
      }
  }
        catch (Exception ex)
     {
       Debug.LogError($"[FiresRPGmaker] Failed to write synced file {fullPath}: {ex.Message}");
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

                // Companion prefab re-registration removed â€” FiresCompanions owns companion prefabs.

                // Pre-register piece prefabs (wayshrines etc.) with ZNetScene
                // BEFORE ZDOMan.Load runs and tries to spawn the persistent
                // ZDOs that reference them. PieceTable wiring (Hammer build
                // menu) still happens in AddPiecesToHammer at ObjectDB.Awake
                // because that one needs the Hammer ItemDrop to be ready Ã¢â‚¬â€
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
        // No __instance parameter Ã¢â‚¬â€ body uses static ObjectDB / ZNetScene state only.
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
            // and adds it to the hammer's PieceTable Ã¢â‚¬â€ both are required for PlacePiece.
            if (FiresRPGmaker.piecePrefabs.Count > 0 && ZNetScene.instance != null)
            {
                VAPieceManager.AddPiecesToHammer();
                Debug.Log($"[FiresRPGmaker] Re-registered {FiresRPGmaker.piecePrefabs.Count} piece prefabs to hammer on ObjectDB Awake");
            }

            // Pre-warm CompanionRandomLoadout's item-list cache. The cache
            // does a full ObjectDB.m_items scan with per-prefab validation
            // (GetPrefab + GetComponent + child traversal) Ã¢â‚¬â€ a one-shot cost
            // of tens of ms. If we let it lazy-build on the first wild
            // companion spawn, that cost lands on the spawn frame and
            // causes a visible hitch. Doing it here, while ObjectDB is
            // already warm in memory and we're not in gameplay, is free.
            try
            {
                FiresCore.Npc.CompanionRandomLoadout.CacheItemLists();
            }
            catch (Exception cacheEx)
            {
                Debug.LogWarning($"[FiresRPGmaker] CompanionRandomLoadout.CacheItemLists prewarm failed: {cacheEx.Message}");
            }
         }
            catch (Exception ex)
      {
          Debug.LogError($"[FiresRPGmaker] ObjectDB_Awake_Postfix error: {ex.Message}");
 }
    }
    }
}











