using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Controller for the Quest NPC configuration screen (admin edit mode).
    /// Creates a clean, simple form layout with warm orange/gold color scheme.
    /// </summary>
  public class QuestConfigScreenController : MonoBehaviour
    {
        public static QuestConfigScreenController Instance { get; private set; }

        public GameObject screenRoot;

      // UI Fields
        public TMP_InputField questIdInput;
        public TMP_InputField questNameInput;
      public TMP_Dropdown questTypeDropdown;
  public Toggle autoCompleteToggle;
        public TMP_InputField descriptionInput;
        public TMP_InputField targetPrefabInput;
        public TMP_InputField targetAmountInput;
   public TMP_InputField targetLevelInput;
        public TMP_InputField cooldownInput;
        public TMP_InputField timeLimitInput;
    public TMP_InputField rewardsInput;
  public TMP_InputField requirementsInput;
        public TMP_InputField onStartEventInput;
 public TMP_InputField onCompleteEventInput;
        public TMP_InputField previewImageInput;
        public TMP_Dropdown questProfileDropdown;
        public TMP_InputField questProfileInput;
      public Button applyButton;
  public TMP_Text statusText;

 // Dynamic label references for quest type-specific updates
   private TMP_Text _targetLabel;
    private TMP_Text _amountLabel;
        private TMP_Text _levelLabel;
        private GameObject _levelLabelGO;
        private GameObject _levelInputGO;

     private NpcController _currentNpc;
    private bool _hasUnsavedChanges;
        private bool _initialized;
        private List<string> _availableProfiles = new List<string>();

        // Layout settings
     private const float FIELD_HEIGHT = 20f;
        private const float DESC_HEIGHT = 80f;
        private const float MULTI_HEIGHT = 40f;
        private const float SPACING = 5f;
        private const float FONT = 11f;
        private const float SMALL_FONT = 9f;
        private const float LABEL_W = 95f;
        private const float BUTTON_HEIGHT = 30f;

        // Colors - warm orange/gold theme matching main UI
private static readonly Color InputBgColor = new Color(0.06f, 0.06f, 0.08f, 0.6f);
        private static readonly Color DropdownBgColor = new Color(0.06f, 0.06f, 0.08f, 0.6f);
        private static readonly Color CheckboxBgColor = new Color(0.08f, 0.08f, 0.1f, 0.6f);
     private static readonly Color ButtonNormalColor = new Color(0.15f, 0.12f, 0.08f, 0.9f);
        private static readonly Color ButtonHoverColor = new Color(0.4f, 0.3f, 0.15f, 0.95f);
     private static readonly Color ButtonPressColor = new Color(0.6f, 0.4f, 0.2f, 1f);
    
        // Text colors - warm gold/orange
 private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f);
        private static readonly Color LabelColor = new Color(0.9f, 0.75f, 0.45f);
      private static readonly Color PlaceholderColor = new Color(0.5f, 0.4f, 0.25f, 0.6f);

     private static string QuestsConfigFolder => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Quests");
        private static string QuestProfilesFolder => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "QuestProfiles");
 
        // Quest types - "Move" renamed to "Go To"
