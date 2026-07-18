using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Diagnostics
{
    /// <summary>
    /// Static-NPC ZDO lifecycle tracer. Placed static NPCs have been vanishing across zone
    /// unload / relog even though their prefab is persistent and registered; every suspected
    /// destroy path read clean, so this instruments the two seams EVERY ZDO death funnels
    /// through and names the killer with a stack trace:
    ///
    ///   • <see cref="ZDOMan.DestroyZDO"/> — the ORIGINATOR side (only the owner can queue a
    ///     destroy). The stack trace here is the actual culprit call chain.
    ///   • <c>ZDOMan.HandleDestroyedZDO</c> — where the removal actually lands on every machine
    ///     (local destroys route back through the "DestroyZDO" routed RPC too). No useful stack
    ///     for remote-initiated destroys, but it proves the removal and which machine saw it.
    ///   • <see cref="ZNetScene.Destroy"/> — direct networked GameObject+ZDO destroys.
    ///
    /// Matches only our static NPCs (StaticNpc prefab hash or the npc_static_placement ZDO
    /// flag), so the per-call overhead is a hash compare; logs fire only on the rare event.
    /// </summary>
    internal static class StaticNpcZdoDiagnostics
    {
        private static readonly int StaticNpcHash = "StaticNpc".GetStableHashCode();

        // Off unless VerboseLogging — this traces every static-NPC destroy with a full stack, useful
        // when hunting a culling bug but pure spam otherwise.
        private static bool Enabled
        {
            get { try { return global::FiresRPGmaker.Managers.ConfigManager.Instance?.configVerboseLogging?.Value ?? false; } catch { return false; } }
        }

        internal static bool Matches(ZDO zdo)
        {
            if (!Enabled || zdo == null) return false;
            try
            {
                if (zdo.GetPrefab() == StaticNpcHash) return true;
                return zdo.GetBool("npc_static_placement", false);
            }
            catch { return false; }
        }

        internal static string Describe(ZDO zdo)
        {
            if (zdo == null) return "<null zdo>";
            string prefabName = "<unknown>";
            try { prefabName = ZNetScene.instance?.GetPrefab(zdo.GetPrefab())?.name ?? zdo.GetPrefab().ToString(); }
            catch { }
            string role = ZNet.instance != null && ZNet.instance.IsServer() ? "SERVER" : "CLIENT";
            return $"uid={zdo.m_uid} prefab={prefabName} pos={zdo.GetPosition()} persistent={zdo.Persistent} " +
                   $"owner={zdo.GetOwner()} isOwner={zdo.IsOwner()} machine={role}";
        }

        /// <summary>Originator side — the stack trace here is the killer.</summary>
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.DestroyZDO))]
        private static class ZDOMan_DestroyZDO_Trace
        {
            private static void Prefix(ZDO zdo)
            {
                if (!Matches(zdo)) return;
                UnityEngine.Debug.LogWarning(
                    $"[StaticNpcZdoDiag] DestroyZDO CALLED for static NPC — {Describe(zdo)}\n{Environment.StackTrace}");
            }
        }

        /// <summary>Landing side — proves the removal on this machine (remote or local-routed).</summary>
        [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
        private static class ZDOMan_HandleDestroyedZDO_Trace
        {
            private static void Prefix(ZDOMan __instance, ZDOID uid)
            {
                ZDO zdo = __instance.GetZDO(uid);
                if (!Matches(zdo)) return;
                UnityEngine.Debug.LogWarning(
                    $"[StaticNpcZdoDiag] HandleDestroyedZDO REMOVING static NPC — {Describe(zdo)}");
            }
        }

        /// <summary>Direct networked destroys (hammer removal, mod calls).</summary>
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
        private static class ZNetScene_Destroy_Trace
        {
            private static void Prefix(GameObject go)
            {
                if (go == null) return;
                var nview = go.GetComponent<ZNetView>();
                var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                if (!Matches(zdo)) return;
                UnityEngine.Debug.LogWarning(
                    $"[StaticNpcZdoDiag] ZNetScene.Destroy for static NPC '{go.name}' — {Describe(zdo)}\n{Environment.StackTrace}");
            }
        }
    }
}
