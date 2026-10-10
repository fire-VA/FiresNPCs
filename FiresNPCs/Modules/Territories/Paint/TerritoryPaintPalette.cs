using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using FiresCore.UI;

namespace FiresRPGmaker.Modules.Territories.Paint
{
    /// <summary>
    /// The admin territory brush — an IMGUI panel in the shared <see cref="FiresRoundedSkin"/> chrome (the dark
    /// rounded FUC look, same as the faction/config editors), NOT the old baked parchment prefab. Shown while the
    /// large map is open (admin only); every control edits the shared <see cref="TerritoryPaintDriver.Brush"/>.
    /// Shift+Alt+drag paints with it; Shift+Alt+RMB an existing territory loads it here for editing.
    ///
    /// Collapses to a small button pinned to the RIGHT edge of the map (above the vanilla map toolbar) so it stays
    /// out of the way until the admin wants it. Client-only (no OnGUI on a dedicated server).
    /// </summary>
    public class TerritoryPaintPalette : MonoBehaviour
    {
        private const int WindowId = 0x7E44;
        private const float WindowAlpha = 0.95f;
        private static readonly Vector2 Size = new Vector2(330f, 560f);

        private static TerritoryPaintPalette _instance;
        private static bool _show;             // driver: large map open + local admin
        private static bool _collapsed = true; // start clean = just the edge button
        private static readonly TerritoryBrush _snap = new TerritoryBrush();

        public static string EditingCfgRel { get; private set; }
        public static string EditingName { get; private set; }   // the name as loaded (a rename edits the blocks still under it)
        private string _ownerArg = "";

        private Rect _rect = new Rect(-1f, -1f, 0f, 0f);
        private Rect _collapsedRect;
        private float _scale;
        private Vector2 _scroll;
        private readonly object _textToken = new object();

        // numeric field buffers (kept as strings so partial typing works; synced from the brush on load)
        private string _rStr = "60", _gStr = "180", _bStr = "220", _radStr = "30";
        private readonly List<FlagRow> _flags = new List<FlagRow>();
        private int _flagSel;
        private static string[] _flagNames;

        private sealed class FlagRow { public string Flag; public string Val; }

        private static readonly string[] TypeNames = { "Flood Fill", "Road", "Circle", "Square" };
        private static readonly TerritoryPaintType[] TypeVals =
            { TerritoryPaintType.FloodFill, TerritoryPaintType.Road, TerritoryPaintType.Circle, TerritoryPaintType.Square };

        // 0.0.533: JoinOtherServer is unsupported (0.0.531) and left out; ForSale and HeightBounds are line keys, not enum
        // bits, so they are offered by name ([release] 10-08).
        private static string[] FlagNames => _flagNames ??=
            Enum.GetNames(typeof(TerritoryFlags)).Where(n => n != "None")
            .Concat(Enum.GetNames(typeof(AdditionalTerritoryFlags)).Where(n => n != "None" && n != nameof(AdditionalTerritoryFlags.JoinOtherServer)))
            .Concat(new[] { "ForSale", "HeightBounds" })
            .ToArray();

        // ── static API (call sites in TerritoryPaintDriver / TerritoryPaintCommit are unchanged) ──

        public static void SetVisible(bool show)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return; // client-only
            _show = show;
            if (show) EnsureHost();
            if (!show && _instance != null) FiresCore.Input.FiresInputBlock.Release(_instance._textToken);
        }

        public static void ClearEdit() { EditingCfgRel = null; EditingName = null; }

        public static void PushBrushToUi() => _instance?.SyncFromBrush();

