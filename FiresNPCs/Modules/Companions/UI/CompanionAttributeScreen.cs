using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using FiresRPGmaker.UI;
using FiresRPGmaker.Managers;
using FiresCore.Npc;
using FiresCore.Npc.Vault;

namespace FiresRPGmaker.Modules.Companions
{
    /// <summary>
    /// Companion Attribute Screen - manages companion leveling and attribute point spending.
    /// 
    /// FEATURES:
    /// - Shows companion level and XP progress
    /// - Displays current attributes with their bonuses
    /// - Allows spending unspent attribute points
    /// - Shows stat bars for health, stamina, eitr
    /// - Reset attributes option
    /// 
    /// DESIGN:
    /// - Uses UIBuilderHelper for consistent styling
    /// - Follows same patterns as CompanionInventoryScreen
    /// - Modal popup that can be opened from stats screen
    /// </summary>
    public class CompanionAttributeScreen : MonoBehaviour
    {
        #region Constants
        
        private const float PANEL_WIDTH = 400f;
        private const float PANEL_HEIGHT = 500f;
        
        // Colors matching the companion UI theme
        private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f);
        private static readonly Color LabelColor = new Color(0.9f, 0.75f, 0.45f);
        private static readonly Color ValueColor = new Color(0.6f, 1f, 0.6f);
        private static readonly Color BonusColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color UnspentColor = new Color(1f, 0.9f, 0.3f);
        private static readonly Color HeaderColor = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        private static readonly Color RowEvenColor = new Color(0.08f, 0.07f, 0.06f, 0.5f);
        private static readonly Color RowOddColor = new Color(0.10f, 0.09f, 0.08f, 0.5f);
        
        // Stat bar colors
        private static readonly Color HealthBarColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color StaminaBarColor = new Color(0.9f, 0.8f, 0.2f, 1f);
        private static readonly Color EitrBarColor = new Color(0.3f, 0.5f, 0.9f, 1f);
        private static readonly Color XpBarColor = new Color(0.6f, 0.4f, 0.8f, 1f);
        
        #endregion
        
        #region Fields
        
        private static CompanionAttributeScreen _instance;
        public static CompanionAttributeScreen Instance => _instance;
        
        private GameObject _root;
        private GameObject _backgroundPanel; // Background image panel
        private CompanionController _companion;
        
        // Header
        private TMP_Text _headerText;
        private TMP_Text _levelText;
        private Image _xpBarFill;
        private TMP_Text _xpText;
        
        // Unspent points
        private TMP_Text _unspentPointsText;
        
        // Attribute rows
        private AttributeRowUI[] _attributeRows = new AttributeRowUI[5];
        
        // Stat bars
        private Image _healthBarFill;
        private Image _staminaBarFill;
        private Image _eitrBarFill;
        private TMP_Text _healthText;
        private TMP_Text _staminaText;
        private TMP_Text _eitrText;
        
        // Buttons
        private Button _resetButton;
        private Button _closeButton;
        
        // RectTransforms for manual click detection (InventoryGui blocks pointer events)
        private RectTransform _closeButtonRect;
        private RectTransform _resetButtonRect;
        private RectTransform _doneButtonRect;
        private RectTransform[] _increaseButtonRects = new RectTransform[5];
        
        // Panel dragging
        private RectTransform _headerRect;
        private bool _isDragging;
        private Vector2 _dragOffset;
        
        #endregion
        
        #region Attribute Row Helper
        
        private class AttributeRowUI
        {
            public GameObject Root;
            public TMP_Text NameText;
            public TMP_Text ValueText;
            public TMP_Text BonusText;
            public Button IncreaseButton;
            public CompanionProgression.AttributeType AttributeType;
        }
        
        #endregion
        
        #region Public API
        
        public static bool IsVisible => _instance != null && _instance._root != null && _instance._root.activeSelf;
        
