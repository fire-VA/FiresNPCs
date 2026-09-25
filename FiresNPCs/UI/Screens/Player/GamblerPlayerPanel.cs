using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Gambler — a multi-mini-game "gambling den" over one shared draw (cost item, MAXROLLS
    /// draws, weighted reward pool). The profile's Games list drives procedural tabs; each tab swaps the
    /// center play-area:
    ///   Cards  — DEAL assigns rewards to 18 tiles; player clicks MAXROLLS to reveal + claim.
    ///   Bones / Runes / Wheel — one action (CAST/CAST/SPIN) draws MAXROLLS rewards and reveals them in
    ///   per-game styled slots. (Bespoke dice-tumble / wheel-spin visuals are a later polish pass.)
    /// Layout lives in FiresGamblerPanel.prefab (the den); this binds its named nodes.
    /// </summary>
    public class GamblerPlayerPanel : BasePlayerModulePanel
    {
        private static GamblerPlayerPanel _instance;
        protected override string PanelTitle => "Gambler";
        protected override Vector2 PanelSize => new Vector2(920f, 660f);
        protected override string PrefabName => "FiresGamblerPanel.prefab";

        private const int CardCount = 18;

        private string _profileName;
        private string _activeGame = GamblerGames.Cards;

        // ----- bound prefab nodes -----
        private TMP_Text _descriptionText;     // odds strip / "you won"
        private TMP_Text _requiredText;        // stake / have
        private TMP_Text _statusText;          // picks-left / prompt
        private RectTransform _grid;           // GambleElements — the play area (GridLayoutGroup)
        private GameObject _cardTemplate;      // tile template cloned per slot
        private Button _rollButton;
        private TMP_Text _rollButtonText;
        private RectTransform _tabBar;
        private GameObject _tabTemplate;

        // ----- cards state -----
        private bool _dealt;
        private int _remainingPicks;
        private readonly HashSet<int> _clicked = new();
        private readonly Dictionary<int, GamblerItemEntry> _assigned = new();

        // ----- live tiles + tabs -----
        private readonly List<Tile> _tiles = new();
        private readonly List<GameObject> _tabs = new();

        private class Tile { public GameObject Go; public Image Bg; public TMP_Text Face; public Button Btn; }

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

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            _descriptionText = FindTmp(t, "DescriptionText");
            _requiredText    = FindTmp(t, "RequiredText");
            _statusText      = FindTmp(t, "StatusText");

            _grid         = FindRect(t, "GambleElements");
            _cardTemplate = Find(t, "CardTemplate");
            if (_cardTemplate != null) _cardTemplate.SetActive(false);

            _rollButton = FindBtn(t, "RollButton");
            if (_rollButton != null)
            {
                _rollButton.onClick.RemoveAllListeners();
                _rollButton.onClick.AddListener(OnActionClicked);
                _rollButtonText = _rollButton.GetComponentInChildren<TMP_Text>();
            }

            _tabBar = FindRect(t, "TabBar");
            _tabTemplate = Find(t, "TabTemplate");
            if (_tabTemplate != null) _tabTemplate.SetActive(false);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            var profile = GamblerProfileManager.GetProfile(profileName);
            BuildTabs(profile);
            string first = profile != null && profile.Games.Count > 0 ? profile.Games[0] : GamblerGames.Cards;
            SelectGame(first);
        }

        // ----- tabs -----

        private void BuildTabs(GamblerProfile profile)
        {
            foreach (var go in _tabs) if (go != null) Destroy(go);
            _tabs.Clear();
            if (_tabBar != null)
                for (int i = _tabBar.childCount - 1; i >= 0; i--)
                {
                    var ch = _tabBar.GetChild(i).gameObject;
                    if (ch != _tabTemplate) Destroy(ch);   // strip the prefab's static preview tabs
                }

            var games = profile != null && profile.Games.Count > 0 ? profile.Games : new List<string> { GamblerGames.Cards };
            // A single-game gambler shows no tab bar — just that game.
            if (_tabBar != null) _tabBar.gameObject.SetActive(games.Count > 1);
            if (games.Count <= 1 || _tabBar == null || _tabTemplate == null) return;

            foreach (var g in games)
            {
                var tab = Instantiate(_tabTemplate, _tabBar);
                tab.name = g + "Tab";
                tab.SetActive(true);
                var lbl = FindTmp(tab.transform, "Label");
                if (lbl != null) lbl.text = TabLabel(g);
                var btn = tab.GetComponent<Button>();
                string game = g;
                if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => SelectGame(game)); }
                _tabs.Add(tab);
            }
        }

        private static string TabLabel(string g) =>
            g == GamblerGames.Bones ? "BONES" : g == GamblerGames.Runes ? "RUNES" : g == GamblerGames.Wheel ? "WHEEL" : g == GamblerGames.Duel ? "DUEL" : "CARDS";
        private static string ActionLabel(string g) =>
            g == GamblerGames.Bones ? "CAST" : g == GamblerGames.Runes ? "CAST" : g == GamblerGames.Wheel ? "SPIN" : g == GamblerGames.Duel ? "ROLL" : "DEAL";

        private void SelectGame(string game)
        {
            _activeGame = game;
            foreach (var tab in _tabs)
            {
                bool on = tab.name == game + "Tab";
                var img = tab.GetComponent<Image>();
                if (img != null) img.color = on ? new Color(0.48f, 0.35f, 0.16f, 1f) : new Color(0.32f, 0.22f, 0.12f, 0.95f);
                var lbl = FindTmp(tab.transform, "Label");
                if (lbl != null) lbl.color = on ? new Color(0.96f, 0.91f, 0.74f) : new Color(0.79f, 0.71f, 0.54f);
            }
            ResetState();
            Refresh();
        }

        // ----- shared refresh -----

        private void Refresh()
        {
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                ClearTiles();
                if (_descriptionText != null) _descriptionText.text = "Profile '" + _profileName + "' not loaded.";
                if (_requiredText != null) _requiredText.text = "";
                string loaded = string.Join(", ", GamblerProfileManager.AllProfileNames);
                if (_statusText != null)
                    _statusText.text = string.IsNullOrEmpty(loaded) ? "No profiles loaded." : "Loaded: " + loaded;
                SetRollButton(ActionLabel(_activeGame), false);
                return;
            }

            RefreshOdds(profile);
            RefreshStake(profile);

            int activeCount = _activeGame == GamblerGames.Cards
                ? Mathf.Min(profile.Rewards.Count, CardCount)
                : Mathf.Clamp(profile.MaxRolls, 1, Mathf.Max(1, profile.Rewards.Count));

            BuildTiles(activeCount);
            bool canPlay = CountInInventory(profile.RequiredItem.Prefab) >= profile.RequiredItem.Min;

            if (_activeGame == GamblerGames.Cards && _dealt)
            {
                _statusText.text = "Pick " + _remainingPicks + " card" + (_remainingPicks == 1 ? "" : "s");
                SetRollButton("Pick a card", false);
            }
            else
            {
                _statusText.text = _activeGame == GamblerGames.Cards
                    ? "Cards per deal: " + Mathf.Clamp(profile.MaxRolls, 1, activeCount)
                    : ActionLabel(_activeGame) + " to play  (" + Mathf.Clamp(profile.MaxRolls, 1, activeCount) + ")";
                SetRollButton(canPlay ? ActionLabel(_activeGame) : "Need more " + profile.RequiredItem.Prefab, canPlay);
            }
        }

        private void RefreshOdds(GamblerProfile profile)
        {
            if (_descriptionText == null) return;
            var sb = new System.Text.StringBuilder("You can win:  ");
            for (int i = 0; i < profile.Rewards.Count; i++)
            {
                var it = profile.Rewards[i];
                if (i > 0) sb.Append("  ·  ");
                sb.Append(it.Prefab).Append(" x").Append(it.Min);
                if (it.Max != it.Min) sb.Append('-').Append(it.Max);
            }
            _descriptionText.text = sb.ToString();
        }

        private void RefreshStake(GamblerProfile profile)
        {
            if (_requiredText == null) return;
            int have = CountInInventory(profile.RequiredItem.Prefab);
            int need = profile.RequiredItem.Min;
            string color = have >= need ? "#9CC24A" : "#FF6347";
            _requiredText.text = "STAKE  " + need + " " + profile.RequiredItem.Prefab +
                                 "   <color=" + color + ">(have " + have + ")</color>";
        }

        // ----- play-area tiles (shared by every game) -----

        private void BuildTiles(int count)
        {
            ClearTiles();
            if (_grid == null || _cardTemplate == null) return;
            Color face = TileFace(_activeGame);
            Color bg = TileBg(_activeGame);
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                var go = Instantiate(_cardTemplate, _grid);
                go.name = "Tile_" + i.ToString("00");
                go.SetActive(true);
                var tile = new Tile { Go = go, Bg = go.GetComponent<Image>(), Face = FindTmp(go.transform, "Face"), Btn = go.GetComponent<Button>() };
                if (tile.Bg != null) tile.Bg.color = bg;
                if (tile.Face != null) { tile.Face.text = "?"; tile.Face.color = face; }
                if (tile.Btn != null)
                {
                    tile.Btn.onClick.RemoveAllListeners();
                    bool interactive = _activeGame == GamblerGames.Cards;
                    tile.Btn.interactable = interactive;
                    if (interactive) tile.Btn.onClick.AddListener(() => OnCardClicked(idx));
                }
                _tiles.Add(tile);
            }
        }

        private void ClearTiles()
        {
            foreach (var t in _tiles) if (t.Go != null) Destroy(t.Go);
            _tiles.Clear();
        }

        // Reset existing tiles to face-down + re-apply interactability (clears the previous round before a new deal).
        private void ResetTiles()
        {
            Color face = TileFace(_activeGame);
            Color bg = TileBg(_activeGame);
            bool interactive = _activeGame == GamblerGames.Cards;
            foreach (var t in _tiles)
            {
                if (t.Face != null) { t.Face.text = "?"; t.Face.color = face; t.Face.fontSize = 22f; }
                if (t.Bg != null) t.Bg.color = bg;
                if (t.Btn != null) t.Btn.interactable = interactive;
            }
        }

        // Cards: flip a tile edge-on, swap to its revealed face at the thin point, then flip back — a real card turn.
        private System.Collections.IEnumerator FlipReveal(int index, GamblerItemEntry reward)
        {
            if (index < 0 || index >= _tiles.Count) yield break;
            var rt = _tiles[index].Go != null ? _tiles[index].Go.GetComponent<RectTransform>() : null;
            if (rt == null) { RevealTile(index, reward, won: true); yield break; }
            const float half = 0.11f;
            for (float t = 0f; t < half; t += Time.deltaTime) { rt.localScale = new Vector3(Mathf.Lerp(1f, 0f, t / half), 1f, 1f); yield return null; }
            RevealTile(index, reward, won: true);
            for (float t = 0f; t < half; t += Time.deltaTime) { rt.localScale = new Vector3(Mathf.Lerp(0f, 1f, t / half), 1f, 1f); yield return null; }
            rt.localScale = Vector3.one;
        }

        // Cards: deal the tiles in with a quick staggered pop so a fresh hand feels dealt.
        private System.Collections.IEnumerator DealIn()
        {
            foreach (var tile in _tiles) { var rt = tile.Go != null ? tile.Go.GetComponent<RectTransform>() : null; if (rt != null) rt.localScale = new Vector3(0f, 1f, 1f); }
            foreach (var tile in _tiles)
            {
                var rt = tile.Go != null ? tile.Go.GetComponent<RectTransform>() : null;
                if (rt != null) StartCoroutine(PopIn(rt));
                yield return new WaitForSeconds(0.028f);
            }
        }

        private System.Collections.IEnumerator PopIn(RectTransform rt)
        {
            const float d = 0.16f;
            for (float t = 0f; t < d; t += Time.deltaTime) { float e = 1f - Mathf.Pow(1f - t / d, 2f); rt.localScale = new Vector3(e, 1f, 1f); yield return null; }
            rt.localScale = Vector3.one;
        }

        private static Color TileBg(string g) =>
            g == GamblerGames.Bones ? new Color(0.91f, 0.87f, 0.76f, 0.96f) :
            g == GamblerGames.Runes ? new Color(0.49f, 0.46f, 0.40f, 0.96f) :
            g == GamblerGames.Wheel ? new Color(0.50f, 0.34f, 0.12f, 0.96f) :
                                      new Color(0.11f, 0.08f, 0.05f, 0.95f);
        private static Color TileFace(string g) =>
            g == GamblerGames.Bones ? new Color(0.18f, 0.14f, 0.10f) : new Color(0.96f, 0.91f, 0.74f);

        // ----- action dispatch -----

        private void OnActionClicked()
        {
            if (_activeGame == GamblerGames.Cards) OnDealClicked();
            else if (_activeGame == GamblerGames.Duel) OnDuel();
            else OnAutoPlay();
        }

        // Duel: double-or-nothing best-of-3 vs the Jarl. Stake the currency; a win pays 2× back, a loss forfeits it.
        private void OnDuel()
        {
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null) return;
            if (!PayCost(profile)) { Refresh(); return; }
            int stake = profile.RequiredItem.Min;
            string cur = profile.RequiredItem.Prefab;
            _statusText.text = "Dueling the Jarl…";
            Minigames.DuelGame.Open(won =>
            {
                if (won)
                {
                    GrantOrSpawn(Player.m_localPlayer, Player.m_localPlayer.GetInventory(), cur, stake * 2);
                    _descriptionText.text = "<size=18><color=#1f5c0f>You take the pot — " + (stake * 2) + " " + cur + "!</color></size>";
                }
                else _descriptionText.text = "<size=18><color=#8a1f1f>The Jarl takes the pot.</color></size>";
                EndRound(profile);
            });
        }

        // ----- Cards: deal then pick -----

        private void OnDealClicked()
        {
            if (_dealt) return;
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null || profile.Rewards.Count == 0) return;
            if (!PayCost(profile)) { Refresh(); return; }
            ResetTiles();

            int activeCount = _tiles.Count;
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
            _remainingPicks = Mathf.Clamp(profile.MaxRolls, 1, activeCount);
            _dealt = true;
            StartCoroutine(DealIn());
            _descriptionText.text = "<color=#1f5c0f>Choose " + _remainingPicks + " card" + (_remainingPicks > 1 ? "s" : "") + " to reveal your reward.</color>";
            RefreshStake(profile);
            _statusText.text = "Picks left: " + _remainingPicks;
            SetRollButton("Pick a card", false);
        }

        private void OnCardClicked(int index)
        {
            if (!_dealt || _remainingPicks <= 0 || _clicked.Contains(index)) return;
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null || !_assigned.TryGetValue(index, out var reward) || index >= _tiles.Count) return;

            _clicked.Add(index);
            _remainingPicks--;
            StartCoroutine(FlipReveal(index, reward));
            GrantOrSpawn(Player.m_localPlayer, Player.m_localPlayer.GetInventory(), reward.Prefab, RollQty(reward));
            AppendWon(reward.Prefab, RollQty(reward));

            // Only the cards you pick reveal; the rest stay a mystery. Round ends when picks run out.
            if (_remainingPicks <= 0) EndRound(profile);
            else _statusText.text = "Picks left: " + _remainingPicks;
        }

        // ----- Bones / Runes / Wheel: one action draws MAXROLLS rewards -----

        private void OnAutoPlay()
        {
            var profile = GamblerProfileManager.GetProfile(_profileName);
            if (profile == null || profile.Rewards.Count == 0 || _tiles.Count == 0) return;
            if (!PayCost(profile)) { Refresh(); return; }
            ResetTiles();

            // Bones plays the dice-cast overlay first: draw the pool up front so the cast can SHOW what was won, then
            // grant it once the player collects (theater — the payout is unchanged, the cast is presentation).
            if (_activeGame == GamblerGames.Bones)
            {
                _statusText.text = "Casting…";
                var draws = RollDraws(profile);
                Minigames.DicePouchGame.Open(_tiles.Count, FormatDraws(draws), () => ApplyDraws(profile, draws));
                return;
            }
            if (_activeGame == GamblerGames.Wheel)
            {
                _statusText.text = "Spinning…";
                var draws = RollDraws(profile);
                var labels = WheelLabels(profile);
                int target = draws.Count > 0 ? labels.FindIndex(s => s == draws[0].Key.Prefab) : 0;
                Minigames.WheelGame.Open(labels, target < 0 ? 0 : target, FormatDraws(draws), () => ApplyDraws(profile, draws));
                return;
            }
            if (_activeGame == GamblerGames.Runes)
            {
                _statusText.text = "Casting…";
                var draws = RollDraws(profile);
                Minigames.RunesGame.Open(_tiles.Count, FormatDraws(draws), () => ApplyDraws(profile, draws));
                return;
            }
            ApplyDraws(profile, RollDraws(profile));
        }

        // Wheel wedge captions: the distinct reward prefabs from the pool (capped at 8, padded to a min of 2).
        private System.Collections.Generic.List<string> WheelLabels(GamblerProfile profile)
        {
            var labels = new System.Collections.Generic.List<string>();
            foreach (var e in profile.Rewards)
            {
                if (labels.Count >= 8) break;
                if (!labels.Contains(e.Prefab)) labels.Add(e.Prefab);
            }
            while (labels.Count < 2) labels.Add(labels.Count > 0 ? labels[0] : "Prize");
            return labels;
        }

        private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GamblerItemEntry, int>> RollDraws(GamblerProfile profile)
        {
            var draws = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GamblerItemEntry, int>>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                var r = DrawReward(profile);
                draws.Add(new System.Collections.Generic.KeyValuePair<GamblerItemEntry, int>(r, RollQty(r)));
            }
            return draws;
        }

        private static string FormatDraws(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GamblerItemEntry, int>> draws)
        {
            string s = "You won:  ";
            for (int i = 0; i < draws.Count; i++)
            {
                if (i > 0) s += "   ·   ";
                s += draws[i].Key.Prefab + " ×" + draws[i].Value;
            }
            return s;
        }

        // Reveal each drawn reward on its tile, grant it, and close the round. Shared by every instant/animated game.
        private void ApplyDraws(GamblerProfile profile, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GamblerItemEntry, int>> draws)
        {
            if (profile == null) return;
            _descriptionText.text = "<size=18>You won:</size>  ";
            bool first = true;
            for (int i = 0; i < draws.Count && i < _tiles.Count; i++)
            {
                var reward = draws[i].Key; int qty = draws[i].Value;
                RevealTile(i, reward, won: true);
                GrantOrSpawn(Player.m_localPlayer, Player.m_localPlayer.GetInventory(), reward.Prefab, qty);
                if (!first) _descriptionText.text += ", ";
                _descriptionText.text += "<color=#1f5c0f>" + reward.Prefab + " x" + qty + "</color>";
                first = false;
            }
            EndRound(profile);
        }

        private GamblerItemEntry DrawReward(GamblerProfile profile) => profile.Rewards[Random.Range(0, profile.Rewards.Count)];

        // ----- shared helpers -----

        private void RevealTile(int index, GamblerItemEntry reward, bool won)
        {
            if (index < 0 || index >= _tiles.Count) return;
            var tile = _tiles[index];
            if (tile.Face != null)
            {
                tile.Face.text = reward.Prefab + "\nx" + reward.Min;
                tile.Face.color = won ? new Color(0.70f, 1f, 0.70f) : new Color(0.78f, 0.72f, 0.60f);
                tile.Face.fontSize = 11f;
            }
            if (tile.Bg != null) tile.Bg.color = won ? new Color(0.10f, 0.18f, 0.08f, 0.95f) : new Color(0.08f, 0.06f, 0.04f, 0.92f);
            if (tile.Btn != null) tile.Btn.interactable = false;
        }

        private void EndRound(GamblerProfile profile)
        {
            _dealt = false;
            foreach (var t in _tiles) if (t.Btn != null) t.Btn.interactable = false;
            bool canPlay = CountInInventory(profile.RequiredItem.Prefab) >= profile.RequiredItem.Min;
            string again = _activeGame == GamblerGames.Cards ? "DEAL AGAIN" : ActionLabel(_activeGame) + " AGAIN";
            SetRollButton(canPlay ? again : "Need more " + profile.RequiredItem.Prefab, canPlay);
            RefreshStake(profile);
            _statusText.text = "Click " + (_activeGame == GamblerGames.Cards ? "DEAL" : ActionLabel(_activeGame)) + " to play again.";
        }

        private bool PayCost(GamblerProfile profile)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            int cost = profile.RequiredItem.Min;
            if (CountInInventory(profile.RequiredItem.Prefab) < cost) return false;
            return ConsumeFromInventory(player.GetInventory(), profile.RequiredItem.Prefab, cost);
        }

        private static int RollQty(GamblerItemEntry e) => e.Min == e.Max ? e.Min : Random.Range(e.Min, e.Max + 1);

        private void AppendWon(string prefab, int qty)
        {
            if (_descriptionText == null) return;
            if (!_descriptionText.text.Contains("You won"))
                _descriptionText.text = "<size=18>You won:</size>  <color=#1f5c0f>" + prefab + " x" + qty + "</color>";
            else
                _descriptionText.text += ", <color=#1f5c0f>" + prefab + " x" + qty + "</color>";
        }

        private void SetRollButton(string text, bool enabled)
        {
            if (_rollButton != null) _rollButton.interactable = enabled;
            if (_rollButtonText != null) _rollButtonText.text = text;
        }

        private void ResetState()
        {
            _dealt = false;
            _remainingPicks = 0;
            _clicked.Clear();
            _assigned.Clear();
        }

        // ----- inventory (unchanged from KG.InstantiateItem path) -----

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
            NotifyWin(player, drop, prefab, qty);
            if (inv.CanAddItem(drop.m_itemData, qty)) { inv.AddItem(prefab, qty, 1, 0, 0L, "", false); return; }
            var go = Object.Instantiate(pf,
                player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f,
                Quaternion.identity);
            var dr = go.GetComponent<ItemDrop>();
            if (dr != null) dr.m_itemData.m_stack = qty;
        }

        // kg Gambler_UI win flow: the winner's client asks the SERVER to post the Discord webhook (links
        // never leave the server), and — when GamblerEnableWinNotifications is on — shouts a global
        // [GAMBLER] chat line via the vanilla ChatMessage route, kg's exact presentation.
        private static void NotifyWin(Player player, ItemDrop drop, string prefab, int qty)
        {
            try
            {
                if (player == null || ZRoutedRpc.instance == null) return;
                string displayName = prefab;
                try { displayName = Localization.instance.Localize(drop.m_itemData.m_shared.m_name); } catch { }

                var pkg = new ZPackage();
                pkg.Write(player.GetPlayerName() ?? "");
                pkg.Write(qty);
                pkg.Write(displayName);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, Modules.Marketplace.DiscordWebhooks.GamblerWinRpc, pkg);

                if (Modules.Marketplace.MarketplaceGlobals.GamblerEnableWinNotifications)
                {
                    var user = UserInfo.GetLocalUser();
                    user.Name = "<color=#FF00FF>[GAMBLER]</color>";
                    string line = $"{player.GetPlayerName()} just won {displayName} x{qty}";
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ChatMessage",
                        player.GetHeadPoint(), 2, user, line);
                }
            }
            catch { }
        }
    }
}
