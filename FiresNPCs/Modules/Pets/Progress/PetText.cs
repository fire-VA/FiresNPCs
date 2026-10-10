using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>Words for the tooltip, the status bar and the Pet panel: buff lines now » next level, the skill line, stars.</summary>
    public static class PetText
    {
        public static string PetName(string creature)
        {
            var go = ZNetScene.instance != null && !string.IsNullOrEmpty(creature) ? ZNetScene.instance.GetPrefab(creature) : null;
            var c = go != null ? go.GetComponent<Character>() : null;
            string n = c != null ? c.m_name : creature ?? "";
            return Localization.instance != null ? Localization.instance.Localize(n) : n;
        }

        public static string Stars(int level) => new string('*', Mathf.Clamp(level, 0, 5)) + new string('-', 5 - Mathf.Clamp(level, 0, 5));

        public static bool IsPercent(PetStat s) =>
            !(s == PetStat.Carry || s == PetStat.Armor || s == PetStat.HealthRegen || s == PetStat.HealthRegenWater || s == PetStat.EitrRegen || s == PetStat.ExploreRadius);

        public static bool IsReduction(PetStat s) =>
            s == PetStat.ResistFire || s == PetStat.ResistFrost || s == PetStat.ResistPoison || s == PetStat.ResistSpirit || s == PetStat.ResistPhysical ||
            s == PetStat.ResistBlunt || s == PetStat.ResistPierce || s == PetStat.Noise || s == PetStat.NoiseNight || s == PetStat.RunStaminaUse ||
            s == PetStat.SwimStaminaUse || s == PetStat.FallDamage || s == PetStat.Stagger || s == PetStat.SlowResist;

        public static bool IsPhase2(PetStat s) => s == PetStat.FoodDuration || s == PetStat.ExploreRadius || s == PetStat.AttackSpeed || s == PetStat.SlowResist;

        public static string Value(PetStat s, float v)
        {
            if (IsPercent(s)) return (IsReduction(s) ? "-" : "+") + (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            return "+" + v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>"+6% stamina regen » +8% at level 4" lines for a creature at a level (the next-level half only below 5).</summary>
        public static List<string> BuffLines(string creature, int level, bool withNext)
        {
            var def = PetBuffTable.For(creature, PetFacts.Family(creature));
            float scale = PetProgress.Scale;
            var lines = new List<string>();
            foreach (var b in def.Buffs)
            {
                float now = b.At(level, scale), next = b.At(level + 1, scale);
                string soon = IsPhase2(b.Stat) ? " <color=#9a8a6a>(coming soon)</color>" : "";
                string line = (level <= 0 ? "<color=#9a8a6a>" + Value(b.Stat, next) + " " + b.Label + " from level 1</color>" : Value(b.Stat, now) + " " + b.Label) + soon;
                if (withNext && level > 0 && level < PetProgress.MaxLevel && next > now + 0.0001f)
                    line += " <color=#7fbf5a>» " + Value(b.Stat, next) + (level + 1 < PetProgress.MaxLevel ? "" : "") + " at level " + (level + 1) + "</color>";
                lines.Add(line);
            }
            return lines;
        }

        /// <summary>"Flyer: Light wing, slower fall while holding Jump (level 5)" style.</summary>
        public static string SkillLine(string creature, int level)
        {
            var def = PetBuffTable.For(creature, PetFacts.Family(creature));
            var s = def.Skill;
            if (s == null) return "";
            float v = s.At(Mathf.Max(level, s.MinLevel));
            string text = s.Text.Contains("{0}") ? string.Format(s.Text, Fmt(s, v)) : s.Text;
            string tail = s.Phase == 2 ? " <color=#9a8a6a>(coming soon)</color>"
                        : level < s.MinLevel ? " <color=#9a8a6a>(from level " + s.MinLevel + ")</color>" : "";
            return "<b>" + s.Name + "</b>: " + text + tail;
        }

        private static string Fmt(PetSkill s, float v) => v < 1f ? (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%" : v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>The status-bar / SE tooltip text.</summary>
        public static string BuffSummary(SE_FiresPet se)
        {
            var sb = new StringBuilder();
            sb.Append(PetName(se.Creature)).Append(" · level ").Append(se.PetLevel);
            foreach (var l in BuffLines(se.Creature, se.PetLevel, false)) sb.Append('\n').Append(l);
            string skill = SkillLine(se.Creature, se.PetLevel);
            if (skill.Length > 0) sb.Append('\n').Append(skill);
            return sb.ToString();
        }

        /// <summary>The pet item tooltip block (family, tier, stars, XP line, buffs now » next, skill, the hint).</summary>
        public static string ItemTooltip(ItemDrop.ItemData item)
        {
            string creature = PetCatalog.CreatureOf(item);
            if (string.IsNullOrEmpty(creature)) return "";
            int level = PetProgress.Level(item), xp = PetProgress.Xp(item);
            var sb = new StringBuilder();
            sb.Append("\n<color=orange>").Append(Stars(level)).Append("</color>  Pet · ").Append(PetFacts.Family(creature))
              .Append(" · Tier ").Append(PetFacts.Tier(creature)).Append(" · Level ").Append(level).Append('/').Append(PetProgress.MaxLevel);
            if (level == 0) sb.Append("\n<color=#9a8a6a>Un-upgraded: looks only. Feed it and level it up for a buff and a skill.</color>");
            foreach (var l in BuffLines(creature, level, true)) sb.Append('\n').Append(l);
            string skill = SkillLine(creature, level);
            if (skill.Length > 0) sb.Append('\n').Append(skill);
            if (level < PetProgress.MaxLevel) sb.Append("\nXP ").Append(xp).Append(" / ").Append(PetProgress.XpFor(level));
            else sb.Append("\n<color=orange>Ascended</color>");
            sb.Append("\n<color=#9a8a6a>Equip in a Pet slot (you or a companion). Right-click the slot » Pet to feed and level it.</color>");
            return sb.ToString();
        }
    }
}
