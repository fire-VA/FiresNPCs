using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Services;
using FiresRPGmaker.UI.QuestJournal;
using PlayerQuestStatus = FiresRPGmaker.Services.QuestStatusService.PlayerQuestStatus;

namespace FiresRPGmaker.UI.Screens
{
    public class QuestInteractionScreenController : MonoBehaviour
    {
        public static QuestInteractionScreenController Instance { get; set; }
        public GameObject screenRoot;

        private GameObject _questListPanel;
    private ScrollRect _questListScrollRect;
        private RectTransform _questListContent;
        private GameObject _questDetailsContainer;
        private TMP_Text _questNameText;
        private TMP_Text _questDescriptionText;
        private ScrollRect _descriptionScrollRect;
        private RectTransform _descriptionContent;
        private GameObject _previewImageContainer;
        private RawImage _previewRawImage;
        private Button _acceptButton;
        private Button _cancelButton;
        private GameObject _buttonsPanel;
     private NpcController _currentNpc;
        private List<QuestDefinition> _availableQuests = new List<QuestDefinition>();
        private List<GameObject> _questListItems = new List<GameObject>();
        private QuestDefinition _selectedQuest;
        private bool _initialized;

        // 3D preview pagination
        private List<string> _previewPrefabs = new List<string>();
        private int _previewIndex;
        private Button _prevPreviewBtn;
        private Button _nextPreviewBtn;
        private TMP_Text _previewPageText;
        private bool _using3DPreview;

        // Panel dimensions - matches NpcMainPanel size
     private const float PANEL_WIDTH = 570f;
  private const float PANEL_HEIGHT = 650f;
    
  private const float ITEM_HEIGHT = 32f;
        private const float ITEM_SPACING = 4f;
        private const float FONT_SIZE = 12f;
   private const float TITLE_FONT_SIZE = 18f;  // Quest name label size
        private const float DESC_FONT_SIZE = 11f;   // Description text
  private const float HEADER_FONT_SIZE = 16f; // "Available Quests" header - increased from 12f

        // Use centralized colors from UIFontConfig.Colors
        private static Color ItemNormalColor => UIFontConfig.Colors.ItemNormal;
 private static Color ItemSelectedColor => UIFontConfig.Colors.ItemSelected;
    private static Color ItemHoverColor => UIFontConfig.Colors.ItemHover;
        private static Color TextColor => UIFontConfig.Colors.Gold;
      private static Color LabelColor => UIFontConfig.Colors.LabelGold;
    private static Color ButtonNormalColor => UIFontConfig.Colors.ButtonNormal;
     private static Color ButtonHoverColor => UIFontConfig.Colors.ButtonHover;
        private static Color ButtonPressColor => UIFontConfig.Colors.ButtonPressed;
     private static Color ScrollbarColor => UIFontConfig.Colors.ScrollbarHandle;
        private static Color ScrollbarBgColor => UIFontConfig.Colors.ScrollbarBackground;

        private static string RuntimeSpritesFolder => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "RuntimeSprites");
        private static Dictionary<string, Texture2D> _spriteCache = new Dictionary<string, Texture2D>();

