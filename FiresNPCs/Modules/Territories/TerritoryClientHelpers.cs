using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Client-only helper methods for territory features that reference types
    /// unavailable on dedicated servers (Minimap, InventoryGui, etc.).
    ///
    /// These are kept in a separate non-[HarmonyPatch] class so that Harmony's
    /// IL scanner never encounters the references when processing TerritoryRuntime
    /// on a headless server.
    /// </summary>
    internal static class TerritoryClientHelpers
    {
        private static MethodInfo _minimapExplore;
        private static int _minimapExploreMode;
        private static bool _minimapExploreResolved;

        /// <summary>
        /// Reveals an area on the minimap. Returns true on success.
        /// </summary>
        public static bool TryMinimapExplore(Vector3 center, float radius)
        {
            var mm = Minimap.instance;
            if (mm == null) return false;

            if (!_minimapExploreResolved)
            {
                _minimapExploreResolved = true;
                var methods = typeof(Minimap)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "Explore");

                foreach (var m in methods)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(Vector3) && ps[1].ParameterType == typeof(float))
                    {
                        _minimapExplore = m;
                        _minimapExploreMode = 1;
                        break;
                    }
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float) && _minimapExplore == null)
                    {
                        _minimapExplore = m;
                        _minimapExploreMode = 2;
                    }
                }
            }

            if (_minimapExplore == null) return false;

            try
            {
                if (_minimapExploreMode == 1)
                {
                    _minimapExplore.Invoke(mm, new object[] { center, radius });
                    return true;
                }
                if (_minimapExploreMode == 2)
                {
                    _minimapExplore.Invoke(mm, new object[] { center.x, center.z });
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Returns true if the minimap is available (client-side only).
        /// </summary>
        public static bool IsMinimapAvailable()
        {
            return Minimap.instance != null;
        }

        /// <summary>
        /// Sets the PVP toggle on InventoryGui if it's open.
        /// </summary>
        public static void SetInventoryPvpToggle(bool pvpEnabled)
        {
            if (InventoryGui.instance != null)
                InventoryGui.instance.m_pvp.isOn = pvpEnabled;
        }
    }
}
