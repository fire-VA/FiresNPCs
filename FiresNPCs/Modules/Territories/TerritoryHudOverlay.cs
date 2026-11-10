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
        private const float LabelGrace = 1.5f;   // Seconds the small-map label survives a gap between territories
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

        // 0.0.805 (Fire 10-31 cache rule): the field is looked up once, not on every Minimap.Update
        private static readonly System.Reflection.FieldInfo s_modeField = AccessTools.Field(typeof(Minimap), "m_mode");
        private static Minimap.MapMode GetMode(Minimap mm)
        {
            return s_modeField != null ? (Minimap.MapMode)s_modeField.GetValue(mm) : Minimap.MapMode.Small;
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
            bool hide = true;   // Always hidden; the HideMinimapBackdrop key is gone
            if (_smallBackdrop.enabled == hide) _smallBackdrop.enabled = !hide;
        }

        private static void EnsureUi(Minimap mm)
        {
            if (mm == null || _root != null) return;
            _font = ResolveFont(mm);
            _root = BuildCard(mm.m_smallRoot != null ? mm.m_smallRoot.transform : mm.transform, "FiresTerritoryHud", out _nameText, out _flagsRoot);
            _root.gameObject.AddComponent<TerritoryCardClick>();
            _boxRect = _root;   // Core's live drag handle (the card)
        }

        // The large map shows its OWN card (built once under m_largeRoot, placed once), so the HUD element is never moved, unregistered
        // or re-registered by the map.
        private static RectTransform _mapRoot, _mapFlags;
        private static Text _mapName;
        private static string _mapSignature;
        private static void EnsureMapCard(Minimap mm)
        {
            if (_mapRoot != null || mm == null || mm.m_largeRoot == null) return;
            _mapRoot = BuildCard(mm.m_largeRoot.transform, "FiresTerritoryMapCard", out _mapName, out _mapFlags);
            _mapRoot.gameObject.AddComponent<TerritoryCardClick>();
            _mapRoot.anchorMin = _mapRoot.anchorMax = new Vector2(1f, 1f);
            _mapRoot.pivot = new Vector2(1f, 1f);
            _mapRoot.anchoredPosition = new Vector2(-20f, -110f);
        }

        private static RectTransform BuildCard(Transform parent, string name, out Text nameText, out RectTransform flagsRoot)
        {
            var boxGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var root = boxGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.sizeDelta = new Vector2(BoxWidth, 60f);

            var bg = boxGo.GetComponent<Image>();
            bg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(10, BrownFill, GoldBorder, 2);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;   // SHIFT+click (admin) opens the quick edit
            // UI audit: no own DraggableHud - Core places the box (minimap column / plate, else the HUD dock)

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
            nameGo.transform.SetParent(root, false);
            nameText = nameGo.AddComponent<Text>();
            nameText.font = _font;
            nameText.fontSize = NameSize;
            nameText.fontStyle = FontStyle.Bold;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = NameColor;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.verticalOverflow = VerticalWrapMode.Overflow;
            nameText.supportRichText = true;

            var flagsGo = new GameObject("Flags", typeof(RectTransform));
            flagsGo.transform.SetParent(root, false);
            flagsRoot = flagsGo.GetComponent<RectTransform>();
            var fvlg = flagsGo.AddComponent<VerticalLayoutGroup>();
            fvlg.spacing = 5f;
            fvlg.childAlignment = TextAnchor.UpperCenter;
            fvlg.childControlWidth = true;
            fvlg.childControlHeight = true;
            fvlg.childForceExpandWidth = false;
            fvlg.childForceExpandHeight = false;
            // No ContentSizeFitter on a layout-group child; the root VLG sizes this column and centres it, and each chip row below is
            // centred by this VLG (UpperCenter).

            root.gameObject.SetActive(false);
            return root;
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
            bool large = mode == Minimap.MapMode.Large;
            if (large) EnsureMapCard(mm);
            // One card per map mode; the other one is only hidden (its rect is never written)
            if (_mapRoot != null && !large && _mapRoot.gameObject.activeSelf) { _mapRoot.gameObject.SetActive(false); _mapSignature = null; }
            if (large && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);

            if (mode == Minimap.MapMode.None || (large && _mapRoot == null))
            {
                _root.gameObject.SetActive(false);
                _lastSignature = null;
                return;
            }

            // Honour the vanilla Ctrl+F3 "hide HUD" toggle (screenshots) — same gate nameplates/health bars use.
            var card = large ? _mapRoot : _root;
            if (Hud.IsUserHidden())
            {
                card.gameObject.SetActive(false);
                return;
            }

            // On the small map the pill is HUD, so it hides under every full-screen UI - a Core HUD-hide holder (NpcBook, ...), the HUD
            // editor, the Esc menu, the inventory, any Fires panel, any registered mod screen, and (catch-all for screens that register
            // nothing) an unlocked cursor outside chat. The large map keeps it: there it is the map's territory widget.
            // Non-unified UI (Fires UI override OFF): the Kit-styled pill has no text host and drew empty dark plates over the group HUD
            // (Fire's shot 220); the small-map pill is hidden there (Core 0.2.778 shows its own plain day/time line under the minimap).
            // 0.0.768: 0.0.766 hid the pill here entirely and Fire lost the territory HUD ("does not show at all anymore"); it shows
            // again in both modes, and the first show per mode logs what it draws (font, text, parent, size) to prove the empty-plate case.

            if (!large && FullScreenUiOpen(out string why))
            {
                if (_root.gameObject.activeSelf) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryHud] pill hidden: " + why);
                _root.gameObject.SetActive(false);
                return;
            }

            Territory terr;
            if (large)
            {
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
                // The small map keeps the last label through a short gap (between two disks of one road), so it doesn't
                // blink; it hides once the player has really been out for LabelGrace seconds.
                if (!large && _lastSignature != null && Time.time - _lastSeenAt < LabelGrace) return;
                card.gameObject.SetActive(false);
                if (large) _mapSignature = null; else _lastSignature = null;
                return;
            }
            if (!large) _lastSeenAt = Time.time;
            ShownTerritory = terr;   // What a SHIFT+click edits

            var flags = CollectFlags(terr);
            string sig = terr.Name + "|" + string.Join(",", flags);
            if (large ? sig != _mapSignature : sig != _lastSignature)
            {
                if (large) _mapSignature = sig; else _lastSignature = sig;
                (large ? _mapName : _nameText).text = terr.Name;
                RebuildFlags(flags, card, large ? _mapFlags : _flagsRoot);
            }
            card.gameObject.SetActive(true);
            if (!large)
            {
                bool unified = global::FiresRPGmaker.UI.FiresKitBookSkin.SafeOverride();
                if (_shownLoggedMode != (unified ? 1 : 2))
                {
                    _shownLoggedMode = unified ? 1 : 2;
                    var r = ((RectTransform)card).rect;
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] pill shown ({(unified ? "unified" : "non-unified")}): \"{_nameText?.text}\" font {(_nameText?.font != null ? _nameText.font.name : "NULL")}, {_flagsRoot?.childCount ?? 0} flag row(s), parent {card.parent?.name}, size {r.width:0}x{r.height:0}, in Core column {_inColumn}");
                }
            }
        }
        private static int _shownLoggedMode;

        // With Core's round minimap (override ON) an info plate sits directly under the map; the box then hangs under that plate
        // (FiresShell.MinimapPlateRect, read by reflection so older Cores just keep the m_smallRoot anchor). The territory the
        // card shows; an admin's SHIFT+left-click on the card opens the quick edit.
        internal static Territory ShownTerritory;

        internal static void OnCardClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e == null || e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
            if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
            // Admins on any territory, owners on their own (owner-mode editor).
            if (ShownTerritory == null || !TerritoryQuickEdit.CanEdit(ShownTerritory)) return;
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

        // The old code re-registered after every map close and rewrote anchor / pivot / width / position every tick, over the user's drag
        // and resize. Now the card is registered ONCE (Core minimap column + HUD dock), the default spot is applied ONCE, and after that
        // only Core (and the user's drag) moves it. The large map has its own card (EnsureMapCard), so nothing here is undone by opening
        // the map.
        private static bool _columnTried, _inColumn, _placedOnce;
        private static void PositionSmall(Minimap mm)
        {
            var plate = MinimapPlate();
            if (!_columnTried) { _columnTried = true; _inColumn = MinimapColumn.Register("FiresRPGmaker.Territory", _root, 10); MinimapColumn.SetClick("FiresRPGmaker.Territory", OnCardClick); }
            if (!_hudDocked) _hudDocked = global::FiresRPGmaker.UI.CoreUiBridge.RegisterHud("rpgmaker.territory", _root, "Territory", "FiresRPGmaker", liveEdit: true, dragHandle: _boxRect);
            if (_placedOnce) { CheckDayBoxOverlapOnce(); return; }
            _placedOnce = true;
            // 0.0.790 (Fire 10-28: the box sat ON Core's day/time box): once Core's column has the box it owns the placement in BOTH
            // UIs (unified: under the plate; non-unified: under the day/time box). Before, non-unified (no plate) fell through to the
            // -8,-8 minimap-corner default below, which is exactly the day box's spot; it showed whenever the column skipped the box
            // (dock id listed in Core's column_detached.txt). We never set a position here now.
            if (_inColumn)
            {
                // pivot top-centre so Core's "centred on the map x" lands the text centre on the column; width = the plate's
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
                _root.pivot = new Vector2(0.5f, 1f);
                _root.sizeDelta = new Vector2(plate != null && plate.rect.width > 1f ? plate.rect.width : BoxWidth, _root.sizeDelta.y);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] 0.0.790 default placed once: Core's minimap column owns the spot ({(plate != null ? "under the plate" : "non-unified: under the day/time box")}); RPGM sets no position");
                return;
            }
            if (plate != null)
            {
                // Under Core's plate, the plate's width, a small gap under its bottom edge
                _root.SetParent(plate, false);
                _root.anchorMin = new Vector2(0f, 0f); _root.anchorMax = new Vector2(1f, 0f);
                _root.pivot = new Vector2(0.5f, 1f);
                _root.sizeDelta = new Vector2(0f, _root.sizeDelta.y);
                _root.anchoredPosition = new Vector2(0f, -PlateGap);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryHud] 0.0.711 default placed once: under Core's minimap plate");
                return;
            }
            // Hang the box just below the minimap, right-aligned, growing downward.
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 1f);
            _root.sizeDelta = new Vector2(BoxWidth, _root.sizeDelta.y);
            _root.anchoredPosition = new Vector2(-8f, -8f);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryHud] 0.0.711 default placed once: under the minimap");
        }

        // 0.0.790 (Fire 10-28 "never overlap the day/time"): ~2 s after the box first shows, one check against Core's day/time box
        // (FiresShell.DayTimeBoxRect, Core 0.2.970+). Overlapping AND detached from Core's column (an old drag in column_detached.txt)
        // -> DockService.ReattachToColumn, so Core's column moves it; RPGM never moves Core's box. A detached box the player put
        // somewhere clear stays. Reflected by name: an older Core has no check.
        private static float _shownAt = -1f;
        private static bool _overlapChecked;
        private static System.Reflection.PropertyInfo s_dayRect, s_dayVis;
        private static System.Reflection.MethodInfo s_reattach, s_isDetached;
        private const string DockId = "rpgmaker.territory";

        private static void CheckDayBoxOverlapOnce()
        {
            if (_overlapChecked || _root == null || !_root.gameObject.activeInHierarchy) return;
            if (_shownAt < 0f) { _shownAt = Time.realtimeSinceStartup; return; }
            if (Time.realtimeSinceStartup - _shownAt < 2f) return;
            _overlapChecked = true;
            try
            {
                var shell = global::FiresRPGmaker.Utilities.QuietTypes.Find("FiresCore.UI.Shell.FiresShell");
                var dock = global::FiresRPGmaker.Utilities.QuietTypes.Find("FiresCore.UI.Dock.DockService");
                s_dayRect = shell != null ? AccessTools.Property(shell, "DayTimeBoxRect") : null;
                s_dayVis = shell != null ? AccessTools.Property(shell, "DayTimeBoxVisible") : null;
                s_reattach = dock != null ? AccessTools.Method(dock, "ReattachToColumn", new[] { typeof(string) }) : null;
                s_isDetached = dock != null ? AccessTools.Method(dock, "IsDetachedFromColumn", new[] { typeof(string) }) : null;
                if (s_dayRect == null || s_dayVis == null)
                { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryHud] day/time overlap check: this Core has no DayTimeBoxRect (needs Core 0.2.970+), not checked"); return; }
                if (!(bool)s_dayVis.GetValue(null))
                { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] day/time overlap check: the day/time box is hidden; territory banner at {BoxScreenRect()}, overlap 0"); return; }
                var day = (Rect)s_dayRect.GetValue(null);
                var box = BoxScreenRect();
                float ow = Mathf.Min(box.xMax, day.xMax) - Mathf.Max(box.xMin, day.xMin), oh = Mathf.Min(box.yMax, day.yMax) - Mathf.Max(box.yMin, day.yMin);
                bool overlap = ow > 0.5f && oh > 0.5f;
                bool detached = s_isDetached != null && (bool)s_isDetached.Invoke(null, new object[] { DockId });
                if (!overlap)
                { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] territory banner placed {(box.yMax <= day.yMin + 0.5f ? "below" : "clear of")} the day/time box at {box} (day box {day}), overlap 0{(detached ? "; detached by the player, left where it is" : "")}"); return; }
                if (detached && s_reattach != null && (bool)s_reattach.Invoke(null, new object[] { DockId }))
                {
                    Debug.LogWarning($"[FiresRPGmaker] [TerritoryHud] the territory box sat ON the day/time box ({ow:0}x{oh:0} px at {box}, day box {day}) while detached from Core's minimap column: reattached, Core's column places it below the day/time box");
                    _overlapChecked = false; _shownAt = Time.realtimeSinceStartup;   // re-check after Core's next layout: proves the "overlap 0"
                    return;
                }
                Debug.LogWarning($"[FiresRPGmaker] [TerritoryHud] territory box overlaps the day/time box by {ow:0}x{oh:0} px at {box} (day box {day}); detached {detached}, reattach {(s_reattach != null ? "did nothing" : "not in this Core")}");
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] [TerritoryHud] day/time overlap check failed: {(ex.InnerException ?? ex).Message}"); }
        }

        private static Rect BoxScreenRect()
        {
            var c = new Vector3[4]; _root.GetWorldCorners(c);
            var canvas = _root.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private const float PlateGap = 6f;
        private static string _lastCentreLog;

        // Proof line, logged when it changes.
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


        private static bool _hudDocked;
        private static RectTransform _boxRect;
        private static void SaveTerrPosition(Vector2 pos)
        {
            var cfg = global::FiresRPGmaker.Managers.ConfigManager.Instance;
            if (cfg?.configTerritoryHudPosX != null) cfg.configTerritoryHudPosX.Value = pos.x;
            if (cfg?.configTerritoryHudPosY != null) cfg.configTerritoryHudPosY.Value = pos.y;
            _appliedX = pos.x; _appliedY = pos.y;
        }

        // Greedy flow: pack chips into rows up to the box's inner width, so flags wrap instead of overflowing.
        private static void RebuildFlags(List<string> flags, RectTransform root, RectTransform flagsRoot)
        {
            for (int i = flagsRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(flagsRoot.GetChild(i).gameObject);

            if (flags == null || flags.Count == 0)
            {
                flagsRoot.gameObject.SetActive(false);
                return;
            }
            flagsRoot.gameObject.SetActive(true);

            float innerWidth = (root.rect.width > 1f ? root.rect.width : BoxWidth) - 2 * Pad;   // The plate width when docked under Core's plate
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
                    currentRow = NewRow(flagsRoot);
                    currentRowWidth = 0f;
                }
                AddChip(currentRow, label);
                currentRowWidth += (currentRowWidth > 0f ? chipSpacing : 0f) + chipW;
            }
        }

        private static RectTransform NewRow(RectTransform flagsRoot)
        {
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(flagsRoot, false);
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

        // 0.0.792: resolved ONCE and cached, null included (Unity 6 throws "Arial.ttf is no longer a valid built in font" - that throw
        // left _root null, so EnsureUi re-ran it every Minimap.Update frame: 19k throws in 16 min in the Studio kit). LegacyRuntime.ttf
        // is Unity 6's built-in; Arial.ttf stays the fallback for older runtimes.
        private static bool _fontResolved;
        private static Font _fontCached;
        private static Font ResolveFont(Minimap mm)
        {
            if (_fontResolved) return _fontCached;
            _fontResolved = true;
            try
            {
                var t = mm.GetComponentsInChildren<Text>(true)?.FirstOrDefault();
                if (t != null && t.font != null) return _fontCached = t.font;
            }
            catch { }
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try { _fontCached = Resources.GetBuiltinResource<Font>(name); } catch { _fontCached = null; }
                if (_fontCached != null) break;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryHud] font resolved once: {(_fontCached != null ? _fontCached.name : "none (Text keeps its default)")}");
            return _fontCached;
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

        internal static List<string> CollectFlags(Territory terr)   // Internal for the Stats card
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
