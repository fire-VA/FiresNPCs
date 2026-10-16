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

        /// <summary>
        /// The world's view of a key. With private keys on, a boss key never reaches the global list, so a boss key
        /// counts as "global" when a player near the local player holds it (Fire 10-13: the highest progression of
        /// the players in range). Other keys read the global list.
        /// </summary>
        public static bool HasGlobalKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            key = key.ToLower();
            var cfg = ProgressionConfig.Instance;
            if (cfg != null && cfg.UsePrivateKeys && BossKeyOrder.TryGetValue(key, out int order) && Player.m_localPlayer != null)
                return GetHighestBossTierNear(Player.m_localPlayer.transform.position, DefaultProgressionRadius) >= order;
            return ZoneSystem.instance != null && ZoneSystem.instance.m_globalKeys.Contains(key);
        }

        /// <summary>Radius for "players in range" reads and for boss-kill credit, in metres.</summary>
        public const float DefaultProgressionRadius = 100f;

        /// <summary>
        /// CONTRACT (FiresMonsterOverhaul reads it by reflection; keep the name, parameters and return type):
        /// the highest boss tier (1 = Eikthyr .. 7 = Fader, 0 = none) held by any player within <paramref name="radius"/>
        /// of <paramref name="position"/>; -1 when private keys are off or no player is in range, so the caller falls back.
        /// Works on the server (its authoritative store) and on clients (the counts the server pushes).
        /// </summary>
        public static int GetHighestBossTierNear(Vector3 position, float radius)
        {
            var cfg = ProgressionConfig.Instance;
            if (cfg == null || !cfg.UsePrivateKeys) return -1;
            int best = -1;
            float r2 = radius * radius;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    var zdo = peer != null && peer.IsReady() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
                    if (zdo == null || (zdo.GetPosition() - position).sqrMagnitude > r2) continue;
                    best = Math.Max(best, TierOf(GetServerKeysForPlayer(zdo.GetLong(ZDOVars.s_playerID))));
                }
            }
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || (p.transform.position - position).sqrMagnitude > r2) continue;
                if (p == Player.m_localPlayer) best = Math.Max(best, TierOf(_privateKeys));
                else if (_bossTiers.TryGetValue(p.GetPlayerID(), out int t)) best = Math.Max(best, t);
                else best = Math.Max(best, 0);
            }
            return best;
        }

        public static int TierOf(HashSet<string> keys)
        {
            int tier = 0;
            foreach (var k in keys) if (BossKeyOrder.TryGetValue(k, out int o) && o > tier) tier = o;
            return tier;
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

        /// <summary>With private keys on: the highest boss tier near the local player (0 when nobody holds one); else the global count.</summary>
        public static int GetGlobalBossCount()
        {
            var cfg = ProgressionConfig.Instance;
            if (cfg != null && cfg.UsePrivateKeys && Player.m_localPlayer != null)
                return Math.Max(0, GetHighestBossTierNear(Player.m_localPlayer.transform.position, DefaultProgressionRadius));
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

        // The server owns every player's keys (0.0.572, Fire 10-13): it grants them, stores them per world
        // (BepInEx\config\FiresRPGmaker\PrivateKeys\<world>.txt, inside the game folder) and pushes them to the
        // owning client, which keeps a copy in m_customData for offline play. A client's own edits (va_ console
        // commands) reach the store only when the client is an admin; anything else is answered with the server's set.
        public const string RPC_ServerBossDefeated = "VA_ServerBossDefeated";
        public const string RPC_ClientSyncKeys     = "VA_SyncPrivateKeys";
        public const string RPC_ClientBossTiers    = "VA_PrivateBossTiers";

        // Client side: playerID -> highest boss tier of every online player, pushed by the server.
        private static readonly Dictionary<long, int> _bossTiers = new Dictionary<long, int>();
        private static bool _serverStoreLoaded;

        public static void RegisterRPCs()
        {
            if (_rpcsRegistered) return;
            _rpcsRegistered = true;

            try
            {
                ZRoutedRpc.instance.Register<string>(RPC_ClientSetKey, RPC_OnSetPrivateKey);
                ZRoutedRpc.instance.Register<string>(RPC_ClientRemoveKey, RPC_OnRemovePrivateKey);
                ZRoutedRpc.instance.Register(RPC_ClientResetKeys, RPC_OnResetPrivateKeys);
                ZRoutedRpc.instance.Register<string>(RPC_ClientSyncKeys, RPC_OnSyncKeys);
                ZRoutedRpc.instance.Register<string>(RPC_ClientBossTiers, RPC_OnBossTiers);

                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerSetKeys, RPC_OnServerSetKeys);
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerAddKey, RPC_OnServerAddKey);
                    ZRoutedRpc.instance.Register<string, long>(RPC_ServerRemoveKey, RPC_OnServerRemoveKey);
                    ZRoutedRpc.instance.Register<string, Vector3>(RPC_ServerBossDefeated, RPC_OnServerBossDefeated);
                    LoadServerStore();
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
        /// A boss died at <paramref name="position"/> and its key was kept off the global list. Whoever ran the death
        /// (the boss's owner: a client or the dedicated server) reports it here; the server credits every player
        /// within <see cref="DefaultProgressionRadius"/> of the BOSS.
        /// </summary>
        public static void ReportBossDefeated(string key, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(key) || ZRoutedRpc.instance == null) return;
            key = key.ToLower();
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ServerGrantNear(key, position, DefaultProgressionRadius, "boss died here"); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RPC_ServerBossDefeated, key, position);
        }

        // -- Server-side --

        private static void RPC_OnServerBossDefeated(long sender, string key, Vector3 position)
        {
            key = (key ?? "").ToLower();
            if (ShouldBlockPrivateKey(key)) { Debug.LogWarning($"[PrivateKeyManager] boss report for '{key}' from {sender} refused: not a private key"); return; }
            // The reporter owns the boss, so it is near it; a report from far away is not a kill it ran.
            var peer = ZNet.instance.GetPeer(sender);
            var zdo = peer != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (zdo != null && (zdo.GetPosition() - position).sqrMagnitude > ReportRange * ReportRange)
            {
                Debug.LogWarning($"[PrivateKeyManager] boss report for '{key}' from {sender} refused: the reporter is {Vector3.Distance(zdo.GetPosition(), position):0} m from the boss (max {ReportRange:0})");
                return;
            }
            ServerGrantNear(key, position, DefaultProgressionRadius, $"reported by {sender}");
        }

        private const float ReportRange = 200f;

        private static void ServerGrantNear(string key, Vector3 position, float radius, string why)
        {
            float r2 = radius * radius;
            var granted = new List<string>();
            foreach (var peer in ZNet.instance.GetPeers())
            {
                var zdo = peer != null && peer.IsReady() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
                if (zdo == null || (zdo.GetPosition() - position).sqrMagnitude > r2) continue;
                long playerId = zdo.GetLong(ZDOVars.s_playerID);
                if (playerId == 0L) continue;
                if (ServerKeys(playerId).Add(key)) granted.Add(peer.m_playerName);
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RPC_ClientSetKey, key);
            }
            // A listen host is not a peer of itself.
            var local = Player.m_localPlayer;
            if (local != null && (local.transform.position - position).sqrMagnitude <= r2)
            {
                if (ServerKeys(local.GetPlayerID()).Add(key)) granted.Add(local.GetPlayerName());
                AddLocal(key);
            }
            SaveServerStore();
            BroadcastBossTiers();
            Debug.Log($"[PrivateKeyManager] '{key}' at ({position.x:0},{position.z:0}) {why}: granted to {granted.Count} player(s) within {radius:0} m: {string.Join(", ", granted)}");
        }

        private static void RPC_OnServerSetKeys(long sender, string keys, long playerId)
        {
            if (!SenderOwns(sender, playerId)) return;
            if (_serverKeys.TryGetValue(playerId, out var stored))
            {
                // The server's record wins; the client gets it back (a client can't raise its own progression).
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RPC_ClientSyncKeys, string.Join(",", stored));
            }
            else
            {
                // First time on this world: the character's own keys are taken in once (worlds from before 0.0.572).
                _serverKeys[playerId] = ParseKeySet(keys);
                SaveServerStore();
                Debug.Log($"[PrivateKeyManager] first join of player {playerId}: took in {_serverKeys[playerId].Count} key(s) from the character");
            }
            BroadcastBossTiers();
        }

        private static void RPC_OnServerAddKey(long sender, string key, long playerId)
        {
            if (!SenderOwns(sender, playerId)) return;
            if (!AdminSyncing.IsAdmin(sender) && sender != ZRoutedRpc.instance.m_id)
            {
                Debug.LogWarning($"[PrivateKeyManager] non-admin {sender} tried to add '{key}'; sending the server's keys back");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RPC_ClientSyncKeys, string.Join(",", ServerKeys(playerId)));
                return;
            }
            if (ServerKeys(playerId).Add(key.ToLower())) { SaveServerStore(); BroadcastBossTiers(); }
        }

        private static void RPC_OnServerRemoveKey(long sender, string key, long playerId)
        {
            if (!SenderOwns(sender, playerId)) return;
            if (!AdminSyncing.IsAdmin(sender) && sender != ZRoutedRpc.instance.m_id)
            {
                Debug.LogWarning($"[PrivateKeyManager] non-admin {sender} tried to remove '{key}'; sending the server's keys back");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RPC_ClientSyncKeys, string.Join(",", ServerKeys(playerId)));
                return;
            }
            if (ServerKeys(playerId).Remove(key.ToLower())) { SaveServerStore(); BroadcastBossTiers(); }
        }

        // The playerId in a client's message must be its own character's.
        private static bool SenderOwns(long sender, long playerId)
        {
            if (playerId == 0L) return false;
            if (ZRoutedRpc.instance != null && sender == ZRoutedRpc.instance.m_id) return true;
            var peer = ZNet.instance?.GetPeer(sender);
            var zdo = peer != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (zdo == null) return true;   // character not spawned on the server yet: nothing to compare with
            long own = zdo.GetLong(ZDOVars.s_playerID);
            if (own == playerId) return true;
            Debug.LogWarning($"[PrivateKeyManager] {sender} sent keys for player {playerId} but plays {own}; refused");
            return false;
        }

        private static HashSet<string> ServerKeys(long playerId)
        {
            if (!_serverKeys.TryGetValue(playerId, out var set))
                _serverKeys[playerId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return set;
        }

        private static void BroadcastBossTiers()
        {
            if (ZRoutedRpc.instance == null) return;
            var parts = new List<string>();
            foreach (var kv in _serverKeys) parts.Add(kv.Key + ":" + TierOf(kv.Value));
            string payload = string.Join(";", parts);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RPC_ClientBossTiers, payload);
        }

        private static string StorePath()
        {
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "world";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker", "PrivateKeys", world + ".txt");
        }

        private static void LoadServerStore()
        {
            if (_serverStoreLoaded) return;
            _serverStoreLoaded = true;
            string path = StorePath();
            try
            {
                if (!System.IO.File.Exists(path)) { Debug.Log($"[PrivateKeyManager] no private key store yet ({path})"); return; }
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || !long.TryParse(line.Substring(0, eq), out long id)) continue;
                    _serverKeys[id] = ParseKeySet(line.Substring(eq + 1));
                }
                Debug.Log($"[PrivateKeyManager] loaded private keys of {_serverKeys.Count} player(s) from {path}");
            }
            catch (Exception ex) { Debug.LogWarning($"[PrivateKeyManager] could not read {path}: {ex.Message}"); }
        }

        private static void SaveServerStore()
        {
            string path = StorePath();
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var lines = new List<string>();
                foreach (var kv in _serverKeys) lines.Add(kv.Key + "=" + string.Join(",", kv.Value));
                System.IO.File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception ex) { Debug.LogWarning($"[PrivateKeyManager] could not write {path}: {ex.Message}"); }
        }

        // -- Client-side (keys from the server are applied locally and NOT sent back) --

        private static void AddLocal(string key)
        {
            key = key.ToLower();
            if (!_privateKeys.Add(key)) return;
            Debug.Log($"[PrivateKeyManager] Received private key: {key}");
            MirrorKeyToEwp(key);
        }

        private static void RPC_OnSetPrivateKey(long sender, string key) => AddLocal(key);

        private static void RPC_OnRemovePrivateKey(long sender, string key)
        {
            if (_privateKeys.Remove((key ?? "").ToLower())) Debug.Log($"[PrivateKeyManager] Server removed private key: {key}");
        }

        private static void RPC_OnResetPrivateKeys(long sender)
        {
            _privateKeys.Clear();
            Debug.Log("[PrivateKeyManager] Server reset all private keys");
        }

        private static void RPC_OnSyncKeys(long sender, string csv)
        {
            _privateKeys.Clear();
            foreach (var k in ParseKeySet(csv)) if (!ShouldBlockPrivateKey(k)) _privateKeys.Add(k);
            var cfg = ProgressionConfig.Instance;
            if (cfg != null) foreach (var ek in cfg.EnforcedPrivateKeys) _privateKeys.Add(ek);
            if (Player.m_localPlayer != null) SaveToPlayer(Player.m_localPlayer);
            Debug.Log($"[PrivateKeyManager] keys from the server: {_privateKeys.Count} ({string.Join(",", _privateKeys)})");
        }

        private static void RPC_OnBossTiers(long sender, string payload)
        {
            _bossTiers.Clear();
            foreach (var part in (payload ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = part.IndexOf(':');
                if (c > 0 && long.TryParse(part.Substring(0, c), out long id) && int.TryParse(part.Substring(c + 1), out int t))
                    _bossTiers[id] = t;
            }
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

        public static void Reset()
        {
            _privateKeys.Clear();
            _serverKeys.Clear();
            _bossTiers.Clear();
            _serverStoreLoaded = false;
            _rpcsRegistered = false;
            _initialized = false;
        }
    }
}
