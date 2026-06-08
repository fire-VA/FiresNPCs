using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Transmog — port of KG's MarketplaceTransmogUI flow:
    ///   • LEFT pane: the player's inventory filtered to transmoggable item types (KG's AvailableTypes).
    ///     Items already transmogged render in red. Click an item to select it; click again to deselect.
    ///   • RIGHT pane: available transmogs from the loaded profile, filtered to the SELECTED ITEM'S TYPE.
    ///     Each row shows name + price + Apply button (only enabled if the player can afford). The "header"
    ///     of each type-group is colored with KG's per-type color palette.
    ///   • Reverse Transmog button clears the override on the currently-selected item.
    /// </summary>
    public class TransmogPlayerPanel : BasePlayerModulePanel
    {
        private static TransmogPlayerPanel _instance;
        protected override string PanelTitle => "Transmog";
        protected override Vector2 PanelSize => new Vector2(960f, 600f);

        private string _profileName;
        private RectTransform _inventoryContent;
        private RectTransform _availableContent;
        private TMP_Text _selectedNameText;
        private TMP_Text _statusText;
        private Button _reverseButton;
        private ItemDrop.ItemData _selectedItem;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresTransmogPanel");
                _instance = go.AddComponent<TransmogPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        protected override void BuildContent(RectTransform contentArea)
        {
            // ── LEFT (40%): "Your Items" inventory list + selected display + Reverse button ──
            MakeLabel(contentArea, "Your Items (transmoggable)",
                      new Vector2(0f, 0.93f), new Vector2(0.42f, 1f),
                      new Vector2(12f, 0f), Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));

            _inventoryContent = MakeVerticalScroll(contentArea,
                                                   new Vector2(0f, 0.24f), new Vector2(0.42f, 0.92f),
                                                   new Vector2(12f, 0f), new Vector2(-4f, 0f),
                                                   rowSpacing: 2f, scrollSensitivity: 120f);

            // Selected item box
            MakePanel(contentArea, "ChoosenItem", new Color(0.10f, 0.08f, 0.05f, 0.92f),
                      new Vector2(0f, 0.10f), new Vector2(0.42f, 0.22f),
                      new Vector2(12f, 0f), new Vector2(-4f, 0f));
            MakeLabel(contentArea, "Selected:",
                      new Vector2(0f, 0.16f), new Vector2(0.42f, 0.22f),
                      new Vector2(20f, 0f), new Vector2(-12f, 0f),
                      size: 12f, color: new Color(0.96f, 0.91f, 0.76f));
            _selectedNameText = MakeLabel(contentArea, "(none)",
                                          new Vector2(0f, 0.10f), new Vector2(0.42f, 0.16f),
                                          new Vector2(20f, 0f), new Vector2(-12f, 0f),
                                          size: 16f, color: new Color(1f, 0.85f, 0.55f));

            // Reverse button (clears the transmog override on the selected item)
            _reverseButton = MakeButton(contentArea, "Reverse Transmog",
                                        new Vector2(0f, 0.02f), new Vector2(0.42f, 0.09f),
                                        new Vector2(12f, 0f), new Vector2(-4f, 0f),
                                        bgColor: new Color(0.40f, 0.18f, 0.10f, 0.95f), fontSize: 13f);
            _reverseButton.onClick.AddListener(OnReverseClicked);

            // ── RIGHT (58%): Available transmogs grouped by item type ──
            MakeLabel(contentArea, "Available Transmogs",
                      new Vector2(0.44f, 0.93f), new Vector2(1f, 1f),
                      new Vector2(4f, 0f), new Vector2(-12f, 0f),
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));

            var hdr = MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                                new Vector2(0.44f, 0.86f), new Vector2(1f, 0.92f),
                                new Vector2(4f, 0f), new Vector2(-12f, 0f));
            MakeLabel(contentArea, "Appearance", new Vector2(0.45f, 0.86f), new Vector2(0.66f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Price",      new Vector2(0.66f, 0.86f), new Vector2(0.88f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "",           new Vector2(0.88f, 0.86f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, color: new Color(1f, 0.85f, 0.55f));

            _availableContent = MakeVerticalScroll(contentArea,
                                                   new Vector2(0.44f, 0.08f), new Vector2(1f, 0.85f),
                                                   new Vector2(4f, 0f), new Vector2(-12f, 0f),
                                                   rowSpacing: 2f, scrollSensitivity: 120f);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0.44f, 0f), new Vector2(1f, 0.06f),
                                    new Vector2(4f, 0f), new Vector2(-12f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selectedItem = null;
            Refresh();
        }

        private void Refresh()
        {
            RefreshInventory();
            RefreshAvailable();
        }

        // ─── LEFT pane ───

        private void RefreshInventory()
        {
            for (int i = _inventoryContent.childCount - 1; i >= 0; i--)
                Destroy(_inventoryContent.GetChild(i).gameObject);

            var player = Player.m_localPlayer;
            if (player == null)
            {
                _selectedNameText.text = "(no player)";
                return;
            }
            string playerName = player.GetPlayerName();
            // Filter to KG's AvailableTypes, sort transmogged-first then by name.
            var items = player.GetInventory().GetAllItems()
                .Where(it => it != null && it.m_dropPrefab != null
                          && TransmogState.AvailableTypes.Contains(it.m_shared.m_itemType.ToString()))
                .OrderByDescending(it => TransmogState.HasTransmog(playerName, it.m_dropPrefab.name))
                .ThenBy(it => it.m_shared.m_name)
                .ToList();

            if (items.Count == 0)
            {
                MakeLabel(_inventoryContent, "(no transmoggable items in inventory)",
                          new Vector2(0f, 0f), new Vector2(1f, 1f),
                          new Vector2(8f, 8f), new Vector2(-8f, -8f),
                          size: 12f, align: TextAlignmentOptions.Center,
                          color: new Color(0.65f, 0.60f, 0.50f));
                ReflowScroll(_inventoryContent);
                return;
            }
            foreach (var it in items) BuildInventoryRow(it, playerName);
            ReflowScroll(_inventoryContent);

            if (_selectedItem != null)
            {
                _selectedNameText.text = _selectedItem.m_shared.m_name + "  (" + _selectedItem.m_shared.m_itemType + ")";
                _reverseButton.interactable = TransmogState.HasTransmog(playerName, _selectedItem.m_dropPrefab.name);
            }
            else
            {
                _selectedNameText.text = "(none selected)";
                _reverseButton.interactable = false;
            }
        }

        private void BuildInventoryRow(ItemDrop.ItemData item, string playerName)
        {
            string baseName = item.m_dropPrefab.name;
            bool hasTransmog = TransmogState.HasTransmog(playerName, baseName);
            bool isSelected = _selectedItem == item;

            var rowGo = new GameObject("Inv_" + baseName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            rowGo.transform.SetParent(_inventoryContent, false);
            rowGo.GetComponent<Image>().color = isSelected
                ? new Color(0.10f, 0.45f, 0.10f, 0.95f)
                : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 28f);
            rowGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                _selectedItem = _selectedItem == item ? null : item;
                Refresh();
            });

            // KG colors transmogged items in red, untransmogged in white.
            var nameColor = hasTransmog
                ? new Color(1f, 0.31f, 0.31f)
                : new Color(0.96f, 0.91f, 0.76f);
            MakeLabel(rowGo.transform, item.m_shared.m_name,
                      new Vector2(0.02f, 0f), new Vector2(0.85f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, color: nameColor);
            MakeLabel(rowGo.transform, "x" + item.m_stack,
                      new Vector2(0.85f, 0f), new Vector2(0.99f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 12f, align: TextAlignmentOptions.MidlineRight,
                      color: new Color(0.85f, 0.78f, 0.62f));
        }

        // ─── RIGHT pane ───

        private void RefreshAvailable()
        {
            for (int i = _availableContent.childCount - 1; i >= 0; i--)
                Destroy(_availableContent.GetChild(i).gameObject);

            // No selection → show all transmogs (header per type) without Apply buttons.
            // Selection → show only matching-type transmogs with Apply buttons.
            string targetType = _selectedItem != null ? _selectedItem.m_shared.m_itemType.ToString() : null;

            var pool = TransmogProfileManager.GetAllForProfile(_profileName);
            if (pool == null || pool.Count == 0)
            {
                _statusText.text = "No transmogs loaded for profile '" + _profileName + "'. Drop a .cfg into BepInEx\\config\\FiresRPGmaker\\Transmogrifications\\ and run firesnpcs_reload transmog.";
                ReflowScroll(_availableContent);
                return;
            }

            // Group by Slot (which is item type).
            var grouped = pool.GroupBy(t => t.Slot ?? "Helmet")
                              .OrderBy(g => System.Array.IndexOf(TransmogState.AvailableTypes, g.Key));
            int shownCount = 0;
            foreach (var group in grouped)
            {
                bool typeMatchesSelection = targetType == null
                                          || string.Equals(group.Key, targetType, System.StringComparison.OrdinalIgnoreCase);
                if (targetType != null && !typeMatchesSelection) continue;

                BuildTypeHeaderRow(group.Key);
                foreach (var t in group)
                {
                    BuildAvailableRow(t, typeMatchesSelection);
                    shownCount++;
                }
            }
            ReflowScroll(_availableContent);
            _statusText.text = (_selectedItem == null)
                ? "Click an inventory item to filter + apply transmogs."
                : "Type: " + targetType + " — " + shownCount + " option(s)";
        }

        private void BuildTypeHeaderRow(string typeName)
        {
            var rowGo = new GameObject("TypeHdr_" + typeName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_availableContent, false);
            var color = TransmogState.TypeColors.TryGetValue(typeName, out var c) ? c : Color.gray;
            rowGo.GetComponent<Image>().color = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.95f);
            SetRowHeight(rowGo, 22f);
            MakeLabel(rowGo.transform, typeName,
                      new Vector2(0f, 0f), new Vector2(1f, 1f),
                      new Vector2(8f, 0f), new Vector2(-8f, 0f),
                      size: 12f, align: TextAlignmentOptions.MidlineLeft,
                      color: new Color(1f, 1f, 1f));
        }

        private void BuildAvailableRow(TransmogOption t, bool actionable)
        {
            var rowGo = new GameObject("Trans_" + t.Prefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_availableContent, false);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 26f);

            MakeLabel(rowGo.transform, t.Prefab,
                      new Vector2(0.02f, 0f), new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero, size: 13f);
            MakeLabel(rowGo.transform, t.PricePrefab + " x" + t.PriceAmount,
                      new Vector2(0.56f, 0f), new Vector2(0.85f, 1f), Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center,
                      color: new Color(1f, 0.85f, 0.55f));

            if (!actionable) return;

            var applyBtn = MakeButton(rowGo.transform, "Apply",
                                      new Vector2(0.86f, 0.12f), new Vector2(0.99f, 0.88f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 12f);
            applyBtn.onClick.AddListener(() => OnApplyClicked(t));
        }

        // ─── actions ───

        private void OnApplyClicked(TransmogOption t)
        {
            var player = Player.m_localPlayer; if (player == null) return;
            if (_selectedItem == null) { _statusText.text = "Select an item first."; return; }
            var inv = player.GetInventory();
            int held = CountInInventory(inv, t.PricePrefab);
            if (held < t.PriceAmount) { _statusText.text = "Need " + t.PriceAmount + " " + t.PricePrefab + " (have " + held + ")."; return; }
            if (!ConsumeFromInventory(inv, t.PricePrefab, t.PriceAmount)) { _statusText.text = "Failed to consume currency."; return; }

            TransmogState.Set(player.GetPlayerName(), _selectedItem.m_dropPrefab.name, t.Prefab);
            _statusText.text = "Applied " + t.Prefab + " to " + _selectedItem.m_shared.m_name + ".";
            Refresh();
        }

        private void OnReverseClicked()
        {
            var player = Player.m_localPlayer; if (player == null || _selectedItem == null) return;
            TransmogState.Set(player.GetPlayerName(), _selectedItem.m_dropPrefab.name, null);
            _statusText.text = "Reverted " + _selectedItem.m_shared.m_name + " to default.";
            Refresh();
        }

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
