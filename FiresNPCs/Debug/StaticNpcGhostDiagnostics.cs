using System.Text;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Diagnostics
{
    /// <summary>
    /// Placement-ghost tracer for the static-NPC pieces. Companions ghost/place perfectly while the
    /// StaticNpc piece (a runtime clone of the same donor prefab) ghosts invisible and places
    /// nothing visible — this dumps the FULL render state of every NPC ghost the moment vanilla
    /// builds it, plus every placed NPC instance, so one hammer-open produces the exact diff
    /// between the working companion ghost and ours.
    /// </summary>
    internal static class StaticNpcGhostDiagnostics
    {
        private static bool Interesting(string name)
            => name != null && (name.StartsWith("StaticNpc") || name.StartsWith("FiresNpcTpl_")
                             || name.StartsWith("CompanionNpc") || name.StartsWith("BaseNpc"));

        internal static string DumpTree(GameObject go, string tag)
        {
            if (go == null) return $"[GhostDiag:{tag}] <null>";
            var sb = new StringBuilder();
            sb.Append($"[GhostDiag:{tag}] '{go.name}' activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} " +
                      $"layer={LayerMask.LayerToName(go.layer)} pos={go.transform.position}\n");
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            sb.Append($"  renderers={renderers.Length}\n");
            int shown = 0;
            foreach (var r in renderers)
            {
                if (shown++ >= 12) { sb.Append("  ...(truncated)\n"); break; }
                string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "<null>";
                string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "<null>";
                var smr = r as SkinnedMeshRenderer;
                string mesh = smr != null ? (smr.sharedMesh != null ? smr.sharedMesh.name : "<NULL MESH>")
                                          : (r.GetComponent<MeshFilter>()?.sharedMesh != null ? r.GetComponent<MeshFilter>().sharedMesh.name : "?");
                string bones = smr != null ? $" bones={(smr.bones != null ? smr.bones.Length : 0)} root={(smr.rootBone != null ? smr.rootBone.name : "<null>")}" : "";
                sb.Append($"  - {r.GetType().Name} '{r.name}' enabled={r.enabled} goActive={r.gameObject.activeInHierarchy} " +
                          $"mesh='{mesh}' mat='{mat}' shader='{shader}' bounds={r.bounds.size}{bones}\n");
            }
            var animator = go.GetComponentInChildren<Animator>(true);
            sb.Append($"  animator={(animator != null ? $"'{animator.name}' enabled={animator.enabled} controller={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "<null>")}" : "<none>")}\n");
            var lod = go.GetComponentInChildren<LODGroup>(true);
            if (lod != null) sb.Append($"  lodGroup on '{lod.name}' enabled={lod.enabled} lodCount={lod.lodCount}\n");
            return sb.ToString();
        }

        /// <summary>Dump every NPC placement ghost right after vanilla builds it.</summary>
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        private static class Player_SetupPlacementGhost_Dump
        {
            private static void Postfix(Player __instance)
            {
                try
                {
                    var ghost = __instance != null ? __instance.m_placementGhost : null;
                    if (ghost == null || !Interesting(ghost.name)) return;
                    UnityEngine.Debug.LogWarning(DumpTree(ghost, "ghost"));
                }
                catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[GhostDiag] ghost dump failed: " + ex.Message); }
            }
        }

        /// <summary>Dump the SELECTED piece prefab whenever an NPC piece gets selected — this is the
        /// object the ghost is instantiated FROM, so a broken source shows here first.</summary>
        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.GetSelectedPiece))]
        private static class PieceTable_GetSelectedPiece_Dump
        {
            private static string _last;
            private static void Postfix(Piece __result)
            {
                try
                {
                    if (__result == null || !Interesting(__result.gameObject.name)) return;
                    if (_last == __result.gameObject.name) return;   // once per selection change
                    _last = __result.gameObject.name;
                    UnityEngine.Debug.LogWarning(DumpTree(__result.gameObject, "selectedPrefab"));
                }
                catch { }
            }
        }
    }
}
