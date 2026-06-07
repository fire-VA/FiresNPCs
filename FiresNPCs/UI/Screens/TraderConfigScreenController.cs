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

            CreateLabel(rt, "Trader Profile  (type an existing name to load it, or a new name to create)", 0.91f, 0.99f, 14f, TextAlignmentOptions.Left);
            _profileInput = CreateInput(rt, 0.82f, 0.895f, false);
            _profileInput.onEndEdit.AddListener(LoadProfileTrades);

            CreateLabel(rt, "Trades  -  one per line:   cost, amount, reward, amount[, level]", 0.73f, 0.81f, 14f, TextAlignmentOptions.Left);
            _tradesInput = CreateInput(rt, 0.11f, 0.72f, true);

            _statusText = CreateLabel(rt, "", 0.01f, 0.10f, 13f, TextAlignmentOptions.Left);

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            string name = npc?.traderProfile ?? "";
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(name);
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
            if (_currentNpc == null) return;
            string name = (_profileInput != null ? _profileInput.text : "").Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("Enter a profile name first."); return; }
            TraderProfileManager.SaveProfile(name, _tradesInput != null ? _tradesInput.text : "");
            _currentNpc.traderProfile = name;
            _currentNpc.SaveToZDO();
            var profile = TraderProfileManager.GetProfile(name);
            SetStatus($"Saved '{name}' ({profile?.Trades.Count ?? 0} trades) and assigned to this NPC.");
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
                var all = TraderProfileManager.GetAllProfileNames().OrderBy(n => n).ToList();
                SetStatus(all.Count == 0
                    ? "New profile - add trade lines above, then Apply."
                    : "New/unknown profile. Existing: " + string.Join(", ", all.Take(12)) + (all.Count > 12 ? " ..." : ""));
            }
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // --- minimal Unity-UI builders (anchors are fractional Y bands of the screen rect) ---
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

        private TMP_InputField CreateInput(RectTransform parent, float yMin, float yMax, bool multiline)
        {
            var go = new GameObject(multiline ? "TradesInput" : "ProfileInput", typeof(RectTransform), typeof(Image));
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
            txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

            input.textViewport = vrt;
            input.textComponent = txt;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            return input;
        }
    }
}
