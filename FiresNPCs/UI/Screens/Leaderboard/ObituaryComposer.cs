using System;
using System.Text;

namespace FiresRPGmaker.UI.Leaderboard
{
    /// <summary>
    /// Client-side composer for the memorial obituary shown on a tombstone's leaderboard card. Turns a player's
    /// OVERALL (all-time) stats into a short Norse-flavoured eulogy. Pure + deterministic (no randomness, so it
    /// doesn't flicker across rebuilds); flavour is chosen by stat thresholds. Stat strings (skills, favourite
    /// weapon) are passed in pre-resolved by the GUI, which owns those lookups.
    /// </summary>
    internal static class ObituaryComposer
    {
        public static string Compose(FiresLeaderboardRanking player, long firstJoinedTicks,
                                     int achievementsEarned, int achievementsTotal, string skillsText, string favoriteWeapon)
        {
            string name = string.IsNullOrEmpty(player?.PlayerName) ? "this Viking" : player.PlayerName;

            var sb = new StringBuilder();
            sb.Append("<b>Here lies ").Append(name).Append("</b>\n");

            string firstSeen = FormatFirstSeen(firstJoinedTicks);
            if (firstSeen != null)
                sb.Append("<color=#9a8d6e>First walked these shores ").Append(firstSeen).Append(".</color>\n\n");
            else
                sb.Append('\n');

            // opening line keyed on deaths (the Viking's relationship with Hel).
            int deaths = player != null ? player.Deaths : 0;
            sb.Append(DeathLine(name, deaths)).Append(' ');

            // monsters slain.
            int monsters = player != null ? player.Monsters : 0;
            sb.Append(MonsterLine(monsters)).Append(' ');

            // PvP.
            int pvp = player != null ? player.PlayersKilled : 0;
            if (pvp > 0)
                sb.Append(PvpLine(pvp)).Append(' ');

            sb.Append('\n');

            // favourite weapon.
            if (!string.IsNullOrWhiteSpace(favoriteWeapon) && !LooksPending(favoriteWeapon))
                sb.Append("Their hand favoured the <b>").Append(favoriteWeapon.Trim()).Append("</b>. ");

            // exploration.
            float explored = player != null ? player.MapExplored : 0f;
            sb.Append(ExploreLine(explored)).Append(' ');

            // top skills (the passed string is already a short list).
            if (!string.IsNullOrWhiteSpace(skillsText) && !LooksPending(skillsText))
            {
                string oneLine = skillsText.Replace("\n", ", ").Trim().TrimEnd(',');
                sb.Append("Renowned in ").Append(oneLine).Append(". ");
            }

            sb.Append('\n');

            // deeds / achievements.
            if (achievementsTotal > 0)
                sb.Append("Of ").Append(achievementsTotal).Append(" great deeds, ")
                  .Append(name).Append(" accomplished <b>").Append(achievementsEarned).Append("</b>. ");

            // closing — score-flavoured send-off to the halls.
            int score = player != null ? player.Score : 0;
            sb.Append('\n').Append(ClosingLine(name, score));

            return sb.ToString();
        }

        private static string FormatFirstSeen(long ticks)
        {
            if (ticks <= 0L) return null;
            try
            {
                var dt = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                return dt.ToString("d MMMM yyyy");
            }
            catch { return null; }
        }

        private static string DeathLine(string name, int deaths)
        {
            if (deaths <= 0) return $"In all their days {name} never once fell — a saga the skalds will doubt.";
            if (deaths == 1) return $"{name} tasted Hel's cold but once, and rose again.";
            if (deaths <= 5) return $"Felled {deaths} times, {name} clawed back from the mists each time.";
            if (deaths <= 20) return $"{deaths} deaths could not keep {name} from the fight.";
            return $"Hel knew {name} well — {deaths} times they crossed her threshold, and {deaths} times returned.";
        }

        private static string MonsterLine(int monsters)
        {
            if (monsters <= 0) return "No beast's blood stained their blade — a quiet life by the longhouse fire.";
            if (monsters < 50) return $"They laid low {monsters} foul creatures of the wild.";
            if (monsters < 500) return $"By their hand fell {monsters} beasts — greylings, draugr, and worse.";
            if (monsters < 2000) return $"A slayer of {monsters} monsters; the forests grew quiet at their step.";
            return $"{monsters} creatures met their end on this Viking's steel — a tally worthy of Valhalla itself.";
        }

        private static string PvpLine(int pvp)
        {
            if (pvp == 1) return "One rival Viking they sent to the halls before them.";
            return $"{pvp} fellow Vikings learned to fear their name.";
        }

        private static string ExploreLine(float explored01)
        {
            float pct = explored01 * 100f;
            if (pct < 1f) return "They roamed little, content to defend their own hearth.";
            if (pct < 10f) return $"They charted {pct:0.#}% of these lands.";
            if (pct < 40f) return $"A wanderer who mapped {pct:0.#}% of the realm.";
            return $"Far and wide they sailed — {pct:0.#}% of the world bowed to their gaze.";
        }

        private static string ClosingLine(string name, int score)
        {
            if (score <= 0) return "May the mead never run dry in the hall that keeps them.";
            if (score < 1000) return $"The horns sound for {name} in the lesser halls. Rest well.";
            if (score < 10000) return $"Odin's ravens carried word of {name}. A seat by the fire awaits.";
            return $"Among the mightiest, {name} feasts now in Valhalla. Skål.";
        }

        // The detail card uses "...pending..." / "No skills synced yet." placeholders before data syncs; don't
        // weave those into prose.
        private static bool LooksPending(string s)
        {
            string l = s.ToLowerInvariant();
            return l.Contains("pending") || l.Contains("no skills") || l.Contains("not synced") || l.Contains("unknown");
        }
    }
}
