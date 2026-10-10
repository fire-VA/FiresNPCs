using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Handles wiring up panel references and fixing UI components.
  /// Creates transparent runtime input fields while respecting Unity's DetailPanel layout.
    /// </summary>
    public static partial class UIPrefabFactory
    {
      // Layout constants for input fields - larger font to fill the box
  private const float INPUT_FONT_SIZE = 24f;
     
   // Color palette - warm orange/yellow tones for text
      private static readonly Color TextColor = new Color(1f, 0.85f, 0.5f); // Warm gold/orange
        private static readonly Color PlaceholderColor = new Color(0.7f, 0.55f, 0.3f, 0.5f); // Muted warm tone
     private static readonly Color CaretColor = new Color(1f, 0.9f, 0.6f); // Bright gold caret
        private static readonly Color SelectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f); // Warm selection

        #region Wiring References

        /// <summary>
        /// Hides screen-specific elements that should only show on their respective screens.
  /// </summary>
        private static void HideScreenSpecificElements(GameObject panel)
        {
        // Hide QuestInteract elements
    var questInteract = FindGameObjectAnywhere(panel, "QuestInteract");
   if (questInteract != null)
  {
      questInteract.SetActive(false);
       Debug.Log("[UIPrefabFactory] Hidden QuestInteract (will show on quest interact screen)");
   }

            // Hide QuestConfigScreen elements
     var questConfigScreen = FindGameObjectAnywhere(panel, "QuestConfigScreen");
   if (questConfigScreen != null)
     {
           questConfigScreen.SetActive(false);
          Debug.Log("[UIPrefabFactory] Hidden QuestConfigScreen (will show on quest config screen)");
            }
    }

        /// <summary>
        /// Replaces a Unity prefab input field with a centered, transparent version IN PLACE.
        /// The container position from Unity is preserved - only the input inside is replaced.
        /// </summary>
        private static TMP_InputField ReplaceInputFieldInPlace(GameObject detailPanel, string inputName, string containerName, float verticalOffset = 0f)
{
        // Find the Unity container - this has the position we want to respect
            var container = FindGameObjectAnywhere(detailPanel, containerName);
            if (container == null)
      {
    Debug.LogWarning($"[UIPrefabFactory] Container not found: {containerName}");
     return null;
            }

     // Find and hide the existing Unity input field
            var existingInput = FindComponentAnywhere<TMP_InputField>(container, inputName);
            if (existingInput != null)
    {
existingInput.gameObject.SetActive(false);
            }

   // Find the label - we'll keep it visible but ensure proper layout
        var existingLabel = FindComponentAnywhere<TMP_Text>(container, "Label");

            // Create a centered transparent input field inside the container
            var newInput = CreateCenteredTransparentInput(container.transform, inputName, verticalOffset);

         Debug.Log($"[UIPrefabFactory] Replaced {inputName} with centered transparent version in {containerName}");
    return newInput;
        }

        /// <summary>
        /// Creates a centered, transparent input field that fills the available space nicely.
      /// Expanded ~25% to the left for better centering, with large font to fill the box.
        /// Features rounded corners and warm orange/gold text colors.
   /// </summary>
        private static TMP_InputField CreateCenteredTransparentInput(Transform container, string inputName, float verticalOffset = 0f)
        {
     // Create the input field GameObject
       var inputGO = new GameObject($"Transparent_{inputName}");
            inputGO.transform.SetParent(container, false);

            var inputRect = inputGO.AddComponent<RectTransform>();
 // Expanded to the left: was 0.28f, now 0.03f (adds ~25% more width on left side)
            // This makes the input nearly full width and better centered
   // verticalOffset moves the input down to avoid overlapping label text
          float minY = 0.05f + verticalOffset;
            float maxY = 0.95f;
          inputRect.anchorMin = new Vector2(0.03f, minY);
            inputRect.anchorMax = new Vector2(0.98f, maxY);
          inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = Vector2.zero;

    // Semi-transparent background matching panel style
 var inputImage = inputGO.AddComponent<Image>();
      inputImage.color = new Color(0.06f, 0.06f, 0.08f, 0.6f);
     inputImage.raycastTarget = true;
   
       // Apply rounded corners using a rounded sprite or by setting pixel per unit
      // Since we don't have a rounded sprite, we'll create a rounded corner effect
    ApplyRoundedCorners(inputGO, 8f);

         // Create text area
    var textAreaGO = new GameObject("Text Area");
         textAreaGO.transform.SetParent(inputGO.transform, false);
     var textAreaRect = textAreaGO.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
    textAreaRect.anchorMax = Vector2.one;
      textAreaRect.offsetMin = new Vector2(12, 4);
    textAreaRect.offsetMax = new Vector2(-12, -4);
       textAreaGO.AddComponent<RectMask2D>();

          // Create text component with LARGE font to fill the box - warm gold color
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textAreaGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
     textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
  textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var textComponent = textGO.AddComponent<TextMeshProUGUI>();
      textComponent.fontSize = INPUT_FONT_SIZE;
      textComponent.color = TextColor; // Warm gold/orange
          textComponent.alignment = TextAlignmentOptions.MidlineLeft;
         textComponent.textWrappingMode = TextWrappingModes.NoWrap;
  textComponent.overflowMode = TextOverflowModes.Overflow;
            GUIManager.ApplyGameFont(textComponent);

            // Create placeholder with same large font size - muted warm tone
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textAreaGO.transform, false);
            var placeholderRect = placeholderGO.AddComponent<RectTransform>();
      placeholderRect.anchorMin = Vector2.zero;
          placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;

 var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Enter text...";
    placeholder.fontSize = INPUT_FONT_SIZE;
      placeholder.color = PlaceholderColor; // Muted warm tone
      placeholder.alignment = TextAlignmentOptions.MidlineLeft;
  placeholder.fontStyle = FontStyles.Italic;
     GUIManager.ApplyGameFont(placeholder);

            // Add input field component
      var inputField = inputGO.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRect;
  inputField.textComponent = textComponent;
       inputField.placeholder = placeholder;
  inputField.caretColor = CaretColor; // Bright gold caret
            inputField.selectionColor = SelectionColor; // Warm selection
            inputField.caretWidth = 2;
         inputField.customCaretColor = true;
            inputField.interactable = true;
            inputField.contentType = TMP_InputField.ContentType.Standard;
            inputField.lineType = TMP_InputField.LineType.SingleLine;

