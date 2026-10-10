using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Makes a HUD panel repositionable by dragging — but only while the vanilla pause menu is open
    /// (reposition mode), exactly like Core's GroupHud. During normal play the drag graphic's raycast is
    /// off, so the panel never snags a click. On drop it invokes OnMoved so the owner can persist the new
    /// position to config. Attach to the grab graphic (panel background / chip); set Target to the RT to move.
    /// </summary>
    internal sealed class DraggableHud : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;
        public Graphic DragGraphic;      // raycast enabled only in reposition mode
        public Action<Vector2> OnMoved;  // persist callback (anchoredPosition)

        private Vector2 _startAnchored;
        private Vector2 _startPointer;

        private void Awake()
        {
            if (Target == null) Target = transform as RectTransform;
            if (DragGraphic == null) DragGraphic = GetComponent<Graphic>();
        }

        private static bool RepositionMode()
        {
            try { return Menu.IsVisible(); } catch { return false; }
        }

        private void Update()
        {
            // Only grab-able with the pause menu open — off during play so clicks/aim never snag the panel.
            bool repo = RepositionMode();
            if (DragGraphic != null && DragGraphic.raycastTarget != repo) DragGraphic.raycastTarget = repo;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Target == null || !RepositionMode()) return;
            _startAnchored = Target.anchoredPosition;
            _startPointer = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (Target == null || !RepositionMode()) return;
            float scale = Target.lossyScale.x > 0.0001f ? Target.lossyScale.x : 1f;
            Target.anchoredPosition = _startAnchored + (e.position - _startPointer) / scale;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Target == null) return;
            try { OnMoved?.Invoke(Target.anchoredPosition); } catch { }
        }
    }
}
