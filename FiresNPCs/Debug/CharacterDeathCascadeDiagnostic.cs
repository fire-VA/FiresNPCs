using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Diagnostics
{
    // ─────────────────────────────────────────────────────────────────────────
    // CharacterDeathCascadeDiagnostic
    //
    // Two diagnostic tools that go together:
    //
    // 1. Harmony Prefix on Character.OnDeath.
    //    Logs, ONCE per death, the list of ZNetView'd children attached to the
    //    dying Character. This is the moment when Unity's cascade-destroy is
    //    about to fire — any children listed here are about to be destroyed
    //    WITHOUT going through ZNetScene.Destroy (which is exactly the orphan
    //    pattern that ZNetSceneStaleInstanceDiagnostic catches a frame later).
    //
    //    If the orphan warnings stop appearing for prefabs we see in this log,
    //    we've confirmed the cascade-destroy theory beyond doubt.
    //
    //    This patch is FOUNDATIONAL: when we want to fix the cascade properly
    //    (re-introduce parented FX safely), we'll convert "log children" into
    //    "trigger TimedDestructions / route through ZNetScene.Destroy on each
    //    child before vanilla cascades them away."
    //
    // 2. Console command `va_orphan_stats`.
    //    Prints ZNetSceneStaleInstanceDiagnostic._cumulativeStaleCounts so you
    //    can see total orphans per prefab name across a play session — the
    //    existing diagnostic only logs first-sighting, the cumulative counter
    //    is tracked but never surfaced.
    //
    // Both tools are diagnostic-only, no behaviour change.
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class CharacterDeathCascadeDiagnostic_OnDeath
    {
        private const string LogPrefix = "[FXCascadeDiag]";

        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            try
            {
                if (__instance == null) return;

                var ownNview = __instance.GetComponent<ZNetView>();
                var childViews = __instance.GetComponentsInChildren<ZNetView>(includeInactive: true);
                if (childViews == null || childViews.Length == 0) return;

                // Build a count of ZNetView'd children by prefab name. Skip the
                // Character's own ZNetView so we only report the FX/projectile
                // attachments that are actually at risk of cascade-destroy.
                var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
                int totalChildren = 0;

                foreach (var cv in childViews)
                {
                    if (cv == null) continue;
                    if (cv == ownNview) continue;
                    if (cv.gameObject == __instance.gameObject) continue;

                    string prefabName = ResolvePrefabName(cv);
                    if (counts.TryGetValue(prefabName, out int n)) counts[prefabName] = n + 1;
                    else counts[prefabName] = 1;
                    totalChildren++;
                }

                if (totalChildren == 0) return;

                string charName = SafeName(__instance);
                string list = string.Join(", ", counts.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key));
                UnityEngine.Debug.Log(
                    $"{LogPrefix} {charName} dying with {totalChildren} ZNetView'd " +
                    $"child{(totalChildren == 1 ? "" : "ren")} about to cascade-destroy: {list}");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} prefix failed: {ex.Message}");
            }
        }

        private static string ResolvePrefabName(ZNetView view)
        {
            try
            {
                var go = view != null ? view.gameObject : null;
                if (go == null) return "<null-go>";
                string n = go.name;
                if (string.IsNullOrEmpty(n)) return "<unnamed>";
                int cloneIdx = n.IndexOf("(Clone)");
                if (cloneIdx >= 0) n = n.Substring(0, cloneIdx);
                return n.Trim();
            }
            catch
            {
                return "<resolve-failed>";
            }
        }

        private static string SafeName(Character c)
        {
            try
            {
                if (!string.IsNullOrEmpty(c.m_name)) return c.m_name;
                int cloneIdx = c.gameObject.name.IndexOf("(Clone)");
                return cloneIdx >= 0 ? c.gameObject.name.Substring(0, cloneIdx) : c.gameObject.name;
            }
            catch { return "<unnameable>"; }
        }
    }

    internal static class CharacterDeathCascadeDiagnostic
    {
        public static void RegisterConsoleCommands()
        {
            new Terminal.ConsoleCommand(
                "va_orphan_stats",
                "Prints cumulative ZNetView orphan counts (per prefab) since session start.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    var counts = ZNetSceneStaleInstanceDiagnostic.GetCumulativeStaleCounts();
                    if (counts == null || counts.Count == 0)
                    {
                        args.Context.AddString("[va_orphan_stats] No orphans recorded this session.");
                        return;
                    }

                    var sorted = counts.OrderByDescending(kv => kv.Value).ToList();
                    int total = sorted.Sum(kv => kv.Value);
                    args.Context.AddString($"[va_orphan_stats] {total} total orphan(s) across {sorted.Count} prefab(s):");
                    foreach (var kv in sorted)
                    {
                        args.Context.AddString($"  {kv.Value,6}  {kv.Key}");
                    }
                });
        }
    }
}
