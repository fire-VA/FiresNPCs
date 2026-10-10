using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.ContextMenu;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI.ContextMenu
{
    using Input = UnityEngine.Input;

    /// <summary>
    /// "Grab" mode for a patrol-route node — the node is put on the cursor like an Infinity Hammer placement and
    /// nudged with IH's own move binds (read from infinity_hammer.cfg when IH is installed, else arrow keys +
    /// PageUp/PageDown). The character is frozen and the camera pinned (Core InputBlock) so the keys/clicks are
    /// free; horizontal moves are camera-relative, up/down is world Y. Left-click places (rewrites the route via
    /// SaveRoute + redraws); right-click / Esc cancels; Keypad0 resets. The visualizer previews the move live
    /// without touching disk until you place.
    /// </summary>
    public sealed class PatrolNodeGrabController : MonoBehaviour
    {
        public static PatrolNodeGrabController Instance { get; private set; }
        public static bool IsActive => Instance != null;

        private const float RepeatDelay = 0.32f;
        private const float RepeatInterval = 0.06f;
        private const float VisualLift = 0.3f;   // node spheres sit 0.3m above the saved route point

        // Terrain + pieces the placement ray can land on when mouse-following (bridges/floors included).
        private static readonly int PlacementMask =
            UnityEngine.LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle", "Default_small");

        private string _route;
        private int _index;
        private Vector3 _original;
        private Vector3 _working;
        private Binds _binds;
        private GameObject _hintGo;
        private float _tUp, _tDown, _tLeft, _tRight, _tFwd, _tBack;

        private bool _mouse;          // mouse-driven placement (Shift+Alt+LMB / Grab button) vs pure key-nudge
        private bool _armed;          // set once the initiating click is released, so we don't place on the pick click
        private float _heightOffset;  // PageUp/Down height applied on top of the cursor's surface point

        public static void Begin(string route, int index, bool mouse = false)
        {
            if (Instance != null) Instance.End();
            if (string.IsNullOrEmpty(route) || !PatrolRouteVisualizer.IsShowing(route)) return;
            if (!PatrolRouteVisualizer.TryGetNodeWorld(index, out var world)) return;

            var go = new GameObject("FiresPatrolNodeGrab");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PatrolNodeGrabController>();
            Instance._route = route;
            Instance._index = index;
            Instance._original = world;
            Instance._working = world;
            Instance._binds = Binds.Resolve();
            Instance._mouse = mouse;
            Instance._armed = false;
            Instance._heightOffset = 0f;

            FiresContextMenu.Close();
            FiresContextMenu.SuppressDriver = true;   // keep the hold-Alt driver out while we own the gate
            InputBlock.Block(true);                   // pin camera + free cursor + freeze character
            Instance.BuildHint();
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(_route) || !PatrolRouteVisualizer.IsShowing(_route)) { End(); return; }

            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up) : Vector3.right;
            fwd = fwd.sqrMagnitude < 0.001f ? Vector3.forward : fwd.normalized;
            right = right.sqrMagnitude < 0.001f ? Vector3.right : right.normalized;

            float amt = _binds.LargeHeld() ? _binds.LargeAmount : _binds.SmallAmount;
            bool freeze = _mouse && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));

            if (_mouse && !freeze)
            {
                // Horizontal follows the cursor's world-raycast point; up/down binds add a persistent height offset
                // (lift onto a bridge). The line stays connected because PreviewNode re-curves the touching segments.
                if (Fire(_binds.Up, ref _tUp)) _heightOffset += amt;
                if (Fire(_binds.Down, ref _tDown)) _heightOffset -= amt;
                if (RaycastWorld(cam, out var p))
                {
                    _working = new Vector3(p.x, p.y + _heightOffset, p.z) + Vector3.up * VisualLift;
                    PatrolRouteVisualizer.PreviewNode(_index, _working);
                }
            }
            else
            {
                // Pure key-nudge: the legacy arrow mode, or a frozen mouse grab being fine-tuned (camera-relative).
                Vector3 move = Vector3.zero;
                if (Fire(_binds.Up, ref _tUp)) move += Vector3.up * amt;
                if (Fire(_binds.Down, ref _tDown)) move += Vector3.down * amt;
                if (Fire(_binds.Left, ref _tLeft)) move += -right * amt;
                if (Fire(_binds.Right, ref _tRight)) move += right * amt;
                if (Fire(_binds.Forward, ref _tFwd)) move += fwd * amt;
                if (Fire(_binds.Backward, ref _tBack)) move += -fwd * amt;
                if (move != Vector3.zero)
                {
                    _working += move;
                    PatrolRouteVisualizer.PreviewNode(_index, _working);
                }
            }

            if ((Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Home))
                && !FiresCore.Input.FiresInputBlock.IsTyping()) // 0.0.550 typing gate (Fire 10-09)
            {
                _working = _original;
                _heightOffset = 0f;
                PatrolRouteVisualizer.PreviewNode(_index, _working);
            }

            // In mouse mode, only accept a placing click AFTER the initiating pick-click is released — otherwise the
            // Shift+Alt+LMB that started the grab (or the Grab-button click) would immediately place it.
            if (_mouse && !Input.GetMouseButton(0)) _armed = true;
            bool canPlace = !_mouse || _armed;
            if (canPlace && Input.GetMouseButtonDown(0)) { Confirm(); return; }
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
        }

        // Fires on press, then repeats while held after a short delay.
        private bool Fire(KeyCode key, ref float timer)
        {
            if (key == KeyCode.None) { timer = 0f; return false; }
            if (FiresCore.Input.FiresInputBlock.IsTyping()) { timer = 0f; return false; } // 0.0.550 typing gate (Fire 10-09)
            if (Input.GetKeyDown(key)) { timer = 0f; return true; }
            if (Input.GetKey(key))
            {
                timer += Time.deltaTime;
                if (timer >= RepeatDelay) { timer = RepeatDelay - RepeatInterval; return true; }
                return false;
            }
            timer = 0f;
            return false;
        }

        // World point under the free cursor (camera ray → terrain/pieces). The camera is pinned by InputBlock, so
        // moving the mouse sweeps the ray across the surface like dragging in a fixed-view editor.
        private static bool RaycastWorld(Camera cam, out Vector3 point)
        {
            point = Vector3.zero;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 3000f, PlacementMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            return false;
        }

        private void Confirm()
        {
            string route = _route;
            var r = PatrolRouteManager.GetRoute(route);
            if (r != null && _index >= 0 && _index < r.Points.Count)
            {
                var pts = new List<Vector3>(r.Points);
                pts[_index] = _working - Vector3.up * VisualLift;
                PatrolRouteManager.SaveRoute(route, pts);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Placed node {_index + 1}");
            }
            if (PatrolRouteVisualizer.IsShowing(route)) { PatrolRouteVisualizer.Hide(); PatrolRouteVisualizer.Show(route); }
            End();
        }

        private void Cancel()
        {
            PatrolRouteVisualizer.PreviewNode(_index, _original);   // snap the preview back
            End();
        }

        private void End()
        {
            if (_hintGo != null) Destroy(_hintGo);
            _hintGo = null;
            InputBlock.Block(false);
            FiresContextMenu.SuppressDriver = false;
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        private void BuildHint()
        {
            _hintGo = new GameObject("FiresPatrolNodeGrabHint");
            DontDestroyOnLoad(_hintGo);
            var canvas = _hintGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7000;
            var scaler = _hintGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = UIBuilderHelper.CreateImage(_hintGo.transform, "HintBg",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), UIFontConfig.Colors.ParchmentPanel);
            var prt = panel.rectTransform;
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(940f, 78f);
            prt.anchoredPosition = new Vector2(0f, 120f);
            var ol = panel.gameObject.AddComponent<Outline>();
            ol.effectColor = UIFontConfig.Colors.ParchmentOutline;
            ol.effectDistance = new Vector2(2f, -2f);

            string hint = _mouse
                ? $"Editing node {_index + 1}   •   Mouse: move   •   {Key(_binds.Up)}/{Key(_binds.Down)}: height   " +
                  $"•   hold Ctrl: freeze + arrow-nudge   (hold {Key(_binds.LargeMod)} = {_binds.LargeAmount:0.##}m steps)\n" +
                  "Left-click: place    •    Right-click / Esc: cancel    •    Keypad0: reset"
                : $"Editing node {_index + 1}   •   {Key(_binds.Forward)}/{Key(_binds.Backward)} fwd·back   " +
                  $"{Key(_binds.Left)}/{Key(_binds.Right)} left·right   {Key(_binds.Up)}/{Key(_binds.Down)} up·down   " +
                  $"(hold {Key(_binds.LargeMod)} = {_binds.LargeAmount:0.##}m steps)\n" +
                  "Left-click: place    •    Right-click / Esc: cancel    •    Keypad0: reset";
            UIBuilderHelper.CreateLabel(panel.transform, "HintText", hint, 15,
                UIFontConfig.Colors.ParchmentInk, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
        }

        private static string Key(KeyCode k) => k == KeyCode.None ? "—" : k.ToString();

        // Move binds + step sizes — from Infinity Hammer's config when it's installed, else arrow-key defaults.
        private sealed class Binds
        {
            public KeyCode Up, Down, Left, Right, Forward, Backward, LargeMod;
            public float SmallAmount, LargeAmount;

            public bool LargeHeld() => LargeMod != KeyCode.None && Input.GetKey(LargeMod);

            public static Binds Resolve()
            {
                var b = Defaults();
                try
                {
                    if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("infinity_hammer"))
                    {
                        string path = Path.Combine(BepInEx.Paths.ConfigPath, "infinity_hammer.cfg");
                        if (File.Exists(path)) ParseIh(b, File.ReadAllLines(path));
                    }
                }
                catch { /* keep defaults on any parse trouble */ }
                return b;
            }

            private static Binds Defaults() => new Binds
            {
                Up = KeyCode.PageUp, Down = KeyCode.PageDown,
                Left = KeyCode.LeftArrow, Right = KeyCode.RightArrow,
                Forward = KeyCode.UpArrow, Backward = KeyCode.DownArrow,
                LargeMod = KeyCode.LeftAlt, SmallAmount = 0.1f, LargeAmount = 1f,
            };

            private static void ParseIh(Binds b, string[] lines)
            {
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (name)
                    {
                        case "Move up": b.Up = Primary(val); break;
                        case "Move down": b.Down = Primary(val); break;
                        case "Move left": b.Left = Primary(val); break;
                        case "Move right": b.Right = Primary(val); break;
                        case "Move forward": b.Forward = Primary(val); break;
                        case "Move backward": b.Backward = Primary(val); break;
                        case "Move up (large)": { var m = Modifier(val); if (m != KeyCode.None) b.LargeMod = m; } break;
                        case "Move amount":
                            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) b.SmallAmount = Mathf.Max(0.001f, s);
                            break;
                        case "Move amount large":
                            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)) b.LargeAmount = Mathf.Max(0.001f, l);
                            break;
                    }
                }
            }

            // "PageUp + LeftAlt" -> PageUp ; "LeftArrow" -> LeftArrow ; "" -> None
            private static KeyCode Primary(string val)
                => string.IsNullOrEmpty(val) ? KeyCode.None : ParseKey(val.Split('+')[0].Trim());

            // "PageUp + LeftAlt" -> LeftAlt
            private static KeyCode Modifier(string val)
            {
                if (string.IsNullOrEmpty(val)) return KeyCode.None;
                var parts = val.Split('+');
                return parts.Length < 2 ? KeyCode.None : ParseKey(parts[1].Trim());
            }

            private static KeyCode ParseKey(string s)
            {
                if (string.IsNullOrEmpty(s)) return KeyCode.None;
                try { return (KeyCode)Enum.Parse(typeof(KeyCode), s, true); }
                catch { return KeyCode.None; }
            }
        }
    }
}
