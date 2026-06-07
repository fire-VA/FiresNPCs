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
            Debug.Log("[UIInitializer] Static constructor called - patches should be registered");
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
                Debug.Log("[UIInitializer] Dedicated server detected (graphicsDeviceType=Null) — skipping UI initialization.");
                return;
            }

            // Check if we need to reinitialize (panel was destroyed on logout)
            if (_initialized && _panelInstance == null)
            {
                Debug.Log("[UIInitializer] Panel was destroyed (logout), reinitializing...");
                _initialized = false;
            }

            if (_initialized) return;
            _initialized = true;

            Debug.Log("[UIInitializer] Initialize() called");

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

            // Initialize the Group HUD for companion stat bars
            GroupHudController.Initialize();

            // Initialize the Globe HUD overlay (Health/Eitr/Stamina/Adrenaline globes)
            // Only create if the user hasn't disabled it in the config.
            if (ConfigManager.Instance.configUseGlobeHud?.Value != false)
                GlobeHudController.Initialize();

            // Initialize the shared help panel (accessible from ESC menu button)
            FiresNpcsHelpContent.Install();


            Debug.Log($"[UIInitializer] UI system initialized (AssetBundle: {_usingAssetBundle})");
        }

        /// <summary>
        /// Resets the initialization state. Called when the Hud is destroyed (on logout).
        /// </summary>
        public static void ResetInitialization()
        {
            Debug.Log("[UIInitializer] Resetting initialization state (logout detected)");
            _initialized = false;

            // The NPC panel canvas is a top-level GameObject (not parented under Hud),
            // so it isn't auto-destroyed when Hud is destroyed. Destroy it explicitly
            // to avoid leaking a stale canvas across login sessions.
            if (_panelInstance != null)
            {
                Object.Destroy(_panelInstance);
                _panelInstance = null;
            }

            // Cleanup GUIManager subscriptions
            GUIManager.Cleanup();

            // Cleanup Group HUD
            GroupHudController.Cleanup();

            // Cleanup Globe HUD overlay
            GlobeHudController.Cleanup();

            // Cleanup Help Panel
            FiresCore.Help.HelpPanel.Cleanup();

            // Cleanup standalone-canvas NPC interaction UIs (they live outside Hud,
            // so they don't auto-destroy when Hud is destroyed).
            TraderUIController.Cleanup();
            InfoNpcPlayerPanel.Cleanup();

            // Cleanup Quest Preview Renderer
            QuestPreviewRenderer.Cleanup();

            // Clear cached sprites so they're re-resolved on next login
            CodegenUIWiring.ClearSpriteCache();

            // Clear PanelElementManager's element lookup cache
            PanelElementManager.ClearCache();

            // Also clear screen controller instances since they're destroyed with the scene
            // QuestInteractionScreenController.Instance is handled by its OnDestroy
        }

        /// <summary>
        /// Reloads all UI-related config data.
        /// </summary>
        public static void Reload()
        {
            DialogueManager.Reload();
            QuestProfileManager.Reload();
            NpcMainPanelController.Instance?.RefreshProfiles();
            Debug.Log("[UIInitializer] UI config reloaded");
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
                Debug.Log("[UIInitializer] Panel instance already exists");
                return;
            }

            // Wait for game UI to be ready
            if (Hud.instance == null)
            {
                Debug.Log("[UIInitializer] Hud not ready, deferring UI creation");
                return;
            }

            Debug.Log("[UIInitializer] Creating panel instance via codegen...");

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

            Debug.Log($"[UIInitializer] Created standalone NPC panel canvas (sortingOrder={canvas.sortingOrder})");

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
            Debug.Log($"[UIInitializer] NPC panel created via codegen. Instance: {(controller != null ? "SET" : "NULL")}, " +
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
        /// Harmony patch to close our panels on Escape before game menu opens.
        /// </summary>
        [HarmonyPatch(typeof(Menu), "Update")]
        private static class Menu_Update_Patch
        {
            private static bool Prefix()
            {
                if (Input.GetKeyDown(KeyCode.Escape) && GUIManager.IsCustomPanelOpen())
                {
                    Debug.Log("[UIInitializer] Menu_Update_Patch - Escape pressed, closing panels");
                    GUIManager.CloseAllPanels();
                    return false;
                }
                return true;
            }
        }
    }
}
