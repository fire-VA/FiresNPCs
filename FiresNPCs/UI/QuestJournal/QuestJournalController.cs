using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Services;
using PlayerQuestStatus = FiresRPGmaker.Services.QuestStatusService.PlayerQuestStatus;

namespace FiresRPGmaker.UI.QuestJournal
{
    /// <summary>
    /// Controller for the Quest Journal UI - allows players to view their active and completed quests.
    /// </summary>
    public class QuestJournalController : MonoBehaviour
    {
        public static QuestJournalController Instance { get; private set; }
     public GameObject screenRoot;

        // UI Elements - Background Layer
  private GameObject _backgroundLayer;
        private Image _backgroundImage;

        // UI Elements - Left Panel (Quest List)
        private GameObject _questListPanel;
   private ScrollRect _questListScrollRect;
        private RectTransform _questListContent;
  private TMP_Text _listHeaderText;
        private Button _toggleListButton;
        private TMP_Text _toggleButtonText;

        // UI Elements - Right Panel (Quest Details)
     private GameObject _questDetailsContainer;
        private TMP_Text _questNameText;
        private TMP_Text _questDescriptionText;
        private ScrollRect _descriptionScrollRect;
        private RectTransform _descriptionContent;
        private GameObject _previewImageContainer;
      private RawImage _previewRawImage;

     // UI Elements - Bottom Panel (Buttons)
private GameObject _buttonsPanel;
        private Button _abandonButton;
        private Button _closeButton;

        // State
        private List<ActiveQuestData> _activeQuests = new List<ActiveQuestData>();
        private List<string> _completedQuests = new List<string>();
        private List<GameObject> _questListItems = new List<GameObject>();
      private string _selectedQuestId;
     private bool _showingCompleted = false;
        private bool _initialized;

        // Drag state
        private bool _isDragging;
        private Vector2 _dragOffset;
     private JournalDragHandler _dragHandler;

 // Resize state
  private GameObject _resizeHandle;
   private bool _isResizing;
        private Vector2 _resizeStartMousePos;
   private Vector2 _resizeStartSize;
        private const float MIN_WIDTH = 300f;
      private const float MIN_HEIGHT = 350f;
    private const float MAX_WIDTH = 1200f;
        private const float MAX_HEIGHT = 1000f;
        private const float RESIZE_HANDLE_SIZE = 24f;

    // Config entries for position persistence
        private static ConfigEntry<float> _panelPosXConfig;
        private static ConfigEntry<float> _panelPosYConfig;
        private static ConfigEntry<bool> _panelLockedConfig;
        private static ConfigEntry<float> _panelWidthConfig;
        private static ConfigEntry<float> _panelHeightConfig;

        // Layout constants
private const float ITEM_HEIGHT = 32f;
   private const float ITEM_SPACING = 4f;
        private const float FONT_SIZE = 12f;
      private const float TITLE_FONT_SIZE = 18f;
     private const float DESC_FONT_SIZE = 11f;
        private const float HEADER_FONT_SIZE = 16f;
  private const float BACKGROUND_MARGIN_X = 60f;
        private const float BACKGROUND_MARGIN_Y = 70f;
        
   // Default content size for font scaling reference
        private const float DEFAULT_CONTENT_WIDTH = 600f;
        private const float DEFAULT_CONTENT_HEIGHT = 700f;

        // Colors from UIFontConfig
        private static Color ItemNormalColor => UIFontConfig.Colors.ItemNormal;
        private static Color ItemSelectedColor => UIFontConfig.Colors.ItemSelected;
        private static Color ItemHoverColor => UIFontConfig.Colors.ItemHover;
        private static Color TextColor => UIFontConfig.Colors.Gold;
        private static Color LabelColor => UIFontConfig.Colors.LabelGold;

    private static string RuntimeSpritesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestSprites);
        private static Dictionary<string, Texture2D> _spriteCache = new Dictionary<string, Texture2D>();

        public bool IsVisible => screenRoot != null && screenRoot.activeSelf;

    private void Awake()
{
   Instance = this;

    if (FiresRPGmaker.Instance != null)
      {
     if (_panelPosXConfig == null)
            _panelPosXConfig = FiresRPGmaker.Instance.Config.Bind("UI", "Quest Journal Position X", 0f, "X position of the Quest Journal");
        if (_panelPosYConfig == null)
             _panelPosYConfig = FiresRPGmaker.Instance.Config.Bind("UI", "Quest Journal Position Y", 0f, "Y position of the Quest Journal");
          if (_panelLockedConfig == null)
  _panelLockedConfig = FiresRPGmaker.Instance.Config.Bind("UI", "Quest Journal Locked", false, "Whether the Quest Journal position is locked");
           if (_panelWidthConfig == null)
           _panelWidthConfig = FiresRPGmaker.Instance.Config.Bind("UI", "Quest Journal Width", 600f, "Width of the Quest Journal content area");
           if (_panelHeightConfig == null)
         _panelHeightConfig = FiresRPGmaker.Instance.Config.Bind("UI", "Quest Journal Height", 700f, "Height of the Quest Journal content area");
            }
 }

 private void OnDestroy() { if (Instance == this) Instance = null; }

 private void Update()
        {
   if (!gameObject.activeSelf || !IsVisible) return;
 // Escape owned by the central Menu.Show hook (CloseAllPanels) — no self-close (avoids the menu-opens race).
        }