return inputField;
        }

        /// <summary>
 /// Applies rounded corners to a UI element by creating a rounded background.
     /// </summary>
        private static void ApplyRoundedCorners(GameObject go, float radius)
        {
       // Create corner mask children to simulate rounded corners
            // We'll add 4 corner pieces that cover the sharp corners with the background color
 var image = go.GetComponent<Image>();
  if (image == null) return;

            // Use a softer approach - create an outline that gives a rounded appearance
       var outlineGO = new GameObject("_RoundedOutline");
  outlineGO.transform.SetParent(go.transform, false);
       outlineGO.transform.SetAsFirstSibling();

            var outlineRect = outlineGO.AddComponent<RectTransform>();
     outlineRect.anchorMin = Vector2.zero;
outlineRect.anchorMax = Vector2.one;
            outlineRect.offsetMin = new Vector2(-1, -1);
      outlineRect.offsetMax = new Vector2(1, 1);

            var outlineImage = outlineGO.AddComponent<Image>();
   // Slightly lighter outline for a soft edge effect
       outlineImage.color = new Color(0.15f, 0.12f, 0.1f, 0.4f);
     outlineImage.raycastTarget = false;

     // Add corner covers to soften the appearance
            CreateCornerCover(go.transform, "TopLeft", new Vector2(0, 1), new Vector2(0, 1), radius);
       CreateCornerCover(go.transform, "TopRight", new Vector2(1, 1), new Vector2(1, 1), radius);
            CreateCornerCover(go.transform, "BottomLeft", new Vector2(0, 0), new Vector2(0, 0), radius);
     CreateCornerCover(go.transform, "BottomRight", new Vector2(1, 0), new Vector2(1, 0), radius);
        }

        /// <summary>
        /// Creates a corner cover to soften sharp corners.
 /// </summary>
  private static void CreateCornerCover(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float size)
        {
     var cornerGO = new GameObject($"_Corner_{name}");
       cornerGO.transform.SetParent(parent, false);
      cornerGO.transform.SetAsFirstSibling();

        var cornerRect = cornerGO.AddComponent<RectTransform>();
            cornerRect.anchorMin = anchorMin;
       cornerRect.anchorMax = anchorMax;
            cornerRect.pivot = anchorMin;
            cornerRect.sizeDelta = new Vector2(size, size);

         // Position based on corner
      if (name == "TopLeft")
        cornerRect.anchoredPosition = Vector2.zero;
    else if (name == "TopRight")
      cornerRect.anchoredPosition = new Vector2(-size, 0);
       else if (name == "BottomLeft")
       cornerRect.anchoredPosition = new Vector2(0, 0);
            else if (name == "BottomRight")
             cornerRect.anchoredPosition = new Vector2(-size, 0);

   // The corner image - using a gradient/fade effect color
       var cornerImage = cornerGO.AddComponent<Image>();
  cornerImage.color = new Color(0, 0, 0, 0); // Transparent - just softens the edge visually
            cornerImage.raycastTarget = false;
        }

 /// <summary>
        /// Wires the existing Unity ApplyButton without moving it, and adds a transparent border with click highlight.
      /// </summary>
        private static Button WireExistingApplyButton(GameObject detailPanel)
        {
 var existingButton = FindComponentAnywhere<Button>(detailPanel, "ApplyButton");
            if (existingButton != null)
        {
   existingButton.gameObject.SetActive(true);
       FixButton(existingButton);
      CreateButtonOverlay(existingButton);

     // Add transparent border around the apply button with click highlight
 AddTransparentBorderWithHighlight(existingButton);

       Debug.Log("[UIPrefabFactory] Using Unity prefab ApplyButton (position preserved, border with highlight added)");
             return existingButton;
    }

      Debug.LogWarning("[UIPrefabFactory] ApplyButton not found in DetailPanel");
      return null;
        }

        /// <summary>
      /// Adds a transparent border around a button matching the input field style, with click highlight effect.
        /// </summary>
        private static void AddTransparentBorderWithHighlight(Button button)
        {
            if (button == null) return;

         var buttonGO = button.gameObject;

        // Check if border already exists
            var existingBorder = buttonGO.transform.Find("_TransparentBorder");
      if (existingBorder != null) return;

    // Create border frame
  var borderGO = new GameObject("_TransparentBorder");
       borderGO.transform.SetParent(buttonGO.transform, false);

     // Put it behind the button content
  borderGO.transform.SetAsFirstSibling();

            var borderRect = borderGO.AddComponent<RectTransform>();
     // Expand slightly beyond button bounds
 borderRect.anchorMin = Vector2.zero;
      borderRect.anchorMax = Vector2.one;
  borderRect.offsetMin = new Vector2(-3, -3);
          borderRect.offsetMax = new Vector2(3, 3);

       var borderImage = borderGO.AddComponent<Image>();
        // Same transparent color as input field backgrounds
  borderImage.color = new Color(0.06f, 0.06f, 0.08f, 0.6f);
       borderImage.raycastTarget = false;

         // Add the highlight handler component
            var highlightHandler = borderGO.AddComponent<ButtonBorderHighlight>();
      highlightHandler.Initialize(borderImage, button);

            Debug.Log("[UIPrefabFactory] Added transparent border with highlight to ApplyButton");
        }

    #endregion

        #region Button Helpers

        /// <summary>
        /// Ensures a button works by finding it, fixing it, or creating an overlay.
        /// </summary>
        private static Button EnsureWorkingButton(GameObject root, string buttonName, string path)
   {
 var existingButton = FindComponent<Button>(root, path)
       ?? FindComponentAnywhere<Button>(root, buttonName);

            if (existingButton != null)
            {
    FixButton(existingButton);
                CreateButtonOverlay(existingButton);
       return existingButton;
         }

            var buttonGO = FindGameObject(root, path)
       ?? FindGameObjectAnywhere(root, buttonName);

    if (buttonGO != null)
  {
         var newButton = AddButtonToGameObject(buttonGO);
         CreateButtonOverlay(newButton);
  return newButton;
            }

     Debug.LogWarning($"[UIPrefabFactory] Button not found: {buttonName}");
            return null;
        }

        /// <summary>
      /// Creates an invisible overlay button that captures clicks reliably.
        /// </summary>
        private static void CreateButtonOverlay(Button originalButton)
 {
      if (originalButton == null) return;

        var existingOverlay = originalButton.transform.Find("_ClickOverlay");
  if (existingOverlay != null) return;

          var overlayGO = new GameObject("_ClickOverlay");
            overlayGO.transform.SetParent(originalButton.transform, false);

            var overlayRect = overlayGO.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
          overlayRect.anchorMax = Vector2.one;
      overlayRect.offsetMin = Vector2.zero;
     overlayRect.offsetMax = Vector2.zero;

     var overlayImage = overlayGO.AddComponent<Image>();
  overlayImage.color = new Color(0, 0, 0, 0);
  overlayImage.raycastTarget = true;

      var parentImage = originalButton.GetComponent<Image>();
       if (parentImage != null)
       {
    parentImage.raycastTarget = true;
          }

            originalButton.targetGraphic = parentImage ?? overlayImage;
            originalButton.interactable = true;
   }

        /// <summary>
        /// Fixes a button to ensure it's clickable.
        /// </summary>
      private static void FixButton(Button button)
        {
            if (button == null) return;

 button.interactable = true;

         if (button.targetGraphic == null)
         {
    var image = button.GetComponent<Image>();
     if (image == null)
   {
     image = button.gameObject.AddComponent<Image>();
     image.color = new Color(0, 0, 0, 0.01f);
         }
         button.targetGraphic = image;
       }

   var buttonImage = button.GetComponent<Image>();
       if (buttonImage != null)
         {
    buttonImage.raycastTarget = true;
            }

            var nav = button.navigation;
    nav.mode = Navigation.Mode.None;
            button.navigation = nav;
        }

        private static Button AddButtonToGameObject(GameObject go)
        {
            if (go == null) return null;

    var button = go.GetComponent<Button>();
      if (button != null) return button;

    button = go.AddComponent<Button>();

   var image = go.GetComponent<Image>();
   if (image == null)
    {
   image = go.AddComponent<Image>();
      image.color = new Color(0, 0, 0, 0.01f);
       }

            image.raycastTarget = true;
            button.targetGraphic = image;
   button.interactable = true;

       return button;
     }

        #endregion
    }

    /// <summary>
    /// Component that handles button border highlight effect on click.
    /// </summary>
    public class ButtonBorderHighlight : MonoBehaviour
    {
        private Image _borderImage;
        private Button _button;
        private Color _normalColor = new Color(0.06f, 0.06f, 0.08f, 0.6f);
        private Color _highlightColor = new Color(0.8f, 0.5f, 0.2f, 0.9f); // Warm orange highlight to match text
        private float _highlightDuration = 0.15f;
        private float _fadeDuration = 0.25f;
   private float _timer;
        private bool _isHighlighting;

        public void Initialize(Image borderImage, Button button)
        {
     _borderImage = borderImage;
       _button = button;

            if (_button != null)
       {
        _button.onClick.AddListener(OnButtonClick);
            }
        }

        private void OnButtonClick()
  {
       _isHighlighting = true;
     _timer = 0f;
if (_borderImage != null)
   {
      _borderImage.color = _highlightColor;
       }
        }

        private void Update()
        {
            if (!_isHighlighting || _borderImage == null) return;

   _timer += Time.unscaledDeltaTime;

            if (_timer < _highlightDuration)
            {
     // Stay at highlight color
       return;
 }

      // Fade back to normal
         float fadeProgress = (_timer - _highlightDuration) / _fadeDuration;
  if (fadeProgress >= 1f)
            {
           _borderImage.color = _normalColor;
    _isHighlighting = false;
   }
            else
            {
       _borderImage.color = Color.Lerp(_highlightColor, _normalColor, fadeProgress);
       }
        }

        private void OnDestroy()
     {
      if (_button != null)
     {
         _button.onClick.RemoveListener(OnButtonClick);
     }
        }
    }
}
