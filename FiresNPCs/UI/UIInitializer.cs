using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.UI.QuestJournal;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Initializes the UI system and creates the main panel.
    /// Supports both asset bundle loading (preferred) and programmatic fallback.
    /// </summary>
    public static class UIInitializer
    {
        private static bool _initialized;
        private static GameObject _panelInstance;
        private static bool _usingAssetBundle;

        // Static constructor to ensure patches are applied
        static UIInitializer()
        {
            FiresLogger.LogVerbose("[UIInitializer] Static constructor called - patches should be registered");
        }

        /// <summary>
        /// Initializes the UI system. Should be called after the game UI is ready.
        /// </summary>
        public static void Initialize()
        {
            // Hard-skip on true dedicated servers (no graphics device).  We saw a
            // native crash inside CodegenUIWiring.CreateAndWireUI on dedi when
            // codegen tried to spawn UI prefabs against a null graphics context;
            // the crash was recoverable but deadlocked downstream init.  UI is
            // pointless on a headless server anyway.
            if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                FiresLogger.LogVerbose("[UIInitializer] Dedicated server detected (graphicsDeviceType=Null) — skipping UI initialization.");
                return;
            }

            // Check if we need to reinitialize (panel was destroyed on logout)
            if (_initialized && _panelInstance == null)
            {
                FiresLogger.LogVerbose("[UIInitializer] Panel was destroyed (logout), reinitializing...");
                _initialized = false;
            }

            if (_initialized) return;
            _initialized = true;

            FiresLogger.LogVerbose("[UIInitializer] Initialize() called");

            // Initialize GUIManager for font config change handling
            GUIManager.Initialize();

            // Initialize managers
            DialogueManager.Initialize();
            QuestProfileManager.Initialize();
            VAMiscAssetManager.Initialize();

            // Create panel instance when Hud is available
            CreatePanelInstance();

            // Initialize the Quest Journal hotkey handler
            QuestJournalHotkeyHandler.EnsureCreated();

            // Initialize the Leaderboard hotkey handler (default L)
            LeaderboardHotkeyHandler.EnsureCreated();

            // Group HUD (companion stat bars) removed — companion HUD belongs to FiresCompanions.

            // Initialize the Globe HUD overlay (Health/Eitr/Stamina/Adrenaline globes)
            // Only create if the user hasn't disabled it in the config.
            if (ConfigManager.Instance.configUseGlobeHud?.Value != false)
                GlobeHudController.Initialize();

            // Initialize the shared help panel (accessible from ESC menu button)
            FiresNpcsHelpContent.Install();


            FiresLogger.LogVerbose($"[UIInitializer] UI system initialized (AssetBundle: {_usingAssetBundle})");
        }

        /// <summary>
        /// Resets the initialization state. Called when the Hud is destroyed (on logout).
        /// </summary>
        public static void ResetInitialization()
        {
            FiresLogger.LogVerbose("[UIInitializer] Resetting initialization state (logout detected)");
            _initialized = false;

            // Runs inside Hud.OnDestroy: the whole Hud hierarchy is being destroyed, so no step may SetActive(true)
            // anything under it. Each step is tagged and wrapped so one bad cleanup doesn't abort the rest.
            void Step(string tag, System.Action a) { try { a(); } catch (System.Exception e) { Debug.LogError($"[UIInitializer] cleanup '{tag}' threw: {e.GetType().Name} — {e.Message}"); } }

            Step("destroy panel", () => { if (_panelInstance != null) { Object.Destroy(_panelInstance); _panelInstance = null; } });
            Step("GUIManager", () => GUIManager.Cleanup());
            Step("GlobeHud",   () => GlobeHudController.Cleanup());
            Step("HelpPanel",  () => FiresCore.Help.HelpPanel.Cleanup());
            Step("TraderUI",   () => TraderUIController.Cleanup());
            Step("InfoPanel",  () => InfoNpcPlayerPanel.Cleanup());
            Step("QuestPreview", () => QuestPreviewRenderer.Cleanup());
            Step("SpriteCache",  () => CodegenUIWiring.ClearSpriteCache());
        }

        /// <summary>
        /// Reloads all UI-related config data.
        /// </summary>
        public static void Reload()
        {
            DialogueManager.Reload();
            QuestProfileManager.Reload();
            FiresLogger.LogVerbose("[UIInitializer] UI config reloaded");
        }

        /// <summary>
        /// Sets the asset bundle to use for loading UI prefabs.
        /// Call this before Initialize() if you have a custom asset bundle.
        /// </summary>
        public static void SetAssetBundle(AssetBundle bundle)
        {
            UIPrefabFactory.UIAssetBundle = bundle;
        }

        private static void CreatePanelInstance()
        {
            // RETIRED: the codegen NpcMainPanel is gone. Every admin editor it hosted moved to the
            // animated book (NpcBookController), and player quest accept/turn-in runs on the book
            // (QuestBookJournal) too — see QuestInteractionScreenController.ShowForNpc. Nothing shows
            // this panel anymore, so we no longer build the whole completecompanionsui codegen tree
            // (it was the source of the parallel-admin-UI confusion). The controller classes still
            // exist for their STATIC logic; they are simply never instantiated. NpcMainPanelController
            // .Instance stays null and every consumer already null-guards it.
            return;
        }


        /// <summary>
        /// Gets the UI root transform (Hud canvas).
        /// </summary>
        public static Transform GetUIRoot()
        {
            if (Hud.instance != null)
                return Hud.instance.transform;
            return null;
        }

        /// <summary>
        /// Gets whether the UI is using asset bundle prefabs.
        /// </summary>
        public static bool IsUsingAssetBundle => _usingAssetBundle;

        /// <summary>
        /// Force recreates the panel (useful after font config changes).
        /// </summary>
        public static void RecreatePanel()
        {
            if (_panelInstance != null)
            {
                Object.Destroy(_panelInstance);
                _panelInstance = null;
            }

            CodegenUIWiring.ClearSpriteCache();
            _initialized = false;
            CreatePanelInstance();
        }

        /// <summary>
        /// Harmony patch to initialize UI when Hud is ready.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class Hud_Awake_Patch
        {
            private static void Postfix()
            {
                // Always try to initialize - Initialize() will check if already done
                Initialize();
            }
        }

        /// <summary>
        /// Harmony patch to reset initialization when Hud is destroyed (on logout).
        /// </summary>
        [HarmonyPatch(typeof(Hud), "OnDestroy")]
        private static class Hud_OnDestroy_Patch
        {
            private static void Prefix()
            {
                ResetInitialization();
            }
        }

        /// <summary>
        /// Harmony patch to handle Escape key and custom panel closing.
        /// </summary>
        [HarmonyPatch(typeof(Menu), "IsVisible")]
        private static class Menu_IsVisible_Patch
        {
            private static void Postfix(ref bool __result)
            {
                if (GUIManager.IsCustomPanelOpen())
                {
                    __result = true;
                }
            }
        }

        /// <summary>
        /// THE Escape hook. Valheim opens the pause menu ONLY through Menu.Show() (Menu.cs:264, when Escape is
        /// pressed and no vanilla UI is open). We intercept Show() itself: if one of our UI panels is open, we
        /// CLOSE the panel here and swallow the open (return false), so the menu never appears. Because the
        /// close fires as a RESULT of intercepting the vanilla open — not from a separate per-UI Update reading
        /// its own Escape — the timing is exact and there is no race: the panels no longer self-close on Escape,
        /// so IsCustomPanelOpen() is reliably true at this point. A second Escape (nothing open) falls through
        /// and the menu opens normally. Wrapped so an inspection error can never leak the menu through.
        /// </summary>
        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        private static class Menu_Show_Guard
        {
            private static bool Prefix()
            {
                try
                {
                    // Menu.Show() is the ONLY way Valheim opens the pause menu (Menu.cs:264 calls it when
                    // ZInput.GetKeyDown(Escape) & flag). We do NOT read the Escape key ourselves — we let vanilla
                    // decide, on vanilla's own input frame, then intercept the Show it produces. At that instant a
                    // Fires panel that owns this Escape is still open (nothing self-closes it first), so close it
                    // here and swallow the menu open. This is input-source agnostic — the earlier Menu.Update
                    // guard read Input.GetKeyDown and closed the panel a frame BEFORE vanilla's ZInput fired, so
                    // Show then saw no panel and let the menu through. Removed. A 2nd Escape (nothing open) → menu.
                    bool open = GUIManager.IsCustomPanelOpen();
                    if (open)
                    {
                        GUIManager.CloseAllPanels();
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        /// <summary>kg's proven approach (Marketplace Dialogues_Main_Client: on Escape + dialogue visible it calls
        /// Menu.instance.OnClose()). We run it as a POSTFIX on Menu.Update — AFTER vanilla has done any open — as
        /// a state-based catch-all: whenever a Fires panel is open, the pause menu must NOT be, so if its root is
        /// active, close it with OnClose() (== Hide()). This backstops the Menu.Show prevent-hook and catches any
        /// path that activates the menu root without going through the Show we patched (the "menu opened but no
        /// Show log" case in the logs). Reads no input, so there is no ZInput/Input frame skew. If Show already
        /// blocked the open, the root is inactive and this is a no-op.</summary>
        [HarmonyPatch(typeof(Menu), "Update")]
        private static class Menu_Update_MutexPostfix
        {
            private static void Postfix()
            {
                try
                {
                    if (!GUIManager.IsCustomPanelOpen()) return;
                    var m = Menu.instance;
                    if (m != null && m.m_root != null && m.m_root.gameObject.activeSelf)
                    {
                        m.OnClose();
                    }
                }
                catch { }
            }
        }
    }
}
