using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Holds the parsed buff database + profiles (read from the Fires and Marketplace config roots).
    /// Lazily loaded; call <see cref="Reload"/> on config change. The server is authoritative for the canonical
    /// set; this is also used client-side for the local-apply test path until the sync layer lands.</summary>
    public static class BufferManager
    {
        private static List<FiresBuffData> _buffs = new List<FiresBuffData>();
        private static Dictionary<string, string> _profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static void Reload()
        {
            var (buffs, profiles) = BufferConfigParser.ReadAll();
            _buffs = buffs;
            _profiles = profiles;
            _loaded = true;
        }

        private static void EnsureLoaded() { if (!_loaded) Reload(); }

        public static IReadOnlyList<FiresBuffData> Buffs { get { EnsureLoaded(); return _buffs; } }
        public static IReadOnlyDictionary<string, string> Profiles { get { EnsureLoaded(); return _profiles; } }

        /// <summary>Resolves a buff by UniqueName, case-insensitively (KG stores them case-sensitive; we relax).</summary>
        public static FiresBuffData Resolve(string uniqueName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(uniqueName)) return null;
            foreach (var b in _buffs)
                if (string.Equals(b.UniqueName, uniqueName, StringComparison.OrdinalIgnoreCase))
                    return b;
            return null;
        }

        /// <summary>The buffs a given (lowercased) profile key sells, resolved from its CSV of UniqueNames.</summary>
        public static List<FiresBuffData> ForProfile(string profileKey)
        {
            EnsureLoaded();
            var list = new List<FiresBuffData>();
            if (string.IsNullOrEmpty(profileKey)) return list;
            if (!_profiles.TryGetValue(profileKey.ToLowerInvariant(), out var csv)) return list;
            foreach (var token in csv.Split(','))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var b = Resolve(token.Trim());
                if (b != null) list.Add(b);
            }
            return list;
        }
    }
}
