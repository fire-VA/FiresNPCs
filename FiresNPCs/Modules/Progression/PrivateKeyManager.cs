using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Dialogues;
using FiresCore.Lifecycle;

namespace FiresRPGmaker.Modules.Progression
{
    /// <summary>
    /// Per-player boss key progression system.
    ///
    /// Replaces World Advancement Progression with a simpler, saner default:
    ///  - Private boss keys per player (only nearby players get credit for a kill)
    ///  - Global keys are blocked from the world-wide list (so one player's kill
    ///    doesn't unlock content for everyone)
    ///  - Raids use private keys (each player gets raids matching their progression)
    ///  - Everything else (equip, craft, build, cook, eat) is UNLOCKED by default
    ///  - Only cheatsword, cheathammer, trollstaff are blocked (configurable)
    ///
    /// Keys are persisted in Player.m_customData and mirrored to EWP data bridge.
    /// </summary>
    public static class PrivateKeyManager
    {
        // -- Player save key --
        public const string PLAYER_SAVE_KEY = "VA_PrivateKeys";

        // -- RPC names --
        public const string RPC_ServerSetKeys   = "VA_ServerSetPrivateKeys";
        public const string RPC_ServerAddKey     = "VA_ServerAddPrivateKey";
        public const string RPC_ServerRemoveKey  = "VA_ServerRemovePrivateKey";
        public const string RPC_ClientSetKey     = "VA_SetPrivateKey";
        public const string RPC_ClientRemoveKey  = "VA_RemovePrivateKey";
        public const string RPC_ClientResetKeys  = "VA_ResetPrivateKeys";

        // -- Boss key constants (vanilla Valheim) --
        public static readonly string[] BossKeysInOrder =
        {
            "defeated_eikthyr",
            "defeated_gdking",
            "defeated_bonemass",
            "defeated_dragon",
            "defeated_goblinking",
            "defeated_queen",
            "defeated_fader"
        };

