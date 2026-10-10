using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>The pet's passive buff + the status-effect half of its skill, worn by the OWNER (a player or a companion), applied
    /// by the client that owns that Character's ZDO through SEMan (Tools\COMPANION_PET_SLOT_DESIGN.md §6). Values are recomputed from
    /// config every time it's applied (PetBuffApplier), so an edited item can't push past a cap. Shown in the status bar with the
    /// pet's icon. Never on a level-0 pet.</summary>
    public class SE_FiresPet : SE_Stats
    {
        public const string SeName = "SE_FiresPet";
        public static readonly int SeHash = SeName.GetStableHashCode();

        public string Creature;
        public int PetLevel;
        public readonly Dictionary<PetStat, float> Values = new Dictionary<PetStat, float>();
        public string SkillId;
        public float SkillValue;
        private float _regenTick;

        public float V(PetStat s) => Values.TryGetValue(s, out var v) ? v : 0f;
        public float Skill(string id) => SkillId == id ? SkillValue : 0f;

        private static bool Night() => EnvMan.instance != null && EnvMan.IsNight();
        private bool InCave() => m_character != null && m_character.InInterior();

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            m_time = 0f;   // permanent while the pet is set
            _regenTick += dt;
            if (_regenTick < 1f || m_character == null) return;
            float sec = _regenTick; _regenTick = 0f;
            float hp = V(PetStat.HealthRegen) + (m_character.IsSwimming() || m_character.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) ? V(PetStat.HealthRegenWater) : 0f);
            if (hp > 0f && m_character.GetHealth() < m_character.GetMaxHealth()) m_character.Heal(hp * sec, false);
            if (V(PetStat.EitrRegen) > 0f && m_character is Player p) p.AddEitr(V(PetStat.EitrRegen) * sec);
        }

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            base.ModifyStaminaRegen(ref staminaRegen);
            staminaRegen *= 1f + V(PetStat.StaminaRegen) + (InCave() ? V(PetStat.StaminaRegenCaves) : 0f);
        }

        public override void ModifyEitrRegen(ref float eitrRegen)
        {
            base.ModifyEitrRegen(ref eitrRegen);
            eitrRegen *= 1f + V(PetStat.EitrRegenPct);
        }

        public override void ModifyMaxCarryWeight(float baseLimit, ref float limit)
        {
            base.ModifyMaxCarryWeight(baseLimit, ref limit);
            limit += V(PetStat.Carry);
        }

        public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
        {
            base.ModifySpeed(baseSpeed, ref speed, character, dir);
            if (character != null && character.IsSwimming()) { speed *= 1f + V(PetStat.SwimSpeed); return; }
            speed *= 1f + V(PetStat.RunSpeed) + (Night() ? V(PetStat.SprintSpeedNight) : 0f);
        }

        public override void ModifyJump(Vector3 baseJump, ref Vector3 jump)
        {
            base.ModifyJump(baseJump, ref jump);
            float j = V(PetStat.Jump) + Skill("hop");
            jump.y *= 1f + j;
        }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            float all = 1f + V(PetStat.Damage) + (Time.time < BattleCryUntil ? Skill("battle_cry") : 0f);
            hitData.m_damage.Modify(all);
            float elem = V(PetStat.ElementalDamage);
            if (elem > 0f)
            {
                hitData.m_damage.m_fire *= 1f + elem; hitData.m_damage.m_frost *= 1f + elem; hitData.m_damage.m_lightning *= 1f + elem;
                hitData.m_damage.m_poison *= 1f + elem; hitData.m_damage.m_spirit *= 1f + elem;
            }
            hitData.m_damage.m_fire *= 1f + V(PetStat.FireDamage);
            hitData.m_damage.m_chop *= 1f + V(PetStat.WoodDamage);
        }

        /// <summary>Battle cry (on a kill, +damage for 5 s): set by PetSkills.</summary>
        public float BattleCryUntil;

        // Percentage resistances: vanilla's m_mods only knows Resistant / Weak steps, so the pet scales the incoming hit.
        public override void OnDamaged(HitData hit, Character attacker)
        {
            base.OnDamaged(hit, attacker);
            if (hit == null) return;
            ref var d = ref hit.m_damage;
            d.m_fire *= 1f - Mathf.Clamp01(V(PetStat.ResistFire));
            d.m_frost *= 1f - Mathf.Clamp01(V(PetStat.ResistFrost));
            d.m_poison *= 1f - Mathf.Clamp01(V(PetStat.ResistPoison));
            d.m_spirit *= 1f - Mathf.Clamp01(V(PetStat.ResistSpirit));
            float phys = 1f - Mathf.Clamp01(V(PetStat.ResistPhysical));
            d.m_blunt *= phys * (1f - Mathf.Clamp01(V(PetStat.ResistBlunt) + Skill("thick_bark")));
            d.m_pierce *= phys * (1f - Mathf.Clamp01(V(PetStat.ResistPierce) + Skill("chitin")));
            d.m_slash *= phys;
            if (hit.m_hitType == HitData.HitType.AshlandsLava) d.Modify(1f - Mathf.Clamp01(Skill("lava_born")));
        }

        public override void ModifyNoise(float baseNoise, ref float noise)
        {
            base.ModifyNoise(baseNoise, ref noise);
            noise *= 1f - Mathf.Clamp01(V(PetStat.Noise) + (Night() ? V(PetStat.NoiseNight) : 0f));
        }

        public override void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)
        {
            base.ModifyRunStaminaDrain(baseDrain, ref drain, dir);
            drain *= 1f - Mathf.Clamp01(V(PetStat.RunStaminaUse));
        }

        public override void ModifySwimStaminaUsage(float baseStaminaUse, ref float staminaUse)
        {
            base.ModifySwimStaminaUsage(baseStaminaUse, ref staminaUse);
            staminaUse *= 1f - Mathf.Clamp01(V(PetStat.SwimStaminaUse) + Skill("deep_breath"));
        }

        public override void ModifyFallDamage(float baseDamage, ref float damage)
        {
            base.ModifyFallDamage(baseDamage, ref damage);
            damage *= 1f - Mathf.Clamp01(V(PetStat.FallDamage) + Skill("light_step") + Skill("squish"));
        }

        public override void ModifyStagger(float baseValue, ref float use)
        {
            base.ModifyStagger(baseValue, ref use);
            use *= 1f - Mathf.Clamp01(V(PetStat.Stagger) + Skill("stubborn") + Skill("hulk"));
        }

        public override void ModifyRaiseSkill(Skills.SkillType skill, ref float value)
        {
            base.ModifyRaiseSkill(skill, ref value);
            value *= 1f + V(PetStat.SkillXp);
        }

        public override string GetTooltipString() => PetText.BuffSummary(this);
    }
}
