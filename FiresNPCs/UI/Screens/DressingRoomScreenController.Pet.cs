using System.Collections.Generic;
using FiresRPGmaker.Modules.Pets;
using TMPro;
using UnityEngine;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// 0.0.559 (Fire 10-09, mockup Tools\PET_SLOT_ON_NPC_DRESSING_ROOM.png): the Pet slot in the free 5th place of EQUIPMENT
    /// row 2. Drag a pet item onto it, or right-click it to pick from None + every pet item. Cosmetic only; the write goes
    /// through the outfit redress path (the ZDO owner runs PetApi.SetPet).
    /// </summary>
    public partial class DressingRoomScreenController
    {
        private bool _petFromDrop;          // the slot holds an item the admin dragged in (returned on change); picks cost nothing
        private TMP_Dropdown _petPicker;
        private List<string> _petPickerItems;
        private int _petPickerFrame = -1;

        private void WirePetSlot(Transform grid)
        {
            if (grid.Find("Slot_Pet") == null)
            {
                var src = grid.Find("Slot_Utility");
                if (src == null) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[DressingRoomScreen] Pet slot: no Slot_Utility to copy, skipped"); return; }
                // Free place = the right-most column of the bottom row (row 1 has 5 slots, row 2 has 4).
                float maxX = float.MinValue, minY = float.MaxValue;
                foreach (Transform c in grid)
                {
                    if (!c.name.StartsWith("Slot_")) continue;
                    maxX = Mathf.Max(maxX, c.position.x); minY = Mathf.Min(minY, c.position.y);
                }
                var go = Instantiate(src.gameObject, grid, false);
                go.name = "Slot_Pet";
                foreach (var h in go.GetComponents<MonoBehaviour>()) if (h != null && h.GetType().Name == "SlotClickHandler") Destroy(h);
                go.transform.position = new Vector3(maxX, minY, src.position.z);
                var label = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = "Pet";
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoomScreen] Pet slot built from Slot_Utility at ({maxX:F0}, {minY:F0})");
            }
            WireEquipmentSlot(grid, "Slot_Pet", EquipmentSlot.Pet);
            if (_slots.TryGetValue(EquipmentSlot.Pet, out var s) && s.Label != null) s.Label.text = "Pet";
        }

        private static string PetItemFor(string itemOrCreature)
        {
            if (string.IsNullOrEmpty(itemOrCreature)) return "";
            if (ObjectDB.instance?.GetItemPrefab(itemOrCreature) != null) return itemOrCreature;
            foreach (var item in NpcPetSlot.AllPetItems())
                if (PetCatalog.CreatureOfPrefabName(item) == itemOrCreature) return item;
            return "";
        }

        private void LoadPetSlot(NpcController npc)
        {
            _petFromDrop = false;
            if (!_slots.TryGetValue(EquipmentSlot.Pet, out var slot)) return;
            var ch = npc.GetComponent<Character>();
            string live = ch != null ? PetApi.GetPet(ch) : "";
            string shown = PetItemFor(!string.IsNullOrEmpty(npc.equipPet) ? npc.equipPet : live);
            if (string.IsNullOrEmpty(shown)) ClearSlotVisual(slot); else SetSlotItem(slot, shown);
            if (slot.Label != null) slot.Label.text = "Pet";
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoomScreen] Pet slot: field '{npc.equipPet}' live '{live}' -> '{shown}'");
        }

        private void OnPetDropped(string itemPrefab)
        {
            if (_petFromDrop && _slots.TryGetValue(EquipmentSlot.Pet, out var old) && !string.IsNullOrEmpty(old.CurrentPrefab))
                ReturnItemToInventory(old.CurrentPrefab);
            SetNpcPet(itemPrefab, "drop");
            _petFromDrop = true;
        }

        /// <summary>Sets the NPC's pet ("" clears) and sends it through the outfit redress path.</summary>
        public void SetNpcPet(string itemPrefab, string why)
        {
            if (_slots.TryGetValue(EquipmentSlot.Pet, out var slot))
            {
                if (why != "drop" && _petFromDrop && !string.IsNullOrEmpty(slot.CurrentPrefab)) ReturnItemToInventory(slot.CurrentPrefab);
                if (string.IsNullOrEmpty(itemPrefab)) ClearSlotVisual(slot); else SetSlotItem(slot, itemPrefab);
                if (slot.Label != null) slot.Label.text = "Pet";
            }
            _petFromDrop = false;
            if (_currentNpc == null) return;
            _currentNpc.equipPet = itemPrefab ?? "";
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoomScreen] Pet ({why}) '{_currentNpc.GetDisplayName()}' -> '{_currentNpc.equipPet}'");
            try { _currentNpc.SaveToZDO(); } catch { }
            global::FiresRPGmaker.Managers.SavedNpcManager.RedressNow(_currentNpc, "pet " + why);
        }

        /// <summary>Right-click: a dropdown of None + every pet item under the slot. Tests: PickPet(name or "").</summary>
        private void OpenPetPicker()
        {
            if (_petPickerFrame == Time.frameCount) return;   // the manual hit-test and the EventSystem both see one right-click
            _petPickerFrame = Time.frameCount;
            if (!_slots.TryGetValue(EquipmentSlot.Pet, out var slot)) return;
            if (_petPicker == null)
            {
                TMP_Dropdown src = null;
                foreach (var d in _dropdowns) if (d != null) { src = d; break; }
                if (src == null) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[DressingRoomScreen] Pet picker: no dropdown to copy"); return; }
                var go = Instantiate(src.gameObject, slot.RectTransform.parent.parent, false);
                go.name = "PetPicker";
                _petPicker = go.GetComponent<TMP_Dropdown>();
                _petPicker.onValueChanged = new TMP_Dropdown.DropdownEvent();
                _petPicker.onValueChanged.AddListener(i =>
                {
                    if (_petPickerItems == null || i < 0 || i >= _petPickerItems.Count) return;
                    SetNpcPet(_petPickerItems[i], "pick");
                    _petPicker.gameObject.SetActive(false);
                });
            }
            _petPickerItems = new List<string> { "" };
            _petPickerItems.AddRange(NpcPetSlot.AllPetItems());
            var opts = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("None") };
            for (int i = 1; i < _petPickerItems.Count; i++) opts.Add(new TMP_Dropdown.OptionData(NpcPetSlot.DisplayName(_petPickerItems[i])));
            var rt = (RectTransform)_petPicker.transform;
            _petPicker.gameObject.SetActive(true);
            rt.SetAsLastSibling();
            rt.position = slot.RectTransform.position - new Vector3(0f, slot.RectTransform.rect.height * slot.RectTransform.lossyScale.y * 0.75f, 0f);
            _petPicker.ClearOptions();
            _petPicker.AddOptions(opts);
            int cur = Mathf.Max(0, _petPickerItems.IndexOf(slot.CurrentPrefab ?? ""));
            _petPicker.SetValueWithoutNotify(cur);
            _petPicker.RefreshShownValue();
            _petPicker.Show();
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoomScreen] Pet picker open: {_petPickerItems.Count - 1} pet item(s), current '{slot.CurrentPrefab}'");
        }

        /// <summary>Test entry: the same path a pick takes ("" = None). Returns false for a name that isn't a pet item.</summary>
        public bool PickPet(string itemPrefab)
        {
            if (!string.IsNullOrEmpty(itemPrefab) && !NpcPetSlot.AllPetItems().Contains(itemPrefab)) return false;
            SetNpcPet(itemPrefab ?? "", "pick");
            return true;
        }
    }
}
