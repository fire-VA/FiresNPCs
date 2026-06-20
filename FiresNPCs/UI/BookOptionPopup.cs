using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresCore.UI;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.UI.Screens;   // PopupDragHandler

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// The dialogue option editor popup — a runtime-built, draggable, auto-sizing panel ported from the working
    /// DialogueEditorScreenController.EnsureOptionEditorPopup, recolored to light parchment and operating directly
    /// on a <see cref="RawPlayerOption"/>. Positioned to the right of the book by default. Auto-sizes to its
    /// content (VerticalLayoutGroup + ContentSizeFitter) so nothing is clipped, and edits ALL option fields
    /// including Random Commands (whose + button was dead in the old baked popup).
    /// </summary>
    public class BookOptionPopup : MonoBehaviour
    {
        private const float POPUP_W = 372f, POPUP_LABEL_W = 78f, FONT = 13f, SMALL_FONT = 11f;

        // Parchment palette (the working popup was dark; the book is parchment).
        private static readonly Color PopupBg    = new Color(0.81f, 0.73f, 0.55f, 0.98f);
        private static readonly Color HeaderBg   = new Color(0.55f, 0.42f, 0.22f, 0.95f);
        private static readonly Color HeaderText = new Color(0.97f, 0.92f, 0.78f);
        private static readonly Color InputBg    = new Color(0.30f, 0.20f, 0.09f, 0.22f);
        private static readonly Color BtnNormal  = new Color(0.50f, 0.34f, 0.12f, 0.92f);
        private static readonly Color BtnHover   = new Color(0.60f, 0.44f, 0.18f, 0.95f);
        private static readonly Color BtnPress   = new Color(0.42f, 0.28f, 0.10f, 1f);
        private static readonly Color TextCol    = new Color(0.20f, 0.13f, 0.05f);
        private static readonly Color LabelCol   = new Color(0.36f, 0.26f, 0.12f);
        private static readonly Color PlaceCol   = new Color(0.45f, 0.36f, 0.22f, 0.7f);
        private static readonly Color OutlineCol = new Color(0.30f, 0.20f, 0.09f, 0.85f);

        private static readonly string[] OptionTypeNames = { "Option", "Interaction", "InRange", "OutRange" };
        // Same vocabularies as the working editor / the book.
        private static readonly string[] CommandNames =
        {
            "GiveItem", "RemoveItem", "GiveQuest", "RemoveQuest", "FinishQuest",
            "Teleport", "Spawn", "SpawnRaw", "SpawnXYZ", "Damage", "Heal", "GiveBuff",
            "AddPlayerKey", "RemovePlayerKey", "AddCustomPlayerKey", "RemoveCustomPlayerKey",
            "PlayAnimation", "PlaySound", "ConsoleCommand", "AddPin", "PingMap", "OpenUI",
            "EnterPassword", "SendWebhook"
        };
        private static readonly string[] ConditionNames =
        {
            "HasItem", "NotHasItem", "HasQuest", "NotHasQuest", "QuestFinished", "QuestNotFinished",
            "QuestProgressDone", "QuestProgressNotDone", "HasPlayerKey", "NotHasPlayerKey",
            "GlobalKey", "NotGlobalKey", "HasBuff", "NotHasBuff", "SkillMore", "SkillLess",
            "CustomPlayerKeyMore", "CustomPlayerKeyLess", "HealthMore", "HealthLess",
            "MaxHealthMore", "MaxHealthLess", "StaminaMore", "StaminaLess", "MaxStaminaMore",
            "MaxStaminaLess", "EitrMore", "EitrLess", "MaxEitrMore", "MaxEitrLess"
        };

        private GameObject _popupGo;
        private RectTransform _popupRect;
        private RawPlayerOption _opt;
        private Action _onDone;

        private TMP_Dropdown _typeDd;
        private TMP_InputField _textIn, _iconIn, _transIn, _overrideIn, _rIn, _gIn, _bIn;
        private Toggle _alwaysToggle;
        private Image _colorSwatch;
        private Transform _transContainer, _cmdContainer, _randCmdContainer, _condContainer;

        private class TransRow { public GameObject Root; public TMP_InputField Uid; }
        private class CmdRow   { public GameObject Root; public TMP_Dropdown Dd; public TMP_InputField P; }
        private class RandRow  { public GameObject Root; public TMP_InputField W; public TMP_Dropdown Dd; public TMP_InputField P; }
        private class CondRow  { public GameObject Root; public TMP_InputField G; public TMP_Dropdown Dd; public TMP_InputField P; }
        private readonly List<TransRow> _transRows = new List<TransRow>();
        private readonly List<CmdRow>   _cmdRows   = new List<CmdRow>();
        private readonly List<RandRow>  _randRows  = new List<RandRow>();
        private readonly List<CondRow>  _condRows  = new List<CondRow>();

        // ──────────────── public API ────────────────
        public bool IsOpen => _popupGo != null && _popupGo.activeSelf;

        /// <summary>Opens the popup editing <paramref name="option"/> in place. <paramref name="onDone"/> fires
        /// after the edits are written back (on Done). Positioned to the right of <paramref name="bookPanelRect"/>.</summary>
        public void Open(RawPlayerOption option, RectTransform bookPanelRect, Action onDone)
        {
            if (option == null) return;
            _opt = option;
            _onDone = onDone;
            EnsureBuilt(bookPanelRect);
            LoadFromOption(option);
            _popupGo.SetActive(true);
            _popupGo.transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_popupRect);
        }

        public void Close()
        {
            if (_popupGo != null) _popupGo.SetActive(false);
        }

        /// <summary>Writes pending UI edits back to the edited option without closing (book calls this before save).</summary>
        public void FlushIfOpen()
        {
            if (IsOpen && _opt != null) SaveToOption(_opt);
        }

        // ──────────────── build ────────────────
        private void EnsureBuilt(RectTransform bookPanelRect)
        {
            if (_popupGo != null) return;

            Transform parent = bookPanelRect != null ? bookPanelRect.parent : transform;
            _popupGo = new GameObject("BookOptionPopup");
            _popupGo.transform.SetParent(parent, false);

            _popupRect = _popupGo.AddComponent<RectTransform>();
            _popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            _popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            _popupRect.pivot = new Vector2(0f, 0.5f);
            if (bookPanelRect != null)
            {
                float panelRight = bookPanelRect.anchoredPosition.x + bookPanelRect.sizeDelta.x * (1f - bookPanelRect.pivot.x);
                _popupRect.anchoredPosition = new Vector2(panelRight + 12f, bookPanelRect.anchoredPosition.y);
            }
            _popupRect.sizeDelta = new Vector2(POPUP_W, 120f);   // height driven by ContentSizeFitter

            var bg = _popupGo.AddComponent<Image>();
            bg.color = PopupBg; bg.raycastTarget = true;
            var outline = _popupGo.AddComponent<Outline>();
            outline.effectColor = OutlineCol; outline.effectDistance = new Vector2(2f, -2f);
            _popupGo.AddComponent<CanvasGroup>();

            var vlg = _popupGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2; vlg.padding = new RectOffset(6, 6, 4, 6);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var csf = _popupGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeader(_popupGo.transform);
            _typeDd     = BuildDropdownRow(_popupGo.transform, "Type:", new List<string>(OptionTypeNames));
            _textIn     = BuildInputRow(_popupGo.transform, "Text:", "Button text shown to player");
            _iconIn     = BuildInputRow(_popupGo.transform, "Icon:", "item_prefab_name (optional)");
            _transIn    = BuildInputRow(_popupGo.transform, "Transition:", "dialogue_uid (empty = close)");
            _overrideIn = BuildInputRow(_popupGo.transform, "Err Override:", "shown when conditions fail");
            BuildAlwaysVisibleRow(_popupGo.transform);
            BuildColorRow(_popupGo.transform);

            BuildSectionHeader(_popupGo.transform, "Random Transition", () => { AddTransRow(""); Rebuild(); });
            _transContainer = BuildContainer(_popupGo.transform);
            BuildSectionHeader(_popupGo.transform, "Commands", () => { AddCmdRow("GiveItem", ""); Rebuild(); });
            _cmdContainer = BuildContainer(_popupGo.transform);
            BuildSectionHeader(_popupGo.transform, "Random Commands  (weight)", () => { AddRandRow(1, "GiveItem", ""); Rebuild(); });
            _randCmdContainer = BuildContainer(_popupGo.transform);
            BuildSectionHeader(_popupGo.transform, "Conditions  (group#)", () => { AddCondRow(0, "HasItem", ""); Rebuild(); });
            _condContainer = BuildContainer(_popupGo.transform);

            BuildButtonRow(_popupGo.transform);
            _popupGo.SetActive(false);
        }

        private void BuildHeader(Transform parent)
        {
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(parent, false);
            headerGo.AddComponent<LayoutElement>().preferredHeight = 22;
            var headerBg = headerGo.AddComponent<Image>();
            headerBg.color = HeaderBg; headerBg.raycastTarget = true;
            var drag = headerGo.AddComponent<PopupDragHandler>();
            drag.target = _popupRect;

            var titleGo = CreateChild(headerGo.transform, "TitleText");
            var tr = titleGo.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            var tmp = titleGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "Edit Option  (drag to move)"; tmp.fontSize = FONT; tmp.fontStyle = FontStyles.Bold;
            tmp.color = HeaderText; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
            ApplyFont(tmp);
        }

        private void BuildAlwaysVisibleRow(Transform parent)
        {
            var row = new GameObject("PR_AlwaysVisible");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6; hlg.padding = new RectOffset(2, 2, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var boxGo = new GameObject("Box");
            boxGo.transform.SetParent(row.transform, false);
            var boxLe = boxGo.AddComponent<LayoutElement>(); boxLe.preferredWidth = 16; boxLe.minWidth = 16;
            var boxImg = boxGo.AddComponent<Image>(); boxImg.color = InputBg; boxImg.raycastTarget = true;
            _alwaysToggle = boxGo.AddComponent<Toggle>();
            _alwaysToggle.targetGraphic = boxImg;
            var check = CreateChild(boxGo.transform, "Check");
            var cr = check.AddComponent<RectTransform>();
            cr.anchorMin = new Vector2(0.15f, 0.15f); cr.anchorMax = new Vector2(0.85f, 0.85f); cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
            var checkImg = check.AddComponent<Image>(); checkImg.color = new Color(0.60f, 0.44f, 0.16f, 0.95f); checkImg.raycastTarget = false;
            _alwaysToggle.graphic = checkImg;

            var lblGo = new GameObject("L");
            lblGo.transform.SetParent(row.transform, false);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = "Always Visible"; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineLeft; lt.raycastTarget = false;
            ApplyFont(lt);
        }

        private void BuildColorRow(Transform parent)
        {
            var row = new GameObject("PR_Color");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3; hlg.padding = new RectOffset(2, 2, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var lblGo = new GameObject("L");
            lblGo.transform.SetParent(row.transform, false);
            var lblLe = lblGo.AddComponent<LayoutElement>(); lblLe.preferredWidth = 64; lblLe.minWidth = 64;
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = "Color RGB"; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineLeft; lt.raycastTarget = false;
            ApplyFont(lt);

            _rIn = ColorInput(row.transform);
            _gIn = ColorInput(row.transform);
            _bIn = ColorInput(row.transform);

            var swGo = new GameObject("Swatch");
            swGo.transform.SetParent(row.transform, false);
            var swLe = swGo.AddComponent<LayoutElement>(); swLe.preferredWidth = 28; swLe.minWidth = 28;
            _colorSwatch = swGo.AddComponent<Image>(); _colorSwatch.color = Color.white;

            UnityEngine.Events.UnityAction<string> upd = _ => UpdateSwatch();
            _rIn.onValueChanged.AddListener(upd); _gIn.onValueChanged.AddListener(upd); _bIn.onValueChanged.AddListener(upd);
        }

        private TMP_InputField ColorInput(Transform parent)
        {
            var go = new GameObject("C");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 38; le.minWidth = 30;
            var f = BuildInputField(go, false, 22);
            f.contentType = TMP_InputField.ContentType.IntegerNumber;
            return f;
        }

        private void UpdateSwatch()
        {
            if (_colorSwatch == null) return;
            byte r = (byte)Mathf.Clamp(ParseInt(_rIn?.text, 255), 0, 255);
            byte g = (byte)Mathf.Clamp(ParseInt(_gIn?.text, 255), 0, 255);
            byte b = (byte)Mathf.Clamp(ParseInt(_bIn?.text, 255), 0, 255);
            _colorSwatch.color = new Color32(r, g, b, 255);
        }

        private Transform BuildContainer(Transform parent)
        {
            var c = new GameObject("Container");
            c.transform.SetParent(parent, false);
            var vlg = c.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2; vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var csf = c.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return c.transform;
        }

        private void BuildButtonRow(Transform parent)
        {
            var row = new GameObject("Buttons");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6; hlg.padding = new RectOffset(2, 2, 2, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            var doneGo = new GameObject("Done"); doneGo.transform.SetParent(row.transform, false);
            CreateStyledButton(doneGo, "Done", OnDone);
            var cancelGo = new GameObject("Cancel"); cancelGo.transform.SetParent(row.transform, false);
            CreateStyledButton(cancelGo, "Cancel", Close);
        }

        // ──────────────── section header + label rows ────────────────
        private void BuildSectionHeader(Transform parent, string label, UnityEngine.Events.UnityAction onAdd)
        {
            var row = new GameObject("SH_" + label);
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 18;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4; hlg.padding = new RectOffset(4, 4, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            var lblGo = new GameObject("L"); lblGo.transform.SetParent(row.transform, false);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol; lt.fontStyle = FontStyles.Bold;
            lt.alignment = TextAlignmentOptions.MidlineLeft; lt.raycastTarget = false;
            ApplyFont(lt);

            var addGo = new GameObject("Add"); addGo.transform.SetParent(row.transform, false);
            var addLe = addGo.AddComponent<LayoutElement>(); addLe.preferredWidth = 24; addLe.minWidth = 24;
            CreateStyledButton(addGo, "+", onAdd);
        }

        private TMP_InputField BuildInputRow(Transform parent, string label, string placeholder)
        {
            var row = new GameObject("PR_" + label);
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;

            var lblGo = CreateChild(row.transform, "L");
            var lr = lblGo.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(0, 1);
            lr.pivot = new Vector2(0, 0.5f); lr.anchoredPosition = Vector2.zero;
            lr.sizeDelta = new Vector2(POPUP_LABEL_W, 0);
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineRight; lt.raycastTarget = false;
            ApplyFont(lt);

            var inGo = CreateChild(row.transform, "I");
            var ir = inGo.AddComponent<RectTransform>();
            ir.anchorMin = new Vector2(0, 0); ir.anchorMax = new Vector2(1, 1);
            ir.offsetMin = new Vector2(POPUP_LABEL_W + 3, 0); ir.offsetMax = new Vector2(-2, 0);
            var field = BuildInputField(inGo, false, 22);
            SetPlaceholder(field, placeholder);
            return field;
        }

        private TMP_Dropdown BuildDropdownRow(Transform parent, string label, List<string> options)
        {
            var row = new GameObject("PR_" + label);
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;

            var lblGo = CreateChild(row.transform, "L");
            var lr = lblGo.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(0, 1);
            lr.pivot = new Vector2(0, 0.5f); lr.anchoredPosition = Vector2.zero;
            lr.sizeDelta = new Vector2(POPUP_LABEL_W, 0);
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineRight; lt.raycastTarget = false;
            ApplyFont(lt);

            var ddGo = CreateChild(row.transform, "D");
            var dr = ddGo.AddComponent<RectTransform>();
            dr.anchorMin = new Vector2(0, 0); dr.anchorMax = new Vector2(1, 1);
            dr.offsetMin = new Vector2(POPUP_LABEL_W + 3, 0); dr.offsetMax = new Vector2(-2, 0);
            return BuildDropdown(ddGo, options);
        }

        // ──────────────── dynamic list rows ────────────────
        private void AddTransRow(string uid)
        {
            var r = new TransRow();
            var row = NewListRow(_transContainer, out var hlg); r.Root = row;
            r.Uid = FlexInput(row.transform, "uid (random pick)", uid);
            AddRemoveX(row.transform, () => { _transRows.Remove(r); Destroy(r.Root); Rebuild(); });
            _transRows.Add(r);
        }

        private void AddCmdRow(string name, string parms)
        {
            var r = new CmdRow();
            var row = NewListRow(_cmdContainer, out var hlg); r.Root = row;
            r.Dd = FixedDropdown(row.transform, 108, CommandNames, name);
            r.P = FlexInput(row.transform, "params", parms);
            AddRemoveX(row.transform, () => { _cmdRows.Remove(r); Destroy(r.Root); Rebuild(); });
            _cmdRows.Add(r);
        }

        private void AddRandRow(int weight, string name, string parms)
        {
            var r = new RandRow();
            var row = NewListRow(_randCmdContainer, out var hlg); r.Root = row;
            r.W = FixedInput(row.transform, 30, "w", weight.ToString());
            r.W.contentType = TMP_InputField.ContentType.IntegerNumber;
            r.Dd = FixedDropdown(row.transform, 98, CommandNames, name);
            r.P = FlexInput(row.transform, "params", parms);
            AddRemoveX(row.transform, () => { _randRows.Remove(r); Destroy(r.Root); Rebuild(); });
            _randRows.Add(r);
        }

        private void AddCondRow(int group, string name, string parms)
        {
            var r = new CondRow();
            var row = NewListRow(_condContainer, out var hlg); r.Root = row;
            r.G = FixedInput(row.transform, 26, "g", group.ToString());
            r.G.contentType = TMP_InputField.ContentType.IntegerNumber;
            r.Dd = FixedDropdown(row.transform, 116, ConditionNames, name);
            r.P = FlexInput(row.transform, "params", parms);
            AddRemoveX(row.transform, () => { _condRows.Remove(r); Destroy(r.Root); Rebuild(); });
            _condRows.Add(r);
        }

        private GameObject NewListRow(Transform container, out HorizontalLayoutGroup hlg)
        {
            var row = new GameObject("Row");
            row.transform.SetParent(container, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;
            hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            return row;
        }

        private TMP_InputField FixedInput(Transform parent, float w, string ph, string val)
        {
            var go = new GameObject("F"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = w; le.minWidth = w;
            var f = BuildInputField(go, false, 22); SetPlaceholder(f, ph); f.SetTextWithoutNotify(val);
            return f;
        }
        private TMP_InputField FlexInput(Transform parent, string ph, string val)
        {
            var go = new GameObject("F"); go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1;
            var f = BuildInputField(go, false, 22); SetPlaceholder(f, ph); f.SetTextWithoutNotify(val);
            return f;
        }
        private TMP_Dropdown FixedDropdown(Transform parent, float w, string[] options, string selected)
        {
            var go = new GameObject("D"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = w; le.minWidth = w;
            var dd = BuildDropdown(go, new List<string>(options));
            int idx = Array.FindIndex(options, x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
            dd.SetValueWithoutNotify(idx >= 0 ? idx : 0);
            return dd;
        }
        private void AddRemoveX(Transform parent, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("X"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 18; le.minWidth = 18;
            CreateStyledButton(go, "x", onClick);
        }

        private void Rebuild()
        {
            if (_popupRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_popupRect);
        }

        // ──────────────── load / save (RawPlayerOption) ────────────────
        private void LoadFromOption(RawPlayerOption o)
        {
            _typeDd.SetValueWithoutNotify(Mathf.Clamp((int)o.Type, 0, OptionTypeNames.Length - 1));
            _textIn.SetTextWithoutNotify(o.Text ?? "");
            _iconIn.SetTextWithoutNotify(o.Icon ?? "");
            _transIn.SetTextWithoutNotify(o.NextUID ?? "");
            _overrideIn.SetTextWithoutNotify(o.OverrideError ?? "");
            if (_alwaysToggle != null) _alwaysToggle.isOn = o.AlwaysVisible;
            _rIn.SetTextWithoutNotify(o.Color.r.ToString());
            _gIn.SetTextWithoutNotify(o.Color.g.ToString());
            _bIn.SetTextWithoutNotify(o.Color.b.ToString());
            UpdateSwatch();

            ClearRows();
            if (o.RandomTransition != null) foreach (var uid in o.RandomTransition) AddTransRow(uid);
            if (o.Commands != null) foreach (var cmd in o.Commands) { Split(cmd, out var n, out var p); AddCmdRow(n, p); }
            if (o.RandomCommands != null) foreach (var rc in o.RandomCommands) { Split(rc.Value, out var n, out var p); AddRandRow(rc.Key, n, p); }
            if (o.Conditions != null) for (int g = 0; g < o.Conditions.Count; g++) { var grp = o.Conditions[g]; if (grp == null) continue; foreach (var m in grp) { Split(m, out var n, out var p); AddCondRow(g, n, p); } }
            Rebuild();
        }

        private void SaveToOption(RawPlayerOption o)
        {
            o.Type = (InteractionType)Mathf.Clamp(_typeDd.value, 0, OptionTypeNames.Length - 1);
            o.Text = _textIn.text ?? "";
            o.Icon = _iconIn.text?.Trim() ?? "";
            o.NextUID = (_transIn.text ?? "").Replace(" ", "");
            o.OverrideError = _overrideIn.text ?? "";
            if (_alwaysToggle != null) o.AlwaysVisible = _alwaysToggle.isOn;
            o.Color = new Color32(
                (byte)Mathf.Clamp(ParseInt(_rIn.text, 255), 0, 255),
                (byte)Mathf.Clamp(ParseInt(_gIn.text, 255), 0, 255),
                (byte)Mathf.Clamp(ParseInt(_bIn.text, 255), 0, 255), 255);

            o.RandomTransition = _transRows.Select(r => r.Uid.text?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

            o.Commands = _cmdRows
                .Select(r => Join(DdText(r.Dd, CommandNames), r.P.text))
                .Where(s => !string.IsNullOrEmpty(s)).ToArray();

            var rcmds = new List<KeyValuePair<int, string>>();
            foreach (var r in _randRows)
            {
                int w = ParseInt(r.W.text, 0);
                string n = DdText(r.Dd, CommandNames);
                if (w > 0 && !string.IsNullOrEmpty(n)) rcmds.Add(new KeyValuePair<int, string>(w, Join(n, r.P.text)));
            }
            o.RandomCommands = rcmds;

            var groups = new SortedDictionary<int, List<string>>();
            foreach (var r in _condRows)
            {
                string n = DdText(r.Dd, ConditionNames);
                if (string.IsNullOrEmpty(n)) continue;
                int gi = ParseInt(r.G.text, 0);
                if (!groups.ContainsKey(gi)) groups[gi] = new List<string>();
                groups[gi].Add(Join(n, r.P.text));
            }
            o.Conditions = groups.Values.Select(v => v.ToArray()).ToList();
        }

        private void OnDone()
        {
            if (_opt != null) SaveToOption(_opt);
            Close();
            _onDone?.Invoke();
        }

        private void ClearRows()
        {
            foreach (var r in _transRows) if (r.Root != null) Destroy(r.Root);
            foreach (var r in _cmdRows) if (r.Root != null) Destroy(r.Root);
            foreach (var r in _randRows) if (r.Root != null) Destroy(r.Root);
            foreach (var r in _condRows) if (r.Root != null) Destroy(r.Root);
            _transRows.Clear(); _cmdRows.Clear(); _randRows.Clear(); _condRows.Clear();
        }

        // Split "Name,params" on the FIRST comma only (no colon replace — preserves URLs/colons).
        private static void Split(string s, out string name, out string parms)
        {
            s = s ?? "";
            int c = s.IndexOf(',');
            if (c < 0) { name = s.Trim(); parms = ""; }
            else { name = s.Substring(0, c).Trim(); parms = s.Substring(c + 1); }
        }
        private static string Join(string name, string parms) => string.IsNullOrEmpty(parms) ? name : name + "," + parms;
        private static string DdText(TMP_Dropdown dd, string[] names)
            => (dd != null && dd.value >= 0 && dd.value < names.Length) ? names[dd.value] : "";
        private static int ParseInt(string s, int def) => int.TryParse((s ?? "").Trim(), out var v) ? v : def;

        // ──────────────── construction primitives (ported from the working popup, recolored) ────────────────
        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void ApplyFont(TMP_Text text)
        {
            if (text == null) return;
            try { UIBuilderHelper.ApplyBodyFont(text); } catch { }
        }

        private static void SetPlaceholder(TMP_InputField input, string text)
        {
            if (input?.placeholder == null) return;
            var ph = input.placeholder as TMP_Text;
            if (ph != null) ph.text = text;
        }

        private Button CreateStyledButton(GameObject go, string label, UnityEngine.Events.UnityAction onClick)
        {
            var bgImg = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            bgImg.color = BtnNormal; bgImg.raycastTarget = true;
            var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            var cols = btn.colors;
            cols.normalColor = BtnNormal; cols.highlightedColor = BtnHover; cols.pressedColor = BtnPress;
            cols.selectedColor = BtnNormal; cols.fadeDuration = 0.08f;
            btn.colors = cols;

            var tGo = CreateChild(go.transform, "Text");
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one; tR.offsetMin = Vector2.zero; tR.offsetMax = Vector2.zero;
            var tmp = tGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = FONT; tmp.color = HeaderText;
            tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
            ApplyFont(tmp);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        private TMP_InputField BuildInputField(GameObject go, bool multi, float height)
        {
            var bgImg = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            bgImg.color = InputBg; bgImg.raycastTarget = true;

            var vp = CreateChild(go.transform, "Viewport");
            var vpR = vp.AddComponent<RectTransform>();
            vpR.anchorMin = Vector2.zero; vpR.anchorMax = Vector2.one;
            vpR.offsetMin = new Vector2(4, 2); vpR.offsetMax = new Vector2(-4, -2);
            vp.AddComponent<RectMask2D>();

            var tGo = CreateChild(vp.transform, "Text");
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one; tR.offsetMin = Vector2.zero; tR.offsetMax = Vector2.zero;
            var txt = tGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = FONT; txt.color = TextCol;
            txt.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            txt.textWrappingMode = multi ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            txt.raycastTarget = false; ApplyFont(txt);

            var phGo = CreateChild(vp.transform, "Placeholder");
            var phR = phGo.AddComponent<RectTransform>();
            phR.anchorMin = Vector2.zero; phR.anchorMax = Vector2.one; phR.offsetMin = Vector2.zero; phR.offsetMax = Vector2.zero;
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.text = ""; ph.fontSize = FONT; ph.color = PlaceCol; ph.fontStyle = FontStyles.Italic;
            ph.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            ph.raycastTarget = false; ApplyFont(ph);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = vpR; input.textComponent = txt; input.placeholder = ph;
            input.fontAsset = txt.font; input.pointSize = FONT;
            input.lineType = multi ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.characterLimit = multi ? 2000 : 300;
            input.caretColor = new Color(0.20f, 0.13f, 0.05f, 1f); input.caretWidth = 2; input.customCaretColor = true;
            input.selectionColor = new Color(0.62f, 0.45f, 0.16f, 0.5f); input.interactable = true;
            return input;
        }

        private TMP_Dropdown BuildDropdown(GameObject go, List<string> options)
        {
            var bgImg = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            bgImg.color = InputBg; bgImg.raycastTarget = true;
            var dd = go.AddComponent<TMP_Dropdown>();

            var labelGo = CreateChild(go.transform, "Label");
            var labelR = labelGo.AddComponent<RectTransform>();
            labelR.anchorMin = Vector2.zero; labelR.anchorMax = Vector2.one;
            labelR.offsetMin = new Vector2(6, 2); labelR.offsetMax = new Vector2(-16, -2);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.fontSize = SMALL_FONT; labelTmp.color = TextCol;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft; labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.raycastTarget = false; ApplyFont(labelTmp);
            dd.captionText = labelTmp;

            var arrowGo = CreateChild(go.transform, "Arrow");
            var arrowR = arrowGo.AddComponent<RectTransform>();
            arrowR.anchorMin = new Vector2(1, 0.5f); arrowR.anchorMax = new Vector2(1, 0.5f); arrowR.pivot = new Vector2(1, 0.5f);
            arrowR.anchoredPosition = new Vector2(-3, 0); arrowR.sizeDelta = new Vector2(12, 12);
            var arrowTxt = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowTxt.text = "v"; arrowTxt.fontSize = 9; arrowTxt.color = LabelCol;
            arrowTxt.alignment = TextAlignmentOptions.Center; arrowTxt.raycastTarget = false; ApplyFont(arrowTxt);

            var template = CreateChild(go.transform, "Template");
            var templateR = template.AddComponent<RectTransform>();
            templateR.anchorMin = new Vector2(0, 0); templateR.anchorMax = new Vector2(1, 0); templateR.pivot = new Vector2(0.5f, 1);
            templateR.anchoredPosition = Vector2.zero; templateR.sizeDelta = new Vector2(0, 150);
            template.AddComponent<Image>().color = new Color(0.85f, 0.78f, 0.62f, 0.99f);
            var templateScroll = template.AddComponent<ScrollRect>();

            var vpGo = CreateChild(template.transform, "Viewport");
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one; vpRt.offsetMin = new Vector2(2, 2); vpRt.offsetMax = new Vector2(-2, -2);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = CreateChild(vpGo.transform, "Content");
            var contentR = contentGo.AddComponent<RectTransform>();
            contentR.anchorMin = new Vector2(0, 1); contentR.anchorMax = new Vector2(1, 1); contentR.pivot = new Vector2(0, 1);
            contentR.anchoredPosition = Vector2.zero; contentR.sizeDelta = Vector2.zero;
            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 1; contentVlg.childControlWidth = true; contentVlg.childControlHeight = false;
            contentVlg.childForceExpandWidth = true; contentVlg.childForceExpandHeight = false;
            var contentFitter = contentGo.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            templateScroll.viewport = vpRt; templateScroll.content = contentR;
            templateScroll.horizontal = false; templateScroll.vertical = true; templateScroll.movementType = ScrollRect.MovementType.Clamped;

            var itemGo = CreateChild(contentGo.transform, "Item");
            var itemR = itemGo.AddComponent<RectTransform>(); itemR.sizeDelta = new Vector2(0, 22);
            var itemLe = itemGo.AddComponent<LayoutElement>(); itemLe.preferredHeight = 22; itemLe.flexibleWidth = 1;
            var itemBg = itemGo.AddComponent<Image>(); itemBg.color = new Color(0.74f, 0.66f, 0.50f, 0.95f);
            var itemToggle = itemGo.AddComponent<Toggle>(); itemToggle.targetGraphic = itemBg;

            var itemLabelGo = CreateChild(itemGo.transform, "Item Label");
            var itemLabelR = itemLabelGo.AddComponent<RectTransform>();
            itemLabelR.anchorMin = Vector2.zero; itemLabelR.anchorMax = Vector2.one; itemLabelR.offsetMin = new Vector2(6, 0); itemLabelR.offsetMax = new Vector2(-6, 0);
            var itemLabel = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabel.fontSize = FONT; itemLabel.color = TextCol; itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
            itemLabel.textWrappingMode = TextWrappingModes.NoWrap; itemLabel.raycastTarget = false; ApplyFont(itemLabel);

            dd.template = templateR; dd.itemText = itemLabel;
            template.SetActive(false);
            dd.ClearOptions(); dd.AddOptions(options);
            return dd;
        }
    }
}