        public bool IsVisible => screenRoot != null && screenRoot.activeSelf;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) { _renderedIconCache.Clear(); Instance = null; } }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Hide();
    
            // Update cooldown timers every second if a quest with cooldown is selected
            UpdateCooldownDisplay();
        }

        private float _lastCooldownUpdate;
        private const float COOLDOWN_UPDATE_INTERVAL = 1f;

        private void UpdateCooldownDisplay()
        {
   if (Time.time - _lastCooldownUpdate < COOLDOWN_UPDATE_INTERVAL) return;
    _lastCooldownUpdate = Time.time;

// Update the quest list status texts for any quests with cooldowns
 for (int i = 0; i < _availableQuests.Count && i < _questListItems.Count; i++)
     {
  var quest = _availableQuests[i];
     var item = _questListItems[i];
     if (item == null) continue;

     var statusTransform = item.transform.Find("Status");
  if (statusTransform == null) continue;

  var statusText = statusTransform.GetComponent<TMP_Text>();
   if (statusText == null) continue;

var playerStatus = GetPlayerQuestStatus(quest.QuestId);
  if (playerStatus == PlayerQuestStatus.Completed && quest.CooldownDays > 0)
 {
     string cooldownInfo = GetCooldownTimeRemaining(quest);
    if (!string.IsNullOrEmpty(cooldownInfo))
   {
// Match the same formatting as CreateQuestListItem - use smaller sizes for multi-line
   statusText.text = $"<size={FONT_SIZE - 5}>[Done]</size>\n<size={FONT_SIZE - 6}>{cooldownInfo}</size>";
 }
 }
       }
     }

          /// <summary>
          /// Wires this controller to pre-built elements in the codegen hierarchy,
          /// fixing layout issues so they match the programmatic version.
          /// Called by CodegenUIWiring.WireQuestInteractionScreen().
          /// </summary>
          public void WireFromCodegen()
          {
              Instance = this;
              QuestProfileManager.Initialize();

              var containerT = screenRoot.transform;

              // --- Quest List Panel (left side) ---
              _questListPanel = CodegenUIWiring.FindChild(containerT, "QuestListPanel")?.gameObject;
              if (_questListPanel != null)
              {
                  // Codegen sets this inactive � we manage visibility ourselves in Show/Hide
                  // Fix offsets: codegen has non-zero offsets that shift the panel
                   var listRect = _questListPanel.GetComponent<RectTransform>();
                  if (listRect != null)
                  {
                      listRect.anchorMin = new Vector2(0.03f, 0.12f);
                      listRect.anchorMax = new Vector2(0.48f, 0.88f);
                      // Padding: 30px left, 30px bottom, 10px right, 30px top
                      listRect.offsetMin = new Vector2(30f, 30f);
                      listRect.offsetMax = new Vector2(-10f, -30f);
                  }
              }

              // Quest list scroll
              var questListScrollT = CodegenUIWiring.FindChild(containerT, "QuestListPanel/QuestListScroll");
              if (questListScrollT != null)
              {
                  _questListScrollRect = questListScrollT.GetComponent<ScrollRect>();
                  if (_questListScrollRect != null)
                  {
                      _questListContent = _questListScrollRect.content;
                      _questListScrollRect.scrollSensitivity = 240f;
                  }

                  // Fix viewport padding to match programmatic (top for title, right for scrollbar)
                  var viewport = questListScrollT.Find("Viewport");
                  if (viewport != null)
                  {
                      var vpRect = viewport.GetComponent<RectTransform>();
                      if (vpRect != null)
                      {
                          vpRect.offsetMin = new Vector2(5, 5);
                          vpRect.offsetMax = new Vector2(-20, -40);
                      }
                  }

                  // Fix scrollbar: codegen parents it under Content � reparent to scroll root
                  // and wire it as a proper Unity Scrollbar so ScrollRect can drive it
                  FixCodegenScrollbar(questListScrollT, _questListScrollRect, 12f,
                      new Vector2(0, -20), new Vector2(12, -45));
              }

              // --- Quest Details Panel (right side) ---
              _questDetailsContainer = CodegenUIWiring.FindChild(containerT, "QuestDetailsPanel")?.gameObject;
              if (_questDetailsContainer != null)
              {
                  // Fix offsets
                  var detailsRect = _questDetailsContainer.GetComponent<RectTransform>();
                  if (detailsRect != null)
                  {
                      detailsRect.anchorMin = new Vector2(0.50f, 0.12f);
                      detailsRect.anchorMax = new Vector2(0.97f, 0.88f);
                      // Padding: 10px left, 30px bottom, 30px right, 30px top
                      detailsRect.offsetMin = new Vector2(10f, 30f);
                      detailsRect.offsetMax = new Vector2(-30f, -30f);
                  }

                  _questNameText = CodegenUIWiring.FindTMP(_questDetailsContainer.transform, "Label");

                  // Description scroll area
                  var descScrollT = CodegenUIWiring.FindChild(_questDetailsContainer.transform, "DescriptionScrollArea");
                  if (descScrollT != null)
                  {
                      _descriptionScrollRect = descScrollT.GetComponent<ScrollRect>();
                      if (_descriptionScrollRect != null)
                      {
                          _descriptionContent = _descriptionScrollRect.content;
                          _descriptionScrollRect.scrollSensitivity = 240f;
                      }

                      _questDescriptionText = CodegenUIWiring.FindTMP(descScrollT, "DescriptionText");

                      // Add layout components to content if missing (programmatic adds these)
                      if (_descriptionContent != null)
                      {
                          if (_descriptionContent.GetComponent<VerticalLayoutGroup>() == null)
                          {
                              var vlg = _descriptionContent.gameObject.AddComponent<VerticalLayoutGroup>();
                              vlg.padding = new RectOffset(8, 0, 0, 0);
                              vlg.spacing = 0;
                              vlg.childForceExpandWidth = true;
                              vlg.childForceExpandHeight = false;
                              vlg.childControlWidth = true;
                              vlg.childControlHeight = false;
                          }
                          if (_descriptionContent.GetComponent<ContentSizeFitter>() == null)
                          {
                              var csf = _descriptionContent.gameObject.AddComponent<ContentSizeFitter>();
                              csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                              csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                          }
                      }

                      // Fix viewport padding for description area
                      var descViewport = descScrollT.Find("Viewport");
                      if (descViewport != null)
                      {
                          var dvpRect = descViewport.GetComponent<RectTransform>();
                          if (dvpRect != null)
                          {
                              dvpRect.offsetMin = new Vector2(5, 5);
                              dvpRect.offsetMax = new Vector2(-18, -5);
                          }
                      }

                      // Fix scrollbar: reparent from Content to scroll root and wire as Scrollbar
                      FixCodegenScrollbar(descScrollT, _descriptionScrollRect, 12f,
                          new Vector2(-3, 0), new Vector2(12, -10));
                  }

                  // Preview image container
                  _previewImageContainer = CodegenUIWiring.FindChild(_questDetailsContainer.transform, "PNG")?.gameObject;
                  if (_previewImageContainer != null)
                  {
                      var previewT = CodegenUIWiring.FindChild(_previewImageContainer.transform, "PreviewImage");
                      if (previewT != null)
                      {
                          // Codegen creates a regular Image; we need a RawImage for texture assignment
                          var existingImg = previewT.GetComponent<Image>();
                          if (existingImg != null)
                              DestroyImmediate(existingImg);
                          _previewRawImage = previewT.gameObject.GetComponent<RawImage>();
                          if (_previewRawImage == null)
                              _previewRawImage = previewT.gameObject.AddComponent<RawImage>();
                          _previewRawImage.enabled = false;

                          // Add orbit controller for 3D preview interaction
                          if (previewT.GetComponent<QuestPreviewOrbitController>() == null)
                              previewT.gameObject.AddComponent<QuestPreviewOrbitController>();
                          _previewRawImage.raycastTarget = true;
                      }

                      // Add pagination controls
                      CreatePreviewPaginationControls(_previewImageContainer.transform);
                  }
              }

              // --- Buttons Panel ---
              _buttonsPanel = CodegenUIWiring.FindChild(containerT, "QuestButtonsPanel")?.gameObject;
              if (_buttonsPanel != null)
              {
                  // Fix button panel: sit tight against the bottom of the main panels
                  var btnPanelRect = _buttonsPanel.GetComponent<RectTransform>();
                  if (btnPanelRect != null)
                  {
                      btnPanelRect.anchorMin = new Vector2(0.15f, 0.07f);
                      btnPanelRect.anchorMax = new Vector2(0.85f, 0.12f);
                      btnPanelRect.offsetMin = new Vector2(10f, 0f);
                      btnPanelRect.offsetMax = new Vector2(-10f, -5f);
                  }

                  _acceptButton = CodegenUIWiring.FindButton(_buttonsPanel.transform, "Btn_Accept");
                  _cancelButton = CodegenUIWiring.FindButton(_buttonsPanel.transform, "Btn_Cancel");

                  if (_acceptButton != null)
                  {
                      _acceptButton.onClick.RemoveAllListeners();
                      _acceptButton.onClick.AddListener(OnAcceptQuest);
                      // Codegen hides the Label child � re-enable it and fix colors
                      FixCodegenButtonLabel(_acceptButton, "Accept", new Color(0.4f, 1f, 0.4f));
                  }
                  if (_cancelButton != null)
                  {
                      _cancelButton.onClick.RemoveAllListeners();
                      _cancelButton.onClick.AddListener(OnCancelQuest);
                      FixCodegenButtonLabel(_cancelButton, "Cancel", new Color(1f, 0.4f, 0.4f));
                  }
              }

              // Hide the original QuestInteract placeholder
              var questInteractGo = GeneratedUI_completecompanionsui.FindByTag("QuestInteract");
              if (questInteractGo != null)
                  questInteractGo.SetActive(false);

              _initialized = true;
              EnsureRuntimeSpritesFolderExists();

              Debug.Log($"[QuestInteractionScreenController] WireFromCodegen � " +
                        $"ScreenRoot={screenRoot != null}, ListPanel={_questListPanel != null}, " +
                        $"ListContent={_questListContent != null}, Details={_questDetailsContainer != null}, " +
                        $"NameText={_questNameText != null}, DescText={_questDescriptionText != null}, " +
                        $"Preview={_previewRawImage != null}, Accept={_acceptButton != null}, Cancel={_cancelButton != null}");
          }

          /// <summary>
          /// Fixes a codegen button's Label child: re-enables it, sets text content and color.
          /// The codegen marks button labels as SetActive(false) by default.
          /// </summary>
          private static void FixCodegenButtonLabel(Button btn, string text, Color color)
          {
              if (btn == null) return;

              // Find the TMP label (may be on an inactive child named "Label")
              foreach (Transform child in btn.transform)
              {
                  var tmp = child.GetComponent<TextMeshProUGUI>();
                  if (tmp != null)
                  {
                      child.gameObject.SetActive(true);
                      tmp.text = text;
                      tmp.color = color;
                      tmp.alignment = TextAlignmentOptions.Center;
                      return;
                  }
              }

              // No TMP found at all � create one
              var labelGo = new GameObject("ButtonLabel");
              labelGo.transform.SetParent(btn.transform, false);
              var rect = labelGo.AddComponent<RectTransform>();
              rect.anchorMin = Vector2.zero;
              rect.anchorMax = Vector2.one;
              rect.offsetMin = Vector2.zero;
              rect.offsetMax = Vector2.zero;
              var tmpNew = labelGo.AddComponent<TextMeshProUGUI>();
              tmpNew.text = text;
              tmpNew.fontSize = 12f;
              tmpNew.fontStyle = FontStyles.Bold;
              tmpNew.color = color;
              tmpNew.alignment = TextAlignmentOptions.Center;
              tmpNew.raycastTarget = false;
          }

          /// <summary>
          /// Fixes a codegen scrollbar: reparents it from Content to the scroll root,
          /// adds a proper Unity Scrollbar component, and wires it to the ScrollRect.
          /// The codegen generator parents Scrollbar under Content which is wrong �
          /// it should be a sibling of Viewport under the scroll root.
          /// </summary>
          private static void FixCodegenScrollbar(Transform scrollRoot, ScrollRect scrollRect,
              float width, Vector2 anchoredPos, Vector2 sizeDelta)
          {
              if (scrollRoot == null) return;

              // Find the scrollbar wherever it is in the hierarchy
              var scrollbarT = UIBuilderHelper.FindChildRecursive(scrollRoot, "Scrollbar");
              if (scrollbarT == null) return;

              // Reparent to scroll root (was under Content)
              scrollbarT.SetParent(scrollRoot, false);

              // Position flush on right edge
              var sbRect = scrollbarT.GetComponent<RectTransform>();
              if (sbRect != null)
              {
                  sbRect.anchorMin = new Vector2(1, 0);
                  sbRect.anchorMax = new Vector2(1, 1);
                  sbRect.pivot = new Vector2(1, 0.5f);
                  sbRect.anchoredPosition = anchoredPos;
                  sbRect.sizeDelta = sizeDelta;
              }

              // Add Scrollbar component if missing
              var scrollbar = scrollbarT.GetComponent<Scrollbar>();
              if (scrollbar == null)
              {
                  scrollbar = scrollbarT.gameObject.AddComponent<Scrollbar>();
                  scrollbar.direction = Scrollbar.Direction.BottomToTop;

                  // Wire handle
                  var handleT = UIBuilderHelper.FindChildRecursive(scrollbarT, "Handle");
                  if (handleT != null)
                  {
                      var handleRect = handleT.GetComponent<RectTransform>();
                      if (handleRect != null)
                      {
                          // Reset handle anchors so Scrollbar component can control them
                          handleRect.anchorMin = Vector2.zero;
                          handleRect.anchorMax = Vector2.one;
                          handleRect.offsetMin = Vector2.zero;
                          handleRect.offsetMax = Vector2.zero;
                          scrollbar.handleRect = handleRect;
                      }
                      var handleImg = handleT.GetComponent<Image>();
                      if (handleImg != null)
                      {
                          handleImg.raycastTarget = true;
                          scrollbar.targetGraphic = handleImg;
                      }
                  }
              }

              // Ensure all scrollbar images accept raycasts for click/drag
              var sbImg = scrollbarT.GetComponent<Image>();
              if (sbImg != null) sbImg.raycastTarget = true;
              var slidingArea = scrollbarT.Find("SlidingArea");
              if (slidingArea != null)
              {
                  var saImg = slidingArea.GetComponent<Image>();
                  if (saImg != null) saImg.raycastTarget = true;
              }

              // Connect to ScrollRect
              if (scrollRect != null)
              {
                  scrollRect.verticalScrollbar = scrollbar;
                  scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
              }
          }

          public static QuestInteractionScreenController Create(Transform panelRoot)
          {
          if (panelRoot == null)
       {
          Debug.LogError("[QuestInteractionScreenController] Cannot create - panelRoot is null");
      return null;
       }

              // If we already have a working programmatic controller, reuse it
              var existingController = panelRoot.GetComponentInChildren<QuestInteractionScreenController>(true);
              if (existingController != null && existingController._initialized)
              {
                  Debug.Log("[QuestInteractionScreenController] Using existing programmatic controller");
                  Instance = existingController;
                  return existingController;
              }

              // Destroy any codegen QuestScreenContainer so it doesn't conflict
              var codegenContainer = panelRoot.Find("QuestScreenContainer");
              if (codegenContainer != null)
              {
                  Debug.Log("[QuestInteractionScreenController] Destroying codegen QuestScreenContainer");
                  DestroyImmediate(codegenContainer.gameObject);
              }

              // Also hide the codegen QuestInteract placeholder
              var questInteractGo = GeneratedUI_completecompanionsui.FindByTag("QuestInteract");
              if (questInteractGo != null)
                  questInteractGo.SetActive(false);

              var controller_go = new GameObject("QuestScreenContainer");
            controller_go.transform.SetParent(panelRoot, false);
            var controller_rect = controller_go.AddComponent<RectTransform>();
            controller_rect.anchorMin = Vector2.zero;
            controller_rect.anchorMax = Vector2.one;
            controller_rect.offsetMin = Vector2.zero;
            controller_rect.offsetMax = Vector2.zero;

            var controller = controller_go.AddComponent<QuestInteractionScreenController>();
            Instance = controller;
            controller.WireUpPrefabElements(panelRoot);
            controller._initialized = true;
            controller_go.SetActive(false);

            EnsureRuntimeSpritesFolderExists();
 Debug.Log("[QuestInteractionScreenController] Created quest interaction screen inside panel");
            return controller;
        }

        private void WireUpPrefabElements(Transform panelRoot)
        {
            QuestProfileManager.Initialize();
            QuestScreenLayout.LogLayout();

            // The controller's own GameObject IS the screen root container
            screenRoot = gameObject;

            Debug.Log($"[QuestInteractionScreenController] Using controller GO as QuestScreenContainer inside {panelRoot.name}");

            // Create elements inside this container
            CreateQuestListPanel(transform);
            CreateQuestDetailsPanel(transform);
            CreateButtonsPanel(transform);

     FixAllFonts();

            // Hide the original QuestInteract element from prefab
 var originalQuestInteract = panelRoot.Find("QuestInteract");
     if (originalQuestInteract != null)
   {
                originalQuestInteract.gameObject.SetActive(false);
    Debug.Log("[QuestInteractionScreenController] Hidden original QuestInteract prefab element");
}

        _initialized = true;
         Debug.Log("[QuestInteractionScreenController] WireUpPrefabElements complete");
  }

        /// <summary>
