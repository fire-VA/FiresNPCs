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
        private static readonly Color WarmTextInactive = new Color(0.8f, 0.65f, 0.4f); // Muted warm for inactive

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

        private static GameObject CreateTabBar(Transform parent)
  {
            var tabBarGO = new GameObject("TabsBar");
      tabBarGO.transform.SetParent(parent, false);

            var tabRect = tabBarGO.AddComponent<RectTransform>();
          tabRect.anchorMin = new Vector2(0, 0.84f);
      tabRect.anchorMax = new Vector2(1, 0.92f);
            tabRect.offsetMin = new Vector2(10, 0);
          tabRect.offsetMax = new Vector2(-10, 0);

  var layout = tabBarGO.AddComponent<HorizontalLayoutGroup>();
 layout.spacing = 5;
            layout.childAlignment = TextAnchor.MiddleCenter;
      layout.childControlHeight = true;
     layout.childControlWidth = true;
      layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;

            CreateTabButton(tabBarGO.transform, "QuestNpcTab", "Quest");
     CreateTabButton(tabBarGO.transform, "InfoNpcTab", "Info");
   CreateTabButton(tabBarGO.transform, "DialogueNpcTab", "Dialogue");
 CreateTabButton(tabBarGO.transform, "TraderTab", "Trader");

            return tabBarGO;
     }

        private static void CreateTabButton(Transform parent, string name, string label)
  {
            var buttonGO = new GameObject(name);
    buttonGO.transform.SetParent(parent, false);
            buttonGO.AddComponent<RectTransform>();

            var image = buttonGO.AddComponent<Image>();
      image.color = new Color(0.25f, 0.25f, 0.25f, 1f);
          image.raycastTarget = true;

    var button = buttonGO.AddComponent<Button>();
       button.targetGraphic = image;
            button.interactable = true;

            var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
       var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
          textRect.anchorMax = Vector2.one;
  textRect.offsetMin = Vector2.zero;
      textRect.offsetMax = Vector2.zero;

            var text = textGO.AddComponent<TextMeshProUGUI>();
  text.text = label;
     text.fontSize = 14;
            text.color = WarmTextColor; // Warm gold tab text
 text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
}

        private static GameObject CreateSearchBar(Transform parent)
        {
            var searchBarGO = new GameObject("SearchBar");
            searchBarGO.transform.SetParent(parent, false);

      var searchRect = searchBarGO.AddComponent<RectTransform>();
    searchRect.anchorMin = new Vector2(0, 0.76f);
            searchRect.anchorMax = new Vector2(1, 0.84f);
     searchRect.offsetMin = new Vector2(10, 2);
         searchRect.offsetMax = new Vector2(-10, -2);

            searchBarGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var inputField = searchBarGO.AddComponent<TMP_InputField>();

  var textAreaGO = new GameObject("TextArea");
  textAreaGO.transform.SetParent(searchBarGO.transform, false);
 var textAreaRect = textAreaGO.AddComponent<RectTransform>();
   textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
 textAreaRect.offsetMin = new Vector2(10, 2);
          textAreaRect.offsetMax = new Vector2(-10, -2);

   var textArea = textAreaGO.AddComponent<TextMeshProUGUI>();
        textArea.fontSize = 14;
            textArea.color = WarmTextColor; // Warm gold search text

          inputField.textComponent = textArea;
            inputField.textViewport = textAreaRect;

          return searchBarGO;
     }

        private static GameObject CreateDetailPanel(Transform parent)
        {
         var detailGO = new GameObject("DetailPanel");
   detailGO.transform.SetParent(parent, false);

            var detailRect = detailGO.AddComponent<RectTransform>();
         detailRect.anchorMin = new Vector2(0.05f, 0.15f);
    detailRect.anchorMax = new Vector2(0.95f, 0.72f);
 detailRect.offsetMin = Vector2.zero;
          detailRect.offsetMax = Vector2.zero;

detailGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = detailGO.AddComponent<VerticalLayoutGroup>();
         layout.spacing = 10;
   layout.childAlignment = TextAnchor.UpperCenter;
     layout.childControlHeight = false;
     layout.childControlWidth = true;
     layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
     layout.padding = new RectOffset(15, 15, 15, 15);

         CreateLabeledInput(detailGO.transform, "Name Override:", "NameOverrideInput");
    CreateLabeledInput(detailGO.transform, "Profile:", "ProfileInput");
            CreateLabeledInput(detailGO.transform, "Model Override:", "ModelOverrideInput");
         CreateApplyButton(detailGO.transform);

            return detailGO;
        }

        private static void CreateLabeledInput(Transform parent, string labelText, string inputName)
        {
            var containerGO = new GameObject($"Container_{inputName}");
        containerGO.transform.SetParent(parent, false);
  containerGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 40);

     var layout = containerGO.AddComponent<HorizontalLayoutGroup>();
layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
  layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(containerGO.transform, false);
       labelGO.AddComponent<RectTransform>();
       var label = labelGO.AddComponent<TextMeshProUGUI>();
