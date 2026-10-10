using System.Collections.Generic;
using FiresCore.Queue;
using FiresCore.Tables;
using UnityEngine;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// Registers the Hold'em table with the shared FUC queue (<see cref="FiresQueueSystem"/>) and owns the server-side
    /// "table's ready" callback: when the queued players have all voted ready, it spins up a server-run
    /// <see cref="PokerNetHost"/> over <see cref="FiresTableNet"/> (AI seats fill the gaps), seats + invites everyone,
    /// and consumes the queue group. Max 4 for now — the table UI seats four; six-max needs a layout pass.
    /// </summary>
    public static class PokerLobby
    {
        public const string QueueId = "poker";
        private static bool _registered;
        private static int _tableSeq;

        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            PokerNetClient.Init();
            FiresQueueSystem.Register(new FiresQueueDef
            {
                Id = QueueId,
                Title = "Hold'em Lobby",
                Description = "Queue for a seat — the table deals when everyone readies up.",
                MinPlayers = 2,
                MaxPlayers = 4,
                JoinLabel = "Join Table",
                ReadyLabel = "Ready Up",
                CancelReadyLabel = "Cancel Ready",
                LeaveLabel = "Leave Table",
                OnReady = OnTableReady,
            });
        }

        // SERVER-side: everyone queued has readied — form the table and consume the group.
        private static bool OnTableReady(IReadOnlyList<FiresQueueMember> roster)
        {
            try
            {
                var humans = new List<FiresTableSeat>();
                for (int i = 0; i < roster.Count && i < 4; i++)
                    humans.Add(new FiresTableSeat { Id = roster[i].Id, Name = roster[i].Name, Seat = i });
                if (humans.Count < 2) return false;

                string tableId = "poker_" + (++_tableSeq);
                var host = new PokerNetHost(humans, 1000, 10, new System.Random());
                var session = FiresTableNet.Open(tableId, PokerNetHost.GameKey, host);
                if (session == null) return false;
                host.BindSession(session, humans);
                FiresTableNet.SendInvites(session);
                Debug.Log($"[PokerLobby] table {tableId} formed — {humans.Count} player(s) + AI fill.");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PokerLobby] table formation failed: {ex}");
                return false;
            }
        }
    }
}
