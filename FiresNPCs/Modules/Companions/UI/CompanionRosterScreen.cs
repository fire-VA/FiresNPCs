using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.UI;
using FiresRPGmaker.Modules.Vault;
using FiresCore.Npc;
using FiresCore.Npc.Vault;
using FiresCore.Lifecycle;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Companions
{
    /// <summary>
    /// UI Screen for viewing all owned companions and managing them.
    /// Allows viewing stats of any companion and recalling lost companions.
    /// 
    /// Features:
    /// - List all player-owned companions from vault
    /// - Click companion name to view their stats
    /// - Recall button to teleport/respawn companion (costs 500 gold)
    /// - Back button to return to previous companion's inventory
    /// </summary>
    public class CompanionRosterScreen : MonoBehaviour
    {
        public static CompanionRosterScreen Instance { get; private set; }

        #region Constants

        private const float PANEL_WIDTH = 500f;
        private const float PANEL_HEIGHT = 600f;
    private const float HEADER_HEIGHT = 40f;
        private const float FOOTER_HEIGHT = 50f;
        private const float ROW_HEIGHT = 50f;
        private const int RECALL_COST = 20;

        private static readonly Color HeaderFooterColor = new Color(0.06f, 0.05f, 0.04f, 0.9f);
        private static readonly Color RowEvenColor = new Color(0.12f, 0.10f, 0.08f, 0.7f);
        private static readonly Color RowOddColor = new Color(0.15f, 0.12f, 0.10f, 0.7f);
     private static readonly Color RowHoverColor = new Color(0.25f, 0.20f, 0.15f, 0.9f);
        private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f);
        private static readonly Color MutedTextColor = new Color(0.7f, 0.6f, 0.4f);
     private static readonly Color GoldColor = new Color(1f, 0.85f, 0.5f);

     #endregion

        #region Fields

        private GameObject _screenRoot;
        private GameObject _backgroundPanel; // Separate background panel (larger than content)
        private TMP_Text _headerText;
        private Transform _contentParent;
        private ScrollRect _scrollRect;
        private List<CompanionRowUI> _rows = new List<CompanionRowUI>();

        private CompanionController _previousCompanion; // To return to
        private Player _currentPlayer;
        private CompanionRowUI _selectedRow;

        // Button refs for manual hit testing
        private RectTransform _closeButtonRect;
        private RectTransform _backButtonRect;

        private bool _isDragging;
        private Vector2 _dragOffset;
        private RectTransform _headerRect;

        #endregion

        #region Unity Lifecycle

 private void Awake()
        {
    Instance = this;
        }

 private void OnDestroy()
        {
      if (Instance == this) Instance = null;
        }

        private void Update()
        {
        if (_screenRoot == null || !_screenRoot.activeSelf) return;

          // Escape to close
 if (Input.GetKeyDown(KeyCode.Escape))
   {
          HideInternal();
     return;
         }

            // Handle clicks
          if (Input.GetMouseButtonDown(0))
   {
      HandleClick();
    }

// Panel dragging
    HandlePanelDrag();

            // Mouse wheel scroll
            HandleMouseWheelScroll();
  }

        #endregion

   #region Input Handling

        private void HandleClick()
        {
   Vector2 mousePos = Input.mousePosition;

      // Close button
            if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
       {
     HideInternal();
       return;
            }

     // Back button
    if (_backButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_backButtonRect, mousePos, null))
 {
           GoBack();
                return;
         }

     // Check row clicks
     foreach (var row in _rows)
            {
       if (row.RectTransform != null && RectTransformUtility.RectangleContainsScreenPoint(row.RectTransform, mousePos, null))
   {
               // Check if recall button was clicked
              if (row.RecallButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(row.RecallButtonRect, mousePos, null))
              {
     OnRecallClicked(row);
    return;
    }

           // Check if stats button was clicked
      if (row.StatsButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(row.StatsButtonRect, mousePos, null))
      {
          OnViewStatsClicked(row);
               return;
          }

               // Check if remove button was clicked
               if (row.RemoveButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(row.RemoveButtonRect, mousePos, null))
               {
                   OnRemoveClicked(row);
                   return;
               }

            // Row itself clicked - select it
            SelectRow(row);
     return;
       }
    }
        }

     private void HandlePanelDrag()
        {
         if (_headerRect == null) return;

       Vector2 mousePos = Input.mousePosition;

            if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_headerRect, mousePos, null))
     {
          if (_closeButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
    return;

         _isDragging = true;
          var panelRect = _screenRoot?.GetComponent<RectTransform>();
       if (panelRect != null)
      {
      _dragOffset = (Vector2)panelRect.position - mousePos;
        }
    }
            }

    if (_isDragging && Input.GetMouseButton(0))
   {
      var panelRect = _screenRoot?.GetComponent<RectTransform>();
                if (panelRect != null)
       {
        panelRect.position = mousePos + _dragOffset;
             }
            }

    if (_isDragging && Input.GetMouseButtonUp(0))
        {
     _isDragging = false;
    }
        }

        private void HandleMouseWheelScroll()
        {
            if (_scrollRect == null) return;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.01f) return;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                _scrollRect.verticalNormalizedPosition + scroll * 0.5f);
        }

