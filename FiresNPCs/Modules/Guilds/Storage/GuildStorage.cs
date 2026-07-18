using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds.Core;

namespace FiresRPGmaker.Modules.Guilds.Storage
{
    /// <summary>
    /// Persists guild data as a single JSON file per world under:
    ///   BepInEx/config/FiresRPGmaker/guilds/{worldName}.json
    ///
    /// Chat history persists alongside it, capped to the most recent
    /// MAX_HISTORY messages per guild:
    ///   BepInEx/config/FiresRPGmaker/guilds/{worldName}_chat.json
    ///
    /// Only the server reads/writes disk.  Clients receive the guild list
    /// as a serialized JSON blob pushed by GuildSyncManager on connect and
    /// after every server-side mutation.
    /// </summary>
    public static class GuildStorage
    {
        private static readonly string ConfigRoot =
            Path.Combine(FiresCore.Storage.FiresConfigPaths.Guilds);

        public const int MAX_CHAT_HISTORY = 500;

        // ── Path helpers ──────────────────────────────────────────────────────

        private static string GetFilePath()
        {
            var worldName = ZNet.instance?.GetWorldName() ?? "default";
            Directory.CreateDirectory(ConfigRoot);
            return Path.Combine(ConfigRoot, $"{SanitizeFileName(worldName)}.json");
        }

        private static string GetChatFilePath()
        {
            var worldName = ZNet.instance?.GetWorldName() ?? "default";
            Directory.CreateDirectory(ConfigRoot);
            return Path.Combine(ConfigRoot, $"{SanitizeFileName(worldName)}_chat.json");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ── Disk I/O ──────────────────────────────────────────────────────────

        public static List<GuildData> LoadAll()
        {
            try
            {
                var path = GetFilePath();
                if (!File.Exists(path)) return new List<GuildData>();
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<GuildData>>(json) ?? new List<GuildData>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] Load failed: {ex.Message}");
                return new List<GuildData>();
            }
        }

        public static void SaveAll(List<GuildData> guilds)
        {
            try
            {
                var path = GetFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonConvert.SerializeObject(guilds, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] Save failed: {ex.Message}");
            }
        }

        // ── Network serialization ─────────────────────────────────────────────

        public static string Serialize(List<GuildData> guilds)
        {
            try { return JsonConvert.SerializeObject(guilds); }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] Serialize failed: {ex.Message}");
                return "[]";
            }
        }

        public static List<GuildData> Deserialize(string json)
        {
            try { return JsonConvert.DeserializeObject<List<GuildData>>(json) ?? new List<GuildData>(); }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] Deserialize failed: {ex.Message}");
                return new List<GuildData>();
            }
        }

        // ── Chat history I/O ──────────────────────────────────────────────────
        //
        // Stored as a Dictionary<guildId, List<ChatMessage>> per world. Only
        // the server reads/writes; clients get the recent slice for their
        // guild via FullSync (or a chat-history-specific message).

        [Serializable]
        public class ChatMessage
        {
            public string SenderName;
            public string Text;
            public long UtcTicks;
        }

        public static Dictionary<string, List<ChatMessage>> LoadChatHistory()
        {
            try
            {
                var path = GetChatFilePath();
                if (!File.Exists(path)) return new Dictionary<string, List<ChatMessage>>();
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<string, List<ChatMessage>>>(json)
                       ?? new Dictionary<string, List<ChatMessage>>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] LoadChatHistory failed: {ex.Message}");
                return new Dictionary<string, List<ChatMessage>>();
            }
        }

        public static void SaveChatHistory(Dictionary<string, List<ChatMessage>> history)
        {
            try
            {
                var path = GetChatFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonConvert.SerializeObject(history, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] SaveChatHistory failed: {ex.Message}");
            }
        }

        public static string SerializeChatList(List<ChatMessage> messages)
        {
            try { return JsonConvert.SerializeObject(messages ?? new List<ChatMessage>()); }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] SerializeChatList failed: {ex.Message}");
                return "[]";
            }
        }

        public static List<ChatMessage> DeserializeChatList(string json)
        {
            try { return JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>(); }
            catch (Exception ex)
            {
                Debug.LogError($"[GuildStorage] DeserializeChatList failed: {ex.Message}");
                return new List<ChatMessage>();
            }
        }
    }
}
