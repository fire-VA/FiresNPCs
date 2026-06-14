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

        private string _profileName;
        private RectTransform _rowContent;
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

        protected override void BuildContent(RectTransform contentArea)
        {
            _profileSummary = MakeLabel(contentArea, "",
                                        new Vector2(0f, 0.93f), new Vector2(0.70f, 1f),
                                        new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                        size: 13f, color: new Color(0.85f, 0.78f, 0.62f));

            // Master "Deposit All" button — sweeps inventory for every accepted currency.
            var depositAllBtn = MakeButton(contentArea, "Deposit All",
                                           new Vector2(0.72f, 0.93f), new Vector2(1f, 1f),
                                           new Vector2(4f, 0f), new Vector2(-8f, 0f),
                                           bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 14f);
            depositAllBtn.onClick.AddListener(OnDepositAllClicked);

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.85f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "Currency",
                      new Vector2(0.02f, 0.85f), new Vector2(0.28f, 0.92f), Vector2.zero, Vector2.zero,
                      size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Inventory",
                      new Vector2(0.29f, 0.85f), new Vector2(0.42f, 0.92f), Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Bank",
                      new Vector2(0.43f, 0.85f), new Vector2(0.55f, 0.92f), Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Amount",
                      new Vector2(0.56f, 0.85f), new Vector2(0.78f, 0.92f), Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Actions",
                      new Vector2(0.79f, 0.85f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));

            _rowContent = MakeVerticalScroll(contentArea,
                                             new Vector2(0f, 0.06f), new Vector2(1f, 0.84f),
                                             Vector2.zero, Vector2.zero,
                                             rowSpacing: 3f, scrollSensitivity: 120f);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0f), new Vector2(1f, 0.05f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
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
            for (int i = _rowContent.childCount - 1; i >= 0; i--)
                Destroy(_rowContent.GetChild(i).gameObject);
            _rows.Clear();

            var profile = BankerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                _profileSummary.text = "Profile '" + _profileName + "' not loaded.";
                string loaded = string.Join(", ", BankerProfileManager.AllProfileNames);
                _statusText.text = (string.IsNullOrEmpty(loaded) ? "No profiles loaded yet. " : "Loaded profiles: " + loaded + ". ")
                                 + "Drop a .cfg into BepInEx\\config\\FiresRPGmaker\\Bankers\\ and run firesnpcs_reload banker.";
                return;
            }
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

        private void BuildRow(string currency, string playerName, int limit)
        {
            var rowGo = new GameObject("Curr_" + currency, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_rowContent, false);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 40f);

            int balance = BankerBalanceStore.Get(playerName, currency);
            int inventory = CountInInventory(currency);

            MakeLabel(rowGo.transform, currency,
                      new Vector2(0.02f, 0f), new Vector2(0.28f, 1f), Vector2.zero, Vector2.zero, size: 14f);
            var invLabel = MakeLabel(rowGo.transform, inventory.ToString("N0"),
                      new Vector2(0.29f, 0f), new Vector2(0.42f, 1f), Vector2.zero, Vector2.zero,
                      size: 14f, align: TextAlignmentOptions.Center);
            var bankLabel = MakeLabel(rowGo.transform, balance.ToString("N0") + " / " + limit.ToString("N0"),
                      new Vector2(0.43f, 0f), new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero,
                      size: 14f, align: TextAlignmentOptions.Center,
                      color: new Color(1f, 0.85f, 0.55f));

            // Cache the live-value labels so the periodic tick updates them in place (no rebuild → no flash).
            _rows[currency] = new RowRefs { Inventory = invLabel, Bank = bankLabel, Limit = limit };

            // Per-row amount input (replaces my old global "select then type" flow).
            var input = BuildAmountInput(rowGo.transform,
                                         new Vector2(0.56f, 0.12f), new Vector2(0.78f, 0.88f));
            // + (deposit) button
            var addBtn = MakeButton(rowGo.transform, "+",
                                    new Vector2(0.79f, 0.12f), new Vector2(0.88f, 0.88f),
                                    new Vector2(4f, 0f), new Vector2(-2f, 0f),
                                    bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 16f);
            addBtn.onClick.AddListener(() => OnRowDeposit(currency, input, limit));
            // - (withdraw) button
            var subBtn = MakeButton(rowGo.transform, "-",
                                    new Vector2(0.88f, 0.12f), new Vector2(0.99f, 0.88f),
                                    new Vector2(2f, 0f), new Vector2(-4f, 0f),
                                    bgColor: new Color(0.42f, 0.30f, 0.15f, 0.95f), fontSize: 16f);
            subBtn.onClick.AddListener(() => OnRowWithdraw(currency, input));
        }

        private TMP_InputField BuildAmountInput(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("AmountField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(4f, 0f); rt.offsetMax = new Vector2(-4f, 0f);
            go.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(-8f, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 14f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.Center;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(txt);

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.text = "";
            return input;
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
