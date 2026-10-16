using HarmonyLib;
using System;
using System.Collections;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Quest;
using FiresCore.Lifecycle;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Vault
{
    // World save hook - patch the public ZNet.Save(...) method called in all save paths
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Save))]
    public static class ZNet_Save_VaultSave
    {
        private static void Postfix(bool sync, bool saveOtherPlayerProfiles, bool waitForNextFrame)
        {
            try
            {
                if (VaultOfKnowledge.Instance == null) return;
                if (ZNet.instance == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] ZNet.instance is null during Vault save - skipping");
                    return;
                }

                // CORRECT APPROACH: Iterate through actual Player objects, not peers
                // Player.GetPlayerID() returns the correct ID that matches our vault files
                var allPlayers = Player.GetAllPlayers();
                if (allPlayers != null)
                {
                    foreach (var player in allPlayers)
                    {
                        try
                        {
                            if (player == null) continue;

                            long playerId = player.GetPlayerID();
                            if (playerId == 0) continue;

                            // Only flush players whose data has actually changed since their last save.
                            // Without this check, every world-save (which Valheim runs whenever ANY
                            // peer disconnects) rewrites every connected player's JSON — e.g. player
                            // A logging off causes Hobo's file to be re-saved with no real changes.
                            bool isDirty = false;
                            try
                            {
                                if (player.m_customData != null &&
                                    player.m_customData.TryGetValue("FiresRPGmaker_Dirty", out string dirty) &&
                                    dirty == "1")
                                {
                                    isDirty = true;
                                }
                            }
                            catch { }

                            if (!isDirty) continue;

                            string playerName = player.GetPlayerName();

                            var playerData = VaultOfKnowledge.Instance.GetPlayerData(playerId);
                            if (playerData == null) continue;

                            // Ensure player name is set
                            if (!string.IsNullOrEmpty(playerName) && 
                                (string.IsNullOrEmpty(playerData.PlayerName) || playerData.PlayerName == "Unknown"))
                            {
                                playerData.PlayerName = playerName;
                            }

                            // Use the existing public SavePlayerData method
                            VaultOfKnowledge.Instance.SavePlayerData(playerId, playerData);

                            // Clear the dirty marker so we don't rewrite the same JSON next save.
                            // Idempotent — if the gate is closed and the deferred clear is dropped,
                            // the next world-save just re-saves the same data (harmless).
                            PlayerSpawnGate.SafeSetCustomData(player, "FiresRPGmaker_Dirty", "0", 10f);
                        }
                        catch (Exception exPlayer)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed saving vault data for player during world save: {exPlayer.Message}");
                        }
                    }
                }

                // Also flush server data if dirty
                try
                {
                    if (VaultOfKnowledge.Instance.serverData != null && VaultOfKnowledge.Instance.serverData.IsDirty)
                    {
                        VaultOfKnowledge.Instance.SaveServerData(VaultOfKnowledge.Instance.serverData);
                        VaultOfKnowledge.Instance.serverData.IsDirty = false;
                    }
                }
                catch (Exception exServer)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to save server vault data during world save: {exServer.Message}");
                }

                Debug.Log("[FiresRPGmaker] Vault data flushed to JSON during ZNet.Save()");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Unexpected error in Vault save hook: {ex.Message}");
            }
        }
    }

    // New connection hook - patch ZNet.OnNewConnection(ZNetPeer) so we can record platform info early
    // NOTE: At this point, peer.m_uid may be 0 and peer.m_playerName is empty.
    // We only capture the Steam ID here and defer full vault setup to RPC_PeerInfo or OnSpawned.
    [HarmonyPatch(typeof(ZNet), "OnNewConnection", typeof(ZNetPeer))]
    public static class ZNet_OnNewConnection_VaultRequest
    {
        private static void Postfix(ZNet __instance, ZNetPeer peer)
        {
            if (!__instance.IsServer()) return;
            if (peer == null || VaultOfKnowledge.Instance == null) return;

            try
            {
                // Indicate a player is joining so periodic saves can be skipped briefly
                try { VaultOfKnowledge.Instance.NotifyPlayerJoining(); } catch { }
                
                // At OnNewConnection time, peer.m_uid is often 0 and peer.m_playerName is empty.
                // We can only reliably get the Steam ID from the socket at this point.
                string platformId = peer.m_socket?.GetHostName() ?? string.Empty;
                
                // Store platformId for later use when we have proper peer info
                // Don't create vault entries yet - wait for RPC_PeerInfo or OnSpawned
                if (!string.IsNullOrEmpty(platformId))
                {
                    Debug.Log($"[FiresRPGmaker] OnNewConnection: Captured platformId={platformId} (peer not fully initialized yet, will process on spawn)");
                }
                else
                {
                    Debug.Log("[FiresRPGmaker] OnNewConnection: No platformId available yet");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed in OnNewConnection hook: {ex.Message}");
            }
        }
    }
    
    // Hook into RPC_PeerInfo which is called when the peer sends their character info
    // At this point we have: peer.m_uid (valid), peer.m_playerName, peer.m_characterID
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    public static class ZNet_RPC_PeerInfo_VaultSetup
    {
        private static void Postfix(ZNet __instance, ZRpc rpc, ZPackage pkg)
        {
            if (!__instance.IsServer()) return;
            if (VaultOfKnowledge.Instance == null) return;

            try
            {
                // Find the peer that sent this RPC
                ZNetPeer peer = null;
                foreach (var p in ZNet.instance.GetPeers())
                {
                    if (p != null && p.m_rpc == rpc)
                    {
                        peer = p;
                        break;
                    }
                }
                
                if (peer == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] RPC_PeerInfo: Could not find peer for RPC");
                    return;
                }
                
                long peerUid = peer.m_uid;
                string playerName = peer.m_playerName;
                string platformId = peer.m_socket?.GetHostName() ?? string.Empty;
                
                // At RPC_PeerInfo time, we may not have m_characterID yet (that comes with character spawn)
                // But we have peerUid and playerName which is enough to set up the vault entry
                
                Debug.Log($"[FiresRPGmaker] RPC_PeerInfo: peerUid={peerUid}, playerName={playerName}, platformId={platformId}");
                
                if (peerUid == 0)
                {
                    Debug.LogWarning("[FiresRPGmaker] RPC_PeerInfo: peerUid is still 0, deferring to OnSpawned");
                    return;
                }

                // Create this character's leaderboard row the moment it connects, so a freshly-made character shows
                // on the board immediately (at 0) instead of only after its first recorded stat — the only passive
                // stat is the 30s map-explore tick, so without this a new char is invisible for ~30s. Keyed by name
                // (== OwnerKey), so two characters from one Steam ID each get their own row.
                if (!string.IsNullOrEmpty(playerName))
                {
                    try { FiresCore.Storage.LeaderboardRepository.EnsureRow(playerName.Trim(), playerName); }
                    catch (Exception lex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard GetOrCreate on connect failed: {lex.Message}"); }
                }
                
                // We don't have playerId yet (character not spawned), but we can send quest files now.
                //
                // TIMING NOTE: this fires at RPC_PeerInfo time — T+0 of the connection, before
                // the client has downloaded the world or spawned. FiresGhettoNetworkMod's
                // AutoTune probe gates on Game.m_playerInitialSpawn + 30s settle, which is well
                // after this RPC. With FiresSteamworksPatcher's network-rate bumps the push
                // completes long before the probe begins competing for bandwidth.
                try
                {
                    if (QuestManager.Instance != null)
                    {
                        QuestManager.Instance.SendAllQuestFilesToPeer(peerUid);
                        try { QuestManager.Instance.BroadcastQuestDatabaseToPeer(peerUid); } catch { }
                        // Profiles get the same reliable always-send treatment as the DB — the folder
                        // diff-sync above was leaving clients with 0 profiles ("no quests available").
                        try { QuestManager.Instance.BroadcastQuestProfilesToPeer(peerUid); } catch { }
                        Debug.Log($"[FiresRPGmaker] Sent quest files and QuestDB to peer {peerUid} ({playerName}) eagerly at RPC_PeerInfo");
                    }

                    // REMOVED: ServerConfigFileWatcher.SendAllConfigsToClient(peerUid);
                    // This was sending ALL config files (Quests, Dialogues, Territories, NPCs, etc.)
                    // EVERY login, causing massive bandwidth waste and connection issues.
                    // Individual systems should handle their own smart sync:
                    // - Territories: Already has delta sync with file hashing
                    // - Quests: Sent above (TODO: add delta sync like territories)
                    // - Dialogues: TODO - add delta sync
                    // - NPCs: TODO - only send on first login or when changed
                    // - UILayouts: TODO - only send when changed
                }
                catch (Exception exSend)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to push quest files to peer {peerUid}: {exSend.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_PeerInfo postfix exception: {ex.Message}");
            }
        }
    }

    // Client requests own data on spawn - Player.OnSpawned(bool) is public
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class Player_OnSpawned_LoadVaultData
    {
        private static void Postfix(Player __instance, bool spawnValkyrie)
        {
            // CRITICAL: Sending the announce/request RPCs synchronously
            // from inside Player.OnSpawned can race against the engine's
            // own post-spawn finalisation. The response RPCs come back on
            // a later frame and write to player.m_customData — which has
            // been observed to hard-crash the spawn pipeline on a brand-
            // new world (Player.log truncates mid-spawn, no managed
            // exception). We defer until PlayerSpawnGate confirms the
            // local player is fully spawned (m_nview valid, customData
            // initialised, not teleporting) before sending anything.
            //
            // Hardened against the single-player-fresh-world hang reported in
            // logs that stopped right after "[SavedNpcManager] Registered NPC
            // data sync RPCs". Previously this method had no try/catch, so any
            // exception in InvokeRoutedRPC (e.g. routing self-RPCs before
            // VaultOfKnowledge has finished registering its handlers) would
            // bubble out of the Harmony postfix chain and silently abort the
            // remaining patches, freezing the spawn flow.
            try
            {
                if (__instance != Player.m_localPlayer) return;

                // DIAG (login-freeze)
                FiresLogger.LogVerbose("[LoginFreeze][DIAG] ENTER VaultPatches.Player_OnSpawned_LoadVaultData.Postfix");

                // SINGLE-PLAYER / LISTEN-SERVER GUARD.
                //
                // This whole patch is the *client-side* "ask the server for
                // my saved data" flow. On a listen-server (single-player or
                // hosted-multiplayer with a local character) the host IS the
                // server and routing these RPCs to ZRoutedRpc.Everybody
                // self-routes them back into our own server-side handlers
                // (RPC_AnnouncePlayerInfo / RPC_RequestPlayerData) which
                // then write the host's own m_customData and call
                // SavePlayerData on disk — at the exact same time the
                // separate Player_OnSpawned_ServerUpdateVault postfix is
                // already doing the equivalent server-side work for the
                // same Player object on the same frame.
                //
                // The two paths racing against each other on the host's
                // freshly-spawned Player object reproduces the new-world
                // hard-crash (Player.log ends at "OnSpawned: Could not find
                // peer for player <name>" with no managed exception). The
                // server-side path is sufficient for the host: it already
                // resolves admin status, peer info, and persists to disk.
                // Nothing about the host's situation requires asking
                // itself for data over RPC.
                //
                // Pure clients (connected to a dedicated server) skip past
                // this gate and run normally.
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log("[FiresRPGmaker] Player_OnSpawned_LoadVaultData: host detected — skipping client-side announce/request (server-side ServerUpdateVault covers it)");
                    // 0.0.532: the host's own player still needs its client-local active-quest store. This return used to
                    // skip ActiveQuestStore.Load (RunLoadVaultData), so on a host or in single player the store never loaded:
                    // a book accept flipped back after 6 s, no tracker row appeared and kills never counted. Load only
                    // reads the player's own m_customData, so it doesn't race the server path above.
                    PlayerSpawnGate.RunWhenReady(__instance, 30f, p =>
                    {
                        if (p != Player.m_localPlayer) return;
                        try { global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Load(p); }
                        catch (Exception ex) { Debug.LogWarning("[QuestPipe] ActiveQuestStore.Load (host) failed: " + ex.Message); }
                    });
                    return;
                }

                // 30 s ceiling is generous — the gate normally opens within
                // a frame or two, and a player who hasn't reached "ready"
                // status in 30 s is stuck regardless of what we do.
                PlayerSpawnGate.RunWhenReady(__instance, 30f, RunLoadVaultData);

                // DIAG (login-freeze)
                FiresLogger.LogVerbose("[LoginFreeze][DIAG] EXIT VaultPatches.Player_OnSpawned_LoadVaultData.Postfix (RunLoadVaultData scheduled via PlayerSpawnGate)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Player_OnSpawned_LoadVaultData postfix scheduling failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RunLoadVaultData(Player player)
        {
            try
            {
                if (player != Player.m_localPlayer) return;
                if (ZRoutedRpc.instance == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] Player_OnSpawned_LoadVaultData: ZRoutedRpc.instance is null, skipping vault announce/request");
                    return;
                }

                // In single-player the local player is also the server, and on a
                // brand-new world VaultOfKnowledge.EnsureRpcsRegistered may not have
                // fired yet (no save data to load). Self-routing an RPC to a name
                // the server hasn't registered is harmless on the wire, but if
                // the Vault singleton itself isn't constructed we should not even
                // try — better to defer than to crash the spawn pipeline.
                if (VaultOfKnowledge.Instance == null)
                {
                    Debug.LogWarning("[FiresRPGmaker] Player_OnSpawned_LoadVaultData: VaultOfKnowledge not initialized yet, skipping announce/request (will retry from periodic sync)");
                    return;
                }

                // kg-parity: hydrate the client-local active-quest store from per-quest m_customData keys, and
                // one-time migrate any quests still in the old vault blob. Runs before the tracker/book read.
                try { global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Load(player); }
                catch (Exception ex) { Debug.LogWarning("[QuestPipe] ActiveQuestStore.Load failed: " + ex.Message); }

                long playerId = player.GetPlayerID();
                string playerName = null;
                try { playerName = player.GetPlayerName(); } catch { }

                // Send our character playerId and playerName to server so it can load the correct file and set name early
                var announcePkg = new ZPackage();
                announcePkg.Write(playerId);
                try { announcePkg.Write(playerName ?? string.Empty); } catch { }
                try { announcePkg.Write(string.Empty); } catch { } // platformId (optional)
                try { announcePkg.Write(string.Empty); } catch { } // platform (optional)

                // Build the request package now so the coroutine captures
                // payload data only (no live Player reference).
                var reqPkg = new ZPackage();
                reqPkg.Write(playerId);
                try { reqPkg.Write(playerName ?? string.Empty); } catch { }

                // Announce first so server can save authoritative info
                try
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "FiresRPGmaker_Vault_AnnouncePlayerInfo", announcePkg);
                }
                catch (Exception exAnnounce)
                {
                    Debug.LogWarning($"[FiresRPGmaker] AnnouncePlayerInfo RPC send failed: {exAnnounce.Message}");
                }

                // Stagger the request 250ms after the announce. The two RPCs
                // used to fire back-to-back on the same frame, which on a
                // login storm (multiple players connecting at once) puts the
                // matching server-side handlers on the same tick and forces
                // the response writes to compete for the wire. Spacing them
                // by 250ms gives the announce's RPC_AnnouncePlayerInfo
                // postfix room to finish its disk write before the request's
                // RPC_RequestPlayerData fires the response back. The
                // FiresRPGmaker.Instance host outlives the local Player; we
                // capture only the request payload so a character switch
                // between announce and request is harmless.
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(DeferredVaultRequest(reqPkg));
                }
                else
                {
                    // Fallback: no host MB to drive the coroutine, send immediately.
                    try
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "FiresRPGmaker_Vault_RequestPlayerData", reqPkg);
                    }
                    catch (Exception exReqImmediate)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] RequestPlayerData (immediate fallback) RPC send failed: {exReqImmediate.Message}");
                    }
                }

                Debug.Log("[FiresRPGmaker] Local player announced Vault data on spawn (request staggered 250ms)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Player_OnSpawned_LoadVaultData deferred body failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static IEnumerator DeferredVaultRequest(ZPackage reqPkg)
        {
            yield return new WaitForSeconds(0.25f);

            if (ZRoutedRpc.instance == null)
            {
                Debug.LogWarning("[FiresRPGmaker] DeferredVaultRequest: ZRoutedRpc.instance null at stagger expiry, skipping request");
                yield break;
            }

            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "FiresRPGmaker_Vault_RequestPlayerData", reqPkg);
            }
            catch (Exception exReq)
            {
                Debug.LogWarning($"[FiresRPGmaker] RequestPlayerData (staggered) RPC send failed: {exReq.Message}");
            }
        }
    }

    // Server-side: update vault entry when a player spawns on server (capturing their proper name/ID)
    // This is the authoritative point where we have all player info: playerId, playerName, platformId, etc.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class Player_OnSpawned_ServerUpdateVault
    {
        private static void Postfix(Player __instance, bool spawnValkyrie)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (__instance == null || VaultOfKnowledge.Instance == null) return;

            // CRITICAL: Same crash-window concern as Player_OnSpawned_LoadVaultData.
            // Touching the player's customData (via GetPlayerData → DeserializeFromCustomData,
            // and via the SavePlayerData → SerializeToCustomData chain) before the
            // engine has finished post-spawn finalisation can hard-crash the
            // spawn pipeline. Defer the entire body until PlayerSpawnGate
            // reports the player as ready.
            PlayerSpawnGate.RunWhenReady(__instance, 30f, RunServerUpdate);
        }

        private static void RunServerUpdate(Player player)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (player == null || VaultOfKnowledge.Instance == null) return;

                long playerId = player.GetPlayerID();
                string playerName = player.GetPlayerName();

                if (playerId == 0)
                {
                    Debug.LogWarning($"[FiresRPGmaker] OnSpawned: playerId is 0 for player {playerName}, skipping vault setup");
                    return;
                }

                var data = VaultOfKnowledge.Instance.GetPlayerData(playerId);

                if (!string.IsNullOrEmpty(playerName))
                    data.PlayerName = playerName;

                // Find matching peer by player name to get platform id and admin status
                ZNetPeer peer = null;
                try
                {
                    peer = ZNet.instance.GetPeerByPlayerName(playerName);
                }
                catch { peer = null; }
                
                if (peer != null)
                {
                    var platformId = peer.m_socket?.GetHostName() ?? string.Empty;
                    if (!string.IsNullOrEmpty(platformId))
                    {
                        data.PlatformId = platformId;
                        data.Platform = ZNet.m_onlineBackend.ToString();
                    }

                    // Check admin status
                    try
                    {
                        var adminList = ZNet.instance.GetAdminList();
                        if (adminList != null)
                        {
                            if ((!string.IsNullOrEmpty(platformId) && adminList.Contains(platformId)) || 
                                (!string.IsNullOrEmpty(peer.m_playerName) && adminList.Contains(peer.m_playerName)))
                            {
                                data.IsAdmin = true;
                            }
                        }
                    }
                    catch (Exception) { }

                    // If there was a pending request from this peer, respond now
                    VaultOfKnowledge.Instance.ProcessPendingRequestForPeer(peer.m_uid, playerId);
                    
                    // Ensure client has quest DB: send all quest files to this peer
                    try
                    {
                        if (global::FiresRPGmaker.Modules.Quest.QuestManager.Instance != null)
                        {
                            global::FiresRPGmaker.Modules.Quest.QuestManager.Instance.SendAllQuestFilesToPeer(peer.m_uid);
                            Debug.Log($"[FiresRPGmaker] Sent quest files to peer {peer.m_uid} on spawn");
                        }
                    }
                    catch (Exception exSend) 
                    { 
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send quest files on spawn to {peer.m_uid}: {exSend.Message}"); 
                    }
                    
                    Debug.Log($"[FiresRPGmaker] OnSpawned: Player {playerName} (ID {playerId}) connected from {data.PlatformId}, admin={data.IsAdmin}");
                }
                else if (player == Player.m_localPlayer)
                {
                    // The host's own player (single player, listen server) has no peer: the platform id is the local
                    // user's, admin is the host's, and the quest files are already in this process.
                    data.PlatformId = FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(ZNet.GetUID(), playerId);
                    data.Platform = ZNet.m_onlineBackend.ToString();
                    data.IsAdmin = FiresCore.Sync.AdminSyncing.IsLocalAdmin();
                    VaultOfKnowledge.Instance.ProcessPendingRequestForPeer(ZNet.GetUID(), playerId);
                    Debug.Log($"[FiresRPGmaker] OnSpawned: host player {playerName} (ID {playerId}) as {data.PlatformId}, admin={data.IsAdmin}");
                }
                else
                {
                    Debug.LogWarning($"[FiresRPGmaker] OnSpawned: Could not find peer for player {playerName}");
                }

                data.LastSeen = DateTime.UtcNow;
                data.IsDirty = true;

                // CRITICAL: Preserve companions from disk before saving.
                // GetPlayerData(playerId) above pulls from Player.m_customData via
                // DeserializeFromCustomData, which BY DESIGN excludes companions
                // (anti-cross-server-pollution comment on the deserializer).
                // After a death/bed-respawn the new Player MonoBehaviour starts
                // with partial customData restored; DeserializeFromCustomData
                // returns a non-null PlayerVaultData with name/stats but
                // data.Companions empty.  If we save without preserving the
                // disk copy, the on-disk JSON shrinks from ~8 KB to ~2 KB and
                // the companion list is wiped — exactly the "I had 3 companions
                // and now I have 1" bug.  SavePlayerData itself has a
                // defense-in-depth safeguard for this, but doing it here too
                // means the common respawn path doesn't trip the warning log.
                try
                {
                    bool hasNoCompanions =
                        data.Companions == null || data.Companions.Count == 0;
                    if (hasNoCompanions)
                    {
                        var disk = VaultOfKnowledge.Instance.LoadPlayerDataFromDisk(playerId);
                        if (disk?.Companions != null && disk.Companions.Count > 0)
                        {
                            data.Companions = disk.Companions;
                            Debug.Log($"[FiresRPGmaker] OnSpawned: restored {disk.Companions.Count} companion(s) from disk before vault save (post-respawn / cross-session preservation)");
                        }
                    }
                }
                catch (Exception preserveEx)
                {
                    Debug.LogWarning($"[FiresRPGmaker] OnSpawned: companion-preservation read failed: {preserveEx.Message}");
                }

                // CRITICAL: Actually save the player data to disk now that we have all info
                // This ensures the JSON file is created with the correct player name
                VaultOfKnowledge.Instance.SavePlayerData(playerId, data);

                Debug.Log($"[FiresRPGmaker] Saved vault entry for player {data.PlayerName} (ID {playerId}) on server spawn");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Error updating vault on spawn: {ex.Message}");
            }
        }
    }

    // Handle peer disconnect (server)
    // CRITICAL: We do NOT save vault data on disconnect!
    // At disconnect time, the Player object is already gone (destroyed with their ZDOs),
    // so we can't get accurate data. Any attempt to save results in "Unknown_*" files.
    // Instead, we rely on:
    // 1. Periodic saves (SaveAllDirty) that run while player is connected
    // 2. World saves (ZNet.Save hook) that save all connected players
    // 3. Explicit saves when companions/quest data changes
    [HarmonyPatch(typeof(ZNet), "Disconnect", typeof(ZNetPeer))]
    public static class ZNet_Disconnect_VaultSave
    {
        private static void Postfix(ZNet __instance, ZNetPeer peer)
        {
            try
            {
                if (__instance == null || !__instance.IsServer()) return;
                if (peer == null || VaultOfKnowledge.Instance == null) return;

                try
                {
                    // Clear any pending queued request for this peer (avoid stale requests)
                    VaultOfKnowledge.Instance.ClearPendingRequest(peer.m_uid);

                    // DO NOT attempt to save player data here!
                    // The player is already disconnected and their ZDOs are destroyed.
                    // peer.m_characterID.UserID returns the PEER UID, not the PLAYER ID,
                    // which results in saving to wrong files like "Unknown_27314710.json".
                    // The last valid save was done by periodic saves or companion sync RPCs
                    // while the player was still connected.

                    // Save server data if dirty (this is safe - it's server-global data)
                    try
                    {
                        if (VaultOfKnowledge.Instance.serverData != null && VaultOfKnowledge.Instance.serverData.IsDirty)
                        {
                            VaultOfKnowledge.Instance.SaveServerData(VaultOfKnowledge.Instance.serverData);
                            VaultOfKnowledge.Instance.serverData.IsDirty = false;
                        }
                    }
                    catch (Exception exSrv)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to save server vault data on disconnect: {exSrv.Message}");
                    }
                    
                    Debug.Log($"[FiresRPGmaker] Cleaned up pending requests for disconnected peer {peer.m_uid} (player data already saved by periodic/explicit saves)");
                }
                catch (Exception exInner)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Error handling disconnect for peer {peer?.m_uid}: {exInner.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] ZNet_Disconnect_VaultSave postfix exception: {ex.Message}");
            }
        }
    }
}