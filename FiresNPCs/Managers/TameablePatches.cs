using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace FiresRPGmaker.Modules.Vault
{
    // Guards to prevent NRE spam in Tameable.UpdateSummon / GetName when references are missing
    [HarmonyPatch]
    internal static class TameablePatches
    {
        [HarmonyPatch(typeof(Tameable), "UpdateSummon")]
        [HarmonyPrefix]
        private static bool Tameable_UpdateSummon_Safe(Tameable __instance)
        {
            try
            {
                if (__instance == null) return false;
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return false;
                var character = __instance.GetComponent<Character>();
                if (character == null) return false;
                // Accept either MonsterAI (vanilla) or our CompanionAI as a valid AI type.
                // The stub MonsterAI was removed; companions use CompanionAI exclusively.
                var monsterAI = __instance.GetComponent<MonsterAI>();
                var companionAI = __instance.GetComponent<FiresCore.Npc.AI.CompanionAI>();
                if (monsterAI == null && companionAI == null) return false;
                return true; // let original run
            }
            catch
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(Tameable), "GetName")]
        [HarmonyPrefix]
        private static bool Tameable_GetName_Safe(Tameable __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<Character>() == null)
                {
                    __result = string.Empty;
                    return false;
                }
                return true; // proceed to original
            }
            catch
            {
                __result = string.Empty;
                return false;
            }
        }

        [HarmonyPatch(typeof(Tameable), "GetHoverName")]
        [HarmonyPrefix]
        private static bool Tameable_GetHoverName_Safe(Tameable __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<Character>() == null)
                {
                    __result = string.Empty;
                    return false;
                }
                return true; // proceed to original (which calls GetName)
            }
            catch
            {
                __result = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Guard MonsterAI.UpdateAI against destroyed instances.
        /// Valheim's MonoUpdaters keeps a stale list of BaseAI objects; when a MonsterAI
        /// component's GameObject is destroyed the C# wrapper still exists and UpdateAI
        /// gets called on it, causing BaseAI.UpdateAI to NRE inside native Unity bindings.
        /// Returning false skips the original method entirely for dead objects.
        /// </summary>
        [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
        [HarmonyPrefix]
        private static bool MonsterAI_UpdateAI_DestroyedGuard(MonsterAI __instance)
        {
            return __instance != null && (bool)__instance.gameObject;
        }

        // Cached reflection for BaseAI.m_nview (protected field ï¿½ accessed once at class init).
        private static readonly FieldInfo _baseAI_nview =
            AccessTools.Field(typeof(BaseAI), "m_nview");

        /// <summary>
        /// Guard BaseAI.UpdateAI against a null m_nview.
        /// ZNetScene can destroy the ZNetView component before OnDisable removes the BaseAI
        /// from MonoUpdaters' list, leaving m_nview null. The first line of BaseAI.UpdateAI
        /// calls m_nview.IsValid() which throws NRE when m_nview itself is null.
        /// This prefix covers ALL BaseAI subclasses (MonsterAI, AnimalAI, etc.) in one place.
        /// </summary>
        [HarmonyPatch(typeof(BaseAI), "UpdateAI")]
        [HarmonyPrefix]
        private static bool BaseAI_UpdateAI_NviewGuard(BaseAI __instance)
        {
            if (__instance == null || !(bool)(UnityEngine.Object)__instance)
                return false;
            return _baseAI_nview.GetValue(__instance) != null;
        }
    }
}
