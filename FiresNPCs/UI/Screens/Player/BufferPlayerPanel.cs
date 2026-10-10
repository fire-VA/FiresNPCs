using System.Collections.Generic;
using FiresRPGmaker.Modules.Buffer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Player-facing buff shop. 10-09 (Fire approved the parchment mockup, Tools\Mockups\rpg_buffer_player_mockup.png):
    /// one table, a row per buff the Buffer NPC's profile sells: icon, name, effect, duration, strength, cost and its own Buy
    /// (greyed with a tooltip when the bag can't pay). Clicking a row shows it on the "Selected" line; the footer shows what
    /// the bag holds of the profile's currencies. Buy routes through the server-authoritative grant
    /// (<see cref="BufferNetwork.RequestApply"/>): the server applies the profile's authored duration + strength.
    /// Layout lives in the FiresBufferPanel prefab (WishboneNpcPanels builder).</summary>
    public class BufferPlayerPanel : BasePlayerModulePanel
    {
        private static BufferPlayerPanel _instance;
        protected override string PanelTitle => "Buffs";
        protected override Vector2 PanelSize => new Vector2(780f, 450f);
        protected override string PrefabName => "FiresBufferPanel.prefab";

        private static readonly Color Ink = new Color(0.24f, 0.16f, 0.08f, 1f);
        private static readonly Color Good = new Color(0.23f, 0.36f, 0.07f, 1f);
        private static readonly Color Bad = new Color(0.60f, 0.16f, 0.09f, 1f);
        private static readonly Color SelectedRow = new Color(1f, 0.97f, 0.88f, 0.75f);

        private string _profileName, _npcName;
        private RectTransform _listContent;
        private GameObject _rowTemplate;
        private TMP_Text _statusText, _footerRight, _subtitle;
        private RectTransform _tipBox;

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

        /// <summary>Makes the baked popup draggable by its title bar: attaches the same PopupDragHandler
        /// the dialogue editor popup uses to "Header", moving the whole root.</summary>
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
            _footerRight = FindTmp(t, "FooterRightText");
            _subtitle    = FindTmp(t, "SubtitleText");
            _tipBox      = FindRect(t, "TooltipBox");

            _listContent = BuildPoolScroll(FindRect(t, "BuffListPool"));
            _rowTemplate = Find(t, "BuffRowTemplate");
            if (_rowTemplate != null) _rowTemplate.SetActive(false);
            if (_listContent != null) { var vlg = _listContent.GetComponent<VerticalLayoutGroup>(); if (vlg != null) { vlg.spacing = 0f; vlg.padding = new RectOffset(0, 0, 0, 0); } }

            _detailName = FindTmp(t, "DetailName");
            _detailDesc = FindTmp(t, "DetailDesc");
            _detailCost = FindTmp(t, "DetailCost");
            _durInfo    = FindTmp(t, "DurationInfo");
            _strInfo    = FindTmp(t, "StrengthInfo");
            _buyButton  = FindBtn(t, "BuyButton");   // legacy single Buy (hidden in the 10-09 layout; rows carry their own)
            if (_buyButton != null) { _buyButton.onClick.RemoveAllListeners(); _buyButton.onClick.AddListener(() => Buy(_selected)); }
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _npcName = npcDisplayName;
            _selected = null;
            RebuildList();
            ShowDetails(null);
        }

        private void RebuildList()
        {
            if (_listContent == null || _rowTemplate == null) return;
            ClearRowClones(_listContent);

            var buffs = BufferManager.ForProfile(_profileName);
            if (_subtitle != null) _subtitle.text = $"{_npcName} · profile '{_profileName}' · {buffs.Count} buff{(buffs.Count == 1 ? "" : "s")}";
            UpdateBagLine(buffs);
            if (buffs.Count == 0)
            {
                Status($"'{_profileName}' sells no buffs yet.", false);
                return;
            }
            Status("Click a buff to read it, Buy to get it.", true);
            foreach (var b in buffs) BuildListRow(b);
        }

        private void BuildListRow(FiresBuffData b)
        {
            var row = CloneRow(_listContent, _rowTemplate, 40f);
            if (row == null) return;
            var t = row.transform;
            SetText(t, "NameLabel", DisplayName(b));
            SetText(t, "EffectLabel", DescribeEffect(b));
            SetText(t, "DurLabel", FormatClock(b.Duration));
            SetText(t, "StrLabel", Mathf.RoundToInt(StrengthOf(b) * 100f) + "%");
            bool canPay = CanPay(b, out int have);
            var cost = FindTmp(t, "CostLabel");
            if (cost != null) { cost.text = CostText(b); cost.color = b.NeededPrefabCount <= 0 ? Good : canPay ? Ink : Bad; }
            var icon = Find(t, "Icon")?.GetComponent<Image>();
            if (icon != null) { var s = IconFor(b); icon.sprite = s; icon.enabled = s != null; }

            var bg = Find(t, "SelectButton");
            var sel = bg != null ? bg.GetComponent<Button>() : null;
            if (sel != null) { sel.onClick.RemoveAllListeners(); sel.onClick.AddListener(() => { _selected = b; ShowDetails(b); HighlightRows(); }); }
            var tag = row.GetComponent<BuffRowTag>() ?? row.AddComponent<BuffRowTag>();
            tag.Buff = b;

            var buy = FindBtn(t, "RowBuyButton");
            if (buy != null)
            {
                buy.interactable = canPay;
                buy.onClick.RemoveAllListeners();
                buy.onClick.AddListener(() => { _selected = b; ShowDetails(b); HighlightRows(); Buy(b); });
                PanelHoverTip.Set(buy.gameObject, _tipBox, canPay ? null : $"You have {have} {ItemName(b.NeededPrefab)} (need {b.NeededPrefabCount})");
            }
        }

        private sealed class BuffRowTag : MonoBehaviour { public FiresBuffData Buff; }

        private void HighlightRows()
        {
            if (_listContent == null) return;
            foreach (Transform row in _listContent)
            {
                var tag = row.GetComponent<BuffRowTag>();
                var bg = row.Find("SelectButton")?.GetComponent<Image>();
                if (tag == null || bg == null) continue;
                bg.color = tag.Buff == _selected ? SelectedRow : new Color(1f, 1f, 1f, 0f);
            }
        }

        private void ShowDetails(FiresBuffData b)
        {
            if (b == null)
            {
                SetLabel(_detailName, "none, click a row");
                SetLabel(_detailDesc, "");
                SetLabel(_detailCost, "");
                SetLabel(_durInfo, "-");
                SetLabel(_strInfo, "-");
                if (_buyButton != null) _buyButton.interactable = false;
                return;
            }
            SetLabel(_detailName, DisplayName(b));
            var parts = new List<string> { DescribeEffect(b) };
            if (!string.IsNullOrEmpty(b.BuffGroup)) parts.Add($"group '{b.BuffGroup}' (replaces another {b.BuffGroup} buff)");
            parts.Add("cost taken on Buy");
            SetLabel(_detailDesc, string.Join(" · ", parts));
            SetLabel(_detailCost, FormatClock(b.Duration) + " · " + Mathf.RoundToInt(StrengthOf(b) * 100f) + "% · " + CostText(b));
            SetLabel(_durInfo, FormatClock(b.Duration));
            SetLabel(_strInfo, Mathf.RoundToInt(StrengthOf(b) * 100f) + "%");
            if (_buyButton != null) _buyButton.interactable = CanPay(b, out _);
        }

        private void Buy(FiresBuffData b)
        {
            if (b == null) { Status("Select a buff first.", false); return; }
            if (!CanPay(b, out int have)) { Status($"Need {b.NeededPrefabCount} {ItemName(b.NeededPrefab)} (have {have}).", false); return; }
            // 0 duration + full strength » the server applies the profile's authored Duration + StrengthCap.
            BufferNetwork.RequestApply(_profileName, b.UniqueName, 0, 1f);
            Status("Asked for " + DisplayName(b) + " (" + FormatClock(b.Duration) + ").", true);
            // The cost leaves the bag once the server grants it; refresh the affordability shortly after.
            Invoke(nameof(RefreshAfterBuy), 0.8f);
        }

        private void RefreshAfterBuy() { if (IsOpen) { var keep = _selected; RebuildList(); _selected = keep; ShowDetails(keep); HighlightRows(); } }

        private void UpdateBagLine(List<FiresBuffData> buffs)
        {
            if (_footerRight == null) return;
            var player = Player.m_localPlayer;
            var seen = new List<string>();
            foreach (var b in buffs)
                if (b.NeededPrefabCount > 0 && !string.IsNullOrEmpty(b.NeededPrefab) && !seen.Contains(b.NeededPrefab)) seen.Add(b.NeededPrefab);
            if (player == null || seen.Count == 0) { _footerRight.text = ""; return; }
            var parts = new List<string>();
            foreach (var p in seen) parts.Add(CountInBag(player, p) + " " + ItemName(p));
            _footerRight.text = "In bag: " + string.Join(" · ", parts);
        }

        private void Status(string s, bool ok)
        {
            if (_statusText == null) return;
            _statusText.text = s;
            _statusText.color = ok ? Good : Bad;
        }

        // ── helpers ──
        private static void SetText(Transform t, string name, string s) { var l = FindTmp(t, name); if (l != null) l.text = s; }
        private static void SetLabel(TMP_Text l, string s) { if (l != null) l.text = s; }
        private static string DisplayName(FiresBuffData b) => string.IsNullOrEmpty(b.Name) ? b.UniqueName : b.Name;
        private static float StrengthOf(FiresBuffData b) => b.StrengthCap <= 0f ? 1f : Mathf.Clamp01(b.StrengthCap);

        private static bool CanPay(FiresBuffData b, out int have)
        {
            have = 0;
            if (b.NeededPrefabCount <= 0 || string.IsNullOrEmpty(b.NeededPrefab)) return true;
            var player = Player.m_localPlayer;
            if (player == null) return false;
            have = CountInBag(player, b.NeededPrefab);
            return have >= b.NeededPrefabCount;
        }

        private static int CountInBag(Player player, string prefab)
        {
            int n = 0;
            foreach (var it in player.GetInventory().GetAllItems())
                if (it?.m_dropPrefab != null && string.Equals(it.m_dropPrefab.name, prefab, System.StringComparison.OrdinalIgnoreCase)) n += it.m_stack;
            return n;
        }

        private static string ItemName(string prefab)
        {
            var drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            if (drop == null) return prefab;
            string n = drop.m_itemData.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(n) : n;
        }

        private static Sprite IconFor(FiresBuffData b)
        {
            try
            {
                if (!string.IsNullOrEmpty(b.SpritePrefab) && ObjectDB.instance != null)
                {
                    var drop = ObjectDB.instance.GetItemPrefab(b.SpritePrefab)?.GetComponent<ItemDrop>();
                    if (drop != null) return drop.m_itemData.GetIcon();
                }
                if (b.Backend == BuffBackend.TargetSE)
                {
                    var se = StatusEffectCatalog.Resolve(b.TargetSE);
                    if (se != null && se.m_icon != null) return se.m_icon;
                }
                if (!string.IsNullOrEmpty(b.NeededPrefab) && ObjectDB.instance != null)
                {
                    var drop = ObjectDB.instance.GetItemPrefab(b.NeededPrefab)?.GetComponent<ItemDrop>();
                    if (drop != null) return drop.m_itemData.GetIcon();
                }
            }
            catch { }
            return null;
        }

        private static string CostText(FiresBuffData b)
            => b.NeededPrefabCount > 0 ? $"{b.NeededPrefabCount} {ItemName(b.NeededPrefab)}" : "Free";

        internal static string DescribeEffect(FiresBuffData b)
        {
            if (b.Backend == BuffBackend.TargetSE)
            {
                var se = StatusEffectCatalog.Resolve(b.TargetSE);
                string n = se != null && !string.IsNullOrEmpty(se.m_name) ? se.m_name : b.TargetSE;
                return Localization.instance != null ? Localization.instance.Localize(n) : n;
            }
            var parts = new List<string>();
            void Mul(WhatToModify w, string label)
            {
                if (b.ModifyList.TryGetValue(w, out var v) && Mathf.Abs(v - 1f) > 0.001f) parts.Add($"{label} ×{v:0.##}");
            }
            Mul(WhatToModify.ModifyAttack, "Attack");
            Mul(WhatToModify.ModifySpeed, "Speed");
            Mul(WhatToModify.ModifyHealthRegen, "HP regen");
            Mul(WhatToModify.ModifyStaminaRegen, "Stamina regen");
            Mul(WhatToModify.RunStaminaDrain, "Run drain");
            Mul(WhatToModify.ModifyStealth, "Stealth");
            Mul(WhatToModify.ModifyNoise, "Noise");
            Mul(WhatToModify.ModifyRaiseSkills, "Skill gain");
            if (b.ModifyList.TryGetValue(WhatToModify.ModifyMaxCarryWeight, out var carry) && Mathf.Abs(carry) > 0.001f)
                parts.Add($"Carry {(carry >= 0f ? "+" : "")}{carry:0.##}");
            if (b.ModifyList.TryGetValue(WhatToModify.DamageReduction, out var dr) && dr > 0.001f)
                parts.Add($"Damage taken -{Mathf.RoundToInt(dr * 100f)}%");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Buff";
        }

        private static string FormatClock(int s)
        {
            if (s <= 0) return "-";
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