 public static QuestJournalController Create()
  {
      if (Instance != null && Instance._initialized)
          {
          Debug.Log("[QuestJournalController] Using existing controller");
    return Instance;
 }

  var canvasGO = new GameObject("QuestJournalCanvas");
            DontDestroyOnLoad(canvasGO);

            var canvas = canvasGO.AddComponent<Canvas>();
          canvas.renderMode = RenderMode.ScreenSpaceOverlay;
         canvas.sortingOrder = 100;

 var scaler = canvasGO.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
         scaler.referenceResolution = new Vector2(1920, 1080);
      scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

   var rootGO = new GameObject("QuestJournalRoot");
      rootGO.transform.SetParent(canvasGO.transform, false);

         var rootRect = rootGO.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
      rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);

     float contentWidth = _panelWidthConfig?.Value ?? 600f;
            float contentHeight = _panelHeightConfig?.Value ?? 700f;
            rootRect.sizeDelta = new Vector2(contentWidth + BACKGROUND_MARGIN_X * 2, contentHeight + BACKGROUND_MARGIN_Y * 2);
        rootRect.anchoredPosition = Vector2.zero;

    var controller = rootGO.AddComponent<QuestJournalController>();
    controller.screenRoot = rootGO;
            controller.CreateUI(rootRect);
            controller._initialized = true;

        rootGO.SetActive(false);

