using System;
using UnityEngine;
using TMPro;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Territory-entry announcement in Valheim's native location-discovery style: a runtime clone of
    /// MessageHud.m_biomeFoundPrefab (the "biome discovered" plaque — gold BraidLineHorisontalMedium braids
    /// top and bottom, darken backdrop, Norsebold title, its own reveal/fade animation). Enter-only, local
    /// player only, 5s throttle. Mirrors kg's proven TerritorySystem_Main_Client.ShowCustomTerritoryMessage.
    /// </summary>
    internal static class TerritoryEntryBanner
    {
        // 0.0.499 (Fire 10-05): the title is per NAME, not per territory. A road is many disks with one name and different
        // UIDs, and every disk crossing used to reach here (only the old 5 s throttle held it back, so it re-showed every few
        // seconds down a road). Now: shown when the name differs from the last one shown, or when the player has been out of
        // that name for longer than LeftGrace (so a short gap between two disks never re-triggers it).
        private const float LeftGrace = 4f;
        private static string _lastShownName;
        private static float _leftLastShownAt = -1f;   // < 0 while still inside a territory of that name

        /// <summary>Every territory change of the local player (old and new may be null): keeps track of being in the last shown name.</summary>
        public static void NoteChange(string oldName, string newName)
        {
            if (_lastShownName == null) return;
            bool wasIn = Same(oldName, _lastShownName), isIn = Same(newName, _lastShownName);
            if (isIn) _leftLastShownAt = -1f;
            else if (wasIn && _leftLastShownAt < 0f) _leftLastShownAt = Time.time;
        }

        private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static void Show(string territoryName)
        {
            if (string.IsNullOrEmpty(territoryName)) return;
            var mh = MessageHud.instance;
            if (mh == null || mh.m_biomeFoundPrefab == null) return; // headless server / not ready yet
            if (Same(territoryName, _lastShownName) && (_leftLastShownAt < 0f || Time.time - _leftLastShownAt < LeftGrace)) return;
            _lastShownName = territoryName;
            _leftLastShownAt = -1f;

            try
            {
                var go = UnityEngine.Object.Instantiate(mh.m_biomeFoundPrefab, mh.transform);

                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -200f);
                }

                var title = FindTitle(go.transform);
                if (title != null)
                {
                    title.text = territoryName;
                    FitFrame(title);
                }

                var anim = go.GetComponentInChildren<Animator>();
                if (anim != null) anim.speed = 2f; // kg-parity snappier reveal (~2s total)

                var td = go.AddComponent<TimedDestruction>();
                td.m_timeout = 2f;
                td.Trigger();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] TerritoryEntryBanner failed: {ex.Message}");
            }
        }

        // Fire (10-08): "fix the framing around our text, the decorative top and bottom borders don't fit when it's a double
        // line name". Vanilla's plaque has a 400x57 title and the two braids fixed at y = +-40 (550 wide), sized for a one-line
        // biome name; a long territory name wrapped to two lines and the braids cut through it.
        private const float TitleWidth = 520f;   // inside the 550-wide braids
        private const int MaxLines = 2;
        private const float BraidGap = 14f;      // between the text's edge and a braid's centre
        private const float MinFontRatio = 0.6f;

        /// <summary>
        /// Fits the title, then the frame to it: shrinks the font (down to 60 %) until the name takes at most two lines, sizes
        /// the title to its real rendered height, and moves the braids out so they sit clear above and below it (a one-line
        /// name keeps vanilla's +-40).
        /// </summary>
        private static void FitFrame(TMP_Text title)
        {
            RectTransform titleRt = title.rectTransform;
            titleRt.sizeDelta = new Vector2(TitleWidth, titleRt.sizeDelta.y);
            title.enableAutoSizing = false;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Overflow;

            float start = title.fontSize, min = start * MinFontRatio;
            title.ForceMeshUpdate();
            while (title.textInfo.lineCount > MaxLines && title.fontSize - 2f >= min)
            {
                title.fontSize -= 2f;
                title.ForceMeshUpdate();
            }
            if (title.textInfo.lineCount > MaxLines)
            {
                title.overflowMode = TextOverflowModes.Ellipsis;
                title.maxVisibleLines = MaxLines;
            }

            float height = Mathf.Max(title.GetPreferredValues(title.text, TitleWidth, 0f).y, 1f);
            if (title.textInfo.lineCount > MaxLines) height = Mathf.Min(height, title.fontSize * 1.25f * MaxLines);
            titleRt.sizeDelta = new Vector2(TitleWidth, height);

            float clear = height * 0.5f + BraidGap;
            Transform frame = titleRt.parent;
            int braids = 0;
            for (int i = 0; frame != null && i < frame.childCount; i++)
            {
                Transform child = frame.GetChild(i);
                if (!string.Equals(child.name, "braid", StringComparison.Ordinal)) continue;
                var rt = child as RectTransform;
                if (rt == null) continue;
                float y = rt.anchoredPosition.y;
                float side = y >= 0f ? 1f : -1f;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, side * Mathf.Max(Mathf.Abs(y), clear));
                braids++;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territories] entry banner: '{title.text}' {title.textInfo.lineCount} line(s) at {title.fontSize:0.#} pt "
                + $"(from {start:0.#}), title height {height:0}, braids at +-{clear:0} ({braids} moved if needed)");
        }

        private static TMP_Text FindTitle(Transform root)
        {
            var found = FindDeep(root, "Title");
            if (found != null)
            {
                var t = found.GetComponent<TMP_Text>();
                if (t != null) return t;
            }
            return root.GetComponentInChildren<TMP_Text>(true);
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (string.Equals(c.name, name, StringComparison.Ordinal)) return c;
                var deeper = FindDeep(c, name);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
