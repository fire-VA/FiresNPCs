using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Admin Banker-config screen (Banker tab of the NPC panel). Profile name + Saved Profiles
    /// dropdown + multi-line config textarea where each line is either:
    ///   interest=PCT_PER_MIN
    ///   CurrencyPrefab:maxBalance
    /// Apply writes the .cfg through <see cref="BankerProfileManager.SaveProfile"/> and assigns
    /// the NPC type to Banker so player E reaches HandleBankerInteraction.
    /// </summary>
    public class BankerConfigScreenController : MonoBehaviour
    {
        // Top-anchored pixel layout — fixed offsets from the screen rect's TOP. This avoids
        // percent-anchoring against a rect that may have been shrunk by ClearSecondTabRow on
        // the source QuestConfigScreen (which would push content into the lower half of the panel).
        private const float TOP_PAD          = 8f;
        private const float LABEL_H          = 24f;
        private const float INPUT_H          = 32f;
        private const float HINT_H           = 24f;
        private const float ROW_GAP          = 4f;
        private const float SECTION_GAP      = 8f;
        private const float STATUS_H         = 24f;
        private const float SCROLLBAR_W      = 14f;
        // Side padding pulls every form element away from the parchment's painted border so labels
        // and the textarea don't run into the ornament. 12 was too tight; 32 reads cleanly.
        private const float SIDE_PAD         = 32f;

        // Computed top offsets
        private const float Y_PROFILE_LABEL  = TOP_PAD;
        private const float Y_PROFILE_ROW    = Y_PROFILE_LABEL + LABEL_H + ROW_GAP;
        private const float Y_FORMAT_HINT    = Y_PROFILE_ROW + INPUT_H + SECTION_GAP;
        private const float Y_BODY_TOP       = Y_FORMAT_HINT + HINT_H + ROW_GAP;
        private const float BOTTOM_PAD       = 8f;
        private const float Y_STATUS_BOTTOM  = BOTTOM_PAD;
        private const float BODY_BOTTOM_PAD  = Y_STATUS_BOTTOM + STATUS_H + SECTION_GAP;

        private NpcController _currentNpc;
        private TMP_InputField _profileInput;
        private TMP_InputField _bodyInput;
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
            var rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            CreateLabelTop(rt, "Banker Profile  (apply existing or create new)", Y_PROFILE_LABEL, LABEL_H, 14f, TextAlignmentOptions.Left);

            // Profile input (left 60%) + Saved Profiles dropdown (right ~36%).
            _profileInput = CreateInputTop(rt, SIDE_PAD, 0.60f, Y_PROFILE_ROW, INPUT_H, multiline: false);
            _profileInput.onEndEdit.AddListener(LoadProfileBody);

            _savedProfilesDropdown = SavedProfilesDropdown.BuildTopAnchored(rt, 0.64f, SIDE_PAD, Y_PROFILE_ROW, INPUT_H, OnSavedProfilePicked);

            CreateLabelTop(rt,
                "Banker entries  -  one per line:   interest=PCT_PER_MIN   or   CurrencyPrefab:maxBalance",
                Y_FORMAT_HINT, HINT_H, 14f, TextAlignmentOptions.Left);

            _bodyInput = CreateBodyTextarea(rt, Y_BODY_TOP, BODY_BOTTOM_PAD);

            _statusText = CreateLabelBottom(rt, "", Y_STATUS_BOTTOM, STATUS_H, 13f, TextAlignmentOptions.Left);

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            string current = npc?.bankerProfile ?? "";
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(current);
            BankerProfileManager.EnsureLoaded();
            SavedProfilesDropdown.Refresh(_savedProfilesDropdown, BankerProfileManager.AllProfileNames);
            LoadProfileBody(current);
            gameObject.SetActive(true);
            if (_applyButton != null) _applyButton.gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (_applyButton != null) _applyButton.gameObject.SetActive(false);
        }

        private void OnSavedProfilePicked(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(name);
            LoadProfileBody(name);
        }

        private void LoadProfileBody(string name)
        {
            name = (name ?? "").Trim();
            if (_bodyInput == null) return;
            string body = "";
            if (!string.IsNullOrEmpty(name))
            {
                BankerProfileManager.EnsureLoaded();
                var profile = BankerProfileManager.GetProfile(name);
                if (profile != null) body = BankerProfileManager.Serialize(profile);
            }
            _bodyInput.SetTextWithoutNotify(body);
            if (string.IsNullOrEmpty(name)) SetStatus("No banker profile set.");
            else if (!string.IsNullOrEmpty(body)) SetStatus("Loaded '" + name + "'. Edit + Apply to save.");
            else SetStatus("New profile '" + name + "' - add config below, then Apply.");
        }

        public void ApplyChanges()
        {
            if (_currentNpc == null) { SetStatus("No NPC selected."); return; }
            string name = (_profileInput != null ? _profileInput.text : "").Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("Enter a profile name first."); return; }
            int eq = name.IndexOf('='); if (eq >= 0) name = name.Substring(0, eq).Trim();

            if (_bodyInput != null) BankerProfileManager.SaveProfile(name, _bodyInput.text);

            _currentNpc.bankerProfile = name;
            _currentNpc.npcType = FiresCore.Npc.NpcType.Banker;
            _currentNpc.SaveToZDO();
            SetStatus("Saved '" + name + "' and assigned this NPC as Banker.");
            SavedProfilesDropdown.Refresh(_savedProfilesDropdown, BankerProfileManager.AllProfileNames);
            Debug.Log("[BankerConfig] Apply: npc=" + _currentNpc.GetDisplayName() + " -> bankerProfile='" + name + "', npcType=Banker");
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // ── Layout helpers (top-anchored, pixel-based) ────────────────────────

        private static TMP_Text CreateLabelTop(RectTransform parent, string text, float topPx, float height, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(SIDE_PAD, -(topPx + height));
            rt.offsetMax = new Vector2(-SIDE_PAD, -topPx);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text; tmp.fontSize = fontSize; tmp.alignment = align;
            tmp.color = new Color(0.10f, 0.07f, 0.04f, 1f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static TMP_Text CreateLabelBottom(RectTransform parent, string text, float bottomPx, float height, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(SIDE_PAD, bottomPx);
            rt.offsetMax = new Vector2(-SIDE_PAD, bottomPx + height);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text; tmp.fontSize = fontSize; tmp.alignment = align;
            tmp.color = new Color(0.10f, 0.07f, 0.04f, 1f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static TMP_InputField CreateInputTop(RectTransform parent, float leftPx, float widthPct, float topPx, float height, bool multiline)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(widthPct, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(leftPx, -(topPx + height));
            rt.offsetMax = new Vector2(0f, -topPx);
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(-8f, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = TMP_Settings.defaultFontAsset;
            txt.fontSize = 15f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static TMP_InputField CreateBodyTextarea(RectTransform parent, float topPx, float bottomPx)
        {
            var go = new GameObject("BodyInput", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(SIDE_PAD, bottomPx);
            rt.offsetMax = new Vector2(-SIDE_PAD, -topPx);
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(8f, 4f);
            vrt.offsetMax = new Vector2(-(SCROLLBAR_W + 12f), -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = TMP_Settings.defaultFontAsset;
            txt.fontSize = 15f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.TopLeft;

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;

            var scrollbar = FiresCore.UI.UIBuilderHelper.CreateVerticalScrollbar(rt, SCROLLBAR_W);
            input.verticalScrollbar = scrollbar;
            input.scrollSensitivity = 6f;
            return input;
        }
    }
}
