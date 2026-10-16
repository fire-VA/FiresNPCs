using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker;
using IOCompressionLevel = System.IO.Compression.CompressionLevel;

namespace FiresRPGmaker.UI.QuestJournal
{
    /// <summary>
    /// Handles syncing RuntimeSprites folder from server to clients.
    /// Clients receive quest preview images automatically when they open the Quest Journal.
    /// Images are sent in chunks to avoid exceeding Steam's max message size.
    /// </summary>
    public static class RuntimeSpriteSync
    {
        private static bool _rpcsRegistered;
        private static bool _syncRequested;
        private static bool _syncComplete;
        private static DateTime _lastSyncRequestTime;
     private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(30);

        // RPC names - server-to-client sync (pull)
        private const string RpcRequestSprites = "FiresRPGmaker_Sprites_Request";
        private const string RpcReceiveSprite = "FiresRPGmaker_Sprites_Receive";
        private const string RpcReceiveSpriteChunk = "FiresRPGmaker_Sprites_Chunk";

        // RPC names - server tells client which files server is missing (bidirectional sync)
        private const string RpcServerNeedsList = "FiresRPGmaker_Sprites_ServerNeeds";

        // RPC names - client-to-server push (admin pushcfgs)
        private const string RpcPushSprite = "FiresRPGmaker_Sprites_Push";
        private const string RpcPushSpriteChunk = "FiresRPGmaker_Sprites_PushChunk";

    // Supported image extensions
  private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

   // Max file size to sync (50MB - chunking handles any size under this limit)
        private const int MaxFileSizeBytes = 50 * 1024 * 1024;

        // Chunk size for sending (400KB to stay under Steam's 512KB limit with overhead)
      // Larger chunks = fewer RPCs = more efficient transfer
        private const int ChunkSize = 400 * 1024;

        // Pending chunks on client side (fileName -> accumulated data)
        private static readonly Dictionary<string, PendingImageData> _pendingImages = new Dictionary<string, PendingImageData>();

        private class PendingImageData
   {
     public byte[] Data;
      public int TotalSize;
            public int ReceivedSize;
  public int TotalChunks;
      public int ReceivedChunks;
          public bool IsCompressed;
        }

        /// <summary>
        /// Path to RuntimeSprites folder.
        /// </summary>
        public static string RuntimeSpritesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestSprites);

