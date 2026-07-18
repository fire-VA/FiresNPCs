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
        private static float _nextAllowedTime;

        public static void Show(string territoryName)
        {
            if (string.IsNullOrEmpty(territoryName)) return;
            var mh = MessageHud.instance;
            if (mh == null || mh.m_biomeFoundPrefab == null) return; // headless server / not ready yet
            if (Time.time < _nextAllowedTime) return;
            _nextAllowedTime = Time.time + 5f;

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
                if (title != null) title.text = territoryName;

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
