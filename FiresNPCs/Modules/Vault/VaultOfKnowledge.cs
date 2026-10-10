using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;
using FiresCore.Lifecycle;

namespace FiresRPGmaker.Modules.Vault
{
    public class VaultOfKnowledge
    {
        public static VaultOfKnowledge Instance { get; private set; }

        private string BaseSavePath => FiresCore.Storage.FiresConfigPaths.Root;
        private string ServerFilePath => FiresCore.Storage.FiresConfigPaths.ServerJson;
        private string PlayersDir => FiresCore.Storage.FiresConfigPaths.Players;

        // Per-world path for the shared Core LiteDB vault (config/FiresRPGmaker/Worlds/<world>/vault.db).
        // Per-world (NOT profile-global) so guild / leaderboard / player-identity data can't bleed across
        // worlds on a shared r2modman profile — the same cross-server-pollution class server.json guards against.
        private string VaultDbPath =>
            Path.Combine(FiresCore.Storage.FiresConfigPaths.Worlds, SanitizeWorldName(ZNet.instance?.GetWorldName()), "vault.db");

        private static string SanitizeWorldName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "default";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            return name.Length == 0 ? "default" : name;
        }

        // Idempotent, Instance-independent vault configuration. Called from Init AND from every server-side
        // economy handler entry (banker / mail / marketplace) so a missing or THROWN VaultOfKnowledge.Init
        // can never leave the shared Core vault unconfigured — which otherwise makes every DB-backed op throw
        // inside VaultDatabase.Open() and silently read back as a zero balance (the vanishing-deposit bug).
        // No-op off-server or before the world name is available (the next economy op retries).
        public static void EnsureVaultConfigured()
        {
            if (FiresCore.Storage.VaultDatabase.IsConfigured) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string world = ZNet.instance.GetWorldName();
            if (string.IsNullOrEmpty(world)) return;
            try
            {
                string path = Path.Combine(FiresCore.Storage.FiresConfigPaths.Worlds, SanitizeWorldName(world), "vault.db");
                FiresCore.Storage.VaultDatabase.Configure(path);
                FiresLogger.LogInfo($"[FiresRPGmaker] VaultDatabase configured (per-world): {path}");
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] EnsureVaultConfigured failed: {ex.Message}"); }
        }

        public ServerVaultData serverData = new();

        // pending requests from peers (key = peerUid, value = (requested playerId, requested playerName))
        private readonly Dictionary<long, (long playerId, string playerName)> pendingRequests = new();
        private readonly object pendingLock = new object();

        // small in-memory cache for received player data (fallback for clients/tools)
        private readonly Dictionary<long, PlayerVaultData> playerCache = new();

        // Control flag used by periodic save coroutine to skip the next scheduled save when a player is joining
        private volatile bool skipNextScheduledSave = false;

        private VaultOfKnowledge() { }

        public static void Init()
        {
            if (Instance != null) return;
            Instance = new VaultOfKnowledge();

            // Log PlayerFileStorage version (verbose-only - only useful for debugging DLL loading issues)
            if (FiresLogger.VerboseEnabled)
            {
                try
                {
                    PlayerFileStorage.LogVersion();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] PlayerFileStorage.LogVersion() failed: {ex.Message}");
                }
            }
            