private static readonly string[] QuestTypes = { "Kill", "Collect", "Harvest", "Craft", "Talk", "Build", "Go To" };
        
        // Internal quest type names for config file (keeps compatibility)
        private static readonly string[] QuestTypeConfigNames = { "Kill", "Collect", "Harvest", "Craft", "Talk", "Build", "Move" };

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Wires this controller to pre-built elements in the codegen hierarchy.
        /// Called by CodegenUIWiring.WireQuestConfigScreen().
        /// Children of QuestConfigScreen are ordered: label, input, label, input, ...
        /// We walk them by sibling index to resolve the duplicate "Input" names.
        /// </summary>
        public void WireFromCodegen(GameObject screenGo, Button applyBtn)
        {
            screenRoot = screenGo;
            var t = screenGo.transform;

            // Walk direct children by index to pair labels with their inputs/dropdowns.
            // Child order (from codegen):
            //  0  Lbl_Quest ID:
            //  1  Input  ? questIdInput
            //  2  Lbl_Quest Name:
            //  3  Input  ? questNameInput
            //  4  Lbl_Type:
            //  5  QuestTypeDropdown
            //  6  Checkbox_Auto-Complete
            //  7  CenterLbl_Quest Description:
            //  8  MultiInput ? descriptionInput
            //  9  Lbl_Target:
            // 10  Input  ? targetPrefabInput
            // 11  Lbl_Amount:
            // 12  Input  ? targetAmountInput
            // 13  Lbl_Min Lvl
            // 14  Input  ? targetLevelInput
            // 15  Lbl_Cooldown Days
            // 16  Input  ? cooldownInput
            // 17  Lbl_Limit Mins
            // 18  Input  ? timeLimitInput
            // 19  Lbl_Rewards:
            // 20  Input  ? rewardsInput
            // 21  Lbl_Required:
            // 22  Input  ? requirementsInput
            // 23  CenterLbl_On Quest Start Event:
            // 24  MultiInput ? onStartEventInput
            // 25  CenterLbl_On Quest Complete Event:
            // 26  MultiInput ? onCompleteEventInput
            // 27  CenterLbl_--- Save To Quest Profile ---
            // 28  Lbl_Existing:
            // 29  ProfileDropdown
            // 30  Lbl_Or New:
            // 31  Input  ? questProfileInput
            // 32  Lbl_Preview PNG:
            // 33  Input  ? previewImageInput
            // 34  CenterLbl_ (statusText)

            questIdInput        = GetInputFieldAtChild(t, 1);
            questNameInput      = GetInputFieldAtChild(t, 3);
            questTypeDropdown   = GetDropdownAtChild(t, 5);
            autoCompleteToggle  = GetToggleAtChild(t, 6);
            descriptionInput    = GetInputFieldAtChild(t, 8);
            targetPrefabInput   = GetInputFieldAtChild(t, 10);
            targetAmountInput   = GetInputFieldAtChild(t, 12);
            targetLevelInput    = GetInputFieldAtChild(t, 14);
            cooldownInput       = GetInputFieldAtChild(t, 16);
            timeLimitInput      = GetInputFieldAtChild(t, 18);
            rewardsInput        = GetInputFieldAtChild(t, 20);
            requirementsInput   = GetInputFieldAtChild(t, 22);
            onStartEventInput   = GetInputFieldAtChild(t, 24);
            onCompleteEventInput= GetInputFieldAtChild(t, 26);
            questProfileDropdown= GetDropdownAtChild(t, 29);
            questProfileInput   = GetInputFieldAtChild(t, 31);
            previewImageInput   = GetInputFieldAtChild(t, 33);

            // Dynamic labels for quest-type-specific updates
            _targetLabel = GetTMPAtChild(t, 9);
            _amountLabel = GetTMPAtChild(t, 11);
            _levelLabel  = GetTMPAtChild(t, 13);
            _levelLabelGO = t.childCount > 13 ? t.GetChild(13).gameObject : null;
            _levelInputGO = t.childCount > 14 ? t.GetChild(14).gameObject : null;

            // Status text is the last child
            statusText = GetTMPAtChild(t, 34);

            // Apply button (sibling of screen, tagged "QuestApplyButton")
            applyButton = applyBtn;
            if (applyButton != null)
            {
                applyButton.onClick.RemoveAllListeners();
                applyButton.onClick.AddListener(ApplyChanges);
                applyButton.gameObject.SetActive(false);
            }

            // Populate quest type dropdown options
            if (questTypeDropdown != null)
            {
                questTypeDropdown.ClearOptions();
                questTypeDropdown.AddOptions(new List<string>(QuestTypes));
                questTypeDropdown.RefreshShownValue();
            }

            WireChangeListeners();
            OnQuestTypeChanged(0);
            _initialized = true;

            screenGo.SetActive(false);

            Debug.Log($"[QuestConfigScreenController] WireFromCodegen � " +
                      $"QuestId={questIdInput != null}, QuestName={questNameInput != null}, " +
                      $"Type={questTypeDropdown != null}, AutoComplete={autoCompleteToggle != null}, " +
                      $"Desc={descriptionInput != null}, Target={targetPrefabInput != null}, " +
                      $"Amount={targetAmountInput != null}, Level={targetLevelInput != null}, " +
                      $"Cooldown={cooldownInput != null}, TimeLimit={timeLimitInput != null}, " +
                      $"Rewards={rewardsInput != null}, Requirements={requirementsInput != null}, " +
                      $"OnStart={onStartEventInput != null}, OnComplete={onCompleteEventInput != null}, " +
                      $"Profile={questProfileDropdown != null}, ProfileInput={questProfileInput != null}, " +
                      $"Preview={previewImageInput != null}, Status={statusText != null}, " +
                      $"Apply={applyButton != null}");
        }

        private static TMP_InputField GetInputFieldAtChild(Transform parent, int index)
        {
            if (index >= parent.childCount) return null;
            return parent.GetChild(index).GetComponentInChildren<TMP_InputField>(true);
        }

        private static TMP_Dropdown GetDropdownAtChild(Transform parent, int index)
        {
            if (index >= parent.childCount) return null;
            return parent.GetChild(index).GetComponentInChildren<TMP_Dropdown>(true);
        }

        private static Toggle GetToggleAtChild(Transform parent, int index)
        {
            if (index >= parent.childCount) return null;
            return parent.GetChild(index).GetComponentInChildren<Toggle>(true);
        }

        private static TMP_Text GetTMPAtChild(Transform parent, int index)
        {
            if (index >= parent.childCount) return null;
            return parent.GetChild(index).GetComponentInChildren<TMP_Text>(true);
        }

        public static QuestConfigScreenController Create(Transform panelRoot)
  {
  if (panelRoot == null)
  {
   Debug.LogError("[QuestConfigScreenController] Cannot create - panelRoot is null");
    return null;
  }

    var existing = panelRoot.Find("QuestConfigScreen");
 if (existing != null)
     UnityEngine.Object.Destroy(existing.gameObject);

   var screenGO = new GameObject("QuestConfigScreen");
 screenGO.transform.SetParent(panelRoot, false);

var rootRect = screenGO.AddComponent<RectTransform>();
   rootRect.anchorMin = new Vector2(0.04f, 0.12f); // Leave room at bottom for button
   rootRect.anchorMax = new Vector2(0.96f, 0.87f); // Original size - good coverage
   rootRect.offsetMin = new Vector2(8, 8);
   rootRect.offsetMax = new Vector2(-8, -8);

   // Add a semi-transparent background for improved readability
       var bgImage = screenGO.AddComponent<Image>();
   bgImage.color = UIFontConfig.Colors.FormSectionBackground;
            bgImage.raycastTarget = false;

 var controller = screenGO.AddComponent<QuestConfigScreenController>();
  controller.screenRoot = screenGO;
       controller.CreateForm(rootRect);
       controller._initialized = true;

     screenGO.SetActive(false);
Debug.Log("[QuestConfigScreenController] Created quest config screen with section background");
    return controller;
      }

      private void CreateForm(RectTransform parent)
    {
            // Start lower to leave room for tabs at the top of the background
          float y = -45f;

         // Row 1: Quest ID + Quest Name
            CreateFieldLabel(parent, "Quest ID:", 0f, 0.48f, y, FONT - 1);
            questIdInput = CreateTextField(parent, 0f, 0.48f, y, false);

      CreateFieldLabel(parent, "Quest Name:", 0.52f, 1f, y, FONT - 1);
   questNameInput = CreateTextField(parent, 0.52f, 1f, y, false);
         y -= FIELD_HEIGHT + SPACING;

      // Row 2: Quest Type (wider) + Auto-Complete
            CreateFieldLabel(parent, "Type:", 0f, 0.52f, y, FONT - 1);
            questTypeDropdown = CreateQuestTypeDropdown(parent, 0f, 0.52f, y);

          autoCompleteToggle = CreateCheckbox(parent, "Auto-Complete", 0.56f, y);
            y -= FIELD_HEIGHT + SPACING;

            // Row 3: Description label
     CreateCenterLabel(parent, "Quest Description:", y);
          y -= 16f;

  // Row 4: Description multiline
        descriptionInput = CreateMultilineField(parent, 0f, 1f, y, DESC_HEIGHT);
       y -= DESC_HEIGHT + SPACING;

          // Row 5: Target + Amount (with dynamic labels)
    _targetLabel = CreateFieldLabelWithRef(parent, "Target:", 0f, 0.58f, y, FONT - 1);
       targetPrefabInput = CreateTextField(parent, 0f, 0.58f, y, false);
      SetPlaceholder(targetPrefabInput, "Prefab name (e.g. Wolf)");

       _amountLabel = CreateFieldLabelWithRef(parent, "Amount:", 0.62f, 0.9f, y, FONT - 1);
            targetAmountInput = CreateTextField(parent, 0.62f, 0.9f, y, false);
         y -= FIELD_HEIGHT + SPACING;

 // Row 6: Level + Cooldown + TimeLimit - centered with left margin
        _levelLabel = CreateSmallFieldLabelWithRef(parent, "Min Lvl", 0.10f, 0.30f, y, out _levelLabelGO);
            targetLevelInput = CreateTextFieldWithLabelWidth(parent, 0.10f, 0.30f, y, false, 66f);
            _levelInputGO = targetLevelInput.gameObject;

     CreateSmallFieldLabel(parent, "Cooldown Days", 0.32f, 0.62f, y);
  cooldownInput = CreateTextFieldWithLabelWidth(parent, 0.32f, 0.62f, y, false, 125f);

  CreateSmallFieldLabel(parent, "Limit Mins", 0.64f, 0.90f, y);
        timeLimitInput = CreateTextFieldWithLabelWidth(parent, 0.64f, 0.90f, y, false, 100f);
            y -= FIELD_HEIGHT + SPACING;

// Row 7: Rewards + Requirements
   CreateFieldLabel(parent, "Rewards:", 0f, 0.48f, y, FONT - 1);
  rewardsInput = CreateTextField(parent, 0f, 0.48f, y, false);

 CreateFieldLabel(parent, "Required:", 0.52f, 1f, y, FONT - 1);
     requirementsInput = CreateTextField(parent, 0.52f, 1f, y, false);
            y -= FIELD_HEIGHT + SPACING;

            // Row 8: OnStart label
   CreateCenterLabel(parent, "On Quest Start Event:", y);
          y -= 16f;

    // Row 9: OnStart multiline
  onStartEventInput = CreateMultilineField(parent, 0f, 1f, y, MULTI_HEIGHT - 5);
            y -= (MULTI_HEIGHT - 5) + SPACING;

            // Row 10: OnComplete label
         CreateCenterLabel(parent, "On Quest Complete Event:", y);
            y -= 16f;

    // Row 11: OnComplete multiline
      onCompleteEventInput = CreateMultilineField(parent, 0f, 1f, y, MULTI_HEIGHT - 5);
          y -= (MULTI_HEIGHT - 5) + SPACING + 4;

  // Row 12: Quest Profile Assignment section
            CreateCenterLabel(parent, "--- Save To Quest Profile ---", y);
       y -= 18f;

            // Row 13: Profile dropdown + text input
 CreateFieldLabel(parent, "Existing:", 0f, 0.51f, y, FONT - 1);
            questProfileDropdown = CreateProfileDropdown(parent, 0f, 0.51f, y);

   CreateFieldLabel(parent, "Or New:", 0.51f, 1f, y, FONT - 1);
          questProfileInput = CreateTextField(parent, 0.51f, 1f, y, false);
            y -= FIELD_HEIGHT + SPACING;

 // Row 14: Preview Image
            CreateCenteredFieldLabel(parent, "Preview PNG:", y, FONT - 1);
            previewImageInput = CreateCenteredTextField(parent, y, 0.40f);
            y -= FIELD_HEIGHT + SPACING;

          // Row 15: Status
         statusText = CreateCenterLabel(parent, "", y);
      statusText.color = new Color(0.5f, 0.9f, 0.5f);

            // Create Apply Button at BOTTOM CENTER
    CreateApplyButtonAtBottom();

       WireChangeListeners();
 
   // Set initial labels for Kill quest type
 OnQuestTypeChanged(0);
        }

        /// <summary>
      /// Updates UI labels and placeholders based on the selected quest type.
  /// </summary>
        private void OnQuestTypeChanged(int questTypeIndex)
        {
     // Quest types: 0=Kill, 1=Collect, 2=Harvest, 3=Craft, 4=Talk, 5=Build, 6=Go To
        string targetLabelText = "Target:";
  string targetPlaceholder = "Prefab name (e.g. Wolf)";
     string amountLabelText = "Amount:";
        string levelLabelText = "Min Lvl";
 bool showLevelField = true;

        switch (questTypeIndex)
            {
  case 0: // Kill
        targetLabelText = "Target:";
           targetPlaceholder = "Creature prefab (e.g. Wolf)";
    amountLabelText = "Amount:";
                    levelLabelText = "Min Lvl";
        showLevelField = true;
    break;
               
           case 1: // Collect
             targetLabelText = "Item:";
        targetPlaceholder = "Item prefab (e.g. Wood)";
    amountLabelText = "Amount:";
  levelLabelText = "Quality";
   showLevelField = true;
        break;
   
       case 2: // Harvest
     targetLabelText = "Harvest:";
  targetPlaceholder = "Resource prefab (e.g. Beech1)";
          amountLabelText = "Amount:";
     levelLabelText = "";
              showLevelField = false;
 break;
   
        case 3: // Craft
            targetLabelText = "Craft:";
            targetPlaceholder = "Item prefab (e.g. ArrowWood)";
           amountLabelText = "Amount:";
         levelLabelText = "Quality";
        showLevelField = true;
     break;
             
       case 4: // Talk
    targetLabelText = "Speak To:";
                targetPlaceholder = "NPC name";
        amountLabelText = "Amount:";
      levelLabelText = "";
   showLevelField = false;
     break;
          
       case 5: // Build
      targetLabelText = "Build:";
     targetPlaceholder = "Piece prefab (e.g. wood_wall)";
        amountLabelText = "Amount:";
          levelLabelText = "";
        showLevelField = false;
                    break;
          
     case 6: // Go To (formerly Move)
     targetLabelText = "Go To:";
        targetPlaceholder = "x,z,radius (e.g. 112,34,20)";
          amountLabelText = "Amount:";
      levelLabelText = "";
      showLevelField = false;
break;
    }

            // Update labels
         if (_targetLabel != null)
             _targetLabel.text = targetLabelText;
 
            if (_amountLabel != null)
          _amountLabel.text = amountLabelText;
            
            if (_levelLabel != null)
      _levelLabel.text = levelLabelText;

        // Show/hide level field
     if (_levelLabelGO != null)
          _levelLabelGO.SetActive(showLevelField);
            if (_levelInputGO != null)
        _levelInputGO.SetActive(showLevelField);

       // Update placeholder
   SetPlaceholder(targetPrefabInput, targetPlaceholder);
        }

        /// <summary>
 /// Sets the placeholder text for a TMP_InputField.
     /// </summary>
    private void SetPlaceholder(TMP_InputField input, string text)
    {
            if (input == null) return;
            var placeholder = input.placeholder as TMP_Text;
     if (placeholder != null)
        placeholder.text = text;
        }

        /// <summary>
        /// Creates a field label and returns a reference to the TMP_Text component.
        /// </summary>
   private TMP_Text CreateFieldLabelWithRef(RectTransform parent, string text, float xMin, float xMax, float y, float fontSize)
        {
    var go = new GameObject($"Lbl_{text}");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
   rect.anchorMin = new Vector2(xMin, 1);
       rect.anchorMax = new Vector2(xMin, 1);
     rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, y);
   rect.sizeDelta = new Vector2(LABEL_W - 2, FIELD_HEIGHT);

       var tmp = go.AddComponent<TextMeshProUGUI>();
       tmp.text = text;
            tmp.fontSize = fontSize;
 tmp.color = LabelColor;
     tmp.alignment = TextAlignmentOptions.MidlineRight;
     tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
            
 return tmp;
      }

        /// <summary>
     /// Creates a small field label and returns a reference to the TMP_Text component and GameObject.
   /// </summary>
        private TMP_Text CreateSmallFieldLabelWithRef(RectTransform parent, string text, float xMin, float xMax, float y, out GameObject labelGO)
 {
     var go = new GameObject($"Lbl_{text}");
 go.transform.SetParent(parent, false);
            labelGO = go;

  float segmentWidth = xMax - xMin;

            var rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(xMin, 1);
            rect.anchorMax = new Vector2(xMax, 1);
    rect.pivot = new Vector2(0, 1);
  rect.anchoredPosition = new Vector2(2, y);
            rect.sizeDelta = new Vector2(-4, FIELD_HEIGHT);

  var tmp = go.AddComponent<TextMeshProUGUI>();
  tmp.text = text;
            tmp.fontSize = SMALL_FONT;
  tmp.color = LabelColor;
     tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
    tmp.overflowMode = TextOverflowModes.Overflow;
 tmp.raycastTarget = false;
     ApplyFont(tmp);
            
            return tmp;
        }

        #region UI Creation

        /// <summary>
        /// Creates the Apply button at the bottom center of the panel (outside the scrollable form area).
        /// </summary>
        private void CreateApplyButtonAtBottom()
        {
          // Create button as sibling to screenRoot, positioned at bottom of panel
        var panelRoot = screenRoot.transform.parent;
   
        var go = new GameObject("QuestApplyButton");
            go.transform.SetParent(panelRoot, false);

        var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.25f, 0.03f);
            rect.anchorMax = new Vector2(0.75f, 0.10f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Background with warm color
        var bg = go.AddComponent<Image>();
            bg.color = ButtonNormalColor;
            bg.raycastTarget = true;

        var button = go.AddComponent<Button>();
            button.targetGraphic = bg;

            // Color transitions
        var colors = button.colors;
            colors.normalColor = ButtonNormalColor;
            colors.highlightedColor = ButtonHoverColor;
            colors.pressedColor = ButtonPressColor;
            colors.selectedColor = ButtonNormalColor;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            // Button text with warm gold color
        var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
        var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

        var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = "Save Quest Changes";
            text.fontSize = FONT + 2;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            ApplyFont(text);

            // Add border highlight effect
            AddButtonBorderHighlight(button);

        button.onClick.AddListener(ApplyChanges);
            applyButton = button;

            // Initially hidden (shown when screen is shown)
            go.SetActive(false);
}

   /// <summary>
   /// Adds a transparent border with click highlight effect to a button.
   /// </summary>
      private void AddButtonBorderHighlight(Button button)
        {
     if (button == null) return;

     var borderGO = new GameObject("_TransparentBorder");
         borderGO.transform.SetParent(button.transform, false);
         borderGO.transform.SetAsFirstSibling();

      var borderRect = borderGO.AddComponent<RectTransform>();
          borderRect.anchorMin = Vector2.zero;
          borderRect.anchorMax = Vector2.one;
          borderRect.offsetMin = new Vector2(-3, -3);
          borderRect.offsetMax = new Vector2(3, 3);

      var borderImage = borderGO.AddComponent<Image>();
          borderImage.color = new Color(0.06f, 0.06f, 0.08f, 0.6f);
          borderImage.raycastTarget = false;

          var highlightHandler = borderGO.AddComponent<ButtonBorderHighlight>();
          highlightHandler.Initialize(borderImage, button);
  }

        private void CreateFieldLabel(RectTransform parent, string text, float xMin, float xMax, float y, float fontSize)
    {
   var go = new GameObject($"Lbl_{text}");
       go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(xMin, 1);
            rect.anchorMax = new Vector2(xMin, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(LABEL_W - 2, FIELD_HEIGHT);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = LabelColor;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
        }

        private void CreateSmallFieldLabel(RectTransform parent, string text, float xMin, float xMax, float y)
        {
     var go = new GameObject($"Lbl_{text}");
         go.transform.SetParent(parent, false);

         float segmentWidth = xMax - xMin;

     var rect = go.AddComponent<RectTransform>();
         rect.anchorMin = new Vector2(xMin, 1);
         rect.anchorMax = new Vector2(xMax, 1);
         rect.pivot = new Vector2(0, 1);
         rect.anchoredPosition = new Vector2(2, y);
         rect.sizeDelta = new Vector2(-4, FIELD_HEIGHT);

         var tmp = go.AddComponent<TextMeshProUGUI>();
         tmp.text = text;
         tmp.fontSize = SMALL_FONT;
         tmp.color = LabelColor;
         tmp.alignment = TextAlignmentOptions.MidlineLeft;
         tmp.textWrappingMode = TextWrappingModes.NoWrap;
         tmp.overflowMode = TextOverflowModes.Overflow;
         tmp.raycastTarget = false;
         ApplyFont(tmp);
        }

    private void CreateCenteredFieldLabel(RectTransform parent, string text, float y, float fontSize)
        {
            var go = new GameObject($"Lbl_{text}");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
        // Center the label: position it to the left of center
        rect.anchorMin = new Vector2(0.5f, 1);
        rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(1, 1); // Right-aligned pivot so it sits to the left of the input
        rect.anchoredPosition = new Vector2(-2, y); // Small gap before input
        rect.sizeDelta = new Vector2(LABEL_W - 2, FIELD_HEIGHT);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = LabelColor;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            ApplyFont(tmp);
    }

        private TMP_InputField CreateCenteredTextField(RectTransform parent, float y, float widthRatio)
        {
         var go = new GameObject("Input");
         go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
            // Center the input field: position it to the right of center
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0, 1); // Left-aligned pivot so it sits to the right of the label
            rect.anchoredPosition = new Vector2(2, y); // Small gap after label
            // Calculate width based on parent width ratio (use approximate pixel width)
            float inputWidth = 150f; // Approximate width for 0.40f ratio on typical panel
            rect.sizeDelta = new Vector2(inputWidth, FIELD_HEIGHT);

            return BuildInputField(go, false, FIELD_HEIGHT);
 }

 private TMP_Text CreateCenterLabel(RectTransform parent, string text, float y)
        {
            var go = new GameObject($"CenterLbl_{text}");
            go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(0, 16);

   var tmp = go.AddComponent<TextMeshProUGUI>();
       tmp.text = text;
       tmp.fontSize = FONT - 1;
       tmp.color = LabelColor;
       tmp.alignment = TextAlignmentOptions.Center;
       tmp.raycastTarget = false;
       ApplyFont(tmp);
       return tmp;
      }

        private TMP_InputField CreateTextField(RectTransform parent, float xMin, float xMax, float y, bool multi)
     {
            var go = new GameObject("Input");
            go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(xMin, 1);
            rect.anchorMax = new Vector2(xMax, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(LABEL_W, y);
            rect.sizeDelta = new Vector2(-LABEL_W - 4, FIELD_HEIGHT);

   return BuildInputField(go, multi, FIELD_HEIGHT);
        }

    private TMP_InputField CreateTextFieldWithLabelWidth(RectTransform parent, float xMin, float xMax, float y, bool multi, float labelWidth)
        {
var go = new GameObject("Input");
    go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
       rect.anchorMin = new Vector2(xMin, 1);
  rect.anchorMax = new Vector2(xMax, 1);
            rect.pivot = new Vector2(0, 1);
         rect.anchoredPosition = new Vector2(labelWidth, y);
 rect.sizeDelta = new Vector2(-labelWidth - 4, FIELD_HEIGHT);

     return BuildInputField(go, multi, FIELD_HEIGHT);
        }

  private TMP_InputField CreateMultilineField(RectTransform parent, float xMin, float xMax, float y, float height)
        {
         var go = new GameObject("MultiInput");
    go.transform.SetParent(parent, false);

     var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(xMin, 1);
       rect.anchorMax = new Vector2(xMax, 1);
 rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, y);
       rect.sizeDelta = new Vector2(-8, height);

      return BuildInputField(go, true, height);
        }

        private TMP_InputField BuildInputField(GameObject go, bool multiline, float height)
      {
            var bg = go.AddComponent<Image>();
 bg.color = InputBgColor;
         bg.raycastTarget = true;

            var viewportGO = new GameObject("Viewport");
      viewportGO.transform.SetParent(go.transform, false);
    var viewportRect = viewportGO.AddComponent<RectTransform>();
     viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(3, 2);
            viewportRect.offsetMax = new Vector2(-3, -2);
   viewportGO.AddComponent<RectMask2D>();

       var textGO = new GameObject("Text");
   textGO.transform.SetParent(viewportGO.transform, false);
var textRect = textGO.AddComponent<RectTransform>();
   textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
     textRect.offsetMin = Vector2.zero;
    textRect.offsetMax = Vector2.zero;

  var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = FONT;
        text.color = TextColor;
            text.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
          text.raycastTarget = false;
            ApplyFont(text);

       var placeholderGO = new GameObject("Placeholder");
     placeholderGO.transform.SetParent(viewportGO.transform, false);
        var placeholderRect = placeholderGO.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
 placeholderRect.offsetMin = Vector2.zero;
       placeholderRect.offsetMax = Vector2.zero;

       var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
  placeholder.text = "";
        placeholder.fontSize = FONT;
       placeholder.color = PlaceholderColor;
         placeholder.fontStyle = FontStyles.Italic;
    placeholder.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
      placeholder.raycastTarget = false;
            ApplyFont(placeholder);

       var input = go.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
   input.placeholder = placeholder;
            input.fontAsset = text.font;
            input.pointSize = FONT;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
  input.characterLimit = multiline ? 500 : 100;
            input.caretColor = new Color(1f, 0.9f, 0.6f);
    input.caretWidth = 1;
          input.customCaretColor = true;
            input.selectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);
            input.interactable = true;

 return input;
        }

    private TMP_Dropdown CreateQuestTypeDropdown(RectTransform parent, float xMin, float xMax, float y)
        {
   var go = new GameObject("QuestTypeDropdown");
   go.transform.SetParent(parent, false);

  var rect = go.AddComponent<RectTransform>();
 rect.anchorMin = new Vector2(xMin, 1);
 rect.anchorMax = new Vector2(xMax, 1);
     rect.pivot = new Vector2(0, 1);
         rect.anchoredPosition = new Vector2(LABEL_W, y);
 rect.sizeDelta = new Vector2(-LABEL_W - 4, FIELD_HEIGHT);

var bg = go.AddComponent<Image>();
          bg.color = DropdownBgColor;
bg.raycastTarget = true;

     var captionGO = new GameObject("Label");
          captionGO.transform.SetParent(go.transform, false);
          var captionRect = captionGO.AddComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero;
   captionRect.anchorMax = Vector2.one;
   captionRect.offsetMin = new Vector2(6, 0);
     captionRect.offsetMax = new Vector2(-20, 0);

        var caption = captionGO.AddComponent<TextMeshProUGUI>();
      caption.fontSize = FONT;
   caption.color = TextColor;
    caption.alignment = TextAlignmentOptions.MidlineLeft;
     caption.raycastTarget = false;
     caption.textWrappingMode = TextWrappingModes.NoWrap;
          caption.overflowMode = TextOverflowModes.Ellipsis;
            ApplyFont(caption);

      var arrowGO = new GameObject("Arrow");
   arrowGO.transform.SetParent(go.transform, false);
 var arrowRect = arrowGO.AddComponent<RectTransform>();
     arrowRect.anchorMin = new Vector2(1, 0);
     arrowRect.anchorMax = new Vector2(1, 1);
   arrowRect.pivot = new Vector2(1, 0.5f);
      arrowRect.sizeDelta = new Vector2(18, 0);
     arrowRect.anchoredPosition = new Vector2(-2, 0);

            var arrow = arrowGO.AddComponent<TextMeshProUGUI>();
          arrow.text = "v";
      arrow.fontSize = 10;
   arrow.color = LabelColor;
    arrow.alignment = TextAlignmentOptions.Center;
  arrow.raycastTarget = false;
      ApplyFont(arrow);

   // Template - 7 items at 24px each + 8px padding = 176px
   var templateGO = new GameObject("Template");
   templateGO.transform.SetParent(go.transform, false);
      var templateRect = templateGO.AddComponent<RectTransform>();
         templateRect.anchorMin = new Vector2(0, 0);
     templateRect.anchorMax = new Vector2(1, 0);
  templateRect.pivot = new Vector2(0.5f, 1);
     templateRect.anchoredPosition = new Vector2(0, 0);
            templateRect.sizeDelta = new Vector2(0, 176);

            var templateBg = templateGO.AddComponent<Image>();
      templateBg.color = new Color(0.05f, 0.05f, 0.07f, 0.98f);

      var viewportGO = new GameObject("Viewport");
      viewportGO.transform.SetParent(templateGO.transform, false);
  var viewportRect = viewportGO.AddComponent<RectTransform>();
viewportRect.anchorMin = Vector2.zero;
 viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(2, 4);
     viewportRect.offsetMax = new Vector2(-12, -4);
   viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content");
contentGO.transform.SetParent(viewportGO.transform, false);
var contentRect = contentGO.AddComponent<RectTransform>();
     contentRect.anchorMin = new Vector2(0, 1);
contentRect.anchorMax = new Vector2(1, 1);
     contentRect.pivot = new Vector2(0, 1);
 contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 24);