        /// <summary>
        /// Shows the attribute screen for a companion.
        /// </summary>
        public static void Show(CompanionController companion)
        {
            if (companion == null) return;
            
            if (_instance == null)
            {
                CreateScreen();
            }
            
            // Ensure stats are recalculated with latest food bonuses before showing
            companion.GetStats()?.RecalculateMaxStats();
            
            _instance._companion = companion;
            _instance._root.SetActive(true);
            _instance.RefreshUI();
            
            // IMPORTANT: Keep input blocked - don't toggle it
            // This allows mouse cursor to work on the attributes screen
            // The inventory screen already has input blocked when it's open
            if (!GUIManager.IsInputBlocked)
            {
                GUIManager.BlockInput(true);
            }
        }
        
        /// <summary>
        /// Hides the attribute screen.
        /// </summary>
        public static void Hide()
        {
            if (_instance == null) return;
            
            _instance._root?.SetActive(false);
            _instance._companion = null;
            
            // Unblock game input
            GUIManager.BlockInput(false);
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _instance = this;
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
        
        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;
            
            // Close on Escape
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }
            
            // Manual button click detection - InventoryGui blocks normal pointer events
            if (Input.GetMouseButtonDown(0))
            {
                HandleManualButtonClick();
            }
            
            // Handle panel dragging
            HandlePanelDrag();
            
