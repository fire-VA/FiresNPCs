using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        public enum InnerTab { Stats, Achievements }

        private string _leaderboardProfile;
        private string _achievementProfile;
        private InnerTab _tab = InnerTab.Stats;
        private string _sortBy = "KilledCreatures";

        // Stats tab widgets
        private RectTransform _statsRowContent;
        private GameObject _statsHeader;
        private GameObject _statsSortBar;
        private TMP_Text _summaryText;

        // Achievement tab widgets
        private RectTransform _achRowContent;
        private GameObject _achHeader;
        private TMP_Text _achSummary;

        private Button _tabBtnStats;
        private Button _tabBtnAch;

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

        protected override void BuildContent(RectTransform contentArea)
        {
            _summaryText = MakeLabel(contentArea, "",
                                     new Vector2(0f, 0.94f), new Vector2(1f, 1f),
                                     new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                     size: 14f, color: new Color(1f, 0.85f, 0.55f));

            // Inner tabs
            _tabBtnStats = MakeButton(contentArea, "Leaderboard",
                                      new Vector2(0f, 0.86f), new Vector2(0.22f, 0.93f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            _tabBtnStats.onClick.AddListener(() => { _tab = InnerTab.Stats; Refresh(); });

            _tabBtnAch = MakeButton(contentArea, "Achievements",
                                    new Vector2(0.23f, 0.86f), new Vector2(0.46f, 0.93f),
                                    Vector2.zero, Vector2.zero,
                                    bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            _tabBtnAch.onClick.AddListener(() => { _tab = InnerTab.Achievements; Refresh(); });

            BuildStatsView(contentArea);
            BuildAchievementsView(contentArea);
        }

        private void BuildStatsView(RectTransform contentArea)
        {
            // Sort-by buttons
            _statsSortBar = new GameObject("StatsSortBar", typeof(RectTransform));
            _statsSortBar.transform.SetParent(contentArea, false);
            var sbrt = _statsSortBar.GetComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(0f, 0.78f); sbrt.anchorMax = new Vector2(1f, 0.85f);
            sbrt.offsetMin = Vector2.zero; sbrt.offsetMax = Vector2.zero;
            string[] sorts =   { "Kills", "PvP", "Built", "Crafted", "Deaths", "Explored" };
            string[] sortKeys = { "KilledCreatures", "KilledPlayers", "BuiltStructures", "ItemsCrafted", "Died", "MapExplored" };
            for (int i = 0; i < sorts.Length; i++)
            {
                float xMin = 0.00f + i * 0.165f;
                float xMax = xMin + 0.155f;
                int idx = i;
                var btn = MakeButton(_statsSortBar.transform, sorts[i],
                                     new Vector2(xMin, 0f), new Vector2(xMax, 1f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 12f);
                btn.onClick.AddListener(() => { _sortBy = sortKeys[idx]; Refresh(); });
            }

            // Column header
            _statsHeader = new GameObject("StatsHeader", typeof(RectTransform), typeof(Image));
            _statsHeader.transform.SetParent(contentArea, false);
            var shrt = _statsHeader.GetComponent<RectTransform>();
            shrt.anchorMin = new Vector2(0f, 0.71f); shrt.anchorMax = new Vector2(1f, 0.77f);
            shrt.offsetMin = Vector2.zero; shrt.offsetMax = Vector2.zero;
            _statsHeader.GetComponent<Image>().color = new Color(0.05f, 0.04f, 0.02f, 0.95f);
            BuildStatsHeaderLabels(_statsHeader.transform);

            _statsRowContent = MakeVerticalScroll(contentArea,
                                                  new Vector2(0f, 0f), new Vector2(1f, 0.70f),
                                                  Vector2.zero, Vector2.zero,
                                                  rowSpacing: 2f, scrollSensitivity: 120f);
        }

        // Stats columns: widened + no-wrap so headers don't line-break ("Crafte d" / "Death s" / "Explor e").
        private static readonly (string Label, float xMin, float xMax, TextAlignmentOptions align)[] StatsCols =
        {
            ("#",       0.00f, 0.05f, TextAlignmentOptions.Center),
            ("Player",  0.06f, 0.30f, TextAlignmentOptions.MidlineLeft),
            ("Kills",   0.31f, 0.43f, TextAlignmentOptions.Center),
            ("PvP",     0.44f, 0.52f, TextAlignmentOptions.Center),
            ("Built",   0.53f, 0.64f, TextAlignmentOptions.Center),
            ("Craft",   0.65f, 0.75f, TextAlignmentOptions.Center),
            ("Died",    0.76f, 0.84f, TextAlignmentOptions.Center),
            ("Map%",    0.85f, 0.99f, TextAlignmentOptions.Center),
        };

        private void BuildStatsHeaderLabels(Transform parent)
        {
            foreach (var c in StatsCols)
            {
                var t = MakeLabel(parent, c.Label,
                                  new Vector2(c.xMin, 0f), new Vector2(c.xMax, 1f),
                                  Vector2.zero, Vector2.zero,
                                  size: 13f, align: c.align, color: new Color(1f, 0.85f, 0.55f));
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Truncate;
            }
        }

        private void BuildAchievementsView(RectTransform contentArea)
        {
            _achSummary = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.78f), new Vector2(1f, 0.85f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 13f, color: new Color(0.96f, 0.91f, 0.76f));

            _achHeader = new GameObject("AchHeader", typeof(RectTransform), typeof(Image));
            _achHeader.transform.SetParent(contentArea, false);
            var arh = _achHeader.GetComponent<RectTransform>();
            arh.anchorMin = new Vector2(0f, 0.71f); arh.anchorMax = new Vector2(1f, 0.77f);
            arh.offsetMin = Vector2.zero; arh.offsetMax = Vector2.zero;
            _achHeader.GetComponent<Image>().color = new Color(0.05f, 0.04f, 0.02f, 0.95f);
            MakeLabel(_achHeader.transform, "Achievement", new Vector2(0.02f, 0f), new Vector2(0.50f, 1f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(_achHeader.transform, "Progress",    new Vector2(0.52f, 0f), new Vector2(0.78f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(_achHeader.transform, "Score",       new Vector2(0.80f, 0f), new Vector2(1f,    1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));

            _achRowContent = MakeVerticalScroll(contentArea,
                                                new Vector2(0f, 0f), new Vector2(1f, 0.70f),
                                                Vector2.zero, Vector2.zero,
                                                rowSpacing: 3f, scrollSensitivity: 120f);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _leaderboardProfile = profileName;
            Refresh();
        }

        private void Refresh()
        {
            HighlightTab(_tabBtnStats, _tab == InnerTab.Stats);
            HighlightTab(_tabBtnAch, _tab == InnerTab.Achievements);

            bool isStats = (_tab == InnerTab.Stats);
            _statsSortBar.SetActive(isStats);
            _statsHeader.SetActive(isStats);
            (_statsRowContent.parent.parent.gameObject).SetActive(isStats); // ScrollView root
            _achHeader.SetActive(!isStats);
            _achSummary.gameObject.SetActive(!isStats);
            (_achRowContent.parent.parent.gameObject).SetActive(!isStats);
            _summaryText.gameObject.SetActive(isStats);

            if (isStats) RefreshStats(); else RefreshAchievements();
        }

        private void HighlightTab(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active
                ? new Color(0.32f, 0.22f, 0.14f, 0.95f)
                : new Color(0.18f, 0.14f, 0.08f, 0.95f);
        }

        private void RefreshStats()
        {
            for (int i = _statsRowContent.childCount - 1; i >= 0; i--)
                Destroy(_statsRowContent.GetChild(i).gameObject);

            var sample = LeaderboardProfileManager.GetSampleEntries(_leaderboardProfile);
            var entries = new List<LeaderboardEntry>(sample);
            foreach (var live in LeaderboardTracker.All.Values)
            {
                bool replaced = false;
                for (int i = 0; i < entries.Count; i++)
                    if (string.Equals(entries[i].PlayerName, live.PlayerName, System.StringComparison.OrdinalIgnoreCase))
                    { entries[i] = ToEntry(live); replaced = true; break; }
                if (!replaced) entries.Add(ToEntry(live));
            }
            if (entries.Count == 0)
            {
                _summaryText.text = "No leaderboard data for profile '" + _leaderboardProfile + "'. Drop a .cfg into config\\firesnpcs\\leaderboard\\.";
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

        private static LeaderboardEntry ToEntry(LeaderboardTracker.Stats s) => new LeaderboardEntry
        {
            PlayerName = s.PlayerName,
            KilledCreatures = s.KilledCreatures, KilledPlayers = s.KilledPlayers,
            BuiltStructures = s.BuiltStructures, ItemsCrafted = s.ItemsCrafted,
            Died = s.Died, MapExplored = s.MapExplored,
        };

        private void BuildStatRow(int rank, LeaderboardEntry e, bool isMe)
        {
            var rowGo = new GameObject("LBRow_" + rank, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_statsRowContent, false);
            rowGo.GetComponent<Image>().color = isMe
                ? new Color(0.32f, 0.22f, 0.10f, 0.95f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 26f);
            var nameColor = isMe ? new Color(1f, 0.95f, 0.55f) : new Color(0.96f, 0.91f, 0.76f);
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
            for (int i = 0; i < StatsCols.Length; i++)
            {
                var c = StatsCols[i];
                var t = MakeLabel(rowGo.transform, vals[i],
                                  new Vector2(c.xMin, 0f), new Vector2(c.xMax, 1f),
                                  Vector2.zero, Vector2.zero,
                                  size: 13f, align: c.align, color: nameColor);
                t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Truncate;
            }
        }

        private void RefreshAchievements()
        {
            for (int i = _achRowContent.childCount - 1; i >= 0; i--)
                Destroy(_achRowContent.GetChild(i).gameObject);

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
            var rowGo = new GameObject("Ach_" + def.Id, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_achRowContent, false);
            rowGo.GetComponent<Image>().color = done
                ? new Color(0.12f, 0.20f, 0.10f, 0.92f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 56f);

            MakeLabel(rowGo.transform, (done ? "[X] " : "[  ] ") + def.Name,
                      new Vector2(0.02f, 0.55f), new Vector2(0.50f, 0.95f), Vector2.zero, Vector2.zero,
                      size: 14f, color: done ? new Color(0.70f, 1f, 0.70f) : new Color(0.96f, 0.91f, 0.76f));
            MakeLabel(rowGo.transform, def.Description,
                      new Vector2(0.02f, 0.05f), new Vector2(0.50f, 0.50f), Vector2.zero, Vector2.zero,
                      size: 11f, color: new Color(0.78f, 0.72f, 0.60f));

            float pct = def.MinAmount <= 0 ? 0f : Mathf.Clamp01((float)progress / def.MinAmount);
            MakePanel(rowGo.transform, "BarBg", new Color(0.04f, 0.03f, 0.02f, 0.95f),
                      new Vector2(0.52f, 0.30f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero);
            MakePanel(rowGo.transform, "BarFill",
                      done ? new Color(0.35f, 0.75f, 0.30f, 0.95f) : new Color(0.55f, 0.40f, 0.20f, 0.95f),
                      new Vector2(0.52f, 0.30f), new Vector2(0.52f + (0.78f - 0.52f) * pct, 0.70f),
                      Vector2.zero, Vector2.zero);
            MakeLabel(rowGo.transform, progress + "/" + def.MinAmount,
                      new Vector2(0.52f, 0.30f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero,
                      size: 12f, align: TextAlignmentOptions.Center);

            MakeLabel(rowGo.transform, def.Score.ToString(),
                      new Vector2(0.80f, 0.30f), new Vector2(1f, 0.70f), Vector2.zero, Vector2.zero,
                      size: 16f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
        }
    }
}