// Scrollbar
   var scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(templateGO.transform, false);
 var scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
   scrollbarRect.anchorMin = new Vector2(1, 0);
         scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.pivot = new Vector2(1, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-2, 0);
    scrollbarRect.sizeDelta = new Vector2(8, -8);

      var scrollbarBg = scrollbarGO.AddComponent<Image>();
          scrollbarBg.color = new Color(0.15f, 0.15f, 0.18f, 0.8f);

   var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
      scrollbar.direction = Scrollbar.Direction.BottomToTop;

     var slidingGO = new GameObject("SlidingArea");
  slidingGO.transform.SetParent(scrollbarGO.transform, false);
   var slidingRect = slidingGO.AddComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
    slidingRect.offsetMin = Vector2.zero;
      slidingRect.offsetMax = Vector2.zero;

     var handleGO = new GameObject("Handle");
     handleGO.transform.SetParent(slidingGO.transform, false);
  var handleRect = handleGO.AddComponent<RectTransform>();
  handleRect.anchorMin = new Vector2(0, 0);
   handleRect.anchorMax = new Vector2(1, 0.3f);
            handleRect.offsetMin = Vector2.zero;
 handleRect.offsetMax = Vector2.zero;

   var handleImg = handleGO.AddComponent<Image>();
       handleImg.color = new Color(0.6f, 0.45f, 0.25f, 0.9f);

