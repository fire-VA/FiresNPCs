using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Builds a per-instance StatusEffect from a buff record + a duration + a 0..1 strength, and adds
    /// it to a player. Strength is "0% = no effect, 100% = the admin-authored value", so multiplicative knobs
    /// map as 1+(v-1)*s and additive/delta knobs as v*s — composing correctly with vanilla's stacking.
    ///
    /// NOTE: this is the owner-local apply (the player whose Character this SEMan belongs to). The
    /// server-authoritative grant flow (validate cost/cooldown/caps on the server, then instruct the owning
    /// client to call this) is layered on top — vanilla's SE RPC carries only a name hash and can't transport a
    /// custom ttl/strength, so the final AddStatusEffect must run on the owner.</summary>
    public static class BufferApplyEngine
    {
        public static bool ApplyToLocalPlayer(FiresBuffData b, int durationSecs, float strength01, out string msg)
        {
            msg = "";
            var player = Player.m_localPlayer;
            if (player == null) { msg = "no local player"; return false; }
            return Apply(player, b, durationSecs, strength01, out msg);
        }

        public static bool Apply(Character target, FiresBuffData b, int durationSecs, float strength01, out string msg)
        {
            msg = "";
            if (b == null) { msg = "null buff"; return false; }
            var seman = target?.GetSEMan();
            if (seman == null) { msg = "no SEMan"; return false; }

            float cap = b.StrengthCap <= 0f ? 1f : Mathf.Clamp01(b.StrengthCap);
            float s = Mathf.Clamp(strength01, 0f, cap);

            int dur = durationSecs > 0 ? durationSecs : b.Duration;
            if (b.EffectiveDurationCap > 0) dur = Mathf.Min(dur, b.EffectiveDurationCap);
            if (dur <= 0) dur = b.Duration > 0 ? b.Duration : 60;

            StatusEffect se;
            if (b.Backend == BuffBackend.TargetSE)
            {
                var shared = StatusEffectCatalog.Resolve(b.TargetSE);
                if (shared == null) { msg = $"status effect '{b.TargetSE}' not found in ObjectDB"; return false; }
                se = shared.Clone();                       // shallow clone — only set value-type fields below
                se.m_ttl = dur;
                if (se is SE_Stats st && s < 0.999f) ScaleSeStats(st, s);
                // (SE_Shield/SE_Burning/SE_Poison setter-based scaling is layered in a later pass; duration
                //  always applies, which is why opaque SEs are badged "Time-only".)
            }
            else
            {
                var fb = ScriptableObject.CreateInstance<FiresStatBuff>();
                fb.name = b.UniqueName;
                fb.m_name = string.IsNullOrEmpty(b.Name) ? b.UniqueName : b.Name;
                fb.m_ttl = dur;
                fb.m_icon = ResolveIcon(b.SpritePrefab);
                if (!string.IsNullOrEmpty(b.BuffGroup)) fb.m_category = "fbg_" + b.BuffGroup;   // prefixed → no vanilla collision
                ApplySyntheticModifiers(fb, b, s);
                se = fb;
            }

            // No-stack within a non-empty BuffGroup (synthetic buffs carry the prefixed category).
            if (!string.IsNullOrEmpty(b.BuffGroup) && b.Backend == BuffBackend.Synthetic &&
                seman.HaveStatusEffectCategory("fbg_" + b.BuffGroup) && !seman.HaveStatusEffect(se.NameHash()))
            {
                msg = $"already have a buff in group '{b.BuffGroup}'";
                return false;
            }

            // Re-applying the same buff: AddStatusEffect won't upgrade strength/duration in place (it only
            // refreshes time), so remove the active instance first to let the new values take.
            int hash = se.NameHash();
            if (seman.HaveStatusEffect(hash)) seman.RemoveStatusEffect(hash, true);

            var applied = seman.AddStatusEffect(se, true);
            if (applied == null) { msg = "apply failed (CanAdd false?)"; return false; }

            // AddStatusEffect clones the instance and runs Setup(), which re-seeds m_ttl from the asset for many
            // SEs (the reason a 5m override still expired at the effect's native 20s). Re-assert the override on
            // the LIVE instance. Magnitude scaling was applied pre-clone and copied across, so don't re-scale.
            applied.m_ttl = dur;
            applied.m_time = 0f;

            msg = $"applied '{applied.m_name}' for {dur}s at {Mathf.RoundToInt(s * 100f)}% strength";
            return true;
        }

        /// <summary>Maps KG WhatToModify values onto the SE_Stats fields, scaled by strength s. Unconfigured
        /// flags sit at their defaults (1 multiplicative / 0 additive), so the formulas are no-ops for them.</summary>
        private static void ApplySyntheticModifiers(FiresStatBuff fb, FiresBuffData b, float s)
        {
            var m = b.ModifyList;
            float Mul(WhatToModify w) => 1f + (Get(m, w, 1f) - 1f) * s;   // 1+(v-1)*s
            float Add(WhatToModify w) => Get(m, w, 0f) * s;               // v*s

            fb.m_modifyAttackSkill = Skills.SkillType.All;
            fb.m_damageModifier = Mul(WhatToModify.ModifyAttack);
            fb.m_healthRegenMultiplier = Mul(WhatToModify.ModifyHealthRegen);
            fb.m_staminaRegenMultiplier = Mul(WhatToModify.ModifyStaminaRegen);
            fb.m_speedModifier = (Get(m, WhatToModify.ModifySpeed, 1f) - 1f) * s;
            fb.m_noiseModifier = (Get(m, WhatToModify.ModifyNoise, 1f) - 1f) * s;
            fb.m_stealthModifier = (Get(m, WhatToModify.ModifyStealth, 1f) - 1f) * s;
            fb.m_raiseSkill = Skills.SkillType.All;
            fb.m_raiseSkillModifier = (Get(m, WhatToModify.ModifyRaiseSkills, 1f) - 1f) * s;
            float run = (Get(m, WhatToModify.RunStaminaDrain, 1f) - 1f) * s;
            fb.m_runStaminaDrainModifier = run;
            fb.m_jumpStaminaUseModifier = run;
            fb.m_addMaxCarryWeight = Add(WhatToModify.ModifyMaxCarryWeight);
            fb.DamageReduction = Mathf.Clamp01(Add(WhatToModify.DamageReduction));
        }

        private static float Get(System.Collections.Generic.Dictionary<WhatToModify, float> m, WhatToModify w, float def)
            => m != null && m.TryGetValue(w, out var v) ? v : def;

        /// <summary>Scales the common magnitude fields of an arbitrary SE_Stats clone by strength s. Multiplicative
        /// fields (default 1) scale as 1+(v-1)*s; additive/over-time magnitudes (default 0) scale as v*s. Timing
        /// fields (intervals, durations) are left alone. s==1 leaves the asset's authored values untouched.</summary>
        private static void ScaleSeStats(SE_Stats st, float s)
        {
            st.m_damageModifier = 1f + (st.m_damageModifier - 1f) * s;
            st.m_healthRegenMultiplier = 1f + (st.m_healthRegenMultiplier - 1f) * s;
            st.m_staminaRegenMultiplier = 1f + (st.m_staminaRegenMultiplier - 1f) * s;
            st.m_eitrRegenMultiplier = 1f + (st.m_eitrRegenMultiplier - 1f) * s;

            st.m_speedModifier *= s;
            st.m_swimSpeedModifier *= s;
            st.m_noiseModifier *= s;
            st.m_stealthModifier *= s;
            st.m_addMaxCarryWeight *= s;
            st.m_addArmor *= s;
            st.m_armorMultiplier *= s;
            st.m_raiseSkillModifier *= s;
            st.m_runStaminaDrainModifier *= s;
            st.m_jumpStaminaUseModifier *= s;
            st.m_attackStaminaUseModifier *= s;
            st.m_blockStaminaUseModifier *= s;
            st.m_healthUpFront *= s;
            st.m_healthOverTime *= s;
            st.m_healthPerTick *= s;
            st.m_staminaUpFront *= s;
            st.m_staminaOverTime *= s;
            st.m_eitrUpFront *= s;
            st.m_eitrOverTime *= s;
            st.m_adrenalineUpFront *= s;
            st.m_adrenalineModifier *= s;
            st.m_staggerModifier *= s;
            st.m_timedBlockBonus *= s;
            st.m_fallDamageModifier *= s;
        }

        private static Sprite ResolveIcon(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || ObjectDB.instance == null) return null;
            try
            {
                var go = ObjectDB.instance.GetItemPrefab(prefabName);
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop != null && drop.m_itemData != null) return drop.m_itemData.GetIcon();
            }
            catch { }
            return null;
        }
    }
}
