using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Server?Client sync for dialogue config files.
    /// 
    /// Flow:
    /// 1. Server's <see cref="ServerConfigFileWatcher"/> detects .cfg change in Dialogues/
    /// 2. Server broadcasts raw file content via <c>FiresRPGmaker_FileUpdate</c> RPC
    /// 3. Client receives, saves to local Dialogues/ folder, reloads <see cref="DialogueCodex"/>
    /// 
    /// Admin push (client?server):
    /// 1. Admin edits dialogue in-game via editor
    /// 2. Client sends file content via <c>FiresRPGmaker_SyncFiles</c> RPC to server
    /// 3. Server saves to disk (triggers file watcher ? re-broadcasts to all other clients)
    ///
    /// Large files are sent in chunks by the server. The client reassembles them
    /// before writing to disk. See <see cref="ServerConfigFileWatcher.SendFileToClient"/>.
    /// </summary>
    public static class DialogueSyncRPC
    {
        private static string DialoguesDir => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Dialogues");

        // Reassembly buffer for chunked file transfers.
        // Key: "folderName/fileName", Value: array of chunk strings (null = not yet received)
        private static readonly Dictionary<string, string[]> _chunkBuffers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // Files we've recently written from server sync � suppress file watcher re-trigger
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

            ZRoutedRpc.instance.Register<ZPackage>("FiresRPGmaker_FileUpdate", RPC_FileUpdate);
            ZRoutedRpc.instance.Register<ZPackage>("FiresRPGmaker_SyncFiles", RPC_SyncFiles);

            Debug.Log("[FiresRPGmaker] DialogueSyncRPC registered");
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
                string folderName = pkg.ReadString();
                string fileName = pkg.ReadString();
                string contentOrChunk = pkg.ReadString();

                if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName)) return;

                // Try reading chunk fields � if they exist this is a chunked transfer
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
                    // Not chunked � single message
                    chunkIndex = -1;
                    totalChunks = 0;
                }

                string content;
                if (totalChunks > 1 && chunkIndex >= 0)
                {
                    // Chunked transfer � buffer until all chunks arrive
                    content = BufferChunk(folderName, fileName, contentOrChunk, chunkIndex, totalChunks);
                    if (content == null)
                        return; // Still waiting for more chunks
                }
                else
                {
                    content = contentOrChunk;
                }

                // Write to disk and reload
                ApplyFileUpdate(folderName, fileName, content);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_FileUpdate error: {ex.Message}");
            }
        }

        /// <summary>
        /// Buffers a chunk and returns the full reassembled content when all chunks have arrived.
        /// Returns null if still waiting for more chunks.
        /// </summary>
        private static string BufferChunk(string folderName, string fileName, string chunk, int chunkIndex, int totalChunks)
        {
            string key = $"{folderName}/{fileName}";

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

            // All chunks received � reassemble
            _chunkBuffers.Remove(key);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < totalChunks; i++)
                sb.Append(chunks[i]);

            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[FiresRPGmaker] Reassembled {totalChunks} chunks for {key} ({sb.Length} chars)");

            return sb.ToString();
        }

        /// <summary>
        /// Writes a complete file to disk and reloads the relevant subsystem.
        /// Marks the write so the file watcher ignores it (prevents re-broadcast loops
        /// on non-dedicated hosts where both server and client code run).
        /// </summary>
        private static void ApplyFileUpdate(string folderName, string fileName, string content)
        {
            string baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker", folderName);
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
        /// Server-side handler: receives a config file push from an admin client.
        /// Validates admin status, saves the file to disk (which triggers ServerConfigFileWatcher),
        /// and marks the file as admin-synced to avoid re-broadcasting to the sender.
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
                    Debug.LogWarning($"[FiresRPGmaker] Non-admin {sender} attempted to push dialogue config");
                    return;
                }

                string folderName = pkg.ReadString();
                string fileName = pkg.ReadString();
                string content = pkg.ReadString();

                if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(fileName)) return;

                string baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker", folderName);
                string fullPath = Path.Combine(baseDir, fileName);

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
                    File.WriteAllText(fullPath, content);
                    Debug.Log($"[FiresRPGmaker] Admin {sender} pushed config: {folderName}/{fileName} ({content.Length} chars)");
                }

                // The file watcher will detect the change and broadcast to other clients
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RPC_SyncFiles error: {ex.Message}");
            }
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

            sb.Append(string.Join(" | ", segments));
            return sb.ToString();
        }

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
