using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.Modules.Buffer;
using TMPro;
using UnityEngine;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Admin buff-profile builder — a draggable two-page book. Left page lists every status effect
    /// (vanilla + modded) with a search box + Full/Partial/Time badge filter; adding one creates a TargetSE buff
    /// record and appends it to the assembled profile shown on the right page. Save writes the buff records + the
    /// profile in the drop-in .cfg format and pushes them to the server. Layout lives in FiresBufferAdminPanel.</summary>
    public class BufferAdminPanel : BasePlayerModulePanel
    {
        private static BufferAdminPanel _instance;
        protected override string PanelTitle => "Buff Profile Builder";
        protected override Vector2 PanelSize => new Vector2(1180f, 1062f);
        protected override string PrefabName => "FiresBufferAdminPanel.prefab";

        private TMP_InputField _search;
        private TMP_InputField _profileName;
        private RectTransform _catContent;
        private GameObject _catTemplate;
        private RectTransform _profContent;
        private GameObject _profTemplate;
        private TMP_Text _statusText;
        private string _filter = "all";                                  // all | full | partial | time
        private sealed class ProfileBuff { public string Name; public int Dur = 300; public int StrPct = 100; }
        private readonly List<ProfileBuff> _assembled = new List<ProfileBuff>();   // buffs in the profile + their authored time/strength

        public static void Show()
        {
            if (Player.m_localPlayer == null) return;
            // Inherent guard — profile authoring is an admin surface; the method is public static and
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
            _instance.Open("", "");
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            BufferPlayerPanel.MakeDraggable(t);
            _statusText  = FindTmp(t, "StatusText");
            _search      = FindInputUnder(t, "SearchField");
            _profileName = FindInputUnder(t, "ProfileNameField");

            _catContent  = BuildPoolScroll(FindRect(t, "CatalogPool"));
            _catTemplate = Find(t, "CatalogRowTemplate");
            if (_catTemplate != null) _catTemplate.SetActive(false);
            _profContent  = BuildPoolScroll(FindRect(t, "ProfilePool"));
            _profTemplate = Find(t, "ProfileRowTemplate");
            if (_profTemplate != null) _profTemplate.SetActive(false);

            if (_search != null) { _search.onValueChanged.RemoveAllListeners(); _search.onValueChanged.AddListener(_ => RebuildCatalog()); }
            WireFilter(t, "FilterAll", "all");
            WireFilter(t, "FilterFull", "full");
            WireFilter(t, "FilterPartial", "partial");
            WireFilter(t, "FilterTime", "time");
            var save = FindBtn(t, "SaveProfileButton");
            if (save != null) { save.onClick.RemoveAllListeners(); save.onClick.AddListener(OnSave); }
        }

        private void WireFilter(Transform t, string name, string key)
        {
            var b = FindBtn(t, name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => { _filter = key; RebuildCatalog(); }); }
        }

        protected override void OnOpen(string a, string b)
        {
            _assembled.Clear();
            RebuildCatalog();
            RebuildProfile();
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
                BuildCatalogRow(se, badge);
                shown++;
            }
            if (_statusText != null) _statusText.text = $"{shown} status effect(s)";
        }

        private void BuildCatalogRow(StatusEffect se, StatusEffectCatalog.StrengthBadge badge)
        {
            var row = CloneRow(_catContent, _catTemplate, 26f);
            if (row == null) return;
            var nm = FindTmp(row.transform, "SeNameLabel");
            if (nm != null) nm.text = string.IsNullOrEmpty(se.m_name) ? se.name : $"{se.m_name}  ({se.name})";
            var bd = FindTmp(row.transform, "SeBadgeLabel");
            if (bd != null) bd.text = "[" + badge + "]";
            var add = FindBtn(row.transform, "AddButton");
            if (add != null) { string seName = se.name; add.onClick.RemoveAllListeners(); add.onClick.AddListener(() => AddToProfile(seName)); }
        }

        private void AddToProfile(string seName)
        {
            if (_assembled.Exists(p => p.Name == seName)) return;
            _assembled.Add(new ProfileBuff { Name = seName });
            RebuildProfile();
        }

        private void RebuildProfile()
        {
            if (_profContent == null || _profTemplate == null) return;
            ClearRowClones(_profContent);
            foreach (var pb in _assembled)
            {
                var row = CloneRow(_profContent, _profTemplate, 28f);
                if (row == null) continue;
                var nm = FindTmp(row.transform, "BuffNameLabel");
                if (nm != null) nm.text = pb.Name;

                var entry = pb;   // capture per row
                var durIn = FindInputUnder(row.transform, "DurCell");
                if (durIn != null)
                {
                    durIn.SetTextWithoutNotify(entry.Dur.ToString());
                    durIn.onValueChanged.RemoveAllListeners();
                    durIn.onValueChanged.AddListener(s => { if (int.TryParse(s, out var d) && d > 0) entry.Dur = d; });
                }
                var strIn = FindInputUnder(row.transform, "StrCell");
                if (strIn != null)
                {
                    strIn.SetTextWithoutNotify(entry.StrPct.ToString());
                    strIn.onValueChanged.RemoveAllListeners();
                    strIn.onValueChanged.AddListener(s => { if (int.TryParse(s, out var v)) entry.StrPct = Mathf.Clamp(v, 0, 100); });
                }
                var rm = FindBtn(row.transform, "RemoveButton");
                if (rm != null) { rm.onClick.RemoveAllListeners(); rm.onClick.AddListener(() => { _assembled.Remove(entry); RebuildProfile(); }); }
            }
        }

        private void OnSave()
        {
            string profile = (_profileName != null ? _profileName.text : "").Trim();
            if (string.IsNullOrEmpty(profile)) { Status("Enter a profile name first."); return; }
            if (_assembled.Count == 0) { Status("Add at least one status effect."); return; }

            // Write a TargetSE buff record per picked SE with the admin's authored duration + strength.
            foreach (var pb in _assembled)
            {
                var se = StatusEffectCatalog.Resolve(pb.Name);
                BufferConfigWriter.WriteBuff(new FiresBuffData
                {
                    UniqueName = pb.Name,
                    Name = se != null && !string.IsNullOrEmpty(se.m_name) ? se.m_name : pb.Name,
                    Duration = pb.Dur,
                    Backend = BuffBackend.TargetSE,
                    TargetSE = pb.Name,
                    StrengthCap = Mathf.Clamp01(pb.StrPct / 100f),
                });
            }
            BufferConfigWriter.WriteProfile(profile.ToLowerInvariant(), _assembled.ConvertAll(p => p.Name));
            Status($"Saved profile '{profile}' with {_assembled.Count} buff(s).");
        }

        private void Status(string s) { if (_statusText != null) _statusText.text = s; }
    }
}
