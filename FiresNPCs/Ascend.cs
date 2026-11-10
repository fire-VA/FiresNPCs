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
    [BepInDependency("com.Fire.FiresUnifiedCore", "0.2.775")]   // 0.0.769: the Territory Ledger window needs Core's KitWindow
    [BepInDependency("com.Fire.FiresWayshrines", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.Fire.VAInventory", BepInDependency.DependencyFlags.SoftDependency)]
    public class FiresRPGmaker : FiresMod<FiresRPGmaker>
    {
        public const string PluginGUID = "com.Fire.FiresRPGmaker";
        public const string PluginName = "FiresRPGmaker";
        public const string PluginVersion = "0.0.846";   // 0.0.846 ([classes-ui] 11-10, Fire): the live map no longer marks doors (route map only); TestDoorCount expects 0 there
        // 0.0.845:   // 0.0.845 ([ghost], [classes-ui] 11-08): the territory paint's FAT biome-panel lookup is quiet (assembly scan + GetType(name, false)): no HarmonyX "Could not find type named VerdantsAscent.Terrain.Zones.FatBiomeMapTool" Warning when FiresAdminTerrain is not installed; Info says "FiresAdminTerrain not installed". // 0.0.844 ([ghost], Fire 11-08 shots 605/606): live map / cutaway opens on All levels (was the player's level); Roofs off also hides each roof's snow cap (WearNTear m_snow / m_snowWorn / m_snowBroken, inactive ones included) with forceRenderingOff for the map capture only. Debug: "[LiveMap] roofs toggle OFF: ... + N roof snow-cap renderer(s)". // 0.0.843 ([ghost], Fire 11-08): large map header Auto (default) / Live map / Default. Auto = the painted map down to vanilla's closest zoom; zooming further in (only with the view centre in the loaded area) crossfades into the live view, zooming out fades it back; soft edge, no gold ring. Key [Live map] "Auto live map when zoomed in" (default on, read live; off = vanilla's zoom floor only). // 0.0.842 ([ghost] 11-07): build only, compiled against the pinned LIVE Core 0.2.1198 (FABE9551DD96) instead of the old Core 793 scratch build, so a changed Core API fails at compile time, not at runtime. No code change. // 0.0.841 ([ghost], log sweep 11-07): Dressing preview reads VisEquipment item hashes as prefab names (no more hair "0"); GetBodyModel falls back to vanilla VisEquipment.m_bodyModel and names the object when it fails; classic book with kit book OFF logs Debug, not Warning.
        // was:   // 0.0.838 ([ghost], Fire 11-07): book grip bounds = the pixels that differ from va_background4's OPAQUE backdrop (0.0.837 alpha scan found the whole sprite); screen-space proof line.
        // was:   // 0.0.837 ([ghost], Fire 11-07): NPC book resize grip on the opaque book art inside the sprite; StaticNpcAnimProbe removed.
        // was:   // 0.0.836 ([ghost], Fire 11-06 one method per job + frozen placed NPCs): ONE static NPC appearance method (ApplyBodyFromZDO) for fresh placement and every load - ApplyAppearance + RestoreAppearanceFromZDO deleted - ending in Animator.Rebind() like Core's BodyModelSwap, Hips before/after in one Info line. (0.0.835 withdrawn.)// 0.0.834 ([ghost], Fire 11-06 "still no resize handlers"): the NPC book's grip bounds follow the BookBackground art (aspect-fitted sprite), not the pages' content boxes (605x428 put the grip inside the right page).// 0.0.833 ([ghost], Fire 11-06): NPC info panel registers with Core when shown (opened half off screen), sizes to its text, L/M/S button removed; static NPCs keep CompanionIdleBehavior enabled for the head look-at (Core 0.2.1186 runs only that on fixtures); probe v3 (Hips early/late/end-of-frame, LateUpdate writers, skeleton paths, false FLIPs fixed).// 0.0.832 ([ghost], lead 11-06 probe v2, proof only): per-second + end probe lines add renderer visibility/bounds, animator state/normalizedTime/IsInTransition and the Hips bone rotation.// 0.0.831 ([ghost], Fire 11-06 book grip): the NPC book hands Core its ART rect (pages / kit window) as HitBounds, so the live-panel grip sits on the book (needs Core 1185 for always-shown).// 0.0.830 ([ghost], lead 11-06 log levels): probe load / FLIP / 30 s summary at Info, the per-second repeat at Debug.// 0.0.829 ([ghost], Fire 11-06 placed NPCs frozen / T-pose after load): Info probe per placed NPC, animator state on load + every 1 s for 30 s, the frame it flips; no fix yet (cause first).// 0.0.828 ([ghost], perf pass): NPC hover nameplate sleeps unless its style is on; client patrol key reads only on a ZDO change; quest + leaderboard hotkeys check state only on key press (keys in [Performance], ON). 0.0.827 ([ghost], Fire): biome painting moved to FAT's own panel; no territory stroke while that panel is open
        // 0.0.826:   // 0.0.826 ([ghost], Fire): repeating and test-only lines go to Debug, useful Info stays
        // 0.0.824:   // 0.0.824 ([ghost], Fire): test and state-changing commands need devcommands; recovercompanion asks the server first, so no second copy
        // 0.0.823:   // 0.0.823 ([ghost], Fire): recurring log lines are real Debug
        // 0.0.819:   // 0.0.819 ([ghost], Fire): biome mode on the territory map, through FAT
        // 0.0.818:   // 0.0.818 ([classes-ui] for [ghost]: two more territory measure lines, no fix); 0.0.817 ([classes-ui] for [ghost]: territory measure lines only, no fix); 0.0.816 ([ghost], Fire 11-02 config list): the [Companions] billboard, Companions.Behavior (8) and Companions.Persistence (2) keys leave RPGmaker for FiresCompanions; NpcConfigBridge chains with Companions' (either load order reaches both mods' keys); [Leaderboard] Batch stat writes moves here from Core (value copied once). Live map stays (NPC routes use it). was 0.0.815:0.0.815 ([ghost], Fire 10-31 minimal profile: "NullReferenceException at FootStep.Start ()" when the Static NPC hammer ghost appears): vanilla's FootStep is removed from the NPC placement ghost in a SetupPlacementGhost postfix (client only), before its Start; placed NPCs keep footsteps; and (Fire: "the resize handler also never shows up", holding Alt+Shift) the quest journal moves objects added under its canvas (Core's live-panel grip + drag strip) onto its private camera layer - its UI camera renders only that layer, so the grip was never drawn. was 0.0.814: 0.0.814([ghost], Fire 10-31; supersedes the never-deployed 0.0.813): a quest page with no quest loaded clears the UI bundle's baked example text (examples only as greyed hints, never saved); a valid starter quest "example_wolf_hunt" (Kill Wolf x10, Coins 50, no commands) ships in Quests\ExampleQuests.cfg, created only when missing, never overwriting; and ("the autocomplete checkbox does not work either in our admin book UI"):the quest page's Auto-Complete toggle, parked on the description caption row, sat under the description input and never got the click - it now has its own canvas above the page + raycaster; one line per flip and one per save (the saved Autocomplete value). was 0.0.813:0.0.813 ([ghost], Fire 10-31 fresh profile: the NPC book's quest page "already has a quest for wolves filled out"): a quest page with no quest loaded clears every field - the UI bundle's baked example text was left in the inputs and Save wrote it as a real quest; examples now show only as the greyed hints, never saved. was 0.0.812:0.0.812 ([ghost], Fire 10-31: the wolves example quest kept "-" in Requirements; saved unchanged, nobody could accept it): a placeholder-only requirement ("-", "—", "none", ...) is no requirement - QuestManager.CheckRequirements skips it (fixes quests already saved with it) and the NPC book saves it as empty. was 0.0.811:0.0.811 ([ghost], Hexium compatibility check on 0.0.810): the five JToken.ToString(Formatting.None) calls in NpcTemplatePresetKind use ToString(Formatting, params JsonConverter[]) - the one-argument overload is not in the game's Newtonsoft.Json (client or dedicated server) and would throw at runtime; same output. was 0.0.810:0.0.810 ([ghost], Fire 10-31 minimal server, RPGmaker without FiresCompanions: "we can spawn the static npc, we just need to be able to have it on the hammer" / "why is it pulling from two different prefabs? make it correct"): ONE prefab - the hammer places the bundle StaticNpc itself (the one spawn uses and every placed body loads as); the hammer-only clone of FiresCompanions' CompanionNpc is gone (without Companions it did not exist, so nothing was on the hammer). One-shot line "[BuildMenu] N NPC piece(s) on the 'Hammer' table ...". was 0.0.809:0.0.809 ([ghost], Fire 10-31 minimal-server shot of the 1.0 build menu): pieces with no Piece.m_usage flag get the one matching their category (NPC pieces -> Misc) - 1.0's Categories list files pieces by m_usage, not m_category, so the NPCs showed under "Show All" only; the quest journal and the NPC / quest editor book are Core live panels (bottom-right grip scales the whole panel, saved per player). was 0.0.808:0.0.808 ([ghost], Fire 10-31 minimal server without FAP: "[Warning: HarmonyX] AccessTools.TypeByName: Could not find type named VerdantsAscent.Api.FapPresets"): every optional-mod type lookup (FAP FapPresets, FAT FatApi, EBM baked pieces, leaderboard canvas types) goes through the quiet QuietTypes.Find, so an absent optional mod prints no HarmonyX warning; one Debug line where it matters. was 0.0.807:0.0.807 ([ghost], Fire 10-31 for a player running RPGmaker alone): without FAP the NPC pieces go on the hammer's vanilla Misc tab (the custom category had no tab - search only); the quest book reads the same active-quest store Accept writes, and an already-active accept shows "Quest already active!" and redraws the book (was a silent no-op). was 0.0.806:0.0.806 ([ghost], Fire 10-31 "spread between frames and budgeted" - "may require some testing"; all three default OFF until their A/B): [Territories] Spread map overlay paint (the 209 ms overlay pass runs as a Core FrameBudgetScheduler coroutine, ~2 ms per frame, one upload at the end); [Territories] Write territory cache in the background (the sync's disk write on a pool thread, newest snapshot wins); [Vault] Serialize received vault data in the background (the 36 ms receive JSON on a pool thread). Debug timing per phase: [TerritoryOverlay] paint, [TerritorySync] chunk / final chunk, [VaultReceive]. was 0.0.805:0.0.805 ([ghost], Fire 10-31 event-driven / cache rule, same behaviour only): BaseAI.UpdateAI null guard uses a FieldRef; the territory HUD's Minimap.m_mode field is looked up once (was every Minimap.Update); the static-NPC nameplate patch reuses its one NpcController lookup. was 0.0.804:0.0.804 ([ghost], lead 10-31; Fire: "group hud is toggled on, but not showing"): the group HUD settings are owned here - [UI.Hud] GroupHud Show / MaxRowsPerColumn / Columns / DefaultPosX / DefaultPosY are handed to Core (CoreSettings.Provide, Core 0.2.1069+ reads them) and a value saved in Core's cfg is copied once; the dead [Companions] ShowGroupHud copy is removed. was 0.0.803:Debug [VisProbe] (key [Debug] VisEquipmentProbe, default OFF, not even patched when off): VisEquipment.CustomUpdate + Character.FixedUpdate cost per kind of body, top 5, slot re-equips per second - proof for the RPGWorld VisEquipment cost; FrameCostProbe now defaults OFF. 0.0.802: NPC templates as the Studio E-window preset kind "npc" (FapPresets.RegisterKind, by reflection) from the SERVER's own template store, filled once by "upload my templates to the server" (admin-only, capped, server-named files); placement and the preset Apply share NpcTemplatePlacedSwapper.WriteAppearance. 0.0.801: the 6 asset bundles load from files next to the DLL (LoadFromFile, embedded copy only as fallback), key General.LoadBundlesFromFiles ON - about 130 MB less RAM. 0.0.800: the debounced vault save writes its file on a background worker (snapshot on the main thread, other file reads/writes wait for it), key Vault.BackgroundVaultWrites ON. 0.0.799: Debug [GlobeHudCost] split of GlobeHudController.Update (liquid / food row / hide vanilla per frame, every 10 s) before the trim. 0.0.798: Debug [RpgFrameCost] - RPGM's own MonoBehaviour Update/LateUpdate/FixedUpdate/OnGUI timed, top 10 by ms/s every 10 s (key Debug.FrameCostProbe). 0.0.797: vault stat FILE saves debounced (one per player per 5 s + world save / logout / quit), key Vault.DebounceStatSaves ON; per-kill Info line now Debug. 0.0.796: Debug [PerfProbe] timing in the kill / skill RPC handlers (RecordStat, RecordSkill, UpdateStat), no behaviour change. 0.0.795: territory map overlay redrawn after the territory load (host showed 0 territories; key Territories.RedrawMapOverlayAfterLoad, default ON); WorldPins Debug timing. 0.0.794: shared world pins (World Studio WP7): server-held, on every map, RpgMapApi ListPins / AddPin / RemovePin for Studio Link. 0.0.793: QuestTracker + GlobeFood fonts resolved once + cached, LegacyRuntime.ttf before Arial.ttf (Unity 6). 0.0.792: territory HUD font resolved once + cached (Unity 6: Arial.ttf threw every Minimap frame), LegacyRuntime.ttf fallback. 0.0.791: NoDeathPenalty HardDeath prefix scoped to Player.OnDeath (the "No skill drain" icon blinked every frame inside the territory). 0.0.790: territory box never self-placed while in Core's minimap column (both UIs); on-load day/time overlap check, reattach to the column when detached + overlapping. 0.0.789: BiomeBlend GetBiomeHeight postfix: FAT check via compiled getter (no per-call boxing), bounds test first; Debug cost line every 30 s. 0.0.788: rpgm_studio_test pre-cleans leftover test NPCs/territories; NPC restore checks the spot before writing the record; refusals name the blocking anchor. 0.0.787: NPC Snapshot serializes its record via Vector3Converter (was a self-reference loop); rpgm_studio_test never goes silent on a throw. 0.0.786: TerritoryStudioOps (server-side World Studio territory ops, internal; not wired yet)
        // was 0.0.785:   // 0.0.783: the live minimap re-renders only after the player moved > 0.5 m or turned (capped at its FPS), else once a second; Debug renders/s line. 0.0.782: 0.0.782: the live map Roofs toggle also hides EBM baked roofs (EbmRoofs.HideRoofsFor, by reflection, map camera only); roof rule = EBM's ("roof", not "wall_roof"). 0.0.781: 0.0.781: live map doors drawn as a thin bar along the wall (true local collider extents, no diamond); Debug proof line on the Roofs toggle (what it hid, and the non-piece renderers over roofs). 0.0.780: 0.0.780 (round #6a): live map levels also count EBM's baked floor pieces (EbmBakedPieces.Near, they have no live object); a homestead focus is held 1 s after the map opens (vanilla put its zoom back). 0.0.779 0.0.779 (Fire 10-27 shots 582): the live map draws EBM's baked stand-ins (renders after EBM's draws); levels from storey floor heights, not 2 m slices of the whole view; renders only on a view change (fps cap) or once a second. 0.0.778 0.0.778 (Fire 10-27 shot 569): the live render stays pinned to the world while the map is dragged or zoomed (overlay uvRect follows the view between renders); "[LiveMap] pieces in range ..." proof line for missing building pieces. 0.0.777 0.0.777 (Fire 10-27 shot 556): LiveMapApi.SetHomesteadMode hides the collapsed Territory pill while Core's homestead map shows; LiveMapApi.DockAnchor for its docked Job Board. 0.0.776 0.0.776 (Fire 10-27 homestead map): LiveMapApi.Focus(center, width, cutaway) for Core 0.2.856+ opens the live map centred on a base. 0.0.775 0.0.775 (Fire 10-27: "doors show incorrectly, we should be marking the frame not the door itself"): the live map and live minimap draw every door closed in its frame (the leaf in its prefab's closed rotation for the map render only, put back the same frame). 0.0.774 0.0.774 ([seasons] for Fire's deep snow): a territory made, edited or removed at runtime refreshes FAT's deep-ground colour at once (Core SnowField.InvalidateBaseCache, then FatApi.RefreshBiomeOverrides, by name; an older Core: FAT asked again 10 s later). 0.0.773 0.0.773 ([visual]: HaloFrame owns NPC body gravity on a ring): StaticNpcInitializer / NpcController no longer set useGravity = true while the body is in a halo ring field (that leaked world gravity for a physics step). 0.0.772 (Fire 10-24, halo ring: gravity / up re-targeted): one RingUp helper (Core GravityUp.InField by reflection; world up off a ring, unchanged there) for territory gravity zones + room capsule, territory spawners (ring floor along -up, upright to the ring), DeepNorth ocean ice + ground paint standing down on a ring, ice-rock ray, static NPC feet snap, NPC anchor facing, the patrol-node and anchor grab tools, dialogue spawns / drops, and pet follow / perch. Proof: "[RingUp] ... on a halo ring" (Debug).
        // ConfigSync's minimum for connecting clients: a FIXED constant, raised only when a synced key's name, type or meaning changes
        // (0.0.571 moved synced keys to new sections). Not PluginVersion, which forced every update in lockstep.
        private const string MinimumSyncVersion = "0.0.763";   // 0.0.763: mail attachments carry item fidelity (escrow returns); 0.0.762: party messages carry the server-stamped sender and NPC re-dress goes through the server, so older clients would silently lose both

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
          MinimumRequiredVersion = MinimumSyncVersion   // A fixed constant, raised only when a synced key changes
 };

   configSync.lockedConfigChanged += ConfigManager.Instance.OnConfigLockChanged;
            AdminSyncing.AdminStatusChanged += _refreshConfigLockOnAdminChange;

            global::FiresRPGmaker.Managers.ConfigMoves.PreBind(Config);   // Merged sections, moved before any bind
            ConfigManager.Instance.Initialize(Config);
            try { global::FiresRPGmaker.Modules.Pets.PetVisual.BindConfig(Config); } catch { }
            // Pets.Progress joins the first config sync
            try { global::FiresRPGmaker.Modules.Pets.Progress.PetProgress.BindConfig(); } catch (Exception ex) { Logger.LogWarning($"PetProgress.BindConfig: {ex.Message}"); }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.TurnSecondsCfg = Config.Bind("Poker", "PlayerTurnSeconds", 60, new BepInEx.Configuration.ConfigDescription("Seconds a seated human has to act at the poker table before auto-fold (min 10).", new BepInEx.Configuration.AcceptableValueRange<int>(10, 600))); } catch { }
            try { global::FiresRPGmaker.Modules.Marketplace.MarketplaceNetwork.ListingLifetimeDaysCfg = Config.Bind("Marketplace", "ListingLifetimeDays", 7, new BepInEx.Configuration.ConfigDescription("Real days a marketplace listing stays up before its item is mailed back to the seller. 0 = never expire. Read by the server.", new BepInEx.Configuration.AcceptableValueRange<int>(0, 3650))); } catch { }
            try { global::FiresRPGmaker.Modules.Poker.PokerAI.DifficultyCfg = Config.Bind("Poker", "Difficulty", "Medium", new BepInEx.Configuration.ConfigDescription("Poker AI table strength (hot-reloads).", new BepInEx.Configuration.AcceptableValueList<string>("Easy", "Medium", "Hard", "Master"))); } catch { }
            try
            {
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.CurrencyCfg = Config.Bind("Poker", "CashCurrency", "", "Currency item played at open (cash) tables - bank it with the Banker to buy in. Empty = Coins.");   // 10-20: ships empty (Coins in code)
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.BuyInCfg = Config.Bind("Poker", "CashBuyIn", 500, new BepInEx.Configuration.ConfigDescription("Open-table buy-in, withdrawn from your bank when you sit down.", new BepInEx.Configuration.AcceptableValueRange<int>(0, 1000000)));
                global::FiresRPGmaker.Modules.Poker.PokerCashTables.BigBlindCfg = Config.Bind("Poker", "CashBigBlind", 10, new BepInEx.Configuration.ConfigDescription("Open-table big blind.", new BepInEx.Configuration.AcceptableValueRange<int>(1, 100000)));
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.DifficultyCfg = Config.Bind("Chess", "Difficulty", "Medium", new BepInEx.Configuration.ConfigDescription("Chess AI strength (hot-reloads; a firesnpcs_chess depth argument overrides it).", new BepInEx.Configuration.AcceptableValueList<string>("Easy", "Medium", "Hard", "Master"))); } catch { }
            try { global::FiresRPGmaker.UI.Books.FiresBookView.DebugCapture = Config.Bind("General", "BookViewCaptures", false, "Dev tool (off by default): when a 3D book view opens, dump render PNGs + a state file to BepInEx/config/FiresRPGmaker/Debug/BookView. Off avoids the login capture stall + [FiresBookView] log spam.").Value; } catch { }
            global::FiresRPGmaker.Managers.ConfigMoves.Apply(Config);   // [Debug] BookViewCaptures -> [General]
            global::FiresRPGmaker.UI.FiresKitBookSkin.Bind(Config);   // [UI] Use classic book UI (default true = today)
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.UiScaleCfg = Config.Bind("Poker", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Poker window scale (drag the corner grip in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f)));
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.OffsetXCfg = Config.Bind("Poker", "OffsetX", 0f, new BepInEx.Configuration.ConfigDescription("Poker window horizontal position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.OffsetYCfg = Config.Bind("Poker", "OffsetY", 0f, new BepInEx.Configuration.ConfigDescription("Poker window vertical position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.UiScaleCfg = Config.Bind("Chess", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Chess window scale - stretches the whole board UI (hot-reloads).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f))); } catch { }
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.OffsetXCfg = Config.Bind("Chess", "OffsetX", 0f, new BepInEx.Configuration.ConfigDescription("Chess window horizontal position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.OffsetYCfg = Config.Bind("Chess", "OffsetY", 0f, new BepInEx.Configuration.ConfigDescription("Chess window vertical position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            }
            catch { }
            try { global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.UiScaleCfg = Config.Bind("Fanorona", "UiScale", 1.0f, new BepInEx.Configuration.ConfigDescription("Fanorona window scale - stretches the whole board UI (hot-reloads).", new BepInEx.Configuration.AcceptableValueRange<float>(0.6f, 1.6f))); } catch { }
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.OffsetXCfg = Config.Bind("Fanorona", "OffsetX", 0f, new BepInEx.Configuration.ConfigDescription("Fanorona window horizontal position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
                global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.OffsetYCfg = Config.Bind("Fanorona", "OffsetY", 0f, new BepInEx.Configuration.ConfigDescription("Fanorona window vertical position (drag the title bar in-game).", new BepInEx.Configuration.AcceptableValueRange<float>(-4000f, 4000f)));
            }
            catch { }
 BindConfigsToSync();


            // Restore persisted chat channel preference (client-only, not synced).
            ChatChannelManager.InitConfig(Config);

            // Settings UI tags - after every startup bind and the ConfigSync adds.
            global::FiresRPGmaker.RpgConfigUi.Apply(Config);

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
            // Minimap / Camera / RenderTexture, client only.
            "FiresRPGmaker.UI.Map.LiveMap+MinimapStartPatch",
            "FiresRPGmaker.Modules.WorldPins.WorldPins+ClearOnMapDestroy",
            "FiresRPGmaker.Modules.WorldPins.WorldPins+KeepWorldPins",
            "FiresRPGmaker.Modules.WorldPins.WorldPins+ShowOnMapStart",
            // 0.0.815: the hammer ghost exists only on clients
            "FiresRPGmaker.Managers.VAPieceManager+StaticNpcGhost_NoFootStep",
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
            global::FiresRPGmaker.UI.FiresStatsSections.TryRegister();   // Core hub Stats cards (no-op before Core 0.2.510)
            global::FiresRPGmaker.Managers.NpcTemplatePresetKind.Register();   // Studio preset kind 'npc' (no-op before FAP 1.1.341)
        }
        protected override void TitleScene(bool isFirstBoot) { }

        private static void EmitWorldStartSummary()
        {
            try
            {
                // RPG Maker's own guild module is removed; guilds come from FiresGuilds or VikingLands.Core.
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

            // RPG Maker's own guild module is removed. Guilds come from FiresGuilds (or VikingLands.Core);
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
                    global::FiresRPGmaker.Modules.WorldPins.WorldPins.EnsureRegistered();
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
        /// (It no longer registers guilds; RPG Maker's guild module is removed.)
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
                    global::FiresRPGmaker.Modules.WorldPins.WorldPins.EnsureRegistered();
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
            configSync.AddConfigEntry(cm.configMaxTerritoriesPerPlayer);
            configSync.AddConfigEntry(cm.configLedgerPlayerFlags);
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
                global::FiresRPGmaker.FiresLogger.LogDebug($"{PluginName}: AssetBundle already loaded.");
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

                // The baked UI panels and the inventory Leaderboards tab are UI only;
                // a dedicated server shows no UI, so it neither loads those two bundles nor installs their hosts.
                bool headless = FiresCore.Lifecycle.FiresMod.IsDedicatedServer;
                uiBundle = headless ? null : GetAssetBundleFromResources("firesnpcsui");
                booksBundle = GetAssetBundleFromResources("firesbooks");   // self-contained (ships Custom/FiresPiece); no cross-bundle deps
                leaderboardTabBundle = headless ? null : GetAssetBundleFromResources("firesleaderboardtab");
                chessBundle = GetAssetBundleFromResources("vaitems_chessboard");   // self-contained vanilla-clone chess table

                if (headless) Debug.Log("[FiresRPGmaker] dedicated server: the UI bundles (firesnpcsui, firesleaderboardtab) are not loaded.");
                else
                {
                    global::FiresRPGmaker.UI.FiresNpcsUIBuilderHost.Install();
                    global::FiresRPGmaker.UI.Leaderboard.LeaderboardInventoryTab.Register();
                    global::FiresRPGmaker.UI.Leaderboard.LeaderboardHubModule.Register();   // Player > Leaderboard in the Core hub
                }

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

        private static long s_bundleFileBytes; private static int s_bundleFiles, s_bundleEmbedded;
        private AssetBundle GetAssetBundleFromResources(string name)
        {
            // 0.0.801 (lead 10-30, VMMap of Fire's client: FiresRPGmaker.dll 75 MB resident): the six bundles ship as FILES next to
            // the DLL and load with AssetBundle.LoadFromFile - no managed byte[] copies, no native in-memory copy, and the embedded
            // resource pages of the DLL are never touched (so they never become resident). The embedded copy stays as a fallback
            // when the file is missing (an install without the extras). Key [General] LoadBundlesFromFiles, default ON.
            try
            {
                bool fromFiles = ConfigManager.Instance?.configLoadBundlesFromFiles?.Value ?? true;
                string dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = string.IsNullOrEmpty(dir) ? null : System.IO.Path.Combine(dir, name);
                if (fromFiles && path != null && System.IO.File.Exists(path))
                {
                    var fromFile = AssetBundle.LoadFromFile(path);
                    if (fromFile != null)
                    {
                        s_bundleFiles++; s_bundleFileBytes += new System.IO.FileInfo(path).Length;
                        if (s_bundleFiles == 1) Debug.Log($"{PluginName}: asset bundles load from the files next to the DLL: ON (fix 0.0.801, no in-memory copies)");
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Bundles] '{name}' loaded from file ({new System.IO.FileInfo(path).Length / 1048576f:0.0} MB, no in-memory copy) - {s_bundleFiles} from files so far, {s_bundleFileBytes / 1048576f:0.0} MB");
                        return fromFile;
                    }
                    Debug.LogWarning($"{PluginName}: bundle file '{path}' did not load - using the embedded copy");
                }
                else if (fromFiles) Debug.LogWarning($"{PluginName}: bundle file '{name}' not found next to the DLL - using the embedded copy (more RAM; deploy the bundle files)");
            }
            catch (Exception ex) { Debug.LogWarning($"{PluginName}: bundle file load for '{name}' failed ({ex.Message}) - using the embedded copy"); }
            s_bundleEmbedded++;
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
                            global::FiresRPGmaker.FiresLogger.LogDebug($"{PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs");
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
                        global::FiresRPGmaker.FiresLogger.LogDebug($"{PluginName}: Scrubbed {dead.Count} fake-null entries from ObjectDB.m_itemByHash");
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
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Territory sync retry - no territories received yet");
                TerritorySyncedState.RequestFromServer();
            }
        }

        /// <summary>
        /// Diagnostic for the guild hand-off (FiresGuilds or VikingLands.Core own guilds; removed RPG Maker's own module):
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
            global::FiresRPGmaker.FiresLogger.LogDebug($"{PluginName}: Waiting for ZNetScene & ObjectDB...");
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
                          global::FiresRPGmaker.Modules.Sounds.NpcSoundSync.Init();   // Synced custom NPC sounds
                          global::FiresRPGmaker.UI.PatrolRouteSync.EnsureRegistered();   // Route key via the server

                           if (ZNet.instance != null && !ZNet.instance.IsServer())
                           {
                        TerritorySyncedState.RequestFromServer();
                               StartCoroutine(RetryTerritorySync());
                               // Request sprite + UI asset sync from server on first login (deferred to ensure connection is ready)
                               StartCoroutine(DeferredSpriteSyncRequest());

                               // No guild sync of our own (the module is removed). When a guild mod owns
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
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[{PluginName}] Periodic SaveAllDirty executed ({allPlayers.Count} players online)");
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
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Re-registered {FiresRPGmaker.itemPrefabs.Count} item prefabs to ZNetScene on Awake");
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
             global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Re-registered items to ObjectDB on Awake");
         }


            // Re-register piece prefabs to hammer and ZNetScene.
            // AddPiecesToHammer internally registers each piece to ZNetScene.m_namedPrefabs
            // and adds it to the hammer's PieceTable - both are required for PlacePiece.
            if (FiresRPGmaker.piecePrefabs.Count > 0 && ZNetScene.instance != null)
            {
                VAPieceManager.AddPiecesToHammer();
                global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Re-registered {FiresRPGmaker.piecePrefabs.Count} piece prefabs to hammer on ObjectDB Awake");
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











