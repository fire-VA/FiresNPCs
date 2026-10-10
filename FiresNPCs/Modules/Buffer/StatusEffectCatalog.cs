using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Enumerates every status effect the game knows about — vanilla AND modded — from the single
    /// authoritative registry <c>ObjectDB.instance.m_StatusEffects</c> (mods Add to this exact list, so a scan
    /// captures them). Identity is the ScriptableObject <c>.name</c> hash (StatusEffect.NameHash), NOT m_name.
    /// Scan LAZILY (on demand) so SEs other plugins register after ObjectDB.Awake are included.</summary>
    public static class StatusEffectCatalog
    {
        /// <summary>How much of a status effect's strength we can actually scale. Honest, type-derived.</summary>
        public enum StrengthBadge
        {
            Full,       // SE_Stats — public modifier fields, fully scalable
            Partial,    // SE_Shield / SE_Burning / SE_Poison — scalable via their setters
            TimeOnly,   // opaque (private fields, no setter) — only duration is adjustable
        }

        public static List<StatusEffect> All()
        {
            var list = new List<StatusEffect>();
            var db = ObjectDB.instance;
            if (db?.m_StatusEffects == null) return list;
            foreach (var se in db.m_StatusEffects)
                if (se != null) list.Add(se);
            return list;
        }

        /// <summary>Resolves a status effect by its <c>.name</c> (exact hash first, then case-insensitive over
        /// .name and m_name). Returns the SHARED asset — clone before mutating.</summary>
        public static StatusEffect Resolve(string name)
        {
            var db = ObjectDB.instance;
            if (string.IsNullOrEmpty(name) || db?.m_StatusEffects == null) return null;

            var byHash = db.GetStatusEffect(name.GetStableHashCode());
            if (byHash != null) return byHash;

            foreach (var se in db.m_StatusEffects)
            {
                if (se == null) continue;
                if (string.Equals(se.name, name, StringComparison.OrdinalIgnoreCase)) return se;
                if (!string.IsNullOrEmpty(se.m_name) && string.Equals(se.m_name, name, StringComparison.OrdinalIgnoreCase)) return se;
            }
            return null;
        }

        public static StrengthBadge Badge(StatusEffect se)
        {
            if (se is SE_Stats) return StrengthBadge.Full;
            string t = se?.GetType().Name ?? "";
            if (t == "SE_Shield" || t == "SE_Burning" || t == "SE_Poison") return StrengthBadge.Partial;
            return StrengthBadge.TimeOnly;
        }
    }
}
