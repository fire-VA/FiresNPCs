using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Client-side achievement-completion watcher. Achievements have no completion event — they're evaluated
    /// on demand from live <see cref="LeaderboardTracker"/> stats (<c>progress &gt;= MinAmount</c>) — so this
    /// polls the active profile every few seconds and raises <see cref="FiresCore.Bridge.ProgressEvents"/>
    /// for each NEW crossing. Presentation is someone else's job (FiresDiscordIntegration screenshots and
    /// posts it); with no subscriber the raise is a no-op.
    ///
    /// Anti-re-announce: everything already completed when the player spawns is baselined silently, and the
    /// baseline keeps absorbing for a grace window after spawn so achievements "appearing" while the tracker
    /// primes its persisted stats never announce. State resets per connection, so a character switch
    /// re-baselines against the new character's own progress.
    /// </summary>
    [HarmonyPatch]
    internal static class AchievementAnnouncer
    {
        private const float PollSeconds = 5f;
        private const float BaselineGraceSeconds = 30f;

        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private static bool _running;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        private static void ZNet_Awake_Postfix(ZNet __instance)
        {
            try
            {
                if (__instance == null || __instance.IsServer() || _running) return;
                _running = true;
                FiresCore.Async.MainThreadDispatcher.StartRoutine(WatchLoop());
            }
            catch (Exception ex)
            {
                _running = false;
                Debug.LogWarning($"[RPGmaker] Achievement watcher start failed: {ex.Message}");
            }
        }

        private static IEnumerator WatchLoop()
        {
            _seen.Clear();
            float playerFirstSeen = -1f;

            while (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                yield return new WaitForSeconds(PollSeconds);
                if (Player.m_localPlayer == null) { playerFirstSeen = -1f; continue; }
                if (playerFirstSeen < 0f) playerFirstSeen = Time.realtimeSinceStartup;
                bool inGrace = Time.realtimeSinceStartup - playerFirstSeen < BaselineGraceSeconds;

                try
                {
                    string profile = AchievementProfileManager.ResolveActiveProfile();
                    if (string.IsNullOrEmpty(profile)) continue;

                    var completed = AchievementCompletion.CompletedForLocalPlayer(profile);
                    for (int i = 0; i < completed.Count; i++)
                    {
                        AchievementDef def = completed[i];
                        if (def == null) continue;
                        string key = $"{profile}:{def.Id}:{def.Name}";
                        if (!_seen.Add(key)) continue;
                        if (inGrace) continue; // silently absorb pre-existing + tracker-priming completions

                        FiresCore.Bridge.ProgressEvents.RaiseAchievementCompleted(def.Name, def.Description);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RPGmaker] Achievement watcher tick failed: {ex.Message}");
                }
            }

            _running = false;
        }
    }
}
