using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.Modules.ChatSystem
{
    /// <summary>
    /// RPG-style channel toggle buttons that sit to the LEFT of the vanilla
    /// chat box.  The vanilla Chat_box hierarchy is left completely untouched �
    /// no resizing, no re-parenting of any vanilla object.
    ///
    /// Hierarchy (from the dump):
    ///   IngameGui
    ///     Chat_box  [Canvas, CanvasScaler, RectMask2D]   ? m_chatWindow
    ///       root  anchor(1,0)  500�400                   ? the visible panel
    ///         bkg
    ///         Text        (m_output)
    ///         ChatInput   (m_input)  500�35.5
    ///
    /// We add our button column inside <c>root</c> so it lives in the same
    /// Canvas / CanvasScaler as the rest of the chat UI and inherits the
    /// same show/hide behaviour (Chat_box toggled by vanilla code).
    ///
    /// Visual result (bottom-right of screen):
    ///
    ///   [ L ]                        
    ///   [ G ]   |  vanilla chat window �  |
    ///   [Grp]   |  ChatInput field �      |
    ///   [ C ]
    ///
    /// The column is anchored to the left edge of <c>root</c>, vertically
    /// centered on ChatInput, growing upward via a VerticalLayoutGroup.
    /// </summary>
    public class ChatChannelUI : MonoBehaviour
    {
        private static ChatChannelUI _instance;
        public static ChatChannelUI Instance => _instance;

        // ?? Layout tunables ???????????????????????????????????
        private const float BTN_WIDTH       = 36f;
        private const float BTN_HEIGHT      = 24f;
        private const float BTN_SPACING     = 2f;
        private const float BTN_FONT        = 13f;
        private const float GAP_TO_CHAT     = 4f;    // pixels between column and chat root

        // ?? Runtime state ?????????????????????????????????????
        private GameObject _column;
        private RectTransform _columnRect;
        private Button[] _buttons;
        private TextMeshProUGUI[] _labels;
        private Image[] _backgrounds;
        private bool _built;

        // ?? Public API ????????????????????????????????????????

        /// <summary>
        /// Idempotent � call after Chat.instance exists.
        /// </summary>
        public static void EnsureCreated()
        {
            if (_instance != null) return;
            if (Chat.instance == null) return;

            // Attach to Chat so we get destroyed with it.
            var go = new GameObject("VA_ChatChannelUI");
            go.transform.SetParent(Chat.instance.transform, false);
            _instance = go.AddComponent<ChatChannelUI>();
        }

        // ?? Unity lifecycle ???????????????????????????????????

        private void Awake()
        {
            _instance = this;
            ChatChannelManager.OnChannelChanged += OnChannelChanged;
        }

        private void OnDestroy()
        {
            ChatChannelManager.OnChannelChanged -= OnChannelChanged;
            if (_instance == this) _instance = null;
        }

        private void LateUpdate()
        {
            // Deferred build: wait until Chat UI hierarchy is ready.
            if (!_built)
            {
                if (Chat.instance != null &&
                    Chat.instance.m_chatWindow != null &&
                    Chat.instance.m_input != null)
                {
                    Build();
                }
                return;
            }

            SyncVisibility();
            HandleTabCycle();
        }

        // ?? Build ?????????????????????????????????????????????

        private void Build()
        {
            // m_chatWindow = Chat_box (has Canvas + CanvasScaler).
            // Its first child "root" is the 500�400 visible panel.
            var chatBoxRT = Chat.instance.m_chatWindow;
            Transform rootTransform = chatBoxRT.Find("root");
            if (rootTransform == null)
            {
                // Fallback: use m_chatWindow itself
                rootTransform = chatBoxRT;
            }

            var inputRT = (Chat.instance.m_input as Component).GetComponent<RectTransform>();

            // ?? Create column GO as a child of "root" ??
            _column = new GameObject("VA_ChannelBtnCol");
            _column.transform.SetParent(rootTransform, false);

            _columnRect = _column.AddComponent<RectTransform>();

            // Anchor to bottom-left of "root", same anchor point as ChatInput.
            // root uses pivot (0.5, 0.5) with size 500�400, anchor (1,0) in Chat_box.
            // ChatInput has anchor (0,0) in root ? that maps to root's bottom-left.
            _columnRect.anchorMin = new Vector2(0f, 0f);
            _columnRect.anchorMax = new Vector2(0f, 0f);

            // Pivot at (1, 0) = right-bottom so the column grows UPWARD
            // from the bottom of the input field.
            _columnRect.pivot = new Vector2(1f, 0f);

            // Place right edge flush with ChatInput's left edge, bottom
            // aligned with the input field's bottom edge.
            // ChatInput anchoredPos ? (-0.5, 150.25), size = (500, 35.5)
            // Left edge  = anchoredPos.x - size.x * 0.5
            // Bottom     = anchoredPos.y - size.y * 0.5
            float inputLeftLocal   = inputRT.anchoredPosition.x - inputRT.sizeDelta.x * 0.5f;
            float inputBottomLocal = inputRT.anchoredPosition.y - inputRT.sizeDelta.y * 0.5f;

            _columnRect.anchoredPosition = new Vector2(
                inputLeftLocal - GAP_TO_CHAT,
                inputBottomLocal);

            // VerticalLayoutGroup stacks the buttons top-to-bottom.
            var vlg = _column.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = BTN_SPACING;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childForceExpandWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            var fitter = _column.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Create buttons.
            var channels = (ChatChannelManager.Channel[])System.Enum.GetValues(typeof(ChatChannelManager.Channel));
            _buttons     = new Button[channels.Length];
            _labels      = new TextMeshProUGUI[channels.Length];
            _backgrounds = new Image[channels.Length];

            for (int i = 0; i < channels.Length; i++)
                MakeButton(channels[i], i);

            RefreshVisuals();
            _built = true;
        }

        private void MakeButton(ChatChannelManager.Channel channel, int idx)
        {
            string text = ChannelLabel(channel);

            // ?? Root GO with Image (raycast target) ??
            var go = new GameObject($"VA_Btn_{channel}");
            go.transform.SetParent(_column.transform, false);

            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            img.raycastTarget = true;
            _backgrounds[idx] = img;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = BTN_WIDTH;
            le.preferredHeight = BTN_HEIGHT;

            // ?? Button (uses Image as targetGraphic) ??
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var cb = ColorBlock.defaultColorBlock;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.8f);
            cb.pressedColor     = new Color(0.7f, 0.7f, 0.7f, 1f);
            cb.fadeDuration     = 0.05f;
            btn.colors = cb;

            // No keyboard/gamepad navigation � must not steal focus from input.
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            _buttons[idx] = btn;

            // ?? Text label (child, fills parent, NOT a raycast target) ??
            var txtGO = new GameObject("Lbl");
            txtGO.transform.SetParent(go.transform, false);

            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text           = text;
            tmp.fontSize       = BTN_FONT;
            tmp.alignment      = TextAlignmentOptions.Center;
            tmp.overflowMode   = TextOverflowModes.Truncate;
            tmp.raycastTarget  = false;

            var font = GrabFont();
            if (font != null) tmp.font = font;

            _labels[idx] = tmp;

            // ?? Click ??
            var ch = channel;
            btn.onClick.AddListener(() => OnClick(ch));
        }

        // ?? Visibility sync (every frame) ?????????????????????

        private void SyncVisibility()
        {
            if (_column == null || Chat.instance == null) return;

            // Chat_box is toggled active/inactive by vanilla code.
            // m_input's own activeSelf is always true � the parent Chat_box
            // controls hierarchy visibility.  Check activeInHierarchy.
            bool show = Chat.instance.m_chatWindow != null &&
                        Chat.instance.m_chatWindow.gameObject.activeInHierarchy &&
                        Chat.instance.m_input != null &&
                        Chat.instance.m_input.gameObject.activeInHierarchy;

            _column.SetActive(show);
        }

        // ?? Tab-key channel cycling ?????????????????????????

        /// <summary>
        /// When the chat input is focused, Tab cycles through channels.
        /// The mouse cursor isn't available during chat so this is the
        /// primary way to switch channels.
        /// </summary>
        private void HandleTabCycle()
        {
            if (Chat.instance == null) return;
            if (Chat.instance.m_input == null) return;
            if (!Chat.instance.m_input.isFocused) return;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                ChatChannelManager.CycleChannel();

                // Re-focus the input field in case Tab caused a focus change.
                Chat.instance.m_input.ActivateInputField();
            }
        }

        // ?? Click handling ????????????????????????????????????

        private void OnClick(ChatChannelManager.Channel channel)
        {
            if (!ChatChannelManager.IsChannelAvailable(channel))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{ChatChannelManager.GetChannelDisplayName(channel)} chat � coming soon!");
            }
            else
            {
                ChatChannelManager.ActiveChannel = channel;
            }

            // Always re-focus the input so typing isn't interrupted.
            Chat.instance?.m_input?.ActivateInputField();
        }

        private void OnChannelChanged(ChatChannelManager.Channel _) => RefreshVisuals();

        // ?? Visual refresh ????????????????????????????????????

        private void RefreshVisuals()
        {
            if (_buttons == null) return;

            var channels = (ChatChannelManager.Channel[])System.Enum.GetValues(typeof(ChatChannelManager.Channel));
            var active   = ChatChannelManager.ActiveChannel;

            for (int i = 0; i < channels.Length && i < _buttons.Length; i++)
            {
                var ch    = channels[i];
                var img   = _backgrounds[i];
                var txt   = _labels[i];
                if (img == null || txt == null) continue;

                Color col = ChatChannelManager.GetChannelColor(ch);
                bool sel  = ch == active;
                bool avl  = ChatChannelManager.IsChannelAvailable(ch);

                if (sel)
                {
                    img.color = new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.9f);
                    txt.color = col;
                }
                else if (avl)
                {
                    img.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
                    txt.color = new Color(0.55f, 0.55f, 0.55f, 0.8f);
                }
                else
                {
                    img.color = new Color(0.06f, 0.06f, 0.06f, 0.45f);
                    txt.color = new Color(0.3f, 0.3f, 0.3f, 0.45f);
                }
            }
        }

        // ?? Helpers ???????????????????????????????????????????

        private static string ChannelLabel(ChatChannelManager.Channel ch)
        {
            switch (ch)
            {
                case ChatChannelManager.Channel.Local:  return "L";
                case ChatChannelManager.Channel.Global: return "G";
                case ChatChannelManager.Channel.Group:  return "Grp";
                case ChatChannelManager.Channel.Clan:   return "Gld";
                default: return "?";
            }
        }

        private static TMP_FontAsset GrabFont()
        {
            if (Chat.instance?.m_input != null)
            {
                var field = Chat.instance.m_input as TMP_InputField;
                if (field?.textComponent?.font != null)
                    return field.textComponent.font;
            }
            return null;
        }
    }
}
