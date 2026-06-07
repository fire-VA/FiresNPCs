using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Shows the current territory (and its flags) under the minimap, plus hover territory when the map is open.
    /// </summary>
    [HarmonyPatch]
    internal static class TerritoryHudOverlay
    {
        private static Text _text;
        private static RectTransform _root;
        private static MethodInfoCache _hoverResolver;
        private static float _nextUpdateTime;
        private static string _cachedText = string.Empty;
        private const float UpdateInterval = 0.5f; // Update HUD text twice per second max

        private class MethodInfoCache
        {
            public System.Reflection.MethodInfo Method;
            public bool Initialized;
        }

        [HarmonyPatch(typeof(Minimap), "Update")]
        [HarmonyPostfix]
        private static void Minimap_Update_Postfix(Minimap __instance)
        {
            EnsureUi(__instance);
            
            // Throttle updates to reduce territory lookups
            if (Time.time < _nextUpdateTime)
            {
                // For large map hover, we still need real-time updates for responsiveness
                var modeField = AccessTools.Field(typeof(Minimap), "m_mode");
                var mode = modeField != null ? (Minimap.MapMode)modeField.GetValue(__instance) : Minimap.MapMode.Small;
                if (mode == Minimap.MapMode.Large)
                {
                    UpdateOverlay(__instance);
                }
                return;
            }
            _nextUpdateTime = Time.time + UpdateInterval;
            
            UpdateOverlay(__instance);
        }

        private static void EnsureUi(Minimap mm)
        {
            if (mm == null) return;
            if (_root == null)
            {
                var anchor = new GameObject("TerritoryHudOverlay", typeof(RectTransform));
                _root = anchor.GetComponent<RectTransform>();
                _root.SetParent(mm.m_smallRoot?.transform ?? mm.transform, false);
                _root.anchorMin = new Vector2(1f, 0f);
                _root.anchorMax = new Vector2(1f, 0f);
                _root.pivot = new Vector2(1f, 0f);
                _root.anchoredPosition = new Vector2(-18f, 0f);
                _root.sizeDelta = new Vector2(440f, 64f);

                var font = ResolveFont(mm);
                _text = CreateText("TerritoryText", _root, font, 16, Vector2.zero, FontStyle.Bold);
            }
        }

        private static Text CreateText(string name, RectTransform parent, Font font, int size, Vector2 anchoredPos, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(420f, 24f);

            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.alignment = TextAnchor.MiddleRight;
            txt.color = Color.white;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
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

        private static void UpdateOverlay(Minimap mm)
        {
            var modeField = AccessTools.Field(typeof(Minimap), "m_mode");
            var mode = modeField != null ? (Minimap.MapMode)modeField.GetValue(mm) : Minimap.MapMode.Small;

            if (_root == null || _text == null)
                return;

            if (mode == Minimap.MapMode.None)
            {
                _text.text = string.Empty;
                _root.gameObject.SetActive(false);
                return;
            }

            _root.gameObject.SetActive(true);

            if (mode == Minimap.MapMode.Large)
            {
                MoveToLarge(mm);
                if (!TryGetHoveredWorldPos(mm, out var world)) { _text.text = string.Empty; return; }
                var terr = TerritorySyncedState.GetCurrent(world);
                if (terr == null) { _text.text = string.Empty; return; }
                _text.text = BuildHudText(terr);
                return;
            }

            // Small/minimap: show current territory
            MoveToSmall(mm);
            var terrCurrent = TerritoryRuntime.Current;
            if (terrCurrent == null && Player.m_localPlayer != null)
            {
                terrCurrent = TerritorySyncedState.GetCurrent(Player.m_localPlayer.transform.position);
            }
            if (terrCurrent == null)
            {
                _text.text = string.Empty;
                return;
            }
            _text.text = BuildHudText(terrCurrent);
        }

        private static void MoveToLarge(Minimap mm)
        {
            if (_root == null) return;
            var targetParent = mm.m_largeRoot != null ? mm.m_largeRoot.transform : mm.transform;
            if (_root.parent != targetParent)
            {
                _root.SetParent(targetParent, false);
            }
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-20f, -100f);
            _root.sizeDelta = new Vector2(440f, 64f);
            var rt = _text != null ? _text.rectTransform : null;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-4f, -10f);
                rt.sizeDelta = new Vector2(420f, 48f);
            }
        }

        private static void MoveToSmall(Minimap mm)
        {
            if (_root == null) return;
            var targetParent = mm.m_smallRoot != null ? mm.m_smallRoot.transform : mm.transform;
            if (_root.parent != targetParent)
            {
                _root.SetParent(targetParent, false);
            }
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-18f, 0f);
            _root.sizeDelta = new Vector2(380f, 64f);
            var rt = _text != null ? _text.rectTransform : null;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-4f, 8f);
                rt.sizeDelta = new Vector2(360f, 48f);
            }
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
                    // Prefer the current ScreenToWorldPoint signature (Vector3) then older GetWorldPoint(out)
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
                    if (res is Vector3 v3)
                    {
                        world = v3;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string BuildHudText(Territory terr)
        {
            if (terr == null) return string.Empty;
            var flags = CollectFlags(terr);
            if (flags.Count == 0) return terr.Name;

            var lines = new List<string>();
            for (int i = 0; i < flags.Count; i += 2)
            {
                lines.Add(string.Join(", ", flags.Skip(i).Take(2)));
            }
            return terr.Name + "\n" + string.Join("\n", lines);
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
                if (flag == AdditionalTerritoryFlags.CustomJereSpawner) continue; // skip legacy alias to avoid duplicates
                if (!terr.AdditionalFlags.HasFlag(flag)) continue;
                var name = flag.ToString();
                if (seen.Add(name)) list.Add(name);
            }
            return list;
        }
    }
}
