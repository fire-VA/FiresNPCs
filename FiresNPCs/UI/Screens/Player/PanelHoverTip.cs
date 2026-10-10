using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>Hover tooltip for the parchment panels (Buffer / Transmog, 10-09 mockups): shows the panel's baked
    /// "TooltipBox" (with "TooltipText") just above the hovered control while the pointer is over it. Works on disabled
    /// buttons too (Selectable.interactable doesn't stop pointer events), so "You have 0 Ruby (need 1)" can explain a grey Buy.</summary>
    public class PanelHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Text;
        public RectTransform Box;

        /// <summary>Sets (or clears, with null/empty) the tip on a control.</summary>
        public static void Set(GameObject go, RectTransform box, string text)
        {
            if (go == null) return;
            var tip = go.GetComponent<PanelHoverTip>() ?? go.AddComponent<PanelHoverTip>();
            tip.Box = box; tip.Text = text;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (Box == null || string.IsNullOrEmpty(Text)) return;
            var label = Box.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { label.text = Text; Box.sizeDelta = new Vector2(Mathf.Max(80f, label.preferredWidth + 16f), Box.sizeDelta.y); }
            // Above the control, right-aligned to it, in the panel's space.
            var me = (RectTransform)transform;
            var corners = new Vector3[4];
            me.GetWorldCorners(corners);                       // 0 BL, 1 TL, 2 TR, 3 BR
            Box.position = corners[2];
            Box.anchoredPosition += new Vector2(-Box.sizeDelta.x, Box.sizeDelta.y + 4f);
            Box.SetAsLastSibling();
            Box.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData e) { if (Box != null) Box.gameObject.SetActive(false); }
        private void OnDisable() { if (Box != null) Box.gameObject.SetActive(false); }
    }
}
