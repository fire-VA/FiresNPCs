using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Transmog UI: left pane shows 4 armor slots (helmet/chest/legs/cape) with currently
    /// transmogged appearance, right pane lists available transmogs filterable by slot. Click in left =
    /// pick the slot, click in right = apply that transmog visual to the picked slot.
    /// v1 reads available transmogs from <see cref="TransmogProfileManager"/>; the actual armor-prefab
    /// visual swap lands when the transmog ItemDataComponent is wired in a follow-up.
    /// </summary>
    public class TransmogPlayerPanel : BasePlayerModulePanel
    {
        private static TransmogPlayerPanel _instance;
        protected override string PanelTitle => "Transmog";
        protected override Vector2 PanelSize => new Vector2(820f, 540f);

        // Slot categories — match Valheim's armor slot scheme.
        private enum Slot { Helmet, Chest, Legs, Cape }
        private Slot _selectedSlot = Slot.Helmet;
        private static string LocalName => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
        private static string SlotKey(Slot s) => s switch
        {
            Slot.Helmet => TransmogState.SlotHelmet,
            Slot.Chest => TransmogState.SlotChest,
            Slot.Legs => TransmogState.SlotLegs,
            Slot.Cape => TransmogState.SlotCape,
            _ => "",
        };

        private string _profileName;
        private RectTransform _availableContent;
        private TMP_Text _statusText;
        private readonly Dictionary<Slot, TMP_Text> _slotLabels = new();
        private readonly Dictionary<Slot, Image> _slotBgs = new();

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
            // Left: slot grid (40%)
            MakeLabel(contentArea, "Your Armor Slots",
                      new Vector2(0f, 0.94f), new Vector2(0.40f, 1f),
                      new Vector2(8f, 0f), Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));

            BuildSlotCell(contentArea, Slot.Helmet, "Helmet", 0.02f, 0.20f, 0.66f, 0.92f);
            BuildSlotCell(contentArea, Slot.Chest,  "Chest",  0.22f, 0.40f, 0.66f, 0.92f);
            BuildSlotCell(contentArea, Slot.Legs,   "Legs",   0.02f, 0.20f, 0.36f, 0.62f);
            BuildSlotCell(contentArea, Slot.Cape,   "Cape",   0.22f, 0.40f, 0.36f, 0.62f);

            var clearBtn = MakeButton(contentArea, "Clear Slot",
                                      new Vector2(0.02f, 0.22f), new Vector2(0.40f, 0.30f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.4f, 0.18f, 0.10f, 0.95f), fontSize: 13f);
            clearBtn.onClick.AddListener(OnClearSlotClicked);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0f, 0.02f), new Vector2(0.40f, 0.20f),
                                    new Vector2(8f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));

            // Right: available transmogs (60%)
            MakeLabel(contentArea, "Available Transmogs",
                      new Vector2(0.42f, 0.94f), new Vector2(1f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));

            // Column header
            MakePanel(contentArea, "ColHeader", new Color(0.05f, 0.04f, 0.02f, 0.95f),
                      new Vector2(0.42f, 0.86f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);
            MakeLabel(contentArea, "Appearance", new Vector2(0.43f, 0.86f), new Vector2(0.65f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "Price",      new Vector2(0.65f, 0.86f), new Vector2(0.85f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));
            MakeLabel(contentArea, "",           new Vector2(0.85f, 0.86f), new Vector2(0.99f, 0.92f), Vector2.zero, Vector2.zero, size: 12f, color: new Color(1f, 0.85f, 0.55f));

            _availableContent = MakeVerticalScroll(contentArea,
                                                   new Vector2(0.42f, 0.02f), new Vector2(1f, 0.85f),
                                                   Vector2.zero, Vector2.zero,
                                                   rowSpacing: 2f, scrollSensitivity: 120f);
        }

        private void BuildSlotCell(RectTransform parent, Slot slot, string label, float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject("Slot_" + slot, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.10f, 0.08f, 0.05f, 0.92f);
            _slotBgs[slot] = img;
            go.GetComponent<Button>().onClick.AddListener(() => { _selectedSlot = slot; Refresh(); });

            MakeLabel(go.transform, label,
                      new Vector2(0f, 0.75f), new Vector2(1f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, align: TextAlignmentOptions.Center,
                      color: new Color(1f, 0.85f, 0.55f));
            var txt = MakeLabel(go.transform, "(default)",
                                new Vector2(0f, 0.05f), new Vector2(1f, 0.70f),
                                Vector2.zero, Vector2.zero,
                                size: 13f, align: TextAlignmentOptions.Center);
            _slotLabels[slot] = txt;
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selectedSlot = Slot.Helmet;
            Refresh();
        }

        private void Refresh()
        {
            // Highlight selected slot
            foreach (var kv in _slotBgs)
            {
                kv.Value.color = kv.Key == _selectedSlot
                    ? new Color(0.32f, 0.22f, 0.10f, 0.95f)
                    : new Color(0.10f, 0.08f, 0.05f, 0.92f);
            }

            // Slot label content reflects picked appearance
            foreach (var kv in _slotLabels)
            {
                var pick = TransmogState.Get(LocalName, SlotKey(kv.Key));
                kv.Value.text = string.IsNullOrEmpty(pick) ? "(default)" : pick;
            }

            // Rebuild available list for the selected slot
            for (int i = _availableContent.childCount - 1; i >= 0; i--)
                Destroy(_availableContent.GetChild(i).gameObject);

            var available = TransmogProfileManager.GetForSlot(_profileName, _selectedSlot.ToString());
            if (available.Count == 0)
            {
                _statusText.text = "No transmogs for " + _selectedSlot
                                   + " under profile '" + _profileName + "'. Drop a .cfg.";
                return;
            }
            foreach (var t in available) BuildAvailableRow(t);
            _statusText.text = "Slot: " + _selectedSlot + "  -  " + available.Count + " option(s)";
        }

        private void BuildAvailableRow(TransmogOption t)
        {
            var rowGo = new GameObject("Trans_" + t.Prefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_availableContent, false);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.85f);
            rowGo.GetComponent<LayoutElement>().minHeight = 28f;

            MakeLabel(rowGo.transform, t.Prefab,                          new Vector2(0.01f, 0f), new Vector2(0.55f, 1f), Vector2.zero, Vector2.zero, size: 13f);
            MakeLabel(rowGo.transform, t.PricePrefab + " x" + t.PriceAmount, new Vector2(0.56f, 0f), new Vector2(0.83f, 1f), Vector2.zero, Vector2.zero, size: 13f, align: TextAlignmentOptions.Center, color: new Color(1f, 0.85f, 0.55f));

            var applyBtn = MakeButton(rowGo.transform, "Apply",
                                      new Vector2(0.84f, 0.15f), new Vector2(0.99f, 0.85f),
                                      Vector2.zero, Vector2.zero,
                                      bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 12f);
            applyBtn.onClick.AddListener(() =>
            {
                TransmogState.Set(LocalName, SlotKey(_selectedSlot), t.Prefab);
                _statusText.text = "Applied " + t.Prefab + " to " + _selectedSlot + ".";
                Refresh();
            });
        }

        private void OnClearSlotClicked()
        {
            TransmogState.Set(LocalName, SlotKey(_selectedSlot), null);
            _statusText.text = "Cleared transmog from " + _selectedSlot + ".";
            Refresh();
        }
    }
}
