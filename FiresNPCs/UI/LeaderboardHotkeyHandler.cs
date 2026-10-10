using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI.Leaderboard;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Global hotkey that opens the Leaderboard panel (default L). Mirrors <see cref="QuestJournal.QuestJournalHotkeyHandler"/>.
    /// Client-only (spawned from the client-only UIInitializer); defers to VikingLands.Core's own leaderboard
    /// hotkey while that mod is present, until the deference flip.
    /// </summary>
    public class LeaderboardHotkeyHandler : MonoBehaviour
    {
        private static LeaderboardHotkeyHandler _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!CanProcessHotkey()) return;
            var cfg = ConfigManager.Instance?.configLeaderboardHotkey;
            if (cfg == null || cfg.Value.MainKey == KeyCode.None) return;
            if (FiresCore.Input.Chord.IsDown(cfg.Value))
                FiresLeaderboardGui.Open();
        }

        private static bool CanProcessHotkey()
        {
            if (Player.m_localPlayer == null) return false;
            if (FiresCore.Input.FiresInputBlock.IsCapturing) return false; // typing in a Fires field — don't open on the keystroke
            if (Menu.IsVisible()) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible()) return false;
            if (TextInput.IsVisible()) return false;
            if (InventoryGui.IsVisible()) return false;
            if (StoreGui.IsVisible()) return false;
            if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present) return false;
            return true;
        }

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            var go = new GameObject("LeaderboardHotkeyHandler");
            go.AddComponent<LeaderboardHotkeyHandler>();
        }
    }
}
