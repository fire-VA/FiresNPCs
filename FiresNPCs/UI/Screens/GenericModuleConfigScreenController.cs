using System.Linq;
using System.Reflection;
using FiresCore.Npc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Generic per-module admin config screen, modelled on the trader-config layout but parameterised
    /// for each module type via <see cref="Initialize"/>. Three distinct layouts share this controller:
    ///   • <b>Profile-with-body</b> (Banker, Gambler, Mail, Transmog) — Profile name input + Saved
    ///     Profiles dropdown + multi-line config textarea + format-hint label. Apply writes the .cfg
    ///     file through the matching <c>ProfileManager.SaveProfile</c> and commits the NPC.
    ///   • <b>Marketplace</b> — no profile, no body. The Marketplace UI is global; an NPC just needs
    ///     <c>npcType = Marketplace</c> set so player interaction opens the Marketplace panel.
    ///   • <b>Stub</b> (Achievement, Leaderboard) — profile name only, no body editor yet; the
    ///     module data formats land in a follow-up.
    /// </summary>
    public class GenericModuleConfigScreenController : MonoBehaviour
    {
        private NpcController _currentNpc;
        private NpcType _moduleType;
        private string _profileFieldName = "";
        private string _moduleLabel = "Module";
        private FieldInfo _profileField;
        private TMP_InputField _profileInput;
        private TMP_InputField _bodyInput;
        private TMP_Dropdown _savedProfilesDropdown;
        private TMP_Text _statusText;
        private TMP_Text _formatHintLabel;
        private Button _applyButton;
        private bool _built;
        private const float ScrollbarWidth = 14f;

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
                // Marketplace doesn't use per-NPC profiles — the listing UI is global. The admin just
                // needs to commit npcType=Marketplace so player E opens the Marketplace panel.
                CreateLabel(rt, "Marketplace NPC", 0.82f, 0.92f, 18f, TextAlignmentOptions.Center);
                CreateLabel(rt,
                    "The Marketplace listing UI is global — every Marketplace NPC opens the same panel " +
                    "where players see all listings and post their own. No per-NPC profile is needed; just " +
                    "click Apply to flag this NPC as a Marketplace and player interaction will route there.",
                    0.55f, 0.78f, 14f, TextAlignmentOptions.TopLeft);
                _statusText = CreateLabel(rt, "", 0.12f, 0.18f, 13f, TextAlignmentOptions.Left);
            }
            else
            {
                // Profile name + saved profiles dropdown (top band).
                CreateLabel(rt, _moduleLabel + " Profile  (apply existing or create new)", 0.83f, 0.89f, 14f, TextAlignmentOptions.Left);
                _profileInput = CreateInput(rt, 0.02f, 0.64f, 0.76f, 0.83f, multiline: false);
                _profileInput.onEndEdit.AddListener(LoadProfileBody);
                _savedProfilesDropdown = SavedProfilesDropdown.Build(rt, 0.66f, 0.98f, 0.76f, 0.83f, OnSavedProfilePicked);

                // Format-hint + body editor (only present for modules that actually have a body format).
                if (HasBodyFormat())
                {
                    _formatHintLabel = CreateLabel(rt, GetFormatHint(), 0.71f, 0.755f, 14f, TextAlignmentOptions.Left);
                    _bodyInput = CreateInput(rt, 0.02f, 0.98f, 0.20f, 0.705f, multiline: true);
                }
                else
                {
                    CreateLabel(rt,
                        "v1 stub - profile name + Apply only. The full " + _moduleLabel + " editor lands when the data format is finalised.",
                        0.30f, 0.70f, 13f, TextAlignmentOptions.TopLeft);
                }

                _statusText = CreateLabel(rt, "", 0.12f, 0.18f, 13f, TextAlignmentOptions.Left);
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

            // Persist the body via the module's SaveProfile (writes .cfg + reloads). Stub modules with
            // no body editor skip this step.
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
            NpcType.Banker   => true,
            NpcType.Gambler  => true,
            NpcType.Mail     => true,
            NpcType.Transmog => true,
            _ => false,
        };

        private string GetFormatHint() => _moduleType switch
        {
            NpcType.Banker   => "Banker entries  -  one per line:   interest=PCT_PER_MIN  or  CurrencyPrefab:maxBalance",
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
                    case NpcType.Banker:
                        Modules.Marketplace.BankerProfileManager.EnsureLoaded();
                        var bp = Modules.Marketplace.BankerProfileManager.GetProfile(name);
                        return bp == null ? "" : Modules.Marketplace.BankerProfileManager.Serialize(bp);
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
                    case NpcType.Banker:   Modules.Marketplace.BankerProfileManager.SaveProfile(name, bodyText); break;
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
                    NpcType.Banker      => Modules.Marketplace.BankerProfileManager.AllProfileNames,
                    NpcType.Gambler     => Modules.Marketplace.GamblerProfileManager.AllProfileNames,
                    NpcType.Achievement => GetAchievementProfileNames(),
                    NpcType.Leaderboard => GetLeaderboardProfileNames(),
                    NpcType.Mail        => GetMailProfileNames(),
                    NpcType.Transmog    => GetTransmogProfileNames(),
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
            tmp.color = new Color(0.10f, 0.07f, 0.04f, 1f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private TMP_InputField CreateInput(RectTransform parent, float xMin, float xMax, float yMin, float yMax, bool multiline)
        {
            var go = new GameObject(_moduleLabel + (multiline ? "BodyInput" : "ProfileInput"), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            float rightInset = multiline ? -(ScrollbarWidth + 12f) : -8f;
            vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(rightInset, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = TMP_Settings.defaultFontAsset;
            txt.fontSize = 15f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;

            if (multiline)
            {
                var scrollbar = FiresCore.UI.UIBuilderHelper.CreateVerticalScrollbar(rt, ScrollbarWidth);
                input.verticalScrollbar = scrollbar;
                input.scrollSensitivity = 6f;
            }
            return input;
        }
    }
}
