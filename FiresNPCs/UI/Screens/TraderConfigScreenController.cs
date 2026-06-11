using System.Linq;
using FiresRPGmaker.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Admin Trader-config screen (the NPC panel's Trader tab). Edits a trader profile: the profile name
    /// + its trades (one .cfg line per trade); Apply writes FiresRPGmaker/Traders/&lt;name&gt;.cfg and assigns
    /// the profile to the NPC. Programmatic CreateForm is the source layout (captured -> baked later, then
    /// laid out around the trader PNG); WireFromCodegen is stubbed for the bake step.
    ///
    /// Trade-line format (same as the .cfg, round-trips via TraderProfileManager.SerializeTrades/ParseTradeLine):
    ///   legacy:  cost, amount, reward, amount[, level]
    ///   multi:   cost,amt[,lvl], ...  =  reward,amt[,lvl], ...
    ///   + optional trailing  , Stock=X, Restock=Y
    /// </summary>
    public class TraderConfigScreenController : MonoBehaviour
    {
        private NpcController _currentNpc;
        private TMP_InputField _profileInput;
        private TMP_InputField _tradesInput;
        private TMP_Text _statusText;
        private Button _applyButton;
        private TMP_Dropdown _savedProfilesDropdown;
        private bool _built;

        public bool IsVisible => gameObject.activeSelf;

        public void WireFromCodegen(GameObject screenGo, Button applyBtn) => BuildForm(applyBtn);

        public void BuildForm(Button applyButton)
        {
            if (_built) return;
            _built = true;

            // The container rect is set by the wiring (mirrors the baked QuestConfigScreen so this lands
            // in the content area below the tabs). Do NOT override it here — just lay out children inside it.
            // The panel Header already shows "Setup Trader: <npc>", so there is no in-screen title.
            var rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            // Layout bands (Y fractions of the content rect, 0 = bottom, 1 = top of form area).
            // Designed so labels sit BETWEEN their neighbouring elements and the Trades input can
            // grow tall without colliding with the Apply button at the bottom:
            //   tabs   ────────────────────── ~0.95 (above the form)
            //   "Trader Profile" hint label   0.83-0.89 (nudged down a bit from the prior 0.84)
            //   Profile input (left 62%) +
            //     Saved Profiles dropdown (right 36%)   both at 0.76-0.83
            //   "Trades" hint label           0.71-0.755 (tight against profile input bottom)
            //   Trades input (scrollable)     0.20-0.705 (largest area, vertical scrollbar on right)
            //   Status / "Saved..." text      0.12-0.18
            //   Apply button                  bottom (anchored outside this rect)
            CreateLabel(rt, "Trader Profile  (apply existing or create new)", 0.83f, 0.89f, 14f, TextAlignmentOptions.Left);
            _profileInput = CreateInput(rt, 0.02f, 0.64f, 0.76f, 0.83f, false);
            _profileInput.onEndEdit.AddListener(LoadProfileTrades);

            _savedProfilesDropdown = BuildSavedProfilesDropdown(rt, 0.66f, 0.98f, 0.76f, 0.83f);

            CreateLabel(rt, "Trades  -  one per line:   cost, amount, reward, amount[, level]", 0.71f, 0.755f, 14f, TextAlignmentOptions.Left);
            _tradesInput = CreateInput(rt, 0.02f, 0.98f, 0.20f, 0.705f, true);

            _statusText = CreateLabel(rt, "", 0.12f, 0.18f, 13f, TextAlignmentOptions.Left);

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            string name = npc?.traderProfile ?? "";
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(name);
            RefreshSavedProfilesDropdown(name);
            LoadProfileTrades(name);
            gameObject.SetActive(true);
            if (_applyButton != null) _applyButton.gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (_applyButton != null) _applyButton.gameObject.SetActive(false);
        }

        public void ApplyChanges()
        {
            if (_currentNpc == null) { SetStatus("No NPC selected."); return; }
            string name = (_profileInput != null ? _profileInput.text : "").Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("Enter a profile name first."); return; }
            // Strip any MP-style `=flag` suffix the user may have typed/auto-loaded so the profile name is canonical.
            int nameEq = name.IndexOf('='); if (nameEq >= 0) name = name.Substring(0, nameEq).Trim();

            TraderProfileManager.SaveProfile(name, _tradesInput != null ? _tradesInput.text : "");
            _currentNpc.traderProfile = name;
            // CRITICAL: also commit the NPC type to Trader — otherwise HandleNormalInteraction (Player E)
            // falls through to the default branch and never opens the trader UI even though the profile is set.
            _currentNpc.npcType = global::FiresCore.Npc.NpcType.Trader;
            _currentNpc.SaveToZDO();
            var profile = TraderProfileManager.GetProfile(name);
            SetStatus($"Saved '{name}' ({profile?.Trades.Count ?? 0} trades) and assigned this NPC as Trader.");
            // A new profile may have just been created — refresh the dropdown so it shows up.
            RefreshSavedProfilesDropdown(name);
            Debug.Log($"[TraderConfig] Apply: npc={_currentNpc.GetDisplayName()} → traderProfile='{name}', npcType=Trader");
        }

        private void LoadProfileTrades(string name)
        {
            name = (name ?? "").Trim();
            TraderProfileManager.Initialize();
            var profile = string.IsNullOrEmpty(name) ? null : TraderProfileManager.GetProfile(name);
            if (profile != null)
            {
                if (_tradesInput != null) _tradesInput.SetTextWithoutNotify(TraderProfileManager.SerializeTrades(profile));
                SetStatus($"Loaded '{profile.Name}' ({profile.Trades.Count} trades). Edit + Apply to save.");
            }
            else
            {
                // Don't enumerate existing profile names here — the Saved Profiles dropdown handles that.
                SetStatus("New profile - add trade lines above, then Apply.");
            }
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // --- label + input builders ---
        //
        // Labels read as ink-black on the parchment (the prior tan-on-parchment was nearly invisible).
        // The trades input is given a real Scrollbar wired through TMP_InputField.verticalScrollbar
        // so long profile lists (44+ trades in the screenshot) scroll properly. Scrollbar is built
        // via FiresCore.UI.UIBuilderHelper.CreateVerticalScrollbar — same path BasePlayerModulePanel
        // and QuestInteractionScreenController use, so we don't keep two parallel scrollbar widgets.
        private static readonly Color INK_BLACK = new Color(0.10f, 0.07f, 0.04f, 1f);
        private const float SCROLLBAR_WIDTH = 14f;

        private TMP_Text CreateLabel(RectTransform parent, string text, float yMin, float yMax, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, yMin); rt.anchorMax = new Vector2(0.98f, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align;
            tmp.color = INK_BLACK;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private TMP_InputField CreateInput(RectTransform parent, float xMin, float xMax, float yMin, float yMax, bool multiline)
        {
            var go = new GameObject(multiline ? "TradesInput" : "ProfileInput", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();

            // Viewport — leave room on the right for the scrollbar when multiline.
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            float rightInset = multiline ? -(SCROLLBAR_WIDTH + 12f) : -8f;
            vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(rightInset, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = TMP_Settings.defaultFontAsset;
            txt.fontSize = 15f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

            input.textViewport = vrt;
            input.textComponent = txt;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;

            // Add a vertical scrollbar on the right edge of the multiline trades input, wired to the
            // TMP_InputField — TMP handles all the scroll math, we just hand it the Scrollbar reference.
            if (multiline)
            {
                var scrollbar = FiresCore.UI.UIBuilderHelper.CreateVerticalScrollbar(rt, SCROLLBAR_WIDTH);
                input.verticalScrollbar = scrollbar;
                input.scrollSensitivity = 6f;
            }
            return input;
        }

        // --- Saved Profiles dropdown ---
        //
        // Standalone TMP_Dropdown — its first option is "Saved Profiles" (acts as a header so the
        // user can see what the dropdown is for at a glance), and the remaining options are every
        // profile name returned by TraderProfileManager. Selecting one writes its name into the
        // profile input and loads its trades, same as if the user had typed the name. Dropdown
        // pattern mirrors MarketplacePlayerPanel.BuildCurrencyDropdown so we have one shape of
        // dropdown across the project, not several.

        private TMP_Dropdown BuildSavedProfilesDropdown(RectTransform parent, float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject("SavedProfilesDropdown", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var dd = go.AddComponent<TMP_Dropdown>();

            // Caption label (shows the current selection / header on the closed dropdown).
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-22f, -2f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 14f; label.color = new Color(0.96f, 0.91f, 0.76f);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            // Arrow indicator (small chevron on the right).
            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(go.transform, false);
            var art = arrowGo.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(1f, 0.5f); art.anchorMax = new Vector2(1f, 0.5f);
            art.pivot = new Vector2(1f, 0.5f);
            art.sizeDelta = new Vector2(20f, 20f); art.anchoredPosition = new Vector2(-4f, 0f);
            var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowText.font = TMP_Settings.defaultFontAsset;
            arrowText.text = "v"; arrowText.fontSize = 16f; arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.color = new Color(1f, 0.85f, 0.55f);

            // Template (the popup panel that appears when the dropdown is opened).
            var template = new GameObject("Template", typeof(RectTransform), typeof(Image));
            template.transform.SetParent(go.transform, false);
            var trt = template.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 0f); trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 220f); trt.anchoredPosition = new Vector2(0f, -2f);
            template.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.98f);

            var scroll = template.AddComponent<ScrollRect>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.0f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0f, 28f);

            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            var irt = item.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(1f, 0.5f);
            irt.sizeDelta = new Vector2(0f, 26f);

            var itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(Image));
            itemBg.transform.SetParent(item.transform, false);
            var ibgRt = itemBg.GetComponent<RectTransform>();
            ibgRt.anchorMin = Vector2.zero; ibgRt.anchorMax = Vector2.one;
            ibgRt.offsetMin = Vector2.zero; ibgRt.offsetMax = Vector2.zero;
            itemBg.GetComponent<Image>().color = new Color(0.18f, 0.14f, 0.09f, 0.95f);

            var itemChecked = new GameObject("Item Checkmark", typeof(RectTransform), typeof(Image));
            itemChecked.transform.SetParent(item.transform, false);
            var icRt = itemChecked.GetComponent<RectTransform>();
            icRt.anchorMin = new Vector2(0f, 0.5f); icRt.anchorMax = new Vector2(0f, 0.5f);
            icRt.sizeDelta = new Vector2(18f, 18f); icRt.anchoredPosition = new Vector2(12f, 0f);
            itemChecked.GetComponent<Image>().color = new Color(1f, 0.85f, 0.55f);

            var itemLabelGo = new GameObject("Item Label", typeof(RectTransform));
            itemLabelGo.transform.SetParent(item.transform, false);
            var ilrt = itemLabelGo.GetComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(28f, 2f); ilrt.offsetMax = new Vector2(-4f, -2f);
            var itemLabelText = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabelText.font = TMP_Settings.defaultFontAsset;
            itemLabelText.fontSize = 13f; itemLabelText.color = new Color(0.96f, 0.91f, 0.76f);
            itemLabelText.alignment = TextAlignmentOptions.MidlineLeft;

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBg.GetComponent<Image>();
            toggle.graphic = itemChecked.GetComponent<Image>();
            toggle.isOn = true;

            scroll.content = crt; scroll.viewport = vrt;
            scroll.horizontal = false; scroll.vertical = true;

            dd.template = trt;
            dd.captionText = label;
            dd.itemText = itemLabelText;
            template.SetActive(false);

            dd.onValueChanged.AddListener(OnSavedProfileSelected);
            return dd;
        }

        /// <summary>Rebuilds the dropdown's option list from <see cref="TraderProfileManager"/>.
        /// First option is always the header "Saved Profiles" so the closed dropdown shows that as
        /// its caption when no profile is selected. Selecting it is a no-op (see OnSavedProfileSelected).</summary>
        private void RefreshSavedProfilesDropdown(string currentName)
        {
            if (_savedProfilesDropdown == null) return;
            TraderProfileManager.Initialize();
            var profileNames = TraderProfileManager.GetAllProfileNames().OrderBy(n => n).ToList();
            var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("Saved Profiles"));
            foreach (var name in profileNames) options.Add(new TMP_Dropdown.OptionData(name));
            _savedProfilesDropdown.ClearOptions();
            _savedProfilesDropdown.AddOptions(options);
            _savedProfilesDropdown.SetValueWithoutNotify(0);
            if (_savedProfilesDropdown.captionText != null) _savedProfilesDropdown.captionText.text = "Saved Profiles";
        }

        private void OnSavedProfileSelected(int idx)
        {
            // Index 0 is the "Saved Profiles" header — ignore.
            if (_savedProfilesDropdown == null || idx <= 0) return;
            var opt = _savedProfilesDropdown.options[idx];
            string name = opt?.text;
            if (string.IsNullOrEmpty(name)) return;
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(name);
            LoadProfileTrades(name);
            // Reset caption back to the header so the field doesn't permanently latch onto the picked
            // profile (the typed input below it is the source of truth for what gets saved).
            _savedProfilesDropdown.SetValueWithoutNotify(0);
            if (_savedProfilesDropdown.captionText != null) _savedProfilesDropdown.captionText.text = "Saved Profiles";
        }
    }
}
