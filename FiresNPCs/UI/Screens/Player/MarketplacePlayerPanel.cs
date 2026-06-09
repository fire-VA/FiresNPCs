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

        protected override void BuildContent(RectTransform contentArea)
        {
            // Top tab strip
            _buyTab = MakeTab(contentArea, "TabBuy", "Buy", 0.00f, 0.16f, Tab.Buy);
            _sellTab = MakeTab(contentArea, "TabSell", "Sell", 0.17f, 0.33f, Tab.Sell);
            _myTab  = MakeTab(contentArea, "TabMyListings", "My Listings", 0.34f, 0.55f, Tab.MyListings);

            // Search bar
            MakeLabel(contentArea, "Search:",
                      new Vector2(0.58f, 0.94f), new Vector2(0.68f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.MidlineRight);
            BuildSearchField(contentArea);

            // KG-style category strip just below the tabs (ALL / Weapons / Armor / Consumable / Tools / Resources).
            string[] catNames = { "All", "Weapons", "Armor", "Consumable", "Tools", "Resources" };
            for (int i = 0; i < catNames.Length; i++)
            {
                int idx = i;
                float xMin = 0.02f + i * 0.155f;
                float xMax = xMin + 0.145f;
                var btn = MakeButton(contentArea, catNames[i],
                                     new Vector2(xMin, 0.88f), new Vector2(xMax, 0.93f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 12f);
                btn.name = "Cat" + catNames[i];
                btn.onClick.AddListener(() => { _category = (MarketCategory)idx; _page = 0; _selected = null; HideConfirmPanel(); Refresh(); });
                _categoryButtons[i] = btn;
            }

            // Column header — KG-parity sort: clicking a column sets/toggles the sort mode (asc↔desc).
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.82f), new Vector2(1f, 0.87f), Vector2.zero, Vector2.zero);
            _hdrItem   = MakeSortHeader(contentArea, "Item",   SortBy.ItemName, new Vector2(0.02f, 0.82f), new Vector2(0.40f, 0.87f), TextAlignmentOptions.Left);
            _hdrQty    = MakeSortHeader(contentArea, "Qty",    SortBy.Count,    new Vector2(0.42f, 0.82f), new Vector2(0.54f, 0.87f), TextAlignmentOptions.Center);
            _hdrPrice  = MakeSortHeader(contentArea, "Price",  SortBy.Price,    new Vector2(0.56f, 0.82f), new Vector2(0.72f, 0.87f), TextAlignmentOptions.Center);
            _hdrSeller = MakeSortHeader(contentArea, "Seller", SortBy.Seller,   new Vector2(0.74f, 0.82f), new Vector2(0.88f, 0.87f), TextAlignmentOptions.Left);

            // Tile size cycle button — top-right of the header row.
            _sizeCycleButton = MakeButton(contentArea, "Tiles",
                                          new Vector2(0.89f, 0.82f), new Vector2(0.99f, 0.87f),
                                          Vector2.zero, Vector2.zero,
                                          bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 11f);
            _sizeCycleButton.name = "SizeCycle";
            _sizeCycleLabel = _sizeCycleButton.GetComponentInChildren<TMP_Text>();
            _sizeCycleButton.onClick.AddListener(CycleTileSize);

            _listingContent = MakeVerticalScroll(contentArea,
                                                 new Vector2(0f, 0.27f), new Vector2(1f, 0.81f),
                                                 Vector2.zero, Vector2.zero,
                                                 rowSpacing: 2f, scrollSensitivity: 120f);

            // Page navigation row just above the confirm strip
            var pagePrev = MakeButton(contentArea, "<",
                                      new Vector2(0.34f, 0.22f), new Vector2(0.40f, 0.27f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            pagePrev.name = "PageLeft";
            pagePrev.onClick.AddListener(() => { if (_page > 0) { _page--; Refresh(); } });
            _pageNumberText = MakeLabel(contentArea, "1 / 1",
                                        new Vector2(0.40f, 0.22f), new Vector2(0.60f, 0.27f),
                                        Vector2.zero, Vector2.zero,
                                        size: 13f, align: TextAlignmentOptions.Center,
                                        color: new Color(1f, 0.85f, 0.55f));
            var pageNext = MakeButton(contentArea, ">",
                                      new Vector2(0.60f, 0.22f), new Vector2(0.66f, 0.27f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            pageNext.name = "PageRight";
            pageNext.onClick.AddListener(() => { if (_page + 1 < _maxPage) { _page++; Refresh(); } });

            // AFTERPRESS confirm strip at the bottom — hidden until a row is selected.
            BuildConfirmPanel(contentArea);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.00f), new Vector2(1f, 0.05f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        private Button MakeTab(Transform parent, string name, string label, float xMin, float xMax, Tab id)
        {
            var btn = MakeButton(parent, label,
                                 new Vector2(xMin, 0.94f), new Vector2(xMax, 1f),
                                 Vector2.zero, Vector2.zero,
                                 bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            btn.name = name;
            btn.onClick.AddListener(() => { _currentTab = id; _selected = null; HideConfirmPanel(); Refresh(); });
            return btn;
        }

        private void BuildSearchField(Transform parent)
        {
            var go = new GameObject("SearchField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.68f, 0.94f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
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
            txt.fontSize = 14f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.Left;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(txt);

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onValueChanged.AddListener(_ => Refresh());
            _searchField = input;
        }

        private void BuildConfirmPanel(RectTransform contentArea)
        {
            _confirmPanel = new GameObject("ConfirmPanel", typeof(RectTransform), typeof(Image));
            _confirmPanel.transform.SetParent(contentArea, false);
            var rt = _confirmPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.06f); rt.anchorMax = new Vector2(1f, 0.21f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _confirmPanel.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.06f, 0.95f);

            _confirmItemName = MakeLabel(_confirmPanel.transform, "",
                                         new Vector2(0f, 0.55f), new Vector2(0.40f, 1f),
                                         new Vector2(12f, 0f), new Vector2(-4f, 0f),
                                         size: 14f, color: new Color(1f, 0.85f, 0.55f));

            // Quantity input
            MakeLabel(_confirmPanel.transform, "Quantity:",
                      new Vector2(0f, 0f), new Vector2(0.12f, 0.55f),
                      new Vector2(12f, 0f), Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.MidlineRight);
            _confirmQuantity = BuildConfirmInput(_confirmPanel.transform, "QuantityInput",
                                                 new Vector2(0.12f, 0.10f), new Vector2(0.24f, 0.50f));

            // Price input (sell-only)
            _confirmPriceLabel = MakeLabel(_confirmPanel.transform, "Price:",
                      new Vector2(0.26f, 0f), new Vector2(0.36f, 0.55f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.MidlineRight);
            _confirmPrice = BuildConfirmInput(_confirmPanel.transform, "PriceInput",
                                              new Vector2(0.36f, 0.10f), new Vector2(0.48f, 0.50f));

            // Currency dropdown (sell-only). On Buy/MyListings we re-use the listing's stored Currency.
            _currencyDropdownLabel = MakeLabel(_confirmPanel.transform, "Currency:",
                      new Vector2(0.49f, 0f), new Vector2(0.60f, 0.55f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.MidlineRight);
            _currencyDropdown = BuildCurrencyDropdown(_confirmPanel.transform,
                                                     new Vector2(0.60f, 0.10f), new Vector2(0.74f, 0.50f));

            // Total summary
            _confirmTotal = MakeLabel(_confirmPanel.transform, "",
                                      new Vector2(0.76f, 0f), new Vector2(0.99f, 0.55f),
                                      new Vector2(8f, 0f), new Vector2(-4f, 0f),
                                      size: 12f, align: TextAlignmentOptions.MidlineLeft,
                                      color: new Color(0.85f, 0.78f, 0.62f));

            // Action button
            _confirmActionButton = MakeButton(_confirmPanel.transform, "Confirm",
                                              new Vector2(0.80f, 0.10f), new Vector2(0.98f, 0.90f),
                                              Vector2.zero, Vector2.zero,
                                              bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 14f);
            _confirmActionLabel = _confirmActionButton.GetComponentInChildren<TMP_Text>();
            _confirmActionButton.onClick.AddListener(OnConfirmClicked);

            _confirmQuantity.onValueChanged.AddListener(_ => UpdateConfirmTotal());
            _confirmPrice.onValueChanged.AddListener(_ => UpdateConfirmTotal());
            _currencyDropdown.onValueChanged.AddListener(_ => UpdateConfirmTotal());
            _confirmPanel.SetActive(false);
        }

        /// <summary>Builds a TMP_Dropdown with the union of all loaded Banker profile currencies. KG sources
        /// this from <c>_possibleCurrencies</c> global config; we union per-profile <see cref="BankerProfile.CurrencyLimits"/>
        /// so the seller can list in any currency at least one bank accepts (plus "Coins" baseline).</summary>
        private TMP_Dropdown BuildCurrencyDropdown(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("CurrencyDropdown", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.92f);

            var dropdown = go.AddComponent<TMP_Dropdown>();

            // Label child (shows the current selection)
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 2f); lrt.offsetMax = new Vector2(-20f, -2f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 12f; label.color = new Color(0.96f, 0.91f, 0.76f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(label);

            // Arrow indicator
            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(go.transform, false);
            var art = arrowGo.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(1f, 0.5f); art.anchorMax = new Vector2(1f, 0.5f);
            art.pivot = new Vector2(1f, 0.5f);
            art.sizeDelta = new Vector2(20f, 20f); art.anchoredPosition = new Vector2(-4f, 0f);
            var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowText.text = "v"; arrowText.fontSize = 14f; arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.color = new Color(1f, 0.85f, 0.55f);
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(arrowText);

            // Template (the dropdown options popup)
            var template = new GameObject("Template", typeof(RectTransform), typeof(Image));
            template.transform.SetParent(go.transform, false);
            var trt = template.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 0f); trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 140f); trt.anchoredPosition = new Vector2(0f, -2f);
            template.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.06f, 0.98f);

            // Scroll inside template
            var scroll = template.AddComponent<ScrollRect>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.0f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0f, 28f);

            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            var irt = item.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(1f, 0.5f);
            irt.sizeDelta = new Vector2(0f, 26f);

            var itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(Image));
            itemBg.transform.SetParent(item.transform, false);
            var ibgRt = itemBg.GetComponent<RectTransform>();
            ibgRt.anchorMin = Vector2.zero; ibgRt.anchorMax = Vector2.one;
            ibgRt.offsetMin = Vector2.zero; ibgRt.offsetMax = Vector2.zero;
            itemBg.GetComponent<Image>().color = new Color(0.20f, 0.16f, 0.10f, 0.95f);

            var itemChecked = new GameObject("Item Checkmark", typeof(RectTransform), typeof(Image));
            itemChecked.transform.SetParent(item.transform, false);
            var icRt = itemChecked.GetComponent<RectTransform>();
            icRt.anchorMin = new Vector2(0f, 0.5f); icRt.anchorMax = new Vector2(0f, 0.5f);
            icRt.sizeDelta = new Vector2(20f, 20f); icRt.anchoredPosition = new Vector2(12f, 0f);
            itemChecked.GetComponent<Image>().color = new Color(1f, 0.85f, 0.55f);

            var itemLabel = new GameObject("Item Label", typeof(RectTransform));
            itemLabel.transform.SetParent(item.transform, false);
            var ilrt = itemLabel.GetComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(28f, 2f); ilrt.offsetMax = new Vector2(-4f, -2f);
            var itemLabelText = itemLabel.AddComponent<TextMeshProUGUI>();
            itemLabelText.fontSize = 12f; itemLabelText.color = new Color(0.96f, 0.91f, 0.76f);
            itemLabelText.alignment = TextAlignmentOptions.MidlineLeft;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(itemLabelText);

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBg.GetComponent<Image>();
            toggle.graphic = itemChecked.GetComponent<Image>();
            toggle.isOn = true;

            scroll.content = crt; scroll.viewport = vrt;
            scroll.horizontal = false; scroll.vertical = true;

            dropdown.template = trt;
            dropdown.captionText = label;
            dropdown.itemText = itemLabelText;
            template.SetActive(false);

            return dropdown;
        }

        private TMP_InputField BuildConfirmInput(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(6f, 2f); vrt.offsetMax = new Vector2(-6f, -2f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 13f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.Center;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(txt);

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            return input;
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _currentTab = Tab.Buy;
            _selected = null;
            HideConfirmPanel();
            Refresh();
        }

        private void Refresh()
        {
            HighlightTab(_buyTab, _currentTab == Tab.Buy);
            HighlightTab(_sellTab, _currentTab == Tab.Sell);
            HighlightTab(_myTab,  _currentTab == Tab.MyListings);

            for (int i = _listingContent.childCount - 1; i >= 0; i--)
                Destroy(_listingContent.GetChild(i).gameObject);

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

            // ── DIAG: dump row rendering state ──
            try
            {
                Debug.Log("[MP DIAG] rows.Count=" + rows.Count + " skip=" + skip + " take=" + take +
                          " listingContent.childCount=" + _listingContent.childCount +
                          " listingContent.sizeDelta=" + _listingContent.sizeDelta +
                          " listingContent.rect=" + _listingContent.rect);
                for (int i = 0; i < Mathf.Min(3, _listingContent.childCount); i++)
                {
                    var c = _listingContent.GetChild(i) as RectTransform;
                    if (c == null) continue;
                    Debug.Log("[MP DIAG]   child[" + i + "] name=" + c.name + " active=" + c.gameObject.activeInHierarchy +
                              " sizeDelta=" + c.sizeDelta + " anchoredPos=" + c.anchoredPosition +
                              " rect=" + c.rect + " labels=" + c.childCount);
                    var img = c.GetComponent<Image>();
                    if (img != null) Debug.Log("[MP DIAG]   child[" + i + "] image.color=" + img.color + " enabled=" + img.enabled);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MP DIAG] " + ex.Message); }

            // Highlight active category button.
            for (int i = 0; i < _categoryButtons.Length; i++)
                HighlightTab(_categoryButtons[i], (int)_category == i);
            _pageNumberText.text = (_page + 1) + " / " + _maxPage;

            _statusText.text = rows.Count + " listing(s)  -  Tab: " + _currentTab + "  -  Cat: " + _category;
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
            var rowGo = new GameObject("Listing_" + l.ItemPrefab, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            rowGo.transform.SetParent(_listingContent, false);
            rowGo.GetComponent<Image>().color = l == _selected
                ? new Color(0.32f, 0.22f, 0.14f, 0.95f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, RowHeightPx);
            rowGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                _selected = l;
                ShowConfirmPanelFor(l);
                Refresh();
            });

            float fs = RowFontSize;
            MakeLabel(rowGo.transform, l.ItemPrefab,           new Vector2(0.02f, 0f), new Vector2(0.40f, 1f), Vector2.zero, Vector2.zero, size: fs);
            MakeLabel(rowGo.transform, l.Count.ToString(),      new Vector2(0.42f, 0f), new Vector2(0.54f, 1f), Vector2.zero, Vector2.zero, size: fs, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, l.Price + " " + l.Currency, new Vector2(0.56f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero, size: fs, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(rowGo.transform, l.SellerName,            new Vector2(0.74f, 0f), new Vector2(0.99f, 1f), Vector2.zero, Vector2.zero, size: fs);
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
                // Repopulate dropdown options from the union of every loaded Banker profile.
                var currencies = BankerProfileManager.GetAllAcceptedCurrencies();
                _currencyDropdown.ClearOptions();
                var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
                foreach (var c in currencies) options.Add(new TMP_Dropdown.OptionData(c));
                _currencyDropdown.AddOptions(options);
                // Default to the listing's currency if it's in the list.
                int idx = currencies.ToList().FindIndex(c => string.Equals(c, l.Currency, System.StringComparison.OrdinalIgnoreCase));
                _currencyDropdown.value = idx >= 0 ? idx : 0;
                _currencyDropdown.RefreshShownValue();
            }

            _confirmActionLabel.text = _currentTab switch
            {
                Tab.MyListings => "Cancel",
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
                MarketplaceListingManager.RemoveListing(_profileName, l);
                _statusText.text = "Cancelled listing for " + l.ItemPrefab + ".";
                _selected = null; HideConfirmPanel(); Refresh();
                return;
            }
            if (_currentTab == Tab.Sell)
            {
                var player = Player.m_localPlayer; if (player == null) { _statusText.text = "No active player."; return; }
                var inv = player.GetInventory();
                if (qty <= 0 || price <= 0) { _statusText.text = "Quantity and price must be positive."; return; }
                if (!ConsumeFromInventory(inv, l.ItemPrefab, qty)) { _statusText.text = "Couldn't consume " + qty + "x " + l.ItemPrefab + " from inventory."; return; }
                string chosenCurrency = _currencyDropdown != null && _currencyDropdown.options.Count > 0
                    ? _currencyDropdown.options[_currencyDropdown.value].text
                    : l.Currency;
                MarketplaceListingManager.AddListing(_profileName, new MarketplaceListing
                {
                    Currency = chosenCurrency, Price = price,
                    ItemPrefab = l.ItemPrefab, Count = qty,
                    SellerName = l.SellerName, SellerId = l.SellerId,
                });
                _statusText.text = "Listed " + qty + "x " + l.ItemPrefab + " for " + price + " " + chosenCurrency + ".";
                _selected = null; HideConfirmPanel(); Refresh();
                return;
            }
            // Tab.Buy
            {
                var player = Player.m_localPlayer; if (player == null) { _statusText.text = "No active player."; return; }
                var inv = player.GetInventory();
                int totalCost = qty * l.Price;
                if (qty <= 0 || qty > l.Count) { _statusText.text = "Quantity must be 1.." + l.Count + "."; return; }
                int held = CountInInventory(inv, l.Currency);
                if (held < totalCost) { _statusText.text = "Need " + totalCost + " " + l.Currency + " (have " + held + ")."; return; }
                if (!ConsumeFromInventory(inv, l.Currency, totalCost)) { _statusText.text = "Failed to consume currency."; return; }
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(l.ItemPrefab) : null;
                if (prefab == null) { _statusText.text = "Unknown prefab '" + l.ItemPrefab + "'."; return; }
                var drop = prefab.GetComponent<ItemDrop>();
                if (drop != null && inv.CanAddItem(drop.m_itemData, qty)) { inv.AddItem(l.ItemPrefab, qty, 1, 0, 0L, ""); }
                else
                {
                    var go = Object.Instantiate(prefab, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
                    var dr = go.GetComponent<ItemDrop>(); if (dr != null) dr.m_itemData.m_stack = qty;
                }
                // Decrement or remove the listing
                if (qty >= l.Count) MarketplaceListingManager.RemoveListing(_profileName, l);
                else l.Count -= qty;
                _statusText.text = "Bought " + qty + "x " + l.ItemPrefab + " for " + totalCost + " " + l.Currency + ".";
                _selected = null; HideConfirmPanel(); Refresh();
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

        // ─── KG-parity sort + size cycle ─────────────────────────────────

        /// <summary>Builds a column-header that acts as a sort toggle. Visual: glyph (⇧/⇩) gets
        /// appended to the active column's label so the player can read the current sort at a glance.</summary>
        private TMP_Text MakeSortHeader(Transform parent, string label, SortBy mode, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align)
        {
            var btn = MakeButton(parent, label,
                                 anchorMin, anchorMax,
                                 Vector2.zero, Vector2.zero,
                                 bgColor: new Color(0f, 0f, 0f, 0f), fontSize: 13f);
            btn.name = "Sort" + mode;
            var txt = btn.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.color = new Color(1f, 0.85f, 0.55f);
                txt.alignment = align;
            }
            btn.onClick.AddListener(() => ChangeSortBy(mode));
            return txt;
        }

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
