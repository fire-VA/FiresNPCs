using System.Reflection;
using FiresCore.Npc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Generic Marketplace-module admin config screen. Used as a STUB for every module whose admin needs
    /// nothing more than a profile-name input + Apply that commits the type + profile to the NPC.
    /// (Banker, Gambler, Marketplace, Achievement, Leaderboard, Mail, Transmog — all share this shape
    /// in v1; per-module specifics get their own controller when those modules' data formats land.)
    ///
    /// Initialize(NpcType, fieldName, label) tells the screen WHICH NpcController profile field to
    /// read/write via reflection so we don't need a controller-per-module.
    /// </summary>
    public class GenericModuleConfigScreenController : MonoBehaviour
    {
        private NpcController _currentNpc;
        private NpcType _moduleType;
        private string _profileFieldName = "";
        private string _moduleLabel = "Module";
        private FieldInfo _profileField; // cached
        private TMP_InputField _profileInput;
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

            CreateLabel(rt, _moduleLabel + " Profile  (type an existing name to load it, or a new name to create)", 0.78f, 0.88f, 14f, TextAlignmentOptions.Left);
            _profileInput = CreateInput(rt, 0.66f, 0.755f);
            CreateLabel(rt,
                "v1 stub - sets npcType=" + _moduleType + " + saves the profile name to the NPC. The full " +
                _moduleLabel + " UI / config parser / persistence lands in a follow-up.",
                0.30f, 0.62f, 13f, TextAlignmentOptions.TopLeft);
            _statusText = CreateLabel(rt, "", 0.10f, 0.17f, 13f, TextAlignmentOptions.Left);

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            string current = (_profileField?.GetValue(npc) as string) ?? "";
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(current);
            SetStatus(string.IsNullOrEmpty(current) ? "No " + _moduleLabel.ToLowerInvariant() + " profile set." : "Loaded '" + current + "'.");
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
            int eq = name.IndexOf('='); if (eq >= 0) name = name.Substring(0, eq).Trim();

            _profileField?.SetValue(_currentNpc, name);
            _currentNpc.npcType = _moduleType;
            _currentNpc.SaveToZDO();
            SetStatus("Saved '" + name + "' and assigned this NPC as " + _moduleLabel + ".");
            Debug.Log("[" + _moduleLabel + "Config] Apply: npc=" + _currentNpc.GetDisplayName() + " -> " + _profileFieldName + "='" + name + "', npcType=" + _moduleType);
        }

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
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align; tmp.color = new Color(0.96f, 0.91f, 0.76f);
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private TMP_InputField CreateInput(RectTransform parent, float yMin, float yMax)
        {
            var go = new GameObject(_moduleLabel + "ProfileInput", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, yMin); rt.anchorMax = new Vector2(0.98f, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(-8f, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.font = TMP_Settings.defaultFontAsset;
            txt.fontSize = 15f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = TextAlignmentOptions.Left;

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }
    }
}
