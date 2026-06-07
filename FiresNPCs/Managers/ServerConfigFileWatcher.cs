using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Territories;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// Server-side file watcher that monitors config folders for changes and
    /// automatically pushes updates to all connected clients.
    /// Watches: Quests, QuestProfiles, QuestEvents, Dialogues, Traders, Territories, ServerInfos
    /// </summary>
    public class ServerConfigFileWatcher : IDisposable
    {
        private static ServerConfigFileWatcher _instance;
        public static ServerConfigFileWatcher Instance => _instance;

        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>();
        private readonly Dictionary<string, DateTime> _lastEventTimes = new Dictionary<string, DateTime>();
        private readonly object _lockObj = new object();
        
        // Track files recently written by admin sync to avoid re-broadcasting to the sender
        // Key: normalized file path, Value: (timestamp, sender peer UID)
        private readonly Dictionary<string, (DateTime time, long senderUid)> _adminSyncedFiles = new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);
        private const int AdminSyncExpirySeconds = 10; // How long to remember admin-synced files

        private bool _isDisposed;
        private bool _isInitialized;

        // Per-peer "config sync coroutine has finished" flag. Populated at the end of
        // SendAllConfigsToClientCoroutine and queried by CompanionRestoreService so
        // companion adopt + vault restore only runs AFTER the client has received its
        // config files. Cleared on disconnect. This ordering prevents companion ZDO
        // ownership/position writes from racing the config-file traffic during the
        // already-saturated login window.
        private static readonly HashSet<long> _peersWithConfigSyncComplete = new HashSet<long>();

        /// <summary>
        /// Returns true if <see cref="SendAllConfigsToClient"/> has finished pushing
        /// configs to this peer (manifest diff-sync done, UILayouts synced, NPCs
        /// shipped, quest DB broadcast). Other server-side login work (companion
        /// adopt + vault restore) can gate on this to keep the wire/CPU clear during
        /// the config-sync window.
        /// </summary>
        public static bool HasCompletedConfigSync(long peerUid)
        {
            return peerUid != 0L && _peersWithConfigSyncComplete.Contains(peerUid);
        }

        /// <summary>Drop the completion flag when a peer disconnects.</summary>
        public static void ClearConfigSyncComplete(long peerUid)
        {
            if (peerUid == 0L) return;
            _peersWithConfigSyncComplete.Remove(peerUid);
        }

        // Per-folder reload debounce: tracks when we last reloaded each folder to avoid
        // reloading the same subsystem dozens of times when multiple files arrive at once
        private readonly Dictionary<string, Coroutine> _pendingReloads = new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);
        
        // Debounce settings
        private const float DebounceDelaySeconds = 2f;
        private const int MinEventIntervalMs = 500;
        
        // Base config directory
        private static string BaseConfigDir => Path.Combine(Paths.ConfigPath, "FiresRPGmaker");
        
        // Folders to watch
        private static readonly string[] WatchedFolders = new[]
        {
            "Quests",
            "QuestProfiles", 
            "QuestEvents",
            "Dialogues",
            "Traders",
            "Territories",
            "ServerInfos",
            "SavedNPCs",
            "UILayouts"
        };

        /// <summary>
        /// Per-folder manifest namespace used by <see cref="ManifestDiffSync"/>.
        /// Kept prefixed so it can't collide with ad-hoc namespace keys used by other
        /// subsystems (SavedNPCs, UILayouts, ?). The server queries this namespace on
        /// login so clients that already have identical config files on disk get skipped.
        /// </summary>
        private static string ManifestNamespaceFor(string folder) => "Cfg:" + folder;

        /// <summary>
        /// Register every cfg-watched folder with <see cref="ManifestDiffSync"/>
        /// so clients can answer {relativePath -&gt; sha256} manifest queries. Called once
        /// from <c>Ascend.cs</c> during plugin init on BOTH server and client ? the client
        /// is the responder. Folders that are already handled by a dedicated sync path
        /// (UILayouts has its own JSON manifest RPC, SavedNPCs uses the SavedNpcManager
        /// namespace) are skipped here to avoid double-registration.
        /// </summary>
        public static void RegisterManifestFolders()
        {
            foreach (var folder in WatchedFolders)
            {
                if (folder.Equals("UILayouts", StringComparison.OrdinalIgnoreCase)) continue;
                if (folder.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase)) continue;

                string captured = folder; // closure-safe
                ManifestDiffSync.RegisterFolder(
                    ManifestNamespaceFor(captured),
                    () => Path.Combine(BaseConfigDir, captured),
                    "*.cfg",
                    SearchOption.AllDirectories);
            }
        }

        private ServerConfigFileWatcher() { }

        /// <summary>
        /// Initialize the file watcher system on the server.
        /// Should only be called on dedicated server or host.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null && _instance._isInitialized)
            {
                Debug.Log("[FiresRPGmaker] ServerConfigFileWatcher already initialized");
                return;
            }

            // Only run on server
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose("[FiresRPGmaker] ServerConfigFileWatcher skipped - not server");
                return;
            }

            _instance = new ServerConfigFileWatcher();
            _instance.SetupWatchers();
            _instance._isInitialized = true;
            
            Debug.Log("[FiresRPGmaker] ServerConfigFileWatcher initialized - watching config folders");
        }

        /// <summary>
        /// Shutdown and dispose all watchers.
        /// </summary>
        public static void Shutdown()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
        }

        private void SetupWatchers()
        {
            try
            {
                Directory.CreateDirectory(BaseConfigDir);

                foreach (var folder in WatchedFolders)
                {
                    string folderPath = Path.Combine(BaseConfigDir, folder);
                    
                    try
                    {
                        Directory.CreateDirectory(folderPath);
                        CreateWatcher(folder, folderPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to create watcher for {folder}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] ServerConfigFileWatcher setup failed: {ex.Message}");
            }
        }

        private void CreateWatcher(string folderName, string folderPath)
        {
            if (_watchers.ContainsKey(folderName))
            {
                var existing = _watchers[folderName];
                if (existing != null)
                {
                    existing.Dispose();
                }
                _watchers.Remove(folderName);
            }

            string filter = (folderName.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase)
                          || folderName.Equals("UILayouts", StringComparison.OrdinalIgnoreCase))
                          ? "*.json" : "*.cfg";
            var watcher = new FileSystemWatcher
            {
                Path = folderPath,
                Filter = filter,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            watcher.Changed += (s, e) => OnFileEvent(folderName, e.FullPath, e.ChangeType);
            watcher.Created += (s, e) => OnFileEvent(folderName, e.FullPath, e.ChangeType);
            watcher.Deleted += (s, e) => OnFileEvent(folderName, e.FullPath, e.ChangeType);
            watcher.Renamed += (s, e) => OnFileEvent(folderName, e.FullPath, WatcherChangeTypes.Changed);
            watcher.Error += (s, e) => Debug.LogWarning($"[FiresRPGmaker] FileWatcher error in {folderName}: {e.GetException()?.Message}");

            _watchers[folderName] = watcher;
            
            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Created file watcher for {folderName} at {folderPath}");
        }

        private void OnFileEvent(string folderName, string fullPath, WatcherChangeTypes changeType)
        {
            if (_isDisposed) return;

            try
            {
                // Debounce check
                string key = fullPath.ToLowerInvariant();
                var now = DateTime.UtcNow;
                
                lock (_lockObj)
                {
                    if (_lastEventTimes.TryGetValue(key, out var lastTime))
                    {
                        if ((now - lastTime).TotalMilliseconds < MinEventIntervalMs)
                            return;
                    }
                    _lastEventTimes[key] = now;
                }

                string fileName = Path.GetFileName(fullPath);
                
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Config file change detected: {folderName}/{fileName} ({changeType})");

                // Use coroutine for debounced processing
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(ProcessFileChangeDelayed(folderName, fullPath, fileName, changeType));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Error handling file event: {ex.Message}");
            }
        }

        private IEnumerator ProcessFileChangeDelayed(string folderName, string fullPath, string fileName, WatcherChangeTypes changeType)
        {
            // Wait for debounce period
            yield return new WaitForSeconds(DebounceDelaySeconds);

            if (_isDisposed) yield break;

            // Check if this file was recently written by a server?client sync.
            // On non-dedicated hosts both server and client code run in the same process,
            // so a client-side write from RPC_FileUpdate triggers the server's file watcher.
            // We must suppress this to prevent a re-broadcast loop.
            if (DialogueSyncRPC.IsRecentServerWrite(fullPath))
            {
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Suppressed re-broadcast for server-synced file: {folderName}/{fileName}");
                yield break;
            }

            // Read file content (empty if deleted)
            string content = string.Empty;
            bool readSucceeded = false;
            
            if (changeType != WatcherChangeTypes.Deleted && File.Exists(fullPath))
            {
                // First attempt
                try
                {
                    content = File.ReadAllText(fullPath);
                    readSucceeded = true;
                }
                catch (IOException)
                {
                    // File might still be locked, will retry
                    readSucceeded = false;
                }
            }
            else if (changeType == WatcherChangeTypes.Deleted)
            {
                readSucceeded = true; // Empty content is correct for deleted files
            }
            
            // Retry if first attempt failed
            if (!readSucceeded && changeType != WatcherChangeTypes.Deleted)
            {
                yield return new WaitForSeconds(0.5f);
                
                if (File.Exists(fullPath))
                {
                    try
                    {
                        content = File.ReadAllText(fullPath);
                        readSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to read file {fileName} after retry: {ex.Message}");
                        yield break;
                    }
                }
            }

            // Get relative file name for subfolders (e.g., Dialogues/legacy/sample.cfg)
            string baseFolder = Path.Combine(BaseConfigDir, folderName);
            string relativePath = fileName;
            try
            {
                if (fullPath.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = fullPath.Substring(baseFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            catch { relativePath = fileName; }

            // Process the change
            ProcessConfigChange(folderName, relativePath, content, changeType);
        }

        private void ProcessConfigChange(string folderName, string fileName, string content, WatcherChangeTypes changeType)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            // Check if this file was recently synced by an admin client
            string fileKey = Path.Combine(folderName, fileName).ToLowerInvariant();
            long excludeSender = -1;
            
            lock (_lockObj)
            {
                if (_adminSyncedFiles.TryGetValue(fileKey, out var syncInfo))
                {
                    // Check if the sync record is still valid (not expired)
                    if ((DateTime.UtcNow - syncInfo.time).TotalSeconds < AdminSyncExpirySeconds)
                    {
                        excludeSender = syncInfo.senderUid;
                        Debug.Log($"[FiresRPGmaker] Config change from admin sync - will exclude sender {excludeSender} from broadcast: {folderName}/{fileName}");
                    }
                    // Remove the entry (one-time use)
                    _adminSyncedFiles.Remove(fileKey);
                }
            }

            Debug.Log($"[FiresRPGmaker] Processing config change: {folderName}/{fileName} ({changeType})");

            // SavedNPCs uses its own dedicated RPC format � let SavedNpcManager handle everything
            if (folderName.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase))
            {
                SavedNpcManager.OnServerFileChanged(fileName);
                return;
            }

            // UILayouts uses UILayoutSyncRPC for chunked/compressed JSON transfer
            if (folderName.Equals("UILayouts", StringComparison.OrdinalIgnoreCase))
            {
                UILayoutCodex.Reload();

                var peers = ZNet.instance?.GetPeers();
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        if (peer == null || !peer.IsReady()) continue;
                        if (excludeSender > 0 && peer.m_uid == excludeSender) continue;
                        try
                        {
                            UILayoutSyncRPC.SendLayoutToClient(
                                peer.m_uid, fileName, content);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed to send UILayout {fileName} to peer {peer.m_uid}: {ex.Message}");
                        }
                    }
                }

                Debug.Log($"[FiresRPGmaker] Broadcasted UILayout '{fileName}' to {peers?.Count ?? 0} client(s)");
                return;
            }

            // 1. Reload the relevant system on server
            ReloadServerData(folderName);

            // 3. Broadcast to all connected clients (excluding the admin who pushed if applicable)
            BroadcastFileToAllClients(folderName, fileName, content, excludeSender);

            // 4. Perform any post-sync actions
            PerformPostSyncActions(folderName, excludeSender);
        }

        private void ReloadServerData(string folderName)
        {
            string key = folderName.ToLowerInvariant();

            // Territories already have their own debounce via TerritorySyncedState
            if (key == "territories")
            {
                TerritorySyncedState.ScheduleDebouncedReload();
                return;
            }

            // For all other folders, debounce: if a reload coroutine is already pending
            // for this folder, restart it so the actual reload only fires once after
            // all files have been written.
            if (FiresRPGmaker.Instance != null)
            {
                if (_pendingReloads.TryGetValue(key, out var existing) && existing != null)
                    FiresRPGmaker.Instance.StopCoroutine(existing);
                _pendingReloads[key] = FiresRPGmaker.Instance.StartCoroutine(DebouncedReloadCoroutine(key));
            }
            else
            {
                ExecuteReload(key);
            }
        }

        private IEnumerator DebouncedReloadCoroutine(string folderKey)
        {
            yield return new WaitForSeconds(1f);
            _pendingReloads.Remove(folderKey);
            ExecuteReload(folderKey);
        }

        private void ExecuteReload(string folderKey)
        {
            try
            {
                switch (folderKey)
                {
                    case "quests":
                        if (QuestManager.Instance != null)
                        {
                            QuestManager.Instance.LoadQuestDatabase();
                            Debug.Log($"[FiresRPGmaker] Reloaded quest database; count={QuestManager.Instance.GetAllQuests().Count}");
                        }
                        break;

                    case "questprofiles":
                        if (QuestManager.Instance != null)
                        {
                            QuestManager.Instance.LoadQuestProfiles();
                            Debug.Log("[FiresRPGmaker] Reloaded quest profiles");
                        }
                        break;

                    case "questevents":
                        Debug.Log("[FiresRPGmaker] QuestEvents folder changed; clients will receive updated files");
                        break;

                    case "dialogues":
                        try
                        {
                            var initField = typeof(DialogueCodex).GetField("_initialized", 
                                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                            if (initField != null) initField.SetValue(null, false);
                            DialogueCodex.Init();
                            Debug.Log($"[FiresRPGmaker] Reloaded dialogues; count={DialogueCodex.Dialogues.Count}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed to reload dialogues: {ex.Message}");
                        }
                        break;

                    case "territories":
                        TerritorySyncedState.ScheduleDebouncedReload();
                        break;

                    case "serverinfos":
                        global::FiresRPGmaker.UI.Screens.InfoNpcScreenController.ReloadCache();
                        Debug.Log("[FiresRPGmaker] Reloaded ServerInfos cache");
                        break;

                    case "traders":
                        TraderProfileManager.Reset();
                        TraderProfileManager.Initialize();
                        Debug.Log($"[FiresRPGmaker] Reloaded trader profiles; count={TraderProfileManager.GetAllProfileNames().Count()}");
                        break;

                    case "savednpcs":
                        SavedNpcManager.Reload();
                        Debug.Log($"[FiresRPGmaker] Reloaded SavedNPCs from disk");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to reload server data for {folderKey}: {ex.Message}");
            }
        }

        // Max bytes of string content per RPC message. Valheim's network layer starts
        // struggling around 64KB per routed RPC, so we stay well under that.
        private const int MaxChunkBytes = 32000;

        private void BroadcastFileToAllClients(string folderName, string fileName, string content, long excludeSender = -1)
        {
            if (ZRoutedRpc.instance == null) return;

            try
            {
                var peers = ZNet.instance?.GetPeers();
                if (peers == null || peers.Count == 0)
                {
                    if (FiresLogger.VerboseEnabled)
                        FiresLogger.LogVerbose("[FiresRPGmaker] No connected peers to broadcast config change");
                    return;
                }

                // Determine the appropriate RPC based on folder type
                string rpcName = GetRpcNameForFolder(folderName);
                if (string.IsNullOrEmpty(rpcName)) return;

                int successCount = 0;
                int skippedCount = 0;
                foreach (var peer in peers)
                {
                    if (peer == null || !peer.IsReady()) continue;

                    // Skip the sender if this was an admin-pushed config
                    if (excludeSender > 0 && peer.m_uid == excludeSender)
                    {
                        skippedCount++;
                        if (FiresLogger.VerboseEnabled)
                            FiresLogger.LogVerbose($"[FiresRPGmaker] Skipping {folderName}/{fileName} broadcast to sender {peer.m_uid}");
                        continue;
                    }

                    try
                    {
                        SendFileToClient(peer.m_uid, rpcName, folderName, fileName, content);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send {folderName}/{fileName} to peer {peer.m_uid}: {ex.Message}");
                    }
                }

                string skipMsg = skippedCount > 0 ? $" (skipped {skippedCount} sender)" : "";
                Debug.Log($"[FiresRPGmaker] Broadcasted {folderName}/{fileName} to {successCount} clients{skipMsg}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to broadcast file to clients: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a config file to a specific client, chunking if the content exceeds MaxChunkBytes.
        /// Small files are sent as a single RPC for simplicity. Large files are split into
        /// numbered chunks that the client reassembles via <see cref="DialogueSyncRPC"/>.
        ///
        /// Single-message format:  folder, fileName, content  (totalChunks=0, no chunk header)
        /// Chunked format:         folder, fileName, chunkContent, chunkIndex (0-based), totalChunks (>0)
        /// </summary>
        private static void SendFileToClient(long peerUid, string rpcName, string folderName, string fileName, string content)
        {
            if (ZRoutedRpc.instance == null) return;
            content = content ?? string.Empty;

            // Small enough to send in one message
            if (System.Text.Encoding.UTF8.GetByteCount(content) <= MaxChunkBytes)
            {
                var pkg = new ZPackage();
                pkg.Write(folderName);
                pkg.Write(fileName);
                pkg.Write(content);
                ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, rpcName, pkg);
                return;
            }

            // Split into chunks by character count (conservative � UTF-8 could be wider,
            // but config text is ASCII so 1 char ? 1 byte)
            int charsPerChunk = MaxChunkBytes;
            int totalChunks = (content.Length + charsPerChunk - 1) / charsPerChunk;

            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * charsPerChunk;
                int length = Math.Min(charsPerChunk, content.Length - start);
                string chunk = content.Substring(start, length);

                var pkg = new ZPackage();
                pkg.Write(folderName);
                pkg.Write(fileName);
                pkg.Write(chunk);
                pkg.Write(i);            // chunkIndex
                pkg.Write(totalChunks);  // totalChunks (>0 signals chunked mode)
                ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, rpcName, pkg);
            }

            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Sent {folderName}/{fileName} to {peerUid} in {totalChunks} chunks");
        }

        private string GetRpcNameForFolder(string folderName)
        {
            switch (folderName.ToLowerInvariant())
            {
                case "quests":
                case "questprofiles":
                case "questevents":
                    return "FiresRPGmaker_FileUpdate";

                case "dialogues":
                    return "FiresRPGmaker_FileUpdate";

                case "territories":
                    return "FiresRPGmaker_Territory_FileUpdate";

                case "serverinfos":
                    return "FiresRPGmaker_FileUpdate";

                case "savednpcs":
                    return "FiresRPGmaker_NpcDataUpdate";

                default:
                    return "FiresRPGmaker_FileUpdate";
            }
        }

        private void PerformPostSyncActions(string folderName, long excludeSender = -1)
        {
            try
            {
                switch (folderName.ToLowerInvariant())
                {
                    case "territories":
                        // Debounced reload already handles reload + broadcast + map redraw
                        TerritorySyncedState.ScheduleDebouncedReload();
                        break;

                    case "quests":
                        // Broadcast updated quest database to all clients (excluding sender if applicable)
                        if (QuestManager.Instance != null)
                        {
                            var peers = ZNet.instance?.GetPeers();
                            if (peers != null)
                            {
                                foreach (var peer in peers)
                                {
                                    if (peer == null || !peer.IsReady()) continue;
                                    // Skip the sender who pushed the config
                                    if (excludeSender > 0 && peer.m_uid == excludeSender) continue;
                                    QuestManager.Instance.BroadcastQuestDatabaseToPeer(peer.m_uid);
                                }
                            }
                        }
                        break;

                    case "savednpcs":
                        // SavedNPCs uses its own dedicated RPC broadcast.
                        // Reload already happened in ReloadServerData, now broadcast to clients.
                        SavedNpcManager.Reload();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Post-sync action failed for {folderName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually trigger a full sync of all config files to a specific client.
        /// Used when a new player joins.
        /// </summary>
        public static void SendAllConfigsToClient(long peerUid)
        {
            if (_instance == null || !_instance._isInitialized) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            try
            {
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(_instance.SendAllConfigsToClientCoroutine(peerUid));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to start SendAllConfigsToClient for {peerUid}: {ex.Message}");
            }
        }

        private IEnumerator SendAllConfigsToClientCoroutine(long peerUid)
        {
            // Small delay to let the client finish registering its routed-RPC handlers
            // before we start firing at it. 250 ms is empirically sufficient; the old
            // 1 s value was a conservative guess and added a flat 750 ms to every login.
            yield return new WaitForSeconds(0.25f);

            // Diff-sync: ask the client up front for a manifest of every cfg-folder
            // we're about to iterate. Firing all requests in parallel lets replies
            // queue up. Files whose client-side SHA-256 already matches our local
            // hash get skipped entirely — no wire traffic.
            foreach (var mfolder in WatchedFolders)
            {
                if (mfolder.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase)) continue;
                if (mfolder.Equals("UILayouts", StringComparison.OrdinalIgnoreCase)) continue; // UILayouts has its own manifest path
                ManifestDiffSync.RequestFromPeer(peerUid, ManifestNamespaceFor(mfolder));
            }

            // Pre-wait for the first manifest reply BEFORE entering the per-folder loop.
            // The old per-folder 2s timeout was firing the "send everything" fallback on
            // heavy modpack logins because the client was too busy spawning to reply in
            // 2s — the user repeatedly saw configs being resent every login despite
            // having them cached. One 10s pre-wait against the first folder gives the
            // client a real window to reply before we start iterating; subsequent
            // folders' replies typically arrive during the iteration anyway since
            // they're all already in flight.
            string firstNs = null;
            foreach (var mfolder in WatchedFolders)
            {
                if (mfolder.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase)) continue;
                if (mfolder.Equals("UILayouts", StringComparison.OrdinalIgnoreCase)) continue;
                firstNs = ManifestNamespaceFor(mfolder);
                break;
            }
            if (!string.IsNullOrEmpty(firstNs))
            {
                yield return ManifestDiffSync.WaitForManifest(peerUid, firstNs, 10f);
                bool firstArrived = ManifestDiffSync.TryGetManifest(peerUid, firstNs, out _);
                Debug.Log($"[FiresRPGmaker] Config-sync pre-wait for peer {peerUid}: first manifest '{firstNs}' arrived={firstArrived}");
            }

            foreach (var folder in WatchedFolders)
            {
                // SavedNPCs uses its own dedicated RPC and .json files � handled separately below
                if (folder.Equals("SavedNPCs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string folderPath = Path.Combine(BaseConfigDir, folder);
                if (!Directory.Exists(folderPath)) continue;

                string rpcName = GetRpcNameForFolder(folder);
                if (string.IsNullOrEmpty(rpcName)) continue;

                // Wait up to 5s per folder for the manifest. The first folder usually
                // already has its reply (the 10s pre-wait above covered it); subsequent
                // folders should arrive while we iterate previous ones since requests
                // were fired in parallel. If a manifest STILL never comes, we now
                // SKIP the folder entirely rather than fall back to "send everything"
                // — the user explicitly reported bandwidth waste from the old fallback
                // behaviour. A client that's missing a config can refresh via reconnect
                // or admin push; that's a much smaller cost than re-shipping every
                // file every login.
                string manifestNs = ManifestNamespaceFor(folder);
                bool haveManifest = false;
                if (!folder.Equals("UILayouts", StringComparison.OrdinalIgnoreCase))
                {
                    yield return ManifestDiffSync.WaitForManifest(peerUid, manifestNs, 5f);
                    haveManifest = ManifestDiffSync.TryGetManifest(peerUid, manifestNs, out _);
                    if (!haveManifest)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Config-sync: manifest '{manifestNs}' never arrived from peer {peerUid} after 5s — skipping folder '{folder}' (client keeps any cached copies; reconnect to retry).");
                        continue;
                    }
                }

                string searchPattern = "*.cfg";
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(folderPath, searchPattern, SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Error listing {folder} configs: {ex.Message}");
                    continue;
                }

                if (files == null) continue;

                int sent = 0, skipped = 0;
                foreach (var file in files)
                {
                    string content = null;
                    string relativePath = null;

                    try
                    {
                        content = File.ReadAllText(file);
                        // Forward-slash normalised so it matches ManifestDiffSync.BuildLocalManifest's
                        // keys regardless of platform path-separator.
                        relativePath = file.Substring(folderPath.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace('\\', '/');
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Error reading config file {file}: {ex.Message}");
                        continue;
                    }

                    if (content != null && relativePath != null)
                    {
                        // Skip files whose client-side hash already matches.
                        if (haveManifest)
                        {
                            string localHash = ManifestDiffSync.ComputeSha256(content);
                            if (ManifestDiffSync.HasSameHash(peerUid, manifestNs, relativePath, localHash))
                            {
                                skipped++;
                                continue;
                            }
                        }

                        try
                        {
                            if (ZRoutedRpc.instance != null)
                            {
                                SendFileToClient(peerUid, rpcName, folder, relativePath, content);
                                sent++;
                            }
                        }
                        catch { }
                    }

                    // Small yield to prevent overwhelming the network
                    yield return null;
                }

                if (haveManifest && (sent > 0 || skipped > 0))
                    Debug.Log($"[FiresRPGmaker] Folder '{folder}' diff-sync to peer {peerUid}: sent {sent}, skipped {skipped} unchanged.");
            }

            // Also send territory data and quest database
            try
            {
                TerritorySyncedState.BroadcastTerritories();
            }
            catch { }

            try
            {
                if (QuestManager.Instance != null)
                {
                    QuestManager.Instance.BroadcastQuestDatabaseToPeer(peerUid);
                }
            }
            catch { }

            // Send all saved NPC data via dedicated NPC RPC
            try
            {
                SavedNpcManager.SendAllNpcDataToClient(peerUid);
            }
            catch { }

            // Send all UILayout JSON files via the dedicated chunked/compressed layout RPC
            string[] uiLayoutFiles = null;
            try
            {
                string uiLayoutsDir = Path.Combine(BaseConfigDir, "UILayouts");
                if (Directory.Exists(uiLayoutsDir))
                    uiLayoutFiles = Directory.GetFiles(uiLayoutsDir, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to enumerate UILayouts for client sync: {ex.Message}");
            }

            if (uiLayoutFiles != null)
            {
                string uiLayoutsDir = Path.Combine(BaseConfigDir, "UILayouts");

                // Ask the client for its existing UILayouts manifest so we can skip files
                // whose SHA256 already matches. Wait briefly for the reply; if it never
                // arrives we fall back to the legacy "send everything" behaviour so the
                // client is guaranteed to end up synced.
                FiresCore.UI.UILayoutSyncRPC.RequestManifestFromPeer(peerUid);

                const float ManifestWaitSeconds = 3f;
                float waited = 0f;
                System.Collections.Generic.Dictionary<string, string> clientManifest = null;
                while (waited < ManifestWaitSeconds)
                {
                    if (FiresCore.UI.UILayoutSyncRPC.TryGetPeerManifest(peerUid, out clientManifest))
                        break;
                    yield return new WaitForSeconds(0.1f);
                    waited += 0.1f;
                }

                int sent = 0, skipped = 0;
                foreach (var layoutFile in uiLayoutFiles)
                {
                    string relPath = null;
                    string jsonContent = null;
                    try
                    {
                        relPath = layoutFile
                            .Substring(uiLayoutsDir.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        jsonContent = File.ReadAllText(layoutFile);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to read UILayout '{layoutFile}': {ex.Message}");
                        continue;
                    }

                    // Normalise to forward slashes so the hash key matches what the client sent.
                    string manifestKey = relPath.Replace(Path.DirectorySeparatorChar, '/');

                    if (clientManifest != null
                        && clientManifest.TryGetValue(manifestKey, out string clientSha)
                        && string.Equals(clientSha,
                            FiresCore.UI.UILayoutSyncRPC.ComputeSha256(jsonContent),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        UILayoutSyncRPC.SendLayoutToClient(peerUid, relPath, jsonContent);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to send UILayout '{layoutFile}' to {peerUid}: {ex.Message}");
                    }

                    yield return null; // Spread over frames to avoid burst
                }

                // Drop the manifest now that we've used it ? keeps the cache from growing
                // unbounded across many reconnects.
                FiresCore.UI.UILayoutSyncRPC.ForgetPeerManifest(peerUid);

                if (clientManifest != null)
                    Debug.Log($"[FiresRPGmaker] UILayout sync to peer {peerUid}: sent {sent}, cached/skipped {skipped} (of {uiLayoutFiles.Length})");
                else
                    Debug.Log($"[FiresRPGmaker] Sent {uiLayoutFiles.Length} UILayout file(s) to client {peerUid} (no manifest reply)");
            }

            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Sent all configs to client {peerUid}");

            // Signal: config sync coroutine has fully completed for this peer.
            // CompanionRestoreService gates on this so companion ZDO writes don't
            // race the config push.
            _peersWithConfigSyncComplete.Add(peerUid);
            Debug.Log($"[FiresRPGmaker] Config sync complete for peer {peerUid} — companion restore can now proceed.");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var watcher in _watchers.Values)
            {
                if (watcher != null)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    catch { }
                }
            }

            _watchers.Clear();
            _lastEventTimes.Clear();
            _isInitialized = false;

            Debug.Log("[FiresRPGmaker] ServerConfigFileWatcher disposed");
        }

        /// <summary>
        /// Mark a file as recently synced by an admin client.
        /// Called from RPC_SyncFiles when an admin pushes a config.
        /// The file watcher will then exclude this sender from the broadcast.
        /// </summary>
        /// <param name="folderName">The folder name (e.g., "Quests", "Territories")</param>
        /// <param name="fileName">The relative file name within the folder</param>
        /// <param name="senderUid">The peer UID of the admin who sent the file</param>
        public static void MarkFileAsAdminSynced(string folderName, string fileName, long senderUid)
        {
            if (_instance == null || !_instance._isInitialized) return;
            
            string fileKey = Path.Combine(folderName, fileName).ToLowerInvariant();
            
            lock (_instance._lockObj)
            {
                _instance._adminSyncedFiles[fileKey] = (DateTime.UtcNow, senderUid);
                
                // Clean up old entries while we're here
                var keysToRemove = new List<string>();
                foreach (var kvp in _instance._adminSyncedFiles)
                {
                    if ((DateTime.UtcNow - kvp.Value.time).TotalSeconds > AdminSyncExpirySeconds * 2)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    _instance._adminSyncedFiles.Remove(key);
                }
            }
            
            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Marked file as admin-synced: {folderName}/{fileName} from sender {senderUid}");
        }
    }
}