      Debug.Log("[QuestJournalController] Created Quest Journal UI with background layer");
   return controller;
        }

        private void CreateUI(RectTransform rootRect)
        {
            CreateBackgroundLayer(rootRect);

            var contentPanelGO = new GameObject("ContentPanel");
            contentPanelGO.transform.SetParent(rootRect, false);

 var contentRect = contentPanelGO.AddComponent<RectTransform>();
          contentRect.anchorMin = Vector2.zero;
         contentRect.anchorMax = Vector2.one;
     contentRect.offsetMin = new Vector2(BACKGROUND_MARGIN_X, BACKGROUND_MARGIN_Y);
     contentRect.offsetMax = new Vector2(-BACKGROUND_MARGIN_X, -BACKGROUND_MARGIN_Y);

      var contentBg = contentPanelGO.AddComponent<Image>();
      contentBg.color = UIFontConfig.Colors.PanelBackground;
            contentBg.raycastTarget = false;

CreateHeader(contentRect);
            CreateQuestListPanel(contentRect);
       CreateQuestDetailsPanel(contentRect);
   CreateButtonsPanel(contentRect);
       CreateResizeHandle(rootRect);

      SetupDragHandler();
            FixAllFonts();
  }

        private void CreateBackgroundLayer(RectTransform parent)
     {
 _backgroundLayer = new GameObject("BackgroundLayer");
    _backgroundLayer.transform.SetParent(parent, false);
            _backgroundLayer.transform.SetAsFirstSibling();

            var bgRect = _backgroundLayer.AddComponent<RectTransform>();
   bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
   bgRect.offsetMin = Vector2.zero;
 bgRect.offsetMax = Vector2.zero;

            _backgroundImage = _backgroundLayer.AddComponent<Image>();

   var bgSprite = UIPrefabFactory.GetBackgroundSprite();
 if (bgSprite != null)
 {
             _backgroundImage.sprite = bgSprite;
 _backgroundImage.type = Image.Type.Sliced;
     _backgroundImage.color = Color.white;
       Debug.Log("[QuestJournalController] Applied asset bundle background sprite");
      }
     else
          {
  _backgroundImage.color = new Color(0.08f, 0.08f, 0.1f, 0.98f);
      Debug.Log("[QuestJournalController] Using fallback solid background color");
     }

     _backgroundImage.raycastTarget = true;
        }

        private void CreateHeader(RectTransform parent)
        {
          var headerGO = new GameObject("Header");
        headerGO.transform.SetParent(parent, false);

            var headerRect = headerGO.AddComponent<RectTransform>();
         headerRect.anchorMin = new Vector2(0, 0.92f);
    headerRect.anchorMax = new Vector2(1, 1f);
            headerRect.offsetMin = Vector2.zero;
      headerRect.offsetMax = new Vector2(0, 0);

       var headerBg = headerGO.AddComponent<Image>();
            headerBg.color = UIFontConfig.Colors.HeaderBackground;
 headerBg.raycastTarget = true;

  var headerText = UIBuilderHelper.CreateText(headerGO.transform, "HeaderText", "Quest Journal",
          UIFontConfig.AvailableQuestsTitle, new Vector2(0, 0), new Vector2(1, 1));
            var textRect = headerText.GetComponent<RectTransform>();
 textRect.offsetMin = new Vector2(10, 0);
        textRect.offsetMax = new Vector2(-10, 0);
         headerText.alignment = TextAlignmentOptions.Center;
            headerText.fontSize = 20f;

    var headerDragHandler = headerGO.AddComponent<JournalDragHandler>();
    headerDragHandler.Initialize(this);
        }

    private void CreateQuestListPanel(RectTransform parent)
        {
            _questListPanel = UIBuilderHelper.CreatePanel(parent, "QuestListPanel",
      new Vector2(0.02f, 0.12f), new Vector2(0.48f, 0.90f), UIFontConfig.Colors.PanelBackground);

            var titleContainer = new GameObject("TitleContainer");
   titleContainer.transform.SetParent(_questListPanel.transform, false);
            var titleContainerRect = titleContainer.AddComponent<RectTransform>();
 titleContainerRect.anchorMin = new Vector2(0, 1);
    titleContainerRect.anchorMax = new Vector2(1, 1);
            titleContainerRect.pivot = new Vector2(0.5f, 1);
 titleContainerRect.anchoredPosition = new Vector2(0, -5);
        titleContainerRect.sizeDelta = new Vector2(-10, 50);

            _listHeaderText = UIBuilderHelper.CreateText(titleContainer.transform, "Title", "Active Quests",
    UIFontConfig.AvailableQuestsTitle, new Vector2(0, 0.5f), new Vector2(1, 1));
     var titleRect = _listHeaderText.GetComponent<RectTransform>();
     titleRect.offsetMin = new Vector2(5, 0);
         titleRect.offsetMax = new Vector2(-5, 0);
            _listHeaderText.alignment = TextAlignmentOptions.Center;

            _toggleListButton = UIBuilderHelper.CreateButton(titleContainer.transform, "Show Completed",
        new Vector2(0.1f, 0f), new Vector2(0.9f, 0.45f), OnToggleListClicked);
            _toggleButtonText = _toggleListButton.GetComponentInChildren<TMP_Text>();
          if (_toggleButtonText != null)
       {
     _toggleButtonText.fontSize = 10f;
            }

     var scrollResult = UIBuilderHelper.CreateScrollArea(_questListPanel.transform, "QuestListScroll",
    Vector2.zero, Vector2.one, null, 12f);

      scrollResult.Viewport.offsetMax = new Vector2(-18, -60);
            scrollResult.Viewport.offsetMin = new Vector2(5, 5);

          _questListContent = scrollResult.Content;
       _questListScrollRect = scrollResult.ScrollRect;

            var scrollbarRect = scrollResult.Scrollbar.GetComponent<RectTransform>();
       scrollbarRect.anchoredPosition = new Vector2(-3, -30);
      scrollbarRect.sizeDelta = new Vector2(12, -70);
    }

      private void CreateQuestDetailsPanel(RectTransform parent)
        {
          _questDetailsContainer = UIBuilderHelper.CreatePanel(parent, "QuestDetailsPanel",
           new Vector2(0.50f, 0.12f), new Vector2(0.98f, 0.90f), UIFontConfig.Colors.PanelBackground);

            CreatePngPreviewArea();
  CreateQuestNameLabel();
            CreateDescriptionScrollArea();
}

        private void CreatePngPreviewArea()
        {
            _previewImageContainer = UIBuilderHelper.CreatePanel(_questDetailsContainer.transform, "PNG",
     new Vector2(0.02f, 0.56f), new Vector2(0.98f, 0.98f), UIFontConfig.Colors.PanelBackgroundLight);

   _previewRawImage = UIBuilderHelper.CreateRawImage(_previewImageContainer.transform, "PreviewImage",
           new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));

            var placeholderText = UIBuilderHelper.CreateLabel(_previewImageContainer.transform, "Placeholder",
       "No Preview", 10f, UIFontConfig.Colors.PlaceholderText, Vector2.zero, Vector2.one);
        }

 private void CreateQuestNameLabel()
   {
   _questNameText = UIBuilderHelper.CreateText(_questDetailsContainer.transform, "Label", "Select a Quest",
        UIFontConfig.SelectedQuestName, new Vector2(0.02f, 0.48f), new Vector2(0.98f, 0.55f));
        }

        private void CreateDescriptionScrollArea()
        {
    var scrollResult = UIBuilderHelper.CreateScrollArea(_questDetailsContainer.transform, "DescriptionScrollArea",
     new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.47f), UIFontConfig.Colors.PanelBackgroundDim, 12f);

     _descriptionContent = scrollResult.Content;
        _descriptionScrollRect = scrollResult.ScrollRect;

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

     UIBuilderHelper.AddLayoutElement(textGO, flexibleWidth: 1);
      UIBuilderHelper.AddContentSizeFitter(textGO,
     ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

            UIBuilderHelper.AddVerticalLayout(_descriptionContent.gameObject, new RectOffset(8, 0, 0, 0),
       0, true, true, true, false);
  UIBuilderHelper.AddContentSizeFitter(_descriptionContent.gameObject,
                ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);
  }

        private void CreateButtonsPanel(RectTransform parent)
        {
         _buttonsPanel = UIBuilderHelper.CreatePanel(parent, "ButtonsPanel",
  new Vector2(0.15f, 0.02f), new Vector2(0.85f, 0.10f), UIFontConfig.Colors.PanelBackgroundLight);

            _abandonButton = UIBuilderHelper.CreateButton(_buttonsPanel.transform, "Abandon Quest",
    new Vector2(0.05f, 0.15f), new Vector2(0.47f, 0.85f), OnAbandonQuest);

            _closeButton = UIBuilderHelper.CreateButton(_buttonsPanel.transform, "Close",
    new Vector2(0.53f, 0.15f), new Vector2(0.95f, 0.85f), Hide);
        }

        private void CreateResizeHandle(RectTransform parent)
        {
  _resizeHandle = new GameObject("ResizeHandle");
   _resizeHandle.transform.SetParent(parent, false);

   var handleRect = _resizeHandle.AddComponent<RectTransform>();
     handleRect.anchorMin = new Vector2(1, 0);
      handleRect.anchorMax = new Vector2(1, 0);
     handleRect.pivot = new Vector2(1, 0);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(RESIZE_HANDLE_SIZE, RESIZE_HANDLE_SIZE);

  var handleImage = _resizeHandle.AddComponent<Image>();
      handleImage.color = new Color(0.6f, 0.5f, 0.3f, 0f);   // 0.0.542: invisible hit area; the dot triangle below is the grip (it was drawn over a solid square)
    handleImage.raycastTarget = true;

            var lineContainer = new GameObject("ResizeLines");
            lineContainer.transform.SetParent(_resizeHandle.transform, false);
            var lineRect = lineContainer.AddComponent<RectTransform>();
 lineRect.anchorMin = Vector2.zero;
lineRect.anchorMax = Vector2.one;
         lineRect.offsetMin = new Vector2(4, 4);
    lineRect.offsetMax = new Vector2(-4, -4);

     for (int i = 0; i < 3; i++)
     {
     for (int j = 0; j < 3 - i; j++)
  {
  var dot = new GameObject($"Dot_{i}_{j}");
   dot.transform.SetParent(lineContainer.transform, false);
            var dotRect = dot.AddComponent<RectTransform>();
dotRect.anchorMin = new Vector2(1, 0);
      dotRect.anchorMax = new Vector2(1, 0);
         dotRect.pivot = new Vector2(1, 0);
dotRect.sizeDelta = new Vector2(3, 3);
     dotRect.anchoredPosition = new Vector2(-4 - (i * 5), 4 + (j * 5));

  var dotImage = dot.AddComponent<Image>();
         dotImage.color = new Color(0.9f, 0.8f, 0.5f, 1f);
      dotImage.raycastTarget = false;
         }
}

            var resizeHandler = _resizeHandle.AddComponent<JournalResizeHandler>();
          resizeHandler.Initialize(this);

        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[QuestJournalController] resize handle: dot-triangle grip on a transparent hit area");
        }

        private bool _fontsApplied;

        private void FixAllFonts()
        {
          // Only apply fonts once - iterating all TMP children dirties every text element
          if (_fontsApplied) return;
          _fontsApplied = true;
          UIBuilderHelper.ApplyValheimFontToAll(screenRoot);
     }

        #region Drag Handling

        private void SetupDragHandler()
        {
            if (_backgroundLayer != null)
 {
                _dragHandler = _backgroundLayer.GetComponent<JournalDragHandler>();
         if (_dragHandler == null)
 {
            _dragHandler = _backgroundLayer.AddComponent<JournalDragHandler>();
        }
   _dragHandler.Initialize(this);
}
            Debug.Log("[QuestJournalController] Drag handler setup complete");
        }

        public void OnDragStart(Vector2 mousePosition)
        {
 if (_panelLockedConfig?.Value == true) return;

        _isDragging = true;
         var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
     {
         _dragOffset = (Vector2)rectTransform.position - mousePosition;
            }
        }

    public void OnDrag(Vector2 mousePosition)
 {
       if (!_isDragging || _panelLockedConfig?.Value == true) return;

            var rectTransform = GetComponent<RectTransform>();
  if (rectTransform != null)
  {
            rectTransform.position = mousePosition + _dragOffset;
            }
        }

   public void OnDragEnd()
        {
  if (!_isDragging) return;

      _isDragging = false;
            SavePanelPosition();
    }

 private void SavePanelPosition()
        {
   var rectTransform = GetComponent<RectTransform>();
   if (rectTransform == null) return;

         if (_panelPosXConfig != null && _panelPosYConfig != null)
   {
           _panelPosXConfig.Value = rectTransform.anchoredPosition.x;
    _panelPosYConfig.Value = rectTransform.anchoredPosition.y;
       FiresRPGmaker.Instance?.Config.Save();
      Debug.Log($"[QuestJournalController] Position saved: ({_panelPosXConfig.Value}, {_panelPosYConfig.Value})");
            }
        }

        private void LoadPanelPosition()
 {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null) return;

     if (_panelPosXConfig != null && _panelPosYConfig != null)
 {
     var savedPos = new Vector2(_panelPosXConfig.Value, _panelPosYConfig.Value);
    rectTransform.anchoredPosition = savedPos;
                Debug.Log($"[QuestJournalController] Position loaded: ({_panelPosXConfig.Value}, {_panelPosYConfig.Value})");
       }
      }

  public void ToggleLock()
        {
    if (_panelLockedConfig != null)
            {
        _panelLockedConfig.Value = !_panelLockedConfig.Value;
        FiresRPGmaker.Instance?.Config.Save();

   string status = _panelLockedConfig.Value ? "locked" : "unlocked";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest Journal position {status}");
        Debug.Log($"[QuestJournalController] Position {status}");
       }
        }

        public void ResetPosition()
        {
        var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
         {
         rectTransform.anchoredPosition = Vector2.zero;
  SavePanelPosition();
      MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Quest Journal position reset");
       }
   }

        #endregion

  #region Resize Handling

     public void OnResizeStart(Vector2 mousePosition)
        {
      if (_panelLockedConfig?.Value == true) return;

            _isResizing = true;
            _resizeStartMousePos = mousePosition;
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
          {
           _resizeStartSize = rectTransform.sizeDelta;
      }
        }

  public void OnResize(Vector2 mousePosition)
        {
      if (!_isResizing || _panelLockedConfig?.Value == true) return;

   var rectTransform = GetComponent<RectTransform>();
   if (rectTransform == null) return;

            Vector2 delta = mousePosition - _resizeStartMousePos;

   float newWidth = _resizeStartSize.x + delta.x;
    float newHeight = _resizeStartSize.y - delta.y;

    float minTotalWidth = MIN_WIDTH + BACKGROUND_MARGIN_X * 2;
        float maxTotalWidth = MAX_WIDTH + BACKGROUND_MARGIN_X * 2;
   float minTotalHeight = MIN_HEIGHT + BACKGROUND_MARGIN_Y * 2;
       float maxTotalHeight = MAX_HEIGHT + BACKGROUND_MARGIN_Y * 2;

        newWidth = Mathf.Clamp(newWidth, minTotalWidth, maxTotalWidth);
      newHeight = Mathf.Clamp(newHeight, minTotalHeight, maxTotalHeight);

         rectTransform.sizeDelta = new Vector2(newWidth, newHeight);
         
  // Apply font scaling in real-time during resize for immediate visual feedback
         ApplyFontScaling();
        }

        public void OnResizeEnd()
        {
      if (!_isResizing) return;

 _isResizing = false;
     SavePanelSize();
            ApplyFontScaling();
        }

     private void SavePanelSize()
        {
          var rectTransform = GetComponent<RectTransform>();
         if (rectTransform == null) return;

            if (_panelWidthConfig != null && _panelHeightConfig != null)
        {
           float contentWidth = rectTransform.sizeDelta.x - BACKGROUND_MARGIN_X * 2;
         float contentHeight = rectTransform.sizeDelta.y - BACKGROUND_MARGIN_Y * 2;
       _panelWidthConfig.Value = contentWidth;
     _panelHeightConfig.Value = contentHeight;
    FiresRPGmaker.Instance?.Config.Save();
         Debug.Log($"[QuestJournalController] Size saved: {contentWidth}x{contentHeight}");
            }
        }

        private void LoadPanelSize()
        {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null) return;

         if (_panelWidthConfig != null && _panelHeightConfig != null)
            {
    float contentWidth = Mathf.Clamp(_panelWidthConfig.Value, MIN_WIDTH, MAX_WIDTH);
              float contentHeight = Mathf.Clamp(_panelHeightConfig.Value, MIN_HEIGHT, MAX_HEIGHT);
    rectTransform.sizeDelta = new Vector2(
      contentWidth + BACKGROUND_MARGIN_X * 2,
             contentHeight + BACKGROUND_MARGIN_Y * 2
         );
                Debug.Log($"[QuestJournalController] Size loaded: {contentWidth}x{contentHeight}");
        ApplyFontScaling();
            }
        }

