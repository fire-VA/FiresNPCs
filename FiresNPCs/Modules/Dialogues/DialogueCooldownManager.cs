using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using FiresCore.Sync;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Server-authoritative store of per-dialogue-option cooldown expiries.
    /// Two maps keyed by option key (and player id for per-player scope), each value
    /// is the UtcNow.Ticks at which the cooldown ends. Persisted per-server to
    /// FiresRPGmaker/&lt;server&gt;/dialogue_cooldowns.json via an atomic tmp+replace write.
    ///
    /// All methods are IsServer-gated by callers (the RPC handlers and the reset command).
    /// </summary>
    public static class DialogueCooldownManager
    {
        [Serializable]
        private class Store
        {
            // key = optionKey -> expiry UtcNow.Ticks
            public Dictionary<string, long> Global { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            // key = "{platformId}|{optionKey}" -> expiry UtcNow.Ticks
            public Dictionary<string, long> Player { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        private static Store _store;
        private static readonly object _lock = new object();

        private static string BaseDir => FiresServerScope.ScopedDir(Path.Combine(Paths.ConfigPath, "FiresRPGmaker"));
        private static string FilePath => Path.Combine(BaseDir, "dialogue_cooldowns.json");

        private static string PlayerKey(string platformId, string optionKey) => (platformId ?? string.Empty) + "|" + optionKey;

        private static void EnsureLoaded()
        {
            if (_store != null) return;
            lock (_lock)
            {
                if (_store != null) return;
                try
                {
                    string path = FilePath;
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        _store = JsonConvert.DeserializeObject<Store>(json) ?? new Store();
                    }
                    else
                    {
                        _store = new Store();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to load dialogue cooldowns: {ex.Message}");
                    _store = new Store();
                }
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                string json = JsonConvert.SerializeObject(_store, Formatting.Indented);
                string path = FilePath;
                string tmpFile = path + ".tmp";
                int attempts = 0;
                const int maxAttempts = 3;
                while (true)
                {
                    try
                    {
                        File.WriteAllText(tmpFile, json);
                        if (File.Exists(path))
                            File.Replace(tmpFile, path, null);
                        else
                            File.Move(tmpFile, path);
                        break;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        if (attempts >= maxAttempts)
                        {
                            Debug.LogError($"[FiresRPGmaker] Failed to save dialogue cooldowns after {attempts} attempts: {ex.Message}");
                            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
                            break;
                        }
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to save dialogue cooldowns: {ex.Message}");
            }
        }

        /// <summary>Seconds remaining before the option is available again; 0 when available now.</summary>
        public static int RemainingSeconds(CooldownScope scope, string optionKey, string platformId)
        {
            if (scope == CooldownScope.None || string.IsNullOrEmpty(optionKey)) return 0;
            EnsureLoaded();

            lock (_lock)
            {
                var map = scope == CooldownScope.Global ? _store.Global : _store.Player;
                string key = scope == CooldownScope.Global ? optionKey : PlayerKey(platformId, optionKey);
                if (!map.TryGetValue(key, out long expiryTicks)) return 0;

                long remainingTicks = expiryTicks - DateTime.UtcNow.Ticks;
                if (remainingTicks <= 0)
                {
                    map.Remove(key);
                    return 0;
                }
                return (int)Math.Ceiling(remainingTicks / (double)TimeSpan.TicksPerSecond);
            }
        }

        /// <summary>Stamps the option as just-used, starting a fresh cooldown of <paramref name="seconds"/>.</summary>
        public static void Stamp(CooldownScope scope, string optionKey, string platformId, int seconds)
        {
            if (scope == CooldownScope.None || string.IsNullOrEmpty(optionKey) || seconds <= 0) return;
            EnsureLoaded();

            lock (_lock)
            {
                long expiry = DateTime.UtcNow.Ticks + seconds * TimeSpan.TicksPerSecond;
                if (scope == CooldownScope.Global)
                    _store.Global[optionKey] = expiry;
                else
                    _store.Player[PlayerKey(platformId, optionKey)] = expiry;
            }
            Save();
        }

        /// <summary>Clears every recorded cooldown across all players and global options.</summary>
        public static void ClearAll()
        {
            EnsureLoaded();
            lock (_lock)
            {
                _store.Global.Clear();
                _store.Player.Clear();
            }
            Save();
        }

        /// <summary>
        /// Clears cooldowns for a single option key: the global entry and every
        /// per-player entry that targets that option (any player).
        /// </summary>
        public static void ClearKey(string optionKey)
        {
            if (string.IsNullOrEmpty(optionKey)) return;
            EnsureLoaded();

            lock (_lock)
            {
                _store.Global.Remove(optionKey);
                string suffix = "|" + optionKey;
                var toRemove = new List<string>();
                foreach (var k in _store.Player.Keys)
                {
                    if (k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(k);
                }
                foreach (var k in toRemove)
                    _store.Player.Remove(k);
            }
            Save();
        }
    }
}
