using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Server->Client sync for dialogue config files.
    /// 
    /// Flow:
    /// 1. Server's <see cref="ServerConfigFileWatcher"/> detects .cfg change in Dialogues/
    /// 2. Server broadcasts raw file content via <c>FiresRPGmaker_FileUpdate</c> RPC
    /// 3. Client receives, saves to local Dialogues/ folder, reloads <see cref="DialogueCodex"/>
    /// 
    /// Admin push (client->server):
    /// 1. Admin edits dialogue in-game via editor
    /// 2. Client sends file content via <c>FiresRPGmaker_SyncFiles</c> RPC to server
    /// 3. Server saves to disk (triggers file watcher -> re-broadcasts to all other clients)
    ///
    /// Large files are sent in chunks by the server. The client reassembles them
    /// before writing to disk. See <see cref="ServerConfigFileWatcher.SendFileToClient"/>.
    /// </summary>
    public static class DialogueSyncRPC
    {
        private static string DialoguesDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Dialogues);

        // Reassembly buffer for chunked file transfers.
        // Key: "folderName/fileName", Value: array of chunk strings (null = not yet received)
        private static readonly Dictionary<string, string[]> _chunkBuffers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // Files we've recently written from server sync - suppress file watcher re-trigger
        private static readonly HashSet<string> _recentServerWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Debounce: folders that need a reload, batched to avoid reloading per-file
        private static readonly HashSet<string> _pendingReloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Coroutine _reloadCoroutine;

        /// <summary>
        /// Returns true if the given file path was recently written by a server sync
        /// and should be ignored by the file watcher to prevent re-broadcast loops.
        /// </summary>
        public static bool IsRecentServerWrite(string fullPath)
        {
            lock (_recentServerWrites)
            {
                return _recentServerWrites.Remove(fullPath.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Registers the client-side and server-side RPC handlers.
        /// Called from ZNet.Awake via Harmony patch.
        /// </summary>
        public static void Register()
        {
            if (ZRoutedRpc.instance == null) return;

            RegisterRpc("FiresRPGmaker_FileUpdate", RPC_FileUpdate);
            RegisterRpc("FiresRPGmaker_SyncFiles", RPC_SyncFiles);
            // Admin raw-file loop: pull materializes the server's cfg tree on an ADMIN's disk for
            // external editing (regular clients never receive files — FileUpdate stays in-memory);
            // pushes ride the existing SyncFiles channel.
            RegisterRpc("FiresRPGmaker_PullConfigs", RPC_PullConfigs);
            RegisterRpc("FiresRPGmaker_FileDownload", RPC_FileDownload);
            // [release] audit #1: the server's MarketPlace.cfg for the NPC book's Marketplace + Mail tabs.
            try { global::FiresRPGmaker.Managers.MarketCfgSync.Register(); } catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] MarketCfgSync RPC registration failed: {ex.Message}"); }

            // NPC data/delete/anchor-move RPCs MUST bind here too. Their FinishInit registration runs
            // during plugin Awake when ZRoutedRpc is still null (the ManifestDiffSync note below in
            // Ascend.FinishInit describes the same trap), and the Player.OnSpawned fallback never fires
            // on a DEDICATED server — so the dedi never had RPC_NpcDataDelete bound and every admin
            // hammer-delete was silently dropped server-side (THE delete-resurrection root cause).
            // ZNet.Awake postfix is the one site that binds reliably on every host.
            try { global::FiresRPGmaker.Managers.SavedNpcManager.RegisterRPCs(); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] SavedNpcManager RPC registration failed: {ex.Message}"); }

            // Dialogue option cooldowns (server-authoritative gate + stamp)
            RegisterRpc("Fires_DlgCdQuery", RPC_DlgCdQuery);
            RegisterRpc("Fires_DlgCdState", RPC_DlgCdState);
            RegisterRpc("Fires_DlgPick", RPC_DlgPick);
            RegisterRpc("Fires_DlgPickResult", RPC_DlgPickResult);

            Debug.Log("[FiresRPGmaker] DialogueSyncRPC registered");
        }

        /// <summary>
        /// Registers a single routed RPC, isolating each binding so one failure can't abort the
        /// rest of <see cref="Register"/>. ZRoutedRpc.Register uses Dictionary.Add, which throws
        /// ArgumentException if the name is already bound; without this isolation a duplicate
        /// registration would silently drop every RPC queued after it (notably the cooldown RPCs).
        /// </summary>
        private static void RegisterRpc(string name, Action<long, ZPackage> handler)
        {
            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(name, handler);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC '{name}' could not be registered (already bound?): {ex.Message}");
            }
        }

        /// <summary>
        /// Client-side handler: receives a config file update from the server.
        /// Supports both single-message and chunked transfers.
        ///
        /// Single format:  folder, fileName, content  (no extra fields)
        /// Chunked format: folder, fileName, chunkContent, chunkIndex, totalChunks
        /// </summary>
        private static void RPC_FileUpdate(long sender, ZPackage pkg)
        {
            if (pkg == null) return;

            try
            {
                if (!TryReadFilePacket(pkg, "up:", out string folderName, out string fileName, out string content))
                    return; // invalid packet or still waiting for more chunks

                ApplyFileUpdate(folderName, fileName, content);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_FileUpdate error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads a file-transfer packet (single or chunked; see <see cref="ServerConfigFileWatcher.SendFileToClient"/>
        /// for the wire format). Returns false while a chunked transfer is still incomplete. The
        /// streamPrefix keeps concurrent update/download streams for the same file from colliding
        /// in the reassembly buffer.
        /// </summary>
        internal static bool TryReadFilePacket(ZPackage pkg, string streamPrefix, out string folderName, out string fileName, out string content)
        {
            folderName = pkg.ReadString();
            fileName = pkg.ReadString();
            string contentOrChunk = pkg.ReadString();
            content = null;

            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName)) return false;

            // Try reading chunk fields - if they exist this is a chunked transfer
            int chunkIndex = -1;
            int totalChunks = 0;
            try
            {
                // These fields only exist in chunked messages
                chunkIndex = pkg.ReadInt();
                totalChunks = pkg.ReadInt();
            }
            catch
            {
                // Not chunked - single message
                chunkIndex = -1;
                totalChunks = 0;
            }

            if (totalChunks > 1 && chunkIndex >= 0)
            {
                content = BufferChunk(streamPrefix + folderName + "/" + fileName, contentOrChunk, chunkIndex, totalChunks);
                return content != null;
            }

            content = contentOrChunk;
            return true;
        }

        /// <summary>
        /// Server-side: an admin requested the raw config tree (firesnpcs_pull_configs). Streams every
        /// watched .cfg to them via FiresRPGmaker_FileDownload — the ONLY channel that ever materializes
        /// config files on a client machine.
        /// </summary>
        private static void RPC_PullConfigs(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                if (!AdminSyncing.IsAdmin(sender))
                {
                    Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} requested a config pull");
                    return;
                }
                int sent = ServerConfigFileWatcher.SendAllConfigFilesForDownload(sender);
                Debug.Log($"[FiresRPGmaker] Admin {sender} pulled the config tree ({sent} file(s) sent)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_PullConfigs error: {ex.Message}");
            }
        }

        /// <summary>
        /// Client-side: a pulled raw file arrives (admin-requested download). Written to the GROUPED
        /// path — the same tree the server reads — so external edits round-trip cleanly through
        /// firesnpcs_push_configs. Regular sync traffic never uses this handler.
        /// </summary>
        private static void RPC_FileDownload(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                if (!TryReadFilePacket(pkg, "dl:", out string folderName, out string fileName, out string content))
                    return;

                string baseDir = FiresCore.Storage.FiresConfigPaths.GroupedPath(folderName);
                string fullPath = Path.Combine(baseDir, fileName);
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (string.IsNullOrEmpty(content))
                {
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                }
                else
                {
                    File.WriteAllText(fullPath, content);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_FileDownload error: {ex.Message}");
            }
        }

        /// <summary>
        /// Buffers a chunk and returns the full reassembled content when all chunks have arrived.
        /// Returns null if still waiting for more chunks. Key = stream prefix + folder/file.
        /// </summary>
        private static string BufferChunk(string key, string chunk, int chunkIndex, int totalChunks)
        {
            if (!_chunkBuffers.TryGetValue(key, out var chunks) || chunks.Length != totalChunks)
            {
                chunks = new string[totalChunks];
                _chunkBuffers[key] = chunks;
            }

            if (chunkIndex >= 0 && chunkIndex < totalChunks)
                chunks[chunkIndex] = chunk;

            // Check if all chunks received
            for (int i = 0; i < totalChunks; i++)
            {
                if (chunks[i] == null)
                    return null;
            }

            // All chunks received - reassemble
            _chunkBuffers.Remove(key);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < totalChunks; i++)
                sb.Append(chunks[i]);

            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Reassembled {totalChunks} chunks for {key} ({sb.Length} chars)");

            return sb.ToString();
        }

        /// <summary>
        /// Applies a synced config file. The Marketplace model: regular clients keep NOTHING on
        /// disk — content goes into the in-memory <see cref="SyncedConfigStore"/> and the family
        /// loader re-reads from it (FiresConfigFiles routes there on pure clients). Only a
        /// server/host receiving one (defensive edge) writes to disk — at the GROUPED path the
        /// loaders actually read, never the old flat path that was invisible until next boot.
        /// </summary>
        private static void ApplyFileUpdate(string folderName, string fileName, string content)
        {
            bool pureClient = ZNet.instance != null && !ZNet.instance.IsServer();
            if (pureClient)
            {
                SyncedConfigStore.Apply(folderName, fileName, content);
                // QuestProfiles has its own dedicated in-memory apply (client Reload() is a no-op
                // by design) — feed it directly.
                if (folderName.Equals("QuestProfiles", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(content))
                    global::FiresRPGmaker.UI.QuestProfileManager.ApplyProfileDataFromServer(fileName, content);
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Stored synced config in memory: {folderName}/{fileName} ({(content ?? "").Length} chars)");
                HandleFolderReload(folderName);
                return;
            }

            string baseDir = FiresCore.Storage.FiresConfigPaths.GroupedPath(folderName);
            string fullPath = Path.Combine(baseDir, fileName);

            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Mark this write so the file watcher ignores it
            lock (_recentServerWrites)
            {
                _recentServerWrites.Add(fullPath.ToLowerInvariant());
            }

            if (string.IsNullOrEmpty(content))
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    if (FiresLogger.VerboseEnabled)
                        FiresLogger.LogVerbose($"[FiresRPGmaker] Deleted synced config: {folderName}/{fileName}");
                }
            }
            else
            {
                File.WriteAllText(fullPath, content);
                if (FiresLogger.VerboseEnabled)
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Saved synced config: {folderName}/{fileName} ({content.Length} chars)");
            }

            HandleFolderReload(folderName);
        }

        /// <summary>
        /// Top-level config folders an admin client is allowed to push into. Matched against the
        /// first path segment of the folder field, so subfolders (e.g. "SavedNPCs/templates") are
        /// permitted as long as their root is on this list.
        /// </summary>
        private static readonly string[] AllowedSyncFolders =
        {
            "Quests", "QuestProfiles", "QuestEvents", "Dialogues", "Traders", "Territories", "ServerInfos", "SavedNPCs",
            "Bankers", "Gamblers", "Transmogrifications", "Mail", "Buffers", "BufferProfiles", "LeaderboardAchievements",
            "Factions", "PatrolRoutes"   // 0.0.505: Core 0.2.308 pushes admin patrol-route saves here
        };

        /// <summary>
        /// Server-side handler: receives a config file push from an admin client. This is the single
        /// canonical "FiresRPGmaker_SyncFiles" handler for the whole mod. It validates admin status
        /// and the requested path, then writes (or deletes) the file. The actual write triggers
        /// <see cref="ServerConfigFileWatcher"/>, which reloads the relevant server-side cache and
        /// re-broadcasts the file to the other clients — so no explicit per-folder reload is needed
        /// here.
        ///
        /// Path validation mirrors the old Ascend handler's intent (folder allowlist + .cfg/.json
        /// extension + traversal rejection) but, unlike that handler, preserves the subfolder paths
        /// ("Dialogues" + "legacy/foo.cfg", folder "SavedNPCs/templates") and the empty-content
        /// delete that the live senders depend on.
        /// </summary>
        private static void RPC_SyncFiles(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            try
            {
                // Verify admin status
                if (!AdminSyncing.IsAdmin(sender))
                {
                    Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} attempted to push config via SyncFiles");
                    return;
                }

                string folderName = pkg.ReadString();
                string fileName = pkg.ReadString();
                string content = pkg.ReadString();

                if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName)) return;

                if (!TryResolveSyncPath(folderName, fileName, out string fullPath))
                    return;

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Mark as admin-synced so file watcher excludes the sender from broadcast
                ServerConfigFileWatcher.MarkFileAsAdminSynced(folderName, fileName, sender);

                if (string.IsNullOrEmpty(content))
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                    Debug.Log($"[FiresRPGmaker] Admin {sender} deleted config: {folderName}/{fileName}");
                }
                else
                {
                    // Block-structured quest configs (Quests / QuestProfiles) are UPSERT-merged, never blind-overwritten.
                    // A pure client only ever holds a SPARSE local copy of the quest DB (the sample-fallback file, since
                    // clients read the real DB from the in-memory sync blob, not disk) — a straight File.WriteAllText let
                    // a single admin "save quest" push that sparse copy over the server's full 24-quest DB and WIPE it.
                    // Merge preserves every existing [block]; pushed blocks replace same-named ones and new ones append.
                    if (IsBlockMergeFolder(folderName) && File.Exists(fullPath))
                    {
                        try { content = MergeBlockCfg(File.ReadAllText(fullPath), content); }
                        catch (Exception mex) { Debug.LogWarning($"[FiresRPGmaker] block-merge {folderName}/{fileName} failed, writing as-is: {mex.Message}"); }
                    }
                    File.WriteAllText(fullPath, content);
                    Debug.Log($"[FiresRPGmaker] Admin {sender} pushed config: {folderName}/{fileName} ({content.Length} chars)");
                }

                // The file watcher will detect the change, reload server caches, and broadcast to other clients
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_SyncFiles error: {ex.Message}");
            }
        }

        // Folders whose .cfg files are structured as independent [header] blocks and must be upsert-merged on push
        // rather than overwritten, so a sparse client copy can never wipe the server's full set.
        private static bool IsBlockMergeFolder(string folder)
            => folder.Equals("Quests", StringComparison.OrdinalIgnoreCase)
            || folder.Equals("QuestProfiles", StringComparison.OrdinalIgnoreCase);

        // Upsert [header] blocks from `pushed` into `existing`: same-named blocks are replaced, brand-new blocks
        // append, and every existing block the push doesn't mention is PRESERVED (never dropped). Keying is by the
        // "[id]" header token, case-insensitive. Content before the first header is preserved as a leading preamble.
        private static string MergeBlockCfg(string existing, string pushed)
        {
            var order = new List<string>();
            var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string preamble = null;

            void Ingest(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                var lines = text.Replace("\r\n", "\n").Split('\n');
                string cur = null;
                var sb = new StringBuilder();
                var pre = new StringBuilder();
                void Flush() { if (cur != null) { blocks[cur] = sb.ToString().TrimEnd('\n'); if (!order.Contains(cur)) order.Add(cur); } }
                foreach (var line in lines)
                {
                    var t = line.TrimStart();
                    int close = t.IndexOf(']');
                    if (t.StartsWith("[") && close > 0)
                    {
                        Flush();
                        cur = t.Substring(0, close + 1);
                        sb = new StringBuilder();
                        sb.Append(line).Append('\n');
                    }
                    else if (cur != null) { sb.Append(line).Append('\n'); }
                    else { pre.Append(line).Append('\n'); }   // lines before the first [header]
                }
                Flush();
                if (preamble == null && pre.ToString().Trim().Length > 0) preamble = pre.ToString().TrimEnd('\n');
            }

            Ingest(existing);   // seed order + blocks from the full server copy FIRST
            Ingest(pushed);     // pushed blocks upsert same-named, new ones append after

            var outSb = new StringBuilder();
            if (!string.IsNullOrEmpty(preamble)) outSb.Append(preamble).Append("\n\n");
            foreach (var key in order)
                outSb.Append(blocks[key].TrimEnd('\n')).Append("\n\n");
            return outSb.ToString().TrimEnd('\n') + "\n";
        }

        /// <summary>
        /// Validates an admin sync request and resolves the absolute destination path. Confirms the
        /// folder's top-level segment is allow-listed, the file has a .cfg/.json extension, and the
        /// combined path contains no traversal or absolute components. The resolved path is finally
        /// confirmed to stay inside the FiresRPGmaker config tree. Returns false (and logs) on any
        /// rejection. Subfolders are permitted; "..", rooted paths, and folders outside the allowlist
        /// are not.
        /// </summary>
        private static bool TryResolveSyncPath(string folderName, string fileName, out string fullPath)
        {
            fullPath = null;

            string normFolder = folderName.Replace('\\', '/').Trim('/');
            string normFile = fileName.Replace('\\', '/').TrimStart('/');
            if (normFolder.Length == 0 || normFile.Length == 0) return false;

            // The server-wide Marketplace settings live in the KG Marketplace plugin's own config dir
            // (config/Marketplace), OUTSIDE the FiresRPGmaker tree. This reserved token maps to that single
            // fixed file so an admin's edit reaches the server (where the KG plugin's own watcher reloads it).
            // The caller's fileName is ignored — there is no traversal surface, and admin was already verified.
            if (string.Equals(normFolder, "MarketplaceCfg", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");
                return true;
            }

            if (HasTraversal(normFolder) || HasTraversal(normFile))
            {
                Debug.LogWarning($"[FiresRPGmaker] SyncFiles rejected suspicious path: {folderName}/{fileName}");
                return false;
            }

            string topFolder = normFolder.Split('/')[0];
            if (Array.IndexOf(AllowedSyncFolders, topFolder) < 0)
            {
                Debug.LogWarning($"[FiresRPGmaker] SyncFiles rejected disallowed folder: {folderName}");
                return false;
            }

            if (!normFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) &&
                !normFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !normFile.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
                !normFile.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[FiresRPGmaker] SyncFiles rejected file extension: {fileName}");
                return false;
            }

            string baseDir = Path.GetFullPath(FiresCore.Storage.FiresConfigPaths.Root);
            // Sync keys stay flat; GroupedPath remaps the top folder into its group (still under the root, so the
            // containment guard below holds).
            string resolved = Path.GetFullPath(Path.Combine(FiresCore.Storage.FiresConfigPaths.GroupedPath(normFolder), normFile));

            // Final containment guard: never write outside the FiresRPGmaker config tree.
            if (!resolved.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[FiresRPGmaker] SyncFiles rejected path outside config root: {folderName}/{fileName}");
                return false;
            }

            fullPath = resolved;
            return true;
        }

        /// <summary>
        /// Returns true if a normalized (forward-slash) relative path contains any traversal or
        /// absolute component that could escape its intended folder.
        /// </summary>
        private static bool HasTraversal(string normalizedPath)
        {
            if (Path.IsPathRooted(normalizedPath)) return true;
            foreach (var seg in normalizedPath.Split('/'))
            {
                if (seg == "." || seg == "..") return true;
            }
            return false;
        }

        /// <summary>
        /// Schedules a debounced reload for the given folder subsystem.
        /// Multiple files received in quick succession will batch into a single reload.
        /// </summary>
        private static void HandleFolderReload(string folderName)
        {
            _pendingReloads.Add(folderName.ToLowerInvariant());

            if (FiresRPGmaker.Instance != null)
            {
                if (_reloadCoroutine != null)
                    FiresRPGmaker.Instance.StopCoroutine(_reloadCoroutine);
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(DebouncedReloadCoroutine());
            }
            else
            {
                // Fallback: immediate reload
                ExecutePendingReloads();
            }
        }

        private static System.Collections.IEnumerator DebouncedReloadCoroutine()
        {
            yield return new WaitForSeconds(0.5f);
            ExecutePendingReloads();
            _reloadCoroutine = null;
        }

        private static void ExecutePendingReloads()
        {
            var folders = new List<string>(_pendingReloads);
            _pendingReloads.Clear();

            foreach (var folderName in folders)
            {
                try
                {
                    switch (folderName)
                    {
                        case "dialogues":
                            DialogueCodex.Reload();
                            if (FiresLogger.VerboseEnabled)
                                FiresLogger.LogVerbose($"[FiresRPGmaker] Dialogues reloaded from sync; count={DialogueCodex.Dialogues.Count}");
                            break;

                        case "serverinfos":
                            try { global::FiresRPGmaker.UI.Screens.InfoNpcScreenController.ReloadCache(); } catch { }
                            break;

                        case "buffers":
                        case "bufferprofiles":
                            try { global::FiresRPGmaker.Modules.Buffer.BufferManager.Reload(); } catch { }
                            break;

                        case "quests":
                            try
                            {
                                if (global::FiresRPGmaker.Modules.Quest.QuestManager.Instance != null)
                                    global::FiresRPGmaker.Modules.Quest.QuestManager.Instance.LoadQuestDatabase();
                                else
                                    global::FiresRPGmaker.Modules.Quest.QuestManager.Init();
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[FiresRPGmaker] Failed to reload quest DB from sync: {ex.Message}");
                            }
                            break;

                        case "questprofiles":
                            try { global::FiresRPGmaker.UI.QuestProfileManager.Reload(); }
                            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Failed to reload quest profiles from sync: {ex.Message}"); }
                            break;

                        case "traders":
                            try
                            {
                                global::FiresRPGmaker.Managers.TraderProfileManager.Reset();
                                global::FiresRPGmaker.Managers.TraderProfileManager.Initialize();
                            }
                            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Failed to reload trader profiles from sync: {ex.Message}"); }
                            break;

                        case "bankers":
                            try { global::FiresRPGmaker.Modules.Marketplace.BankerProfileManager.ReloadAll(); } catch { }
                            break;
                        case "gamblers":
                            try { global::FiresRPGmaker.Modules.Marketplace.GamblerProfileManager.ReloadAll(); } catch { }
                            break;
                        case "transmogrifications":
                            try { global::FiresRPGmaker.Modules.Marketplace.TransmogProfileManager.ReloadAll(); } catch { }
                            break;
                        case "mail":
                            try { global::FiresRPGmaker.Modules.Marketplace.MailProfileManager.ReloadAll(); } catch { }
                            break;
                        case "leaderboardachievements":
                            try { global::FiresRPGmaker.Modules.Marketplace.AchievementProfileManager.ReloadAll();
                                  global::FiresRPGmaker.Modules.Marketplace.LeaderboardProfileManager.ReloadAll(); } catch { }
                            break;
                        case "patrolroutes":   // 0.0.505: an admin's patrol route (Core 0.2.308 push), on the server and on clients
                            try { FiresCore.Npc.Patrol.PatrolRouteManager.Reset(); FiresCore.Npc.Patrol.PatrolRouteManager.Initialize(); } catch { }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Debounced reload failed for {folderName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Admin client pushes a dialogue config file to the server.
        /// Called from the dialogue editor when saving.
        /// </summary>
        /// <param name="fileName">Relative file path within the Dialogues folder (e.g., "legacy/MyDialogue.cfg")</param>
        /// <param name="content">Full file content, or empty/null to delete</param>
        public static void PushDialogueToServer(string fileName, string content)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null) return;

            try
            {
                var pkg = new ZPackage();
                pkg.Write("Dialogues");
                pkg.Write(fileName);
                pkg.Write(content ?? string.Empty);

                // Send to server (peer 0 = server)
                ZRoutedRpc.instance.InvokeRoutedRPC("FiresRPGmaker_SyncFiles", pkg);

                // Also apply locally immediately
                string fullPath = Path.Combine(DialoguesDir, fileName);
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (string.IsNullOrEmpty(content))
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                else
                {
                    File.WriteAllText(fullPath, content);
                }

                DialogueCodex.Reload();
                Debug.Log($"[FiresRPGmaker] Pushed dialogue config to server: {fileName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to push dialogue to server: {ex.Message}");
            }
        }

        /// <summary>
        /// Serializes a <see cref="RawDialogue"/> back to Marketplace-compatible .cfg format.
        /// Used by the admin editor when saving.
        /// </summary>
        public static string SerializeDialogueToCfg(RawDialogue dialogue)
        {
            if (dialogue == null) return string.Empty;

            var sb = new System.Text.StringBuilder();

            // Header
            if (!string.IsNullOrEmpty(dialogue.BG_Image))
                sb.AppendLine($"[{dialogue.UID}={dialogue.BG_Image}]");
            else
                sb.AppendLine($"[{dialogue.UID}]");

            // NPC text
            string text = dialogue.Text ?? "";
            sb.AppendLine(text.Replace("\n", "\\n"));

            // Options/interactions
            if (dialogue.Interactions != null)
            {
                foreach (var opt in dialogue.Interactions)
                {
                    sb.AppendLine(SerializeOption(opt));
                }
            }

            return sb.ToString();
        }

        private static string SerializeOption(RawPlayerOption opt)
        {
            var sb = new System.Text.StringBuilder();

            // Type prefix
            switch (opt.Type)
            {
                case InteractionType.Interaction:
                    sb.Append("@interaction ");
                    break;
                case InteractionType.InRange:
                    sb.Append("@inrange ");
                    break;
                case InteractionType.OutRange:
                    sb.Append("@outrange ");
                    break;
            }

            var segments = new System.Collections.Generic.List<string>();

            // Text
            if (!string.IsNullOrEmpty(opt.Text))
                segments.Add("Text: " + opt.Text.Replace("\n", "\\n"));

            // Transition
            if (!string.IsNullOrEmpty(opt.NextUID))
                segments.Add("Transition: " + opt.NextUID);

            // RandomTransition
            if (opt.RandomTransition != null && opt.RandomTransition.Length > 0)
                segments.Add("RandomTransition: " + string.Join(",", opt.RandomTransition));

            // Commands
            if (opt.Commands != null)
            {
                foreach (var cmd in opt.Commands)
                    segments.Add("Command: " + cmd);
            }

            // RandomCommands
            if (opt.RandomCommands != null)
            {
                foreach (var rc in opt.RandomCommands)
                    segments.Add($"RandomCommand: {rc.Key},{rc.Value}");
            }

            // Conditions
            if (opt.Conditions != null)
            {
                foreach (var group in opt.Conditions)
                {
                    if (group != null && group.Length > 0)
                        segments.Add("Condition: " + string.Join("||", group));
                }
            }

            // Icon
            if (!string.IsNullOrEmpty(opt.Icon))
                segments.Add("Icon: " + opt.Icon);

            // AlwaysVisible (only write if false, since default is true)
            if (!opt.AlwaysVisible)
                segments.Add("AlwaysVisible: false");

            // Color (only write if not default white)
            if (opt.Color.r != 255 || opt.Color.g != 255 || opt.Color.b != 255)
                segments.Add($"Color: {opt.Color.r},{opt.Color.g},{opt.Color.b}");

            // OverrideError
            if (!string.IsNullOrEmpty(opt.OverrideError))
                segments.Add("OverrideError: " + opt.OverrideError);

            // Cooldown (only write when a scope is set; default is None)
            if (opt.Cooldown != CooldownScope.None)
            {
                string cd = $"{opt.Cooldown},{opt.CooldownSeconds}";
                if (opt.ShowWhileCooling) cd += ",show";
                segments.Add("Cooldown: " + cd);
            }

            // CooldownKey (only write when frozen)
            if (!string.IsNullOrEmpty(opt.CooldownKey))
                segments.Add("CooldownKey: " + opt.CooldownKey);

            sb.Append(string.Join(" | ", segments));
            return sb.ToString();
        }

        #region Cooldown RPCs

        /// <summary>
        /// Client cache of optionKey -> remaining seconds for the dialogue currently shown.
        /// Populated by the server's Fires_DlgCdState reply. Read by DialogueUI to gate/label options.
        /// </summary>
        public static readonly Dictionary<string, int> CooldownCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on the client when a fresh cooldown state lands, so the open dialogue can refresh.</summary>
        public static Action OnCooldownStateReceived;

        /// <summary>Raised on the client when the server resolves a pick: (approved, optionKey, remainingSeconds).</summary>
        public static Action<bool, string, int> OnPickResult;

        /// <summary>Client → server: request cooldown state for every cooldown-bearing option in a dialogue.</summary>
        public static void SendCooldownQuery(string dialogueUID)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(dialogueUID)) return;
            var pkg = new ZPackage();
            pkg.Write(dialogueUID);
            ZRoutedRpc.instance.InvokeRoutedRPC("Fires_DlgCdQuery", pkg);
        }

        /// <summary>Client → server: claim a cooldown-bearing option.</summary>
        public static void SendPick(string dialogueUID, string optionKey)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(dialogueUID) || string.IsNullOrEmpty(optionKey)) return;
            var pkg = new ZPackage();
            pkg.Write(dialogueUID);
            pkg.Write(optionKey);
            ZRoutedRpc.instance.InvokeRoutedRPC("Fires_DlgPick", pkg);
        }

        private static void RPC_DlgCdQuery(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;

            try
            {
                string dialogueUID = pkg.ReadString();
                var dialogue = DialogueCodex.Get(dialogueUID);
                if (dialogue == null) return;

                string platformId = ResolveSenderPlatformId(sender);

                var reply = new ZPackage();
                var entries = new List<KeyValuePair<string, int>>();
                if (dialogue.Interactions != null)
                {
                    foreach (var opt in dialogue.Interactions)
                    {
                        if (opt.Cooldown == CooldownScope.None) continue;
                        string optionKey = DialogueKeyUtil.OptionKey(dialogueUID, opt);
                        int remaining = DialogueCooldownManager.RemainingSeconds(opt.Cooldown, optionKey, platformId);
                        entries.Add(new KeyValuePair<string, int>(optionKey, remaining));
                    }
                }

                reply.Write(dialogueUID);
                reply.Write(entries.Count);
                foreach (var e in entries)
                {
                    reply.Write(e.Key);
                    reply.Write(e.Value);
                }

                global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.SafeInvokeRoutedRPC(sender, "Fires_DlgCdState", reply);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_DlgCdQuery error: {ex.Message}");
            }
        }

        private static void RPC_DlgCdState(long sender, ZPackage pkg)
        {
            // Anyone with a local player: a client, or a host / single player answering itself. 0.0.532: this returned on
            // every server, so a listen host or single player never got its own cooldown state or pick result back, and
            // cooldown options never ran their commands there.
            if (Player.m_localPlayer == null && ZNet.instance != null && ZNet.instance.IsServer()) return;
            if (pkg == null) return;

            try
            {
                pkg.ReadString(); // dialogueUID (cache is keyed by optionKey)
                int count = pkg.ReadInt();
                CooldownCache.Clear();
                for (int i = 0; i < count; i++)
                {
                    string optionKey = pkg.ReadString();
                    int remaining = pkg.ReadInt();
                    CooldownCache[optionKey] = remaining;
                }
                OnCooldownStateReceived?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_DlgCdState error: {ex.Message}");
            }
        }

        private static void RPC_DlgPick(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;

            try
            {
                string dialogueUID = pkg.ReadString();
                string claimedKey = pkg.ReadString();

                var dialogue = DialogueCodex.Get(dialogueUID);
                if (dialogue == null)
                {
                    ReplyPickResult(sender, false, claimedKey, 0);
                    return;
                }

                // Re-derive the option server-side from our own codex; never trust the client's claimed reward.
                RawPlayerOption matched = null;
                if (dialogue.Interactions != null)
                {
                    foreach (var opt in dialogue.Interactions)
                    {
                        if (opt.Cooldown == CooldownScope.None) continue;
                        if (string.Equals(DialogueKeyUtil.OptionKey(dialogueUID, opt), claimedKey, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = opt;
                            break;
                        }
                    }
                }

                if (matched == null)
                {
                    ReplyPickResult(sender, false, claimedKey, 0);
                    return;
                }

                string optionKey = DialogueKeyUtil.OptionKey(dialogueUID, matched);
                string platformId = ResolveSenderPlatformId(sender);

                int remaining = DialogueCooldownManager.RemainingSeconds(matched.Cooldown, optionKey, platformId);
                if (remaining > 0)
                {
                    ReplyPickResult(sender, false, optionKey, remaining);
                    return;
                }

                // Stamp first, then approve, so a re-open cannot re-claim before commands run client-side.
                DialogueCooldownManager.Stamp(matched.Cooldown, optionKey, platformId, matched.CooldownSeconds);
                ReplyPickResult(sender, true, optionKey, 0);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_DlgPick error: {ex.Message}");
            }
        }

        private static void ReplyPickResult(long target, bool approved, string optionKey, int remainingSeconds)
        {
            var reply = new ZPackage();
            reply.Write(approved);
            reply.Write(optionKey ?? string.Empty);
            reply.Write(remainingSeconds);
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.SafeInvokeRoutedRPC(target, "Fires_DlgPickResult", reply);
        }

        private static void RPC_DlgPickResult(long sender, ZPackage pkg)
        {
            if (Player.m_localPlayer == null && ZNet.instance != null && ZNet.instance.IsServer()) return; // see RPC_DlgCdState
            if (pkg == null) return;

            try
            {
                bool approved = pkg.ReadBool();
                string optionKey = pkg.ReadString();
                int remaining = pkg.ReadInt();
                OnPickResult?.Invoke(approved, optionKey, remaining);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_DlgPickResult error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the platform (Steam) id of an RPC sender on the server, mirroring
        /// EwpDataBridge.ResolvePlatformId. Never returns the save-file player id.
        /// </summary>
        private static string ResolveSenderPlatformId(long sender)
        {
            if (ZNet.instance == null) return null;

            var peer = ZNet.instance.GetPeer(sender);
            if (peer != null)
            {
                string host = peer.m_socket?.GetHostName();
                if (!string.IsNullOrEmpty(host))
                    return EwpDataBridge.StripPlatformPrefix(host);
            }

            // Sender is the host itself (listen server) — no peer entry exists.
            if (!ZNet.instance.IsDedicated())
            {
                try
                {
                    var players = ZNet.instance.GetPlayerList();
                    if (players.Count > 0)
                        return EwpDataBridge.StripPlatformPrefix(players[0].m_userInfo.m_id.ToString());
                }
                catch { }
            }

            return null;
        }

        #endregion

        #region Harmony Patches

        /// <summary>Registers RPC handlers when ZNet initializes.</summary>
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_DialogueSync_Patch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                DialogueSyncRPC.Register();
            }
        }

        /// <summary>Sends all dialogue configs to newly connected clients.</summary>
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZNet_RPC_PeerInfo_DialogueSync_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;

                // ServerConfigFileWatcher.SendAllConfigsToClient handles bulk sync
                // which includes Dialogues folder. No additional action needed here
                // since the file watcher already sends all .cfg files on connect.
            }
        }

        #endregion
    }
}