        /// <summary>
        /// Path to UIAssets folder tree (CapturedAssets/sprites and Sprites subfolders).
        /// </summary>
        private static string UIAssetsDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.UiAssets);

        /// <summary>
        /// Returns all syncable folders as (directory, relativePrefix) pairs.
        /// The prefix is prepended to filenames in the manifest so the client
        /// saves each file to the correct subfolder.
        /// </summary>
        private static List<(string Dir, string Prefix)> GetSyncFolders()
        {
            return new List<(string, string)>
            {
                (RuntimeSpritesFolder, ""),
                (Path.Combine(UIAssetsDir, "Sprites"), "UIAssets_Sprites/"),
                (Path.Combine(UIAssetsDir, "CapturedAssets", "sprites"), "UIAssets_Captured/"),
            };
        }

        /// <summary>
        /// Resolves a prefixed filename to a full disk path for saving.
        /// </summary>
        private static string ResolveTargetPath(string prefixedName)
        {
            if (prefixedName.StartsWith("UIAssets_Sprites/"))
                return Path.Combine(UIAssetsDir, "Sprites", prefixedName.Substring("UIAssets_Sprites/".Length));
            if (prefixedName.StartsWith("UIAssets_Captured/"))
                return Path.Combine(UIAssetsDir, "CapturedAssets", "sprites", prefixedName.Substring("UIAssets_Captured/".Length));
            return Path.Combine(RuntimeSpritesFolder, prefixedName);
        }

        /// <summary>
        /// Sprite names that our UIs actually reference and need synced to clients.
        /// Only files whose base name (without extension) matches one of these will be
        /// synced from the UIAssets folders. RuntimeSprites/ syncs everything (quest images).
        /// </summary>
        private static readonly HashSet<string> RequiredUISpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "crafting_panel_bkg",   // DialogueUI, InfoNpcPlayerPanel background
            "npcuibkg",             // CodegenUIWiring, UIPrefabFactory, QuestJournal background
            "npcuibkg1",            // CompanionAttributeScreen, CompanionRosterScreen background
            "npcuibkg4",            // VAMiscAssetManager registered sprite
            "va_background5",       // TraderUI background
            "XPIcon_vad",           // QuestInteractionScreen XP icon
            "npc_icon_default",     // Default NPC icon
            "quest_icon_default",   // Default quest icon
            "dialogue_icon_default",// Default dialogue icon
            "panel_background",     // Generic panel background
        };

        /// <summary>
        /// Returns true if a UIAssets file should be synced (its base name is in RequiredUISpriteNames).
        /// RuntimeSprites files always return true (they are admin quest images, all needed).
        /// </summary>
        private static bool IsRequiredForSync(string prefixedName)
        {
            // RuntimeSprites/ - always sync everything (quest preview images)
            if (!prefixedName.StartsWith("UIAssets_")) return true;

            // Extract base name without prefix and extension
            string fileName = prefixedName;
            int slashIdx = fileName.LastIndexOf('/');
            if (slashIdx >= 0) fileName = fileName.Substring(slashIdx + 1);
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            return RequiredUISpriteNames.Contains(baseName);
        }

        // Throttle: max number of file sends per frame on the server to avoid
        // flooding Steam's send buffer and causing client disconnects.
        private const int MaxSendsPerFrame = 2;

        // Throttle: delay between batches of sends (seconds).
        // Gives Steam networking time to flush its buffer.
        private const float SendBatchDelay = 0.15f;

        // Active server-side send coroutine (so we can cancel on shutdown)
        private static Coroutine _activeSendCoroutine;

        // Per-server sprite scope: synced sprites are profile-global on disk, so when a
        // client reconnects to a DIFFERENT server we must drop the previous server's
        // synced sprites first or its quest art / UI sprites bleed onto the new server.
        // Checked once per session in RequestSyncFromServer.
        private static bool _spriteScopeChecked;
        private static string SpriteServerMarkerPath =>
            Path.Combine(FiresCore.Storage.FiresConfigPaths.Cache, "sprite_server.key");

        /// <summary>
        /// Initialize the sprite sync system and register RPCs.
        /// Safe to call multiple times - only registers once per ZRoutedRpc instance.
        /// </summary>
        public static void Init()
        {
            EnsureRpcsRegistered();
        }

        /// <summary>
        /// Resets all static state on logout so that RPCs re-register and sync
        /// re-triggers on the next login. Without this, re-login from main menu
        /// leaves all RPC registrations dead (ZRoutedRpc.instance was destroyed).
        /// </summary>
        public static void ResetForLogout()
        {
            _rpcsRegistered = false;
            _syncRequested = false;
            _syncComplete = false;
            _pendingImages.Clear();
            _activeSendCoroutine = null;
            _spriteScopeChecked = false;
            Debug.Log("[RuntimeSpriteSync] State reset for logout");
        }

        private static void EnsureRpcsRegistered()
        {
            if (_rpcsRegistered) return;
            if (ZRoutedRpc.instance == null) return;

            try
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcRequestSprites, RPC_RequestSprites);
                ZRoutedRpc.instance.Register<ZPackage>(RpcReceiveSprite, RPC_ReceiveSprite);
                ZRoutedRpc.instance.Register<ZPackage>(RpcReceiveSpriteChunk, RPC_ReceiveSpriteChunk);
                ZRoutedRpc.instance.Register<ZPackage>(RpcServerNeedsList, RPC_ServerNeedsList);
                ZRoutedRpc.instance.Register<ZPackage>(RpcPushSprite, RPC_PushSprite);
                ZRoutedRpc.instance.Register<ZPackage>(RpcPushSpriteChunk, RPC_PushSpriteChunk);
                _rpcsRegistered = true;
                Debug.Log("[RuntimeSpriteSync] RPCs registered");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] Failed to register RPCs: {ex.Message}");
            }
        }

        /// <summary>
        /// Request sprite sync from server. Called when Quest Journal is opened.
        /// Uses cooldown to avoid spamming requests.
        /// </summary>
        public static void RequestSyncFromServer()
        {
      EnsureRpcsRegistered();

            if (ZRoutedRpc.instance == null) return;
          if (ZNet.instance == null || ZNet.instance.IsServer()) return;

            // Drop another server's synced sprites before we report our manifest, so the
            // current server re-sends its own (no cross-server quest-art bleed).
            EnsureSpriteScopeMatchesServer();

         // Check cooldown
            var now = DateTime.UtcNow;
      if (_syncRequested && now - _lastSyncRequestTime < SyncCooldown)
            {
  return;
   }

            try
            {
       _syncRequested = true;
                _lastSyncRequestTime = now;

          // Build manifest of what we already have
        var pkg = new ZPackage();
    WriteClientManifest(pkg);
     ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRequestSprites, pkg);
          Debug.Log("[RuntimeSpriteSync] Requested sprite sync from server");
            }
            catch (Exception ex)
        {
       Debug.LogWarning($"[RuntimeSpriteSync] Failed to request sprite sync: {ex.Message}");
      _syncRequested = false;
   }
        }

   /// <summary>
  /// Writes client's current file manifest (filename + MD5 hash) to the package.
        /// Server uses this to determine what files to send.
        /// </summary>
        private static void WriteClientManifest(ZPackage pkg)
        {
    try
       {
          var allFiles = new List<(string PrefixedName, string FullPath)>();
          foreach (var (dir, prefix) in GetSyncFolders())
          {
              if (!Directory.Exists(dir)) continue;
              foreach (var ext in SupportedExtensions)
              {
                  foreach (var file in Directory.GetFiles(dir, $"*{ext}"))
                  {
                      string prefixedName = prefix + Path.GetFileName(file);
                      if (IsRequiredForSync(prefixedName))
                          allFiles.Add((prefixedName, file));
                  }
              }
          }

             pkg.Write(allFiles.Count);
      foreach (var (prefixedName, fullPath) in allFiles)
   {
          string hash = ComputeFileHash(fullPath);
  pkg.Write(prefixedName);
          pkg.Write(hash);
  }
       }
       catch (Exception ex)
             {
                 Debug.LogWarning($"[RuntimeSpriteSync] Failed to build manifest: {ex.Message}");
                 pkg.Write(0);
             }
         }

