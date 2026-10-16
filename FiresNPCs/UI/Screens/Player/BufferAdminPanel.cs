using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FiresRPGmaker.Modules.Buffer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Admin: Buffer Setup (10-09, Fire approved Tools\Mockups\rpg_buffer_admin_mockup.png). One page edits a whole
    /// buffer profile and EVERY FiresBuffData field of its buffs:
    /// - the profile row: name, a picker of the saved profiles, New / Duplicate / Delete (press twice) / Preview as player;
    /// - the left pane: the profile's buffs (click to edit), "+ Stat buff", and the status-effect catalog (search, filters, Add);
    /// - the right pane: display name, unique ID, kind (status effect / stat changes), status effect, group, icon, start effect,
    ///   duration + max, strength cap, cost item + count, cost scaling;
    /// - the stat-changes pane: the 10 KG modifiers;
    /// - Save to server checks every number first (the field is named in red), Revert reloads the saved profile.
    /// It opens on the NPC's profile and works on copies: nothing is written until Save. A profile entry that doesn't resolve to a
    /// buff record stays in the list untouched. Layout: the FiresBufferAdminPanel prefab (WishboneNpcPanels builder).</summary>
    public class BufferAdminPanel : BasePlayerModulePanel
    {
        private static BufferAdminPanel _instance;
        protected override string PanelTitle => "Buffer Setup";
        protected override Vector2 PanelSize => new Vector2(900f, 740f);
        protected override string PrefabName => "FiresBufferAdminPanel.prefab";

        private static readonly Color Ink = new Color(0.24f, 0.16f, 0.08f, 1f);
        private static readonly Color Muted = new Color(0.45f, 0.34f, 0.21f, 1f);
        private static readonly Color Good = new Color(0.23f, 0.36f, 0.07f, 1f);
        private static readonly Color Bad = new Color(0.60f, 0.16f, 0.09f, 1f);
        private static readonly Color FieldBg = new Color(0.98f, 0.95f, 0.87f, 1f);
        private static readonly Color FieldBad = new Color(0.97f, 0.88f, 0.84f, 1f);
        private static readonly Color SelectedRow = new Color(1f, 0.97f, 0.88f, 0.85f);

        private static readonly (string node, WhatToModify flag)[] Stats =
        {
            ("StatAttack", WhatToModify.ModifyAttack), ("StatSpeed", WhatToModify.ModifySpeed), ("StatHpRegen", WhatToModify.ModifyHealthRegen),
            ("StatStamRegen", WhatToModify.ModifyStaminaRegen), ("StatRunDrain", WhatToModify.RunStaminaDrain), ("StatStealth", WhatToModify.ModifyStealth),
            ("StatNoise", WhatToModify.ModifyNoise), ("StatSkillGain", WhatToModify.ModifyRaiseSkills), ("StatCarry", WhatToModify.ModifyMaxCarryWeight),
            ("StatDmgReduce", WhatToModify.DamageReduction),
        };
        private static readonly string[] Scales = { "none", "duration", "strength", "both" };

        // One profile entry: the buff's working copy (null = the name doesn't resolve; kept as it is).
        private sealed class Entry { public string Name; public FiresBuffData Data; }
        private readonly List<Entry> _entries = new List<Entry>();
        private Entry _sel;
        private string _loadedProfile = "";
        private bool _dirty, _filling;
        private string _deleteArmed; private float _deleteArmedAt = -100f;

        private TMP_InputField _profileName, _search;
        private TMP_Dropdown _profileDD;
        private RectTransform _profContent, _catContent;
        private GameObject _profTemplate, _catTemplate;
        private TMP_Text _status, _validation, _editorTitle, _strValue;
        private string _filter = "all";
        private TMP_InputField _fName, _fId, _fTarget, _fGroup, _fIcon, _fFx, _fDur, _fDurCap, _fCostItem, _fCostCount;
        private TMP_Dropdown _kindDD, _scalesDD;
        private Slider _strength;
        private readonly Dictionary<WhatToModify, TMP_InputField> _stat = new Dictionary<WhatToModify, TMP_InputField>();

        public static void Show() => Show("");

        /// <summary>Opens the editor on an existing profile (the NPC book's Buffers tab passes its profile name).</summary>
        public static void Show(string profile)
        {
            if (Player.m_localPlayer == null) return;
            // Inherent guard: profile authoring is an admin surface; the method is public static and
            // must not be openable by any path (console cmd, dialogue command, mod call) on a non-admin.
            if (!(FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Only admins can edit buff profiles.");
                return;
            }
            if (_instance == null)
            {
                var go = new GameObject("FiresBufferAdminPanel");
                _instance = go.AddComponent<BufferAdminPanel>();
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
            _editorTitle = FindTmp(t, "EditorTitle");
            _strValue = FindTmp(t, "StrengthValue");

            _profileName = FindInputUnder(t, "ProfileNameField");
            _profileDD = Find(t, "ProfileDD")?.GetComponent<TMP_Dropdown>();
            if (_profileDD != null) { _profileDD.onValueChanged.RemoveAllListeners(); _profileDD.onValueChanged.AddListener(OnProfilePicked); }
            Wire(t, "NewProfileButton", OnNewProfile);
            Wire(t, "DuplicateButton", OnDuplicate);
            Wire(t, "DeleteProfileButton", OnDeleteProfile);
            Wire(t, "PreviewButton", () => BufferPlayerPanel.Show(Key(), "Preview"));
            Wire(t, "AddStatBuffButton", OnAddStatBuff);
            Wire(t, "RemoveBuffButton", OnRemoveBuff);
            Wire(t, "RevertButton", () => { Load(_loadedProfile); Status("Reverted to the saved profile.", true); });
            Wire(t, "SaveProfileButton", OnSave);

            _profContent = BuildPoolScroll(FindRect(t, "ProfilePool"));
            _profTemplate = Find(t, "ProfileRowTemplate");
            if (_profTemplate != null) _profTemplate.SetActive(false);
            _catContent = BuildPoolScroll(FindRect(t, "CatalogPool"));
            _catTemplate = Find(t, "CatalogRowTemplate");
            if (_catTemplate != null) _catTemplate.SetActive(false);
            Tight(_profContent); Tight(_catContent);

            _search = FindInputUnder(t, "SearchField");
            if (_search != null) { _search.onValueChanged.RemoveAllListeners(); _search.onValueChanged.AddListener(_ => RebuildCatalog()); }
            WireFilter(t, "FilterAll", "all"); WireFilter(t, "FilterFull", "full"); WireFilter(t, "FilterPartial", "partial"); WireFilter(t, "FilterTime", "time");

            _fName = Field(t, "NameField"); _fId = Field(t, "IdField"); _fTarget = Field(t, "TargetSEField"); _fGroup = Field(t, "GroupField");
            _fIcon = Field(t, "IconField"); _fFx = Field(t, "StartFxField"); _fDur = Field(t, "DurationField"); _fDurCap = Field(t, "DurCapField");
            _fCostItem = Field(t, "CostItemField"); _fCostCount = Field(t, "CostCountField");
            _kindDD = Find(t, "KindDD")?.GetComponent<TMP_Dropdown>();
            if (_kindDD != null) { _kindDD.onValueChanged.RemoveAllListeners(); _kindDD.onValueChanged.AddListener(_ => { if (!_filling) { PullEditor(); ApplyKindLook(); } }); }
            _scalesDD = Find(t, "CostScalesDD")?.GetComponent<TMP_Dropdown>();
            if (_scalesDD != null) { _scalesDD.onValueChanged.RemoveAllListeners(); _scalesDD.onValueChanged.AddListener(_ => { if (!_filling) PullEditor(); }); }
            _strength = Find(t, "StrengthSlider")?.GetComponent<Slider>();
            if (_strength != null)
            {
                _strength.minValue = 0; _strength.maxValue = 100; _strength.wholeNumbers = true;
                _strength.onValueChanged.RemoveAllListeners();
                _strength.onValueChanged.AddListener(v => { if (_strValue != null) _strValue.text = Mathf.RoundToInt(v) + "%"; if (!_filling) PullEditor(); });
            }
            foreach (var (node, flag) in Stats) { var f = Field(t, node); if (f != null) _stat[flag] = f; }
        }

        private TMP_InputField Field(Transform t, string name)
        {
            var f = FindInputUnder(t, name);
            if (f != null) { f.onValueChanged.RemoveAllListeners(); f.onValueChanged.AddListener(_ => { if (!_filling) PullEditor(); }); }
            return f;
        }

        private static void Wire(Transform t, string name, UnityEngine.Events.UnityAction a)
        {
            var b = FindBtn(t, name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(a); }
        }

        private void WireFilter(Transform t, string name, string key)
        {
            var b = FindBtn(t, name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => { _filter = key; RebuildCatalog(); }); }
        }

        private static void Tight(RectTransform content)
        {
            var vlg = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg != null) { vlg.spacing = 0f; vlg.padding = new RectOffset(0, 0, 0, 0); }
        }

        protected override void OnOpen(string a, string b)
        {
            Load(string.IsNullOrEmpty(a) ? "default" : a);
            RebuildCatalog();
        }

        // ───────────────────────── profile ─────────────────────────
        private string Key() => ((_profileName != null ? _profileName.text : _loadedProfile) ?? "").Trim().ToLowerInvariant();

        private void Load(string profile)
        {
            profile = (profile ?? "").Trim().ToLowerInvariant();
            _entries.Clear(); _sel = null; _dirty = false;
            _loadedProfile = profile;
            if (_profileName != null) _profileName.SetTextWithoutNotify(profile);
            if (BufferManager.Profiles.TryGetValue(profile, out var csv) && !string.IsNullOrEmpty(csv))
                foreach (var token in csv.Split(','))
                {
                    string name = token.Trim();
                    if (name.Length == 0 || _entries.Exists(e => Same(e.Name, name))) continue;
                    var rec = BufferManager.Resolve(name);
                    _entries.Add(new Entry { Name = name, Data = rec != null ? Clone(rec) : null });
                }
            FillProfileDD();
            if (_entries.Count > 0) _sel = _entries[0];
            RebuildProfile(); FillEditor(); Validate();
            var subtitle = _status != null ? FindTmp(_status.transform.root, "SubtitleText") : null;
            Status(BufferManager.Profiles.ContainsKey(profile)
                ? $"Loaded '{profile}' ({_entries.Count} buff{(_entries.Count == 1 ? "" : "s")})."
                : $"'{profile}' is new: add buffs, then Save.", true);
            FiresRPGmaker.Log?.LogDebug($"[BufferAdmin] loaded profile '{profile}': {_entries.Count} entr(ies), {_entries.Count(e => e.Data == null)} unresolved kept");
        }

        private void FillProfileDD()
        {
            if (_profileDD == null) return;
            var keys = BufferManager.Profiles.Keys.OrderBy(k => k).ToList();
            _profileDD.ClearOptions();
            _profileDD.AddOptions(new List<string> { "v" }.Concat(keys).ToList());
            _profileDD.SetValueWithoutNotify(0);
        }

        private void OnProfilePicked(int idx)
        {
            if (idx <= 0 || _profileDD == null || idx >= _profileDD.options.Count) return;
            string key = _profileDD.options[idx].text;
            if (_dirty) Status($"Unsaved changes to '{_loadedProfile}' were dropped.", false);
            Load(key);
        }

        private void OnNewProfile()
        {
            int n = 1; string key;
            do key = "profile" + n++; while (BufferManager.Profiles.ContainsKey(key));
            _entries.Clear(); _sel = null; _loadedProfile = key; _dirty = true;
            if (_profileName != null) _profileName.SetTextWithoutNotify(key);
            RebuildProfile(); FillEditor(); Validate();
            Status($"New profile '{key}': rename it if you like, add buffs, then Save.", true);
        }

        private void OnDuplicate()
        {
            string src = Key(); int n = 2; string key;
            do key = src + "_" + n++; while (BufferManager.Profiles.ContainsKey(key));
            _loadedProfile = key; _dirty = true;
            if (_profileName != null) _profileName.SetTextWithoutNotify(key);
            Status($"Copied as '{key}' (the buffs are shared records). Save to keep it.", true);
        }

        // Two presses within 4 s; empties the profile's list on the server (the buff records stay for other profiles).
        private void OnDeleteProfile()
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
            BufferConfigWriter.WriteProfile(key, new string[0]);
            Status($"Deleted profile '{key}' (its buff list is now empty).", true);
            Load(key);
        }

        // ───────────────────────── list + catalog ─────────────────────────
        private void RebuildProfile()
        {
            if (_profContent == null || _profTemplate == null) return;
            ClearRowClones(_profContent);
            foreach (var e in _entries)
            {
                var row = CloneRow(_profContent, _profTemplate, 28f);
                if (row == null) continue;
                var entry = e;
                var nm = FindTmp(row.transform, "BuffNameLabel");
                if (nm != null) { nm.text = entry.Data != null && !string.IsNullOrEmpty(entry.Data.Name) ? entry.Data.Name : entry.Name; nm.color = entry.Data == null ? Bad : Ink; }
                var kind = FindTmp(row.transform, "KindLabel");
                if (kind != null) kind.text = entry.Data == null ? "missing record" : entry.Data.Backend == BuffBackend.TargetSE ? "Status effect" : "Stat changes";
                var cost = FindTmp(row.transform, "CostLabel");
                if (cost != null) cost.text = entry.Data == null ? "" : entry.Data.NeededPrefabCount > 0 ? $"{entry.Data.NeededPrefabCount} {entry.Data.NeededPrefab}" : "Free";
                var bg = Find(row.transform, "SelectButton");
                if (bg != null)
                {
                    var img = bg.GetComponent<Image>(); if (img != null) img.color = entry == _sel ? SelectedRow : new Color(1f, 1f, 1f, 0f);
                    var b = bg.GetComponent<Button>();
                    if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => { _sel = entry; RebuildProfile(); FillEditor(); Validate(); }); }
                }
            }
        }

        private void RebuildCatalog()
        {
            if (_catContent == null || _catTemplate == null) return;
            ClearRowClones(_catContent);
            string q = (_search != null ? _search.text : "").Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var se in StatusEffectCatalog.All().OrderBy(s => s.name))
            {
                if (se == null) continue;
                var badge = StatusEffectCatalog.Badge(se);
                if (_filter == "full" && badge != StatusEffectCatalog.StrengthBadge.Full) continue;
                if (_filter == "partial" && badge != StatusEffectCatalog.StrengthBadge.Partial) continue;
                if (_filter == "time" && badge != StatusEffectCatalog.StrengthBadge.TimeOnly) continue;
                if (q.Length > 0 && !se.name.ToLowerInvariant().Contains(q) && !(se.m_name ?? "").ToLowerInvariant().Contains(q)) continue;
                if (shown >= 300) break;
                var row = CloneRow(_catContent, _catTemplate, 26f);
                if (row == null) continue;
                string seName = se.name;
                var nm = FindTmp(row.transform, "SeNameLabel");
                if (nm != null) nm.text = string.IsNullOrEmpty(se.m_name) ? se.name : $"{Loc(se.m_name)}  ({se.name})";
                var bd = FindTmp(row.transform, "SeBadgeLabel");
                if (bd != null) bd.text = badge.ToString();
                var add = FindBtn(row.transform, "AddButton");
                if (add != null) { add.onClick.RemoveAllListeners(); add.onClick.AddListener(() => AddStatusEffect(seName)); }
                shown++;
            }
        }

        private void AddStatusEffect(string seName)
        {
            if (_entries.Exists(e => Same(e.Name, seName))) { Status($"'{seName}' is already in the profile.", false); return; }
            var se = StatusEffectCatalog.Resolve(seName);
            var existing = BufferManager.Resolve(seName);
            var data = existing != null ? Clone(existing) : new FiresBuffData
            {
                UniqueName = seName, Name = se != null && !string.IsNullOrEmpty(se.m_name) ? Loc(se.m_name) : seName,
                Duration = 300, Backend = BuffBackend.TargetSE, TargetSE = seName, StrengthCap = 1f,
            };
            var e = new Entry { Name = seName, Data = data };
            _entries.Add(e); _sel = e; _dirty = true;
            RebuildProfile(); FillEditor(); Validate();
            Status($"Added '{data.Name}'. Set its cost and duration, then Save.", true);
        }

        private void OnAddStatBuff()
        {
            int n = 1; string id;
            do id = "buff_" + n++; while (_entries.Exists(e => Same(e.Name, id)) || BufferManager.Resolve(id) != null);
            var e = new Entry { Name = id, Data = new FiresBuffData { UniqueName = id, Name = "New buff", Duration = 300, Backend = BuffBackend.Synthetic } };
            _entries.Add(e); _sel = e; _dirty = true;
            RebuildProfile(); FillEditor(); Validate();
            Status("Added a stat buff: name it and set its stat changes.", true);
        }

        private void OnRemoveBuff()
        {
            if (_sel == null) return;
            int i = _entries.IndexOf(_sel);
            _entries.Remove(_sel); _dirty = true;
            _sel = _entries.Count == 0 ? null : _entries[Mathf.Clamp(i, 0, _entries.Count - 1)];
            RebuildProfile(); FillEditor(); Validate();
            Status("Removed from the profile (its record stays for other profiles).", true);
        }

        // ───────────────────────── editor ─────────────────────────
        private void FillEditor()
        {
            _filling = true;
            try
            {
                var d = _sel?.Data;
                if (_editorTitle != null) _editorTitle.text = _sel == null ? "Select a buff" : d == null ? _sel.Name + " (no record)" : (string.IsNullOrEmpty(d.Name) ? d.UniqueName : d.Name);
                Set(_fName, d?.Name); Set(_fId, d?.UniqueName); Set(_fTarget, d?.TargetSE); Set(_fGroup, d?.BuffGroup);
                Set(_fIcon, d?.SpritePrefab); Set(_fFx, d?.StartEffectPrefab);
                Set(_fDur, d != null ? d.Duration.ToString(CultureInfo.InvariantCulture) : "");
                Set(_fDurCap, d != null && d.DurationCap > 0 ? d.DurationCap.ToString(CultureInfo.InvariantCulture) : "");
                Set(_fCostItem, d?.NeededPrefab);
                Set(_fCostCount, d != null ? d.NeededPrefabCount.ToString(CultureInfo.InvariantCulture) : "");
                if (_kindDD != null) _kindDD.SetValueWithoutNotify(d != null && d.Backend == BuffBackend.Synthetic ? 1 : 0);
                if (_scalesDD != null) _scalesDD.SetValueWithoutNotify(d != null ? Mathf.Max(0, System.Array.IndexOf(Scales, (d.CostScales ?? "none").ToLowerInvariant())) : 0);
                if (_strength != null) _strength.SetValueWithoutNotify(d != null ? Mathf.RoundToInt(Mathf.Clamp01(d.StrengthCap <= 0f ? 1f : d.StrengthCap) * 100f) : 100);
                if (_strValue != null) _strValue.text = (_strength != null ? Mathf.RoundToInt(_strength.value) : 100) + "%";
                foreach (var kv in _stat)
                {
                    float v = d != null && d.ModifyList.TryGetValue(kv.Key, out var x) ? x : Default(kv.Key);
                    Set(kv.Value, v.ToString("0.##", CultureInfo.InvariantCulture));
                }
                bool editable = d != null;
                foreach (var f in AllFields()) if (f != null) f.interactable = editable;
                if (_kindDD != null) _kindDD.interactable = editable;
                if (_scalesDD != null) _scalesDD.interactable = editable;
                if (_strength != null) _strength.interactable = editable;
                ApplyKindLook();
            }
            finally { _filling = false; }
        }

        private IEnumerable<TMP_InputField> AllFields()
        {
            foreach (var f in new[] { _fName, _fId, _fTarget, _fGroup, _fIcon, _fFx, _fDur, _fDurCap, _fCostItem, _fCostCount }) yield return f;
            foreach (var f in _stat.Values) yield return f;
        }

        // Status-effect buffs use the status effect field; stat buffs use the stat pane. The other is greyed.
        private void ApplyKindLook()
        {
            bool se = _kindDD == null || _kindDD.value == 0;
            if (_fTarget != null) _fTarget.interactable = se && _sel?.Data != null;
            foreach (var f in _stat.Values) if (f != null) f.interactable = !se && _sel?.Data != null;
        }

        // Editor fields » the selected entry's copy. Numbers that don't parse leave the copy as it is; Validate names them.
        private void PullEditor()
        {
            var d = _sel?.Data;
            if (d == null) return;
            _dirty = true;
            d.Name = Txt(_fName);
            string id = Txt(_fId);
            if (id.Length > 0 && !Same(id, d.UniqueName) && !_entries.Exists(e => e != _sel && Same(e.Name, id))) { d.UniqueName = id; _sel.Name = id; }
            d.Backend = _kindDD != null && _kindDD.value == 1 ? BuffBackend.Synthetic : BuffBackend.TargetSE;
            d.TargetSE = d.Backend == BuffBackend.TargetSE ? Txt(_fTarget) : "";
            d.BuffGroup = Txt(_fGroup); d.SpritePrefab = Txt(_fIcon); d.StartEffectPrefab = Txt(_fFx);
            if (int.TryParse(Txt(_fDur), out var dur)) d.Duration = dur;
            string dc = Txt(_fDurCap); if (dc.Length == 0) d.DurationCap = 0; else if (int.TryParse(dc, out var dcv)) d.DurationCap = dcv;
            d.NeededPrefab = Txt(_fCostItem);
            string cc = Txt(_fCostCount); if (cc.Length == 0) d.NeededPrefabCount = 0; else if (int.TryParse(cc, out var ccv)) d.NeededPrefabCount = ccv;
            d.CostScales = _scalesDD != null ? Scales[Mathf.Clamp(_scalesDD.value, 0, Scales.Length - 1)] : "none";
            if (_strength != null) d.StrengthCap = Mathf.Clamp01(_strength.value / 100f);
            d.Flags = WhatToModify.None;
            foreach (var kv in _stat)
            {
                if (TryNum(Txt(kv.Value), out float v)) d.ModifyList[kv.Key] = v;
                if (Mathf.Abs(d.ModifyList[kv.Key] - Default(kv.Key)) > 0.0001f) d.Flags |= kv.Key;
            }
            if (_editorTitle != null) _editorTitle.text = string.IsNullOrEmpty(d.Name) ? d.UniqueName : d.Name;
            Validate();
        }

        // Every number checked; the first problem shows in the stats pane's line and its field turns red. Returns OK.
        private bool Validate()
        {
            var problems = new List<string>();
            foreach (var f in AllFields()) Mark(f, false);
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i]; var d = e.Data;
                if (d == null) continue;
                bool sel = e == _sel;
                string who = string.IsNullOrEmpty(d.Name) ? d.UniqueName : d.Name;
                if (string.IsNullOrWhiteSpace(d.UniqueName)) problems.Add($"{who}: the unique ID is empty");
                if (!IntOk(sel ? Txt(_fDur) : d.Duration.ToString(), 1, out _)) { problems.Add($"{who}: duration must be a whole number of seconds, 1 or more"); if (sel) Mark(_fDur, true); }
                string dc = sel ? Txt(_fDurCap) : (d.DurationCap > 0 ? d.DurationCap.ToString() : "");
                if (dc.Length > 0 && (!IntOk(dc, 1, out int dcv) || dcv < d.Duration)) { problems.Add($"{who}: max duration must be at least the duration"); if (sel) Mark(_fDurCap, true); }
                string cc = sel ? Txt(_fCostCount) : d.NeededPrefabCount.ToString();
                if (cc.Length > 0 && !IntOk(cc, 0, out _)) { problems.Add($"{who}: the cost count must be 0 or more"); if (sel) Mark(_fCostCount, true); }
                if (d.NeededPrefabCount > 0 && string.IsNullOrEmpty(d.NeededPrefab)) { problems.Add($"{who}: a cost count needs a cost item"); if (sel) Mark(_fCostItem, true); }
                if (!string.IsNullOrEmpty(d.NeededPrefab) && ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(d.NeededPrefab) == null)
                { problems.Add($"{who}: no item '{d.NeededPrefab}'"); if (sel) Mark(_fCostItem, true); }
                if (d.Backend == BuffBackend.TargetSE && StatusEffectCatalog.Resolve(d.TargetSE) == null)
                { problems.Add($"{who}: no status effect '{d.TargetSE}'"); if (sel) Mark(_fTarget, true); }
                if (sel && d.Backend == BuffBackend.Synthetic)
                    foreach (var kv in _stat)
                    {
                        string s = Txt(kv.Value);
                        bool ok = TryNum(s, out float v);
                        if (ok && kv.Key == WhatToModify.DamageReduction) ok = v >= 0f && v <= 0.9f;
                        else if (ok && kv.Key == WhatToModify.ModifyMaxCarryWeight) ok = v >= -1000f && v <= 1000f;
                        else if (ok) ok = v >= 0f && v <= 10f;
                        if (!ok)
                        {
                            Mark(kv.Value, true);
                            problems.Add(kv.Key == WhatToModify.DamageReduction ? "Damage reduction must be between 0 and 0.9"
                                       : kv.Key == WhatToModify.ModifyMaxCarryWeight ? "Carry + must be a number (-1000 to 1000)"
                                       : $"{Label(kv.Key)} must be a multiplier between 0 and 10");
                        }
                    }
            }
            if (string.IsNullOrWhiteSpace(Key())) problems.Add("Enter a profile name");
            if (_validation != null) _validation.text = problems.Count == 0 ? "" : problems[0] + (problems.Count > 1 ? $"  (+{problems.Count - 1} more)" : "");
            var save = _status != null ? FindBtn(_status.transform.parent, "SaveProfileButton") : null;
            if (save != null) save.interactable = problems.Count == 0;
            if (_status != null && _dirty && problems.Count == 0) Status($"Unsaved changes to '{Key()}'.", true);
            return problems.Count == 0;
        }

        private void OnSave()
        {
            PullEditor();
            if (!Validate()) { Status("Fix the field in red first; nothing was saved.", false); return; }
            string key = Key();
            int wrote = 0;
            foreach (var e in _entries)
                if (e.Data != null) { BufferConfigWriter.WriteBuff(e.Data); wrote++; }
            BufferConfigWriter.WriteProfile(key, _entries.Select(e => e.Data != null ? e.Data.UniqueName : e.Name));
            _loadedProfile = key; _dirty = false;
            FillProfileDD();
            Status($"Saved '{key}' to the server: {_entries.Count} buff(s), {wrote} record(s) written.", true);
            FiresRPGmaker.Log?.LogDebug($"[BufferAdmin] saved profile '{key}': {_entries.Count} entr(ies), {wrote} record(s)");
        }

        // ───────────────────────── small helpers ─────────────────────────
        private void Status(string s, bool ok) { if (_status != null) { _status.text = s; _status.color = ok ? Good : Bad; } }
        private static void Set(TMP_InputField f, string s) { if (f != null) f.SetTextWithoutNotify(s ?? ""); }
        private static string Txt(TMP_InputField f) => f != null ? (f.text ?? "").Trim() : "";
        private static bool Same(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
        private static string Loc(string s) => Localization.instance != null ? Localization.instance.Localize(s) : s;
        private static float Default(WhatToModify w) => w == WhatToModify.ModifyMaxCarryWeight || w == WhatToModify.DamageReduction ? 0f : 1f;
        private static bool TryNum(string s, out float v) => float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        private static bool IntOk(string s, int min, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= min;

        private static void Mark(TMP_InputField f, bool bad)
        {
            if (f == null) return;
            var img = f.GetComponent<Image>();
            if (img != null) img.color = bad ? FieldBad : FieldBg;
        }

        private static string Label(WhatToModify w)
        {
            foreach (var (node, flag) in Stats) if (flag == w) return node.Substring(4);
            return w.ToString();
        }

        private static FiresBuffData Clone(FiresBuffData s) => new FiresBuffData
        {
            UniqueName = s.UniqueName, Name = s.Name, Duration = s.Duration, SpritePrefab = s.SpritePrefab, NeededPrefab = s.NeededPrefab,
            NeededPrefabCount = s.NeededPrefabCount, StartEffectPrefab = s.StartEffectPrefab, BuffGroup = s.BuffGroup, Flags = s.Flags,
            ModifyList = new Dictionary<WhatToModify, float>(s.ModifyList), Backend = s.Backend, TargetSE = s.TargetSE,
            StrengthCap = s.StrengthCap, DurationCap = s.DurationCap, CostScales = s.CostScales,
        };
    }
}