scrollbar.handleRect = handleRect;
     scrollbar.targetGraphic = handleImg;

      var scroll = templateGO.AddComponent<ScrollRect>();
       scroll.horizontal = false;
      scroll.vertical = true;
    scroll.movementType = ScrollRect.MovementType.Clamped;
    scroll.scrollSensitivity = 120f;  // Higher value = faster scrolling
     scroll.inertia = false;
  scroll.viewport = viewportRect;
   scroll.content = contentRect;
            scroll.verticalScrollbar = scrollbar;
  scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

    // Item template
    var itemGO = new GameObject("Item");
      itemGO.transform.SetParent(contentGO.transform, false);
            var itemRect = itemGO.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
   itemRect.pivot = new Vector2(0.5f, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 24);
         itemRect.anchoredPosition = Vector2.zero;

     var itemBgGO = new GameObject("Item Background");
        itemBgGO.transform.SetParent(itemGO.transform, false);
      var itemBgRect = itemBgGO.AddComponent<RectTransform>();
       itemBgRect.anchorMin = Vector2.zero;
    itemBgRect.anchorMax = Vector2.one;
   itemBgRect.offsetMin = Vector2.zero;
 itemBgRect.offsetMax = Vector2.zero;
     var itemBg = itemBgGO.AddComponent<Image>();
         itemBg.color = new Color(0.12f, 0.10f, 0.08f, 0.8f);

       var itemCheckGO = new GameObject("Item Checkmark");
