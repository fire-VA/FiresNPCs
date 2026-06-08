using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Banker UI: per-currency balance list + deposit/withdraw flow.
    /// Reads accepted currencies + limits from the assigned <see cref="BankerProfile"/>; balances persist
    /// via <see cref="BankerBalanceStore"/>. Deposit consumes inventory items, withdraw drops them into the
    /// player's inventory.
    /// </summary>
    public class BankerPlayerPanel : BasePlayerModulePanel
    {
        private static BankerPlayerPanel _instance;
        protected override string PanelTitle => "Banker";
        protected override Vector2 PanelSize => new Vector2(780f, 520f);

        private string _profileName;
        private RectTransform _rowContent;
        private TMP_InputField _amountField;
        private TMP_Text _statusText;
        private TMP_Text _profileSummary;
        private string _selectedCurrency;

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
                                        new Vector2(0f, 0.93f), new Vector2(1f, 1f),
                                        new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                        size: 13f, color: new Color(0.85f, 0.78f, 0.62f));

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.83f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "Currency",      new Vector2(0.02f, 0.83f), new Vector2(0.40f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Balance",       new Vector2(0.42f, 0.83f), new Vector2(0.66f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Limit",         new Vector2(0.68f, 0.83f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "",              new Vector2(0.92f, 0.83f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));

            _rowContent = MakeVerticalScroll(contentArea,
                                             new Vector2(0f, 0.24f), new Vector2(1f, 0.82f),
                                             Vector2.zero, Vector2.zero,
                                             rowSpacing: 2f, scrollSensitivity: 120f);

            // Bottom: amount input + deposit/withdraw buttons
            MakeLabel(contentArea, "Amount:",
                      new Vector2(0f, 0.12f), new Vector2(0.16f, 0.22f),
                      new Vector2(8f, 0f), Vector2.zero,
                      size: 14f, align: TextAlignmentOptions.MidlineRight);
            BuildAmountField(contentArea);

            var depositBtn = MakeButton(contentArea, "Deposit",
                                        new Vector2(0.50f, 0.12f), new Vector2(0.72f, 0.22f),
                                        new Vector2(8f, 0f), new Vector2(-4f, 0f),
                                        bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 15f);
            depositBtn.onClick.AddListener(() => OnTransfer(true));

            var withdrawBtn = MakeButton(contentArea, "Withdraw",
                                         new Vector2(0.72f, 0.12f), new Vector2(0.94f, 0.22f),
                                         new Vector2(4f, 0f), new Vector2(-8f, 0f),
                                         bgColor: new Color(0.42f, 0.30f, 0.15f, 0.95f), fontSize: 15f);
            withdrawBtn.onClick.AddListener(() => OnTransfer(false));

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.02f), new Vector2(1f, 0.11f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        private void BuildAmountField(Transform parent)
        {
            var go = new GameObject("AmountField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.16f, 0.12f); rt.anchorMax = new Vector2(0.50f, 0.22f);
            rt.offsetMin = new Vector2(8f, 0f); rt.offsetMax = new Vector2(-8f, 0f);
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
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 16f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.Left;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(txt);

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            _amountField = input;
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selectedCurrency = null;
            Refresh();
        }

        // Tick interest accrual while the panel is open so the player can watch balances grow at the
        // configured rate. The static accrual batches per real-minute internally.
        private float _refreshTimer;
        protected override void Update()
        {
            base.Update();
            if (!IsOpen) return;
            BankerInterestAccrual.Tick(Time.unscaledDeltaTime, _profileName);
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer > 5f) { _refreshTimer = 0f; Refresh(); }
        }

        private void Refresh()
        {
            for (int i = _rowContent.childCount - 1; i >= 0; i--)
                Destroy(_rowContent.GetChild(i).gameObject);

            var profile = BankerProfileManager.GetProfile(_profileName);
            if (profile == null)
            {
                _profileSummary.text = "Profile '" + _profileName + "' not loaded.";
                _statusText.text = "Drop a .cfg into config\\firesnpcs\\banker\\ then use the admin Apply button to reload.";
                return;
            }
            _profileSummary.text = "Interest: " + profile.InterestPerMinute.ToString("0.##") + "% / min  -  " + profile.CurrencyLimits.Count + " accepted currency(ies)";
            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?";

            foreach (var kv in profile.CurrencyLimits)
            {
                BuildRow(kv.Key, BankerBalanceStore.Get(playerName, kv.Key), kv.Value);
            }
        }

        private void BuildRow(string currency, int balance, int limit)
        {
            var rowGo = new GameObject("Curr_" + currency, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            rowGo.transform.SetParent(_rowContent, false);
            rowGo.GetComponent<Image>().color = currency == _selectedCurrency
                ? new Color(0.32f, 0.22f, 0.10f, 0.95f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 30f);
            rowGo.GetComponent<Button>().onClick.AddListener(() => { _selectedCurrency = currency; Refresh(); });

            MakeLabel(rowGo.transform, currency,                new Vector2(0.02f, 0f), new Vector2(0.40f, 1f), Vector2.zero, Vector2.zero, size: 14f);
            MakeLabel(rowGo.transform, balance.ToString("N0"),   new Vector2(0.42f, 0f), new Vector2(0.66f, 1f), Vector2.zero, Vector2.zero, size: 14f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(rowGo.transform, limit.ToString("N0"),     new Vector2(0.68f, 0f), new Vector2(0.92f, 1f), Vector2.zero, Vector2.zero, size: 14f, align: TextAlignmentOptions.Center);
        }

        private void OnTransfer(bool deposit)
        {
            if (string.IsNullOrEmpty(_selectedCurrency)) { _statusText.text = "Select a currency row first."; return; }
            if (!int.TryParse(_amountField?.text ?? "0", out int amount) || amount <= 0)
            { _statusText.text = "Enter a positive amount."; return; }

            var profile = BankerProfileManager.GetProfile(_profileName);
            if (profile == null || !profile.CurrencyLimits.TryGetValue(_selectedCurrency, out int limit))
            { _statusText.text = "This bank doesn't accept " + _selectedCurrency + "."; return; }

            var player = Player.m_localPlayer;
            if (player == null) { _statusText.text = "No active player."; return; }
            var inv = player.GetInventory();
            string name = player.GetPlayerName();
            int current = BankerBalanceStore.Get(name, _selectedCurrency);

            if (deposit)
            {
                int held = CountInInventory(inv, _selectedCurrency);
                if (held < amount) { _statusText.text = "Not enough " + _selectedCurrency + " in your inventory (have " + held + ")."; return; }
                int room = Mathf.Max(0, limit - current);
                if (amount > room) { _statusText.text = "Bank only has room for " + room + " more " + _selectedCurrency + "."; return; }
                if (!ConsumeFromInventory(inv, _selectedCurrency, amount))
                { _statusText.text = "Failed to consume from inventory."; return; }
                BankerBalanceStore.Set(name, _selectedCurrency, current + amount);
                _statusText.text = "Deposited " + amount + " " + _selectedCurrency + ".";
            }
            else
            {
                if (amount > current) { _statusText.text = "You only have " + current + " " + _selectedCurrency + " banked."; return; }
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(_selectedCurrency) : null;
                if (prefab == null) { _statusText.text = "Unknown prefab '" + _selectedCurrency + "' (currency not in ObjectDB)."; return; }
                var item = prefab.GetComponent<ItemDrop>();
                if (item == null) { _statusText.text = "'" + _selectedCurrency + "' is not an ItemDrop."; return; }
                if (!inv.CanAddItem(item.m_itemData, amount)) { _statusText.text = "No room in your inventory."; return; }
                inv.AddItem(_selectedCurrency, amount, 1, 0, 0L, "");
                BankerBalanceStore.Set(name, _selectedCurrency, current - amount);
                _statusText.text = "Withdrew " + amount + " " + _selectedCurrency + ".";
            }
            Refresh();
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