public void ResetSize()
        {
   var rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
       {
 rectTransform.sizeDelta = new Vector2(600f + BACKGROUND_MARGIN_X * 2, 700f + BACKGROUND_MARGIN_Y * 2);
      SavePanelSize();
           ApplyFontScaling();
           MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Quest Journal size reset");
            }
        }

        /// <summary>
        /// Calculates the current font scale factor based on panel size relative to default.
        /// </summary>
        private float GetFontScaleFactor()
  {
       var rectTransform = GetComponent<RectTransform>();
  if (rectTransform == null) return 1f;

         float currentWidth = rectTransform.sizeDelta.x - BACKGROUND_MARGIN_X * 2;
    float currentHeight = rectTransform.sizeDelta.y - BACKGROUND_MARGIN_Y * 2;

      // Calculate scale based on the smaller dimension ratio to maintain readability
    float widthScale = currentWidth / DEFAULT_CONTENT_WIDTH;
     float heightScale = currentHeight / DEFAULT_CONTENT_HEIGHT;

        // Use the smaller scale to ensure text fits, but don't go below 0.5 or above 1.2
      float scale = Mathf.Min(widthScale, heightScale);
  return Mathf.Clamp(scale, 0.5f, 1.2f);
  }

        /// <summary>
        /// Applies font scaling to all text elements based on current panel size.
        /// </summary>
        private void ApplyFontScaling()
        {
        float scale = GetFontScaleFactor();

 // Scale header text
       if (_listHeaderText != null)
    _listHeaderText.fontSize = HEADER_FONT_SIZE * scale;

   if (_toggleButtonText != null)
_toggleButtonText.fontSize = 10f * scale;

    // Scale quest name text
  if (_questNameText != null)
    _questNameText.fontSize = TITLE_FONT_SIZE * scale;

        // Scale description text
    if (_questDescriptionText != null)
    _questDescriptionText.fontSize = DESC_FONT_SIZE * scale;

    // Scale quest list items
 foreach (var item in _questListItems)
            {
          if (item == null) continue;
         var texts = item.GetComponentsInChildren<TMP_Text>();
   foreach (var text in texts)
   {
        if (text != null)
           {
    float baseSize = text.fontSize > 10f ? FONT_SIZE : (FONT_SIZE - 2);
     text.fontSize = baseSize * scale;
 }
                }
       }

   // Scale button text
     if (_abandonButton != null)
        {
 var btnText = _abandonButton.GetComponentInChildren<TMP_Text>();
if (btnText != null) btnText.fontSize = FONT_SIZE * scale;
    }

            if (_closeButton != null)
         {
            var btnText = _closeButton.GetComponentInChildren<TMP_Text>();
           if (btnText != null) btnText.fontSize = FONT_SIZE * scale;
            }
   }

        #endregion

    #region Public API

        public static void Toggle()
    {
       if (Instance == null || !Instance._initialized)
            {
    Create();
            }

      if (Instance != null)
            {
                if (Instance.IsVisible)
              Instance.Hide();
             else
             Instance.Show();
            }
        }

        public void Show()
        {
            if (!_initialized) return;

            // Sprite sync now runs automatically on login (bidirectional).
            // No need to request again here.

            LoadPlayerQuests();
        _showingCompleted = false;
     UpdateListHeader();
   PopulateQuestList();
            ClearQuestDetails();
     UpdateButtonStates();

         screenRoot?.SetActive(true);
        GUIManager.BlockInput(true);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

    FixAllFonts();
     LoadPanelPosition();
  LoadPanelSize();
   Canvas.ForceUpdateCanvases();

            Debug.Log($"[QuestJournalController] Showing Quest Journal - Active: {_activeQuests.Count}, Completed: {_completedQuests.Count}");
        }

      public void Hide()
        {
            screenRoot?.SetActive(false);
            _selectedQuestId = null;
            _isDragging = false;
    ClearQuestList();
            GUIManager.BlockInput(false);
        }

        #endregion

   #region Data Loading

    private void LoadPlayerQuests()
        {
         _activeQuests.Clear();
 _completedQuests.Clear();

   try
  {
                var player = Player.m_localPlayer;
        if (player == null) return;

       long playerId = player.GetPlayerID();
     if (playerId <= 0) return;

          var vault = VaultOfKnowledge.Instance;
       if (vault == null) return;

       var playerData = vault.GetPlayerData(playerId);
          if (playerData == null) return;

  if (playerData.ActiveQuests != null)
        {
           _activeQuests.AddRange(playerData.ActiveQuests);
       }

                if (playerData.CompletedQuests != null)
            {
      _completedQuests.AddRange(playerData.CompletedQuests.Keys);
                }
   }
            catch (Exception ex)
      {
  Debug.LogWarning($"[QuestJournalController] Error loading player quests: {ex.Message}");
         }
        }

        #endregion

   #region Quest List

     private void OnToggleListClicked()
      {
_showingCompleted = !_showingCompleted;
      UpdateListHeader();
            PopulateQuestList();
            ClearQuestDetails();
          UpdateButtonStates();
        }

     private void UpdateListHeader()
        {
            if (_listHeaderText != null)
        {
       _listHeaderText.text = _showingCompleted ? "Completed Quests" : "Active Quests";
            }

 if (_toggleButtonText != null)
      {
       _toggleButtonText.text = _showingCompleted ? "Show Active" : "Show Completed";
         }
 }

    private void PopulateQuestList()
        {
            ClearQuestList();
            if (_questListContent == null) return;

  float y = -5f;
            float scale = GetFontScaleFactor();

            if (_showingCompleted)
    {
     foreach (var questId in _completedQuests)
                {
   var questDef = QuestManager.Instance?.GetQuestDefinition(questId);
      if (questDef != null)
 {
                var item = CreateQuestListItem(questDef, y, isCompleted: true, null, scale);
     _questListItems.Add(item);
      y -= (ITEM_HEIGHT * scale) + ITEM_SPACING;
                    }
                }
    }
else
            {
       foreach (var activeQuest in _activeQuests)
       {
     var questDef = QuestManager.Instance?.GetQuestDefinition(activeQuest.QuestId);
     if (questDef != null)
           {
   var item = CreateQuestListItem(questDef, y, isCompleted: false, activeQuest, scale);
        _questListItems.Add(item);
          y -= (ITEM_HEIGHT * scale) + ITEM_SPACING;
            }
    }
       }

          _questListContent.sizeDelta = new Vector2(_questListContent.sizeDelta.x, Mathf.Abs(y) + 10f);
  if (_questListScrollRect != null) _questListScrollRect.verticalNormalizedPosition = 1f;
        }

   private GameObject CreateQuestListItem(QuestDefinition quest, float yPos, bool isCompleted, ActiveQuestData activeEntry = null, float scale = 1f)
        {
      float scaledHeight = ITEM_HEIGHT * scale;
      var itemResult = UIBuilderHelper.CreateListItem(_questListContent, $"QuestItem_{quest.QuestId}",
    quest.Name ?? quest.QuestId, yPos, scaledHeight);

       var go = itemResult.Root;
  var statusText = itemResult.StatusText;

   // Apply font scaling to the item's text elements
   var texts = go.GetComponentsInChildren<TMP_Text>();
       foreach (var text in texts)
   {
           if (text != null)
      {
        // Status text gets smaller font (FONT_SIZE - 4 instead of FONT_SIZE - 2)
    text.fontSize = (text == statusText ? (FONT_SIZE - 4) : FONT_SIZE) * scale;
      }
     }

        if (isCompleted)
          {
    statusText.text = "[Completed]";
   statusText.color = UIFontConfig.Colors.QuestCompleted;
}
          else if (activeEntry != null)
    {
      var progressInfo = QuestProgressTracker.GetProgressSummary(quest, activeEntry);
  statusText.text = progressInfo;
    statusText.color = UIFontConfig.Colors.QuestActive;
     }

var questRef = quest;
     var activeRef = activeEntry;
     itemResult.Button.onClick.AddListener(() => OnQuestSelected(questRef, go, activeRef, isCompleted));

  return go;
     }

        private void ClearQuestList()
        {
            foreach (var item in _questListItems)
     {
        if (item != null) Destroy(item);
            }
    _questListItems.Clear();
        }

   private void OnQuestSelected(QuestDefinition quest, GameObject listItem, ActiveQuestData activeEntry, bool isCompleted)
     {
 _selectedQuestId = quest.QuestId;

foreach (var item in _questListItems)
 {
         var img = item?.GetComponent<Image>();
  if (img != null) img.color = (item == listItem) ? ItemSelectedColor : ItemNormalColor;
        }

            ShowQuestDetails(quest, activeEntry, isCompleted);
         LoadPreviewImage(quest);
UpdateButtonStates();
   }

        #endregion

        #region Quest Details

        private void ShowQuestDetails(QuestDefinition quest, ActiveQuestData activeEntry, bool isCompleted)
        {
            if (quest == null)
      {
   ClearQuestDetails();
  return;
            }

   if (_questNameText != null) _questNameText.text = quest.Name ?? quest.QuestId;

     if (_questDescriptionText != null)
            {
     float scale = GetFontScaleFactor();
          float smallSize = (DESC_FONT_SIZE - 2) * scale;
    string gold = "#C9A86C";
       string desc = "#C9A86C";
     string gray = "#CFCFCF";
     string green = "#66FF66";
      string yellow = "#FFFF66";

   var sb = new System.Text.StringBuilder();
  sb.AppendLine($"<color={desc}>{quest.Description ?? "No description"}</color>\n");

    if (quest.QuestTargets != null && quest.QuestTargets.Count > 0)
  {
      sb.AppendLine($"<b><color={gold}>Objectives:</color></b>");

 foreach (var target in quest.QuestTargets)
        {
   string progressStr = "";
   if (!isCompleted && activeEntry != null)
   {
          int current = QuestProgressTracker.GetProgressForTarget(quest, activeEntry, target);
 string progressColor = current >= target.Amount ? green : yellow;
      progressStr = $" <color={progressColor}>[{current}/{target.Amount}]</color>";
     }
      else if (isCompleted)
   {
 progressStr = $" <color={green}>[{target.Amount}/{target.Amount}]</color>";
  }

     // Use GetTargetDisplayName to prefer NameFilter over Prefab
    string targetName = QuestProgressTracker.GetTargetDisplayName(target);
    var line = $"<size={smallSize}><color=#FFB366>{targetName}</color> <color={gray}>x{target.Amount}";
         if (target.MinLevel > 0) line += $" (Lvl {target.MinLevel}+)";

   line += $"</color>{progressStr}</size>";
      sb.AppendLine(line);
        }
     sb.AppendLine();
          }

          if (quest.Rewards != null && quest.Rewards.Count > 0)
    {
       sb.AppendLine($"<b><color={gold}>Rewards:</color></b>");
   foreach (var r in quest.Rewards)
         sb.AppendLine($"<size={smallSize}><color=#6699FF>{r.Target}</color> <color={gray}>x{r.Amount}</color></size>");
   sb.AppendLine();
    }

      if (quest.TimeLimitMinutes > 0)
      sb.AppendLine($"<b><color={gold}>Time Limit:</color></b> <size={smallSize}><color={gray}>{quest.TimeLimitMinutes} minutes</color></size>\n");

  if (!isCompleted && activeEntry != null)
    {
      sb.AppendLine($"<b><color={gold}>Progress:</color></b>");
        string detailedProgress = QuestProgressTracker.GetDetailedProgress(quest, activeEntry);
   sb.AppendLine($"<size={smallSize}>{detailedProgress}</size>");
     sb.AppendLine();

             if (activeEntry.StartedAt != default)
       {
          DateTime startedAtUtc = activeEntry.StartedAt.Kind == DateTimeKind.Unspecified
       ? DateTime.SpecifyKind(activeEntry.StartedAt, DateTimeKind.Utc)
       : activeEntry.StartedAt.ToUniversalTime();

          var elapsed = DateTime.UtcNow - startedAtUtc;
        string elapsedStr = elapsed.TotalDays >= 1
    ? $"{(int)elapsed.TotalDays}d {elapsed.Hours}h ago"
  : elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m ago"
   : $"{elapsed.Minutes}m ago";
     sb.AppendLine($"<b><color={gold}>Started:</color></b> <size={smallSize}><color={gray}>{elapsedStr}</color></size>");
     }
    }
      else if (isCompleted)
      {
      sb.AppendLine($"<b><color={gold}>Status:</color></b> <size={smallSize}><color={green}>Completed!</color></size>");
           }

     _questDescriptionText.text = sb.ToString();
       _questDescriptionText.fontSize = DESC_FONT_SIZE * scale;

     Canvas.ForceUpdateCanvases();
     if (_descriptionScrollRect != null) _descriptionScrollRect.verticalNormalizedPosition = 1f;
   }
    }

