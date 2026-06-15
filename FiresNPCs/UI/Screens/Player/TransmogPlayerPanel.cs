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
        protected override string PrefabName => "FiresTransmogPanel.prefab";

        private string _profileName;
        private RectTransform _inventoryContent;
        private GameObject _invRowTemplate;
        private GameObject _invEmptyLabel;
        private RectTransform _availableContent;
        private GameObject _typeHeaderTemplate;
        private GameObject _availRowTemplate;
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

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            _inventoryContent  = FindRect(t, "InvScroll/Viewport/Content");
            _invRowTemplate    = Find(t, "InvRowTemplate");
            if (_invRowTemplate != null) _invRowTemplate.SetActive(false);
            EnsureReflowMeta(_inventoryContent, rowSpacing: 2f, padding: 6f);
            _invEmptyLabel = Find(t, "InvEmptyLabel");
            if (_invEmptyLabel != null) _invEmptyLabel.SetActive(false);

            _selectedNameText = FindTmp(t, "SelectedNameText");
            _reverseButton = FindBtn(t, "ReverseButton");
            if (_reverseButton != null) { _reverseButton.onClick.RemoveAllListeners(); _reverseButton.onClick.AddListener(OnReverseClicked); }

            _availableContent   = FindRect(t, "AvailScroll/Viewport/Content");
            _typeHeaderTemplate = Find(t, "TypeHeaderTemplate");
            _availRowTemplate   = Find(t, "AvailRowTemplate");
            if (_typeHeaderTemplate != null) _typeHeaderTemplate.SetActive(false);
            if (_availRowTemplate != null) _availRowTemplate.SetActive(false);
            EnsureReflowMeta(_availableContent, rowSpacing: 2f, padding: 6f);

            _statusText = FindTmp(t, "StatusText");
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
            if (_inventoryContent == null || _invRowTemplate == null) return;
            ClearClones(_inventoryContent, _invRowTemplate);

            var player = Player.m_localPlayer;
            if (player == null)
            {
                if (_selectedNameText != null) _selectedNameText.text = "(no player)";
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

            if (_invEmptyLabel != null) _invEmptyLabel.SetActive(items.Count == 0);
            if (items.Count == 0)
            {
                ReflowScroll(_inventoryContent);
                return;
            }
            foreach (var it in items) BuildInventoryRow(it, playerName);
            ReflowScroll(_inventoryContent);

            if (_selectedItem != null)
            {
                string sn = Localization.instance != null
                    ? Localization.instance.Localize(_selectedItem.m_shared.m_name)
                    : _selectedItem.m_shared.m_name;
                if (_selectedNameText != null) _selectedNameText.text = sn + "  (" + _selectedItem.m_shared.m_itemType + ")";
                if (_reverseButton != null) _reverseButton.interactable = TransmogState.HasTransmog(playerName, _selectedItem.m_dropPrefab.name);
            }
            else
            {
                if (_selectedNameText != null) _selectedNameText.text = "(none selected)";
                if (_reverseButton != null) _reverseButton.interactable = false;
            }
        }

        private void BuildInventoryRow(ItemDrop.ItemData item, string playerName)
        {
            string baseName = item.m_dropPrefab.name;
            bool hasTransmog = TransmogState.HasTransmog(playerName, baseName);
            bool isSelected = _selectedItem == item;

            var rowGo = Instantiate(_invRowTemplate, _inventoryContent);
            rowGo.name = "Inv_" + baseName;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 28f);
            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = isSelected ? new Color(0.10f, 0.45f, 0.10f, 0.95f) : new Color(0.10f, 0.08f, 0.05f, 0.85f);
            var btn = rowGo.GetComponent<Button>();
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => { _selectedItem = _selectedItem == item ? null : item; Refresh(); }); }

            // KG colors transmogged items in red, untransmogged in white. m_shared.m_name is a $localization
            // key — resolve through Valheim's Localization service for a readable name.
            var nameColor = hasTransmog ? new Color(1f, 0.31f, 0.31f) : new Color(0.96f, 0.91f, 0.76f);
            string displayName = Localization.instance != null ? Localization.instance.Localize(item.m_shared.m_name) : item.m_shared.m_name;
            var nameLabel = FindTmp(rowGo.transform, "NameLabel");
            if (nameLabel != null) { nameLabel.text = displayName; nameLabel.color = nameColor; }
            var qtyLabel = FindTmp(rowGo.transform, "QtyLabel");
            if (qtyLabel != null) qtyLabel.text = "x" + item.m_stack;
        }

        // ─── RIGHT pane ───

        private void RefreshAvailable()
        {
            if (_availableContent == null || _typeHeaderTemplate == null || _availRowTemplate == null) return;
            ClearClones(_availableContent, _typeHeaderTemplate, _availRowTemplate);

            // No selection → show all transmogs (header per type) without Apply buttons.
            // Selection → show only matching-type transmogs with Apply buttons.
            string targetType = _selectedItem != null ? _selectedItem.m_shared.m_itemType.ToString() : null;

            var pool = TransmogProfileManager.GetAllForProfile(_profileName);
            if (pool == null || pool.Count == 0)
            {
                if (_statusText != null) _statusText.text = "No transmogs loaded for profile '" + _profileName + "'. Drop a .cfg into BepInEx\\config\\FiresRPGmaker\\Transmogrifications\\ and run firesnpcs_reload transmog.";
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
            if (_statusText != null) _statusText.text = (_selectedItem == null)
                ? "Click an inventory item to filter + apply transmogs."
                : "Type: " + targetType + " — " + shownCount + " option(s)";
        }

        private void BuildTypeHeaderRow(string typeName)
        {
            var rowGo = Instantiate(_typeHeaderTemplate, _availableContent);
            rowGo.name = "TypeHdr_" + typeName;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 22f);
            var color = TransmogState.TypeColors.TryGetValue(typeName, out var c) ? c : Color.gray;
            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.95f);
            var label = FindTmp(rowGo.transform, "TypeLabel");
            if (label != null) label.text = typeName;
        }

        private void BuildAvailableRow(TransmogOption t, bool actionable)
        {
            var rowGo = Instantiate(_availRowTemplate, _availableContent);
            rowGo.name = "Trans_" + t.Prefab;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 26f);

            var nameLabel = FindTmp(rowGo.transform, "NameLabel");
            if (nameLabel != null) nameLabel.text = t.Prefab;
            var priceLabel = FindTmp(rowGo.transform, "PriceLabel");
            if (priceLabel != null) priceLabel.text = t.PricePrefab + " x" + t.PriceAmount;

            // The Apply button only acts when the row's type matches the selected item; otherwise hide it.
            var applyBtn = FindBtn(rowGo.transform, "ApplyButton");
            if (applyBtn != null)
            {
                applyBtn.gameObject.SetActive(actionable);
                if (actionable) { applyBtn.onClick.RemoveAllListeners(); applyBtn.onClick.AddListener(() => OnApplyClicked(t)); }
            }
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
            string itemDisp = Localization.instance != null ? Localization.instance.Localize(_selectedItem.m_shared.m_name) : _selectedItem.m_shared.m_name;
            _statusText.text = "Applied " + t.Prefab + " to " + itemDisp + ".";
            // TransmogState.Set already calls RefreshLocalEquipment (which routes through SetupEquipment).
            Refresh();
        }

        private void OnReverseClicked()
        {
            var player = Player.m_localPlayer; if (player == null || _selectedItem == null) return;
            TransmogState.Set(player.GetPlayerName(), _selectedItem.m_dropPrefab.name, null);
            string itemDisp = Localization.instance != null ? Localization.instance.Localize(_selectedItem.m_shared.m_name) : _selectedItem.m_shared.m_name;
            _statusText.text = "Reverted " + itemDisp + " to default.";
            try { player.GetComponent<VisEquipment>()?.UpdateEquipmentVisuals(); } catch { }
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
