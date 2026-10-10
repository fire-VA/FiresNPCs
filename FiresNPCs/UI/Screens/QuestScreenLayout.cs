using UnityEngine;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Layout configuration for the Quest Interaction Screen.
    /// All anchor values and sizes are defined here for easy tuning.
    /// </summary>
    public static class QuestScreenLayout
    {
        // ===========================================
        // OVERALL PANEL MARGINS (relative to parent)
        // ===========================================
        
        /// <summary>Left margin from edge of background (10%)</summary>
        public const float LeftMargin = 0.10f;
        
        /// <summary>Right margin from edge of background (10%)</summary>
        public const float RightMargin = 0.10f;
        
        /// <summary>Top content area starts at (12% from bottom)</summary>
        public const float ContentBottom = 0.12f;
        
        /// <summary>Top content area ends at (88% from bottom)</summary>
        public const float ContentTop = 0.88f;
        
        /// <summary>Gap between left and right panels (0.5%)</summary>
        public const float CenterGap = 0.005f;

        // ===========================================
        // LEFT PANEL (Quest List)
        // ===========================================
        
        /// <summary>Left panel left anchor</summary>
        public const float LeftPanelLeft = LeftMargin; // 0.10
        
        /// <summary>Left panel right anchor (leaves gap before right panel)</summary>
        public const float LeftPanelRight = 0.445f; // 34.5% width
        
        /// <summary>Left panel bottom anchor</summary>
        public const float LeftPanelBottom = ContentBottom; // 0.12
        
        /// <summary>Left panel top anchor</summary>
        public const float LeftPanelTop = ContentTop; // 0.88
        
        /// <summary>Title offset above the panel (pixels)</summary>
        public const float TitleOffsetY = 20f;

        // ===========================================
        // RIGHT PANEL (QuestInteract container)
        // ===========================================
  
        /// <summary>Right panel left anchor (after gap)</summary>
        public const float RightPanelLeft = 0.45f;
        
        /// <summary>Right panel right anchor</summary>
        public const float RightPanelRight = 1f - RightMargin; // 0.90
        
        /// <summary>Right panel bottom anchor</summary>
        public const float RightPanelBottom = ContentBottom; // 0.12
        
        /// <summary>Right panel top anchor</summary>
        public const float RightPanelTop = ContentTop; // 0.88

        // ===========================================
        // RIGHT PANEL INTERNAL LAYOUT
        // Within QuestInteract (0.0 to 1.0 local space)
        // These are the WORKING values from the old code:
        //   - PNG: top 45% (0.56 to 0.98)
        //   - Label: thin strip (0.48 to 0.55)
        //   - Description: bottom 45% (0.02 to 0.47)
        // ===========================================
        
        /// <summary>Small padding from edges</summary>
        public const float InnerPadding = 0.02f;
     
    /// <summary>Description box bottom (with padding)</summary>
        public const float DescriptionBottom = 0.02f;
        
        /// <summary>Description box top - matches old code</summary>
        public const float DescriptionTop = 0.47f;
        
   /// <summary>Label bottom - matches old code</summary>
        public const float LabelBottom = 0.48f;
     
  /// <summary>Label top - matches old code</summary>
        public const float LabelTop = 0.55f;
        
    /// <summary>PNG preview bottom - matches old code</summary>
        public const float PngBottom = 0.56f;
        
        /// <summary>PNG preview top (with padding) - matches old code</summary>
        public const float PngTop = 0.98f;

     // ===========================================
      // BUTTONS PANEL (below right panel)
   // ===========================================
        
        /// <summary>Buttons panel left anchor (aligned with right panel)</summary>
        public const float ButtonsPanelLeft = RightPanelLeft; // 0.45
        
        /// <summary>Buttons panel right anchor (aligned with right panel)</summary>
        public const float ButtonsPanelRight = RightPanelRight; // 0.90
        
        /// <summary>Buttons panel bottom anchor</summary>
        public const float ButtonsPanelBottom = 0.02f;
     
      /// <summary>Buttons panel top anchor</summary>
        public const float ButtonsPanelTop = 0.10f;

        // ===========================================
        // HELPER METHODS
        // ===========================================
        
        /// <summary>
        /// Get the anchor values for the left quest list panel.
        /// </summary>
        public static (Vector2 min, Vector2 max) GetLeftPanelAnchors()
        {
  return (
      new Vector2(LeftPanelLeft, LeftPanelBottom),
      new Vector2(LeftPanelRight, LeftPanelTop)
          );
        }
   
    /// <summary>
  /// Get the anchor values for the right details panel (QuestInteract).
        /// </summary>
        public static (Vector2 min, Vector2 max) GetRightPanelAnchors()
        {
  return (
             new Vector2(RightPanelLeft, RightPanelBottom),
          new Vector2(RightPanelRight, RightPanelTop)
  );
        }
        
     /// <summary>
 /// Get the anchor values for the PNG preview (within QuestInteract).
     /// </summary>
        public static (Vector2 min, Vector2 max) GetPngAnchors()
        {
 return (
                new Vector2(InnerPadding, PngBottom),
         new Vector2(1f - InnerPadding, PngTop)
       );
}
        
     /// <summary>
        /// Get the anchor values for the quest name label (within QuestInteract).
        /// </summary>
 public static (Vector2 min, Vector2 max) GetLabelAnchors()
        {
            return (
    new Vector2(InnerPadding, LabelBottom),
         new Vector2(1f - InnerPadding, LabelTop)
         );
        }
        
        /// <summary>
        /// Get the anchor values for the description scroll area (within QuestInteract).
        /// </summary>
        public static (Vector2 min, Vector2 max) GetDescriptionAnchors()
   {
          return (
         new Vector2(InnerPadding, DescriptionBottom),
     new Vector2(1f - InnerPadding, DescriptionTop)
    );
        }
        
        /// <summary>
        /// Get the anchor values for the buttons panel.
        /// </summary>
    public static (Vector2 min, Vector2 max) GetButtonsPanelAnchors()
        {
  return (
      new Vector2(ButtonsPanelLeft, ButtonsPanelBottom),
   new Vector2(ButtonsPanelRight, ButtonsPanelTop)
        );
        }
    
      /// <summary>
   /// Apply anchors to a RectTransform with zero offsets.
        /// </summary>
        public static void ApplyAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
      if (rect == null) return;
            rect.anchorMin = anchorMin;
 rect.anchorMax = anchorMax;
       rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
     }
        
        /// <summary>
 /// Log the current layout for debugging.
        /// </summary>
        public static void LogLayout()
      {
         Debug.Log($"[QuestScreenLayout] Left Panel: ({LeftPanelLeft:F3}, {LeftPanelBottom:F3}) to ({LeftPanelRight:F3}, {LeftPanelTop:F3})");
         Debug.Log($"[QuestScreenLayout] Right Panel: ({RightPanelLeft:F3}, {RightPanelBottom:F3}) to ({RightPanelRight:F3}, {RightPanelTop:F3})");
         Debug.Log($"[QuestScreenLayout] PNG (local): ({InnerPadding:F3}, {PngBottom:F3}) to ({1f-InnerPadding:F3}, {PngTop:F3}) = {(PngTop-PngBottom)*100:F1}% height");
         Debug.Log($"[QuestScreenLayout] Label (local): ({InnerPadding:F3}, {LabelBottom:F3}) to ({1f-InnerPadding:F3}, {LabelTop:F3}) = {(LabelTop-LabelBottom)*100:F1}% height");
         Debug.Log($"[QuestScreenLayout] Desc (local): ({InnerPadding:F3}, {DescriptionBottom:F3}) to ({1f-InnerPadding:F3}, {DescriptionTop:F3}) = {(DescriptionTop-DescriptionBottom)*100:F1}% height");
        }
  }
}