            // Refresh UI periodically for live updates
            RefreshStatBars();
        }
        
        /// <summary>
        /// Manually checks if any buttons were clicked.
        /// This bypasses Unity's event system which is blocked by InventoryGui.
        /// </summary>
        private void HandleManualButtonClick()
        {
            Vector2 mousePos = Input.mousePosition;
            
            // Check close button (X)
            if (_closeButtonRect != null && 
                RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
            {
                Hide();
                return;
            }
            
            // Check done button
            if (_doneButtonRect != null && 
                RectTransformUtility.RectangleContainsScreenPoint(_doneButtonRect, mousePos, null))
            {
                Hide();
                return;
            }
            
            // Check reset button
            if (_resetButtonRect != null && 
                RectTransformUtility.RectangleContainsScreenPoint(_resetButtonRect, mousePos, null))
            {
                OnResetClicked();
                return;
            }
            
            // Check attribute increase buttons
            for (int i = 0; i < _increaseButtonRects.Length; i++)
            {
                if (_increaseButtonRects[i] != null && 
                    RectTransformUtility.RectangleContainsScreenPoint(_increaseButtonRects[i], mousePos, null))
                {
                    if (_attributeRows[i] != null && _attributeRows[i].IncreaseButton.interactable)
                    {
                        OnIncreaseAttribute(_attributeRows[i].AttributeType);
                    }
                    return;
                }
            }
        }
        
        /// <summary>
        /// Handles panel dragging via the header area.
        /// </summary>
        private void HandlePanelDrag()
        {
            if (_headerRect == null) return;
            
            Vector2 mousePos = Input.mousePosition;
            
            // Start drag on mouse down over header (but not over close button)
            if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_headerRect, mousePos, null))
                {
                    // Make sure we're not over the close button
                    if (_closeButtonRect != null && 
                        RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, mousePos, null))
                        return;
                    
                    // Start dragging
                    _isDragging = true;
                    var panelRect = _root.GetComponent<RectTransform>();
                    if (panelRect != null)
                    {
                        _dragOffset = (Vector2)panelRect.position - mousePos;
                    }
                }
            }
            
            // Continue drag
            if (_isDragging && Input.GetMouseButton(0))
            {
                var panelRect = _root.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.position = mousePos + _dragOffset;
                }
            }
            
            // End drag
            if (_isDragging && Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
            }
        }
        
        #endregion
        
        #region Screen Creation
        
        private static void CreateScreen()
        {
            // Find or create canvas
            Canvas canvas = null;
            if (Hud.instance != null)
            {
                canvas = Hud.instance.GetComponentInParent<Canvas>();
            }
            
            if (canvas == null)
            {
                var canvasGO = new GameObject("CompanionAttributeCanvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 105; // Above inventory screen
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            
            // Create root
            var rootGO = new GameObject("CompanionAttributeScreen");
            rootGO.transform.SetParent(canvas.transform, false);
            
            var rootRect = rootGO.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
            rootRect.anchoredPosition = Vector2.zero;
            
            _instance = rootGO.AddComponent<CompanionAttributeScreen>();
            _instance._root = rootGO;
            
            // Create background panel first (behind everything else)
            _instance.CreateBackgroundPanel(rootGO.transform);
            
            // Then create content on top
            _instance.CreateContent(rootGO.transform);
            
            rootGO.SetActive(false);
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
            // Make it 10% larger than the root panel to show parchment border
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(PANEL_WIDTH * 1.15f, PANEL_HEIGHT * 1.15f);
            bgRect.anchoredPosition = Vector2.zero;
            
            // Try to load sprite from asset bundle
            var bgSprite = VAMiscAssetManager.GetSprite("npcuibkg1");
            
            var bgImage = _backgroundPanel.AddComponent<Image>();
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
                bgImage.color = Color.white;
                Debug.Log("[CompanionAttributeScreen] Loaded parchment background sprite: npcuibkg1");
            }
            else
            {
                // Fallback to solid color if sprite not found
                bgImage.color = new Color(0.85f, 0.75f, 0.6f, 1f); // Parchment color
                Debug.LogWarning("[CompanionAttributeScreen] Could not load npcuibkg1 sprite, using fallback color");
            }
            bgImage.raycastTarget = true; // Needed for dragging
            
            // Add drag handler to background
            var dragHandler = _backgroundPanel.AddComponent<AttributePanelDragHandler>();
            dragHandler.Initialize(this);
        }
        
        private void CreateContent(Transform parent)
        {
            // Create main content panel with semi-transparent background
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
            contentBg.raycastTarget = false; // Don't intercept clicks - let background handle dragging
            
            // Create UI elements as children of content panel
            CreateHeader(contentPanel.transform);
            CreateLevelSection(contentPanel.transform);
            CreateUnspentSection(contentPanel.transform);
            CreateAttributeRows(contentPanel.transform);
            CreateStatBars(contentPanel.transform);
            CreateButtons(contentPanel.transform);
        }
        
        private void CreateHeader(Transform parent)
        {
            var headerPanel = UIBuilderHelper.CreatePanel(parent, "Header",
                new Vector2(0, 0.92f), new Vector2(1, 1), HeaderColor);
            
            // Cache header rect for dragging
            _headerRect = headerPanel.GetComponent<RectTransform>();
            
            _headerText = UIBuilderHelper.CreateLabel(headerPanel.transform, "Title",
                "Companion Attributes", 14f, TextColor,
                new Vector2(0.05f, 0), new Vector2(0.90f, 1), TextAlignmentOptions.Center);
            _headerText.fontStyle = FontStyles.Bold;
            _headerText.textWrappingMode = TextWrappingModes.NoWrap;
            _headerText.overflowMode = TextOverflowModes.Ellipsis;
            
            // Close button
            _closeButton = UIBuilderHelper.CreateButton(parent, "X",
                new Vector2(0.92f, 0.92f), new Vector2(0.98f, 0.98f), () => Hide());
            _closeButtonRect = _closeButton.GetComponent<RectTransform>();
            var closeText = _closeButton.GetComponentInChildren<TMP_Text>();
            if (closeText != null)
            {
                closeText.fontSize = 14f;
            }
        }
        
        private void CreateLevelSection(Transform parent)
        {
            var levelPanel = UIBuilderHelper.CreatePanel(parent, "LevelSection",
                new Vector2(0.02f, 0.82f), new Vector2(0.98f, 0.91f), UIFontConfig.Colors.SectionBackground);
            
            // Level text
            _levelText = UIBuilderHelper.CreateLabel(levelPanel.transform, "Level",
                "Level 0", 13f, TextColor,
                new Vector2(0.02f, 0.4f), new Vector2(0.28f, 0.95f), TextAlignmentOptions.MidlineLeft);
            _levelText.fontStyle = FontStyles.Bold;
            _levelText.textWrappingMode = TextWrappingModes.NoWrap;
            
            // XP bar background
            var xpBarBg = UIBuilderHelper.CreateImage(levelPanel.transform, "XpBarBg",
                new Vector2(0.32f, 0.35f), new Vector2(0.98f, 0.75f), new Color(0.1f, 0.08f, 0.06f, 0.9f));
            
            // XP bar fill
            var xpBarFillGO = new GameObject("XpBarFill");
            xpBarFillGO.transform.SetParent(xpBarBg.transform, false);
            var xpFillRect = xpBarFillGO.AddComponent<RectTransform>();
            xpFillRect.anchorMin = new Vector2(0, 0);
            xpFillRect.anchorMax = new Vector2(0.5f, 1);
            xpFillRect.offsetMin = new Vector2(2, 2);
            xpFillRect.offsetMax = new Vector2(-2, -2);
            xpFillRect.pivot = new Vector2(0, 0.5f);
            _xpBarFill = xpBarFillGO.AddComponent<Image>();
            _xpBarFill.color = XpBarColor;
            
            // XP text
            _xpText = UIBuilderHelper.CreateLabel(levelPanel.transform, "XpText",
                "0 / 100 XP", 10f, LabelColor,
                new Vector2(0.32f, 0.05f), new Vector2(0.98f, 0.35f), TextAlignmentOptions.Center);
        }
        
        private void CreateUnspentSection(Transform parent)
        {
            var unspentPanel = UIBuilderHelper.CreatePanel(parent, "UnspentSection",
                new Vector2(0.02f, 0.76f), new Vector2(0.98f, 0.81f), new Color(0.2f, 0.15f, 0.05f, 0.8f));
            
            _unspentPointsText = UIBuilderHelper.CreateLabel(unspentPanel.transform, "UnspentText",
                "0 Attribute Points Available", 11f, UnspentColor,
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            _unspentPointsText.fontStyle = FontStyles.Bold;
            _unspentPointsText.textWrappingMode = TextWrappingModes.NoWrap;
        }
        
        private void CreateAttributeRows(Transform parent)
        {
            var attributePanel = UIBuilderHelper.CreatePanel(parent, "AttributeSection",
                new Vector2(0.02f, 0.40f), new Vector2(0.98f, 0.75f), UIFontConfig.Colors.SectionBackground);
            
            // Header row - reduced font sizes and adjusted columns
            var attrHeader = UIBuilderHelper.CreateLabel(attributePanel.transform, "AttrHeader",
                "Attribute", 9f, LabelColor,
                new Vector2(0.02f, 0.88f), new Vector2(0.32f, 0.98f), TextAlignmentOptions.MidlineLeft);
            attrHeader.textWrappingMode = TextWrappingModes.NoWrap;
            
            var valueHeader = UIBuilderHelper.CreateLabel(attributePanel.transform, "ValueHeader",
                "Val", 9f, LabelColor,
                new Vector2(0.32f, 0.88f), new Vector2(0.44f, 0.98f), TextAlignmentOptions.Center);
            valueHeader.textWrappingMode = TextWrappingModes.NoWrap;
            
            var bonusHeader = UIBuilderHelper.CreateLabel(attributePanel.transform, "BonusHeader",
                "Bonus", 9f, LabelColor,
                new Vector2(0.44f, 0.88f), new Vector2(0.78f, 0.98f), TextAlignmentOptions.Center);
            bonusHeader.textWrappingMode = TextWrappingModes.NoWrap;
            
            // Attribute rows
            float rowHeight = 0.17f;
            float startY = 0.70f;
            
            CreateAttributeRow(attributePanel.transform, 0, "Strength", CompanionProgression.AttributeType.Strength,
                "+{0}% melee damage", startY, rowHeight);
            CreateAttributeRow(attributePanel.transform, 1, "Speed", CompanionProgression.AttributeType.Speed,
                "+{0:F1}% move speed", startY - rowHeight, rowHeight);
            CreateAttributeRow(attributePanel.transform, 2, "Health", CompanionProgression.AttributeType.Health,
                "+{0} max HP", startY - rowHeight * 2, rowHeight);
            CreateAttributeRow(attributePanel.transform, 3, "Endurance", CompanionProgression.AttributeType.Endurance,
                "+{0} max stamina", startY - rowHeight * 3, rowHeight);
            CreateAttributeRow(attributePanel.transform, 4, "Intelligence", CompanionProgression.AttributeType.Intelligence,
                "+{0} max eitr", startY - rowHeight * 4, rowHeight);
        }
        
        private void CreateAttributeRow(Transform parent, int index, string name, CompanionProgression.AttributeType attrType,
            string bonusFormat, float yPos, float height)
        {
            var rowColor = index % 2 == 0 ? RowEvenColor : RowOddColor;
            var rowPanel = UIBuilderHelper.CreatePanel(parent, $"Row_{name}",
                new Vector2(0, yPos), new Vector2(1, yPos + height), rowColor);
            
            var row = new AttributeRowUI
            {
                Root = rowPanel,
                AttributeType = attrType
            };
            
            // Attribute name - reduced font, prevent wrapping
            row.NameText = UIBuilderHelper.CreateLabel(rowPanel.transform, "Name",
                name, 10f, TextColor,
                new Vector2(0.02f, 0), new Vector2(0.32f, 1), TextAlignmentOptions.MidlineLeft);
            row.NameText.textWrappingMode = TextWrappingModes.NoWrap;
            row.NameText.overflowMode = TextOverflowModes.Ellipsis;
            
            // Current value - reduced font
            row.ValueText = UIBuilderHelper.CreateLabel(rowPanel.transform, "Value",
                "0", 11f, ValueColor,
                new Vector2(0.32f, 0), new Vector2(0.44f, 1), TextAlignmentOptions.Center);
            row.ValueText.fontStyle = FontStyles.Bold;
            row.ValueText.textWrappingMode = TextWrappingModes.NoWrap;
            
            // Bonus description - reduced font, wider column
            row.BonusText = UIBuilderHelper.CreateLabel(rowPanel.transform, "Bonus",
                "", 9f, BonusColor,
                new Vector2(0.44f, 0), new Vector2(0.80f, 1), TextAlignmentOptions.Center);
            row.BonusText.textWrappingMode = TextWrappingModes.NoWrap;
            row.BonusText.overflowMode = TextOverflowModes.Ellipsis;
            
            // Increase button (+) - slightly adjusted position
            row.IncreaseButton = UIBuilderHelper.CreateButton(rowPanel.transform, "+",
                new Vector2(0.82f, 0.15f), new Vector2(0.98f, 0.85f), () => OnIncreaseAttribute(attrType));
            
            // Cache the button rect for manual click detection
            _increaseButtonRects[index] = row.IncreaseButton.GetComponent<RectTransform>();
            
            var btnText = row.IncreaseButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null)
            {
                btnText.fontSize = 14f;
                btnText.fontStyle = FontStyles.Bold;
            }
            
            _attributeRows[index] = row;
        }
        
        private void CreateStatBars(Transform parent)
        {
            var statPanel = UIBuilderHelper.CreatePanel(parent, "StatBarsSection",
                new Vector2(0.02f, 0.18f), new Vector2(0.98f, 0.39f), UIFontConfig.Colors.SectionBackground);
            
            // Section label - centered above the bars
            var statsLabel = UIBuilderHelper.CreateLabel(statPanel.transform, "StatsLabel",
                "Current Stats", 10f, LabelColor,
                new Vector2(0.0f, 0.85f), new Vector2(1.0f, 0.98f), TextAlignmentOptions.Center);
            statsLabel.textWrappingMode = TextWrappingModes.NoWrap;
            statsLabel.fontStyle = FontStyles.Bold;
            
            // Health bar - centered layout
            CreateStatBarCentered(statPanel.transform, "Health", 0.62f, HealthBarColor, out _healthBarFill, out _healthText);
            
            // Stamina bar - centered layout
            CreateStatBarCentered(statPanel.transform, "Stamina", 0.38f, StaminaBarColor, out _staminaBarFill, out _staminaText);
            
            // Eitr bar - centered layout
            CreateStatBarCentered(statPanel.transform, "Eitr", 0.14f, EitrBarColor, out _eitrBarFill, out _eitrText);
        }
        
        /// <summary>
        /// Creates a centered stat bar with label on left, bar in middle, value on right.
        /// Uses anchorMax.x for bar fill percentage - reliable and correct.
        /// </summary>
        private void CreateStatBarCentered(Transform parent, string name, float yPos, Color fillColor,
            out Image fillImage, out TMP_Text valueText)
        {
            float barHeight = 0.18f;
            
            // Label - left side, right-aligned
            var label = UIBuilderHelper.CreateLabel(parent, $"{name}Label",
                name + ":", 9f, LabelColor,
                new Vector2(0.05f, yPos), new Vector2(0.20f, yPos + barHeight), TextAlignmentOptions.MidlineRight);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            
            // Bar background - center of panel
            var barBg = UIBuilderHelper.CreateImage(parent, $"{name}BarBg",
                new Vector2(0.22f, yPos + 0.02f), new Vector2(0.70f, yPos + barHeight - 0.02f),
                new Color(0.1f, 0.08f, 0.06f, 0.9f));
            
            // Inner container with padding - this handles the 2px inset
            var innerGO = new GameObject($"{name}Inner");
            innerGO.transform.SetParent(barBg.transform, false);
            var innerRect = innerGO.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(2, 2);  // 2px padding
            innerRect.offsetMax = new Vector2(-2, -2);
            
            // Bar fill - stretches within inner container, anchorMax.x controls width
            var fillGO = new GameObject($"{name}Fill");
            fillGO.transform.SetParent(innerGO.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);  // anchorMax.x will be updated dynamically
            fillRect.offsetMin = Vector2.zero;  // No offsets on fill - anchors do all the work
            fillRect.offsetMax = Vector2.zero;
            fillImage = fillGO.AddComponent<Image>();
            fillImage.color = fillColor;
            
            // Value text - right side of panel
            valueText = UIBuilderHelper.CreateLabel(parent, $"{name}Value",
                "0/0", 9f, TextColor,
                new Vector2(0.72f, yPos), new Vector2(0.95f, yPos + barHeight), TextAlignmentOptions.MidlineLeft);
            valueText.textWrappingMode = TextWrappingModes.NoWrap;
            valueText.overflowMode = TextOverflowModes.Ellipsis;
        }
        
        private void CreateButtons(Transform parent)
        {
            // Reset button
            _resetButton = UIBuilderHelper.CreateButton(parent, "Reset Attributes",
                new Vector2(0.05f, 0.03f), new Vector2(0.45f, 0.10f), OnResetClicked);
            _resetButtonRect = _resetButton.GetComponent<RectTransform>();
            var resetColors = _resetButton.colors;
            resetColors.normalColor = new Color(0.4f, 0.15f, 0.1f, 0.9f);
            resetColors.highlightedColor = new Color(0.6f, 0.2f, 0.15f, 0.95f);
            _resetButton.colors = resetColors;
            
            // Done button
            var doneBtn = UIBuilderHelper.CreateButton(parent, "Done",
                new Vector2(0.55f, 0.03f), new Vector2(0.95f, 0.10f), () => Hide());
            _doneButtonRect = doneBtn.GetComponent<RectTransform>();
        }
        
        #endregion
        
        #region UI Updates
        
        private void RefreshUI()
        {
            if (_companion == null) return;
            
            var progression = _companion.GetProgression();
            if (progression == null) return;
            
            // Update header
            _headerText.text = $"{_companion.GetDisplayName()} - Attributes";
            
            // Update level
            _levelText.text = $"Level {progression.Level}";
            
            // Update XP bar
            float xpPercent = progression.LevelProgress;
            _xpBarFill.rectTransform.anchorMax = new Vector2(xpPercent, 1);
            _xpText.text = $"{progression.CurrentXp:F0} / {progression.XpForNextLevel:F0} XP";
            
            // Update unspent points
            int unspent = progression.UnspentAttributePoints;
            _unspentPointsText.text = unspent > 0 
                ? $"{unspent} Attribute Point{(unspent > 1 ? "s" : "")} Available!"
                : "No Attribute Points Available";
            _unspentPointsText.color = unspent > 0 ? UnspentColor : BonusColor;
            
            // Update attribute rows
            UpdateAttributeRow(0, CompanionProgression.AttributeType.Strength, "{0}%", 1f);
            UpdateAttributeRow(1, CompanionProgression.AttributeType.Speed, "{0:F1}%", 0.5f);
            UpdateAttributeRow(2, CompanionProgression.AttributeType.Health, "{0}", 5f);
            UpdateAttributeRow(3, CompanionProgression.AttributeType.Endurance, "{0}", 3f);
            UpdateAttributeRow(4, CompanionProgression.AttributeType.Intelligence, "{0}", 3f);
            
            // Update stat bars
            RefreshStatBars();
        }
        
        private void UpdateAttributeRow(int index, CompanionProgression.AttributeType attrType, string bonusFormat, float multiplier)
        {
            if (_attributeRows[index] == null) return;
            
            var progression = _companion?.GetProgression();
            if (progression == null) return;
            
            var row = _attributeRows[index];
            int value = progression.GetAttributeValue(attrType);
            
            row.ValueText.text = value.ToString();
            row.BonusText.text = "+" + string.Format(bonusFormat, value * multiplier);
            
            // Enable/disable increase button based on available points
            bool canIncrease = progression.UnspentAttributePoints > 0;
            row.IncreaseButton.interactable = canIncrease;
            
            // Visual feedback
            var btnImage = row.IncreaseButton.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = canIncrease 
                    ? UIFontConfig.Colors.ButtonNormal 
                    : UIFontConfig.Colors.ButtonDisabled;
            }
        }
        
        private void RefreshStatBars()
        {
            if (_companion == null) return;
            
            // Use centralized stat calculator for consistent values
            var calc = new CompanionStatCalculator(_companion);
            
            if (calc.IsValid)
            {
                // Health bar
                float healthPercent = Mathf.Clamp01(calc.HealthPercentage);
                SetBarFillPercent(_healthBarFill, healthPercent);
                if (_healthText != null)
                    _healthText.text = $"{calc.CurrentHealth:F0}/{calc.MaxHealth:F0}";
                
                // Stamina bar
                float staminaPercent = Mathf.Clamp01(calc.StaminaPercentage);
                SetBarFillPercent(_staminaBarFill, staminaPercent);
                if (_staminaText != null)
                    _staminaText.text = $"{calc.CurrentStamina:F0}/{calc.MaxStamina:F0}";
                
                // Eitr bar
                float eitrPercent = Mathf.Clamp01(calc.EitrPercentage);
                SetBarFillPercent(_eitrBarFill, eitrPercent);
                if (_eitrText != null)
                    _eitrText.text = $"{calc.CurrentEitr:F0}/{calc.MaxEitr:F0}";
            }
        }
        
        /// <summary>
        /// Sets a bar fill to the specified percentage (0-1).
        /// Uses anchorMax.x and ensures offsets are zeroed for proper sizing.
        /// </summary>
        private void SetBarFillPercent(Image fillImage, float percent)
        {
            if (fillImage == null) return;
            
            var rect = fillImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(percent, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        
        #endregion
        
        #region Button Handlers
        
        private void OnIncreaseAttribute(CompanionProgression.AttributeType attrType)
        {
            if (_companion == null) return;
            
            var progression = _companion.GetProgression();
            if (progression == null) return;
            
            if (progression.SpendAttributePoint(attrType))
            {
                // Play feedback sound
                PlayAttributeSound();
                
                // Refresh UI
                RefreshUI();

                // Phase 6: live ZDO is authoritative for progression. The
                // periodic FlushDebugMirror in CompanionVault picks up the
                // change for the on-disk debug mirror; no per-event save.
                Debug.Log($"[CompanionAttributeScreen] Attribute change for {_companion.companionName} (ID: {_companion.companionId}): " +
                    $"{attrType} -> {progression.GetAttributeValue(attrType)}, " +
                    $"unspent: {progression.UnspentAttributePoints}, " +
                    $"progressionData: {progression.GetProgressionDataForVault()}");
            }
        }
        
        private void OnResetClicked()
        {
            if (_companion == null) return;
            
            var progression = _companion.GetProgression();
            if (progression == null) return;
            
            // Only reset if there are points spent
            if (progression.GetTotalAttributePoints() > 0)
            {
                progression.ResetAttributes();
                
                // Refresh UI
                RefreshUI();

                // Phase 6: see OnAttributeUpClicked. Live ZDO is
                // authoritative; periodic flush mirrors it.

                // Show message
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"Reset attributes for {_companion.GetDisplayName()}");
                
                Debug.Log($"[CompanionAttributeScreen] Reset and saved attributes for {_companion.companionName}");
            }
        }
        
        private void PlayAttributeSound()
        {
            try
            {
                // Try to play a level-up style sound
                var effectPrefab = ZNetScene.instance?.GetPrefab("sfx_gui_craftitem");
                if (effectPrefab != null)
                {
                    UnityEngine.Object.Instantiate(effectPrefab, Vector3.zero, Quaternion.identity);
                }
            }
            catch { }
        }
        
        #endregion
        
        #region Drag Start Helper
        
        /// <summary>
        /// Called by AttributePanelDragHandler when drag starts on the background.
        /// </summary>
        public void OnBackgroundDragStart(Vector2 mousePosition)
        {
            _isDragging = true;
            var panelRect = _root.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                _dragOffset = (Vector2)panelRect.position - mousePosition;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Drag handler for the attribute screen background panel.
    /// Allows dragging the entire panel by clicking anywhere on the parchment background.
    /// </summary>
    public class AttributePanelDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CompanionAttributeScreen _screen;
        private RectTransform _panelRect;
        private Vector2 _dragOffset;
        
        public void Initialize(CompanionAttributeScreen screen)
        {
            _screen = screen;
            // Get the root panel's RectTransform (parent of background)
            _panelRect = screen?.transform.parent?.GetComponent<RectTransform>() 
                      ?? transform.parent?.GetComponent<RectTransform>();
        }
        
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_panelRect != null)
            {
                _dragOffset = (Vector2)_panelRect.position - eventData.position;
            }
            // Also notify the screen so it tracks dragging state
            _screen?.OnBackgroundDragStart(eventData.position);
        }
        
        public void OnDrag(PointerEventData eventData)
        {
            if (_panelRect != null)
            {
                _panelRect.position = eventData.position + _dragOffset;
            }
        }
        
        public void OnEndDrag(PointerEventData eventData)
        {
            // Dragging ends - nothing special needed
        }
    }
}
