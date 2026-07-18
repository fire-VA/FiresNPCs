using System;
using UnityEngine;
using FiresCore.Storage;
using FiresRPGmaker.Modules.Marketplace;

namespace FiresRPGmaker.UI.Leaderboard
{
    // Drives a remote row's mannequin preview: requests the player's appearance from the server (lazy, per-row),
    // feeds it to the Core UIMannequinPreview widget when it arrives, and unsubscribes cleanly. Lives on the
    // preview host so its lifecycle (OnEnable/OnDestroy) tracks the canvas rebuild — when ClearCanvasContent
    // destroys the host, the subscription is dropped, so a late reply never touches a dead widget.
    internal sealed class LeaderboardMannequinBinder : MonoBehaviour
    {
        private string _playerName;
        private FiresCore.UI.UIMannequinPreview _widget;
        private bool _subscribed;
        private bool _applied;

        public void Initialize(string playerName, FiresCore.UI.UIMannequinPreview widget)
        {
            _playerName = playerName;
            _widget = widget;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_playerName) || _widget == null) return;

            if (LeaderboardAppearanceBoard.TryGet(_playerName, out PlayerAppearance cached))
            {
                Apply(cached);
                return;
            }

            LeaderboardAppearanceBoard.AppearanceReceived += OnAppearanceReceived;
            _subscribed = true;
            LeaderboardAppearanceBoard.RequestAppearance(_playerName);
        }

        private void OnAppearanceReceived(string name)
        {
            if (_applied || !string.Equals(name, _playerName, StringComparison.OrdinalIgnoreCase)) return;
            if (LeaderboardAppearanceBoard.TryGet(_playerName, out PlayerAppearance record))
            {
                Apply(record);
                Unsubscribe();
            }
        }

        // SetAppearance treats an empty/unavailable record as the transparent "unavailable" state, so we can hand
        // it the record verbatim — including the empty marker returned for a player who never captured a look.
        private void Apply(PlayerAppearance record)
        {
            if (_widget == null) return;
            _applied = true;
            _widget.SetAppearance(record);
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            LeaderboardAppearanceBoard.AppearanceReceived -= OnAppearanceReceived;
            _subscribed = false;
        }
    }
}
