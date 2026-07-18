using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// One faction definition — the Marketplace <c>Faction</c> shape (schema:
    /// Docs/marketplace-schemas/Factions.md). Item/building/monster/craft lists are stored as
    /// stable-hash int sets (matching MP's runtime form) so aggro/PvP/restriction checks are hash
    /// compares, not string compares. The map key (a lowercased faction id) is what a player stores
    /// as their membership; it is NOT this Name.
    /// </summary>
    public sealed class FactionData
    {
        public string Id;                 // lowercased map key = membership value
        public string Name;               // display name (required)
        public string Icon;               // optional icon prefab/sprite name
        public readonly HashSet<int> SpecificUseItems = new HashSet<int>();
        public readonly HashSet<int> BlockPickupItems = new HashSet<int>();
        public readonly HashSet<int> SpecificBuildings = new HashSet<int>();
        public readonly HashSet<int> SpecificCraftItems = new HashSet<int>();
        public readonly HashSet<int> FriendlyMonsters = new HashSet<int>();
        public readonly Dictionary<Skills.SkillType, int> BonusSkills = new Dictionary<Skills.SkillType, int>();
        public string Buff;               // optional status-effect UniqueName (lowercased)
        public bool CanDamageSameFaction = true;   // MP default: true

        // Original prefab NAMES kept beside the runtime hash sets so the editor can round-trip the YAML
        // (the hash is machine-stable but not reversible). Runtime checks use the hash sets; only the editor
        // reads/writes these. Kept in lockstep with the sets by the parser and the editor.
        public readonly List<string> UseNames = new List<string>();
        public readonly List<string> PickupNames = new List<string>();
        public readonly List<string> BuildNames = new List<string>();
        public readonly List<string> CraftNames = new List<string>();
        public readonly List<string> FriendlyNames = new List<string>();

        public bool HasAnyRestriction =>
            SpecificUseItems.Count + BlockPickupItems.Count + SpecificBuildings.Count + SpecificCraftItems.Count > 0;

        /// <summary>Deep copy for the editor's discardable working buffer.</summary>
        public FactionData Clone()
        {
            var c = new FactionData
            {
                Id = Id, Name = Name, Icon = Icon, Buff = Buff, CanDamageSameFaction = CanDamageSameFaction,
            };
            c.SpecificUseItems.UnionWith(SpecificUseItems);
            c.BlockPickupItems.UnionWith(BlockPickupItems);
            c.SpecificBuildings.UnionWith(SpecificBuildings);
            c.SpecificCraftItems.UnionWith(SpecificCraftItems);
            c.FriendlyMonsters.UnionWith(FriendlyMonsters);
            foreach (var kv in BonusSkills) c.BonusSkills[kv.Key] = kv.Value;
            c.UseNames.AddRange(UseNames);
            c.PickupNames.AddRange(PickupNames);
            c.BuildNames.AddRange(BuildNames);
            c.CraftNames.AddRange(CraftNames);
            c.FriendlyNames.AddRange(FriendlyNames);
            return c;
        }
    }
}
