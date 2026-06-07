using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Centralized manager for NPC panel element visibility and styling.
    /// Ensures consistent element visibility and font styling across different screen modes.
    /// ALL header text styling should go through this class to ensure consistency.
    ///
    /// Performance: element lookups are cached after the first Find() call so that
    /// repeated ConfigureForMode() calls avoid O(n) Transform.Find() on the 575-node
    /// codegen hierarchy.
    /// </summary>
    public static class PanelElementManager
    {
        /// <summary>
        /// Screen modes for the NPC panel system.
        /// </summary>
 public enum ScreenMode
        {
            /// <summary>Admin configuration panel (Shift+E) - shows tabs, detail inputs, etc.</summary>
            AdminConfig,
   
    /// <summary>Player quest interaction (E on quest NPC) - shows quest list, details, accept/cancel.</summary>
       QuestInteraction,
      
  /// <summary>Quest config screen (admin tab) - shows quest configuration UI.</summary>
            QuestConfig,
 
      /// <summary>Info NPC screen (admin tab) - shows info configuration UI.</summary>
          InfoConfig,
   
     /// <summary>Player info view (E on info NPC) - shows info text to player.</summary>
         InfoInteraction,
          
            /// <summary>Dressing room screen (admin) - shows equipment slots for NPC.</summary>
     DressingRoom,

            /// <summary>Dialogue editor screen (admin tab) - shows dialogue authoring UI.</summary>
            DialogueEditor,

            /// <summary>Trader config screen (admin tab) - shows trader profile + trades editor.</summary>
            TraderConfig,

            /// <summary>Banker config screen (admin tab) - shows banker profile config.</summary>
            BankerConfig,

            /// <summary>Generic Marketplace-module admin tabs (stub UI for now).</summary>
            GamblerConfig,
            MarketplaceConfig,
            AchievementConfig,
            LeaderboardConfig,
            MailConfig,
            TransmogConfig,

            /// <summary>Hidden - panel is closed.</summary>
            Hidden
        }

        /// <summary>
      /// Configures all panel elements for the specified screen mode.
   /// </summary>
    /// <param name="panelRoot">The root transform of the NpcMainPanel.</param>
        /// <param name="mode">The screen mode to configure for.</param>
        /// <param name="npcName">Optional NPC name to display in header.</param>
        public static void ConfigureForMode(Transform panelRoot, ScreenMode mode, string npcName = null)
        {
            if (panelRoot == null)
            {
     Debug.LogWarning("[PanelElementManager] Cannot configure - panelRoot is null");
    return;
      }

         Debug.Log($"[PanelElementManager] Configuring panel for mode: {mode}, NPC: {npcName ?? "none"}");

   switch (mode)
   {
             case ScreenMode.AdminConfig:
    ConfigureAdminMode(panelRoot, npcName);
    break;
         
 case ScreenMode.QuestInteraction:
      ConfigureQuestInteractionMode(panelRoot, npcName);
         break;
      
             case ScreenMode.QuestConfig:
        ConfigureQuestConfigMode(panelRoot, npcName);
    break;
     
  case ScreenMode.InfoConfig:
        ConfigureInfoConfigMode(panelRoot, npcName);
           break;
    
    case ScreenMode.InfoInteraction:
         ConfigureInfoInteractionMode(panelRoot, npcName);
           break;
      
            case ScreenMode.DressingRoom:
  ConfigureDressingRoomMode(panelRoot, npcName);
          break;

            case ScreenMode.DialogueEditor:
                ConfigureDialogueEditorMode(panelRoot, npcName);
                break;

            case ScreenMode.TraderConfig:
                ConfigureTraderConfigMode(panelRoot, npcName);
                break;

            case ScreenMode.BankerConfig:
                ConfigureBankerConfigMode(panelRoot, npcName);
                break;

            case ScreenMode.GamblerConfig: ConfigureModuleMode(panelRoot, "GamblerConfigScreen", "GamblerApplyButton", "Gambler Config"); break;
            case ScreenMode.MarketplaceConfig: ConfigureModuleMode(panelRoot, "MarketplaceConfigScreen", "MarketplaceApplyButton", "Marketplace Config"); break;
            case ScreenMode.AchievementConfig: ConfigureModuleMode(panelRoot, "AchievementConfigScreen", "AchievementApplyButton", "Achievement Config"); break;
            case ScreenMode.LeaderboardConfig: ConfigureModuleMode(panelRoot, "LeaderboardConfigScreen", "LeaderboardApplyButton", "Leaderboard Config"); break;
            case ScreenMode.MailConfig: ConfigureModuleMode(panelRoot, "MailConfigScreen", "MailApplyButton", "Mail Config"); break;
            case ScreenMode.TransmogConfig: ConfigureModuleMode(panelRoot, "TransmogConfigScreen", "TransmogApplyButton", "Transmog Config"); break;

       case ScreenMode.Hidden:
                HideAllElements(panelRoot);
     break;
    }
        }

        /// <summary>
   /// Admin configuration mode (Shift+E).
        /// </summary>
        private static void ConfigureAdminMode(Transform panelRoot, string npcName)
      {
          // SHOW core elements
            SetElementActive(panelRoot, "Header", true);
 SetElementActive(panelRoot, "SizeButton", true);
  
    // SHOW admin elements
            SetElementActive(panelRoot, "TabsBar", true);
            SetElementActive(panelRoot, "DetailPanel", true);
  SetElementActive(panelRoot, "PageControls", true);
   SetElementActive(panelRoot, "TabContent", true);
 
       // HIDE unused elements
      SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "SearchInput", false);
       
            // HIDE screen-specific elements
            SetElementActive(panelRoot, "QuestScreenContainer", false);
    SetElementActive(panelRoot, "QuestConfigScreen", false);
            SetElementActive(panelRoot, "InfoNpcScreen", false);
            SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            // TraderConfigScreen + TraderApplyButton must hide in EVERY non-trader mode — otherwise switching
            // away from the trader tab (or hitting back from it) leaves the trader form overlapping the
            // next screen (was: AdminConfig's DetailPanel showed THROUGH the lingering trader form).
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);
   
      // Set header text with admin style
            SetHeaderText(panelRoot, $"Configure NPC: {npcName ?? "NPC"}", UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
    /// Quest interaction mode (E on quest NPC with profile).
 /// </summary>
        private static void ConfigureQuestInteractionMode(Transform panelRoot, string npcName)
        {
        // SHOW core elements
      SetElementActive(panelRoot, "Header", true);
            SetElementActive(panelRoot, "SizeButton", true);
    
    // SHOW quest interaction elements
            SetElementActive(panelRoot, "QuestScreenContainer", true);
      
            // HIDE admin elements
            SetElementActive(panelRoot, "TabsBar", false);
  SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "PageControls", false);
            SetElementActive(panelRoot, "TabContent", false);
    
     // HIDE other screen elements
            SetElementActive(panelRoot, "QuestConfigScreen", false);
      SetElementActive(panelRoot, "InfoNpcScreen", false);
   SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            // TraderConfigScreen + TraderApplyButton must hide in EVERY non-trader mode — otherwise switching
            // away from the trader tab (or hitting back from it) leaves the trader form overlapping the
            // next screen (was: AdminConfig's DetailPanel showed THROUGH the lingering trader form).
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);
            SetElementActive(panelRoot, "QuestInteract", false);
    
            // Set header text with NPC name style (larger, for player-facing screens)
        SetHeaderText(panelRoot, npcName ?? "Quest NPC", UIFontConfig.NpcNameHeader);
     }

        /// <summary>
      /// Quest config mode (admin Quest NPC tab).
