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
        private int _fitLoggedFlags = -1;
        private float _bounceLastY;

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
        private int _biomeOpenRow = -1;   // The ForceBiome row whose biome list is open (or a choice flag's list)
        private Vector2 _pickScroll;   // The open list's own scroll
        private static string[] _flagNames;

        private sealed class FlagRow { public string Flag; public string Val; }

        private static readonly string[] TypeNames = { "Flood Fill", "Road", "Circle", "Square" };
        private static readonly TerritoryPaintType[] TypeVals =
            { TerritoryPaintType.FloodFill, TerritoryPaintType.Road, TerritoryPaintType.Circle, TerritoryPaintType.Square };

        // JoinOtherServer is unsupported and left out; ForSale and HeightBounds are line keys, not enum bits, so they are
        // offered by name.
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
            // The stored colour line and the values shown for it; Save sends the line back unchanged unless a colour field was
            // changed
            TerritoryAdminEdit.EditOriginalColour = terr.ColorLine;
            TerritoryAdminEdit.EditOriginalRgba = (b.R, b.G, b.B, b.Alpha);
            // The flags line exactly as stored (every value: ForceBiome=16:Heath,32, PeriodicHeal=5,2, CustomPaint=Paved, ...), so Save writes
            // back what is shown; the rebuilt line only for an older server that does not send it
            b.Flags = !string.IsNullOrWhiteSpace(terr.FlagsLine) ? terr.FlagsLine : FlagsToLine(terr);
            if (terr.Shape == TerritoryShape.Circle || terr.Shape == TerritoryShape.Square) { b.Radius = Mathf.Max(1, terr.Radius); b.Type = terr.Shape == TerritoryShape.Circle ? TerritoryPaintType.Circle : TerritoryPaintType.Square; }
            else if (terr.Shape == TerritoryShape.Polygon) b.Type = TerritoryPaintType.FloodFill;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryPaint] editing '{terr.Name}': flags '{b.Flags}', colour {b.R},{b.G},{b.B},{b.Alpha}, {terr.Shape}{(terr.Shape == TerritoryShape.Circle || terr.Shape == TerritoryShape.Square ? " r " + terr.Radius : "")}");
            EditingCfgRel = cfgRel ?? "*";   // The server finds every block of the name; no local file needed
            EditingName = terr.Name;
            _collapsed = false;
            _instance?.SyncFromBrush();
        }

        /// <summary>True while the (expanded panel or collapsed button) is under the cursor — the paint driver
        /// checks this so a Shift+Alt+drag over the panel doesn't paint the map underneath it.</summary>
        /// <summary>A text field of the panel has the keyboard (Enter must not finish a flood outline then).</summary>
        public static bool IsTypingInPanel() => _instance != null && _show && GUIUtility.keyboardControl != 0;

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

        private void Awake() { try { if (EditingCfgRel == null) TerritoryPaintDriver.Brush.Name = UniqueName(TerritoryPaintDriver.Brush.Name); } catch { } SyncFromBrush(); }   // Never opens on a name already in use

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

                if (_collapsed) { if (!global::FiresRPGmaker.UI.Map.LiveMapApi.HomesteadMode) DrawCollapsed(logW, logH); return; }   // 0.0.777: Core's homestead map docks its Job Board there

                if (_rect.x < 0f) _rect = new Rect(16f, Mathf.Max(20f, (logH - Size.y) / 2f), Size.x, Size.y);
                _rect.width = Size.x; _rect.height = Size.y;
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "", FiresRoundedSkin.Window);
                if (s_askName != null)   // The overwrite question, centred, over the panel
                    s_askRect = GUILayout.Window(WindowId + 77, new Rect((logW - 380f) / 2f, (logH - 150f) / 2f, 380f, 150f), DrawAsk, "", FiresRoundedSkin.Window);
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
            // Stack under the map info box: directly below the Live map Level widget when it shows, else below the
            // biome/coords box; same width and right edge as that box, 8 px gap.
            float wPill = w;
            try
            {
                RectTransform anchor = global::FiresRPGmaker.UI.Map.LiveMap.WidgetRect;
                if (anchor == null && Minimap.instance != null && Minimap.instance.m_biomeNameLarge != null) anchor = Minimap.instance.m_biomeNameLarge.rectTransform;
                if (anchor != null)
                {
                    var c = new Vector3[4];
                    anchor.GetWorldCorners(c);                             // overlay canvas: world == screen px
                    float left = c[0].x / _scale, right = c[2].x / _scale, bottomGui = (Screen.height - c[0].y) / _scale;
                    wPill = Mathf.Max(120f, right - left);
                    x = right - wPill;
                    y = bottomGui + 8f;
                }
            }
            catch { }
            _collapsedRect = new Rect(x, y, wPill, h);
            if (GUI.Button(_collapsedRect, "<b>» Territory</b>", FiresRoundedSkin.Button))
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

            // Pick a territory on the map to edit it.
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
                // Each flag gets the control its value needs - none for an on/off flag, the biome list for ForceBiome, a choice list for a
                // fixed set ("Dirt|Cultivated|..."), else a value box
                string hint = Modules.Territories.TerritoryApi.ValueHintOf(_flags[i].Flag);
                bool biomeFlag = string.Equals(_flags[i].Flag, "ForceBiome", StringComparison.OrdinalIgnoreCase);
                string[] choices = !biomeFlag && hint != null && hint.IndexOf('|') > 0 && hint.IndexOf(' ') < 0 ? hint.Split('|') : null;
                if (hint == null)
                {
                    if (!string.IsNullOrEmpty(_flags[i].Val)) { _flags[i].Val = ""; BuildFlags(b); }   // an on/off flag carries no value
                }
                else if (biomeFlag)
                {
                    // The value is picked from the biomes this server has (vanilla + Expand World Data's), shown by name, stored as its
                    // Heightmap.Biome number
                    string shown = Modules.Territories.TerritoryBiomes.NameOfForce(_flags[i].Val) ?? "pick a biome";   // "Plains - <alt>"
                    if (GUILayout.Button(shown + "  v", FiresRoundedSkin.Button, GUILayout.Width(120f))) { _biomeOpenRow = _biomeOpenRow == i ? -1 : i; _pickScroll = Vector2.zero; }
                }
                else if (choices != null)
                {
                    string shown = string.IsNullOrEmpty(_flags[i].Val) ? "pick" : _flags[i].Val;
                    if (GUILayout.Button(shown + "  v", FiresRoundedSkin.Button, GUILayout.Width(120f))) { _biomeOpenRow = _biomeOpenRow == i ? -1 : i; _pickScroll = Vector2.zero; }
                }
                else
                {
                    string nv = GUILayout.TextField(_flags[i].Val ?? "", FiresRoundedSkin.TextInput, GUILayout.Width(64f));
                    if (nv != _flags[i].Val) { _flags[i].Val = nv; BuildFlags(b); }
                }
                if (GUILayout.Button("<color=#E8A0A0>x</color>", FiresRoundedSkin.ButtonSmall, GUILayout.Width(26f))) removeAt = i;
                GUILayout.EndHorizontal();
                if (hint != null && !biomeFlag && choices == null && string.IsNullOrEmpty(_flags[i].Val)) GUILayout.Label("   value: " + hint, FiresRoundedSkin.Hint);
                if ((biomeFlag || choices != null) && _biomeOpenRow == i)
                {
                    // The list scrolls inside a box of its own
                    var options = biomeFlag
                        ? Modules.Territories.TerritoryBiomes.Choices()   // Each biome, then its vanilla alt biomes
                        : choices.Select(x => (val: x, name: x)).ToList();
                    _pickScroll = GUILayout.BeginScrollView(_pickScroll, GUILayout.Height(Mathf.Min(options.Count * 26f + 6f, 160f)));
                    foreach (var o in options)
                    {
                        // The blend band ("...,32") the row already had is kept when another biome is picked
                        string cur = _flags[i].Val ?? "", band = "";
                        if (biomeFlag) { int comma = cur.LastIndexOf(','); if (comma > 0) { band = cur.Substring(comma); cur = cur.Substring(0, comma); } }
                        bool on = biomeFlag ? string.Equals(cur, o.val, StringComparison.OrdinalIgnoreCase)
                                            : string.Equals(_flags[i].Val, o.val, StringComparison.OrdinalIgnoreCase);
                        if (GUILayout.Button(on ? "<b>" + o.name + "</b>" : o.name, FiresRoundedSkin.ButtonSmall))
                        {
                            _flags[i].Val = o.val + band;
                            _biomeOpenRow = -1;
                            BuildFlags(b);
                        }
                    }
                    GUILayout.EndScrollView();
                }
            }            if (removeAt >= 0) { _flags.RemoveAt(removeAt); BuildFlags(b); }

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
                // Remove one owner through the same server admin path.
                if (GUILayout.Button("- Remove", FiresRoundedSkin.Button, GUILayout.Width(72f)) && !string.IsNullOrWhiteSpace(_ownerArg))
                { TerritoryAdminEdit.RequestOwners("remove", EditingName, _ownerArg.Trim()); _ownerArg = ""; }
                GUILayout.EndHorizontal();
                FiresRoundedSkin.MarkHint("An online player's name, or a Steam ID.");
                if (GUILayout.Button("Clear owners", FiresRoundedSkin.Button)) TerritoryAdminEdit.RequestOwners("clear", EditingName, "");
                // Danger button, confirm first; admins only.
                if (FiresCore.Sync.AdminSyncing.IsLocalAdmin())
                {
                    GUILayout.Space(6f);
                    if (GUILayout.Button("<color=#E8A0A0><b>Delete territory</b></color>", FiresRoundedSkin.Button, GUILayout.Height(28f)))
                    { string del = EditingName; TerritoryDeleteConfirm.Ask(del, () => { RestoreBrush(); ClearEdit(); SyncFromBrush(); }); }
                }
            }

            // Fire 11-02 ("tries to scroll ... bounces back up"): content end vs scroll position, whenever the position moves. Debug only
            if (Event.current.type == EventType.Repaint && Mathf.Abs(_scroll.y - _bounceLastY) > 0.5f)
            {
                float contentEnd = GUILayoutUtility.GetLastRect().yMax;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryBounce] palette scroll {_bounceLastY:0} -> {_scroll.y:0}, content end {contentEnd:0}, view {_rect.height - 140f:0}, window {_rect.height:0}, scale {_scale:0.00}");
                _bounceLastY = _scroll.y;
            }
            GUILayout.EndScrollView();
            // Fire 11-02: "cannot scroll down to the bottom to edit the flags or save": measure first. Logged when the flag count changes
            if (Event.current.type == EventType.Repaint && _flags.Count != _fitLoggedFlags)
            {
                _fitLoggedFlags = _flags.Count;
                var view = GUILayoutUtility.GetLastRect();
                float logH = Screen.height / _scale;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryPaint] palette fit: {_flags.Count} flag(s), window y {_rect.y:0} height {_rect.height:0} of {logH:0} (scale {_scale:0.00}), scroll view {view.height:0} px, bottom {(_rect.yMax > logH ? "OFF" : "on")} screen");
            }

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            bool flood = TerritoryPaintDriver.Brush.Type == TerritoryPaintType.FloodFill;   // A flood outline is finished with Fill
            if (GUILayout.Button(flood ? "Fill" : "Save", FiresRoundedSkin.Button, GUILayout.Height(30f))) OnSave();
            if (GUILayout.Button("Clear", FiresRoundedSkin.Button, GUILayout.Height(30f))) OnClear();
            GUILayout.EndHorizontal();
            GUILayout.Label(flood ? "Release to move the map; Shift+Alt+drag again to keep drawing. Fill to finish."
                                  : "Shift+Alt+drag on the map to draw a shape, then Save.", FiresRoundedSkin.Hint);

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
        // ---- Overwrite confirmation + unique names ----------------------------------------------------------------------------
        private static string s_askName; private static Rect s_askRect;
        internal static void AskOverwrite(string name)
        {
            s_askName = name;
            _collapsed = false;   // _show is the driver's (large map open); the popup draws while the panel shows
            Hud($"'{name}' already exists - answer the popup");
        }
        private void DrawAsk(int id)
        {
            GUILayout.Label($"Territory '{s_askName}' already exists.\nOverwrite it?", FiresRoundedSkin.Label);
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Overwrite", FiresRoundedSkin.Button)) { TerritoryPaintDriver.OverwriteConfirmed = true; s_askName = null; TerritoryPaintDriver.CommitPending(); }
            if (GUILayout.Button("Save as new...", FiresRoundedSkin.Button))
            {
                string n = UniqueName(s_askName); s_askName = null;
                TerritoryPaintDriver.Brush.Name = n; SyncFromBrush();
                TerritoryPaintDriver.CommitPending();
            }
            if (GUILayout.Button("Cancel", FiresRoundedSkin.Button))
            {
                global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] save '{s_askName}': cancelled (the drawing is kept; rename it or Clear)");
                s_askName = null;
            }
            GUILayout.EndHorizontal();
        }
        /// <summary>"territory" -> "territory 2", "territory 3", ... the first name no territory uses.</summary>
        internal static string UniqueName(string baseName)
        {
            string b = (baseName ?? "territory").Trim();
            int sp = b.LastIndexOf(' '); if (sp > 0 && int.TryParse(b.Substring(sp + 1), out _)) b = b.Substring(0, sp);
            if (b.Length == 0) b = "territory";
            if (TerritoryAdminEdit.AreaCount(b) == 0) return b;
            for (int i = 2; i < 10000; i++) { string n = b + " " + i; if (TerritoryAdminEdit.AreaCount(n) == 0) return n; }
            return b + " " + DateTime.Now.Ticks;
        }

        private void OnSave()
        {
            if (EditingCfgRel != null)
            {
                // Every block of the name, on the server (it answers with what it changed).
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
                // The valued body flags keep their value when a territory is loaded back into the brush
                if (add == AdditionalTerritoryFlags.Gravity) parts.Add(name + "=" + terr.Gravity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                else if (add == AdditionalTerritoryFlags.Scale) parts.Add(name + "=" + terr.Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                else parts.Add(name);
            }
            // The line keys the ledger reads outside the enums, so loading a territory back into the brush keeps them.
            if (terr.Type == TerritoryType.ForSale) parts.Add(terr.SellPrice > 0 ? "ForSale=" + terr.SellPrice : "ForSale");
            parts.AddRange(TerritoryLedger.AccessTokens(terr));
            if (!(terr.HeightBounds.Item1 == -100000 && terr.HeightBounds.Item2 == 100000))
                parts.Add("HeightBounds=" + terr.HeightBounds.Item1 + "," + terr.HeightBounds.Item2);
            return parts.Count > 0 ? string.Join("|", parts) : "None";
        }

        private static void Hud(string msg) { try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg); } catch { } }
    }
}
