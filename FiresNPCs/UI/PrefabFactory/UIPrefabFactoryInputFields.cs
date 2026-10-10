using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Handles creating runtime input fields and fixing text components.
    /// </summary>
    public static partial class UIPrefabFactory
    {
     #region Runtime Input Field Creation

        /// <summary>
        /// Creates a fully functional input field at runtime with semi-transparent styling.
     /// </summary>
      private static TMP_InputField CreateRuntimeInputField(Transform parent, string inputName, string labelText, int index)
        {
// Create container
          var containerGO = new GameObject($"Runtime_{inputName}_Container");
      containerGO.transform.SetParent(parent, false);
  
     var containerRect = containerGO.AddComponent<RectTransform>();
       containerRect.anchorMin = new Vector2(0, 1);
   containerRect.anchorMax = new Vector2(1, 1);
        containerRect.pivot = new Vector2(0.5f, 1);
         containerRect.sizeDelta = new Vector2(-16, 36); // Slightly taller for better text visibility
          
 // Position based on index
          float yOffset = -8 - (index * 42);
       containerRect.anchoredPosition = new Vector2(0, yOffset);

      // Add layout
            var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
       layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
     layout.childForceExpandWidth = true;
     layout.padding = new RectOffset(8, 8, 3, 3);

       // Create label
 var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(containerGO.transform, false);
    labelGO.AddComponent<RectTransform>();
   var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = labelText;
    label.fontSize = 12;
   label.enableAutoSizing = true;
            label.fontSizeMin = 9;
            label.fontSizeMax = 12;
            label.color = Color.white;
label.alignment = TextAlignmentOptions.MidlineLeft;
          var labelLayout = labelGO.AddComponent<LayoutElement>();
   labelLayout.preferredWidth = 95; // Shorter label to give more room to input
      labelLayout.minWidth = 70;
          GUIManager.ApplyGameFont(label);

  // Create input field container
            var inputGO = new GameObject(inputName);
            inputGO.transform.SetParent(containerGO.transform, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
  
        // Add semi-transparent background
       var inputImage = inputGO.AddComponent<Image>();
  inputImage.color = new Color(0.1f, 0.1f, 0.12f, 0.75f); // Semi-transparent dark
       inputImage.raycastTarget = true;

  // Create text area with viewport
   var textAreaGO = new GameObject("Text Area");
            textAreaGO.transform.SetParent(inputGO.transform, false);
       var textAreaRect = textAreaGO.AddComponent<RectTransform>();
         textAreaRect.anchorMin = Vector2.zero;
    textAreaRect.anchorMax = Vector2.one;
  textAreaRect.offsetMin = new Vector2(6, 3);
            textAreaRect.offsetMax = new Vector2(-6, -3);

    // Add mask for text clipping
   textAreaGO.AddComponent<RectMask2D>();

            // Create the actual text component
   var textGO = new GameObject("Text");
            textGO.transform.SetParent(textAreaGO.transform, false);
 var textRect = textGO.AddComponent<RectTransform>();
       textRect.anchorMin = Vector2.zero;
      textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
   textRect.offsetMax = Vector2.zero;

            var textComponent = textGO.AddComponent<TextMeshProUGUI>();
            textComponent.fontSize = 11; // Smaller font for better fit
         textComponent.color = Color.white;
    textComponent.alignment = TextAlignmentOptions.MidlineLeft;
    textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            textComponent.overflowMode = TextOverflowModes.Overflow;
            GUIManager.ApplyGameFont(textComponent);

       // Create placeholder
            var placeholderGO = new GameObject("Placeholder");
placeholderGO.transform.SetParent(textAreaGO.transform, false);
            var placeholderRect = placeholderGO.AddComponent<RectTransform>();
          placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;

   var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
 placeholder.text = "Enter text...";
      placeholder.fontSize = 11;
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.fontStyle = FontStyles.Italic;
            GUIManager.ApplyGameFont(placeholder);

        // Add the input field component
      var inputField = inputGO.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRect;
  inputField.textComponent = textComponent;
      inputField.placeholder = placeholder;
       inputField.caretColor = Color.white;
            inputField.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.4f);
     inputField.caretWidth = 2;
   inputField.customCaretColor = true;
         inputField.interactable = true;
        inputField.contentType = TMP_InputField.ContentType.Standard;
  inputField.lineType = TMP_InputField.LineType.SingleLine;

      // Ensure proper layering - give input field more flexible width
     var inputLayout = inputGO.AddComponent<LayoutElement>();
 inputLayout.flexibleWidth = 2; // More weight to the input field
  inputLayout.minWidth = 120;

         return inputField;
        }

        #endregion

      #region Text Component Fixes

        /// <summary>
        /// Fixes all text components in the hierarchy.
        /// </summary>
        private static void FixAllTextComponents(GameObject root)
        {
            if (root == null) return;
  
 var font = GUIManager.GetGameFont();
       int fontApplied = 0;
    
     var allTMP = root.GetComponentsInChildren<TMP_Text>(true);
         Debug.Log($"[UIPrefabFactory] Found {allTMP.Length} TMP_Text components in hierarchy");
   
            foreach (var tmp in allTMP)
      {
           if (font != null && tmp.font == null)
      {
             tmp.font = font;
     fontApplied++;
         }
    }
  
    Debug.Log($"[UIPrefabFactory] FixAllTextComponents: applied font to {fontApplied} components");
    
      FixButtons(root);
    DisableRogueTextObjects(root);
  EnsureDetailPanelVisible(root);
        }
        
     private static void DisableRogueTextObjects(GameObject root)
        {
            string[] rogueObjectNames = { "charactername", "charactername (1)", "topictext", "topic" };
 
            foreach (var name in rogueObjectNames)
      {
     var rogueObj = FindGameObjectAnywhere(root, name);
     if (rogueObj != null)
 {
          var tmpText = rogueObj.GetComponent<TMP_Text>();
   if (tmpText != null)
             {
          string text = tmpText.text ?? "";
            if (!string.IsNullOrEmpty(text) && 
          !text.Equals("X", StringComparison.OrdinalIgnoreCase) &&
     !text.Equals("L", StringComparison.OrdinalIgnoreCase) &&
      !text.Equals("S", StringComparison.OrdinalIgnoreCase) &&
          !text.Equals("M", StringComparison.OrdinalIgnoreCase))
            {
      Debug.Log($"[UIPrefabFactory] Disabling rogue text object: '{rogueObj.name}'");
      rogueObj.SetActive(false);
        }
}
       }
         }
  }
      
        private static void FixButtons(GameObject root)
   {
   var buttons = root.GetComponentsInChildren<Button>(true);
         int fixedCount = 0;
  
    foreach (var button in buttons)
            {
        bool needsFix = false;
    
         if (button.targetGraphic == null)
    {
       var image = button.GetComponent<Image>();
  if (image != null)
          {
       button.targetGraphic = image;
            needsFix = true;
        }
   }
       
  var buttonImage = button.GetComponent<Image>();
    if (buttonImage != null && !buttonImage.raycastTarget)
     {
         buttonImage.raycastTarget = true;
    needsFix = true;
    }
   
      if (!button.interactable)
   {
        button.interactable = true;
     needsFix = true;
      }
      
       if (needsFix)
          {
           fixedCount++;
     }
          }
    
  Debug.Log($"[UIPrefabFactory] FixButtons: checked {buttons.Length} buttons, fixed {fixedCount}");
        }
  
        private static void EnsureDetailPanelVisible(GameObject root)
        {
            var detailPanel = FindGameObjectAnywhere(root, "DetailPanel");
if (detailPanel == null) return;

            detailPanel.SetActive(true);
  
     foreach (Transform child in detailPanel.transform)
    {
            child.gameObject.SetActive(true);
 }
     
     Debug.Log($"[UIPrefabFactory] Ensured DetailPanel visibility - children: {detailPanel.transform.childCount}");
     }

 #endregion
    }
}