/// </summary>
        private static void ConfigureQuestConfigMode(Transform panelRoot, string npcName)
        {
        // SHOW core elements
    SetElementActive(panelRoot, "Header", true);
   SetElementActive(panelRoot, "SizeButton", true);
       
         // SHOW tabs (for navigation)
            SetElementActive(panelRoot, "TabsBar", true);
   
      // SHOW page controls (contains the back button for navigation)
    SetElementActive(panelRoot, "PageControls", true);
 
            // SHOW quest config screen and its apply button
            SetElementActive(panelRoot, "QuestConfigScreen", true);
            SetElementActive(panelRoot, "QuestApplyButton", true);
  
          // HIDE other elements
SetElementActive(panelRoot, "DetailPanel", false);
    SetElementActive(panelRoot, "SearchBar", false);
         SetElementActive(panelRoot, "TabContent", false);
         SetElementActive(panelRoot, "QuestScreenContainer", false);
       SetElementActive(panelRoot, "InfoNpcScreen", false);
            SetElementActive(panelRoot, "DressingRoomScreen", false);
        SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);

            // Set header text with admin style
   SetHeaderText(panelRoot, "Quest Config", UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
        /// Trader config mode (admin Trader NPC tab).
        /// </summary>
        private static void ConfigureTraderConfigMode(Transform panelRoot, string npcName)
        {
            SetElementActive(panelRoot, "Header", true);
            SetElementActive(panelRoot, "SizeButton", true);
            SetElementActive(panelRoot, "TabsBar", true);
            SetElementActive(panelRoot, "PageControls", true);
            SetElementActive(panelRoot, "TraderConfigScreen", true);
            SetElementActive(panelRoot, "TraderApplyButton", true);
            // HIDE other elements
            SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "SearchInput", false);
            SetElementActive(panelRoot, "TabContent", false);
            SetElementActive(panelRoot, "QuestScreenContainer", false);
            SetElementActive(panelRoot, "QuestInteract", false);
            SetElementActive(panelRoot, "QuestConfigScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "InfoNpcScreen", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetHeaderText(panelRoot, "Trader Config", UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
        /// Banker config mode (admin Banker NPC tab).
        /// </summary>
        private static void ConfigureBankerConfigMode(Transform panelRoot, string npcName)
        {
            SetElementActive(panelRoot, "Header", true);
            SetElementActive(panelRoot, "SizeButton", true);
            SetElementActive(panelRoot, "TabsBar", true);
            SetElementActive(panelRoot, "PageControls", true);
            SetElementActive(panelRoot, "BankerConfigScreen", true);
            SetElementActive(panelRoot, "BankerApplyButton", true);
            SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "SearchInput", false);
            SetElementActive(panelRoot, "TabContent", false);
            SetElementActive(panelRoot, "QuestScreenContainer", false);
            SetElementActive(panelRoot, "QuestInteract", false);
            SetElementActive(panelRoot, "QuestConfigScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "InfoNpcScreen", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);
            SetHeaderText(panelRoot, "Banker Config", UIFontConfig.AdminPanelHeader);
        }

        // Centralized list of every module screen + apply-button name so any new module is added in ONE
        // place (here) — every Configure*Mode just toggles its own active pair and uses these for the rest.
        private static readonly string[] ALL_MODULE_SCREENS = {
            "QuestConfigScreen", "InfoNpcScreen", "DialogueEditorScreen", "DressingRoomScreen",
            "TraderConfigScreen", "BankerConfigScreen",
            "GamblerConfigScreen", "MarketplaceConfigScreen", "AchievementConfigScreen",
            "LeaderboardConfigScreen", "MailConfigScreen", "TransmogConfigScreen",
        };
        private static readonly string[] ALL_MODULE_APPLIES = {
            "QuestApplyButton", "InfoApplyButton", "TraderApplyButton", "BankerApplyButton",
            "GamblerApplyButton", "MarketplaceApplyButton", "AchievementApplyButton",
            "LeaderboardApplyButton", "MailApplyButton", "TransmogApplyButton",
        };

        /// <summary>Generic admin tab mode: shows header/tabs/page-controls + the one active screen + its
        /// apply button; hides every other module screen/apply. Used by every v1-stub module tab.</summary>
        private static void ConfigureModuleMode(Transform panelRoot, string activeScreen, string activeApply, string headerText)
        {
            SetElementActive(panelRoot, "Header", true);
            SetElementActive(panelRoot, "SizeButton", true);
            SetElementActive(panelRoot, "TabsBar", true);
            SetElementActive(panelRoot, "PageControls", true);
            SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "SearchInput", false);
            SetElementActive(panelRoot, "TabContent", false);
            SetElementActive(panelRoot, "QuestScreenContainer", false);
            SetElementActive(panelRoot, "QuestInteract", false);
            foreach (var s in ALL_MODULE_SCREENS) SetElementActive(panelRoot, s, s == activeScreen);
            foreach (var a in ALL_MODULE_APPLIES) SetElementActive(panelRoot, a, a == activeApply);
            SetHeaderText(panelRoot, headerText, UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
      /// Info config mode (admin Info NPC tab).
   /// </summary>
        private static void ConfigureInfoConfigMode(Transform panelRoot, string npcName)
  {
            // SHOW core elements
          SetElementActive(panelRoot, "Header", true);
          SetElementActive(panelRoot, "SizeButton", true);
     
            // SHOW tabs (for navigation)
   SetElementActive(panelRoot, "TabsBar", true);
   
 // SHOW page controls (contains the back button for navigation)
            SetElementActive(panelRoot, "PageControls", true);
  
        // SHOW info screen
        SetElementActive(panelRoot, "InfoNpcScreen", true);
    SetElementActive(panelRoot, "InfoApplyButton", true);
 
            // HIDE other elements
      SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
     SetElementActive(panelRoot, "TabContent", false);
        SetElementActive(panelRoot, "QuestScreenContainer", false);
       SetElementActive(panelRoot, "QuestConfigScreen", false);
SetElementActive(panelRoot, "DressingRoomScreen", false);
  SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);

     // Set header text with admin style
     SetHeaderText(panelRoot, "Info NPC Config", UIFontConfig.AdminPanelHeader);
        }

     /// <summary>
        /// Info interaction mode (E on info NPC).
        /// </summary>
        private static void ConfigureInfoInteractionMode(Transform panelRoot, string npcName)
        {
          // SHOW core elements
         SetElementActive(panelRoot, "Header", true);
  SetElementActive(panelRoot, "SizeButton", true);
   
       // SHOW info display
    SetElementActive(panelRoot, "InfoNpcScreen", true);
   
   // HIDE admin elements
       SetElementActive(panelRoot, "TabsBar", false);
            SetElementActive(panelRoot, "DetailPanel", false);
         SetElementActive(panelRoot, "SearchBar", false);
         SetElementActive(panelRoot, "PageControls", false);
  SetElementActive(panelRoot, "TabContent", false);
   
            // HIDE other screens
        SetElementActive(panelRoot, "QuestScreenContainer", false);
        SetElementActive(panelRoot, "QuestConfigScreen", false);
        SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
     SetElementActive(panelRoot, "InfoApplyButton", false);
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);

            // Set header text with NPC name style (player-facing)
            SetHeaderText(panelRoot, npcName ?? "Info", UIFontConfig.NpcNameHeader);
        }

   /// <summary>
        /// Dressing room mode (admin equipment configuration).
        /// </summary>
        private static void ConfigureDressingRoomMode(Transform panelRoot, string npcName)
        {
          // SHOW core elements
        SetElementActive(panelRoot, "Header", true);
        SetElementActive(panelRoot, "SizeButton", true);
       
            // SHOW page controls (contains the back button for navigation)
        SetElementActive(panelRoot, "PageControls", true);
          
            // SHOW dressing room screen
      SetElementActive(panelRoot, "DressingRoomScreen", true);
   
            // HIDE admin elements (we're in a sub-screen)
      SetElementActive(panelRoot, "TabsBar", false);
            SetElementActive(panelRoot, "DetailPanel", false);
    SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "TabContent", false);
    
     // HIDE other screens
            SetElementActive(panelRoot, "QuestScreenContainer", false);
         SetElementActive(panelRoot, "QuestConfigScreen", false);
            SetElementActive(panelRoot, "InfoNpcScreen", false);
       SetElementActive(panelRoot, "DialogueEditorScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
 SetElementActive(panelRoot, "InfoApplyButton", false);
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);

// Set header text with admin style
            SetHeaderText(panelRoot, $"Dressing Room: {npcName ?? "NPC"}", UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
        /// Dialogue editor mode (admin Dialogue NPC tab).
        /// Shows header + size button + tabs for navigation, hides everything else.
        /// The DialogueEditorScreen itself overlays on top.
        /// </summary>
        private static void ConfigureDialogueEditorMode(Transform panelRoot, string npcName)
        {
            // SHOW core elements
            SetElementActive(panelRoot, "Header", true);
            SetElementActive(panelRoot, "SizeButton", true);

            // SHOW tabs (for navigation between NPC types)
            SetElementActive(panelRoot, "TabsBar", true);

            // SHOW page controls (contains the back button)
            SetElementActive(panelRoot, "PageControls", true);

            // SHOW the dialogue editor screen itself
            SetElementActive(panelRoot, "DialogueEditorScreen", true);

            // HIDE everything else
            SetElementActive(panelRoot, "DetailPanel", false);
            SetElementActive(panelRoot, "SearchBar", false);
            SetElementActive(panelRoot, "SearchInput", false);
            SetElementActive(panelRoot, "TabContent", false);
            SetElementActive(panelRoot, "QuestScreenContainer", false);
            SetElementActive(panelRoot, "QuestConfigScreen", false);
            SetElementActive(panelRoot, "InfoNpcScreen", false);
            SetElementActive(panelRoot, "DressingRoomScreen", false);
            SetElementActive(panelRoot, "QuestApplyButton", false);
            SetElementActive(panelRoot, "InfoApplyButton", false);
            // TraderConfigScreen + TraderApplyButton must hide in EVERY non-trader mode — otherwise switching
            // away from the trader tab (or hitting back from it) leaves the trader form overlapping the
            // next screen (was: AdminConfig's DetailPanel showed THROUGH the lingering trader form).
            SetElementActive(panelRoot, "TraderConfigScreen", false);
            SetElementActive(panelRoot, "TraderApplyButton", false);
            SetElementActive(panelRoot, "BankerConfigScreen", false);
            SetElementActive(panelRoot, "BankerApplyButton", false);

            // Set header text with admin style
            SetHeaderText(panelRoot, $"Setup Dialogue NPC: {npcName ?? "NPC"}", UIFontConfig.AdminPanelHeader);
        }

        /// <summary>
        /// Hides all panel elements.
        /// </summary>
        private static void HideAllElements(Transform panelRoot)
        {
      string[] allElements = {
           "Header", "SizeButton", "TabsBar", "DetailPanel", "SearchBar", "SearchInput",
          "PageControls", "TabContent", "QuestScreenContainer", "QuestInteract",
   "QuestConfigScreen", "InfoNpcScreen", "DressingRoomScreen",
                "DialogueEditorScreen", "QuestApplyButton", "InfoApplyButton",
                "TraderConfigScreen", "TraderApplyButton",
                "BankerConfigScreen", "BankerApplyButton"
   };
       
            foreach (var elementName in allElements)
  {
     SetElementActive(panelRoot, elementName, false);
            }
        }

        #region Element Cache

        // Keyed by (panelRoot instance ID, element name) to support multiple panels.
        // Cleared on scene change / logout via ClearCache().
        private static readonly Dictionary<int, Dictionary<string, Transform>> _cache
            = new Dictionary<int, Dictionary<string, Transform>>();
        private static TMP_Text _cachedTitleText;
        private static int _cachedTitlePanelId;

        /// <summary>
        /// Clears the element lookup cache. Call on scene change / logout.
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _cachedTitleText = null;
            _cachedTitlePanelId = 0;
        }

        private static Transform GetCachedElement(Transform panelRoot, string elementName)
        {
            int id = panelRoot.GetInstanceID();
            if (!_cache.TryGetValue(id, out var map))
            {
                map = new Dictionary<string, Transform>(16);
                _cache[id] = map;
            }

            if (!map.TryGetValue(elementName, out var t))
            {
                t = panelRoot.Find(elementName);
                map[elementName] = t; // cache even if null to avoid repeated misses
            }
            return t;
        }

        #endregion

        /// <summary>
        /// Sets the active state of a named element.
        /// Skips the call when the element is already in the desired state to
        /// avoid dirtying the canvas batch.
        /// </summary>
        private static void SetElementActive(Transform panelRoot, string elementName, bool active)
        {
            var element = GetCachedElement(panelRoot, elementName);
            if (element != null && element.gameObject.activeSelf != active)
            {
                element.gameObject.SetActive(active);
            }
        }

        /// <summary>
        /// Sets the header text with a specific style from UIFontConfig.
        /// This is the ONLY place header text styling should be applied for consistency.
        /// </summary>
        /// <param name="panelRoot">The panel root transform.</param>
        /// <param name="text">The text to display.</param>
        /// <param name="style">The UITextStyle to apply (from UIFontConfig).</param>
        public static void SetHeaderText(Transform panelRoot, string text, UITextStyle style)
        {
            int id = panelRoot.GetInstanceID();
            if (_cachedTitleText == null || _cachedTitlePanelId != id)
            {
                var header = GetCachedElement(panelRoot, "Header");
                _cachedTitleText = header?.Find("TitleText")?.GetComponent<TMP_Text>();
                _cachedTitlePanelId = id;
            }
            if (_cachedTitleText != null)
            {
                _cachedTitleText.text = text;
                UIFontConfig.ApplyStyle(_cachedTitleText, style);
            }
        }

     /// <summary>
      /// Applies a style to a TMP_Text component directly.
  /// Use this for text elements that are not the main header.
  /// </summary>
        /// <param name="text">The TMP_Text component.</param>
    /// <param name="style">The UITextStyle to apply.</param>
 public static void ApplyTextStyle(TMP_Text text, UITextStyle style)
    {
   if (text != null)
 {
         UIFontConfig.ApplyStyle(text, style);
   }
        }

        /// <summary>
        /// Ensures the close button is properly wired.
        /// </summary>
        public static void EnsureCloseButtonWired(Transform panelRoot, System.Action onClose)
        {
            var closeButton = panelRoot.Find("Header/CloseButton")?.GetComponent<UnityEngine.UI.Button>();
 if (closeButton != null)
            {
  closeButton.onClick.RemoveAllListeners();
      closeButton.onClick.AddListener(() => onClose?.Invoke());
      }
      }
    }
}