        public static readonly Dictionary<string, int> BossKeyOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "defeated_eikthyr",    1 },
            { "defeated_gdking",     2 },
            { "defeated_bonemass",   3 },
            { "defeated_dragon",     4 },
            { "defeated_goblinking", 5 },
            { "defeated_queen",      6 },
            { "defeated_fader",      7 }
        };

        // -- Season keys to ignore (from Seasonality mods) --
        private static readonly HashSet<string> IgnoredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "season_winter", "season_fall", "season_summer", "season_spring"
        };

        // -- Items blocked by default (cheat items) --
        public static readonly HashSet<string> DefaultBlockedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SwordCheat", "HammerCheat", "StaffCheat"   // cheatsword, cheathammer, trollstaff
        };

        // -- Per-player private keys (local player) --
        private static HashSet<string> _privateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // -- Server-side: all players' keys (server only) --
        private static Dictionary<long, HashSet<string>> _serverKeys = new Dictionary<long, HashSet<string>>();

        // -- State --
        private static bool _rpcsRegistered;
        private static bool _initialized;

        // ==============================================================
        // Public API
        // ==============================================================

        public static HashSet<string> PrivateKeys => _privateKeys;

        public static bool HasPrivateKey(string key)
        {
            return string.IsNullOrEmpty(key) || _privateKeys.Contains(key.ToLower());
        }

        /// <summary>
        /// Returns true if the player has the key - checks private keys first,
        /// falls back to global keys if private keys are disabled.
        /// </summary>
        public static bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;

            var cfg = ProgressionConfig.Instance;
            if (cfg != null && cfg.UsePrivateKeys)
                return HasPrivateKey(key);

            return HasGlobalKey(key);
        }

        public static bool HasGlobalKey(string key)
        {
            return string.IsNullOrEmpty(key) ||
                   (ZoneSystem.instance != null && ZoneSystem.instance.m_globalKeys.Contains(key.ToLower()));
        }

        public static int GetPrivateBossCount()
        {
            int count = 0;
            foreach (var bk in BossKeysInOrder)
            {
                if (_privateKeys.Contains(bk)) count++;
            }
            return count;
        }

        public static int GetGlobalBossCount()
        {
            int count = 0;
            if (ZoneSystem.instance == null) return count;
            foreach (var bk in BossKeysInOrder)
            {
                if (ZoneSystem.instance.m_globalKeys.Contains(bk)) count++;
            }
            return count;
        }

        // ==============================================================
        // Key management
        // ==============================================================

        public static void AddPrivateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            key = key.ToLower();
            if (!_privateKeys.Add(key)) return;

            Debug.Log($"[PrivateKeyManager] Added private key: {key}");
            SendKeyToServer(key);
            MirrorKeyToEwp(key);
        }

        public static void RemovePrivateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            key = key.ToLower();
            if (!_privateKeys.Remove(key)) return;

            Debug.Log($"[PrivateKeyManager] Removed private key: {key}");
            SendRemoveKeyToServer(key);
        }

        public static void ResetPrivateKeys()
        {
            _privateKeys.Clear();
            Debug.Log("[PrivateKeyManager] Reset all private keys");
            SendAllKeysToServer();
        }

        /// <summary>
        /// Determines whether a global key should be blocked from the world-wide list.
        /// Boss keys are always blocked (redirected to private keys instead).
        /// World modifiers and season keys are never blocked.
        /// </summary>
        public static bool ShouldBlockGlobalKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            if (IgnoredKeys.Contains(key)) return false;
            if (IsWorldModifier(key)) return false;

            var cfg = ProgressionConfig.Instance;
            if (cfg == null || !cfg.UsePrivateKeys) return false;

            // Block all boss keys from global - they go to private instead
            if (BossKeyOrder.ContainsKey(key)) return true;

            // If blocking all global keys, check allow list
            if (cfg.BlockAllGlobalKeys)
                return cfg.AllowedGlobalKeys.Count == 0 || !cfg.AllowedGlobalKeys.Contains(key);

            return false;
        }

        /// <summary>
        /// Determines whether a private key should be rejected.
        /// </summary>
        public static bool ShouldBlockPrivateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            if (IgnoredKeys.Contains(key)) return true;
            if (IsWorldModifier(key)) return true;

            var cfg = ProgressionConfig.Instance;
            if (cfg == null) return true;

            if (cfg.BlockedPrivateKeys.Count > 0)
                return cfg.BlockedPrivateKeys.Contains(key);

            if (cfg.AllowedPrivateKeys.Count > 0)
                return !cfg.AllowedPrivateKeys.Contains(key);

            return false;
        }

        public static bool IsWorldModifier(string key)
        {
            ZoneSystem.GetKeyValue(key, out string _, out GlobalKeys gk);
            return gk < GlobalKeys.NonServerOption || gk == GlobalKeys.activeBosses;
        }

        // ==============================================================
        // Blocked item check (only cheat items by default)
        // ==============================================================

        /// <summary>
        /// Returns true if the item is in the blocked items list.
        /// By default only cheat items are blocked.
        /// </summary>
        public static bool IsItemBlocked(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            return DefaultBlockedItems.Contains(prefabName);
        }

        // ==============================================================
        // Persistence - load/save from Player.m_customData
        // ==============================================================

        public static void LoadFromPlayer(Player player)
        {
            _privateKeys.Clear();

            if (player.m_customData.TryGetValue(PLAYER_SAVE_KEY, out string saved))
            {
                foreach (var key in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string k = key.Trim().ToLower();
                    if (!ShouldBlockPrivateKey(k))
                        _privateKeys.Add(k);
                }
            }

            // Apply enforced keys
            var cfg = ProgressionConfig.Instance;
            if (cfg != null)
            {
                foreach (var ek in cfg.EnforcedPrivateKeys)
                    _privateKeys.Add(ek);
            }

            Debug.Log($"[PrivateKeyManager] Loaded {_privateKeys.Count} private keys for {player.GetPlayerName()}");
        }

        public static void SaveToPlayer(Player player)
        {
            if (player == null) return;
            string keysStr = string.Join(",", _privateKeys);
            // Indexer assignment handles both add-if-missing and update-if-present;
            // the explicit ContainsKey branch was redundant.
            PlayerSpawnGate.SafeSetCustomData(player, PLAYER_SAVE_KEY, keysStr);
        }

        // ==============================================================
        // EWP bridge - mirror boss keys for Expand World Pro
        // ==============================================================

        private static void MirrorKeyToEwp(string key)
        {
            try
            {
                string steamId = EwpDataBridge.GetLocalSteamId();
                EwpDataBridge.WriteKey(steamId, key, 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PrivateKeyManager] EWP mirror failed: {ex.Message}");
            }
        }

        // ==============================================================
        // RPC networking
        // ==============================================================

        public static void RegisterRPCs()
        {
            if (_rpcsRegistered) return;
            _rpcsRegistered = true;

            try
            {
                ZRoutedRpc.instance.Register<string>(RPC_ClientSetKey, RPC_OnSetPrivateKey);
                ZRoutedRpc.instance.Register<string>(RPC_ClientRemoveKey, RPC_OnRemovePrivateKey);
                ZRoutedRpc.instance.Register(RPC_ClientResetKeys, RPC_OnResetPrivateKeys);

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerSetKeys, RPC_OnServerSetKeys);
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerAddKey, RPC_OnServerAddKey);
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerRemoveKey, RPC_OnServerRemoveKey);
                }
            }
            catch
            {
                Debug.Log("[PrivateKeyManager] RPCs already registered, skipping");
            }
        }

        private static void SendKeyToServer(string key)
        {
            if (ZRoutedRpc.instance == null) return;
            long playerId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerAddKey, key, playerId);
        }

        private static void SendRemoveKeyToServer(string key)
        {
            if (ZRoutedRpc.instance == null) return;
            long playerId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerRemoveKey, key, playerId);
        }

        private static void SendAllKeysToServer()
        {
            if (ZRoutedRpc.instance == null) return;
            long playerId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            string keysStr = string.Join(",", _privateKeys);
            ZRoutedRpc.instance.InvokeRoutedRPC(RPC_ServerSetKeys, keysStr, playerId);
        }

        /// <summary>
        /// Sends a private key to a specific player by name (used when boss dies near multiple players).
        /// </summary>
        public static void SendKeyToPlayer(string playerName, string key)
        {
            long targetId = GetPlayerZDOID(playerName);
            if (targetId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(targetId, RPC_ClientSetKey, key);
        }

        // -- Server-side RPC handlers --

        private static void RPC_OnServerSetKeys(long sender, string keys, long playerId)
        {
            var set = ParseKeySet(keys);
            _serverKeys[playerId] = set;
        }

        private static void RPC_OnServerAddKey(long sender, string key, long playerId)
        {
            if (!_serverKeys.ContainsKey(playerId))
                _serverKeys[playerId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _serverKeys[playerId].Add(key.ToLower());
        }

        private static void RPC_OnServerRemoveKey(long sender, string key, long playerId)
        {
            if (_serverKeys.ContainsKey(playerId))
                _serverKeys[playerId].Remove(key.ToLower());
        }

        // -- Client-side RPC handlers --

        private static void RPC_OnSetPrivateKey(long sender, string key)
        {
            Debug.Log($"[PrivateKeyManager] Received private key from server: {key}");
            AddPrivateKey(key);
        }

        private static void RPC_OnRemovePrivateKey(long sender, string key)
        {
            RemovePrivateKey(key);
        }

        private static void RPC_OnResetPrivateKeys(long sender)
        {
            ResetPrivateKeys();
        }

        // ==============================================================
        // Server key access (for raids)
        // ==============================================================

        public static HashSet<string> GetServerKeysForPlayer(long playerId)
        {
            return _serverKeys.TryGetValue(playerId, out var keys) ? keys : new HashSet<string>();
        }

        // ==============================================================
        // Utilities
        // ==============================================================

        public static HashSet<string> ParseKeySet(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (var k in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                set.Add(k.Trim().ToLower());
            return set;
        }

        private static long GetPlayerZDOID(string playerName)
        {
            if (ZNet.instance == null) return 0L;
            string lower = playerName.Trim().ToLower();
            var playerList = ZNet.instance.GetPlayerList();
            for (int i = 0; i < playerList.Count; i++)
            {
                if (playerList[i].m_name.Trim().ToLower().Equals(lower))
                    return playerList[i].m_characterID.UserID;
            }
            return 0L;
        }

        public static void Reset()
        {
            _privateKeys.Clear();
            _serverKeys.Clear();
            _rpcsRegistered = false;
            _initialized = false;
        }
    }
}
