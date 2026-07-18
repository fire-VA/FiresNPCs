using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Admin screen for creating, editing, and deleting dialogue entries.
    /// Fills the NpcMainPanel area, hides underlying elements, and provides
    /// a sidebar list + form editor with command dropdowns.
    /// </summary>
    public class DialogueEditorScreenController : MonoBehaviour
    {
        public static DialogueEditorScreenController Instance { get; private set; }

        public GameObject screenRoot;

        // ?? Sidebar ??
        private Transform _sidebarContent;
        private ScrollRect _sidebarScroll;
        private readonly List<GameObject> _sidebarItems = new List<GameObject>();
        private string _selectedUID;

        // ?? Form fields ??
        private TMP_InputField _uidInput;
        private TMP_InputField _npcTextInput;
        private TMP_InputField _bgImageInput;

        // ?? Options ??
        private Transform _optionsContent;
        private ScrollRect _optionsScroll;
        private RectTransform _formContentRect;
        private readonly List<OptionRow> _optionRows = new List<OptionRow>();

        // ?? Option Editor Popup (positioned outside the main panel) ??
        private GameObject _optionEditorPopup;
        private int _editingOptionIndex = -1;
        private TMP_Dropdown _popupTypeDropdown;
        private TMP_InputField _popupTextInput;
        private TMP_InputField _popupIconInput;
        private TMP_InputField _popupTransitionInput;
        // Multi-command support
        private Transform _popupCommandsContainer;
        private readonly List<PopupCommandRow> _popupCommandRows = new List<PopupCommandRow>();
        // Multi-condition support
        private Transform _popupConditionsContainer;
        private readonly List<PopupConditionRow> _popupConditionRows = new List<PopupConditionRow>();
        // Cooldown controls
        private TMP_Dropdown _popupCooldownScopeDropdown;
        private TMP_InputField _popupCooldownDurationInput;
        private TMP_Dropdown _popupCooldownUnitDropdown;
        private TMP_Dropdown _popupCooldownShowDropdown;

        // Cooldown unit dropdown indices -> seconds-per-unit
        private static readonly int[] CooldownUnitSeconds = { 1, 60, 3600, 86400 };

        private class PopupCommandRow
        {
            public GameObject Root;
            public TMP_Dropdown CommandDropdown;
            public TMP_InputField ParamsInput;
        }

        private class PopupConditionRow
        {
            public GameObject Root;
            public TMP_Dropdown ConditionDropdown;
            public TMP_InputField ParamsInput;
        }

        // ?? Buttons ??
        private Button _addOptionButton;
        private Button _saveButton;
        private Button _deleteButton;
        private Button _previewButton;
        private Button _backButton;
        private TMP_Text _statusText;

        // ?? State ??
        private NpcController _currentNpc;
        private bool _initialized;
        private bool _dirty;

        // ?? Layout ??
        private const float FIELD_H = 26f;
        private const float MULTI_H = 80f;
        private const float SPACING = 4f;
        private const float FONT = 13f;
        private const float SMALL_FONT = 11f;
        private const float LABEL_W = 90f;
        private const float SIDEBAR_W_RATIO = 0.22f;
        private const float OPT_ROW_H = 200f;
        private const float ROW_SPACING = 2f;

        // ?? Colors ??
        private static readonly Color InputBg       = new Color(0.06f, 0.06f, 0.08f, 0.65f);
        private static readonly Color BtnNormal     = new Color(0.15f, 0.12f, 0.08f, 0.9f);
        private static readonly Color BtnHover      = new Color(0.4f, 0.3f, 0.15f, 0.95f);
        private static readonly Color BtnPress      = new Color(0.6f, 0.4f, 0.2f, 1f);
        private static readonly Color TextCol       = new Color(1f, 0.85f, 0.5f);
        private static readonly Color LabelCol      = new Color(0.9f, 0.75f, 0.45f);
        private static readonly Color PlaceCol      = new Color(0.5f, 0.4f, 0.25f, 0.6f);
        private static readonly Color SideItemBg    = new Color(0.1f, 0.09f, 0.07f, 0.8f);
        private static readonly Color SideSelBg     = new Color(0.35f, 0.25f, 0.12f, 0.9f);
        private static readonly Color OptionRowBg   = new Color(0.09f, 0.08f, 0.07f, 0.65f);
        private static readonly Color OptionAreaBg  = new Color(0.05f, 0.05f, 0.07f, 0.6f);
        private static readonly Color FullBg        = new Color(0.04f, 0.04f, 0.06f, 0.97f);

        // ?? Command definitions for dropdown ??
        // ONE canonical vocabulary + hint set (BookOptionPopup owns it) — this editor aliases it so
        // new engine verbs appear here automatically instead of drifting out of sync.
        private static readonly string[] CommandNames = BookOptionPopup.CommandNames;
        private static readonly string[] ConditionNames = BookOptionPopup.ConditionNames;
        private static readonly Dictionary<string, string> ConditionHints = BookOptionPopup.ConditionHints;
        private static readonly Dictionary<string, string> CommandHints = BookOptionPopup.CommandHints;

        private void Awake()  { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ????????????????????????????????????
        //  Factory
        // ????????????????????????????????????

        /// <summary>
        /// Wires this controller to pre-built elements in the codegen hierarchy.
        /// Called by CodegenUIWiring.WireDialogueEditorScreen().
        /// </summary>
        public void WireFromCodegen(GameObject screenGo)
        {
            Instance = this;
            screenRoot = screenGo;

            var t = screenGo.transform;

            // Sidebar scroll
            var sidebarT = CodegenUIWiring.FindChild(t, "Sidebar");
            if (sidebarT != null)
            {
                // CRITICAL: Codegen adds a VLG to the ScrollView root which fights
                // with the ScrollRect and collapses the viewport. Remove it.
                var rootVlg = sidebarT.GetComponent<VerticalLayoutGroup>();
                if (rootVlg != null) Destroy(rootVlg);

                _sidebarScroll = sidebarT.GetComponent<ScrollRect>();
                if (_sidebarScroll != null && _sidebarScroll.content != null)
                    _sidebarContent = _sidebarScroll.content;

                // Ensure sidebar content has VLG + CSF for dynamic items
                if (_sidebarContent != null)
                {
                    EnsureVerticalLayoutGroup(_sidebarContent.gameObject, 2f, new RectOffset(2, 2, 2, 2));
                    EnsureContentSizeFitter(_sidebarContent.gameObject);

                    // Clear any captured sidebar items from codegen (they'll be rebuilt dynamically)
                    for (int i = _sidebarContent.childCount - 1; i >= 0; i--)
                        Destroy(_sidebarContent.GetChild(i).gameObject);
                }
            }

            // Form scroll area � wire references then rebuild the options section layout
            var formScrollT = CodegenUIWiring.FindChild(t, "FormScroll");
            if (formScrollT != null)
            {
                // Fix codegen's negative left offset that bleeds FormScroll into the sidebar area.
                var formScrollRect = formScrollT.GetComponent<RectTransform>();
                if (formScrollRect != null)
                {
                    formScrollRect.offsetMin = new Vector2(2f, formScrollRect.offsetMin.y);
                }

                var formScroll = formScrollT.GetComponent<ScrollRect>();
                Transform formContent = formScroll != null ? formScroll.content?.transform : null;

                if (formContent != null)
                {
                    _formContentRect = formContent.GetComponent<RectTransform>();

                    _uidInput = CodegenUIWiring.FindInputField(formContent, "Row_UID:");
                    _bgImageInput = CodegenUIWiring.FindInputField(formContent, "Row_BG Image:");
                    _npcTextInput = CodegenUIWiring.FindInputField(formContent, "MultiInput");

                    // ?? Fix the "Options / Interactions" label ??
                    // Codegen bakes it with a huge 222px sizeDelta and absolute position at Y=-422.
                    // Shrink to 20px and reposition right below the NPC Text multiline input
                    // (which ends at ~270px from content top).
                    var optLabelT = CodegenUIWiring.FindChild(formContent, "CL_?? Options / Interactions ??");
                    if (optLabelT != null)
                    {
                        var optLabelRect = optLabelT.GetComponent<RectTransform>();
                        if (optLabelRect != null)
                        {
                            // Below NPC Text input (bottom ~-304), add 16px gap
                            optLabelRect.anchoredPosition = new Vector2(optLabelRect.anchoredPosition.x, -320f);
                            optLabelRect.sizeDelta = new Vector2(optLabelRect.sizeDelta.x, 20f);
                        }
                        var optLabelLe = optLabelT.GetComponent<LayoutElement>();
                        if (optLabelLe != null)
                            optLabelLe.preferredHeight = 20f;
                    }

                    // ?? Fix the AddOptionBtn ??
                    // Codegen bakes it 191px tall. Reposition directly below the
                    // Options/Interactions label, shrink to 24px, reactivate label, wire click.
                    var addOptT = CodegenUIWiring.FindChild(formContent, "AddOptionBtn");
                    _addOptionButton = addOptT != null ? addOptT.GetComponent<Button>() : null;
                    if (_addOptionButton != null)
                    {
                        _addOptionButton.onClick.RemoveAllListeners();
                        _addOptionButton.onClick.AddListener(OnAddOptionClicked);

                        // Reactivate deactivated label children
                        for (int i = 0; i < addOptT.childCount; i++)
                        {
                            var child = addOptT.GetChild(i);
                            if (!child.gameObject.activeSelf)
                                child.gameObject.SetActive(true);
                        }

                        // Position directly below the Options label (label bottom=330, +6 gap, btn center=346)
                        var addRect = addOptT.GetComponent<RectTransform>();
                        if (addRect != null)
                        {
                            addRect.anchoredPosition = new Vector2(addRect.anchoredPosition.x, -346f);
                            addRect.sizeDelta = new Vector2(addRect.sizeDelta.x, 24f);
                        }
                        var addLe = addOptT.GetComponent<LayoutElement>();
                        if (addLe != null)
                            addLe.preferredHeight = 24f;
                    }

                    // ?? Rebuild the OptionsContainer as a scrollable sub-area ??
                    // Codegen creates OptionsContainer as a tiny 4px spacer with absolute
                    // positioning. We destroy it and build a proper ScrollRect that fills
                    // the remaining space below the AddOptionBtn down to the bottom bar.
                    var oldOptContainer = CodegenUIWiring.FindChild(formContent, "OptionsContainer");
                    if (oldOptContainer != null)
                        Destroy(oldOptContainer.gameObject);

                    // Disable the FormScroll's own scrolling � the top fields are
                    // short enough to always fit, and options get their own scroll.
                    if (formScroll != null)
                        formScroll.vertical = false;

                    // Create OptionsScrollArea parented to formScrollT (not content)
                    // so it overlays the lower portion without fighting the content layout.
                    // AddOptionBtn bottom = 358 from content top, +4 gap = 362.
                    var optScrollArea = new GameObject("OptionsScrollArea");
                    optScrollArea.transform.SetParent(formScrollT, false);
                    var osRect = optScrollArea.AddComponent<RectTransform>();
                    osRect.anchorMin = new Vector2(0f, 0f);
                    osRect.anchorMax = new Vector2(1f, 1f);
                    osRect.pivot = new Vector2(0.5f, 0.5f);
                    osRect.offsetMin = new Vector2(30f, 130f);   // left=30, bottom=36 (clear the bottom bar buttons)
                    osRect.offsetMax = new Vector2(-30f, -362f); // right=-30, top=362px below parent top

                    var osBg = optScrollArea.AddComponent<Image>();
                    osBg.color = OptionAreaBg;
                    osBg.raycastTarget = true;

                    // Viewport � needs a transparent Image so the ScrollRect's
                    // event system works and child button clicks propagate correctly.
                    var osViewport = new GameObject("Viewport");
                    osViewport.transform.SetParent(optScrollArea.transform, false);
                    var osVpRect = osViewport.AddComponent<RectTransform>();
                    osVpRect.anchorMin = Vector2.zero;
                    osVpRect.anchorMax = Vector2.one;
                    osVpRect.offsetMin = new Vector2(2f, 2f);
                    osVpRect.offsetMax = new Vector2(-2f, -2f);
                    var vpImg = osViewport.AddComponent<Image>();
                    vpImg.color = new Color(0, 0, 0, 0);
                    vpImg.raycastTarget = true;
                    osViewport.AddComponent<RectMask2D>();

                    // Content
                    var osContent = new GameObject("Content");
                    osContent.transform.SetParent(osViewport.transform, false);
                    var osCRect = osContent.AddComponent<RectTransform>();
                    osCRect.anchorMin = new Vector2(0f, 1f);
                    osCRect.anchorMax = new Vector2(1f, 1f);
                    osCRect.pivot = new Vector2(0f, 1f);
                    osCRect.anchoredPosition = Vector2.zero;
                    osCRect.sizeDelta = Vector2.zero;

                    var osVlg = osContent.AddComponent<VerticalLayoutGroup>();
                    osVlg.spacing = 4f;
                    osVlg.childControlWidth = true;
                    osVlg.childControlHeight = true;
                    osVlg.childForceExpandWidth = true;
                    osVlg.childForceExpandHeight = false;
                    osVlg.padding = new RectOffset(2, 2, 2, 2);

                    var osCsf = osContent.AddComponent<ContentSizeFitter>();
                    osCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                    _optionsScroll = optScrollArea.AddComponent<ScrollRect>();
                    _optionsScroll.horizontal = false;
                    _optionsScroll.vertical = true;
                    _optionsScroll.viewport = osVpRect;
                    _optionsScroll.content = osCRect;
                    _optionsScroll.movementType = ScrollRect.MovementType.Clamped;
                    _optionsScroll.scrollSensitivity = 30f;

                    _optionsContent = osContent.transform;
                }
            }

            // Bottom bar buttons
            var bottomBarT = CodegenUIWiring.FindChild(t, "BottomBar");
            if (bottomBarT != null)
            {
                _saveButton = CodegenUIWiring.FindButton(bottomBarT, "SaveBtn");
                _deleteButton = CodegenUIWiring.FindButton(bottomBarT, "DeleteBtn");
                _previewButton = CodegenUIWiring.FindButton(bottomBarT, "PreviewBtn");
                _statusText = CodegenUIWiring.FindTMP(bottomBarT, "Status");

                if (_saveButton != null) { _saveButton.onClick.RemoveAllListeners(); _saveButton.onClick.AddListener(OnSaveClicked); }
                if (_deleteButton != null) { _deleteButton.onClick.RemoveAllListeners(); _deleteButton.onClick.AddListener(OnDeleteClicked); }
                if (_previewButton != null) { _previewButton.onClick.RemoveAllListeners(); _previewButton.onClick.AddListener(OnPreviewClicked); }
            }

            // Back button
            var backBtnT = CodegenUIWiring.FindChild(t, "BackBtn");
            _backButton = backBtnT != null ? backBtnT.GetComponent<Button>() : null;
            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(OnBackClicked);
            }

            // New dialogue button
            var newBtnT = CodegenUIWiring.FindChild(t, "NewBtn");
            if (newBtnT != null)
            {
                var newBtn = newBtnT.GetComponent<Button>();
                if (newBtn != null)
                {
                    newBtn.onClick.RemoveAllListeners();
                    newBtn.onClick.AddListener(OnNewDialogueClicked);
                }
            }

            _initialized = true;
            screenGo.SetActive(false);

            Debug.Log($"[DialogueEditorScreenController] WireFromCodegen � " +
                      $"Sidebar={_sidebarContent != null}, UIDInput={_uidInput != null}, " +
                      $"NpcTextInput={_npcTextInput != null}, SaveBtn={_saveButton != null}, " +
                      $"OptionsContent={_optionsContent != null}, BackBtn={_backButton != null}, " +
                      $"OptionsScroll={_optionsScroll != null}");
        }

        public static DialogueEditorScreenController Create(Transform panelRoot)
        {
            if (panelRoot == null) return null;

            var existing = panelRoot.Find("DialogueEditorScreen");
            if (existing != null)
                Destroy(existing.gameObject);

            var go = new GameObject("DialogueEditorScreen");
            go.transform.SetParent(panelRoot, false);

            // Fill the parent panel area, leaving room for header/tabs at top and border at edges
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.01f, 0.04f);
            rect.anchorMax = new Vector2(0.99f, 0.94f);
            rect.offsetMin = new Vector2(6, 2);
            rect.offsetMax = new Vector2(-6, -2);

            // Semi-transparent background � lets the ornate border show through
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.06f, 0.92f);
            bg.raycastTarget = true;

            var ctrl = go.AddComponent<DialogueEditorScreenController>();
            ctrl.screenRoot = go;
            ctrl.BuildUI(rect);
            ctrl._initialized = true;

            go.SetActive(false);
            Debug.Log("[DialogueEditorScreen] Created");
            return ctrl;
        }

        // ????????????????????????????????????
        //  Build UI
        // ????????????????????????????????????
        private void BuildUI(RectTransform root)
        {
            // Back button (top-left, just under header/resize area)
            BuildBackButton(root);

            // Left sidebar
            BuildSidebar(root);

            // Right form area (scrollable)
            BuildFormArea(root);

            // Bottom button bar
            BuildBottomBar(root);
        }

        // ????????????? BACK BUTTON ?????????????
        private void BuildBackButton(RectTransform root)
        {
            var go = CreateChild(root, "BackBtn");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(4, 0);
            rect.sizeDelta = new Vector2(64, 24);
            _backButton = CreateStyledButton(go, "< Back", OnBackClicked);
        }

        // ????????????? SIDEBAR ?????????????
        private void BuildSidebar(RectTransform root)
        {
            var sideGo = CreateChild(root, "Sidebar");
            var sideRect = sideGo.AddComponent<RectTransform>();
            sideRect.anchorMin = Vector2.zero;
            sideRect.anchorMax = new Vector2(SIDEBAR_W_RATIO, 1f);
            sideRect.offsetMin = new Vector2(2, 30);
            sideRect.offsetMax = new Vector2(-2, -28);

            var sideBg = sideGo.AddComponent<Image>();
            sideBg.color = new Color(0.05f, 0.05f, 0.07f, 0.8f);
            sideBg.raycastTarget = true;

            // Viewport
            var viewport = CreateChild(sideGo.transform, "Viewport");
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(2, 2);
            vpRect.offsetMax = new Vector2(-14, -2);
            viewport.AddComponent<RectMask2D>();

            // Content
            var content = CreateChild(viewport.transform, "Content");
            var cRect = content.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0, 1);
            cRect.anchoredPosition = Vector2.zero;
            cRect.sizeDelta = new Vector2(0, 0);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(2, 2, 2, 2);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sidebarScroll = sideGo.AddComponent<ScrollRect>();
            _sidebarScroll.horizontal = false;
            _sidebarScroll.vertical = true;
            _sidebarScroll.viewport = vpRect;
            _sidebarScroll.content = cRect;
            _sidebarScroll.movementType = ScrollRect.MovementType.Clamped;
            _sidebarScroll.scrollSensitivity = 30f;

            _sidebarContent = content.transform;

            // "+ New Dialogue" button at bottom of the sidebar column
            var newGo = CreateChild(root, "NewBtn");
            var newRect = newGo.AddComponent<RectTransform>();
            newRect.anchorMin = new Vector2(0, 0);
            newRect.anchorMax = new Vector2(SIDEBAR_W_RATIO, 0);
            newRect.pivot = new Vector2(0.5f, 0);
            newRect.anchoredPosition = new Vector2(0, 4);
            newRect.sizeDelta = new Vector2(-8, 24);
            CreateStyledButton(newGo, "+ New Dialogue", OnNewDialogueClicked);
        }

        // ????????????? FORM AREA ?????????????
        private void BuildFormArea(RectTransform root)
        {
            // The entire right-side form is a single scroll view so everything
            // scrolls together (header fields + options).
            var formScroll = CreateChild(root, "FormScroll");
            var fsRect = formScroll.AddComponent<RectTransform>();
            fsRect.anchorMin = new Vector2(SIDEBAR_W_RATIO + 0.005f, 0);
            fsRect.anchorMax = new Vector2(1, 1);
            fsRect.offsetMin = new Vector2(4, 32);
            fsRect.offsetMax = new Vector2(-4, -4);

            // Viewport
            var viewport = CreateChild(formScroll.transform, "Viewport");
            var vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            // Content � uses a VerticalLayoutGroup so all children stack properly
            var content = CreateChild(viewport.transform, "Content");
            var cRect = content.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0, 1);
            cRect.anchoredPosition = Vector2.zero;
            cRect.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = SPACING;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var cFitter = content.AddComponent<ContentSizeFitter>();
            cFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = formScroll.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.viewport = vpRect;
            scroll.content = cRect;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // ?? Build form rows inside the content layout group ??
            Transform c = content.transform;

            // UID row
            _uidInput = CreateLayoutRow(c, "UID:", "unique_dialogue_id");

            // BG Image row
            _bgImageInput = CreateLayoutRow(c, "BG Image:", "optional background image");

            // NPC Text label
            CreateLayoutLabel(c, "NPC Text:");

            // NPC Text multiline
            _npcTextInput = CreateLayoutMultiInput(c, MULTI_H, "What the NPC says when this dialogue loads...  Use \\n for newlines");

            // Section header
            CreateLayoutCenteredLabel(c, "?? Options / Interactions ??");

            // + Add Option button
            var addGo = new GameObject("AddOptionBtn");
            addGo.transform.SetParent(c, false);
            var addLe = addGo.AddComponent<LayoutElement>();
            addLe.preferredHeight = 24;
            _addOptionButton = CreateStyledButton(addGo, "+ Add Option", OnAddOptionClicked);

            // Options container (not a separate scroll � just more layout elements below)
            var optContainer = new GameObject("OptionsContainer");
            optContainer.transform.SetParent(c, false);
            var optLe = optContainer.AddComponent<LayoutElement>();
            optLe.flexibleHeight = 1;
            optLe.minHeight = 0;

            var optVlg = optContainer.AddComponent<VerticalLayoutGroup>();
            optVlg.spacing = 6;
            optVlg.childControlWidth = true;
            optVlg.childControlHeight = false;
            optVlg.childForceExpandWidth = true;
            optVlg.childForceExpandHeight = false;
            optVlg.padding = new RectOffset(0, 0, 2, 2);

            var optFitter = optContainer.AddComponent<ContentSizeFitter>();
            optFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _optionsContent = optContainer.transform;
        }

        // ????????????? BOTTOM BAR ?????????????
        private void BuildBottomBar(RectTransform root)
        {
            var bar = CreateChild(root, "BottomBar");
            var bRect = bar.AddComponent<RectTransform>();
            bRect.anchorMin = new Vector2(SIDEBAR_W_RATIO + 0.005f, 0);
            bRect.anchorMax = new Vector2(1, 0);
            bRect.pivot = new Vector2(0.5f, 0);
            bRect.anchoredPosition = new Vector2(0, 4);
            bRect.sizeDelta = new Vector2(-12, 28);

            float x = 0;
            float btnW = 0.22f;

            // Save
            var saveGo = AnchoredChild(bar.transform, "SaveBtn", x, 0, x + btnW, 1);
            _saveButton = CreateStyledButton(saveGo, "Save", OnSaveClicked);
            x += btnW + 0.02f;

            // Delete
            var delGo = AnchoredChild(bar.transform, "DeleteBtn", x, 0, x + btnW, 1);
            _deleteButton = CreateStyledButton(delGo, "Delete", OnDeleteClicked);
            delGo.GetComponent<Image>().color = new Color(0.25f, 0.08f, 0.06f, 0.9f);
            x += btnW + 0.02f;

            // Preview
            var prevGo = AnchoredChild(bar.transform, "PreviewBtn", x, 0, x + btnW, 1);
            _previewButton = CreateStyledButton(prevGo, "Preview", OnPreviewClicked);
            x += btnW + 0.02f;

            // Status text
            var statusGo = AnchoredChild(bar.transform, "Status", x, 0, 1, 1);
            _statusText = statusGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize = SMALL_FONT;
            _statusText.color = new Color(0.5f, 0.9f, 0.5f);
            _statusText.alignment = TextAlignmentOptions.MidlineRight;
            _statusText.textWrappingMode = TextWrappingModes.NoWrap;
            _statusText.raycastTarget = false;
            ApplyFont(_statusText);
        }

        // ????????????????????????????????????
        //  Layout-group based form helpers
        // ????????????????????????????????????

        /// <summary>Creates a single-line label + input row inside a VerticalLayoutGroup.</summary>
        private TMP_InputField CreateLayoutRow(Transform parent, string label, string placeholder)
        {
            var row = new GameObject($"Row_{label}");
            row.transform.SetParent(parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = FIELD_H;

            // Label (left-aligned, fixed width)
            var lblGo = CreateChild(row.transform, "Label");
            var lblRect = lblGo.AddComponent<RectTransform>();
            lblRect.anchorMin = new Vector2(0, 0);
            lblRect.anchorMax = new Vector2(0, 1);
            lblRect.pivot = new Vector2(0, 0.5f);
            lblRect.anchoredPosition = new Vector2(0, 0);
            lblRect.sizeDelta = new Vector2(LABEL_W, 0);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = FONT - 1;
            lblTmp.color = LabelCol;
            lblTmp.alignment = TextAlignmentOptions.MidlineRight;
            lblTmp.textWrappingMode = TextWrappingModes.NoWrap;
            lblTmp.raycastTarget = false;
            ApplyFont(lblTmp);

            // Input (fills remaining width)
            var inGo = CreateChild(row.transform, "Input");
            var inRect = inGo.AddComponent<RectTransform>();
            inRect.anchorMin = new Vector2(0, 0);
            inRect.anchorMax = new Vector2(1, 1);
            inRect.offsetMin = new Vector2(LABEL_W + 4, 0);
            inRect.offsetMax = new Vector2(-2, 0);

            var input = BuildInputField(inGo, false, FIELD_H);
            SetPlaceholder(input, placeholder);
            return input;
        }

        /// <summary>Creates a standalone label row inside a VerticalLayoutGroup.</summary>
        private void CreateLayoutLabel(Transform parent, string text)
        {
            var go = new GameObject($"Lbl_{text}");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = FONT - 1;
            tmp.color = LabelCol;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
        }

        /// <summary>Creates a centered label row inside a VerticalLayoutGroup.</summary>
        private void CreateLayoutCenteredLabel(Transform parent, string text)
        {
            var go = new GameObject($"CL_{text}");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = FONT - 1;
            tmp.color = LabelCol;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
        }

        /// <summary>Creates a multiline input row inside a VerticalLayoutGroup.</summary>
        private TMP_InputField CreateLayoutMultiInput(Transform parent, float height, string placeholder)
        {
            var go = new GameObject("MultiInput");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var inRect = go.AddComponent<RectTransform>();
            // RectTransform is already added by LayoutElement usage; just let layout control it.

            var input = BuildInputField(go, true, height);
            SetPlaceholder(input, placeholder);
            return input;
        }

        // ????????????????????????????????????
        //  Public API
        // ????????????????????????????????????
        public void Show(NpcController npc)
        {
            _currentNpc = npc;
            _dirty = false;
            if (_statusText != null) _statusText.text = "";

            RefreshSidebar();

            string profile = npc?.dialogueProfile;
            if (!string.IsNullOrEmpty(profile) && DialogueCodex.Get(profile) != null)
                SelectDialogue(profile);
            else
                ClearForm();

            screenRoot?.SetActive(true);
        }

        public void Hide()
        {
            // Close option editor popup
            if (_editingOptionIndex >= 0 && _editingOptionIndex < _optionRows.Count)
                SavePopupToOptionRow(_editingOptionIndex);
            _editingOptionIndex = -1;
            if (_optionEditorPopup != null) _optionEditorPopup.SetActive(false);

            screenRoot?.SetActive(false);
            _currentNpc = null;
        }

        public bool IsVisible => screenRoot != null && screenRoot.activeSelf;

        // ????????????????????????????????????
        //  Sidebar
        // ????????????????????????????????????
        private void RefreshSidebar()
        {
            foreach (var go in _sidebarItems)
                if (go != null) Destroy(go);
            _sidebarItems.Clear();

            if (_sidebarContent == null)
            {
                Debug.LogWarning("[DialogueEditorScreenController] RefreshSidebar � _sidebarContent is null, sidebar items cannot be added");
                return;
            }

            DialogueCodex.Init();

            var dialogues = DialogueCodex.GetAll().OrderBy(d => d.UID).ToList();
            Debug.Log($"[DialogueEditorScreenController] RefreshSidebar � found {dialogues.Count} dialogue(s) to populate");
            foreach (var d in dialogues)
                AddSidebarItem(d.UID);
        }

        private void AddSidebarItem(string uid)
        {
            var go = new GameObject("Item_" + uid);
            go.transform.SetParent(_sidebarContent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 20;
            le.flexibleWidth = 1;

            bool selected = uid == _selectedUID;
            var bg = go.AddComponent<Image>();
            bg.color = selected ? SideSelBg : SideItemBg;
            bg.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            string capturedUid = uid;
            btn.onClick.AddListener(() => SelectDialogue(capturedUid));

            var textGo = CreateChild(go.transform, "Text");
            var tRect = textGo.AddComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.offsetMin = new Vector2(6, 0);
            tRect.offsetMax = new Vector2(-6, 0);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = uid;
            tmp.fontSize = FONT - 1;
            tmp.color = selected ? TextCol : LabelCol;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            ApplyFont(tmp);

            _sidebarItems.Add(go);
        }

        // ????????????????????????????????????
        //  Selection / Form population
        // ????????????????????????????????????
        private void SelectDialogue(string uid)
        {
            _selectedUID = uid;
            var dialogue = DialogueCodex.Get(uid);
            if (dialogue == null) { ClearForm(); RefreshSidebar(); return; }

            if (_uidInput != null) _uidInput.text = dialogue.UID ?? "";
            if (_bgImageInput != null) _bgImageInput.text = dialogue.BG_Image ?? "";
            if (_npcTextInput != null) _npcTextInput.text = dialogue.Text ?? "";

            RebuildOptionRows(dialogue);
            RefreshSidebar();
            _dirty = false;
            if (_statusText != null) _statusText.text = "";
        }

        private void ClearForm()
        {
            _selectedUID = null;
            if (_uidInput != null) _uidInput.text = "";
            if (_bgImageInput != null) _bgImageInput.text = "";
            if (_npcTextInput != null) _npcTextInput.text = "";
            ClearOptionRows();
            _dirty = false;
        }

        // ��������������������������������
        //  Option rows (compact summaries in OptionsContainer)
        // ��������������������������������
        private void RebuildOptionRows(RawDialogue dialogue)
        {
            ClearOptionRows();
            if (dialogue.Interactions == null) return;
            foreach (var opt in dialogue.Interactions)
                AddOptionRow(opt);
        }

        private void AddOptionRow(RawPlayerOption opt)
        {
            var row = new OptionRow { Data = opt };
            int idx = _optionRows.Count;

            // Compact summary row � just type + text + edit/delete buttons
            var go = new GameObject($"Opt_{idx}");
            go.transform.SetParent(_optionsContent, false);
            row.Root = go;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.flexibleWidth = 1;

            var bg = go.AddComponent<Image>();
            bg.color = OptionRowBg;
            bg.raycastTarget = false; // Row background should NOT eat clicks � let buttons handle them

            // Horizontal layout for the summary
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.padding = new RectOffset(6, 6, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true; // Let layout control height for wrapping
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false; // Don't force expand � let CSF drive height

            // Index badge
            var badgeGo = new GameObject("Badge");
            badgeGo.transform.SetParent(go.transform, false);
            var badgeLe = badgeGo.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 20;
            var badgeTmp = badgeGo.AddComponent<TextMeshProUGUI>();
            badgeTmp.text = $"{idx + 1}.";
            badgeTmp.fontSize = FONT;
            badgeTmp.fontStyle = FontStyles.Bold;
            badgeTmp.color = TextCol;
            badgeTmp.alignment = TextAlignmentOptions.MidlineLeft;
            badgeTmp.raycastTarget = false;
            ApplyFont(badgeTmp);

            // Summary text (fills remaining space)
            var textGo = new GameObject("Summary");
            textGo.transform.SetParent(go.transform, false);
            var textLe = textGo.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1;
            row.SummaryText = textGo.AddComponent<TextMeshProUGUI>();
            row.SummaryText.fontSize = FONT - 1;
            row.SummaryText.color = LabelCol;
            row.SummaryText.alignment = TextAlignmentOptions.MidlineLeft;
            row.SummaryText.textWrappingMode = TextWrappingModes.Normal;
            row.SummaryText.overflowMode = TextOverflowModes.Overflow;
            row.SummaryText.raycastTarget = false;
            ApplyFont(row.SummaryText);

            // Add ContentSizeFitter so wrapped text drives the row height
            var summaryCSF = textGo.AddComponent<ContentSizeFitter>();
            summaryCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UpdateSummaryText(row);

            int capturedIdx = idx;

            // Edit button � small, transparent background, text-only look
            var editGo = new GameObject("EditBtn");
            editGo.transform.SetParent(go.transform, false);
            var editLe = editGo.AddComponent<LayoutElement>();
            editLe.preferredWidth = 34;
            editLe.minWidth = 34;
            editLe.preferredHeight = 22;
            editLe.minHeight = 22;
            var editBtn = CreateStyledButton(editGo, "Edit", () => OpenOptionEditor(capturedIdx));
            editBtn.targetGraphic.raycastTarget = true;
            editGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var editCols = editBtn.colors;
            editCols.normalColor = new Color(0, 0, 0, 0);
            editCols.highlightedColor = new Color(0.4f, 0.3f, 0.15f, 0.3f);
            editCols.pressedColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);
            editCols.selectedColor = new Color(0, 0, 0, 0);
            editBtn.colors = editCols;

            // Delete button � small, transparent background, text-only look
            var delGo = new GameObject("DelBtn");
            delGo.transform.SetParent(go.transform, false);
            var delLe = delGo.AddComponent<LayoutElement>();
            delLe.preferredWidth = 22;
            delLe.minWidth = 22;
            delLe.preferredHeight = 22;
            delLe.minHeight = 22;
            var delBtn = CreateStyledButton(delGo, "X", () => OnRemoveOptionClicked(capturedIdx));
            delBtn.targetGraphic.raycastTarget = true;
            delGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var delCols = delBtn.colors;
            delCols.normalColor = new Color(0, 0, 0, 0);
            delCols.highlightedColor = new Color(0.5f, 0.15f, 0.1f, 0.3f);
            delCols.pressedColor = new Color(0.7f, 0.2f, 0.15f, 0.4f);
            delCols.selectedColor = new Color(0, 0, 0, 0);
            delBtn.colors = delCols;

            _optionRows.Add(row);
        }

        private static void UpdateSummaryText(OptionRow row)
        {
            if (row.SummaryText == null || row.Data == null) return;
            string type = row.Data.Type != InteractionType.Option ? $"[{row.Data.Type}] " : "";
            string text = string.IsNullOrEmpty(row.Data.Text) ? "(empty)" : row.Data.Text;
            string trans = string.IsNullOrEmpty(row.Data.NextUID) ? "" : $" ? {row.Data.NextUID}";
            row.SummaryText.text = $"{type}{text}{trans}";
        }

        private System.Collections.IEnumerator DelayedScrollRefresh()
        {
            yield return null;
            RefreshScrollContentSize();
        }

        // ��������������������������������
        //  Option Editor Popup (positioned to the right of the main panel)
        // ��������������������������������

        private const float POPUP_LABEL_W = 70f;
        private const float POPUP_W = 360f;

        private void EnsureOptionEditorPopup()
        {
            if (_optionEditorPopup != null) return;

            Transform npcPanel = screenRoot?.transform.parent;
            Transform popupParent = npcPanel?.parent ?? screenRoot.transform.parent;

            _optionEditorPopup = new GameObject("OptionEditorPopup");
            _optionEditorPopup.transform.SetParent(popupParent, false);

            var rect = _optionEditorPopup.AddComponent<RectTransform>();
            if (npcPanel != null)
            {
                var npcRect = npcPanel.GetComponent<RectTransform>();
                if (npcRect != null)
                {
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    float panelRight = npcRect.anchoredPosition.x + npcRect.sizeDelta.x * (1f - npcRect.pivot.x);
                    rect.anchoredPosition = new Vector2(panelRight + 10f, npcRect.anchoredPosition.y);
                    rect.sizeDelta = new Vector2(POPUP_W, 100); // height driven by CSF
                }
            }
            else
            {
                rect.anchorMin = new Vector2(0.68f, 0.25f);
                rect.anchorMax = new Vector2(0.96f, 0.75f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            // Background
            var bg = _optionEditorPopup.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.06f, 0.97f);
            bg.raycastTarget = true;

            var outline = _optionEditorPopup.AddComponent<Outline>();
            outline.effectColor = new Color(0.4f, 0.3f, 0.15f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);

            _optionEditorPopup.AddComponent<CanvasGroup>();

            // VLG for stacking fields
            var vlg = _optionEditorPopup.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(6, 6, 4, 6);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = _optionEditorPopup.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ��� Draggable Header ���
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(_optionEditorPopup.transform, false);
            headerGo.AddComponent<LayoutElement>().preferredHeight = 22;
            var headerBg = headerGo.AddComponent<Image>();
            headerBg.color = new Color(0.08f, 0.07f, 0.06f, 0.9f);
            headerBg.raycastTarget = true;
            // Add drag handler
            var drag = headerGo.AddComponent<PopupDragHandler>();
            drag.target = rect;
            // Title text on a child so we don't put two Graphics on the same GO
            var titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(headerGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Edit Option";
            titleTmp.fontSize = FONT;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = TextCol;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.raycastTarget = false;
            ApplyFont(titleTmp);

            // ��� Type ���
            _popupTypeDropdown = BuildPopupDropdownRow(_optionEditorPopup.transform, "Type:",
                new List<string> { "Option", "Interaction", "InRange", "OutRange" });

            // ��� Text ���
            _popupTextInput = BuildPopupInputRow(_optionEditorPopup.transform, "Text:", "Button text shown to player");

            // ��� Icon ���
            _popupIconInput = BuildPopupInputRow(_optionEditorPopup.transform, "Icon:", "item_prefab_name (optional)");

            // ��� Transition ���
            _popupTransitionInput = BuildPopupInputRow(_optionEditorPopup.transform, "Transition:", "dialogue_uid (empty = close)");

            // ��� Commands Section ���
            BuildPopupSectionHeader(_optionEditorPopup.transform, "Commands", OnPopupAddCommand);

            var cmdContainer = new GameObject("CommandsContainer");
            cmdContainer.transform.SetParent(_optionEditorPopup.transform, false);
            var cmdVlg = cmdContainer.AddComponent<VerticalLayoutGroup>();
            cmdVlg.spacing = 2;
            cmdVlg.childControlWidth = true;
            cmdVlg.childControlHeight = true;
            cmdVlg.childForceExpandWidth = true;
            cmdVlg.childForceExpandHeight = false;
            var cmdCsf = cmdContainer.AddComponent<ContentSizeFitter>();
            cmdCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _popupCommandsContainer = cmdContainer.transform;

            // ��� Conditions Section ���
            BuildPopupSectionHeader(_optionEditorPopup.transform, "Conditions", OnPopupAddCondition);

            var condContainer = new GameObject("ConditionsContainer");
            condContainer.transform.SetParent(_optionEditorPopup.transform, false);
            var condVlg = condContainer.AddComponent<VerticalLayoutGroup>();
            condVlg.spacing = 2;
            condVlg.childControlWidth = true;
            condVlg.childControlHeight = true;
            condVlg.childForceExpandWidth = true;
            condVlg.childForceExpandHeight = false;
            var condCsf = condContainer.AddComponent<ContentSizeFitter>();
            condCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _popupConditionsContainer = condContainer.transform;

            // ��� Cooldown ���
            _popupCooldownScopeDropdown = BuildPopupDropdownRow(_optionEditorPopup.transform, "Cooldown:",
                new List<string> { "None", "Per-Player", "Global" });
            _popupCooldownDurationInput = BuildPopupInputRow(_optionEditorPopup.transform, "Duration:", "0");
            _popupCooldownUnitDropdown = BuildPopupDropdownRow(_optionEditorPopup.transform, "Unit:",
                new List<string> { "Seconds", "Minutes", "Hours", "Days" });
            _popupCooldownShowDropdown = BuildPopupDropdownRow(_optionEditorPopup.transform, "On cooldown:",
                new List<string> { "Hide", "Show disabled" });

            // ��� Done Button ���
            var doneBtnGo = new GameObject("DoneBtn");
            doneBtnGo.transform.SetParent(_optionEditorPopup.transform, false);
            doneBtnGo.AddComponent<LayoutElement>().preferredHeight = 24;
            CreateStyledButton(doneBtnGo, "Done", CloseOptionEditor);

            _optionEditorPopup.SetActive(false);
        }

        /// <summary>Compact label+input row for the popup.</summary>
        private TMP_InputField BuildPopupInputRow(Transform parent, string label, string placeholder)
        {
            var row = new GameObject($"PR_{label}");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;

            var lbl = CreateChild(row.transform, "L");
            var lr = lbl.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(0, 1);
            lr.pivot = new Vector2(0, 0.5f); lr.anchoredPosition = Vector2.zero;
            lr.sizeDelta = new Vector2(POPUP_LABEL_W, 0);
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineRight;
            lt.textWrappingMode = TextWrappingModes.NoWrap; lt.raycastTarget = false;
            ApplyFont(lt);

            var inp = CreateChild(row.transform, "I");
            var ir = inp.AddComponent<RectTransform>();
            ir.anchorMin = new Vector2(0, 0); ir.anchorMax = new Vector2(1, 1);
            ir.offsetMin = new Vector2(POPUP_LABEL_W + 3, 0); ir.offsetMax = new Vector2(-2, 0);
            var field = BuildInputField(inp, false, 22);
            SetPlaceholder(field, placeholder);
            return field;
        }

        /// <summary>Compact label+dropdown row for the popup.</summary>
        private TMP_Dropdown BuildPopupDropdownRow(Transform parent, string label, List<string> options)
        {
            var row = new GameObject($"PR_{label}");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;

            var lbl = CreateChild(row.transform, "L");
            var lr = lbl.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(0, 1);
            lr.pivot = new Vector2(0, 0.5f); lr.anchoredPosition = Vector2.zero;
            lr.sizeDelta = new Vector2(POPUP_LABEL_W, 0);
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineRight;
            lt.textWrappingMode = TextWrappingModes.NoWrap; lt.raycastTarget = false;
            ApplyFont(lt);

            var dd = CreateChild(row.transform, "D");
            var dr = dd.AddComponent<RectTransform>();
            dr.anchorMin = new Vector2(0, 0); dr.anchorMax = new Vector2(1, 1);
            dr.offsetMin = new Vector2(POPUP_LABEL_W + 3, 0); dr.offsetMax = new Vector2(-2, 0);
            return BuildDropdown(dd, options);
        }

        /// <summary>Section header with a + button for adding rows.</summary>
        private void BuildPopupSectionHeader(Transform parent, string label, UnityEngine.Events.UnityAction onAdd)
        {
            var row = new GameObject($"SH_{label}");
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 18;

            // HLG
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4; hlg.padding = new RectOffset(4, 4, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            // Label
            var lblGo = new GameObject("L");
            lblGo.transform.SetParent(row.transform, false);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = SMALL_FONT; lt.color = LabelCol;
            lt.alignment = TextAlignmentOptions.MidlineLeft;
            lt.textWrappingMode = TextWrappingModes.NoWrap; lt.raycastTarget = false;
            ApplyFont(lt);

            // + button
            var addGo = new GameObject("Add");
            addGo.transform.SetParent(row.transform, false);
            var addLe = addGo.AddComponent<LayoutElement>();
            addLe.preferredWidth = 24; addLe.minWidth = 24;
            var addBtn = CreateStyledButton(addGo, "+", onAdd);
            var addCols = addBtn.colors;
            addCols.normalColor = new Color(0.12f, 0.1f, 0.08f, 0.9f);
            addCols.highlightedColor = new Color(0.3f, 0.25f, 0.12f, 0.95f);
            addBtn.colors = addCols;
        }

        // ��� Command row (dropdown + params + X) ���
        private PopupCommandRow AddPopupCommandRow(string cmdName = "", string cmdParams = "")
        {
            var pcr = new PopupCommandRow();
            var row = new GameObject($"Cmd_{_popupCommandRows.Count}");
            row.transform.SetParent(_popupCommandsContainer, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;
            pcr.Root = row;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            // Dropdown
            var ddGo = new GameObject("DD");
            ddGo.transform.SetParent(row.transform, false);
            ddGo.AddComponent<LayoutElement>().preferredWidth = 110;
            pcr.CommandDropdown = BuildDropdown(ddGo, new List<string>(CommandNames));

            // Params input
            var pGo = new GameObject("P");
            pGo.transform.SetParent(row.transform, false);
            pGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            pcr.ParamsInput = BuildInputField(pGo, false, 22);

            // Set initial values
            int ddIdx = Array.FindIndex(CommandNames, c => c.Equals(cmdName, StringComparison.OrdinalIgnoreCase));
            if (ddIdx >= 0) pcr.CommandDropdown.SetValueWithoutNotify(ddIdx);
            pcr.ParamsInput.SetTextWithoutNotify(cmdParams);
            if (CommandHints.TryGetValue(ddIdx >= 0 ? CommandNames[ddIdx] : "GiveItem", out string hint))
                SetPlaceholder(pcr.ParamsInput, hint);

            // Update placeholder when command changes; show quest dropdown for GiveQuest
            pcr.CommandDropdown.onValueChanged.AddListener(idx =>
            {
                string cn = CommandNames[idx];
                if (CommandHints.TryGetValue(cn, out string h)) SetPlaceholder(pcr.ParamsInput, h);
            });

            // X button
            var xGo = new GameObject("X");
            xGo.transform.SetParent(row.transform, false);
            var xLe = xGo.AddComponent<LayoutElement>();
            xLe.preferredWidth = 18; xLe.minWidth = 18;
            var captured = pcr;
            var xBtn = CreateStyledButton(xGo, "x", () => RemovePopupCommandRow(captured));
            xGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var xCols = xBtn.colors;
            xCols.normalColor = new Color(0, 0, 0, 0);
            xCols.highlightedColor = new Color(0.5f, 0.15f, 0.1f, 0.3f);
            xBtn.colors = xCols;

            _popupCommandRows.Add(pcr);
            return pcr;
        }

        private void RemovePopupCommandRow(PopupCommandRow pcr)
        {
            if (pcr.Root != null) Destroy(pcr.Root);
            _popupCommandRows.Remove(pcr);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_optionEditorPopup.GetComponent<RectTransform>());
        }

        private void OnPopupAddCommand()
        {
            AddPopupCommandRow("GiveItem", "");
        }

        // ��� Condition row (dropdown + params + X) ���
        private PopupConditionRow AddPopupConditionRow(string condName = "", string condParams = "")
        {
            var pcr = new PopupConditionRow();
            var row = new GameObject($"Cond_{_popupConditionRows.Count}");
            row.transform.SetParent(_popupConditionsContainer, false);
            row.AddComponent<LayoutElement>().preferredHeight = 22;
            pcr.Root = row;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            // Dropdown
            var ddGo = new GameObject("DD");
            ddGo.transform.SetParent(row.transform, false);
            ddGo.AddComponent<LayoutElement>().preferredWidth = 130;
            pcr.ConditionDropdown = BuildDropdown(ddGo, new List<string>(ConditionNames));

            // Params input
            var pGo = new GameObject("P");
            pGo.transform.SetParent(row.transform, false);
            pGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            pcr.ParamsInput = BuildInputField(pGo, false, 22);

            // Set initial values
            int ddIdx = Array.FindIndex(ConditionNames, c => c.Equals(condName, StringComparison.OrdinalIgnoreCase));
            if (ddIdx >= 0) pcr.ConditionDropdown.SetValueWithoutNotify(ddIdx);
            pcr.ParamsInput.SetTextWithoutNotify(condParams);
            if (ConditionHints.TryGetValue(ddIdx >= 0 ? ConditionNames[ddIdx] : "HasItem", out string hint))
                SetPlaceholder(pcr.ParamsInput, hint);

            pcr.ConditionDropdown.onValueChanged.AddListener(idx =>
            {
                string cn = ConditionNames[idx];
                if (ConditionHints.TryGetValue(cn, out string h)) SetPlaceholder(pcr.ParamsInput, h);
            });

            // X button
            var xGo = new GameObject("X");
            xGo.transform.SetParent(row.transform, false);
            var xLe = xGo.AddComponent<LayoutElement>();
            xLe.preferredWidth = 18; xLe.minWidth = 18;
            var captured = pcr;
            var xBtn = CreateStyledButton(xGo, "x", () => RemovePopupConditionRow(captured));
            xGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var xCols = xBtn.colors;
            xCols.normalColor = new Color(0, 0, 0, 0);
            xCols.highlightedColor = new Color(0.5f, 0.15f, 0.1f, 0.3f);
            xBtn.colors = xCols;

            _popupConditionRows.Add(pcr);
            return pcr;
        }

        private void RemovePopupConditionRow(PopupConditionRow pcr)
        {
            if (pcr.Root != null) Destroy(pcr.Root);
            _popupConditionRows.Remove(pcr);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_optionEditorPopup.GetComponent<RectTransform>());
        }

        private void OnPopupAddCondition()
        {
            AddPopupConditionRow("HasItem", "");
        }

        private void ClearPopupCommandRows()
        {
            foreach (var r in _popupCommandRows) if (r.Root != null) Destroy(r.Root);
            _popupCommandRows.Clear();
        }

        private void ClearPopupConditionRows()
        {
            foreach (var r in _popupConditionRows) if (r.Root != null) Destroy(r.Root);
            _popupConditionRows.Clear();
        }

        private void OpenOptionEditor(int index)
        {
            if (index < 0 || index >= _optionRows.Count) return;

            // Save any pending edits from a previously-open option
            if (_editingOptionIndex >= 0 && _editingOptionIndex < _optionRows.Count)
                SavePopupToOptionRow(_editingOptionIndex);

            _editingOptionIndex = index;
            var data = _optionRows[index].Data;

            EnsureOptionEditorPopup();

            // Populate type
            int typeIdx = 0;
            switch (data.Type)
            {
                case InteractionType.Interaction: typeIdx = 1; break;
                case InteractionType.InRange:     typeIdx = 2; break;
                case InteractionType.OutRange:    typeIdx = 3; break;
            }
            _popupTypeDropdown.SetValueWithoutNotify(typeIdx);
            _popupTextInput.SetTextWithoutNotify(data.Text ?? "");
            _popupIconInput.SetTextWithoutNotify(data.Icon ?? "");
            _popupTransitionInput.SetTextWithoutNotify(data.NextUID ?? "");

            // Populate commands
            ClearPopupCommandRows();
            if (data.Commands != null && data.Commands.Length > 0)
            {
                foreach (string raw in data.Commands)
                {
                    var parts = raw.Replace(":", ",").Split(new[] { ',' }, 2);
                    string cn = parts[0].Trim();
                    string cp = parts.Length > 1 ? parts[1].Trim() : "";
                    AddPopupCommandRow(cn, cp);
                }
            }

            // Populate conditions
            ClearPopupConditionRows();
            if (data.Conditions != null && data.Conditions.Count > 0)
            {
                foreach (var group in data.Conditions)
                {
                    foreach (string cond in group)
                    {
                        var parts = cond.Replace(":", ",").Split(new[] { ',' }, 2);
                        string cn = parts[0].Trim();
                        string cp = parts.Length > 1 ? parts[1].Trim() : "";
                        AddPopupConditionRow(cn, cp);
                    }
                }
            }

            // Populate cooldown controls
            int scopeIdx = 0;
            switch (data.Cooldown)
            {
                case CooldownScope.PerPlayer: scopeIdx = 1; break;
                case CooldownScope.Global:    scopeIdx = 2; break;
            }
            _popupCooldownScopeDropdown.SetValueWithoutNotify(scopeIdx);

            // Split seconds back to the largest whole unit for display.
            int totalSeconds = data.CooldownSeconds;
            int unitIdx = 0;
            int displayDuration = totalSeconds;
            for (int u = CooldownUnitSeconds.Length - 1; u >= 0; u--)
            {
                int per = CooldownUnitSeconds[u];
                if (totalSeconds > 0 && totalSeconds % per == 0)
                {
                    unitIdx = u;
                    displayDuration = totalSeconds / per;
                    break;
                }
            }
            _popupCooldownUnitDropdown.SetValueWithoutNotify(unitIdx);
            _popupCooldownDurationInput.SetTextWithoutNotify(displayDuration.ToString());
            _popupCooldownShowDropdown.SetValueWithoutNotify(data.ShowWhileCooling ? 1 : 0);

            _optionEditorPopup.SetActive(true);
        }

        private void CloseOptionEditor()
        {
            if (_editingOptionIndex >= 0 && _editingOptionIndex < _optionRows.Count)
            {
                SavePopupToOptionRow(_editingOptionIndex);
                UpdateSummaryText(_optionRows[_editingOptionIndex]);
                MarkDirty();
            }

            _editingOptionIndex = -1;
            if (_optionEditorPopup != null)
                _optionEditorPopup.SetActive(false);
        }

        private void SavePopupToOptionRow(int index)
        {
            if (index < 0 || index >= _optionRows.Count) return;
            var data = _optionRows[index].Data;

            int typeIdx = _popupTypeDropdown?.value ?? 0;
            switch (typeIdx)
            {
                case 1: data.Type = InteractionType.Interaction; break;
                case 2: data.Type = InteractionType.InRange; break;
                case 3: data.Type = InteractionType.OutRange; break;
                default: data.Type = InteractionType.Option; break;
            }

            data.Text = _popupTextInput?.text ?? "";
            data.Icon = _popupIconInput?.text?.Trim() ?? "";
            data.NextUID = _popupTransitionInput?.text?.Trim() ?? "";

            // Commands � each row is one full command string
            var cmds = new List<string>();
            foreach (var cr in _popupCommandRows)
            {
                string cn = CommandNames[cr.CommandDropdown?.value ?? 0];
                string cp = cr.ParamsInput?.text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(cp))
                    cmds.Add($"{cn},{cp}");
                else
                    cmds.Add(cn);
            }
            data.Commands = cmds.ToArray();

            // Conditions � each row is one condition, each becomes its own AND group
            var conds = new List<string[]>();
            foreach (var cr in _popupConditionRows)
            {
                string cn = ConditionNames[cr.ConditionDropdown?.value ?? 0];
                string cp = cr.ParamsInput?.text?.Trim() ?? "";
                string full = !string.IsNullOrEmpty(cp) ? $"{cn},{cp}" : cn;
                conds.Add(new string[] { full });
            }
            data.Conditions = conds;

            // Cooldown
            int scopeIdx = _popupCooldownScopeDropdown?.value ?? 0;
            data.Cooldown = scopeIdx == 1 ? CooldownScope.PerPlayer
                          : scopeIdx == 2 ? CooldownScope.Global
                          : CooldownScope.None;

            int durationUnits = 0;
            int.TryParse(_popupCooldownDurationInput?.text?.Trim(), out durationUnits);
            int unitIdx = _popupCooldownUnitDropdown?.value ?? 0;
            if (unitIdx < 0 || unitIdx >= CooldownUnitSeconds.Length) unitIdx = 0;
            data.CooldownSeconds = data.Cooldown == CooldownScope.None
                ? 0
                : Math.Max(0, durationUnits) * CooldownUnitSeconds[unitIdx];

            data.ShowWhileCooling = (_popupCooldownShowDropdown?.value ?? 0) == 1;

            // Freeze a stable key so cooldowns survive later text/reward edits.
            if (data.Cooldown != CooldownScope.None && string.IsNullOrEmpty(data.CooldownKey))
            {
                string uid = _uidInput?.text?.Trim() ?? string.Empty;
                data.CooldownKey = DialogueKeyUtil.OptionKey(uid, data);
            }
            else if (data.Cooldown == CooldownScope.None)
            {
                data.CooldownKey = null;
            }
        }

        private void ClearOptionRows()
        {
            // Close the popup if it's open
            _editingOptionIndex = -1;
            if (_optionEditorPopup != null)
                _optionEditorPopup.SetActive(false);

            foreach (var row in _optionRows)
                if (row.Root != null) Destroy(row.Root);
            _optionRows.Clear();
        }

        /// <summary>
        /// Recalculates the scroll content height to encompass all absolutely-positioned
        /// children plus the dynamically-growing OptionsContainer.  Without this the
        /// ScrollRect doesn't know the content grew and option rows overflow the viewport.
        /// 
        /// All children use anchor (0,1)-(0,1) (top-left) with negative Y anchoredPositions.
        /// Pivot varies per element (some 0.5,0.5 center, some 0.5,1 top).
        /// Bottom edge = |y| + height * (1 - pivot.y).
        /// </summary>
        private void RefreshScrollContentSize()
        {
            if (_formContentRect == null) return;

            float maxBottom = 0f;
            for (int i = 0; i < _formContentRect.childCount; i++)
            {
                var child = _formContentRect.GetChild(i).GetComponent<RectTransform>();
                if (child == null || !child.gameObject.activeInHierarchy) continue;

                // top edge = |anchoredPosition.y| - height * pivot.y
                // bottom edge = |anchoredPosition.y| + height * (1 - pivot.y)
                float absY = Mathf.Abs(child.anchoredPosition.y);
                float h = child.sizeDelta.y;
                float bottom = absY + h * (1f - child.pivot.y);
                if (bottom > maxBottom) maxBottom = bottom;
            }

            _formContentRect.sizeDelta = new Vector2(_formContentRect.sizeDelta.x, maxBottom + 10f);
        }

        // ????????????????????????????????????
        //  Button handlers
        // ????????????????????????????????????
        private void OnBackClicked()
        {
            // Hide this screen first so GoToMainScreen's RefreshUI doesn't see us as visible
            Hide();
            var panel = NpcMainPanelController.Instance;
            if (panel != null)
                panel.GoToMainScreen();
        }

        private void OnNewDialogueClicked()
        {
            ClearForm();
            if (_uidInput != null)
                _uidInput.text = "new_dialogue_" + UnityEngine.Random.Range(1000, 9999);
            MarkDirty();
        }

        private void OnAddOptionClicked()
        {
            AddOptionRow(new RawPlayerOption
            {
                Type = InteractionType.Option,
                Text = "New option",
                AlwaysVisible = true,
                Color = new Color32(255, 255, 255, 255)
            });
            MarkDirty();
            // Immediately open editor for the new option
            OpenOptionEditor(_optionRows.Count - 1);
        }

        private void OnRemoveOptionClicked(int index)
        {
            if (index < 0 || index >= _optionRows.Count) return;

            // Close popup if editing this or any option
            _editingOptionIndex = -1;
            if (_optionEditorPopup != null) _optionEditorPopup.SetActive(false);

            // Collect surviving option data (skip the deleted index)
            var survivingData = new List<RawPlayerOption>();
            for (int i = 0; i < _optionRows.Count; i++)
            {
                if (i != index)
                    survivingData.Add(_optionRows[i].Data);
            }

            // Destroy all existing row GameObjects
            foreach (var row in _optionRows)
                if (row.Root != null) Destroy(row.Root);
            _optionRows.Clear();

            // Rebuild with fresh captured indices
            foreach (var data in survivingData)
                AddOptionRow(data);

            MarkDirty();
            StartCoroutine(DelayedScrollRefresh());
        }

        private void OnSaveClicked()
        {
            string uid = _uidInput?.text?.Trim();
            if (string.IsNullOrEmpty(uid)) { SetStatus("UID required", true); return; }

            try
            {
                var dialogue = BuildDialogueFromForm(uid);
                string cfg = DialogueSyncRPC.SerializeDialogueToCfg(dialogue);
                string fileName = $"legacy/{SanitizeFileName(uid)}.cfg";

                DialogueSyncRPC.PushDialogueToServer(fileName, cfg);

                _selectedUID = uid;
                _dirty = false;
                SetStatus("Saved", false);
                RefreshSidebar();

                if (_currentNpc != null)
                {
                    _currentNpc.dialogueProfile = uid;
                    _currentNpc.npcType = NpcType.DialogueNpc;
                    _currentNpc.SaveToZDO();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", true);
                Debug.LogError($"[DialogueEditor] Save failed: {ex}");
            }
        }

        private void OnDeleteClicked()
        {
            string uid = _uidInput?.text?.Trim();
            if (string.IsNullOrEmpty(uid)) { SetStatus("Nothing to delete", true); return; }

            try
            {
                string fileName = $"legacy/{SanitizeFileName(uid)}.cfg";
                DialogueSyncRPC.PushDialogueToServer(fileName, null);
                ClearForm();
                SetStatus("Deleted", false);
                RefreshSidebar();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", true);
            }
        }

        private void OnPreviewClicked()
        {
            string uid = _uidInput?.text?.Trim();
            if (string.IsNullOrEmpty(uid)) { SetStatus("UID required", true); return; }

            // Always parse from form so preview reflects latest edits
            try
            {
                var dialogue = BuildDialogueFromForm(uid);
                string cfg = DialogueSyncRPC.SerializeDialogueToCfg(dialogue);
                DialogueCodex.ParseFromContent(cfg);
            }
            catch (Exception ex)
            {
                SetStatus($"Parse error: {ex.Message}", true);
                return;
            }

            // Force destroy old UI so it rebuilds fresh
            DialogueUI.Destroy();

            string npcName = _currentNpc?.GetDisplayName() ?? "Preview NPC";
            DialogueUI.Show(npcName, uid, _currentNpc?.GetComponent<ZNetView>());
            SetStatus("Preview opened", false);
        }

        // ????????????????????????????????????
        //  Build dialogue from form
        // ????????????????????????????????????
        private RawDialogue BuildDialogueFromForm(string uid)
        {
            // Flush any pending popup edits
            if (_editingOptionIndex >= 0 && _editingOptionIndex < _optionRows.Count)
                SavePopupToOptionRow(_editingOptionIndex);

            var d = new RawDialogue
            {
                UID = uid,
                Text = _npcTextInput?.text ?? "",
                BG_Image = _bgImageInput?.text?.Trim() ?? "",
                Interactions = new RawPlayerOption[_optionRows.Count]
            };

            for (int i = 0; i < _optionRows.Count; i++)
            {
                var src = _optionRows[i].Data;
                var opt = new RawPlayerOption
                {
                    Type = src.Type,
                    Text = src.Text ?? "",
                    Icon = src.Icon ?? "",
                    NextUID = src.NextUID ?? "",
                    Commands = src.Commands ?? new string[0],
                    Conditions = src.Conditions,
                    AlwaysVisible = true,
                    Color = new Color32(255, 255, 255, 255)
                };
                d.Interactions[i] = opt;
            }

            return d;
        }

        // ????????????????????????????????????
        //  Condition serialization
        // ????????????????????????????????????
        private static string SerializeConditions(List<string[]> conditions)
        {
            if (conditions == null || conditions.Count == 0) return "";
            var parts = new List<string>();
            foreach (var group in conditions)
                if (group != null && group.Length > 0)
                    parts.Add(string.Join("||", group));
            return string.Join("; ", parts);
        }

        private static List<string[]> ParseConditionsFromInput(string text)
        {
            var result = new List<string[]>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var g in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = g.Trim();
                if (trimmed.Length == 0) continue;
                var orParts = trimmed.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                if (orParts.Length > 0)
                    result.Add(orParts);
            }
            return result;
        }

        // ????????????????????????????????????
        //  Dirty / status
        // ????????????????????????????????????
        private void MarkDirty()
        {
            _dirty = true;
            if (_statusText != null) _statusText.text = "* Unsaved";
        }

        private void SetStatus(string msg, bool isError)
        {
            if (_statusText == null) return;
            _statusText.text = msg;
            _statusText.color = isError ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 1f, 0.5f);
        }

        // ????????????????????????????????????
        //  UI factory helpers
        // ????????????????????????????????????
        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject AnchoredChild(Transform parent, string name,
            float aMinX, float aMinY, float aMaxX, float aMaxY)
        {
            var go = CreateChild(parent, name);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(aMinX, aMinY);
            r.anchorMax = new Vector2(aMaxX, aMaxY);
            r.offsetMin = new Vector2(2, 2);
            r.offsetMax = new Vector2(-2, -2);
            return go;
        }

        private void CreateLabel(Transform parent, string text, float x, float y, float width)
        {
            var go = CreateChild(parent, $"Lbl_{text}");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, FIELD_H);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = FONT - 1;
            tmp.color = LabelCol;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
        }

        private TMP_Text CreateCenteredLabel(Transform parent, string text, float y)
        {
            var go = CreateChild(parent, $"CL_{text}");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(0, 16);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = FONT - 1;
            tmp.color = LabelCol;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
            return tmp;
        }

        private TMP_InputField CreateInput(Transform parent, float x, float y, bool multi)
        {
            var go = CreateChild(parent, "Input");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(-x - 6, FIELD_H);
            return BuildInputField(go, multi, FIELD_H);
        }

        private TMP_InputField CreateInputAbsolute(Transform parent, float x, float y,
            float widthOffset, float height)
        {
            var go = CreateChild(parent, "Input");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(widthOffset, height);
            return BuildInputField(go, false, height);
        }

        private TMP_InputField CreateMultiInput(Transform parent, float x, float y, float h)
        {
            var go = CreateChild(parent, "MultiInput");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(-x - 6, h);
            return BuildInputField(go, true, h);
        }

        private TMP_InputField BuildInputField(GameObject go, bool multi, float height)
        {
            var bgImg = go.GetComponent<Image>();
            if (bgImg == null)
                bgImg = go.AddComponent<Image>();
            bgImg.color = InputBg;
            bgImg.raycastTarget = true;

            var vp = CreateChild(go.transform, "Viewport");
            var vpR = vp.AddComponent<RectTransform>();
            vpR.anchorMin = Vector2.zero;
            vpR.anchorMax = Vector2.one;
            vpR.offsetMin = new Vector2(4, 2);
            vpR.offsetMax = new Vector2(-4, -2);
            vp.AddComponent<RectMask2D>();

            var tGo = CreateChild(vp.transform, "Text");
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero;
            tR.anchorMax = Vector2.one;
            tR.offsetMin = Vector2.zero;
            tR.offsetMax = Vector2.zero;

            var txt = tGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = FONT;
            txt.color = TextCol;
            txt.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            txt.textWrappingMode = multi ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            txt.raycastTarget = false;
            ApplyFont(txt);

            var phGo = CreateChild(vp.transform, "Placeholder");
            var phR = phGo.AddComponent<RectTransform>();
            phR.anchorMin = Vector2.zero;
            phR.anchorMax = Vector2.one;
            phR.offsetMin = Vector2.zero;
            phR.offsetMax = Vector2.zero;

            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.text = "";
            ph.fontSize = FONT;
            ph.color = PlaceCol;
            ph.fontStyle = FontStyles.Italic;
            ph.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            ph.raycastTarget = false;
            ApplyFont(ph);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = vpR;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = txt.font;
            input.pointSize = FONT;
            input.lineType = multi ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.characterLimit = multi ? 2000 : 300;
            input.caretColor = new Color(1f, 0.9f, 0.6f);
            input.caretWidth = 1;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);
            input.interactable = true;

            return input;
        }

        /// <summary>Builds a TMP_Dropdown on an existing GameObject (assumes RectTransform already set).</summary>
        private TMP_Dropdown BuildDropdown(GameObject go, List<string> options)
        {
            var bgImg = go.GetComponent<Image>();
            if (bgImg == null)
                bgImg = go.AddComponent<Image>();
            bgImg.color = InputBg;
            bgImg.raycastTarget = true;

            var dd = go.AddComponent<TMP_Dropdown>();

            // Caption label
            var labelGo = CreateChild(go.transform, "Label");
            var labelR = labelGo.AddComponent<RectTransform>();
            labelR.anchorMin = Vector2.zero;
            labelR.anchorMax = Vector2.one;
            labelR.offsetMin = new Vector2(6, 2);
            labelR.offsetMax = new Vector2(-20, -2);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.fontSize = FONT;
            labelTmp.color = TextCol;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.raycastTarget = false;
            ApplyFont(labelTmp);
            dd.captionText = labelTmp;

            // Arrow
            var arrowGo = CreateChild(go.transform, "Arrow");
            var arrowR = arrowGo.AddComponent<RectTransform>();
            arrowR.anchorMin = new Vector2(1, 0.5f);
            arrowR.anchorMax = new Vector2(1, 0.5f);
            arrowR.pivot = new Vector2(1, 0.5f);
            arrowR.anchoredPosition = new Vector2(-4, 0);
            arrowR.sizeDelta = new Vector2(14, 14);
            var arrowTxt = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowTxt.text = "?";
            arrowTxt.fontSize = 8;
            arrowTxt.color = LabelCol;
            arrowTxt.alignment = TextAlignmentOptions.Center;
            arrowTxt.raycastTarget = false;
            ApplyFont(arrowTxt);

            // Template (dropdown popup)
            var template = CreateChild(go.transform, "Template");
            var templateR = template.AddComponent<RectTransform>();
            templateR.anchorMin = new Vector2(0, 0);
            templateR.anchorMax = new Vector2(1, 0);
            templateR.pivot = new Vector2(0.5f, 1);
            templateR.anchoredPosition = Vector2.zero;
            templateR.sizeDelta = new Vector2(0, 160);
            var templateBg = template.AddComponent<Image>();
            templateBg.color = new Color(0.08f, 0.08f, 0.1f, 0.98f);

            var templateScroll = template.AddComponent<ScrollRect>();

            var vpGo = CreateChild(template.transform, "Viewport");
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(2, 2);
            vpRt.offsetMax = new Vector2(-2, -2);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = CreateChild(vpGo.transform, "Content");
            var contentR = contentGo.AddComponent<RectTransform>();
            contentR.anchorMin = new Vector2(0, 1);
            contentR.anchorMax = new Vector2(1, 1);
            contentR.pivot = new Vector2(0, 1);
            contentR.anchoredPosition = Vector2.zero;
            contentR.sizeDelta = new Vector2(0, 0);

            // VerticalLayoutGroup on content so items stack horizontally-full, vertically-stacked
            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 1;
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = false;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.padding = new RectOffset(0, 0, 0, 0);

            var contentFitter = contentGo.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            templateScroll.viewport = vpRt;
            templateScroll.content = contentR;
            templateScroll.horizontal = false;
            templateScroll.vertical = true;
            templateScroll.movementType = ScrollRect.MovementType.Clamped;

            // Item template
            var itemGo = CreateChild(contentGo.transform, "Item");
            var itemR = itemGo.AddComponent<RectTransform>();
            itemR.sizeDelta = new Vector2(0, 24);
            var itemLe = itemGo.AddComponent<LayoutElement>();
            itemLe.preferredHeight = 24;
            itemLe.flexibleWidth = 1;
            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.12f, 0.11f, 0.1f, 0.95f);
            var itemToggle = itemGo.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBg;

            var itemLabelGo = CreateChild(itemGo.transform, "Item Label");
            var itemLabelR = itemLabelGo.AddComponent<RectTransform>();
            itemLabelR.anchorMin = Vector2.zero;
            itemLabelR.anchorMax = Vector2.one;
            itemLabelR.offsetMin = new Vector2(6, 0);
            itemLabelR.offsetMax = new Vector2(-6, 0);
            var itemLabel = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabel.fontSize = FONT;
            itemLabel.color = TextCol;
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
            itemLabel.textWrappingMode = TextWrappingModes.NoWrap;
            itemLabel.raycastTarget = false;
            ApplyFont(itemLabel);

            dd.template = templateR;
            dd.itemText = itemLabel;

            template.SetActive(false);

            dd.ClearOptions();
            dd.AddOptions(options);

            return dd;
        }

        private TMP_Dropdown CreateDropdown(Transform parent, float x, float y,
            List<string> options, float width = 0)
        {
            var go = CreateChild(parent, "Dropdown");
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(width > 0 ? 0 : 1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = width > 0
                ? new Vector2(width, FIELD_H)
                : new Vector2(-x - 6, FIELD_H);

            return BuildDropdown(go, options);
        }

        private Button CreateStyledButton(GameObject go, string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var bgImg = go.GetComponent<Image>();
            if (bgImg == null) bgImg = go.AddComponent<Image>();
            bgImg.color = BtnNormal;
            bgImg.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            btn.targetGraphic = bgImg;

            var cols = btn.colors;
            cols.normalColor = BtnNormal;
            cols.highlightedColor = BtnHover;
            cols.pressedColor = BtnPress;
            cols.selectedColor = BtnNormal;
            cols.fadeDuration = 0.08f;
            btn.colors = cols;

            var tGo = CreateChild(go.transform, "Text");
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero;
            tR.anchorMax = Vector2.one;
            tR.offsetMin = Vector2.zero;
            tR.offsetMax = Vector2.zero;

            var tmp = tGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = FONT;
            tmp.color = TextCol;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            ApplyFont(tmp);

            if (onClick != null)
                btn.onClick.AddListener(onClick);

            return btn;
        }

        private static void ApplyFont(TMP_Text text)
        {
            if (text == null) return;
            UIBuilderHelper.ApplyBodyFont(text);
        }

        private static void SetPlaceholder(TMP_InputField input, string text)
        {
            if (input?.placeholder == null) return;
            var ph = input.placeholder as TMP_Text;
            if (ph != null) ph.text = text;
        }

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "dialogue";
        }

        // ????????????????????????????????????
        //  Codegen layout fixup helpers
        // ????????????????????????????????????

        /// <summary>
        /// Ensures a VerticalLayoutGroup exists on the given GameObject.
        /// Codegen bakes absolute positions; the runtime controller needs VLG
        /// so dynamically-added children (sidebar items, option rows) stack correctly.
        /// </summary>
        private static void EnsureVerticalLayoutGroup(GameObject go, float spacing, RectOffset padding)
        {
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = padding;
        }

        /// <summary>
        /// Ensures a ContentSizeFitter (vertical preferred) exists on the given GameObject.
        /// Required for scroll content that grows as children are added.
        /// </summary>
        private static void EnsureContentSizeFitter(GameObject go)
        {
            var csf = go.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ????????????????????????????????????
        //  Option row data holder
        // ????????????????????????????????????
        private class OptionRow
        {
            public GameObject Root;          // Summary row in OptionsContainer
            public TMP_Text SummaryText;     // Label on summary row
            public RawPlayerOption Data;     // The option data
        }
    }

    /// <summary>
    /// Simple drag handler that moves a RectTransform by pointer delta.
    /// Attach to a header bar to make a popup draggable.
    /// </summary>
    internal class PopupDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform target;
        private Vector2 _dragOffset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target.parent as RectTransform, eventData.position, eventData.pressEventCamera, out _dragOffset);
            _dragOffset = target.anchoredPosition - _dragOffset;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target.parent as RectTransform, eventData.position, eventData.pressEventCamera, out var localPoint);
            target.anchoredPosition = localPoint + _dragOffset;
        }
    }
}
