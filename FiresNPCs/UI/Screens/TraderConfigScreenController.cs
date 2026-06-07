using System.Collections.Generic;
using System.Linq;
using System.Text;
using FiresRPGmaker.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Admin Trader-config screen (the NPC panel's Trader tab). Mirrors QuestConfigScreenController:
    /// a programmatic CreateForm is the source layout (captured -> baked later), with a WireFromCodegen
    /// path for when a baked "TraderConfigScreen" exists. Increment 1: pick a trader profile (from the
    /// loaded set, incl. the Marketplace drop-in) + live trades preview; Apply assigns it to the NPC.
    /// Increment 2 (later): inline trade-row editing + write-back to the trader .cfg.
    /// </summary>
    public class TraderConfigScreenController : MonoBehaviour
    {
        private NpcController _currentNpc;
        private TMP_InputField _profileInput;
        private TMP_Text _previewText;
        private Button _applyButton;
        private bool _built;

        public bool IsVisible => gameObject.activeSelf;

        /// <summary>Wire from a baked "TraderConfigScreen" codegen element (parity with the other screens).
        /// Not used yet (no baked trader screen); kept so the bake step is a drop-in later.</summary>
        public void WireFromCodegen(GameObject screenGo, Button applyBtn)
        {
            _applyButton = applyBtn;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
            // Field lookups would go here once a baked layout exists; for now the programmatic CreateForm path is used.
            BuildForm(applyBtn);
        }

        /// <summary>Programmatic form (source layout). Built once under this GO (the "TraderConfigScreen").</summary>
        public void BuildForm(Button applyButton)
        {
            if (_built) return;
            _built = true;

            var rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 60f); rt.offsetMax = new Vector2(-20f, -70f);

            CreateLabel(rt, "Trader Setup", 0.92f, 0.99f, 22f, TextAlignmentOptions.Center);
            CreateLabel(rt, "Trader Profile", 0.84f, 0.90f, 16f, TextAlignmentOptions.Left);
            _profileInput = CreateInput(rt, 0.76f, 0.83f);
            _profileInput.onValueChanged.AddListener(_ => UpdatePreview());
            CreateLabel(rt, "Trades", 0.70f, 0.75f, 16f, TextAlignmentOptions.Left);
            _previewText = CreateLabel(rt, "", 0.02f, 0.69f, 15f, TextAlignmentOptions.TopLeft);
            _previewText.enableWordWrapping = true;

            _applyButton = applyButton;
            if (_applyButton != null) { _applyButton.onClick.RemoveAllListeners(); _applyButton.onClick.AddListener(ApplyChanges); }
        }

        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            if (_profileInput != null) _profileInput.SetTextWithoutNotify(npc?.traderProfile ?? "");
            UpdatePreview();
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
            _currentNpc.traderProfile = (_profileInput != null ? _profileInput.text : _currentNpc.traderProfile)?.Trim();
            _currentNpc.SaveToZDO();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_previewText == null) return;
            TraderProfileManager.Initialize();
            string name = _profileInput != null ? _profileInput.text.Trim() : "";
            var profile = string.IsNullOrEmpty(name) ? null : TraderProfileManager.GetProfile(name);
            if (profile == null)
            {
                var all = TraderProfileManager.GetAllProfileNames().OrderBy(n => n).ToList();
                _previewText.text = all.Count == 0
                    ? "No trader profiles loaded.\nAdd .cfg to config/FiresRPGmaker/Traders, or drop in a Marketplace config tree."
                    : "Type a profile name above. Available:\n" + string.Join("\n", all.Select(n => "  " + n));
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{profile.Name}</b>   ({profile.Trades.Count} trades)");
            foreach (var t in profile.Trades.Take(40))
            {
                string cost = string.Join(" + ", t.CostItems.Select(c => $"{c.Amount}x {c.PrefabName}"));
                string reward = string.Join(" + ", t.RewardItems.Select(r => $"{r.Amount}x {r.PrefabName}" + (r.Level > 0 ? $" (lv{r.Level})" : "")));
                sb.AppendLine($"  {cost}  ->  {reward}");
            }
            if (profile.Trades.Count > 40) sb.AppendLine($"  ...and {profile.Trades.Count - 40} more");
            _previewText.text = sb.ToString();
        }

        // --- minimal Unity-UI builders (anchors are fractional Y bands of the screen rect) ---
        private TMP_Text CreateLabel(RectTransform parent, string text, float yMin, float yMax, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, yMin); rt.anchorMax = new Vector2(0.98f, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align; tmp.color = new Color(0.96f, 0.91f, 0.76f);
            return tmp;
        }

        private TMP_InputField CreateInput(RectTransform parent, float yMin, float yMax)
        {
            var go = new GameObject("ProfileInput", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, yMin); rt.anchorMax = new Vector2(0.98f, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = new Vector2(8f, 2f); vrt.offsetMax = new Vector2(-8f, -2f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 16f; txt.color = new Color(0.96f, 0.91f, 0.76f); txt.alignment = TextAlignmentOptions.Left;

            input.textViewport = vrt;
            input.textComponent = txt;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }
    }
}
