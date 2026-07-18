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

            // Each step wrapped in try/catch with a tagged label so we can see which cleanup the 6×
            // "GameObjects can not be made active when they are being destroyed" errors actually come from,
            // and so one bad cleanup doesn't abort the rest.
            void Step(string tag, System.Action a) { try { a(); } catch (System.Exception e) { Debug.LogError($"[UIInitializer] cleanup '{tag}' threw: {e.GetType().Name} — {e.Message}"); } }

            Step("destroy panel", () => { if (_panelInstance != null) { Object.Destroy(_panelInstance); _panelInstance = null; } });
            Step("GUIManager", () => GUIManager.Cleanup());
            Step("GlobeHud",   () => GlobeHudController.Cleanup());
            Step("HelpPanel",  () => FiresCore.Help.HelpPanel.Cleanup());
            Step("TraderUI",   () => TraderUIController.Cleanup());
            Step("InfoPanel",  () => InfoNpcPlayerPanel.Cleanup());
            Step("QuestPreview", () => QuestPreviewRenderer.Cleanup());
            Step("SpriteCache",  () => CodegenUIWiring.ClearSpriteCache());
            Step("PanelElemCache", () => PanelElementManager.ClearCache());
        }

        /// <summary>
        /// Reloads all UI-related config data.
        /// </summary>
        public static void Reload()
        {
            DialogueManager.Reload();
            QuestProfileManager.Reload();
            NpcMainPanelController.Instance?.RefreshProfiles();
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
            if (_panelInstance != null)
            {
                FiresLogger.LogVerbose("[UIInitializer] Panel instance already exists");
                return;
            }

            // Wait for game UI to be ready
            if (Hud.instance == null)
            {
                FiresLogger.LogVerbose("[UIInitializer] Hud not ready, deferring UI creation");
                return;
            }

            FiresLogger.LogVerbose("[UIInitializer] Creating panel instance via codegen...");

            // Log available fonts for debugging (remove after confirming fonts are found)
            UIFontConfig.LogAvailableFonts();

            // Build a standalone, top-level Canvas instead of nesting under the Hud canvas.
            // Parenting under Hud's canvas causes every panel show/hide to dirty the entire
            // inventory grid hierarchy and tank fps. Match the help panel pattern.
            var canvasGo = new GameObject("FiresNPCs_NpcPanelCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // sortingOrder must exceed Valheim's IngameGui sibling canvases (HudMessage,
            // EnemyHud, DamageText etc.) so center-screen messages don't render over the
            // panel. 200 stays below the help panel (300) which is meant to cover us.
            canvas.sortingOrder = 200;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            FiresLogger.LogVerbose($"[UIInitializer] Created standalone NPC panel canvas (sortingOrder={canvas.sortingOrder})");

            // Build the entire UI from codegen as a child of our standalone canvas.
            var root = CodegenUIWiring.CreateAndWireUI(canvasGo.transform);
            if (root == null)
            {
                Object.Destroy(canvasGo);
                Debug.LogError("[UIInitializer] CodegenUIWiring.CreateAndWireUI returned null!");
                return;
            }

            // _panelInstance tracks the standalone canvas so cleanup destroys the
            // full hierarchy on logout (it's no longer auto-destroyed with Hud).
            _panelInstance = canvasGo;
            _usingAssetBundle = false;

            // Keep Root always active � it's just a transparent stretch-fill container.
            // Hide the actual NpcPanel; ShowForNpc() toggles it via NpcMainPanelController.
            var panelGo = NpcMainPanelController.Instance?.gameObject;
            if (panelGo != null)
                panelGo.SetActive(false);
            else
                _panelInstance.SetActive(false);

            // Verify final state
            var controller = NpcMainPanelController.Instance;
            FiresLogger.LogVerbose($"[UIInitializer] NPC panel created via codegen. Instance: {(controller != null ? "SET" : "NULL")}, " +
                      $"ContentParent: {(controller?.contentParent != null ? "SET" : "NULL")}, " +
                      $"CloseButton: {(controller?.closeButton != null ? "SET" : "NULL")}, " +
                      $"ApplyButton: {(controller?.applyButton != null ? "SET" : "NULL")}");
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
