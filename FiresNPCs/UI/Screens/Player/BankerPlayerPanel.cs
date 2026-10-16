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
        private TMP_Text _titleText;       // NPC name, centered in the header
        private TMP_Text _profileSummary;  // interest line (the centered SubtitleText)
        private TMP_Text _statusText;
        private float _refreshTimer;

        // Per-currency row cache so the periodic refresh updates the live numbers in place instead of
        // destroying + rebuilding every row (which flashed the +/- buttons every refresh tick).
        private sealed class RowRefs { public TMP_Text Inventory; public TMP_Text Bank; public int Limit; public TMP_Text Capacity; public Image Fill; }
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
            _titleText      = FindTmp(t, "TitleText");
            _profileSummary = FindTmp(t, "SubtitleText");   // interest line lives in the centered subtitle now
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
            // kg-style: the NPC's name is the centered header. RebuildRows fills the interest line beneath it
            // (SubtitleText), overwriting the base panel's default "NPC - Profile" subtitle.
            if (_titleText != null) _titleText.text = string.IsNullOrEmpty(npcDisplayName) ? "Banker" : npcDisplayName;
            BankerNetwork.OnResult = OnBankResult;
            RebuildRows();
            BankerNetwork.RequestBalance(_profileName);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsOpen) return;
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer > 5f)
            {
                _refreshTimer = 0f;
                // Pull the authoritative balance from the server (this also ticks server-side interest at the
                // profile's rate). Rebuild rows if the profile only finished loading after the panel opened.
                if (_rows.Count == 0) RebuildRows();
                BankerNetwork.RequestBalance(_profileName);
            }
        }

        // Fired on the local client when a server reply lands (sync, deposit, or withdraw). The reply handler
        // has already updated the cache + granted/refunded items; here we just surface the status and repaint.
        private static void OnBankResult(string status)
        {
            if (_instance == null || !_instance.IsOpen) return;
            if (!string.IsNullOrEmpty(status) && _instance._statusText != null) _instance._statusText.text = status;
            _instance.UpdateValues();
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
            // 0.0.548 (Fire, stream 10:19: "Profile '' not loaded"): a banker NPC with no profile name still works. It falls back
            // to the "default" profile, or the first loaded one, and the panel and its server calls use that name from here on.
            // 0.0.565: also when the named profile isn't loaded (FDT 582 "fdt_bigbatch" showed no rows).
            if (profile == null)
            {
                string fallback = null;
                foreach (var n in BankerProfileManager.AllProfileNames)
                {
                    if (string.Equals(n, "default", System.StringComparison.OrdinalIgnoreCase)) { fallback = n; break; }
                    if (fallback == null) fallback = n;
                }
                if (fallback != null)
                {
                    string wanted = _profileName;
                    _profileName = fallback;
                    profile = BankerProfileManager.GetProfile(fallback);
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogInfo($"[Banker] banker profile '{wanted}' not loaded; using '{fallback}'");
                }
            }
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
                // 0.0.557: "<rate>% <period> interest · Accepts A, B, C" (the approved mockup's subtitle).
                _profileSummary.text = (profile.InterestPerMinute > 0f ? profile.InterestPerMinute.ToString("0.##") + "% " + profile.PeriodLabel + " interest" : "No interest")
                                     + "  ·  Accepts " + string.Join(", ", profile.CurrencyLimits.Keys);
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
                // 0.0.558 redesign: BankLabel = the banked number; CapacityText "x / max" + CapacityFill bar (old prefabs: BankLabel "x / max").
                if (kv.Value.Bank != null) kv.Value.Bank.text = kv.Value.Capacity != null ? balance.ToString("N0") : balance.ToString("N0") + " / " + kv.Value.Limit.ToString("N0");
                if (kv.Value.Capacity != null) kv.Value.Capacity.text = balance.ToString("N0") + " / " + kv.Value.Limit.ToString("N0");
                if (kv.Value.Fill != null) kv.Value.Fill.fillAmount = kv.Value.Limit > 0 ? Mathf.Clamp01(balance / (float)kv.Value.Limit) : 0f;
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
            SetRowHeight(rowGo, 42f);
            EnsureRowIcon(rowGo, currency);   // set the baked Icon node's sprite

            int balance = BankerBalanceStore.Get(playerName, currency);
            int inventory = CountInInventory(currency);

            var currLabel = FindTmp(rowGo.transform, "CurrencyLabel");
            var invLabel  = FindTmp(rowGo.transform, "InventoryLabel");
            var bankLabel = FindTmp(rowGo.transform, "BankLabel");
            if (currLabel != null) currLabel.text = currency;
            if (invLabel  != null) invLabel.text  = inventory.ToString("N0");
            if (bankLabel != null) bankLabel.text = balance.ToString("N0") + " / " + limit.ToString("N0");

            // Cache the live-value labels so the periodic tick updates them in place (no rebuild → no flash).
            var capText = FindTmp(rowGo.transform, "CapacityText");
            var fillGo = Find(rowGo.transform, "CapacityFill");
            _rows[currency] = new RowRefs { Inventory = invLabel, Bank = bankLabel, Limit = limit, Capacity = capText, Fill = fillGo != null ? fillGo.GetComponent<Image>() : null };

            // 0.0.558 redesign (Fire-approved mockup): quick buttons per row. Minus = withdraw, Plus = deposit, All = deposit
            // every one of this currency in the bag. Shift multiplies the step by 10. Each shows a hover tip.
            WireStep(rowGo, "Minus10", currency, limit, -10);
            WireStep(rowGo, "Minus1",  currency, limit, -1);
            WireStep(rowGo, "Plus1",   currency, limit, +1);
            WireStep(rowGo, "Plus10",  currency, limit, +10);
            var allBtn = FindBtn(rowGo.transform, "AllButton");
            if (allBtn != null)
            {
                allBtn.onClick.RemoveAllListeners();
                allBtn.onClick.AddListener(() => DepositAmount(currency, CountInInventory(currency), limit));
                AddTip(allBtn.gameObject, "Deposit every " + currency + " in your bag");
            }

            var input = FindInput(rowGo.transform, "AmountField");
            if (input != null) input.text = "";

            var addBtn = FindBtn(rowGo.transform, "AddButton");
            if (addBtn != null) { addBtn.onClick.RemoveAllListeners(); addBtn.onClick.AddListener(() => OnRowDeposit(currency, input, limit)); }
            var subBtn = FindBtn(rowGo.transform, "SubButton");
            if (subBtn != null) { subBtn.onClick.RemoveAllListeners(); subBtn.onClick.AddListener(() => OnRowWithdraw(currency, input)); }
        }

        private void WireStep(GameObject rowGo, string name, string currency, int limit, int step)
        {
            var btn = FindBtn(rowGo.transform, name);
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                int n = Mathf.Abs(step) * ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 10 : 1);
                if (step > 0) DepositAmount(currency, n, limit); else WithdrawAmount(currency, n);
            });
            AddTip(btn.gameObject, (step > 0 ? "Deposit " : "Withdraw ") + Mathf.Abs(step) + " " + currency + " (Shift = " + (Mathf.Abs(step) * 10) + ")");
        }

        private static void AddTip(GameObject go, string text)
        {
            var trig = go.GetComponent<UnityEngine.EventSystems.EventTrigger>() ?? go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { try { FiresCore.UI.FiresHoverTip.ShowAt("", text, (Vector2)Input.mousePosition + new Vector2(14f, -14f)); } catch { } });
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { try { FiresCore.UI.FiresHoverTip.HideTip(); } catch { } });
            trig.triggers.Add(enter); trig.triggers.Add(exit);
        }

        private void DepositAmount(string currency, int amount, int limit)
        {
            if (amount <= 0) { _statusText.text = "You have no " + currency + " to deposit."; return; }
            var player = Player.m_localPlayer; if (player == null) return;
            string name = player.GetPlayerName();
            int balance = BankerBalanceStore.Get(name, currency);
            int held = CountInInventory(currency);
            int room = Mathf.Max(0, limit - balance);
            if (room <= 0) { _statusText.text = "Bank only has room for 0 more " + currency + "."; return; }
            amount = Mathf.Min(amount, held, room);
            if (amount <= 0) { _statusText.text = "Not enough " + currency + " (have " + held + ")."; return; }
            if (!ConsumeFromInventory(player.GetInventory(), currency, amount)) { _statusText.text = "Consume failed."; return; }
            BankerNetwork.RequestDeposit(_profileName, currency, amount);
            UpdateValues();
        }

        private void WithdrawAmount(string currency, int amount)
        {
            var player = Player.m_localPlayer; if (player == null) return;
            int balance = BankerBalanceStore.Get(player.GetPlayerName(), currency);
            amount = Mathf.Min(amount, balance);
            if (amount <= 0) { _statusText.text = "You only have " + balance + " " + currency + " banked."; return; }
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(currency) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop == null) { _statusText.text = "Unknown prefab '" + currency + "'."; return; }
            if (!player.GetInventory().CanAddItem(drop.m_itemData, amount)) { _statusText.text = "No room in your inventory."; return; }
            BankerNetwork.RequestWithdraw(_profileName, currency, amount);
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
            BankerNetwork.RequestDeposit(_profileName, currency, amount);
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
            BankerNetwork.RequestWithdraw(_profileName, currency, amount);
            input.text = "";
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
                BankerNetwork.RequestDeposit(_profileName, kv.Key, take);
                totalDeposited += take; currenciesTouched++;
            }
            _statusText.text = currenciesTouched == 0 ? "Nothing to deposit." :
                "Deposited " + totalDeposited + " item(s) across " + currenciesTouched + " currency(ies).";
            UpdateValues();
        }

        // Resolves an item's inventory icon from its prefab (ObjectDB first, ZNetScene fallback). Set onto the
        // row's BAKED Icon node — see EnsureRowIcon.
        private static Sprite ResolveItemIcon(string prefabName)
        {
            try
            {
                GameObject pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
                if (pf == null && ZNetScene.instance != null) pf = ZNetScene.instance.GetPrefab(prefabName);
                var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
                var icon = drop != null && drop.m_itemData != null ? drop.m_itemData.GetIcon() : null;
                if (icon != null) return icon;
            }
            catch { }
            return null;
        }

        // The Icon node is BAKED into the row template (RowTemplate/IconSlot/Icon), so we only set its sprite.
        // The old code created the icon at runtime and shoved the CurrencyLabel right to clear it, which
        // collided with the tight columns — that was the overlapping-boxes bug. Now the layout is fixed in the
        // prefab and this just fills in the art.
        private void EnsureRowIcon(GameObject rowGo, string prefab)
        {
            var iconGo = Find(rowGo.transform, "Icon");
            var img = iconGo != null ? iconGo.GetComponent<Image>() : null;
            if (img == null) return;
            var sprite = ResolveItemIcon(prefab);
            img.sprite = sprite;
            img.color = Color.white;
            img.enabled = sprite != null;   // hide the slot's inner image when no icon resolves
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
