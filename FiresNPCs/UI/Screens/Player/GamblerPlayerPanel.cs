using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Gambler — direct port of KG's MarketplaceGamblerNewUI flow:
    ///   Idle → ROLL pays the RequiredItem cost and shuffles cards.
    ///   Done → player clicks MAXROLLS cards; each click reveals + grants its hidden reward.
    ///   When the last roll is spent, every unrevealed card flips so the player sees what they missed.
    /// Skips KG's CannonBall/Smoke/Ring sprite effects + audio cues (deferred until the prefab swap);
    /// state machine + assignment logic match KG's Gambler_UI.cs.
    /// </summary>
    public class GamblerPlayerPanel : BasePlayerModulePanel
    {
        private static GamblerPlayerPanel _instance;
        protected override string PanelTitle => "Gambler";
        protected override Vector2 PanelSize => new Vector2(920f, 660f);

        private const int CardCount = 18;
        private const int Cols = 6;

        private enum Status { Idle, Shuffling, Done, Revealed }
        private Status _status = Status.Idle;
        private int _remainingRolls;
        private readonly HashSet<int> _clicked = new();
        private readonly Dictionary<int, GamblerItemEntry> _assigned = new();

        private string _profileName;
        private TMP_Text _descriptionText;     // chance list / "Choose Reward" / "You won"
        private TMP_Text _requiredText;        // "have / need"
        private TMP_Text _statusText;          // "ROLL to start" / "X rolls left"
        private RectTransform _grid;
        private Button _rollButton;
        private TMP_Text _rollButtonText;
        private readonly Card[] _cards = new Card[CardCount];

        private class Card { public GameObject Go; public Image Bg; public TMP_Text Face; public Button Btn; }

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
            // Description shows the chance list at idle, then "Choose Reward", then the "You won" list.
            _descriptionText = MakeLabel(contentArea, "",
                                         new Vector2(0f, 0.78f), new Vector2(0.62f, 0.93f),
                                         new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                         size: 13f, align: TextAlignmentOptions.TopLeft);

            // Required-cost box (top-right of left half)
            MakePanel(contentArea, "ReqBox", new Color(0.10f, 0.08f, 0.05f, 0.85f),
                      new Vector2(0f, 0.92f), new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero);
            _requiredText = MakeLabel(contentArea, "Required: -",
                                      new Vector2(0f, 0.92f), new Vector2(0.62f, 1f),
                                      new Vector2(12f, 0f), new Vector2(-12f, 0f),
                                      size: 14f, align: TextAlignmentOptions.MidlineLeft,
                                      color: new Color(1f, 0.85f, 0.55f));

            // 18-card grid (6x3) — left 62% of panel
            _grid = MakeGrid(contentArea, new Vector2(0f, 0.16f), new Vector2(0.62f, 0.76f));
            for (int i = 0; i < CardCount; i++) _cards[i] = BuildCard(_grid.transform, i);

            // ROLL button along the bottom
            _rollButton = MakeButton(contentArea, "ROLL",
                                     new Vector2(0.05f, 0.04f), new Vector2(0.57f, 0.14f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.40f, 0.18f, 0.10f, 0.95f), fontSize: 20f);
            _rollButton.onClick.AddListener(OnRollClicked);
            _rollButtonText = _rollButton.GetComponentInChildren<TMP_Text>();

            // Rolls-remaining status under the button
            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0f), new Vector2(0.62f, 0.04f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 13f, align: TextAlignmentOptions.MidlineLeft,
                                    color: new Color(0.85f, 0.78f, 0.62f));

            // Right-side static "Possible Rewards" pane (the chance list at idle, also useful while shuffled).
            MakeLabel(contentArea, "Possible Rewards",
                      new Vector2(0.64f, 0.92f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                      size: 15f, align: TextAlignmentOptions.MidlineLeft,
                      color: new Color(1f, 0.85f, 0.55f));
            var rewardScroll = MakeVerticalScroll(contentArea,
                                                  new Vector2(0.64f, 0.02f), new Vector2(1f, 0.90f),
                                                  Vector2.zero, Vector2.zero,
                                                  rowSpacing: 2f, scrollSensitivity: 80f);
            _rewardScrollContent = rewardScroll;
        }

        private RectTransform _rewardScrollContent;

        private RectTransform MakeGrid(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("GambleElements", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Cols;
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(4, 4, 4, 4);
            // Cell size chosen so 6 cols + spacing/padding fit the grid rect on a 920-wide panel.
            grid.cellSize = new Vector2(82f, 110f);
            return rt;
        }

        private Card BuildCard(Transform parent, int index)
        {
            var go = new GameObject("Card_" + index.ToString("00"),
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var card = new Card { Go = go };
            card.Bg = go.GetComponent<Image>();
            card.Bg.color = new Color(0.06f, 0.05f, 0.03f, 0.92f);
            card.Btn = go.GetComponent<Button>();
            card.Btn.onClick.AddListener(() => OnCardClicked(index));

            card.Face = MakeLabel(go.transform, "?",
                                  new Vector2(0f, 0f), new Vector2(1f, 1f),
                                  new Vector2(4f, 4f), new Vector2(-4f, -4f),
                                  size: 16f, align: TextAlignmentOptions.Center,
                                  color: new Color(1f, 0.95f, 0.6f));
            return card;
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            ResetToIdle();
            Refresh();
        }

        private void Refresh()
        {
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                _descriptionText.text = "Profile '" + _profileName + "' not loaded.";
                string loaded = string.Join(", ", GamblerProfileManager.AllProfileNames);
                _requiredText.text = "";
                _statusText.text = (string.IsNullOrEmpty(loaded) ? "No profiles loaded yet. " : "Loaded profiles: " + loaded + ". ")
                                 + "Drop a .cfg into BepInEx\\config\\FiresRPGmaker\\Gamblers\\ and run firesnpcs_reload gambler.";
                SetRollButton("ROLL", false);
                HideAllCards();
                ClearRewardScroll();
                return;
            }

            RefreshRewardChanceList(profile);
            RefreshRequiredText(profile);
            RefreshStaticRewardScroll(profile);

            if (_status == Status.Idle)
            {
                _statusText.text = "Items per roll: " + profile.MaxRolls + ".";
                bool canRoll = CountInInventory(profile.RequiredItem.Prefab) >= profile.RequiredItem.Min;
                SetRollButton(canRoll ? "ROLL" : "Need more " + profile.RequiredItem.Prefab, canRoll);
                ShowCardsBlank(profile);
            }
            else if (_status == Status.Done)
            {
                _statusText.text = "Choose a card. Rolls left: " + _remainingRolls;
                SetRollButton("Pick a card", false);
            }
        }

        private void RefreshRewardChanceList(GamblerProfile profile)
        {
            // KG's "$mpasn_gambleryouhaveachance" message: "You have a chance: <Item> x<Min>-<Max>".
            var sb = new System.Text.StringBuilder("You have a chance to win:\n");
            foreach (var it in profile.Rewards)
            {
                sb.Append(it.Prefab);
                sb.Append("  x");
                sb.Append(it.Min);
                if (it.Max != it.Min) { sb.Append('-'); sb.Append(it.Max); }
                sb.Append('\n');
            }
            _descriptionText.text = sb.ToString();
        }

        private void RefreshRequiredText(GamblerProfile profile)
        {
            int have = CountInInventory(profile.RequiredItem.Prefab);
            int need = profile.RequiredItem.Min;
            string color = have >= need ? "#7CFC00" : "#FF6347";
            _requiredText.text = "Required: <color=" + color + ">" + have + " / " + need + "</color>  " + profile.RequiredItem.Prefab;
        }

        private void RefreshStaticRewardScroll(GamblerProfile profile)
        {
            ClearRewardScroll();
            foreach (var item in profile.Rewards)
            {
                var rowGo = new GameObject("Reward_" + item.Prefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                rowGo.transform.SetParent(_rewardScrollContent, false);
                rowGo.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.06f, 0.85f);
                SetRowHeight(rowGo, 24f);
                MakeLabel(rowGo.transform, item.Prefab + "  x" + item.Min + (item.Min == item.Max ? "" : "-" + item.Max),
                          new Vector2(0f, 0f), new Vector2(1f, 1f),
                          new Vector2(8f, 0f), new Vector2(-8f, 0f),
                          size: 12f);
            }
            ReflowScroll(_rewardScrollContent);
        }

        private void ClearRewardScroll()
        {
            for (int i = _rewardScrollContent.childCount - 1; i >= 0; i--)
                Destroy(_rewardScrollContent.GetChild(i).gameObject);
        }

        private void ShowCardsBlank(GamblerProfile profile)
        {
            // Show only as many cards as the profile has rewards (KG's behaviour — max 18).
            int activeCount = Mathf.Min(profile.Rewards.Count, CardCount);
            for (int i = 0; i < CardCount; i++)
            {
                bool on = i < activeCount;
                _cards[i].Go.SetActive(on);
                _cards[i].Face.text = "?";
                _cards[i].Face.color = new Color(1f, 0.95f, 0.6f);
                _cards[i].Bg.color = new Color(0.06f, 0.05f, 0.03f, 0.92f);
            }
        }

        private void HideAllCards()
        {
            for (int i = 0; i < CardCount; i++) _cards[i].Go.SetActive(false);
        }

        private void OnRollClicked()
        {
            if (_status != Status.Idle) return;
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null || profile.Rewards.Count == 0) return;

            var player = Player.m_localPlayer; if (player == null) return;
            var inv = player.GetInventory();

            int cost = profile.RequiredItem.Min;
            if (CountInInventory(profile.RequiredItem.Prefab) < cost) { Refresh(); return; }
            if (!ConsumeFromInventory(inv, profile.RequiredItem.Prefab, cost)) return;

            // Assign each active card a UNIQUE reward (KG samples without replacement; if rewards <
            // cards then KG repeats — we mirror that with cycle index).
            int activeCount = Mathf.Min(profile.Rewards.Count, CardCount);
            var remaining = new List<int>();
            for (int i = 0; i < profile.Rewards.Count; i++) remaining.Add(i);
            _assigned.Clear();
            for (int c = 0; c < activeCount; c++)
            {
                if (remaining.Count == 0) for (int i = 0; i < profile.Rewards.Count; i++) remaining.Add(i);
                int pick = Random.Range(0, remaining.Count);
                _assigned[c] = profile.Rewards[remaining[pick]];
                remaining.RemoveAt(pick);
            }

            _clicked.Clear();
            _remainingRolls = Mathf.Clamp(profile.MaxRolls, 1, activeCount);
            _status = Status.Done;
            _descriptionText.text = "<color=#7CFC00>Choose " + _remainingRolls + " card" + (_remainingRolls > 1 ? "s" : "") + " to reveal your reward.</color>";
            RefreshRequiredText(profile);
            ShowCardsBlank(profile);
            _statusText.text = "Rolls left: " + _remainingRolls;
            SetRollButton("Pick a card", false);
        }

        private void OnCardClicked(int index)
        {
            if (_status != Status.Done) return;
            if (_remainingRolls <= 0) return;
            if (_clicked.Contains(index)) return;
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null) return;
            if (!_assigned.TryGetValue(index, out var reward)) return;

            _clicked.Add(index);
            _remainingRolls--;

            int qty = reward.Min == reward.Max ? reward.Min : Random.Range(reward.Min, reward.Max + 1);
            _cards[index].Face.text = reward.Prefab + "\nx" + qty;
            _cards[index].Face.color = new Color(0.70f, 1f, 0.70f);
            _cards[index].Bg.color = new Color(0.10f, 0.18f, 0.08f, 0.92f);
            GrantOrSpawn(Player.m_localPlayer, Player.m_localPlayer.GetInventory(), reward.Prefab, qty);

            AppendWonLine(reward.Prefab, qty);

            if (_remainingRolls <= 0)
            {
                // Reveal every unrevealed card so the player sees what they "missed", matching KG.
                foreach (var kv in _assigned)
                {
                    if (_clicked.Contains(kv.Key)) continue;
                    _cards[kv.Key].Face.text = kv.Value.Prefab + "\nx" + kv.Value.Min;
                    _cards[kv.Key].Face.color = new Color(0.78f, 0.72f, 0.60f);
                    _cards[kv.Key].Bg.color = new Color(0.08f, 0.06f, 0.04f, 0.92f);
                }
                ResetToIdleAfterRoll();
            }
            else
            {
                _statusText.text = "Rolls left: " + _remainingRolls;
            }
        }

        private void ResetToIdleAfterRoll()
        {
            _status = Status.Idle;
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile != null)
            {
                bool canRoll = CountInInventory(profile.RequiredItem.Prefab) >= profile.RequiredItem.Min;
                SetRollButton(canRoll ? "ROLL AGAIN" : "Need more " + profile.RequiredItem.Prefab, canRoll);
                RefreshRequiredText(profile);
            }
            _statusText.text = "Spin done. Click ROLL to play again.";
        }

        private void ResetToIdle()
        {
            _status = Status.Idle;
            _remainingRolls = 0;
            _clicked.Clear();
            _assigned.Clear();
        }

        private void AppendWonLine(string prefab, int qty)
        {
            if (!_descriptionText.text.Contains("You won"))
                _descriptionText.text = "<size=18>You won:</size>\n<color=#7CFC00>" + prefab + " x" + qty + "</color>";
            else
                _descriptionText.text += ", <color=#7CFC00>" + prefab + " x" + qty + "</color>";
        }

        private void SetRollButton(string text, bool enabled)
        {
            if (_rollButton != null) _rollButton.interactable = enabled;
            if (_rollButtonText != null) _rollButtonText.text = text;
        }

        // ─── inventory helpers (same as KG.InstantiateItem) ───

        private static int CountInInventory(string prefab)
        {
            var p = Player.m_localPlayer; if (p == null) return 0;
            int total = 0;
            foreach (var it in p.GetInventory().GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == prefab) total += it.m_stack;
            return total;
        }

        private static bool ConsumeFromInventory(Inventory inv, string prefab, int amount)
        {
            int remaining = amount;
            var copy = new List<ItemDrop.ItemData>(inv.GetAllItems());
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
            if (inv.CanAddItem(drop.m_itemData, qty)) { inv.AddItem(prefab, qty, 1, 0, 0L, ""); return; }
            var go = Object.Instantiate(pf,
                player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f,
                Quaternion.identity);
            var dr = go.GetComponent<ItemDrop>();
            if (dr != null) dr.m_itemData.m_stack = qty;
        }
    }
}
