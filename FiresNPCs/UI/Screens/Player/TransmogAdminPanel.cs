using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Admin: Transmog Setup (10-09, Fire approved Tools\Mockups\rpg_transmog_admin_mockup.png). Edits a transmog
    /// profile's offerings (Slot | appearance prefab | currency | price, = TransmogOption): the profile row (name, picker of the
    /// saved profiles, New / Duplicate / Delete (press twice) / Preview as player), a search + slot filter, one editable row per
    /// offering with an icon, move up / down and remove, "+ Add offering" (takes the slot filter), Reload from server, and Save
    /// to server, blocked while any row is wrong (unknown prefab, negative or non-number price; the reason is named).
    /// Works on a copy: nothing is written until Save. Layout: the FiresTransmogAdminPanel prefab (WishboneNpcPanels builder).</summary>
    public class TransmogAdminPanel : BasePlayerModulePanel
    {
        private static TransmogAdminPanel _instance;
        protected override string PanelTitle => "Transmog Setup";
        protected override Vector2 PanelSize => new Vector2(860f, 560f);
        protected override string PrefabName => "FiresTransmogAdminPanel.prefab";

        private static readonly Color Good = new Color(0.23f, 0.36f, 0.07f, 1f);
        private static readonly Color Bad = new Color(0.60f, 0.16f, 0.09f, 1f);
        private static readonly Color FieldBg = new Color(0.98f, 0.95f, 0.87f, 1f);
        private static readonly Color FieldBad = new Color(0.97f, 0.88f, 0.84f, 1f);

        // The slot filter chips » the item types each one shows.
        private static readonly Dictionary<string, string[]> FilterTypes = new Dictionary<string, string[]>
        {
            { "FilterAll", null }, { "FilterHelmet", new[] { "Helmet" } }, { "FilterChest", new[] { "Chest" } }, { "FilterLegs", new[] { "Legs" } },
            { "FilterCape", new[] { "Shoulder" } }, { "FilterShield", new[] { "Shield" } },
            { "FilterWeapons", new[] { "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Tool" } },
        };

        private sealed class Offer { public string Slot = "Helmet", Prefab = "", Currency = "Coins", Price = "0"; }
        private readonly List<Offer> _offers = new List<Offer>();
        private string _loaded = "";
        private string _filterKey = "FilterAll";
        private bool _dirty;
        private string _deleteArmed; private float _deleteArmedAt = -100f;

        private TMP_InputField _profileName, _search;
        private TMP_Dropdown _profileDD;
        private RectTransform _content;
        private GameObject _rowTemplate;
        private TMP_Text _status, _validation;
        private Button _save;

        public static void Show(string profile)
        {
            if (Player.m_localPlayer == null) return;
            if (!(FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Only admins can edit transmog profiles.");
                return;
            }
            if (_instance == null)
            {
                var go = new GameObject("FiresTransmogAdminPanel");
                _instance = go.AddComponent<TransmogAdminPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profile ?? "", "");
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            BufferPlayerPanel.MakeDraggable(t);
            _status = FindTmp(t, "StatusText");
            _validation = FindTmp(t, "ValidationText");
            _profileName = FindInputUnder(t, "ProfileNameField");
            _profileDD = Find(t, "ProfileDD")?.GetComponent<TMP_Dropdown>();
            if (_profileDD != null) { _profileDD.onValueChanged.RemoveAllListeners(); _profileDD.onValueChanged.AddListener(OnProfilePicked); }
            if (_profileName != null) { _profileName.onValueChanged.RemoveAllListeners(); _profileName.onValueChanged.AddListener(_ => { _dirty = true; Validate(); }); }
            _search = FindInputUnder(t, "SearchField");
            if (_search != null) { _search.onValueChanged.RemoveAllListeners(); _search.onValueChanged.AddListener(_ => Rebuild()); }
            foreach (var key in FilterTypes.Keys.ToList())
            {
                var b = FindBtn(t, key); string k = key;
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => { _filterKey = k; Rebuild(); }); }
            }
            Wire(t, "NewProfileButton", OnNew);
            Wire(t, "DuplicateButton", OnDuplicate);
            Wire(t, "DeleteProfileButton", OnDelete);
            Wire(t, "PreviewButton", () => TransmogPlayerPanel.Show(Key(), "Preview"));
            Wire(t, "AddOfferingButton", OnAdd);
            Wire(t, "ReloadButton", () => { TransmogProfileManager.ReloadAll(); Load(_loaded); Status("Reloaded from the server.", true); });
            Wire(t, "SaveButton", OnSave);
            _save = FindBtn(t, "SaveButton");

            _content = BuildPoolScroll(FindRect(t, "OfferPool"));
            _rowTemplate = Find(t, "OfferRowTemplate");
            if (_rowTemplate != null) _rowTemplate.SetActive(false);
            var vlg = _content != null ? _content.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg != null) { vlg.spacing = 0f; vlg.padding = new RectOffset(0, 0, 0, 0); }
        }

        private static void Wire(Transform t, string name, UnityEngine.Events.UnityAction a)
        {
            var b = FindBtn(t, name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(a); }
        }

        protected override void OnOpen(string profile, string _)
        {
            Load(string.IsNullOrEmpty(profile) ? "default" : profile);
        }

        private string Key() => ((_profileName != null ? _profileName.text : _loaded) ?? "").Trim();

        private void Load(string profile)
        {
            _loaded = (profile ?? "").Trim();
            _offers.Clear();
            foreach (var o in TransmogProfileManager.GetProfileOptions(_loaded))
                _offers.Add(new Offer { Slot = o.Slot ?? "Helmet", Prefab = o.Prefab ?? "", Currency = string.IsNullOrEmpty(o.PricePrefab) ? "Coins" : o.PricePrefab, Price = o.PriceAmount.ToString(CultureInfo.InvariantCulture) });
            if (_profileName != null) _profileName.SetTextWithoutNotify(_loaded);
            _dirty = false;
            FillProfileDD();
            Rebuild();
            bool known = TransmogProfileManager.GetLoadedProfileNames().Any(n => string.Equals(n, _loaded, System.StringComparison.OrdinalIgnoreCase));
            Status(known ? $"Loaded '{_loaded}' ({_offers.Count} offering{(_offers.Count == 1 ? "" : "s")})." : $"'{_loaded}' is new: add offerings, then Save.", true);
        }

        private void FillProfileDD()
        {
            if (_profileDD == null) return;
            _profileDD.ClearOptions();
            _profileDD.AddOptions(new List<string> { "v" }.Concat(TransmogProfileManager.GetLoadedProfileNames().OrderBy(n => n)).ToList());
            _profileDD.SetValueWithoutNotify(0);
        }

        private void OnProfilePicked(int idx)
        {
            if (idx <= 0 || _profileDD == null || idx >= _profileDD.options.Count) return;
            Load(_profileDD.options[idx].text);
        }

        private void OnNew()
        {
            int n = 1; string key; var names = TransmogProfileManager.GetLoadedProfileNames().ToList();
            do key = "transmog" + n++; while (names.Any(x => string.Equals(x, key, System.StringComparison.OrdinalIgnoreCase)));
            _loaded = key; _offers.Clear(); _dirty = true;
            if (_profileName != null) _profileName.SetTextWithoutNotify(key);
            Rebuild();
            Status($"New profile '{key}': add offerings, then Save.", true);
        }

        private void OnDuplicate()
        {
            string key = Key() + "_copy";
            _loaded = key; _dirty = true;
            if (_profileName != null) _profileName.SetTextWithoutNotify(key);
            Validate();
            Status($"Copied as '{key}'. Save to keep it.", true);
        }

        // Two presses within 4 s; saves the profile empty on the server.
        private void OnDelete()
        {
            string key = Key();
            if (string.IsNullOrEmpty(key)) return;
            if (_deleteArmed != key || Time.unscaledTime - _deleteArmedAt > 4f)
            {
                _deleteArmed = key; _deleteArmedAt = Time.unscaledTime;
                Status($"Delete profile '{key}'? Press Delete again to confirm.", false);
                return;
            }
            _deleteArmed = null;
            TransmogProfileManager.SaveProfile(key, "");
            Load(key);
            Status($"Deleted the offerings of '{key}'.", true);
        }

        private void OnAdd()
        {
            var types = FilterTypes[_filterKey];
            _offers.Add(new Offer { Slot = types != null ? types[0] : "Helmet" });
            _dirty = true;
            Rebuild();
            Status("Added a row: type the appearance prefab.", true);
        }

        private bool Shown(Offer o)
        {
            var types = FilterTypes[_filterKey];
            if (types != null && !types.Contains(o.Slot)) return false;
            string q = (_search != null ? _search.text : "").Trim().ToLowerInvariant();
            return q.Length == 0 || o.Prefab.ToLowerInvariant().Contains(q) || ItemName(o.Prefab).ToLowerInvariant().Contains(q);
        }

        private void Rebuild()
        {
            if (_content == null || _rowTemplate == null) return;
            ClearRowClones(_content);
            for (int i = 0; i < _offers.Count; i++)
            {
                var o = _offers[i];
                if (!Shown(o)) continue;
                var row = CloneRow(_content, _rowTemplate, 36f);
                if (row == null) continue;
                int index = i;
                var dd = Find(row.transform, "SlotDD")?.GetComponent<TMP_Dropdown>();
                if (dd != null)
                {
                    dd.ClearOptions();
                    dd.AddOptions(TransmogState.AvailableTypes.ToList());
                    dd.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(TransmogState.AvailableTypes, o.Slot)));
                    dd.onValueChanged.RemoveAllListeners();
                    dd.onValueChanged.AddListener(v => { o.Slot = TransmogState.AvailableTypes[v]; _dirty = true; Validate(); });
                }
                Bind(row.transform, "AppearanceField", o.Prefab, s => { o.Prefab = s; SetIcon(row.transform, s); });
                Bind(row.transform, "CurrencyField", o.Currency, s => o.Currency = s);
                Bind(row.transform, "PriceField", o.Price, s => o.Price = s);
                SetIcon(row.transform, o.Prefab);
                Wire(row.transform, "UpButton", () => Move(index, -1));
                Wire(row.transform, "DownButton", () => Move(index, +1));
                Wire(row.transform, "RemoveButton", () => { _offers.RemoveAt(index); _dirty = true; Rebuild(); Status("Removed a row.", true); });
            }
            Validate();
        }

        private void Bind(Transform row, string name, string value, System.Action<string> set)
        {
            var f = FindInputUnder(row, name);
            if (f == null) return;
            f.SetTextWithoutNotify(value ?? "");
            f.onValueChanged.RemoveAllListeners();
            f.onValueChanged.AddListener(s => { set((s ?? "").Trim()); _dirty = true; Validate(); });
        }

        private static void SetIcon(Transform row, string prefab)
        {
            var img = row.Find("Icon")?.GetComponent<Image>();
            if (img == null) return;
            var drop = ObjectDB.instance != null && !string.IsNullOrEmpty(prefab) ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            var s = drop != null ? drop.m_itemData.GetIcon() : null;
            img.sprite = s; img.enabled = s != null;
        }

        private void Move(int i, int d)
        {
            int j = i + d;
            if (j < 0 || j >= _offers.Count) return;
            var x = _offers[i]; _offers[i] = _offers[j]; _offers[j] = x;
            _dirty = true; Rebuild();
        }

        // Every row checked (shown or filtered out); the fields of shown rows turn red. Save stays off until all pass.
        private bool Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(Key())) problems.Add("Enter a profile name");
            for (int i = 0; i < _offers.Count; i++)
            {
                var o = _offers[i];
                if (string.IsNullOrEmpty(o.Prefab)) { problems.Add($"Row {i + 1}: the appearance is empty"); continue; }
                if (!ItemExists(o.Prefab)) problems.Add($"Row {i + 1}: no item '{o.Prefab}'" + Suggest(o.Prefab));
                if (!ItemExists(o.Currency)) problems.Add($"Row {i + 1}: no currency item '{o.Currency}'");
                if (!int.TryParse(o.Price, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) || p < 0) problems.Add($"Row {i + 1}: the price must be a whole number, 0 or more");
            }
            if (_content != null)
                foreach (Transform row in _content)
                {
                    if (row.name != "Row") continue;
                    var app = FindInputUnder(row, "AppearanceField"); var cur = FindInputUnder(row, "CurrencyField"); var price = FindInputUnder(row, "PriceField");
                    Mark(app, app != null && (app.text.Trim().Length == 0 || !ItemExists(app.text.Trim())));
                    Mark(cur, cur != null && !ItemExists(cur.text.Trim()));
                    Mark(price, price != null && (!int.TryParse(price.text.Trim(), out int pv) || pv < 0));
                }
            if (_validation != null) _validation.text = problems.Count == 0 ? "" : string.Join(" · ", problems.Take(2)) + (problems.Count > 2 ? $"  (+{problems.Count - 2} more)" : "") + ". Save is blocked until fixed.";
            if (_save != null) _save.interactable = problems.Count == 0;
            if (_dirty && problems.Count == 0) Status($"Unsaved changes to '{Key()}'.", true);
            return problems.Count == 0;
        }

        private void OnSave()
        {
            if (!Validate()) { Status("Fix the rows in red first; nothing was saved.", false); return; }
            string key = Key();
            var body = string.Join(System.Environment.NewLine, _offers.Select(o => $"{o.Slot}|{o.Prefab}|{o.Currency}|{int.Parse(o.Price, CultureInfo.InvariantCulture)}"));
            TransmogProfileManager.SaveProfile(key, body);
            _loaded = key; _dirty = false;
            FillProfileDD();
            Status($"Saved '{key}' to the server ({_offers.Count} offering{(_offers.Count == 1 ? "" : "s")}).", true);
            FiresRPGmaker.Log?.LogDebug($"[TransmogAdmin] saved profile '{key}': {_offers.Count} offering(s)");
        }

        private void Status(string s, bool ok) { if (_status != null) { _status.text = s; _status.color = ok ? Good : Bad; } }

        private static void Mark(TMP_InputField f, bool bad)
        {
            var img = f != null ? f.GetComponent<Image>() : null;
            if (img != null) img.color = bad ? FieldBad : FieldBg;
        }

        private static bool ItemExists(string prefab) => !string.IsNullOrEmpty(prefab) && (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(prefab) != null);

        private static string ItemName(string prefab)
        {
            var drop = ObjectDB.instance != null && !string.IsNullOrEmpty(prefab) ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            if (drop == null) return prefab ?? "";
            string n = drop.m_itemData.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(n) : n;
        }

        // "did you mean X?": the item prefab with the smallest edit distance (only for close misses).
        private static string Suggest(string prefab)
        {
            if (ObjectDB.instance == null || string.IsNullOrEmpty(prefab)) return "";
            string best = null; int bestD = 3;
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (go == null) continue;
                int d = Distance(prefab.ToLowerInvariant(), go.name.ToLowerInvariant(), bestD);
                if (d < bestD) { bestD = d; best = go.name; }
            }
            return best != null ? $" (did you mean {best}?)" : "";
        }

        private static int Distance(string a, string b, int cap)
        {
            if (System.Math.Abs(a.Length - b.Length) >= cap) return cap;
            var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = System.Math.Min(System.Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[b.Length];
        }
    }
}
