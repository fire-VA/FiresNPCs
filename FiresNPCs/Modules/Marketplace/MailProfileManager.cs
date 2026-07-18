using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    public class MailAttachment
    {
        public string Prefab;
        public int Count;
    }

    public class MailEntry
    {
        public int Id;
        public string SenderName;
        public string DateString;
        public string Subject;
        public string Body;
        public List<MailAttachment> Attachments = new List<MailAttachment>();
    }

    /// <summary>
    /// Loads sample mail data from <c>BepInEx/config/firesnpcs/mail/</c>. .cfg format:
    /// <code>
    /// [profilename]
    /// SENDER|DATE|SUBJECT|BODY|prefab1:count1;prefab2:count2;...
    /// </code>
    /// Real send/receive flow + cross-player mailbox lands when LiteDB MailRepository is wired.
    /// </summary>
    public static class MailProfileManager
    {
        private static readonly Dictionary<string, List<MailEntry>> _byProfile = new();
        private static bool _initialized;

        private static int _nextLocalId = 10000;

        /// <summary>Returns the next unique mail id for client-staged outgoing mails.</summary>
        public static int NextLocalId() { return System.Threading.Interlocked.Increment(ref _nextLocalId); }

        /// <summary>
        /// Stage a mail in the recipient's profile inbox (local-only v1 — no cross-player delivery yet).
        /// Once the LiteDB MailRepository RPC lands, this becomes a network call; the API stays the same
        /// so the panel send button doesn't change.
        /// </summary>
        public static void AppendLocalMail(string recipientProfile, MailEntry entry)
        {
            EnsureLoaded();
            string key = (recipientProfile ?? "").ToLowerInvariant();
            if (!_byProfile.TryGetValue(key, out var pool))
            {
                pool = new List<MailEntry>();
                _byProfile[key] = pool;
            }
            pool.Insert(0, entry); // newest first
        }

        public static IReadOnlyList<MailEntry> GetMail(string profile)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<MailEntry>();
            return _byProfile.TryGetValue(profile.ToLowerInvariant(), out var list) ? list : new List<MailEntry>();
        }

        public static IEnumerable<string> GetLoadedProfileNames() { EnsureLoaded(); return _byProfile.Keys; }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _byProfile.Clear();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder))
                    try { Directory.CreateDirectory(folder); } catch { }
                // Always seed example.cfg if missing — same fix as BankerProfileManager.
                WriteExampleProfile(folder);
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Mail))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[MailProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[MailProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " message(s) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Mail);

        private static void LoadOne(string file, string[] lines)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<MailEntry>();
            int nextId = 1;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_byProfile.ContainsKey(key)) _byProfile[key] = new List<MailEntry>();
                    pool = _byProfile[key];
                    continue;
                }
                var parts = line.Split('|');
                if (parts.Length < 4) continue;
                var entry = new MailEntry
                {
                    Id = nextId++,
                    SenderName = parts[0].Trim(),
                    DateString = parts[1].Trim(),
                    Subject = parts[2].Trim(),
                    Body = parts[3].Trim().Replace("\\n", "\n"),   // un-escape multi-line bodies (see book SaveMail)
                };
                if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                {
                    foreach (var attRaw in parts[4].Split(';'))
                    {
                        var ab = attRaw.Split(':');
                        if (ab.Length != 2) continue;
                        if (!int.TryParse(ab[1], out int cnt)) continue;
                        entry.Attachments.Add(new MailAttachment { Prefab = ab[0].Trim(), Count = cnt });
                    }
                }
                pool.Add(entry);
            }
            if (pool.Count > 0 && !_byProfile.ContainsKey(key)) _byProfile[key] = pool;
        }

        /// <summary>Dumps a profile's mail entries to the pipe-separated row format the editor consumes.</summary>
        public static string Serialize(IReadOnlyList<MailEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var e in entries)
            {
                sb.Append(e.SenderName).Append('|').Append(e.DateString).Append('|')
                  .Append(e.Subject).Append('|').Append(e.Body);
                if (e.Attachments != null && e.Attachments.Count > 0)
                {
                    sb.Append('|');
                    for (int i = 0; i < e.Attachments.Count; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(e.Attachments[i].Prefab).Append(':').Append(e.Attachments[i].Count);
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        public static void SaveProfile(string name, string bodyText)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string content = $"[{name.Trim()}]" + System.Environment.NewLine + (bodyText ?? "").Trim() + System.Environment.NewLine;
                string safe = System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");
                string file = Path.Combine(folder, safe + ".cfg");
                File.WriteAllText(file, content);
                _initialized = false;
                EnsureLoaded();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Mail", safe + ".cfg", content);
                Debug.Log($"[MailProfileManager] Saved mail profile '{name}' -> {file}");
            }
            catch (System.Exception ex) { Debug.LogError("[MailProfileManager] SaveProfile failed: " + ex.Message); }
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example mail data. Drop in BepInEx\\config\\firesnpcs\\mail\\<name>.cfg.",
                "# Format per row: SENDER|DATE|SUBJECT|BODY|prefab1:count1;prefab2:count2;...",
                "# Attachments column is OPTIONAL.",
                "",
                "[example]",
                "AdminMcHelpful|Yesterday|Welcome to the server!|Hi traveller — here's a starter kit. Be kind, build wisely.|Wood:50;Stone:30;Bronze:5",
                "Postmaster Penn|3 days ago|Quarterly stipend|Your weekly stipend from the council, as agreed.|Coins:100",
                "FletcherGertrude|Today|Thanks for the trade!|That bow you sold me is incredible. Here's a small thank-you.|Feathers:10",
                "RaidBossCorpsey|5 days ago|Bounty reward|You hit me harder than anyone in months. Cherish this.|YagluthDrop:1",
                "Anonymous|1 hour ago|Mystery package|...|Resin:25;Flint:10",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
