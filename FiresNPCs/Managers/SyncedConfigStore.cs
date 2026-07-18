using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// Client-side in-memory config store — the Marketplace model: the SERVER's cfg tree is the only
    /// file tree; regular clients receive file CONTENT over the FileUpdate RPC and hold it here, in
    /// RAM, for the length of the session. Nothing is written to a regular client's disk. Loaders go
    /// through <see cref="FiresConfigFiles.Enumerate"/>, which reads the grouped disk tree on the
    /// server/host and this store on pure clients — one switch, every profile family.
    /// Admins who want editable raw files pull them explicitly (firesnpcs_pull_configs).
    /// </summary>
    public static class SyncedConfigStore
    {
        // folder flat-key -> (relative file path -> content)
        private static readonly Dictionary<string, Dictionary<string, string>> _folders =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        // Footprint instrumentation: config corpora are text-sized (a heavy server measures in
        // single-digit MB), but log every 4MB boundary so a runaway family shows up in numbers
        // instead of as someone's mystery RAM usage.
        private static long _totalChars;
        private static long _lastLoggedBoundary;
        private const long LogBoundaryChars = 4L * 1024 * 1024;

        /// <summary>Total characters currently held (≈ bytes for ASCII cfg text).</summary>
        public static long TotalChars { get { lock (_lock) return _totalChars; } }

        /// <summary>Stores one synced file (empty/null content = server-side delete).</summary>
        public static void Apply(string folder, string relPath, string content)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(relPath)) return;
            relPath = relPath.Replace('\\', '/');
            lock (_lock)
            {
                if (!_folders.TryGetValue(folder, out var files))
                {
                    files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _folders[folder] = files;
                }
                if (files.TryGetValue(relPath, out var old)) _totalChars -= old.Length;
                if (string.IsNullOrEmpty(content)) files.Remove(relPath);
                else { files[relPath] = content; _totalChars += content.Length; }

                if (_totalChars / LogBoundaryChars != _lastLoggedBoundary)
                {
                    _lastLoggedBoundary = _totalChars / LogBoundaryChars;
                    UnityEngine.Debug.Log($"[SyncedConfigStore] holding {_totalChars / (1024f * 1024f):F1} MB of synced config text");
                }
            }
        }

        /// <summary>Snapshot of a folder's files (relative path -> content).</summary>
        public static List<KeyValuePair<string, string>> Files(string folder)
        {
            lock (_lock)
            {
                if (!_folders.TryGetValue(folder, out var files)) return new List<KeyValuePair<string, string>>();
                return files.ToList();
            }
        }

        /// <summary>Manifest for the diff-sync handshake (relative path -> sha256 of stored content),
        /// so reconnects within a session skip files the store already holds.</summary>
        public static Dictionary<string, string> BuildManifest(string folder)
        {
            var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                if (!_folders.TryGetValue(folder, out var files)) return manifest;
                foreach (var kv in files)
                    manifest[kv.Key] = FiresCore.Sync.ManifestDiffSync.ComputeSha256(kv.Value);
            }
            return manifest;
        }

        /// <summary>Drops everything — called when a connection ends so the next server starts clean.</summary>
        public static void Clear()
        {
            lock (_lock) _folders.Clear();
        }
    }

    /// <summary>
    /// The one enumeration point every profile-family loader reads config files through.
    /// Server/host (and the main menu): the grouped on-disk tree — unchanged behavior, the disk is
    /// the source of truth there. Pure client: the <see cref="SyncedConfigStore"/> — no disk reads,
    /// no disk writes, stale local files are ignored entirely.
    /// </summary>
    public static class FiresConfigFiles
    {
        /// <summary>True when running as a pure client connected to a remote server.</summary>
        public static bool UseStore => ZNet.instance != null && !ZNet.instance.IsServer();

        /// <summary>Yields (relative name, content) for every .cfg of a flat folder key.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Enumerate(string flatKey)
        {
            if (UseStore) return SyncedConfigStore.Files(flatKey);
            return EnumerateDisk(flatKey);
        }

        /// <summary>Same as <see cref="Enumerate"/> but with contents pre-split into lines, since most
        /// family parsers are line-based.</summary>
        public static IEnumerable<KeyValuePair<string, string[]>> EnumerateLines(string flatKey)
        {
            foreach (var kv in Enumerate(flatKey))
                yield return new KeyValuePair<string, string[]>(kv.Key, SplitLines(kv.Value));
        }

        public static string[] SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
            return content.Replace("\r\n", "\n").Split('\n');
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateDisk(string flatKey)
        {
            string dir = FiresCore.Storage.FiresConfigPaths.GroupedPath(flatKey);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.cfg", SearchOption.AllDirectories); }
            catch { yield break; }
            foreach (var f in files)
            {
                string content;
                try { content = File.ReadAllText(f); }
                catch { continue; }
                string rel = f.Length > dir.Length ? f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/') : Path.GetFileName(f);
                yield return new KeyValuePair<string, string>(rel, content);
            }
        }
    }
}
