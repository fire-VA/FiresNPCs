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
            bool hide = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configHideMinimapBackdrop?.Value ?? true;
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
            bg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(14, BrownFill, GoldBorder, 2);
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = false;

            // Drag-to-reposition — only grab-able with the pause menu open (DraggableHud gates it). Persists to config.
            var drag = boxGo.AddComponent<global::FiresRPGmaker.UI.DraggableHud>();
            drag.Target = _root; drag.DragGraphic = bg; drag.OnMoved = SaveTerrPosition;

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
            var ffit = flagsGo.AddComponent<ContentSizeFitter>();
            ffit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            ffit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _root.gameObject.SetActive(false);
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

        private static void PositionSmall(Minimap mm)
        {
            var parent = mm.m_smallRoot != null ? mm.m_smallRoot.transform : mm.transform;
            if (_root.parent != parent) _root.SetParent(parent, false);
            // Hang the box just below the minimap, right-aligned, growing downward.
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 1f);
            // Config-driven offset (drag persists here). Applied-guard so we don't fight a live drag every frame.
            var cfg = global::FiresRPGmaker.Managers.ConfigManager.Instance;
            float px = cfg?.configTerritoryHudPosX?.Value ?? -8f;
            float py = cfg?.configTerritoryHudPosY?.Value ?? -8f;
            if (px != _appliedX || py != _appliedY) { _root.anchoredPosition = new Vector2(px, py); _appliedX = px; _appliedY = py; }
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

            float innerWidth = BoxWidth - 2 * Pad;
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
            var fit = rowGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
            var cfit = chipGo.AddComponent<ContentSizeFitter>();
            cfit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            cfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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

        private static List<string> CollectFlags(Territory terr)
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
