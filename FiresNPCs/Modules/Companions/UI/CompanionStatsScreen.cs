using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using FiresRPGmaker.UI;
using FiresCore.Npc;
using FiresCore.Npc.Combat;
using FiresCore.Npc.Archetypes;

namespace FiresRPGmaker.Modules.Companions
{
    /// <summary>
    /// Companion Stats Screen - displays detailed companion statistics.
    /// Separated from CompanionInventoryScreen for maintainability.
    /// 
    /// Displays:
    /// - Combat stats (health, armor, damage)
    /// - Equipment bonuses
    /// - Skills and levels
    /// - Kill tracking statistics
    /// - Inventory summary
    /// - Current status
    /// - Button to view all owned companions
    /// </summary>
    public class CompanionStatsScreen : MonoBehaviour
    {
        #region Constants
    
        // =========================================================================
        // COLOR CONSTANTS - Using UIFontConfig for consistency
        // =========================================================================
        private static readonly Color TextColor = UIFontConfig.Colors.Gold;
     
        #endregion

        #region Fields

        private GameObject _root;
        private ScrollRect _scrollRect;
        private RectTransform _scrollbarRect;
        private TMP_Text _statsText;
        private CompanionController _companion;

        // Button RectTransforms for manual click detection (InventoryGui blocks pointer events)
        private RectTransform _viewAllButtonRect;
        private RectTransform _manageAttributesButtonRect;

        // Manual scrollbar drag state
        private bool _isDraggingScrollbar;

        #endregion

        #region Public API

        /// <summary>
        /// Creates the stats panel as a child of the given parent.
   /// </summary>
        public static CompanionStatsScreen Create(Transform parent, RectTransform contentArea)
        {
      var go = new GameObject("StatsScreen");
            go.transform.SetParent(parent, false);

    var rect = go.AddComponent<RectTransform>();
        // =========================================================================
            // PANEL POSITIONING - Fill the content area
            // anchorMin/Max (0,0) to (1,1) = stretch to fill parent
// =========================================================================
     rect.anchorMin = Vector2.zero;    // Bottom-left anchor
            rect.anchorMax = Vector2.one;     // Top-right anchor
   rect.offsetMin = Vector2.zero;    // No offset from anchors
        rect.offsetMax = Vector2.zero;

            var bg = go.AddComponent<Image>();
            bg.color = UIFontConfig.Colors.FormSectionBackground;
            bg.raycastTarget = true;

 var screen = go.AddComponent<CompanionStatsScreen>();
  screen._root = go;
   screen.CreateContent(go.transform);

      go.SetActive(false);
      return screen;
        }

        /// <summary>
        /// Shows the stats screen for the given companion.
        /// </summary>
        public void Show(CompanionController companion)
        {
            _companion = companion;
            
            // Ensure stats are recalculated with latest food bonuses before showing
            companion?.GetStats()?.RecalculateMaxStats();
            
            _root.SetActive(true);
            RefreshStats();
        }

        /// <summary>
        /// Hides the stats screen.
        /// </summary>
   public void Hide()
     {
            _root.SetActive(false);
         _companion = null;
        }

        /// <summary>
        /// Refreshes the stats display with current companion data.
        /// </summary>
        public void RefreshStats()
        {
            if (_statsText == null)
            {
                Debug.LogWarning("[CompanionStatsScreen] RefreshStats failed: _statsText is null");
                return;
            }
            if (_companion == null)
            {
                Debug.LogWarning("[CompanionStatsScreen] RefreshStats failed: _companion is null");
                return;
            }
            
            var statsString = BuildStatsText();
            if (string.IsNullOrEmpty(statsString))
            {
                Debug.LogWarning("[CompanionStatsScreen] BuildStatsText returned empty string");
                statsString = "No stats available.";
            }
            
            _statsText.text = statsString;
            Debug.Log($"[CompanionStatsScreen] Refreshed stats, text length: {statsString.Length}");

            // Force layout update so ContentSizeFitter recalculates.
            // Use targeted LayoutRebuilder instead of Canvas.ForceUpdateCanvases()
            // which rebuilds ALL canvases in the scene and is very expensive.
            var statsRect = _root.GetComponent<RectTransform>();
            if (statsRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(statsRect);

            // Reset scroll to top
            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        public bool IsVisible => _root != null && _root.activeSelf;
        
        /// <summary>
        /// Gets the current companion being displayed.
        /// </summary>
        public CompanionController GetCurrentCompanion() => _companion;
        
        /// <summary>
        /// Returns true if any stats screen is open for the specified companion.
        /// </summary>
        public static bool IsOpenForCompanion(CompanionController companion)
        {
            if (companion == null) return false;
            
            // Check all active CompanionStatsScreen instances
            var allScreens = FindObjectsByType<CompanionStatsScreen>(FindObjectsSortMode.None);
            foreach (var screen in allScreens)
            {
                if (screen.IsVisible && screen.GetCurrentCompanion() == companion)
                    return true;
            }
            return false;
        }

        #endregion

        #region Unity Lifecycle
     
        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;

            // Manual button click detection - InventoryGui blocks normal pointer events
            if (Input.GetMouseButtonDown(0))
            {
                HandleManualButtonClick();
                HandleScrollbarMouseDown();
            }

            // Handle scrollbar drag
            if (_isDraggingScrollbar)
            {
                HandleScrollbarDrag();
            }

            if (Input.GetMouseButtonUp(0))
            {
                _isDraggingScrollbar = false;
            }

            // Handle scroll wheel manually if mouse is over scroll area
            HandleManualScroll();
        }
        
        /// <summary>
        /// Manually checks if any buttons were clicked.
        /// This bypasses Unity's event system which is blocked by InventoryGui.
        /// </summary>
        private void HandleManualButtonClick()
        {
            Vector2 mousePos = Input.mousePosition;
            
            // Check View All Companions button
            if (_viewAllButtonRect != null && 
                RectTransformUtility.RectangleContainsScreenPoint(_viewAllButtonRect, mousePos, null))
            {
                OnViewAllCompanionsClicked();
                return;
            }
            
            // Check Manage Attributes button
            if (_manageAttributesButtonRect != null && 
                RectTransformUtility.RectangleContainsScreenPoint(_manageAttributesButtonRect, mousePos, null))
            {
                OnManageAttributesClicked();
                return;
            }
        }
        
        /// <summary>
        /// Handles mouse wheel scrolling manually since InventoryGui can block scroll events.
        /// </summary>
        private void HandleManualScroll()
        {
            if (_scrollRect == null) return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            // Check if mouse is over our scroll area
            Vector2 mousePos = Input.mousePosition;
            var scrollRectTransform = _scrollRect.GetComponent<RectTransform>();
            if (scrollRectTransform != null && 
                RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, mousePos, null))
            {
                // Apply scroll - positive scroll = up, so increase normalized position
                _scrollRect.verticalNormalizedPosition += scroll * 0.6f;
                _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(_scrollRect.verticalNormalizedPosition);
            }
        }

        /// <summary>
        /// Checks if the mouse down landed on the scrollbar and starts drag tracking.
        /// </summary>
        private void HandleScrollbarMouseDown()
        {
            if (_scrollbarRect == null || _scrollRect == null) return;

            Vector2 mousePos = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(_scrollbarRect, mousePos, null))
            {
                _isDraggingScrollbar = true;
                // Immediately apply position so clicking the track jumps there
                ApplyScrollbarPosition(mousePos);
            }
        }

