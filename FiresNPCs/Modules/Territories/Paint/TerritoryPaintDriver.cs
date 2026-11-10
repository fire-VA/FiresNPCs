using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Territories.Paint
{
    /// <summary>Client-only admin map-paint driver. While the large map is open, an admin holds <b>Shift+Alt</b>
    /// and drags the LEFT mouse like a paintbrush: the stroke is sampled into world points and, on release, stays as
    /// a PENDING shape (the preview remains) per the active brush type (Flood = the drawn outline floods; Road = the
    /// line; Circle/Square = drag from center to edge sets the radius). The admin then hits <b>Save</b> on the
    /// palette to commit it (<see cref="CommitPending"/>) or <b>Clear</b> to discard it (<see cref="ClearPending"/>).
    /// While Shift+Alt is held the vanilla map-pan is suppressed (we skip Minimap.Update); without the modifier the
    /// map pans normally. Esc discards the pending stroke. Brush config comes from the docked palette.</summary>
    public class TerritoryPaintDriver : MonoBehaviour
    {
        private const float SampleDist = 6f;   // world metres between sampled stroke points

        private static TerritoryPaintDriver _instance;
        public static readonly TerritoryBrush Brush = new TerritoryBrush();

        private bool _striking;
        private readonly List<Vector3> _stroke = new List<Vector3>();
        private readonly List<GameObject> _markers = new List<GameObject>();

        public static void Ensure()
        {
            if (_instance != null || Application.isBatchMode) return;
            var go = new GameObject("FiresTerritoryPaintDriver");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TerritoryPaintDriver>();
        }

        /// <summary>True while the admin is actively holding the paint gesture (Shift+Alt+LMB) over the open map —
        /// used by the Minimap.Update patch to suppress the vanilla drag-pan for that frame.</summary>
        public static bool IsPaintGestureActive()
        {
            if (_instance == null) return false;
            return ShiftAltHeld() && Input.GetMouseButton(0) && MapPaintMath.IsLargeMapOpen()
                   && MapPaintMath.CursorOverMap() && !TerritoryPaintPalette.IsCursorOverPanel() && IsLocalAdmin();
        }

        private void Update()
        {
            bool mapOpen = MapPaintMath.IsLargeMapOpen();
            bool admin = IsLocalAdmin();
            TerritoryPaintPalette.SetVisible(mapOpen && admin);
            if (PickMode && (!mapOpen || !admin || Input.GetKeyDown(KeyCode.Escape))) CancelPick();
            if (!mapOpen) { if (_striking) Cancel(); return; }
            if (!admin) { if (_striking) Cancel(); return; }

            bool gesture = ShiftAltHeld() && Input.GetMouseButton(0) && MapPaintMath.CursorOverMap()
                        && !TerritoryPaintPalette.IsCursorOverPanel();

            if (gesture)
            {
                if (!_striking)
                {
                    _striking = true;
                    // A pending FLOOD outline is continued, from whichever end is nearer the cursor (reversed when that is its first point);
                    // every other brush still starts a new shape.
                    bool cont = Brush.Type == TerritoryPaintType.FloodFill && _stroke.Count > 0;
                    if (cont)
                    {
                        if (MapPaintMath.CursorToWorld(out var w0) && Vector3.Distance(w0, _stroke[0]) < Vector3.Distance(w0, _stroke[_stroke.Count - 1]))
                            _stroke.Reverse();
                        Hud($"Continuing the outline ({_stroke.Count} points so far)");
                    }
                    else
                    {
                        _stroke.Clear(); ClearMarkers();    // a new draw replaces any prior pending shape
                        TerritoryPaintPalette.ClearEdit();  // drawing a new shape exits edit-an-existing mode
                    }
                }
                if (MapPaintMath.CursorToWorld(out var w) &&
                    (_stroke.Count == 0 || Vector3.Distance(_stroke[_stroke.Count - 1], w) >= SampleDist))
                    _stroke.Add(w);
            }
            else if (_striking)
            {
                _striking = false;
                // Don't auto-commit — the drawn stroke stays as a PENDING shape (preview remains) until the admin
                // hits Save on the palette (or Clear to discard).
                if (_stroke.Count > 0)
                    Hud(Brush.Type == TerritoryPaintType.FloodFill
                        ? "Outline kept - move the map, Shift+Alt+drag again to keep drawing; Fill (or Enter) to finish, Clear to discard"
                        : "Shape drawn — hit Save on the panel to confirm (Clear to discard)");
            }

            // Enter finishes a pending flood outline (the same as the palette's Fill / Save)
            if (!_striking && _stroke.Count >= 3 && Brush.Type == TerritoryPaintType.FloodFill
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && !TerritoryPaintPalette.IsTypingInPanel())
                CommitPendingInternal();
            if (Input.GetKeyDown(KeyCode.Escape) && (_striking || _stroke.Count > 0)) Cancel();
            UpdatePreview();
        }

        /// <summary>Palette Save button: commit the drawn-but-unsaved shape into a territory cfg.</summary>
        public static void CommitPending()
        {
            if (_instance != null) _instance.CommitPendingInternal();
        }

        /// <summary>Palette Clear button: discard the drawn-but-unsaved shape (and its preview).</summary>
        public static void ClearPending()
        {
            if (_instance == null) return;
            bool had = _instance._stroke.Count > 0;
            _instance.Cancel();
            Hud(had ? "Cleared the drawn shape" : "Nothing to clear");
        }

        // A NEW drawing whose name already exists asks first (the palette's popup); Overwrite / Save as new come back here with the
        // answer
        internal static bool OverwriteConfirmed;
        /// <summary>The one rule the save path uses (and territory_tools_test checks): a new drawing under a name in use asks first.</summary>
        internal static bool MustAskBeforeSaving(string name) => !OverwriteConfirmed && TerritoryAdminEdit.AreaCount((name ?? "").Trim()) > 0;
        private void CommitPendingInternal()
        {
            if (_stroke.Count == 0) { Hud("Nothing drawn — Shift+Alt+drag a shape on the map first"); return; }
            string nm = (Brush.Name ?? "").Trim();
            if (nm.Length == 0) { Hud("Give the territory a name first"); return; }
            if (MustAskBeforeSaving(nm)) { TerritoryPaintPalette.AskOverwrite(nm); return; }
            bool overwrite = OverwriteConfirmed; OverwriteConfirmed = false;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] save '{nm}': {(overwrite ? "overwrite confirmed" : "new")}");
            try
            {
                switch (Brush.Type)
                {
                    case TerritoryPaintType.Circle:
                        TerritoryPaintCommit.CommitCircle(Brush, _stroke[0], StrokeRadius());
                        Hud($"Saved '{Brush.Name}' ({Brush.Type})");
                        break;
                    case TerritoryPaintType.Square:
                        TerritoryPaintCommit.CommitSquare(Brush, _stroke[0], StrokeRadius());
                        Hud($"Saved '{Brush.Name}' ({Brush.Type})");
                        break;
                    case TerritoryPaintType.FloodFill:
                        if (_stroke.Count < 3) { Hud("Outline too small — draw a bigger loop, then Save"); return; }
                        TerritoryPaintCommit.CommitFlood(Brush, new List<Vector3>(_stroke));
                        Hud($"Saved '{Brush.Name}' (flood)");
                        break;
                    case TerritoryPaintType.Road:
                        if (_stroke.Count < 2) { Hud("Road too short — draw a longer line, then Save"); return; }
                        TerritoryPaintCommit.CommitRoad(Brush, new List<Vector3>(_stroke));
                        Hud($"Saved '{Brush.Name}' (road)");
                        break;
                }
                try { TerritoryMapOverlay.ForceRedraw(); } catch { }
            }
            catch (System.Exception ex) { Debug.LogWarning("[TerritoryPaint] commit failed: " + ex.Message); }
            _stroke.Clear();
            ClearMarkers();
            // The next drawing gets a fresh unique name
            Brush.Name = TerritoryPaintPalette.UniqueName("territory");
            TerritoryPaintPalette.PushBrushToUi();
        }

        // Circle/square: drag from the center outward; radius = distance from first to last sampled point
        // (or the brush's configured radius if the admin just clicks without dragging).
        private int StrokeRadius()
        {
            if (_stroke.Count >= 2)
                return Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(_stroke[0], _stroke[_stroke.Count - 1])));
            return Mathf.Max(1, Brush.Radius);
        }

        private void Cancel() { _striking = false; _stroke.Clear(); ClearMarkers(); }

        // ── live preview: colored dots along the stroke (the brush trail) ──
        private void UpdatePreview()
        {
            while (_markers.Count < _stroke.Count) _markers.Add(MakeDot());
            while (_markers.Count > _stroke.Count)
            {
                var m = _markers[_markers.Count - 1]; _markers.RemoveAt(_markers.Count - 1);
                if (m != null) Destroy(m);
            }
            for (int i = 0; i < _stroke.Count; i++)
            {
                var m = _markers[i]; if (m == null) continue;
                if (MapPaintMath.WorldToMapLocal(_stroke[i], out var anchored, out var mapRect) && mapRect != null)
                {
                    var rt = m.GetComponent<RectTransform>();
                    if (rt.parent != mapRect) rt.SetParent(mapRect, false);
                    rt.anchoredPosition = anchored;
                    m.SetActive(true);
                }
                else m.SetActive(false);
            }
        }

        private GameObject MakeDot()
        {
            var go = new GameObject("PaintDot", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(7f, 7f);
            var img = go.GetComponent<Image>();
            img.color = new Color(Brush.R / 255f, Brush.G / 255f, Brush.B / 255f, 0.9f);
            img.raycastTarget = false;
            return go;
        }

        private void ClearMarkers() { foreach (var m in _markers) if (m != null) Destroy(m); _markers.Clear(); }

        // FAT's Biome panel owns Shift+Alt on the map while it is open, so that stroke must not become a pending territory too. Absent on an older FAT.
        // 0.0.840: FatBiomeMapTool.PanelOpen is a static PROPERTY (`internal static bool PanelOpen => s_open && MapOpen();`), so look up its getter;
        // the old AccessTools.Field lookup always missed and warned on every load.
        private static System.Reflection.MethodInfo s_fatPanelGetter; private static bool s_fatLooked, s_fatWasOpen;
        private static bool FatBiomePanelOpen()
        {
            if (!s_fatLooked)
            {
                s_fatLooked = true;
                // 0.0.845 ([classes-ui] 11-08, Fire's BlueHills perf stack without FAT): a quiet lookup. AccessTools.TypeByName logged a
                // HarmonyX Warning "Could not find type named ..." whenever FAT is not installed; asm.GetType(name, false) never logs.
                System.Type t = null; bool fatLoaded = false;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "FiresAdminTerrain") fatLoaded = true;
                    t = asm.GetType("VerdantsAscent.Terrain.Zones.FatBiomeMapTool", false);
                    if (t != null) break;
                }
                var p = t?.GetProperty("PanelOpen", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                s_fatPanelGetter = p != null && p.PropertyType == typeof(bool) ? p.GetGetMethod(true) : null;
                global::FiresRPGmaker.FiresLogger.LogInfo($"[TerritoryPaint] FAT biome panel flag: {(s_fatPanelGetter != null ? "found (FatBiomeMapTool.PanelOpen property): Shift+Alt with that panel open stays FAT's" : fatLoaded || t != null ? "not on this FAT: territory drawing keeps Shift+Alt" : "FiresAdminTerrain not installed: territory drawing keeps Shift+Alt")}");
            }
            bool open = false;
            if (s_fatPanelGetter != null)
            {
                try { open = (bool)s_fatPanelGetter.Invoke(null, null); }
                catch (System.Exception e) { s_fatPanelGetter = null; global::FiresRPGmaker.FiresLogger.LogWarning($"[TerritoryPaint] FAT biome panel flag read failed, ignoring it from now on: {e.GetBaseException().Message}"); }
            }
            if (open != s_fatWasOpen) { s_fatWasOpen = open; global::FiresRPGmaker.FiresLogger.LogDebug($"[TerritoryPaint] FAT biome panel {(open ? "open: territory drawing off" : "closed: territory drawing on")}"); }
            return open;
        }

        private static bool ShiftAltHeld()
            => !FatBiomePanelOpen() && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));

        private static bool IsLocalAdmin() => FiresCore.Sync.AdminSyncing.IsLocalAdmin();

        // The palette's "Edit territory" button arms a pick: the next right-click on the open map loads the territory
        // under the cursor (no Shift+Alt needed). Escape or closing the map cancels.
        public static bool PickMode { get; private set; }

        public static void BeginPick()
        {
            if (!IsLocalAdmin()) return;
            PickMode = true;
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Right-click the territory you want to edit"); } catch { }
        }

        public static void CancelPick() => PickMode = false;

        internal static void PickClick() { PickMode = false; OnEditRightClick(); }

        // Shift+Alt+Right-click: first click on a territory loads it into the palette for editing; a second click
        // (now in edit mode) saves the tweaked colour/alpha/flags back to that territory's cfg, keeping its shape.
        public static void OnEditRightClick()
        {
            if (!IsLocalAdmin()) return;
            if (!MapPaintMath.CursorToWorld(out var w)) return;
            var terrs = ZNet.instance != null && ZNet.instance.IsServer()
                ? TerritoryLedger.Territories
                : TerritorySyncedState.ClientTerritories;
            Territory hit = null;
            if (terrs != null)
                foreach (var tt in terrs)
                    if (tt != null && tt.IsInside2D(w.x, w.z)) { hit = tt; break; }
            if (hit == null) { Hud("No territory here — Shift+Alt+drag to paint a new one"); return; }

            string rel = TerritoryPaintCommit.FindCfgRel(hit.Name);
            TerritoryPaintPalette.LoadFromTerritory(hit, rel);
            Hud($"Editing '{hit.Name}' — adjust, then Save on the panel (every area named '{hit.Name}' changes)");
        }

        private static void Hud(string msg)
        { try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg); } catch { } }
    }

    /// <summary>While the admin holds the paint gesture (Shift+Alt+LMB) over the open large map, skip vanilla
    /// Minimap.Update so the drag paints a stroke instead of panning the map. Released → vanilla resumes.</summary>
    [HarmonyPatch(typeof(Minimap), "Update")]
    internal static class Minimap_Update_PaintPanSuppress
    {
        private static bool Prefix() => !TerritoryPaintDriver.IsPaintGestureActive();
    }

    /// <summary>Shift+Alt+Right-click on the open large map: an admin loads the territory under the cursor into the
    /// paint palette to edit it (Save Changes / Cancel on the panel commit or discard), suppressing the vanilla
    /// pin-remove for that click only. A plain right-click still removes the nearest pin.</summary>
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class Minimap_OnMapRightClick_PaintEdit
    {
        private static bool Prefix()
        {
            if (Minimap.instance == null || !Minimap.instance.m_largeRoot.activeSelf) return true;
            if (TerritoryPaintDriver.PickMode) { TerritoryPaintDriver.PickClick(); return false; }
            bool shiftAlt = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                         && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            if (!shiftAlt) return true;
            TerritoryPaintDriver.OnEditRightClick();
            return false;
        }
    }
}
