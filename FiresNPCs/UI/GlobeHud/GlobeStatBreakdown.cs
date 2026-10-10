using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Tooltip text for a globe: the vanilla parts of its maximum (base, each food, trinkets for adrenaline), then
    /// everything beyond vanilla: a raised base, WackysDatabase status-effect bonuses by name, and whatever other mods
    /// add that nothing here can attribute.
    /// </summary>
    internal static class GlobeStatBreakdown
    {
        private const float VanillaBaseHealth = 25f;
        private const float VanillaBaseStamina = 50f;
        private const float VanillaBaseAdrenaline = 0f;
        private const float NegligibleAmount = 0.5f;
        private const float SecondsPerMinute = 60f;
        private const string ValueColumn = "<pos=72%>";
        private const string Indent = "   ";
        private const string VanillaColor = "#d8cfbd";
        private const string BeyondColor = "#ffd36a";
        private const string GainColor = "#9ceb6b";
        private const string LossColor = "#ff7a6b";
        private const string MutedColor = "#9a948a";

        private readonly struct Part
        {
            public readonly string Label;
            public readonly float Amount;

            public Part(string label, float amount)
            {
                Label = label;
                Amount = amount;
            }
        }

        public static string Topic(GlobeResource resource)
        {
            switch (resource)
            {
                case GlobeResource.Health: return "Health";
                case GlobeResource.Stamina: return "Stamina";
                case GlobeResource.Eitr: return "Eitr";
                default: return "Adrenaline";
            }
        }

        public static string Describe(GlobeResource resource, Player player)
        {
            var vanilla = new List<Part>();
            var beyond = new List<Part>();
            float current;
            float max;

            if (resource == GlobeResource.Adrenaline)
            {
                current = player.GetAdrenaline();
                max = player.GetMaxAdrenaline();
                AddBase(vanilla, beyond, player.m_maxAdrenaline, VanillaBaseAdrenaline);
                foreach (var item in player.GetInventory().GetEquippedItems())
                {
                    if (Mathf.Abs(item.m_shared.m_maxAdrenaline) >= NegligibleAmount)
                        vanilla.Add(new Part(Localize(item.m_shared.m_name), item.m_shared.m_maxAdrenaline));
                }
            }
            else
            {
                ReadPool(resource, player, out current, out max, out float baseAmount, out float vanillaBase);
                AddBase(vanilla, beyond, baseAmount, vanillaBase);
                foreach (var food in player.GetFoods())
                {
                    float amount = FoodAmount(resource, food);
                    if (amount >= NegligibleAmount)
                        vanilla.Add(new Part(Localize(food.m_item.m_shared.m_name) + " (" + Mathf.CeilToInt(food.m_time / SecondsPerMinute) + "m)", amount));
                }
                foreach (var bonus in WackyStatusEffectBonuses.For(player, resource))
                    beyond.Add(new Part(Localize(bonus.Key.m_name, bonus.Key.name), bonus.Value));
            }

            float explained = Sum(vanilla) + Sum(beyond);
            float unexplained = max - explained;
            if (Mathf.Abs(unexplained) >= NegligibleAmount)
                beyond.Add(new Part("Other mods", unexplained));

            return Compose(resource, current, max, vanilla, beyond);
        }

        private static void ReadPool(GlobeResource resource, Player player, out float current, out float max, out float baseAmount, out float vanillaBase)
        {
            switch (resource)
            {
                case GlobeResource.Health:
                    current = player.GetHealth();
                    max = player.GetMaxHealth();
                    baseAmount = player.m_baseHP;
                    vanillaBase = VanillaBaseHealth;
                    return;
                case GlobeResource.Stamina:
                    current = player.GetStamina();
                    max = player.GetMaxStamina();
                    baseAmount = player.m_baseStamina;
                    vanillaBase = VanillaBaseStamina;
                    return;
                default:
                    current = player.GetEitr();
                    max = player.GetMaxEitr();
                    baseAmount = 0f;
                    vanillaBase = 0f;
                    return;
            }
        }

        private static float FoodAmount(GlobeResource resource, Player.Food food)
        {
            switch (resource)
            {
                case GlobeResource.Health: return food.m_health;
                case GlobeResource.Stamina: return food.m_stamina;
                default: return food.m_eitr;
            }
        }

        private static void AddBase(List<Part> vanilla, List<Part> beyond, float baseAmount, float vanillaBase)
        {
            if (vanillaBase >= NegligibleAmount) vanilla.Add(new Part("Base", vanillaBase));
            if (Mathf.Abs(baseAmount - vanillaBase) >= NegligibleAmount) beyond.Add(new Part("Base raised by a mod", baseAmount - vanillaBase));
        }

        private static string Compose(GlobeResource resource, float current, float max, List<Part> vanilla, List<Part> beyond)
        {
            var text = new StringBuilder();
            text.Append("<size=120%><b>").Append(Mathf.CeilToInt(current)).Append("</b> / ").Append(Mathf.RoundToInt(max)).Append("</size>\n");

            if (max < NegligibleAmount)
            {
                text.Append(Colored(MutedColor, resource == GlobeResource.Adrenaline
                    ? "No adrenaline: equip a trinket to hold some."
                    : "Nothing is providing any right now."));
                return text.ToString();
            }

            text.Append(Colored(VanillaColor, "Vanilla" + ValueColumn + Mathf.RoundToInt(Sum(vanilla)))).Append('\n');
            foreach (var part in vanilla)
                text.Append(Indent).Append(part.Label).Append(ValueColumn).Append(Mathf.RoundToInt(part.Amount)).Append('\n');

            if (beyond.Count == 0)
            {
                text.Append(Colored(MutedColor, "Nothing beyond vanilla"));
                return text.ToString();
            }

            text.Append(Colored(BeyondColor, "Beyond vanilla" + ValueColumn + Signed(Sum(beyond)))).Append('\n');
            foreach (var part in beyond)
                text.Append(Indent).Append(part.Label).Append(ValueColumn)
                    .Append(Colored(part.Amount >= 0f ? GainColor : LossColor, Signed(part.Amount))).Append('\n');
            return text.ToString().TrimEnd('\n');
        }

        private static float Sum(List<Part> parts)
        {
            float total = 0f;
            foreach (var part in parts) total += part.Amount;
            return total;
        }

        private static string Signed(float amount) => (amount >= 0f ? "+" : "") + Mathf.RoundToInt(amount);
        private static string Colored(string color, string text) => "<color=" + color + ">" + text + "</color>";

        private static string Localize(string token, string fallback = null)
        {
            if (string.IsNullOrEmpty(token)) return fallback ?? "";
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        /// <summary>
        /// WackysDatabase raises max health/stamina/eitr per status effect through its own GetTotalFoodValue postfix,
        /// keyed by the effect's name in WMRecipeCust.SEaddBonus. Read that same table so each bonus can be named.
        /// </summary>
        private static class WackyStatusEffectBonuses
        {
            private const string TableType = "wackydatabase.WMRecipeCust";
            private const string TableField = "SEaddBonus";

            private static bool _resolved;
            private static IDictionary _table;
            private static FieldInfo _addHealth;
            private static FieldInfo _addStamina;
            private static FieldInfo _addEitr;

            public static IEnumerable<KeyValuePair<StatusEffect, float>> For(Player player, GlobeResource resource)
            {
                Resolve();
                if (_table == null || _table.Count == 0) yield break;
                var field = resource == GlobeResource.Health ? _addHealth : resource == GlobeResource.Stamina ? _addStamina : _addEitr;
                if (field == null) yield break;
                foreach (var effect in player.GetSEMan().GetStatusEffects())
                {
                    if (effect == null || !_table.Contains(effect.name)) continue;
                    var amount = (float?)field.GetValue(_table[effect.name]);
                    if (amount.HasValue && Mathf.Abs(amount.Value) >= NegligibleAmount)
                        yield return new KeyValuePair<StatusEffect, float>(effect, amount.Value);
                }
            }

            private static void Resolve()
            {
                if (_resolved) return;
                _resolved = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var tableType = assembly.GetType(TableType, false);
                    if (tableType == null) continue;
                    _table = tableType.GetField(TableField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IDictionary;
                    var bonusType = _table?.GetType().GetGenericArguments()[1];
                    _addHealth = bonusType?.GetField("AddHP");
                    _addStamina = bonusType?.GetField("AddStamina");
                    _addEitr = bonusType?.GetField("AddEitr");
                    return;
                }
            }
        }
    }
}
