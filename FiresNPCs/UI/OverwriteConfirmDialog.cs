using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// A simple modal confirmation dialog for NPC profile overwrite warnings.
    /// Created once and reused. Shown when saving an NPC profile would overwrite
    /// an existing profile with the same name.
    /// </summary>
    public class OverwriteConfirmDialog : MonoBehaviour
    {
        private static OverwriteConfirmDialog _instance;
        private static GameObject _dialogRoot;

        private TMP_Text _messageText;
        private Button _overwriteButton;
        private Button _cancelButton;
        private Action _onConfirm;

        /// <summary>
        /// Shows the overwrite confirmation dialog.
        /// </summary>
        /// <param name="profileName">The profile name that would be overwritten.</param>
        /// <param name="onConfirm">Action to invoke if the user clicks Overwrite.</param>
        public static void Show(string profileName, Action onConfirm)
        {
            EnsureCreated();
            if (_instance == null) return;

            _instance._onConfirm = onConfirm;
            _instance._messageText.text = $"A profile named <b>{profileName}</b> already exists.\nDo you want to overwrite it?";
            _dialogRoot.SetActive(true);
        }

        /// <summary>
        /// Returns true if the dialog is currently visible.
        /// </summary>
        public static bool IsVisible => _dialogRoot != null && _dialogRoot.activeSelf;

        private static void EnsureCreated()
        {
            if (_dialogRoot != null) return;

            // Find a parent canvas (use the Hud canvas or create one)
            Canvas parentCanvas = null;
            if (Hud.instance != null)
            {
                parentCanvas = Hud.instance.GetComponentInParent<Canvas>();
            }
            if (parentCanvas == null)
            {
                parentCanvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            }
            if (parentCanvas == null)
            {
                Debug.LogWarning("[OverwriteConfirmDialog] No canvas found, cannot create dialog");
                return;
            }

            // Root panel
            _dialogRoot = new GameObject("OverwriteConfirmDialog");
            _dialogRoot.transform.SetParent(parentCanvas.transform, false);
            var rootRect = _dialogRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Dim background
            var dimGo = new GameObject("DimBG");
            dimGo.transform.SetParent(_dialogRoot.transform, false);
            var dimRect = dimGo.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImg = dimGo.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.6f);

            // Dialog box
            var boxGo = new GameObject("DialogBox");
            boxGo.transform.SetParent(_dialogRoot.transform, false);
            var boxRect = boxGo.AddComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(420, 180);
            var boxImg = boxGo.AddComponent<Image>();
            boxImg.color = new Color(0.12f, 0.10f, 0.08f, 0.95f);

            // Border
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(boxGo.transform, false);
            var borderRect = borderGo.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;
            var borderOutline = borderGo.AddComponent<Outline>();
            borderOutline.effectColor = new Color(0.6f, 0.45f, 0.25f, 0.8f);
            borderOutline.effectDistance = new Vector2(2, 2);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = Color.clear;

            // Message text
            var textGo = new GameObject("Message");
            textGo.transform.SetParent(boxGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0.4f);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(20, 0);
            textRect.offsetMax = new Vector2(-20, -15);
            var tmpText = textGo.AddComponent<TextMeshProUGUI>();
            tmpText.fontSize = 16;
            tmpText.color = new Color(1f, 0.85f, 0.5f);
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.text = "";

            // Button area
            float btnW = 140;
            float btnH = 36;
            float btnY = 25;
            float gap = 20;

            // Overwrite button
            var overwriteGo = CreateButton(boxGo.transform, "OverwriteBtn",
                new Vector2(-gap / 2 - btnW / 2, btnY), new Vector2(btnW, btnH),
                "Overwrite", new Color(0.6f, 0.25f, 0.2f, 1f));

            // Cancel button
            var cancelGo = CreateButton(boxGo.transform, "CancelBtn",
                new Vector2(gap / 2 + btnW / 2, btnY), new Vector2(btnW, btnH),
                "Cancel", new Color(0.3f, 0.3f, 0.3f, 1f));

            // Wire up component
            _instance = _dialogRoot.AddComponent<OverwriteConfirmDialog>();
            _instance._messageText = tmpText;
            _instance._overwriteButton = overwriteGo.GetComponent<Button>();
            _instance._cancelButton = cancelGo.GetComponent<Button>();

            _instance._overwriteButton.onClick.AddListener(_instance.OnOverwriteClicked);
            _instance._cancelButton.onClick.AddListener(_instance.OnCancelClicked);

            _dialogRoot.SetActive(false);
        }

        private static GameObject CreateButton(Transform parent, string name, Vector2 pos, Vector2 size, string label, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.8f;
            btn.colors = colors;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmpLabel = labelGo.AddComponent<TextMeshProUGUI>();
            tmpLabel.text = label;
            tmpLabel.fontSize = 15;
            tmpLabel.color = new Color(1f, 0.9f, 0.7f);
            tmpLabel.alignment = TextAlignmentOptions.Center;

            return go;
        }

        private void OnOverwriteClicked()
        {
            _dialogRoot.SetActive(false);
            _onConfirm?.Invoke();
            _onConfirm = null;
        }

        private void OnCancelClicked()
        {
            _dialogRoot.SetActive(false);
            _onConfirm = null;
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Save cancelled");
        }

        private void Update()
        {
            if (!_dialogRoot.activeSelf) return;

            // Allow Escape to cancel
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnCancelClicked();
            }
        }
    }
}