label.text = labelText;
 label.fontSize = 14;
            label.color = WarmTextColor; // Warm gold labels
            labelGO.AddComponent<LayoutElement>().preferredWidth = 120;

     var inputGO = new GameObject(inputName);
 inputGO.transform.SetParent(containerGO.transform, false);
  inputGO.AddComponent<RectTransform>();
     inputGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var inputField = inputGO.AddComponent<TMP_InputField>();
    var textAreaGO = new GameObject("Text");
            textAreaGO.transform.SetParent(inputGO.transform, false);
   var textAreaRect = textAreaGO.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
     textAreaRect.offsetMin = new Vector2(5, 2);
   textAreaRect.offsetMax = new Vector2(-5, -2);

            var textArea = textAreaGO.AddComponent<TextMeshProUGUI>();
            textArea.fontSize = 14;
      textArea.color = WarmTextColor; // Warm gold input text
            inputField.textComponent = textArea;
     inputField.textViewport = textAreaRect;
        }

        private static void CreateApplyButton(Transform parent)
        {
 var buttonGO = new GameObject("ApplyButton");
            buttonGO.transform.SetParent(parent, false);
            buttonGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 40);

  var buttonImage = buttonGO.AddComponent<Image>();
          buttonImage.color = new Color(0.2f, 0.4f, 0.2f, 1f);
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
     text.text = "Apply Changes";
            text.fontSize = 16;
      text.color = WarmTextColor; // Warm gold button text
      text.alignment = TextAlignmentOptions.Center;
     text.raycastTarget = false;
  }

        private static GameObject CreatePagination(Transform parent)
        {
            var paginationGO = new GameObject("PageControls");
   paginationGO.transform.SetParent(parent, false);

     var paginationRect = paginationGO.AddComponent<RectTransform>();
            paginationRect.anchorMin = new Vector2(0, 0);
       paginationRect.anchorMax = new Vector2(1, 0.12f);
       paginationRect.offsetMin = new Vector2(10, 10);
   paginationRect.offsetMax = new Vector2(-10, -5);

     var layout = paginationGO.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
   layout.childControlHeight = true;
       layout.childControlWidth = false;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            CreatePaginationButton(paginationGO.transform, "PrevButton", "<");

            var pageTextGO = new GameObject("PageText");
     pageTextGO.transform.SetParent(paginationGO.transform, false);
  pageTextGO.AddComponent<RectTransform>().sizeDelta = new Vector2(100, 30);
     var pageText = pageTextGO.AddComponent<TextMeshProUGUI>();
 pageText.text = "1 / 1";
   pageText.fontSize = 14;
      pageText.color = WarmTextColor; // Warm gold page text
          pageText.alignment = TextAlignmentOptions.Center;

            CreatePaginationButton(paginationGO.transform, "NextButton", ">");

   return paginationGO;
        }

        private static void CreatePaginationButton(Transform parent, string name, string label)
    {
            var buttonGO = new GameObject(name);
            buttonGO.transform.SetParent(parent, false);
     buttonGO.AddComponent<RectTransform>().sizeDelta = new Vector2(40, 30);

      var buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);
         buttonGO.AddComponent<Button>().targetGraphic = buttonImage;

        var textGO = new GameObject("Text");
   textGO.transform.SetParent(buttonGO.transform, false);
      var textRect = textGO.AddComponent<RectTransform>();
     textRect.anchorMin = Vector2.zero;
    textRect.anchorMax = Vector2.one;
         textRect.offsetMin = Vector2.zero;
    textRect.offsetMax = Vector2.zero;

            var text = textGO.AddComponent<TextMeshProUGUI>();
   text.text = label;
 text.fontSize = 18;
          text.color = WarmTextColor; // Warm gold pagination arrows
    text.alignment = TextAlignmentOptions.Center;
        }

        private static GameObject CreateLoadingIndicator(Transform parent)
        {
     var loadingGO = new GameObject("LoadingIndicator");
    loadingGO.transform.SetParent(parent, false);

     var loadingRect = loadingGO.AddComponent<RectTransform>();
    loadingRect.anchorMin = new Vector2(0.3f, 0.4f);
   loadingRect.anchorMax = new Vector2(0.7f, 0.6f);
            loadingRect.offsetMin = Vector2.zero;
            loadingRect.offsetMax = new Vector2(0, 0);

            loadingGO.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

   var textGO = new GameObject("Text");
 textGO.transform.SetParent(loadingGO.transform, false);
    var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
      textRect.anchorMax = Vector2.one;
    textRect.offsetMin = Vector2.zero;
textRect.offsetMax = Vector2.zero;

 var text = textGO.AddComponent<TextMeshProUGUI>();
   text.text = "Loading...";
            text.fontSize = 18;
    text.color = WarmTextColor; // Warm gold loading text
         text.alignment = TextAlignmentOptions.Center;

         loadingGO.SetActive(false);
return loadingGO;
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
