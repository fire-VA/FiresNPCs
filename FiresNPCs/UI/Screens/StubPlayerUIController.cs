using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Programmatic placeholder player UI used by every v1-stub Marketplace module (Gambler, Marketplace,
    /// Achievement, Leaderboard, Mail, Transmog). Opens a small centered panel showing
    /// "&lt;Module&gt; NPC" + the assigned profile + a Close button. Each module's real player UI replaces
    /// this dispatch in NpcController.HandleGenericModuleStub when its prefab + controller ship.
    ///
    /// Single shared instance (DontDestroyOnLoad) reused across NPCs / sessions.
    /// </summary>
    public class StubPlayerUIController : MonoBehaviour
    {
        private static StubPlayerUIController _instance;
        private GameObject _panel;
        private TMP_Text _titleText;
        private TMP_Text _bodyText;
        private GameObject _canvasGo;

        public static void Show(string moduleLabel, string profileName)
        {
            EnsureInstance();
            _instance.ShowInternal(moduleLabel, profileName);
        }

        public static void HideIfOpen()
        {
            if (_instance != null && _instance._panel != null && _instance._panel.activeSelf)
                _instance._panel.SetActive(false);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresStubPlayerUI");
            _instance = go.AddComponent<StubPlayerUIController>();
            DontDestroyOnLoad(go);
        }

        private void ShowInternal(string moduleLabel, string profileName)
        {
            EnsurePanel();
            _titleText.text = moduleLabel + " NPC";
            _bodyText.text = string.IsNullOrEmpty(profileName)
                ? "No profile assigned. Use the admin panel to set one."
                : "Profile: " + profileName + "\n\n[Full UI coming in a future update]";
            _panel.SetActive(true);
            try { GUIManager.BlockInput(true); } catch { }
        }

        private void Update()
        {
            // ESC closes the stub panel (mirrors how Inventory + admin panels behave).
            if (_panel != null && _panel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            {
                _panel.SetActive(false);
                try { GUIManager.BlockInput(false); } catch { }
            }
        }

        private void EnsurePanel()
        {
            if (_panel != null) return;

            // Standalone overlay canvas (not parked under Hud — same pattern as the trader UI / admin panel).
            _canvasGo = new GameObject("StubCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("StubPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(_canvasGo.transform, false);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 260f);
            _panel.GetComponent<Image>().color = new Color(0.18f, 0.13f, 0.08f, 0.95f);

            BuildTitle(rt);
            BuildBody(rt);
            BuildCloseButton(rt);
        }

        private void BuildTitle(RectTransform parent)
        {
            var go = new GameObject("Title", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var trt = go.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.78f); trt.anchorMax = new Vector2(1f, 0.96f);
            trt.offsetMin = new Vector2(14f, 0f); trt.offsetMax = new Vector2(-14f, 0f);
            _titleText = go.AddComponent<TextMeshProUGUI>();
            _titleText.font = TMP_Settings.defaultFontAsset;
            _titleText.fontSize = 24f; _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.color = new Color(1f, 0.85f, 0.55f);
            _titleText.fontStyle = FontStyles.Bold;
        }

        private void BuildBody(RectTransform parent)
        {
            var go = new GameObject("Body", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var brt = go.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.05f, 0.26f); brt.anchorMax = new Vector2(0.95f, 0.76f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            _bodyText = go.AddComponent<TextMeshProUGUI>();
            _bodyText.font = TMP_Settings.defaultFontAsset;
            _bodyText.fontSize = 16f; _bodyText.alignment = TextAlignmentOptions.Center;
            _bodyText.color = new Color(0.96f, 0.91f, 0.76f);
            _bodyText.enableWordWrapping = true;
        }

        private void BuildCloseButton(RectTransform parent)
        {
            var go = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var crt = go.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.3f, 0.06f); crt.anchorMax = new Vector2(0.7f, 0.22f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.32f, 0.22f, 0.14f, 0.95f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                _panel.SetActive(false);
                try { GUIManager.BlockInput(false); } catch { }
            });

            var labelGo = new GameObject("Text", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = "Close"; label.fontSize = 16f; label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.96f, 0.91f, 0.76f);
        }
    }
}
