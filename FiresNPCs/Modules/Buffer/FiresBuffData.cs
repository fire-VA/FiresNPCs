using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Which application backend a buff uses.</summary>
    public enum BuffBackend
    {
        /// <summary>Synthetic stat buff — KG-compatible WhatToModify modifiers applied via a vanilla SE_Stats clone.</summary>
        Synthetic = 0,
        /// <summary>References an existing vanilla/modded StatusEffect by name; cloned + retimed + scaled at apply.</summary>
        TargetSE = 1,
    }

    /// <summary>KG-Marketplace-compatible stat-modifier flags (Buffer_DataTypes.WhatToModify, Buffer_DataTypes.cs:24-38).
    /// Bit values MUST match KG exactly so a drop-in Configs/Buffers/*.cfg modifier line parses identically.</summary>
    [Flags]
    public enum WhatToModify
    {
        None = 0,
        ModifyAttack = 1,
        ModifyHealthRegen = 2,
        ModifyStaminaRegen = 4,
        ModifyRaiseSkills = 8,
        ModifySpeed = 16,
        ModifyNoise = 32,
        ModifyMaxCarryWeight = 64,
        ModifyStealth = 128,
        RunStaminaDrain = 256,
        DamageReduction = 512,
    }

    /// <summary>One buff definition. KG-compatible fields (a KG record parses as a Synthetic buff with the
    /// extensions at their defaults) plus Fires extensions: an arbitrary-StatusEffect backend and an
    /// admin strength ceiling. The player's strength dial is 0..1 of the authored value, capped by StrengthCap.</summary>
    public class FiresBuffData
    {
        // ── shared / KG-compatible ────────────────────────────────────────────
        public string UniqueName;                 // identity (KG case-sensitive; we resolve case-insensitively)
        public string Name = "";                   // display name (interior spaces preserved)
        public int Duration;                       // seconds — the authored / max duration
        public string SpritePrefab = "";           // icon prefab indirection
        public string NeededPrefab = "";           // cost item prefab
        public int NeededPrefabCount;              // cost count
        public string StartEffectPrefab = "";      // VFX prefab spawned on apply
        public string BuffGroup = "";              // mutual-exclusion key (no two active in the same non-empty group)

        // ── backend A: synthetic SE_Stats modifiers (KG WhatToModify) ─────────
        public WhatToModify Flags = WhatToModify.None;
        public Dictionary<WhatToModify, float> ModifyList = DefaultModifyList();

        // ── backend B: arbitrary status effect ────────────────────────────────
        public BuffBackend Backend = BuffBackend.Synthetic;
        public string TargetSE = "";               // existing StatusEffect .name (ObjectDB) — presence sets Backend=TargetSE

        // ── Fires extensions ─────────────────────────────────────────────────
        public float StrengthCap = 1f;             // 0..1 ceiling on the player's strength dial (1 = full authored value)
        public int DurationCap;                    // <= cap; defaults to Duration when 0
        public string CostScales = "none";         // none | duration | strength | both

        public int EffectiveDurationCap => DurationCap > 0 ? DurationCap : Duration;

        /// <summary>KG-exact default modifier seeding (Buffer_DataTypes.cs:45-87): multiplicative knobs default
        /// 1, additive MaxCarryWeight + DamageReduction default 0. Only flags present in the cfg deviate.</summary>
        public static Dictionary<WhatToModify, float> DefaultModifyList() => new Dictionary<WhatToModify, float>
        {
            { WhatToModify.ModifyAttack, 1f },
            { WhatToModify.ModifyHealthRegen, 1f },
            { WhatToModify.ModifyStaminaRegen, 1f },
            { WhatToModify.ModifyRaiseSkills, 1f },
            { WhatToModify.ModifySpeed, 1f },
            { WhatToModify.ModifyNoise, 1f },
            { WhatToModify.ModifyMaxCarryWeight, 0f },
            { WhatToModify.ModifyStealth, 1f },
            { WhatToModify.RunStaminaDrain, 1f },
            { WhatToModify.DamageReduction, 0f },
        };
    }
}
