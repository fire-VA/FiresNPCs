using System;
using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;

namespace FiresRPGmaker.Modules.Guilds.Achievements
{
    /// <summary>
    /// Tracks and evaluates guild achievements.
    ///
    /// DESIGN
    /// ------
    /// Achievement progress is stored per-guild inside
    /// <see cref="GuildData"/> (as a JSON-serialised List stored alongside
    /// the guild in the world file).  The server is the authority; clients
    /// receive updates via the normal FullSync.
    ///
    /// To register a new achievement, call
    /// <see cref="Register"/> early in the mod lifecycle (e.g. from GuildModule.Initialize).
    /// Progress is incremented via <see cref="IncrementProgress"/>.
    ///
    /// Built-in achievements (registered by GuildModule):
    ///   "guild_members_5"     — Recruit 5 members
    ///   "guild_members_10"    — Reach 10 members
    ///   "guild_members_25"    — Reach 25 members
    /// </summary>
    public static class GuildAchievements
    {
        // ── Achievement registry ──────────────────────────────────────────────

        private static readonly Dictionary<string, GuildAchievementDef> _defs =
            new Dictionary<string, GuildAchievementDef>();

        /// <summary>Fired when a guild earns a new achievement tier.</summary>
        public static event Action<GuildData, GuildAchievementDef, int> OnAchievementEarned;

        public static void Register(GuildAchievementDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return;
            _defs[def.Id] = def;
        }

        public static IReadOnlyDictionary<string, GuildAchievementDef> GetAll() => _defs;

        // ── Default achievements ───────────────────────────────────────────────

        public static void RegisterDefaults()
        {
            Register(new GuildAchievementDef
            {
                Id = "members", Name = "Growing Guild",
                Description = "Recruit members to your guild.",
                Thresholds  = new List<float> { 5, 10, 25, 50 },
            });
            Register(new GuildAchievementDef
            {
                Id = "age_days", Name = "Established",
                Description = "Keep your guild running.",
                Thresholds  = new List<float> { 7, 30, 90 },
            });
        }

        // ── Progress tracking ─────────────────────────────────────────────────

        /// <summary>
        /// Increments progress for <paramref name="achievementId"/> on the guild
        /// that owns <paramref name="guild"/> and evaluates tier completion.
        /// Call this only on the server.
        /// </summary>
        public static void IncrementProgress(GuildData guild, string achievementId, float amount = 1f)
        {
            if (guild == null || !_defs.TryGetValue(achievementId, out var def)) return;

            var prog = GetOrCreateProgress(guild, achievementId);
            prog.Progress += amount;
            EvaluateTiers(guild, def, prog);
        }

        /// <summary>Checks time-based achievements (age).</summary>
        public static void EvaluatePeriodic(GuildData guild)
        {
            if (guild == null) return;

            if (_defs.TryGetValue("members", out var memberDef))
            {
                var prog = GetOrCreateProgress(guild, "members");
                prog.Progress = guild.Members.Count;
                EvaluateTiers(guild, memberDef, prog);
            }

            if (_defs.TryGetValue("age_days", out var ageDef))
            {
                var prog = GetOrCreateProgress(guild, "age_days");
                prog.Progress = (float)(DateTime.UtcNow - new DateTime(guild.CreatedUtcTicks)).TotalDays;
                EvaluateTiers(guild, ageDef, prog);
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static GuildAchievementProgress GetOrCreateProgress(GuildData guild, string id)
        {
            // Progress is stored in a transient list on GuildData; extend GuildData
            // with an Achievements field when wiring this fully. For now, use a
            // module-local dictionary keyed by guildId+achievementId.
            var key = $"{guild.GuildId}|{id}";
            if (!_progress.TryGetValue(key, out var p))
            {
                p = new GuildAchievementProgress { AchievementId = id };
                _progress[key] = p;
            }
            return p;
        }

        private static readonly Dictionary<string, GuildAchievementProgress> _progress =
            new Dictionary<string, GuildAchievementProgress>();

        private static void EvaluateTiers(GuildData guild, GuildAchievementDef def, GuildAchievementProgress prog)
        {
            for (int i = prog.TierReached; i < def.Thresholds.Count; i++)
            {
                if (prog.Progress < def.Thresholds[i]) break;
                prog.TierReached = i + 1;
                prog.CompletedAtTicks.Add(DateTime.UtcNow.Ticks);

                Debug.Log($"[GuildAchievements] Guild '{guild.Name}' earned '{def.Name}' tier {i + 1}.");
                OnAchievementEarned?.Invoke(guild, def, i + 1);
            }
        }
    }
}
