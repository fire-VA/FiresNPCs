using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>The synthetic buff status effect: a vanilla <see cref="SE_Stats"/> (so all 30+ stat-modifier
    /// behaviours come for free) plus the one knob SE_Stats lacks — incoming DamageReduction, which KG's
    /// bespoke BufferMain applied in its own damage override. Per-instance values are set by the apply engine
    /// on a fresh instance/clone, never on a shared registered asset.</summary>
    public class FiresStatBuff : SE_Stats
    {
        /// <summary>Incoming-damage reduction as a 0..1 fraction (0.1 = take 10% less damage). Mirrors KG's
        /// hit.ApplyModifier(Clamp01(1 - reduction)).</summary>
        public float DamageReduction;

        public override void OnDamaged(HitData hit, Character attacker)
        {
            base.OnDamaged(hit, attacker);
            if (DamageReduction > 0f && hit != null)
                hit.ApplyModifier(Mathf.Clamp01(1f - DamageReduction));
        }
    }
}
