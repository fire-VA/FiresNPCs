using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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

        // Codegen panel reference - the "GO" VLG container where options are added
        private static Transform _goContainer;

        // Pre-cached sprites & font for option rows - resolved once, reused per row
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
            public string ErrorMessage;
            public Image IconImage;
            public bool Enabled;
            public string CooldownKey;
        }

        // Cooldown gating: subscription guard + the in-flight pick awaiting server approval.
        private static bool _cooldownHandlersHooked;
        private static string _pendingPickDialogueUID;
        private static RawPlayerOption _pendingPickOption;
        private static ZNetView _pendingPickNpcView;

        public static bool IsVisible => _visible;

        /// <summary>Set when PopulateDialogue closed the panel itself (a page of only @interaction lines).</summary>
        private static bool _closedWhilePopulating;

        /// <summary>
        /// Set where a page is ENTERED (Show, TransitionTo, GoBack) and consumed by PopulateDialogue: @interaction lines run
        /// once per entry. The cooldown reply's re-populate (a hidden-while-cooling option appearing) doesn't set it, so it
        /// no longer runs them twice (rpg_dialogue_test finding 5: a GiveItem interaction gave double on the dedi).
        /// </summary>
        private static bool _pageEntered;

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

            // Publish NPC context for dialogue conditions (NPCNameEquals / NPCModelEquals lookup).
            try
            {
                DialogueConditionEngine.CurrentNpcName = _currentNpcName;
                string model = null;
                if (npcView != null)
                {
                    // Pull the model override from the NpcController on the same GameObject if present.
                    var npcCtl = npcView.GetComponentInChildren<global::FiresRPGmaker.UI.NpcController>(true);
                    if (npcCtl != null) model = npcCtl.modelOverride;
                }
                DialogueConditionEngine.CurrentNpcModel = model;
            }
            catch { }

            EnsureUI();
            HookCooldownHandlers();
            RequestCooldownState(dialogueUID);
            _closedWhilePopulating = false;
            _pageEntered = true;
            PopulateDialogue(dialogue);
            // A page of only @interaction lines runs them and closes; it must not be reopened, empty, below
            // (rpg_dialogue_test, 10-08: opened by E it stayed up with nothing in it).
            if (_closedWhilePopulating) return;

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
                Debug.LogWarning($"[DialogueUI] Transition target '{nextUID}' not found - closing");
                Hide();
                return;
            }

            // Push current UID to stack for back navigation
            if (!string.IsNullOrEmpty(_currentUID))
                _navigationStack.Push(_currentUID);

            _currentUID = nextUID;
            RequestCooldownState(nextUID);
            _pageEntered = true;
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
                Debug.LogWarning($"[DialogueUI] Back target '{previousUID}' not found - closing");
                Hide();
                return;
            }

            _currentUID = previousUID;
            RequestCooldownState(previousUID);
            _pageEntered = true;
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
            DialogueConditionEngine.CurrentNpcName = null;
            DialogueConditionEngine.CurrentNpcModel = null;

            NpcHoverHudSuppressor.Clear();
            GUIManager.BlockInput(false);

            if (_root != null)
                _root.SetActive(false);
        }

        /// <summary>Called every frame to handle input and fade animation.</summary>
        public static void Update()
        {
            if (!_visible) return;
            if (_root == null) return;

            // Escape is owned by the central Menu.Show hook (UIInitializer.Menu_Show_Guard → CloseAllPanels).
            // No self-close on Escape here: the panel stays open until the hook intercepts the vanilla menu
            // open, so the menu never appears on the same press. (Backspace still steps back the nav stack.)
            //
            // SPACEBAR closes the dialogue (owner request — dialogue + info only, never UIs with text fields).
            // Space doesn't open the vanilla menu, so a direct self-close here is safe (no race/hook needed).
            // Keys go through Core's FiresKeys (the same seam FDT's TestKeys press), and none of them act while the player
            // is typing in chat, the console or a text prompt (an EnterPassword box) (rpg_dialogue_test, 10-08).
            bool typing = Typing();
            if (!typing && FiresCore.Input.FiresKeys.GetKeyDown(KeyCode.Space))
            {
                Hide();
                return;
            }

            // Back on Backspace
            if (!typing && FiresCore.Input.FiresKeys.GetKeyDown(KeyCode.Backspace) && _navigationStack.Count > 0)
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
            if (!typing) HandleHotkeys();

            // Fade animation - only update when not yet at target to avoid
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

            // Fix the "frame" overlay - codegen sets fillCenter=true which paints a
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

            // The "GO" container is the VLG that holds name, text, spacers - and where
            // we'll add option rows dynamically. Options go after the bottom spacer.
            var goContainer = GeneratedUI_npcdialogueuitest1.FindByTag("GO");
            _goContainer = goContainer != null ? goContainer.transform : null;
            _optionsContent = _goContainer; // Options are added directly to the GO VLG
            _optionsScroll = null;

            // Top-right close ✕ (rounded gold-on-brown, matches the dialogue theme). Escape also closes via the
            // central Menu.Show hook — this is the visible click target. Anchor to the VISIBLE panel ("GO", the
            // 750px bottom-centre box), NOT _root, which is a full-screen stretch (0,0→1,1) whose top-right is
            // the screen corner (the "X way up top-right" bug). AddCloseX marks itself ignoreLayout so the GO
            // VerticalLayoutGroup leaves it floating in the corner.
            var closeXHost = (goContainer != null ? goContainer.GetComponent<RectTransform>() : null) ?? _panelRect;
            var closeX = global::FiresRPGmaker.UI.GUIManager.AddCloseX(closeXHost, Hide);
            if (closeX != null) closeX.transform.SetAsLastSibling();

            // Breathing room between the last option and the panel's bottom edge.
            var goVlg = goContainer != null ? goContainer.GetComponent<VerticalLayoutGroup>() : null;
            if (goVlg != null)
            {
                var pad = goVlg.padding;
                pad.bottom += 14;
                goVlg.padding = pad;
            }

            // Activate the BACKGROUND child and apply background sprite.
            // Uses the same fallback chain as InfoNpcPlayerPanel:
            //   1. crafting_panel_bkg (vanilla Valheim sprite)
            //   2. npcuibkg (mod asset bundle - always available)
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

            // No close button in marketplace style - ESC closes
            _closeButton = null;

            // Back button - created as an option-style row, initially hidden
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

            // Row RIM: a WHITE rounded-rect sprite tinted per-row (bronze at rest, green on hover — set in
            // CreateOptionRow). It's the full rounded rect drawn BEHIND the fill; only its ~3px edge is ever
            // visible because the OPAQUE fill below sits IN FRONT of its centre and blocks it. Tinting the rim
            // green can never fill the button — the brown fill covers the centre. Rounded to match the fill.
            var rootImg = rowGO.GetComponent<Image>();
            if (rootImg != null)
            {
                rootImg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(11, Color.white);
                rootImg.type = Image.Type.Sliced;
                rootImg.fillCenter = true;
            }

            // Row FILL (the 'darken' child): an OPAQUE brown rounded rect, INSET 3px so the rim shows around
            // it. Opaque = the rim's green can NEVER bleed through the centre (the fix for the "whole button
            // turns green" bug). Rounded to match the rim; raycast off; ignores layout so the inset holds.
            var darken = rowGO.transform.Find("darken");
            if (darken != null)
            {
                var dImg = darken.GetComponent<Image>();
                if (dImg != null)
                {
                    dImg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(9, Color.white);
                    dImg.type = Image.Type.Sliced;
                    dImg.color = new Color(0.30f, 0.21f, 0.12f, 1f);
                    dImg.raycastTarget = false;
                }
                var dLE = darken.GetComponent<LayoutElement>() ?? darken.gameObject.AddComponent<LayoutElement>();
                dLE.ignoreLayout = true;
                var dRect = darken.GetComponent<RectTransform>();
                if (dRect != null)
                {
                    dRect.anchorMin = Vector2.zero; dRect.anchorMax = Vector2.one;
                    dRect.offsetMin = new Vector2(3f, 3f);
                    dRect.offsetMax = new Vector2(-3f, -3f);
                }
            }

            // Indexer badge — our SQUARE (owner: the first slot is a square, not kg's round coin), now a
            // rounded dark chip with a thin gold inset border (FUC rounded skin), number CENTERED inside, and
            // sized a touch larger than the row so it reads as a raised chip (kg parity).
            var indexer = rowGO.transform.Find("Indexer");
            if (indexer != null)
            {
                var iImg = indexer.GetComponent<Image>();
                if (iImg != null)
                {
                    iImg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(6,
                        new Color(0.12f, 0.10f, 0.07f, 0.98f), new Color(0.72f, 0.55f, 0.28f, 1f), 1);
                    iImg.type = Image.Type.Sliced;
                    iImg.color = Color.white;
                }
                // Center-anchored on the row so the SQUARE and the CIRCLE line up at the same height.
                var iRect = indexer.GetComponent<RectTransform>();
                if (iRect != null)
                {
                    iRect.anchorMin = new Vector2(0f, 0.5f);
                    iRect.anchorMax = new Vector2(0f, 0.5f);
                    iRect.pivot = new Vector2(0.5f, 0.5f);
                    iRect.sizeDelta = new Vector2(40f, 40f);        // slightly larger than the 38 row
                    // Flush to the LEFT edge so the chip COVERS the rim's left segment — the green highlight
                    // then reads as a 3-sided frame that begins at the chip's right edge (owner request).
                    iRect.anchoredPosition = new Vector2(20f, 0f);
                }
                var iLE = indexer.GetComponent<LayoutElement>() ?? indexer.gameObject.AddComponent<LayoutElement>();
                iLE.ignoreLayout = true;

                // Center the NUMBER inside the badge box (was left/baseline-drifting before).
                var iText = indexer.Find("Text");
                if (iText != null)
                {
                    var tRect = iText.GetComponent<RectTransform>();
                    if (tRect != null)
                    {
                        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one;
                        tRect.offsetMin = Vector2.zero; tRect.offsetMax = Vector2.zero;
                    }
                    var iTmp = iText.GetComponent<TextMeshProUGUI>();
                    if (iTmp != null)
                    {
                        if (_cachedBodyFont != null) iTmp.font = _cachedBodyFont;
                        iTmp.alignment = TextAlignmentOptions.Center;
                        iTmp.color = new Color(0.93f, 0.82f, 0.52f);
                    }
                }
            }

            // Icon — kg's plain circle sprite.
            var textPanel = rowGO.transform.Find("Text");
            if (textPanel != null)
            {
                var iconT = textPanel.Find("Icon");
                if (iconT != null)
                {
                    var iconImg = iconT.GetComponent<Image>();
                    if (iconImg != null)
                        iconImg.sprite = _cachedCircle != null ? _cachedCircle : _cachedButtonSmall;
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
            Debug.LogWarning("[DialogueUI] No background sprite found - using solid color fallback");
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

            // Background image - re-apply fallback chain if sprite is still missing,
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

                // Admin-tunable background opacity (UI.Dialogue/DialogueBackgroundOpacity).
                float bgOpacity = 1f;
                try { bgOpacity = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configDialogueBgOpacity?.Value ?? 1f; }
                catch { }
                var bgColor = _backgroundImage.color;
                bgColor.a = Mathf.Clamp(bgOpacity, 0.1f, 1f);
                _backgroundImage.color = bgColor;

                _backgroundImage.gameObject.SetActive(true);
            }

            // Dialogue text - wrap in quotes to match Marketplace presentation
            string rawText = dialogue.Text ?? "";
            rawText = DialogueCommandEngine.ApplyReplacements(rawText);
            string localizedText = Localization.instance != null
                ? Localization.instance.Localize(rawText)
                : rawText;
            _dialogueText.text = $"\"{localizedText}\"";

            // Process interactions (their commands only on a fresh page entry, see _pageEntered)
            bool runInteractions = _pageEntered;
            _pageEntered = false;
            int optionIndex = 0;
            if (dialogue.Interactions != null)
            {
                foreach (var interaction in dialogue.Interactions)
                {
                    switch (interaction.Type)
                    {
                        case InteractionType.Interaction:
                            // Auto-execute: evaluate conditions, if pass run commands silently (once per page entry)
                            if (runInteractions) ExecuteInteraction(interaction);
                            break;

                        case InteractionType.InRange:
                        case InteractionType.OutRange:
                            // Proximity triggers — fired by NpcController.FireProximityDialogueInteractions
                            // on the 10m/12m range transition; never rendered as options.
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
                    _closedWhilePopulating = true;
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
                return; // Hidden - don't show at all

            // Cooldown gate: if the server says this option is still cooling, hide or grey it.
            int cooldownRemaining = GetCooldownRemaining(option);
            if (cooldownRemaining > 0)
            {
                if (!option.ShowWhileCooling)
                    return; // Hidden while cooling

                condPass = false;
                reason = $"Available in {FormatCooldown(cooldownRemaining)}";
            }

            // Numbered by its VISIBLE position (hidden options take no number): the badge, its hotkey and the click all
            // index _activeOptions, so a hidden option above can't make a click run the wrong row (rpg_dialogue_test).
            var entry = CreateOptionRow(option, _activeOptions.Count, condPass, reason);
            _activeOptions.Add(entry);
        }

        private static OptionEntry CreateOptionRow(RawPlayerOption option, int index, bool condPass, string reason)
        {
            var entry = new OptionEntry { Option = option, Enabled = condPass };
            if (option.Cooldown != CooldownScope.None)
                entry.CooldownKey = DialogueKeyUtil.OptionKey(_currentUID, option);

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
            rowLE.minHeight = 38f;   // fits the 36px badge + 32px circle without clipping

            // Text start: with an icon the layout is square(3..37) + circle(41..75) → text at 82; with
            // no icon the circle is dropped and text starts right after the square (46).
            bool hasIcon = !string.IsNullOrEmpty(option.Icon);
            int textLeftPad = hasIcon ? 82 : 46;
            var rowVlg = rowGO.GetComponent<VerticalLayoutGroup>();
            if (rowVlg != null)
            {
                var rowPad = rowVlg.padding;
                rowPad.left = textLeftPad;
                rowVlg.padding = rowPad;
            }

            // Apply pre-cached sprites & font directly - no per-row ResolveAssets call
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
                entry.ErrorMessage = errorMsg;   // what a click / hotkey on this greyed row says (OverrideError first)
            }

            // The "Text" panel (go_4) - add the label TMP directly as a child of
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
                entry.LabelText.fontSize = 16f;   // kg prefab label size
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
                // No icon on this option → drop the circle entirely; text already starts at the
                // square (textLeftPad above), so the row reads square → text with no empty slot.
                if (icon != null && !hasIcon)
                {
                    icon.gameObject.SetActive(false);
                    icon = null;
                }
                if (icon != null)
                {
                    // Square-with-number, then the circle at the SAME visual height as the square,
                    // its center dot on the row's vertical center, then the text. Reparented to the
                    // row root so the collapsed text panel can't drag it off-center.
                    icon.SetParent(rowGO.transform, false);
                    var iconRect = icon.GetComponent<RectTransform>();
                    if (iconRect != null)
                    {
                        iconRect.anchorMin = new Vector2(0f, 0.5f);
                        iconRect.anchorMax = new Vector2(0f, 0.5f);
                        iconRect.pivot = new Vector2(0.5f, 0.5f);
                        iconRect.sizeDelta = new Vector2(34f, 34f);       // same height as the square
                        iconRect.anchoredPosition = new Vector2(58f, 0f); // circle slot, row vertical center
                    }
                    var iconLE = icon.gameObject.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
                    iconLE.ignoreLayout = true;

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

            // --- kg's Frame_White border (parity) + green hover tint ---
            // The codegen root Image is kg's own Frame_White frame, and ApplyCachedOptionAssets sets
            // fillCenter=false → it renders as a THIN outlined border, not a slab (the old white slab was
            // fillCenter=true + Selectable ColorTint, both gone: transition stays None). Default tint is
            // bronze/gold — kg's outlined button; on real pointer-over the SAME border tints green. The dark
            // row fill comes from the 'darken' child (BottomBar_Background), so the text stays readable.
            var bronze = new Color(0.62f, 0.47f, 0.24f, 1f);
            var green = new Color(0.2235f, 1f, 0f, 1f);
            // kg `Color: R,G,B` option tint (Dialogues_UI: line + Indexer/bg). White = the unset default →
            // keep our bronze look. A custom color becomes the row's REST rim tint (hover still green) and
            // tints the number chip, mapping kg's two accent surfaces onto ours.
            bool hasCustomColor = option.Color.r != 255 || option.Color.g != 255 || option.Color.b != 255;
            if (hasCustomColor)
            {
                bronze = new Color(option.Color.r / 255f, option.Color.g / 255f, option.Color.b / 255f, 1f);
                var badgeImg = rowGO.transform.Find("Indexer")?.GetComponent<Image>();
                if (badgeImg != null) badgeImg.color = bronze;
            }
            var rootImg = rowGO.GetComponent<Image>();
            if (rootImg != null)
            {
                rootImg.color = bronze;
                rootImg.raycastTarget = true;
            }
            entry.Button = rowGO.GetComponent<Button>();
            if (entry.Button != null)
            {
                entry.Button.transition = Selectable.Transition.None;   // no auto-select grey slab
                entry.Button.interactable = condPass;
                int capturedIndex = index;
                entry.Button.onClick.RemoveAllListeners();
                entry.Button.onClick.AddListener(() => OnOptionClicked(capturedIndex));
            }
            if (condPass && rootImg != null)
            {
                var hover = rowGO.AddComponent<DialogueOptionHover>();
                hover.Target = rootImg;
                hover.Normal = bronze;
                hover.Hover = green;
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

        /// <summary>The player is typing somewhere: chat, the console, or a text prompt.</summary>
        private static bool Typing() =>
            FiresCore.Input.FiresInputBlock.IsTyping() // 0.0.550 typing gate (Fire 10-09)
            || (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible();

        private static void HandleHotkeys()
        {
            if (Typing()) return; // 0.0.550 typing gate (Fire 10-09)
            // Keys 1-9 map to options 0-8; key 0 maps to option 9. Options are numbered by their VISIBLE position, so the
            // key, the badge and a click all reach the same row.
            for (int i = 0; i < 10 && i < _activeOptions.Count; i++)
            {
                KeyCode key = i < 9 ? KeyCode.Alpha1 + i : KeyCode.Alpha0;
                if (FiresCore.Input.FiresKeys.GetKeyDown(key))
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
                // Show error message for disabled option: its OverrideError, else the failed condition's reason.
                string errorMsg = !string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "Requirements not met.";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, errorMsg);
                return;
            }

            // Press feedback comes from the Button's kg ColorBlock (pressed grey) — the old
            // pressed-sprite swap replaced the square badge with a round coin mid-click.
            var option = entry.Option;

            if (option.Cooldown == CooldownScope.None)
            {
                // No cooldown - run commands locally exactly as before.
                RunOptionCommands(option);
            }
            else
            {
                // Cooldown option: ask the server to gate + stamp before the reward commands run.
                // Only the COMMANDS wait for approval; the dialogue transition proceeds immediately below.
                _pendingPickDialogueUID = _currentUID;
                _pendingPickOption = option;
                _pendingPickNpcView = _currentNpcView;
                DialogueSyncRPC.SendPick(_currentUID, entry.CooldownKey);
            }

            // Talk-quest progression hook (KG parity): every dialogue option click counts as "talking to"
            // the NPC. The Talk quest's target is the NPC's display name and the quest's TargetCount is
            // the number of separate dialogue interactions required.
            try { global::FiresRPGmaker.Modules.Quest.TalkQuestBridge.OnTalkedTo(_currentNpcName); } catch { }

            // Transition
            string nextUID = option.GetNextTransition();
            if (!string.IsNullOrEmpty(nextUID))
            {
                TransitionTo(nextUID);
            }
            else if (option.Cooldown != CooldownScope.None && OpensTextPrompt(option))
            {
                // A cooling option runs its commands only when the server says yes (OnPickResult). If one of them opens a
                // text prompt, keep the dialogue up until then, or the prompt's answer would land on a closed dialogue.
            }
            else if (TextInput.IsVisible())
            {
                // The option opened a text prompt (EnterPassword) and has no transition: the prompt's own answer moves
                // the dialogue on (OnSuccess / OnFail, or closes it). Closing here cleared the dialogue and the prompt
                // showed nothing (rpg_dialogue_test, 10-08).
            }
            else
            {
                // No transition - close dialogue
                Hide();
            }
        }

        /// <summary>True when one of the option's commands opens a text prompt (EnterPassword).</summary>
        private static bool OpensTextPrompt(RawPlayerOption option)
        {
            if (option?.Commands == null) return false;
            foreach (string command in option.Commands)
                if (command != null && command.TrimStart().StartsWith("EnterPassword", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Runs an option's reward commands (the shared, non-gated execution path).</summary>
        private static void RunOptionCommands(RawPlayerOption option, ZNetView npcView = null)
        {
            var view = npcView ?? _currentNpcView;
            if (option.Commands != null && option.Commands.Length > 0)
                DialogueCommandEngine.ExecuteAll(option.Commands, view);
            if (option.RandomCommands != null && option.RandomCommands.Count > 0)
                DialogueCommandEngine.ExecuteRandom(option.RandomCommands, view);
        }

        #endregion

        #region Cooldowns

        /// <summary>Subscribes once to the server's cooldown state/pick replies.</summary>
        private static void HookCooldownHandlers()
        {
            if (_cooldownHandlersHooked) return;
            _cooldownHandlersHooked = true;
            DialogueSyncRPC.OnCooldownStateReceived += OnCooldownStateReceived;
            DialogueSyncRPC.OnPickResult += OnPickResult;
        }

        /// <summary>Clears the local cache and asks the server for the dialogue's cooldown state.</summary>
        private static void RequestCooldownState(string dialogueUID)
        {
            DialogueSyncRPC.CooldownCache.Clear();
            DialogueSyncRPC.SendCooldownQuery(dialogueUID);
        }

        private static int GetCooldownRemaining(RawPlayerOption option)
        {
            if (option == null || option.Cooldown == CooldownScope.None) return 0;
            string key = DialogueKeyUtil.OptionKey(_currentUID, option);
            return DialogueSyncRPC.CooldownCache.TryGetValue(key, out int remaining) ? remaining : 0;
        }

        /// <summary>Applies fresh cooldown state to the open dialogue. kg shows no flicker here, so the
        /// old full PopulateDialogue re-render (destroy + rebuild every row = the visible flash after
        /// every pick) is now an IN-PLACE patch of the existing rows; only a change in which options
        /// are VISIBLE (hidden-while-cooling appearing/expiring) still forces the full rebuild.</summary>
        private static void OnCooldownStateReceived()
        {
            if (!_visible || string.IsNullOrEmpty(_currentUID)) return;
            var dialogue = DialogueCodex.Get(_currentUID);
            if (dialogue == null) return;

            if (CooldownVisibilityChanged(dialogue))
            {
                PopulateDialogue(dialogue);
                return;
            }
            foreach (var entry in _activeOptions)
                RefreshOptionState(entry);
        }

        /// <summary>True when the fresh cooldown state changes WHICH options are visible (an option
        /// hidden while cooling appeared or expired) — the only case that needs a row rebuild.</summary>
        private static bool CooldownVisibilityChanged(RawDialogue dialogue)
        {
            var visible = new List<RawPlayerOption>();
            if (dialogue.Interactions != null)
            {
                foreach (var interaction in dialogue.Interactions)
                {
                    if (interaction.Type == InteractionType.Interaction
                        || interaction.Type == InteractionType.InRange
                        || interaction.Type == InteractionType.OutRange) continue;
                    bool condPass = DialogueConditionEngine.EvaluateAll(interaction.Conditions, out _);
                    if (!condPass && !interaction.AlwaysVisible) continue;
                    if (GetCooldownRemaining(interaction) > 0 && !interaction.ShowWhileCooling) continue;
                    visible.Add(interaction);
                }
            }
            if (visible.Count != _activeOptions.Count) return true;
            for (int i = 0; i < visible.Count; i++)
                if (!ReferenceEquals(visible[i], _activeOptions[i].Option)) return true;
            return false;
        }

        /// <summary>In-place row update for a cooldown/condition change: interactable state, label
        /// suffix, and colors — the row GameObject itself is never rebuilt.</summary>
        private static void RefreshOptionState(OptionEntry entry)
        {
            if (entry?.Option == null || entry.Root == null) return;

            bool condPass = DialogueConditionEngine.EvaluateAll(entry.Option.Conditions, out string reason);
            int remaining = GetCooldownRemaining(entry.Option);
            if (remaining > 0)
            {
                condPass = false;
                reason = $"Available in {FormatCooldown(remaining)}";
            }

            entry.Enabled = condPass;
            if (entry.Button != null) entry.Button.interactable = condPass;

            if (entry.LabelText != null)
            {
                string optText = DialogueCommandEngine.ApplyReplacements(entry.Option.Text ?? "");
                string displayText = Localization.instance != null ? Localization.instance.Localize(optText) : optText;
                if (!condPass)
                {
                    string errorMsg = !string.IsNullOrEmpty(entry.Option.OverrideError) ? entry.Option.OverrideError : reason;
                    if (!string.IsNullOrEmpty(errorMsg))
                        displayText += $"\n<size=11><color=#B3261E><i>{errorMsg}</i></color></size>";
                }
                entry.LabelText.text = displayText;
                entry.LabelText.color = condPass ? new Color(1f, 0.95f, 0.85f) : new Color(0.5f, 0.4f, 0.3f);
            }
        }

        /// <summary>Server verdict on a cooldown pick: run the reward commands only if approved.</summary>
        private static void OnPickResult(bool approved, string optionKey, int remainingSeconds)
        {
            var option = _pendingPickOption;
            var npcView = _pendingPickNpcView;
            string pickDialogueUID = _pendingPickDialogueUID;
            _pendingPickOption = null;
            _pendingPickNpcView = null;
            _pendingPickDialogueUID = null;

            if (option == null) return;

            if (approved)
            {
                RunOptionCommands(option, npcView);

                // If we're still showing the dialogue the pick came from, refresh so the
                // just-used option flips to its cooling-down state.
                if (_visible && string.Equals(_currentUID, pickDialogueUID, StringComparison.OrdinalIgnoreCase))
                    RequestCooldownState(_currentUID);
            }
            else
            {
                string msg = !string.IsNullOrEmpty(option.OverrideError)
                    ? option.OverrideError
                    : $"Not available yet ({FormatCooldown(remainingSeconds)})";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
            }
        }

        /// <summary>Formats remaining seconds as a compact "Xh Ym" / "Ym Zs" / "Zs" label.</summary>
        private static string FormatCooldown(int totalSeconds)
        {
            if (totalSeconds <= 0) return "0s";
            int days = totalSeconds / 86400;
            int hours = (totalSeconds % 86400) / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            if (days > 0) return $"{days}d {hours}h";
            if (hours > 0) return $"{hours}h {minutes}m";
            if (minutes > 0) return $"{minutes}m {seconds}s";
            return $"{seconds}s";
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

                // 2) Try ZNetScene prefab - could be an item or a creature
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

                // 3) Try the trophy item directly (e.g. user typed "Boar" -> look for "TrophyBoar")
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

    /// <summary>Shows/hides an option row's green hover border on real pointer enter/exit — replaces
    /// Unity's Selectable ColorTint, which auto-selects the first option and leaves a permanent grey
    /// slab. Only attached to enabled options, so disabled ones never highlight.</summary>
    internal sealed class DialogueOptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public UnityEngine.UI.Image Target;   // the row's rounded RIM image (bronze → green)
        public Color Normal;                  // bronze/gold at rest
        public Color Hover;                   // green on pointer-over
        private float _readyAt;               // ignore the spurious enter Unity fires the frame a fresh row
                                              // materializes under the cursor (the "first open = all green" bug)
        private void OnEnable() { if (Target != null) Target.color = Normal; _readyAt = Time.realtimeSinceStartup + 0.1f; }
        public void OnPointerEnter(PointerEventData e) { if (Target != null && Time.realtimeSinceStartup >= _readyAt) Target.color = Hover; }
        public void OnPointerExit(PointerEventData e) { if (Target != null) Target.color = Normal; }
        private void OnDisable() { if (Target != null) Target.color = Normal; }
    }
}
