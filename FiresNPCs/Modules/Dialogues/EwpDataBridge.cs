using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Reads and writes custom player keys to the Expand World Pro ewp_data.yaml file.
    /// 
    /// ENTRY FORMAT:
    ///   "{platformID}/{keyName}"          - presence-only key (no value)
    ///   "{platformID}/{keyName}: {value}" - key with integer value
    /// 
    /// Both forms are valid. The platformID prefix makes each entry per-player.
    /// Presence-only keys are treated as value = 1 when read.
    /// 
    /// ARCHITECTURE:
    ///   - The ewp_data.yaml file lives on the SERVER at BepInEx/config/expand_world/
    ///   - If we ARE the server (IsServer), we write directly.
    ///   - If we are a client, we send an RPC to the server to perform the write.
    ///   - The server resolves the caller's platform ID from peer.m_socket.GetHostName().
    ///   - The client can read its own platform ID from the synced player list.
    /// 
    /// See: Debug/DebugRefs/VALHEIM_NETWORKING_IDENTITY_REFERENCE.md
    /// </summary>
    public static class EwpDataBridge
    {
        private static string _ewpDir;
        private static string _ewpFilePath;
        private static bool _rpcsRegistered;

        // RPC names
        public const string RPC_ServerAddKey = "VA_EwpAddKey";
        public const string RPC_ServerRemoveKey = "VA_EwpRemoveKey";
        public const string RPC_ServerWriteKey = "VA_EwpWriteKey";
        public const string RPC_ServerClearMyKeys = "VA_EwpClearMyKeys";
        public const string RPC_ServerClearAllKeys = "VA_EwpClearAllKeys";

        private static void EnsurePaths()
        {
            if (_ewpDir != null) return;
            _ewpDir = Path.Combine(BepInEx.Paths.ConfigPath, "expand_world");
            _ewpFilePath = Path.Combine(_ewpDir, "ewp_data.yaml");
        }

        // ==============================================================
        // RPC Registration
        // ==============================================================

        /// <summary>
        /// Register server-side RPCs for EWP file operations.
        /// Call once after ZRoutedRpc is available (e.g. alongside PrivateKeyManager.RegisterRPCs).
        /// </summary>
        public static void RegisterRPCs()
        {
            if (_rpcsRegistered) return;
            _rpcsRegistered = true;

            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    ZRoutedRpc.instance.Register<string>(RPC_ServerAddKey, RPC_OnServerAddKey);
                    ZRoutedRpc.instance.Register<string>(RPC_ServerRemoveKey, RPC_OnServerRemoveKey);
                    ZRoutedRpc.instance.Register<string, int>(RPC_ServerWriteKey, RPC_OnServerWriteKey);
                    ZRoutedRpc.instance.Register(RPC_ServerClearMyKeys, new Action<long>(RPC_OnServerClearMyKeys));
                    ZRoutedRpc.instance.Register(RPC_ServerClearAllKeys, new Action<long>(RPC_OnServerClearAllKeys));
                    Debug.Log("[EwpDataBridge] Server RPCs registered");
                }
            }
            catch
            {
                Debug.Log("[EwpDataBridge] RPCs already registered, skipping");
            }
        }

        // -- Server-side RPC handlers --

        private static void RPC_OnServerAddKey(long sender, string keyName)
        {
            string platformId = ResolvePlatformId(sender);
            if (string.IsNullOrEmpty(platformId)) return;
            Debug.Log($"[EwpDataBridge] RPC AddKey from {platformId}: {keyName}");
            FileAddKey(platformId, keyName);
        }

        private static void RPC_OnServerRemoveKey(long sender, string keyName)
        {
            string platformId = ResolvePlatformId(sender);
            if (string.IsNullOrEmpty(platformId)) return;
            Debug.Log($"[EwpDataBridge] RPC RemoveKey from {platformId}: {keyName}");
            FileRemoveKey(platformId, keyName);
        }

        private static void RPC_OnServerWriteKey(long sender, string keyName, int value)
        {
            string platformId = ResolvePlatformId(sender);
            if (string.IsNullOrEmpty(platformId)) return;
            Debug.Log($"[EwpDataBridge] RPC WriteKey from {platformId}: {keyName} = {value}");
            FileWriteKey(platformId, keyName, value);
        }

        private static void RPC_OnServerClearMyKeys(long sender)
        {
            string platformId = ResolvePlatformId(sender);
            if (string.IsNullOrEmpty(platformId)) return;
            Debug.Log($"[EwpDataBridge] RPC ClearMyKeys from {platformId}");
            FileClearPlayerKeys(platformId);
        }

        private static void RPC_OnServerClearAllKeys(long sender)
        {
            Debug.Log($"[EwpDataBridge] RPC ClearAllKeys from sender {sender}");
            FileClearAllKeys();
        }

        /// <summary>
        /// Resolves the platform ID (Steam ID) for an RPC sender on the server.
        /// Uses peer.m_socket.GetHostName() which is the canonical way.
        /// </summary>
        private static string ResolvePlatformId(long sender)
        {
            if (ZNet.instance == null) return null;

            var peer = ZNet.instance.GetPeer(sender);
            if (peer != null)
            {
                string host = peer.m_socket?.GetHostName();
                if (!string.IsNullOrEmpty(host))
                    return host;
            }

            // Sender is the server/host itself - no peer entry exists
            // On non-dedicated, the host player's ID is in the player list
            if (!ZNet.instance.IsDedicated())
            {
                try
                {
                    var players = ZNet.instance.GetPlayerList();
                    if (players.Count > 0)
                        return StripPlatformPrefix(players[0].m_userInfo.m_id.ToString());
                }
                catch { }
            }

            return null;
        }

        // ==============================================================
        // Platform ID Resolution
        // ==============================================================

        /// <summary>
        /// Strips platform prefix from a PlatformUserID string.
        /// e.g. "Steam_76561199085129375" -> "76561199085129375"
        /// EWP data file stores raw numeric IDs, not prefixed ones.
        /// </summary>
        public static string StripPlatformPrefix(string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) return platformId;
            int underscoreIdx = platformId.IndexOf('_');
            if (underscoreIdx >= 0 && underscoreIdx < platformId.Length - 1)
                return platformId.Substring(underscoreIdx + 1);
            return platformId;
        }

        /// <summary>
        /// Gets the local player's platform ID string (Steam ID) as raw numbers.
        /// 
        /// On CLIENT: reads from the server-synced player list, which contains
        ///   each player's PlatformUserID as sent by the server.
        /// On SERVER (non-dedicated host): reads from the player list.
        /// 
        /// NEVER falls back to Player.GetPlayerID() - that's a save file ID, not a platform ID.
        /// Returns raw numeric ID (e.g. "76561199085129375"), not prefixed.
        /// </summary>
        public static string GetLocalPlatformId()
        {
            try
            {
                if (ZNet.instance == null) return null;

                // Server/host: the first entry in the player list is the host player
                // (added in UpdatePlayerList from PlatformManager.DistributionPlatform.LocalUser)
                if (ZNet.instance.IsServer())
                {
                    var players = ZNet.instance.GetPlayerList();
                    if (players.Count > 0)
                    {
                        string id = StripPlatformPrefix(players[0].m_userInfo.m_id.ToString());
                        if (!string.IsNullOrEmpty(id))
                            return id;
                    }
                    return null;
                }

                // Client: find ourselves in the player list (server syncs this via RPC_PlayerList)
                var playerList = ZNet.instance.GetPlayerList();
                ZDOID localCharId = ZNet.instance.LocalPlayerCharacterID;
                if (!localCharId.IsNone())
                {
                    foreach (var info in playerList)
                    {
                        if (info.m_characterID == localCharId)
                        {
                            string id = StripPlatformPrefix(info.m_userInfo.m_id.ToString());
                            if (!string.IsNullOrEmpty(id))
                                return id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to get local platform ID: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Backwards-compatible name. Calls GetLocalPlatformId().
        /// Returns "0" if unable to resolve (should not normally happen).
        /// </summary>
        public static string GetLocalSteamId()
        {
            return GetLocalPlatformId() ?? "0";
        }

        // ==============================================================
        // Public API - these route to server via RPC or write directly
        // ==============================================================

        /// <summary>
        /// Adds a presence-only key for the local player.
        /// Routes to server via RPC if not the server.
        /// </summary>
        public static void AddKey(string steamId, string keyName)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Debug.Log($"[EwpDataBridge] AddKey (server-direct): {steamId}/{keyName}");
                FileAddKey(steamId, keyName);
            }
            else if (ZRoutedRpc.instance != null)
            {
                Debug.Log($"[EwpDataBridge] AddKey (client RPC): {keyName}");
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerAddKey, keyName);
            }
            else
            {
                Debug.LogWarning($"[EwpDataBridge] AddKey FAILED - no ZRoutedRpc: {keyName}");
            }
        }

        /// <summary>
        /// Writes a key with an integer value for the local player.
        /// Routes to server via RPC if not the server.
        /// </summary>
        public static void WriteKey(string steamId, string keyName, int value)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Debug.Log($"[EwpDataBridge] WriteKey (server-direct): {steamId}/{keyName} = {value}");
                FileWriteKey(steamId, keyName, value);
            }
            else if (ZRoutedRpc.instance != null)
            {
                Debug.Log($"[EwpDataBridge] WriteKey (client RPC): {keyName} = {value}");
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerWriteKey, keyName, value);
            }
            else
            {
                Debug.LogWarning($"[EwpDataBridge] WriteKey FAILED - no ZRoutedRpc: {keyName}");
            }
        }

        /// <summary>
        /// Removes a key for the local player.
        /// Routes to server via RPC if not the server.
        /// </summary>
        public static void RemoveKey(string steamId, string keyName)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Debug.Log($"[EwpDataBridge] RemoveKey (server-direct): {steamId}/{keyName}");
                FileRemoveKey(steamId, keyName);
            }
            else if (ZRoutedRpc.instance != null)
            {
                Debug.Log($"[EwpDataBridge] RemoveKey (client RPC): {keyName}");
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerRemoveKey, keyName);
            }
            else
            {
                Debug.LogWarning($"[EwpDataBridge] RemoveKey FAILED - no ZRoutedRpc: {keyName}");
            }
        }

        /// <summary>
        /// Clears all EWP keys for the local player.
        /// Routes to server via RPC if not the server.
        /// </summary>
        public static void ClearMyKeys(string steamId)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Debug.Log($"[EwpDataBridge] ClearMyKeys (server-direct): {steamId}");
                FileClearPlayerKeys(steamId);
            }
            else if (ZRoutedRpc.instance != null)
            {
                Debug.Log($"[EwpDataBridge] ClearMyKeys (client RPC)");
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerClearMyKeys);
            }
            else
            {
                Debug.LogWarning("[EwpDataBridge] ClearMyKeys FAILED - no ZRoutedRpc");
            }
        }

        /// <summary>
        /// Clears ALL keys from ewp_data.yaml (all players).
        /// Routes to server via RPC if not the server.
        /// </summary>
        public static void ClearAllKeys()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Debug.Log("[EwpDataBridge] ClearAllKeys (server-direct)");
                FileClearAllKeys();
            }
            else if (ZRoutedRpc.instance != null)
            {
                Debug.Log("[EwpDataBridge] ClearAllKeys (client RPC)");
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerClearAllKeys);
            }
            else
            {
                Debug.LogWarning("[EwpDataBridge] ClearAllKeys FAILED - no ZRoutedRpc");
            }
        }

        /// <summary>
        /// Lists all keys for a given platformId from ewp_data.yaml.
        /// This reads the local file - only accurate on the server.
        /// Returns key name -> value. Presence-only keys return value = 1.
        /// </summary>
        public static Dictionary<string, int> ListKeys(string steamId)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                EnsurePaths();

                if (!File.Exists(_ewpFilePath))
                    return result;

                string prefix = $"{steamId}/";
                foreach (var line in File.ReadAllLines(_ewpFilePath))
                {
                    string trimmed = line.TrimStart();
                    if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string remainder = trimmed.Substring(prefix.Length);
                    int colonIdx = remainder.IndexOf(':');

                    if (colonIdx < 0)
                    {
                        string keyName = remainder.Trim();
                        if (!string.IsNullOrEmpty(keyName))
                            result[keyName] = 1;
                    }
                    else
                    {
                        string keyName = remainder.Substring(0, colonIdx).Trim();
                        string valPart = remainder.Substring(colonIdx + 1).Trim();
                        if (!string.IsNullOrEmpty(keyName))
                        {
                            result[keyName] = int.TryParse(valPart, out int val) ? val : 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to list keys for '{steamId}': {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Reads a key's value from ewp_data.yaml.
        /// Presence-only keys return 1. Missing keys return 0.
        /// Only accurate when called on the server.
        /// </summary>
        public static int ReadKey(string steamId, string keyName)
        {
            try
            {
                EnsurePaths();

                if (!File.Exists(_ewpFilePath))
                    return 0;

                string entry = $"{steamId}/{keyName}";
                foreach (var line in File.ReadAllLines(_ewpFilePath))
                {
                    string trimmed = line.TrimStart();

                    if (trimmed.Equals(entry, StringComparison.OrdinalIgnoreCase))
                        return 1;

                    if (trimmed.StartsWith(entry + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = trimmed.Substring(entry.Length + 1).Trim();
                        return int.TryParse(valPart, out int val) ? val : 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to read key '{keyName}' for '{steamId}': {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Checks if a key exists for a given platformId (presence or valued).
        /// </summary>
        public static bool HasKey(string steamId, string keyName)
        {
            return ReadKey(steamId, keyName) > 0;
        }

        // ==============================================================
        // File I/O - server-side only, direct file operations
        // ==============================================================

        private static void FileAddKey(string platformId, string keyName)
        {
            try
            {
                EnsurePaths();
                if (!Directory.Exists(_ewpDir))
                    Directory.CreateDirectory(_ewpDir);

                string entry = $"{platformId}/{keyName}";
                var lines = new List<string>();
                if (File.Exists(_ewpFilePath))
                    lines.AddRange(File.ReadAllLines(_ewpFilePath));

                bool exists = lines.Any(l =>
                {
                    string t = l.TrimStart();
                    return t.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                           t.StartsWith(entry + ":", StringComparison.OrdinalIgnoreCase);
                });

                if (!exists)
                {
                    lines.Add(entry);
                    File.WriteAllLines(_ewpFilePath, lines.ToArray(), Encoding.UTF8);
                    Debug.Log($"[EwpDataBridge] Added key to file: {entry}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to add key '{keyName}' for '{platformId}': {ex.Message}");
            }
        }

        private static void FileWriteKey(string platformId, string keyName, int value)
        {
            try
            {
                EnsurePaths();

                if (!Directory.Exists(_ewpDir))
                    Directory.CreateDirectory(_ewpDir);

                string entry = $"{platformId}/{keyName}";
                string newLine = $"{entry}: {value}";
                var lines = new List<string>();

                if (File.Exists(_ewpFilePath))
                    lines.AddRange(File.ReadAllLines(_ewpFilePath));

                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith(entry + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = newLine;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    lines.Add(newLine);

                File.WriteAllLines(_ewpFilePath, lines.ToArray(), Encoding.UTF8);
                Debug.Log($"[EwpDataBridge] Wrote key to file: {newLine}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to write key '{keyName}' for '{platformId}': {ex.Message}");
            }
        }

        private static void FileRemoveKey(string platformId, string keyName)
        {
            try
            {
                EnsurePaths();

                if (!File.Exists(_ewpFilePath))
                    return;

                string entry = $"{platformId}/{keyName}";
                var lines = new List<string>(File.ReadAllLines(_ewpFilePath));
                int removedCount = lines.RemoveAll(l =>
                {
                    string t = l.TrimStart();
                    return t.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                           t.StartsWith(entry + ":", StringComparison.OrdinalIgnoreCase);
                });

                if (removedCount > 0)
                {
                    File.WriteAllLines(_ewpFilePath, lines.ToArray(), Encoding.UTF8);
                    Debug.Log($"[EwpDataBridge] Removed {removedCount} line(s) from file: {entry}");
                }
                else
                {
                    Debug.Log($"[EwpDataBridge] Key not found in file for removal: {entry}");
                    // Log all lines for debugging
                    foreach (var line in lines)
                        Debug.Log($"[EwpDataBridge]   line: '{line}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to remove key '{keyName}' for '{platformId}': {ex.Message}");
            }
        }

        private static void FileClearPlayerKeys(string platformId)
        {
            try
            {
                EnsurePaths();
                if (!File.Exists(_ewpFilePath)) return;

                string prefix = $"{platformId}/";
                var lines = new List<string>(File.ReadAllLines(_ewpFilePath));
                int removedCount = lines.RemoveAll(l =>
                    l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (removedCount > 0)
                {
                    File.WriteAllLines(_ewpFilePath, lines.ToArray(), Encoding.UTF8);
                    Debug.Log($"[EwpDataBridge] Cleared {removedCount} key(s) for player {platformId}");
                }
                else
                {
                    Debug.Log($"[EwpDataBridge] No keys found for player {platformId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to clear keys for '{platformId}': {ex.Message}");
            }
        }

        private static void FileClearAllKeys()
        {
            try
            {
                EnsurePaths();
                if (!File.Exists(_ewpFilePath)) return;

                File.WriteAllText(_ewpFilePath, "{}", Encoding.UTF8);
                Debug.Log("[EwpDataBridge] Cleared ALL keys from ewp_data.yaml");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EwpDataBridge] Failed to clear all keys: {ex.Message}");
            }
        }
    }
}
