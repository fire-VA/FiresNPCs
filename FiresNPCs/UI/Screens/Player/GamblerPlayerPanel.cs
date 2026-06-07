using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
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
            if (profile == null || profile.Rewards.Count == 0) { _resultText.text = "Profile has no rewards."; return; }

            var player = Player.m_localPlayer; if (player == null) { _resultText.text = "No active player."; return; }
            var inv = player.GetInventory();

            // Charge cost
            int cost = profile.RequiredItem.Min;
            int held = CountInInventory(inv, profile.RequiredItem.Prefab);
            if (held < cost)
            {
                _resultText.text = "Not enough " + profile.RequiredItem.Prefab + " (have " + held + ", need " + cost + ").";
                return;
            }
            if (!ConsumeFromInventory(inv, profile.RequiredItem.Prefab, cost))
            {
                _resultText.text = "Failed to consume currency.";
                return;
            }

            // Roll N rewards
            var grants = new System.Collections.Generic.List<string>();
            int rolls = Mathf.Max(1, profile.MaxRolls);
            for (int r = 0; r < rolls; r++)
            {
                var pick = profile.Rewards[Random.Range(0, profile.Rewards.Count)];
                int qty = pick.Min == pick.Max ? pick.Min : Random.Range(pick.Min, pick.Max + 1);
                if (r < _reelTexts.Length)
                    _reelTexts[r].text = (pick.Prefab.Length > 6 ? pick.Prefab.Substring(0, 6) : pick.Prefab) + "\nx" + qty;
                GrantOrSpawn(player, inv, pick.Prefab, qty);
                grants.Add(pick.Prefab + " x" + qty);
            }
            _resultText.text = "Won: " + string.Join(", ", grants);
        }

        private static int CountInInventory(Inventory inv, string prefab)
        {
            int total = 0;
            foreach (var it in inv.GetAllItems())
            {
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == prefab) total += it.m_stack;
            }
            return total;
        }

        private static bool ConsumeFromInventory(Inventory inv, string prefab, int amount)
        {
            int remaining = amount;
            var copy = new System.Collections.Generic.List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (var it in copy)
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != prefab) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
            return remaining == 0;
        }

        private static void GrantOrSpawn(Player player, Inventory inv, string prefab, int qty)
        {
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            if (pf == null) { Debug.LogWarning("[Gambler] unknown prefab '" + prefab + "'"); return; }
            var drop = pf.GetComponent<ItemDrop>(); if (drop == null) return;
            if (inv.CanAddItem(drop.m_itemData, qty))
            { inv.AddItem(prefab, qty, 1, 0, 0L, ""); return; }
            // Inventory full — drop at player's feet
            var go = Object.Instantiate(pf, player.transform.position + Vector3.up, Quaternion.identity);
            var dr = go.GetComponent<ItemDrop>();
            if (dr != null) dr.m_itemData.m_stack = qty;
        }
    }
}