private void ClearQuestDetails()
        {
 if (_questNameText != null) _questNameText.text = "Select a Quest";
          if (_questDescriptionText != null) _questDescriptionText.text = "Click on a quest from the list to see details.";
        ClearPreviewImage();
    }

        #endregion

  #region Preview Image

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
            }

            foreach (var ext in extensions)
       {
        string imagePath = Path.Combine(RuntimeSpritesFolder, quest.QuestId + ext);
    if (File.Exists(imagePath)) { LoadTextureFromFile(imagePath); return; }
         }
}

   private void LoadTextureFromFile(string filePath)
        {
       try
            {
     if (_spriteCache.TryGetValue(filePath, out var cachedTexture))
                {
       ApplyPreviewTexture(cachedTexture);
         return;
     }

         byte[] fileData = File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
      bool loaded = false;

        var imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
       if (imageConversionType != null)
      {
       var loadImageMethod = imageConversionType.GetMethod("LoadImage",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null,
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
      if (loadImageMethod != null)
   loaded = (bool)loadImageMethod.Invoke(null, new object[] { texture, fileData, false });
  }

      if (!loaded)
        {
  try
      {
         var loadImageDirect = typeof(Texture2D).GetMethod("LoadImage",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null,
          new[] { typeof(byte[]) }, null);
              if (loadImageDirect != null)
               loaded = (bool)loadImageDirect.Invoke(texture, new object[] { fileData });
         }
         catch { }
       }

      if (loaded && texture.width > 8)
      {
          texture.name = Path.GetFileNameWithoutExtension(filePath);
        _spriteCache[filePath] = texture;
            ApplyPreviewTexture(texture);
            }
         else
          {
            UnityEngine.Object.Destroy(texture);
        }
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
            if (_previewRawImage != null)
  {
       _previewRawImage.texture = null;
             _previewRawImage.enabled = false;
        }
    if (_previewImageContainer != null)
            {
  var placeholder = _previewImageContainer.transform.Find("Placeholder");
      if (placeholder != null) placeholder.gameObject.SetActive(true);
          }
     }

        #endregion

    #region Button Actions

        private void UpdateButtonStates()
        {
    bool hasSelection = !string.IsNullOrEmpty(_selectedQuestId);
         bool isActiveQuest = hasSelection && !_showingCompleted && _activeQuests.Exists(q => q.QuestId == _selectedQuestId);

      if (_abandonButton != null)
          {
    _abandonButton.interactable = isActiveQuest;
            }
        }

     private void OnAbandonQuest()
        {
            if (string.IsNullOrEmpty(_selectedQuestId)) return;

            try
       {
          var localPlayer = Player.m_localPlayer;
 if (localPlayer == null) return;

      long playerId = localPlayer.GetPlayerID();
      if (playerId <= 0) return;

     // kg-parity: abandon is a local per-quest removal.
     global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Abandon(_selectedQuestId);

      var questDef = QuestManager.Instance?.GetQuestDefinition(_selectedQuestId);
      string questName = questDef?.Name ?? _selectedQuestId;
     MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest abandoned: {questName}");

   StartCoroutine(RefreshAfterDelay(0.3f));
         
            // Also refresh the QuestInteractionScreen if it's open
   NotifyQuestInteractionScreen();
      }
       catch (Exception ex)
  {
  Debug.LogError($"[QuestJournalController] Failed to abandon quest: {ex.Message}");
  }
   }

        /// <summary>
        /// Notifies the QuestInteractionScreenController to refresh if it's visible.
        /// This keeps both quest UIs in sync when quest state changes.
  /// </summary>
        private void NotifyQuestInteractionScreen()
        {
        }

        private System.Collections.IEnumerator RefreshAfterDelay(float delay)
   {
   yield return new WaitForSeconds(delay);
     LoadPlayerQuests();
    PopulateQuestList();
   _selectedQuestId = null;
            ClearQuestDetails();
   UpdateButtonStates();
   
    // Also notify the quest interaction screen after the delay
  NotifyQuestInteractionScreen();
   }

      #endregion
  }

 /// <summary>
    /// Helper component for handling Quest Journal drag events.
    /// </summary>
    public class JournalDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
   private QuestJournalController _controller;
 private float _lastClickTime;
     private const float DoubleClickTime = 0.3f;

  public void Initialize(QuestJournalController controller)
 {
      _controller = controller;
   }

 public void OnBeginDrag(PointerEventData eventData)
   {
         if (_controller != null)
        {
           _controller.OnDragStart(eventData.position);
            }
   }

        public void OnDrag(PointerEventData eventData)
     {
       if (_controller != null)
     {
 _controller.OnDrag(eventData.position);
   }
        }

   public void OnEndDrag(PointerEventData eventData)
     {
       if (_controller != null)
       {
   _controller.OnDragEnd();
 }
        }

        public void OnPointerClick(PointerEventData eventData)
  {
 if (Time.unscaledTime - _lastClickTime < DoubleClickTime)
   {
    _controller?.ToggleLock();
 _lastClickTime = 0f;
            }
  else
       {
      _lastClickTime = Time.unscaledTime;
     }
        }
    }

    /// <summary>
    /// Helper component for handling Quest Journal resize events.
    /// </summary>
    public class JournalResizeHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
 private QuestJournalController _controller;
        private float _lastClickTime;
      private const float DoubleClickTime = 0.3f;

    public void Initialize(QuestJournalController controller)
    {
            _controller = controller;
        }

  public void OnBeginDrag(PointerEventData eventData)
    {
        if (_controller != null)
  {
      _controller.OnResizeStart(eventData.position);
    }
    }

      public void OnDrag(PointerEventData eventData)
        {
            if (_controller != null)
          {
     _controller.OnResize(eventData.position);
  }
        }

    public void OnEndDrag(PointerEventData eventData)
   {
       if (_controller != null)
    {
      _controller.OnResizeEnd();
            }
    }

     public void OnPointerClick(PointerEventData eventData)
    {
       // Double-click to reset size
    if (Time.unscaledTime - _lastClickTime < DoubleClickTime)
    {
         _controller?.ResetSize();
  _lastClickTime = 0f;
       }
            else
{
      _lastClickTime = Time.unscaledTime;
        }
        }
    }
}
