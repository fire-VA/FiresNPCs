using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// The current territory (name + flags) shown in a FUC rounded-skin box under the minimap, plus the
    /// hovered territory when the full map is open. Flags render as chips packed into rows via a greedy
    /// flow layout so a fixed-width box wraps them onto new lines — they never run off the edge, and the
    /// box height grows to fit however many are shown.
    /// </summary>
    [HarmonyPatch]
    internal static class TerritoryHudOverlay
    {
        private static RectTransform _root;      // the box: bg image + VLG + fitter
        private static Text _nameText;
        private static RectTransform _flagsRoot; // vertical stack of chip rows
        private static Font _font;
        private static float _nextUpdateTime;
        private static string _lastSignature;
        private const float LabelGrace = 1.5f;   // 0.0.499: seconds the small-map label survives a gap between territories
        private static float _lastSeenAt;
        private static float _appliedX = float.NaN, _appliedY = float.NaN;
        private static Image _smallBackdrop;   // the minimap's baked InputFieldBackground square (the "blank box")
        private static bool _backdropResolved;
        private const float UpdateInterval = 0.4f;

        internal const float BoxWidth = 212f; // shared with QuestTrackerHud so the two right-side boxes match

        /// <summary>The territory box RectTransform (null before it's built / when hidden), so other HUD elements can stack beneath it.</summary>
        internal static RectTransform BoxRect => _root;
        private const int Pad = 8;
        private const int NameSize = 14;
        private const int FlagSize = 11;

        private static readonly Color BrownFill  = new Color(0.13f, 0.093f, 0.058f, 0.88f);
        private static readonly Color GoldBorder = new Color(0.72f, 0.55f, 0.28f, 1f);
        private static readonly Color ChipFill   = new Color(0.07f, 0.15f, 0.23f, 0.80f);
        private static readonly Color ChipBorder = new Color(0.36f, 0.53f, 0.68f, 1f);
        private static readonly Color ChipText   = new Color(0.86f, 0.92f, 1f, 1f);
        private static readonly Color NameColor  = new Color(0.49f, 0.86f, 0.49f, 1f);

        private class MethodInfoCache
        {
            public System.Reflection.MethodInfo Method;
            public bool Initialized;
        }
        private static MethodInfoCache _hoverResolver;

        [HarmonyPatch(typeof(Minimap), "Update")]
        [HarmonyPostfix]
        private static void Minimap_Update_Postfix(Minimap __instance)
        {
            EnsureUi(__instance);
            ApplyBackdropHide(__instance);
            var mode = GetMode(__instance);

            // Large map hover needs real-time response; small map is throttled.
            if (mode != Minimap.MapMode.Large && Time.time < _nextUpdateTime) return;
            _nextUpdateTime = Time.time + UpdateInterval;
            UpdateOverlay(__instance, mode);
        }

        private static Minimap.MapMode GetMode(Minimap mm)
        {
            var f = AccessTools.Field(typeof(Minimap), "m_mode");
            return f != null ? (Minimap.MapMode)f.GetValue(mm) : Minimap.MapMode.Small;
        }

        // The small minimap prefab carries a baked square Image (Unity's InputFieldBackground sprite) behind the
        // circular map. Vanilla never touches it (Minimap only drives the m_mapImageSmall RawImage + toggles
        // m_smallRoot), but its corners poke past the round map and read as an empty box behind the territory UI.
        // Resolve it once, then keep its enabled state synced to config so it can be toggled back without a relog.
        private static void ApplyBackdropHide(Minimap mm)
        {
            if (!_backdropResolved)
            {
                var root = mm != null ? mm.m_smallRoot : null;
                if (root == null) return; // minimap not ready yet — retry next frame
                _backdropResolved = true;
                foreach (var img in root.GetComponents<Image>())
                    if (img != null && img.sprite != null && img.sprite.name == "InputFieldBackground") { _smallBackdrop = img; break; }
            }
            if (_smallBackdrop == null) return;
            bool hide = true;   // 0.0.588 (Fire 10-15: fixes are not toggles): always hidden; the HideMinimapBackdrop key is gone
            if (_smallBackdrop.enabled == hide) _smallBackdrop.enabled = !hide;
        }

        private static void EnsureUi(Minimap mm)
        {
            if (mm == null || _root != null) return;
            _font = ResolveFont(mm);

            var boxGo = new GameObject("FiresTerritoryHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _root = boxGo.GetComponent<RectTransform>();
            _root.SetParent(mm.m_smallRoot != null ? mm.m_smallRoot.transform : mm.transform, false);
            _root.sizeDelta = new Vector2(BoxWidth, 60f);

            var bg = boxGo.GetComponent<Image>();
            bg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(10, BrownFill, GoldBorder, 2);   // 0.0.594 (Fire 10-15: rounded like the kit, radius 10 = Core's minimap plate)
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;   // 0.0.598: SHIFT+click (admin) opens the quick edit
            boxGo.AddComponent<TerritoryCardClick>();

            // 0.0.655 (UI audit): no own DraggableHud - Core places the box (minimap column / plate, else the HUD dock)

            var vlg = boxGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(Pad, Pad, Pad, Pad);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = boxGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width stays BoxWidth
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // height grows to content

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(_root, false);
            _nameText = nameGo.AddComponent<Text>();
            _nameText.font = _font;
            _nameText.fontSize = NameSize;
            _nameText.fontStyle = FontStyle.Bold;
            _nameText.alignment = TextAnchor.MiddleCenter;
            _nameText.color = NameColor;
            _nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _nameText.verticalOverflow = VerticalWrapMode.Overflow;
            _nameText.supportRichText = true;

            var flagsGo = new GameObject("Flags", typeof(RectTransform));
            flagsGo.transform.SetParent(_root, false);
            _flagsRoot = flagsGo.GetComponent<RectTransform>();
            var fvlg = flagsGo.AddComponent<VerticalLayoutGroup>();
            fvlg.spacing = 5f;
            fvlg.childAlignment = TextAnchor.UpperCenter;
            fvlg.childControlWidth = true;
            fvlg.childControlHeight = true;
            fvlg.childForceExpandWidth = false;
            fvlg.childForceExpandHeight = false;
            // 0.0.594 (Fire 10-15: "the text centered"): no ContentSizeFitter on a layout-group child; the root VLG sizes this column
            // and centres it, and each chip row below is centred by this VLG (UpperCenter).

            _root.gameObject.SetActive(false);
        }

        private static System.Reflection.PropertyInfo s_hudHeld, s_hudEdit;
        private static bool s_dockLooked;
        internal static bool FullScreenUiOpen(out string why)
        {
            why = null;
            try
            {
                if (!s_dockLooked)
                {
                    s_dockLooked = true;
                    var t = Type.GetType("FiresCore.UI.Dock.DockService, FiresUnifiedCore");
                    s_hudHeld = t?.GetProperty("HudHeldHidden", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    s_hudEdit = t?.GetProperty("HudEditMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                }
                if (s_hudHeld != null && (bool)s_hudHeld.GetValue(null)) { why = "Core HUD hide held"; return true; }
                if (s_hudEdit != null && (bool)s_hudEdit.GetValue(null)) { why = "HUD editor"; return true; }
            }
            catch { }
            if (Menu.IsVisible()) { why = "Esc menu"; return true; }
            if (InventoryGui.IsVisible()) { why = "inventory"; return true; }
            if (StoreGui.IsVisible()) { why = "trader"; return true; }
            try { if (global::FiresRPGmaker.UI.GUIManager.IsCustomPanelOpen()) { why = "a Fires panel"; return true; } } catch { }
            try { if (FiresCore.Bridge.ModUiRegistry.IsAnyOpen()) { why = "a registered mod screen"; return true; } } catch { }
            bool chat = Chat.instance != null && Chat.instance.HasFocus();
            if (!chat && Player.m_localPlayer != null && Cursor.lockState != CursorLockMode.Locked && Cursor.visible) { why = "a screen with a free cursor"; return true; }
            return false;
        }

        private static void UpdateOverlay(Minimap mm, Minimap.MapMode mode)
        {
            if (_root == null) return;

            if (mode == Minimap.MapMode.None)
            {
                _root.gameObject.SetActive(false);
                _lastSignature = null;
                return;
            }

            // Honour the vanilla Ctrl+F3 "hide HUD" toggle (screenshots) — same gate nameplates/health bars use.
            if (Hud.IsUserHidden())
            {
                _root.gameObject.SetActive(false);
                return;
            }

            // 0.0.635 (Fire via the lead: the pill showed on about half of the UI tour screens): on the small map the pill is HUD, so it
            // hides under every full-screen UI - a Core HUD-hide holder (NpcBook, ...), the HUD editor, the Esc menu, the inventory,
            // any Fires panel, any registered mod screen, and (catch-all for screens that register nothing) an unlocked cursor
            // outside chat. The large map keeps it: there it is the map's territory widget.
            if (mode != Minimap.MapMode.Large && FullScreenUiOpen(out string why))
            {
                if (_root.gameObject.activeSelf) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryHud] pill hidden: " + why);
                _root.gameObject.SetActive(false);
                return;
            }

            Territory terr;
            if (mode == Minimap.MapMode.Large)
            {
                PositionLarge(mm);
                terr = TryGetHoveredWorldPos(mm, out var world) ? TerritorySyncedState.GetCurrent(world) : null;
            }
            else
            {
                PositionSmall(mm);
                terr = TerritoryRuntime.Current;
                if (terr == null && Player.m_localPlayer != null)
                    terr = TerritorySyncedState.GetCurrent(Player.m_localPlayer.transform.position);
            }

            if (terr == null)
            {
                // 0.0.499: the small map keeps the last label through a short gap (between two disks of one road), so it
                // doesn't blink; it hides once the player has really been out for LabelGrace seconds.
                if (mode != Minimap.MapMode.Large && _lastSignature != null && Time.time - _lastSeenAt < LabelGrace) return;
                _root.gameObject.SetActive(false);
                _lastSignature = null;
                return;
            }
            _lastSeenAt = Time.time;
            ShownTerritory = terr;   // 0.0.598: what a SHIFT+click edits

            var flags = CollectFlags(terr);
            string sig = terr.Name + "|" + string.Join(",", flags);
            if (sig != _lastSignature)
            {
                _lastSignature = sig;
                _nameText.text = terr.Name;
                RebuildFlags(flags);
            }
            _root.gameObject.SetActive(true);
        }

        // 0.0.594 ([generator] Core 0.2.501): with Core's round minimap (override ON) an info plate sits directly under the map; the box
        // then hangs under that plate (FiresShell.MinimapPlateRect, read by reflection so older Cores just keep the m_smallRoot anchor).
        // 0.0.598 (Fire 10-15): the territory the card shows; an admin's SHIFT+left-click on the card opens the quick edit.
        internal static Territory ShownTerritory;

        internal static void OnCardClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e == null || e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
            if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
            if (ShownTerritory == null || !FiresCore.Sync.AdminSyncing.IsLocalAdmin()) return;
            TerritoryQuickEdit.Open(ShownTerritory);
        }

        // Before Core 0.2.514 (no column click hook) the card's own background takes the click; with the hook Core forwards it, so
        // this stays silent and a click never fires twice.
        private sealed class TerritoryCardClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) { if (!MinimapColumn.HasClickHook) OnCardClick(e); }
        }

        private static System.Reflection.PropertyInfo s_plateProp;
        private static bool s_plateLooked;
        internal static RectTransform MinimapPlate()
        {
            if (!s_plateLooked)
            {
                s_plateLooked = true;
                var t = global::FiresRPGmaker.Utilities.QuietTypes.Find("FiresCore.UI.Shell.FiresShell");
                s_plateProp = t != null ? AccessTools.Property(t, "MinimapPlateRect") : null;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] Core MinimapPlateRect {(s_plateProp != null ? "found: the box docks under Core's map plate when it is shown" : "not in this Core: box stays under the minimap")}");
            }
            try { var r = s_plateProp?.GetValue(null) as RectTransform; return r != null && r.gameObject.activeInHierarchy ? r : null; }
            catch { return null; }
        }

        private static void PositionSmall(Minimap mm)
        {
            var plate = MinimapPlate();
            // 0.0.595 ([generator] Core 0.2.502): with Core's minimap column the box stays under m_smallRoot and Core places it (order 10);
            // we only match the plate's width. Re-parenting under the plate as well would move it twice.
            bool column = MinimapColumn.Register("FiresRPGmaker.Territory", _root, 10) && plate != null;
            MinimapColumn.SetClick("FiresRPGmaker.Territory", OnCardClick);   // 0.0.598: Core 0.2.514+ forwards clicks (also above Esc)
            var parent = plate != null && !column ? (Transform)plate : mm.m_smallRoot != null ? mm.m_smallRoot.transform : mm.transform;
            if (_root.parent != parent)
            {
                _root.SetParent(parent, false);
                _appliedX = _appliedY = float.NaN;   // re-apply the offset under the new parent
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] box anchored under {(column ? "the minimap (placed by Core's minimap column)" : plate != null ? "Core's minimap plate" : "the minimap")}");
            }
            if (column)
            {
                // pivot top-centre so Core's "centred on the map x" lands the text centre on the column; width = the plate's
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
                _root.pivot = new Vector2(0.5f, 1f);
                float w = plate.rect.width > 1f ? plate.rect.width : BoxWidth;
                if (Mathf.Abs(_root.sizeDelta.x - w) > 0.5f) _root.sizeDelta = new Vector2(w, _root.sizeDelta.y);
                _appliedX = _appliedY = float.NaN;
                LogCentres(plate);
                return;
            }
            if (plate != null)
            {
                // 0.0.594 (Fire 10-15: "the text centered", one column with Core's plate): exactly the plate's width and x, a small gap
                // under its bottom edge; the user's drag offset is not used here (it belongs to the vanilla map position).
                _root.anchorMin = new Vector2(0f, 0f); _root.anchorMax = new Vector2(1f, 0f);
                _root.pivot = new Vector2(0.5f, 1f);
                _root.sizeDelta = new Vector2(0f, _root.sizeDelta.y);
                _root.anchoredPosition = new Vector2(0f, -PlateGap);
                _appliedX = _appliedY = float.NaN;
                LogCentres(plate);
                return;
            }
            // Hang the box just below the minimap, right-aligned, growing downward.
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 1f);
            if (_root.sizeDelta.x != BoxWidth) _root.sizeDelta = new Vector2(BoxWidth, _root.sizeDelta.y);
            // 0.0.655 (UI audit): the default spot once, then Core's HUD dock owns it (the [Territory] HUD position keys are retired)
            if (float.IsNaN(_appliedX)) { _root.anchoredPosition = new Vector2(-8f, -8f); _appliedX = _appliedY = -8f; }
            if (!_hudDocked) _hudDocked = global::FiresRPGmaker.UI.CoreUiBridge.RegisterHud("rpgmaker.territory", _root, "Territory", "FiresRPGmaker");
        }

        private const float PlateGap = 6f;
        private static string _lastCentreLog;

        // 0.0.594 proof line (the lead: "title and pill centre x against the plate centre x, expect +/-1 px"), logged when it changes.
        private static void LogCentres(RectTransform plate)
        {
            if (_nameText == null || !_root.gameObject.activeInHierarchy) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
            float px = CentreX(plate), bx = CentreX(_root), tx = CentreX(_nameText.rectTransform);
            float pill = _flagsRoot != null && _flagsRoot.gameObject.activeInHierarchy && _flagsRoot.childCount > 0 ? CentreX((RectTransform)_flagsRoot.GetChild(0)) : float.NaN;
            float tw = _nameText.preferredWidth, rw = _root.rect.width;
            string line = $"[TerritoryHud] centres (screen px): plate {px:0.0}, box {bx:0.0}, title {tx:0.0} (text {tw:0} of {rw:0} wide), pill row {(float.IsNaN(pill) ? "none" : pill.ToString("0.0"))}; box width {rw:0} vs plate {plate.rect.width:0}";
            if (line == _lastCentreLog) return;
            _lastCentreLog = line;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug(line);
        }

        private static float CentreX(RectTransform r)
        {
            var c = new Vector3[4]; r.GetWorldCorners(c);
            var cam = r.GetComponentInParent<Canvas>()?.rootCanvas;
            var uiCam = cam != null && cam.renderMode != RenderMode.ScreenSpaceOverlay ? cam.worldCamera : null;
            return (RectTransformUtility.WorldToScreenPoint(uiCam, c[0]).x + RectTransformUtility.WorldToScreenPoint(uiCam, c[2]).x) * 0.5f;
        }

        private static void PositionLarge(Minimap mm)
        {
            var parent = mm.m_largeRoot != null ? mm.m_largeRoot.transform : mm.transform;
            if (_root.parent != parent) _root.SetParent(parent, false);
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-20f, -110f);
            _appliedX = _appliedY = float.NaN; // force PositionSmall to re-apply the config offset on return
        }

        private static bool _hudDocked;   // 0.0.655
        private static void SaveTerrPosition(Vector2 pos)
        {
            var cfg = global::FiresRPGmaker.Managers.ConfigManager.Instance;
            if (cfg?.configTerritoryHudPosX != null) cfg.configTerritoryHudPosX.Value = pos.x;
            if (cfg?.configTerritoryHudPosY != null) cfg.configTerritoryHudPosY.Value = pos.y;
            _appliedX = pos.x; _appliedY = pos.y;
        }

        // Greedy flow: pack chips into rows up to the box's inner width, so flags wrap instead of overflowing.
        private static void RebuildFlags(List<string> flags)
        {
            for (int i = _flagsRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_flagsRoot.GetChild(i).gameObject);

            if (flags == null || flags.Count == 0)
            {
                _flagsRoot.gameObject.SetActive(false);
                return;
            }
            _flagsRoot.gameObject.SetActive(true);

            float innerWidth = (_root.rect.width > 1f ? _root.rect.width : BoxWidth) - 2 * Pad;   // 0.0.594: the plate width when docked under Core's plate
            const float chipHPad = 15f;   // horizontal padding + border slack per chip
            const float chipSpacing = 4f;

            RectTransform currentRow = null;
            float currentRowWidth = 0f;

            foreach (var flag in flags)
            {
                string label = Prettify(flag);
                float chipW = Mathf.Ceil(label.Length * FlagSize * 0.58f) + chipHPad;
                if (chipW > innerWidth) chipW = innerWidth;

                bool needNewRow = currentRow == null || (currentRowWidth + chipSpacing + chipW) > innerWidth;
                if (needNewRow)
                {
                    currentRow = NewRow();
                    currentRowWidth = 0f;
                }
                AddChip(currentRow, label);
                currentRowWidth += (currentRowWidth > 0f ? chipSpacing : 0f) + chipW;
            }
        }

        private static RectTransform NewRow()
        {
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(_flagsRoot, false);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 5f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            return rowGo.GetComponent<RectTransform>();
        }

        private static void AddChip(RectTransform row, string label)
        {
            var chipGo = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            chipGo.transform.SetParent(row, false);
            var img = chipGo.GetComponent<Image>();
            img.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(8, ChipFill, ChipBorder, 1);
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = false;

            var chlg = chipGo.AddComponent<HorizontalLayoutGroup>();
            chlg.padding = new RectOffset(6, 6, 1, 1);
            chlg.childAlignment = TextAnchor.MiddleCenter;
            chlg.childControlWidth = true;
            chlg.childControlHeight = true;
            chlg.childForceExpandWidth = false;
            chlg.childForceExpandHeight = false;

            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(chipGo.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.font = _font;
            txt.fontSize = FlagSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = ChipText;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            txt.text = label;
        }

        // "NoBuildDamage" -> "No Build Damage" (split on lower->upper boundaries).
        private static string Prettify(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return flag;
            var sb = new StringBuilder(flag.Length + 6);
            for (int i = 0; i < flag.Length; i++)
            {
                char c = flag[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(flag[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static Font ResolveFont(Minimap mm)
        {
            try
            {
                var t = mm.GetComponentsInChildren<Text>(true)?.FirstOrDefault();
                if (t != null && t.font != null) return t.font;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static bool TryGetHoveredWorldPos(Minimap mm, out Vector3 world)
        {
            world = Vector3.zero;
            try
            {
                if (_hoverResolver == null) _hoverResolver = new MethodInfoCache();
                if (!_hoverResolver.Initialized)
                {
                    var mmType = mm.GetType();
                    _hoverResolver.Method = mmType.GetMethod("ScreenToWorldPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(Vector3) }, null)
                                         ?? mmType.GetMethod("GetWorldPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(Vector3), typeof(Vector3).MakeByRefType() }, null)
                                         ?? mmType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                                                    .FirstOrDefault(mi => mi.Name == "ScreenToWorldPoint" && mi.GetParameters().Length == 1);
                    _hoverResolver.Initialized = true;
                }
                if (_hoverResolver.Method == null) return false;

                var mi = _hoverResolver.Method;
                var parameters = mi.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType.IsByRef)
                {
                    object[] args = { Input.mousePosition, world };
                    var okObj = mi.Invoke(mm, args);
                    world = (Vector3)args[1];
                    return okObj is bool ok ? ok : true;
                }
                else if (parameters.Length == 1)
                {
                    var res = mi.Invoke(mm, new object[] { Input.mousePosition });
                    if (res is Vector3 v3) { world = v3; return true; }
                }
            }
            catch { }
            return false;
        }

        internal static List<string> CollectFlags(Territory terr)   // 0.0.596: internal for the Stats card
        {
            var list = new List<string>();
            if (terr == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TerritoryFlags flag in Enum.GetValues(typeof(TerritoryFlags)))
            {
                if (flag == TerritoryFlags.None) continue;
                if (!terr.Flags.HasFlag(flag)) continue;
                var name = flag.ToString();
                if (seen.Add(name)) list.Add(name);
            }
            foreach (AdditionalTerritoryFlags flag in Enum.GetValues(typeof(AdditionalTerritoryFlags)))
            {
                if (flag == AdditionalTerritoryFlags.None) continue;
                if (flag == AdditionalTerritoryFlags.CustomJereSpawner) continue; // legacy alias dedupe
                if (!terr.AdditionalFlags.HasFlag(flag)) continue;
                var name = flag.ToString();
                if (seen.Add(name)) list.Add(name);
            }
            return list;
        }
    }
}