/// Creates the quest list panel (left side) inside the container.
        /// </summary>
        private void CreateQuestListPanel(Transform container)
  {
    var existing = container.Find("QuestListPanel");
   if (existing != null)
    {
    _questListPanel = existing.gameObject;
      _questListScrollRect = existing.GetComponent<ScrollRect>();
    var viewport = existing.Find("Viewport");
     if (viewport != null)
       {
   var content = viewport.Find("Content");
  if (content != null)
      _questListContent = content.GetComponent<RectTransform>();
     }
   return;
  }

  // Create the panel using UIBuilderHelper
  _questListPanel = UIBuilderHelper.CreatePanel(container, "QuestListPanel",
      new Vector2(0.03f, 0.12f), new Vector2(0.48f, 0.88f), UIFontConfig.Colors.PanelBackground);

     // Create scroll area using helper
   var scrollResult = UIBuilderHelper.CreateScrollArea(_questListPanel.transform, "QuestListScroll",
         Vector2.zero, Vector2.one, null, 12f);

   // Adjust viewport: top padding for title, right padding so scrollbar doesn't overlap items
    scrollResult.Viewport.offsetMin = new Vector2(5, 5);
    scrollResult.Viewport.offsetMax = new Vector2(-20, -40);

       _questListContent = scrollResult.Content;
       _questListScrollRect = scrollResult.ScrollRect;

   // Increase scroll speed (double the default)
   _questListScrollRect.scrollSensitivity = 240f;

   // Position scrollbar flush on the right edge, leaving room for title at top
 var scrollbarRect = scrollResult.Scrollbar.GetComponent<RectTransform>();
     scrollbarRect.anchorMin = new Vector2(1, 0);
     scrollbarRect.anchorMax = new Vector2(1, 1);
     scrollbarRect.pivot = new Vector2(1, 0.5f);
     scrollbarRect.anchoredPosition = new Vector2(0, -20);
     scrollbarRect.sizeDelta = new Vector2(12, -45);

  // Title at top - created AFTER scroll area so it renders on top and is never overlapped
   var titleText = UIBuilderHelper.CreateText(_questListPanel.transform, "Title", "Available Quests",
     UIFontConfig.AvailableQuestsTitle, new Vector2(0, 1), new Vector2(1, 1));
       var titleRect = titleText.GetComponent<RectTransform>();
  titleRect.pivot = new Vector2(0.5f, 1);
         titleRect.anchoredPosition = new Vector2(0, -5);
         titleRect.sizeDelta = new Vector2(-10, 28);

_questListPanel.SetActive(false);
   Debug.Log("[QuestInteractionScreenController] Created QuestListPanel with UIBuilderHelper");
      }

        /// <summary>
        /// Creates the quest details panel (right side) inside the container.
        /// </summary>
        private void CreateQuestDetailsPanel(Transform container)
 {
      // Create details panel using UIBuilderHelper
    _questDetailsContainer = UIBuilderHelper.CreatePanel(container, "QuestDetailsPanel",
        new Vector2(0.50f, 0.12f), new Vector2(0.97f, 0.88f), UIFontConfig.Colors.PanelBackground);

    CreatePngPreviewArea();
 CreateQuestNameLabel();
         CreateDescriptionScrollArea();
        }

        /// <summary>
        /// Creates the buttons panel - centered across full panel width at bottom.
        /// </summary>
        private void CreateButtonsPanel(Transform container)
      {
    // Create button container using UIBuilderHelper
       _buttonsPanel = UIBuilderHelper.CreatePanel(container, "QuestButtonsPanel",
        new Vector2(0.15f, 0.02f), new Vector2(0.85f, 0.10f), UIFontConfig.Colors.PanelBackgroundLight);

// Create buttons using UIBuilderHelper
    _acceptButton = UIBuilderHelper.CreateButton(_buttonsPanel.transform, "Accept",
   new Vector2(0.05f, 0.15f), new Vector2(0.47f, 0.85f), OnAcceptQuest);

            _cancelButton = UIBuilderHelper.CreateButton(_buttonsPanel.transform, "Cancel",
    new Vector2(0.53f, 0.15f), new Vector2(0.95f, 0.85f), OnCancelQuest);
  }

 private void CreatePngPreviewArea()
        {
   // Create PNG container using UIBuilderHelper
       _previewImageContainer = UIBuilderHelper.CreatePanel(_questDetailsContainer.transform, "PNG",
            new Vector2(0.02f, 0.56f), new Vector2(0.98f, 0.98f), UIFontConfig.Colors.PanelBackgroundLight);

 // Create preview RawImage using UIBuilderHelper
       _previewRawImage = UIBuilderHelper.CreateRawImage(_previewImageContainer.transform, "PreviewImage",
     new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));

       // Add orbit controller to the RawImage for 3D preview interaction
       _previewRawImage.gameObject.AddComponent<QuestPreviewOrbitController>();
       _previewRawImage.raycastTarget = true;

       // Create placeholder text using UIBuilderHelper
        var placeholderText = UIBuilderHelper.CreateLabel(_previewImageContainer.transform, "Placeholder",
           "No Preview", 10f, UIFontConfig.Colors.PlaceholderText, Vector2.zero, Vector2.one);

       // Add pagination controls
       CreatePreviewPaginationControls(_previewImageContainer.transform);
  }

  private void CreateQuestNameLabel()
   {
// Create quest name label using UIBuilderHelper
   _questNameText = UIBuilderHelper.CreateText(_questDetailsContainer.transform, "Label", "Select a Quest",
        UIFontConfig.SelectedQuestName, new Vector2(0.02f, 0.48f), new Vector2(0.98f, 0.55f));
     }

  private void CreateDescriptionScrollArea()
     {
 // Create scroll area using UIBuilderHelper
        var scrollResult = UIBuilderHelper.CreateScrollArea(_questDetailsContainer.transform, "DescriptionScrollArea",
      new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.47f), UIFontConfig.Colors.PanelBackgroundDim, 12f);

       _descriptionContent = scrollResult.Content;
        _descriptionScrollRect = scrollResult.ScrollRect;
        _descriptionScrollRect.scrollSensitivity = 240f;

        // Create description text inside content
   var textGO = new GameObject("DescriptionText");
      textGO.transform.SetParent(_descriptionContent, false);

     var textRect = textGO.AddComponent<RectTransform>();
         textRect.anchorMin = new Vector2(0, 1);
    textRect.anchorMax = new Vector2(1, 1);
    textRect.pivot = new Vector2(0, 1);
    textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

  _questDescriptionText = textGO.AddComponent<TextMeshProUGUI>();
 _questDescriptionText.text = "Click on a quest to see details.";
   _questDescriptionText.fontSize = DESC_FONT_SIZE;
   _questDescriptionText.color = TextColor;
       _questDescriptionText.alignment = TextAlignmentOptions.TopLeft;
 _questDescriptionText.textWrappingMode = TextWrappingModes.Normal;
