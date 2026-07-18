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
    /// The DBSM speed-preset editor for a route. Opened from the patrol node context menu ("Edit speed
    /// presets…") or the dressing-room patrol popup. Manages named speed presets and, per preset, the
    /// section envelopes (Peak / Floor / Shape / ShapeK sliders + a 2D curve editor for Custom), stop-point
    /// pauses, and the run threshold. Auto-shows the route and colors its nodes by resulting speed so tuning
    /// is visible in-world.
    ///
    /// Like PatrolRoutePopup this drives everything with raw Input polling (the InventoryGui swallows Unity's
    /// event system): clicks via a manual RectangleContainsScreenPoint hit-test, sliders via a mouse-held drag
    /// mapped across the track, and cfg writes DEBOUNCED to slider release (SaveRouteFull reloads the whole
    /// route dict; the drag preview reads an in-memory working copy).
    /// </summary>
    public class PatrolPresetPopup : MonoBehaviour
    {
        public static PatrolPresetPopup Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;

        private bool _open;
        private string _route;
        private SpeedPreset _work;      // working copy of the selected preset (edited live, saved on commit)
        private Section _editSection;   // section being edited on the SectionEditor screen

        private GameObject _canvasGo;
        private RectTransform _content;
        private TextMeshProUGUI _status;

        private struct HotZone { public RectTransform Rect; public Action Action; public Action RightAction; }
        private readonly List<HotZone> _hot = new List<HotZone>();

        // Drag-slider tracks: mapped from mouseX across the slider rect while the left button is held.
        private struct SliderZone { public Slider Slider; public float Min; public float Max; public Action<float> Set; }
        private readonly List<SliderZone> _sliders = new List<SliderZone>();
        private int _dragSlider = -1;

        // 2D curve editor (Custom shape): fixed-u vertical handles whose height sets the speed at that u.
        private RectTransform _curveArea;
        private readonly List<RectTransform> _curveHandles = new List<RectTransform>();
        private int _dragCurve = -1;
        private const float CurveSpeedMin = 0.1f, CurveSpeedMax = 2.5f;

        // RETIRED: the DBSM speed editor is now the baked va_background4 Patrol Route Editor book (proper Unity
        // controls, renderable). Kept as a thin redirect so existing callers (dressing-room patrol popup, etc.)
        // open the new UI. The rest of this file is dead but harmless (never instantiated).
        public static void Open(string routeName)
        {
            if (string.IsNullOrEmpty(routeName)) return;
            global::FiresRPGmaker.UI.Screens.Patrol.PatrolEditorPanel.Open(routeName, 0);
        }

        // ── lifecycle ───────────────────────────────────────────────────────
        private void Update()
        {
            if (!_open) return;

            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;

            Vector2 mouse = Input.mousePosition;

            // Active slider drag.
            if (_dragSlider >= 0)
            {
                if (Input.GetMouseButton(0)) { DriveSlider(_dragSlider, mouse); }
                else { _dragSlider = -1; Commit(); }   // release → debounced save
                return;
            }
            // Active curve-handle drag.
            if (_dragCurve >= 0)
            {
                if (Input.GetMouseButton(0)) { DriveCurve(_dragCurve, mouse); }
                else { _dragCurve = -1; Commit(); }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                // A slider track under the cursor begins a drag.
                for (int i = 0; i < _sliders.Count; i++)
                    if (_sliders[i].Slider != null && Contains(_sliders[i].Slider.GetComponent<RectTransform>(), mouse))
                    { _dragSlider = i; DriveSlider(i, mouse); return; }
                // A curve handle under the cursor begins a drag.
                for (int i = 0; i < _curveHandles.Count; i++)
                    if (_curveHandles[i] != null && Contains(_curveHandles[i], mouse, pad: 10f))
                    { _dragCurve = i; return; }
                DispatchHit(mouse, rightClick: false);
                return;
            }
            if (Input.GetMouseButtonDown(1)) { DispatchHit(mouse, rightClick: true); return; }
        }

        private static bool Contains(RectTransform rt, Vector2 mouse, float pad = 0f)
        {
            if (rt == null) return false;
            if (pad <= 0f) return RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null);
            // Slightly enlarged hit for thin slider tracks / small handles.
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return mouse.x >= corners[0].x - pad && mouse.x <= corners[2].x + pad
                && mouse.y >= corners[0].y - pad && mouse.y <= corners[2].y + pad;
        }

        private void DispatchHit(Vector2 mouse, bool rightClick)
        {
            foreach (var h in _hot)
            {
                if (h.Rect == null) continue;
                var act = rightClick ? h.RightAction : h.Action;
                if (act == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(h.Rect, mouse, null)) { act.Invoke(); return; }
            }
        }

        // Map mouseX across the slider track → value; update the slider visual, the model, and live preview.
        private void DriveSlider(int i, Vector2 mouse)
        {
            var z = _sliders[i];
            if (z.Slider == null) return;
            var rt = z.Slider.GetComponent<RectTransform>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mouse, null, out var local)) return;
            float t = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
            float val = Mathf.Lerp(z.Min, z.Max, t);
            z.Slider.value = val;
            z.Set?.Invoke(val);
            LivePreview();
        }

        private void DriveCurve(int i, Vector2 mouse)
        {
            if (_curveArea == null || _work == null || _editSection == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveArea, mouse, null, out var local)) return;
            float ty = Mathf.Clamp01((local.y - _curveArea.rect.yMin) / Mathf.Max(1f, _curveArea.rect.height));
            float speed = Mathf.Lerp(CurveSpeedMin, CurveSpeedMax, ty);
            var pts = _editSection.CustomCurve;
            if (pts != null && i < pts.Count)
            {
                pts[i] = new Vector2(pts[i].x, speed);
                PositionCurveHandle(i);
                LivePreview();
            }
        }

        private void Close()
        {
            _open = false;
            FiresCore.Bridge.ModUiRegistry.Unregister("FiresPatrolPresetPopup");
            if (_canvasGo != null) Destroy(_canvasGo);
            _canvasGo = null;
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        // ── build shell ───────────────────────────────────────────────────────
        private void Build()
        {
            _canvasGo = new GameObject("FiresPatrolPresetPopupCanvas");
            DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6100; // above the patrol popup (6000)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(_canvasGo.transform, false);
            var prect = panelGo.AddComponent<RectTransform>();
            prect.anchorMin = prect.anchorMax = prect.pivot = new Vector2(0.5f, 0.5f);
            prect.sizeDelta = new Vector2(520f, 560f);
            prect.anchoredPosition = Vector2.zero;
            var pbg = panelGo.AddComponent<Image>();
            pbg.color = UIFontConfig.Colors.ParchmentPanel;
            pbg.raycastTarget = true;
            var pol = panelGo.AddComponent<Outline>();
            pol.effectColor = UIFontConfig.Colors.ParchmentOutline;
            pol.effectDistance = new Vector2(2f, -2f);

            var header = UIBuilderHelper.CreateImage(panelGo.transform, "Header",
                new Vector2(0f, 0.92f), new Vector2(1f, 1f), UIFontConfig.Colors.ParchmentHeader);
            header.raycastTarget = false;
            UIBuilderHelper.CreateLabel(panelGo.transform, "Title", "Patrol Speeds", 17,
                UIFontConfig.Colors.ParchmentButtonInk, new Vector2(0.04f, 0.92f), new Vector2(0.96f, 1f),
                TextAlignmentOptions.Center);

            _status = UIBuilderHelper.CreateLabel(panelGo.transform, "Status", "", 12,
                UIFontConfig.Colors.ParchmentLabel, new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.92f),
                TextAlignmentOptions.Center);

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(panelGo.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(1f, 0.84f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;

            _open = true;
        }

        // ── screen: preset list ─────────────────────────────────────────────
        private void BuildPresetList()
        {
            _work = null; _editSection = null;
            ClearContent();
            var route = PatrolRouteManager.GetRoute(_route);
            if (route == null) { SetStatus("Route not found."); AddButton("Close", A(0.35f, 0.02f), A(0.65f, 0.10f), Close); return; }

            SetStatus($"Route '{_route}' — {route.Presets.Count} preset(s). Assign a preset to an NPC to use it.");

            float y = 0.92f;
            const float h = 0.09f, gap = 0.015f;
            foreach (var p in route.Presets.ToList())
            {
                string name = p.Name;
                bool sel = string.Equals(PatrolEditSession.PresetName, name, StringComparison.OrdinalIgnoreCase);
                AddButtonRC((sel ? "» " : "") + $"{name}  ({p.Sections.Count} sec, {p.StopPoints.Count} stop)",
                    A(0.05f, y - h), A(0.95f, y),
                    () => EditPreset(name),
                    () => DeletePreset(name));
                y -= (h + gap);
                if (y < 0.24f) break; // simple overflow guard (no scroll here)
            }

            AddButton("+ New preset", A(0.06f, 0.13f), A(0.48f, 0.22f), NewPreset);
            AddButton(PatrolRouteVisualizer.IsPreviewingSpeed ? "Hide speed colors" : "Show speed colors",
                A(0.52f, 0.13f), A(0.94f, 0.22f), ToggleColors);
            AddButton("Close", A(0.35f, 0.02f), A(0.65f, 0.11f), Close);
            FiresCore.UI.FiresPopupTheme.Reskin(_content.parent.gameObject);
        }

        private void NewPreset()
        {
            var route = PatrolRouteManager.GetRoute(_route);
            if (route == null) return;
            string name = PatrolEditSession.UniquePresetName(route, "Preset");
            PatrolEditSession.Mutate(_route, (r, _) =>
            {
                if (r.GetPreset(name) == null) r.Presets.Add(new SpeedPreset { Name = name });
            }, ensurePreset: false);
            PatrolEditSession.PresetName = name;
            BuildPresetList();
        }

        private void DeletePreset(string name)
        {
            PatrolEditSession.Mutate(_route, (r, _) =>
            {
                var p = r.GetPreset(name);
                if (p != null) r.Presets.Remove(p);
            }, ensurePreset: false);
            if (string.Equals(PatrolEditSession.PresetName, name, StringComparison.OrdinalIgnoreCase))
                PatrolEditSession.PresetName = PatrolRouteManager.GetRoute(_route)?.Presets.FirstOrDefault()?.Name;
            PatrolRouteVisualizer.RefreshSpeedPreview();
            BuildPresetList();
        }

        // ── screen: preset editor (run threshold + sections + stops) ─────────
        private void EditPreset(string name)
        {
            var route = PatrolRouteManager.GetRoute(_route);
            var live = route?.GetPreset(name);
            if (live == null) { BuildPresetList(); return; }
            _work = live.Clone();
            PatrolEditSession.PresetName = name;
            PatrolRouteVisualizer.SetSpeedPreview(name);
            _editSection = null;
            BuildPresetEditor();
        }

        private void BuildPresetEditor()
        {
            ClearContent();
            if (_work == null) { BuildPresetList(); return; }
            SetStatus($"Preset '{_work.Name}' — drag sliders; changes save on release.");

            AddSlider("Run threshold (×walk)", A(0.06f, 0.86f), A(0.94f, 0.93f), 1.0f, 1.6f, _work.RunThreshold,
                v => _work.RunThreshold = v, () => _work.RunThreshold.ToString("0.00"));

            UIBuilderHelper.CreateLabel(_content, "SecHdr", "Sections (span → speed envelope):", 12,
                UIFontConfig.Colors.ParchmentLabel, A(0.06f, 0.79f), A(0.94f, 0.84f), TextAlignmentOptions.Left);

            float y = 0.78f;
            const float h = 0.075f, gap = 0.012f;
            var route = PatrolRouteManager.GetRoute(_route);
            int nodeCount = route != null ? route.Points.Count : 0;
            foreach (var s in _work.Sections.ToList())
            {
                var sec = s;
                AddButtonRC($"Nodes {sec.Low + 1}–{sec.High + 1}  ·  {sec.Shape}  peak {sec.Peak:0.0} floor {sec.Floor:0.0}",
                    A(0.06f, y - h), A(0.94f, y),
                    () => EditSection(sec),
                    () => { _work.Sections.Remove(sec); Commit(); BuildPresetEditor(); });
                y -= (h + gap);
                if (y < 0.42f) break;
            }

            AddButton("+ Section: whole route", A(0.06f, 0.33f), A(0.94f, 0.40f), () =>
            {
                if (nodeCount >= 2)
                {
                    _work.Sections.Add(new Section { StartIndex = 0, EndIndex = nodeCount - 1, Peak = 1.4f, Floor = 0.7f, Shape = SpeedShape.EaseInOut, ShapeK = 1f });
                    _work.Sections.Sort((a, b) => a.Low.CompareTo(b.Low));
                    Commit(); BuildPresetEditor();
                }
            });

            // Stops summary (edit durations here; add/remove via the world context menu on nodes).
            string stopList = _work.StopPoints.Count == 0
                ? "Stops: none (flag nodes via right-click in the world)"
                : "Stops: " + string.Join(", ", _work.StopPoints.OrderBy(sp => sp.Index).Select(sp => $"#{sp.Index + 1}={sp.Duration:0.#}s"));
            UIBuilderHelper.CreateLabel(_content, "StopHdr", stopList, 12,
                UIFontConfig.Colors.ParchmentLabel, A(0.06f, 0.24f), A(0.94f, 0.31f), TextAlignmentOptions.Left);

            if (_work.StopPoints.Count > 0)
            {
                var first = _work.StopPoints.OrderBy(sp => sp.Index).First();
                AddSlider($"Stop #{first.Index + 1} pause (s)", A(0.06f, 0.15f), A(0.94f, 0.22f), 0f, 20f, first.Duration,
                    v => first.Duration = v, () => first.Duration.ToString("0.0"));
            }

            AddButton("‹ Presets", A(0.06f, 0.02f), A(0.48f, 0.11f), () => { Commit(); BuildPresetList(); });
            AddButton(PatrolRouteVisualizer.IsPreviewingSpeed ? "Hide colors" : "Show colors",
                A(0.52f, 0.02f), A(0.94f, 0.11f), ToggleColors);
            FiresCore.UI.FiresPopupTheme.Reskin(_content.parent.gameObject);
        }

        // ── screen: section editor (envelope sliders + curve for Custom) ─────
        private void EditSection(Section sec)
        {
            _editSection = sec;
            BuildSectionEditor();
        }

        private void BuildSectionEditor()
        {
            ClearContent();
            var s = _editSection;
            if (s == null || _work == null) { BuildPresetEditor(); return; }
            SetStatus($"Section {s.Low + 1}–{s.High + 1} · shape {s.Shape}");

            AddSlider("Peak (×walk)", A(0.06f, 0.87f), A(0.94f, 0.93f), 0.2f, 2.5f, s.Peak,
                v => s.Peak = v, () => s.Peak.ToString("0.00"));
            AddSlider("Floor (×walk, span ends)", A(0.06f, 0.78f), A(0.94f, 0.84f), 0.1f, 1.5f, s.Floor,
                v => s.Floor = v, () => s.Floor.ToString("0.00"));
            AddSlider("Shape amount (k)", A(0.06f, 0.69f), A(0.94f, 0.75f), 0.2f, 3.0f, s.ShapeK,
                v => s.ShapeK = v, () => s.ShapeK.ToString("0.00"));

            AddButton($"Shape: {s.Shape}  (click to cycle)", A(0.06f, 0.59f), A(0.94f, 0.66f), () =>
            {
                s.Shape = NextShape(s.Shape);
                if (s.Shape == SpeedShape.Custom && (s.CustomCurve == null || s.CustomCurve.Count == 0))
                    s.CustomCurve = DefaultCurve();
                Commit();
                BuildSectionEditor();
            });

            if (s.Shape == SpeedShape.Custom)
                BuildCurveEditor(s);

            AddButton("Delete section", A(0.06f, 0.02f), A(0.48f, 0.10f), () =>
            {
                _work.Sections.Remove(s); _editSection = null; Commit(); BuildPresetEditor();
            });
            AddButton("‹ Back", A(0.52f, 0.02f), A(0.94f, 0.10f), () => { Commit(); BuildPresetEditor(); });
            FiresCore.UI.FiresPopupTheme.Reskin(_content.parent.gameObject);
        }

        // 2D curve editor: fixed-u vertical handles; drag a handle's height to set speed at that u
        // (piecewise-linear read by DbsmEnvelope.SampleCustom). X = section progress, Y = speed.
        private void BuildCurveEditor(Section s)
        {
            _curveHandles.Clear();
            var areaGo = new GameObject("CurveArea", typeof(RectTransform), typeof(Image));
            areaGo.transform.SetParent(_content, false);
            _curveArea = (RectTransform)areaGo.transform;
            _curveArea.anchorMin = A(0.10f, 0.14f); _curveArea.anchorMax = A(0.90f, 0.55f);
            _curveArea.offsetMin = _curveArea.offsetMax = Vector2.zero;
            areaGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

            UIBuilderHelper.CreateLabel(_content, "CurveHdr", "Drag the dots — X = progress, Y = speed", 11,
                UIFontConfig.Colors.ParchmentLabel, A(0.10f, 0.55f), A(0.90f, 0.585f), TextAlignmentOptions.Center);

            var pts = s.CustomCurve ?? (s.CustomCurve = DefaultCurve());
            for (int i = 0; i < pts.Count; i++)
            {
                var dot = new GameObject("Pt" + i, typeof(RectTransform), typeof(Image));
                dot.transform.SetParent(_curveArea, false);
                var drt = (RectTransform)dot.transform;
                drt.sizeDelta = new Vector2(14f, 14f);
                drt.anchorMin = drt.anchorMax = new Vector2(0f, 0f);
                dot.GetComponent<Image>().color = new Color(1f, 0.9f, 0.3f, 1f);
                _curveHandles.Add(drt);
                PositionCurveHandle(i);
            }
        }

        private void PositionCurveHandle(int i)
        {
            if (_curveArea == null || _editSection?.CustomCurve == null) return;
            if (i < 0 || i >= _curveHandles.Count || i >= _editSection.CustomCurve.Count) return;
            var p = _editSection.CustomCurve[i];
            // Fractional anchors (not pixel offsets) so placement is correct even on the build frame before
            // the Canvas has laid the curve area out (rect.width/height would be 0 then).
            float tx = Mathf.Clamp01(p.x);
            float ty = Mathf.Clamp01(Mathf.InverseLerp(CurveSpeedMin, CurveSpeedMax, p.y));
            var h = _curveHandles[i];
            h.anchorMin = h.anchorMax = new Vector2(tx, ty);
            h.anchoredPosition = Vector2.zero;
        }

        private static List<Vector2> DefaultCurve() => new List<Vector2>
        {
            new Vector2(0f, 0.6f), new Vector2(0.25f, 1.2f), new Vector2(0.5f, 1.6f),
            new Vector2(0.75f, 1.2f), new Vector2(1f, 0.6f),
        };

        private static SpeedShape NextShape(SpeedShape s)
        {
            switch (s)
            {
                case SpeedShape.Flat: return SpeedShape.EaseInOut;
                case SpeedShape.EaseInOut: return SpeedShape.Arch;
                case SpeedShape.Arch: return SpeedShape.Ramp;
                case SpeedShape.Ramp: return SpeedShape.Custom;
                default: return SpeedShape.Flat;
            }
        }

        // ── persistence ──────────────────────────────────────────────────────
        // Writes the working preset back into the route (by name) and saves. Debounced to slider release +
        // discrete actions, never per drag-frame (SaveRouteFull reloads the whole dict). The live preview
        // during a drag comes from LivePreview() reading the in-memory working copy.
        private void Commit()
        {
            if (_work == null) return;
            PatrolEditSession.Mutate(_route, (r, _) =>
            {
                var existing = r.GetPreset(_work.Name);
                if (existing != null) r.Presets[r.Presets.IndexOf(existing)] = _work.Clone();
                else r.Presets.Add(_work.Clone());
            }, ensurePreset: false);
            PatrolEditSession.PresetName = _work.Name;
        }

        private void LivePreview()
        {
            if (_work != null) PatrolRouteVisualizer.PreviewSpeedPreset(_work);
        }

        private void ToggleColors()
        {
            if (PatrolRouteVisualizer.IsPreviewingSpeed) PatrolRouteVisualizer.SetSpeedPreview(null);
            else PatrolRouteVisualizer.SetSpeedPreview(_work != null ? _work.Name : PatrolEditSession.PresetName);
        }

        // ── ui helpers ─────────────────────────────────────────────────────
        private static Vector2 A(float x, float y) => new Vector2(x, y);

        private void SetStatus(string t) { if (_status != null) _status.text = t; }

        private Button AddButton(string label, Vector2 min, Vector2 max, Action action)
        {
            var btn = UIBuilderHelper.CreateParchmentButton(_content, label, min, max, null);
            _hot.Add(new HotZone { Rect = btn.GetComponent<RectTransform>(), Action = action });
            return btn;
        }

        private void AddButtonRC(string label, Vector2 min, Vector2 max, Action left, Action right)
        {
            var btn = UIBuilderHelper.CreateParchmentButton(_content, label, min, max, null);
            _hot.Add(new HotZone { Rect = btn.GetComponent<RectTransform>(), Action = left, RightAction = right });
        }

        // A labelled drag-slider row: caption + live value on top, the slider track below.
        private void AddSlider(string caption, Vector2 min, Vector2 max, float lo, float hi, float value, Action<float> set, Func<string> readout)
        {
            var capMin = new Vector2(min.x, (min.y + max.y) * 0.5f);
            var label = UIBuilderHelper.CreateLabel(_content, caption + "Cap", caption, 12,
                UIFontConfig.Colors.ParchmentLabel, capMin, new Vector2(max.x, max.y), TextAlignmentOptions.Left);

            var slider = UIBuilderHelper.CreateSlider(_content, caption + "Slider", new Vector2(min.x, min.y),
                new Vector2(max.x, (min.y + max.y) * 0.5f), lo, hi, Mathf.Clamp(value, lo, hi), null);

            _sliders.Add(new SliderZone
            {
                Slider = slider, Min = lo, Max = hi,
                Set = v => { set(v); if (label != null) label.text = $"{caption}: {readout()}"; },
            });
            if (label != null) label.text = $"{caption}: {readout()}";
        }

        private void ClearContent()
        {
            _hot.Clear();
            _sliders.Clear();
            _curveHandles.Clear();
            _curveArea = null;
            _dragSlider = -1;
            _dragCurve = -1;
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }
    }
}
