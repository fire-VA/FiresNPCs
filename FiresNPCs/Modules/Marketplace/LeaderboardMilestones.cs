using System;
using System.Collections.Generic;
using System.Linq;
using Entry = FiresCore.Storage.LeaderboardEntry;

namespace FiresRPGmaker.Modules.Marketplace
{
    // NOTE: this namespace also defines a legacy Marketplace `LeaderboardEntry` that shadows the Core one, and a
    // same-namespace type outranks a using-alias for the bare name — so we alias the Core per-world entry as
    // `Entry` (unambiguous) and use that throughout. The Core entry is the one carrying KilledCreatures /
    // BiomesReached / Tamed that the milestones read.

    /// <summary>One Adventure-timeline milestone: a stable id, a display label, an optional illustrative vanilla
    /// prefab (its inventory icon fills the slot), and a predicate over a Core <see cref="Entry"/>.</summary>
    public sealed class MilestoneDef
    {
        public string Id;
        public string Label;
        public string Icon;   // optional vanilla prefab whose sprite illustrates the slot (blank = no icon)
        public Func<Entry, bool> Achieved;
    }

    /// <summary>
    /// The 16-slot Adventure-timeline catalog for the leaderboard Progression view — a rough Valheim progression
    /// arc (biome → boss). Boss kills and "firsts" are DERIVED from the entry's existing counters (so they count
    /// retroactively for characters that already did them); biome-reached and first-tame read the capture fields
    /// added to the Core entry. The SERVER computes the achieved-id list per entry into the board snapshot; the
    /// CLIENT renders slot labels + completed state from this same catalog.
    /// </summary>
    public static class LeaderboardMilestones
    {
        public static readonly List<MilestoneDef> All = new List<MilestoneDef>
        {
            Def("first_build",       "First structure", "Hammer",                e => Sum(e.BuiltStructures) > 0),
            Def("first_craft",       "First craft",     "Club",                e => Sum(e.ItemsCrafted) > 0),
            Def("biome_blackforest", "Black Forest",    "TrophyGreydwarf",                e => Biome(e, "BlackForest")),
            Def("boss_eikthyr",      "Eikthyr",         "TrophyEikthyr",     e => Killed(e, "eikthyr")),
            Def("biome_swamp",       "Swamp",           "TrophyDraugr",                e => Biome(e, "Swamp")),
            Def("boss_elder",        "The Elder",       "TrophyTheElder",    e => Killed(e, "gd_king", "elder")),
            Def("boss_bonemass",     "Bonemass",        "TrophyBonemass",    e => Killed(e, "bonemass")),
            Def("biome_mountain",    "Mountains",       "TrophyWolf",                e => Biome(e, "Mountain")),
            Def("boss_moder",        "Moder",           "TrophyDragonQueen", e => Killed(e, "dragon", "moder")),
            Def("biome_plains",      "Plains",          "TrophyGoblin",                e => Biome(e, "Plains")),
            Def("boss_yagluth",      "Yagluth",         "TrophyGoblinKing",  e => Killed(e, "goblinking", "yagluth")),
            Def("biome_mistlands",   "Mistlands",       "TrophySeeker",                e => Biome(e, "Mistlands")),
            Def("boss_queen",        "The Queen",       "TrophySeekerQueen", e => Killed(e, "seekerqueen")),
            Def("biome_ashlands",    "Ashlands",        "TrophyCharredMelee",                e => Biome(e, "AshLands")),
            Def("boss_fader",        "Fader",           "TrophyFader",                e => Killed(e, "fader")),
            Def("first_tame",        "First tame",      "TrophyBoar",                e => e.Tamed > 0),
        };

        private static readonly Dictionary<string, MilestoneDef> ById =
            All.ToDictionary(m => m.Id, StringComparer.Ordinal);

        /// <summary>Catalog entry by id, or null.</summary>
        public static MilestoneDef Get(string id) =>
            !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out var d) ? d : null;

        /// <summary>Server-side: the milestone ids this entry has achieved, in catalog order.</summary>
        public static List<string> Achieved(Entry entry)
        {
            var list = new List<string>();
            if (entry == null) return list;
            foreach (var m in All)
                try { if (m.Achieved(entry)) list.Add(m.Id); } catch { }
            return list;
        }

        private static MilestoneDef Def(string id, string label, string icon, Func<Entry, bool> pred)
            => new MilestoneDef { Id = id, Label = label, Icon = icon, Achieved = pred };

        private static int Sum(Dictionary<string, int> d)
        {
            if (d == null) return 0;
            int s = 0; foreach (var v in d.Values) s += v; return s;
        }

        private static bool Biome(Entry e, string biome)
            => e.BiomesReached != null && e.BiomesReached.ContainsKey(biome);

        // Tolerant boss-kill match: the kill feed's creature key may be the prefab name or a localized token, so
        // match case-insensitively on any given fragment. A boss that doesn't match just renders as locked.
        private static bool Killed(Entry e, params string[] tokens)
        {
            if (e?.KilledCreatures == null) return false;
            foreach (var kv in e.KilledCreatures)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                string k = kv.Key.ToLowerInvariant();
                foreach (var t in tokens) if (k.Contains(t)) return true;
            }
            return false;
        }
    }
}