_questDescriptionText.richText = true;
_questDescriptionText.lineSpacing = 18f;

    // Add layout components
     UIBuilderHelper.AddLayoutElement(textGO, flexibleWidth: 1);
 UIBuilderHelper.AddContentSizeFitter(textGO,
 ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

    // Add layout to content
 UIBuilderHelper.AddVerticalLayout(_descriptionContent.gameObject, new RectOffset(8, 0, 0, 0),
     0, true, true, true, false);
        UIBuilderHelper.AddContentSizeFitter(_descriptionContent.gameObject,
       ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

     Debug.Log("[QuestInteractionScreenController] Created description scroll area with UIBuilderHelper");
  }

        /// <summary>
        /// Creates pagination controls inside the preview container.
        /// Shared by both programmatic (Create) and codegen (WireFromCodegen) paths.
        /// </summary>
        private void CreatePreviewPaginationControls(Transform container)
        {
            // Skip if already created
            if (_prevPreviewBtn != null) return;

            // Prev button (top-left)
            var prevGo = new GameObject("PrevPreview");
            prevGo.transform.SetParent(container, false);
            var prevRect = prevGo.AddComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0, 1);
            prevRect.anchorMax = new Vector2(0, 1);
            prevRect.pivot = new Vector2(0, 1);
            prevRect.anchoredPosition = new Vector2(4, -4);
            prevRect.sizeDelta = new Vector2(24, 24);
            var prevImg = prevGo.AddComponent<Image>();
            prevImg.color = new Color(0.1f, 0.08f, 0.06f, 0.85f);
            _prevPreviewBtn = prevGo.AddComponent<Button>();
            _prevPreviewBtn.targetGraphic = prevImg;
            _prevPreviewBtn.onClick.AddListener(OnPrevPreview);
            var prevLabel = new GameObject("Label");
            prevLabel.transform.SetParent(prevGo.transform, false);
            var plr = prevLabel.AddComponent<RectTransform>();
            plr.anchorMin = Vector2.zero; plr.anchorMax = Vector2.one;
            plr.offsetMin = plr.offsetMax = Vector2.zero;
            var prevTmp = prevLabel.AddComponent<TextMeshProUGUI>();
            prevTmp.text = "<"; prevTmp.fontSize = 14f; prevTmp.fontStyle = FontStyles.Bold;
            prevTmp.color = UIFontConfig.Colors.Gold; prevTmp.alignment = TextAlignmentOptions.Center;
            prevTmp.raycastTarget = false;
            prevGo.SetActive(false);

            // Next button (top-right)
            var nextGo = new GameObject("NextPreview");
            nextGo.transform.SetParent(container, false);
            var nextRect = nextGo.AddComponent<RectTransform>();
            nextRect.anchorMin = new Vector2(1, 1);
            nextRect.anchorMax = new Vector2(1, 1);
            nextRect.pivot = new Vector2(1, 1);
            nextRect.anchoredPosition = new Vector2(-4, -4);
            nextRect.sizeDelta = new Vector2(24, 24);
            var nextImg = nextGo.AddComponent<Image>();
            nextImg.color = new Color(0.1f, 0.08f, 0.06f, 0.85f);
            _nextPreviewBtn = nextGo.AddComponent<Button>();
            _nextPreviewBtn.targetGraphic = nextImg;
            _nextPreviewBtn.onClick.AddListener(OnNextPreview);
            var nextLabel = new GameObject("Label");
            nextLabel.transform.SetParent(nextGo.transform, false);
            var nlr = nextLabel.AddComponent<RectTransform>();
            nlr.anchorMin = Vector2.zero; nlr.anchorMax = Vector2.one;
            nlr.offsetMin = nlr.offsetMax = Vector2.zero;
            var nextTmp = nextLabel.AddComponent<TextMeshProUGUI>();
            nextTmp.text = ">"; nextTmp.fontSize = 14f; nextTmp.fontStyle = FontStyles.Bold;
            nextTmp.color = UIFontConfig.Colors.Gold; nextTmp.alignment = TextAlignmentOptions.Center;
            nextTmp.raycastTarget = false;
            nextGo.SetActive(false);

            // Page indicator (centered top)
            var pageGo = new GameObject("PreviewPage");
            pageGo.transform.SetParent(container, false);
            var pageRect = pageGo.AddComponent<RectTransform>();
            pageRect.anchorMin = new Vector2(0.3f, 1);
            pageRect.anchorMax = new Vector2(0.7f, 1);
            pageRect.pivot = new Vector2(0.5f, 1);
            pageRect.anchoredPosition = new Vector2(0, -6);
            pageRect.sizeDelta = new Vector2(0, 18);
            _previewPageText = pageGo.AddComponent<TextMeshProUGUI>();
            _previewPageText.text = "";
            _previewPageText.fontSize = 9f;
            _previewPageText.color = UIFontConfig.Colors.MutedGold;
            _previewPageText.alignment = TextAlignmentOptions.Center;
            _previewPageText.raycastTarget = false;
            pageGo.SetActive(false);
        }

        /// <summary>
      /// Creates a button - delegates to UIBuilderHelper for consistency.
     /// Kept for backward compatibility with any external callers.
       /// </summary>
        private Button CreateButton(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax)
        {
            return UIBuilderHelper.CreateButton(parent, text, anchorMin, anchorMax);
    }

  private bool _fontsApplied;
  private bool _stylesApplied;

  private void FixAllFonts()
  {
  // Only apply fonts once � iterating all TMP children is expensive and
  // dirties every text element's canvas batch. Fonts don't change at runtime.
  if (_fontsApplied) return;
  _fontsApplied = true;

  // Delegate to UIBuilderHelper for font application
       UIBuilderHelper.ApplyValheimFontToAll(screenRoot);
      UIBuilderHelper.ApplyValheimFontToAll(_questListPanel);
           UIBuilderHelper.ApplyValheimFontToAll(_buttonsPanel);
        }

        /// <summary>
        /// Applies one-time static styles to quest screen elements (button labels,
        /// quest name style, Available Quests title). Guarded by _stylesApplied so
        /// the work only happens once per controller lifetime.
        /// </summary>
        private void ApplyStaticStyles()
        {
            if (_questNameText != null)
            {
                UIFontConfig.ApplyStyle(_questNameText, UIFontConfig.SelectedQuestName);
                _questNameText.enableAutoSizing = true;
                _questNameText.fontSizeMin = 12f;
                _questNameText.fontSizeMax = UIFontConfig.SelectedQuestName.Size;
            }

            if (_acceptButton != null)
            {
                var acceptText = _acceptButton.GetComponentInChildren<TMP_Text>();
                if (acceptText != null)
                {
                    acceptText.text = "Accept";
                    acceptText.color = new Color(0.4f, 1f, 0.4f);
                    acceptText.alignment = TextAlignmentOptions.Center;
                }
            }
            if (_cancelButton != null)
            {
                var cancelText = _cancelButton.GetComponentInChildren<TMP_Text>();
                if (cancelText != null)
                {
                    cancelText.text = "Cancel";
                    cancelText.color = new Color(1f, 0.4f, 0.4f);
                    cancelText.alignment = TextAlignmentOptions.Center;
                }
            }

            if (_questListPanel != null)
            {
                var titleTransform = _questListPanel.transform.Find("Title");
                if (titleTransform != null)
                {
                    var titleText = titleTransform.GetComponent<TMP_Text>();
                    if (titleText != null)
                        UIFontConfig.ApplyStyle(titleText, UIFontConfig.AvailableQuestsTitle);
                }
            }
        }

        private static TMP_FontAsset GetValheimFont()
        {
  return UIFontConfig.GetFont(UIFontConfig.FontStyle.Valheim);
 }

        private static Transform FindChildRecursive(Transform parent, string name)
{
        return UIBuilderHelper.FindChildRecursive(parent, name);
    }

   private static void EnsureRuntimeSpritesFolderExists()
{
        try { if (!Directory.Exists(RuntimeSpritesFolder)) Directory.CreateDirectory(RuntimeSpritesFolder); } catch { }
        }

public static void ShowForNpc(NpcController npc)
     {
     if (npc == null) { Debug.LogWarning("[QuestInteractionScreenController] Cannot show - NPC is null"); return; }
   if (string.IsNullOrEmpty(npc.questProfile))
    {
        Debug.Log($"[QuestInteractionScreenController] NPC '{npc.GetDisplayName()}' has no quest profile assigned");
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has no quests available.");
         return;
            }
    
    // Ensure the controller is created before trying to show
    if (Instance == null || !Instance._initialized)
    {
  var mainPanel = NpcMainPanelController.Instance;
        if (mainPanel == null) 
        { 
       Debug.LogWarning("[QuestInteractionScreenController] NpcMainPanelController not available"); 
            return; 
}
        // Force initialization so codegen wiring runs (WireQuestInteractionScreen)
        // This handles the case where ShowForNpc is called before Start()
        mainPanel.EnsureInitialized();

        // If codegen wiring succeeded, Instance is now set
        if (Instance == null || !Instance._initialized)
        {
            // Fallback: codegen elements missing, build programmatically
            var controller = Create(mainPanel.transform);
            if (controller == null)
            {
                Debug.LogError("[QuestInteractionScreenController] Failed to create controller");
                return;
            }
        }
    }
    
  // Now show the screen
    if (Instance != null && Instance._initialized) 
    { 
  Instance.Show(npc); 
        
        // Ensure input is blocked and cursor is visible
        GUIManager.BlockInput(true);
        
        // Force cursor visible immediately - some Unity versions need explicit setting
      Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
 }

        public void Show(NpcController npc)
        {
            if (!_initialized) return;
            _currentNpc = npc;
            _selectedQuest = null;
            LoadQuestsFromProfile(npc?.questProfile);
            PopulateQuestList();
            ClearQuestDetails();
            UpdateButtonStates();
            ClearPreviewImage();

            var mainPanel = NpcMainPanelController.Instance;
            if (mainPanel != null)
            {
                // PERF: Configure element visibility BEFORE activating the panel.
                // This ensures admin-only sub-trees (TabsBar, DetailPanel, TabContent, etc.)
                // are already inactive when the panel's SetActive(true) fires, so Unity
                // doesn't activate/layout/rebuild their hundreds of children.
                PanelElementManager.ConfigureForMode(mainPanel.transform, PanelElementManager.ScreenMode.QuestInteraction, npc?.GetDisplayName() ?? "Quest NPC");

                // Now activate � only the quest-specific subtrees are active
                if (!mainPanel.gameObject.activeSelf)
                    mainPanel.gameObject.SetActive(true);

                // Force proper panel positioning on first show
                var panelRect = mainPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                    panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                    panelRect.pivot = new Vector2(0.5f, 0.5f);
                    panelRect.anchoredPosition = Vector2.zero;
                }
            }

            // Activate the screen root (which is our gameObject � enables Update too)
            if (screenRoot != null && !screenRoot.activeSelf)
                screenRoot.SetActive(true);

            // Codegen creates child panels as inactive � explicitly activate them
            if (_questListPanel != null && !_questListPanel.activeSelf) _questListPanel.SetActive(true);
            if (_buttonsPanel != null && !_buttonsPanel.activeSelf) _buttonsPanel.SetActive(true);
            if (_questDetailsContainer != null && !_questDetailsContainer.activeSelf) _questDetailsContainer.SetActive(true);

            // Apply fonts once � they don't change between interactions
            FixAllFonts();

            // Apply styles once � guard with flag to avoid redundant TMP updates
            if (!_stylesApplied)
            {
                _stylesApplied = true;
                ApplyStaticStyles();
            }

            Debug.Log($"[QuestInteractionScreenController] Showing for NPC: {npc?.GetDisplayName() ?? "null"}, Profile: {npc?.questProfile ?? "none"}, Quests: {_availableQuests.Count}");
        }

        private void Hide()
        {
            HideQuestPanels();
            _currentNpc = null;
            _selectedQuest = null;
            ClearQuestList();
            ClearPreviewImage();
            var mainPanel = NpcMainPanelController.Instance;
            if (mainPanel != null) mainPanel.Hide();
            GUIManager.BlockInput(false);
        }

        /// <summary>
        /// Hides the quest screen without closing the parent panel.
        /// Called by NpcMainPanelController when switching screens or closing.
        /// </summary>
        public static void HideQuestScreenIfExists()
        {
            if (Instance == null) return;
            Instance.HideQuestPanels();
            Instance._currentNpc = null;
            Instance._selectedQuest = null;
            Instance.ClearQuestList();
        }

        /// <summary>
        /// Deactivates the screen root and all individually-activated child panels.
        /// Also finds any other QuestScreenContainer siblings to handle the duplicate case
        /// where both programmatic and codegen containers exist.
        /// </summary>
        private void HideQuestPanels()
        {
            if (screenRoot != null) screenRoot.SetActive(false);
            if (_questListPanel != null) _questListPanel.SetActive(false);
            if (_questDetailsContainer != null) _questDetailsContainer.SetActive(false);
            if (_buttonsPanel != null) _buttonsPanel.SetActive(false);

            // Safety net: hide ALL QuestScreenContainer objects under the panel root
            // in case a duplicate was created by the Create fallback path
            var mainPanel = NpcMainPanelController.Instance;
            if (mainPanel != null)
            {
                foreach (Transform child in mainPanel.transform)
                {
                    if (child.name == "QuestScreenContainer" && child.gameObject.activeSelf)
                        child.gameObject.SetActive(false);
                }
            }
        }

        private void LoadQuestsFromProfile(string profileName)
        {
   _availableQuests.Clear();
            if (string.IsNullOrEmpty(profileName)) return;
    QuestProfileManager.Initialize();
      var questManager = QuestManager.Instance;
  if (questManager == null)
  {
      Debug.LogWarning($"[QuestInteractionScreenController] QuestManager.Instance is null!");
      return;
  }

  // Debug: log quest database state
  var allQuests = questManager.GetAllQuests();
  Debug.Log($"[QuestInteractionScreenController] QuestManager has {allQuests?.Count ?? 0} quest definitions loaded");

  var profile = QuestProfileManager.GetProfile(profileName);
  if (profile != null && profile.QuestIds != null && profile.QuestIds.Count > 0)
  {
      Debug.Log($"[QuestInteractionScreenController] Found profile '{profileName}' with {profile.QuestIds.Count} quest IDs");
      foreach (var qid in profile.QuestIds)
      {
          var q = questManager.GetQuestDefinition(qid);
          if (q != null)
              _availableQuests.Add(q);
          else
              Debug.LogWarning($"[QuestInteractionScreenController] Quest definition not found for ID '{qid}'");
      }
  }
  else
  {
      Debug.LogWarning($"[QuestInteractionScreenController] Profile '{profileName}' not found in QuestProfileManager (profiles: {string.Join(", ", QuestProfileManager.GetAllProfileNames())})");
      var questIds = questManager.GetQuestsForProfile(profileName);
      if (questIds != null && questIds.Count > 0)
      {
          Debug.Log($"[QuestInteractionScreenController] Fallback: QuestManager has {questIds.Count} IDs for profile '{profileName}'");
          foreach (var qid in questIds) { var q = questManager.GetQuestDefinition(qid); if (q != null) _availableQuests.Add(q); }
      }
      else
      {
          Debug.LogWarning($"[QuestInteractionScreenController] Fallback also found 0 quest IDs for '{profileName}'");
      }
  }
            Debug.Log($"[QuestInteractionScreenController] Loaded {_availableQuests.Count} quests for profile '{profileName}'");
  }

        private void PopulateQuestList()
  {
  ClearQuestList();
  if (_questListContent == null) return;
       float y = -30f; // This number is what handles where the first entry is drawn in relation to top of panel
      foreach (var quest in _availableQuests)
       {
var item = CreateQuestListItem(quest, y);
  _questListItems.Add(item);
     y -= ITEM_HEIGHT + ITEM_SPACING;
    }
            _questListContent.sizeDelta = new Vector2(_questListContent.sizeDelta.x, Mathf.Abs(y) + 10f);
   if (_questListScrollRect != null) _questListScrollRect.verticalNormalizedPosition = 1f;
      }

        private GameObject CreateQuestListItem(QuestDefinition quest, float yPos)
        {
      // Create list item using UIBuilderHelper
 var itemResult = UIBuilderHelper.CreateListItem(_questListContent, $"QuestItem_{quest.QuestId}",
    quest.Name ?? quest.QuestId, yPos, ITEM_HEIGHT);

      var go = itemResult.Root;
  var statusText = itemResult.StatusText;

    // Configure status based on quest state
   var playerStatus = GetPlayerQuestStatus(quest.QuestId);
 if (playerStatus == PlayerQuestStatus.Active)
      {
    statusText.text = "[Active]";
       statusText.color = UIFontConfig.Colors.QuestActive;
  }
  else if (playerStatus == PlayerQuestStatus.Completed)
 {
   string cooldownInfo = GetCooldownTimeRemaining(quest);
    if (!string.IsNullOrEmpty(cooldownInfo))
   {
     // Use smaller sizes for multi-line cooldown text
     statusText.text = $"<size={FONT_SIZE - 5}>[Done]</size>\n<size={FONT_SIZE - 6}>{cooldownInfo}</size>";
   statusText.color = UIFontConfig.Colors.QuestCompletedFaded;
}
     else
   {
    statusText.text = "[Done]";
   statusText.color = UIFontConfig.Colors.QuestCompleted;
 }
   }
     else
    {
     statusText.text = "";
    }

     // Add click handler
     var questRef = quest;
          itemResult.Button.onClick.AddListener(() => OnQuestSelected(questRef, go));

     return go;
    }

        /// <summary>
