using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using BepInEx; // for Paths
using System.Security.Cryptography;
using System.Text;
using FiresRPGmaker; // for AdminSyncing
using System.IO;
using System.IO.Compression;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Simple territory sync payload. Server loads territories and sends to clients via RPC.
    /// </summary>
    public static class TerritorySyncedState
    {
        private static bool _rpcsRegistered;
        public static List<Territory> ClientTerritories { get; private set; } = new();
        private static readonly Dictionary<long, List<Territory>> _pendingChunks = new();
        private static readonly List<CombinedTerritory> _combinedClient = new();
        private const int MaxTerritoriesPerChunk = 25;

        private sealed class CombinedTerritory
        {
            public string Name;
            public Vector2 Min;
            public Vector2 Max;
            public readonly List<Territory> Members = new();
        }

        private static readonly Dictionary<long, DateTime> _recentResponses = new();
        private static readonly TimeSpan ResponseCooldown = TimeSpan.FromSeconds(10);

        // Throttled outbound chunk queue. Sending 12+ routed RPCs in a single frame
        // (one per territory chunk) triggers Steam's k_EResultLimitExceeded and silently
        // drops chunks ? clients then never get a "final chunk" and keep re-requesting.
        // We stage each chunk here and let DrainSendQueue() pop one per frame so the
        // stream stays under Steam's rate limit.
        private static readonly Queue<Action> _sendQueue = new Queue<Action>();
        private const int MaxChunksPerFrame = 2;

        private const string RpcRequest = "FiresRPGmaker_Territory_Request";
        private const string RpcReceive = "FiresRPGmaker_Territory_Receive";
        private const string RpcReceiveCompressed = "FiresRPGmaker_Territory_ReceiveC";
        private const string RpcFileUpdate = "FiresRPGmaker_Territory_FileUpdate";

        /// <summary>
        /// Namespace key used by <see cref="ManifestDiffSync"/> so the
        /// server can skip re-sending the full territory snapshot to a peer that already
        /// has a client-cached SHA-256 matching the current server ledger. Persisted per
        /// session under <c>BepInEx/config/FiresRPGmaker/cache/manifests/Territories.hash</c>.
        /// </summary>
        public const string TerritoriesManifestNamespace = "Territories";

        /// <summary>
        /// Deterministic fingerprint of a territory list. Both server and client invoke
        /// this on their own in-memory copy; if the <see cref="Territory.Serialize"/>
        /// output is stable (which it is for the scalar/string fields Territory actually
        /// persists) the hashes match byte-for-byte and the server's "do you already have
        /// this?" check succeeds.
        /// </summary>
        private static string ComputeTerritoriesFingerprint(IReadOnlyList<Territory> data)
        {
            try
            {
                var pkg = new ZPackage();
                int count = data?.Count ?? 0;
                pkg.Write(count);
                if (data != null)
                    for (int i = 0; i < count; i++) data[i].Serialize(pkg);
                byte[] bytes = TryGetPackageBytes(pkg) ?? Array.Empty<byte>();
                return ManifestDiffSync.ComputeSha256(bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] ComputeTerritoriesFingerprint failed: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool _awaitingTerritories;
        private static DateTime _lastRequestTimeUtc;
        private static readonly TimeSpan RequestCooldown = TimeSpan.FromSeconds(10);
        private static int _requestRetryCount;
        private const int MaxRequestRetries = 5;
        private const float RetryIntervalSeconds = 12f;
        private static readonly TimeSpan PendingChunkTimeout = TimeSpan.FromSeconds(30);

        // Debounce: batch multiple rapid territory file updates into one reload+broadcast
        private static bool _reloadPending;
        private static float _reloadScheduledTime;
        private const float ReloadDebounceSeconds = 0.5f;
        private static Coroutine _reloadCoroutine;

        public static void Init()
        {
            EnsureRpcs();
            // server loads data once
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                TerritoryLedger.Init();
            }
            else
            {
                // Client: hydrate ClientTerritories from the on-disk cache so we
                // don't start the session with Count==0 and trigger a full
                // re-download from the server on every login. The cache is
                // refreshed by HandleReceive whenever the server actually sends
                // a new payload, and the server can short-circuit by comparing
                // its current fingerprint with the persisted blob hash that
                // RememberBlobHash wrote alongside the cache.
                LoadClientCacheFromDisk();
            }
        }

        /// <summary>
        /// Public entry point to register territory RPCs as early as possible (from the plugin
        /// Awake/FinishInit path) so that request/receive handlers are guaranteed to be bound
        /// before ZNetScene/WaitForZNetReady runs. Safe to call multiple times \u2014 internal guard.
        /// </summary>
        public static void EnsureRpcsRegistered() => EnsureRpcs();

        private static void EnsureRpcs()
        {
            if (_rpcsRegistered) return;
            if (ZRoutedRpc.instance == null) return;
            try
            {
                TryRegister(RpcRequest, (Action<long, ZPackage>)RPC_Request);
                TryRegister(RpcReceive, (Action<long, ZPackage>)RPC_Receive);
                TryRegister(RpcReceiveCompressed, (Action<long, ZPackage>)RPC_ReceiveCompressed);
                TryRegister(RpcFileUpdate, (Action<long, ZPackage>)RPC_FileUpdate);
                _rpcsRegistered = true;
                FiresLogger.LogVerbose("[FiresRPGmaker] Territory RPCs registered");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory RPC register failed: {ex.Message}");
            }
        }

        private static void TryRegister(string name, Action<long, ZPackage> handler)
        {
            try { ZRoutedRpc.instance.Register<ZPackage>(name, handler); }
            catch (Exception ex)
            {
                // Duplicate registrations will throw; log once and continue
                // Debug.LogWarning($"[FiresRPGmaker] RPC register '{name}' skipped: {ex.Message}");
            }
        }

        // Client call: ask server for territories
        public static void RequestFromServer()
        {
            EnsureRpcs();
            if (ZRoutedRpc.instance == null)
            {
                Debug.LogWarning("[FiresRPGmaker] Territory RequestFromServer aborted: ZRoutedRpc.instance is null");
                return;
            }

            // REMOVED: Skip check if territories exist in memory
            // The client should ALWAYS send the manifest so the server can determine
            // if the client needs updates. The old check prevented delta sync from working.
            // Old code:
            // if (ClientTerritories.Count > 0 && _pendingChunks.Count == 0) { return; }

            var now = DateTime.UtcNow;
            if (_awaitingTerritories && now - _lastRequestTimeUtc < RequestCooldown)
            {
                FiresLogger.LogVerbose("[FiresRPGmaker] Territory RequestFromServer dropped: already awaiting response (cooldown)");
                return;
            }

            // Clear stale pending chunks from a previous failed attempt
            if (_pendingChunks.Count > 0 && now - _lastRequestTimeUtc > PendingChunkTimeout)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory clearing {_pendingChunks.Count} stale pending chunk entries (timed out)");
                _pendingChunks.Clear();
            }

            try
            {
                _awaitingTerritories = true;
                _lastRequestTimeUtc = now;
                _requestRetryCount++;
                var pkg = new ZPackage();
                WriteClientManifest(pkg);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRequest, pkg);
                Debug.Log($"[FiresRPGmaker] Territory RequestFromServer sent to server (attempt {_requestRetryCount}, have {ClientTerritories.Count} in cache)");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory request failed: {ex.Message}");
                _awaitingTerritories = false;
            }
        }

        /// <summary>
        /// Returns true if the client still needs territories (no data received yet).
        /// Used by the retry coroutine in Ascend to periodically re-request.
        /// </summary>
        public static bool NeedsRetry
        {
            get
            {
                if (ZNet.instance == null || ZNet.instance.IsServer()) return false;
                if (ClientTerritories.Count > 0) return false;
                if (_requestRetryCount >= MaxRequestRetries) return false;
                return true;
            }
        }

        /// <summary>
        /// Reset retry state. Called when territories are successfully received.
        /// </summary>
        private static void ResetRetryState()
        {
            _requestRetryCount = 0;
            _awaitingTerritories = false;
        }

        // Build client-side file manifest (name + hash) to allow delta sync
        private static void WriteClientManifest(ZPackage pkg)
        {
            // Lead with the persistent blob fingerprint of the cached client
            // ClientTerritories. If this matches the server's current
            // ComputeTerritoriesFingerprint, the server can immediately answer
            // with a 0-count "you're up-to-date" ack and skip the per-file MD5
            // walk entirely. Older servers that don't read this field still see
            // a valid file-count int next, so the wire format stays compatible.
            try
            {
                pkg.Write(ManifestDiffSync.GetPersistedBlobHash(
                    TerritoriesManifestNamespace) ?? string.Empty);
            }
            catch
            {
                pkg.Write(string.Empty);
            }

            try
            {
                string baseDir = System.IO.Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Territories");
                if (!System.IO.Directory.Exists(baseDir)) { pkg.Write(0); return; }
                var files = System.IO.Directory.GetFiles(baseDir, "*.cfg", System.IO.SearchOption.AllDirectories);
                pkg.Write(files.Length);
                string baseFull = System.IO.Path.GetFullPath(baseDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                foreach (var file in files)
                {
                    string full = System.IO.Path.GetFullPath(file);
                    string rel = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                        ? full.Substring(baseFull.Length + 1)
                        : System.IO.Path.GetFileName(file);
                    pkg.Write(rel);
                    pkg.Write(ComputeFileHash(file));
                }
            }
            catch
            {
                pkg.Write(0);
            }
        }

        private static string ComputeFileHash(string path)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = System.IO.File.OpenRead(path);
                var hash = md5.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private static void RPC_Request(long sender, ZPackage pkg)
        {
            try
            {
                string clientBlobHash = string.Empty;
                Dictionary<string, string> clientHashes = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    // Wire format: <blobHashString><fileCount><relPath/hash * fileCount>
                    // Old clients didn't write the leading hash, so a stray exception
                    // here drops us into the catch and we treat the whole package as
                    // legacy-format with empty hashes (preserves backward compat).
                    clientBlobHash = pkg.ReadString() ?? string.Empty;
                    int count = pkg.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        string fname = pkg.ReadString();
                        string hash = pkg.ReadString();
                        if (!string.IsNullOrEmpty(fname)) clientHashes[fname] = hash ?? string.Empty;
                    }
                }
                catch { /* old clients send empty pkg */ }

                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                var now = DateTime.UtcNow;
                if (_recentResponses.TryGetValue(sender, out var last) && now - last < ResponseCooldown)
                {
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Territory RPC_Request from {sender} dropped (within {ResponseCooldown.TotalSeconds}s cooldown)");
                    return; // avoid duplicate sends within cooldown
                }
                _recentResponses[sender] = now;

                TerritoryLedger.Init();
                var data = TerritoryLedger.Territories;

                // FAST PATH: if the client's persisted snapshot fingerprint matches
                // what we'd be about to ship, skip the per-file MD5 walk entirely
                // and just send the empty up-to-date ack. This is the case the user
                // hits on every normal login when the server's territories haven't
                // changed since their last session.
                if (!string.IsNullOrEmpty(clientBlobHash))
                {
                    string serverFingerprint = ComputeTerritoriesFingerprint(data);
                    if (string.Equals(clientBlobHash, serverFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"[FiresRPGmaker] Territory RPC_Request from {sender}: blob fingerprint matches ({data.Count} territories), sending up-to-date ack");
                        var ackPkg = new ZPackage();
                        ackPkg.Write(0);
                        ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcReceive, ackPkg);
                        return;
                    }
                }

                // Check if client has matching territory data by comparing config file hashes
                bool needsUpdate = false;
                string baseDir = System.IO.Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Territories");
                if (System.IO.Directory.Exists(baseDir))
                {
                    var serverFiles = System.IO.Directory.GetFiles(baseDir, "*.cfg", System.IO.SearchOption.AllDirectories);
                    string baseFull = System.IO.Path.GetFullPath(baseDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                    // Check if any server file is missing or has different hash on client
                    foreach (var file in serverFiles)
                    {
                        string full = System.IO.Path.GetFullPath(file);
                        string name = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                            ? full.Substring(baseFull.Length + 1)
                            : System.IO.Path.GetFileName(file);
                        string serverHash = ComputeFileHash(file);

                        if (!clientHashes.TryGetValue(name, out var clientHash) || !string.Equals(serverHash, clientHash, StringComparison.Ordinal))
                        {
                            needsUpdate = true;
                            break;
                        }
                    }

                    // Check if client has any files that no longer exist on server
                    if (!needsUpdate && clientHashes.Count > 0)
                    {
                        var serverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in serverFiles)
                        {
                            string full = System.IO.Path.GetFullPath(file);
                            string name = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                                ? full.Substring(baseFull.Length + 1)
                                : System.IO.Path.GetFileName(file);
                            serverNames.Add(name);
                        }

                        foreach (var clientFile in clientHashes.Keys)
                        {
                            if (!serverNames.Contains(clientFile))
                            {
                                needsUpdate = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // No territory files on server, client needs to clear theirs
                    needsUpdate = clientHashes.Count > 0;
                }

                // If client is up-to-date, send empty response to acknowledge
                if (!needsUpdate && clientHashes.Count > 0)
                {
                    Debug.Log($"[FiresRPGmaker] Territory RPC_Request from {sender}: client is up-to-date ({clientHashes.Count} files match), skipping send");

                    // Send empty acknowledgement so client knows request was processed
                    // This resets the client's retry counter
                    var ackPkg = new ZPackage();
                    ackPkg.Write(0); // 0 territories = client is up-to-date
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcReceive, ackPkg);
                    return;
                }

                Debug.Log($"[FiresRPGmaker] Territory RPC_Request from {sender}: sending {data.Count} territories (client needs update)");
                SendTerritoryChunks(sender, data);

                // Admin-only cfg sync
                if (AdminSyncing.IsAdmin(sender))
                {
                    try
                    {
                        var serverFiles = System.IO.Directory.Exists(baseDir)
                            ? System.IO.Directory.GetFiles(baseDir, "*.cfg", System.IO.SearchOption.AllDirectories)
                            : Array.Empty<string>();

                        string baseFull = System.IO.Path.GetFullPath(baseDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                        var serverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in serverFiles)
                        {
                            string full = System.IO.Path.GetFullPath(file);
                            string name = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                                ? full.Substring(baseFull.Length + 1)
                                : System.IO.Path.GetFileName(file);
                            serverNames.Add(name);
                            string serverHash = ComputeFileHash(file);
                            if (!clientHashes.TryGetValue(name, out var clientHash) || !string.Equals(serverHash, clientHash, StringComparison.Ordinal))
                            {
                                var pkgFile = new ZPackage();
                                pkgFile.Write("Territories");
                                pkgFile.Write(name);
                                pkgFile.Write(System.IO.File.ReadAllText(file));
                                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcFileUpdate, pkgFile);
                            }
                        }

                        // Send deletions for client files that no longer exist on server
                        foreach (var kv in clientHashes)
                        {
                            if (!serverNames.Contains(kv.Key))
                            {
                                var pkgFile = new ZPackage();
                                pkgFile.Write("Territories");
                                pkgFile.Write(kv.Key);
                                pkgFile.Write(string.Empty);
                                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcFileUpdate, pkgFile);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory RPC_Request failed: {ex.Message}");
            }
        }

        private static void RPC_Receive(long sender, ZPackage pkg)
        {
            try
            {
                HandleReceive(sender, pkg);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory RPC_Receive failed: {ex.Message}");
            }
        }

        private static void RPC_ReceiveCompressed(long sender, ZPackage pkg)
        {
            try
            {
                var compressed = pkg.ReadByteArray();
                var decompressed = DecompressBytes(compressed);
                if (decompressed == null) return;
                HandleReceive(sender, new ZPackage(decompressed));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory RPC_ReceiveCompressed failed: {ex.Message}");
            }
        }

        private static void HandleReceive(long sender, ZPackage pkg)
        {
            var sw = Stopwatch.StartNew();
            int count = pkg.ReadInt();
            if (count == -1)
            {
                int totalCount = pkg.ReadInt();
                int chunkIndex = pkg.ReadInt();
                int chunkTotal = pkg.ReadInt();
                int chunkCount = pkg.ReadInt();

                if (!_pendingChunks.TryGetValue(sender, out var agg))
                {
                    agg = new List<Territory>(totalCount);
                    _pendingChunks[sender] = agg;
                }

                for (int i = 0; i < chunkCount; i++) agg.Add(Territory.Deserialize(pkg));

                bool isFinal = chunkIndex >= chunkTotal - 1 || agg.Count >= totalCount;
                if (isFinal)
                {
                    ClientTerritories = agg;
                    _pendingChunks.Remove(sender);
                    ResetRetryState();
                    RebuildCombinedClient();

                    // Persist a fingerprint of what we just received so future manifest
                    // queries let the server skip re-sending this exact snapshot.
                    try
                    {
                        ManifestDiffSync.RememberBlobHash(
                            TerritoriesManifestNamespace,
                            ComputeTerritoriesFingerprint(ClientTerritories));
                    }
                    catch { }

                    // Persist the full snapshot to disk so next login starts with
                    // ClientTerritories already populated and the server fast-paths
                    // via the matching blob fingerprint above.
                    SaveClientCacheToDisk();

                    sw.Stop();
                    Debug.Log($"[FiresRPGmaker] Received {ClientTerritories.Count} territories from server (chunked) in {sw.ElapsedMilliseconds}ms. First: {(ClientTerritories.Count > 0 ? ClientTerritories[0].Name : "none")}");
                    OnTerritoriesReceived();
                }
                return;
            }

            // Handle 0-territory response (client is up-to-date)
            if (count == 0)
            {
                ResetRetryState();
                sw.Stop();
                Debug.Log($"[FiresRPGmaker] Territory sync: client is up-to-date ({ClientTerritories.Count} territories cached)");

                // Still trigger map overlay update in case it wasn't initialized yet
                try
                {
                    TerritoryMapOverlay.OnTerritoriesChanged();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] TerritoryMapOverlay.OnTerritoriesChanged failed: {ex}");
                }
                return;
            }

            var list = new List<Territory>(count);
            for (int i = 0; i < count; i++) list.Add(Territory.Deserialize(pkg));
            ClientTerritories = list;
            ResetRetryState();
            RebuildCombinedClient();

            try
            {
                ManifestDiffSync.RememberBlobHash(
                    TerritoriesManifestNamespace,
                    ComputeTerritoriesFingerprint(ClientTerritories));
            }
            catch { }

            // Persist the full snapshot to disk so next login starts with
            // ClientTerritories already populated and the server fast-paths
            // via the matching blob fingerprint above.
            SaveClientCacheToDisk();

            sw.Stop();
            Debug.Log($"[FiresRPGmaker] Received {ClientTerritories.Count} territories from server in {sw.ElapsedMilliseconds}ms. First: {(ClientTerritories.Count > 0 ? ClientTerritories[0].Name : "none")}");
            OnTerritoriesReceived();
        }

        /// <summary>
        /// Called after territories are successfully received from server.
        /// Triggers map overlay update and terrain paint application.
        /// </summary>
        private static void OnTerritoriesReceived()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                TerritoryMapOverlay.OnTerritoriesChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] TerritoryMapOverlay.OnTerritoriesChanged failed: {ex}");
            }

            // Invalidate snow territory cache so it rebuilds with new territories
            try
            {
                TerritoryRuntime.InvalidateSnowTerritoryCache();
            }
            catch { }

            // Apply terrain paint now that territories are loaded
            try
            {
                TerritoryRuntime.ApplyAllCustomPaints(clearCache: true);

                // Force refresh any already-loaded heightmaps
                RefreshLoadedHeightmaps();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory paint application after sync failed: {ex}");
            }
            sw.Stop();
            Debug.Log($"[FiresRPGmaker] OnTerritoriesReceived post-processing took {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Forces a refresh on all loaded heightmaps to trigger paint application.
        /// Called after territories are synced to apply paint to already-loaded terrain.
        /// NOTE: We do NOT call Poke() because it internally calls GetAndCreateTerrainCompiler()
        /// which causes "Found another terrain compiler" spam. Instead, we directly call
        /// RebuildRenderMesh via reflection if available.
        /// </summary>
        private static void RefreshLoadedHeightmaps()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var heightmaps = Heightmap.GetAllHeightmaps();
                if (heightmaps == null || heightmaps.Count == 0) return;

                int refreshed = 0;
                foreach (var hm in heightmaps)
                {
                    if (hm == null) continue;
                    try
                    {
                        // CRITICAL: Do NOT call Poke() - it creates TerrainComps which causes spam
                        // Instead, directly regenerate the render mesh which is what we need for paint
                        // to show up. The terrain data doesn't need to change, just the visual rendering.
                        var renderer = hm.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                            // Force material update by toggling enabled
                            renderer.enabled = false;
                            renderer.enabled = true;
                        }
                        refreshed++;
                    }
                    catch { }
                }
                sw.Stop();
                if (refreshed > 0)
                {
                    Debug.Log($"[FiresRPGmaker] RefreshLoadedHeightmaps refreshed {refreshed} heightmaps in {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] RefreshLoadedHeightmaps failed: {ex.Message}");
            }
        }

        private static void RebuildCombinedClient()
        {
            _combinedClient.Clear();
            if (ClientTerritories.Count == 0) return;

            foreach (var terr in ClientTerritories)
            {
                var bounds = GetBounds(terr);
                CombinedTerritory target = null;
                for (int i = 0; i < _combinedClient.Count; i++)
                {
                    var c = _combinedClient[i];
                    if (!string.Equals(c.Name, terr.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (BoundsOverlap(c.Min, c.Max, bounds.min, bounds.max)) { target = c; break; }
                }
                if (target == null)
                {
                    target = new CombinedTerritory { Name = terr.Name, Min = bounds.min, Max = bounds.max };
                    _combinedClient.Add(target);
                }
                else
                {
                    target.Min = new Vector2(Mathf.Min(target.Min.x, bounds.min.x), Mathf.Min(target.Min.y, bounds.min.y));
                    target.Max = new Vector2(Mathf.Max(target.Max.x, bounds.max.x), Mathf.Max(target.Max.y, bounds.max.y));
                }
                target.Members.Add(terr);
            }
        }

        private static (Vector2 min, Vector2 max) GetBounds(Territory terr)
        {
            float minX, maxX, minY, maxY;
            switch (terr.Shape)
            {
                case TerritoryShape.Rectangle:
                    minX = terr.X[0];
                    maxX = terr.X[0] + (terr.X.Length > 1 ? terr.X[1] : 0);
                    minY = terr.Y[0];
                    maxY = terr.Y[0] + (terr.Y.Length > 1 ? terr.Y[1] : 0);
                    break;
                case TerritoryShape.Square:
                case TerritoryShape.Circle:
                default:
                    minX = terr.X[0] - terr.Radius;
                    maxX = terr.X[0] + terr.Radius;
                    minY = terr.Y[0] - terr.Radius;
                    maxY = terr.Y[0] + terr.Radius;
                    break;
            }
            return (new Vector2(minX, minY), new Vector2(maxX, maxY));
        }

        private static bool BoundsOverlap(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.x <= bMax.x && aMax.x >= bMin.x && aMin.y <= bMax.y && aMax.y >= bMin.y;
        }

        private static void RPC_FileUpdate(long sender, ZPackage pkg)
        {
            try
            {
                string folder = pkg.ReadString();
                string name = pkg.ReadString();
                string content = pkg.ReadString();
                if (!string.Equals(folder, "Territories", StringComparison.OrdinalIgnoreCase)) return;

                string baseDir = System.IO.Path.Combine(Paths.ConfigPath, "FiresRPGmaker", folder);
                System.IO.Directory.CreateDirectory(baseDir);
                var fullPath = System.IO.Path.Combine(baseDir, name);
                var parentDir = System.IO.Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parentDir)) System.IO.Directory.CreateDirectory(parentDir);
                if (string.IsNullOrEmpty(content))
                {
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
                else
                {
                    System.IO.File.WriteAllText(fullPath, content);
                }

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    // Save the file immediately but debounce the reload/broadcast.
                    // No need to BroadcastFile � the debounced BroadcastTerritories sends
                    // the full compiled territory data to all clients once the batch is complete.
                    ScheduleDebouncedReload();
                }
                else
                {
                    // client: save the file locally, the server will also send the full
                    // territory list via BroadcastTerritories after its debounced reload
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory FileUpdate failed: {ex.Message}");
            }
        }

        private static void BroadcastFile(string folder, string name, string content, long excludeSender)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var pkg = new ZPackage();
            pkg.Write(folder);
            pkg.Write(name);
            pkg.Write(content ?? string.Empty);
            var payload = TryGetPackageBytes(pkg);
             foreach (var peer in ZNet.instance.GetPeers())
             {
                 if (peer == null || peer.m_uid == excludeSender) continue;
                try
                {
                    var sendPkg = payload != null ? new ZPackage(payload) : pkg;
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcFileUpdate, sendPkg);
                }
                catch { }
             }
         }

        /// <summary>
        /// Schedule a debounced territory reload + broadcast.
        /// Multiple rapid calls (e.g. pushcfgs sending 9 territory files) collapse into
        /// a single reload after a short delay, avoiding redundant work.
        /// </summary>
        public static void ScheduleDebouncedReload()
        {
            _reloadPending = true;

            if (FiresRPGmaker.Instance != null)
            {
                if (_reloadCoroutine != null)
                    FiresRPGmaker.Instance.StopCoroutine(_reloadCoroutine);
                _reloadCoroutine = FiresRPGmaker.Instance.StartCoroutine(DebouncedReloadCoroutine());
            }
            else
            {
                // Fallback: no MonoBehaviour available (dedicated server edge case) � execute immediately
                ExecuteTerritoryReload();
            }
        }

        private static System.Collections.IEnumerator DebouncedReloadCoroutine()
        {
            yield return new WaitForSeconds(ReloadDebounceSeconds);
            if (_reloadPending)
            {
                ExecuteTerritoryReload();
            }
            _reloadCoroutine = null;
        }

        private static void ExecuteTerritoryReload()
        {
            _reloadPending = false;
            try
            {
                TerritoryLedger.Reload();
                TerritoryRuntime.ApplyAllCustomPaints(clearCache: true);
                BroadcastTerritories();
                try { if (ZNet.instance != null && !ZNet.instance.IsDedicated()) TerritoryMapOverlay.ForceRedraw(); } catch { }
                Debug.Log("[FiresRPGmaker] Territory debounced reload+broadcast complete");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory debounced reload failed: {ex.Message}");
            }
        }

        public static void BroadcastTerritories(long excludeSender = -1)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            TerritoryLedger.Init();
            var data = TerritoryLedger.Territories;

            // Pre-compute the fingerprint of what we would send so we can ask every peer
            // "do you already have this?" and skip the 100s of chunked RPCs when they do.
            string localHash = ComputeTerritoriesFingerprint(data);

            // Diff-sync must yield (waiting on manifest replies) so drive per-peer sends
            // from a coroutine. Fall back to the legacy blind broadcast if we somehow
            // can't reach a MonoBehaviour host ? correctness over bandwidth.
            var host = FiresRPGmaker.Instance;
            if (host != null)
            {
                host.StartCoroutine(BroadcastTerritoriesCoroutine(excludeSender, data, localHash));
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_uid == excludeSender) continue;
                try { SendTerritoryChunks(peer.m_uid, data); }
                catch { }
            }
        }

        private static IEnumerator BroadcastTerritoriesCoroutine(long excludeSender,
            IReadOnlyList<Territory> data, string localHash)
        {
            if (ZNet.instance == null) yield break;

            // Kick off one manifest request per target peer up front so the replies
            // fan in while we work through the list.
            var targets = new List<long>();
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_uid == excludeSender) continue;
                targets.Add(peer.m_uid);
                ManifestDiffSync.RequestFromPeer(peer.m_uid, TerritoriesManifestNamespace);
            }

            int sent = 0, skipped = 0;
            foreach (var peerUid in targets)
            {
                yield return ManifestDiffSync.WaitForManifest(peerUid, TerritoriesManifestNamespace, 1.5f);

                if (!string.IsNullOrEmpty(localHash) &&
                    ManifestDiffSync.PeerHasBlob(peerUid, TerritoriesManifestNamespace, localHash))
                {
                    skipped++;
                    continue;
                }

                try { SendTerritoryChunks(peerUid, data); sent++; }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Territory send to {peerUid} failed: {ex.Message}");
                }

                // Small yield so RPC enqueues don't pile into a single Steam frame.
                yield return null;
            }

            if (sent > 0 || skipped > 0)
                Debug.Log($"[FiresRPGmaker] Territory diff-sync: sent to {sent} peer(s), skipped {skipped} unchanged.");
        }

        private static void SendTerritoryChunks(long target, IReadOnlyList<Territory> data)
        {
            if (ZRoutedRpc.instance == null) return;
            int total = data.Count;
            int chunkTotal = Math.Max(1, (int)Math.Ceiling(total / (double)MaxTerritoriesPerChunk));

            for (int chunkIndex = 0; chunkIndex < chunkTotal; chunkIndex++)
            {
                int start = chunkIndex * MaxTerritoriesPerChunk;
                int count = Math.Min(MaxTerritoriesPerChunk, total - start);
                var pkg = new ZPackage();
                pkg.Write(-1); // chunk marker
                pkg.Write(total);
                pkg.Write(chunkIndex);
                pkg.Write(chunkTotal);
                pkg.Write(count);
                for (int i = 0; i < count; i++)
                {
                    data[start + i].Serialize(pkg);
                }

                var payload = TryGetPackageBytes(pkg);
                var compressed = CompressBytes(payload);

                // Capture locals for the closure ? do NOT reference chunkIndex inside it
                // because the loop variable will have advanced by the time the action runs.
                long capturedTarget      = target;
                byte[] capturedCompressed = compressed;
                byte[] capturedPayload    = payload;
                ZPackage capturedRaw      = pkg;

                _sendQueue.Enqueue(() =>
                {
                    try
                    {
                        if (capturedCompressed != null && capturedPayload != null &&
                            capturedCompressed.Length < capturedPayload.Length)
                        {
                            var wrap = new ZPackage();
                            wrap.Write(capturedCompressed);
                            ZRoutedRpc.instance.InvokeRoutedRPC(capturedTarget, RpcReceiveCompressed, wrap);
                        }
                        else
                        {
                            var sendPkg = capturedPayload != null ? new ZPackage(capturedPayload) : capturedRaw;
                            ZRoutedRpc.instance.InvokeRoutedRPC(capturedTarget, RpcReceive, sendPkg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Territory chunk send failed: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Must be pumped once per frame (from <c>Ascend.Update</c>). Pops at most
        /// <see cref="MaxChunksPerFrame"/> queued chunks per call so the outbound rate
        /// stays under Steam's per-frame RPC limit.
        /// </summary>
        public static void DrainSendQueue()
        {
            int sent = 0;
            while (_sendQueue.Count > 0 && sent < MaxChunksPerFrame)
            {
                var action = _sendQueue.Dequeue();
                try { action(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Territory DrainSendQueue action threw: {ex.Message}");
                }
                sent++;
            }
        }

        private static byte[] TryGetPackageBytes(ZPackage pkg)
        {
            try
            {
                var type = pkg.GetType();
                var getArray = type.GetMethod("GetArray", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                               ?? type.GetMethod("GetBuffer", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (getArray != null)
                {
                    return getArray.Invoke(pkg, null) as byte[];
                }
            }
            catch { }
            return null;
        }

        // ===================================================================
        // Client-side persistent territory cache
        //
        // Sits next to the manifest hash file so the two travel together:
        //   BepInEx/config/FiresRPGmaker/cache/territories.bin   <- snapshot
        //   BepInEx/config/FiresRPGmaker/cache/manifests/Territories.hash
        //
        // Wire format = the same Territory.Serialize stream we already use for
        // the RPC payload, prefixed with a single int count. We deliberately do
        // NOT version this file; if the schema ever changes Deserialize will
        // throw, the catch will scrap the cache, and the next request will
        // re-pull from the server (the "old wire format" backstop is the same
        // path that already protects the in-memory RPC handler).
        // ===================================================================

        private static string ClientCachePath =>
            System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker", "cache", "territories.bin");

        private static void SaveClientCacheToDisk()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(ClientCachePath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

                var pkg = new ZPackage();
                int n = ClientTerritories?.Count ?? 0;
                pkg.Write(n);
                if (n > 0)
                {
                    for (int i = 0; i < n; i++) ClientTerritories[i].Serialize(pkg);
                }
                byte[] bytes = TryGetPackageBytes(pkg);
                if (bytes == null) return;
                System.IO.File.WriteAllBytes(ClientCachePath, bytes);
                FiresLogger.LogVerbose($"[FiresRPGmaker] Saved {n} territories to client cache ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SaveClientCacheToDisk failed: {ex.Message}");
            }
        }

        private static void LoadClientCacheFromDisk()
        {
            try
            {
                if (!System.IO.File.Exists(ClientCachePath)) return;
                byte[] bytes = System.IO.File.ReadAllBytes(ClientCachePath);
                if (bytes == null || bytes.Length == 0) return;

                var pkg = new ZPackage(bytes);
                int n = pkg.ReadInt();
                if (n < 0 || n > 100_000) return; // sanity
                var list = new List<Territory>(n);
                for (int i = 0; i < n; i++) list.Add(Territory.Deserialize(pkg));
                ClientTerritories = list;
                RebuildCombinedClient();
                Debug.Log($"[FiresRPGmaker] Loaded {ClientTerritories.Count} territories from client cache");

                // Refresh the persisted fingerprint from the loaded snapshot in
                // case the .hash file was deleted but the .bin survived. This
                // keeps the server fast-path consistent on the very first
                // request after this session starts.
                try
                {
                    ManifestDiffSync.RememberBlobHash(
                        TerritoriesManifestNamespace,
                        ComputeTerritoriesFingerprint(ClientTerritories));
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] LoadClientCacheFromDisk failed (cache will be rebuilt on next sync): {ex.Message}");
                ClientTerritories = new List<Territory>();
            }
        }

        private static byte[] CompressBytes(byte[] input)
        {
            if (input == null || input.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream();
                using (var ds = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    ds.Write(input, 0, input.Length);
                }
                return ms.ToArray();
            }
            catch { return null; }
        }

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
            catch { return null; }
        }

        public static Territory GetCurrent(Vector3 pos)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                foreach (var t in TerritoryLedger.Territories)
                {
                    if (t.IsInside(pos)) return t;
                }
                return null;
            }

            if (_combinedClient.Count > 0)
            {
                Vector2 p2 = new Vector2(pos.x, pos.z);
                foreach (var group in _combinedClient)
                {
                    if (p2.x < group.Min.x || p2.x > group.Max.x || p2.y < group.Min.y || p2.y > group.Max.y) continue;
                    foreach (var t in group.Members)
                    {
                        if (t.IsInside(pos)) return t;
                    }
                }
                return null;
            }

            foreach (var t in ClientTerritories)
            {
                if (t.IsInside(pos)) return t;
            }
            return null;
        }
    }
}