            Directory.CreateDirectory(Instance.PlayersDir);
            // server.json holds server-authoritative state and is profile-global; only the
            // server may load it. A client loading it would carry one server's timers/global
            // state onto another on a shared r2modman profile. Clients get pushed state via RPC.
            // Guarded: a corrupt/locked server.json must NOT abort Init — everything below (vault config,
            // RPC registration, quest/dialogue init in the caller coroutine) depends on Init running to
            // completion. An unguarded throw here was a path to a silently unconfigured vault.
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                try { Instance.LoadServerData(); }
                catch (Exception ex) { Debug.LogError($"[FiresRPGmaker] LoadServerData failed (continuing init): {ex.Message}"); }
            }

            // Configure the shared Core LiteDB vault (per-world) on the server so PlayerIdentity capture and
            // the leaderboard/banker/mail repositories actually persist. Server-only — clients receive snapshots
            // via RPC, they never open the DB. This is now the same idempotent path the economy RPC handlers
            // call defensively, so configuration can't be lost if an earlier server-init step throws.
            EnsureVaultConfigured();

            EnsureRpcsRegistered();

            // Ensure timer manager is running so server can enforce timers
            try { QuestTimerManager.EnsureStarted(); } catch { QuestTimerManager.EnsureStarted(); }
            // Load persisted timers into runtime manager
            try { QuestTimerManager.LoadPersistedTimers(Instance.serverData.Timers); } catch { }

            FiresLogger.LogVerbose("[FiresRPGmaker] VaultOfKnowledge initialized (hybrid ZDO + JSON)");
        }

        // Ensure RPCs are registered - minimal set for hybrid approach. Registration is bound to the CURRENT
        // ZRoutedRpc.instance: when that instance changes (reconnect / world change) the handlers on the old
        // one are dead, so we re-register on the new instance. A one-shot bool used to leave a reconnected
        // CLIENT unable to RECEIVE server pushes — the leaderboard snapshot showed empty until a full relaunch.
        // The ZNet.Start postfix (RpcReRegisterOnConnect) re-invokes this every session.
        private static ZRoutedRpc _rpcInstance;

        // Re-bind the routed-RPC handlers on every ZNet session start (initial connect AND reconnect / world
        // change). The one-time WaitForZNetReady coroutine fires only once per process, so without this a
        // reconnected client keeps dead handlers on the previous ZRoutedRpc.instance and drops server pushes
        // (that is why the leaderboard board showed empty until a full relaunch). EnsureRpcsRegistered no-ops
        // when the instance is unchanged, so firing it redundantly is harmless.
        [HarmonyPatch(typeof(ZNet), "Start")]
        private static class RpcReRegisterOnConnect
        {
            [HarmonyPostfix]
            private static void Postfix() => EnsureRpcsRegistered();
        }

        internal static void EnsureRpcsRegistered()
        {
            if (ZRoutedRpc.instance == null) return;
            if (ReferenceEquals(_rpcInstance, ZRoutedRpc.instance)) return;

            try
            {
                // clients send kill events and stat updates; server applies them to m_customData and persists
                ZRoutedRpc.instance.Register("FiresRPGmaker_KillEvent", new Action<long, ZPackage>(RPC_KillEvent));
                ZRoutedRpc.instance.Register("FiresRPGmaker_QuestProgressEvent", new Action<long, ZPackage>(RPC_QuestProgressEvent));
                ZRoutedRpc.instance.Register("FiresRPGmaker_QuestMoveCheck", new Action<long, ZPackage>(RPC_QuestMoveCheck));
                ZRoutedRpc.instance.Register("FiresRPGmaker_UpdateStat", new Action<long, ZPackage>(RPC_UpdateStat));
                Marketplace.LeaderboardServerFeed.RegisterRpc(ZRoutedRpc.instance);
                Marketplace.LeaderboardBoard.RegisterRpcs(ZRoutedRpc.instance);
                Marketplace.LeaderboardAppearanceFeed.RegisterRpc(ZRoutedRpc.instance);
                Marketplace.LeaderboardAppearanceBoard.RegisterRpcs(ZRoutedRpc.instance);
                Marketplace.LeaderboardAchievementBoard.RegisterRpcs(ZRoutedRpc.instance);
                Marketplace.LeaderboardSkillFeed.RegisterRpc(ZRoutedRpc.instance);
                ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_AnnouncePlayerInfo", new Action<long, ZPackage>(RPC_AnnouncePlayerInfo));
                ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_RequestPlayerData", new Action<long, ZPackage>(RPC_RequestPlayerData));
                ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_ReceivePlayerData", new Action<long, ZPackage>(RPC_ReceivePlayerData));
                ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_UpdateQuestFlag", new Action<long, ZPackage>(RPC_UpdateQuestFlag));
                // Acknowledgement RPC from server -> client after quest flag applied; client will request authoritative data
                ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_UpdateQuestFlag_Ack", new Action<long, ZPackage>(RPC_UpdateQuestFlag_Ack));
                // Quest completion notify: server -> client to show message and list rewards
                ZRoutedRpc.instance.Register("FiresRPGmaker_Quest_CompleteNotify", new Action<long, ZPackage>(RPC_QuestCompleteNotify));
                // Quest DB RPC: server can push full quest DB JSON to clients
                try
                {
                    ZRoutedRpc.instance.Register("FiresRPGmaker_QuestDB", new Action<long, ZPackage>(global::FiresRPGmaker.Modules.Quest.QuestManager.HandleQuestDB));
                    // Quest reward overflow (inventory full → Mail/Bank via server; result → client message)
                    ZRoutedRpc.instance.Register(global::FiresRPGmaker.Modules.Quest.QuestRewardDispatcher.RpcOverflow, new Action<long, ZPackage>(global::FiresRPGmaker.Modules.Quest.QuestRewardDispatcher.RPC_Overflow));
                    ZRoutedRpc.instance.Register(global::FiresRPGmaker.Modules.Quest.QuestRewardDispatcher.RpcOverflowResult, new Action<long, ZPackage>(global::FiresRPGmaker.Modules.Quest.QuestRewardDispatcher.RPC_OverflowResult));
                    // NOTE: "FiresRPGmaker_FileUpdate" is deliberately NOT registered here.
                    // DialogueSyncRPC.Register (ZNet.Awake — runs BEFORE this ZNet.Start hook) owns that
                    // name for every config family; a second Register is a Dictionary.Add throw, and
                    // because it sat mid-try it silently skipped every registration BELOW it (quest
                    // start/timer + companion sync RPCs) on all hosts.
                    // Quest start response RPC: server -> client to report requirement failure/success message
                    ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_QuestStartResponse", new Action<long, ZPackage>(RPC_QuestStartResponse));
                    // Quest timer expiry notification: server -> client to inform timer expired
                    ZRoutedRpc.instance.Register("FiresRPGmaker_Quest_TimerExpired", new Action<long, ZPackage>(RPC_QuestTimerExpired));
                    // Quest timer started notification: server -> client to inform timer start/absolute expiry
                    ZRoutedRpc.instance.Register("FiresRPGmaker_Quest_TimerStarted", new Action<long, ZPackage>(RPC_QuestTimerStarted));
                    // Companion sync RPC: client -> server to sync companion data
                    ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_SyncCompanion", new Action<long, ZPackage>(RPC_SyncCompanion));
                    // Companion remove RPC: client -> server to remove a companion
                    ZRoutedRpc.instance.Register("FiresRPGmaker_Vault_RemoveCompanion", new Action<long, ZPackage>(RPC_RemoveCompanionRPC));
                }
                catch { }
                _rpcInstance = ZRoutedRpc.instance;
                FiresLogger.LogVerbose("[FiresRPGmaker] Vault RPCs registered (hybrid)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to register Vault RPCs: {ex.Message}");
            }
        }

        // Handler called by QuestTimerManager when a quest timer expires
        public void HandleQuestTimerExpired(long playerId, string questId)
        {
            try
            {
                // Only server should process expiry
                if (ZNet.instance != null && !ZNet.instance.IsServer()) return;

                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                if (player != null)
                    data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                else
                    data = LoadPlayerData(playerId);

                if (data == null) return;

                // Remove from active quests as if never picked up and record as failed
                bool removed = false;
                for (int i = data.ActiveQuests.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(data.ActiveQuests[i].QuestId, questId, StringComparison.OrdinalIgnoreCase))
                    {
                        data.ActiveQuests.RemoveAt(i);
                        removed = true;
                    }
                }

                if (removed)
                {
                    // KG-parity lifecycle: fire OnQuestTimeout commands before the row is gone.
                    try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.Fire(QuestManager.Instance?.GetQuestDefinition(questId), global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnTimeout); } catch { }
                    try
                    {
                        if (data.FailedQuests == null) data.FailedQuests = new List<string>();
                        data.FailedQuests.Add(questId);
                        data.IsDirty = true;

                        // Remove timer persistence
                        try
                        {
                            var key = playerId.ToString() + ":" + questId;
                            if (serverData.Timers.ContainsKey(key)) serverData.Timers.Remove(key);
                            serverData.IsDirty = true;
                            SaveServerData(serverData);
                        }
                        catch { }

                        if (player != null)
                        {
                            SerializeToCustomData(player, data);
                            SavePlayerDataFromZDO(playerId, player);
                        }
                        else
                        {
                            SavePlayerData(playerId, data);
                        }

                        // Notify player if online and push refreshed data so client clears active flag
                        try
                        {
                            var pkg = new ZPackage();
                            pkg.Write(questId ?? string.Empty);
                            pkg.Write(QuestManager.Instance?.GetQuestDefinition(questId)?.Name ?? questId ?? string.Empty);
                            long targetUid = 0L;
                            try
                            {
                                if (player != null && ZNet.instance != null)
                                {
                                    var peer = ZNet.instance.GetPeerByPlayerName(player.GetPlayerName());
                                    if (peer != null) targetUid = peer.m_uid;
                                }
                            }
                            catch { }
                            SafeInvokeRoutedRPC(targetUid, "FiresRPGmaker_Quest_TimerExpired", pkg);

                            // also push authoritative player data to clear client-side active flag
                            try
                            {
                                var dataPkg = new ZPackage();
                                data.Serialize(dataPkg);
                                SafeInvokeRoutedRPC(targetUid, "FiresRPGmaker_Vault_ReceivePlayerData", dataPkg);
                            }
                            catch { }
                        }
                        catch { }

                        // Show center message on server (local)
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest timer expired: {QuestManager.Instance?.GetQuestDefinition(questId)?.Name ?? questId}. Please try again");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] HandleQuestTimerExpired failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] HandleQuestTimerExpired exception: {ex.Message}");
            }
        }

        // Safe wrapper for invoking routed RPCs to avoid NullReferenceExceptions when ZRoutedRpc.instance is not available.
        // Delegates to SafeRoutedRpc so every Vault RPC is also size-gated
        // (<= 480 KB per-message ceiling) - an oversized ZPackage is now refused and logged
        // instead of crashing Steam's 512 KB per-message cap.
        public static void SafeInvokeRoutedRPC(long target, string method, ZPackage pkg)
        {
            EnsureRpcsRegistered();
            SafeRoutedRpc.InvokeSafe(target, method, pkg);
        }

        // Normalize quest id strings received from clients or commands.
        // Strips common accidental prefixes such as 'startquest ', 'startquest=', 'startquest,' and trims whitespace.
        public static string NormalizeQuestId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = raw.Trim();
            if (s.StartsWith("startquest", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring("startquest".Length).TrimStart(' ', '=', ',', ':');
                return s.Trim();
            }

            // handle 'startquest,killdeer' or 'startquest, killdeer'
            if (s.Contains(',') && s.IndexOf(' ') < 0)
            {
                var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                if (parts.Length > 1 && parts[0].Equals("startquest", StringComparison.OrdinalIgnoreCase))
                    return parts[1];
            }

            // handle 'startquest=killdeer'
            if (s.Contains('=') && s.IndexOf(' ') < 0)
            {
                var parts = s.Split(new[] { '=' }, 2).Select(p => p.Trim()).ToArray();
                if (parts.Length > 1 && parts[0].Equals("startquest", StringComparison.OrdinalIgnoreCase))
                    return parts[1];
            }

            return s;
        }

        // Client -> Server: announce local player info so server can authoritative-set platform/admin and save
        private static void RPC_AnnouncePlayerInfo(long sender, ZPackage pkg)
        {
            // only process on server
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (Instance == null) return;

            try
            {
                long playerId = 0;
                string playerName = null;
                string platformId = null;
                string platform = null;

                if (pkg != null)
                {
                    try { playerId = pkg.ReadLong(); } catch { playerId = 0; }
                    try { playerName = pkg.ReadString(); } catch { playerName = null; }
                    try { platformId = pkg.ReadString(); } catch { platformId = null; }
                    try { platform = pkg.ReadString(); } catch { platform = null; }
                }

                if (playerId == 0)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_AnnouncePlayerInfo received invalid playerId from sender {sender}");
                    return;
                }

                var player = Player.GetPlayer(playerId) ?? Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == playerId);
                PlayerVaultData data = null;
                if (player != null)
                {
                    data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                }
                else
                {
                    // offline fallback: read from disk
                    data = Instance.LoadPlayerData(playerId);
                }

                if (!string.IsNullOrEmpty(playerName)) data.PlayerName = playerName;
                if (!string.IsNullOrEmpty(platformId)) { data.PlatformId = platformId; data.Platform = platform ?? data.Platform; }

                // server determines admin status
                try
                {
                    if (ZNet.instance != null)
                    {
                        var peer = ZNet.instance.GetPeerByPlayerName(data.PlayerName);
                        var adminList = ZNet.instance.GetAdminList();
                        string hostPlatform = peer?.m_socket?.GetHostName() ?? string.Empty;
                        if (string.IsNullOrEmpty(data.PlatformId) && !string.IsNullOrEmpty(hostPlatform))
                        {
                            data.PlatformId = hostPlatform;
                            data.Platform = ZNet.m_onlineBackend.ToString();
                        }

                        if (adminList != null && (!string.IsNullOrEmpty(data.PlatformId) && adminList.Contains(data.PlatformId) || (peer != null && adminList.Contains(peer.m_playerName))))
                        {
                            data.IsAdmin = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to determine admin status for {playerId}: {ex.Message}");
                }

                // persist into ZDO if player present, otherwise save to disk
                if (player != null)
                {
                    Instance.SerializeToCustomData(player, data);
                    Instance.SavePlayerDataFromZDO(playerId, player);
                }
                else
                {
                    data.IsDirty = true;
                    Instance.SavePlayerData(playerId, data); // write to disk for later
                }

                FiresLogger.LogVerbose($"[FiresRPGmaker] Processed AnnouncePlayerInfo for {data.PlayerName} (ID {playerId})");

                // Player announced and their vault data is ready — let the Core NPC engine's
                // restore service trigger companion restoration for this peer.
                FiresCore.Bridge.CompanionVaultBridge.RaisePlayerAnnounced(playerId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_AnnouncePlayerInfo exception: {ex.Message}");
            }
        }

        #region Companion Persistence

        /// <summary>
        /// Saves a companion to the player's vault data.
        /// Companions are stored by their unique CompanionId to allow multiple companions per player.
        /// On clients, this sends an RPC to the server to persist the data.
        /// </summary>
        public void SaveCompanion(long playerId, FiresCore.Npc.CompanionSaveData companionData)
        {
            if (playerId == 0 || companionData == null || string.IsNullOrEmpty(companionData.CompanionId))
            {
                Debug.LogWarning("[FiresRPGmaker] SaveCompanion called with invalid parameters");
                return;
            }

            // If we're a client, send RPC to server
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(playerId);
                    string companionJson = JsonConvert.SerializeObject(companionData);
                    pkg.Write(companionJson);
                    SafeInvokeRoutedRPC(0L, "FiresRPGmaker_Vault_SyncCompanion", pkg);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] Client sent companion sync RPC for {companionData.CompanionName} (ID: {companionData.CompanionId})");
                    
                    // Also update local cache so client has immediate access
                    SaveCompanionLocally(playerId, companionData);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to send companion sync RPC: {ex.Message}");
                }
                return;
            }

            // Server-side: save directly
            SaveCompanionLocally(playerId, companionData);
        }

        /// <summary>
        /// Internal method to save companion data locally (used by server and for client cache).
        /// CRITICAL: Companions are SERVER-SPECIFIC and excluded from m_customData to prevent cross-server pollution.
        /// Therefore, we ALWAYS load from disk on the server to get existing companions, not from customData.
        /// On clients, we use playerCache which was populated by the server's RPC_ReceivePlayerData.
        /// </summary>
        /// <param name="playerId">The player's ID</param>
        /// <param name="companionData">The companion data to save</param>
        /// <param name="knownPlayerName">Optional: If we already know the player name (e.g., from peer info), pass it here to avoid Unknown_ file issues</param>
        private void SaveCompanionLocally(long playerId, FiresCore.Npc.CompanionSaveData companionData, string knownPlayerName = null)
        {
            try
            {
                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                bool isServer = ZNet.instance == null || ZNet.instance.IsServer();

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[FiresRPGmaker] SaveCompanionLocally START - isServer={isServer}, playerId={playerId}, companion={companionData?.CompanionName}, knownPlayerName={knownPlayerName}");

                // CRITICAL FIX: Always load from disk on server to get existing companions
                // DeserializeFromCustomData EXCLUDES companions by design (to prevent cross-server pollution)
                // So we must load from disk to preserve existing companion data
                if (isServer)
                {
                    // Check if file exists first - if not, create new data with known player name
                    string existingFile = FindPlayerFileById(playerId);
                    if (!string.IsNullOrEmpty(existingFile) && File.Exists(existingFile))
                    {
                        // Load existing data from disk
                        data = LoadPlayerData(playerId);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Loaded from disk, companions count: {data?.Companions?.Count ?? 0}");
                    }
                    else
                    {
                        // No existing file - create new with known player name to avoid Unknown_ rejection
                        data = new PlayerVaultData(playerId);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - No existing file, created new PlayerVaultData");
                    }
                    
                    // CRITICAL: Set player name from knownPlayerName parameter FIRST (most reliable)
                    // This comes from the peer info and is authoritative
                    if (!string.IsNullOrEmpty(knownPlayerName))
                    {
                        data.PlayerName = knownPlayerName;
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Set player name from knownPlayerName: {knownPlayerName}");
                    }
                    // Only try other sources if we still don't have a valid name
                    else if (string.IsNullOrEmpty(data.PlayerName) || data.PlayerName == "Unknown")
                    {
                        string resolvedName = null;
                        
                        // Try online player first
                        if (player != null)
                        {
                            try { resolvedName = player.GetPlayerName(); } catch { }
                        }
                        
                        // Try ZNet peers if player not found directly
                        if (string.IsNullOrEmpty(resolvedName) && ZNet.instance != null)
                        {
                            try
                            {
                                foreach (var peer in ZNet.instance.GetPeers())
                                {
                                    if (peer != null && peer.IsReady() && peer.m_uid == playerId && !string.IsNullOrEmpty(peer.m_playerName))
                                    {
                                        resolvedName = peer.m_playerName;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                        
                        // Try all players as fallback
                        if (string.IsNullOrEmpty(resolvedName))
                        {
                            try
                            {
                                foreach (var p in Player.GetAllPlayers())
                                {
                                    if (p?.GetPlayerID() == playerId)
                                    {
                                        resolvedName = p.GetPlayerName();
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                        
                        if (!string.IsNullOrEmpty(resolvedName))
                        {
                            data.PlayerName = resolvedName;
                            if (FiresLogger.VerboseEnabled)
                                Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Resolved player name: {resolvedName}");
                        }
                        else
                        {
                            Debug.LogWarning($"[FiresRPGmaker] SaveCompanionLocally - Could not resolve player name for {playerId}, file may be rejected");
                        }
                    }
                }
                else
                {
                    // CLIENT-SIDE: Use playerCache first (populated by server's RPC_ReceivePlayerData)
                    // This contains the authoritative companion data from the server
                    if (playerCache.TryGetValue(playerId, out var cachedData) && cachedData != null)
                    {
                        // Clone the cached data to avoid modifying the cache directly
                        data = new PlayerVaultData(playerId)
                        {
                            PlayerName = cachedData.PlayerName,
                            PlatformId = cachedData.PlatformId,
                            Platform = cachedData.Platform,
                            IsAdmin = cachedData.IsAdmin,
                            LastSeen = cachedData.LastSeen,
                            ActiveQuests = cachedData.ActiveQuests != null ? new List<ActiveQuestData>(cachedData.ActiveQuests) : new List<ActiveQuestData>(),
                            CompletedQuests = cachedData.CompletedQuests != null ? new Dictionary<string, CompletedQuestEntry>(cachedData.CompletedQuests) : new Dictionary<string, CompletedQuestEntry>(),
                            FailedQuests = cachedData.FailedQuests != null ? new List<string>(cachedData.FailedQuests) : new List<string>(),
                            Stats = cachedData.Stats != null ? new Dictionary<string, int>(cachedData.Stats) : new Dictionary<string, int>(),
                            BankItems = cachedData.BankItems != null ? new List<object>(cachedData.BankItems) : new List<object>(),
                            OwnedTerritories = cachedData.OwnedTerritories != null ? new List<object>(cachedData.OwnedTerritories) : new List<object>(),
                            // CRITICAL: Copy companions from cache
                            Companions = cachedData.Companions != null ? new List<object>(cachedData.Companions) : new List<object>()
                        };
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Client loaded from playerCache, companions count: {data.Companions?.Count ?? 0}");
                    }
                    else if (player != null)
                    {
                        // Fallback: use customData (companions excluded, but that's the fallback)
                        data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Client fallback to customData, companions count: {data.Companions?.Count ?? 0}");
                    }
                    else
                    {
                        data = new PlayerVaultData(playerId);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Client created new PlayerVaultData");
                    }
                }

                // Initialize companions list if null
                if (data.Companions == null)
                {
                    data.Companions = new List<object>();
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Initialized empty companions list");
                }

                // Find existing companion with same ID and update, or add new
                bool found = false;
                for (int i = 0; i < data.Companions.Count; i++)
                {
                    try
                    {
                        var existing = ParseCompanionData(data.Companions[i]);
                        if (existing != null && existing.CompanionId == companionData.CompanionId)
                        {
                            data.Companions[i] = companionData;
                            found = true;
                            if (FiresLogger.VerboseEnabled)
                                Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Updated existing companion at index {i}");
                            break;
                        }
                    }
                    catch { }
                }

                if (!found)
                {
                    data.Companions.Add(companionData);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Added new companion, total count: {data.Companions.Count}");
                }

                data.IsDirty = true;

                // Save back
                if (isServer)
                {
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - About to save to disk with {data.Companions.Count} companions");

                    // Server: Save directly to disk (companions are server-specific)
                    SavePlayerData(playerId, data);

                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - SavePlayerData completed");

                    // Also update customData (without companions) if player is online
                    if (player != null)
                    {
                        SerializeToCustomData(player, data);
                    }
                }
                else
                {
                    // Client: Update playerCache so subsequent calls see the updated companions
                    playerCache[playerId] = data;
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SaveCompanionLocally - Client updated playerCache with {data.Companions.Count} companions");
                    
                    // Also update customData (without companions) if player available
                    if (player != null)
                    {
                        SerializeToCustomData(player, data);
                    }
                }

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[FiresRPGmaker] SaveCompanionLocally END - Saved companion {companionData.CompanionName} (ID: {companionData.CompanionId}) to vault for player {playerId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SaveCompanionLocally failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// RPC handler: Client sends companion data to server for persistence.
        /// </summary>
        private static void RPC_SyncCompanion(long sender, ZPackage pkg)
        {
            try
            {
                // Only process on server
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (Instance == null) return;
                if (pkg == null) return;

                long playerId = 0;
                string companionJson = null;

                try { playerId = pkg.ReadLong(); } catch { playerId = 0; }
                try { companionJson = pkg.ReadString(); } catch { companionJson = null; }

                // CRITICAL: Get player info from the sender peer
                // The sender is the peer UID - we can get player name directly from ZNetPeer
                string playerName = null;
                ZNetPeer peer = null;
                
                try
                {
                    peer = ZNet.instance.GetPeer(sender);
                    if (peer != null && peer.IsReady())
                    {
                        playerName = peer.m_playerName;
                        // If playerId is 0 or invalid, use the peer's UID (m_uid is the character ID)
                        if (playerId == 0)
                        {
                            playerId = peer.m_uid;
                        }
                        Debug.Log($"[FiresRPGmaker] RPC_SyncCompanion - Got peer info: playerName={playerName}, playerId={playerId}, peerUid={peer.m_uid}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_SyncCompanion failed to get peer info: {ex.Message}");
                }

                if (playerId == 0 || string.IsNullOrEmpty(companionJson))
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_SyncCompanion invalid payload from {sender} (playerId={playerId})");
                    return;
                }

                FiresCore.Npc.CompanionSaveData companionData = null;
                try
                {
                    companionData = JsonConvert.DeserializeObject<FiresCore.Npc.CompanionSaveData>(companionJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_SyncCompanion failed to deserialize companion data: {ex.Message}");
                    return;
                }

                if (companionData == null || string.IsNullOrEmpty(companionData.CompanionId))
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_SyncCompanion received invalid companion data from {sender}");
                    return;
                }

                // Save companion data on server, passing the resolved player name
                Instance.SaveCompanionLocally(playerId, companionData, playerName);
                
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[FiresRPGmaker] Server received and saved companion sync for {companionData.CompanionName} (ID: {companionData.CompanionId}) from player {playerId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_SyncCompanion exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a companion from the player's vault data.
        /// On clients, this sends an RPC to the server.
        /// </summary>
        public void RemoveCompanion(long playerId, string companionId)
        {
            if (playerId == 0 || string.IsNullOrEmpty(companionId))
            {
                return;
            }

            // If we're a client, send RPC to server
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(playerId);
                    pkg.Write(companionId);
                    SafeInvokeRoutedRPC(0L, "FiresRPGmaker_Vault_RemoveCompanion", pkg);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] Client sent remove companion RPC for {companionId}");
                    
                    // Also update local cache
                    RemoveCompanionLocally(playerId, companionId);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to send remove companion RPC: {ex.Message}");
                }
                return;
            }

            // Server-side: remove directly
            RemoveCompanionLocally(playerId, companionId);
        }

        /// <summary>
        /// Internal method to remove companion locally.
        /// CRITICAL: Like SaveCompanionLocally, we must load from disk on server to get existing companions.
        /// On clients, we use playerCache which was populated by the server's RPC_ReceivePlayerData.
        /// </summary>
        private void RemoveCompanionLocally(long playerId, string companionId)
        {
            try
            {
                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                bool isServer = ZNet.instance == null || ZNet.instance.IsServer();

                // CRITICAL FIX: Always load from disk on server to get existing companions
                // On clients, use playerCache which contains authoritative data from server
                if (isServer)
                {
                    data = LoadPlayerData(playerId);
                }
                else
                {
                    // Client: Use playerCache first (populated by server's RPC_ReceivePlayerData)
                    if (playerCache.TryGetValue(playerId, out var cachedData) && cachedData != null)
                    {
                        // Clone the cached data
                        data = new PlayerVaultData(playerId)
                        {
                            PlayerName = cachedData.PlayerName,
                            Companions = cachedData.Companions != null ? new List<object>(cachedData.Companions) : new List<object>()
                        };
                    }
                    else if (player != null)
                    {
                        data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                    }
                }

                if (data?.Companions == null) return;

                // Find and remove companion with matching ID
                bool removed = false;
                for (int i = data.Companions.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var existing = ParseCompanionData(data.Companions[i]);
                        if (existing != null && existing.CompanionId == companionId)
                        {
                            data.Companions.RemoveAt(i);
                            removed = true;
                            data.IsDirty = true;
                            break;
                        }
                    }
                    catch { }
                }

                // Save back if changed
                if (removed)
                {
                    if (isServer)
                    {
                        // Server: Save directly to disk
                        SavePlayerData(playerId, data);
                        
                        // Also update customData if player is online
                        if (player != null)
                        {
                            SerializeToCustomData(player, data);
                        }
                    }
                    else
                    {
                        // Client: Update playerCache
                        playerCache[playerId] = data;
                        
                        // Also update customData if player available
                        if (player != null)
                        {
                            SerializeToCustomData(player, data);
                        }
                    }

                    // Only log if verbose logging is enabled
                    if (FiresLogger.VerboseEnabled)
                    {
                        Debug.Log($"[FiresRPGmaker] Removed companion {companionId} from vault for player {playerId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RemoveCompanionLocally failed: {ex.Message}");
            }
        }

        /// <summary>
        /// RPC handler: Client requests server to remove a companion.
        /// </summary>
        private static void RPC_RemoveCompanionRPC(long sender, ZPackage pkg)
        {
            try
            {
                // Only process on server
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (Instance == null) return;
                if (pkg == null) return;

                long playerId = 0;
                string companionId = null;

                try { playerId = pkg.ReadLong(); } catch { playerId = 0; }
                try { companionId = pkg.ReadString(); } catch { companionId = null; }

                if (playerId == 0 || string.IsNullOrEmpty(companionId))
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_RemoveCompanionRPC invalid payload from {sender}");
                    return;
                }

                Instance.RemoveCompanionLocally(playerId, companionId);
                
                // Only log on server if verbose logging is enabled
                if (FiresLogger.VerboseEnabled)
                {
                    Debug.Log($"[FiresRPGmaker] Server removed companion {companionId} for player {playerId} (requested by {sender})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_RemoveCompanionRPC exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all companions for a player from vault data.
        /// 
        /// IMPORTANT: Companions are server-specific and MUST be loaded from disk files only,
        /// not from m_customData. This prevents cross-server companion contamination.
        /// 
        /// On the server: Load directly from disk JSON files.
        /// On clients: Use the playerCache which was populated by server RPC.
        /// </summary>
        public List<FiresCore.Npc.CompanionSaveData> GetCompanions(long playerId)
        {
            var result = new List<FiresCore.Npc.CompanionSaveData>();

            if (playerId == 0) return result;

            try
            {
                PlayerVaultData data = null;
                
                // CRITICAL: For companions, we must prioritize server disk data to prevent cross-server pollution.
                // On server: Load from disk files which contain the authoritative companion data.
                // On client: Use cached data from server RPC (playerCache), not m_customData.
                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    // Server: Load directly from disk to get authoritative companion data
                    data = LoadPlayerData(playerId);
                }
                else
                {
                    // Client: Try to get from playerCache first (populated by RPC_ReceivePlayerData)
                    // This is the authoritative data from server
                    if (playerCache.TryGetValue(playerId, out var cachedData))
                    {
                        data = cachedData;
                    }
                    else
                    {
                        // Fallback: Try GetPlayerData which may read from m_customData
                        // Note: m_customData won't have companions due to SerializeToCustomData excluding them
                        data = GetPlayerData(playerId);
                    }
                }
                
                if (data?.Companions == null) return result;

                foreach (var obj in data.Companions)
                {
                    try
                    {
                        var companion = ParseCompanionData(obj);
                        if (companion != null)
                        {
                            result.Add(companion);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] GetCompanions failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Parse companion data from generic object (handles JSON deserialization quirks).
        /// </summary>
        private static FiresCore.Npc.CompanionSaveData ParseCompanionData(object obj)
        {
            if (obj == null) return null;
            if (obj is FiresCore.Npc.CompanionSaveData csd) return csd;

            try
            {
                return JsonConvert.DeserializeObject<FiresCore.Npc.CompanionSaveData>(
                    JsonConvert.SerializeObject(obj));
            }
            catch
            {
                return null;
            }
        }

        #endregion

        // Hybrid core: read from Player.m_customData (runtime source-of-truth)
        public PlayerVaultData GetPlayerData(long playerId)
        {
            if (playerId == 0) return new PlayerVaultData(0);

            PlayerVaultData d = null;
            var player = Player.GetPlayer(playerId);
            if (player != null) d = DeserializeFromCustomData(player);
            if (d == null) d = LoadPlayerData(playerId);

            // kg-parity: a player's ACTIVE quests live client-local in ActiveQuestStore (per-quest m_customData
            // keys), not this vault blob. For the local player, surface the store as the authoritative active set so
            // every reader (HasQuest / tracker / book / dialogue) agrees and a stale blob can never wipe it.
            if (d != null && ActiveQuestStore.IsLoadedFor(playerId))
            {
                d.ActiveQuests = ActiveQuestStore.Active.ToList();
                // Completions are client-local too (SavePlayerData no-ops on clients) — overlay them so
                // CheckRequirements / HasCompletedQuest see a turn-in immediately and QuestFinished chains unlock.
                ActiveQuestStore.ApplyCompletedTo(d);
            }
            return d;
        }

        public PlayerVaultData GetPlayerQuestData(long playerId)
        {
            return GetPlayerData(playerId);
        }

        public ServerVaultData GetServerData() => serverData;

        public void MarkPlayerDirty(long playerId)
        {
            if (playerId == 0) return;
            var player = Player.GetPlayer(playerId);
            if (player != null)
            {
                PlayerSpawnGate.SafeSetCustomData(player, "FiresRPGmaker_Dirty", "1");
            }
        }

        public void MarkServerDirty() => serverData.IsDirty = true;

        // SaveAllDirty: iterate connected Players, call SavePlayerDataFromZDO if dirty
        public void SaveAllDirty()
        {
            // Only the server should write JSON files
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose("SaveAllDirty called on client - skipping");
                return;
            }

            try
            {
                var players = Player.GetAllPlayers();
                if (players != null)
                {
                    foreach (var p in players)
                    {
                        try
                        {
                            SavePlayerDataFromZDO(p.GetPlayerID(), p);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed to save player data for {p.GetPlayerID()}: {ex.Message}");
                        }
                    }
                }

                if (serverData.IsDirty)
                {
                    SaveServerData(serverData);
                    serverData.IsDirty = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SaveAllDirty failed: {ex.Message}");
            }
        }

        // SanitizeFileName: produce a path-safe representation of a real player
        // name. Returns NULL for empty / whitespace / sanitization-collapses-to-
        // empty input, so callers can early-out instead of producing an
        // "Unknown_*" filename. The legacy behavior - returning the literal
        // string "Unknown" as a fallback - was the root of the recurring
        // "Unknown_<id>.json saves rejected downstream" warning class; that
        // fallback is intentionally gone. Per the save-method contract: we
        // write a real player's file, or we write nothing.
        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                var invalid = Path.GetInvalidFileNameChars();
                var filtered = new string(name.Where(c => !invalid.Contains(c)).ToArray());
                filtered = filtered.Replace(' ', '_');
                return string.IsNullOrWhiteSpace(filtered) ? null : filtered;
            }
            catch { return null; }
        }

        // GetPlayerFileName: returns the path-safe save filename for a player,
        // or NULL if the supplied name doesn't resolve to a real value. NULL
        // is the canonical "do not save" signal - every caller must check and
        // abort the save rather than substituting a placeholder.
        private string GetPlayerFileName(long playerId, string playerName)
        {
            // Final gate: an unresolved/placeholder name yields NULL (= do not save), so no code path —
            // guarded or not — can ever mint an "Unknown*"/"Player_*"/"..." file.
            if (IsUnresolvedPlayerName(playerName)) return null;
            var safe = SanitizeFileName(playerName);
            return safe == null ? null : $"{safe}_{playerId}.json";
        }

        // TryResolvePlayerNameById: best-effort resolution of a player's real
        // display name from their player ID. Mirrors the resolver pattern used
        // elsewhere in the mod (see GuildSyncManager.ResolvePlayerName) but
        // omits the synthetic "Player_<id>" fallback - for save-path use we
        // want a definitive yes-or-no answer, not a placeholder. Returns null
        // when no real name is reachable from the current process state, which
        // is the signal to callers that we cannot safely write a file for this
        // player right now.
        //
        // Resolution order:
        //   1. Live in-scene Player MonoBehaviour (Player.GetAllPlayers)
        //   2. ZNet online-peer list (entry.m_characterID.UserID == playerId)
        //
        // Both sources return real display names set by the player. Neither
        // exists in scope of this method while the player is genuinely offline
        // and not in the local scene - exactly the case where no JSON file
        // should be created.
        // A name is "unresolved" (must NOT become a save filename) when it's blank, the "Unknown" placeholder
        // in ANY shape (Unknown, Unknown_<id>, Unknown-<id>, "Unknown 123"), the vanilla mid-load "..." name,
        // or a synthetic "Player_<id>". Centralized so every save gate + the resolver agree — the exact-equals
        // "Unknown" checks let dash/id variants slip straight through to the PlayerFileStorage reject spam.
        public static bool IsUnresolvedPlayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string t = name.Trim();
            if (t == "...") return true;
            if (t.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                // exact "Unknown", or "Unknown" followed only by a separator + digits (placeholder id).
                string rest = t.Substring("Unknown".Length);
                if (rest.Length == 0) return true;
                rest = rest.TrimStart('_', '-', ' ');
                if (rest.Length == 0 || rest.All(char.IsDigit)) return true;
            }
            if (t.StartsWith("Player_", StringComparison.OrdinalIgnoreCase)
                && t.Substring("Player_".Length).All(char.IsDigit)) return true;
            return false;
        }

        private static string TryResolvePlayerNameById(long playerId)
        {
            if (playerId == 0) return null;

            try
            {
                foreach (var p in Player.GetAllPlayers())
                {
                    if (p != null && p.GetPlayerID() == playerId)
                    {
                        var nm = p.GetPlayerName();
                        if (!IsUnresolvedPlayerName(nm)) return nm;
                    }
                }
            }
            catch { }

            try
            {
                var list = ZNet.instance?.GetPlayerList();
                if (list != null)
                {
                    foreach (var entry in list)
                    {
                        if (entry.m_characterID.UserID == playerId
                            && !IsUnresolvedPlayerName(entry.m_name))
                        {
                            return entry.m_name;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private string FindPlayerFileById(long playerId) => FindPlayerFile(PlayersDir, playerId);

        private static string FindPlayerFile(string playersDir, long playerId)
        {
            try
            {
                if (!Directory.Exists(playersDir)) return null;

                // prefer name_id pattern
                var matches = Directory.GetFiles(playersDir, $"*_{playerId}.json");
                if (matches != null && matches.Length > 0)
                    return matches[0];

                // legacy id-only file
                var legacy = Path.Combine(playersDir, $"{playerId}.json");
                if (File.Exists(legacy)) return legacy;

                // orphan zero file
                var zero = Path.Combine(playersDir, "0.json");
                if (File.Exists(zero)) return zero;
            }
            catch (Exception) { }

            return null;
        }

        /// <summary>
        /// Public wrapper around the on-disk loader.  Used by external callers
        /// (e.g. <c>Player_OnSpawned_ServerUpdateVault</c>) that need to read
        /// the canonical disk copy of a player's vault data - typically to
        /// preserve fields like <c>Companions</c> that are deliberately
        /// excluded from <see cref="DeserializeFromCustomData"/>.  Returns
        /// <c>null</c> if no file exists for that player ID.
        /// </summary>
        public PlayerVaultData LoadPlayerDataFromDisk(long playerId)
        {
            return LoadPlayerData(playerId);
        }

        // Load from disk into memory object (used when player not online).
        //
        // Behaviour: returns the on-disk record if a file exists, otherwise
        // creates a fresh PlayerVaultData AND writes it to disk as a sample
        // so the server admin has a concrete file to inspect/edit.
        //
        // CALLERS: anything that wants the canonical "give me this player's
        // data, materialising a default if needed" semantics. SAVE-PATH
        // SAFEGUARDS MUST NOT CALL THIS - see ReadPlayerDataFromDiskNoCreate
        // below for the recursion-safe variant.
        private PlayerVaultData LoadPlayerData(long playerId) => ReadPlayerDataFromDiskNoCreate(playerId) ?? CreateSamplePlayerData(playerId);

        private PlayerVaultData CreateSamplePlayerData(long playerId)
        {
            // No file on disk. Before we write a "sample" record for this
            // player, resolve their real display name. Without a real name we
            // would be writing "Unknown_<id>.json" - exactly the case the save
            // method contract forbids: we save a real player's file, or we
            // write nothing.
            //
            // Returning the in-memory PlayerVaultData with an empty PlayerName
            // is intentional: callers that read-then-mutate-then-save will
            // have their save dropped at SavePlayerData's PlayerName check,
            // which is the correct outcome. The mutation stays in process
            // memory until either (a) the player joins and a real save flow
            // creates their file with a resolved name, or (b) the process
            // exits and the delta is discarded - preferable to writing a
            // phantom file to disk for someone we can't identify.
            var newData = new PlayerVaultData(playerId);

            string resolvedName = TryResolvePlayerNameById(playerId);
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                // No name resolution path available right now. Hand back the
                // transient record without touching disk.
                return newData;
            }

            newData.PlayerName = resolvedName;
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                try
                {
                    // Write the sample file using the resolved name. The file
                    // name is guaranteed to be a real player filename, not
                    // Unknown_*.json.
                    SavePlayerData(playerId, newData);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to create sample player file for {playerId}: {ex.Message}");
                }
            }

            return newData;
        }

        /// <summary>
        /// READ-ONLY disk load. Returns the on-disk PlayerVaultData if a file
        /// exists for <paramref name="playerId"/>, otherwise returns null.
        /// 
        /// CRITICAL: this is the variant that must be used by any
        /// <see cref="SavePlayerData"/> / <see cref="SavePlayerDataFromZDO"/>
        /// companion-wipe safeguard. Those safeguards are "what's already on
        /// disk so I don't overwrite it?" - they must not trigger
        /// <see cref="LoadPlayerData"/>'s "create sample on miss" branch
        /// because that branch calls <see cref="SavePlayerData"/> recursively
        /// to write the sample. On a brand-new world (no save file yet) the
        /// chain is:
        /// 
        ///     SavePlayerData(p, data)            // outer
        ///       safeguard: data.Companions empty
        ///       LoadPlayerData(p)                // miss -> create sample
        ///         SavePlayerData(p, newData)     // inner
        ///           safeguard: newData.Companions empty
        ///           LoadPlayerData(p)            // STILL miss (outer hasn't
        ///                                        //  reached its File.Write
        ///                                        //  yet) -> create sample
        ///             SavePlayerData(p, newer)
        ///               ...
        /// 
        /// Unbounded recursion -> StackOverflowException. The CLR cannot
        /// catch <see cref="StackOverflowException"/> via managed try/catch,
        /// so the process terminates instantly with no managed exception
        /// and no log entry - exactly the symptom that surfaced as the
        /// fresh-world hard-crash on listen-server hosts.
        /// 
        /// The safeguards have been switched to call THIS method instead,
        /// breaking the recursion. They retain identical functional intent
        /// - they just no-op the "create sample" side effect, which the
        /// outer SavePlayerData call is about to do anyway.
        /// </summary>
        private PlayerVaultData ReadPlayerDataFromDiskNoCreate(long playerId) => FinishDiskRead(playerId, ReadPlayerFile(PlayersDir, playerId));

        /// <summary>The disk half of a vault load (find, read, parse): no Unity or game state, so it can run on a worker thread.</summary>
        private sealed class DiskRead
        {
            public PlayerVaultData Data;
            public string Error;
        }

        private static DiskRead ReadPlayerFile(string playersDir, long playerId)
        {
            var read = new DiskRead();
            string file = FindPlayerFile(playersDir, playerId);
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return read;
            try
            {
                read.Data = JsonConvert.DeserializeObject<PlayerVaultData>(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                read.Error = ex.Message;
            }
            return read;
        }

        /// <summary>The main-thread half: the loaded record's timers (server only), or null when there was no usable file.</summary>
        private PlayerVaultData FinishDiskRead(long playerId, DiskRead read)
        {
            if (read.Error != null)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to load player data {playerId}: {read.Error}");
                return null;
            }
            var data = read.Data;
            if (data == null) return null;

            data.IsDirty = false;

            // Auto-schedule missing timers for active quests with time limits (server only)
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                try
                {
                    ScheduleMissingTimersForPlayerData(playerId, data);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to schedule missing timers for player {playerId}: {ex.Message}");
                }
            }

            return data;
        }

        // Auto-schedule missing timers for active quests
        private void ScheduleMissingTimersForPlayerData(long playerId, PlayerVaultData data)
        {
            if (data?.ActiveQuests == null) return;

            var now = DateTime.UtcNow;
            foreach (var activeQuest in data.ActiveQuests.ToList()) // ToList to allow modification
            {
                try
                {
                    var def = QuestManager.Instance?.GetQuestDefinition(activeQuest.QuestId);
                    if (def == null || def.TimeLimitMinutes <= 0) continue;

                    var key = playerId.ToString() + ":" + activeQuest.QuestId;

                    // Check if timer already exists in persistence
                    if (serverData.Timers.ContainsKey(key)) continue;

                    // Calculate expiry from quest start time + time limit
                    var expiry = activeQuest.StartedAt.AddMinutes(def.TimeLimitMinutes);

                    if (expiry <= now)
                    {
                        // Quest has expired - handle it immediately
                        Debug.Log($"[FiresRPGmaker] Found expired quest {activeQuest.QuestId} for player {playerId} - handling expiry");
                        HandleQuestTimerExpired(playerId, activeQuest.QuestId);
                    }
                    else
                    {
                        // Schedule timer for remaining time
                        QuestTimerManager.ScheduleTimerAt(playerId, activeQuest.QuestId, expiry);
                        serverData.Timers[key] = expiry.Ticks;
                        serverData.IsDirty = true;
                        Debug.Log($"[FiresRPGmaker] Auto-scheduled missing timer for quest {activeQuest.QuestId} player {playerId} expiry {expiry:o}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to schedule timer for quest {activeQuest.QuestId}: {ex.Message}");
                }
            }

            if (serverData.IsDirty)
            {
                try { SaveServerData(serverData); } catch { }
            }
        }

        // Save from runtime ZDO (player.m_customData) to disk
        // CRITICAL: Companions are excluded from m_customData to prevent cross-server pollution.
        // We must PRESERVE companions from the existing disk file when saving other data.
        public void SavePlayerDataFromZDO(long playerId, Player player)
        {
            if (playerId == 0) return;
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"Attempted to save player {playerId} on client - skipping");
                return;
            }

            try
            {
                if (!player.m_customData.TryGetValue("FiresRPGmaker_Dirty", out string dirty) || dirty != "1") return;

                var data = DeserializeFromCustomData(player);
                if (data == null)
                {
                    // No customData payload yet (player has joined but the ZDO
                    // hasn't seeded vault state). Resolve the player's real
                    // name from the runtime - first directly from the live
                    // Player object, then from the broader runtime resolver
                    // (online peer list, in-scene Player roster). If neither
                    // yields a real name, this player isn't identifiable to
                    // us right now and we have no business writing a file:
                    // abort cleanly. Skipping is the correct behavior, not
                    // a fallback to "Unknown_<id>.json".
                    string resolvedName = null;
                    try { resolvedName = player.GetPlayerName(); } catch { resolvedName = null; }
                    if (string.IsNullOrWhiteSpace(resolvedName))
                        resolvedName = TryResolvePlayerNameById(playerId);

                    if (string.IsNullOrWhiteSpace(resolvedName))
                    {
                        if (FiresLogger.VerboseEnabled)
                            FiresLogger.LogVerbose(
                                $"SavePlayerDataFromZDO: player {playerId} has no resolved name (no customData, no runtime Player name, no peer match) - skipping save.");
                        return;
                    }

                    data = new PlayerVaultData(playerId) { PlayerName = resolvedName };
                    try
                    {
                        if (ZNet.instance != null)
                        {
                            var peer = ZNet.instance.GetPeerByPlayerName(data.PlayerName);
                            if (peer != null)
                            {
                                data.PlatformId = peer.m_socket?.GetHostName() ?? string.Empty;
                                data.Platform = ZNet.m_onlineBackend.ToString();
                            }
                        }
                    }
                    catch { }
                }

                // Even if DeserializeFromCustomData returned a record, it might
                // have an empty PlayerName (stale customData, partial init) or the
                // legacy RealmLedger placeholder "Unknown" — which sanitizes to a
                // perfectly valid filename and used to sail through to the
                // "Unknown_<id>.json REJECTED" loop (and because the reject aborts
                // before the dirty-marker clear, it retried every cycle forever).
                // Treat the placeholder as unresolved and re-resolve; real name or nothing.
                if (IsUnresolvedPlayerName(data.PlayerName))
                {
                    string reResolved = null;
                    try { reResolved = player.GetPlayerName(); } catch { reResolved = null; }
                    if (IsUnresolvedPlayerName(reResolved))
                        reResolved = TryResolvePlayerNameById(playerId);

                    if (IsUnresolvedPlayerName(reResolved))
                    {
                        if (FiresLogger.VerboseEnabled)
                            FiresLogger.LogVerbose(
                                $"SavePlayerDataFromZDO: player {playerId} has no resolvable PlayerName (customData had '{data.PlayerName}') - skipping save.");
                        return;
                    }
                    data.PlayerName = reResolved;
                }

                // CRITICAL FIX: Preserve companions from disk!
                // DeserializeFromCustomData excludes companions (by design, to prevent cross-server pollution).
                // We must load existing companions from disk and preserve them.
                //
                // RECURSION-SAFE READ: must call ReadPlayerDataFromDiskNoCreate,
                // not LoadPlayerData. LoadPlayerData on a no-file miss recurses
                // back into SavePlayerData to write a sample, which on a
                // brand-new world produces unbounded recursion -> unhandlable
                // StackOverflowException -> instant native process death.
                try
                {
                    var diskData = ReadPlayerDataFromDiskNoCreate(playerId);
                    int diskCompanionCount = diskData?.Companions?.Count ?? 0;
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[FiresRPGmaker] SavePlayerDataFromZDO - Preserving {diskCompanionCount} companions from disk for player {playerId}");
                    if (diskData?.Companions != null && diskData.Companions.Count > 0)
                    {
                        data.Companions = diskData.Companions;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] SavePlayerDataFromZDO - Failed to preserve companions: {ex.Message}");
                }

                Directory.CreateDirectory(PlayersDir);

                // Defense in depth: name was validated above, but if it sanitizes
                // to empty (all-invalid chars) GetPlayerFileName returns null -
                // we don't write rather than fall through to a placeholder.
                string playerFileName = GetPlayerFileName(playerId, data.PlayerName);
                if (playerFileName == null)
                {
                    if (FiresLogger.VerboseEnabled)
                        FiresLogger.LogVerbose(
                            $"SavePlayerDataFromZDO: player {playerId} PlayerName '{data.PlayerName}' sanitized to empty - skipping save.");
                    return;
                }
                string file = Path.Combine(PlayersDir, playerFileName);

                // Clean up any legacy files for this player (legacy id-only or other name variants)
                try
                {
                    // Remove any 'Unknown_*' files that actually belong to this player (parse to verify)
                    try
                    {
                        PlayerFileStorage.DeleteUnknownPrefixedFiles();
                    }
                    catch { }

                    var existing = Directory.GetFiles(PlayersDir, $"*_{playerId}.json");
                    foreach (var f in existing)
                    {
                        if (!string.Equals(f, file, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }

                    // legacy id-only file
                    var legacy = Path.Combine(PlayersDir, $"{playerId}.json");
                    if (File.Exists(legacy))
                    {
                        try { File.Delete(legacy); } catch { }
                    }
                }
                catch { }

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);

                // Save via centralized helper (skips Unknown_* and handles atomic write)
                if (!PlayerFileStorage.SavePlayerFile(file, json))
                {
                    Debug.LogWarning($"[FiresRPGmaker] SavePlayerData skipped/failed for player {playerId}");
                    return;
                }

                // Clear the ZDO dirty marker so this player isn't re-saved next cycle
                // until something actually changes again.
                try { player.m_customData["FiresRPGmaker_Dirty"] = "0"; } catch { }

                Debug.Log($"[FiresRPGmaker] Saved player data for {playerId} -> {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to save player data {playerId}: {ex.Message}");
            }
        }

        // Serialize PlayerVaultData into player's m_customData (triggers ZDO sync)
        // IMPORTANT: Companions are EXCLUDED from m_customData to prevent cross-server contamination.
        // Companions are server-specific and stored only in the server's JSON files.
        public void SerializeToCustomData(Player player, PlayerVaultData data)
        {
            try
            {
                // Create a copy without companions for serialization to m_customData
                // This prevents companions from one server appearing on another server
                var dataForSync = new PlayerVaultData
                {
                    PlayerId = data.PlayerId,
                    PlayerName = data.PlayerName,
                    PlatformId = data.PlatformId,
                    Platform = data.Platform,
                    IsAdmin = data.IsAdmin,
                    LastSeen = data.LastSeen,
                    ActiveQuests = data.ActiveQuests,
                    CompletedQuests = data.CompletedQuests,
                    FailedQuests = data.FailedQuests,
                    Stats = data.Stats,
                    BankItems = data.BankItems,
                    OwnedTerritories = data.OwnedTerritories,
                    // CRITICAL: Exclude companions - they are server-specific
                    Companions = new List<object>(),
                    IsDirty = false
                };
                
                string json = JsonConvert.SerializeObject(dataForSync);
                player.m_customData["FiresRPGmaker_Vault"] = json;
                player.m_customData["FiresRPGmaker_Dirty"] = "1";

                // Avoid accessing internal fields; rely on Valheim's ZDO sync to replicate m_customData.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SerializeToCustomData failed for {player.GetPlayerID()}: {ex.Message}");
            }
        }

        // Deserialize PlayerVaultData from player's m_customData
        /// <summary>0.0.532: a quest turned in or dropped from <see cref="ActiveQuestStore"/> stayed listed in the old vault
        /// (the m_customData blob, and on a host the server record). ActiveQuestStore.Load's one-time migration then copied
        /// it back on every spawn, so a finished quest returned after a relog. Called from ActiveQuestStore.Abandon.</summary>
        internal static void DropActiveQuestFromVault(Player player, string questId)
        {
            if (player == null || string.IsNullOrEmpty(questId)) return;
            try
            {
                var blob = DeserializeFromCustomData(player);
                if (blob?.ActiveQuests != null
                    && blob.ActiveQuests.RemoveAll(q => string.Equals(ActiveQuestStore.Norm(q?.QuestId), questId, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    Instance?.SerializeToCustomData(player, blob);
                    Debug.Log($"[QuestPipe] dropped {questId} from the old vault blob");
                }
                if (Instance != null && ZNet.instance != null && ZNet.instance.IsServer())
                {
                    long pid = player.GetPlayerID();
                    var record = Instance.ReadPlayerDataFromDiskNoCreate(pid);
                    if (record?.ActiveQuests != null
                        && record.ActiveQuests.RemoveAll(q => string.Equals(ActiveQuestStore.Norm(q?.QuestId), questId, StringComparison.OrdinalIgnoreCase)) > 0)
                    {
                        record.IsDirty = true;
                        Instance.SavePlayerData(pid, record);
                        Debug.Log($"[QuestPipe] dropped {questId} from the server vault record");
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[QuestPipe] dropping {questId} from the old vault failed: {ex.Message}"); }
        }

        private static PlayerVaultData DeserializeFromCustomData(Player player)
        {
            try
            {
                if (player.m_customData.TryGetValue("FiresRPGmaker_Vault", out string json) && !string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var data = JsonConvert.DeserializeObject<PlayerVaultData>(json);
                        return data;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to deserialize vault data for {player.GetPlayerID()}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Error reading customData for {player.GetPlayerID()}: {ex.Message}");
            }

            return null;
        }

        // RPC: update stat (now operates on m_customData)
        private static void RPC_UpdateStat(long sender, ZPackage pkg)
        {
            if (!ZNet.instance.IsServer()) return;
            if (Instance == null) return;

            try
            {
                long playerId = pkg.ReadLong();
                string statKey = pkg.ReadString();
                int delta = pkg.ReadInt();

                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                if (player != null)
                {
                    data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                }
                else
                {
                    data = Instance.LoadPlayerData(playerId);
                }

                if (!data.Stats.ContainsKey(statKey)) data.Stats[statKey] = 0;
                data.Stats[statKey] += delta;
                data.IsDirty = true;

                if (player != null)
                {
                    Instance.SerializeToCustomData(player, data);
                    Instance.SavePlayerDataFromZDO(playerId, player);
                }
                else
                {
                    Instance.SavePlayerData(playerId, data);
                }

                Debug.Log($"[FiresRPGmaker] Updated stat {statKey} by {delta} for player {playerId}. New value: {data.Stats[statKey]}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] RPC_UpdateStat exception: {ex}");
            }
        }

        // Client -> Server: notification that a kill occurred. Server applies quest progress to m_customData.
        private static void RPC_KillEvent(long sender, ZPackage pkg)
        {
            try
            {
                if (!ZNet.instance.IsServer()) return;
                if (Instance == null) return;
                if (pkg == null) return;

                long playerId = 0;
                string prefab = null;
                int star = 0;
                try { playerId = pkg.ReadLong(); } catch { }
                try { prefab = pkg.ReadString(); } catch { }
                try { star = pkg.ReadInt(); } catch { }

                if (playerId == 0 || string.IsNullOrEmpty(prefab)) return;

                FiresLogger.LogVerbose($"[FiresRPGmaker] RPC_KillEvent: player={playerId}, prefab={prefab}, star={star}");

                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                if (player != null)
                {
                    data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                }
                else
                {
                    data = Instance.LoadPlayerData(playerId);
                }

                bool anyChange = false;
                for (int i = data.ActiveQuests.Count - 1; i >= 0; i--)
                {
                    // kg AllowMultipleQuestsScore (default false): one kill event scores only the FIRST
                    // matching active quest; the toggle restores every-matching-quest scoring.
                    if (anyChange && !global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.AllowMultipleQuestScore) break;
                    var active = data.ActiveQuests[i];
                    var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                    if (def == null || def.QuestType != "Kill") continue;

                    for (int ti = 0; ti < def.QuestTargets.Count; ti++)
                    {
                        var targetDef = def.QuestTargets[ti];
                        if (!string.Equals(targetDef.Prefab, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                        int cur = active.GetTargetProgress(ti, def.QuestTargets.Count);
                        if (cur >= targetDef.Amount) continue; // objective already done — try another matching target

                        active.SetTargetProgress(ti, cur + 1, def.QuestTargets.Count);
                        anyChange = true;
                        FiresLogger.LogVerbose($"[FiresRPGmaker] Server: Progress +1 for quest {active.QuestId} objective {ti} (player {playerId}): {active.GetTargetProgress(ti, def.QuestTargets.Count)}/{targetDef.Amount}");

                        if (active.AllTargetsComplete(def) && def.Autocomplete)
                        {
                            data.AddCompletedQuest(active.QuestId, 1);
                            data.ActiveQuests.RemoveAt(i);
                            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest completed: {def.Name ?? active.QuestId}");
                            FiresLogger.LogVerbose($"[FiresRPGmaker] Server: Auto-completed {active.QuestId} for player {playerId}");

                            // Persist rewards into player's BankItems (server-side) and notify player
                            try
                            {
                                if (def.Rewards != null && def.Rewards.Count > 0)
                                {
                                    // Per-type dispatch — handles Item / RandomItem / Skill / Skill_EXP / Pet /
                                    // SetCustomValue / AddCustomValue / Mod-bridge stubs (KG parity).
                                    QuestRewardDispatcher.GrantAll(data, player, def.Rewards);
                                }

                                // KG-parity lifecycle: OnCompleteQuest commands + TransferComplition chain follow-up.
                                try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.OnCompleted(def, data, player); } catch { }

                                // send notify to player (sender of RPC_KillEvent will be used by caller)
                                try
                                {
                                    var notifyPkg = new ZPackage();
                                    notifyPkg.Write(active.QuestId ?? string.Empty);
                                    notifyPkg.Write(def.Name ?? active.QuestId ?? string.Empty);
                                    int rewardCount = def.Rewards?.Count ?? 0;
                                    notifyPkg.Write(rewardCount);
                                    if (rewardCount > 0)
                                    {
                                        foreach (var rwd in def.Rewards)
                                        {
                                            notifyPkg.Write(rwd.Type ?? string.Empty);
                                            notifyPkg.Write(rwd.Target ?? string.Empty);
                                            notifyPkg.Write(rwd.Amount);
                                        }
                                    }

                                    SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Quest_CompleteNotify", notifyPkg);
                                }
                                catch (Exception exNotify)
                                {
                                    Debug.LogWarning($"[FiresRPGmaker] Failed to notify player of quest completion: {exNotify.Message}");
                                }
                            }
                            catch (Exception exReward)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] Failed to persist quest rewards for player {playerId}: {exReward.Message}");
                            }
                        }

                        break;
                    }
                }

                if (anyChange)
                {
                    data.IsDirty = true;
                    if (player != null)
                    {
                        Instance.SerializeToCustomData(player, data);
                        Instance.SavePlayerDataFromZDO(playerId, player);
                    }
                    else
                    {
                        Instance.SavePlayerData(playerId, data);
                    }

                    // Send authoritative copy back to the requester so client syncs immediately
                    try
                    {
                        var sendPkg = new ZPackage();
                        data.Serialize(sendPkg);
                        SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_ReceivePlayerData", sendPkg);
                        // Also send ack to prompt client to request player data if needed
                        try
                        {
                            var ackPkg = new ZPackage();
                            ackPkg.Write(playerId);
                            SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_UpdateQuestFlag_Ack", ackPkg);
                        }
                        catch { }
                    }
                    catch (Exception exSend)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send updated player data after KillEvent: {exSend.Message}");
                    }

                    Debug.Log($"[FiresRPGmaker] Server applied kill event for player {playerId} prefab {prefab}");
                }
                else
                {
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Server: KillEvent caused no quest progress for player {playerId} prefab {prefab}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] RPC_KillEvent exception: {ex}");
            }
        }

        /// <summary>Generic progress dispatcher for Collect/Harvest/Craft/Build/Talk quest types (KG parity).
        /// Kill stays on its own RPC for backwards-compat; everything else funnels through here.</summary>
        private static void RPC_QuestProgressEvent(long sender, ZPackage pkg)
        {
            try
            {
                if (!ZNet.instance.IsServer() || Instance == null || pkg == null) return;
                long playerId = pkg.ReadLong();
                string questType = pkg.ReadString();
                string prefab = pkg.ReadString();
                int amount = pkg.ReadInt();
                if (playerId == 0 || string.IsNullOrEmpty(questType) || string.IsNullOrEmpty(prefab)) return;
                ApplyQuestProgressLocal(playerId, questType, prefab, amount);
            }
            catch (Exception ex) { Debug.LogError("[FiresRPGmaker] RPC_QuestProgressEvent exception: " + ex); }
        }

        /// <summary>Move-quest position check. Compares player's xz position to each Move active quest's
        /// target ("x,y,z" string in <c>QuestTarget.Prefab</c>) and counts a hit within 10m.</summary>
        private static void RPC_QuestMoveCheck(long sender, ZPackage pkg)
        {
            try
            {
                if (!ZNet.instance.IsServer() || Instance == null || pkg == null) return;
                long playerId = pkg.ReadLong();
                float x = pkg.ReadSingle();
                float y = pkg.ReadSingle();
                float z = pkg.ReadSingle();
                var position = new Vector3(x, y, z);
                if (Player.GetPlayer(playerId) == null && ZNet.instance != null) QueueMoveCheckUntilRead(playerId, position);
                else ApplyMoveQuestCheckLocal(playerId, position);
            }
            catch (Exception ex) { Debug.LogError("[FiresRPGmaker] RPC_QuestMoveCheck exception: " + ex); }
        }

        // A move check for a player whose object the server hasn't loaded yet (early in a join) read that player's vault file on
        // the main thread every 3 s: 464 ms on the rig dedi (2026-09-28 R18). The file is now read on a worker thread; checks
        // that arrive meanwhile wait, and all of them are applied in order on the main thread against the same record.
        private static readonly Dictionary<long, List<Vector3>> s_movesWaitingForDisk = new Dictionary<long, List<Vector3>>();

        private static void QueueMoveCheckUntilRead(long playerId, Vector3 position)
        {
            if (s_movesWaitingForDisk.TryGetValue(playerId, out var waiting)) { waiting.Add(position); return; }
            s_movesWaitingForDisk[playerId] = new List<Vector3> { position };
            string playersDir = Instance.PlayersDir;
            var read = System.Threading.Tasks.Task.Run(() => ReadPlayerFile(playersDir, playerId));
            ZNet.instance.StartCoroutine(ApplyMovesWhenRead(playerId, read));
        }

        private static System.Collections.IEnumerator ApplyMovesWhenRead(long playerId, System.Threading.Tasks.Task<DiskRead> read)
        {
            while (!read.IsCompleted) yield return null;
            s_movesWaitingForDisk.TryGetValue(playerId, out var moves);
            s_movesWaitingForDisk.Remove(playerId);
            if (Instance == null || QuestManager.Instance == null || moves == null) yield break;

            PlayerVaultData data = null;
            bool finished = false;
            foreach (Vector3 position in moves)
            {
                if (Player.GetPlayer(playerId) != null) { ApplyMoveQuestCheckLocal(playerId, position); continue; }
                if (!finished)
                {
                    DiskRead disk = read.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                        ? read.Result
                        : new DiskRead { Error = read.Exception?.GetBaseException().Message };
                    data = Instance.FinishDiskRead(playerId, disk) ?? Instance.CreateSamplePlayerData(playerId);
                    finished = true;
                }
                ApplyMoveQuestCheck(playerId, position, null, data);
            }
        }

        /// <summary>Shared progress applier — used by both the RPC handler and the inline single-player
        /// path. Walks the player's vault data, finds active quests of the matching type whose targets
        /// hit the prefab, bumps progress, auto-completes if applicable, banks rewards, saves.</summary>
        public static void ApplyQuestProgressLocal(long playerId, string questType, string prefab, int amount)
        {
            if (Instance == null || QuestManager.Instance == null) return;
            var player = Player.GetPlayer(playerId);
            PlayerVaultData data = player != null
                ? (DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId))
                : Instance.LoadPlayerData(playerId);
            if (data == null) return;

            bool anyChange = false;
            for (int i = data.ActiveQuests.Count - 1; i >= 0; i--)
            {
                var active = data.ActiveQuests[i];
                var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                if (def == null) continue;
                if (!string.Equals(def.QuestType, questType, StringComparison.OrdinalIgnoreCase)) continue;
                for (int ti = 0; ti < def.QuestTargets.Count; ti++)
                {
                    var t = def.QuestTargets[ti];
                    if (!string.Equals(t.Prefab, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                    int cur = active.GetTargetProgress(ti, def.QuestTargets.Count);
                    if (cur >= t.Amount) continue;
                    active.SetTargetProgress(ti, Math.Min(t.Amount, cur + Math.Max(1, amount)), def.QuestTargets.Count);
                    anyChange = true;
                    if (active.AllTargetsComplete(def) && def.Autocomplete)
                    {
                        data.AddCompletedQuest(active.QuestId, 1);
                        data.ActiveQuests.RemoveAt(i);
                        try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest completed: " + (def.Name ?? active.QuestId)); } catch { }
                        if (def.Rewards != null)
                            QuestRewardDispatcher.GrantAll(data, player, def.Rewards);
                        try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.OnCompleted(def, data, player); } catch { }
                    }
                    break;
                }
            }
            if (anyChange)
            {
                data.IsDirty = true;
                if (player != null) { Instance.SerializeToCustomData(player, data); Instance.SavePlayerDataFromZDO(playerId, player); }
                else Instance.SavePlayerData(playerId, data);
            }
        }

        /// <summary>CLIENT-LOCAL quest progress. kg-parity moved active quests into <see cref="ActiveQuestStore"/>
        /// (per-quest m_customData), so craft/harvest/build/talk events must bump THAT store — routing them to a
        /// server that holds no copy of these quests silently dropped every non-kill objective. Mirrors
        /// <see cref="ApplyQuestProgressLocal"/>'s matching but writes through the store so it persists + displays.</summary>
        public static void ApplyQuestProgressClientLocal(string questType, string prefab, int amount)
        {
            try
            {
                if (QuestManager.Instance == null) return;
                var player = Player.m_localPlayer;
                if (player == null) return;
                PlayerVaultData data = null;

                bool eventScored = false;
                foreach (var active in ActiveQuestStore.Active.ToList())
                {
                    // kg AllowMultipleQuestsScore (default false): one harvest/craft/build/talk event
                    // scores only the FIRST matching quest unless the toggle enables multi-scoring.
                    if (eventScored && !global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.AllowMultipleQuestScore) break;
                    var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                    if (def?.QuestTargets == null) continue;
                    int tc = def.QuestTargets.Count;
                    for (int ti = 0; ti < tc; ti++)
                    {
                        var t = def.QuestTargets[ti];
                        if (t == null || string.IsNullOrEmpty(t.Prefab)) continue;
                        // Per-objective type ("Item:" collect line etc.) wins over the quest-level type.
                        string objType = !string.IsNullOrEmpty(t.Type) ? t.Type : def.QuestType;
                        if (!string.Equals(objType, questType, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(t.Prefab, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                        int cur = active.GetTargetProgress(ti, tc);
                        if (cur >= t.Amount) break;
                        int next = Math.Min(t.Amount, cur + Math.Max(1, amount));
                        eventScored = true;
                        ActiveQuestStore.SetTargetProgress(active.QuestId, ti, next, tc);
                        Debug.Log($"[QuestPipe] Progress {active.QuestId} obj{ti} {objType}:{prefab} {cur}->{next}/{t.Amount}");

                        if (def.Autocomplete && active.AllTargetsComplete(def))
                        {
                            ActiveQuestStore.MarkCompleted(active.QuestId);
                            ActiveQuestStore.Abandon(active.QuestId);
                            if (data == null) data = Instance?.GetPlayerData(player.GetPlayerID());
                            try { if (def.Rewards != null && def.Rewards.Count > 0) QuestRewardDispatcher.GrantAll(data, player, def.Rewards); } catch { }
                            try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.OnCompleted(def, data, player); } catch { }
                            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest completed: " + (def.Name ?? active.QuestId)); } catch { }
                            Debug.Log($"[QuestPipe] Autocomplete {active.QuestId}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[FiresRPGmaker] ApplyQuestProgressClientLocal failed: " + ex.Message); }
        }

        /// <summary>Move-quest distance check on the player's own client (0.0.532), against the same active-quest store
        /// (m_customData "FiresQuest.&lt;id&gt;") every other objective type uses. Each Move target ("x,y,z" in
        /// <c>QuestTarget.Prefab</c>) within 10 m (x/z) counts one tick through <see cref="ApplyQuestProgressClientLocal"/>,
        /// which logs "[QuestPipe] Progress ..." and handles autocomplete and rewards.</summary>
        public static void ApplyMoveQuestCheckClientLocal(Vector3 position)
        {
            try
            {
                if (QuestManager.Instance == null || Player.m_localPlayer == null) return;
                var ticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var active in ActiveQuestStore.Active.ToList())
                {
                    var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                    if (def?.QuestTargets == null) continue;
                    foreach (var t in def.QuestTargets)
                    {
                        if (t == null || string.IsNullOrEmpty(t.Prefab) || ticked.Contains(t.Prefab)) continue;
                        string objType = !string.IsNullOrEmpty(t.Type) ? t.Type : def.QuestType;
                        if (!string.Equals(objType, "Move", StringComparison.OrdinalIgnoreCase)) continue;
                        var bits = t.Prefab.Split(',');
                        if (bits.Length < 3
                            || !float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tx)
                            || !float.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var tz)) continue;
                        if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(tx, tz)) > 10f) continue;
                        ticked.Add(t.Prefab);
                        ApplyQuestProgressClientLocal("Move", t.Prefab, 1);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[FiresRPGmaker] ApplyMoveQuestCheckClientLocal failed: " + ex.Message); }
        }

        /// <summary>Move-quest distance check (KG parity, every 3s on client). Target stored as
        /// "x,y,z" string in <c>QuestTarget.Prefab</c>; within 10m counts as one progress tick.
        /// 0.0.532: no longer called by the client. It reads the vault blob, which a dedicated server never has for a
        /// remote player (vanilla keeps Player.m_customData in the client's profile), so Move quests never progressed there.
        /// Kept for firesrpg_test and the old RPC.</summary>
        public static void ApplyMoveQuestCheckLocal(long playerId, Vector3 position)
        {
            if (Instance == null || QuestManager.Instance == null) return;
            var player = Player.GetPlayer(playerId);
            PlayerVaultData data = player != null
                ? (DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId))
                : Instance.LoadPlayerData(playerId);
            ApplyMoveQuestCheck(playerId, position, player, data);
        }

        private static void ApplyMoveQuestCheck(long playerId, Vector3 position, Player player, PlayerVaultData data)
        {
            if (data == null) return;

            bool anyChange = false;
            for (int i = data.ActiveQuests.Count - 1; i >= 0; i--)
            {
                var active = data.ActiveQuests[i];
                var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                if (def == null || !string.Equals(def.QuestType, "Move", StringComparison.OrdinalIgnoreCase)) continue;
                for (int ti = 0; ti < def.QuestTargets.Count; ti++)
                {
                    var t = def.QuestTargets[ti];
                    int cur = active.GetTargetProgress(ti, def.QuestTargets.Count);
                    if (cur >= t.Amount) continue;
                    if (string.IsNullOrEmpty(t.Prefab)) continue;
                    var bits = t.Prefab.Split(',');
                    if (bits.Length < 3) continue;
                    if (!float.TryParse(bits[0], out var tx) || !float.TryParse(bits[1], out var ty) || !float.TryParse(bits[2], out var tz)) continue;
                    var target = new Vector3(tx, ty, tz);
                    if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(target.x, target.z)) > 10f) continue;
                    active.SetTargetProgress(ti, Math.Min(t.Amount, cur + 1), def.QuestTargets.Count);
                    anyChange = true;
                    if (active.AllTargetsComplete(def) && def.Autocomplete)
                    {
                        data.AddCompletedQuest(active.QuestId, 1);
                        data.ActiveQuests.RemoveAt(i);
                        try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest completed: " + (def.Name ?? active.QuestId)); } catch { }
                        if (def.Rewards != null)
                            QuestRewardDispatcher.GrantAll(data, player, def.Rewards);
                        try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.OnCompleted(def, data, player); } catch { }
                    }
                    break;
                }
            }
            if (anyChange)
            {
                data.IsDirty = true;
                if (player != null) { Instance.SerializeToCustomData(player, data); Instance.SavePlayerDataFromZDO(playerId, player); }
                else Instance.SavePlayerData(playerId, data);
            }
        }

        // RPC: update quest flags (activate/progress/complete) from clients
        private static void RPC_UpdateQuestFlag(long sender, ZPackage pkg)
        {
            try
            {
                // Accepts/abandons are rare — log receipt unconditionally so a dropped RPC is diagnosable from logs.
                Debug.Log($"[FiresRPGmaker] RPC_UpdateQuestFlag received from {sender} (isServer={(ZNet.instance != null && ZNet.instance.IsServer())})");

                // Ensure ZNet is available
                if (ZNet.instance == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] RPC_UpdateQuestFlag: ZNet.instance is null");
                    return;
                }

                // Only the server should apply quest flag changes
                if (!ZNet.instance.IsServer())
                {
                    if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] RPC_UpdateQuestFlag received on non-server (IsServer={ZNet.instance.IsServer()}) - ignoring");
                    return;
                }

                if (Instance == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] VaultOfKnowledge.Instance is null in RPC_UpdateQuestFlag; ignoring");
                    return;
                }

                if (pkg == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] RPC_UpdateQuestFlag received null package");
                    return;
                }

                long playerId = 0;
                string questId = null;
                string flagType = null;

                try { playerId = pkg.ReadLong(); } catch { }
                try { questId = pkg.ReadString(); } catch { }
                try { flagType = pkg.ReadString(); } catch { }

                if (playerId == 0 || string.IsNullOrEmpty(questId) || string.IsNullOrEmpty(flagType))
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateQuestFlag invalid payload from {sender}");
                    return;
                }

                // Normalize quest id (strip command prefixes like 'startquest') before any lookup
                try
                {
                    questId = NormalizeQuestId(questId);
                }
                catch { }

                if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] RPC_UpdateQuestFlag parsed: sender={sender}, playerId={playerId}, questId={questId}, flagType={flagType}");

                // Delegate to ApplyQuestFlagLocally to ensure correct persistence and optional ack back to requester
                try
                {
                    if (flagType == "progress")
                    {
                        int delta = 1;
                        try { delta = pkg.ReadInt(); } catch { delta = 1; }
                        Instance.ApplyQuestFlagLocally(playerId, questId, "progress", false, delta, sender);
                    }
                    else if (flagType == "active")
                    {
                        bool value = true;
                        try { value = pkg.ReadBool(); } catch { value = true; }

                        // If request came from client (sender !=0) and value==true, perform server-side checks before activating
                        if (value && sender != 0)
                        {
                            // Early cooldown check before processing inventory snapshot
                            try
                            {
                                var def = QuestManager.Instance?.GetQuestDefinition(questId);
                                if (def != null && def.CooldownDays > 0)
                                {
                                    var playerData = Instance.GetPlayerData(playerId);
                                    if (playerData?.CompletedQuests != null && playerData.CompletedQuests.TryGetValue(questId, out var completedEntry))
                                    {
                                        if (ChroniclesOfTime.IsQuestOnCooldown(completedEntry, def.CooldownDays, out DateTime availableAt))
                                        {
                                            string cooldownMsg = ChroniclesOfTime.BuildCooldownMessage(questId, def.Name, availableAt);
                                            Debug.Log($"[FiresRPGmaker] RPC_UpdateQuestFlag: Rejecting quest start due to cooldown - {cooldownMsg}");

                                            // Send rejection to requester
                                            try
                                            {
                                                var rsp = new ZPackage();
                                                rsp.Write(false);
                                                rsp.Write(cooldownMsg);
                                                SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_QuestStartResponse", rsp);
                                            }
                                            catch { }

                                            return; // Do not proceed with activation
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] Cooldown check failed in RPC_UpdateQuestFlag: {ex.Message}");
                            }

                            // gather inventory snapshot from package if provided
                            System.Collections.Generic.List<StoredBankItem> snapshot = null;
                            try
                            {
                                // optional flag: does client include snapshot? next bool indicates presence
                                bool hasSnapshot = false;
                                try { hasSnapshot = pkg.ReadBool(); } catch { hasSnapshot = false; }
                                if (hasSnapshot)
                                {
                                    snapshot = new System.Collections.Generic.List<StoredBankItem>();
                                    int count = 0;
                                    try { count = pkg.ReadInt(); } catch { count = 0; }
                                    for (int si = 0; si < count; si++)
                                    {
                                        try
                                        {
                                            string type = pkg.ReadString();
                                            string prefab = pkg.ReadString();
                                            int amount = pkg.ReadInt();
                                            int quality = pkg.ReadInt();
                                            float durability = -1f;
                                            try
                                            {
                                                var dstr = pkg.ReadString();
                                                if (!float.TryParse(dstr, NumberStyles.Float, CultureInfo.InvariantCulture, out durability))
                                                    durability = -1f;
                                            }
                                            catch { durability = -1f; }
                                            var sb = new StoredBankItem(type, prefab, amount, quality, durability);
                                            snapshot.Add(sb);
                                        }
                                        catch { }
                                    }
                                    // Debug: log snapshot received on server
                                    try
                                    {
                                        Debug.Log($"[FiresRPGmaker] Received inventory snapshot from {sender}: {snapshot.Count} items");
                                        foreach (var sbi in snapshot)
                                        {
                                            try { Debug.Log($"[FiresRPGmaker] Snapshot item from {sender}: Type={sbi.Type}, Prefab={sbi.Prefab}, Amount={sbi.Amount}, Quality={sbi.Quality}, Durability={sbi.Durability}"); } catch { }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { snapshot = null; }

                            // Check requirements via QuestManager
                            try
                            {
                                var def = QuestManager.Instance?.GetQuestDefinition(questId);
                                var missing = new System.Collections.Generic.List<string>();
                                bool ok = true;
                                if (def != null)
                                {
                                    ok = QuestManager.Instance.CheckRequirements(playerId, def, out missing, snapshot);
                                }
                                else
                                {
                                    ok = false;
                                    missing.Add($"Quest not found: {questId}");
                                }

                                if (!ok)
                                {
                                    // This was a fully SILENT server-side exit — log it so failed accepts are diagnosable.
                                    Debug.Log($"[FiresRPGmaker] RPC_UpdateQuestFlag: rejecting '{questId}' for player {playerId} — missing: {string.Join(", ", missing)}");
                                    // Send rejection to requester with missing items
                                    try
                                    {
                                        var rsp = new ZPackage();
                                        rsp.Write(false);
                                        rsp.Write(string.Join(", ", missing));
                                        SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_QuestStartResponse", rsp);
                                    }
                                    catch { }
                                    return;
                                }
                                else
                                {
                                    // send success response to requester
                                    try
                                    {
                                        var rsp = new ZPackage();
                                        rsp.Write(true);
                                        rsp.Write("Quest start accepted");
                                        SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_QuestStartResponse", rsp);
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] Requirement check failed in RPC_UpdateQuestFlag: {ex.Message}");
                            }
                        }

                        Instance.ApplyQuestFlagLocally(playerId, questId, "active", value, 0, sender);
                    }
                    else if (flagType == "complete")
                    {
                        bool value = true;
                        try { value = pkg.ReadBool(); } catch { value = true; }
                        Instance.ApplyQuestFlagLocally(playerId, questId, "complete", value, 0, sender);
                    }
                    else if (flagType == "none")
                    {
                        Instance.ApplyQuestFlagLocally(playerId, questId, "none", false, 0, sender);
                    }

                    // After applying, send authoritative copy back to requester
                    try
                    {
                        var updated = Instance.GetPlayerData(playerId) ?? new PlayerVaultData(playerId);
                        var sendPkg = new ZPackage();
                        updated.Serialize(sendPkg);
                        SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_ReceivePlayerData", sendPkg);
                        if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] Sent authoritative Vault data to {sender} for player {playerId}");
                    }
                    catch (Exception exSend)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send authoritative player data after UpdateQuestFlag: {exSend.Message}");
                    }
                }
                catch (Exception exApply)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateQuestFlag apply failed: {exApply.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] RPC_UpdateQuestFlag crashed: {ex}\n{ex.StackTrace}");
            }
        }

        // Apply quest flag changes locally on server/host without routing an RPC
        public void ApplyQuestFlagLocally(long playerId, string questId, string flagType, bool boolValue, int intValue = 0, long requesterUid = 0)
        {
            try
            {
                Debug.Log($"[FiresRPGmaker] ApplyQuestFlagLocally called: playerId={playerId}, questId={questId}, flagType={flagType}, boolValue={boolValue}, intValue={intValue}, requesterUid={requesterUid}");

                var player = Player.GetPlayer(playerId);
                PlayerVaultData data = null;
                if (player != null)
                    data = DeserializeFromCustomData(player) ?? new PlayerVaultData(playerId);
                else
                    data = LoadPlayerData(playerId);

                if (data == null) return;

                // normalize questId to strip command prefixes like 'startquest '
                questId = NormalizeQuestId(questId);

                // Validate quest exists (server authoritative). Do NOT create fallbacks here.
                QuestDefinition def = null;
                try { def = QuestManager.Instance?.GetQuestDefinition(questId); } catch { def = null; }
                if (def == null)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Refusing to apply quest flag for unknown quest id '{questId}'");
                    return;
                }

                if (flagType == "progress")
                {
                    int delta = intValue != 0 ? intValue : 1;
                    var active = data.ActiveQuests.FirstOrDefault(q => q.QuestId == questId);
                    if (active != null)
                    {
                        // refresh def if needed
                        def = QuestManager.Instance?.GetQuestDefinition(questId) ?? def;

                        // Generic progress bump (no specific objective/prefab context): advance the first
                        // not-yet-complete objective so multi-objective quests progress one at a time.
                        if (def?.QuestTargets != null && def.QuestTargets.Count > 0)
                        {
                            int tc = def.QuestTargets.Count;
                            for (int ti = 0; ti < tc; ti++)
                            {
                                int cur = active.GetTargetProgress(ti, tc);
                                if (cur >= def.QuestTargets[ti].Amount) continue;
                                active.SetTargetProgress(ti, Math.Min(def.QuestTargets[ti].Amount, cur + delta), tc);
                                break;
                            }
                        }
                        else
                        {
                            active.Progress += delta;
                        }

                        if (def != null)
                        {
                            var target = def.QuestTargets.FirstOrDefault();
                            if (target != null && active.AllTargetsComplete(def) && def.Autocomplete)
                            {
                                data.AddCompletedQuest(active.QuestId, 1);
                                data.ActiveQuests.RemoveAll(q => q.QuestId == active.QuestId);
                                try
                                {
                                    QuestTimerManager.CancelTimer(playerId, active.QuestId);
                                    var key = playerId.ToString() + ":" + active.QuestId;
                                    if (Instance.serverData.Timers.ContainsKey(key)) Instance.serverData.Timers.Remove(key);
                                    Instance.serverData.IsDirty = true;
                                    Instance.SaveServerData(Instance.serverData);
                                }
                                catch { }
                                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest completed: {def.Name ?? active.QuestId}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[FiresRPGmaker] ApplyQuestFlagLocally progress: no active quest entry for {questId} on player {playerId}");
                        }
                    }
                }
                else if (flagType == "active")
                {
                    if (boolValue)
                    {
                        // SERVER-SIDE COOLDOWN CHECK: Enforce cooldown before allowing quest activation
                        if (data.CompletedQuests != null && data.CompletedQuests.TryGetValue(questId, out var completedEntry))
                        {
                            if (def.CooldownDays > 0 && ChroniclesOfTime.IsQuestOnCooldown(completedEntry, def.CooldownDays, out DateTime availableAt))
                            {
                                string cooldownMsg = ChroniclesOfTime.BuildCooldownMessage(questId, def.Name, availableAt);
                                Debug.Log($"[FiresRPGmaker] Quest activation rejected - {cooldownMsg}");

                                // Send rejection to requester if provided
                                if (requesterUid != 0)
                                {
                                    try
                                    {
                                        var rsp = new ZPackage();
                                        rsp.Write(false);
                                        rsp.Write(cooldownMsg);
                                        SafeInvokeRoutedRPC(requesterUid, "FiresRPGmaker_Vault_QuestStartResponse", rsp);
                                    }
                                    catch { }
                                }
                                return; // Do not activate quest
                            }
                        }

                        if (!data.ActiveQuests.Any(q => q.QuestId == questId))
                        {
                            data.ActiveQuests.Add(new ActiveQuestData { QuestId = questId, Progress = 0, StartedAt = DateTime.UtcNow });
                            // KG-parity lifecycle: fire OnAcceptQuest commands.
                            try { QuestLifecycleDispatcher.Fire(QuestManager.Instance?.GetQuestDefinition(questId), QuestLifecycleDispatcher.EventOnAccept); } catch { }

                            // Start timer if quest defines a time limit >0
                            try
                            {
                                var tdef = QuestManager.Instance?.GetQuestDefinition(questId);
                                if (tdef != null && tdef.TimeLimitMinutes > 0)
                                {
                                    // compute absolute expiry and persist
                                    var expiry = DateTime.UtcNow.AddMinutes(tdef.TimeLimitMinutes);
                                    QuestTimerManager.ScheduleTimerAt(playerId, questId, expiry);
                                    try
                                    {
                                        var key = playerId.ToString() + ":" + questId;
                                        Instance.serverData.Timers[key] = expiry.Ticks;
                                        Instance.serverData.IsDirty = true;
                                        Instance.SaveServerData(Instance.serverData);
                                    }
                                    catch { }
                                    // notify client of timer start and absolute expiry
                                    try
                                    {
                                        var pkg = new ZPackage();
                                        pkg.Write(questId ?? string.Empty);
                                        pkg.Write(expiry.ToString("o"));
                                        long targetUid = requesterUid != 0 ? requesterUid : 0L;
                                        // if requester not provided try find peer
                                        if (targetUid == 0 && Player.GetPlayer(playerId) != null && ZNet.instance != null)
                                        {
                                            var peer = ZNet.instance.GetPeerByPlayerName(Player.GetPlayer(playerId).GetPlayerName());
                                            if (peer != null) targetUid = peer.m_uid;
                                        }
                                        SafeInvokeRoutedRPC(targetUid, "FiresRPGmaker_Quest_TimerStarted", pkg);
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        data.ActiveQuests.RemoveAll(q => q.QuestId == questId);
                        try
                        {
                            QuestTimerManager.CancelTimer(playerId, questId);
                            var key = playerId.ToString() + ":" + questId;
                            if (Instance.serverData.Timers.ContainsKey(key)) Instance.serverData.Timers.Remove(key);
                            Instance.serverData.IsDirty = true;
                            Instance.SaveServerData(Instance.serverData);
                        }
                        catch { }
                    }
                }
                else if (flagType == "complete")
                {
                    if (boolValue)
                    {
                        data.AddCompletedQuest(questId, 1);
                        data.ActiveQuests.RemoveAll(q => q.QuestId == questId);
                        try
                        {
                            QuestTimerManager.CancelTimer(playerId, questId);
                            var key = playerId.ToString() + ":" + questId;
                            if (Instance.serverData.Timers.ContainsKey(key)) Instance.serverData.Timers.Remove(key);
                            Instance.serverData.IsDirty = true;
                            Instance.SaveServerData(Instance.serverData);
                        }
                        catch { }
                    }
                    else
                    {
                        data.RemoveCompletedQuest(questId);
                    }
                }
                else if (flagType == "none")
                {
                    data.ActiveQuests.RemoveAll(q => q.QuestId == questId);
                    data.RemoveCompletedQuest(questId);
                    // Also remove from failed list
                    if (data.FailedQuests != null)
                    {
                        data.FailedQuests.RemoveAll(fq => string.Equals(fq, questId, StringComparison.OrdinalIgnoreCase));
                    }
                    try
                    {
                        QuestTimerManager.CancelTimer(playerId, questId);
                        var key = playerId.ToString() + ":" + questId;
                        if (Instance.serverData.Timers.ContainsKey(key)) Instance.serverData.Timers.Remove(key);
                        Instance.serverData.IsDirty = true;
                        Instance.SaveServerData(Instance.serverData);
                    }
                    catch { }
                }

                // mark dirty and persist
                data.IsDirty = true;
                if (player != null)
                {
                    SerializeToCustomData(player, data);
                    SavePlayerDataFromZDO(playerId, player);
                    Debug.Log($"[FiresRPGmaker] Applied quest flag locally and saved via ZDO for player {playerId}");
                }
                else
                {
                    SavePlayerData(playerId, data);
                    Debug.Log($"[FiresRPGmaker] Applied quest flag locally and saved to disk for player {playerId}");
                }

                // If a requester UID is provided, send an acknowledgement so client may fetch authoritative copy
                if (requesterUid != 0)
                {
                    try
                    {
                        var ackPkg = new ZPackage();
                        ackPkg.Write(playerId);
                        SafeInvokeRoutedRPC(requesterUid, "FiresRPGmaker_Vault_UpdateQuestFlag_Ack", ackPkg);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send UpdateQuestFlag ack to {requesterUid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] ApplyQuestFlagLocally failed for {playerId}/{questId}: {ex.Message}");
            }
        }

        // Client: server notifies that a quest timer expired
        private static void RPC_QuestTimerExpired(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string questId = string.Empty;
                string questName = string.Empty;
                try { questId = pkg.ReadString(); } catch { questId = string.Empty; }
                try { questName = pkg.ReadString(); } catch { questName = string.Empty; }

                string msg = $"Quest timer expired: {questName} ({questId}). Please try again";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
                Debug.Log($"[FiresRPGmaker] RPC_QuestTimerExpired: {msg}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_QuestTimerExpired exception: {ex.Message}");
            }
        }

        // Client: server notifies that a quest timer was started
        private static void RPC_QuestTimerStarted(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string questId = string.Empty;
                string expiryIso = string.Empty;
                try { questId = pkg.ReadString(); } catch { questId = string.Empty; }
                try { expiryIso = pkg.ReadString(); } catch { expiryIso = string.Empty; }

                if (string.IsNullOrEmpty(questId) || string.IsNullOrEmpty(expiryIso)) return;

                DateTime expiry;
                if (!DateTime.TryParseExact(expiryIso, "o", null, DateTimeStyles.AdjustToUniversal, out expiry)) return;

                // Store into local player's customData under 'FiresRPGmaker_QuestTimers' as JSON mapping.
                // Defer through PlayerSpawnGate so a server push that arrives while we're mid-teleport
                // doesn't write into a half-torn-down Player object.
                string capturedQuestId = questId;
                DateTime capturedExpiry = expiry;
                PlayerSpawnGate.RunWhenLocalReady(30f, local =>
                {
                    try
                    {
                        Dictionary<string, string> map = new Dictionary<string, string>();
                        if (local.m_customData.TryGetValue("FiresRPGmaker_QuestTimers", out string raw) && !string.IsNullOrEmpty(raw))
                        {
                            try { map = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>(); } catch { map = new Dictionary<string, string>(); }
                        }

                        map[capturedQuestId] = capturedExpiry.ToString("o");
                        local.m_customData["FiresRPGmaker_QuestTimers"] = JsonConvert.SerializeObject(map);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_QuestTimerStarted exception: {ex.Message}");
            }
        }

        // Client-side handler: server acknowledges quest flag update; request authoritative player data
        private static void RPC_UpdateQuestFlag_Ack(long sender, ZPackage pkg)
        {
            try
            {
                // Only handle on clients
                if (ZNet.instance != null && ZNet.instance.IsServer()) return;
                if (pkg == null) return;

                long playerId = 0;
                try { playerId = pkg.ReadLong(); } catch { }
                if (playerId == 0) return;

                // Request authoritative copy from server
                try
                {
                    var req = new ZPackage();
                    req.Write(playerId);
                    try { req.Write(Player.m_localPlayer?.GetPlayerName() ?? string.Empty); } catch { req.Write(string.Empty); }
                    SafeInvokeRoutedRPC(0L, "FiresRPGmaker_Vault_RequestPlayerData", req);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateQuestFlag_Ack failed to request player data: {ex.Message}");
                }

                // Server already sends an authoritative FiresRPGmaker_Vault_ReceivePlayerData when it applies changes.
                // The ack simply indicates the server processed the request. Log for debugging and rely on ReceivePlayerData handler.
                try
                {
                    Debug.Log($"[FiresRPGmaker] Received UpdateQuestFlag ack from {sender} for player {playerId}");
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_UpdateQuestFlag_Ack exception: {ex.Message}");
            }
        }

        // RPC handler: client requests server copy of player data
        private static void RPC_RequestPlayerData(long sender, ZPackage pkg)
        {
            try
            {
                if (!ZNet.instance.IsServer()) return;
                if (Instance == null) return;
                if (pkg == null) return;

                long playerId = 0;
                string playerName = null;
                try { playerId = pkg.ReadLong(); } catch { }
                try { playerName = pkg.ReadString(); } catch { }

                if (playerId == 0)
                {
                    Debug.LogWarning($"[FiresRPGmaker] RPC_RequestPlayerData invalid playerId from {sender}");
                    return;
                }

                // Load authoritative data from disk (server)
                var data = Instance.LoadPlayerData(playerId);

                // If the player object is present, also populate their ZDO so it syncs.
                // SerializeToCustomData writes m_customData; defer through the spawn gate so
                // a client-host who is mid-teleport doesn't get hit by a write during engine
                // teardown of the local Player object.
                var player = Player.GetPlayer(playerId);
                if (player != null)
                {
                    var capturedData = data;
                    PlayerSpawnGate.RunWhenReady(player, 30f, p =>
                    {
                        try { Instance.SerializeToCustomData(p, capturedData); }
                        catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Deferred SerializeToCustomData failed for {p.GetPlayerID()}: {ex.Message}"); }
                    });
                }
                else
                {
                    // queue request so when peer spawns we can migrate data back to them
                    lock (Instance.pendingLock)
                    {
                        Instance.pendingRequests[sender] = (playerId, playerName);
                    }
                }

                // Send authoritative copy back to requester
                var sendPkg = new ZPackage();
                data.Serialize(sendPkg);
                SafeInvokeRoutedRPC(sender, "FiresRPGmaker_Vault_ReceivePlayerData", sendPkg);
                FiresLogger.LogVerbose($"[FiresRPGmaker] Responded to RequestPlayerData for {playerId} to {sender}");

                // NOTE: quest-files / QuestDB are NOT re-sent here. They're already pushed:
                //   1. VaultPatches.RPC_PeerInfo (T+0) - eager SendAllQuestFilesToPeer +
                //      BroadcastQuestDatabaseToPeer.
                //   2. CompanionPatches.RPC_CharacterID (after character ZDO spawns) -
                //      ServerConfigFileWatcher.SendAllConfigsToClient pushes everything
                //      (Quests/, QuestProfiles/, Dialogues/, UILayouts/, etc.).
                // Re-pushing here would duplicate the entire config set (visible as a
                // second "Saved synced config" block in the client log).
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_RequestPlayerData exception: {ex.Message}");
            }
        }

        // RPC handler: client receives player data package from server
        private static void RPC_ReceivePlayerData(long sender, ZPackage pkg)
        {
            try
            {
                if (Instance == null) return;
                if (pkg == null) return;

                PlayerVaultData data = null;
                try { data = PlayerVaultData.Deserialize(pkg); } catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_ReceivePlayerData deserialize failed: {ex.Message}"); return; }
                if (data == null) return;

                // Debug: log received active/completed quest info
                try
                {
                    FiresLogger.LogVerbose($"[FiresRPGmaker] RPC_ReceivePlayerData: Received data for player {data.PlayerId} - ActiveQuests={data.ActiveQuests.Count}, Completed={data.CompletedQuests.Count}");
                    foreach (var aq in data.ActiveQuests)
                    {
                        FiresLogger.LogVerbose($"[FiresRPGmaker] Received ActiveQuest: id={aq.QuestId}, progress={aq.Progress}");
                    }
                }
                catch { }

                var local = Player.m_localPlayer;
                if (local != null && local.GetPlayerID() == data.PlayerId)
                {
                    // CHECKPOINT (non-verbose): bracket the deferred customData write.
                    // The hard-freeze investigation needs to know whether vault data landed
                    // before the freeze. Client-side, this writes to m_localPlayer.m_customData.
                    int activeQuests = data.ActiveQuests?.Count ?? 0;
                    int completedQuests = data.CompletedQuests?.Count ?? 0;
                    int companions = data.Companions?.Count ?? 0;
                    int bankItems = data.BankItems?.Count ?? 0;
                    Debug.Log($"[FiresRPGmaker] RPC_ReceivePlayerData scheduling customData write for player {data.PlayerId} (activeQuests={activeQuests}, completedQuests={completedQuests}, companions={companions}, bankItems={bankItems})");

                    // Defer the write - server push can land while the client is mid-teleport
                    // / mid-respawn, and writing to m_customData during engine teardown of the
                    // Player MonoBehaviour has been observed to wedge the loading screen.
                    PlayerVaultData capturedData = data;
                    PlayerSpawnGate.RunWhenLocalReady(30f, lp =>
                    {
                        try
                        {
                            string json = JsonConvert.SerializeObject(capturedData);
                            lp.m_customData["FiresRPGmaker_Vault"] = json;
                            lp.m_customData["FiresRPGmaker_Dirty"] = "0";
                            Debug.Log($"[FiresRPGmaker] RPC_ReceivePlayerData wrote vault data ({json.Length} bytes) into local player customData for {capturedData.PlayerId}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed to write received vault data to local customData: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // store into Instance cache as fallback
                    try
                    {
                        Instance.playerCache[data.PlayerId] = data;
                        FiresLogger.LogVerbose($"[FiresRPGmaker] RPC_ReceivePlayerData stored received player data for {data.PlayerId}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to cache received vault data: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_ReceivePlayerData exception: {ex.Message}");
            }
        }

        // Server -> Client: notify quest completion and rewards
        private static void RPC_QuestCompleteNotify(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string questId = string.Empty;
                string questName = string.Empty;
                try { questId = pkg.ReadString(); } catch { questId = string.Empty; }
                try { questName = pkg.ReadString(); } catch { questName = string.Empty; }
                int rewardCount = 0;
                try { rewardCount = pkg.ReadInt(); } catch { rewardCount = 0; }

                string msg = $"Quest completed: {questName} ({questId})";
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
                Debug.Log($"[FiresRPGmaker] RPC_QuestCompleteNotify: {msg}");

                var local = Player.m_localPlayer;
                if (local != null && rewardCount > 0)
                {
                    // Read all rewards out of the package synchronously (the ZPackage is single-use
                    // and won't survive the deferred frame) - only the customData write is gated.
                    try
                    {
                        PlayerVaultData pv = null;
                        try { pv = VaultOfKnowledge.Instance?.GetPlayerData(local.GetPlayerID()); } catch { pv = null; }
                        if (pv == null) pv = new PlayerVaultData(local.GetPlayerID());

                        for (int i = 0; i < rewardCount; i++)
                        {
                            string type = pkg.ReadString();
                            string target = pkg.ReadString();
                            int amount = 0;
                            try { amount = pkg.ReadInt(); } catch { amount = 0; }
                            pv.AddBankItem(type, target, amount);
                        }

                        PlayerVaultData capturedPv = pv;
                        PlayerSpawnGate.RunWhenLocalReady(30f, lp =>
                        {
                            try
                            {
                                lp.m_customData["FiresRPGmaker_Vault"] = JsonConvert.SerializeObject(capturedPv);
                                lp.m_customData["FiresRPGmaker_Dirty"] = "0";
                                Debug.Log($"[FiresRPGmaker] RPC_QuestCompleteNotify stored rewards into local Vault for player {lp.GetPlayerID()}");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] RPC_QuestCompleteNotify failed to store rewards locally: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] RPC_QuestCompleteNotify failed to store rewards locally: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_QuestCompleteNotify exception: {ex.Message}");
            }
        }

        // Called by server-side spawn hook to satisfy any queued request for the given peer
        public void ProcessPendingRequestForPeer(long peerUid, long playerId)
        {
            (long playerId, string playerName) requested = (0, null);
            lock (pendingLock)
            {
                if (!pendingRequests.TryGetValue(peerUid, out requested)) return;
                pendingRequests.Remove(peerUid);
            }

            long requestedId = requested.playerId;
            string requestedName = requested.playerName;
            long idToUse = playerId != 0 ? playerId : requestedId;
            if (idToUse == 0) return;

            try
            {
                // Attempt to migrate any on-disk peer file into the player's ZDO
                string peerFile = null;
                try
                {
                    var matches = Directory.Exists(PlayersDir) ? Directory.GetFiles(PlayersDir, $"*_{peerUid}.json") : null;
                    if (matches != null && matches.Length > 0)
                        peerFile = matches[0];
                    else
                    {
                        var legacy = Path.Combine(PlayersDir, $"{peerUid}.json");
                        if (File.Exists(legacy)) peerFile = legacy;
                    }
                }
                catch { peerFile = null; }

                if (!string.IsNullOrEmpty(peerFile) && File.Exists(peerFile))
                {
                    try
                    {
                        var jsonPeer = File.ReadAllText(peerFile);
                        var diskPeer = JsonConvert.DeserializeObject<PlayerVaultData>(jsonPeer);
                        if (diskPeer != null)
                        {
                            var player = Player.GetPlayer(playerId);
                            if (player != null)
                            {
                                // SavePlayerDataFromZDO reads the customData we just wrote, so both
                                // ops must run inside the same deferred frame.
                                var capturedDiskPeer = diskPeer;
                                var capturedPlayerId = playerId;
                                var capturedPeerFile = peerFile;
                                PlayerSpawnGate.RunWhenReady(player, 30f, p =>
                                {
                                    try
                                    {
                                        p.m_customData["FiresRPGmaker_Vault"] = JsonConvert.SerializeObject(capturedDiskPeer);
                                        p.m_customData["FiresRPGmaker_Dirty"] = "1";
                                        SavePlayerDataFromZDO(capturedPlayerId, p);
                                        Debug.Log($"[FiresRPGmaker] Migrated on-disk peer file {Path.GetFileName(capturedPeerFile)} -> player {capturedPlayerId}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"[FiresRPGmaker] Deferred peer migration failed for {capturedPlayerId}: {ex.Message}");
                                    }
                                });
                            }
                            try { File.Delete(peerFile); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to read peer file {peerUid}.json: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Error in ProcessPendingRequestForPeer for {peerUid}: {ex.Message}");
            }

            var p = Player.GetPlayer(playerId);
            if (p != null)
            {
                var data = DeserializeFromCustomData(p);
                if (!string.IsNullOrEmpty(requestedName) && data != null && string.IsNullOrEmpty(data.PlayerName))
                {
                    data.PlayerName = requestedName;
                    SerializeToCustomData(p, data);
                }

                var pkg = new ZPackage();
                // keep backward-compatible serialized package
                data = data ?? LoadPlayerData(idToUse);
                data.Serialize(pkg);
                SafeInvokeRoutedRPC(peerUid, "FiresRPGmaker_Vault_ReceivePlayerData", pkg);
                try { FiresLogger.LogVerbose($"[FiresRPGmaker] Sent pending player data to peer {peerUid} (ID {idToUse})"); } catch { }
            }
        }

        public void SavePlayerData(long playerId, PlayerVaultData data)
        {
            // Validate player ID
            if (playerId == 0)
            {
                Debug.LogWarning("[FiresRPGmaker] SavePlayerData: playerId is 0 - skipping");
                return;
            }

            // Validate data
            if (data == null)
            {
                Debug.LogWarning($"[FiresRPGmaker] SavePlayerData: data is null for player {playerId} - skipping");
                return;
            }

            // Contract: we save a real player's file, or we save nothing.
            // An unresolved PlayerName used to fall through to "Unknown_<id>.json"
            // and get rejected downstream - that produced the recurring
            // "REJECTED: Cannot save file with Unknown_ prefix" + "SavePlayerData
            // skipped/failed" warning pair. The literal "Unknown" (legacy
            // RealmLedger default) counts as unresolved too — it sanitizes to a
            // valid filename and previously slipped straight through this guard.
            // Try to re-resolve the real name (player may be online); otherwise skip.
            if (IsUnresolvedPlayerName(data.PlayerName))
            {
                string resolved = TryResolvePlayerNameById(playerId);
                if (IsUnresolvedPlayerName(resolved))
                {
                    if (FiresLogger.VerboseEnabled)
                        FiresLogger.LogVerbose(
                            $"SavePlayerData: player {playerId} has no resolvable PlayerName (had '{data.PlayerName}') - skipping save (caller responsibility to populate PlayerName before invoking).");
                    return;
                }
                data.PlayerName = resolved;
            }

            // Only the server should write JSON files
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose($"SavePlayerData called on client for {playerId} - skipping");
                return;
            }

            try
            {
                // -- COMPANION-WIPE SAFEGUARD -----------------------------------
                // CRITICAL: prevent the "I had 3 companions and now I have 1"
                // bug-class.  Most non-companion code paths build `data` via
                //   GetPlayerData -> DeserializeFromCustomData
                // which by design EXCLUDES companions (see the comments on
                // DeserializeFromCustomData around line 1212/407 - the exclusion
                // is to prevent cross-server companion pollution).  Those callers
                // then update something unrelated (a stat, a quest, a name field,
                // etc.) and call SavePlayerData - which used to blindly serialize
                // the now companion-less object and overwrite the GOOD on-disk
                // copy of the player's companion list.
                //
                // Every legitimate companion-modifying path (CompanionVault, the
                // companion-add/remove flows in this class, vault sync RPCs, etc.)
                // ALWAYS sets data.Companions explicitly before calling here, so a
                // null-or-empty list reaching this method when disk has companions
                // is unambiguously the accidental-wipe pattern.  Detect it, restore
                // from disk, log loudly so the source can be tracked down if it
                // ever comes from a new caller.
                bool incomingHasNoCompanions =
                    data.Companions == null || data.Companions.Count == 0;
                if (incomingHasNoCompanions)
                {
                    try
                    {
                        // RECURSION-SAFE READ - see ReadPlayerDataFromDiskNoCreate
                        // doc-comment for why we cannot call LoadPlayerData here.
                        // Short version: LoadPlayerData on a no-file miss calls
                        // SavePlayerData to write a sample, which is exactly
                        // this method, producing infinite recursion on fresh
                        // worlds and crashing with an uncatchable
                        // StackOverflowException.
                        var diskData = ReadPlayerDataFromDiskNoCreate(playerId);
                        int diskCompanionCount = diskData?.Companions?.Count ?? 0;
                        if (diskCompanionCount > 0)
                        {
                            data.Companions = diskData.Companions;
                            Debug.LogWarning(
                                $"[FiresRPGmaker] SavePlayerData: incoming data had 0 companions but disk had {diskCompanionCount} for player {playerId} - preserved disk copy to prevent companion wipe. " +
                                $"(This is normal when a non-companion-related code path saves player data; the safeguard correctly retained the companion list.)");
                        }
                    }
                    catch (Exception preserveEx)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] SavePlayerData: companion-wipe safeguard read failed for player {playerId}: {preserveEx.Message}");
                    }
                }
                // ===============================================================

                // Ensure players directory exists
                Directory.CreateDirectory(PlayersDir);

                // Build file path. GetPlayerFileName returns null when the name
                // sanitizes to empty (all-invalid chars, whitespace-only, etc.).
                // The top-of-method PlayerName check should catch this already,
                // but defend in depth: a NULL filename here means we can't
                // express this save as a real player file, so we don't write.
                string fileName = GetPlayerFileName(playerId, data.PlayerName);
                if (fileName == null)
                {
                    if (FiresLogger.VerboseEnabled)
                        FiresLogger.LogVerbose(
                            $"SavePlayerData: player {playerId} PlayerName '{data.PlayerName}' sanitized to empty - skipping save.");
                    return;
                }
                string filePath = Path.Combine(PlayersDir, fileName);

                // Clean up legacy files for this player
                CleanupLegacyPlayerFiles(playerId, filePath);

                // Serialize data
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);

                // Log companion data for debugging
                if (FiresLogger.VerboseEnabled && data.Companions != null && data.Companions.Count > 0)
                {
                    Debug.Log($"[FiresRPGmaker] SavePlayerData: {data.Companions.Count} companions for player {playerId}");
                }

                // Save via centralized helper
                if (!PlayerFileStorage.SavePlayerFile(filePath, json))
                {
                    Debug.LogWarning($"[FiresRPGmaker] SavePlayerData: PlayerFileStorage.SavePlayerFile returned false for {fileName}");
                    return;
                }

                Debug.Log($"[FiresRPGmaker] Saved player data: {fileName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] SavePlayerData failed for {playerId}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Cleans up legacy player files (old naming conventions, duplicate files).
        /// </summary>
        private void CleanupLegacyPlayerFiles(long playerId, string currentFilePath)
        {
            try
            {
                // Remove any 'Unknown_*' files
                PlayerFileStorage.DeleteUnknownPrefixedFiles();

                // Remove other files matching *_{playerId}.json that aren't the current file
                if (Directory.Exists(PlayersDir))
                {
                    var existing = Directory.GetFiles(PlayersDir, $"*_{playerId}.json");
                    foreach (var f in existing)
                    {
                        if (!string.Equals(f, currentFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            try 
                            { 
                                File.Delete(f);
                                Debug.Log($"[FiresRPGmaker] Cleaned up legacy file: {Path.GetFileName(f)}");

                                // Also drop the matching .bak / .tmp so they don't accumulate
                                // for players whose character name changed across sessions.
                                try { if (File.Exists(f + ".bak")) File.Delete(f + ".bak"); } catch { }
                                try { if (File.Exists(f + ".tmp")) File.Delete(f + ".tmp"); } catch { }
                            } 
                            catch { }
                        }
                    }

                    // Remove legacy id-only file
                    var legacy = Path.Combine(PlayersDir, $"{playerId}.json");
                    if (File.Exists(legacy))
                    {
                        try 
                        { 
                            File.Delete(legacy);
                            Debug.Log($"[FiresRPGmaker] Cleaned up legacy file: {playerId}.json");
                        } 
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] CleanupLegacyPlayerFiles error: {ex.Message}");
            }
        }

        // Wipe a player's stored vault data completely.
        // This clears any cached copy, removes the player's customData entry (if online), and deletes on-disk files.
        public void WipePlayerVault(long playerId)
        {
            try
            {
                if (playerId == 0) return;

                // Remove from in-memory cache
                try { playerCache.Remove(playerId); } catch { }

                // If player is online, remove their ZDO/customData entry so it syncs out
                try
                {
                    var player = Player.GetPlayer(playerId);
                    if (player != null)
                    {
                        var capturedPlayerId = playerId;
                        PlayerSpawnGate.RunWhenReady(player, 10f, p =>
                        {
                            try
                            {
                                if (p.m_customData.ContainsKey("FiresRPGmaker_Vault"))
                                    p.m_customData.Remove("FiresRPGmaker_Vault");
                                p.m_customData["FiresRPGmaker_Dirty"] = "0";
                                Debug.Log($"[FiresRPGmaker] Cleared in-memory vault data for player {capturedPlayerId}");
                            }
                            catch { }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed clearing in-memory vault data for {playerId}: {ex.Message}");
                }

                // Delete any on-disk files for this player
                try
                {
                    if (Directory.Exists(PlayersDir))
                    {
                        var patterns = new[] { $"*_{playerId}.json", $"{playerId}.json" };
                        foreach (var p in patterns)
                        {
                            var files = Directory.GetFiles(PlayersDir, p);
                            foreach (var f in files)
                            {
                                try { File.Delete(f); Debug.Log($"[FiresRPGmaker] Deleted vault file {Path.GetFileName(f)} for player {playerId}"); } catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed deleting on-disk vault files for {playerId}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] WipePlayerVault failed for {playerId}: {ex.Message}");
            }
        }

        // Called by connection/spawn hooks to indicate a player is joining; the periodic save coroutine can use this
        // to skip the next scheduled save and reset its timer to avoid writing while join is in progress.
        public void NotifyPlayerJoining()
        {
            try
            {
                skipNextScheduledSave = true;
            }
            catch { }
        }

        // Consume and clear the skip flag; returns true if a skip was requested.
        public bool ConsumeSkipNextScheduledSave()
        {
            if (skipNextScheduledSave)
            {
                skipNextScheduledSave = false;
                return true;
            }
            return false;
        }

        // Clear any pending queued request for a peer (called on disconnect to avoid stale queues)
        public void ClearPendingRequest(long peerUid)
        {
            try
            {
                lock (pendingLock)
                {
                    if (pendingRequests.ContainsKey(peerUid)) pendingRequests.Remove(peerUid);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] ClearPendingRequest failed for {peerUid}: {ex.Message}");
            }
        }

        // Load server data from disk
        private void LoadServerData()
        {
            try
            {
                if (File.Exists(ServerFilePath))
                {
                    string json = File.ReadAllText(ServerFilePath);
                    var data = JsonConvert.DeserializeObject<ServerVaultData>(json);
                    if (data != null)
                    {
                        serverData = data;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to load server data: {ex.Message}");
            }
            serverData = new ServerVaultData();
        }

        // Save server data to disk (public wrapper for patches)
        public void SaveServerData(ServerVaultData data)
        {
            SaveServerDataInternal(data);
        }

        // Internal save logic (private)
        private void SaveServerDataInternal(ServerVaultData data)
        {
            try
            {
                Directory.CreateDirectory(BaseSavePath);
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                string tmpFile = ServerFilePath + ".tmp";
                int attempts = 0;
                const int maxAttempts = 3;
                bool saved = false;
                while (!saved)
                {
                    try
                    {
                        File.WriteAllText(tmpFile, json);
                        if (File.Exists(ServerFilePath))
                        {
                            File.Replace(tmpFile, ServerFilePath, null);
                        }
                        else
                        {
                            File.Move(tmpFile, ServerFilePath);
                        }
                        saved = true;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        if (attempts >= maxAttempts)
                        {
                            Debug.LogError($"[FiresRPGmaker] Failed to save server data after {attempts} attempts: {ex.Message}");
                            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
                            break;
                        }
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to save server data: {ex.Message}");
            }
        }

        // RPC: report back to client after quest start/flag update
        private static void RPC_QuestStartResponse(long sender, ZPackage pkg)
        {
            // client-side handler for server response; reads success flag and optional message list
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) return; // only clients
                if (pkg == null) return;
                bool ok = false;
                try { ok = pkg.ReadBool(); } catch { ok = false; }
                string msg = string.Empty;
                try { msg = pkg.ReadString(); } catch { msg = string.Empty; }
                if (!ok)
                {
                    // display the failure message(s)
                    if (!string.IsNullOrEmpty(msg)) MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest start failed: {msg}");
                    Debug.LogWarning($"[FiresRPGmaker] Quest start rejected: {msg}");
                }
                else
                {
                    if (!string.IsNullOrEmpty(msg)) MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg);
                    Debug.Log($"[FiresRPGmaker] Quest start acknowledged: {msg}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_QuestStartResponse exception: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class ServerVaultData
    {
        public bool IsDirty { get; set; } = false;
        // persisted timers: key = "{playerId}:{questId}" -> expiry ticks (UTC)
        public Dictionary<string, long> Timers { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }
}