#endregion

        #region Static API

        public static void Show(Player player, CompanionController returnToCompanion = null)
        {
            if (player == null) return;

     if (Instance == null)
          {
                CreateScreen();
     }

Instance?.ShowInternal(player, returnToCompanion);
        }

        public static void Hide()
        {
        Instance?.HideInternal();
        }

        public static bool IsVisible => Instance?._screenRoot?.activeSelf ?? false;

        #endregion

        #region Screen Creation

        private static void CreateScreen()
        {
            Canvas canvas = null;
            if (Hud.instance != null)
            {
                canvas = Hud.instance.GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                var canvasGO = new GameObject("CompanionRosterCanvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 101;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Create root container (invisible, just for positioning)
            var panelGO = new GameObject("CompanionRosterPanel");
            panelGO.transform.SetParent(canvas.transform, false);

            var panelRect = panelGO.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            panelRect.anchoredPosition = Vector2.zero;

            var canvasGroup = panelGO.AddComponent<CanvasGroup>();
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var controller = panelGO.AddComponent<CompanionRosterScreen>();
            controller._screenRoot = panelGO;

            // Create background panel FIRST (behind everything) - 15% larger for parchment border effect
            controller.CreateBackgroundPanel(panelGO.transform);
            
            // Create content panel with semi-transparent background
            controller.CreateContentPanel(panelGO.transform);

            panelGO.SetActive(false);
            Debug.Log("[CompanionRosterScreen] Created roster screen");
        }

        /// <summary>
        /// Creates the background panel with parchment sprite from asset bundle.
        /// This panel is larger than the content to show a border/frame effect.
        /// </summary>
        private void CreateBackgroundPanel(Transform parent)
        {
            _backgroundPanel = new GameObject("BackgroundPanel");
            _backgroundPanel.transform.SetParent(parent, false);
            _backgroundPanel.transform.SetAsFirstSibling(); // Put behind everything

            var bgRect = _backgroundPanel.AddComponent<RectTransform>();
            // Make it 15% larger than the root panel to show parchment border
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(PANEL_WIDTH * 1.15f, PANEL_HEIGHT * 1.10f);
            bgRect.anchoredPosition = Vector2.zero;

            // Try to load parchment background from asset bundle
            var bgSprite = VAMiscAssetManager.GetSprite("npcuibkg1");

            var bgImage = _backgroundPanel.AddComponent<Image>();
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
                bgImage.color = Color.white;
                Debug.Log("[CompanionRosterScreen] Loaded parchment background sprite: npcuibkg1");
            }
            else
            {
                // Fallback to solid color if sprite not found
                bgImage.color = new Color(0.85f, 0.75f, 0.6f, 1f); // Parchment color
                Debug.LogWarning("[CompanionRosterScreen] Could not load npcuibkg1 sprite, using fallback color");
            }
            bgImage.raycastTarget = true; // Needed for dragging
        }

        /// <summary>
        /// Creates the main content panel with header, content area, and footer.
        /// </summary>
        private void CreateContentPanel(Transform parent)
        {
            // Create content container that fills the root panel
            var contentPanel = new GameObject("ContentPanel");
            contentPanel.transform.SetParent(parent, false);

            var contentRect = contentPanel.AddComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            // Semi-transparent dark background for content area
            var contentBg = contentPanel.AddComponent<Image>();
            contentBg.color = UIFontConfig.Colors.PanelBackground;
            contentBg.raycastTarget = false;

            // Create UI elements
            CreateHeader(contentPanel.transform);
            CreateContent(contentPanel.transform);
            CreateFooter(contentPanel.transform);
        }

     private void CreateHeader(Transform parent)
     {
     var headerGO = new GameObject("Header");
   headerGO.transform.SetParent(parent, false);

  var headerRect = headerGO.AddComponent<RectTransform>();
  headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
          headerRect.anchoredPosition = new Vector2(0, -8);
  headerRect.sizeDelta = new Vector2(-16, HEADER_HEIGHT);

            var headerBg = headerGO.AddComponent<Image>();
            headerBg.color = HeaderFooterColor;
         headerBg.raycastTarget = true;

      _headerRect = headerRect;

            // Title
            var titleGO = new GameObject("Title");
      titleGO.transform.SetParent(headerGO.transform, false);

    var titleRect = titleGO.AddComponent<RectTransform>();
     titleRect.anchorMin = new Vector2(0.02f, 0);
            titleRect.anchorMax = new Vector2(0.85f, 1);
            titleRect.offsetMin = Vector2.zero;
   titleRect.offsetMax = Vector2.zero;

    _headerText = titleGO.AddComponent<TextMeshProUGUI>();
         _headerText.text = "My Companions";
         _headerText.fontSize = 18;
  _headerText.fontStyle = FontStyles.Bold;
            _headerText.color = GoldColor;
            _headerText.alignment = TextAlignmentOptions.MidlineLeft;
        UIBuilderHelper.ApplyValheimFont(_headerText);

 // Close button
     var closeBtn = CreateHeaderButton(headerGO.transform, "X", 0.88f, 0.98f);
   _closeButtonRect = closeBtn.GetComponent<RectTransform>();
}

        private void CreateContent(Transform parent)
    {
var contentGO = new GameObject("ContentArea");
      contentGO.transform.SetParent(parent, false);

 var contentRect = contentGO.AddComponent<RectTransform>();
       contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(12, FOOTER_HEIGHT + 16);
            contentRect.offsetMax = new Vector2(-12, -(HEADER_HEIGHT + 24));

            var contentBg = contentGO.AddComponent<Image>();
    contentBg.color = new Color(0.08f, 0.07f, 0.06f, 0.7f);

            // Scroll view
            var scrollGO = new GameObject("ScrollView");
  scrollGO.transform.SetParent(contentGO.transform, false);

    var scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(4, 4);
       scrollRect.offsetMax = new Vector2(-20, -4); // leave 16px on right for scrollbar

  _scrollRect = scrollGO.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
    _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 40f;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            // Scrollbar (right edge of contentGO, NOT inside scrollGO so it sits outside the viewport)
            var scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(contentGO.transform, false);
            var scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot     = new Vector2(1f, 0.5f);
            scrollbarRect.anchoredPosition = new Vector2(-4f, 0f);
            scrollbarRect.sizeDelta = new Vector2(12f, -8f);

            var scrollbarImg = scrollbarGO.AddComponent<Image>();
            scrollbarImg.color = new Color(0.08f, 0.06f, 0.04f, 0.8f);

            var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            // Sliding area
            var slidingAreaGO = new GameObject("SlidingArea");
            slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
            var slidingRect = slidingAreaGO.AddComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(2f, 2f);
            slidingRect.offsetMax = new Vector2(-2f, -2f);

            // Handle
            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slidingAreaGO.transform, false);
            var handleRect = handleGO.AddComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.45f, 0.2f, 0.85f);

            scrollbar.handleRect    = handleRect;
            scrollbar.targetGraphic = handleImg;

            _scrollRect.verticalScrollbar = scrollbar;

            // Viewport
        var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);

  var viewportRect = viewportGO.AddComponent<RectTransform>();
    viewportRect.anchorMin = Vector2.zero;
   viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

          var viewportMask = viewportGO.AddComponent<Mask>();
  viewportMask.showMaskGraphic = false;
    var viewportImg = viewportGO.AddComponent<Image>();
       viewportImg.color = Color.white;

    _scrollRect.viewport = viewportRect;

      // Content container
   var listContentGO = new GameObject("ListContent");
    listContentGO.transform.SetParent(viewportGO.transform, false);

        var listContentRect = listContentGO.AddComponent<RectTransform>();
          listContentRect.anchorMin = new Vector2(0, 1);
       listContentRect.anchorMax = new Vector2(1, 1);
            listContentRect.pivot = new Vector2(0.5f, 1);
       listContentRect.anchoredPosition = Vector2.zero;
            listContentRect.sizeDelta = new Vector2(0, 0);

            var verticalLayout = listContentGO.AddComponent<VerticalLayoutGroup>();
    verticalLayout.childAlignment = TextAnchor.UpperCenter;
    verticalLayout.childControlHeight = false;
      verticalLayout.childControlWidth = true;
    verticalLayout.childForceExpandHeight = false;
  verticalLayout.childForceExpandWidth = true;
 verticalLayout.spacing = 2;
