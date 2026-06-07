using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.Player
{
    /// <summary>
    /// Player-facing Achievement UI: scrollable list of achievement entries with progress bar + Claim button.
    /// Each row mirrors KG's Achievement shape (Name, Description, Score, ProgressTowardThreshold).
    /// v1 reads from <see cref="AchievementProfileManager"/>; progress is local-mock until the Leaderboard
    /// tracker module lands to feed real player stats.
    /// </summary>
    public class AchievementPlayerPanel : BasePlayerModulePanel
    {
        private static AchievementPlayerPanel _instance;
        protected override string PanelTitle => "Achievements";
        protected override Vector2 PanelSize => new Vector2(720f, 560f);

        private string _profileName;
        private RectTransform _listContent;
        private TMP_Text _summaryText;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresAchievementPanel");
                _instance = go.AddComponent<AchievementPlayerPanel>();
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

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.86f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "Achievement",  new Vector2(0.02f, 0.86f), new Vector2(0.50f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Progress",     new Vector2(0.52f, 0.86f), new Vector2(0.78f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Score",        new Vector2(0.80f, 0.86f), new Vector2(1f,    0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));

            _listContent = MakeVerticalScroll(contentArea,
                                              new Vector2(0f, 0f), new Vector2(1f, 0.85f),
                                              Vector2.zero, Vector2.zero,
                                              rowSpacing: 3f, scrollSensitivity: 120f);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            var defs = AchievementProfileManager.GetAchievements(_profileName);
            if (defs == null || defs.Count == 0)
            {
                _summaryText.text = "No achievements defined for profile '" + _profileName + "'. Drop a .cfg into config\\firesnpcs\\achievement\\.";
                return;
            }

            int unlocked = 0; int totalScore = 0; int earnedScore = 0;
            foreach (var def in defs)
            {
                bool done = (def.MockProgress >= def.MinAmount);
                if (done) unlocked++;
                totalScore += def.Score;
                if (done) earnedScore += def.Score;
                BuildRow(def, done);
            }
            _summaryText.text = "Unlocked: " + unlocked + "/" + defs.Count + "  -  Score: " + earnedScore + "/" + totalScore;
        }

        private void BuildRow(AchievementDef def, bool done)
        {
            var rowGo = new GameObject("Ach_" + def.Id, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_listContent, false);
            rowGo.GetComponent<Image>().color = done
                ? new Color(0.12f, 0.20f, 0.10f, 0.92f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            rowGo.GetComponent<LayoutElement>().minHeight = 56f;

            var prefix = done ? "[X] " : "[  ] ";
            MakeLabel(rowGo.transform, prefix + def.Name,
                      new Vector2(0.02f, 0.55f), new Vector2(0.50f, 0.95f), Vector2.zero, Vector2.zero,
                      size: 14f, color: done ? new Color(0.70f, 1f, 0.70f) : new Color(0.96f, 0.91f, 0.76f));
            MakeLabel(rowGo.transform, def.Description,
                      new Vector2(0.02f, 0.05f), new Vector2(0.50f, 0.50f), Vector2.zero, Vector2.zero,
                      size: 11f, color: new Color(0.78f, 0.72f, 0.60f));

            // Progress bar
            float pct = def.MinAmount <= 0 ? 0f : Mathf.Clamp01((float)def.MockProgress / def.MinAmount);
            MakePanel(rowGo.transform, "BarBg", new Color(0.04f, 0.03f, 0.02f, 0.95f),
                      new Vector2(0.52f, 0.30f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero);
            MakePanel(rowGo.transform, "BarFill",
                      done ? new Color(0.35f, 0.75f, 0.30f, 0.95f) : new Color(0.55f, 0.40f, 0.20f, 0.95f),
                      new Vector2(0.52f, 0.30f), new Vector2(0.52f + (0.78f - 0.52f) * pct, 0.70f),
                      Vector2.zero, Vector2.zero);
            MakeLabel(rowGo.transform, def.MockProgress + "/" + def.MinAmount,
                      new Vector2(0.52f, 0.30f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero,
                      size: 12f, align: TextAlignmentOptions.Center);

            MakeLabel(rowGo.transform, def.Score.ToString(),
                      new Vector2(0.80f, 0.30f), new Vector2(1f, 0.70f), Vector2.zero, Vector2.zero,
                      size: 16f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
        }
    }
}