itemCheckGO.transform.SetParent(itemGO.transform, false);
       var itemCheckRect = itemCheckGO.AddComponent<RectTransform>();
            itemCheckRect.anchorMin = new Vector2(0, 0.5f);
         itemCheckRect.anchorMax = new Vector2(0, 0.5f);
      itemCheckRect.pivot = new Vector2(0, 0.5f);
    itemCheckRect.anchoredPosition = new Vector2(4, 0);
 itemCheckRect.sizeDelta = new Vector2(12, 12);
        var itemCheck = itemCheckGO.AddComponent<Image>();
            itemCheck.color = new Color(1f, 0.7f, 0.3f);

  var itemLabelGO = new GameObject("Item Label");
            itemLabelGO.transform.SetParent(itemGO.transform, false);
            var itemLabelRect = itemLabelGO.AddComponent<RectTransform>();
 itemLabelRect.anchorMin = Vector2.zero;
itemLabelRect.anchorMax = Vector2.one;
       itemLabelRect.pivot = new Vector2(0.5f, 0.5f);
   itemLabelRect.offsetMin = new Vector2(22, 2);
itemLabelRect.offsetMax = new Vector2(-6, -2);

     var itemLabel = itemLabelGO.AddComponent<TextMeshProUGUI>();
    itemLabel.fontSize = FONT;
     itemLabel.color = TextColor;
         itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
    itemLabel.raycastTarget = false;
 itemLabel.textWrappingMode = TextWrappingModes.NoWrap;
  itemLabel.overflowMode = TextOverflowModes.Ellipsis;
            ApplyFont(itemLabel);

     var itemToggle = itemGO.AddComponent<Toggle>();
    itemToggle.targetGraphic = itemBg;
        itemToggle.graphic = itemCheck;
   itemToggle.isOn = true;

  templateGO.SetActive(false);

      var dropdown = go.AddComponent<TMP_Dropdown>();
    dropdown.template = templateRect;
            dropdown.captionText = caption;
   dropdown.itemText = itemLabel;
     dropdown.ClearOptions();
            // Add the 7 quest types: Kill, Collect, Harvest, Craft, Talk, Build, Move
       dropdown.AddOptions(new List<string>(QuestTypes));
     dropdown.RefreshShownValue();

    return dropdown;
 }

    private TMP_Dropdown CreateProfileDropdown(RectTransform parent, float xMin, float xMax, float y)
        {
         var go = new GameObject("ProfileDropdown");
go.transform.SetParent(parent, false);

   var rect = go.AddComponent<RectTransform>();
     rect.anchorMin = new Vector2(xMin, 1);
          rect.anchorMax = new Vector2(xMax, 1);
    rect.pivot = new Vector2(0, 1);
  rect.anchoredPosition = new Vector2(LABEL_W, y);
     rect.sizeDelta = new Vector2(-LABEL_W - 4, FIELD_HEIGHT);

var bg = go.AddComponent<Image>();
            bg.color = DropdownBgColor;
       bg.raycastTarget = true;

var captionGO = new GameObject("Label");
        captionGO.transform.SetParent(go.transform, false);
    var captionRect = captionGO.AddComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero;
    captionRect.anchorMax = Vector2.one;
  captionRect.offsetMin = new Vector2(6, 0);
            captionRect.offsetMax = new Vector2(-20, 0);

var caption = captionGO.AddComponent<TextMeshProUGUI>();
caption.fontSize = FONT;
caption.color = TextColor;
   caption.alignment = TextAlignmentOptions.MidlineLeft;
      caption.raycastTarget = false;
      caption.textWrappingMode = TextWrappingModes.NoWrap;
    caption.overflowMode = TextOverflowModes.Ellipsis;
            ApplyFont(caption);

   var arrowGO = new GameObject("Arrow");
    arrowGO.transform.SetParent(go.transform, false);
            var arrowRect = arrowGO.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0);
      arrowRect.anchorMax = new Vector2(1, 1);
       arrowRect.pivot = new Vector2(1, 0.5f);
            arrowRect.sizeDelta = new Vector2(18, 0);
       arrowRect.anchoredPosition = new Vector2(-2, 0);

   var arrow = arrowGO.AddComponent<TextMeshProUGUI>();
            arrow.text = "v";
      arrow.fontSize = 10;
   arrow.color = LabelColor;
    arrow.alignment = TextAlignmentOptions.Center;
  arrow.raycastTarget = false;
 ApplyFont(arrow);

            // Template - taller with working scroll
         var templateGO = new GameObject("Template");
            templateGO.transform.SetParent(go.transform, false);
            var templateRect = templateGO.AddComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
     templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
  templateRect.anchoredPosition = new Vector2(0, 0);
            templateRect.sizeDelta = new Vector2(0, 130);

         var templateBg = templateGO.AddComponent<Image>();
            templateBg.color = new Color(0.05f, 0.05f, 0.07f, 0.98f);

     var viewportGO = new GameObject("Viewport");
