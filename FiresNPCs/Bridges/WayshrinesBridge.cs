using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FiresRPGmaker.Bridges
{
    // Reflective seam to the standalone FiresWayshrines mod (a soft dependency). Lets a
    // Teleporter NPC drive the wayshrine destination chooser without a hard reference;
    // every method is a null-safe no-op when the mod isn't installed. Resolution is
    // lazy (first use) per the gameplan's "bridges tolerate either load order" rule, so
    // it doesn't matter which Fires plugin loaded first.
    internal static class WayshrinesBridge
    {
        private const string ApiTypeName = "FiresWayshrines.WayshrinesApi";

        private static bool _resolved;
        private static MethodInfo _getProfileNames;
        private static MethodInfo _openSelectionForProfile;
        private static MethodInfo _openSelection;
        private static MethodInfo _teleportToNamed;

        public static bool Available { get { Resolve(); return _openSelectionForProfile != null; } }

        public static IEnumerable<string> GetProfileNames()
        {
            Resolve();
            try { return _getProfileNames?.Invoke(null, null) as IEnumerable<string> ?? Enumerable.Empty<string>(); }
            catch { return Enumerable.Empty<string>(); }
        }

        public static void OpenSelectionForProfile(Player player, string profile)
        {
            Resolve();
            if (_openSelectionForProfile == null)
            {
                player?.Message(MessageHud.MessageType.Center, "Wayshrines mod is not installed.");
                return;
            }

            try { _openSelectionForProfile.Invoke(null, new object[] { player, profile ?? "" }); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] WayshrinesBridge.OpenSelectionForProfile failed: {ex.Message}"); }
        }

        public static void OpenSelection(Player player)
        {
            Resolve();
            if (_openSelection == null)
            {
                player?.Message(MessageHud.MessageType.Center, "Wayshrines mod is not installed.");
                return;
            }

            try { _openSelection.Invoke(null, new object[] { player }); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] WayshrinesBridge.OpenSelection failed: {ex.Message}"); }
        }

        public static bool TeleportToNamed(Player player, string name)
        {
            Resolve();
            if (_teleportToNamed == null) return false;

            try
            {
                object[] args = { player, name ?? "", null }; // 3rd slot = out string reason
                object result = _teleportToNamed.Invoke(null, args);
                return result is bool ok && ok;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] WayshrinesBridge.TeleportToNamed failed: {ex.Message}");
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                Type api = FindType(ApiTypeName);
                if (api == null) return;

                _getProfileNames = api.GetMethod("GetProfileNames", BindingFlags.Public | BindingFlags.Static);
                _openSelectionForProfile = api.GetMethod("OpenSelectionForProfile", BindingFlags.Public | BindingFlags.Static);
                _openSelection = api.GetMethod("OpenSelection", BindingFlags.Public | BindingFlags.Static);
                _teleportToNamed = api.GetMethod("TeleportToNamed", BindingFlags.Public | BindingFlags.Static);

                Debug.Log("[FiresRPGmaker] WayshrinesBridge resolved FiresWayshrines.WayshrinesApi");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] WayshrinesBridge resolve failed: {ex.Message}");
            }
        }

        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName + ", FiresWayshrines");
            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); if (t != null) return t; }
                catch { }
            }

            return null;
        }
    }
}
