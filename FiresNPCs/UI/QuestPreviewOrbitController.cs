using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Attach to the preview RawImage to enable orbit/zoom interactions.
    /// Left-drag orbits, scroll zooms.
    /// </summary>
    public class QuestPreviewOrbitController : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private bool _isDragging;
        private Vector2 _lastDragPos;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _isDragging = true;
            _lastDragPos = eventData.position;
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;
            var renderer = QuestPreviewRenderer.Instance;
            if (renderer == null || !renderer.HasPreview) return;

            Vector2 delta = eventData.position - _lastDragPos;
            _lastDragPos = eventData.position;
            renderer.Orbit(delta.x, delta.y);
            eventData.Use();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            eventData.Use();
        }

        public void OnScroll(PointerEventData eventData)
        {
            var renderer = QuestPreviewRenderer.Instance;
            if (renderer == null || !renderer.HasPreview) return;
            renderer.Zoom(eventData.scrollDelta.y * 2f);
            eventData.Use();
        }
    }
}
