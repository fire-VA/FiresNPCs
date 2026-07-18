using System;
using UnityEngine;

namespace FiresRPGmaker.UI.Leaderboard
{
    /// <summary>
    /// Ported leaderboard window (TDV's live uGUI canvas path), reskinned to the Fires parchment theme and
    /// retargeted to <see cref="FiresLeaderboardAdapter"/> (which reads <c>LeaderboardBoard.Rows</c>).
    /// Self-managed <c>DontDestroyOnLoad</c> MonoBehaviour; no prefab, no bundle — it builds itself at runtime
    /// like the rest of the Fires UI. The dead IMGUI half and the (force-hidden) vanilla window shell from TDV
    /// are not ported; the parchment <c>CreatePanelBox</c> owns the background.
    /// </summary>
    public sealed partial class FiresLeaderboardGui : MonoBehaviour
    {
        private const string ViewRanking = "ranking";
        private const string ViewAchievements = "achievements";
        private const string ViewProgression = "progression";
        private const string DetailViewMode = "player_details";

        private const float BaseWindowWidth = 1120f;
        private const float BaseWindowHeight = 780f;
        private const float WindowScale = 1.25f;
        private const float AutoRefreshIntervalSeconds = 15f;

        private static FiresLeaderboardGui _instance;

        private readonly FiresLeaderboardAdapter _adapter = new FiresLeaderboardAdapter();
        private Rect _windowRect;
        private string _localViewMode = ViewRanking;
        private float _nextAutoRefreshAt;

        // Player-details drill-down state (selected row + per-view completed-achievement pagers).
        private FiresLeaderboardRanking _selectedPlayer;
        private int _detailAchievementsPage = 1;
        private int _progressAchievementsPage = 1;

        // Memorial mode: opened from a mausoleum tombstone. Forces OVERALL/all-time stats, hides the season
        // toggle, and swaps the empty Adventure timeline for an auto-composed Norse obituary. Reset on every
        // normal open path so the live board keeps its season selector + current-season behavior.
        private bool _memorialMode;
        private long _memorialFirstJoinedTicks;

        // Achievements-tab scroll: first visible row index, and the max scrollable offset (set during the build
        // from defs.Count - rowsThatFit). Mouse wheel drives it; included in the canvas signature so a change
        // triggers a rebuild that re-windows the list.
        private int _achievementsScrollOffset;
        private int _achievementsMaxScroll;

        public static bool IsVisible { get; private set; }
        public static bool IsInputBlockingActive => IsVisible;

        public static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresLeaderboardGui");
            _instance = go.AddComponent<FiresLeaderboardGui>();
            DontDestroyOnLoad(go);
        }

        /// <summary>Open the window (creates the singleton if needed). Gated on InventoryGui readiness.</summary>
        public static void Open()
        {
            EnsureInstance();
            _instance.OpenInternal();
        }

        public static void Close()
        {
            if (_instance != null) _instance.CloseInternal();
        }

        /// <summary>
        /// Open the leaderboard focused on a specific player's detail card (the memorial seam: another mod calls
        /// this reflectively when a player interacts with a mausoleum headstone). Opens the window, looks the
        /// player up in the board snapshot by name, and switches to the player-details view. If the board hasn't
        /// synced a row for that name yet, a minimal placeholder record (name only) is shown so the card still
        /// renders. No-op until InventoryGui is ready (same gate as <see cref="Open"/>).
        /// </summary>
        public static void OpenForPlayer(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return;
            Open(); // creates the singleton + shows the window; resets _selectedPlayer/_localViewMode
            if (!IsVisible || _instance == null) return; // InventoryGui not ready — bail quietly
            _instance._memorialMode = false; // normal player-detail open: keep the live season toggle
            _instance.ShowPlayerDetail(playerName);
        }

        /// <summary>
        /// Open the leaderboard in MEMORIAL mode for a player (the mausoleum tombstone seam). The card hides the
        /// season toggle, forces OVERALL/all-time stats (season -1, merged live + archives), and replaces the
        /// empty Adventure timeline with an auto-composed Norse obituary built from those overall stats and the
        /// passed first-seen date. Otherwise reuses the normal player-detail card unchanged.
        /// </summary>
        public static void OpenMemorial(string playerName, long firstJoinedTicks = 0L)
        {
            if (string.IsNullOrEmpty(playerName)) return;
            Open();
            if (!IsVisible || _instance == null) return;
            _instance._memorialMode = true;
            _instance._memorialFirstJoinedTicks = firstJoinedTicks;
            // force the all-time/overall board so the obituary reflects a whole life, not one season.
            _instance._adapter.RequestBoard(Modules.Marketplace.LeaderboardBoard.OverallSeason);
            _instance.ShowPlayerDetail(playerName);
        }