        /// <summary>Loads an existing territory into the brush for editing (Shift+Alt+RMB on it) and pops open.</summary>
        public static void LoadFromTerritory(Territory terr, string cfgRel)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EnsureHost();
            SnapshotBrush();
            var b = TerritoryPaintDriver.Brush;
            b.Name = terr.Name;
            if (terr.Colors != null && terr.Colors.Count > 0) { var c = terr.Colors[0]; b.R = c.r; b.G = c.g; b.B = c.b; b.Alpha = c.a; }
            b.Flags = FlagsToLine(terr);
            EditingCfgRel = cfgRel ?? "*";   // 0.0.510: the server finds every block of the name; no local file needed
            EditingName = terr.Name;
            _collapsed = false;
            _instance?.SyncFromBrush();
        }

        /// <summary>True while the (expanded panel or collapsed button) is under the cursor — the paint driver
        /// checks this so a Shift+Alt+drag over the panel doesn't paint the map underneath it.</summary>
        public static bool IsCursorOverPanel()
        {
            if (_instance == null || !_show) return false;
            float sc = _instance._scale <= 0f ? 1f : _instance._scale;
            var p = new Vector2(Input.mousePosition.x / sc, (Screen.height - Input.mousePosition.y) / sc);
            return (_collapsed ? _instance._collapsedRect : _instance._rect).Contains(p);
        }

        private static void EnsureHost()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresTerritoryPaletteHost");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TerritoryPaintPalette>();
        }

        // ── lifecycle ──

        private void Awake() => SyncFromBrush();

        private void Update()
        {
            if (!_show) { FiresCore.Input.FiresInputBlock.Release(_textToken); return; }
            // Only gate game hotkeys while actually typing in the expanded panel; never block the map otherwise
            // (the admin still needs Shift+Alt+drag to paint).
            if (!_collapsed && GUIUtility.keyboardControl != 0) FiresCore.Input.FiresInputBlock.Acquire(_textToken);
            else FiresCore.Input.FiresInputBlock.Release(_textToken);
        }

        private void OnGUI()
        {
            if (!_show) return;
            if (_scale <= 0f) _scale = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.7f);

            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            try
            {
                FiresRoundedSkin.Ensure(WindowAlpha);
                float logW = Screen.width / _scale, logH = Screen.height / _scale;

                if (_collapsed) { DrawCollapsed(logW, logH); return; }

                if (_rect.x < 0f) _rect = new Rect(16f, Mathf.Max(20f, (logH - Size.y) / 2f), Size.x, Size.y);
                _rect.width = Size.x; _rect.height = Size.y;
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "", FiresRoundedSkin.Window);
                _rect.x = Mathf.Clamp(_rect.x, 0f, logW - 60f);
                _rect.y = Mathf.Clamp(_rect.y, 0f, logH - 40f);
                FiresRoundedSkin.DrawPendingTooltip();
            }
            finally { GUI.matrix = matrix; }
        }

        // Collapsed: a single pill parked INSIDE the map's bottom-right, just above the Cartography Table
        // rows. Anchored to the large-map RectTransform (world corners → GUI coords) so it tracks the map
        // on any resolution — the old screen-edge park (logW-w, 34% height) hung OFF the map's right edge.
        private void DrawCollapsed(float logW, float logH)
        {
            const float w = 150f, h = 34f;
            float x = logW - w - 12f, y = logH * 0.34f;   // legacy fallback if the map rect is unavailable
            try
            {
                var lg = Minimap.instance != null ? Minimap.instance.m_largeRoot : null;
                var rt = lg != null ? lg.GetComponent<RectTransform>() : null;
                if (rt != null)
                {
                    var wc = new Vector3[4];
                    rt.GetWorldCorners(wc);                                // overlay canvas: world == screen px
                    float mapRight = wc[2].x / _scale;
                    float mapBottomGui = (Screen.height - wc[0].y) / _scale;
                    float mapHL = (wc[1].y - wc[0].y) / _scale;
                    x = mapRight - w - 16f;
                    y = mapBottomGui - mapHL * 0.16f - h;                  // clears the Cartography Table rows (~13% up)
                }
            }
            catch { }
            _collapsedRect = new Rect(x, y, w, h);
            if (GUI.Button(_collapsedRect, "<b>▸ Territory</b>", FiresRoundedSkin.Button))
                { _collapsed = false; SyncFromBrush(); }
        }

        private void DrawWindow(int id)
        {
            var b = TerritoryPaintDriver.Brush;
            FiresRoundedSkin.ResetTooltip();

            GUILayout.BeginHorizontal();
            GUILayout.Label(EditingCfgRel != null ? $"Editing: {b.Name}" : "Territory Brush", FiresRoundedSkin.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("<b>–</b>", FiresRoundedSkin.ButtonSmall, GUILayout.Width(28f))) _collapsed = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            // 0.0.510 (Fire 10-06): pick a territory on the map to edit it.
            if (GUILayout.Button(TerritoryPaintDriver.PickMode ? "<b>Right-click a territory on the map…</b>  (Esc cancels)" : "<b>Edit territory</b>",
                    TerritoryPaintDriver.PickMode ? FiresRoundedSkin.NavSel : FiresRoundedSkin.Button, GUILayout.Height(28f)))
            {
                if (TerritoryPaintDriver.PickMode) TerritoryPaintDriver.CancelPick(); else TerritoryPaintDriver.BeginPick();
            }
            GUILayout.Space(3f);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_rect.height - 140f));

            if (EditingName != null)
            {
                int areas = TerritoryAdminEdit.AreaCount(EditingName);
                GUILayout.Label($"Editing '{EditingName}': {areas} area(s) share this name. Saving changes all of them (name, colour, flags; shapes stay).", FiresRoundedSkin.Hint);
            }

            GUILayout.Label("Territory Name", FiresRoundedSkin.Hint);
            b.Name = GUILayout.TextField(b.Name ?? "", FiresRoundedSkin.TextInput);

            GUILayout.Space(4f);
            GUILayout.Label("Brush Type", FiresRoundedSkin.Hint);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TypeNames.Length; i++)
                if (GUILayout.Button(TypeNames[i], b.Type == TypeVals[i] ? FiresRoundedSkin.NavSel : FiresRoundedSkin.NavItem))
                    b.Type = TypeVals[i];
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("Brush Color  (R  G  B)", FiresRoundedSkin.Hint);
            GUILayout.BeginHorizontal();
            _rStr = ColorField(_rStr, ref b.R);
            _gStr = ColorField(_gStr, ref b.G);
            _bStr = ColorField(_bStr, ref b.B);
            var sw = GUILayoutUtility.GetRect(46f, 26f, GUILayout.Width(46f), GUILayout.Height(26f));
            if (Event.current.type == EventType.Repaint)
            {
                var prev = GUI.color;
                GUI.color = new Color(b.R / 255f, b.G / 255f, b.B / 255f, Mathf.Clamp01(b.Alpha / 255f));
                GUI.DrawTexture(sw, Texture2D.whiteTexture);
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map Alpha", FiresRoundedSkin.Label, GUILayout.Width(72f));
            b.Alpha = Mathf.RoundToInt(GUILayout.HorizontalSlider(b.Alpha, 0f, 255f, FiresRoundedSkin.Slider, FiresRoundedSkin.SliderThumb));
            GUILayout.Label(b.Alpha.ToString(), FiresRoundedSkin.ValueText, GUILayout.Width(34f));
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("Radius / Width (m)", FiresRoundedSkin.Hint);
            _radStr = GUILayout.TextField(_radStr ?? "", FiresRoundedSkin.TextInput);
            if (int.TryParse(_radStr, out var rad)) b.Radius = Mathf.Max(1, rad);

            GUILayout.Space(4f);
            GUILayout.Label(TypeHint(b.Type), FiresRoundedSkin.Hint);

            GUILayout.Space(6f);
            GUILayout.Label("Flags  (add multiple)", FiresRoundedSkin.SectionBar);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄", FiresRoundedSkin.ButtonSmall, GUILayout.Width(26f))) _flagSel = (_flagSel - 1 + FlagNames.Length) % FlagNames.Length;
            GUILayout.Label(FlagNames[_flagSel], FiresRoundedSkin.Field);
            if (GUILayout.Button("►", FiresRoundedSkin.ButtonSmall, GUILayout.Width(26f))) _flagSel = (_flagSel + 1) % FlagNames.Length;
            if (GUILayout.Button("+ Add", FiresRoundedSkin.Button, GUILayout.Width(56f))
                && !_flags.Any(f => string.Equals(f.Flag, FlagNames[_flagSel], StringComparison.OrdinalIgnoreCase)))
            { _flags.Add(new FlagRow { Flag = FlagNames[_flagSel], Val = "" }); BuildFlags(b); }
            GUILayout.EndHorizontal();
            FiresRoundedSkin.MarkHint("Pick a flag with the arrows, then + Add. Some flags take a value (e.g. a multiplier).");

            int removeAt = -1;
            for (int i = 0; i < _flags.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_flags[i].Flag, FiresRoundedSkin.Field);
                string nv = GUILayout.TextField(_flags[i].Val ?? "", FiresRoundedSkin.TextInput, GUILayout.Width(64f));
                if (nv != _flags[i].Val) { _flags[i].Val = nv; BuildFlags(b); }
                if (GUILayout.Button("<color=#E8A0A0>x</color>", FiresRoundedSkin.ButtonSmall, GUILayout.Width(26f))) removeAt = i;
                GUILayout.EndHorizontal();
            }
            if (removeAt >= 0) { _flags.RemoveAt(removeAt); BuildFlags(b); }

            // Owners (name-keyed: they cover every area of the name; a rename carries them over).
            if (EditingName != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Owners", FiresRoundedSkin.SectionBar);
                var owners = TerritoryOwnership.OwnersOf(EditingName);
                GUILayout.Label(owners.Count == 0 ? "(none)" : string.Join(", ", owners), FiresRoundedSkin.Field);
                GUILayout.BeginHorizontal();
                _ownerArg = GUILayout.TextField(_ownerArg ?? "", FiresRoundedSkin.TextInput);
                if (GUILayout.Button("+ Add", FiresRoundedSkin.Button, GUILayout.Width(56f)) && !string.IsNullOrWhiteSpace(_ownerArg))
                { TerritoryAdminEdit.RequestOwners("add", EditingName, _ownerArg.Trim()); _ownerArg = ""; }
                GUILayout.EndHorizontal();
                FiresRoundedSkin.MarkHint("An online player's name, or a Steam ID.");
                if (GUILayout.Button("Clear owners", FiresRoundedSkin.Button)) TerritoryAdminEdit.RequestOwners("clear", EditingName, "");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", FiresRoundedSkin.Button, GUILayout.Height(30f))) OnSave();
            if (GUILayout.Button("Clear", FiresRoundedSkin.Button, GUILayout.Height(30f))) OnClear();
            GUILayout.EndHorizontal();
            GUILayout.Label("Shift+Alt+drag on the map to draw a shape, then Save.", FiresRoundedSkin.Hint);

            HandleScaleGrip();
            GUI.DragWindow(new Rect(0f, 0f, _rect.width - 34f, 26f));
        }

        private void HandleScaleGrip()
        {
            var grip = new Rect(_rect.width - 20f, _rect.height - 20f, 18f, 18f);
            GUI.Label(grip, "//", FiresRoundedSkin.Hint);
            var e = Event.current;
            if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition)) { _scaling = true; e.Use(); }
            else if (_scaling && e.type == EventType.MouseDrag) { _scale = Mathf.Clamp(_scale + (e.delta.x + e.delta.y) * 0.0025f, 0.7f, 2.2f); e.Use(); }
            else if (_scaling && e.type == EventType.MouseUp) { _scaling = false; e.Use(); }
        }
        private bool _scaling;

        // ── field helpers ──
        private static string ColorField(string s, ref int val)
        {
            s = GUILayout.TextField(s ?? "", FiresRoundedSkin.TextInput, GUILayout.Width(52f));
            if (int.TryParse(s, out var v)) val = Mathf.Clamp(v, 0, 255);
            return s;
        }

        private void SyncFromBrush()
        {
            var b = TerritoryPaintDriver.Brush;
            _rStr = b.R.ToString(); _gStr = b.G.ToString(); _bStr = b.B.ToString(); _radStr = b.Radius.ToString();
            ParseFlags(b.Flags);
        }

        private static string TypeHint(TerritoryPaintType t) => t switch
        {
            TerritoryPaintType.FloodFill => "Flood: hold Shift+Alt and drag an outline; the inside fills.",
            TerritoryPaintType.Road      => "Road: hold Shift+Alt and drag a line across the map.",
            TerritoryPaintType.Circle    => "Circle: hold Shift+Alt and drag from the center outward.",
            TerritoryPaintType.Square    => "Square: hold Shift+Alt and drag from the center outward.",
            _ => "",
        };

        // ── flags ⇄ brush.Flags line ──
        private void ParseFlags(string flags)
        {
            _flags.Clear();
            if (string.IsNullOrWhiteSpace(flags) || flags.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var tok in flags.Split('|', ';'))
            {
                var t = tok.Trim(); if (t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq > 0) _flags.Add(new FlagRow { Flag = t.Substring(0, eq).Trim(), Val = t.Substring(eq + 1).Trim() });
                else _flags.Add(new FlagRow { Flag = t, Val = "" });
            }
        }

        private static void BuildFlags(TerritoryBrush b)
        {
            var parts = _instance._flags
                .Where(f => !string.IsNullOrEmpty(f.Flag))
                .Select(f => string.IsNullOrEmpty(f.Val) ? f.Flag : f.Flag + "=" + f.Val)
                .ToList();
            b.Flags = parts.Count > 0 ? string.Join("|", parts) : "None";
        }

        // ── save / clear (context-aware: commit a drawn shape, or save/cancel an edit) ──
        private void OnSave()
        {
            if (EditingCfgRel != null)
            {
                // 0.0.510: every block of the name, on the server (it answers with what it changed).
                TerritoryAdminEdit.RequestEdit(EditingName ?? TerritoryPaintDriver.Brush.Name, TerritoryPaintDriver.Brush);
                try { TerritoryMapOverlay.ForceRedraw(); } catch { }
                ClearEdit();
                return;
            }
            TerritoryPaintDriver.CommitPending();
        }

        private void OnClear()
        {
            if (EditingCfgRel != null)
            {
                RestoreBrush();
                ClearEdit();
                SyncFromBrush();
                Hud("Edit cancelled");
                return;
            }
            TerritoryPaintDriver.ClearPending();
        }

        // ── edit snapshot (Cancel restores the brush that was active before Shift+Alt+RMB) ──
        private static void SnapshotBrush()
        {
            var b = TerritoryPaintDriver.Brush;
            _snap.Name = b.Name; _snap.Type = b.Type; _snap.R = b.R; _snap.G = b.G; _snap.B = b.B; _snap.Alpha = b.Alpha; _snap.Radius = b.Radius; _snap.Flags = b.Flags;
        }
        private static void RestoreBrush()
        {
            var b = TerritoryPaintDriver.Brush;
            b.Name = _snap.Name; b.Type = _snap.Type; b.R = _snap.R; b.G = _snap.G; b.B = _snap.B; b.Alpha = _snap.Alpha; b.Radius = _snap.Radius; b.Flags = _snap.Flags;
        }

        private static string FlagsToLine(Territory terr)
        {
            var parts = new List<string>();
            foreach (var name in Enum.GetNames(typeof(TerritoryFlags)))
            {
                if (name == "None") continue;
                if (terr.Flags.HasFlag((TerritoryFlags)Enum.Parse(typeof(TerritoryFlags), name))) parts.Add(name);
            }
            foreach (var name in Enum.GetNames(typeof(AdditionalTerritoryFlags)))
            {
                if (name == "None") continue;
                var add = (AdditionalTerritoryFlags)Enum.Parse(typeof(AdditionalTerritoryFlags), name);
                if (!terr.AdditionalFlags.HasFlag(add)) continue;
                // 0.0.498: the valued body flags keep their value when a territory is loaded back into the brush
                if (add == AdditionalTerritoryFlags.Gravity) parts.Add(name + "=" + terr.Gravity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                else if (add == AdditionalTerritoryFlags.Scale) parts.Add(name + "=" + terr.Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                else parts.Add(name);
            }
            // The line keys the ledger reads outside the enums, so loading a territory back into the brush keeps them.
            if (terr.Type == TerritoryType.ForSale) parts.Add(terr.SellPrice > 0 ? "ForSale=" + terr.SellPrice : "ForSale");
            if (!(terr.HeightBounds.Item1 == -100000 && terr.HeightBounds.Item2 == 100000))
                parts.Add("HeightBounds=" + terr.HeightBounds.Item1 + "," + terr.HeightBounds.Item2);
            return parts.Count > 0 ? string.Join("|", parts) : "None";
        }

        private static void Hud(string msg) { try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg); } catch { } }
    }
}
