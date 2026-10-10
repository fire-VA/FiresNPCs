using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI.Screens.Patrol
{
    /// <summary>
    /// Baked va_background4 book editor for a patrol route — replaces the clunky node context menu + the old
    /// code-built PatrolPresetPopup. Opened by <see cref="PatrolEditorDriver"/> on Shift+RMB of a route node; the
    /// left page edits the selected node (stop toggle + pause-seconds field + node/route actions), the right page
    /// edits the active speed preset and the section covering the selected node (Peak/Floor/Shape sliders, run
    /// threshold). Uses real Unity controls (a freed cursor via ModUiRegistry lets the EventSystem drive them) with
    /// a working-copy preset so slider drags preview live and persist on release.
    /// </summary>
    public static class PatrolEditorPanel
    {
        private static GameObject _canvas, _panel;
        private static bool _built;

        private static string _route;
        private static int _node;
        private static SpeedPreset _work;              // working copy of the selected preset (live edit, save on commit)
        private static int _pendingStart = -1;         // section-mark start node (-1 = none)

        private static TMP_Text _title, _routeInfo, _nodeLabel, _sectionInfo, _runVal, _peakVal, _floorVal, _stopBtnLabel, _mustHitBtnLabel;
        private static TMP_Text _radiusVal, _lookVal, _smoothVal;
        private static TMP_InputField _stopField;
        private static TMP_Dropdown _presetDD, _shapeDD;
        private static Slider _run, _peak, _floor, _radius, _look, _smooth;
        private static Image _mustHitImg;
        private static GameObject _newBtn, _delBtn, _markBtn;

        private static readonly Color MustHitOn = new Color(0.20f, 0.52f, 0.66f, 1f);
        private static readonly Color MustHitOff = new Color(0.50f, 0.34f, 0.12f, 0.92f);

        private static readonly string[] _shapeNames = { "Flat", "EaseInOut", "Arch", "Ramp" };

        public static bool IsOpen => _built && _canvas != null && _canvas.activeSelf;

        /// <summary>Open (or re-focus) the editor on a route node. Called from the Shift+RMB driver.</summary>
        public static void Open(string route, int node)
        {
            if (string.IsNullOrEmpty(route) || PatrolRouteManager.GetRoute(route) == null) return;
            EnsureBuilt();
            if (_canvas == null) return;

            // A pending section-mark: this click is the section END.
            if (_pendingStart >= 0 && string.Equals(route, _route, StringComparison.OrdinalIgnoreCase) && node >= 0 && node != _pendingStart)
            {
                CreateSection(_pendingStart, node);
                _pendingStart = -1;
            }

            _route = route;
            if (node >= 0) _node = node;
            PatrolEditSession.RouteName = route;
            LoadWork();
            if (!PatrolRouteVisualizer.IsShowing(route)) PatrolRouteVisualizer.Show(route);
            _canvas.SetActive(true);
            // Free the cursor + suppress player input while open (opened from the world, where there's no
            // InventoryGui to do it) so the EventSystem drives our buttons/sliders/dropdowns. Mirrors the popups.
            FiresCore.Bridge.ModUiRegistry.Register("FiresPatrolEditorPanel", () => IsOpen, Close, () => _canvas);
            RefreshAll();
        }

        public static void Close()
        {
            _pendingStart = -1;
            if (_canvas != null) _canvas.SetActive(false);
            FiresCore.Bridge.ModUiRegistry.Unregister("FiresPatrolEditorPanel");
        }

        // ── build / bind ──
        private static void EnsureBuilt()
        {
            if (_built) return;
            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) return;
            GameObject prefab = null;
            foreach (var an in bundle.GetAllAssetNames())
                if (an.EndsWith("/firespatroleditorpanel.prefab", StringComparison.OrdinalIgnoreCase)) { prefab = bundle.LoadAsset<GameObject>(an); break; }
            if (prefab == null) { Debug.LogError("[PatrolEditor] FiresPatrolEditorPanel.prefab not in bundle — rebuild it"); return; }

            _canvas = new GameObject("FiresPatrolEditorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 6200;
            var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;

            _panel = UnityEngine.Object.Instantiate(prefab, _canvas.transform);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;

            Bind(_panel.transform);
            _canvas.AddComponent<CursorKeeper>();   // world-opened UI: frees the cursor + hides the crosshair while shown
            _built = true;
        }

        // Full clickable-modal recipe (Tools/FIRES_CLICKABLE_UI_RECIPE.md) — ModUiRegistry alone doesn't free the
        // cursor for a WORLD-opened panel (no map/inventory to do it). This engages InputBlock, forces the cursor in
        // OnGUI (wins the per-frame relock race), and suppresses/hides the vanilla crosshair. Tied to the canvas's
        // active state: OnEnable when the panel shows, OnDisable when it hides.
        private sealed class CursorKeeper : MonoBehaviour
        {
            private readonly List<Graphic> _crosshair = new List<Graphic>();
            private GameObject _crosshairGo;

            private void OnEnable()
            {
                try { FiresCore.UI.InputBlock.Block(true); } catch { }
                try { FiresCore.UI.CrosshairRaycast.Suppress(_crosshair); } catch { }
                SetCrosshairHidden(true);
            }

            private void OnDisable()
            {
                try { FiresCore.UI.InputBlock.Block(false); } catch { }
                try { FiresCore.UI.CrosshairRaycast.Restore(_crosshair); } catch { }
                SetCrosshairHidden(false);
            }

            private void OnGUI()
            {
                if (!IsOpen) return;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            private void Update()
            {
                if (!IsOpen) return;
                try { FiresCore.UI.CrosshairRaycast.Reassert(_crosshair); } catch { }
                SetCrosshairHidden(true);
            }

            private void SetCrosshairHidden(bool hidden)
            {
                if (_crosshairGo == null && global::Hud.instance != null) _crosshairGo = FindChild(global::Hud.instance.transform, "crosshair");
                if (_crosshairGo != null && _crosshairGo.activeSelf == hidden) _crosshairGo.SetActive(!hidden);
            }

            private static GameObject FindChild(Transform root, string name)
            {
                if (root == null) return null;
                if (root.name == name) return root.gameObject;
                for (int i = 0; i < root.childCount; i++) { var r = FindChild(root.GetChild(i), name); if (r != null) return r; }
                return null;
            }
        }

        private static void Bind(Transform t)
        {
            _title      = Tmp(t, "TitleText");
            _routeInfo  = Tmp(t, "RouteInfo");
            _nodeLabel  = Tmp(t, "NodeLabel");
            _sectionInfo = Tmp(t, "SectionInfo");
            _stopField  = Input(t, "StopDurationField");
            _presetDD   = Dd(t, "PresetDD");
            _shapeDD    = Dd(t, "ShapeDD");
            _run   = Sl(t, "RunSlider");   _runVal   = Tmp(t, "RunSliderValue");
            _peak  = Sl(t, "PeakSlider");  _peakVal  = Tmp(t, "PeakSliderValue");
            _floor = Sl(t, "FloorSlider"); _floorVal = Tmp(t, "FloorSliderValue");
            _radius = Sl(t, "CheckpointRadiusSlider"); _radiusVal = Tmp(t, "CheckpointRadiusSliderValue");
            _look   = Sl(t, "LookAheadSlider");        _lookVal   = Tmp(t, "LookAheadSliderValue");
            _smooth = Sl(t, "SmoothingSlider");        _smoothVal = Tmp(t, "SmoothingSliderValue");
            _newBtn = Find(t, "NewPresetButton"); _delBtn = Find(t, "DelPresetButton"); _markBtn = Find(t, "MarkSectionButton");
            _stopBtnLabel = Tmp(Find(t, "StopToggleButton")?.transform, "Label");
            _mustHitBtnLabel = Tmp(Find(t, "MustHitButton")?.transform, "Label");
            _mustHitImg = Find(t, "MustHitButton")?.GetComponent<Image>();

            // draggable by the header strip
            var header = Find(t, "Header");
            if (header != null)
            {
                var drag = header.GetComponent<global::FiresRPGmaker.UI.Screens.PopupDragHandler>() ?? header.AddComponent<global::FiresRPGmaker.UI.Screens.PopupDragHandler>();
                drag.target = _panel.GetComponent<RectTransform>();
            }

            Click(t, "CloseButton", Close);
            Click(t, "StopToggleButton", ToggleStop);
            Click(t, "MustHitButton", ToggleMustHit);
            Click(t, "GrabButton", () => { var r = _route; var n = _node; Close(); global::FiresRPGmaker.UI.ContextMenu.PatrolNodeGrabController.Begin(r, n, mouse: true); });
            Click(t, "InsertButton", () => EditNodes(r => InsertAfter(r, _node)));
            Click(t, "DeleteButton", () => EditNodes(r => DeleteAt(r, _node)));
            Click(t, "HideButton", () => { PatrolRouteVisualizer.Hide(); Close(); });
            Click(t, "SaveButton", SaveRoute);
            Click(t, "NewPresetButton", NewPreset);
            Click(t, "DelPresetButton", DelPreset);
            Click(t, "ColorsButton", ToggleColors);
            Click(t, "MarkSectionButton", MarkSection);

            if (_stopField != null) _stopField.onEndEdit.AddListener(OnStopDuration);
            if (_presetDD != null) _presetDD.onValueChanged.AddListener(OnPresetChanged);
            if (_shapeDD != null) { _shapeDD.ClearOptions(); _shapeDD.AddOptions(_shapeNames.ToList()); _shapeDD.onValueChanged.AddListener(OnShapeChanged); }
            WireSlider(_run,   v => { if (_work != null) _work.RunThreshold = v; if (_runVal != null) _runVal.text = v.ToString("0.00"); LivePreview(); }, Commit);
            WireSlider(_peak,  v => { var s = CurSection(); if (s != null) s.Peak = v;  if (_peakVal != null) _peakVal.text = v.ToString("0.00"); LivePreview(); }, Commit);
            WireSlider(_floor, v => { var s = CurSection(); if (s != null) s.Floor = v; if (_floorVal != null) _floorVal.text = v.ToString("0.00"); LivePreview(); }, Commit);

            // Route-level path-following knobs — live-edit the cached route (smoothing also re-curves the drawn
            // line), persist on release. These apply even to the Default no-preset walk, so they're always active.
            WireSlider(_radius, v => { SetRoute(r => r.ArrivalRadius = v); if (_radiusVal != null) _radiusVal.text = v.ToString("0.0"); }, CommitRoute);
            WireSlider(_look,   v => { SetRoute(r => r.LookAhead = v);     if (_lookVal   != null) _lookVal.text   = v.ToString("0.0"); }, CommitRoute);
            WireSlider(_smooth, v => { SetRoute(r => r.Smoothing = v);     if (_smoothVal != null) _smoothVal.text = v.ToString("0.00"); PatrolRouteVisualizer.RefreshCurve(v); }, CommitRoute);
        }

        // Mutate the LIVE cached route in place (no disk write) — for slider-drag preview.
        private static void SetRoute(Action<PatrolRoute> apply)
        {
            var r = PatrolRouteManager.GetRoute(_route);
            if (r != null) apply(r);
        }

        // Persist the route's current state (points + presets + follow tuning + must-hit). Called on slider release.
        private static void CommitRoute()
        {
            var r = PatrolRouteManager.GetRoute(_route);
            if (r != null) PatrolRouteManager.SaveRouteFull(r);
        }

        // ── state helpers ──
        private static SpeedPreset LiveSelected()
        {
            var route = PatrolRouteManager.GetRoute(_route);
            return route?.GetPreset(PatrolEditSession.PresetName) ?? route?.Presets.FirstOrDefault();
        }

        private static void LoadWork()
        {
            var sel = LiveSelected();
            if (sel != null) { PatrolEditSession.PresetName = sel.Name; _work = sel.Clone(); }
            else _work = null;
        }

        // The section of the working preset covering the selected node (what the sliders edit).
        private static Section CurSection() => _work?.SectionAt(_node);

        // ── refresh ──
        private static void RefreshAll()
        {
            var route = PatrolRouteManager.GetRoute(_route);
            int count = route != null ? route.Points.Count : 0;
            _node = Mathf.Clamp(_node, 0, Mathf.Max(0, count - 1));
            if (_title != null) _title.text = "Patrol Route Editor";
            if (_routeInfo != null) _routeInfo.text = $"{_route}  ·  {count} nodes  ·  {(route != null && route.IsLoop ? "Loop" : "Open")}";
            RefreshNode();
            RefreshPath();
            RefreshPresetDropdown();
            RefreshSection();
        }

        // Sync the 3 route-level path sliders from the live route (no notify, so it doesn't re-save).
        private static void RefreshPath()
        {
            var route = PatrolRouteManager.GetRoute(_route);
            if (route == null) return;
            if (_radius != null) { _radius.SetValueWithoutNotify(route.ArrivalRadius); if (_radiusVal != null) _radiusVal.text = route.ArrivalRadius.ToString("0.0"); }
            if (_look   != null) { _look.SetValueWithoutNotify(route.LookAhead);        if (_lookVal   != null) _lookVal.text   = route.LookAhead.ToString("0.0"); }
            if (_smooth != null) { _smooth.SetValueWithoutNotify(route.Smoothing);      if (_smoothVal != null) _smoothVal.text = route.Smoothing.ToString("0.00"); }
        }

        private static void RefreshNode()
        {
            var route = PatrolRouteManager.GetRoute(_route);
            if (route == null) return;
            int count = route.Points.Count;
            bool isEnd = !route.IsLoop && (_node == 0 || _node == count - 1);
            if (_nodeLabel != null) _nodeLabel.text = $"Node {_node + 1} of {count}" + (route != null && !route.IsLoop && _node == 0 ? "  (start)" : (isEnd ? "  (end)" : ""));
            var stop = _work?.StopAt(_node);
            if (_stopBtnLabel != null) _stopBtnLabel.text = stop != null ? "Remove Stop Point" : "Add Stop Point";
            if (_stopField != null) _stopField.SetTextWithoutNotify(stop != null ? stop.Duration.ToString("0.#") : "2");

            bool mustHit = route.MustHit.Contains(_node);
            if (_mustHitBtnLabel != null) _mustHitBtnLabel.text = mustHit ? "Never Skip: ON" : "Never Skip: OFF";
            if (_mustHitImg != null) _mustHitImg.color = mustHit ? MustHitOn : MustHitOff;
        }

        private static void RefreshPresetDropdown()
        {
            if (_presetDD == null) return;
            var route = PatrolRouteManager.GetRoute(_route);
            var names = route?.Presets.Select(p => p.Name).ToList() ?? new List<string>();
            if (names.Count == 0) names.Add("(no presets)");
            _presetDD.onValueChanged.RemoveListener(OnPresetChanged);
            _presetDD.ClearOptions(); _presetDD.AddOptions(names);
            int sel = Mathf.Max(0, names.FindIndex(n => string.Equals(n, PatrolEditSession.PresetName, StringComparison.OrdinalIgnoreCase)));
            _presetDD.SetValueWithoutNotify(sel);
            _presetDD.onValueChanged.AddListener(OnPresetChanged);
        }

        private static void RefreshSection()
        {
            var s = CurSection();
            bool has = s != null;
            if (_sectionInfo != null)
                _sectionInfo.text = has ? $"Nodes {s.Low + 1} – {s.High + 1}"
                    : (_pendingStart >= 0 ? $"Marking… start node {_pendingStart + 1}, Shift+RMB the end node"
                                          : "No section on this node — Mark Section");
            SetInteractable(_peak, has); SetInteractable(_floor, has); SetInteractable(_shapeDD, has);
            if (has)
            {
                if (_run != null)   { _run.SetValueWithoutNotify(_work.RunThreshold); if (_runVal != null) _runVal.text = _work.RunThreshold.ToString("0.00"); }
                if (_peak != null)  { _peak.SetValueWithoutNotify(s.Peak);  if (_peakVal != null) _peakVal.text = s.Peak.ToString("0.00"); }
                if (_floor != null) { _floor.SetValueWithoutNotify(s.Floor); if (_floorVal != null) _floorVal.text = s.Floor.ToString("0.00"); }
                if (_shapeDD != null) _shapeDD.SetValueWithoutNotify(Mathf.Clamp((int)s.Shape, 0, _shapeNames.Length - 1));
            }
            else if (_run != null && _work != null) { _run.SetValueWithoutNotify(_work.RunThreshold); if (_runVal != null) _runVal.text = _work.RunThreshold.ToString("0.00"); }
        }

        // ── handlers ──
        private static void ToggleStop()
        {
            PatrolEditSession.Mutate(_route, (r, preset) =>
            {
                var st = preset.StopAt(_node);
                if (st != null) preset.StopPoints.Remove(st);
                else preset.StopPoints.Add(new StopPoint { Index = _node, Duration = ParseStop() });
            });
            LoadWork(); PatrolRouteVisualizer.RefreshSpeedPreview(); RefreshNode(); RefreshSection();
        }

        // Flag/unflag the selected node as must-hit (never skipped — doorways/bridges). Route-level, so it saves
        // via SaveRouteFull; redraw the visual so the node shows its locked cyan marker.
        private static void ToggleMustHit()
        {
            var r = PatrolRouteManager.GetRoute(_route);
            if (r == null) return;
            if (!r.MustHit.Remove(_node)) r.MustHit.Add(_node);
            PatrolRouteManager.SaveRouteFull(r);
            if (PatrolRouteVisualizer.IsShowing(_route)) { PatrolRouteVisualizer.Hide(); PatrolRouteVisualizer.Show(_route); }
            RefreshNode();
        }

        private static void OnStopDuration(string text)
        {
            if (_work?.StopAt(_node) == null) return;
            PatrolEditSession.Mutate(_route, (r, preset) => { var st = preset.StopAt(_node); if (st != null) st.Duration = ParseStop(); });
            LoadWork();
        }

        private static float ParseStop() => _stopField != null && float.TryParse(_stopField.text, out var v) ? Mathf.Clamp(v, 0f, 120f) : 2f;

        private static void SaveRoute()
        {
            var r = PatrolRouteManager.GetRoute(_route);
            if (r != null) { PatrolRouteManager.SaveRouteFull(r); Hud($"Route '{_route}' saved ({r.Points.Count} nodes)"); }
        }

        private static void NewPreset()
        {
            var route = PatrolRouteManager.GetRoute(_route); if (route == null) return;
            string name = PatrolEditSession.UniquePresetName(route, "Preset");
            PatrolEditSession.Mutate(_route, (r, _) => { if (r.GetPreset(name) == null) r.Presets.Add(new SpeedPreset { Name = name }); }, ensurePreset: false);
            PatrolEditSession.PresetName = name; LoadWork(); RefreshAll();
            PatrolRouteVisualizer.SetSpeedPreview(name);
        }

        private static void DelPreset()
        {
            string name = PatrolEditSession.PresetName;
            PatrolEditSession.Mutate(_route, (r, _) => { var p = r.GetPreset(name); if (p != null) r.Presets.Remove(p); }, ensurePreset: false);
            PatrolEditSession.PresetName = PatrolRouteManager.GetRoute(_route)?.Presets.FirstOrDefault()?.Name;
            LoadWork(); PatrolRouteVisualizer.RefreshSpeedPreview(); RefreshAll();
        }

        private static void OnPresetChanged(int i)
        {
            var route = PatrolRouteManager.GetRoute(_route);
            var names = route?.Presets.Select(p => p.Name).ToList();
            if (names != null && i >= 0 && i < names.Count) PatrolEditSession.PresetName = names[i];
            LoadWork(); PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.PresetName); RefreshSection();
        }

        private static void OnShapeChanged(int i)
        {
            var s = CurSection(); if (s == null) return;
            s.Shape = (SpeedShape)Mathf.Clamp(i, 0, _shapeNames.Length - 1);
            Commit(); LivePreview();
        }

        private static void MarkSection()
        {
            if (_pendingStart < 0) { _pendingStart = _node; Hud($"Section start = node {_node + 1}. Shift+RMB the end node."); }
            else { CreateSection(_pendingStart, _node); _pendingStart = -1; }
            RefreshSection();
        }

        private static void CreateSection(int a, int b)
        {
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            PatrolEditSession.Mutate(_route, (r, preset) =>
            {
                preset.Sections.RemoveAll(s => s.Low <= hi && s.High >= lo);   // non-overlapping
                preset.Sections.Add(new Section { StartIndex = lo, EndIndex = hi, Peak = 1.5f, Floor = 0.6f, Shape = SpeedShape.EaseInOut, ShapeK = 1f });
                preset.Sections.Sort((x, y) => x.Low.CompareTo(y.Low));
            });
            _node = lo; LoadWork(); PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.PresetName);
            RefreshNode(); RefreshSection();
            Hud($"Section {lo + 1}–{hi + 1} added — tune Peak / Floor / Shape.");
        }

        private static void ToggleColors()
        {
            if (PatrolRouteVisualizer.IsPreviewingSpeed) PatrolRouteVisualizer.SetSpeedPreview(null);
            else PatrolRouteVisualizer.SetSpeedPreview(PatrolEditSession.PresetName);
        }

        // Writes the working preset back into the route (by name) + saves. Called on slider release + discrete edits.
        private static void Commit()
        {
            if (_work == null) return;
            PatrolEditSession.Mutate(_route, (r, _) =>
            {
                var existing = r.GetPreset(_work.Name);
                if (existing != null) r.Presets[r.Presets.IndexOf(existing)] = _work.Clone();
                else r.Presets.Add(_work.Clone());
            }, ensurePreset: false);
        }

        private static void LivePreview() { if (_work != null) PatrolRouteVisualizer.PreviewSpeedPreset(_work); }

        // ── node structural edits ── work on a full route CLONE (points + presets + follow tuning + must-hit) so
        // an insert/delete fixes up preset section/stop indices AND the must-hit flags together, then SaveRouteFull.
        private static void EditNodes(Action<PatrolRoute> mutate)
        {
            var live = PatrolRouteManager.GetRoute(_route); if (live == null) return;
            var r = live.Clone();
            mutate(r);
            if (r.Points.Count < 2) { Hud("A route needs at least 2 nodes"); return; }
            foreach (var p in r.Presets) p.ClampToRoute(r.Points.Count);
            r.ClampMustHit(r.Points.Count);
            PatrolRouteManager.SaveRouteFull(r);
            if (PatrolRouteVisualizer.IsShowing(_route)) { PatrolRouteVisualizer.Hide(); PatrolRouteVisualizer.Show(_route); }
            _node = Mathf.Clamp(_node, 0, r.Points.Count - 1);
            LoadWork(); RefreshAll();
            Hud($"Route '{_route}' updated ({r.Points.Count} nodes)");
        }

        private static void InsertAfter(PatrolRoute r, int idx)
        {
            if (idx < 0 || idx >= r.Points.Count) return;
            int next = (idx + 1) % r.Points.Count;
            r.Points.Insert(idx + 1, (r.Points[idx] + r.Points[next]) * 0.5f);
            foreach (var p in r.Presets) p.ShiftForInsert(idx + 1);
            r.ShiftMustHitForInsert(idx + 1);
        }

        private static void DeleteAt(PatrolRoute r, int idx)
        {
            if (r.Points.Count <= 2 || idx < 0 || idx >= r.Points.Count) return;
            r.Points.RemoveAt(idx);
            foreach (var p in r.Presets) p.ShiftForDelete(idx);
            r.ShiftMustHitForDelete(idx);
        }

        // ── small ui helpers ──
        private static GameObject Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root.gameObject;
            for (int i = 0; i < root.childCount; i++) { var r = Find(root.GetChild(i), name); if (r != null) return r; }
            return null;
        }
        private static TMP_Text Tmp(Transform root, string name) { var go = Find(root, name); return go != null ? go.GetComponent<TMP_Text>() : null; }
        private static TMP_InputField Input(Transform root, string name) { var go = Find(root, name); return go != null ? go.GetComponentInChildren<TMP_InputField>(true) : null; }
        private static TMP_Dropdown Dd(Transform root, string name) { var go = Find(root, name); return go != null ? go.GetComponent<TMP_Dropdown>() : null; }
        private static Slider Sl(Transform root, string name) { var go = Find(root, name); return go != null ? go.GetComponent<Slider>() : null; }
        private static void Click(Transform root, string name, Action a) { var b = Find(root, name)?.GetComponent<Button>(); if (b != null) b.onClick.AddListener(() => a()); }

        private static void WireSlider(Slider s, Action<float> onChange, Action onRelease)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v => onChange(v));
            var trig = s.gameObject.GetComponent<EventTrigger>() ?? s.gameObject.AddComponent<EventTrigger>();
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => onRelease());
            trig.triggers.Add(up);
        }

        private static void SetInteractable(Selectable s, bool on) { if (s != null) s.interactable = on; }

        private static void Hud(string msg) { try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg); } catch { } }
    }
}
