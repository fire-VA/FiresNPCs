using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Marketplace — KG-style flow with three tabs (Buy / Sell / MyListings) and KG's
    /// AFTERPRESS confirm pattern: clicking a row SELECTS it and reveals a confirm strip at the bottom
    /// with a Quantity input (Sell also gets a Price input) + action button. v2 prep for categories +
    /// pagination + currency dropdown lives behind the same shape so they can be added without UI churn.
    /// </summary>
    public class MarketplacePlayerPanel : BasePlayerModulePanel
    {
        private static MarketplacePlayerPanel _instance;
        protected override string PanelTitle => "Marketplace";
        protected override Vector2 PanelSize => new Vector2(960f, 640f);
        protected override string PrefabName => "FiresMarketplacePanel.prefab";

        private enum Tab { Buy, Sell, MyListings }
        private Tab _currentTab = Tab.Buy;
        private MarketCategory _category = MarketCategory.All;

        /// <summary>KG-parity sort modes — values match KG's ChangeSortBy(int) wire format so we can
        /// reuse identical 1..4 indexing on header clicks.</summary>
        private enum SortBy { None = 0, ItemName = 1, Count = 2, Price = 3, Seller = 4 }
        private enum SortType { Ascending, Descending }
        private SortBy _sortMode = SortBy.None;
        private SortType _sortType = SortType.Ascending;
        private TMP_Text _hdrItem, _hdrQty, _hdrPrice, _hdrSeller;

        /// <summary>Tile density cycle — KG ships fixed 14 r/p; we extend with Compact/Normal/Large so
        /// players can trade scroll depth for at-a-glance listing count without losing rows on a 4K monitor.</summary>
        private enum TileSize { Compact, Normal, Large }
        private TileSize _tileSize = TileSize.Normal;
        private int RowsPerPage => _tileSize switch { TileSize.Compact => 24, TileSize.Large => 8, _ => 14 };
        private float RowHeightPx => _tileSize switch { TileSize.Compact => 18f, TileSize.Large => 46f, _ => 30f };
        private float RowFontSize => _tileSize switch { TileSize.Compact => 11f, TileSize.Large => 16f, _ => 13f };
        private Button _sizeCycleButton;
        private TMP_Text _sizeCycleLabel;

        private int _page = 0;
        private int _maxPage = 1;

        private string _profileName;
        private RectTransform _listingContent;
        private GameObject _listingRowTemplate;
        private TMP_Text _statusText;
        private Button _buyTab, _sellTab, _myTab;
        private TMP_InputField _searchField;
        private TMP_Text _pageNumberText;
        private readonly Button[] _categoryButtons = new Button[6];

        // Selection + confirm panel
        private MarketplaceListing _selected;
        private GameObject _confirmPanel;
        private TMP_Text _confirmItemName;
        private TMP_InputField _confirmQuantity;
        private TMP_InputField _confirmPrice; // visible only on Sell tab
        private TMP_Text _confirmPriceLabel;
        private TMP_Text _confirmTotal;
        private Button _confirmActionButton;
        private TMP_Text _confirmActionLabel;
        private TMP_Dropdown _currencyDropdown; // visible only on Sell tab
        private TMP_Text _currencyDropdownLabel;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresMarketplacePanel");
                _instance = go.AddComponent<MarketplacePlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;

            _buyTab = FindBtn(t, "TabBuy");
            _sellTab = FindBtn(t, "TabSell");
            _myTab  = FindBtn(t, "TabMyListings");
            WireTab(_buyTab, Tab.Buy);
            WireTab(_sellTab, Tab.Sell);
            WireTab(_myTab, Tab.MyListings);

            _searchField = FindInput(t, "SearchField");
            if (_searchField != null) { _searchField.onValueChanged.RemoveAllListeners(); _searchField.onValueChanged.AddListener(_ => Refresh()); }

            string[] cats = { "CatAll","CatWeapons","CatArmor","CatConsumable","CatTools","CatResources" };
            for (int i = 0; i < cats.Length; i++)
            {
                int idx = i;
                var btn = FindBtn(t, cats[i]);
                _categoryButtons[i] = btn;
                if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => { _category = (MarketCategory)idx; _page = 0; _selected = null; HideConfirmPanel(); Refresh(); }); }
            }

            _hdrItem   = WireSortHeader(t, "SortItemName", SortBy.ItemName);
            _hdrQty    = WireSortHeader(t, "SortCount",    SortBy.Count);
            _hdrPrice  = WireSortHeader(t, "SortPrice",    SortBy.Price);
            _hdrSeller = WireSortHeader(t, "SortSeller",   SortBy.Seller);

            _sizeCycleButton = FindBtn(t, "SizeCycle");
            if (_sizeCycleButton != null) { _sizeCycleLabel = _sizeCycleButton.GetComponentInChildren<TMP_Text>(); _sizeCycleButton.onClick.RemoveAllListeners(); _sizeCycleButton.onClick.AddListener(CycleTileSize); }

            _listingContent     = FindRect(t, "ListingScroll/Viewport/Content");
            _listingRowTemplate = Find(t, "ListingRowTemplate");
            if (_listingRowTemplate != null) _listingRowTemplate.SetActive(false);
            EnsureReflowMeta(_listingContent, rowSpacing: 2f, padding: 6f);

            var pagePrev = FindBtn(t, "PageLeft");
            if (pagePrev != null) { pagePrev.onClick.RemoveAllListeners(); pagePrev.onClick.AddListener(() => { if (_page > 0) { _page--; Refresh(); } }); }
            _pageNumberText = FindTmp(t, "PageNumber");
            var pageNext = FindBtn(t, "PageRight");
            if (pageNext != null) { pageNext.onClick.RemoveAllListeners(); pageNext.onClick.AddListener(() => { if (_page + 1 < _maxPage) { _page++; Refresh(); } }); }

            BindConfirmPanel(t);

            _statusText = FindTmp(t, "StatusText");
        }

        private void WireTab(Button btn, Tab id)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => { _currentTab = id; _selected = null; HideConfirmPanel(); Refresh(); });
        }

        private TMP_Text WireSortHeader(Transform root, string node, SortBy mode)
        {
            var btn = FindBtn(root, node);
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => ChangeSortBy(mode)); }
            return FindTmp(root, node + "/Text");
        }

        private void BindConfirmPanel(Transform root)
        {
            _confirmPanel          = Find(root, "ConfirmPanel");
            _confirmItemName       = FindTmp(root, "ConfirmItemName");
            _confirmQuantity       = FindInput(root, "QuantityInput");
            _confirmPriceLabel     = FindTmp(root, "ConfirmPriceLabel");
            _confirmPrice          = FindInput(root, "PriceInput");
            _currencyDropdownLabel = FindTmp(root, "CurrencyDropdownLabel");
            var ddGo = Find(root, "CurrencyDropdown");
            _currencyDropdown      = ddGo != null ? ddGo.GetComponent<TMP_Dropdown>() : null;
            _confirmTotal          = FindTmp(root, "ConfirmTotal");

            _confirmActionButton = FindBtn(root, "ConfirmActionButton");
            if (_confirmActionButton != null)
            {
                _confirmActionLabel = _confirmActionButton.GetComponentInChildren<TMP_Text>();
                _confirmActionButton.onClick.RemoveAllListeners();
                _confirmActionButton.onClick.AddListener(OnConfirmClicked);
            }

            if (_confirmQuantity != null) { _confirmQuantity.onValueChanged.RemoveAllListeners(); _confirmQuantity.onValueChanged.AddListener(_ => UpdateConfirmTotal()); }
            if (_confirmPrice != null)    { _confirmPrice.onValueChanged.RemoveAllListeners();    _confirmPrice.onValueChanged.AddListener(_ => UpdateConfirmTotal()); }
            if (_currencyDropdown != null){ _currencyDropdown.onValueChanged.RemoveAllListeners(); _currencyDropdown.onValueChanged.AddListener(_ => UpdateConfirmTotal()); }

            if (_confirmPanel != null) _confirmPanel.SetActive(false);
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _currentTab = Tab.Buy;
            _selected = null;
            HideConfirmPanel();
            MarketplaceNetwork.OnListings = OnListingsUpdated;
            MarketplaceNetwork.OnResult = OnMarketResult;
            Refresh();
            MarketplaceNetwork.RequestListings(_profileName); // server seeds samples once, returns the global market
        }

        // Fired when the server pushes a fresh listing set (our own request, or another player's broadcast).
        private static void OnListingsUpdated()
        {
            if (_instance == null || !_instance.IsOpen) return;
            _instance.Refresh();
        }

        // Fired with a status line after a buy/sell/cancel reply (item grants/refunds are applied by the network).
        private static void OnMarketResult(string status)
        {
            if (_instance == null || !_instance.IsOpen || string.IsNullOrEmpty(status)) return;
            if (_instance._statusText != null) _instance._statusText.text = status;
        }

        private void Refresh()
        {
            if (_listingContent == null || _listingRowTemplate == null) return;
            HighlightTab(_buyTab, _currentTab == Tab.Buy);
            HighlightTab(_sellTab, _currentTab == Tab.Sell);
            HighlightTab(_myTab,  _currentTab == Tab.MyListings);

            ClearClones(_listingContent, _listingRowTemplate);

            string filter = _searchField?.text?.Trim().ToLowerInvariant() ?? "";
            IReadOnlyList<MarketplaceListing> rows = _currentTab switch
            {
                Tab.Buy        => MarketplaceListingManager.GetListings(false, _profileName, filter, _category),
                Tab.Sell       => MarketplaceListingManager.GetSellPrompts(_profileName, filter),
                Tab.MyListings => MarketplaceListingManager.GetListings(true,  _profileName, filter, _category),
                _ => new List<MarketplaceListing>(),
            };

            // KG-parity sort (None = leave native order, otherwise OrderBy/OrderByDescending).
            rows = ApplySort(rows);

            // Paginate (KG paginates at 14 rows/page; we cycle 8/14/24 by tile size).
            int rpp = RowsPerPage;
            _maxPage = Mathf.Max(1, Mathf.CeilToInt(rows.Count / (float)rpp));
            if (_page >= _maxPage) _page = _maxPage - 1;
            if (_page < 0) _page = 0;
            int skip = _page * rpp;
            int take = Mathf.Min(rpp, rows.Count - skip);
            for (int i = 0; i < take; i++) BuildRow(rows[skip + i]);
            ReflowScroll(_listingContent);

            // Highlight active category button.
            for (int i = 0; i < _categoryButtons.Length; i++)
                HighlightTab(_categoryButtons[i], (int)_category == i);
            _pageNumberText.text = (_page + 1) + " / " + _maxPage;

            _statusText.text = rows.Count + " listing(s)  -  Tab: " + _currentTab + "  -  Cat: " + _category;
            UpdateBuyArea(rows.Count);
        }

        // 0.0.558 redesign (Fire-approved mockup): the BuyArea strip names the selected listing and its button opens the same
        // confirm step a row click does; PagerText repeats the page in the footer. Absent on old prefabs → no-ops.
        private void UpdateBuyArea(int total)
        {
            var root = _listingContent != null ? _listingContent.root : null;
            if (root == null) return;
            var pager = FindTmp(root, "PagerText");
            if (pager != null) pager.text = "Page " + (_page + 1) + " / " + _maxPage + "  ·  " + total + " listing(s)";
            var buyText = FindTmp(root, "BuyText");
            var buyBtn = FindBtn(root, "BuyButton");
            var l = _selected;
            if (buyText != null)
                buyText.text = l == null ? "Select a listing to see it here."
                    : "<b>" + l.ItemPrefab + "</b> × " + l.Count + (string.IsNullOrEmpty(l.SellerName) ? "" : " from " + l.SellerName) + "  ·  " + l.Price + " " + l.Currency;
            if (buyBtn != null)
            {
                buyBtn.interactable = l != null;
                var lbl = buyBtn.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) lbl.text = l == null ? "Buy" : (_currentTab == Tab.MyListings ? "Cancel listing" : _currentTab == Tab.Sell ? "Sell" : "Buy for " + l.Price);
                buyBtn.onClick.RemoveAllListeners();
                buyBtn.onClick.AddListener(() => { if (_selected != null) ShowConfirmPanelFor(_selected); });
            }
        }

        private void HighlightTab(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active
                ? new Color(0.32f, 0.22f, 0.14f, 0.95f)
                : new Color(0.18f, 0.14f, 0.08f, 0.95f);
        }

        private void BuildRow(MarketplaceListing l)
        {
            var rowGo = Instantiate(_listingRowTemplate, _listingContent);
            rowGo.name = "Listing_" + l.ItemPrefab;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, RowHeightPx);

            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = l == _selected ? new Color(0.32f, 0.22f, 0.14f, 0.95f) : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            var btn = rowGo.GetComponent<Button>();
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => { _selected = l; ShowConfirmPanelFor(l); Refresh(); }); }

            float fs = RowFontSize;
            string itemLabel = l.Quality > 1 ? l.ItemPrefab + " <color=#e0c060>★" + l.Quality + "</color>" : l.ItemPrefab;
            if (l.DurabilityPercent < 100) itemLabel += " <color=#c07050>" + l.DurabilityPercent + "%</color>";
            SetCell(rowGo, "ItemLabel",   itemLabel, fs);
            SetCell(rowGo, "QtyLabel",    l.Count.ToString(), fs);
            SetCell(rowGo, "PriceLabel",  l.Price + " " + l.Currency, fs);
            SetCell(rowGo, "SellerLabel", l.SellerName, fs);
        }

        private static void SetCell(GameObject row, string node, string text, float fontSize)
        {
            var t = FindTmp(row.transform, node);
            if (t == null) return;
            t.text = text;
            t.fontSize = fontSize;
        }

        // ─── confirm panel (KG's AFTERPRESS) ───

        private void ShowConfirmPanelFor(MarketplaceListing l)
        {
            _confirmItemName.text = l.ItemPrefab;
            _confirmQuantity.text = l.Count.ToString();
            _confirmPrice.text = l.Price.ToString();

            bool sellTab = _currentTab == Tab.Sell;
            _confirmPriceLabel.gameObject.SetActive(sellTab);
            _confirmPrice.gameObject.SetActive(sellTab);
            _currencyDropdownLabel.gameObject.SetActive(sellTab);
            _currencyDropdown.gameObject.SetActive(sellTab);

            if (sellTab)
            {
                // Dropdown = banker-profile currency union INTERSECTED with MarketPlace.cfg PossibleCurrencies
                // (the server coerces off-list currencies to the first allowed one, so only offer what sticks).
                // Empty intersection falls back to the allowlist itself.
                var currencies = BankerProfileManager.GetAllAcceptedCurrencies()
                    .Where(c => string.Equals(MarketplaceGlobals.CoerceCurrency(c), c, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (currencies.Count == 0) currencies.Add(MarketplaceGlobals.CoerceCurrency(null));
                _currencyDropdown.ClearOptions();
                var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
                foreach (var c in currencies) options.Add(new TMP_Dropdown.OptionData(c));
                _currencyDropdown.AddOptions(options);
                // Default to the listing's currency if it's in the list.
                int idx = currencies.FindIndex(c => string.Equals(c, l.Currency, System.StringComparison.OrdinalIgnoreCase));
                _currencyDropdown.value = idx >= 0 ? idx : 0;
                _currencyDropdown.RefreshShownValue();
            }

            // On My Listings an admin also sees everyone else's rows — a non-owned row is an admin
            // REMOVE (server hard-deletes, nothing granted), own rows stay a Cancel (goods returned).
            bool ownListing = Player.m_localPlayer != null && l.SellerId == global::FiresRPGmaker.Modules.Marketplace.MarketplaceListingManager.LocalSellerKey(); // 0.0.532: platform key, as the server files it
            _confirmActionLabel.text = _currentTab switch
            {
                Tab.MyListings => ownListing ? "Cancel" : "Remove",
                Tab.Sell       => "List",
                _              => "Buy",
            };
            var col = _currentTab switch
            {
                Tab.MyListings => new Color(0.40f, 0.18f, 0.10f, 0.95f),
                Tab.Sell       => new Color(0.42f, 0.30f, 0.15f, 0.95f),
                _              => new Color(0.22f, 0.42f, 0.22f, 0.95f),
            };
            var img = _confirmActionButton.GetComponent<Image>(); if (img != null) img.color = col;

            UpdateConfirmTotal();
            _confirmPanel.SetActive(true);
        }

        private void HideConfirmPanel() { if (_confirmPanel != null) _confirmPanel.SetActive(false); }

        private void UpdateConfirmTotal()
        {
            if (_selected == null) { _confirmTotal.text = ""; return; }
            int qty = ParseOrDefault(_confirmQuantity?.text, _selected.Count);
            int price = ParseOrDefault(_confirmPrice?.text, _selected.Price);
            int total = qty * price;
            string currency = _currentTab == Tab.Sell && _currencyDropdown != null && _currencyDropdown.options.Count > 0
                ? _currencyDropdown.options[_currencyDropdown.value].text
                : _selected.Currency;
            _confirmTotal.text = _currentTab == Tab.Sell
                ? "You get: " + total + " " + currency
                : "Total: " + total + " " + currency;
        }

        private static int ParseOrDefault(string s, int fallback)
        {
            return int.TryParse(s ?? "", out var v) ? v : fallback;
        }

        private void OnConfirmClicked()
        {
            var l = _selected; if (l == null) return;
            int qty = ParseOrDefault(_confirmQuantity?.text, l.Count);
            int price = ParseOrDefault(_confirmPrice?.text, l.Price);

            if (_currentTab == Tab.MyListings)
            {
                // Server verifies ownership, removes the row, and replies with the goods to return (the item was
                // consumed on listing). No local refund — the reply grants it so it can't be double-refunded.
                MarketplaceNetwork.RequestCancel(l.Id);
                _statusText.text = "Cancelling…";
                _selected = null; HideConfirmPanel();
                return;
            }
            if (_currentTab == Tab.Sell)
            {
                var player = Player.m_localPlayer; if (player == null) { _statusText.text = "No active player."; return; }
                var inv = player.GetInventory();
                if (qty <= 0 || price <= 0) { _statusText.text = "Quantity and price must be positive."; return; }
                // Consume the EXACT stack the row showed (prefab + quality + variant), so a level-4 sword
                // listing can't eat the level-1 copy — the fidelity fields ride the listing to the server.
                if (!ConsumeMatchingFromInventory(inv, l, qty)) { _statusText.text = "Couldn't consume " + qty + "x " + l.ItemPrefab + " from inventory."; return; }
                string chosenCurrency = _currencyDropdown != null && _currencyDropdown.options.Count > 0
                    ? _currencyDropdown.options[_currencyDropdown.value].text
                    : l.Currency;
                // Item already consumed; the server records the listing (or replies to refund it if it fails).
                MarketplaceNetwork.RequestList(chosenCurrency, price, l.ItemPrefab, qty,
                    l.Quality, l.Variant, l.DurabilityPercent, l.CrafterName, l.CrafterID, l.CustomData);
                _statusText.text = "Listing…";
                _selected = null; HideConfirmPanel();
                return;
            }
            // Tab.Buy — consume the currency up front; the server grants the item on success or refunds the
            // currency if the listing was already gone/changed.
            {
                var player = Player.m_localPlayer; if (player == null) { _statusText.text = "No active player."; return; }
                var inv = player.GetInventory();
                int totalCost = qty * l.Price;
                if (qty <= 0 || qty > l.Count) { _statusText.text = "Quantity must be 1.." + l.Count + "."; return; }
                int held = CountInInventory(inv, l.Currency);
                if (held < totalCost) { _statusText.text = "Need " + totalCost + " " + l.Currency + " (have " + held + ")."; return; }
                if (!ConsumeFromInventory(inv, l.Currency, totalCost)) { _statusText.text = "Failed to consume currency."; return; }
                MarketplaceNetwork.RequestBuy(l.Id, qty, l.Currency, totalCost);
                _statusText.text = "Buying…";
                _selected = null; HideConfirmPanel();
            }
        }

        // ─── inventory helpers ───

        private static int CountInInventory(Inventory inv, string prefab)
        {
            int total = 0;
            foreach (var it in inv.GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == prefab) total += it.m_stack;
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

        // Sell-consume that honors the listed stack's identity: prefab + quality + variant must match, so
        // the item that leaves the inventory is the one whose fidelity fields ride the listing.
        private static bool ConsumeMatchingFromInventory(Inventory inv, MarketplaceListing l, int amount)
        {
            int remaining = amount;
            var copy = new System.Collections.Generic.List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (var it in copy)
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != l.ItemPrefab) continue;
                if (it.m_quality != l.Quality || it.m_variant != l.Variant) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
            return remaining == 0;
        }

        // ─── KG-parity sort + size cycle ─────────────────────────────────

        private void ChangeSortBy(SortBy mode)
        {
            if (_sortMode == mode)
                _sortType = _sortType == SortType.Ascending ? SortType.Descending : SortType.Ascending;
            else { _sortMode = mode; _sortType = SortType.Ascending; }
            _page = 0;
            UpdateSortHeaderLabels();
            Refresh();
        }

        private void UpdateSortHeaderLabels()
        {
            string glyph = _sortType == SortType.Ascending ? " ⇧" : " ⇩"; // ⇧/⇩
            if (_hdrItem   != null) _hdrItem.text   = "Item"   + (_sortMode == SortBy.ItemName ? glyph : "");
            if (_hdrQty    != null) _hdrQty.text    = "Qty"    + (_sortMode == SortBy.Count    ? glyph : "");
            if (_hdrPrice  != null) _hdrPrice.text  = "Price"  + (_sortMode == SortBy.Price    ? glyph : "");
            if (_hdrSeller != null) _hdrSeller.text = "Seller" + (_sortMode == SortBy.Seller   ? glyph : "");
        }

        private IReadOnlyList<MarketplaceListing> ApplySort(IReadOnlyList<MarketplaceListing> rows)
        {
            if (_sortMode == SortBy.None) return rows;
            System.Collections.Generic.IEnumerable<MarketplaceListing> q = rows;
            q = _sortMode switch
            {
                SortBy.ItemName => _sortType == SortType.Ascending ? q.OrderBy(r => r.ItemPrefab) : q.OrderByDescending(r => r.ItemPrefab),
                SortBy.Count    => _sortType == SortType.Ascending ? q.OrderBy(r => r.Count)      : q.OrderByDescending(r => r.Count),
                SortBy.Price    => _sortType == SortType.Ascending ? q.OrderBy(r => r.Price)      : q.OrderByDescending(r => r.Price),
                SortBy.Seller   => _sortType == SortType.Ascending ? q.OrderBy(r => r.SellerName) : q.OrderByDescending(r => r.SellerName),
                _ => q,
            };
            return q.ToList();
        }

        private void CycleTileSize()
        {
            _tileSize = _tileSize switch
            {
                TileSize.Compact => TileSize.Normal,
                TileSize.Normal  => TileSize.Large,
                _                => TileSize.Compact,
            };
            if (_sizeCycleLabel != null)
                _sizeCycleLabel.text = _tileSize == TileSize.Compact ? "Compact"
                                     : _tileSize == TileSize.Large   ? "Large"
                                     :                                 "Normal";
            _page = 0;
            Refresh();
        }
    }
}
