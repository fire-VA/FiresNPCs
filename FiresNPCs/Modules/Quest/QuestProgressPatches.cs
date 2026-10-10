using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;
using FiresCore.Npc;
using FiresCore.Lifecycle;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// Patches for quest progress tracking (currently focused on kill quests)
    /// Now also tracks companion kills and credits them to the companion's owner.
    /// </summary>
    public static class QuestProgressPatches
    {
        private static readonly FieldInfo lastHitField = typeof(Character).GetField("m_lastHit", BindingFlags.NonPublic | BindingFlags.Instance);

        // Rate limiting for combat logs to reduce spam
        private static System.Collections.Generic.Dictionary<string, DateTime> _lastLogTimes = new System.Collections.Generic.Dictionary<string, DateTime>();
        private static readonly TimeSpan LOG_THROTTLE_INTERVAL = TimeSpan.FromSeconds(5);
        
        /// <summary>
        /// Check if we should log for a specific key (rate-limited to once per LOG_THROTTLE_INTERVAL).
        /// </summary>
        private static bool ShouldLogForKey(string key)
        {
            if (!FiresLogger.VerboseEnabled) return false;
            
            var now = DateTime.UtcNow;
            if (_lastLogTimes.TryGetValue(key, out var lastTime))
            {
                if (now - lastTime < LOG_THROTTLE_INTERVAL)
                    return false;
            }
            _lastLogTimes[key] = now;
            return true;
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        public static class Character_RPC_Damage_KillQuestProgress
        {
            private static DateTime _lastDamageLogUtc;
            private static readonly TimeSpan DamageLogInterval = TimeSpan.FromSeconds(60);

            private static void Postfix(Character __instance, long sender, HitData hit)
            {
                try
                {
                    if (FiresLogger.VerboseEnabled)
                    {
                        var now = DateTime.UtcNow;
                        if (now - _lastDamageLogUtc >= DamageLogInterval)
                        {
                            _lastDamageLogUtc = now;
                            FiresLogger.LogVerbose("[FiresRPGmaker] RPC_Damage postfix STARTED");
                        }
                    }

                    var attacker = hit?.GetAttacker();
                    if (attacker == null) return;

                    // Check if attacker is the local player
                    bool isPlayerAttacker = attacker == Player.m_localPlayer;

                    // Check if attacker is a companion owned by the local player
                    CompanionController companionAttacker = null;
                    bool isCompanionAttacker = false;
                    long ownerPlayerId = 0;

                    if (!isPlayerAttacker)
                    {
                        companionAttacker = attacker.GetComponent<CompanionController>();
                        if (companionAttacker != null && companionAttacker.isTamed)
                        {
                            // Get the companion's owner
                            var owner = companionAttacker.GetOwner();
                            if (owner != null && owner == Player.m_localPlayer)
                            {
                            isCompanionAttacker = true;
                            ownerPlayerId = owner.GetPlayerID();

                            // Rate-limited logging for companion attacks - ONLY for companions actively following the player
                            // Skip logging for companions that are staying/idle to reduce log spam
                            if (companionAttacker.ShouldBeFollowing)
                            {
                                string logKey = $"companion_attack_{companionAttacker.companionId}_{__instance.name}";
                                if (ShouldLogForKey(logKey))
                                {
                                    FiresLogger.LogVerbose($"[FiresRPGmaker] Companion {companionAttacker.GetDisplayName()} attacking {__instance.name}, owner: {owner.GetPlayerName()}");
                                }
                            }
                        }
                    }
                }

                    // If neither player nor their companion, skip
                    if (!isPlayerAttacker && !isCompanionAttacker) return;

                    string prefab = __instance.name.Split('(')[0].Trim();
                    long playerId = isPlayerAttacker ? Player.m_localPlayer.GetPlayerID() : ownerPlayerId;

                    // Rate-limited verbose logging for kill checks (reduces spam)
                    // ONLY log for companions that are actively following the player
                    bool shouldLogCombat = !isCompanionAttacker || (companionAttacker != null && companionAttacker.ShouldBeFollowing);
                    string killCheckKey = $"kill_check_{__instance.name}";
                    if (shouldLogCombat && ShouldLogForKey(killCheckKey))
                    {
                        float health = float.NaN, maxHealth = float.NaN;
                        try { health = __instance.GetHealth(); } catch { }
                        try { maxHealth = __instance.GetMaxHealth(); } catch { }
                        string incoming = hit?.GetAttacker()?.name ?? "null";
                        string recorded = "null";
                        try { recorded = lastHitField?.GetValue(__instance) is HitData rd && rd.GetAttacker() != null ? rd.GetAttacker().name : "null"; } catch { }
                        string attackerType = isCompanionAttacker ? $"COMPANION({companionAttacker.GetDisplayName()})" : "PLAYER";
                        FiresLogger.LogVerbose($"Kill check: target={__instance.name}, isDead={__instance.IsDead()}, health={health}/{maxHealth}, incoming={incoming}, recorded={recorded}, attackerType={attackerType}");
                    }

                    if (__instance.IsDead())
                    {
                        ApplyKillProcessing(__instance, prefab, playerId, companionAttacker);
                        return;
                    }

                    try
                    {
                        var go = __instance.gameObject;
                        var watcher = go.GetComponent<DeathWatcher>();
                        if (watcher == null)
                            watcher = go.AddComponent<DeathWatcher>();

                        watcher.SetContext(prefab, playerId, companionAttacker);
                        // Rate-limited log for DeathWatcher attachment - ONLY log for following companions
                        string watcherKey = $"deathwatcher_{__instance.name}";
                        if (shouldLogCombat && ShouldLogForKey(watcherKey))
                        {
                            string attackerInfo = isCompanionAttacker ? $"companion {companionAttacker.GetDisplayName()}" : "player";
                            FiresLogger.LogVerbose($"Attached DeathWatcher to {__instance.name} for {attackerInfo} (owner: {playerId})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to attach DeathWatcher: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FiresRPGmaker] CRITICAL RPC_Damage postfix crash: {ex.Message}\n{ex.StackTrace}");
                }
            }

            internal static void ApplyKillProcessing(Character target, string prefab, long playerId, CompanionController killingCompanion = null)
            {
                try
                {
                    // Determine star level from character
                    int charLevel = 0;
                    try { charLevel = target.GetLevel(); } catch { charLevel = 0; }
                    // Map levels to stars: level1 -> star0, level2 -> star1, etc.
                    int star = Math.Max(0, charLevel - 1);

                    if (FiresLogger.VerboseEnabled)
                    {
                        string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                        FiresLogger.LogVerbose($"Processing kill: {killerInfo} (owner={playerId}), prefab={prefab}, target={target.name}, level={charLevel}, star={star}");
                    }

                    // RPG Maker public API: surface the credited kill to external quest
                    // consumers (VikingLands leaderboard/groups/boss compat, etc.).
                    RpgMakerApi.FireQuestKill(playerId, prefab, 1);

                    // =========================================
                    // COMPANION KILL TRACKING
                    // Credit the kill to the companion's kill tracker
                    // =========================================
                    if (killingCompanion != null)
                    {
                        var killTracker = killingCompanion.GetComponent<CompanionKillTracker>();
                        if (killTracker != null)
                        {
                            killTracker.RecordKill(target);
                            if (FiresLogger.VerboseEnabled)
                            {
                                FiresLogger.LogVerbose($"[FiresRPGmaker] Recorded kill for companion {killingCompanion.GetDisplayName()}: {prefab}");
                            }
                        }
                        
                        // =========================================
                        // ARCHETYPE SKILL XP FOR KILLS
                        // Award bonus XP to any active combat ability
                        // =========================================
                        var skillSystem = killingCompanion.GetComponent<FiresCore.Npc.Archetypes.ArchetypeSkillSystem>();
                        if (skillSystem != null)
                        {
                            // Award kill XP to the companion's currently active combat skills
                            // This rewards any offensive ability that contributed to the kill
                            skillSystem.OnKillWithAbility(target, star > 0);
                            
                            if (FiresLogger.VerboseEnabled)
                            {
                                FiresLogger.LogVerbose($"[FiresRPGmaker] Awarded skill XP for kill to companion {killingCompanion.GetDisplayName()}");
                            }
                        }
                    }

                    // =========================================
                    // PLAYER QUEST PROGRESS (also credits companion kills to owner)
                    // =========================================

                    // Prefer vault-backed player data (client caches server copy). Fall back to ZDO JSON if missing.
                    PlayerVaultData vaultData = null;
                    try
                    {
                        if (VaultOfKnowledge.Instance != null)
                            vaultData = VaultOfKnowledge.Instance.GetPlayerData(playerId);
                    }
                    catch { vaultData = null; }

                    // If we couldn't get Vault data, attempt to parse local customData for compatibility
                    PlayerQuestData localData = null;
                    var player = Player.m_localPlayer;
                    if (vaultData == null && player != null)
                    {
                        try
                        {
                            if (!player.m_customData.TryGetValue("FiresRPGmaker_Quests", out string json) || string.IsNullOrEmpty(json))
                            {
                                json = "{}";
                                PlayerSpawnGate.SafeSetCustomData(player, "FiresRPGmaker_Quests", json, 10f);
                            }
                            localData = JsonConvert.DeserializeObject<PlayerQuestData>(json) ?? new PlayerQuestData();
                        }
                        catch { localData = new PlayerQuestData(); }
                    }

                    // Diagnostic dump: show active quests available from vault or local
                    try
                    {
                        if (vaultData != null)
                        {
                            FiresLogger.LogVerbose($"Vault active quests count for player {playerId}: {vaultData.ActiveQuests.Count}");
                            foreach (var aq in vaultData.ActiveQuests)
                                FiresLogger.LogVerbose($"Vault ActiveQuest: id={aq.QuestId}, progress={aq.Progress}");
                        }
                        else if (localData != null)
                        {
                            FiresLogger.LogVerbose($"Local active quests count for player {playerId}: {localData.ActiveQuests.Count}");
                            foreach (var aq in localData.ActiveQuests)
                                FiresLogger.LogVerbose($"Local ActiveQuest: id={aq.QuestId}, progress={aq.Progress}");
                        }
                        else
                        {
                            FiresLogger.LogVerbose($"No active quest data present for player {playerId}");
                        }
                    }
                    catch { }

                    // Send stat RPCs as before (updated name)
                    try
                    {
                        // Determine stat name using m_name if present, else prefab
                        string actualNameForStats = null;
                        try
                        {
                            var fn = typeof(Character).GetField("m_name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fn != null)
                                actualNameForStats = fn.GetValue(target) as string;
                        }
                        catch { }
                        string statBase = !string.IsNullOrEmpty(actualNameForStats) ? actualNameForStats : prefab;
                        string statKey = statBase.ToLowerInvariant();

                        if (FiresLogger.VerboseEnabled)
                        {
                            string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                            FiresLogger.LogVerbose($"Stat key computed: '{statKey}' (m_name='{actualNameForStats ?? ""}') from prefab '{prefab}'; level={charLevel}, star={star}, killer={killerInfo}");
                        }

                        if (ZRoutedRpc.instance != null)
                        {
                            try
                            {
                                // Always increment main stat (star0 included here)
                                // For companion kills, we track both player stats and companion-specific stats
                                var pkg = CreateStatPackage(playerId, $"kills.{statKey}", 1);
                                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_UpdateStat", pkg);

                                // Feed the server-authoritative cross-player leaderboard (Core repo). PvP kills
                                // route to the KilledPlayers column; everything else to KilledCreatures.
                                try
                                {
                                    var creditPlayer = Player.GetPlayer(playerId);
                                    string creditName = creditPlayer != null ? creditPlayer.GetPlayerName() : null;
                                    if (!string.IsNullOrEmpty(creditName))
                                    {
                                        var kind = target is Player
                                            ? Marketplace.LeaderboardStat.KilledPlayer
                                            : Marketplace.LeaderboardStat.KilledCreature;
                                        // Feed the leaderboard with the RAW prefab name (PascalCase, e.g. "Greydwarf"),
                                        // NOT the lowercased m_name token used for the internal kills.<statKey> stat above.
                                        // The leaderboard's KilledCreatures key must equal AchievementDef.Prefab (the
                                        // generator writes prefab.name) or every MonstersKilled achievement reads 0.
                                        // (For KilledPlayer the server hardcodes the "Player" key, so this arg is ignored.)
                                        Marketplace.LeaderboardServerFeed.Send(kind, creditName, prefab, 1);
                                    }
                                }
                                catch { }

                                // Only increment starN stat if star > 0, using format 'kills.<name><N>Star'
                                if (star > 0)
                                {
                                    var pkgStar = CreateStatPackage(playerId, $"kills.{statKey}{star}Star", 1);
                                    ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_UpdateStat", pkgStar);
                                }

                                // Track companion-assisted kills separately
                                if (killingCompanion != null)
                                {
                                    var pkgCompanion = CreateStatPackage(playerId, $"companion_kills.{statKey}", 1);
                                    ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_UpdateStat", pkgCompanion);
                                }

                                if (FiresLogger.VerboseEnabled)
                                    FiresLogger.LogVerbose($"Sent stat RPCs for kills.{statKey}" + (star > 0 ? $" and kills.{statKey}{star}Star" : "") + (killingCompanion != null ? $" and companion_kills.{statKey}" : ""));
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateStat failed: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateStat failed: {ex.Message}");
                    }

                    // kg AllowKillQuestsInParty parity (Quest_ProgressionHook): share the kill with every party
                    // member within kg's 100m receiver rule so THEIR kill quests progress too. Runs before the
                    // killer's own quest filtering — a member gets credit even when the killer has no kill quest.
                    // Server applies each member's credit against their own active quests (no-op without one).
                    try
                    {
                        if (global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.AllowKillQuestsInParty
                            && ZRoutedRpc.instance != null && target != null
                            && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                        {
                            var party = global::FiresRPGmaker.Modules.Party.PartyManager.GetLocalParty();
                            if (party != null && party.Members != null && party.Members.Count > 1)
                            {
                                foreach (var member in party.Members)
                                {
                                    if (member == null || member.PlayerId == playerId) continue;
                                    var inst = global::FiresRPGmaker.Modules.Party.PartyManager.FindLocalPlayerInstance(member.PlayerId);
                                    if (inst == null) continue;
                                    if (Vector3.Distance(inst.transform.position, target.transform.position) > 100f) continue;
                                    var sharePkg = new ZPackage();
                                    sharePkg.Write(member.PlayerId);
                                    sharePkg.Write(prefab);
                                    sharePkg.Write(star);
                                    sharePkg.Write(false);   // shared credit carries no companion attribution
                                    ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_KillEvent", sharePkg);
                                }
                            }
                        }
                    }
                    catch { }

                    bool anyChange = false;

                    // Helper to process a vault-styled active quest list
                    void ProcessActiveVaultList(System.Collections.Generic.List<ActiveQuestData> activeList)
                    {
                        for (int i = activeList.Count - 1; i >= 0; i--)
                        {
                            // kg AllowMultipleQuestsScore (default false): one kill scores only the FIRST
                            // matching quest — also caps this loop at ONE KillEvent RPC, since the server
                            // treats each RPC as its own event.
                            if (anyChange && !global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.AllowMultipleQuestScore) break;
                            var active = activeList[i];
                            var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                            if (def == null || def.QuestType != "Kill") continue;

                            foreach (var targetDef in def.QuestTargets)
                            {
                                try
                                {
                                    if (FiresLogger.VerboseEnabled)
                                        FiresLogger.LogVerbose($"Checking quest {active.QuestId} target '{targetDef.Prefab}' against prefab '{prefab}'");
                                }
                                catch { }

                                // Require prefab match
                                bool prefabMatch = targetDef.Prefab.Equals(prefab, StringComparison.OrdinalIgnoreCase);
                                if (!prefabMatch) continue;

                                // If NameFilter is set, require m_name match
                                if (!string.IsNullOrEmpty(targetDef.NameFilter))
                                {
                                    string charName = null;
                                    try
                                    {
                                        var fn = typeof(Character).GetField("m_name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fn != null)
                                        {
                                            charName = fn.GetValue(target) as string;
                                        }
                                    }
                                    catch { }
                                    if (string.IsNullOrEmpty(charName))
                                        charName = target.name;
                                    if (!string.Equals(charName, targetDef.NameFilter, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }

                                // Always use m_name for stat tracking if available, else fallback to prefab
                                string statName = null;
                                string actualName = null;
                                try
                                {
                                    var fn = typeof(Character).GetField("m_name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (fn != null)
                                        actualName = fn.GetValue(target) as string;
                                }
                                catch { }
                                statName = !string.IsNullOrEmpty(actualName) ? actualName : prefab;

                                // Note: stat RPCs are sent once per kill earlier. Do not send duplicate stat RPCs here.

                                // If we're a client connected to a dedicated server, send KillEvent RPC and do NOT modify local data.
                                if (ZRoutedRpc.instance != null && ZNet.instance != null && !ZNet.instance.IsServer())
                                {
                                    try
                                    {
                                        var pkg = new ZPackage();
                                        pkg.Write(playerId);
                                        pkg.Write(prefab);
                                        pkg.Write(star);
                                        // Include companion info in the RPC
                                        pkg.Write(killingCompanion != null);
                                        if (killingCompanion != null)
                                        {
                                            pkg.Write(killingCompanion.companionId ?? "");
                                        }
                                        ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_KillEvent", pkg);
                                        if (FiresLogger.VerboseEnabled)
                                        {
                                            string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                                            FiresLogger.LogVerbose($"Sent KillEvent RPC for prefab {prefab} (player {playerId}, killer: {killerInfo})");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"[FiresRPGmaker] KillEvent RPC failed: {ex.Message}");
                                    }

                                    // Optimistic local update: increment active quest progress so client UI feels responsive.
                                    try
                                    {
                                        var local = Player.m_localPlayer;
                                        if (local != null && local.GetPlayerID() == playerId && vaultData != null)
                                        {
                                            // Only increment if this objective is below its required amount
                                            int optIdx = def.QuestTargets.IndexOf(targetDef);
                                            int optCur = active.GetTargetProgress(optIdx, def.QuestTargets.Count);
                                            if (optCur < targetDef.Amount)
                                            {
                                                active.SetTargetProgress(optIdx, optCur + 1, def.QuestTargets.Count);
                                                // write into local customData so DeserializeFromCustomData will reflect change.
                                                // Optimistic - server resyncs via RPC_ReceivePlayerData if this gets dropped.
                                                var capturedVaultData = vaultData;
                                                var capturedQuestId = active.QuestId;
                                                var capturedProgress = active.Progress;
                                                PlayerSpawnGate.RunWhenLocalReady(10f, lp =>
                                                {
                                                    try
                                                    {
                                                        lp.m_customData["FiresRPGmaker_Vault"] = JsonConvert.SerializeObject(capturedVaultData);
                                                        lp.m_customData["FiresRPGmaker_Dirty"] = "0";
                                                        if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Optimistic client progress +1 for quest {capturedQuestId}: now {capturedProgress}");
                                                    }
                                                    catch { }
                                                });
                                            }
                                            else
                                            {
                                                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Optimistic client: quest {active.QuestId} already at required amount ({active.Progress}); not incrementing");
                                            }
                                        }
                                    }
                                    catch { }

                                    // Mark the event as scored so the outer first-match guard (kg
                                    // AllowMultipleQuestsScore=false) stops after ONE KillEvent RPC.
                                    anyChange = true;
                                    // Server is authoritative; it will send back the corrected data. Break to avoid local authoritative changes.
                                    break;
                                }

                                // If we're running as server/host, apply change authoritatively
                                try
                                {
                                    // Only increment server-side if this objective hasn't reached its required amount
                                    int srvIdx = def.QuestTargets.IndexOf(targetDef);
                                    int srvCur = active.GetTargetProgress(srvIdx, def.QuestTargets.Count);
                                    if (srvCur < targetDef.Amount)
                                    {
                                        active.SetTargetProgress(srvIdx, srvCur + 1, def.QuestTargets.Count);
                                        anyChange = true;
                                        if (FiresLogger.VerboseEnabled)
                                        {
                                            string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                                            FiresLogger.LogVerbose($"Server applied progress +1 for quest {active.QuestId} objective {srvIdx}: now {active.GetTargetProgress(srvIdx, def.QuestTargets.Count)}/{targetDef.Amount} (killer: {killerInfo})");
                                        }

                                        // If ALL objectives are now complete, handle completion for autocomplete quests
                                        if (active.AllTargetsComplete(def))
                                        {
                                            if (def.Autocomplete)
                                            {
                                                // move to completed
                                                if (vaultData != null)
                                                {
                                                    vaultData.AddCompletedQuest(active.QuestId, 1);
                                                    vaultData.ActiveQuests.RemoveAll(q => q.QuestId == active.QuestId);
                                                }
                                                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest completed: {def.Name ?? active.QuestId}");
                                            }
                                            else
                                            {
                                                // Non-autocomplete: cap progress and leave in active list so player can turn it in
                                                if (FiresLogger.VerboseEnabled)
                                                    FiresLogger.LogVerbose($"Quest {active.QuestId} reached required amount ({targetDef.Amount}) but not Autocomplete; progress capped.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (FiresLogger.VerboseEnabled)
                                            FiresLogger.LogVerbose($"Server: quest {active.QuestId} already at required amount ({active.Progress}); not incrementing");
                                    }

                                    // Persist server-side into ZDO or disk using VaultOfKnowledge.
                                    // SerializeToCustomData + SavePlayerDataFromZDO touch m_customData;
                                    // defer them through the spawn gate so a host who is mid-teleport
                                    // doesn't get the write during engine teardown.
                                    if (vaultData != null)
                                    {
                                        vaultData.IsDirty = true;
                                        var playerObj = Player.GetPlayer(playerId);
                                        if (playerObj != null)
                                        {
                                            var capturedVault = vaultData;
                                            var capturedPlayerId = playerId;
                                            PlayerSpawnGate.RunWhenReady(playerObj, 30f, p =>
                                            {
                                                try
                                                {
                                                    VaultOfKnowledge.Instance.SerializeToCustomData(p, capturedVault);
                                                    VaultOfKnowledge.Instance.SavePlayerDataFromZDO(capturedPlayerId, p);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.LogWarning($"[FiresRPGmaker] Deferred quest persist failed for {capturedPlayerId}: {ex.Message}");
                                                }
                                            });
                                        }
                                        else
                                        {
                                            VaultOfKnowledge.Instance.SavePlayerData(playerId, vaultData);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"[FiresRPGmaker] Server progress apply failed: {ex.Message}");
                                }

                                break;
                            }
                        }
                    }

                    // Process whichever data source we have
                    if (vaultData != null)
                    {
                        ProcessActiveVaultList(vaultData.ActiveQuests);
                    }
                    else if (localData != null)
                    {
                        // convert local active quests format (ActiveQuestEntry) to ActiveQuestData for reuse
                        var tempList = localData.ActiveQuests.Select(a => new ActiveQuestData { QuestId = a.QuestId, Progress = a.Progress, StartedAt = a.StartedAt }).ToList();
                        ProcessActiveVaultList(tempList);

                        // write back optimistic local changes
                        if (player != null)
                        {
                            var capturedLocal = localData;
                            PlayerSpawnGate.RunWhenReady(player, 10f, p =>
                            {
                                try
                                {
                                    p.m_customData["FiresRPGmaker_Quests"] = JsonConvert.SerializeObject(capturedLocal);
                                    p.m_customData["FiresRPGmaker_Quests_Dirty"] = "1";
                                }
                                catch { }
                            });
                        }
                    }

                    if (!anyChange && FiresLogger.VerboseEnabled)
                    {
                        string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                        FiresLogger.LogVerbose($"No quest progress change for kill {prefab} by {killerInfo} (owner: {playerId})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FiresRPGmaker] ApplyKillProcessing CRASH: {ex.Message}\n{ex.StackTrace}");
                }
            }

            private static ZPackage CreateStatPackage(long playerId, string key, int delta)
            {
                var pkg = new ZPackage();
                pkg.Write(playerId);
                pkg.Write(key);
                pkg.Write(delta);
                return pkg;
            }
        }

        /// <summary>
        /// Monitors a damaged enemy and processes the kill when it dies.
        /// Now also tracks if a companion dealt the killing blow.
        /// </summary>
        public class DeathWatcher : MonoBehaviour
        {
            private string prefab;
            private long playerId;
            private bool initialized = false;
            private CompanionController killingCompanion;

            /// <summary>
            /// Sets the context for tracking. Called when player or companion damages an enemy.
            /// </summary>
            public void SetContext(string prefabName, long pid, CompanionController companion = null)
            {
                prefab = prefabName;
                playerId = pid;
                killingCompanion = companion;
                initialized = true;
            }

            private void OnDestroy()
            {
                try
                {
                    if (!initialized) return;

                    var character = GetComponent<Character>();
                    if (character != null)
                    {
                        if (FiresLogger.VerboseEnabled)
                        {
                            string killerInfo = killingCompanion != null ? $"companion {killingCompanion.GetDisplayName()}" : "player";
                            FiresLogger.LogVerbose($"DeathWatcher OnDestroy for {character.name}, killer: {killerInfo}");
                        }
                        Character_RPC_Damage_KillQuestProgress.ApplyKillProcessing(character, prefab, playerId, killingCompanion);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] DeathWatcher OnDestroy crash: {ex.Message}");
                }
            }
        }
    }
}