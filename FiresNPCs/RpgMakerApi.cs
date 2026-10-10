using System;
using UnityEngine;

namespace FiresRPGmaker
{
    /// <summary>
    /// Public cross-mod API for the RPG Maker (FiresNPCs) quest / economy system — the
    /// drop-in replacement for KG Marketplace's quest hooks.
    ///
    /// External mods (e.g. VikingLands.Core's leaderboard / groups / boss compat) subscribe
    /// to these events instead of reflecting into Marketplace internals. Consumers that
    /// can't take a hard reference resolve this type by name
    /// (<c>FiresRPGmaker.RpgMakerApi</c>), confirm <see cref="IsAvailable"/>, then add their
    /// handler to the relevant event via reflection — the event signatures use only
    /// framework types (<see cref="Action{T1,T2,T3}"/> over long/string/int) so the delegate
    /// marshals cleanly across the assembly boundary.
    ///
    /// All events fire on the main thread, after the underlying state has been updated.
    /// Player ids are Valheim player ids (<see cref="Player.GetPlayerID"/>). A subscriber's
    /// exception is swallowed so a bad handler can never break the quest flow.
    /// </summary>
    public static class RpgMakerApi
    {
        /// <summary>Presence marker for soft (reflection-based) consumers.</summary>
        public static bool IsAvailable => true;

        /// <summary>A creature was killed and credited to a player. (playerId, creaturePrefab, amount)</summary>
        public static event Action<long, string, int> OnQuestKill;

        /// <summary>A quest auto-completed for a player. (playerId, questId)</summary>
        public static event Action<long, string> OnQuestCompleted;

        /// <summary>A player custom value was written. (playerId, key, newValue, op) — op is "set" or "add".</summary>
        public static event Action<long, string, int, string> OnCustomValue;

        public static void FireQuestKill(long playerId, string creaturePrefab, int amount)
        {
            Action<long, string, int> handler = OnQuestKill;
            if (handler == null)
            {
                return;
            }

            try { handler(playerId, creaturePrefab, amount); }
            catch (Exception ex) { Debug.LogWarning("[RpgMakerApi] OnQuestKill subscriber threw: " + ex.Message); }
        }

        public static void FireQuestCompleted(long playerId, string questId)
        {
            Action<long, string> handler = OnQuestCompleted;
            if (handler == null)
            {
                return;
            }

            try { handler(playerId, questId); }
            catch (Exception ex) { Debug.LogWarning("[RpgMakerApi] OnQuestCompleted subscriber threw: " + ex.Message); }
        }

