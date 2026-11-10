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
         global::FiresRPGmaker.FiresLogger.LogDebug($"[UIPrefabFactory] Found {allTMP.Length} TMP_Text components in hierarchy");

            foreach (var tmp in allTMP)
      {
           if (font != null && tmp.font == null)
      {
             tmp.font = font;
     fontApplied++;
         }
    }

    global::FiresRPGmaker.FiresLogger.LogDebug($"[UIPrefabFactory] FixAllTextComponents: applied font to {fontApplied} components");

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
      global::FiresRPGmaker.FiresLogger.LogDebug($"[UIPrefabFactory] Disabling rogue text object: '{rogueObj.name}'");
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

  global::FiresRPGmaker.FiresLogger.LogDebug($"[UIPrefabFactory] FixButtons: checked {buttons.Length} buttons, fixed {fixedCount}");
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

     global::FiresRPGmaker.FiresLogger.LogDebug($"[UIPrefabFactory] Ensured DetailPanel visibility - children: {detailPanel.transform.childCount}");
     }

 #endregion
    }
}
