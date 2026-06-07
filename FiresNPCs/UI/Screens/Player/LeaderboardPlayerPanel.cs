using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Leaderboard UI: ranked table of players by chosen stat with the local player's row
    /// highlighted. Stat columns reflect KG's Client_Leaderboard fields (kills, deaths, structures, etc.).
    /// v1 reads sample data from <see cref="LeaderboardProfileManager"/>; real cross-player rollup ships
    /// with the LeaderboardTracker module.
    /// </summary>
    public class LeaderboardPlayerPanel : BasePlayerModulePanel
    {
        private static LeaderboardPlayerPanel _instance;
        protected override string PanelTitle => "Leaderboard";
        protected override Vector2 PanelSize => new Vector2(800f, 560f);

        private string _profileName;
        private RectTransform _rowContent;
        private TMP_Text _summaryText;
        private string _sortBy = "KilledCreatures";

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresLeaderboardPanel");
                _instance = go.AddComponent<LeaderboardPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        protected override void BuildContent(RectTransform contentArea)
        {
            _summaryText = MakeLabel(contentArea, "",
                                     new Vector2(0f, 0.93f), new Vector2(1f, 1f),
                                     new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                     size: 14f, color: new Color(1f, 0.85f, 0.55f));

            // Sort-by buttons
            string[] sorts = { "Kills", "PvPKills", "Built", "Crafted", "Deaths", "Explored" };
            string[] sortKeys = { "KilledCreatures", "KilledPlayers", "BuiltStructures", "ItemsCrafted", "Died", "MapExplored" };
            for (int i = 0; i < sorts.Length; i++)
            {
                float xMin = 0.00f + i * 0.165f;
                float xMax = xMin + 0.155f;
                int idx = i;
                var btn = MakeButton(contentArea, sorts[i],
                                     new Vector2(xMin, 0.85f), new Vector2(xMax, 0.92f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 12f);
                btn.onClick.AddListener(() => { _sortBy = sortKeys[idx]; Refresh(); });
            }

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.78f), new Vector2(1f, 0.84f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "#",       new Vector2(0.00f, 0.78f), new Vector2(0.06f, 0.84f), new Vector2(8f, 0f), Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Player",  new Vector2(0.07f, 0.78f), new Vector2(0.32f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Kills",   new Vector2(0.32f, 0.78f), new Vector2(0.44f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "PvP",     new Vector2(0.44f, 0.78f), new Vector2(0.54f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Built",   new Vector2(0.54f, 0.78f), new Vector2(0.66f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Crafted", new Vector2(0.66f, 0.78f), new Vector2(0.78f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Deaths",  new Vector2(0.78f, 0.78f), new Vector2(0.88f, 0.84f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Explore", new Vector2(0.88f, 0.78f), new Vector2(1.0f,  0.84f), new Vector2(0f, 0f), new Vector2(-8f, 0f), size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));

            _rowContent = MakeVerticalScroll(contentArea,
                                             new Vector2(0f, 0f), new Vector2(1f, 0.77f),
                                             Vector2.zero, Vector2.zero,
                                             rowSpacing: 2f, scrollSensitivity: 120f);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = _rowContent.childCount - 1; i >= 0; i--)
                Destroy(_rowContent.GetChild(i).gameObject);

            var sample = LeaderboardProfileManager.GetSampleEntries(_profileName);
            var entries = new System.Collections.Generic.List<LeaderboardEntry>(sample);
            // Merge LIVE local-tracker rows so the local player + anyone we have observed appears
            // alongside the seeded .cfg sample data.
            foreach (var live in LeaderboardTracker.All.Values)
            {
                bool replaced = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].PlayerName, live.PlayerName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        entries[i] = ToEntry(live); replaced = true; break;
                    }
                }
                if (!replaced) entries.Add(ToEntry(live));
            }
            if (entries.Count == 0)
            {
                _summaryText.text = "No leaderboard data for profile '" + _profileName + "'. Drop a .cfg into config\\firesnpcs\\leaderboard\\.";
                return;
            }
            var sorted = LeaderboardProfileManager.SortBy(entries, _sortBy);
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            _summaryText.text = "Top " + sorted.Count + " by " + _sortBy + (string.IsNullOrEmpty(localName) ? "" : "  -  You: " + localName);

            int rank = 1;
            foreach (var e in sorted)
            {
                bool isMe = !string.IsNullOrEmpty(localName) && string.Equals(localName, e.PlayerName, System.StringComparison.OrdinalIgnoreCase);
                BuildRow(rank, e, isMe);
                rank++;
            }
        }

        private static LeaderboardEntry ToEntry(LeaderboardTracker.Stats s) => new LeaderboardEntry
        {
            PlayerName = s.PlayerName,
            KilledCreatures = s.KilledCreatures,
            KilledPlayers = s.KilledPlayers,
            BuiltStructures = s.BuiltStructures,
            ItemsCrafted = s.ItemsCrafted,
            Died = s.Died,
            MapExplored = s.MapExplored,
        };

        private void BuildRow(int rank, LeaderboardEntry e, bool isMe)
        {
            var rowGo = new GameObject("LBRow_" + rank, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_rowContent, false);
            rowGo.GetComponent<Image>().color = isMe
                ? new Color(0.32f, 0.22f, 0.10f, 0.95f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            rowGo.GetComponent<LayoutElement>().minHeight = 26f;

            var nameColor = isMe ? new Color(1f, 0.95f, 0.55f) : new Color(0.96f, 0.91f, 0.76f);
            MakeLabel(rowGo.transform, rank.ToString(),                  new Vector2(0f, 0f),    new Vector2(0.06f, 1f), new Vector2(8f, 0f), Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: nameColor);
            MakeLabel(rowGo.transform, e.PlayerName,                     new Vector2(0.07f, 0f), new Vector2(0.32f, 1f), Vector2.zero, Vector2.zero, size: 13f, color: nameColor);
            MakeLabel(rowGo.transform, e.KilledCreatures.ToString(),     new Vector2(0.32f, 0f), new Vector2(0.44f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, e.KilledPlayers.ToString(),       new Vector2(0.44f, 0f), new Vector2(0.54f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, e.BuiltStructures.ToString(),     new Vector2(0.54f, 0f), new Vector2(0.66f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, e.ItemsCrafted.ToString(),        new Vector2(0.66f, 0f), new Vector2(0.78f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, e.Died.ToString(),                new Vector2(0.78f, 0f), new Vector2(0.88f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, (e.MapExplored * 100f).ToString("0") + "%", new Vector2(0.88f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-8f, 0f), size: 13f, align: TextAlignmentOptions.Center);
        }
    }
}
