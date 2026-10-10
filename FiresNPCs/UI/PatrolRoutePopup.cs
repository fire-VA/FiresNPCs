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

        private NpcController _npc;
        private NpcBookController _book;
        private bool _open;

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private RectTransform _content;
        private TextMeshProUGUI _status;
        private string _selectedRoute;

        // Scrollable route list — driven by the raw mouse wheel, because the open InventoryGui swallows Unity's
        // event system (so a ScrollRect drag wouldn't fire); the same reason every click is a manual hit-test.
        private RectTransform _scrollViewport;
        private RectTransform _scrollContent;
        private float _scrollOffset;
        private List<string> _routeNames;

        private struct HotZone { public RectTransform Rect; public RectTransform Clip; public Action Action; public Action RightAction; }
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

            Vector2 mouse = Input.mousePosition;

            // Mouse-wheel scroll of the route list while the cursor is over it.
            if (_scrollContent != null && _scrollViewport != null)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && RectTransformUtility.RectangleContainsScreenPoint(_scrollViewport, mouse, null))
                {
                    _scrollOffset -= wheel * 45f;
                    ClampScroll();
                }
            }

            if (Input.GetMouseButtonDown(0)) { DispatchHit(mouse, rightClick: false); return; }
            if (Input.GetMouseButtonDown(1)) { DispatchHit(mouse, rightClick: true); return; }
        }

        // Manual hit dispatch (the InventoryGui swallows the event system). Skips a hot-zone whose row is
        // scrolled outside its clip viewport so an invisible row can't be clicked.
        private void DispatchHit(Vector2 mouse, bool rightClick)
        {
            foreach (var h in _hot)
            {
                if (h.Rect == null) continue;
                var act = rightClick ? h.RightAction : h.Action;
                if (act == null) continue;
                if (h.Clip != null && !RectTransformUtility.RectangleContainsScreenPoint(h.Clip, mouse, null)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(h.Rect, mouse, null)) { Fire(act); return; }
            }
        }

        // One run per frame: the manual hit (the mouse while the InventoryGui swallows events) and the button's onClick
        // (a click the EventSystem delivers) can both see the same click.
        private int _hitFrame = -1;
        private void Fire(Action act)
        {
            if (act == null || _hitFrame == Time.frameCount) return;
            _hitFrame = Time.frameCount;
            act.Invoke();
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
            prect.sizeDelta = new Vector2(460f, 440f);
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
                string presetLabel = string.IsNullOrEmpty(_npc.patrolSpeedPreset) ? "Default (flat walk)" : _npc.patrolSpeedPreset;
                SetStatus($"Current route: {_npc.patrolRouteName}  ·  speed: {presetLabel}");
                bool showing = PatrolRouteVisualizer.IsShowing(_npc.patrolRouteName);
                AddButton(showing ? "Hide Route Lines" : "Visualize Route",
                    new Vector2(0.12f, 0.68f), new Vector2(0.88f, 0.80f), OnToggleVisualize);
                AddButton("Speed Presets…", new Vector2(0.12f, 0.54f), new Vector2(0.88f, 0.66f), OnEditSpeeds);
                AddButton("Change Route / Preset", new Vector2(0.12f, 0.40f), new Vector2(0.88f, 0.52f), RebuildPicker);
                AddButton("Clear Route", new Vector2(0.12f, 0.26f), new Vector2(0.88f, 0.38f), OnClear);
            }
            else
            {
                SetStatus("No patrol route set for this NPC.");
                AddButton("Set New Route", new Vector2(0.12f, 0.62f), new Vector2(0.88f, 0.78f), OnSetNew);
                AddButton("Use Existing", new Vector2(0.12f, 0.42f), new Vector2(0.88f, 0.58f), RebuildPicker);
            }

            AddButton("Cancel", new Vector2(0.30f, 0.12f), new Vector2(0.70f, 0.26f), Close);
            FiresCore.UI.FiresPopupTheme.Reskin(_panelGo);
        }

        private void RebuildPicker() => BuildPicker(resetSelection: true);

        private void BuildPicker(bool resetSelection)
        {
            ClearContent();
            if (resetSelection) _selectedRoute = null;

            _routeNames = PatrolRouteManager.GetAllRouteNames()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            if (_routeNames.Count == 0)
            {
                SetStatus("No saved routes yet — use Set New Route.");
                AddButton("Back", new Vector2(0.30f, 0.16f), new Vector2(0.70f, 0.32f), RebuildEntry);
                return;
            }

            SetStatus($"Choose a route ({_routeNames.Count}) — left-click to pick, right-click to delete:");

            // Scrollable viewport over ALL routes — mouse-wheel scroll; rows are clipped to the viewport.
            var vpGo = new GameObject("RouteViewport", typeof(RectTransform), typeof(RectMask2D));
            vpGo.transform.SetParent(_content, false);
            _scrollViewport = (RectTransform)vpGo.transform;
            _scrollViewport.anchorMin = new Vector2(0.06f, 0.18f);
            _scrollViewport.anchorMax = new Vector2(0.94f, 0.97f);
            _scrollViewport.offsetMin = _scrollViewport.offsetMax = Vector2.zero;

            var listGo = new GameObject("RouteList", typeof(RectTransform));
            listGo.transform.SetParent(_scrollViewport, false);
            _scrollContent = (RectTransform)listGo.transform;
            _scrollContent.anchorMin = new Vector2(0f, 1f);
            _scrollContent.anchorMax = new Vector2(1f, 1f);
            _scrollContent.pivot = new Vector2(0.5f, 1f);

            const float rowPx = 46f, gapPx = 6f;
            _scrollContent.sizeDelta = new Vector2(0f, _routeNames.Count * rowPx + Mathf.Max(0, _routeNames.Count - 1) * gapPx);

            for (int i = 0; i < _routeNames.Count; i++)
            {
                string routeName = _routeNames[i];
                bool selected = routeName == _selectedRoute;
                var btn = UIBuilderHelper.CreateParchmentButton(_scrollContent,
                    selected ? "» " + routeName : routeName, Vector2.zero, Vector2.one, null);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                float top = -i * (rowPx + gapPx);
                rt.offsetMin = new Vector2(0f, top - rowPx);
                rt.offsetMax = new Vector2(0f, top);
                if (selected) btn.GetComponent<Image>().color = UIFontConfig.Colors.ParchmentButtonHover;
                btn.onClick.AddListener(() => Fire(() => OnSelectRoute(routeName)));
                _hot.Add(new HotZone
                {
                    Rect = rt,
                    Clip = _scrollViewport,
                    Action = () => OnSelectRoute(routeName),
                    RightAction = () => OnRequestDelete(routeName),
                });
            }

            ClampScroll();

            AddButton("Apply", new Vector2(0.10f, 0.02f), new Vector2(0.48f, 0.15f), OnApplySelected);
            AddButton("Back", new Vector2(0.52f, 0.02f), new Vector2(0.90f, 0.15f), RebuildEntry);
            FiresCore.UI.FiresPopupTheme.Reskin(_panelGo);
        }

        // Right-click a route → confirm before deleting its cfg from disk.
        private void OnRequestDelete(string routeName)
        {
            ClearContent();
            SetStatus($"Delete route '{routeName}'? This removes its saved cfg and can't be undone.");
            AddButton("Delete", new Vector2(0.14f, 0.46f), new Vector2(0.86f, 0.62f), () => OnConfirmDelete(routeName));
            AddButton("Cancel", new Vector2(0.30f, 0.18f), new Vector2(0.70f, 0.34f), () => BuildPicker(resetSelection: false));
            FiresCore.UI.FiresPopupTheme.Reskin(_panelGo);
        }

        private void OnConfirmDelete(string routeName)
        {
            bool ok = PatrolRouteManager.DeleteRoute(routeName);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                ok ? $"Deleted patrol route '{routeName}'" : $"Couldn't delete '{routeName}'");
            if (_selectedRoute == routeName) _selectedRoute = null;
            _scrollOffset = 0f;
            BuildPicker(resetSelection: false);
        }

        private void ClampScroll()
        {
            if (_scrollContent == null || _scrollViewport == null) { _scrollOffset = 0f; return; }
            float max = Mathf.Max(0f, _scrollContent.rect.height - _scrollViewport.rect.height);
            _scrollOffset = Mathf.Clamp(_scrollOffset, 0f, max);
            _scrollContent.anchoredPosition = new Vector2(0f, _scrollOffset);
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
            BuildPresetPick();
        }

        // Choose which speed preset to run this route with (Default = flat base walk, today's behaviour).
        private void BuildPresetPick()
        {
            ClearContent();
            var route = PatrolRouteManager.GetRoute(_selectedRoute);
            var presets = route != null ? route.Presets.Select(p => p.Name).ToList() : new List<string>();
            SetStatus($"Speed for '{_selectedRoute}':");

            AddButton("Default (flat walk)", new Vector2(0.10f, 0.80f), new Vector2(0.90f, 0.90f), () => AssignWithPreset(""));
            float y = 0.68f;
            foreach (var name in presets)
            {
                string p = name;
                AddButton(p, new Vector2(0.10f, y - 0.10f), new Vector2(0.90f, y), () => AssignWithPreset(p));
                y -= 0.12f;
                if (y < 0.30f) break;
            }
            AddButton("Edit Speed Presets…", new Vector2(0.10f, 0.16f), new Vector2(0.90f, 0.26f), () => PatrolPresetPopup.Open(_selectedRoute));
            AddButton("Back", new Vector2(0.30f, 0.03f), new Vector2(0.70f, 0.14f), () => BuildPicker(resetSelection: false));
            FiresCore.UI.FiresPopupTheme.Reskin(_panelGo);
        }

        private void AssignWithPreset(string preset)
        {
            PatrolRouteSync.AssignExisting(_selectedRoute, preset, _npc);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                $"Assigned '{_selectedRoute}'" + (string.IsNullOrEmpty(preset) ? " (Default speed)" : $" · preset '{preset}'"));
            Close();
        }

        private void OnEditSpeeds()
        {
            if (_npc == null || string.IsNullOrEmpty(_npc.patrolRouteName)) return;
            PatrolPresetPopup.Open(_npc.patrolRouteName);
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

        // Draws/hides the assigned route in the world (golden line + node markers). Persists after the popup
        // closes so you can walk the route to inspect it; toggle off here or it auto-clears on logout.
        private void OnToggleVisualize()
        {
            if (_npc == null || string.IsNullOrEmpty(_npc.patrolRouteName)) return;
            PatrolRouteVisualizer.Toggle(_npc.patrolRouteName);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                PatrolRouteVisualizer.IsShowing(_npc.patrolRouteName)
                    ? $"Showing route '{_npc.patrolRouteName}' — close this and walk it"
                    : "Hid route lines");
            RebuildEntry();   // refresh the toggle label
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
            btn.onClick.AddListener(() => Fire(action));   // a click the EventSystem delivers (gamepad, a test); deduped with DispatchHit
            return btn;
        }

        private void ClearContent()
        {
            _hot.Clear();
            _scrollViewport = null;
            _scrollContent = null;
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }
    }
}
