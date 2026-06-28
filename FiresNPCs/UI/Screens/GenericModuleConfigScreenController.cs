using System.Reflection;
using FiresCore.Npc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Generic per-module admin config screen, modelled on BankerConfigScreenController and parameterised
    /// for each module via <see cref="Initialize"/>. Three distinct shapes share this controller:
    ///   • <b>Profile-with-body</b> (Gambler, Mail, Transmog) — Profile name + Saved Profiles dropdown +
    ///     multi-line config textarea + format-hint label. Apply writes the .cfg through the matching
    ///     <c>ProfileManager.SaveProfile</c> and commits the NPC.
    ///   • <b>Marketplace</b> — no profile, no body. The Marketplace UI is global; an NPC just needs
    ///     <c>npcType = Marketplace</c> set so player interaction opens the Marketplace panel.
    ///   • <b>Stub</b> (Achievement, Leaderboard) — profile name only, no body editor yet.
    ///
    /// Layout: top-anchored pixel offsets (NOT percent fractions of the screen rect). The source
    /// QuestConfigScreen rect gets shrunk by ClearSecondTabRow to clear the 2-row tab bar, so any
    /// percent-based positioning ends up in the lower half of the panel.
    /// </summary>
    public class GenericModuleConfigScreenController : MonoBehaviour
    {
        // Same layout constants as BankerConfigScreenController — keep them in sync.
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

        private const float Y_PROFILE_LABEL  = TOP_PAD;
        private const float Y_PROFILE_ROW    = Y_PROFILE_LABEL + LABEL_H + ROW_GAP;
        private const float Y_FORMAT_HINT    = Y_PROFILE_ROW + INPUT_H + SECTION_GAP;
        private const float Y_BODY_TOP       = Y_FORMAT_HINT + HINT_H + ROW_GAP;
        private const float BOTTOM_PAD       = 8f;
        private const float Y_STATUS_BOTTOM  = BOTTOM_PAD;
        private const float BODY_BOTTOM_PAD  = Y_STATUS_BOTTOM + STATUS_H + SECTION_GAP;

        private NpcController _currentNpc;
        private NpcType _moduleType;
        private string _profileFieldName = "";
        private string _moduleLabel = "Module";
        private FieldInfo _profileField;
        private TMP_InputField _profileInput;
        private TMP_InputField _bodyInput;
        private TMP_Dropdown _savedProfilesDropdown;
        private TMP_Text _statusText;
        private Button _applyButton;
        private bool _built;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(NpcType type, string fieldName, string label)
        {
            _moduleType = type; _profileFieldName = fieldName; _moduleLabel = label;
            _profileField = typeof(NpcController).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (_profileField == null) Debug.LogError($"[{_moduleLabel}Config] missing NpcController field '{fieldName}'");
        }

        public void BuildForm(Button applyButton)
        {
            if (_built) return;
            _built = true;
            var rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            if (_moduleType == NpcType.Marketplace)
            {
                // No profile, no body — the Marketplace listing UI is global. Just a title + paragraph
                // explaining the click and the status text.
                CreateLabelTop(rt, "Marketplace NPC", TOP_PAD + 16f, LABEL_H + 4f, 18f, TextAlignmentOptions.Center);
                CreateLabelTop(rt,
                    "The Marketplace listing UI is global — every Marketplace NPC opens the same panel " +
                    "where players see all listings and post their own. No per-NPC profile is needed; just " +
                    "click Apply to flag this NPC as a Marketplace and player interaction will route there.",
                    TOP_PAD + 16f + LABEL_H + 12f, 120f, 14f, TextAlignmentOptions.TopLeft);
                _statusText = CreateLabelBottom(rt, "", Y_STATUS_BOTTOM, STATUS_H, 13f, TextAlignmentOptions.Left);
            }
            else
            {
                CreateLabelTop(rt, _moduleLabel + " Profile  (apply existing or create new)", Y_PROFILE_LABEL, LABEL_H, 14f, TextAlignmentOptions.Left);
                _profileInput = CreateInputTop(rt, SIDE_PAD, 0.60f, Y_PROFILE_ROW, INPUT_H, multiline: false);
                _profileInput.onEndEdit.AddListener(LoadProfileBody);

                _savedProfilesDropdown = SavedProfilesDropdown.BuildTopAnchored(rt, 0.64f, SIDE_PAD, Y_PROFILE_ROW, INPUT_H, OnSavedProfilePicked);

                if (HasBodyFormat())
                {
                    CreateLabelTop(rt, GetFormatHint(), Y_FORMAT_HINT, HINT_H, 14f, TextAlignmentOptions.Left);
                    _bodyInput = CreateBodyTextarea(rt, Y_BODY_TOP, BODY_BOTTOM_PAD);
                }
                else
                {
                    CreateLabelTop(rt,
                        "v1 stub - profile name + Apply only. The full " + _moduleLabel + " editor lands when the data format is finalised.",
                        Y_FORMAT_HINT, 120f, 13f, TextAlignmentOptions.TopLeft);
                }

                _statusText = CreateLabelBottom(rt, "", Y_STATUS_BOTTOM, STATUS_H, 13f, TextAlignmentOptions.Left);
            }

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            if (_moduleType == NpcType.Marketplace)
            {
                bool already = npc != null && npc.npcType == NpcType.Marketplace;
                SetStatus(already
                    ? "Already assigned as Marketplace. Click Apply to re-commit."
                    : "Click Apply to flag this NPC as a Marketplace.");
            }
            else
            {
                string current = (_profileField?.GetValue(npc) as string) ?? "";
                if (_profileInput != null) _profileInput.SetTextWithoutNotify(current);
                SavedProfilesDropdown.Refresh(_savedProfilesDropdown, GetProfileNameSequence());
                LoadProfileBody(current);
            }
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
            if (_bodyInput == null) { ShowProfileLoadStatus(name, false); return; }
            string body = SerializeProfileBody(name);
            _bodyInput.SetTextWithoutNotify(body ?? "");
            ShowProfileLoadStatus(name, !string.IsNullOrEmpty(body));
        }

        private void ShowProfileLoadStatus(string name, bool found)
        {
            if (string.IsNullOrEmpty(name))
                SetStatus("No " + _moduleLabel.ToLowerInvariant() + " profile set.");
            else if (found)
                SetStatus("Loaded '" + name + "'. Edit + Apply to save.");
            else
                SetStatus("New profile '" + name + "' - add config below, then Apply.");
        }

        public void ApplyChanges()
        {
            if (_currentNpc == null) { SetStatus("No NPC selected."); return; }

            if (_moduleType == NpcType.Marketplace)
            {
                _currentNpc.npcType = NpcType.Marketplace;
                _currentNpc.SaveToZDO();
                SetStatus("Assigned this NPC as a Marketplace.");
                Debug.Log($"[MarketplaceConfig] Apply: npc={_currentNpc.GetDisplayName()} -> npcType=Marketplace");
                return;
            }

            string name = (_profileInput != null ? _profileInput.text : "").Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("Enter a profile name first."); return; }
            int eq = name.IndexOf('='); if (eq >= 0) name = name.Substring(0, eq).Trim();

            if (_bodyInput != null) SaveBody(name, _bodyInput.text);

            _profileField?.SetValue(_currentNpc, name);
            _currentNpc.npcType = _moduleType;
            _currentNpc.SaveToZDO();
            SetStatus("Saved '" + name + "' and assigned this NPC as " + _moduleLabel + ".");
            SavedProfilesDropdown.Refresh(_savedProfilesDropdown, GetProfileNameSequence());
            Debug.Log("[" + _moduleLabel + "Config] Apply: npc=" + _currentNpc.GetDisplayName() + " -> " + _profileFieldName + "='" + name + "', npcType=" + _moduleType);
        }

        // ── per-module hooks ─────────────────────────────────────────────────

        private bool HasBodyFormat() => _moduleType switch
        {
            NpcType.Gambler  => true,
            NpcType.Mail     => true,
            NpcType.Transmog => true,
            _ => false,
        };

        private string GetFormatHint() => _moduleType switch
        {
            NpcType.Gambler  => "Gambler entries  -  one per line:   RequiredCurrency,Amount,Reward1,Min[-Max],Reward2,Min[-Max],...",
            NpcType.Mail     => "Mail entries  -  one per line:   SenderName|DateString|Subject|Body|attachment1:count;attachment2:count",
            NpcType.Transmog => "Transmog entries  -  one per line:   Slot|prefab|currency|price   (Slot = Helmet|Chest|Legs|Cape|...)",
            _ => "",
        };

        private string SerializeProfileBody(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            try
            {
                switch (_moduleType)
                {
                    case NpcType.Gambler:
                        Modules.Marketplace.GamblerProfileManager.EnsureLoaded();
                        var gp = Modules.Marketplace.GamblerProfileManager.GetProfile(name);
                        return gp == null ? "" : Modules.Marketplace.GamblerProfileManager.Serialize(gp);
                    case NpcType.Mail:
                        Modules.Marketplace.MailProfileManager.EnsureLoaded();
                        var mail = Modules.Marketplace.MailProfileManager.GetMail(name);
                        return mail == null ? "" : Modules.Marketplace.MailProfileManager.Serialize(mail);
                    case NpcType.Transmog:
                        Modules.Marketplace.TransmogProfileManager.EnsureLoaded();
                        var opts = Modules.Marketplace.TransmogProfileManager.GetProfileOptions(name);
                        return opts == null ? "" : Modules.Marketplace.TransmogProfileManager.Serialize(opts);
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"[{_moduleLabel}Config] SerializeProfileBody '{name}': {ex.Message}"); }
            return "";
        }

        private void SaveBody(string name, string bodyText)
        {
            try
            {
                switch (_moduleType)
                {
                    case NpcType.Gambler:  Modules.Marketplace.GamblerProfileManager.SaveProfile(name, 1, bodyText); break;
                    case NpcType.Mail:     Modules.Marketplace.MailProfileManager.SaveProfile(name, bodyText); break;
                    case NpcType.Transmog: Modules.Marketplace.TransmogProfileManager.SaveProfile(name, bodyText); break;
                }
            }
            catch (System.Exception ex) { Debug.LogError($"[{_moduleLabel}Config] SaveBody '{name}' threw: {ex.Message}"); }
        }

        private System.Collections.Generic.IEnumerable<string> GetProfileNameSequence()
        {
            try
            {
                return _moduleType switch
                {
                    NpcType.Gambler     => Modules.Marketplace.GamblerProfileManager.AllProfileNames,
                    NpcType.Achievement => GetAchievementProfileNames(),
                    NpcType.Leaderboard => GetLeaderboardProfileNames(),
                    NpcType.Mail        => GetMailProfileNames(),
                    NpcType.Transmog    => GetTransmogProfileNames(),
                    NpcType.Teleporter  => Bridges.WayshrinesBridge.GetProfileNames(),
                    _ => null,
                };
            }
            catch { return null; }
        }

        private static System.Collections.Generic.IEnumerable<string> GetAchievementProfileNames()
        { Modules.Marketplace.AchievementProfileManager.EnsureLoaded(); return Modules.Marketplace.AchievementProfileManager.GetLoadedProfileNames(); }
        private static System.Collections.Generic.IEnumerable<string> GetLeaderboardProfileNames()
        { Modules.Marketplace.LeaderboardProfileManager.EnsureLoaded(); return Modules.Marketplace.LeaderboardProfileManager.GetLoadedProfileNames(); }
        private static System.Collections.Generic.IEnumerable<string> GetMailProfileNames()
        { Modules.Marketplace.MailProfileManager.EnsureLoaded(); return Modules.Marketplace.MailProfileManager.GetLoadedProfileNames(); }
        private static System.Collections.Generic.IEnumerable<string> GetTransmogProfileNames()
        { Modules.Marketplace.TransmogProfileManager.EnsureLoaded(); return Modules.Marketplace.TransmogProfileManager.GetLoadedProfileNames(); }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // ── Layout helpers — match BankerConfigScreenController so screens look identical ────────

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
