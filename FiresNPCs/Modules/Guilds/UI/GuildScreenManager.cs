using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Modules.Guilds.UI
{
    /// <summary>
    /// Singleton manager that owns the guild screen lifecycle.
    ///
    /// KEYBIND
    /// -------
    ///   G (configurable) opens/closes the guild screen.
    ///   Pressing G when no guild is joined opens GuildSearchScreen instead.
    ///
    /// SCREEN OWNERSHIP
    /// ----------------
    ///   Only one guild screen is active at a time.  GuildMainScreen,
    ///   GuildCreateScreen, and GuildSearchScreen are instantiated lazily
    ///   and hidden/shown as needed.
    /// </summary>
    public class GuildScreenManager : MonoBehaviour
    {
        public static GuildScreenManager Instance { get; private set; }

        private GuildMainScreen   _mainScreen;
        private GuildCreateScreen _createScreen;
        private GuildSearchScreen _searchScreen;

        private bool _anyOpen;

        // ── Singleton ─────────────────────────────────────────────────────────

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("VA_GuildScreenManager");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GuildScreenManager>();
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Update — keyboard shortcut ────────────────────────────────────────

        private void Update()
        {
            // Only respond when in-world and no other full-screen UI is up.
            if (Player.m_localPlayer == null) return;
            if (InventoryGui.IsVisible()) return;
            if (Minimap.IsOpen()) return;
            if (Chat.instance != null && Chat.instance.HasFocus()) return;
            if (GUIManager.IsCustomPanelOpen()) return;

            // Don't fire the open-hotkey when typing into ANY input field (TMP or
            // legacy Unity UI).  Without this, typing 'g' in the create-guild name
            // box - or any other text input anywhere in the mod - calls Toggle()
            // and slams the panel shut on the user.  The IsCustomPanelOpen check
            // above already covers our own panels, but this is defence in depth
            // for any other mod's input field (Jotunn config menu, etc.) that
            // might steal focus while our manager Update is still ticking.
            if (IsAnyInputFieldFocused()) return;

            var key = ConfigManager.Instance?.configGuildHotkey?.Value ?? KeyCode.G;
            if (key != KeyCode.None && ZInput.GetKeyDown(key) && !ZInput.IsGamepadActive())
                Toggle();
        }

        /// <summary>
        /// True when an input control (TMP_InputField or legacy InputField) currently
        /// owns the EventSystem keyboard focus.  Used to suppress every keybind in
        /// this manager so typed characters end up in the input field, not as
        /// hotkey presses.
        /// </summary>
        private static bool IsAnyInputFieldFocused()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var sel = es.currentSelectedGameObject;
            if (sel == null) return false;
            if (sel.GetComponent<TMPro.TMP_InputField>() != null) return true;
            if (sel.GetComponent<UnityEngine.UI.InputField>() != null) return true;
            return false;
        }

        // ── Public open/close API ─────────────────────────────────────────────

        public void Toggle()
        {
            if (_anyOpen) CloseAll();
            else Open();
        }

        public void Open()
        {
            var guild = GuildAPI.GetOwnGuild();
            if (guild != null)
                OpenMain(guild);
            else
                OpenSearch();
        }

        // VANILLA MENU SHIM
        // -----------------
        // Cursor unlocking is handled by GUIManager.IsCustomPanelOpen() — the
        // Menu.IsVisible Harmony postfix returns true while any custom panel is
        // open, which is what vanilla uses to keep the cursor free.  So the
        // shim's only job here is to make sure the vanilla escape menu isn't
        // rendering on top of (or invisibly underneath) the guild UI when ESC
        // was pressed before G.  We just SetActive(false) on Menu.m_root and
        // restore on close; no CanvasGroup alpha trick (which still rendered
        // every element transparently and added draw-call cost).

        private bool _menuWasOpen;

        private void EnterVanillaMenuShim()
        {
            var menu = Menu.instance;
            if (menu == null || menu.m_root == null) return;

            var menuGo = menu.m_root.gameObject;
            if (menuGo.activeSelf)
            {
                _menuWasOpen = true;
                menuGo.SetActive(false);
            }
        }

        private void ExitVanillaMenuShim()
        {
            if (_menuWasOpen && Menu.instance != null && Menu.instance.m_root != null)
            {
                Menu.instance.m_root.gameObject.SetActive(true);
            }
            _menuWasOpen = false;
        }

        public void OpenMain(GuildData guild = null)
        {
            CloseAll();
            EnsureMain();
            EnterVanillaMenuShim();
            _mainScreen.Show(guild ?? GuildAPI.GetOwnGuild());
            _anyOpen = true;
        }

        public void OpenCreate()
        {
            CloseAll();
            EnsureCreate();
            EnterVanillaMenuShim();
            _createScreen.Show();
            _anyOpen = true;
        }

        public void OpenSearch()
        {
            CloseAll();
            EnsureSearch();
            EnterVanillaMenuShim();
            _searchScreen.Show();
            _anyOpen = true;
        }

        public void CloseAll()
        {
            _mainScreen?.Hide();
            _createScreen?.Hide();
            _searchScreen?.Hide();
            if (_anyOpen) ExitVanillaMenuShim();
            _anyOpen = false;
        }

        public bool IsAnyOpen() => _anyOpen;

        /// <summary>
        /// Called by GuildMainScreen's static history-hook when the server
        /// ships the persisted chat backlog. If the main screen is currently
        /// open, refresh its chat log from the new history.
        /// </summary>
        public void RefreshOpenChatLogs()
        {
            if (_mainScreen != null && _mainScreen.gameObject.activeInHierarchy)
                _mainScreen.RebuildChatLogPublic();
        }

        // ── Lazy screen creation ──────────────────────────────────────────────

        private void EnsureMain()
        {
            if (_mainScreen != null) return;
            var go = new GameObject("VA_GuildMainScreen");
            go.transform.SetParent(transform, false);
            _mainScreen = go.AddComponent<GuildMainScreen>();
            _mainScreen.OnClose += CloseAll;
            _mainScreen.OnOpenSearch += OpenSearch;
            _mainScreen.OnOpenCreate += OpenCreate;
        }

        private void EnsureCreate()
        {
            if (_createScreen != null) return;
            var go = new GameObject("VA_GuildCreateScreen");
            go.transform.SetParent(transform, false);
            _createScreen = go.AddComponent<GuildCreateScreen>();
            _createScreen.OnClose  += CloseAll;
            _createScreen.OnCreated += () => OpenMain();
        }

        private void EnsureSearch()
        {
            if (_searchScreen != null) return;
            var go = new GameObject("VA_GuildSearchScreen");
            go.transform.SetParent(transform, false);
            _searchScreen = go.AddComponent<GuildSearchScreen>();
            _searchScreen.OnClose       += CloseAll;
            _searchScreen.OnCreateGuild += OpenCreate;
        }
    }
}