        private void ShowPlayerDetail(string playerName)
        {
            // In memorial mode we already requested the OVERALL board above; otherwise nudge a normal refresh.
            if (!_memorialMode) _adapter.RequestBoard();

            FiresLeaderboardRanking match = null;
            foreach (var r in _adapter.GetRankings())
            {
                if (r != null && string.Equals(r.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    match = r;
                    break;
                }
            }
            // No synced row yet — render the card with just the name + pending placeholders.
            _selectedPlayer = match ?? new FiresLeaderboardRanking { PlayerName = playerName, PlayerId = playerName };

            _localViewMode = DetailViewMode;
            _detailAchievementsPage = 1;
            RebuildCanvasView(true);
        }

        public static void Toggle()
        {
            EnsureInstance();
            if (IsVisible) _instance.CloseInternal();
            else _instance.OpenInternal();
        }

        private void Awake()
        {
            _instance = this;
            _windowRect = CenterRect(BaseWindowWidth * WindowScale, BaseWindowHeight * WindowScale);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OpenInternal()
        {
            // Template/parent source must exist before we build, or text templates are null (empty boxes).
            if (InventoryGui.instance == null) return;

            IsVisible = true;
            // Open to Progression (the local player's own card) by default, like TDV.
            _localViewMode = ViewProgression;
            _selectedPlayer = null;
            _detailAchievementsPage = 1;
            _progressAchievementsPage = 1;
            _achievementsScrollOffset = 0;
            _memorialMode = false; // normal open (hotkey/etc.): live board keeps its season toggle
            _windowRect = CenterRect(BaseWindowWidth * WindowScale, BaseWindowHeight * WindowScale);

            if (!EnsureCanvasUiReady())
            {
                IsVisible = false;
                return;
            }

            SyncCanvasRootLayout(true);
            _adapter.Open(OnBoardChanged);
            RebuildCanvasView(true);
            _nextAutoRefreshAt = Time.unscaledTime + AutoRefreshIntervalSeconds;
        }

        private void CloseInternal()
        {
            IsVisible = false;
            SetCanvasRootVisible(false);
            SetCursorState(false);
            // The local-player preview (FiresCore.UI.UIOverridePlayerPreview) lives on a canvas child; ClearCanvasContent
            // destroys it on the next rebuild and its OnDestroy tears down the camera/RenderTexture, so nothing to do here.
        }

        private void OnBoardChanged()
        {
            if (IsVisible) RebuildCanvasView(true);
        }

        private void Update()
        {
            if (IsVisible && !IsGameplayContextReady())
            {
                CloseInternal();
                return;
            }

            if (!IsVisible) return;

            SetCursorState(true);

            float now = Time.unscaledTime;
            if (now >= _nextAutoRefreshAt)
            {
                _nextAutoRefreshAt = now + AutoRefreshIntervalSeconds;
                _adapter.RequestBoard();
            }

            HandleAchievementsScroll();
            RefreshCanvasViewIfNeeded();
        }

        // Mouse-wheel scroll for the Achievements tab. Adjusts the row offset (clamped to the build-computed max)
        // and forces a rebuild so the windowed list re-renders at the new position.
        private void HandleAchievementsScroll()
        {
            if (!string.Equals(_localViewMode, ViewAchievements, StringComparison.OrdinalIgnoreCase)) return;
            if (_achievementsMaxScroll <= 0) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;
            int step = wheel > 0f ? -3 : 3; // wheel up scrolls toward the top (lower index)
            int next = Mathf.Clamp(_achievementsScrollOffset + step, 0, _achievementsMaxScroll);
            if (next != _achievementsScrollOffset)
            {
                _achievementsScrollOffset = next;
                RebuildCanvasView(true);
            }
        }

        private void OnGUI()
        {
            if (!IsVisible) return;
            _windowRect = CenterRect(BaseWindowWidth * WindowScale, BaseWindowHeight * WindowScale);
            if (EnsureCanvasUiReady())
            {
                SyncCanvasRootLayout(true);
            }
        }

        private static bool IsGameplayContextReady()
        {
            if (Player.m_localPlayer == null) return false;
            if (InventoryGui.instance == null) return false;
            if (Menu.IsVisible()) return false;
            return true;
        }

        private static Rect CenterRect(float width, float height)
        {
            float w = Mathf.Min(width, Screen.width - 40f);
            float h = Mathf.Min(height, Screen.height - 40f);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            return new Rect(x, y, w, h);
        }

        private static void SetCursorState(bool visible)
        {
            if (visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
