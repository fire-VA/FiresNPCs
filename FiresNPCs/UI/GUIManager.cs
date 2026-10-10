using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.UI.QuestJournal;
using FiresCore.Npc;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Manages UI blocking and input state.
    /// Provides utilities for modal UI panels.
    /// Factory methods delegate to UIBuilderHelper for consistency.
    /// </summary>
    public static class GUIManager
    {
        // Input blocking moved to FiresCore.UI.InputBlock (Shared -> Core). The methods below delegate.

        // Cached TMP font from game UI
   private static TMP_FontAsset _cachedFont;
        private static bool _fontSearched;
        
        // Cached Norse SDF font for headers/titles (Viking-style font)
     private static TMP_FontAsset _cachedNorseFont;
        private static bool _norseFontSearched;
        
        // Flag to track if we've subscribed to font config changes
        private static bool _fontConfigSubscribed;

        /// <summary>
        /// Gets whether UI input is currently blocked.
        /// </summary>
  public static bool IsInputBlocked => FiresCore.UI.InputBlock.IsBlocked;
        
        /// <summary>
        /// Initializes the GUIManager and subscribes to font config changes.
        /// Call this during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            if (!_fontConfigSubscribed)
            {
                UIFontConfig.OnFontConfigChanged += OnFontConfigChanged;
                _fontConfigSubscribed = true;
            }

            // Initialize font cache early to set TMP_Settings.defaultFontAsset,
            // preventing "LiberationSans SDF Font Asset was not found" warnings
            UIBuilderHelper.InitializeFontCache();
        }
        
        /// <summary>
        /// Cleans up subscriptions. Call during mod shutdown.
        /// </summary>
        public static void Cleanup()
        {
            if (_fontConfigSubscribed)
            {
                UIFontConfig.OnFontConfigChanged -= OnFontConfigChanged;
                _fontConfigSubscribed = false;
            }
        }
        
        /// <summary>
        /// Called when font configuration changes.
        /// Refreshes fonts on all active UI screens.
        /// </summary>
        private static void OnFontConfigChanged()
        {
            try
            {
                Debug.Log("[GUIManager] Font configuration changed, refreshing all active UI screens...");
                RefreshAllActiveFonts();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GUIManager] Error refreshing fonts: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Refreshes fonts on all currently active UI screens.
        /// Call this when font configuration changes.
        /// </summary>
        public static void RefreshAllActiveFonts()
        {
            int refreshedCount = 0;
            
            // Refresh Info NPC Screen (Admin config screen)
            if (Screens.InfoNpcScreenController.Instance != null && 
                Screens.InfoNpcScreenController.Instance.screenRoot != null &&
                Screens.InfoNpcScreenController.Instance.screenRoot.activeSelf)
            {
                UIFontConfig.RefreshFontsInHierarchy(Screens.InfoNpcScreenController.Instance.screenRoot);
                refreshedCount++;
            }
            
            // Refresh Info NPC Player Panel (static class - find by name in Hud)
            if (Screens.InfoNpcPlayerPanel.IsVisible)
            {
                try
                {
                    var panelGO = GameObject.Find("InfoNpcPlayerPanel");
                    if (panelGO != null)
                    {
                        UIFontConfig.RefreshFontsInHierarchy(panelGO);
                        refreshedCount++;
                    }
                }
                catch { }
            }
            
            // Refresh Quest Journal
            if (QuestJournalController.Instance != null && QuestJournalController.Instance.IsVisible)
            {
                UIFontConfig.RefreshFontsInHierarchy(QuestJournalController.Instance.gameObject);
                refreshedCount++;
            }
            
            // Refresh registered mod screens (companions, guilds) via Core ModUiRegistry.
            FiresCore.Bridge.ModUiRegistry.ForEachOpenRoot(go =>
            {
                UIFontConfig.RefreshFontsInHierarchy(go);
                refreshedCount++;
            });


            // Refresh Dialogue UI panel
            if (global::FiresRPGmaker.Modules.Dialogues.DialogueUI.IsVisible)
            {
                try
                {
                    var dialoguePanelGO = GameObject.Find("DialoguePanel");
                    if (dialoguePanelGO != null)
                    {
                        UIFontConfig.RefreshFontsInHierarchy(dialoguePanelGO);
                        refreshedCount++;
                    }
                }
                catch { }
            }
            
            // Refresh Quest Config Screen (admin sub-screen)
            try
            {
                var questConfigGO = GameObject.Find("QuestConfigScreen");
                if (questConfigGO != null && questConfigGO.activeSelf)
                {
                    UIFontConfig.RefreshFontsInHierarchy(questConfigGO);
                    refreshedCount++;
                }
            }
            catch { }
            
            // Refresh Dialogue Editor Screen (admin sub-screen)
            try
            {
                var dialogueEditorGO = GameObject.Find("DialogueEditorScreen");
                if (dialogueEditorGO != null && dialogueEditorGO.activeSelf)
                {
                    UIFontConfig.RefreshFontsInHierarchy(dialogueEditorGO);
                    refreshedCount++;
                }
            }
            catch { }
            
            // Refresh Dressing Room Screen (admin sub-screen)
            try
            {
                var dressingGO = GameObject.Find("DressingRoomScreen");
                if (dressingGO != null && dressingGO.activeSelf)
                {
                    UIFontConfig.RefreshFontsInHierarchy(dressingGO);
                    refreshedCount++;
                }
            }
            catch { }
            

            Debug.Log($"[GUIManager] Font refresh completed for {refreshedCount} active UI screens");
        }

        #region Font Management

        /// <summary>
        /// Gets or finds the game's TMP font asset.
        /// Uses the configured Primary font from BepInEx config.
        /// Returns null if not found (TMP components will use their default).
        /// </summary>
        public static TMP_FontAsset GetGameFont()
        {
            // Get the configured primary font (defaults to Valheim if not set)
            var effectiveFont = UIFontConfig.GetConfiguredFont(UIFontConfig.FontCategory.Primary);
            return UIFontConfig.GetFont(effectiveFont);
        }

        /// <summary>
        /// Gets or finds the Norse SDF font (Viking-style header font).
        /// This is Valheim's custom decorative font used for titles and headers.
        /// Uses the configured Decorative font from BepInEx config.
        /// Returns null if not found, falling back to regular game font is recommended.
        /// </summary>
        public static TMP_FontAsset GetNorseFont()
        {
            // Get the configured decorative font (defaults to Norse if not set)
            var effectiveFont = UIFontConfig.GetConfiguredFont(UIFontConfig.FontCategory.Decorative);
            return UIFontConfig.GetFont(effectiveFont);
        }

        /// <summary>
        /// Applies the Norse SDF font to a TMP_Text component for headers/titles.
        /// Uses the configured Decorative font from BepInEx config.
        /// Falls back to regular game font if not available.
        /// </summary>
        public static void ApplyNorseFont(TMP_Text textComponent)
        {
            if (textComponent == null) return;

            var norseFont = GetNorseFont();
            if (norseFont != null)
            {
                textComponent.font = norseFont;
            }
            else
            {
                // Fallback to regular game font
                ApplyGameFont(textComponent);
            }
        }
        
        /// <summary>
        /// Gets the Body font (descriptions, buttons, readable text).
        /// Uses the configured Body font from BepInEx config.
        /// </summary>
        public static TMP_FontAsset GetBodyFont()
        {
            var effectiveFont = UIFontConfig.GetConfiguredFont(UIFontConfig.FontCategory.Body);
            return UIFontConfig.GetFont(effectiveFont);
        }
        
        /// <summary>
        /// Applies the Body font to a TMP_Text component.
        /// Uses the configured Body font from BepInEx config.
        /// </summary>
        public static void ApplyBodyFont(TMP_Text textComponent)
        {
            if (textComponent == null) return;
            
            var bodyFont = GetBodyFont();
            if (bodyFont != null)
            {
                textComponent.font = bodyFont;
            }
            else
            {
                ApplyGameFont(textComponent);
            }
        }
        
        /// <summary>
        /// Applies a font from a specific category to a TMP_Text component.
        /// </summary>
        public static void ApplyFontByCategory(TMP_Text textComponent, UIFontConfig.FontCategory category)
        {
            UIBuilderHelper.ApplyFontByCategory(textComponent, category);
        }

        /// <summary>
        /// Applies the game font to a TMP_Text component if available.
        /// </summary>
        public static void ApplyGameFont(TMP_Text textComponent)
        {
            // Delegate to UIBuilderHelper for consistency
            UIBuilderHelper.ApplyValheimFont(textComponent);
      }

        /// <summary>
     /// Applies the game font to all TMP_Text components under a transform.
 /// </summary>
 public static void ApplyGameFontToAll(Transform root)
        {
        // Delegate to UIBuilderHelper for consistency
            if (root != null)
    UIBuilderHelper.ApplyValheimFontToAll(root.gameObject);
        }

        /// <summary>
        /// Resets the font cache (call if game UI changes).
        /// </summary>
        public static void ResetFontCache()
        {
            _cachedFont = null;
  _fontSearched = false;
            _cachedNorseFont = null;
            _norseFontSearched = false;
            UIFontConfig.ClearCache();
        }

        #endregion

        #region Input Blocking

    /// <summary>
        /// Blocks or unblocks game input for UI interaction.
 /// </summary>
    public static void BlockInput(bool block)
        {
            // Single shared implementation lives in Core (cursor state + Player/GameCamera/Minimap/InventoryGui patches).
            FiresCore.UI.InputBlock.Block(block);
        }

      /// <summary>
 /// Returns true if our custom UI panels should block game input.
      /// Used by Harmony patches to prevent player movement/camera control.
        /// </summary>
      public static bool ShouldBlockGameInput()
        {
            return FiresCore.UI.InputBlock.IsBlocked;
        }

     #endregion

     #region Panel State Management

        /// <summary>
        /// Checks if any custom UI panel is currently open.
        /// </summary>
public static bool IsCustomPanelOpen()
       {
    // Check the book NPC config panel (parchment admin UI)
    if (NpcBookController.Instance != null && NpcBookController.Instance.IsOpen)
        return true;

    // Check player info display panel
    if (Screens.InfoNpcPlayerPanel.IsVisible)
               return true;

    // Check Quest Journal panel
      if (QuestJournalController.Instance != null &&
         QuestJournalController.Instance.IsVisible)
     return true;

    // Check Dialogue UI panel
    if (global::FiresRPGmaker.Modules.Dialogues.DialogueUI.IsVisible)
        return true;

    // Check Trader UI panel
    if (Screens.TraderUIController.Instance != null &&
        Screens.TraderUIController.Instance.IsVisible)
        return true;

    // Check Marketplace-module player panels (Banker, Gambler, Marketplace, Achievement, Leaderboard,
    // Mail, Transmog). The base panel maintains a static AnyOpen counter on Open/Close.
    if (Screens.PlayerPanels.BasePlayerModulePanel.AnyOpen)
        return true;

    // Mail panel was rewritten to load from prefab + no longer extends BasePlayerModulePanel —
    // it owns its own static AnyOpen flag that needs an independent check here, otherwise
    // PlayerInputPatches / Hud see "no custom panel open" while the mail UI is up and let the
    // camera keep tracking the cursor + show NPC hover names.
    if (Screens.PlayerPanels.MailPlayerPanel.AnyOpen)
        return true;

    // Faction editor (IMGUI window, admin)
    if (global::FiresRPGmaker.Modules.Factions.FactionEditorPanel.IsOpen)
        return true;

    // Check Help panel
    if (FiresCore.Help.HelpPanel.Instance != null &&
        FiresCore.Help.HelpPanel.Instance.IsVisible)
        return true;

    // Check registered mod screens (companions, guilds) via Core ModUiRegistry
    if (FiresCore.Bridge.ModUiRegistry.IsAnyOpen())
        return true;

return false;
       }

     /// <summary>
     /// Closes all custom UI panels.
        /// </summary>
  public static void CloseAllPanels()
     {
   NpcBookController.Instance?.Hide();
   Screens.InfoNpcPlayerPanel.Hide();
         QuestJournalController.Instance?.Hide();
         global::FiresRPGmaker.Modules.Dialogues.DialogueUI.Hide();
         Screens.TraderUIController.HideIfVisible();
         Screens.PlayerPanels.BasePlayerModulePanel.CloseAny();
         Screens.PlayerPanels.MailPlayerPanel.CloseAny();
         global::FiresRPGmaker.Modules.Factions.FactionEditorPanel.CloseIfOpen();
         FiresCore.Help.HelpPanel.Instance?.Hide();
         FiresCore.Bridge.ModUiRegistry.CloseAll();
        }

        /// <summary>
        /// Adds a small top-right "✕" close button styled to match the Fires dialogue/panel theme (a rounded
        /// dark chip with a thin gold inset border + gold glyph, same language as the dialogue number badge).
        /// Reddens on hover as the conventional close affordance. onClose fires on click.
        /// </summary>
        public static Button AddCloseX(Transform panel, Action onClose, float size = 30f, float inset = 10f)
        {
            if (panel == null) return null;
            var go = new GameObject("CloseX", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-inset, -inset);
            // Float in the panel's top-right even when the panel is a layout-group container
            // (dialogue's visible box is a VerticalLayoutGroup) — otherwise the group would
            // reposition/stretch the chip into the content flow.
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var img = go.GetComponent<Image>();
            var normal = new Color(0.14f, 0.11f, 0.07f, 0.98f);
            img.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(7, Color.white, new Color(0.72f, 0.55f, 0.28f, 1f), 1);
            img.type = Image.Type.Sliced;
            img.color = normal;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (onClose != null) btn.onClick.AddListener(() => onClose());

            var label = new GameObject("X", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var x = label.AddComponent<TextMeshProUGUI>();
            x.text = "×";   // U+00D7 — in Valheim's AveriaSans font (U+2715 ✕ is NOT, renders as □)
            x.fontSize = size * 0.72f;
            x.alignment = TextAlignmentOptions.Center;
            x.color = new Color(0.93f, 0.82f, 0.52f);
            x.fontStyle = FontStyles.Bold;
            x.raycastTarget = false;
            try { FiresCore.UI.UIBuilderHelper.ApplyValheimFont(x); } catch { }

            var hover = go.AddComponent<CloseXHover>();
            hover.Img = img; hover.Normal = normal; hover.Hover = new Color(0.45f, 0.16f, 0.11f, 0.98f);
            return btn;
        }

        #endregion

        #region UI Factory Methods (Delegate to UIBuilderHelper)

        /// <summary>
        /// Creates a simple button from a template or from scratch.
   /// Delegates to UIBuilderHelper for consistent styling.
        /// </summary>
        public static GameObject CreateButton(Transform parent, string text, Action onClick)
     {
        // Create button using UIBuilderHelper with default positioning
            var button = UIBuilderHelper.CreateButton(
           parent,
                text,
    Vector2.zero,
        Vector2.one,
         onClick != null ? () => onClick() : null);

  // Adjust size since UIBuilderHelper uses anchors
          var rectTransform = button.GetComponent<RectTransform>();
  rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
         rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
      rectTransform.sizeDelta = new Vector2(200, 40);

            return button.gameObject;
      }

        /// <summary>
        /// Creates a simple text label.
        /// Delegates to UIBuilderHelper for consistent styling.
        /// </summary>
        public static GameObject CreateLabel(Transform parent, string text, int fontSize = 16)
        {
          var tmp = UIBuilderHelper.CreateLabel(
     parent,
                "Label",
      text,
       fontSize,
   Color.white,
 Vector2.zero,
                Vector2.one,
      TextAlignmentOptions.Left);

            // Adjust size since UIBuilderHelper uses anchors
            var rectTransform = tmp.GetComponent<RectTransform>();
  rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(200, 30);

    return tmp.gameObject;
        }

        /// <summary>
        /// Creates a vertical layout group container.
  /// Delegates to UIBuilderHelper for consistent styling.
        /// </summary>
        public static GameObject CreateVerticalLayout(Transform parent, float spacing = 5f)
        {
            var containerGO = new GameObject("VerticalLayout");
    containerGO.transform.SetParent(parent, false);

 var rectTransform = containerGO.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;

            var layout = UIBuilderHelper.AddVerticalLayout(
  containerGO,
      new RectOffset(10, 10, 10, 10),
    spacing,
    childControlWidth: true,
childControlHeight: false,
                childForceExpandWidth: true,
       childForceExpandHeight: false);
 layout.childAlignment = TextAnchor.UpperCenter;

        UIBuilderHelper.AddContentSizeFitter(
                containerGO,
            ContentSizeFitter.FitMode.Unconstrained,
       ContentSizeFitter.FitMode.PreferredSize);

            return containerGO;
        }

        /// <summary>
        /// Creates a scroll view container.
        /// Delegates to UIBuilderHelper for consistent styling.
     /// </summary>
  public static (GameObject scrollView, Transform content) CreateScrollView(Transform parent)
        {
    var scrollResult = UIBuilderHelper.CreateScrollArea(
        parent,
        "ScrollView",
Vector2.zero,
   Vector2.one,
     new Color(0.1f, 0.1f, 0.1f, 0.5f),
       12f);

    // Add vertical layout to content for backward compatibility
    var contentLayout = UIBuilderHelper.AddVerticalLayout(
   scrollResult.Content.gameObject,
       new RectOffset(5, 5, 5, 5),
          5f,
childControlWidth: true,
     childControlHeight: false,
     childForceExpandWidth: true,
    childForceExpandHeight: false);
            contentLayout.childAlignment = TextAnchor.UpperCenter;

  UIBuilderHelper.AddContentSizeFitter(
    scrollResult.Content.gameObject,
  ContentSizeFitter.FitMode.Unconstrained,
   ContentSizeFitter.FitMode.PreferredSize);

 return (scrollResult.Root, scrollResult.Content);
        }

        #endregion
    }

    /// <summary>Hover tint for the shared top-right close "✕" — reddens the chip on pointer-over (the
    /// conventional close affordance), restores the theme brown on exit.</summary>
    internal sealed class CloseXHover : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Image Img;
        public Color Normal;
        public Color Hover;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (Img != null) Img.color = Hover; }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (Img != null) Img.color = Normal; }
        private void OnDisable() { if (Img != null) Img.color = Normal; }
    }
}
