using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Programmatic panel creation (fallback when no asset bundle).
    /// </summary>
    public static partial class UIPrefabFactory
    {
        // Shared color palette - warm orange/yellow tones
        private static readonly Color WarmTextColor = new Color(1f, 0.85f, 0.5f); // Warm gold/orange

        #region Programmatic Panel Creation

        private static GameObject CreateHeader(Transform parent)
        {
var headerGO = new GameObject("Header");
     headerGO.transform.SetParent(parent, false);

      var headerRect = headerGO.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 0.92f);
            headerRect.anchorMax = new Vector2(1, 1);
      headerRect.offsetMin = Vector2.zero;
   headerRect.offsetMax = Vector2.zero;

            headerGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);

     var titleGO = new GameObject("TitleText");
  titleGO.transform.SetParent(headerGO.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0);
      titleRect.anchorMax = new Vector2(0.9f, 1);
  titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

    var titleText = titleGO.AddComponent<TextMeshProUGUI>();
      titleText.text = "NPC Configuration";
            titleText.fontSize = 30;  // Increased from 16 for larger header
            titleText.color = WarmTextColor; // Warm gold title
            titleText.alignment = TextAlignmentOptions.Center;
       titleText.fontStyle = FontStyles.Bold;

            var closeGO = new GameObject("CloseButton");
            closeGO.transform.SetParent(headerGO.transform, false);
            var closeRect = closeGO.AddComponent<RectTransform>();
  closeRect.anchorMin = new Vector2(0.92f, 0.1f);
            closeRect.anchorMax = new Vector2(0.99f, 0.9f);
            closeRect.offsetMin = Vector2.zero;
  closeRect.offsetMax = Vector2.zero;

            var closeImage = closeGO.AddComponent<Image>();
        closeImage.color = new Color(0.6f, 0.2f, 0.2f, 1f);
   closeImage.raycastTarget = true;

     var closeButton = closeGO.AddComponent<Button>();
       closeButton.targetGraphic = closeImage;
 closeButton.interactable = true;

         var closeTextGO = new GameObject("Text");
            closeTextGO.transform.SetParent(closeGO.transform, false);
          var closeTextRect = closeTextGO.AddComponent<RectTransform>();
       closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
   closeTextRect.offsetMin = Vector2.zero;
   closeTextRect.offsetMax = Vector2.zero;

       var closeText = closeTextGO.AddComponent<TextMeshProUGUI>();
            closeText.text = "X";
            closeText.fontSize = 14;
            closeText.color = WarmTextColor; // Warm gold X
       closeText.alignment = TextAlignmentOptions.Center;

            return headerGO;
  }

     private static GameObject CreateSizeButton(Transform parent)
        {
            var buttonGO = new GameObject("SizeButton");
         buttonGO.transform.SetParent(parent, false);

            var buttonRect = buttonGO.AddComponent<RectTransform>();
    buttonRect.anchorMin = new Vector2(0, 0.92f);
            buttonRect.anchorMax = new Vector2(0.08f, 1);
            buttonRect.offsetMin = new Vector2(5, 2);
            buttonRect.offsetMax = new Vector2(-2, -2);

   var buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        buttonImage.raycastTarget = true;

            var button = buttonGO.AddComponent<Button>();
  button.targetGraphic = buttonImage;
         button.interactable = true;

            var textGO = new GameObject("Text");
     textGO.transform.SetParent(buttonGO.transform, false);
    var textRect = textGO.AddComponent<RectTransform>();
     textRect.anchorMin = Vector2.zero;
 textRect.anchorMax = Vector2.one;
textRect.offsetMin = Vector2.zero;
    textRect.offsetMax = Vector2.zero;

        var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = "L";
            text.fontSize = 16;
     text.color = WarmTextColor; // Warm gold size button
            text.alignment = TextAlignmentOptions.Center;

  return buttonGO;
        }

#endregion

        #region Entry Prefab Creation

        private static GameObject CreateEntryPrefab(string name, Color? bgColor = null)
        {
   var entryGO = new GameObject(name);

  entryGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 60);
        entryGO.AddComponent<Image>().color = bgColor ?? new Color(0.2f, 0.2f, 0.2f, 0.9f);
            entryGO.AddComponent<Button>();

       var layout = entryGO.AddComponent<HorizontalLayoutGroup>();
      layout.spacing = 10;
          layout.childAlignment = TextAnchor.MiddleLeft;
  layout.padding = new RectOffset(10, 10, 5, 5);

            var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(entryGO.transform, false);
    iconGO.AddComponent<RectTransform>().sizeDelta = new Vector2(50, 50);
         iconGO.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
         iconGO.AddComponent<LayoutElement>().preferredWidth = 50;

            var textGO = new GameObject("Text");
  textGO.transform.SetParent(entryGO.transform, false);
            textGO.AddComponent<RectTransform>();
            var text = textGO.AddComponent<TextMeshProUGUI>();
   text.text = "Entry Text";
  text.fontSize = 14;
    text.color = WarmTextColor; // Warm gold entry text

            ApplyFontsToHierarchy(entryGO);
        return entryGO;
        }

    #endregion
    }
}