/// Gets the remaining cooldown time for a completed quest with cooldown.
/// Delegates to QuestStatusService for centralized cooldown logic.
/// </summary>
        private string GetCooldownTimeRemaining(QuestDefinition quest)
     {
            return QuestStatusService.GetCooldownTimeRemaining(quest);
     }

   private void ClearQuestList() 
   { 
       foreach (var i in _questListItems) 
     if (i != null) Destroy(i); 
    _questListItems.Clear(); 
        }

        private void OnQuestSelected(QuestDefinition quest, GameObject listItem)
        {
            _selectedQuest = quest;
            foreach (var i in _questListItems) 
      { 
       var img = i?.GetComponent<Image>(); 
    if (img != null) img.color = (i == listItem) ? ItemSelectedColor : ItemNormalColor; 
 }
            ShowQuestDetails(quest);
            LoadPreviewImage(quest);
       UpdateButtonStates();
     }

      private void ShowQuestDetails(QuestDefinition quest)
 {
if (quest == null) { ClearQuestDetails(); return; }
if (_questNameText != null) _questNameText.text = quest.Name ?? quest.QuestId;
   if (_questDescriptionText != null)
  {
 float smallSize = DESC_FONT_SIZE - 2;
   string gold = "#C9A86C";  // changed this to match the description text
   string desc = "#C9A86C";  // Softer brownish-gold for description text
   string gray = "#CFCFCF";
   string red = "#FF6666";
   string yellow = "#FFFF66";
   string green = "#66FF66";
   
 var sb = new System.Text.StringBuilder();
    sb.AppendLine($"<color={desc}>{quest.Description ?? "No description"}</color>\n");
   
  // Consistent indent for all sub-items (space for icon + padding)
  string indent = "<space=22>";
  _pendingIconLines.Clear();

  if (quest.QuestTargets != null && quest.QuestTargets.Count > 0)
  {
     sb.AppendLine($"<b><color={gold}>Objectives:</color></b>");
    foreach (var t in quest.QuestTargets)
  {
     string targetName = QuestProgressTracker.GetTargetDisplayName(t);
     // Wrap the target name in a <link> tag so we can find its TMP line after layout
     string linkId = $"obj_{_pendingIconLines.Count}";
     var line = $"<size={smallSize}>{indent}<link=\"{linkId}\"><color=#FFB366>{targetName}</color></link> <color={gray}>x{t.Amount}";
     if (t.MinLevel > 0) line += $" (Lvl {t.MinLevel}+)";
      line += "</color></size>";
     sb.AppendLine(line);
     _pendingIconLines.Add((-1, t.Prefab, true)); // lineIndex filled later from linkInfo
  }
   sb.AppendLine();
    }

 if (quest.Rewards != null && quest.Rewards.Count > 0)
     {
   sb.AppendLine($"<b><color={gold}>Rewards:</color></b>");
     foreach (var r in quest.Rewards)
     {
         string rewardLabel = r.IsSkillExp ? $"Skill Exp {r.Target}" : r.Target;
         string linkId = $"rwd_{_pendingIconLines.Count}";
         // For skill XP rewards, use special marker so we load the XP icon instead of a prefab icon
         string iconKey = r.IsSkillExp ? "__SKILL_XP__" : r.Target;
         if (!r.IsAvailable)
             sb.AppendLine($"<size={smallSize}>{indent}<link=\"{linkId}\"><color=#666666><s>{rewardLabel}</s></link> x{r.Amount} (unavailable)</color></size>");
         else
             sb.AppendLine($"<size={smallSize}>{indent}<link=\"{linkId}\"><color=#6699FF>{rewardLabel}</color></link> <color={gray}>x{r.Amount}</color></size>");
         _pendingIconLines.Add((-1, iconKey, r.IsAvailable));
     }
    sb.AppendLine();
    }

 if (quest.Requirements != null && quest.Requirements.Count > 0)
   {
    sb.AppendLine($"<b><color={gold}>Requirements:</color></b>");
    var requirementStatus = GetRequirementStatus(quest);
     foreach (var req in quest.Requirements)
     {
      bool isMet = requirementStatus.TryGetValue(req, out var met) && met;
      string nameColor = isMet ? green : red;
      string displayText = FormatRequirementForDisplay(req);
   string formattedReq = FormatRequirementWithColors(displayText, nameColor, gray);
     sb.AppendLine($"<size={smallSize}>{formattedReq}</size>");
     }
      sb.AppendLine();
       }
 
  if (quest.TimeLimitMinutes > 0)
  sb.AppendLine($"<b><color={gold}>Time Limit:</color></b> <size={smallSize}><color={gray}>{quest.TimeLimitMinutes} minutes</color></size>\n");
       
    if (quest.CooldownDays > 0)
    sb.AppendLine($"<b><color={gold}>Cooldown:</color></b> <size={smallSize}><color={gray}>{quest.CooldownDays} days</color></size>\n");
    
  var status = GetPlayerQuestStatus(quest.QuestId);
    
 // Determine the status string based on actual availability
 string statusStr;
  if (status == PlayerQuestStatus.Active)
     {
  statusStr = $"<size={smallSize}><color={yellow}>In Progress</color></size>";
  }
   else if (status == PlayerQuestStatus.Completed)
  {
     // Check if on cooldown
  string cooldownInfo = GetCooldownTimeRemaining(quest);
 if (!string.IsNullOrEmpty(cooldownInfo))
{
 statusStr = $"<size={smallSize}><color=#AAAAFF>On Cooldown</color>\n<color={gray}><i>{cooldownInfo}</i></color></size>";
     }
     else
  {
  statusStr = $"<size={smallSize}><color={green}>Available</color> <color={gray}><i>(Repeatable)</i></color></size>";
    }
  }
   else
 {
 // Check actual availability for quests that show as "available"
 var availabilityInfo = GetQuestAvailabilityInfo(quest);
 if (!availabilityInfo.IsAvailable && availabilityInfo.MissingRequirements.Count > 0)
 {
var missingStr = new System.Text.StringBuilder();
 missingStr.Append($"<size={smallSize}><color={red}>Missing</color>");
 foreach (var reason in availabilityInfo.MissingRequirements)
 {
   // Format the missing requirement with item count coloring
 string formattedReason = FormatMissingRequirementForDisplay(reason);
 string formattedMissing = FormatMissingStatusWithItemCount(formattedReason, gray, red, yellow);
   missingStr.Append($"\n{formattedMissing}");
}
   missingStr.Append("</size>");
    statusStr = missingStr.ToString();
}
     else
    {
  statusStr = $"<size={smallSize}><color={green}>Available</color></size>";
  }
  }
  
  sb.AppendLine($"<b><color={gold}>Status:</color></b> {statusStr}");
    
_questDescriptionText.text = sb.ToString();

 // Create inline icon images next to objectives and rewards
 // (CreateDescriptionIcons calls ForceMeshUpdate on the text component
 // to read line positions � no need for Canvas.ForceUpdateCanvases
 // which triggers a full rebuild of every canvas in the scene)
 ClearDescriptionIcons();
 CreateDescriptionIcons(quest);

 if (_descriptionScrollRect != null) _descriptionScrollRect.verticalNormalizedPosition = 1f;
}
  }

        private List<GameObject> _descriptionIcons = new List<GameObject>();
        // Tracks which text lines need icons: (lineIndex, prefabName, available)
        private readonly List<(int lineIndex, string prefabName, bool available)> _pendingIconLines = new();

        private void ClearDescriptionIcons()
        {
            foreach (var icon in _descriptionIcons)
                if (icon != null) Destroy(icon);
            _descriptionIcons.Clear();
        }

        /// <summary>
        /// Creates small icon images next to objective and reward lines.
        /// Uses TMP link tags embedded during text building to find exact line positions.
        /// </summary>
        private void CreateDescriptionIcons(QuestDefinition quest)
        {
            if (quest == null || _questDescriptionText == null || _descriptionContent == null) return;
            if (_pendingIconLines.Count == 0) return;

            // Force text mesh generation so we can read line positions and link data
            _questDescriptionText.ForceMeshUpdate();
            var textInfo = _questDescriptionText.textInfo;
            if (textInfo == null || textInfo.lineCount == 0) return;

            var textRect = _questDescriptionText.rectTransform;

            // Build a map from linkID -> TMP line number using textInfo.linkInfo
            var linkToLine = new Dictionary<string, int>();
            for (int li = 0; li < textInfo.linkCount; li++)
            {
                var linkInfo = textInfo.linkInfo[li];
                string linkId = linkInfo.GetLinkID();
                // Find which line the first character of this link is on
                int firstCharIdx = linkInfo.linkTextfirstCharacterIndex;
                if (firstCharIdx >= 0 && firstCharIdx < textInfo.characterCount)
                {
                    int lineNum = textInfo.characterInfo[firstCharIdx].lineNumber;
                    linkToLine[linkId] = lineNum;
                }
            }

            for (int i = 0; i < _pendingIconLines.Count; i++)
            {
                var (_, prefabName, available) = _pendingIconLines[i];
                if (string.IsNullOrEmpty(prefabName)) continue;

                // Determine expected link ID
                string expectedLinkId;
                // Objectives use "obj_N", rewards use "rwd_N" � both indexed by position in _pendingIconLines
                expectedLinkId = i < (quest.QuestTargets?.Count ?? 0) ? $"obj_{i}" : $"rwd_{i}";

                if (!linkToLine.TryGetValue(expectedLinkId, out int lineIndex))
                {
                    Debug.Log($"[QuestIcons] Link '{expectedLinkId}' not found for '{prefabName}'");
                    continue;
                }

                if (lineIndex < 0 || lineIndex >= textInfo.lineCount) continue;

                var sprite = GetPrefabIcon(prefabName);
                if (sprite == null) continue;

                var lineInfo = textInfo.lineInfo[lineIndex];
                float lineY = lineInfo.lineExtents.max.y - (lineInfo.lineHeight * 0.5f);

                var iconGO = new GameObject($"Icon_{prefabName}");
                iconGO.transform.SetParent(textRect, false);
                var iconRectT = iconGO.AddComponent<RectTransform>();
                iconRectT.anchorMin = new Vector2(0, 1);
                iconRectT.anchorMax = new Vector2(0, 1);
                iconRectT.pivot = new Vector2(0, 0.5f);
                iconRectT.anchoredPosition = new Vector2(2f, lineY);
                iconRectT.sizeDelta = new Vector2(16f, 16f);

                var iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = sprite;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                if (!available) iconImg.color = new Color(0.4f, 0.4f, 0.4f, 0.6f);

                _descriptionIcons.Add(iconGO);
            }
        }

        /// <summary>
    /// Formats missing status items with colored count based on how many are missing.
  /// Example: "Item: Wishbone x3" where x3 is red if 0 owned, yellow if 1-2 owned
      /// </summary>
    private string FormatMissingStatusWithItemCount(string displayText, string grayColor, string redColor, string yellowColor)
      {
 if (string.IsNullOrEmpty(displayText)) return displayText;
 
   // Check if this is an item requirement with a count
     // Format: "Item: ItemName x3" or "Item: ItemName"
 int colonIndex = displayText.IndexOf(':');
      if (colonIndex <= 0) return $"<color={redColor}>{displayText}</color>";
       
  string label = displayText.Substring(0, colonIndex + 1);
     string rest = displayText.Substring(colonIndex + 1).Trim();
          
     // Check for "xN" pattern at the end
  int xIndex = rest.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
  if (xIndex > 0 && label.ToLowerInvariant().Contains("item"))
  {
string itemDisplayName = rest.Substring(0, xIndex).Trim();
     string countPart = rest.Substring(xIndex + 2).Trim();
      
      if (int.TryParse(countPart, out int requiredAmount))
  {
  // Use centralized service for inventory count
  int ownedAmount = PlayerInventoryService.GetPlayerItemCountByDisplayName(itemDisplayName);
      int missing = requiredAmount - ownedAmount;
   
   string countColor;
     if (ownedAmount == 0)
     countColor = redColor;  // None owned - red
       else if (missing > 0)
    countColor = yellowColor;  // Some owned but not enough - yellow
   else
   countColor = UIFontConfig.Colors.Hex.Green;  // Has enough - green
   
     return $"<color={grayColor}>{label}</color> <color={grayColor}>{itemDisplayName}</color> <color={countColor}>x{missing}</color>";
        }
    }
      
    // No count pattern found, color the name red
return $"<color={grayColor}>{label}</color> <color={redColor}>{rest}</color>";
        }

        private string FormatRequirementWithColors(string displayText, string nameColor, string grayColor)
        {
   if (string.IsNullOrEmpty(displayText)) return displayText;
       int colonIndex = displayText.IndexOf(':');
   if (colonIndex > 0 && colonIndex < displayText.Length - 1)
       {
  string label = displayText.Substring(0, colonIndex + 1);
     string name = displayText.Substring(colonIndex + 1).Trim();
   return $"<color={grayColor}>{label}</color> <color={nameColor}>{name}</color>";
   }
   return $"<color={nameColor}>{displayText}</color>";
    }

        /// <summary>
        /// Extracts an item prefab name from a requirement string and returns
        /// a space prefix if that prefab has an icon, or empty string otherwise.
        /// </summary>
        private string GetRequirementIconPrefix(string requirement)
        {
            if (string.IsNullOrEmpty(requirement)) return "";
            var parts = requirement.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            string reqType = parts[0].Trim().ToLowerInvariant();
            if (reqType != "hasitem" && reqType != "item") return "";
            string prefab = parts[1].Trim();
            var icon = GetPrefabIcon(prefab);
            return icon != null ? "<space=20>" : "";
        }

        /// <summary>
        /// Extracts an item prefab name from a requirement string, or null if not applicable.
        /// </summary>
        private static string ExtractRequirementPrefab(string requirement)
        {
            if (string.IsNullOrEmpty(requirement)) return null;
            var parts = requirement.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            string reqType = parts[0].Trim().ToLowerInvariant();
            if (reqType != "hasitem" && reqType != "item") return null;
            return parts[1].Trim();
        }

      /// <summary>
   /// Formats a requirement string for display in the UI.
        /// Delegates to QuestStatusService for centralized formatting.
      /// </summary>
   private string FormatRequirementForDisplay(string requirement)
    {
         return QuestStatusService.FormatRequirementForDisplay(requirement);
   }

     /// <summary>
