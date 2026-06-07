using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.Player
{
    /// <summary>
    /// Player-facing Gambler UI: slot-machine reel display + Roll button + reward-table sidebar.
    /// Matches KG's per-profile shape (RequiredItem currency + N reward Items each with Min/Max range).
    /// v1 reads from <see cref="GamblerProfileManager"/> which loads .cfg files in KG's format.
    /// </summary>
    public class GamblerPlayerPanel : BasePlayerModulePanel
    {
        private static GamblerPlayerPanel _instance;
        protected override string PanelTitle => "Gambler";
        protected override Vector2 PanelSize => new Vector2(760f, 520f);

        private string _profileName;
        private TMP_Text _costText;
        private TMP_Text _resultText;
        private TMP_Text[] _reelTexts = new TMP_Text[3];
        private RectTransform _rewardList;
        private Button _rollButton;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresGamblerPanel");
                _instance = go.AddComponent<GamblerPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        protected override void BuildContent(RectTransform contentArea)
        {
            // Left side: reels + cost + roll button
            MakeLabel(contentArea, "Pay required item to spin the reels for a reward.",
                      new Vector2(0f, 0.92f), new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero,
                      size: 13f);

            // Cost panel
            MakePanel(contentArea, "CostBox", new Color(0.10f, 0.08f, 0.05f, 0.85f),
                      new Vector2(0f, 0.78f), new Vector2(0.62f, 0.90f), Vector2.zero, Vector2.zero);
            _costText = MakeLabel(contentArea, "Cost: -",
                                  new Vector2(0f, 0.78f), new Vector2(0.62f, 0.90f),
                                  new Vector2(12f, 0f), new Vector2(-12f, 0f),
                                  size: 16f, align: TextAlignmentOptions.MidlineLeft,
                                  color: new Color(1f, 0.85f, 0.55f));

            // 3 reels
            for (int i = 0; i < 3; i++)
            {
                float xMin = 0.03f + i * 0.20f;
                float xMax = xMin + 0.18f;
                MakePanel(contentArea, "Reel" + i, new Color(0.06f, 0.05f, 0.03f, 0.92f),
                          new Vector2(xMin, 0.32f), new Vector2(xMax, 0.72f), Vector2.zero, Vector2.zero);
                _reelTexts[i] = MakeLabel(contentArea, "?",
                                          new Vector2(xMin, 0.32f), new Vector2(xMax, 0.72f),
                                          Vector2.zero, Vector2.zero,
                                          size: 36f, align: TextAlignmentOptions.Center,
                                          color: new Color(1f, 0.95f, 0.6f));
            }

            // Roll button
            _rollButton = MakeButton(contentArea, "ROLL",
                                     new Vector2(0.05f, 0.16f), new Vector2(0.57f, 0.26f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.4f, 0.18f, 0.10f, 0.95f), fontSize: 18f);
            _rollButton.onClick.AddListener(OnRollClicked);

            // Result text
            _resultText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.02f), new Vector2(0.62f, 0.14f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 14f, align: TextAlignmentOptions.MidlineLeft,
                                    color: new Color(0.96f, 0.80f, 0.55f));

            // Right side: reward table
            MakeLabel(contentArea, "Possible Rewards",
                      new Vector2(0.64f, 0.92f), new Vector2(1f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 15f, align: TextAlignmentOptions.MidlineLeft,
                      color: new Color(1f, 0.85f, 0.55f));
            _rewardList = MakeVerticalScroll(contentArea,
                                             new Vector2(0.64f, 0.02f), new Vector2(1f, 0.90f),
                                             Vector2.zero, Vector2.zero,
                                             rowSpacing: 2f, scrollSensitivity: 80f);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            Refresh();
        }

        private void Refresh()
        {
            // Clear reward list
            for (int i = _rewardList.childCount - 1; i >= 0; i--)
                Destroy(_rewardList.GetChild(i).gameObject);

            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                _costText.text = "Profile '" + _profileName + "' not loaded.";
                if (_resultText != null) _resultText.text = "Drop a .cfg into config\\firesnpcs\\gambler\\ and use Apply on the admin tab to reload.";
                if (_rollButton != null) _rollButton.interactable = false;
                return;
            }
            _costText.text = "Cost per roll: " + profile.RequiredItem.Prefab + " x" + profile.RequiredItem.Min;
            if (_rollButton != null) _rollButton.interactable = true;
            for (int i = 0; i < _reelTexts.Length; i++) _reelTexts[i].text = "?";
            _resultText.text = "Max " + profile.MaxRolls + " rolls per spin.";

            foreach (var item in profile.Rewards)
            {
                var rowGo = new GameObject("Reward_" + item.Prefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                rowGo.transform.SetParent(_rewardList, false);
                rowGo.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.06f, 0.85f);
                rowGo.GetComponent<LayoutElement>().minHeight = 26f;

                var t = MakeLabel(rowGo.transform, item.Prefab + "  x" + item.Min + (item.Min == item.Max ? "" : "-" + item.Max),
                                  new Vector2(0f, 0f), new Vector2(1f, 1f),
                                  new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                  size: 13f);
            }
        }

        private void OnRollClicked()
        {
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null || profile.Rewards.Count == 0) return;
            // v1 client-only preview: just animate a random pick. Real roll goes through server in a follow-up.
            for (int i = 0; i < _reelTexts.Length; i++)
            {
                var pick = profile.Rewards[Random.Range(0, profile.Rewards.Count)];
                _reelTexts[i].text = pick.Prefab.Length > 6 ? pick.Prefab.Substring(0, 6) : pick.Prefab;
            }
            _resultText.text = "(preview only — server roll lands in a follow-up)";
        }
    }
}
