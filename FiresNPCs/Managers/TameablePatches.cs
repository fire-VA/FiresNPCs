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

        // Vanilla GetName = Localize(m_character ? m_character.m_name : tamed name from the ZDO, else m_piece.m_name). It threw on
        // the dedi (R86, Selective ON): a server-side tame can have m_character unset, and then m_piece (null on a creature) is
        // read, or Localization.instance is null. The same name is worked out here with every reference checked, and vanilla
        // never runs, so GetName cannot throw.
        private static readonly AccessTools.FieldRef<Tameable, Character> s_character = AccessTools.FieldRefAccess<Tameable, Character>("m_character");
        private static readonly AccessTools.FieldRef<Tameable, ZNetView> s_nview = AccessTools.FieldRefAccess<Tameable, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Tameable, Piece> s_piece = AccessTools.FieldRefAccess<Tameable, Piece>("m_piece");

        [HarmonyPatch(typeof(Tameable), "GetName")]
        [HarmonyPrefix]
        private static bool Tameable_GetName_Safe(Tameable __instance, ref string __result)
        {
            string name = string.Empty;
            try
            {
                if (__instance == null) { __result = string.Empty; return false; }
                var character = s_character(__instance);
                if (character == null) character = __instance.GetComponent<Character>();
                var piece = s_piece(__instance);
                string fallback = piece != null ? piece.m_name : string.Empty;
                if (character != null) name = character.m_name ?? string.Empty;
                else
                {
                    var nview = s_nview(__instance);
                    name = nview != null && nview.IsValid() ? nview.GetZDO().GetString(ZDOVars.s_tamedName, fallback) : fallback;
                }
                __result = Localization.instance != null ? Localization.instance.Localize(name) : name;
            }
            catch
            {
                __result = name ?? string.Empty;
            }
            return false;
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

        // Cached reflection for BaseAI.m_nview (protected field - accessed once at class init).
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
