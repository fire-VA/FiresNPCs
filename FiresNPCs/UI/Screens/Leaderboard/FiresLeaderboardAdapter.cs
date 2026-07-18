using System;
using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.Modules.Marketplace;

namespace FiresRPGmaker.UI.Leaderboard
{
    /// <summary>
    /// Which ranking column the client is sorting on. Maps to a field of <see cref="FiresLeaderboardRanking"/>.
    /// </summary>
    public enum FiresRankingSortField
    {
        Rank,
        PlayerName,
        AchievementCount,
        Monsters,
        Quests,
        Structures,
        Deaths,
        PlayersKilled,
        MapExplored,
        Score
    }

    /// <summary>One displayed ranking row: a <see cref="LeaderboardRow"/> plus its derived (sorted) rank.</summary>
    public sealed class FiresLeaderboardRanking
    {
        public int Rank;
        // The board is name-keyed (LeaderboardRow has no id); PlayerId falls back to PlayerName for selection.
        public string PlayerId;
        public string PlayerName;
        public int Monsters;
        public int PlayersKilled;
        public int Structures;
        public int Crafted;
        public int Deaths;
        public float MapExplored;   // 0..1
        public int Score;
        public int AchievementCount;   // server-computed completed-achievement count (v4 snapshot)

        // Favorite weapon (highest weapon_kills entry, resolved server-side) and top skills (level>0, desc).
        public string PreferredWeaponId = string.Empty;
        public string PreferredWeaponName = string.Empty;
        public List<LeaderboardSkillEntry> TopSkills = new List<LeaderboardSkillEntry>();

        // Achieved Adventure-milestone ids (server-computed via LeaderboardMilestones; v5 snapshot). Drives the
        // Progression timeline grid.
        public List<string> Milestones = new List<string>();
    }

    /// <summary>
    /// Thin read adapter for the ported leaderboard window. Sources rows from
    /// <see cref="LeaderboardBoard.Rows"/> (the client snapshot cache), applies a client-side sort,
    /// derives rank from the sorted index, kicks <see cref="LeaderboardBoard.RequestBoard"/> on open,
    /// and forwards <c>LeaderboardBridge.EntryPublished</c> so an open window rebuilds. Replaces TDV's
    /// server round-trip sort/paging.
    /// </summary>
    public sealed class FiresLeaderboardAdapter
    {
        public FiresRankingSortField SortField { get; private set; } = FiresRankingSortField.Score;
        public bool SortAscending { get; private set; }

        private bool _subscribed;
        private Action _onChanged;

        /// <summary>Subscribe to snapshot updates and ask the server for the current board.</summary>
        public void Open(Action onChanged)
        {
            _onChanged = onChanged;
            EnsureSubscription();
            LeaderboardBoard.RequestBoard();
        }

        private void EnsureSubscription()
        {
            if (_subscribed) return;
            _subscribed = true;
            // The window singleton lives for the session, so a one-time subscription is fine.
            FiresCore.Bridge.LeaderboardBridge.EntryPublished += _ => _onChanged?.Invoke();
        }

        /// <summary>Re-request the latest board (auto-refresh / pager refresh).</summary>
        public void RequestBoard() => LeaderboardBoard.RequestBoard();

        /// <summary>Request a specific season's board (0 = current/live).</summary>
        public void RequestBoard(int season) => LeaderboardBoard.RequestBoard(season);

        /// <summary>Flip ascending if the same field is re-selected, else select it (default descending; ascending for name).</summary>
        public void ToggleSort(FiresRankingSortField field)
        {
            if (SortField == field)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                SortField = field;
                SortAscending = field == FiresRankingSortField.PlayerName;
            }
        }

        /// <summary>The full ranked list, sorted per the current field, with rank derived from sorted index+1.</summary>
        public List<FiresLeaderboardRanking> GetRankings()
        {
            var rows = LeaderboardBoard.Rows ?? new List<LeaderboardRow>();
            IEnumerable<LeaderboardRow> ordered = SortRows(rows);
            var list = new List<FiresLeaderboardRanking>(rows.Count);
            int rank = 1;
            foreach (var r in ordered)
            {
                list.Add(new FiresLeaderboardRanking
                {
                    Rank = rank++,
                    PlayerId = r.PlayerName,
                    PlayerName = r.PlayerName,
                    Monsters = r.Monsters,
                    PlayersKilled = r.PlayersKilled,
                    Structures = r.Structures,
                    Crafted = r.Crafted,
                    Deaths = r.Deaths,
                    MapExplored = r.MapExplored,
                    Score = r.Score,
                    AchievementCount = r.AchievementCount,
                    PreferredWeaponId = r.PreferredWeaponId ?? string.Empty,
                    PreferredWeaponName = r.PreferredWeaponName ?? string.Empty,
                    TopSkills = r.TopSkills != null ? new List<LeaderboardSkillEntry>(r.TopSkills) : new List<LeaderboardSkillEntry>(),
                    Milestones = r.Milestones != null ? new List<string>(r.Milestones) : new List<string>(),
                });
            }
            return list;
        }

        private IEnumerable<LeaderboardRow> SortRows(List<LeaderboardRow> rows)
        {
            // Rows already arrive score-sorted (highest first). "Rank" sort == that default order.
            switch (SortField)
            {
                case FiresRankingSortField.PlayerName:
                    return Apply(rows, r => r.PlayerName ?? string.Empty, ascendingDefault: true);
                case FiresRankingSortField.AchievementCount:
                    return Apply(rows, r => (double)r.AchievementCount);
                case FiresRankingSortField.Monsters:
                    return Apply(rows, r => (double)r.Monsters);
                case FiresRankingSortField.Quests:
                    // Fires has no per-player quest count yet → sort by a constant 0 (stable no-op).
                    return Apply(rows, r => 0d);
                case FiresRankingSortField.Structures:
                    return Apply(rows, r => (double)r.Structures);
                case FiresRankingSortField.Deaths:
                    return Apply(rows, r => (double)r.Deaths);
                case FiresRankingSortField.PlayersKilled:
                    return Apply(rows, r => (double)r.PlayersKilled);
                case FiresRankingSortField.MapExplored:
                    return Apply(rows, r => (double)r.MapExplored);
                case FiresRankingSortField.Score:
                    return Apply(rows, r => (double)r.Score);
                case FiresRankingSortField.Rank:
                default:
                    return SortAscending ? rows.AsEnumerable().Reverse() : rows;
            }
        }

        private IEnumerable<LeaderboardRow> Apply<TKey>(List<LeaderboardRow> rows, Func<LeaderboardRow, TKey> key, bool ascendingDefault = false)
        {
            bool asc = SortAscending;
            return asc ? rows.OrderBy(key) : rows.OrderByDescending(key);
        }
    }
}