viewportGO.transform.SetParent(templateGO.transform, false);
  var viewportRect = viewportGO.AddComponent<RectTransform>();
    viewportRect.anchorMin = Vector2.zero;
    viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(2, 4);
    viewportRect.offsetMax = new Vector2(-12, -4);
            viewportGO.AddComponent<RectMask2D>();

         var contentGO = new GameObject("Content");
contentGO.transform.SetParent(viewportGO.transform, false);
       var contentRect = contentGO.AddComponent<RectTransform>();
       contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
contentRect.pivot = new Vector2(0, 1);
   contentRect.anchoredPosition = Vector2.zero;
   contentRect.sizeDelta = new Vector2(0, 24);

       // Scrollbar
            var scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(templateGO.transform, false);
            var scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
scrollbarRect.anchorMin = new Vector2(1, 0);
       scrollbarRect.anchorMax = new Vector2(1, 1);
  scrollbarRect.pivot = new Vector2(1, 0.5f);
      scrollbarRect.anchoredPosition = new Vector2(-2, 0);
   scrollbarRect.sizeDelta = new Vector2(8, -8);

            var scrollbarBg = scrollbarGO.AddComponent<Image>();
            scrollbarBg.color = new Color(0.15f, 0.15f, 0.18f, 0.8f);

            var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

    var slidingGO = new GameObject("SlidingArea");
          slidingGO.transform.SetParent(scrollbarGO.transform, false);
            var slidingRect = slidingGO.AddComponent<RectTransform>();
 slidingRect.anchorMin = Vector2.zero;
     slidingRect.anchorMax = Vector2.one;
          slidingRect.offsetMin = Vector2.zero;
   slidingRect.offsetMax = Vector2.zero;

     var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slidingGO.transform, false);
      var handleRect = handleGO.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
      handleRect.anchorMax = new Vector2(1, 0.3f);
            handleRect.offsetMin = Vector2.zero;
    handleRect.offsetMax = Vector2.zero;

            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.45f, 0.25f, 0.9f);

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImg;

   var scroll = templateGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
      scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 120f;  // Higher value = faster scrolling
            scroll.inertia = false;
    scroll.viewport = viewportRect;
        scroll.content = contentRect;
            scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

       // Item template
        var itemGO = new GameObject("Item");
      itemGO.transform.SetParent(contentGO.transform, false);
     var itemRect = itemGO.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
      itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.pivot = new Vector2(0.5f, 0.5f);
          itemRect.sizeDelta = new Vector2(0, 24);
            itemRect.anchoredPosition = Vector2.zero;

 var itemBgGO = new GameObject("Item Background");
itemBgGO.transform.SetParent(itemGO.transform, false);
    var itemBgRect = itemBgGO.AddComponent<RectTransform>();
         itemBgRect.anchorMin = Vector2.zero;
    itemBgRect.anchorMax = Vector2.one;
   itemBgRect.offsetMin = Vector2.zero;
            itemBgRect.offsetMax = Vector2.zero;
            var itemBg = itemBgGO.AddComponent<Image>();
            itemBg.color = new Color(0.12f, 0.10f, 0.08f, 0.8f);

            var itemCheckGO = new GameObject("Item Checkmark");
            itemCheckGO.transform.SetParent(itemGO.transform, false);
            var itemCheckRect = itemCheckGO.AddComponent<RectTransform>();
            itemCheckRect.anchorMin = new Vector2(0, 0.5f);
   itemCheckRect.anchorMax = new Vector2(0, 0.5f);
            itemCheckRect.pivot = new Vector2(0, 0.5f);
