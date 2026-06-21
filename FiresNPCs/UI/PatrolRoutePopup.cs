using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// The patrol-route configuration popup, opened from the dressing room's "Set Patrol Route" button.
    /// It lives over the dressing room while the InventoryGui is open, which swallows Unity's UI event
    /// system, so EVERY click is dispatched by a manual RectangleContainsScreenPoint hit-test in Update()
    /// (the same proven pattern the book uses for its tabs) rather than button onClick.
    ///
    /// Flow:
    ///   - Entry, no route:   [Set New] [Use Existing] [Cancel]
    ///   - Entry, has route:  shows the current route + [Change Route] [Clear Route] [Cancel]
    ///   - Use Existing/Change: a list of saved routes; pick one, then [Apply] assigns it; [Back] returns.
    ///   - Set New: closes the book and starts the walk-and-record flow (PatrolRecorder).
    /// </summary>
    public class PatrolRoutePopup : MonoBehaviour
    {
        public static PatrolRoutePopup Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;

        private const int MaxVisibleRoutes = 5;

        private NpcController _npc;
        private NpcBookController _book;
        private bool _open;

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private RectTransform _content;
        private TextMeshProUGUI _status;
        private string _selectedRoute;

        private struct HotZone { public RectTransform Rect; public Action Action; }
        private readonly List<HotZone> _hot = new List<HotZone>();

        public static void Open(NpcController npc, NpcBookController book)
        {
            if (npc == null) return;
            if (Instance != null) Instance.Close();

            var go = new GameObject("FiresPatrolRoutePopup");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PatrolRoutePopup>();
            Instance._npc = npc;
            Instance._book = book;
            Instance.Build();
            Instance.RebuildEntry();
        }

        // ── lifecycle ───────────────────────────────────────────────────────
        private void Update()
        {
            if (!_open) return;

            // If the book closed underneath us (e.g. Escape), tear down too.
            if (_book != null && !_book.IsOpen) { Close(); return; }

            // Valheim relocks/hides the cursor every frame while a panel is up — keep it usable.
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mouse = Input.mousePosition;
                foreach (var h in _hot)
                {
                    if (h.Rect != null && RectTransformUtility.RectangleContainsScreenPoint(h.Rect, mouse, null))
                    {
                        h.Action?.Invoke();
                        return;
                    }
                }
            }
        }

        private void Close()
        {
            _open = false;
            if (_canvasGo != null) Destroy(_canvasGo);
            _canvasGo = null;
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        // ── build ───────────────────────────────────────────────────────────
        private void Build()
        {
            _canvasGo = new GameObject("FiresPatrolRoutePopupCanvas");
            DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000; // above the book (210)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            _panelGo = new GameObject("Panel");
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var prect = _panelGo.AddComponent<RectTransform>();
            prect.anchorMin = prect.anchorMax = prect.pivot = new Vector2(0.5f, 0.5f);
            prect.sizeDelta = new Vector2(440f, 340f);
            prect.anchoredPosition = Vector2.zero;
            var pbg = _panelGo.AddComponent<Image>();
            pbg.color = UIFontConfig.Colors.ParchmentPanel;
            pbg.raycastTarget = true;
            var pol = _panelGo.AddComponent<Outline>();
            pol.effectColor = UIFontConfig.Colors.ParchmentOutline;
            pol.effectDistance = new Vector2(2f, -2f);

            // Header bar + title.
            var header = UIBuilderHelper.CreateImage(_panelGo.transform, "Header",
                new Vector2(0f, 0.88f), new Vector2(1f, 1f), UIFontConfig.Colors.ParchmentHeader);
            header.raycastTarget = false;
            UIBuilderHelper.CreateLabel(_panelGo.transform, "Title", "Patrol Route", 17,
                UIFontConfig.Colors.ParchmentButtonInk, new Vector2(0.04f, 0.88f), new Vector2(0.96f, 1f),
                TextAlignmentOptions.Center);

            _status = UIBuilderHelper.CreateLabel(_panelGo.transform, "Status", "", 13,
                UIFontConfig.Colors.ParchmentLabel, new Vector2(0.06f, 0.77f), new Vector2(0.94f, 0.87f),
                TextAlignmentOptions.Center);

            // Content holder (cleared/rebuilt per state).
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(_panelGo.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(1f, 0.76f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;

            _open = true;
        }

        // ── states ──────────────────────────────────────────────────────────
        private void RebuildEntry()
        {
            ClearContent();
            bool hasRoute = !string.IsNullOrEmpty(_npc.patrolRouteName);

            if (hasRoute)
            {
                SetStatus($"Current route: {_npc.patrolRouteName}");
                AddButton("Change Route", new Vector2(0.12f, 0.66f), new Vector2(0.88f, 0.82f), RebuildPicker);
                AddButton("Clear Route", new Vector2(0.12f, 0.46f), new Vector2(0.88f, 0.62f), OnClear);
            }
            else
            {
                SetStatus("No patrol route set for this NPC.");
                AddButton("Set New Route", new Vector2(0.12f, 0.66f), new Vector2(0.88f, 0.82f), OnSetNew);
                AddButton("Use Existing", new Vector2(0.12f, 0.46f), new Vector2(0.88f, 0.62f), RebuildPicker);
            }

            AddButton("Cancel", new Vector2(0.30f, 0.16f), new Vector2(0.70f, 0.32f), Close);
        }

        private void RebuildPicker() => BuildPicker(resetSelection: true);

        private void BuildPicker(bool resetSelection)
        {
            ClearContent();
            if (resetSelection) _selectedRoute = null;

            var names = PatrolRouteManager.GetAllRouteNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            if (names.Count == 0)
            {
                SetStatus("No saved routes yet — use Set New Route.");
                AddButton("Back", new Vector2(0.30f, 0.16f), new Vector2(0.70f, 0.32f), RebuildEntry);
                return;
            }

            int shown = Mathf.Min(names.Count, MaxVisibleRoutes);
            SetStatus(names.Count > shown
                ? $"Choose a route (showing first {shown} of {names.Count}):"
                : "Choose a route:");
            if (names.Count > shown)
                Debug.Log($"[PatrolRoutePopup] {names.Count} routes saved; list capped at {shown}.");

            const float rowTop = 0.86f, rowH = 0.115f, gap = 0.01f;
            for (int i = 0; i < shown; i++)
            {
                string routeName = names[i];
                float top = rowTop - i * (rowH + gap);
                bool selected = routeName == _selectedRoute;
                var btn = AddButton(selected ? "» " + routeName : routeName,
                    new Vector2(0.08f, top - rowH), new Vector2(0.92f, top), () => OnSelectRoute(routeName));
                if (selected) btn.GetComponent<Image>().color = UIFontConfig.Colors.ParchmentButtonHover;
            }

            AddButton("Apply", new Vector2(0.10f, 0.02f), new Vector2(0.48f, 0.16f), OnApplySelected);
            AddButton("Back", new Vector2(0.52f, 0.02f), new Vector2(0.90f, 0.16f), RebuildEntry);
        }

        // ── actions ─────────────────────────────────────────────────────────
        private void OnSelectRoute(string routeName)
        {
            _selectedRoute = routeName;
            BuildPicker(resetSelection: false);
        }

        private void OnApplySelected()
        {
            if (string.IsNullOrEmpty(_selectedRoute))
            {
                SetStatus("Select a route first.");
                return;
            }
            PatrolRouteSync.AssignExisting(_selectedRoute, _npc);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                $"Assigned patrol route '{_selectedRoute}'");
            Close();
        }

        private void OnSetNew()
        {
            var npc = _npc;
            var book = _book;
            Close();
            book?.Hide();
            PatrolRecorder.Begin(npc);
        }

        private void OnClear()
        {
            PatrolRouteSync.ClearAssignment(_npc);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Cleared patrol route");
            Close();
        }

        // ── helpers ─────────────────────────────────────────────────────────
        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        private Button AddButton(string label, Vector2 min, Vector2 max, Action action)
        {
            var btn = UIBuilderHelper.CreateParchmentButton(_content, label, min, max, null);
            _hot.Add(new HotZone { Rect = btn.GetComponent<RectTransform>(), Action = action });
            return btn;
        }

        private void ClearContent()
        {
            _hot.Clear();
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }
    }
}
