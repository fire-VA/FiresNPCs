using System;
using UnityEngine;
using FiresRPGmaker.Modules.Marketplace;

namespace FiresRPGmaker.UI.Leaderboard
{
    // Drives a remote player's completed-achievement list for the season being viewed. The list is rendered INLINE
    // in BuildCanvasPlayerDetails, so (unlike the mannequin binder, which feeds a persistent widget) this one
    // requests the list lazily and, when the matching (player, season) reply arrives, fires an onArrived callback
    // that rebuilds the canvas — BuildCanvasPlayerDetails then re-reads the now-cached list. Lives on the details
    // host so its lifecycle tracks the canvas rebuild: a late reply for a closed/changed card is dropped by the
    // (player, season) match + the onArrived guard, and the post-rebuild binder finds the cache populated (TryGet
    // succeeds) so it never re-requests — no loop.
    internal sealed class LeaderboardAchievementsBinder : MonoBehaviour
    {
        private string _playerName;
        private int _season;
        private Action _onArrived;
        private bool _subscribed;

        public void Initialize(string playerName, int season, Action onArrived)
        {
            _playerName = playerName;
            _season = season;
            _onArrived = onArrived;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_playerName)) return;
            if (LeaderboardAchievementBoard.TryGet(_playerName, _season, out _)) return; // already cached → details used it
            LeaderboardAchievementBoard.AchievementsReceived += OnReceived;
            _subscribed = true;
            LeaderboardAchievementBoard.RequestAchievements(_playerName, _season);
        }

        private void OnReceived(string name, int season)
        {
            if (season != _season || !string.Equals(name, _playerName, StringComparison.OrdinalIgnoreCase)) return;
            Unsubscribe();
            try { _onArrived?.Invoke(); } catch { }
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            LeaderboardAchievementBoard.AchievementsReceived -= OnReceived;
            _subscribed = false;
        }
    }
}
