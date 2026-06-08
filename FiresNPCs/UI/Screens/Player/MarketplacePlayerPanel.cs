using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Marketplace UI (auction house). Three tabs — Buy / Sell / My Listings — over a single
    /// item list area. v1 reads sample listings from <see cref="MarketplaceListingManager"/>; persistence /
    /// player-to-player listing creation lands in a follow-up alongside LiteDB MarketplaceRepository.
    /// </summary>
    public class MarketplacePlayerPanel : BasePlayerModulePanel
    {
        private static MarketplacePlayerPanel _instance;
        protected override string PanelTitle => "Marketplace";
        protected override Vector2 PanelSize => new Vector2(880f, 560f);

        private enum Tab { Buy, Sell, MyListings }
        private Tab _currentTab = Tab.Buy;

        private string _profileName;
        private RectTransform _listingContent;
        private TMP_Text _statusText;
        private Button _buyTab, _sellTab, _myTab;
        private TMP_InputField _searchField;

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
            // Tab bar
            _buyTab = MakeTab(contentArea, "Buy", 0.00f, 0.16f, Tab.Buy);
            _sellTab = MakeTab(contentArea, "Sell", 0.17f, 0.33f, Tab.Sell);
            _myTab  = MakeTab(contentArea, "My Listings", 0.34f, 0.55f, Tab.MyListings);

            // Search bar
            MakeLabel(contentArea, "Search:",
                      new Vector2(0.58f, 0.94f), new Vector2(0.68f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.MidlineRight);
            BuildSearchField(contentArea);

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0f, 0.86f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "Item",     new Vector2(0.02f, 0.86f), new Vector2(0.40f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Qty",      new Vector2(0.42f, 0.86f), new Vector2(0.54f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Price",    new Vector2(0.56f, 0.86f), new Vector2(0.72f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Seller",   new Vector2(0.74f, 0.86f), new Vector2(0.88f, 0.92f), Vector2.zero, Vector2.zero, size: 13f, color: new Color(1f, 0.85f, 0.55f));

            _listingContent = MakeVerticalScroll(contentArea,
                                                 new Vector2(0f, 0.06f), new Vector2(1f, 0.85f),
                                                 Vector2.zero, Vector2.zero,
                                                 rowSpacing: 2f, scrollSensitivity: 120f);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.00f), new Vector2(1f, 0.06f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        private Button MakeTab(Transform parent, string label, float xMin, float xMax, Tab id)
        {
            var btn = MakeButton(parent, label,
                                 new Vector2(xMin, 0.94f), new Vector2(xMax, 1f),
                                 Vector2.zero, Vector2.zero,
                                 bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            btn.onClick.AddListener(() => { _currentTab = id; Refresh(); });
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

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _currentTab = Tab.Buy;
            Refresh();
        }

        private void Refresh()
        {
            // Visual tab-active treatment
            HighlightTab(_buyTab, _currentTab == Tab.Buy);
            HighlightTab(_sellTab, _currentTab == Tab.Sell);
            HighlightTab(_myTab,  _currentTab == Tab.MyListings);

            for (int i = _listingContent.childCount - 1; i >= 0; i--)
                Destroy(_listingContent.GetChild(i).gameObject);

            string filter = _searchField?.text?.Trim().ToLowerInvariant() ?? "";
            IReadOnlyList<MarketplaceListing> rows = _currentTab switch
            {
                Tab.Buy        => MarketplaceListingManager.GetListings(false, _profileName, filter),
                Tab.Sell       => MarketplaceListingManager.GetSellPrompts(_profileName, filter),
                Tab.MyListings => MarketplaceListingManager.GetListings(true, _profileName, filter),
                _ => new List<MarketplaceListing>(),
            };

            foreach (var listing in rows) BuildRow(listing);
            ReflowScroll(_listingContent);

            _statusText.text = rows.Count + " listing(s)  -  Tab: " + _currentTab;
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
            var rowGo = new GameObject("Listing_" + l.ItemPrefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_listingContent, false);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 30f);

            MakeLabel(rowGo.transform, l.ItemPrefab,           new Vector2(0.02f, 0f), new Vector2(0.40f, 1f), Vector2.zero, Vector2.zero, size: 13f);
            MakeLabel(rowGo.transform, l.Count.ToString(),      new Vector2(0.42f, 0f), new Vector2(0.54f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center);
            MakeLabel(rowGo.transform, l.Price + " " + l.Currency, new Vector2(0.56f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(rowGo.transform, l.SellerName,            new Vector2(0.74f, 0f), new Vector2(0.88f, 1f), Vector2.zero, Vector2.zero, size: 13f);

            string btnLabel = _currentTab switch { Tab.MyListings => "Cancel", Tab.Sell => "List", _ => "Buy" };
            Color btnColor = _currentTab switch
            {
                Tab.MyListings => new Color(0.4f, 0.18f, 0.10f, 0.95f),
                Tab.Sell       => new Color(0.42f, 0.30f, 0.15f, 0.95f),
                _              => new Color(0.22f, 0.42f, 0.22f, 0.95f),
            };
            var actionBtn = MakeButton(rowGo.transform, btnLabel,
                                       new Vector2(0.89f, 0.15f), new Vector2(0.99f, 0.85f),
                                       Vector2.zero, Vector2.zero, bgColor: btnColor, fontSize: 12f);
            actionBtn.onClick.AddListener(() =>
            {
                if (_currentTab == Tab.MyListings)
                {
                    MarketplaceListingManager.RemoveListing(_profileName, l);
                    _statusText.text = "Cancelled listing for " + l.ItemPrefab + ".";
                    Refresh();
                    return;
                }
                if (_currentTab == Tab.Sell)
                {
                    var sellPlayer = Player.m_localPlayer; if (sellPlayer == null) { _statusText.text = "No active player."; return; }
                    var sellInv = sellPlayer.GetInventory();
                    if (!ConsumeFromInventory(sellInv, l.ItemPrefab, l.Count))
                    { _statusText.text = "Couldn't consume " + l.ItemPrefab + " from inventory."; return; }
                    MarketplaceListingManager.AddListing(_profileName, new MarketplaceListing
                    {
                        Currency = l.Currency, Price = l.Price,
                        ItemPrefab = l.ItemPrefab, Count = l.Count,
                        SellerName = l.SellerName, SellerId = l.SellerId,
                    });
                    _statusText.text = "Listed " + l.Count + "x " + l.ItemPrefab + " for " + l.Price + " " + l.Currency + ".";
                    Refresh();
                    return;
                }
                var player = Player.m_localPlayer; if (player == null) { _statusText.text = "No active player."; return; }
                var inv = player.GetInventory();
                int held = CountInInventory(inv, l.Currency);
                if (held < l.Price) { _statusText.text = "Need " + l.Price + " " + l.Currency + " (have " + held + ")."; return; }
                if (!ConsumeFromInventory(inv, l.Currency, l.Price)) { _statusText.text = "Failed to consume currency."; return; }
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(l.ItemPrefab) : null;
                if (prefab == null) { _statusText.text = "Unknown prefab '" + l.ItemPrefab + "'."; return; }
                var drop = prefab.GetComponent<ItemDrop>();
                if (drop != null && inv.CanAddItem(drop.m_itemData, l.Count))
                {
                    inv.AddItem(l.ItemPrefab, l.Count, 1, 0, 0L, "");
                }
                else
                {
                    var go = Object.Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
                    var dr = go.GetComponent<ItemDrop>(); if (dr != null) dr.m_itemData.m_stack = l.Count;
                }
                MarketplaceListingManager.RemoveListing(_profileName, l);
                _statusText.text = "Bought " + l.Count + "x " + l.ItemPrefab + " for " + l.Price + " " + l.Currency + ".";
                Refresh();
            });
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
    }
}