/// Formats a missing requirement reason for display.
 /// Delegates to QuestStatusService for centralized formatting.
        /// </summary>
        private string FormatMissingRequirementForDisplay(string reason)
        {
        return QuestStatusService.FormatMissingRequirementForDisplay(reason);
      }

        /// <summary>
  /// Gets the status of all requirements for a quest.
      /// Delegates to QuestStatusService for centralized requirement checking.
        /// </summary>
    private Dictionary<string, bool> GetRequirementStatus(QuestDefinition quest)
        {
   return QuestStatusService.GetRequirementStatus(quest);
 }

      /// <summary>
        /// Gets comprehensive availability information for a quest.
      /// Delegates to QuestStatusService for centralized availability checking.
        /// </summary>
        private Services.QuestAvailabilityInfo GetQuestAvailabilityInfo(QuestDefinition quest)
        {
      return QuestStatusService.GetQuestAvailabilityInfo(quest);
        }

     // Remove the local QuestAvailabilityInfo class since we now use the service's version

      private void ClearQuestDetails()
        {
   if (_questNameText != null) _questNameText.text = "Select a Quest";
          if (_questDescriptionText != null) _questDescriptionText.text = "Click on a quest from the list to see details.";
         ClearPreviewImage();
         ClearDescriptionIcons();
        }

        private void LoadPreviewImage(QuestDefinition quest)
   {
   ClearPreviewImage();
            if (quest == null || _previewRawImage == null) return;
            string[] extensions = { ".png", ".jpg", ".jpeg" };
         if (!string.IsNullOrEmpty(quest.PreviewImage))
            {
      string previewName = quest.PreviewImage.Trim();
      string directPath = Path.Combine(RuntimeSpritesFolder, previewName);
         if (File.Exists(directPath)) { LoadTextureFromFile(directPath); return; }
                foreach (var ext in extensions)
     {
     string imagePath = Path.Combine(RuntimeSpritesFolder, previewName + ext);
           if (File.Exists(imagePath)) { LoadTextureFromFile(imagePath); return; }
          }
            string safePreviewName = MakeSafeFileName(previewName);
   foreach (var ext in extensions)
                {
            string imagePath = Path.Combine(RuntimeSpritesFolder, safePreviewName + ext);
     if (File.Exists(imagePath)) { LoadTextureFromFile(imagePath); return; }
                }
            }
            foreach (var ext in extensions)
            {
     string imagePath = Path.Combine(RuntimeSpritesFolder, quest.QuestId + ext);
         if (File.Exists(imagePath)) { LoadTextureFromFile(imagePath); return; }
   }
            string safeName = MakeSafeFileName(quest.Name ?? quest.QuestId);
       foreach (var ext in extensions)
                {
         string imagePath = Path.Combine(RuntimeSpritesFolder, safeName + ext);
                 if (File.Exists(imagePath)) { LoadTextureFromFile(imagePath); return; }
         }

        // Fallback: show the first quest target's icon as the preview
        TryShowTargetIconAsPreview(quest);
        }

        private void LoadTextureFromFile(string filePath)
  {
    try
    {
     if (_spriteCache.TryGetValue(filePath, out var cachedTexture)) { ApplyPreviewTexture(cachedTexture); return; }
        byte[] fileData = File.ReadAllBytes(filePath);
      var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
             bool loaded = false;
        var imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
             if (imageConversionType != null)
    {
       var loadImageMethod = imageConversionType.GetMethod("LoadImage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
         if (loadImageMethod != null) loaded = (bool)loadImageMethod.Invoke(null, new object[] { texture, fileData, false });
     }
           if (!loaded)
              {
                    try
        {
       var loadImageDirect = typeof(Texture2D).GetMethod("LoadImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(byte[]) }, null);
     if (loadImageDirect != null) loaded = (bool)loadImageDirect.Invoke(texture, new object[] { fileData });
     }
     catch { }
             }
    if (loaded && texture.width > 8) { texture.name = Path.GetFileNameWithoutExtension(filePath); _spriteCache[filePath] = texture; ApplyPreviewTexture(texture); }
   else UnityEngine.Object.Destroy(texture);
    }
            catch { }
   }

        private void ApplyPreviewTexture(Texture2D texture)
        {
         if (_previewRawImage != null && texture != null)
   {
                _previewRawImage.texture = texture;
     _previewRawImage.enabled = true;
        var placeholder = _previewImageContainer?.transform.Find("Placeholder");
 if (placeholder != null) placeholder.gameObject.SetActive(false);
            }
        }

        private void ClearPreviewImage()
        {
 if (_previewRawImage != null) { _previewRawImage.texture = null; _previewRawImage.enabled = false; }
            if (_previewImageContainer != null) { var placeholder = _previewImageContainer.transform.Find("Placeholder"); if (placeholder != null) placeholder.gameObject.SetActive(true); }
            // Clear any sprite-based preview icon
            if (_previewImageContainer != null)
            {
                var iconObj = _previewImageContainer.transform.Find("TargetIcon");
                if (iconObj != null) Destroy(iconObj.gameObject);
            }
            // Clear 3D preview
            _using3DPreview = false;
            _previewPrefabs.Clear();
            if (QuestPreviewRenderer.Instance != null)
                QuestPreviewRenderer.Instance.ClearStage();
            // Hide pagination
            if (_prevPreviewBtn != null) _prevPreviewBtn.gameObject.SetActive(false);
            if (_nextPreviewBtn != null) _nextPreviewBtn.gameObject.SetActive(false);
            if (_previewPageText != null) _previewPageText.gameObject.SetActive(false);
    }

        /// <summary>
        /// When no PNG preview is available, render a 3D preview of the quest's
        /// targets/rewards using the off-screen preview camera.
        /// Supports pagination for multiple previewable prefabs.
        /// </summary>
        private void TryShowTargetIconAsPreview(QuestDefinition quest)
        {
            if (quest == null || _previewImageContainer == null) return;

            // Build list of previewable prefab names (targets then rewards)
            _previewPrefabs.Clear();
            if (quest.QuestTargets != null)
            {
                foreach (var t in quest.QuestTargets)
                {
                    if (!string.IsNullOrEmpty(t.Prefab) && !_previewPrefabs.Contains(t.Prefab))
                        _previewPrefabs.Add(t.Prefab);
                }
            }
            if (quest.Rewards != null)
            {
                foreach (var r in quest.Rewards)
                {
                    if (!r.IsSkillExp && !string.IsNullOrEmpty(r.Target) && !_previewPrefabs.Contains(r.Target))
                        _previewPrefabs.Add(r.Target);
                }
            }

            if (_previewPrefabs.Count == 0) return;

            _previewIndex = 0;
            ShowPreviewAtIndex(0);
        }

        private void ShowPreviewAtIndex(int index)
        {
            if (index < 0 || index >= _previewPrefabs.Count) return;
            _previewIndex = index;

            string prefabName = _previewPrefabs[index];
            var renderer = QuestPreviewRenderer.EnsureInstance();
            bool staged = renderer.StagePreview(prefabName);

            if (staged && renderer.Texture != null)
            {
                // Show 3D preview via RawImage
                _using3DPreview = true;
                if (_previewRawImage != null)
                {
                    _previewRawImage.texture = renderer.Texture;
                    _previewRawImage.enabled = true;
                }
                var placeholder = _previewImageContainer.transform.Find("Placeholder");
                if (placeholder != null) placeholder.gameObject.SetActive(false);

                // Clean up any sprite fallback icon
                var iconObj = _previewImageContainer.transform.Find("TargetIcon");
                if (iconObj != null) Destroy(iconObj.gameObject);
            }
            else
            {
                // Fallback: show 2D sprite icon
                _using3DPreview = false;
                renderer.ClearStage();

                var sprite = GetPrefabIcon(prefabName);
                if (sprite != null)
                {
                    var placeholder = _previewImageContainer.transform.Find("Placeholder");
                    if (placeholder != null) placeholder.gameObject.SetActive(false);
                    if (_previewRawImage != null) _previewRawImage.enabled = false;

                    // Remove old icon if any
                    var existingIcon = _previewImageContainer.transform.Find("TargetIcon");
                    if (existingIcon != null) Destroy(existingIcon.gameObject);

                    var iconGO = new GameObject("TargetIcon");
                    iconGO.transform.SetParent(_previewImageContainer.transform, false);
                    var iconRect = iconGO.AddComponent<RectTransform>();
                    iconRect.anchorMin = new Vector2(0.2f, 0.1f);
                    iconRect.anchorMax = new Vector2(0.8f, 0.9f);
                    iconRect.offsetMin = Vector2.zero;
                    iconRect.offsetMax = Vector2.zero;
                    var iconImg = iconGO.AddComponent<Image>();
                    iconImg.sprite = sprite;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                }
            }

            // Update pagination controls
            UpdatePreviewPagination();
        }

        private void UpdatePreviewPagination()
        {
            bool multi = _previewPrefabs.Count > 1;
            if (_prevPreviewBtn != null) _prevPreviewBtn.gameObject.SetActive(multi);
            if (_nextPreviewBtn != null) _nextPreviewBtn.gameObject.SetActive(multi);
            if (_previewPageText != null)
            {
                _previewPageText.gameObject.SetActive(multi);
                if (multi)
                    _previewPageText.text = $"{_previewIndex + 1} / {_previewPrefabs.Count}";
            }
        }

        private void OnPrevPreview()
        {
            if (_previewPrefabs.Count <= 1) return;
            int newIndex = (_previewIndex - 1 + _previewPrefabs.Count) % _previewPrefabs.Count;
            ShowPreviewAtIndex(newIndex);
        }

        private void OnNextPreview()
        {
            if (_previewPrefabs.Count <= 1) return;
            int newIndex = (_previewIndex + 1) % _previewPrefabs.Count;
            ShowPreviewAtIndex(newIndex);
        }

        /// <summary>
        /// Gets a Sprite icon for a prefab name by checking ObjectDB (items) and ZNetScene (creatures/trophies).
        /// Falls back to rendering a 3D icon for creatures without trophies.
        /// </summary>
        private static Dictionary<string, Sprite> _renderedIconCache = new Dictionary<string, Sprite>();

        private static Sprite GetPrefabIcon(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;

            // Special case: skill XP icon
            if (prefabName == "__SKILL_XP__")
            {
                if (_renderedIconCache.TryGetValue(prefabName, out var xpCached) && xpCached != null)
                    return xpCached;
                var xpSprite = FiresCore.UI.VAMiscAssetManager.GetSprite("XPIcon_vad");
                if (xpSprite != null)
                    _renderedIconCache[prefabName] = xpSprite;
                return xpSprite;
            }

            // Check rendered icon cache first
            if (_renderedIconCache.TryGetValue(prefabName, out var cached) && cached != null)
                return cached;

            try
            {
                // Try item icon
                if (ObjectDB.instance != null)
                {
                    var itemPrefab = ObjectDB.instance.GetItemPrefab(prefabName);
                    if (itemPrefab != null)
                    {
                        var drop = itemPrefab.GetComponent<ItemDrop>();
                        if (drop?.m_itemData != null)
                            return drop.m_itemData.GetIcon();
                    }
                }

                // Try ZNetScene prefab (could be creature or item)
                if (ZNetScene.instance != null)
                {
                    var prefab = ZNetScene.instance.GetPrefab(prefabName);
                    if (prefab != null)
                    {
                        var drop = prefab.GetComponent<ItemDrop>();
                        if (drop?.m_itemData != null)
                            return drop.m_itemData.GetIcon();

                        // Creature - try trophy icon
                        if (prefab.GetComponent<Character>() != null)
                        {
                            if (ObjectDB.instance != null)
                            {
                                var trophy = ObjectDB.instance.GetItemPrefab("Trophy" + prefabName);
                                if (trophy != null)
                                {
                                    var tDrop = trophy.GetComponent<ItemDrop>();
                                    if (tDrop?.m_itemData != null)
                                        return tDrop.m_itemData.GetIcon();
                                }
                            }

                            // No trophy found � render a 3D icon for the creature
                            var rendered = QuestPreviewRenderer.RenderIconSprite(prefabName, 64);
                            if (rendered != null)
                            {
                                _renderedIconCache[prefabName] = rendered;
                                return rendered;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

      private string MakeSafeFileName(string name)
   {
  if (string.IsNullOrEmpty(name)) return "unknown";
        var sb = new System.Text.StringBuilder();
            foreach (char c in name) { if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c); else if (c == ' ') sb.Append('_'); }
  return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        private void UpdateButtonStates()
  {
       if (_selectedQuest == null)
            {
    if (_acceptButton != null) _acceptButton.interactable = false;
             if (_cancelButton != null) _cancelButton.interactable = false;
return;
  }
         var status = GetPlayerQuestStatus(_selectedQuest.QuestId);
  if (_acceptButton != null) _acceptButton.interactable = (status == PlayerQuestStatus.None);
     if (_cancelButton != null) _cancelButton.interactable = (status == PlayerQuestStatus.Active);
        }

        private void OnAcceptQuest()
  {
      if (_selectedQuest == null) return;
   try
 {
      var localPlayer = Player.m_localPlayer;
        if (localPlayer == null) return;
    long playerId = localPlayer.GetPlayerID();
     if (playerId <= 0) return;
       string questId = _selectedQuest.QuestId;
       var questManager = QuestManager.Instance;
  if (questManager != null)
  {
   if (questManager.HasQuest(playerId, questId)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest already active!"); return; }
    if (questManager.HasCompletedQuest(playerId, questId) && _selectedQuest.CooldownDays > 0) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest already completed!"); return; }
   List<string> missing;
      if (!questManager.CheckRequirements(playerId, _selectedQuest, out missing)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Missing requirements: {string.Join(", ", missing)}"); return; }
        }
 bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
     if (!isServer)
    {
    var pkg = new ZPackage();
   pkg.Write(playerId);
    pkg.Write(questId);
    pkg.Write("active");
     pkg.Write(true);
      try
  {
       var vault = VaultOfKnowledge.Instance;
      if (vault != null)
   {
     var playerData = vault.GetPlayerData(playerId);
if (playerData?.BankItems != null && playerData.BankItems.Count > 0)
        {
  pkg.Write(true);
  pkg.Write(playerData.BankItems.Count);
        foreach (var item in playerData.BankItems)
  {
     if (item is StoredBankItem sb)
     {
    pkg.Write(sb.Type ?? "item");
 pkg.Write(sb.Prefab ?? string.Empty);
        pkg.Write(sb.Amount);
   pkg.Write(sb.Quality);
           pkg.Write(sb.Durability.ToString(System.Globalization.CultureInfo.InvariantCulture));
 }
     }
 }
       else pkg.Write(false);
     }
          else pkg.Write(false);
     }
            catch { try { pkg.Write(false); } catch { } }
   ZRoutedRpc.instance?.InvokeRoutedRPC(0L, "FiresRPGmaker_Vault_UpdateQuestFlag", pkg);
      }
    else VaultOfKnowledge.Instance?.ApplyQuestFlagLocally(playerId, questId, "active", true);
  MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest accepted: {_selectedQuest.Name}");

      // Immediately update button states so Accept greys out and Cancel enables
      // without waiting for vault sync round-trip
      if (_acceptButton != null) _acceptButton.interactable = false;
      if (_cancelButton != null) _cancelButton.interactable = true;

      // Refresh the quest list item status text for the selected quest
      UpdateSelectedQuestListItemStatus("[Active]", new Color(1f, 1f, 0.4f));

      // Also refresh the details panel status line
      if (_selectedQuest != null)
          ShowQuestDetails(_selectedQuest);

      // Delayed refresh to catch full server sync
      StartCoroutineSafe(RefreshAfterDelay(0.5f));
     }
  catch (Exception ex) { Debug.LogError($"[QuestInteractionScreenController] Failed to accept quest: {ex.Message}"); }
       }

       /// <summary>
       /// Updates the status text on the currently selected quest list item for immediate visual feedback.
       /// </summary>
       private void UpdateSelectedQuestListItemStatus(string statusText, Color statusColor)
       {
           if (_selectedQuest == null) return;
           for (int i = 0; i < _availableQuests.Count && i < _questListItems.Count; i++)
           {
               if (_availableQuests[i].QuestId == _selectedQuest.QuestId)
               {
                   var item = _questListItems[i];
                   if (item == null) break;
                   var statusTransform = item.transform.Find("Status");
                   if (statusTransform == null) break;
                   var statusTmp = statusTransform.GetComponent<TMP_Text>();
                   if (statusTmp != null)
                   {
                       statusTmp.text = statusText;
                       statusTmp.color = statusColor;
                   }
                   break;
               }
           }
       }

        private void OnCancelQuest()
  {
            if (_selectedQuest == null) return;
     try
       {
      var localPlayer = Player.m_localPlayer;
  if (localPlayer == null) return;
 long playerId = localPlayer.GetPlayerID();
   if (playerId <= 0) return;
   string questId = _selectedQuest.QuestId;
       bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
     if (!isServer)
   {
      var pkg = new ZPackage();
 pkg.Write(playerId);
   pkg.Write(questId);
  pkg.Write("active");
pkg.Write(false);
          pkg.Write(false);
  ZRoutedRpc.instance?.InvokeRoutedRPC(0L, "FiresRPGmaker_Vault_UpdateQuestFlag", pkg);
        }
      else VaultOfKnowledge.Instance?.ApplyQuestFlagLocally(playerId, questId, "active", false);
  MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest abandoned: {_selectedQuest.Name}");

      // Immediately update button states so Cancel greys out and Accept enables
      if (_acceptButton != null) _acceptButton.interactable = true;
      if (_cancelButton != null) _cancelButton.interactable = false;

      // Clear the status text on the quest list item
      UpdateSelectedQuestListItemStatus("", Color.white);

      // Refresh the details panel status line
      if (_selectedQuest != null)
          ShowQuestDetails(_selectedQuest);

      // Delayed refresh to catch full server sync
      StartCoroutineSafe(RefreshAfterDelay(0.5f));
     }
 catch (Exception ex) { Debug.LogError($"[QuestInteractionScreenController] Failed to abandon quest: {ex.Message}"); }
        }

        /// <summary>
        /// Starts a coroutine safely by using an active MonoBehaviour.
        /// This controller may be on an inactive GameObject (QuestInteract), so we need to
        /// run coroutines on an active object like NpcMainPanelController or FiresRPGmaker.
    /// </summary>
   private void StartCoroutineSafe(System.Collections.IEnumerator coroutine)
 {
// Try NpcMainPanelController first (it's always active when UI is showing)
            var mainPanel = NpcMainPanelController.Instance;
          if (mainPanel != null && mainPanel.gameObject.activeInHierarchy)
         {
    mainPanel.StartCoroutine(coroutine);
     return;
            }
            
   // Fallback to FiresRPGmaker instance
          if (FiresRPGmaker.Instance != null)
            {
     FiresRPGmaker.Instance.StartCoroutine(coroutine);
  return;
            }
     
   // Last resort - try on this object (may fail if inactive)
            try
            {
    StartCoroutine(coroutine);
            }
            catch (Exception ex)
            {
         Debug.LogWarning($"[QuestInteractionScreenController] Could not start coroutine: {ex.Message}");
       }
        }

        /// <summary>
        /// Refreshes the quest list UI immediately, preserving the current selection.
  /// Can be called externally to update the UI when quest state changes.
        /// </summary>
      public void RefreshQuestListUI()
    {
   if (!_initialized || !IsVisible) return;

   var currentQuestId = _selectedQuest?.QuestId;
 
   // Reload quests and repopulate the list
      LoadQuestsFromProfile(_currentNpc?.questProfile);
   PopulateQuestList();
            
        // Try to reselect the previously selected quest
    if (!string.IsNullOrEmpty(currentQuestId))
{
  for (int i = 0; i < _availableQuests.Count && i < _questListItems.Count; i++)
           {
 if (_availableQuests[i].QuestId == currentQuestId)
 {
        _selectedQuest = _availableQuests[i];
        var item = _questListItems[i];
          
    // Update selection highlighting
      foreach (var listItem in _questListItems)
 {
        var img = listItem?.GetComponent<Image>();
        if (img != null) img.color = (listItem == item) ? ItemSelectedColor : ItemNormalColor;
   }
          
     // Update details panel
    ShowQuestDetails(_availableQuests[i]);
            LoadPreviewImage(_availableQuests[i]);
  break;
 }
          }
 }
            
  UpdateButtonStates();
    }

 private System.Collections.IEnumerator RefreshAfterDelay(float delay)
        {
         yield return new WaitForSeconds(delay);
            
      // Check if we're still visible before refreshing
  if (Instance != null && Instance.IsVisible)
      {
  Instance.RefreshQuestListUI();
}
         
  // Do another refresh after a bit longer to catch server sync delays
    yield return new WaitForSeconds(0.5f);
            
          if (Instance != null && Instance.IsVisible)
  {
       Instance.RefreshQuestListUI();
          }
        }

        /// <summary>
      /// Gets the player's current status for a quest using the centralized QuestStatusService.
        /// </summary>
  private PlayerQuestStatus GetPlayerQuestStatus(string questId)
        {
        return QuestStatusService.GetPlayerQuestStatus(questId);
        }

        /// <summary>
        /// Applies the Valheim font to a TMP_Text component.
        /// Delegates to UIBuilderHelper for consistency.
        /// </summary>
        private static void ApplyFont(TMP_Text text)
   {
         UIBuilderHelper.ApplyValheimFont(text);
  }
    }
}
