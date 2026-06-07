using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Admin Banker-config screen (Banker tab of the NPC panel). Minimal v1: profile name + Apply, sets
    /// the NPC's banker profile + commits npcType=Banker so player E reaches HandleBankerInteraction.
    /// The deposit/withdraw player UI + LiteDB wiring lands in a follow-up; this gets the admin tab into
    /// users' hands so they can mark NPCs as bankers and load saved profiles.
    /// </summary>
    public class BankerConfigScreenController : MonoBehaviour
    {
        private NpcController _currentNpc;
        private TMP_InputField _profileInput;
        private TMP_Text _statusText;
        private Button _applyButton;
        private bool _built;

        public bool IsVisible => gameObject.activeSelf;

        public void WireFromCodegen(GameObject screenGo, Button applyBtn) => BuildForm(applyBtn);

        public void BuildForm(Button applyButton)
        {
            if (_built) return;
            _built = true;
            var rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

            CreateLabel(rt, "Banker Profile  (type an existing name to load it, or a new name to create)", 0.78f, 0.88f, 14f, TextAlignmentOptions.Left);
            _profileInput = CreateInput(rt, 0.66f, 0.755f);
            CreateLabel(rt,
                "v1 stub — sets npcType=Banker + saves the profile name to the NPC. The full deposit/withdraw " +
                "UI + persistence ships in a follow-up alongside the LiteDB BankRepository wiring.",
                0.30f, 0.62f, 13f, TextAlignmentOptions.TopLeft);
            _statusText = CreateLabel(rt, "", 0.10f, 0.17f, 13f, TextAlignmentOptions.Left);

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(npc?.bankerProfile ?? "");
            SetStatus(string.IsNullOrEmpty(npc?.bankerProfile) ? "No banker profile set." : $"Loaded '{npc.bankerProfile}'.");
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

            _currentNpc.bankerProfile = name;
            _currentNpc.npcType = global::FiresCore.Npc.NpcType.Banker;
            _currentNpc.SaveToZDO();
            SetStatus($"Saved '{name}' and assigned this NPC as Banker.");
            Debug.Log($"[BankerConfig] Apply: npc={_currentNpc.GetDisplayName()} → bankerProfile='{name}', npcType=Banker");
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
            var go = new GameObject("BankerProfileInput", typeof(RectTransform), typeof(Image));
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
