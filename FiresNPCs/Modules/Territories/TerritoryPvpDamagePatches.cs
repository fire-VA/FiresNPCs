using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Forces PvP damage to land inside <see cref="TerritoryFlags.PvpOnly"/>
    /// territories regardless of either player's PvP toggle.
    ///
    /// Why this is needed: <see cref="TerritoryRuntime"/> already flips the
    /// LOCAL player's <c>IsPVPEnabled</c> on territory entry, but the
    /// engine's PvP filter in <c>Character.RPC_Damage</c> reads the TARGET's
    /// flag. In a mixed-toggle scenario (Attacker has PvP on, target has it
    /// off but is standing in a PvP-only zone) the receiver-side gate still
    /// rejects the hit. Setting <see cref="HitData.m_ignorePVP"/> on the
    /// outgoing hit before <c>Character.Damage</c> calls
    /// <c>InvokeRPC("RPC_Damage", hit)</c> propagates the override across
    /// the wire, so the receiver-side gate sees it and lets the hit through.
    ///
    /// Scope: only fires when both attacker and target are players, since
    /// the engine's PvP gate only applies in that case (companions are
    /// <c>Humanoid</c>, not <c>Player</c>, and don't trip the gate).
    /// </summary>
    [HarmonyPatch]
    internal static class TerritoryPvpDamagePatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        [HarmonyPrefix]
        public static void Character_Damage_Prefix(Character __instance, HitData hit)
        {
            try
            {
                if (hit == null || __instance == null) return;
                if (hit.m_ignorePVP) return;

                if (!__instance.IsPlayer()) return;

                var attacker = hit.GetAttacker();
                if (attacker == null || !attacker.IsPlayer()) return;

                // The TARGET's territory rules whether they can opt out of
                // PvP. If the target stands inside a PvpOnly zone, force
                // the hit through regardless of their toggle.
                var terr = TerritorySyncedState.GetCurrent(__instance.transform.position);
                if (terr != null && terr.Flags.HasFlag(TerritoryFlags.PvpOnly))
                {
                    hit.m_ignorePVP = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TerritoryPvpDamagePatches] Character_Damage_Prefix error: {ex.Message}");
            }
        }
    }
}