        /// <summary>
        /// While dragging, converts the mouse Y position to a normalized scroll value.
        /// </summary>
        private void HandleScrollbarDrag()
        {
            if (_scrollbarRect == null || _scrollRect == null) return;
            ApplyScrollbarPosition(Input.mousePosition);
        }

        private void ApplyScrollbarPosition(Vector2 screenPos)
        {
            // Convert mouse position to local position within the scrollbar rect
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _scrollbarRect, screenPos, null, out Vector2 localPos);

            float height = _scrollbarRect.rect.height;
            if (height <= 0f) return;

            // localPos.y goes from -height/2 (bottom) to +height/2 (top)
            // Normalize to 0..1 where 0 = bottom, 1 = top
            float normalized = Mathf.Clamp01((localPos.y + height * 0.5f) / height);
            _scrollRect.verticalNormalizedPosition = normalized;
        }
   
        #endregion

        #region UI Creation

        private void CreateContent(Transform parent)
        {
            // =========================================================================
            // VIEW ALL COMPANIONS BUTTON - At the top
            // =========================================================================
            CreateViewAllButton(parent);
     
            // =========================================================================
            // SCROLL VIEW SETUP - Using UIBuilderHelper for proper scroll handling
            // Added more padding (0.04f instead of 0.02f) to show parchment border better
            // =========================================================================
            var scrollResult = UIBuilderHelper.CreateScrollArea(parent, "StatsScroll",
                new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.88f), null, 12f);

            _scrollRect = scrollResult.ScrollRect;
            _scrollRect.scrollSensitivity = 120f;  // Higher value = faster scrolling
            _scrollbarRect = scrollResult.Scrollbar?.GetComponent<RectTransform>();

            // Add VerticalLayoutGroup to content for proper layout
            var contentLayout = UIBuilderHelper.AddVerticalLayout(
                scrollResult.Content.gameObject,
                padding: new RectOffset(8, 8, 8, 8),
                spacing: 4f,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: false
            );

            // Add ContentSizeFitter to content so it auto-sizes based on children
            UIBuilderHelper.AddContentSizeFitter(scrollResult.Content.gameObject,
                ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

            // Create stats text inside content
            CreateStatsText(scrollResult.Content);
            
            // Create "Manage Attributes" button at the bottom
            CreateManageAttributesButton(parent);
        }
   
   
   
        private void CreateViewAllButton(Transform parent)
        {
            var button = UIBuilderHelper.CreateButton(
                parent, 
                ">> View All Companions <<",
                new Vector2(0.1f, 0.92f),
                new Vector2(0.9f, 0.98f),
                OnViewAllCompanionsClicked
            );
            
            _viewAllButtonRect = button.GetComponent<RectTransform>();
            
            // Override the text style for this specific button
            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.fontSize = 13;
                buttonText.fontStyle = FontStyles.Bold;
                buttonText.color = TextColor;
            }
        }

private void CreateStatsText(Transform parent)
{
    var statsTextGO = new GameObject("StatsText");
    statsTextGO.transform.SetParent(parent, false);

    // With VerticalLayoutGroup on parent, we don't need complex anchoring
    // The layout group will handle positioning
    var statsTextRect = statsTextGO.AddComponent<RectTransform>();
            
    _statsText = statsTextGO.AddComponent<TextMeshProUGUI>();
    _statsText.text = "Loading stats...";
    _statsText.fontSize = 12;
    _statsText.color = TextColor;
    _statsText.alignment = TextAlignmentOptions.TopLeft;
    _statsText.textWrappingMode = TextWrappingModes.Normal;
    _statsText.richText = true;
    _statsText.raycastTarget = false;
    _statsText.lineSpacing = 15f;  // Increased line spacing for better readability
    
    // CRITICAL: Set overflow mode to allow text to expand vertically
    // Ellipsis causes truncation - we want text to wrap to new lines instead
    _statsText.overflowMode = TextOverflowModes.Overflow;
    _statsText.textWrappingMode = TextWrappingModes.Normal;
    
    UIBuilderHelper.ApplyValheimFont(_statsText);

    // Add LayoutElement so ContentSizeFitter can calculate proper size
    var layoutElement = UIBuilderHelper.AddLayoutElement(statsTextGO, 
        minHeight: 50,      // Minimum height
        flexibleWidth: 1,   // Stretch to fill width
        flexibleHeight: 0   // Don't stretch height - use preferred
    );
    
    // IMPORTANT: Set preferred width to a reasonable value for word wrapping
    // This tells TMP where to wrap the text
    layoutElement.preferredWidth = 350;  // Fixed width for consistent wrapping
            
    // Let the text determine its own height based on content
    UIBuilderHelper.AddContentSizeFitter(statsTextGO,
        ContentSizeFitter.FitMode.PreferredSize,  // Use preferred width for wrapping
        ContentSizeFitter.FitMode.PreferredSize);
}
        
