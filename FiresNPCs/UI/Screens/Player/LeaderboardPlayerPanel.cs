using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Pal = FiresCore.UI.UIFontConfig.Colors;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Leaderboard + Achievement UI (one panel, two tabs). Top tab toggles between the
    /// sortable per-player stat board (sample data merged with live local-tracker rows) and the
    /// achievement-definition list with real progress bars driven by <see cref="LeaderboardTracker"/>.
    /// </summary>
    public class LeaderboardPlayerPanel : BasePlayerModulePanel
    {
        private static LeaderboardPlayerPanel _instance;
        protected override string PanelTitle => "Leaderboard";
        protected override Vector2 PanelSize => new Vector2(880f, 600f);
        protected override string PrefabName => "FiresLeaderboardPanel.prefab";

        public enum InnerTab { Stats, Achievements }

        private string _leaderboardProfile;
        private string _achievementProfile;
        private InnerTab _tab = InnerTab.Stats;
        private string _sortBy = "KilledCreatures";

        // Stats tab widgets (bound from prefab)
        private GameObject _statsView;
        private RectTransform _statsRowContent;
        private GameObject _statRowTemplate;
        private TMP_Text _summaryText;

        // Achievement tab widgets (bound from prefab)
        private GameObject _achView;
        private RectTransform _achRowContent;
        private GameObject _achRowTemplate;
        private TMP_Text _achSummary;

        private Button _tabBtnStats;
        private Button _tabBtnAch;

        // Row-cell node names inside StatRowTemplate, parallel to StatsCols / the vals[] order.
        private static readonly string[] CellNodes =
            { "RankLabel","PlayerLabel","KillsLabel","PvPLabel","BuiltLabel","CraftLabel","DiedLabel","MapLabel" };

        public static void Show(string leaderboardProfile, string npcDisplayName, InnerTab startTab = InnerTab.Stats, string achievementProfile = null)
        {
            EnsureInstance();
            _instance._leaderboardProfile = leaderboardProfile;
            _instance._achievementProfile = achievementProfile ?? leaderboardProfile;
            _instance._tab = startTab;
            _instance.Open(leaderboardProfile, npcDisplayName);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresLeaderboardPanel");
            _instance = go.AddComponent<LeaderboardPlayerPanel>();
            DontDestroyOnLoad(go);
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            _summaryText = FindTmp(t, "SummaryText");

            _tabBtnStats = FindBtn(t, "TabStats");
            _tabBtnAch   = FindBtn(t, "TabAch");
            if (_tabBtnStats != null) { _tabBtnStats.onClick.RemoveAllListeners(); _tabBtnStats.onClick.AddListener(() => { _tab = InnerTab.Stats; Refresh(); }); }
            if (_tabBtnAch   != null) { _tabBtnAch.onClick.RemoveAllListeners();   _tabBtnAch.onClick.AddListener(() => { _tab = InnerTab.Achievements; Refresh(); }); }

            _statsView       = Find(t, "StatsView");
            _statsRowContent = FindRect(t, "StatsScroll/Viewport/Content");
            _statRowTemplate = Find(t, "StatRowTemplate");
            if (_statRowTemplate != null) _statRowTemplate.SetActive(false);
            EnsureReflowMeta(_statsRowContent, rowSpacing: 2f, padding: 6f);

            _achView       = Find(t, "AchView");
            _achSummary    = FindTmp(t, "AchSummary");
            _achRowContent = FindRect(t, "AchScroll/Viewport/Content");
            _achRowTemplate = Find(t, "AchRowTemplate");
            if (_achRowTemplate != null) _achRowTemplate.SetActive(false);
            EnsureReflowMeta(_achRowContent, rowSpacing: 3f, padding: 6f);

            WireSort(t, "SortKills",    "KilledCreatures");
            WireSort(t, "SortPvP",      "KilledPlayers");
            WireSort(t, "SortBuilt",    "BuiltStructures");
            WireSort(t, "SortCrafted",  "ItemsCrafted");
            WireSort(t, "SortDeaths",   "Died");
            WireSort(t, "SortExplored", "MapExplored");

            if (_summaryText != null) _summaryText.color = Pal.ParchmentInk;
            if (_achSummary  != null) _achSummary.color  = Pal.ParchmentLabel;
            ApplyConfigurableBackground(root);
        }

        // Optional user-supplied background PNG (BepInEx/config/FiresUIBackgrounds/), VA-Inventory style. When the
        // config toggle is off / the file is missing, the panel keeps its baked default art.
        private void ApplyConfigurableBackground(GameObject root)
        {
            var cm = global::FiresRPGmaker.Managers.ConfigManager.Instance;
            if (cm?.configLeaderboardBackgroundToggle == null || !cm.configLeaderboardBackgroundToggle.Value) return;
            var sprite = FiresCore.UI.UIBackgroundLoader.Load(cm.configLeaderboardBackground?.Value);
            if (sprite == null) return;
            var bgGo = Find(root.transform, "Background");
            var img = bgGo != null ? bgGo.GetComponent<Image>() : root.GetComponent<Image>();
            if (img != null) { img.sprite = sprite; img.type = Image.Type.Sliced; img.color = Color.white; }
        }

        private void WireSort(Transform root, string node, string sortKey)
        {
            var btn = FindBtn(root, node);
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => { _sortBy = sortKey; Refresh(); }); }
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _leaderboardProfile = profileName;
            EnsureBoardSubscription();
            LeaderboardBoard.RequestBoard();   // async; the snapshot arrives via EntryPublished and re-Refreshes
            Refresh();
        }

        private bool _boardSubscribed;
        private void EnsureBoardSubscription()
        {
            if (_boardSubscribed) return;
            _boardSubscribed = true;
            // The singleton lives for the session, so a one-time subscription is fine (no unsubscribe needed).
            FiresCore.Bridge.LeaderboardBridge.EntryPublished += _ => { if (_statsRowContent != null) Refresh(); };
        }

        private void Refresh()
        {
            HighlightTab(_tabBtnStats, _tab == InnerTab.Stats);
            HighlightTab(_tabBtnAch, _tab == InnerTab.Achievements);

            bool isStats = (_tab == InnerTab.Stats);
            if (_statsView != null) _statsView.SetActive(isStats);
            if (_achView   != null) _achView.SetActive(!isStats);
            if (_summaryText != null) _summaryText.gameObject.SetActive(isStats);

            if (isStats) RefreshStats(); else RefreshAchievements();
        }

        private void HighlightTab(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? Pal.ParchmentButtonHover : Pal.ParchmentButton;
        }

        private void RefreshStats()
        {
            if (_statsRowContent == null || _statRowTemplate == null) return;
            ClearClones(_statsRowContent, _statRowTemplate);

            // Live server board (Core LiteDB vault), fetched via LeaderboardBoard.RequestBoard() on open and
            // pushed back through LeaderboardBridge.EntryPublished. Replaces the old sample + client-CSV source.
            var entries = new List<LeaderboardEntry>(LeaderboardBoard.Rows.Count);
            foreach (var r in LeaderboardBoard.Rows)
                entries.Add(new LeaderboardEntry
                {
                    PlayerName = r.PlayerName,
                    KilledCreatures = r.Monsters,
                    KilledPlayers = r.PlayersKilled,
                    BuiltStructures = r.Structures,
                    ItemsCrafted = r.Crafted,
                    Died = r.Deaths,
                    MapExplored = r.MapExplored,
                });
            if (entries.Count == 0)
            {
                _summaryText.text = "No leaderboard data yet — kill, build, craft, and explore to climb the board.";
                return;
            }
            var sorted = LeaderboardProfileManager.SortBy(entries, _sortBy);
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            _summaryText.text = "Top " + sorted.Count + " by " + _sortBy + (string.IsNullOrEmpty(localName) ? "" : "  -  You: " + localName);

            int rank = 1;
            foreach (var e in sorted)
            {
                bool isMe = !string.IsNullOrEmpty(localName) && string.Equals(localName, e.PlayerName, System.StringComparison.OrdinalIgnoreCase);
                BuildStatRow(rank, e, isMe);
                rank++;
            }
            ReflowScroll(_statsRowContent);
        }

        private void BuildStatRow(int rank, LeaderboardEntry e, bool isMe)
        {
            var rowGo = Instantiate(_statRowTemplate, _statsRowContent);
            rowGo.name = "LBRow_" + rank;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 26f);

            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = isMe ? Pal.ParchmentItemSelected : Pal.ParchmentPanel;
            var nameColor = Pal.ParchmentInk;

            string[] vals = {
                rank.ToString(),
                e.PlayerName,
                e.KilledCreatures.ToString(),
                e.KilledPlayers.ToString(),
                e.BuiltStructures.ToString(),
                e.ItemsCrafted.ToString(),
                e.Died.ToString(),
                (e.MapExplored * 100f).ToString("0") + "%",
            };
            for (int i = 0; i < CellNodes.Length; i++)
            {
                var t = FindTmp(rowGo.transform, CellNodes[i]);
                if (t == null) continue;
                t.text = vals[i];
                t.color = nameColor;
            }
        }

        private void RefreshAchievements()
        {
            if (_achRowContent == null || _achRowTemplate == null) return;
            ClearClones(_achRowContent, _achRowTemplate);

            var defs = AchievementProfileManager.GetAchievements(_achievementProfile);
            if (defs == null || defs.Count == 0)
            {
                _achSummary.text = "No achievements defined for profile '" + _achievementProfile + "'. Drop a .cfg into config\\firesnpcs\\achievement\\.";
                return;
            }
            int unlocked = 0, total = 0, earned = 0;
            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
            foreach (var def in defs)
            {
                int progress = LeaderboardTracker.GetProgress(playerName, def.TriggerType, def.Prefab);
                bool done = (progress >= def.MinAmount);
                if (done) unlocked++;
                total += def.Score; if (done) earned += def.Score;
                BuildAchRow(def, progress, done);
            }
            ReflowScroll(_achRowContent);
            _achSummary.text = "Unlocked: " + unlocked + "/" + defs.Count + "  -  Score: " + earned + "/" + total;
        }

        private void BuildAchRow(AchievementDef def, int progress, bool done)
        {
            var rowGo = Instantiate(_achRowTemplate, _achRowContent);
            rowGo.name = "Ach_" + def.Id;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 56f);

            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = done ? Pal.ParchmentItemSelected : Pal.ParchmentPanel;

            var nameLabel = FindTmp(rowGo.transform, "NameLabel");
            if (nameLabel != null) { nameLabel.text = (done ? "[X] " : "[  ] ") + def.Name; nameLabel.color = done ? new Color(0.13f, 0.42f, 0.13f) : Pal.ParchmentInk; }
            var descLabel = FindTmp(rowGo.transform, "DescLabel");
            if (descLabel != null) { descLabel.text = def.Description; descLabel.color = Pal.ParchmentLabel; }

            float pct = def.MinAmount <= 0 ? 0f : Mathf.Clamp01((float)progress / def.MinAmount);
            var barFill = FindRect(rowGo.transform, "BarFill");
            if (barFill != null)
            {
                barFill.anchorMin = new Vector2(0f, barFill.anchorMin.y);
                barFill.anchorMax = new Vector2(pct, barFill.anchorMax.y);
                var fillImg = barFill.GetComponent<Image>();
                if (fillImg != null) fillImg.color = done ? new Color(0.30f, 0.62f, 0.25f, 0.95f) : Pal.ParchmentButton;
            }
            var progressLabel = FindTmp(rowGo.transform, "ProgressLabel");
            if (progressLabel != null) { progressLabel.text = progress + "/" + def.MinAmount; progressLabel.color = Pal.ParchmentLabel; }

            var scoreLabel = FindTmp(rowGo.transform, "ScoreLabel");
            if (scoreLabel != null) { scoreLabel.text = def.Score.ToString(); scoreLabel.color = Pal.ParchmentInk; }
        }
    }
}