verticalLayout.padding = new RectOffset(4, 4, 4, 4);

 _scrollRect.content = listContentRect;
            _contentParent = listContentGO.transform;
        }

        private void CreateFooter(Transform parent)
        {
            var footerGO = new GameObject("Footer");
          footerGO.transform.SetParent(parent, false);

            var footerRect = footerGO.AddComponent<RectTransform>();
    footerRect.anchorMin = new Vector2(0, 0);
   footerRect.anchorMax = new Vector2(1, 0);
            footerRect.pivot = new Vector2(0.5f, 0);
   footerRect.anchoredPosition = new Vector2(0, 8);
            footerRect.sizeDelta = new Vector2(-16, FOOTER_HEIGHT);

        var footerBg = footerGO.AddComponent<Image>();
 footerBg.color = HeaderFooterColor;

      // Back button
         var backBtn = CreateActionButton(footerGO.transform, "? Back", 0.05f, 0.35f);
            _backButtonRect = backBtn.GetComponent<RectTransform>();

        // Info text
            var infoGO = new GameObject("InfoText");
  infoGO.transform.SetParent(footerGO.transform, false);

            var infoRect = infoGO.AddComponent<RectTransform>();
       infoRect.anchorMin = new Vector2(0.4f, 0.15f);
      infoRect.anchorMax = new Vector2(0.95f, 0.85f);
  infoRect.offsetMin = Vector2.zero;
       infoRect.offsetMax = Vector2.zero;

      var infoText = infoGO.AddComponent<TextMeshProUGUI>();
            infoText.text = $"<color=#FFD700>Recall Cost:</color> {RECALL_COST} Gold";
     infoText.fontSize = 11;
   infoText.color = MutedTextColor;
            infoText.alignment = TextAlignmentOptions.MidlineRight;
 UIBuilderHelper.ApplyValheimFont(infoText);
        }

        #endregion

  #region Row Creation

 private void PopulateCompanionList()
        {
            // Clear ALL existing children of the content parent. _rows only tracks
            // companion rows Ã¢â‚¬â€ the empty-state message ("No companions found") added
            // by CreateEmptyMessage isn't in _rows, so previously it lingered when
            // companions appeared on a refresh and rendered alongside the rows.
            if (_contentParent != null)
            {
                for (int i = _contentParent.childCount - 1; i >= 0; i--)
                {
                    var child = _contentParent.GetChild(i);
                    if (child != null) Destroy(child.gameObject);
                }
            }
            _rows.Clear();

     if (_currentPlayer == null) return;

            long playerId = _currentPlayer.GetPlayerID();

            // Phase 6: read via the resolver (roster + Dismissed entries
            // included), with vault fallback for migration.
            var companions = CompanionSavedDataResolver.ResolveAllForRosterUI(playerId);
            if (companions == null || companions.Count == 0)
            {
                CreateEmptyMessage();
      return;
            }

            // Also find any companions currently in the world
            // Use a dictionary with manual add to handle duplicates gracefully
            // (multiple instances of the same companion can exist during respawn)
            var worldCompanions = new Dictionary<string, CompanionController>();
            foreach (var c in FindObjectsByType<CompanionController>(FindObjectsSortMode.None))
            {
                if (c == null || !c.isTamed || c.ownerPlayerId != playerId) continue;
                if (string.IsNullOrEmpty(c.companionId)) continue;
                // Keep the first instance found (or prefer alive over dead)
                if (!worldCompanions.ContainsKey(c.companionId))
                {
                    worldCompanions[c.companionId] = c;
                }
            }

            int index = 0;
  foreach (var data in companions)
            {
          if (data == null) continue;

     // Check if companion exists in world
         CompanionController worldComp = null;
     if (!string.IsNullOrEmpty(data.CompanionId) && worldCompanions.TryGetValue(data.CompanionId, out var wc))
      {
  worldComp = wc;
                }

        CreateCompanionRow(data, worldComp, index);
      index++;
    }

            // Update content size and force layout rebuild so ScrollRect sees the correct height
    var contentRect = _contentParent.GetComponent<RectTransform>();
       if (contentRect != null)
   {
         contentRect.sizeDelta = new Vector2(0, _rows.Count * (ROW_HEIGHT + 2) + 8);
             LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }

            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
      }

