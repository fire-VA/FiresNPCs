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
    }
}
