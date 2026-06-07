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
            var folder = GetProfileFolder();
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); } catch { }
                WriteExampleProfile(folder);
            }
            foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                try { LoadOne(file); }
                catch (System.Exception ex) { Debug.LogError("[MailProfileManager] " + file + ": " + ex.Message); }
            }
            Debug.Log("[MailProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " message(s) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "mail");

        private static void LoadOne(string file)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<MailEntry>();
            int nextId = 1;
            foreach (var raw in File.ReadAllLines(file))
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
                    Body = parts[3].Trim(),
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