itemCheckRect.anchoredPosition = new Vector2(4, 0);
   itemCheckRect.sizeDelta = new Vector2(12, 12);
            var itemCheck = itemCheckGO.AddComponent<Image>();
            itemCheck.color = new Color(1f, 0.7f, 0.3f);

            var itemLabelGO = new GameObject("Item Label");
  itemLabelGO.transform.SetParent(itemGO.transform, false);
     var itemLabelRect = itemLabelGO.AddComponent<RectTransform>();
 itemLabelRect.anchorMin = Vector2.zero;
         itemLabelRect.anchorMax = Vector2.one;
   itemLabelRect.pivot = new Vector2(0.5f, 0.5f);
            itemLabelRect.offsetMin = new Vector2(22, 2);
          itemLabelRect.offsetMax = new Vector2(-6, -2);

            var itemLabel = itemLabelGO.AddComponent<TextMeshProUGUI>();
       itemLabel.fontSize = FONT;
            itemLabel.color = TextColor;
     itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
            itemLabel.raycastTarget = false;
     itemLabel.textWrappingMode = TextWrappingModes.NoWrap;
    itemLabel.overflowMode = TextOverflowModes.Ellipsis;
   ApplyFont(itemLabel);

     var itemToggle = itemGO.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBg;
    itemToggle.graphic = itemCheck;
     itemToggle.isOn = true;

   templateGO.SetActive(false);

    var dropdown = go.AddComponent<TMP_Dropdown>();
    dropdown.template = templateRect;
            dropdown.captionText = caption;
    dropdown.itemText = itemLabel;
     dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "(Select existing)" });
      dropdown.RefreshShownValue();

     return dropdown;
    }

    private Toggle CreateCheckbox(RectTransform parent, string label, float xRatio, float y)
        {
            var containerGO = new GameObject($"Checkbox_{label}");
     containerGO.transform.SetParent(parent, false);
var containerRect = containerGO.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(xRatio, 1);
        containerRect.anchorMax = new Vector2(1, 1);
            containerRect.pivot = new Vector2(0, 1);
            containerRect.anchoredPosition = new Vector2(10, y);
       containerRect.sizeDelta = new Vector2(-10, FIELD_HEIGHT);

       var toggleGO = new GameObject("Toggle");
    toggleGO.transform.SetParent(containerGO.transform, false);
      var toggleRect = toggleGO.AddComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0, 0.5f);
toggleRect.anchorMax = new Vector2(0, 0.5f);
   toggleRect.pivot = new Vector2(0, 0.5f);
   toggleRect.anchoredPosition = Vector2.zero;
     toggleRect.sizeDelta = new Vector2(16, 16);

 var toggleBg = toggleGO.AddComponent<Image>();
            toggleBg.color = CheckboxBgColor;
   toggleBg.raycastTarget = true;

    var checkGO = new GameObject("Checkmark");
       checkGO.transform.SetParent(toggleGO.transform, false);
            var checkRect = checkGO.AddComponent<RectTransform>();
checkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkRect.offsetMin = Vector2.zero;
checkRect.offsetMax = Vector2.zero;

 var checkImg = checkGO.AddComponent<Image>();
     checkImg.color = new Color(1f, 0.7f, 0.3f);

            var toggle = toggleGO.AddComponent<Toggle>();
         toggle.targetGraphic = toggleBg;
    toggle.graphic = checkImg;
            toggle.isOn = false;

            var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(containerGO.transform, false);
         var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(20, 0);
            labelRect.offsetMax = Vector2.zero;

          var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = FONT - 1;
            labelTmp.color = LabelColor;
    labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.raycastTarget = false;
       ApplyFont(labelTmp);

    return toggle;
   }

        private static void ApplyFont(TMP_Text text)
        {
            if (text == null) return;
            
            // Use UIBuilderHelper to apply the configured Body font
            // This respects user font preferences from BepInEx config
            UIBuilderHelper.ApplyBodyFont(text);
        }

      #endregion

    #region Profile Loading

    private void RefreshProfileDropdown()
      {
            _availableProfiles.Clear();
       _availableProfiles.Add("(None)");

   try
            {
  var profileNames = QuestProfileManager.GetAllProfileNames();
    foreach (var name in profileNames)
    {
    if (!string.IsNullOrEmpty(name))
      _availableProfiles.Add(name);
      }
         }
     catch (Exception ex)
            {
       Debug.LogWarning($"[QuestConfigScreenController] Failed to load profiles: {ex.Message}");
    }

      if (questProfileDropdown != null)
    {
        questProfileDropdown.ClearOptions();
     questProfileDropdown.AddOptions(_availableProfiles);
                questProfileDropdown.value = 0;
          questProfileDropdown.RefreshShownValue();

       var templateRect = questProfileDropdown.template;
    if (templateRect != null)
                {
   int itemCount = Mathf.Min(_availableProfiles.Count, 5);
            templateRect.sizeDelta = new Vector2(0, itemCount * 24 + 6);
       }
            }
        }

        #endregion

        #region Change Tracking

private void WireChangeListeners()
        {
       questIdInput?.onValueChanged.AddListener(_ => MarkDirty());
            questNameInput?.onValueChanged.AddListener(_ => MarkDirty());
questTypeDropdown?.onValueChanged.AddListener(OnQuestTypeDropdownChanged);
      autoCompleteToggle?.onValueChanged.AddListener(_ => MarkDirty());
          descriptionInput?.onValueChanged.AddListener(_ => MarkDirty());
            targetPrefabInput?.onValueChanged.AddListener(_ => MarkDirty());
            targetAmountInput?.onValueChanged.AddListener(_ => MarkDirty());
            targetLevelInput?.onValueChanged.AddListener(_ => MarkDirty());
            cooldownInput?.onValueChanged.AddListener(_ => MarkDirty());
        timeLimitInput?.onValueChanged.AddListener(_ => MarkDirty());
        rewardsInput?.onValueChanged.AddListener(_ => MarkDirty());
   requirementsInput?.onValueChanged.AddListener(_ => MarkDirty());
            previewImageInput?.onValueChanged.AddListener(_ => MarkDirty());
     onStartEventInput?.onValueChanged.AddListener(_ => MarkDirty());
        onCompleteEventInput?.onValueChanged.AddListener(_ => MarkDirty());
  questProfileInput?.onValueChanged.AddListener(_ => MarkDirty());
            questProfileDropdown?.onValueChanged.AddListener(_ => MarkDirty());
        }

private void OnQuestTypeDropdownChanged(int value)
        {
  MarkDirty();
   OnQuestTypeChanged(value);
    }

    private void MarkDirty()
        {
            _hasUnsavedChanges = true;
            if (statusText != null) statusText.text = "* Unsaved changes";
    }

        #endregion

        #region Public API

        public void Show(NpcController npc)
        {
   _currentNpc = npc;
          _hasUnsavedChanges = false;
  RefreshProfileDropdown();
     LoadQuestData(npc);
   if (statusText != null) statusText.text = "";
            screenRoot?.SetActive(true);
        
            // Show the apply button
if (applyButton != null)
        applyButton.gameObject.SetActive(true);

     LogScreenState();

Debug.Log($"[QuestConfigScreenController] Showing for NPC: {npc?.GetDisplayName() ?? "null"}");
        }

    private void LogScreenState()
     {
        if (screenRoot == null) return;

     var rect = screenRoot.GetComponent<RectTransform>();
 if (rect != null)
     {
     Debug.Log($"[QuestConfigScreenController] QuestConfigScreen RectTransform: " +
     $"active={screenRoot.activeSelf}, " +
       $"anchoredPos={rect.anchoredPosition}, " +
        $"sizeDelta={rect.sizeDelta}, " +
              $"anchorMin={rect.anchorMin}, anchorMax={rect.anchorMax}");
        }

  var allInputs = screenRoot.GetComponentsInChildren<TMP_InputField>(true);
       var allDropdowns = screenRoot.GetComponentsInChildren<TMP_Dropdown>(true);
            var allTexts = screenRoot.GetComponentsInChildren<TMP_Text>(true);

        Debug.Log($"[QuestConfigScreenController] Found {allInputs.Length} input fields, {allDropdowns.Length} dropdowns, {allTexts.Length} text components");

     int noFontCount = 0;
       foreach (var tmp in allTexts)
    {
                if (tmp.font == null || tmp.font.name.Contains("Liberation"))
      noFontCount++;
            }
        Debug.Log($"[QuestConfigScreenController] {noFontCount} text components missing proper font");
   }

        public void Hide()
        {
            screenRoot?.SetActive(false);
        
            // Hide the apply button
            if (applyButton != null)
              applyButton.gameObject.SetActive(false);
       
       _currentNpc = null;
        }