        public static void FireCustomValue(long playerId, string key, int newValue, string op)
        {
            Action<long, string, int, string> handler = OnCustomValue;
            if (handler == null)
            {
                return;
            }

            try { handler(playerId, key, newValue, op); }
            catch (Exception ex) { Debug.LogWarning("[RpgMakerApi] OnCustomValue subscriber threw: " + ex.Message); }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Social authority hand-off (groups + guilds).
        //
        // When a full social mod (e.g. VikingLands.Core / TDV) is installed it owns the
        // authoritative group + guild membership. It registers its query methods here, and
        // RPG Maker routes its own group/guild-dependent features — guild chat, dialogue guild
        // conditions, territory OnlyForGuild — through these instead of its own half-built
        // systems. Registration mirrors FiresCompanions' CompanionAllyApi.SetAllyResolver:
        // the external mod resolves this type by name and calls the Set* methods (the delegates
        // use only framework + long/string types so they marshal cleanly across the boundary).
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>External "are these two players in the same group" resolver. (playerIdA, playerIdB)</summary>
        public static Func<long, long, bool> GroupResolver;
        public static void SetGroupResolver(Func<long, long, bool> resolver) => GroupResolver = resolver;

        /// <summary>True once an external mod owns group membership.</summary>
        public static bool GroupsOwnedExternally => GroupResolver != null;

        public static bool AreGrouped(long playerA, long playerB)
        {
            var h = GroupResolver;
            if (h == null) return false;
            try { return h(playerA, playerB); } catch { return false; }
        }

        /// <summary>External "are these two players in the same guild" resolver. (playerIdA, playerIdB)</summary>
        public static Func<long, long, bool> GuildResolver;
        public static void SetGuildResolver(Func<long, long, bool> resolver) => GuildResolver = resolver;

        /// <summary>External provider for the local player's guild name (null/empty when not in a guild).</summary>
        public static Func<string> LocalGuildNameProvider;
        public static void SetLocalGuildNameProvider(Func<string> provider) => LocalGuildNameProvider = provider;

        /// <summary>External sink for sending a guild chat message from RPG Maker's /clan channel.</summary>
        public static Action<string> GuildChatSender;
        public static void SetGuildChatSender(Action<string> sender) => GuildChatSender = sender;

        /// <summary>True once an external mod owns guild membership (drives RPG Maker's guild-dependent features).</summary>
        public static bool GuildsOwnedExternally => GuildResolver != null || LocalGuildNameProvider != null;

        public static bool AreGuilded(long playerA, long playerB)
        {
            var h = GuildResolver;
            if (h == null) return false;
            try { return h(playerA, playerB); } catch { return false; }
        }

        /// <summary>The local player's guild name per the external provider, or null when not in a guild / no provider.</summary>
        public static string LocalGuildName()
        {
            var h = LocalGuildNameProvider;
            if (h == null) return null;
            try { var n = h(); return string.IsNullOrEmpty(n) ? null : n; } catch { return null; }
        }

        /// <summary>Routes a guild chat message to the external owner. Returns false (caller handles) when none registered.</summary>
        public static bool SendGuildChat(string text)
        {
            var h = GuildChatSender;
            if (h == null) return false;
            try { h(text); return true; } catch { return false; }
        }

        // ── Richer local-guild providers ───────────────────────────────────────────
        // Let RPG Maker rebuild a lightweight guild object backed by the external owner so its OWN guild
        // queries (GetOwnGuild / AreGuildmates / friendly-fire gates) recognize the external guild instead
        // of reporting 0/none. Marshalled as framework types (long/string arrays) across the reflection
        // boundary. All resolve to the LOCAL player's guild — remote-guild membership is answered by
        // AreGuilded(a,b).

        /// <summary>Leader's player id of the local player's guild (0 when none).</summary>
        public static Func<long> LocalGuildLeaderIdProvider;
        public static void SetLocalGuildLeaderId(Func<long> provider) => LocalGuildLeaderIdProvider = provider;
        public static long LocalGuildLeaderId()
        {
            var h = LocalGuildLeaderIdProvider;
            if (h == null) return 0L;
            try { return h(); } catch { return 0L; }
        }

        /// <summary>Player ids of the local player's guild members (empty when none).</summary>
        public static Func<long[]> LocalGuildMemberIdsProvider;
        public static void SetLocalGuildMemberIds(Func<long[]> provider) => LocalGuildMemberIdsProvider = provider;
        public static long[] LocalGuildMemberIds()
        {
            var h = LocalGuildMemberIdsProvider;
            if (h == null) return System.Array.Empty<long>();
            try { return h() ?? System.Array.Empty<long>(); } catch { return System.Array.Empty<long>(); }
        }

        /// <summary>Names of the local player's guild members, index-aligned with <see cref="LocalGuildMemberIds"/>.</summary>
        public static Func<string[]> LocalGuildMemberNamesProvider;
        public static void SetLocalGuildMemberNames(Func<string[]> provider) => LocalGuildMemberNamesProvider = provider;
        public static string[] LocalGuildMemberNames()
        {
            var h = LocalGuildMemberNamesProvider;
            if (h == null) return System.Array.Empty<string>();
            try { return h() ?? System.Array.Empty<string>(); } catch { return System.Array.Empty<string>(); }
        }
    }
}