private void CreateManageAttributesButton(Transform parent)
        {
            var button = UIBuilderHelper.CreateButton(
                parent,
                "Manage Attributes",
                new Vector2(0.15f, 0.02f),
                new Vector2(0.85f, 0.08f),
                new Color(0.3f, 0.25f, 0.1f, 0.95f),
                new Color(0.5f, 0.4f, 0.15f, 0.98f),
                new Color(0.6f, 0.5f, 0.2f, 1f),
                OnManageAttributesClicked
            );
            
            // Cache the button rect for manual click detection
            _manageAttributesButtonRect = button.GetComponent<RectTransform>();
            
            // Style the button text
            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.fontSize = 14f;
                buttonText.fontStyle = FontStyles.Bold;
                buttonText.color = new Color(1f, 0.9f, 0.5f);
            }
        }

        #endregion
        
        #region Button Handlers
        
        private void OnManageAttributesClicked()
        {
            if (_companion == null) return;
            
            // Open the dedicated attribute management screen
            CompanionAttributeScreen.Show(_companion);
        }
  
   private void OnViewAllCompanionsClicked()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
    
   // Hide the inventory screen and show the roster
       CompanionInventoryScreen.Hide();
            CompanionRosterScreen.Show(player, _companion);
  }
      
    #endregion

    #region Stats Building

    /// <summary>
        /// Builds the complete stats text with all companion information.
        /// </summary>
        private string BuildStatsText()
        {
            if (_companion == null) return "No companion selected.";

            var sb = new StringBuilder();
     
            // Rich text color codes - optimized for dark semi-transparent background
            // Using lighter colors that provide good contrast against dark overlay
            string gold = "#FFD700";      // Bright gold for headers/emphasis
            string white = "#FFFFFF";     // White for labels
            string green = "#66FF66";     // Bright green for positive values
            string red = "#FF6666";       // Bright red for danger/low values
            string gray = "#AAAAAA";      // Light gray for secondary text
            string blue = "#6699FF";      // Bright blue for info
            string purple = "#CC99FF";    // Light purple for special abilities

            // ===== HEADER =====
            sb.AppendLine($"<size=18><b><color={gold}>{_companion.GetDisplayName()}</color></b></size>");
            sb.AppendLine();

            // ===== LEVEL & PROGRESSION =====
            BuildProgressionStats(sb, gold, white, green, purple);

            // ===== ARCHETYPE & ROLE =====
            BuildArchetypeStats(sb, gold, white, green, blue, purple);

            // ===== ABILITIES =====
            BuildAbilitiesSection(sb, gold, white, green, purple);

            // ===== ARCHETYPE SKILLS =====
            BuildArchetypeSkillsSection(sb, gold, white, green, purple);

            // ===== COMBAT STATS =====
            BuildCombatStats(sb, gold, white, green, red, blue);

            // ===== ATTRIBUTES =====
            BuildAttributeStats(sb, gold, white, green);

            // ===== EQUIPMENT BONUSES =====
            BuildEquipmentStats(sb, gold, white, green);

            // ===== SKILLS =====
            BuildSkillStats(sb, gold);

            // ===== KILL TRACKING =====
            BuildKillStats(sb, gold, white, red, gray);

            // ===== INVENTORY =====
            BuildInventoryStats(sb, gold, white, gray);

            // ===== STATUS =====
            BuildStatusInfo(sb, gold, white, gray, blue);

            return sb.ToString();
        }

        private void BuildProgressionStats(StringBuilder sb, string gold, string white, string green, string purple)
        {
            var progression = _companion.GetComponent<CompanionProgression>();
            if (progression == null) return;

            sb.AppendLine($"<b><color={gold}>[ Level & Experience ]</color></b>");
            
            // Level display
            int level = progression.Level;
            string levelColor = level >= 50 ? gold : (level >= 25 ? green : white);
            sb.AppendLine($"<color={white}>Level:</color> <color={levelColor}>{level}</color> / 100");
            
            // XP bar
            float xpPercent = progression.LevelProgress * 100f;
            sb.AppendLine($"<color={white}>Experience:</color> <color={green}>{progression.CurrentXp:F0}</color> / {progression.XpForNextLevel:F0}");
            sb.AppendLine($"<color=#AAAAAA>[{GetProgressBar(xpPercent)}] {xpPercent:F0}%</color>");
            
            // Unspent points
            if (progression.UnspentAttributePoints > 0)
            {
                sb.AppendLine($"<color={purple}>Unspent Attribute Points: {progression.UnspentAttributePoints}</color>");
            }
            
            sb.AppendLine();
        }

        private void BuildArchetypeStats(StringBuilder sb, string gold, string white, string green, string blue, string purple)
        {
            var archetypeController = _companion.GetArchetypeController();
            if (archetypeController == null) return;

            sb.AppendLine($"<b><color={gold}>[ Combat Role ]</color></b>");
            
            // Current archetype
            var archetype = archetypeController.CurrentArchetypeClass;
            if (archetype == ArchetypeClass.None)
            {
                sb.AppendLine($"<color=#888888>No archetype assigned yet. Equip weapons to determine role.</color>");
                sb.AppendLine();
                return;
            }
            
            // Get archetype display info
            string archetypeName = archetypeController.GetArchetypeDisplayName();
            Color archetypeColor = archetypeController.GetArchetypeColor();
            string archetypeColorHex = ColorUtility.ToHtmlStringRGB(archetypeColor);
            
            sb.AppendLine($"<color={white}>Archetype:</color> <color=#{archetypeColorHex}><b>{archetypeName}</b></color>");
            
            // Check for hybrid definition
            var hybrid = archetypeController.GetHybridDefinition();
            if (hybrid != null)
            {
                // Show hybrid description and special ability
            sb.AppendLine($"<color=#CCCCCC>{hybrid.Description}</color>");
                sb.AppendLine();
                
                // Show hybrid-specific bonuses
                sb.AppendLine($"<color={gold}>Hybrid Bonuses:</color>");
                
                if (hybrid.BonusDamageMultiplier > 1f)
                    sb.AppendLine($"  <color={green}>+{(hybrid.BonusDamageMultiplier - 1f) * 100:F0}% Damage</color>");
                if (hybrid.BonusDefenseMultiplier > 1f)
                    sb.AppendLine($"  <color={blue}>+{(hybrid.BonusDefenseMultiplier - 1f) * 100:F0}% Defense</color>");
                if (hybrid.BonusSpeedMultiplier > 1f)
                    sb.AppendLine($"  <color=#AAFFAA>+{(hybrid.BonusSpeedMultiplier - 1f) * 100:F0}% Speed</color>");
                if (hybrid.BonusHealthRegen > 0)
                    sb.AppendLine($"  <color={green}>+{hybrid.BonusHealthRegen:F1} HP/s Regen</color>");
                if (hybrid.BonusStaminaRegen > 0)
                    sb.AppendLine($"  <color=#FFFF88>+{(hybrid.BonusStaminaRegen - 1f) * 100:F0}% Stamina Regen</color>");
                if (hybrid.BonusEitrRegen > 0)
                    sb.AppendLine($"  <color={blue}>+{hybrid.BonusEitrRegen:F1} Eitr/s Regen</color>");
                if (hybrid.BonusCritChance > 0)
                    sb.AppendLine($"  <color=#FFAA44>+{hybrid.BonusCritChance * 100:F0}% Crit Chance</color>");
                if (hybrid.BonusBlockEfficiency > 0)
                    sb.AppendLine($"  <color={blue}>+{hybrid.BonusBlockEfficiency * 100:F0}% Block Efficiency</color>");
                
                sb.AppendLine();
                
                // Show special hybrid ability
                sb.AppendLine($"<color={purple}>Special Ability: {hybrid.SpecialAbilityName}</color>");
                sb.AppendLine($"<color=#AAAAAA>{hybrid.SpecialAbilityDescription}</color>");
                sb.AppendLine($"<color=#888888>Cooldown: {hybrid.SpecialAbilityCooldown:F0}s</color>");
            }
            else
            {
                // Get archetype definition for description (non-hybrid)
                var definition = archetypeController.CurrentDefinition;
                if (definition != null)
                {
                    sb.AppendLine($"<color=#CCCCCC>{definition.Description}</color>");
                    
                    // Determine role based on archetype class
                    string roleType = archetypeController.IsTank ? "Tank" : 
                                      archetypeController.IsSupport ? "Support" : "DPS";
                    string roleColor = archetypeController.IsTank ? "#66AAFF" : 
                                       archetypeController.IsSupport ? "#66FF66" : "#FF6666";
                    
                    sb.AppendLine($"<color={white}>Role:</color> <color={roleColor}>{roleType}</color> | " +
                        $"<color={white}>Range:</color> <color={white}>{(archetypeController.IsMelee ? "Melee" : "Ranged")}</color>");
                }
            }
            
            // Show base archetype bonuses
            sb.AppendLine();
            sb.AppendLine($"<color={gold}>Base Role Abilities:</color>");
            
            switch (archetype)
            {
                case ArchetypeClass.Tank:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Increased aggro generation</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Taunt ability (attracts enemy attention)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Enhanced blocking effectiveness</color>");
                    if (archetypeController.IsTauntOnCooldown)
                    {
                        sb.AppendLine($"  <color=#FFAA44>Taunt cooldown: {archetypeController.TauntCooldownRemaining:F0}s</color>");
                    }
                    else
                    {
                        sb.AppendLine($"  <color={green}>Taunt ready!</color>");
                    }
                    break;
                    
                case ArchetypeClass.Paladin:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Divine Protection (group damage reduction)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Holy Smite (spirit damage burst)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Can tank and support allies</color>");
                    break;
                    
                case ArchetypeClass.Berserker:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Berserk Rage at low health (+damage, -defense)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Warcry (group attack speed buff)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Higher damage, lower survivability</color>");
                    if (archetypeController.IsBerserkActive)
                    {
                        sb.AppendLine($"  <color=#FF4444><b>BERSERK ACTIVE!</b></color>");
                    }
                    break;
                    
                case ArchetypeClass.Rogue:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Backstab bonus ({archetypeController.backstabDamageMultiplier:F1}x damage from behind)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Stealth ability (increased crit chance)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Poison attacks (DoT on enemies)</color>");
                    break;
                    
                case ArchetypeClass.Monk:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Chi Strike (empowered unarmed attacks)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Inner Peace (healing meditation)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Purifying Circle (area heal)</color>");
                    break;
                    
                case ArchetypeClass.Ranger:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Eagle Eye (increased ranged accuracy)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Hunter's Mark (bonus damage on marked target)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Prefers to keep distance from enemies</color>");
                    break;
                    
                case ArchetypeClass.Mage:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Elemental Infusion (boosted magic damage)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Arcane Shield (damage absorption)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Uses Eitr for abilities</color>");
                    break;
                    
                case ArchetypeClass.Healer:
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Sanctuary (group damage reduction aura)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Purify (cleanse debuffs from allies)</color>");
                    sb.AppendLine($"  <color=#CCCCCC>Ã¢â‚¬Â¢ Purifying Circle (area heal)</color>");
                    break;
            }
            
            // Show archetype statistics if available
            var stats = archetypeController.Statistics;
            if (stats != null)
            {
                // Show favored archetype based on time spent
                var favoredArchetype = stats.GetFavoredArchetype();
                float totalTime = stats.GetTotalArchetypeTime();
                
                if (totalTime > 60f) // Only show if more than 1 minute of tracking
                {
                    sb.AppendLine();
                    sb.AppendLine($"<color={gold}>Combat Statistics:</color>");
                    
                    // Time in current archetype
                    float currentPercent = stats.GetArchetypePercentage(archetype);
                    sb.AppendLine($"  <color={white}>Time as {archetypeName}:</color> <color={green}>{currentPercent:F0}%</color>");
                    
                    // Show favorite weapon
                    string favWeapon = stats.GetFavoriteWeaponType();
                    if (favWeapon != "None")
                    {
                        sb.AppendLine($"  <color={white}>Favored Weapon:</color> <color={green}>{favWeapon}</color>");
                    }
                    
                    // Show combat stats based on archetype
                    if (stats.TotalDamageBlocked > 0 && (archetype == ArchetypeClass.Tank || archetype == ArchetypeClass.Paladin))
                    {
                        sb.AppendLine($"  <color={white}>Damage Blocked:</color> <color={blue}>{stats.TotalDamageBlocked:F0}</color>");
                        sb.AppendLine($"  <color={white}>Perfect Parries:</color> <color={blue}>{stats.PerfectParries}</color>");
                    }
                    
                    if (stats.TotalHealingDone > 0 && (archetype == ArchetypeClass.Healer || archetype == ArchetypeClass.Paladin))
                    {
                        sb.AppendLine($"  <color={white}>Healing Done:</color> <color={green}>{stats.TotalHealingDone:F0}</color>");
                    }
                    
                    if (stats.CriticalHits > 0)
                    {
                        sb.AppendLine($"  <color={white}>Critical Hits:</color> <color=#FF8800>{stats.CriticalHits}</color>");
                    }
                    
                    if (stats.BackstabHits > 0 && archetype == ArchetypeClass.Rogue)
                    {
                        sb.AppendLine($"  <color={white}>Backstabs:</color> <color={purple}>{stats.BackstabHits}</color>");
                    }
                }
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// Builds the abilities section showing unlocked and upcoming abilities.
        /// </summary>
        private void BuildAbilitiesSection(StringBuilder sb, string gold, string white, string green, string purple)
        {
            var progression = _companion.GetComponent<CompanionProgression>();
            var archetypeController = _companion.GetArchetypeController();
            
            if (progression == null || archetypeController == null) return;
            
            var archetype = archetypeController.CurrentArchetypeClass;
            if (archetype == ArchetypeClass.None) return;
            
            int level = progression.Level;
            
            sb.AppendLine($"<b><color={gold}>[ Abilities ]</color></b>");
            
            // Get unlocked and locked abilities
            var unlockedAbilities = AbilityUnlockSystem.GetUnlockedAbilities(archetype, level);
            var lockedAbilities = AbilityUnlockSystem.GetLockedAbilities(archetype, level);
            var nextUnlock = AbilityUnlockSystem.GetNextUnlock(archetype, level);
            
            // Show unlock progress
            int totalAbilities = unlockedAbilities.Count + lockedAbilities.Count;
            sb.AppendLine($"<color={white}>Abilities Unlocked:</color> <color={green}>{unlockedAbilities.Count}</color><color=#888888>/{totalAbilities}</color>");
            
            // Show hybrid status
            if (!AbilityUnlockSystem.HasUnlockedHybrid(level))
            {
                sb.AppendLine($"<color=#CCCCCC>Hybrid Class:</color> <color=#FFAA44>Locked</color> <color=#AAAAAA>(unlocks at L{AbilityUnlockSystem.HYBRID_UNLOCK_LEVEL})</color>");
            }
            else if (archetypeController.HasSubArchetype)
            {
                string hybridName = archetypeController.GetArchetypeDisplayName();
                sb.AppendLine($"<color=#CCCCCC>Hybrid Class:</color> <color={purple}>{hybridName}</color>");
                
                // Show if hybrid ability is unlocked
                if (!AbilityUnlockSystem.HasUnlockedHybridAbility(level))
                {
                    var hybrid = archetypeController.GetHybridDefinition();
                    if (hybrid != null)
                    {
                        sb.AppendLine($"<color=#CCCCCC>Hybrid Ability:</color> <color=#FFAA44>{hybrid.SpecialAbilityName}</color> <color=#AAAAAA>(unlocks at L{AbilityUnlockSystem.HYBRID_ABILITY_LEVEL})</color>");
                    }
                }
                else
                {
                    var hybrid = archetypeController.GetHybridDefinition();
                    if (hybrid != null)
                    {
                        sb.AppendLine($"<color=#CCCCCC>Hybrid Ability:</color> <color={green}>{hybrid.SpecialAbilityName}</color> <color=#66FF66>Ready!</color>");
                    }
                }
            }
            else
            {
                sb.AppendLine($"<color=#CCCCCC>Hybrid Class:</color> <color=#FFDD44>Available!</color> <color=#AAAAAA>(equip backup weapons to unlock)</color>");
            }
            
            sb.AppendLine();
            
            // === NEXT UNLOCK SECTION ===
            if (nextUnlock != null)
            {
                sb.AppendLine($"<color={gold}>Next Unlock:</color> <color=#FFAA44>Level {nextUnlock.UnlockLevel}</color>");
                sb.AppendLine($"  <color={purple}>{nextUnlock.DisplayName}</color>");
                sb.AppendLine($"  <color=#CCCCCC>{nextUnlock.Description}</color>");
                sb.AppendLine();
            }
            else if (level >= 100)
            {
                sb.AppendLine($"<color={gold}>All abilities unlocked!</color> <color=#FFAA44>Ã¢Ëœâ€¦ MASTERED Ã¢Ëœâ€¦</color>");
                sb.AppendLine();
            }
            
            // === UNLOCKED ABILITIES ===
            if (unlockedAbilities.Count > 0)
            {
                sb.AppendLine($"<color={green}>Unlocked Abilities:</color>");
                foreach (var ability in unlockedAbilities)
                {
                    string typeTag = ability.IsPassive ? "<color=#AAAAAA>[Passive]</color>" : "";
                    sb.AppendLine($"  <color={white}>Ã¢â‚¬Â¢ {ability.DisplayName}</color> {typeTag}");
                    // Show full description wrapped - no truncation
                    sb.AppendLine($"    <color=#CCCCCC>{ability.Description}</color>");
                }
                sb.AppendLine();
            }
            
            // === UPCOMING ABILITIES (next 3) ===
            var upcomingAbilities = lockedAbilities.Take(3).ToList();
            if (upcomingAbilities.Count > 0)
            {
                sb.AppendLine($"<color=#AAAAAA>Upcoming Abilities:</color>");
                foreach (var ability in upcomingAbilities)
                {
                    int levelsAway = ability.UnlockLevel - level;
                    sb.AppendLine($"  <color=#BBBBBB>Ã¢â‚¬Â¢ L{ability.UnlockLevel}: {ability.DisplayName}</color>");
                    sb.AppendLine($"    <color=#AAAAAA>({levelsAway} levels away)</color>");
                }
                
                if (lockedAbilities.Count > 3)
                {
                    sb.AppendLine($"  <color=#AAAAAA>...and {lockedAbilities.Count - 3} more abilities to unlock!</color>");
                }
            }
            
            sb.AppendLine();
        }
        
        /// <summary>
        /// Truncates a description to fit in the UI.
        /// </summary>
        private string TruncateDescription(string description, int maxLength)
        {
            if (string.IsNullOrEmpty(description)) return "";
            if (description.Length <= maxLength) return description;
            return description.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Builds the archetype skills section showing skill levels from ability use.
        /// </summary>
        private void BuildArchetypeSkillsSection(StringBuilder sb, string gold, string white, string green, string purple)
        {
            var skillSystem = _companion.GetComponent<ArchetypeSkillSystem>();
            var archetypeController = _companion.GetArchetypeController();
            
            if (skillSystem == null || archetypeController == null) return;
            
            var archetype = archetypeController.CurrentArchetypeClass;
            if (archetype == ArchetypeClass.None) return;
            
            sb.AppendLine($"<b><color={gold}>[ Archetype Skills ]</color></b>");
            sb.AppendLine($"<color=#CCCCCC>Skills level up through ability use (max: 100)</color>");
            sb.AppendLine();
            
            // Get skills for current archetype
            string archetypeName = archetype.ToString();
            var archetypeSkills = skillSystem.GetSkillsForArchetype(archetypeName).ToList();
            
            bool hasLeveledSkills = false;
            
            foreach (var skillData in archetypeSkills)
            {
                // Only show skills with some progress
                if (skillData.Level > 1 || skillData.CurrentXP > 0)
                {
                    hasLeveledSkills = true;
                    
                    string skillName = GetArchetypeSkillDisplayName(skillData.Skill);
                    float effectiveness = skillSystem.GetSkillEffectiveness(skillData.Skill);
                    float effectivenessPercent = (effectiveness - 1f) * 100f;
                    
                    // Color based on level
                    string levelColor = GetArchetypeSkillLevelColor(skillData.Level);
                    
                    // Progress to next level
                    float progressPercent = skillData.Progress * 100f;
                    
                    sb.Append($"  <color={white}>{skillName}:</color> ");
                    sb.Append($"<color={levelColor}>Lv {skillData.Level}</color>");
                    
                    // Show effectiveness bonus if > 0
                    if (effectivenessPercent > 0.5f)
                    {
                        sb.Append($" <color={green}>(+{effectivenessPercent:F0}%)</color>");
                    }
                    
                    sb.AppendLine();
                    
                    // Show progress bar for non-maxed skills
                    if (skillData.Level < 100)
                    {
                        sb.AppendLine($"    <color=#AAAAAA>[{GetArchetypeSkillProgressBar(progressPercent)}] {progressPercent:F0}%</color>");
                    }
                    else
                    {
                        sb.AppendLine($"    <color=#FFD700>Ã¢Ëœâ€¦ MASTERED Ã¢Ëœâ€¦</color>");
                    }
                }
            }
            
            if (!hasLeveledSkills)
            {
                sb.AppendLine($"<color=#CCCCCC>Use abilities in combat to level skills!</color>");
                sb.AppendLine();
                
                // Show what skills can be leveled
                sb.AppendLine($"<color=#AAAAAA>Available skills to train:</color>");
                int shown = 0;
                foreach (var skillData in archetypeSkills)
                {
                    if (shown >= 4) break; // Only show first 4
                    string skillName = GetArchetypeSkillDisplayName(skillData.Skill);
                    sb.AppendLine($"  <color=#BBBBBB>Ã¢â‚¬Â¢ {skillName}</color>");
                    shown++;
                }
                if (archetypeSkills.Count > 4)
                {
                    sb.AppendLine($"  <color=#BBBBBB>...and {archetypeSkills.Count - 4} more</color>");
                }
            }
            else
            {
                // Show total skill investment
                int totalPoints = skillSystem.GetTotalSkillPointsInArchetype(archetypeName);
                if (totalPoints > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<color=#CCCCCC>Total {archetypeName} Skill Points: {totalPoints}</color>");
                }
            }
            
            sb.AppendLine();
        }
        
        /// <summary>
        /// Gets a display-friendly name for an archetype skill.
        /// </summary>
        private string GetArchetypeSkillDisplayName(ArchetypeSkillSystem.ArchetypeSkill skill)
        {
            string name = skill.ToString();
            
            // Remove archetype prefix (e.g., "Tank_Taunt" -> "Taunt")
            int underscore = name.IndexOf('_');
            if (underscore >= 0 && underscore < name.Length - 1)
            {
                name = name.Substring(underscore + 1);
            }
            
            // Add spaces before capitals (e.g., "HolySmite" -> "Holy Smite")
            var result = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(c);
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Returns a color string based on archetype skill level.
        /// </summary>
        private string GetArchetypeSkillLevelColor(int level)
        {
            if (level >= 100) return "#FFD700"; // Gold - mastered
            if (level >= 75) return "#FFAA44";  // Orange - master
            if (level >= 50) return "#66FF66";  // Bright green - expert
            if (level >= 25) return "#88DDAA";  // Light green - journeyman
            return "#FFFFFF"; // White - novice
        }
        
        /// <summary>
        /// Creates a progress bar for archetype skills.
        /// </summary>
        private string GetArchetypeSkillProgressBar(float percent)
        {
            int filled = Mathf.RoundToInt(percent / 10f);
            filled = Mathf.Clamp(filled, 0, 10);
            int empty = 10 - filled;
            return new string('|', filled) + new string('.', empty);
        }

        private void BuildCombatStats(StringBuilder sb, string gold, string white, string green, string red, string blue)
        {
            var character = _companion.GetCharacter();
            if (character == null) return;

            // Use centralized stat calculator for consistent values
            var calc = new CompanionStatCalculator(_companion);

            sb.AppendLine($"<b><color={gold}>[ Vital Stats ]</color></b>");
   
            // Health - ALWAYS use CompanionStats values which are the authoritative source
            float maxHealth = calc.MaxHealth;
            float currentHealth = calc.CurrentHealth;
            
            // Clamp current to max for display - prevents showing 420/380
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            float healthPercent = maxHealth > 0 ? (currentHealth / maxHealth) * 100f : 0f;
            string healthColor = healthPercent > 50 ? green : (healthPercent > 25 ? "#B8860B" : red);
       
            sb.AppendLine($"<color={white}>Health:</color> <color={healthColor}>{currentHealth:F0}</color> / <color={green}>{maxHealth:F0}</color>");
            
            // Show health breakdown with all sources
            BuildStatBreakdown(sb, "Health", calc.BaseMaxHealth, calc.AttributeHealthBonus, calc.FoodHealthBonus, 0, maxHealth);
            sb.AppendLine($"<color=#AAAAAA>[{GetHealthBar(healthPercent)}]</color>");

            // Stamina - using calculator which now delegates to CompanionStats
            float maxStamina = calc.MaxStamina;
            float currentStamina = calc.CurrentStamina;
            float stamPercent = maxStamina > 0 ? (currentStamina / maxStamina) * 100f : 0f;
            sb.AppendLine($"<color={white}>Stamina:</color> <color=#B8860B>{currentStamina:F0}</color> / <color=#B8860B>{maxStamina:F0}</color>");
            
            // Show stamina breakdown
            BuildStatBreakdown(sb, "Stamina", calc.BaseMaxStamina, calc.AttributeStaminaBonus, calc.FoodStaminaBonus, 0, maxStamina);
            sb.AppendLine($"<color=#AAAAAA>[{GetStaminaBar(stamPercent)}]</color>");
            
            // Eitr - using calculator which now delegates to CompanionStats
            float maxEitr = calc.MaxEitr;
            if (maxEitr > 0)
            {
                float currentEitr = calc.CurrentEitr;
                float eitrPercent = maxEitr > 0 ? (currentEitr / maxEitr) * 100f : 0f;
                sb.AppendLine($"<color={white}>Eitr:</color> <color={blue}>{currentEitr:F0}</color> / <color={blue}>{maxEitr:F0}</color>");
                
                // Show eitr breakdown
                BuildStatBreakdown(sb, "Eitr", calc.BaseMaxEitr, calc.AttributeEitrBonus, calc.FoodEitrBonus, 0, maxEitr);
                sb.AppendLine($"<color=#AAAAAA>[{GetEitrBar(eitrPercent)}]</color>");
            }
            
            // Health regen info (from food AND status effects)
            float totalRegen = calc.TotalHealthRegen;
            if (totalRegen > 0)
            {
                sb.Append($"<color={white}>Health Regen:</color> <color={green}>+{totalRegen:F1}/s</color>");
                
                // Build regen breakdown
                var regenParts = new List<string>();
                if (calc.FoodHealthRegen > 0)
                    regenParts.Add($"<color=#66FF66>food +{calc.FoodHealthRegen:F1}</color>");
                if (calc.StatusEffectHealthRegen > 0)
                    regenParts.Add($"<color=#6699FF>buffs +{calc.StatusEffectHealthRegen:F1}</color>");
                    
                if (regenParts.Count > 0)
                    sb.Append($" <color=#AAAAAA>({string.Join(", ", regenParts)})</color>");
                sb.AppendLine();
            }
            
            sb.AppendLine();
            
            // === COMBAT MODIFIERS SECTION ===
            sb.AppendLine($"<b><color={gold}>[ Combat Modifiers ]</color></b>");
            
            // Damage multiplier breakdown
            float attrDmgMult = calc.AttributeDamageMultiplier;
            float buffDmgMult = calc.StatusEffectDamageBonus;
            float totalDmgMult = calc.EffectiveDamageMultiplier;
            
            if (totalDmgMult != 1f)
            {
                float bonusPercent = (totalDmgMult - 1f) * 100f;
                sb.AppendLine($"<color={white}>Damage Bonus:</color> <color=#FFAA44>+{bonusPercent:F0}%</color>");
                
                // Show breakdown
                var dmgParts = new List<string>();
                if (attrDmgMult > 1f)
                    dmgParts.Add($"str +{(attrDmgMult - 1f) * 100:F0}%");
                if (buffDmgMult > 1f)
                    dmgParts.Add($"<color=#6699FF>buffs +{(buffDmgMult - 1f) * 100:F0}%</color>");
                    
                if (dmgParts.Count > 0)
                    sb.AppendLine($"  <color=#AAAAAA>({string.Join(", ", dmgParts)})</color>");
            }
            else
            {
                sb.AppendLine($"<color={white}>Damage Bonus:</color> <color=#AAAAAA>None</color>");
            }
            
            // Damage reduction from status effects
            float damageReduction = calc.EffectiveDamageReduction;
            if (damageReduction < 1f)
            {
                float reductionPercent = (1f - damageReduction) * 100f;
                sb.AppendLine($"<color={white}>Damage Reduction:</color> <color=#66FF66>{reductionPercent:F0}%</color> <color=#6699FF>(from buffs)</color>");
            }
            else
            {
                sb.AppendLine($"<color={white}>Damage Reduction:</color> <color=#AAAAAA>None</color>");
            }

            // Weapon damage with multiplier
            var equipData = _companion.GetComponent<CompanionEquipmentData>();
            if (equipData != null && equipData.WeaponItem != null)
            {
                float baseDamage = equipData.WeaponDamage.GetTotalDamage();
                float multiplier = calc.EffectiveDamageMultiplier;
                float totalDamage = baseDamage * multiplier;
                
                sb.AppendLine($"<color={white}>Weapon Damage:</color> <color={green}>{totalDamage:F0}</color>");
                if (multiplier != 1f)
                {
                    sb.AppendLine($"  <color=#AAAAAA>(base {baseDamage:F0} x {multiplier:F2})</color>");
                }
            }

            sb.AppendLine();
        }
        
        /// <summary>
        /// Builds a stat breakdown showing base + all bonus sources.
        /// Only shows sources that have non-zero values.
        /// </summary>
        private void BuildStatBreakdown(StringBuilder sb, string statName, float baseValue, float attrBonus, float foodBonus, float buffBonus, float total)
        {
            // Calculate any "other" bonus (from scale/biome multipliers, etc.)
            float knownTotal = baseValue + attrBonus + foodBonus + buffBonus;
            float otherBonus = total - knownTotal;
            
            // Only show breakdown if there are bonuses
            bool hasBonuses = attrBonus > 0 || foodBonus > 0 || buffBonus > 0 || otherBonus > 1f;
            if (!hasBonuses) return;
            
            var parts = new List<string>();
            parts.Add($"base {baseValue:F0}");
            
            if (otherBonus > 1f)
                parts.Add($"<color=#6699FF>size +{otherBonus:F0}</color>");
            if (attrBonus > 0)
                parts.Add($"<color=#66FF66>attr +{attrBonus:F0}</color>");
            if (foodBonus > 0)
                parts.Add($"<color=#FFAA44>food +{foodBonus:F0}</color>");
            if (buffBonus > 0)
                parts.Add($"<color=#6699FF>buff +{buffBonus:F0}</color>");
            
            sb.AppendLine($"  <color=#AAAAAA>({string.Join(", ", parts)})</color>");
        }

        private void BuildAttributeStats(StringBuilder sb, string gold, string white, string green)
        {
            var progression = _companion.GetComponent<CompanionProgression>();
            if (progression == null) return;

            int unspent = progression.UnspentAttributePoints;
            string unspentColor = unspent > 0 ? "#FFAA44" : "#AAAAAA";
            sb.AppendLine($"<b><color={gold}>[ Attributes ]</color></b> <color={unspentColor}>({unspent} points available)</color>");
            
            int str = progression.GetAttributeValue(CompanionProgression.AttributeType.Strength);
            int spd = progression.GetAttributeValue(CompanionProgression.AttributeType.Speed);
            int hp = progression.GetAttributeValue(CompanionProgression.AttributeType.Health);
            int end = progression.GetAttributeValue(CompanionProgression.AttributeType.Endurance);
            int intel = progression.GetAttributeValue(CompanionProgression.AttributeType.Intelligence);
            
            sb.AppendLine($"<color={white}>Strength:</color> <color={green}>{str}</color> <color=#AAAAAA>(+{str}% melee dmg)</color>");
            sb.AppendLine($"<color={white}>Speed:</color> <color={green}>{spd}</color> <color=#AAAAAA>(+{spd * 0.5f:F1}% move speed)</color>");
            sb.AppendLine($"<color={white}>Health:</color> <color={green}>{hp}</color> <color=#AAAAAA>(+{hp * 5} max HP)</color>");
            sb.AppendLine($"<color={white}>Endurance:</color> <color={green}>{end}</color> <color=#AAAAAA>(+{end * 3} max stamina)</color>");
            sb.AppendLine($"<color={white}>Intelligence:</color> <color={green}>{intel}</color> <color=#AAAAAA>(+{intel * 3} max eitr)</color>");
            
            // Display Luck stat
            var luck = _companion.GetComponent<CompanionLuck>();
            if (luck != null)
            {
                sb.AppendLine();
                int luckValue = luck.BaseLuck;
                string luckTier = luck.LuckTier;
                float maxBonus = luck.GetMaxBonusPercent();
                
                // Color code luck based on tier
                string luckColor;
                if (luckValue >= 90) luckColor = "#FFD700"; // Gold - Blessed
                else if (luckValue >= 75) luckColor = "#66FF66"; // Bright green - Lucky
                else if (luckValue >= 60) luckColor = "#88DDAA"; // Light green - Fortunate
                else if (luckValue >= 40) luckColor = "#FFFFFF"; // White - Average
                else if (luckValue >= 25) luckColor = "#FFAA44"; // Orange - Unlucky
                else if (luckValue >= 10) luckColor = "#FF8866"; // Light red - Cursed
                else luckColor = "#FF4444"; // Red - Doomed
                
                sb.AppendLine($"<color={white}>Luck:</color> <color={luckColor}>{luckValue}</color> <color=#AAAAAA>({luckTier})</color>");
                sb.AppendLine($"<color=#AAAAAA>  Level scaling: up to +{maxBonus:F1}% at max level</color>");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// Creates a text-based progress bar using ASCII characters that TMP can render.
        /// Uses pipe '|' for filled and dot '.' for empty.
        /// </summary>
        private string GetHealthBar(float percent)
        {
            int filled = Mathf.RoundToInt(percent / 5f);
            filled = Mathf.Clamp(filled, 0, 20);
            int empty = 20 - filled;
            string bar = "<color=#FF6666>" + new string('|', filled) + "</color>";
            if (empty > 0) bar += "<color=#666666>" + new string('.', empty) + "</color>";
            return bar;
        }

        private string GetStaminaBar(float percent)
        {
            int filled = Mathf.RoundToInt(percent / 5f);
            filled = Mathf.Clamp(filled, 0, 20);
            int empty = 20 - filled;
            string bar = "<color=#FFDD44>" + new string('|', filled) + "</color>";
            if (empty > 0) bar += "<color=#666666>" + new string('.', empty) + "</color>";
            return bar;
        }

        private string GetEitrBar(float percent)
        {
            int filled = Mathf.RoundToInt(percent / 5f);
            filled = Mathf.Clamp(filled, 0, 20);
            int empty = 20 - filled;
            string bar = "<color=#6699FF>" + new string('|', filled) + "</color>";
            if (empty > 0) bar += "<color=#666666>" + new string('.', empty) + "</color>";
            return bar;
        }

  private void BuildEquipmentStats(StringBuilder sb, string gold, string white, string green)
        {
            var equipData = _companion.GetComponent<CompanionEquipmentData>();
            if (equipData == null) return;
            
            // Use centralized stat calculator
            var calc = new CompanionStatCalculator(_companion);

            sb.AppendLine($"<b><color={gold}>[ Equipment & Defense ]</color></b>");
            
            // Total armor with detailed breakdown
            float totalArmor = calc.TotalArmor;
            float equipArmor = calc.EquipmentArmor;
            float attrArmor = calc.AttributeArmorBonus;
            float buffArmor = calc.StatusEffectArmorBonus;
            
            sb.AppendLine($"<color={white}>Total Armor:</color> <color={green}>{totalArmor:F0}</color>");
            
            // Show armor breakdown
            var armorParts = new List<string>();
            if (equipArmor > 0)
                armorParts.Add($"gear {equipArmor:F0}");
            if (attrArmor > 0)
                armorParts.Add($"<color=#66FF66>toughness +{attrArmor:F0}</color>");
            if (buffArmor > 0)
                armorParts.Add($"<color=#6699FF>buffs +{buffArmor:F0}</color>");
            
            if (armorParts.Count > 0)
                sb.AppendLine($"  <color=#AAAAAA>({string.Join(", ", armorParts)})</color>");
  
            if (equipData.MovementSpeedModifier != 0f)
            {
                string speedColor = equipData.MovementSpeedModifier >= 0f ? green : "#FF6666";
                string speedSign = equipData.MovementSpeedModifier >= 0f ? "+" : "";
                sb.AppendLine($"<color={white}>Movement Speed:</color> <color={speedColor}>{speedSign}{equipData.MovementSpeedModifier * 100:F0}%</color> <color=#AAAAAA>(from gear)</color>");
            }

            if (equipData.ShieldItem != null)
            {
                sb.AppendLine($"<color={white}>Shield:</color> {GetItemDisplayName(equipData.ShieldItem)}");
                sb.AppendLine($"  <color=#AAAAAA>Block Power: {equipData.ShieldBlockPower:F0}</color>");
            }

            sb.AppendLine();
        }
        
        /// <summary>
        /// Gets a display-friendly name for an item.
        /// </summary>
        private string GetItemDisplayName(ItemDrop.ItemData item)
        {
            if (item == null) return "None";
            
            string name = Localization.instance?.Localize(item.m_shared.m_name) ?? item.m_shared.m_name;
            
            // Add quality indicator if upgraded
            if (item.m_quality > 1)
            {
                name += $" <color=#FFAA44>(+{item.m_quality - 1})</color>";
            }
            
            return name;
        }

        private void BuildSkillStats(StringBuilder sb, string gold)
        {
   var skills = _companion.GetComponent<CompanionSkills>();
  if (skills == null) return;

     sb.AppendLine($"<b><color={gold}>[ Skills ]</color></b>");
     
       // Get all skills with levels > 0
     var allSkills = skills.GetAllSkillsWithProgress();
            bool hasSkills = false;
       
    foreach (var skillInfo in allSkills)
 {
     if (skillInfo.level > 0 || skillInfo.accumulator > 0.5f)
        {
 hasSkills = true;
        string skillName = skillInfo.displayName;
       float progress = skillInfo.progressPercent;
        
          // Color based on level
       string levelColor = GetSkillLevelColor(skillInfo.level);
     
// Show level and progress bar
              sb.AppendLine($"<color=#CCCCCC>{skillName}:</color> <color={levelColor}>{skillInfo.level:F0}</color> " +
          $"<color=#AAAAAA>[{GetProgressBar(progress)}]</color>");
 }
     }
     
     if (!hasSkills)
      {
  sb.AppendLine("<color=#CCCCCC>No skills trained yet.</color>");
 }
       
// Show total skill levels
float totalLevels = skills.GetTotalSkillLevels();
    if (totalLevels > 0)
     {
   sb.AppendLine($"<color=#CCCCCC>Total Skill Levels: {totalLevels:F0}</color>");
  }

        sb.AppendLine();
        }
        
        /// <summary>
        /// Returns a color string based on skill level.
      /// </summary>
        private string GetSkillLevelColor(float level)
  {
     if (level >= 75) return "#FFD700"; // Gold - master
           if (level >= 50) return "#FFAA44";  // Orange - expert
          if (level >= 25) return "#66FF66";  // Bright green - journeyman
      return "#FFFFFF"; // White - novice
        }
        
   /// <summary>
     /// Creates a simple text progress bar.
/// </summary>
  private string GetProgressBar(float percent)
        {
            int filledCount = Mathf.RoundToInt(percent / 10f);
            int emptyCount = 10 - filledCount;
      return new string('|', filledCount) + new string('.', emptyCount);
        }

        private void BuildKillStats(StringBuilder sb, string gold, string white, string red, string gray)
   {
var killTracker = _companion.GetComponent<CompanionKillTracker>();
     
            sb.AppendLine($"<b><color={gold}>[ Kill Statistics ]</color></b>");

    if (killTracker != null)
{
      sb.AppendLine($"<color={white}>Total Kills:</color> <color={red}>{killTracker.TotalKills}</color>");
            sb.AppendLine($"<color={white}>Deaths:</color> <color={gray}>{killTracker.Deaths}</color>");
    
    float kdRatio = killTracker.Deaths > 0 ? (float)killTracker.TotalKills / killTracker.Deaths : killTracker.TotalKills;
       sb.AppendLine($"<color={white}>K/D Ratio:</color> <color={gold}>{kdRatio:F2}</color>");
    
  sb.AppendLine();
      
         var topKills = killTracker.GetTopKills(5);
      if (topKills.Count > 0)
 {
            sb.AppendLine($"<color={gold}>Top Kills:</color>");
          foreach (var kvp in topKills)
{
          string creatureName = LocalizeCreatureName(kvp.Key);
    sb.AppendLine($"  <color={gray}>{creatureName}:</color> <color={red}>{kvp.Value}</color>");
          }
    }
         }
 else
   {
          sb.AppendLine($"<color={gray}>Kill tracking not available.</color>");
            }

            sb.AppendLine();
        }

    private void BuildInventoryStats(StringBuilder sb, string gold, string white, string gray)
        {
      var inventory = _companion.GetInventory();
 if (inventory == null) return;

         sb.AppendLine($"<b><color={gold}>[ Inventory ]</color></b>");
          sb.AppendLine($"<color={white}>Total Weight:</color> <color={gray}>{inventory.GetTotalWeight():F1}</color>");

 var storageItems = inventory.GetAllStorageItems();
            int itemCount = storageItems?.Count ?? 0;
            sb.AppendLine($"<color={white}>Storage Items:</color> <color={gray}>{itemCount}</color>");

     sb.AppendLine();
        }

    private void BuildStatusInfo(StringBuilder sb, string gold, string white, string gray, string blue)
 {
            sb.AppendLine($"<b><color={gold}>[ Current Status ]</color></b>");

         string followStatus = _companion.IsFollowing ? "Following" : "Staying";
  string followColor = _companion.IsFollowing ? blue : gray;
 sb.AppendLine($"<color={white}>Mode:</color> <color={followColor}>{followStatus}</color>");

            var idleBehavior = _companion.GetComponent<CompanionIdleBehavior>();
            if (idleBehavior != null)
            {
       sb.AppendLine($"<color={white}>Activity:</color> <color={gray}>{idleBehavior.GetIdleStateDescription()}</color>");
  sb.AppendLine($"<color={white}>Weapons Holstered:</color> <color={gray}>{(idleBehavior.AreWeaponsHolstered ? "Yes" : "No")}</color>");
   }

            var combatMovement = _companion.GetComponent<CompanionCombatMovement>();
  if (combatMovement != null)
            {
   string combatStatus = combatMovement.IsInCombat ? "<color=#8B0000>In Combat</color>" : "<color=#228B22>At Peace</color>";
   sb.AppendLine($"<color={white}>Combat:</color> {combatStatus}");
   }
   
            // Show active status effects with full details
            var calc = new CompanionStatCalculator(_companion);
            var activeEffects = calc.GetActiveStatusEffects();
            if (activeEffects.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"<b><color={gold}>[ Active Buffs & Effects ]</color></b>");
                
                foreach (var effect in activeEffects)
                {
                    string colorHex = ColorUtility.ToHtmlStringRGB(effect.color);
                    
                    // Format duration nicely
                    string timeStr;
                    if (effect.duration > 60)
                        timeStr = $"{effect.duration / 60:F0}m {effect.duration % 60:F0}s";
                    else
                        timeStr = $"{effect.duration:F0}s";
                    
                    sb.AppendLine($"  <color=#{colorHex}>Ã¢â‚¬Â¢ {effect.name}</color> - <color=#AAAAAA>{timeStr}</color>");
                    
                    // Show effect description - this tells the player what the buff does
                    if (!string.IsNullOrEmpty(effect.description))
                    {
                        sb.AppendLine($"    <color=#CCCCCC>{effect.description}</color>");
                    }
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"<b><color={gold}>[ Active Buffs & Effects ]</color></b>");
                sb.AppendLine($"<color=#AAAAAA>No active buffs or effects.</color>");
            }
            
            // Show active food effects
            var consumables = _companion.GetComponent<CompanionConsumables>();
            if (consumables != null)
            {
                var activeFoods = consumables.GetActiveFoodEffects();
                if (activeFoods != null && activeFoods.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<b><color={gold}>[ Active Food Effects ]</color></b>");
                    
                    foreach (var food in activeFoods)
                    {
                        string foodName = Localization.instance?.Localize(food.FoodName) ?? food.FoodName;
                        float timeRemaining = food.TimeRemaining;
                        string timeStr = timeRemaining > 60 ? $"{timeRemaining / 60:F0}m" : $"{timeRemaining:F0}s";
                        
                        sb.AppendLine($"  <color=#FFAA44>Ã¢â‚¬Â¢ {foodName}</color> - <color=#AAAAAA>{timeStr}</color>");
                        
                        // Show what bonuses this food provides
                        var bonuses = new List<string>();
                        if (food.HealthBonus > 0)
                            bonuses.Add($"+{food.HealthBonus:F0} HP");
                        if (food.StaminaBonus > 0)
                            bonuses.Add($"+{food.StaminaBonus:F0} Stam");
                        if (food.EitrBonus > 0)
                            bonuses.Add($"+{food.EitrBonus:F0} Eitr");
                        if (food.HealthRegen > 0)
                            bonuses.Add($"+{food.HealthRegen:F1}/s regen");
                        
                        if (bonuses.Count > 0)
                        {
                            sb.AppendLine($"    <color=#CCCCCC>{string.Join(", ", bonuses)}</color>");
                        }
                    }
                }
            }
        }

        private string LocalizeCreatureName(string prefabName)
  {
            if (string.IsNullOrEmpty(prefabName)) return "Unknown";

     string localized = Localization.instance?.Localize($"$enemy_{prefabName.ToLower()}");
            if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("$"))
            {
                return localized;
            }

     string cleaned = prefabName
            .Replace("_", " ")
                .Replace("(Clone)", "")
         .Trim();

        if (cleaned.Length > 0)
      {
var words = cleaned.Split(' ');
        for (int i = 0; i < words.Length; i++)
  {
      if (words[i].Length > 0)
      {
    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
  }
         }
 cleaned = string.Join(" ", words);
   }

   return cleaned;
      }

        #endregion
    }
}
