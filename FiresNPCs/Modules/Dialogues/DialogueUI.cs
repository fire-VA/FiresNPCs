using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// In-game dialogue panel for NPC conversations.
    /// Built programmatically following the UIPrefabFactory pattern.
    /// 
    /// Features:
    /// - NPC name + dialogue text display (Marketplace-style bottom-center panel)
    /// - Optional background image
    /// - Clickable option buttons with icons, colors, hotbar key shortcuts (1-9, 0)
    /// - Condition-gated options (greyed-out or hidden based on AlwaysVisible)
    /// - @interaction auto-execute (invisible trigger commands)
    /// - ESC to close, close on player death
    /// - Close button (top-right corner)
    /// - Back button with navigation history stack (Backspace hotkey)
    /// - Smooth fade in/out via CanvasGroup
    /// - Input blocking while open
    /// </summary>
    public static class DialogueUI
    {
        // Root objects
        private static GameObject _root;
        private static CanvasGroup _canvasGroup;
        private static RectTransform _panelRect;

        // UI elements
        private static Image _backgroundImage;
        private static TMP_Text _npcNameText;
        private static TMP_Text _dialogueText;
        private static Transform _optionsContent;
        private static ScrollRect _optionsScroll;
        private static Button _closeButton;
        private static Button _backButton;
        private static GameObject _backButtonGO;

        // State
        private static bool _visible;
        private static string _currentUID;
        private static ZNetView _currentNpcView;
        private static string _currentNpcName;
        private static readonly List<OptionEntry> _activeOptions = new List<OptionEntry>();
        private static readonly Stack<string> _navigationStack = new Stack<string>();

        // Codegen panel reference � the "GO" VLG container where options are added
        private static Transform _goContainer;

        // Pre-cached sprites & font for option rows � resolved once, reused per row
        private static bool _optionAssetsCached;
        private static Sprite _cachedFrameWhite;
        private static Sprite _cachedBottomBarBg;
        private static Sprite _cachedRoundFrame;
        private static Sprite _cachedCircle;
        private static Sprite _cachedButtonSmall;
        private static Sprite _cachedButtonSmallPressed;
        private static TMP_FontAsset _cachedBodyFont;

        // Default background sprite (crafting_panel_bkg) applied when no per-dialogue BG_Image is set
        private static Sprite _defaultBgSprite;

        // Fade
        private static float _fadeTarget;
        private static float _fadeSpeed = 6f;

        /// <summary>Tracks a displayed option button and its associated data.</summary>
        private class OptionEntry
        {
            public RawPlayerOption Option;
            public GameObject Root;
            public Button Button;
            public Image BorderImage;
            public TMP_Text IndexText;
            public TMP_Text LabelText;
            public TMP_Text ErrorText;
            public Image IconImage;
            public bool Enabled;
        }

        public static bool IsVisible => _visible;

        #region Lifecycle

        /// <summary>Shows the dialogue UI for a specific NPC and dialogue UID.</summary>
        public static void Show(string npcName, string dialogueUID, ZNetView npcView = null)
        {
            if (string.IsNullOrEmpty(dialogueUID)) return;

            DialogueCodex.Init();
            var dialogue = DialogueCodex.Get(dialogueUID);
            if (dialogue == null)
            {
                Debug.LogWarning($"[DialogueUI] Dialogue UID '{dialogueUID}' not found in codex");
                return;
            }

            _currentNpcView = npcView;
            _currentNpcName = npcName ?? "NPC";
            _currentUID = dialogueUID;
            _navigationStack.Clear();

            EnsureUI();
            PopulateDialogue(dialogue);

            _root.SetActive(true);
            _visible = true;
            _fadeTarget = 1f;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;

            GUIManager.BlockInput(true);
        }

        /// <summary>Transitions to a new dialogue UID (for branching). Pushes current UID to navigation stack.</summary>
        public static void TransitionTo(string nextUID)
        {
            if (string.IsNullOrEmpty(nextUID))
            {
                Hide();
                return;
            }

            DialogueCodex.Init();
            var dialogue = DialogueCodex.Get(nextUID);
            if (dialogue == null)
            {
                Debug.LogWarning($"[DialogueUI] Transition target '{nextUID}' not found � closing");
                Hide();
                return;
            }

            // Push current UID to stack for back navigation
            if (!string.IsNullOrEmpty(_currentUID))
                _navigationStack.Push(_currentUID);

            _currentUID = nextUID;
            PopulateDialogue(dialogue);
        }

        /// <summary>Navigates back to the previous dialogue in the history stack.</summary>
        public static void GoBack()
        {
            if (_navigationStack.Count == 0) return;

            string previousUID = _navigationStack.Pop();

            DialogueCodex.Init();
            var dialogue = DialogueCodex.Get(previousUID);
            if (dialogue == null)
            {
                Debug.LogWarning($"[DialogueUI] Back target '{previousUID}' not found � closing");
                Hide();
                return;
            }

            _currentUID = previousUID;
            PopulateDialogue(dialogue);
        }

        /// <summary>Hides the dialogue UI.</summary>
        public static void Hide()
        {
            if (!_visible) return;

            _visible = false;
            _fadeTarget = 0f;
            _currentUID = null;
            _currentNpcView = null;
            _navigationStack.Clear();

            GUIManager.BlockInput(false);

            if (_root != null)
                _root.SetActive(false);
        }

        /// <summary>Called every frame to handle input and fade animation.</summary>
        public static void Update()
        {
            if (!_visible) return;
            if (_root == null) return;

            // Close on ESC
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            // Back on Backspace
            if (Input.GetKeyDown(KeyCode.Backspace) && _navigationStack.Count > 0)
            {
                GoBack();
                return;
            }

            // Close on player death
            if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead())
            {
                Hide();
                return;
            }

            // Hotbar keys (1-9 = options 0-8, 0 = option 9)
            HandleHotkeys();

            // Fade animation � only update when not yet at target to avoid
            // dirtying the canvas every frame (which forces a full rebuild).
            if (_canvasGroup != null && !Mathf.Approximately(_canvasGroup.alpha, _fadeTarget))
            {
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, _fadeTarget, Time.unscaledDeltaTime * _fadeSpeed);
            }
        }

        #endregion

        #region UI Construction

        private static void EnsureUI()
        {
            if (_root != null) return;
            BuildHardCodedUI();
        }

        private static void BuildHardCodedUI()
        {
            var canvas = GetOrCreateCanvas();

            // Load the baked dialogue panel from the firesnpcsui bundle; fall back to runtime codegen.
            _root = global::FiresRPGmaker.UI.BakedUILoader.Load("npcdialogueuitest1", typeof(GeneratedUI_npcdialogueuitest1), canvas.transform);
            if (_root == null) _root = GeneratedUI_npcdialogueuitest1.Instantiate(canvas.transform);
            _root.name = "DialoguePanel";

            _panelRect = _root.GetComponent<RectTransform>();

            // CanvasGroup for fade
            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            // Add nested canvas for batch isolation when parented under Hud canvas
            EnsureNestedCanvas(_root);

            // Resolve sprites and fonts
            var bodyFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Body);
            GeneratedUI_npcdialogueuitest1.ResolveAssets(CodegenUIWiring.ResolveSpriteByNamePublic, null, bodyFont, null);

            // Fix the "frame" overlay � codegen sets fillCenter=true which paints a
            // solid white rectangle over the BACKGROUND sprite. The frame is a 9-slice
            // border and must NOT fill the center so the background texture shows through.
            var frameGO = GeneratedUI_npcdialogueuitest1.FindByTag("frame");
            if (frameGO != null)
            {
                var frameImg = frameGO.GetComponent<Image>();
                if (frameImg != null)
                {
                    frameImg.fillCenter = false;
                    // If the frame sprite didn't resolve, hide it entirely so it
                    // doesn't render as a white rectangle.
                    if (frameImg.sprite == null)
                        frameImg.color = new Color(1f, 1f, 1f, 0f);
                }
            }

            // Wire tagged elements
            var npcNameGO = GeneratedUI_npcdialogueuitest1.FindByTag("NPC_Name");
            _npcNameText = npcNameGO != null ? npcNameGO.GetComponent<TMP_Text>() : null;

            var dialogueTextGO = GeneratedUI_npcdialogueuitest1.FindByTag("Dialogue_Text");
            _dialogueText = dialogueTextGO != null ? dialogueTextGO.GetComponent<TMP_Text>() : null;

            // The "GO" container is the VLG that holds name, text, spacers � and where
            // we'll add option rows dynamically. Options go after the bottom spacer.
            var goContainer = GeneratedUI_npcdialogueuitest1.FindByTag("GO");
            _goContainer = goContainer != null ? goContainer.transform : null;
            _optionsContent = _goContainer; // Options are added directly to the GO VLG
            _optionsScroll = null;

            // Activate the BACKGROUND child and apply background sprite.
            // Uses the same fallback chain as InfoNpcPlayerPanel:
            //   1. crafting_panel_bkg (vanilla Valheim sprite)
            //   2. npcuibkg (mod asset bundle � always available)
            //   3. solid dark color
            var bgGO = GeneratedUI_npcdialogueuitest1.FindByPath("NPCDialogueUI_Prev/GO/BACKGROUND");
            if (bgGO != null)
            {
                bgGO.SetActive(true);
                _backgroundImage = bgGO.GetComponent<Image>();
                if (_backgroundImage != null)
                {
                    ApplyBackgroundSprite(_backgroundImage);
                }
            }
            else
            {
                _backgroundImage = null;
            }

            // No close button in marketplace style � ESC closes
            _closeButton = null;

            // Back button � created as an option-style row, initially hidden
            _backButtonGO = null;
            _backButton = null;

            _root.SetActive(false);

            // Pre-cache sprites & font for option rows so we avoid per-row ResolveAssets calls
            CacheOptionAssets();
        }

        /// <summary>
        /// Pre-resolves all sprites and the font needed by option row templates.
        /// Called once during BuildHardCodedUI; results are reused for every option row.
        /// </summary>
        private static void CacheOptionAssets()
        {
            if (_optionAssetsCached) return;
            _optionAssetsCached = true;

            var resolver = (Func<string, Sprite>)CodegenUIWiring.ResolveSpriteByNamePublic;
            _cachedFrameWhite = resolver("cached:Frame_White");
            _cachedBottomBarBg = resolver("cached:BottomBar_Background");
            _cachedRoundFrame = resolver("cached:little_round_frame_white");
            _cachedCircle = resolver("cached:circle");
            _cachedButtonSmall = resolver("button_small");
            _cachedButtonSmallPressed = resolver("button_small_pressed");
            _cachedBodyFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Body);
        }

        /// <summary>
        /// Applies the pre-cached sprites and font directly to an option row's components.
        /// Replaces the per-row ResolveAssets() call with simple field assignments.
        /// </summary>
        private static void ApplyCachedOptionAssets(GameObject rowGO)
        {
            // The codegen nodes layout:
            //   _nodes[0] = root button image  -> Frame_White
            //   _nodes[1] = darken image       -> BottomBar_Background
            //   _nodes[2] = Indexer image       -> little_round_frame_white
            //   _nodes[5] = Icon image          -> circle
            //   _nodes[0]/Label TMP             -> bodyFont
            //   _nodes[3] = Indexer/Text TMP    -> bodyFont

            // Root button sprite (Frame_White)
            var rootImg = rowGO.GetComponent<Image>();
            if (rootImg != null && _cachedFrameWhite != null) rootImg.sprite = _cachedFrameWhite;

            // darken child (index 0 in children)
            var darken = rowGO.transform.Find("darken");
            if (darken != null)
            {
                var dImg = darken.GetComponent<Image>();
                if (dImg != null && _cachedBottomBarBg != null) dImg.sprite = _cachedBottomBarBg;
            }

            // Indexer badge sprite � use button_small as background circle for the number
            var indexer = rowGO.transform.Find("Indexer");
            if (indexer != null)
            {
                var iImg = indexer.GetComponent<Image>();
                if (iImg != null)
                    iImg.sprite = _cachedButtonSmall != null ? _cachedButtonSmall : _cachedRoundFrame;

                // Indexer/Text font
                var iTmp = indexer.Find("Text")?.GetComponent<TextMeshProUGUI>();
                if (iTmp != null && _cachedBodyFont != null) iTmp.font = _cachedBodyFont;
            }

            // Icon sprite � use button_small as background circle for the icon
            var textPanel = rowGO.transform.Find("Text");
            if (textPanel != null)
            {
                var iconT = textPanel.Find("Icon");
                if (iconT != null)
                {
                    var iconImg = iconT.GetComponent<Image>();
                    if (iconImg != null)
                        iconImg.sprite = _cachedButtonSmall != null ? _cachedButtonSmall : _cachedCircle;
                }
            }

            // Label font (hidden codegen label)
            var label = rowGO.transform.Find("Label");
            if (label != null)
            {
                var lTmp = label.GetComponent<TextMeshProUGUI>();
                if (lTmp != null && _cachedBodyFont != null) lTmp.font = _cachedBodyFont;
            }
        }

        /// <summary>
        /// Adds a nested Canvas to the dialogue root for batch isolation.
        /// Without this, every text/image change dirties the parent Hud canvas
        /// which contains hundreds of vanilla elements.
        /// </summary>
        private static void EnsureNestedCanvas(GameObject root)
        {
            if (root == null || root.GetComponent<Canvas>() != null) return;
            var canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = false;
            root.AddComponent<GraphicRaycaster>();
        }

        private static Canvas GetOrCreateCanvas()
        {
            // Try to parent under the Hud canvas (Valheim pattern)
            if (Hud.instance != null)
            {
                var hudCanvas = Hud.instance.GetComponentInParent<Canvas>();
                if (hudCanvas != null) return hudCanvas;
            }

            // Fallback: find any screen-space canvas
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(UnityEngine.FindObjectsSortMode.None))
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                    return c;
            }

            // Last resort: create one
            var canvasGO = new GameObject("DialogueCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasGO);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// Applies a background sprite to the given Image using the same fallback
        /// chain as InfoNpcPlayerPanel. Sprite + type + color are applied as a unit
        /// so a failed sprite lookup never leaves a bare white Image.
        /// </summary>
        private static void ApplyBackgroundSprite(Image bgImage)
        {
            // 1. Try crafting_panel_bkg (vanilla Valheim sprite)
            var bgSprite = CodegenUIWiring.ResolveSpriteByNamePublic("crafting_panel_bkg");
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
                bgImage.color = new Color(1f, 1f, 1f, 0.625f);
                _defaultBgSprite = bgSprite;
                return;
            }

            // 2. Fallback: npcuibkg from mod asset bundle (always available)
            var fallbackSprite = UIPrefabFactory.GetBackgroundSprite();
            if (fallbackSprite != null)
            {
                bgImage.sprite = fallbackSprite;
                bgImage.type = Image.Type.Sliced;
                bgImage.color = new Color(1f, 1f, 1f, 0.625f);
                _defaultBgSprite = fallbackSprite;
                return;
            }

            // 3. Last resort: solid dark color (no sprite)
            bgImage.sprite = null;
            bgImage.color = UIFontConfig.Colors.PanelBackground;
            _defaultBgSprite = null;
            Debug.LogWarning("[DialogueUI] No background sprite found � using solid color fallback");
        }

        #endregion

        #region Populate

        private static void PopulateDialogue(RawDialogue dialogue)
        {
            ClearOptions();

            // Show/hide back button based on navigation stack
            if (_backButtonGO != null)
                _backButtonGO.SetActive(_navigationStack.Count > 0);

            // NPC name
            string displayName = DialogueCommandEngine.ApplyReplacements(_currentNpcName);
            _npcNameText.text = Localization.instance != null
                ? Localization.instance.Localize(displayName)
                : displayName;

            // Background image � re-apply fallback chain if sprite is still missing,
            // then handle optional per-dialogue BG_Image override
            if (_backgroundImage != null)
            {
                if (_defaultBgSprite == null)
                    ApplyBackgroundSprite(_backgroundImage);

                if (!string.IsNullOrEmpty(dialogue.BG_Image))
                {
                    var custom = VAMiscAssetManager.GetSprite(dialogue.BG_Image);
                    if (custom != null)
                    {
                        _backgroundImage.sprite = custom;
                        _backgroundImage.type = Image.Type.Sliced;
                    }
                }
                else if (_defaultBgSprite != null && _backgroundImage.sprite != _defaultBgSprite)
                {
                    _backgroundImage.sprite = _defaultBgSprite;
                    _backgroundImage.type = Image.Type.Sliced;
                }

                _backgroundImage.gameObject.SetActive(true);
            }

            // Dialogue text � wrap in quotes to match Marketplace presentation
            string rawText = dialogue.Text ?? "";
            rawText = DialogueCommandEngine.ApplyReplacements(rawText);
            string localizedText = Localization.instance != null
                ? Localization.instance.Localize(rawText)
                : rawText;
            _dialogueText.text = $"\"{localizedText}\"";

            // Process interactions
            int optionIndex = 0;
            if (dialogue.Interactions != null)
            {
                foreach (var interaction in dialogue.Interactions)
                {
                    switch (interaction.Type)
                    {
                        case InteractionType.Interaction:
                            // Auto-execute: evaluate conditions, if pass run commands silently
                            ExecuteInteraction(interaction);
                            break;

                        case InteractionType.InRange:
                        case InteractionType.OutRange:
                            // Proximity triggers � not shown as options (future)
                            break;

                        case InteractionType.Option:
                        default:
                            // Evaluate conditions and create visible option
                            AddOption(interaction, optionIndex);
                            optionIndex++;
                            break;
                    }
                }
            }

            // If no visible options, hide after @interaction commands ran
            if (_activeOptions.Count == 0 && dialogue.Interactions != null)
            {
                bool hasOnlyNonOptions = true;
                foreach (var i in dialogue.Interactions)
                {
                    if (i.Type == InteractionType.Option) { hasOnlyNonOptions = false; break; }
                }
                if (hasOnlyNonOptions)
                {
                    Hide();
                }
            }

            // Reset scroll to top
            if (_optionsScroll != null)
                _optionsScroll.normalizedPosition = new Vector2(0, 1);
        }

        private static void ExecuteInteraction(RawPlayerOption interaction)
        {
            bool condPass = DialogueConditionEngine.EvaluateAll(interaction.Conditions, out _);
            if (!condPass) return;

            if (interaction.Commands != null && interaction.Commands.Length > 0)
                DialogueCommandEngine.ExecuteAll(interaction.Commands, _currentNpcView);
            if (interaction.RandomCommands != null && interaction.RandomCommands.Count > 0)
                DialogueCommandEngine.ExecuteRandom(interaction.RandomCommands, _currentNpcView);
        }

        private static void AddOption(RawPlayerOption option, int index)
        {
            bool condPass = DialogueConditionEngine.EvaluateAll(option.Conditions, out string reason);

            if (!condPass && !option.AlwaysVisible)
                return; // Hidden � don't show at all

            var entry = CreateOptionRow(option, index, condPass, reason);
            _activeOptions.Add(entry);
        }

        private static OptionEntry CreateOptionRow(RawPlayerOption option, int index, bool condPass, string reason)
        {
            var entry = new OptionEntry { Option = option, Enabled = condPass };

            // Create marketplace-style option row from codegen template.
            // The codegen is a static singleton so we call Instantiate (which overwrites
            // static state), then immediately resolve assets and grab references before
            // creating the next option.
            var rowGO = GeneratedUI_dialogueoptiontest.Instantiate(_optionsContent);
            rowGO.name = $"Option_{index}";
            entry.Root = rowGO;

            // Add LayoutElement so options participate in the parent VLG properly
            var rowLE = rowGO.GetComponent<LayoutElement>() ?? rowGO.AddComponent<LayoutElement>();
            rowLE.flexibleWidth = 1f;
            rowLE.minHeight = 30f;

            // Apply pre-cached sprites & font directly � no per-row ResolveAssets call
            CacheOptionAssets();
            ApplyCachedOptionAssets(rowGO);

            // --- Wire index number ---
            var indexer = rowGO.transform.Find("Indexer");
            if (indexer != null)
            {
                var numTmp = indexer.Find("Text")?.GetComponent<TMP_Text>();
                if (numTmp != null)
                {
                    string hotkey = index < 9 ? (index + 1).ToString() : (index == 9 ? "0" : "");
                    numTmp.text = hotkey;
                }
                entry.IndexText = indexer.Find("Text")?.GetComponent<TMP_Text>();
            }

            // --- Wire option text ---
            string optText = option.Text ?? "";
            optText = DialogueCommandEngine.ApplyReplacements(optText);
            string displayText = Localization.instance != null
                ? Localization.instance.Localize(optText)
                : optText;

            if (!condPass)
            {
                string errorMsg = !string.IsNullOrEmpty(option.OverrideError) ? option.OverrideError : reason;
                if (!string.IsNullOrEmpty(errorMsg))
                    displayText += $"\n<size=11><color=#B3261E><i>{errorMsg}</i></color></size>";
            }

            // The "Text" panel (go_4) � add the label TMP directly as a child of
            // the button root's VLG so that TMP's ILayoutElement preferred height
            // drives the row height.  go_4 keeps its Icon but is pulled out of
            // layout flow so it doesn't interfere with sizing.
            var textPanel = rowGO.transform.Find("Text");
            if (textPanel != null)
            {
                // Make the Icon's parent panel ignore layout so it doesn't
                // contribute zero height to the parent VLG.  The Icon stays
                // positioned via its own anchors inside this panel.
                var textPanelLE = textPanel.gameObject.GetComponent<LayoutElement>() ?? textPanel.gameObject.AddComponent<LayoutElement>();
                textPanelLE.ignoreLayout = true;

                // Add the label TMP directly to the button root so the VLG
                // measures it.  The root VLG padding (left=40) already matches
                // the original left offset.
                var labelGo = new GameObject("OptionLabel");
                labelGo.transform.SetParent(rowGO.transform, false);
                var labelRect = labelGo.AddComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                entry.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
                entry.LabelText.text = displayText;
                entry.LabelText.fontSize = 18f;
                entry.LabelText.color = condPass ? new Color(1f, 0.95f, 0.85f) : new Color(0.5f, 0.4f, 0.3f);
                entry.LabelText.alignment = TextAlignmentOptions.MidlineLeft;
                entry.LabelText.textWrappingMode = TextWrappingModes.Normal;
                entry.LabelText.richText = true;
                entry.LabelText.raycastTarget = false;
                entry.LabelText.margin = new Vector4(0f, 4f, 0f, 4f); // left, top, right, bottom
                if (_cachedBodyFont != null) entry.LabelText.font = _cachedBodyFont;
            }

            // --- Wire icon ---
            if (textPanel != null)
            {
                var icon = textPanel.Find("Icon");
                if (icon != null)
                {
                    // Adjust icon position � codegen places it at x=-18 which overlaps
                    // with the Indexer number circle. Move it right for proper padding.
                    var iconRect = icon.GetComponent<RectTransform>();
                    if (iconRect != null)
                        iconRect.anchoredPosition = new Vector2(-10f, iconRect.anchoredPosition.y);

                    var iconImg = icon.GetComponent<Image>();
                    if (iconImg != null)
                    {
                        if (!string.IsNullOrEmpty(option.Icon))
                        {
                            var sprite = TryGetItemSprite(option.Icon);
                            if (sprite != null)
                            {
                                iconImg.sprite = sprite;
                                iconImg.color = condPass ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                            }
                            else
                                iconImg.color = new Color(0.3f, 0.3f, 0.3f, 0.4f);
                        }
                        else
                            iconImg.color = new Color(0.25f, 0.2f, 0.15f, 0.35f);
                        entry.IconImage = iconImg;
                    }
                }
            }

            // --- Wire button ---
            entry.Button = rowGO.GetComponent<Button>();
            entry.BorderImage = rowGO.GetComponent<Image>();
            if (entry.Button != null)
            {
                entry.Button.interactable = condPass;
                int capturedIndex = index;
                entry.Button.onClick.RemoveAllListeners();
                entry.Button.onClick.AddListener(() => OnOptionClicked(capturedIndex));
            }

            return entry;
        }

        private static void ClearOptions()
        {
            foreach (var entry in _activeOptions)
            {
                if (entry.Root != null)
                    UnityEngine.Object.Destroy(entry.Root);
            }
            _activeOptions.Clear();
        }

        #endregion

        #region Input

        private static void HandleHotkeys()
        {
            // Keys 1-9 map to options 0-8; key 0 maps to option 9
            for (int i = 0; i < 10 && i < _activeOptions.Count; i++)
            {
                KeyCode key = i < 9 ? KeyCode.Alpha1 + i : KeyCode.Alpha0;
                if (Input.GetKeyDown(key))
                {
                    OnOptionClicked(i);
                    return;
                }
            }
        }

        private static void OnOptionClicked(int index)
        {
            if (index < 0 || index >= _activeOptions.Count) return;

            var entry = _activeOptions[index];
            if (!entry.Enabled)
            {
                // Show error message for disabled option
                string errorMsg = entry.ErrorText != null ? entry.ErrorText.text : "Requirements not met.";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, errorMsg);
                return;
            }

            // Visual feedback: swap Indexer background to button_small_pressed
            if (_cachedButtonSmallPressed != null)
            {
                var indexer = entry.Root?.transform.Find("Indexer");
                if (indexer != null)
                {
                    var iImg = indexer.GetComponent<Image>();
                    if (iImg != null) iImg.sprite = _cachedButtonSmallPressed;
                }
            }

            var option = entry.Option;

            // Execute commands
            if (option.Commands != null && option.Commands.Length > 0)
                DialogueCommandEngine.ExecuteAll(option.Commands, _currentNpcView);
            if (option.RandomCommands != null && option.RandomCommands.Count > 0)
                DialogueCommandEngine.ExecuteRandom(option.RandomCommands, _currentNpcView);

            // Transition
            string nextUID = option.GetNextTransition();
            if (!string.IsNullOrEmpty(nextUID))
            {
                TransitionTo(nextUID);
            }
            else
            {
                // No transition � close dialogue
                Hide();
            }
        }

        #endregion

        #region Helpers

        /// <summary>Tries to get a sprite by prefab name. Checks items, creature trophies,
        /// and the asset manager as a fallback.</summary>
        private static Sprite TryGetItemSprite(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;

            try
            {
                // 1) Try item prefab icon (e.g. "SwordIron")
                if (ObjectDB.instance != null)
                {
                    var prefab = ObjectDB.instance.GetItemPrefab(iconName);
                    if (prefab != null)
                    {
                        var drop = prefab.GetComponent<ItemDrop>();
                        if (drop != null && drop.m_itemData != null)
                            return drop.m_itemData.GetIcon();
                    }
                }

                // 2) Try ZNetScene prefab � could be an item or a creature
                if (ZNetScene.instance != null)
                {
                    var prefab = ZNetScene.instance.GetPrefab(iconName);
                    if (prefab != null)
                    {
                        // Check for ItemDrop first
                        var drop = prefab.GetComponent<ItemDrop>();
                        if (drop != null && drop.m_itemData != null)
                            return drop.m_itemData.GetIcon();

                        // If it's a creature, try to find its trophy item icon
                        var character = prefab.GetComponent<Character>();
                        if (character != null)
                        {
                            var trophySprite = TryGetCreatureTrophySprite(iconName);
                            if (trophySprite != null)
                                return trophySprite;
                        }
                    }
                }

                // 3) Try the trophy item directly (e.g. user typed "Boar" ? look for "TrophyBoar")
                if (ObjectDB.instance != null)
                {
                    var trophyPrefab = ObjectDB.instance.GetItemPrefab("Trophy" + iconName);
                    if (trophyPrefab != null)
                    {
                        var drop = trophyPrefab.GetComponent<ItemDrop>();
                        if (drop != null && drop.m_itemData != null)
                            return drop.m_itemData.GetIcon();
                    }
                }

                // 4) Fallback: try loading a sprite from the asset manager
                var assetSprite = FiresCore.UI.VAMiscAssetManager.GetSprite(iconName);
                if (assetSprite != null)
                    return assetSprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueUI] Error loading icon '{iconName}': {ex.Message}");
            }

            return null;
        }

        /// <summary>Searches for a creature's trophy item sprite by trying common trophy naming patterns.</summary>
        private static Sprite TryGetCreatureTrophySprite(string creatureName)
        {
            if (ObjectDB.instance == null) return null;

            // Common pattern: "Trophy" + creature name (e.g. TrophyBoar, TrophyDeer)
            string[] trophyNames = {
                "Trophy" + creatureName,
                "Trophy" + creatureName.Replace(" ", ""),
            };

            foreach (var name in trophyNames)
            {
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                if (prefab != null)
                {
                    var drop = prefab.GetComponent<ItemDrop>();
                    if (drop != null && drop.m_itemData != null)
                        return drop.m_itemData.GetIcon();
                }
            }

            return null;
        }

        /// <summary>Cleans up the UI entirely (call on scene unload).</summary>
        public static void Destroy()
        {
            Hide();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _canvasGroup = null;
            _panelRect = null;
            _backgroundImage = null;
            _npcNameText = null;
            _dialogueText = null;
            _optionsContent = null;
            _optionsScroll = null;
            _closeButton = null;
            _backButton = null;
            _backButtonGO = null;
            _goContainer = null;
            _defaultBgSprite = null;
            _optionAssetsCached = false;
            _cachedFrameWhite = null;
            _cachedBottomBarBg = null;
            _cachedRoundFrame = null;
            _cachedCircle = null;
            _cachedButtonSmall = null;
            _cachedButtonSmallPressed = null;
            _cachedBodyFont = null;
            _activeOptions.Clear();
            _navigationStack.Clear();
        }

        #endregion

        #region Harmony Patches

        /// <summary>Drives DialogueUI.Update() every frame via Hud.Update.</summary>
        [HarmonyPatch(typeof(Hud), "Update")]
        private static class Hud_Update_DialogueUI_Patch
        {
            private static void Postfix()
            {
                DialogueUI.Update();
            }
        }

        /// <summary>Cleanup dialogue UI when Hud is destroyed (logout).</summary>
        [HarmonyPatch(typeof(Hud), "OnDestroy")]
        private static class Hud_OnDestroy_DialogueUI_Patch
        {
            private static void Prefix()
            {
                DialogueUI.Destroy();
            }
        }

        #endregion
    }
}