/// <summary>
        /// Server-side: Handle client request for sprites.
        /// Queues a throttled coroutine to send files across multiple frames
        /// so we don't flood Steam's send buffer and disconnect the client.
        /// </summary>
        private static void RPC_RequestSprites(long sender, ZPackage pkg)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                // Read client's manifest
                var clientHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    int count = pkg.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        string fileName = pkg.ReadString();
                        string hash = pkg.ReadString();
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            clientHashes[fileName] = hash ?? string.Empty;
                        }
                    }
                }
                catch { }

                // Collect files that need sending
                var filesToSend = new List<(string PrefixedName, string FullPath)>();
                int skippedCount = 0;

                foreach (var (dir, prefix) in GetSyncFolders())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var ext in SupportedExtensions)
                    {
                        foreach (var file in Directory.GetFiles(dir, $"*{ext}"))
                        {
                            string prefixedName = prefix + Path.GetFileName(file);
                            if (!IsRequiredForSync(prefixedName)) continue;

                            try
                            {
                                string serverHash = ComputeFileHash(file);
                                if (clientHashes.TryGetValue(prefixedName, out var clientHash) &&
                                    string.Equals(serverHash, clientHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                var fileInfo = new FileInfo(file);
                                if (fileInfo.Length > MaxFileSizeBytes)
                                {
                                    Debug.LogWarning($"[RuntimeSpriteSync] File too large to sync: {prefixedName} ({fileInfo.Length} bytes)");
                                    continue;
                                }

                                filesToSend.Add((prefixedName, file));
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[RuntimeSpriteSync] Error checking file {file}: {ex.Message}");
                            }
                        }
                    }
                }

                if (filesToSend.Count > 0)
                {
                    Debug.Log($"[RuntimeSpriteSync] Queuing {filesToSend.Count} sprites to send to peer {sender} ({skippedCount} unchanged)");

                    // Start a throttled coroutine to drip-feed files to the client
                    if (FiresRPGmaker.Instance != null)
                    {
                        FiresRPGmaker.Instance.StartCoroutine(
                            ThrottledSendToClient(sender, filesToSend));
                    }
                }
                else if (skippedCount > 0)
                {
                    Debug.Log($"[RuntimeSpriteSync] All {skippedCount} sprites already up-to-date on peer {sender}");
                }
                else
                {
                    Debug.Log("[RuntimeSpriteSync] No syncable sprite files found on server");
                }

                // Bidirectional: tell the client which files the server is missing
                // so admin clients can auto-push them.
                var serverNeeds = new List<string>();
                foreach (var kv in clientHashes)
                {
                    string clientFile = kv.Key;
                    if (!IsRequiredForSync(clientFile)) continue;

                    string serverPath = ResolveTargetPath(clientFile);
                    if (!File.Exists(serverPath))
                    {
                        serverNeeds.Add(clientFile);
                        continue;
                    }

                    string sHash = ComputeFileHash(serverPath);
                    if (!string.Equals(sHash, kv.Value, StringComparison.OrdinalIgnoreCase))
                        serverNeeds.Add(clientFile);
                }

                if (serverNeeds.Count > 0)
                {
                    var needsPkg = new ZPackage();
                    needsPkg.Write(serverNeeds.Count);
                    foreach (var name in serverNeeds)
                        needsPkg.Write(name);
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcServerNeedsList, needsPkg);
                    Debug.Log($"[RuntimeSpriteSync] Told peer {sender} that server needs {serverNeeds.Count} file(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] RPC_RequestSprites failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Server-side coroutine: sends files to a client with throttling.
        /// Sends MaxSendsPerFrame files per frame, then yields to let Steam
        /// flush its networking buffer. This prevents disconnects when syncing
        /// many or large files.
        /// </summary>
        private static IEnumerator ThrottledSendToClient(
            long target, List<(string PrefixedName, string FullPath)> files)
        {
            int sentCount = 0;
            int sendThisFrame = 0;

            foreach (var (prefixedName, filePath) in files)
            {
                // Bail out if the peer disconnected mid-transfer
                if (ZRoutedRpc.instance == null || ZNet.instance == null)
                {
                    Debug.LogWarning("[RuntimeSpriteSync] Aborting throttled send - ZNet gone");
                    yield break;
                }

                try
                {
                    SendSpriteToClient(target, prefixedName, filePath);
                    sentCount++;
                    sendThisFrame++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RuntimeSpriteSync] Failed to send {prefixedName}: {ex.Message}");
                }

                // Throttle: after MaxSendsPerFrame sends, yield to let the
                // networking layer flush. This is the key disconnect prevention.
                if (sendThisFrame >= MaxSendsPerFrame)
                {
                    sendThisFrame = 0;
                    yield return new WaitForSeconds(SendBatchDelay);
                }
            }

            Debug.Log($"[RuntimeSpriteSync] Throttled send complete: {sentCount}/{files.Count} sprites to peer {target}");
        }

        /// <summary>
        /// Client-side: server told us which files it's missing.
        /// If we're admin, push those files to the server.
        /// </summary>
        private static void RPC_ServerNeedsList(long sender, ZPackage pkg)
        {
            try
            {
                int count = pkg.ReadInt();
                if (count <= 0) return;

                var neededFiles = new List<string>();
                for (int i = 0; i < count; i++)
                    neededFiles.Add(pkg.ReadString());

                // Only push if we're admin
                bool isAdmin = FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false;
                if (!isAdmin)
                {
                    Debug.Log($"[RuntimeSpriteSync] Server needs {count} file(s) but we're not admin - skipping push");
                    return;
                }

                Debug.Log($"[RuntimeSpriteSync] Server needs {count} file(s) - auto-pushing as admin (staggered)");
                if (FiresRPGmaker.Instance != null)
                    FiresRPGmaker.Instance.StartCoroutine(StaggeredAutoPush(neededFiles));
                else
                    Debug.LogWarning("[RuntimeSpriteSync] Cannot stagger push - no MonoBehaviour instance");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] RPC_ServerNeedsList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Pushes files to the server with a delay between each to avoid flooding
        /// the network buffer and causing disconnects.
        /// </summary>
        private static IEnumerator StaggeredAutoPush(List<string> neededFiles)
        {
            int pushed = 0;
            int total = neededFiles.Count;

            foreach (var prefixedName in neededFiles)
            {
                if (ZRoutedRpc.instance == null) yield break;

                try
                {
                    string localPath = ResolveTargetPath(prefixedName);
                    if (!File.Exists(localPath))
                    {
                        Debug.LogWarning($"[RuntimeSpriteSync] Server needs '{prefixedName}' but file not found locally");
                        continue;
                    }

                    var fileInfo = new FileInfo(localPath);
                    if (fileInfo.Length > MaxFileSizeBytes) continue;

                    byte[] fileData = File.ReadAllBytes(localPath);
                    byte[] dataToSend = fileData;
                    bool isCompressed = false;

                    byte[] compressed = CompressBytes(fileData);
                    if (compressed != null && compressed.Length < fileData.Length)
                    {
                        dataToSend = compressed;
                        isCompressed = true;
                    }

                    SendSpriteToServer(prefixedName, dataToSend, isCompressed);
                    pushed++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RuntimeSpriteSync] Failed to auto-push '{prefixedName}': {ex.Message}");
                }

                // Stagger: wait a short time between files to avoid network flooding
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"[RuntimeSpriteSync] Auto-pushed {pushed}/{total} file(s) to server (staggered)");
        }

    /// <summary>
       /// Send a sprite file to a specific client, chunked if necessary.
         /// </summary>
        private static void SendSpriteToClient(long target, string fileName, string filePath)
        {
            try
        {
  byte[] fileData = File.ReadAllBytes(filePath);
   byte[] dataToSend = fileData;
       bool isCompressed = false;

        // Try to compress
                byte[] compressed = CompressBytes(fileData);
       if (compressed != null && compressed.Length < fileData.Length)
     {
   dataToSend = compressed;
        isCompressed = true;
                }

         int totalSize = dataToSend.Length;
   int totalChunks = (int)Math.Ceiling((double)totalSize / ChunkSize);

     // If small enough, send in single message
        if (totalSize <= ChunkSize)
       {
var pkg = new ZPackage();
          pkg.Write(fileName);
   pkg.Write(isCompressed);
    pkg.Write(totalSize);
              pkg.Write(dataToSend);
              // Size-gated dispatch (refuses >480 KB payloads before Steam asserts).
              SafeRoutedRpc.InvokeSafe(target, RpcReceiveSprite, pkg);
           Debug.Log($"[RuntimeSpriteSync] Sent sprite {fileName} ({totalSize} bytes, compressed={isCompressed})");
        }
              else
                {
                    // Paced chunked send: yields a frame between chunks so ZSteamSocket
                    // flushes each chunk as its own Steam message instead of concatenating
                    // them into one oversize send (which trips the 512 KB hard cap).
                    string fileNameCapture = fileName;
                    bool isCompressedCapture = isCompressed;
                    int totalSizeCapture = totalSize;

                    IEnumerator routine = SafeRoutedRpc.PacedChunkedSend(
                        target,
                        RpcReceiveSpriteChunk,
                        dataToSend,
                        ChunkSize,
                        (pkg, chunkIndex, chunksTotal, slice) =>
                        {
                            pkg.Write(fileNameCapture);
                            pkg.Write(isCompressedCapture);
                            pkg.Write(totalSizeCapture);
                            pkg.Write(chunksTotal);
                            pkg.Write(chunkIndex);
                            pkg.Write(slice.Length);
                            pkg.Write(slice);
                        });
                    MainThreadDispatcher.StartRoutine(routine);
                    Debug.Log($"[RuntimeSpriteSync] Queued paced send for sprite {fileName} in {totalChunks} chunks ({totalSize} bytes total, compressed={isCompressed})");
     }
     }
            catch (Exception ex)
     {
    Debug.LogWarning($"[RuntimeSpriteSync] Failed to send sprite {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Client-side: Receive a small sprite file from server (single message).
        /// </summary>
        private static void RPC_ReceiveSprite(long sender, ZPackage pkg)
      {
         try
  {
          string fileName = pkg.ReadString();
          bool isCompressed = pkg.ReadBool();
            int totalSize = pkg.ReadInt();
      byte[] data = pkg.ReadByteArray();

      if (!ValidateFileName(fileName)) return;

      // Decompress if needed
                byte[] fileData = isCompressed ? DecompressBytes(data) : data;
         if (fileData == null || fileData.Length == 0)
      {
    Debug.LogWarning($"[RuntimeSpriteSync] Failed to decompress sprite: {fileName}");
           return;
    }

    SaveSpriteFile(fileName, fileData);
    }
    catch (Exception ex)
    {
                Debug.LogWarning($"[RuntimeSpriteSync] RPC_ReceiveSprite failed: {ex.Message}");
     }
        }

        /// <summary>
    /// Client-side: Receive a chunk of a sprite file from server.
      /// </summary>
  private static void RPC_ReceiveSpriteChunk(long sender, ZPackage pkg)
        {
      try
         {
   string fileName = pkg.ReadString();
           bool isCompressed = pkg.ReadBool();
        int totalSize = pkg.ReadInt();
    int totalChunks = pkg.ReadInt();
                int chunkIndex = pkg.ReadInt();
                int chunkLength = pkg.ReadInt();
   byte[] chunkData = pkg.ReadByteArray();

     if (!ValidateFileName(fileName)) return;

     // Get or create pending image data
        if (!_pendingImages.TryGetValue(fileName, out var pending))
     {
        pending = new PendingImageData
  {
         Data = new byte[totalSize],
      TotalSize = totalSize,
     ReceivedSize = 0,
    TotalChunks = totalChunks,
   ReceivedChunks = 0,
       IsCompressed = isCompressed
      };
  _pendingImages[fileName] = pending;
      }

       // Copy chunk data to correct position
          int offset = chunkIndex * ChunkSize;
         Array.Copy(chunkData, 0, pending.Data, offset, chunkLength);
        pending.ReceivedSize += chunkLength;
        pending.ReceivedChunks++;

// Check if complete
    if (pending.ReceivedChunks >= pending.TotalChunks)
      {
                    _pendingImages.Remove(fileName);

            // Decompress if needed
        byte[] fileData = pending.IsCompressed ? DecompressBytes(pending.Data) : pending.Data;
  if (fileData == null || fileData.Length == 0)
       {
       Debug.LogWarning($"[RuntimeSpriteSync] Failed to decompress chunked sprite: {fileName}");
        return;
 }

       SaveSpriteFile(fileName, fileData);
           Debug.Log($"[RuntimeSpriteSync] Assembled sprite {fileName} from {pending.TotalChunks} chunks");
  }
            }
            catch (Exception ex)
  {
   Debug.LogWarning($"[RuntimeSpriteSync] RPC_ReceiveSpriteChunk failed: {ex.Message}");
      }
        }

    /// <summary>
      /// Validate filename for security.
        /// </summary>
    private static bool ValidateFileName(string fileName)
        {
      if (string.IsNullOrEmpty(fileName))
             {
        Debug.LogWarning("[RuntimeSpriteSync] Received sprite with empty filename");
    return false;
          }

     // Allow our known prefixes, but strip them for further validation
     string baseName = fileName;
     if (baseName.StartsWith("UIAssets_Sprites/")) baseName = baseName.Substring("UIAssets_Sprites/".Length);
     else if (baseName.StartsWith("UIAssets_Captured/")) baseName = baseName.Substring("UIAssets_Captured/".Length);

   // Strip any path components from the base name
   baseName = Path.GetFileName(baseName);
         if (baseName.Contains(".."))
             {
        Debug.LogWarning($"[RuntimeSpriteSync] Invalid filename rejected: {fileName}");
              return false;
             }

  // Validate extension
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        bool validExt = false;
    foreach (var supportedExt in SupportedExtensions)
         {
           if (ext == supportedExt)
   {
        validExt = true;
   break;
 }
            }
          if (!validExt)
    {
 Debug.LogWarning($"[RuntimeSpriteSync] Unsupported file extension rejected: {fileName}");
           return false;
       }

       return true;
        }

        /// <summary>
        /// Save sprite file to disk and clear cache.
        /// </summary>
        private static void SaveSpriteFile(string fileName, byte[] fileData)
        {
            try
   {
      // Resolve target directory from the prefixed filename
      string fullPath = ResolveTargetPath(fileName);
      string dir = Path.GetDirectoryName(fullPath);
      if (!string.IsNullOrEmpty(dir))
          Directory.CreateDirectory(dir);
         File.WriteAllBytes(fullPath, fileData);
        Debug.Log($"[RuntimeSpriteSync] Saved sprite: {fileName} ({fileData.Length} bytes) -> {fullPath}");

   // Clear the sprite cache so the new image is used
                 ClearCachedSprite(fileName);

      // If this is a UIAssets sprite, clear VAMiscAssetManager's negative cache
      // so it re-checks disk on next GetSprite() call
      if (fileName.StartsWith("UIAssets_"))
      {
          try { FiresCore.UI.VAMiscAssetManager.ClearNegativeCache(); } catch { }
      }
          }
       catch (Exception ex)
             {
            Debug.LogWarning($"[RuntimeSpriteSync] Failed to save sprite {fileName}: {ex.Message}");
           }
         }

  /// <summary>
        /// Clear a cached sprite so the newly synced image is loaded fresh.
        /// </summary>
        private static void ClearCachedSprite(string fileName)
        {
    try
            {
       string fullPath = ResolveTargetPath(fileName);

             // Access the cache in QuestJournalController via reflection
      var cacheField = typeof(QuestJournalController).GetField("_spriteCache",
                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

 if (cacheField != null)
          {
           var cache = cacheField.GetValue(null) as Dictionary<string, Texture2D>;
   if (cache != null && cache.ContainsKey(fullPath))
          {
    // Destroy the old texture to free memory
              if (cache[fullPath] != null)
     {
UnityEngine.Object.Destroy(cache[fullPath]);
 }
  cache.Remove(fullPath);
   }
       }
            }
            catch { }
        }

        /// <summary>
        /// Once per session, if the synced sprites on disk belong to a different server
        /// than the one we're now on, delete them and clear sprite caches so the current
        /// server re-syncs its own. Same-server reconnects keep the cache (no re-download).
        /// </summary>
        private static void EnsureSpriteScopeMatchesServer()
        {
            if (_spriteScopeChecked) return;
            string key = FiresCore.Sync.FiresServerScope.Key();
            if (string.IsNullOrEmpty(key)) return; // server not resolved yet; re-check next call
            _spriteScopeChecked = true;
            try
            {
                string marker = SpriteServerMarkerPath;
                string prev = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
                if (prev == key) return; // same server — keep the synced sprite cache

                foreach (var (dir, _) in GetSyncFolders())
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        foreach (var ext in SupportedExtensions)
                            foreach (var f in Directory.GetFiles(dir, $"*{ext}", SearchOption.TopDirectoryOnly))
                                try { File.Delete(f); } catch { }
                    }
                    catch { }
                }

                try { FiresCore.UI.VAMiscAssetManager.ClearAllCaches(); } catch { }
                ClearAllQuestJournalSprites();

                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllText(marker, key);
                Debug.Log($"[RuntimeSpriteSync] Server changed ({prev ?? "none"} -> {key}); cleared synced sprite cache to prevent cross-server bleed.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] Sprite scope check failed: {ex.Message}");
            }
        }

        /// <summary>Clears the entire QuestJournalController sprite cache (destroying textures).</summary>
        private static void ClearAllQuestJournalSprites()
        {
            try
            {
                var cacheField = typeof(QuestJournalController).GetField("_spriteCache",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (cacheField?.GetValue(null) is Dictionary<string, Texture2D> cache)
                {
                    foreach (var tex in cache.Values)
                        if (tex != null) UnityEngine.Object.Destroy(tex);
                    cache.Clear();
                }
            }
            catch { }
        }

        /// <summary>
   /// Compute MD5 hash of a file.
        /// </summary>
   private static string ComputeFileHash(string filePath)
        {
  try
         {
    using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hash = md5.ComputeHash(stream);
         var sb = new StringBuilder(hash.Length * 2);
              foreach (var b in hash)
      {
          sb.Append(b.ToString("x2"));
    }
             return sb.ToString();
     }
catch
          {
                return string.Empty;
  }
    }

        /// <summary>
        /// Compress bytes using Deflate with optimal compression for images.
        /// </summary>
        private static byte[] CompressBytes(byte[] input)
        {
        if (input == null || input.Length == 0) return null;
      try
          {
    using var ms = new MemoryStream();
         // Use Optimal compression for better ratio on image data
    using (var ds = new DeflateStream(ms, IOCompressionLevel.Optimal, true))
       {
              ds.Write(input, 0, input.Length);
           }
       return ms.ToArray();
            }
            catch
            {
       return null;
            }
        }

      /// <summary>
        /// Decompress bytes using Deflate.
        /// </summary>
  private static byte[] DecompressBytes(byte[] input)
        {
            if (input == null || input.Length == 0) return null;
         try
            {
              using var inputMs = new MemoryStream(input);
                using var ds = new DeflateStream(inputMs, CompressionMode.Decompress);
           using var output = new MemoryStream();
        ds.CopyTo(output);
      return output.ToArray();
            }
   catch
    {
                return null;
    }
   }

        /// <summary>
 /// Check if sync is complete (for UI feedback).
        /// </summary>
        public static bool IsSyncComplete => _syncComplete;

        /// <summary>
        /// Reset sync state (e.g., on logout).
        /// </summary>
        public static void Reset()
        {
            _syncRequested = false;
            _syncComplete = false;
            _pendingImages.Clear();
        }

        #region Client-to-Server Push (admin pushcfgs)

        /// <summary>
        /// Push all PNG/JPG files from all syncable sprite folders to the server.
        /// Called by the pushcfgs console command. Only sends files modified since lastCutoff
        /// unless forceAll is true. Returns the count of files sent.
        /// Scans RuntimeSprites, UIAssets/Sprites, and UIAssets/CapturedAssets/sprites.
        /// </summary>
        public static int PushSpritesToServer(DateTime lastCutoff, bool forceAll, Action<string> log = null)
        {
            EnsureRpcsRegistered();

            if (ZRoutedRpc.instance == null)
            {
                log?.Invoke("[RuntimeSpriteSync] No RPC instance available");
                return 0;
            }

            var filesToPush = new List<(string PrefixedName, string FullPath)>();
            int skipped = 0;

            foreach (var (dir, prefix) in GetSyncFolders())
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var ext in SupportedExtensions)
                {
                    foreach (var f in Directory.GetFiles(dir, $"*{ext}"))
                    {
                        try
                        {
                            string prefixedName = prefix + Path.GetFileName(f);

                            // Only push required UI sprites from UIAssets folders
                            if (!IsRequiredForSync(prefixedName))
                                continue;

                            var info = new FileInfo(f);
                            if (info.Length > MaxFileSizeBytes)
                            {
                                log?.Invoke($"[RuntimeSpriteSync] Skipping {Path.GetFileName(f)} - too large ({info.Length} bytes)");
                                continue;
                            }

                            if (!forceAll && info.LastWriteTimeUtc <= lastCutoff)
                            {
                                skipped++;
                                continue;
                            }

                            filesToPush.Add((prefixedName, f));
                        }
                        catch { }
                    }
                }
            }

            if (filesToPush.Count == 0)
            {
                if (skipped > 0)
                    log?.Invoke($"[RuntimeSpriteSync] No modified sprites to push ({skipped} unchanged)");
                return 0;
            }

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            int sent = 0;

            foreach (var (prefixedName, filePath) in filesToPush)
            {
                try
                {
                    byte[] fileData = File.ReadAllBytes(filePath);
                    byte[] dataToSend = fileData;
                    bool isCompressed = false;

                    byte[] compressed = CompressBytes(fileData);
                    if (compressed != null && compressed.Length < fileData.Length)
                    {
                        dataToSend = compressed;
                        isCompressed = true;
                    }

                    if (isServer)
                    {
                        // Server: save locally and broadcast to all clients
                        SaveSpriteFile(prefixedName, fileData);
                        BroadcastSpriteToAllClients(prefixedName, filePath);
                    }
                    else
                    {
                        // Client: send to server via push RPCs (use prefixed name so server saves to correct folder)
                        SendSpriteToServer(prefixedName, dataToSend, isCompressed);
                    }

                    sent++;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[RuntimeSpriteSync] Failed to push {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            log?.Invoke($"[RuntimeSpriteSync] Pushed {sent} sprite(s) to server" +
                       (skipped > 0 ? $" ({skipped} unchanged skipped)" : ""));
            return sent;
        }

        /// <summary>
        /// Client-side: send a sprite to the server, chunked if necessary.
        /// Oversize sends are refused by <see cref="SafeRoutedRpc.InvokeSafe"/>
        /// and the chunked path is paced (one chunk per frame) so Steam's 512 KB
        /// per-message cap is never breached by queue concatenation.
        /// </summary>
        private static void SendSpriteToServer(string fileName, byte[] dataToSend, bool isCompressed)
        {
            int totalSize = dataToSend.Length;
            int totalChunks = (int)Math.Ceiling((double)totalSize / ChunkSize);

            if (totalSize <= ChunkSize)
            {
                var pkg = new ZPackage();
                pkg.Write(fileName);
                pkg.Write(isCompressed);
                pkg.Write(totalSize);
                pkg.Write(dataToSend);
                SafeRoutedRpc.InvokeSafe(0L, RpcPushSprite, pkg);
            }
            else
            {
                string fileNameCapture = fileName;
                bool isCompressedCapture = isCompressed;
                int totalSizeCapture = totalSize;

                IEnumerator routine = SafeRoutedRpc.PacedChunkedSend(
                    0L,
                    RpcPushSpriteChunk,
                    dataToSend,
                    ChunkSize,
                    (pkg, chunkIndex, chunksTotal, slice) =>
                    {
                        pkg.Write(fileNameCapture);
                        pkg.Write(isCompressedCapture);
                        pkg.Write(totalSizeCapture);
                        pkg.Write(chunksTotal);
                        pkg.Write(chunkIndex);
                        pkg.Write(slice.Length);
                        pkg.Write(slice);
                    });
                MainThreadDispatcher.StartRoutine(routine);
            }
        }

        /// <summary>
        /// Server-side: receive a small pushed sprite from an admin client (single message).
        /// Saves to disk and broadcasts to all other clients.
        /// </summary>
        private static void RPC_PushSprite(long sender, ZPackage pkg)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                string fileName = pkg.ReadString();
                bool isCompressed = pkg.ReadBool();
                int totalSize = pkg.ReadInt();
                byte[] data = pkg.ReadByteArray();

                if (!ValidateFileName(fileName)) return;

                byte[] fileData = isCompressed ? DecompressBytes(data) : data;
                if (fileData == null || fileData.Length == 0)
                {
                    Debug.LogWarning($"[RuntimeSpriteSync] Failed to decompress pushed sprite: {fileName}");
                    return;
                }

                SaveSpriteFile(fileName, fileData);
                Debug.Log($"[RuntimeSpriteSync] Received pushed sprite from peer {sender}: {fileName}");

                // Broadcast to all connected clients (except the sender)
                string savedPath = ResolveTargetPath(fileName);
                BroadcastSpriteToAllClients(fileName, savedPath, sender);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] RPC_PushSprite failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Server-side: receive a chunk of a pushed sprite from an admin client.
        /// Accumulates chunks, then saves and broadcasts when complete.
        /// </summary>
        private static void RPC_PushSpriteChunk(long sender, ZPackage pkg)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                string fileName = pkg.ReadString();
                bool isCompressed = pkg.ReadBool();
                int totalSize = pkg.ReadInt();
                int totalChunks = pkg.ReadInt();
                int chunkIndex = pkg.ReadInt();
                int chunkLength = pkg.ReadInt();
                byte[] chunkData = pkg.ReadByteArray();

                if (!ValidateFileName(fileName)) return;

                string key = $"push_{sender}_{fileName}";
                if (!_pendingImages.TryGetValue(key, out var pending))
                {
                    pending = new PendingImageData
                    {
                        Data = new byte[totalSize],
                        TotalSize = totalSize,
                        ReceivedSize = 0,
                        TotalChunks = totalChunks,
                        ReceivedChunks = 0,
                        IsCompressed = isCompressed
                    };
                    _pendingImages[key] = pending;
                }

                int offset = chunkIndex * ChunkSize;
                Array.Copy(chunkData, 0, pending.Data, offset, chunkLength);
                pending.ReceivedSize += chunkLength;
                pending.ReceivedChunks++;

                if (pending.ReceivedChunks >= pending.TotalChunks)
                {
                    _pendingImages.Remove(key);

                    byte[] fileData = pending.IsCompressed ? DecompressBytes(pending.Data) : pending.Data;
                    if (fileData == null || fileData.Length == 0)
                    {
                        Debug.LogWarning($"[RuntimeSpriteSync] Failed to decompress chunked pushed sprite: {fileName}");
                        return;
                    }

                    SaveSpriteFile(fileName, fileData);
                    Debug.Log($"[RuntimeSpriteSync] Assembled pushed sprite from peer {sender}: {fileName} ({totalChunks} chunks)");

                    string savedPath = ResolveTargetPath(fileName);
                    BroadcastSpriteToAllClients(fileName, savedPath, sender);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] RPC_PushSpriteChunk failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Server-side: broadcast a sprite file to all connected clients.
        /// Optionally excludes a specific peer (e.g., the admin who pushed it).
        /// </summary>
        private static void BroadcastSpriteToAllClients(string fileName, string filePath, long excludePeer = 0)
        {
            try
            {
                if (ZNet.instance == null) return;

                var peers = ZNet.instance.GetPeers();
                if (peers == null || peers.Count == 0) return;

                foreach (var peer in peers)
                {
                    if (peer == null) continue;
                    long peerUid = peer.m_uid;
                    if (peerUid == excludePeer) continue;

                    SendSpriteToClient(peerUid, fileName, filePath);
                }

                Debug.Log($"[RuntimeSpriteSync] Broadcast sprite {fileName} to {peers.Count} client(s)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSync] BroadcastSpriteToAllClients failed: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Harmony patches that manage RuntimeSpriteSync lifecycle across login/logout.
    /// 
    /// Problem: RuntimeSpriteSync.Init() is called once from WaitForZNetReady() which
    /// only runs on first plugin load. On logout-to-main-menu and re-login, the static
    /// _rpcsRegistered flag stays true but ZRoutedRpc.instance was destroyed, so all
    /// RPC registrations are dead. _syncRequested also stays true, blocking re-sync.
    /// 
    /// Solution:
    /// - Game.Logout: Reset all static state so RPCs re-register on next login.
    /// - Player.OnSpawned: Re-register RPCs and request sprite sync from server.
    ///   This fires on every login (including re-login) when the local player spawns.
    /// </summary>
    [HarmonyPatch]
    public static class RuntimeSpriteSyncPatches
    {
        [HarmonyPatch(typeof(Game), "Logout")]
        [HarmonyPostfix]
        private static void Game_Logout_Postfix()
        {
            RuntimeSpriteSync.ResetForLogout();
            global::FiresRPGmaker.Modules.Sounds.NpcSoundSync.ResetForLogout();
            global::FiresRPGmaker.UI.PatrolRouteSync.ResetForLogout();
            global::FiresRPGmaker.UI.NpcBookController.VaPlayerPanel(false);   // 0.0.549: never leave the VA player panel hidden after a logout
        }

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        private static void Player_OnSpawned_Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return;

                // DIAG (login-freeze)
                FiresLogger.LogVerbose("[LoginFreeze][DIAG] ENTER RuntimeSpriteSync.Player_OnSpawned_Postfix");

                // Re-register RPCs (safe if already registered for this session)
                RuntimeSpriteSync.Init();

                // Request sprite sync from server on every spawn (including re-login).
                // The 30s cooldown inside RequestSyncFromServer prevents spam if
                // the player dies and respawns quickly.
                if (ZNet.instance != null && !ZNet.instance.IsServer())
                {
                    // Small delay to let the connection fully stabilize
                    if (FiresRPGmaker.Instance != null)
                    {
                        FiresRPGmaker.Instance.StartCoroutine(
                            DeferredSyncOnSpawn());
                    }
                }

                FiresLogger.LogVerbose("[LoginFreeze][DIAG] EXIT RuntimeSpriteSync.Player_OnSpawned_Postfix");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RuntimeSpriteSyncPatches] OnSpawned error: {ex.Message}");
            }
        }

        private static IEnumerator DeferredSyncOnSpawn()
        {
            // Wait a few seconds for the connection to fully stabilize and
            // server-side RPC handlers to finish registering
            yield return new WaitForSeconds(5f);

            if (ZRoutedRpc.instance != null && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                RuntimeSpriteSync.RequestSyncFromServer();
            }
        }
    }
}
