using System.Collections.Generic;
using FiresRPGmaker.Modules.Buffer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Player-facing buff shop — a draggable two-page book. Left page lists the buffs the Buffer NPC's
    /// profile sells; clicking one shows its details on the right page with a Duration slider (1..cap) and a
    /// Strength slider (0..100% of the admin-authored value). Buy routes through the server-authoritative grant
    /// (<see cref="BufferNetwork.RequestApply"/>) — the server validates the profile + clamps the caps, then the
    /// client pays the cost and applies. Layout + row look live in the baked FiresBufferPanel book prefab.</summary>
    public class BufferPlayerPanel : BasePlayerModulePanel
    {
        private static BufferPlayerPanel _instance;
        protected override string PanelTitle => "Buffs";
        protected override Vector2 PanelSize => new Vector2(1180f, 1062f);
        protected override string PrefabName => "FiresBufferPanel.prefab";

        private string _profileName;
        private RectTransform _listContent;
        private GameObject _rowTemplate;
        private TMP_Text _statusText;

        private TMP_Text _detailName, _detailDesc, _detailCost, _durInfo, _strInfo;
        private Button _buyButton;
        private FiresBuffData _selected;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresBufferPanel");
                _instance = go.AddComponent<BufferPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        /// <summary>Makes the baked book popup draggable by its title bar — attaches the same PopupDragHandler
        /// the dialogue editor popup uses to "Header", moving the whole book root.</summary>
        internal static void MakeDraggable(Transform root)
        {
            var header = root.Find("Header");
            if (header == null) return;
            var drag = header.gameObject.GetComponent<global::FiresRPGmaker.UI.Screens.PopupDragHandler>()
                    ?? header.gameObject.AddComponent<global::FiresRPGmaker.UI.Screens.PopupDragHandler>();
            drag.target = root.GetComponent<RectTransform>();
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            MakeDraggable(t);
            _statusText  = FindTmp(t, "StatusText");

            _listContent = BuildPoolScroll(FindRect(t, "BuffListPool"));
            _rowTemplate = Find(t, "BuffRowTemplate");
            if (_rowTemplate != null) _rowTemplate.SetActive(false);

            _detailName = FindTmp(t, "DetailName");
            _detailDesc = FindTmp(t, "DetailDesc");
            _detailCost = FindTmp(t, "DetailCost");
            _durInfo    = FindTmp(t, "DurationInfo");
            _strInfo    = FindTmp(t, "StrengthInfo");
            _buyButton  = FindBtn(t, "BuyButton");
            if (_buyButton != null) { _buyButton.onClick.RemoveAllListeners(); _buyButton.onClick.AddListener(BuySelected); }
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selected = null;
            ShowDetails(null);
            RebuildList();
        }

        private void RebuildList()
        {
            if (_listContent == null || _rowTemplate == null) return;
            ClearRowClones(_listContent);

            var buffs = BufferManager.ForProfile(_profileName);
            if (buffs.Count == 0)
            {
                if (_statusText != null) _statusText.text = $"'{_profileName}' sells no buffs yet.";
                return;
            }
            if (_statusText != null) _statusText.text = $"{buffs.Count} buff(s) — click to view";
            foreach (var b in buffs) BuildListRow(b);
        }

        private void BuildListRow(FiresBuffData b)
        {
            var row = CloneRow(_listContent, _rowTemplate, 28f);
            if (row == null) return;
            var t = row.transform;
            var nameLabel = FindTmp(t, "NameLabel");
            if (nameLabel != null) nameLabel.text = string.IsNullOrEmpty(b.Name) ? b.UniqueName : b.Name;
            var costLabel = FindTmp(t, "CostLabel");
            if (costLabel != null) costLabel.text = CostText(b);
            var sel = FindBtn(t, "SelectButton");
            if (sel != null) { sel.onClick.RemoveAllListeners(); sel.onClick.AddListener(() => { _selected = b; ShowDetails(b); }); }
        }

        private void ShowDetails(FiresBuffData b)
        {
            if (b == null)
            {
                if (_detailName != null) _detailName.text = "Select a buff";
                if (_detailDesc != null) _detailDesc.text = "";
                if (_detailCost != null) _detailCost.text = "";
                if (_durInfo != null) _durInfo.text = "—";
                if (_strInfo != null) _strInfo.text = "—";
                if (_buyButton != null) _buyButton.interactable = false;
                return;
            }
            if (_detailName != null) _detailName.text = string.IsNullOrEmpty(b.Name) ? b.UniqueName : b.Name;
            if (_detailDesc != null) _detailDesc.text = DescribeEffect(b);
            if (_detailCost != null) _detailCost.text = "Cost: " + CostText(b);
            // Duration + strength are fixed by the admin's profile, shown read-only.
            if (_durInfo != null) _durInfo.text = FormatSecs(b.Duration);
            if (_strInfo != null) _strInfo.text = Mathf.RoundToInt((b.StrengthCap <= 0f ? 1f : Mathf.Clamp01(b.StrengthCap)) * 100f) + "%";
            if (_buyButton != null) _buyButton.interactable = true;
        }

        private void BuySelected()
        {
            if (_selected == null) { if (_statusText != null) _statusText.text = "Select a buff first."; return; }
            // 0 duration + full strength → the server applies the profile's authored Duration + StrengthCap.
            BufferNetwork.RequestApply(_profileName, _selected.UniqueName, 0, 1f);
            if (_statusText != null) _statusText.text = "Requested " + (string.IsNullOrEmpty(_selected.Name) ? _selected.UniqueName : _selected.Name) + "...";
        }

        private static string CostText(FiresBuffData b)
            => b.NeededPrefabCount > 0 ? $"{b.NeededPrefabCount} {b.NeededPrefab}" : "Free";

        private static string DescribeEffect(FiresBuffData b)
        {
            if (b.Backend == BuffBackend.TargetSE)
            {
                var se = StatusEffectCatalog.Resolve(b.TargetSE);
                return se != null && !string.IsNullOrEmpty(se.m_name) ? se.m_name : b.TargetSE;
            }
            var parts = new List<string>();
            void Mul(WhatToModify w, string label)
            {
                if (b.ModifyList.TryGetValue(w, out var v) && Mathf.Abs(v - 1f) > 0.001f)
                    parts.Add($"{label} {(v >= 1f ? "+" : "")}{Mathf.RoundToInt((v - 1f) * 100f)}%");
            }
            void Add(WhatToModify w, string label)
            {
                if (b.ModifyList.TryGetValue(w, out var v) && Mathf.Abs(v) > 0.001f)
                    parts.Add($"{label} {(v >= 0f ? "+" : "")}{v:0.##}");
            }
            Mul(WhatToModify.ModifyAttack, "Attack");
            Mul(WhatToModify.ModifySpeed, "Speed");
            Mul(WhatToModify.ModifyHealthRegen, "HP regen");
            Mul(WhatToModify.ModifyStaminaRegen, "Stam regen");
            Mul(WhatToModify.ModifyStealth, "Stealth");
            Mul(WhatToModify.ModifyNoise, "Noise");
            Mul(WhatToModify.ModifyRaiseSkills, "Skill gain");
            Add(WhatToModify.ModifyMaxCarryWeight, "Carry");
            if (b.ModifyList.TryGetValue(WhatToModify.DamageReduction, out var dr) && dr > 0.001f)
                parts.Add($"Dmg reduce {Mathf.RoundToInt(dr * 100f)}%");
            return parts.Count > 0 ? string.Join(", ", parts) : "Buff";
        }

        private static string FormatSecs(int s)
        {
            if (s < 60) return s + "s";
            int m = s / 60, r = s % 60;
            return r == 0 ? m + "m" : $"{m}m {r}s";
        }
    }
}
