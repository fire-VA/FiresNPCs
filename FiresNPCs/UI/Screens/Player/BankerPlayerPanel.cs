using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Banker — direct port of KG's MarketplaceBankerNewUI flow:
    ///   • One row PER ACCEPTED CURRENCY (driven by the profile's CurrencyLimits dictionary).
    ///   • Each row has its own quantity input + per-row + (deposit) and - (withdraw) buttons.
    ///   • A "Deposit All" button at the top sweeps the player's inventory of every accepted currency.
    ///
    /// Interest accrual is OURS (not KG) — ticks while the panel is open at profile.InterestPerMinute %.
    /// Balances persist locally via <see cref="BankerBalanceStore"/>.
    /// </summary>
    public class BankerPlayerPanel : BasePlayerModulePanel
    {
        private static BankerPlayerPanel _instance;
        protected override string PanelTitle => "Banker";
        protected override Vector2 PanelSize => new Vector2(820f, 560f);
        protected override string PrefabName => "FiresBankerPanel.prefab";

        private string _profileName;
        private RectTransform _rowContent;
        private GameObject _rowTemplate;
        private TMP_Text _profileSummary;
        private TMP_Text _statusText;
        private float _refreshTimer;

        // Per-currency row cache so the periodic refresh updates the live numbers in place instead of
        // destroying + rebuilding every row (which flashed the +/- buttons every refresh tick).
        private sealed class RowRefs { public TMP_Text Inventory; public TMP_Text Bank; public int Limit; }
        private readonly Dictionary<string, RowRefs> _rows = new Dictionary<string, RowRefs>();

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresBankerPanel");
                _instance = go.AddComponent<BankerPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            _profileSummary = FindTmp(t, "ProfileSummary");
            _statusText     = FindTmp(t, "StatusText");
            _rowContent     = FindRect(t, "ScrollView/Viewport/Content");
            _rowTemplate    = Find(t, "RowTemplate");
            if (_rowTemplate != null) _rowTemplate.SetActive(false);
            EnsureReflowMeta(_rowContent, rowSpacing: 3f, padding: 6f);

            var depositAll = FindBtn(t, "DepositAllButton");
            if (depositAll != null) { depositAll.onClick.RemoveAllListeners(); depositAll.onClick.AddListener(OnDepositAllClicked); }
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            RebuildRows();
        }

        protected override void Update()
        {
            base.Update();
            if (!IsOpen) return;
            BankerInterestAccrual.Tick(Time.unscaledDeltaTime, _profileName);
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer > 5f)
            {
                _refreshTimer = 0f;
                // Structure is stable once built — only refresh the live numbers (no destroy/rebuild,
                // so the +/- buttons no longer flash). Rebuild only if rows haven't been built yet
                // (e.g. the profile loaded after the panel opened).
                if (_rows.Count == 0) RebuildRows();
                else UpdateValues();
            }
        }

        /// <summary>Full row (re)build — destroys + recreates the currency rows. Called on open and when
        /// the row set isn't built yet. NOT called on the periodic tick (that would flash the buttons).</summary>
        private void RebuildRows()
        {
            if (_rowContent == null || _rowTemplate == null) return;
            for (int i = _rowContent.childCount - 1; i >= 0; i--)
            {
                var child = _rowContent.GetChild(i).gameObject;
                if (child == _rowTemplate) continue; // never destroy the hidden clone template
                Destroy(child);
            }
            _rows.Clear();

            var profile = BankerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                if (_profileSummary != null) _profileSummary.text = "Profile '" + _profileName + "' not loaded.";
                string loaded = string.Join(", ", BankerProfileManager.AllProfileNames);
                if (_statusText != null)
                    _statusText.text = (string.IsNullOrEmpty(loaded) ? "No profiles loaded yet. " : "Loaded profiles: " + loaded + ". ")
                                     + "Drop a .cfg into BepInEx\\config\\FiresRPGmaker\\Bankers\\ and run firesnpcs_reload banker.";
                return;
            }
            if (_profileSummary != null)
                _profileSummary.text = "Interest: " + profile.InterestPerMinute.ToString("0.##") + "% / min  -  " + profile.CurrencyLimits.Count + " accepted currency(ies)";
            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?";

            foreach (var kv in profile.CurrencyLimits)
                BuildRow(kv.Key, playerName, kv.Value);
            ReflowScroll(_rowContent);
        }

        /// <summary>Refreshes only the live numbers (Inventory + Bank columns) on the existing rows —
        /// no GameObjects destroyed/created, so nothing flashes. Used by the periodic tick and after
        /// a deposit/withdraw (where only the values change, never the row structure).</summary>
        private void UpdateValues()
        {
            if (_rows.Count == 0) return;
            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?";
            foreach (var kv in _rows)
            {
                int balance = BankerBalanceStore.Get(playerName, kv.Key);
                int inventory = CountInInventory(kv.Key);
                if (kv.Value.Inventory != null) kv.Value.Inventory.text = inventory.ToString("N0");
                if (kv.Value.Bank != null) kv.Value.Bank.text = balance.ToString("N0") + " / " + kv.Value.Limit.ToString("N0");
            }
        }

        /// <summary>Clones the prefab's hidden RowTemplate for one accepted currency and binds its named
        /// children (CurrencyLabel/InventoryLabel/BankLabel/AmountField/AddButton/SubButton). The row's
        /// look — colors, column widths, button sizes — lives entirely in the prefab and is edited in Unity.</summary>
        private void BuildRow(string currency, string playerName, int limit)
        {
            var rowGo = Instantiate(_rowTemplate, _rowContent);
            rowGo.name = "Curr_" + currency;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 40f);

            int balance = BankerBalanceStore.Get(playerName, currency);
            int inventory = CountInInventory(currency);

            var currLabel = FindTmp(rowGo.transform, "CurrencyLabel");
            var invLabel  = FindTmp(rowGo.transform, "InventoryLabel");
            var bankLabel = FindTmp(rowGo.transform, "BankLabel");
            if (currLabel != null) currLabel.text = currency;
            if (invLabel  != null) invLabel.text  = inventory.ToString("N0");
            if (bankLabel != null) bankLabel.text = balance.ToString("N0") + " / " + limit.ToString("N0");

            // Cache the live-value labels so the periodic tick updates them in place (no rebuild → no flash).
            _rows[currency] = new RowRefs { Inventory = invLabel, Bank = bankLabel, Limit = limit };

            var input = FindInput(rowGo.transform, "AmountField");
            if (input != null) input.text = "";

            var addBtn = FindBtn(rowGo.transform, "AddButton");
            if (addBtn != null) { addBtn.onClick.RemoveAllListeners(); addBtn.onClick.AddListener(() => OnRowDeposit(currency, input, limit)); }
            var subBtn = FindBtn(rowGo.transform, "SubButton");
            if (subBtn != null) { subBtn.onClick.RemoveAllListeners(); subBtn.onClick.AddListener(() => OnRowWithdraw(currency, input)); }
        }

        private void OnRowDeposit(string currency, TMP_InputField input, int limit)
        {
            if (!int.TryParse(input?.text ?? "0", out int amount) || amount <= 0) { _statusText.text = "Enter a positive amount."; return; }
            var player = Player.m_localPlayer; if (player == null) return;
            string name = player.GetPlayerName();
            int balance = BankerBalanceStore.Get(name, currency);
            int held = CountInInventory(currency);
            if (held < amount) { _statusText.text = "Not enough " + currency + " (have " + held + ")."; return; }
            int room = Mathf.Max(0, limit - balance);
            if (amount > room) { _statusText.text = "Bank only has room for " + room + " more " + currency + "."; return; }
            if (!ConsumeFromInventory(player.GetInventory(), currency, amount)) { _statusText.text = "Consume failed."; return; }
            BankerBalanceStore.Set(name, currency, balance + amount);
            _statusText.text = "Deposited " + amount + " " + currency + ".";
            input.text = "";
            UpdateValues();
        }

        private void OnRowWithdraw(string currency, TMP_InputField input)
        {
            if (!int.TryParse(input?.text ?? "0", out int amount) || amount <= 0) { _statusText.text = "Enter a positive amount."; return; }
            var player = Player.m_localPlayer; if (player == null) return;
            string name = player.GetPlayerName();
            int balance = BankerBalanceStore.Get(name, currency);
            if (amount > balance) { _statusText.text = "You only have " + balance + " " + currency + " banked."; return; }
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(currency) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop == null) { _statusText.text = "Unknown prefab '" + currency + "'."; return; }
            var inv = player.GetInventory();
            if (!inv.CanAddItem(drop.m_itemData, amount)) { _statusText.text = "No room in your inventory."; return; }
            inv.AddItem(currency, amount, 1, 0, 0L, "");
            BankerBalanceStore.Set(name, currency, balance - amount);
            _statusText.text = "Withdrew " + amount + " " + currency + ".";
            input.text = "";
            UpdateValues();
        }

        /// <summary>KG's PutAll: sweeps every stack of every accepted currency from the player's inventory into the bank.</summary>
        private void OnDepositAllClicked()
        {
            var player = Player.m_localPlayer; if (player == null) return;
            var profile = BankerProfileManager.GetProfile(_profileName); if (profile == null) return;
            string name = player.GetPlayerName();
            var inv = player.GetInventory();
            int totalDeposited = 0; int currenciesTouched = 0;
            foreach (var kv in profile.CurrencyLimits)
            {
                int balance = BankerBalanceStore.Get(name, kv.Key);
                int held = CountInInventory(kv.Key);
                int room = Mathf.Max(0, kv.Value - balance);
                int take = Mathf.Min(held, room);
                if (take <= 0) continue;
                if (!ConsumeFromInventory(inv, kv.Key, take)) continue;
                BankerBalanceStore.Set(name, kv.Key, balance + take);
                totalDeposited += take; currenciesTouched++;
            }
            _statusText.text = currenciesTouched == 0 ? "Nothing to deposit." :
                "Deposited " + totalDeposited + " item(s) across " + currenciesTouched + " currency(ies).";
            UpdateValues();
        }

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
    }
}