public bool IsVisible => screenRoot != null && screenRoot.activeSelf;
   public bool HasUnsavedChanges => _hasUnsavedChanges;

        public void ApplyChanges()
        {
            if (_currentNpc == null || !_initialized) return;

       string questId = questIdInput?.text?.Trim() ?? "";
   if (string.IsNullOrEmpty(questId))
            {
  SetStatus("Error: Quest ID required", true);
      return;
        }

       string profileName = "";

    // Priority: 1. New profile name entered, 2. Existing profile selected, 3. NPC's current quest profile
    string newProfileName = questProfileInput?.text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(newProfileName))
            {
     profileName = MakeSafeProfileName(newProfileName);
  }
       else if (questProfileDropdown != null && questProfileDropdown.value > 0)
     {
 profileName = _availableProfiles[questProfileDropdown.value];
  }
      else if (!string.IsNullOrEmpty(_currentNpc.questProfile))
     {
    // Use the NPC's existing quest profile
        profileName = _currentNpc.questProfile;
       }
      
    // If still no profile, require the user to specify one
   if (string.IsNullOrEmpty(profileName))
            {
       SetStatus("Error: Select or create a Quest Profile", true);
               return;
    }

try
         {
    string questConfig = BuildQuestConfig(questId);
 SaveQuestToDatabase(questId, questConfig);

             QuestProfileManager.EnsureProfileExists(profileName);
 QuestProfileManager.AddQuestToProfile(profileName, questId);

     _currentNpc.questProfile = profileName;
         _currentNpc.npcType = NpcType.QuestNpc;
     _currentNpc.SaveToZDO();

      _hasUnsavedChanges = false;
   SetStatus($"Saved '{questId}' to profile '{profileName}'", false);

     RefreshProfileDropdown();

       try { global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase(); } catch { }
       }
            catch (Exception ex)
      {
       SetStatus($"Error: {ex.Message}", true);
                Debug.LogError($"[QuestConfigScreenController] Save failed: {ex}");
        }
  }

        #endregion

#region Data Management

        private void LoadQuestData(NpcController npc)
        {
  ClearAllFields();
      if (npc != null)
      {
      if (questIdInput != null)
         questIdInput.text = MakeSafeQuestId(npc.npcProfileName ?? "NewQuest");

    if (!string.IsNullOrEmpty(npc.questProfile) && questProfileDropdown != null)
    {
         int idx = _availableProfiles.IndexOf(npc.questProfile);
        if (idx >= 0)
     questProfileDropdown.value = idx;
 }
   }
        }

        private void ClearAllFields()
        {
         if (questIdInput != null) questIdInput.text = "";
         if (questNameInput != null) questNameInput.text = "";
         if (questTypeDropdown != null) questTypeDropdown.value = 0;
         if (autoCompleteToggle != null) autoCompleteToggle.isOn = false;
         if (descriptionInput != null) descriptionInput.text = "";
         if (targetPrefabInput != null) targetPrefabInput.text = "";
         if (targetAmountInput != null) targetAmountInput.text = "1";
         if (targetLevelInput != null) targetLevelInput.text = "0";
         if (cooldownInput != null) cooldownInput.text = "0";
         if (timeLimitInput != null) timeLimitInput.text = "0";
         if (rewardsInput != null) rewardsInput.text = "";
         if (requirementsInput != null) requirementsInput.text = "";
         if (previewImageInput != null) previewImageInput.text = "";
         if (onStartEventInput != null) onStartEventInput.text = "";
         if (onCompleteEventInput != null) onCompleteEventInput.text = "";
         if (questProfileInput != null) questProfileInput.text = "";
         if (questProfileDropdown != null) questProfileDropdown.value = 0;
        }

        private string BuildQuestConfig(string questId)
        {
            var sb = new StringBuilder();

       sb.AppendLine($"[{questId}]");
     // Use config name (preserves "Move" for compatibility)
   sb.AppendLine($"QuestType: {QuestTypeConfigNames[questTypeDropdown?.value ?? 0]}");
            sb.AppendLine($"QuestName: {questNameInput?.text?.Trim() ?? "Unnamed Quest"}");
    sb.AppendLine($"Description: {descriptionInput?.text?.Trim() ?? "No description"}");

string targetPrefab = targetPrefabInput?.text?.Trim() ?? "Wolf";
            string targetAmount = targetAmountInput?.text?.Trim() ?? "1";
     sb.AppendLine($"QuestTarget: {targetPrefab},{targetAmount}");

    string minLevel = targetLevelInput?.text?.Trim() ?? "0";
     if (!string.IsNullOrEmpty(minLevel) && minLevel != "0")
            {
      // Use "Quality" for collect/craft, "MinLevel" for kill
 int questType = questTypeDropdown?.value ?? 0;
                if (questType == 1 || questType == 3) // Collect or Craft
sb.AppendLine($"Quality: {minLevel}");
    else
          sb.AppendLine($"MinLevel: {minLevel}");
 }

        string rewards = rewardsInput?.text?.Trim() ?? "";
if (!string.IsNullOrEmpty(rewards))
            {
           sb.AppendLine($"QuestReward: {rewards}");
            }

  string cooldown = cooldownInput?.text?.Trim() ?? "0";
            sb.AppendLine($"CooldownDays: {cooldown}");

            string timeLimit = timeLimitInput?.text?.Trim() ?? "0";
          sb.AppendLine($"TimeLimitMinutes: {timeLimit}");

            string requirements = requirementsInput?.text?.Trim() ?? "";
 if (!string.IsNullOrEmpty(requirements))
  {
        sb.AppendLine($"Requirement: {requirements}");
   }

          sb.AppendLine($"Autocomplete: {(autoCompleteToggle?.isOn == true ? "true" : "false")}");

            string previewImage = previewImageInput?.text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(previewImage))
            {
           sb.AppendLine($"PreviewImage: {previewImage}");
  }

      string onStart = onStartEventInput?.text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(onStart))
       {
     sb.AppendLine($"OnStartEvent: {onStart}");
            }

            string onComplete = onCompleteEventInput?.text?.Trim() ?? "";
       if (!string.IsNullOrEmpty(onComplete))
          {
sb.AppendLine($"OnCompleteEvent: {onComplete}");
   }

          return sb.ToString();
        }

        private void SaveQuestToDatabase(string questId, string questConfig)
    {
   Directory.CreateDirectory(QuestsConfigFolder);
       string filePath = Path.Combine(QuestsConfigFolder, "QuestDatabase.cfg");

    var content = new StringBuilder();
            if (File.Exists(filePath))
            {
      content.Append(File.ReadAllText(filePath));
       if (!content.ToString().EndsWith("\n"))
        content.AppendLine();
   content.AppendLine();
          }

    content.Append(questConfig);
            File.WriteAllText(filePath, content.ToString());

     Debug.Log($"[QuestConfigScreenController] Saved quest '{questId}' to {filePath}");
        }

        private string MakeSafeQuestId(string name)
        {
            var sb = new StringBuilder();
   foreach (char c in name)
        {
          if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
     else if (c == ' ') sb.Append('_');
       }
            return sb.Length > 0 ? sb.ToString() : "NewQuest";
        }

  private string MakeSafeProfileName(string name)
  {
        var sb = new StringBuilder();
       foreach (char c in name)
            {
     if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
   else if (c == ' ') sb.Append('_');
         }
          return sb.Length > 0 ? sb.ToString() : "default";
     }

        private void SetStatus(string message, bool isError)
        {
      if (statusText != null)
      {
    statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.5f, 0.5f) : new Color(0.5f, 1f, 0.5f);
            }
        }

    #endregion
    }
}