private void CreateEmptyMessage()
     {
            var msgGO = new GameObject("EmptyMessage");
        msgGO.transform.SetParent(_contentParent, false);

            var msgRect = msgGO.AddComponent<RectTransform>();
        msgRect.sizeDelta = new Vector2(0, 100);

         var msgText = msgGO.AddComponent<TextMeshProUGUI>();
            msgText.text = "No companions found.\n\nTame a companion by giving them the required items!";
   msgText.fontSize = 14;
   msgText.color = MutedTextColor;
        msgText.alignment = TextAlignmentOptions.Center;
       UIBuilderHelper.ApplyValheimFont(msgText);
  }

        private void CreateCompanionRow(CompanionSaveData data, CompanionController worldComp, int index)
        {
        var rowGO = new GameObject($"CompanionRow_{index}");
            rowGO.transform.SetParent(_contentParent, false);

    var rowRect = rowGO.AddComponent<RectTransform>();
     rowRect.sizeDelta = new Vector2(0, ROW_HEIGHT);

            var rowBg = rowGO.AddComponent<Image>();
            rowBg.color = index % 2 == 0 ? RowEvenColor : RowOddColor;
    rowBg.raycastTarget = true;

            var layoutGroup = rowGO.AddComponent<HorizontalLayoutGroup>();
          layoutGroup.childAlignment = TextAnchor.MiddleLeft;
    layoutGroup.childControlHeight = true;
    layoutGroup.childControlWidth = false;
            layoutGroup.childForceExpandHeight = true;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.spacing = 8;
    layoutGroup.padding = new RectOffset(8, 8, 4, 4);

            // Name
     var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(rowGO.transform, false);

     var nameRect = nameGO.AddComponent<RectTransform>();
      nameRect.sizeDelta = new Vector2(150, 0);

          var nameLayout = nameGO.AddComponent<LayoutElement>();
    nameLayout.preferredWidth = 150;
   nameLayout.flexibleWidth = 1;

            var nameText = nameGO.AddComponent<TextMeshProUGUI>();
  string displayName = !string.IsNullOrEmpty(data.DisplayNameOverride) ? data.DisplayNameOverride : data.CompanionName;
   nameText.text = displayName ?? "Unknown";
        nameText.fontSize = 14;
    nameText.fontStyle = FontStyles.Bold;
   nameText.color = TextColor;
  nameText.alignment = TextAlignmentOptions.MidlineLeft;
       UIBuilderHelper.ApplyValheimFont(nameText);

 // Status
            var statusGO = new GameObject("Status");
  statusGO.transform.SetParent(rowGO.transform, false);

        var statusRect = statusGO.AddComponent<RectTransform>();
    statusRect.sizeDelta = new Vector2(80, 0);

            var statusLayout = statusGO.AddComponent<LayoutElement>();
    statusLayout.preferredWidth = 80;

            var statusText = statusGO.AddComponent<TextMeshProUGUI>();
          bool isInWorld = worldComp != null;
            statusText.text = isInWorld ? "<color=#00FF00>Active</color>" : "<color=#808080>Away</color>";
       statusText.fontSize = 11;
            statusText.color = TextColor;
            statusText.alignment = TextAlignmentOptions.Center;
            UIBuilderHelper.ApplyValheimFont(statusText);

            // Stats button
            var statsBtn = CreateRowButton(rowGO.transform, "Stats", 60);
      var statsBtnRect = statsBtn.GetComponent<RectTransform>();

            // Recall button
         var recallBtn = CreateRowButton(rowGO.transform, "Recall", 70);
       var recallBtnRect = recallBtn.GetComponent<RectTransform>();

            // Remove button (with red tint)
            var removeBtn = CreateRowButton(rowGO.transform, "Remove", 70);
            var removeBtnRect = removeBtn.GetComponent<RectTransform>();
            var removeBtnBg = removeBtn.GetComponent<Image>();
            if (removeBtnBg != null)
            {
                removeBtnBg.color = new Color(0.5f, 0.2f, 0.2f, 0.9f); // Dark red
            }

  // Store row data
 var rowUI = new CompanionRowUI
            {
        Root = rowGO,
        RectTransform = rowRect,
     Background = rowBg,
   NameText = nameText,
            StatusText = statusText,
    StatsButtonRect = statsBtnRect,
   RecallButtonRect = recallBtnRect,
   RemoveButtonRect = removeBtnRect,
 CompanionData = data,
       WorldCompanion = worldComp,
    BaseColor = rowBg.color
    };

    // Add hover handler
   var hoverHandler = rowGO.AddComponent<RowHoverHandler>();
 hoverHandler.Initialize(rowUI);

 _rows.Add(rowUI);
        }

        private GameObject CreateRowButton(Transform parent, string text, float width)
        {
    var btnGO = new GameObject($"Btn_{text}");
            btnGO.transform.SetParent(parent, false);

            var btnRect = btnGO.AddComponent<RectTransform>();
       btnRect.sizeDelta = new Vector2(width, 0);

            var btnLayout = btnGO.AddComponent<LayoutElement>();
            btnLayout.preferredWidth = width;
            btnLayout.minWidth = width;

      var btnBg = btnGO.AddComponent<Image>();
    btnBg.color = UIFontConfig.Colors.ButtonNormal;
    btnBg.raycastTarget = true;

            var btnText = new GameObject("Text");
            btnText.transform.SetParent(btnGO.transform, false);

            var textRect = btnText.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
      textRect.anchorMax = Vector2.one;
textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = btnText.AddComponent<TextMeshProUGUI>();
       tmp.text = text;
            tmp.fontSize = 11;
            tmp.fontStyle = FontStyles.Bold;
 tmp.color = TextColor;
   tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
  UIBuilderHelper.ApplyValheimFont(tmp);

       return btnGO;
     }

        #endregion

    #region Button Handlers

      private void SelectRow(CompanionRowUI row)
{
  if (_selectedRow != null)
            {
        _selectedRow.Background.color = _selectedRow.BaseColor;
      }

        _selectedRow = row;
    if (_selectedRow != null)
            {
  _selectedRow.Background.color = RowHoverColor;
     }
     }

        private void OnViewStatsClicked(CompanionRowUI row)
    {
          if (row?.WorldCompanion != null)
  {
         // Companion is in world - open their inventory/stats screen
              HideInternal();
      CompanionInventoryScreen.Show(row.WorldCompanion, _currentPlayer);
  }
  else if (row?.CompanionData != null)
 {
              // Companion not in world - show message
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
          $"{row.CompanionData.CompanionName} is not nearby. Use Recall to summon them first.");
    }
   }

        private void OnRecallClicked(CompanionRowUI row)
        {
 if (row?.CompanionData == null || _currentPlayer == null) return;

   // Check gold
       var inventory = _currentPlayer.GetInventory();
     if (inventory == null) return;

int goldCount = inventory.CountItems("$item_coins");
            if (goldCount < RECALL_COST)
  {
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
          $"Not enough gold! Need {RECALL_COST}, have {goldCount}.");
      return;
            }

            // Deduct gold
      inventory.RemoveItem("$item_coins", RECALL_COST);

        // Perform recall
    RecallCompanion(row);
        }

        private void OnRemoveClicked(CompanionRowUI row)
        {
            if (row?.CompanionData == null || _currentPlayer == null) return;

            string companionName = row.CompanionData.DisplayNameOverride ?? row.CompanionData.CompanionName ?? "this companion";
            string companionId = row.CompanionData.CompanionId;

            // Require shift+click to confirm removal (to prevent accidents)
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            
            if (!shiftHeld && !ctrlHeld)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"Hold SHIFT and click Remove to permanently dismiss {companionName}\n(Hold CTRL+SHIFT for force purge)");
                return;
            }

            long playerId = _currentPlayer.GetPlayerID();
            
            // FORCE PURGE: Ctrl+Shift completely removes all traces of the companion
            bool forcePurge = shiftHeld && ctrlHeld;
            
            Debug.Log($"[CompanionRosterScreen] Removing companion {companionName} (ID: {companionId}), forcePurge: {forcePurge}");

            // Step 1: Cancel any pending respawn for this companion first
            try
            {
                if (!string.IsNullOrEmpty(companionId))
                {
                    CompanionRespawnManager.Instance?.CancelRespawn(companionId);
                    Debug.Log($"[CompanionRosterScreen] Cancelled any pending respawn for {companionName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionRosterScreen] Failed to cancel respawn: {ex.Message}");
            }

            // Step 2: Find and destroy ALL instances of this companion in the world
            int destroyedCount = 0;
            try
            {
                var allCompanions = FindObjectsByType<CompanionController>(FindObjectsSortMode.None);
                
                foreach (var comp in allCompanions)
                {
                    if (comp == null) continue;
                    
                    // Match by companionId OR by name if ID is missing
                    bool matches = false;
                    if (!string.IsNullOrEmpty(companionId) && comp.companionId == companionId)
                    {
                        matches = true;
                    }
                    else if (string.IsNullOrEmpty(companionId) && 
                             comp.ownerPlayerId == playerId &&
                             (comp.companionName == row.CompanionData.CompanionName || 
                              comp.displayNameOverride == row.CompanionData.DisplayNameOverride))
                    {
                        matches = true;
                    }
                    
                    if (matches)
                    {
                        try
                        {
                            // Clear from AllCompanions list first
                            Debug.Log($"[CompanionRosterScreen] Destroying world instance of {companionName}");

                            CompanionNetworkHelper.Destroy(comp.gameObject);
                            destroyedCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CompanionRosterScreen] Failed to destroy companion object: {ex.Message}");
                        }
                    }
                }
                
                if (destroyedCount > 0)
                {
                    Debug.Log($"[CompanionRosterScreen] Destroyed {destroyedCount} world instance(s) of {companionName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionRosterScreen] Error searching for world companions: {ex.Message}");
            }

            // Step 3: Remove from vault (sends RPC to server if on client)
            try
            {
                VaultOfKnowledge.Instance?.RemoveCompanion(playerId, companionId);
                Debug.Log($"[CompanionRosterScreen] Removed companion {companionName} (ID: {companionId}) from vault");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CompanionRosterScreen] Failed to remove companion from vault: {ex.Message}");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    "Failed to remove companion. Check console for details.");
                return;
            }

            // Step 3b: Remove from the per-player roster (Phase 3 of save
            // refactor). This is the ONE site that calls RemoveEntry Ã¯Â¿Â½
            // every other path (Dismiss, death, logout) preserves the
            // entry. The Remove button is the explicit "wipe this companion
            // permanently" command.
            try
            {
                if (_currentPlayer != null)
                {
                    CompanionRosterWriter.OnRosterRemove(_currentPlayer, companionId);
                    Debug.Log($"[CompanionRosterScreen] Removed companion {companionName} (ID: {companionId}) from player roster");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionRosterScreen] Roster RemoveEntry failed (non-fatal): {ex.Message}");
            }

            // Step 4: Force purge - also update local player's custom data directly
            if (forcePurge)
            {
                try
                {
                    // Clear from player's custom data directly to ensure it's gone locally
                    if (_currentPlayer.m_customData.TryGetValue("FiresRPGmaker_Vault", out string vaultJson))
                    {
                        try
                        {
                            var vaultData = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerVaultData>(vaultJson);
                            if (vaultData?.Companions != null)
                            {
                                int removed = 0;
                                for (int i = vaultData.Companions.Count - 1; i >= 0; i--)
                                {
                                    try
                                    {
                                        var compJson = Newtonsoft.Json.JsonConvert.SerializeObject(vaultData.Companions[i]);
                                        var compData = Newtonsoft.Json.JsonConvert.DeserializeObject<CompanionSaveData>(compJson);
                                        if (compData?.CompanionId == companionId)
                                        {
                                            vaultData.Companions.RemoveAt(i);
                                            removed++;
                                        }
                                    }
                                    catch { }
                                }
                                
                                if (removed > 0)
                                {
                                    var capturedVault = vaultData;
                                    int capturedRemoved = removed;
                                    PlayerSpawnGate.RunWhenReady(_currentPlayer, 10f, p =>
                                    {
                                        try
                                        {
                                            p.m_customData["FiresRPGmaker_Vault"] = Newtonsoft.Json.JsonConvert.SerializeObject(capturedVault);
                                            p.m_customData["FiresRPGmaker_Dirty"] = "1";
                                            Debug.Log($"[CompanionRosterScreen] Force-purged {capturedRemoved} companion entry from player custom data");
                                        }
                                        catch { }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CompanionRosterScreen] Error during force purge of custom data: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CompanionRosterScreen] Force purge failed: {ex.Message}");
                }
            }
            else
            {
                // Even without force purge, mark data as dirty so it syncs
                PlayerSpawnGate.SafeSetCustomData(_currentPlayer, "FiresRPGmaker_Dirty", "1", 10f);
            }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                forcePurge ? $"{companionName} has been permanently purged." : $"{companionName} has been dismissed.");

            // Refresh the list immediately
            PopulateCompanionList();
        }

        private void RecallCompanion(CompanionRowUI row)
        {
            if (row?.CompanionData == null || _currentPlayer == null) return;

            string companionId = row.CompanionData.CompanionId;
            string companionName = row.CompanionData.DisplayNameOverride ?? row.CompanionData.CompanionName;

            Debug.Log($"[CompanionRosterScreen] Recalling companion {companionName} (ID: {companionId})");

            // ROSTER MIRROR: no pre-spawn intent flip needed Ã¯Â¿Â½ the
            // post-respawn OnRespawned write auto-promotes Dismissed
            // ? Following. Recall is just a respawn from the player's
            // perspective; the writer encodes the rule.

            // Step 1: Cancel any pending respawn for this companion
            try
            {
                if (!string.IsNullOrEmpty(companionId))
                {
                    CompanionRespawnManager.Instance?.CancelRespawn(companionId);
                    Debug.Log($"[CompanionRosterScreen] Cancelled any pending respawn for {companionName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionRosterScreen] Failed to cancel respawn: {ex.Message}");
            }

            // Step 2: Find any existing instances of this companion in the world
            CompanionController existingCompanion = null;
            if (!string.IsNullOrEmpty(companionId))
            {
                var allCompanions = FindObjectsByType<CompanionController>(FindObjectsSortMode.None);
                foreach (var comp in allCompanions)
                {
                    if (comp != null && comp.companionId == companionId)
                    {
                        existingCompanion = comp;
                        break;
                    }
                }
            }

            Vector3 spawnPos = GetSpawnPositionNearPlayer(_currentPlayer);

            // Step 3: Handle existing companion or spawn new one
            if (existingCompanion != null)
            {
                // Check if companion is dead/defeated - if so, destroy and spawn fresh
                bool isDead = existingCompanion.isDefeated || 
                              (existingCompanion.GetCharacter()?.IsDead() ?? false);

                if (isDead)
                {
                    Debug.Log($"[CompanionRosterScreen] Companion {companionName} is dead - destroying and respawning fresh");

                    // Destroy the dead companion
                    CompanionNetworkHelper.Destroy(existingCompanion.gameObject);

                    // Use CompanionRespawnManager to spawn - it has all the proper restoration logic
                    CompanionRespawnManager.RequestImmediateRespawn(companionId, spawnPos);
                    
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                        $"{companionName} has been resurrected and recalled!");
                }
                else
                {
                    // Companion is alive - just teleport them
                    existingCompanion.transform.position = spawnPos;
                    
                    // Reset any defeated state just in case
                    existingCompanion.isDefeated = false;
                    
                    // Force them to follow
                    existingCompanion.CommandFollow(_currentPlayer);
                    
                    // Restore their stats if they're low
                    var stats = existingCompanion.GetStats();
                    if (stats != null)
                    {
                        stats.RestoreAllStats();
                    }
                    
                    // Save state
                    existingCompanion.SaveToZDO();

                    // ROSTER MIRROR: live companion in hand Ã¯Â¿Â½ do the full
                    // capture+upsert so the snapshot reflects post-recall
                    // state (e.g. restored stats above).
                    try
                    {
                        CompanionRosterWriter.OnRecalled(_currentPlayer, existingCompanion);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CompanionRosterScreen] Roster OnRecalled failed (non-fatal): {ex.Message}");
                    }

                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                        $"{companionName} has been teleported to you!");

                    Debug.Log($"[CompanionRosterScreen] Teleported companion {companionName} to player");
                }
            }
            else
            {
                // Companion not in world - use CompanionRespawnManager to spawn with full vault restoration
                CompanionRespawnManager.RequestImmediateRespawn(companionId, spawnPos);

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                    $"{companionName} has been recalled!");

                Debug.Log($"[CompanionRosterScreen] Requested respawn for companion {companionName} via CompanionRespawnManager");
            }

            // Clear pending respawn state in the row's local copy. Roster
            // authority is updated by CompanionRosterWriter.OnRespawned
            // when the actual respawn finishes; this is just a UI-state
            // cleanup for the row-data we still hold a reference to.
            // Phase 6: no per-event vault write Ã¯Â¿Â½ the periodic
            // FlushDebugMirror handles the on-disk mirror.
            try
            {
                if (row.CompanionData.IsPendingRespawn)
                {
                    row.CompanionData.IsPendingRespawn = false;
                    row.CompanionData.RespawnTimeRemaining = 0;
                    row.CompanionData.DeathTimestamp = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompanionRosterScreen] Failed to clear respawn state: {ex.Message}");
            }

            // Refresh the list
            PopulateCompanionList();
        }

        private Vector3 GetSpawnPositionNearPlayer(Player player)
        {
            Vector3 basePos = player.transform.position;
      Vector3 forward = player.transform.forward;

        // Try positions in a circle around the player
         for (int i = 0; i < 8; i++)
     {
float angle = i * 45f;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * forward;
 Vector3 testPos = basePos + direction * 3f;

  if (ZoneSystem.instance != null)
   {
      if (ZoneSystem.instance.GetGroundHeight(testPos, out float groundHeight))
  {
    testPos.y = groundHeight + 0.5f;
       return testPos;
                    }
        }
   }

 return basePos + forward * 3f + Vector3.up * 0.5f;
        }

        private void GoBack()
   {
            HideInternal();

          if (_previousCompanion != null && _currentPlayer != null)
   {
           CompanionInventoryScreen.Show(_previousCompanion, _currentPlayer);
        }
        }

        #endregion

        #region Show/Hide

        private void ShowInternal(Player player, CompanionController returnTo)
        {
            _currentPlayer = player;
            _previousCompanion = returnTo;
            _selectedRow = null;

            PopulateCompanionList();

            _screenRoot.SetActive(true);
            _screenRoot.transform.SetAsLastSibling();

            // Block game input and unlock cursor for UI interaction
            GUIManager.BlockInput(true);
            
            // Unlock cursor directly - this is what Quest Journal does
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Debug.Log("[CompanionRosterScreen] Opened");
        }

        private void HideInternal()
        {
            _screenRoot?.SetActive(false);
            
            _currentPlayer = null;
            _previousCompanion = null;
            _selectedRow = null;

            // Unblock game input - cursor will be re-locked by game naturally
            GUIManager.BlockInput(false);

            Debug.Log("[CompanionRosterScreen] Hidden");
        }

        #endregion

        #region UI Helpers

  private Button CreateHeaderButton(Transform parent, string text, float xMin, float xMax)
      {
      var btnGO = new GameObject($"Btn_{text}");
   btnGO.transform.SetParent(parent, false);

            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(xMin, 0.12f);
            btnRect.anchorMax = new Vector2(xMax, 0.88f);
            btnRect.offsetMin = Vector2.zero;
            btnRect.offsetMax = Vector2.zero;

            var btnBg = btnGO.AddComponent<Image>();
 btnBg.color = UIFontConfig.Colors.ButtonNormal;
       btnBg.raycastTarget = true;

    var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnBg;

    var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
         textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
          textRect.offsetMin = Vector2.zero;
   textRect.offsetMax = Vector2.zero;

     var btnText = textGO.AddComponent<TextMeshProUGUI>();
      btnText.text = text;
       btnText.fontSize = 14;
  btnText.fontStyle = FontStyles.Bold;
         btnText.color = TextColor;
            btnText.alignment = TextAlignmentOptions.Center;
       btnText.raycastTarget = false;
         UIBuilderHelper.ApplyValheimFont(btnText);

         return btn;
        }

        private Button CreateActionButton(Transform parent, string text, float xMin, float xMax)
        {
      var btnGO = new GameObject($"Btn_{text.Replace(" ", "")}");
            btnGO.transform.SetParent(parent, false);

      var btnRect = btnGO.AddComponent<RectTransform>();
 btnRect.anchorMin = new Vector2(xMin, 0.15f);
            btnRect.anchorMax = new Vector2(xMax, 0.85f);
       btnRect.offsetMin = Vector2.zero;
            btnRect.offsetMax = Vector2.zero;

          var btnBg = btnGO.AddComponent<Image>();
            btnBg.color = UIFontConfig.Colors.ButtonNormal;
btnBg.raycastTarget = true;

 var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnBg;

        var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
 textRect.anchorMin = Vector2.zero;
textRect.anchorMax = Vector2.one;
 textRect.offsetMin = Vector2.zero;
  textRect.offsetMax = Vector2.zero;

            var btnText = textGO.AddComponent<TextMeshProUGUI>();
         btnText.text = text;
            btnText.fontSize = 12;
            btnText.fontStyle = FontStyles.Bold;
 btnText.color = TextColor;
  btnText.alignment = TextAlignmentOptions.Center;
            btnText.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(btnText);

            return btn;
    }

        #endregion

        #region Helper Types

  private class CompanionRowUI
        {
   public GameObject Root;
   public RectTransform RectTransform;
      public Image Background;
         public TMP_Text NameText;
            public TMP_Text StatusText;
 public RectTransform StatsButtonRect;
 public RectTransform RecallButtonRect;
 public RectTransform RemoveButtonRect;
         public CompanionSaveData CompanionData;
     public CompanionController WorldCompanion;
            public Color BaseColor;
        }

    private class RowHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
  private CompanionRowUI _row;

      public void Initialize(CompanionRowUI row) => _row = row;

      public void OnPointerEnter(PointerEventData eventData)
  {
                if (_row?.Background != null)
        _row.Background.color = RowHoverColor;
            }

          public void OnPointerExit(PointerEventData eventData)
  {
         if (_row?.Background != null)
          _row.Background.color = _row.BaseColor;
        }
        }

        #endregion
    }
}
