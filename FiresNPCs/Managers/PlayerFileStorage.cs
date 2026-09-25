using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Vault
{
    /// <summary>
    /// Centralized helper for player vault file I/O.
    /// VERSION: 2.1.0 - Hardened: atomic File.Replace + .bak rotation + JSON validation.
    /// 
    /// This class handles ALL file I/O for player vault data:
    /// - Ensures Unknown_* files are purged
    /// - Prevents saving files with the Unknown_ prefix
    /// - Atomic writes via File.Replace, keeping the previous good copy as a .bak
    /// - Validates the JSON round-trips before swapping so we never replace a good
    ///   file with a corrupt one (catches mid-serialization crashes / partial writes)
    /// - On load, falls back to .bak if the main file is missing or unparseable,
    ///   so a power-loss / crash mid-save can never permanently lose companion data
    /// - Thread-safe with locking
    /// </summary>
    public static class PlayerFileStorage
    {
        /// <summary>
        /// Version identifier for debugging - change this when deploying new builds.
        /// </summary>
        public const string VERSION = "2.1.0-HARDENED";

        private static readonly object FileLock = new object();
        // Filenames we've already dumped a caller stack for (one-shot, avoids per-tick spam).
        private static readonly System.Collections.Generic.HashSet<string> _rejectStackDumped =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Gets the players directory path. Computed fresh each time to handle edge cases.
        /// </summary>
        public static string PlayersDir
        {
            get
            {
                try
                {
                    return Path.Combine(FiresCore.Storage.FiresConfigPaths.Players);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayerFileStorage] Failed to compute PlayersDir: {ex.Message}");
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Called once at startup to log the version and verify the module is loaded.
        /// </summary>
        public static void LogVersion()
        {
            try
            {
                Debug.Log($"[PlayerFileStorage] VERSION: {VERSION}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerFileStorage] Failed to log VERSION: {ex.Message}");
            }
            
            try
            {
                var dir = PlayersDir;
                Debug.Log($"[PlayerFileStorage] PlayersDir: {dir ?? "NULL"}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerFileStorage] Failed to log PlayersDir: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete any files prefixed with Unknown_ in the players directory.
        /// </summary>
        public static void DeleteUnknownPrefixedFiles()
        {
            try
            {
                var dir = PlayersDir;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                
                var unknowns = Directory.GetFiles(dir, "Unknown_*.json");
                foreach (var u in unknowns)
                {
                    try
                    {
                        File.Delete(u);
                        Debug.Log($"[PlayerFileStorage] Removed invalid player file {Path.GetFileName(u)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PlayerFileStorage] Failed to delete invalid file {u}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerFileStorage] DeleteUnknownPrefixedFiles error: {ex.Message}");
            }
        }

        /// <summary>
        /// Atomically saves the given JSON to the specified file path, skipping any Unknown_* files.
        /// Returns true if the save succeeded.
        /// 
        /// This is the ONLY method that should write player vault files.
        ///
        /// Crash-safety guarantees:
        ///   1. JSON is written to <c>filePath.tmp</c> first, then flushed to disk.
        ///   2. The temp file is parsed as JSON to confirm it's well-formed before
        ///      we touch the live file. A serialization crash that produced a
        ///      partial/garbled file is rejected here, the live file is untouched.
        ///   3. On Windows we use <see cref="File.Replace"/> which is atomic and
        ///      writes the previous live file out to <c>filePath.bak</c> in the
        ///      same operation. Even a hard crash between the two cannot lose data:
        ///      either the old file is still in place, or the new file is, or the
        ///      old file is preserved as .bak and recoverable.
        ///   4. If File.Replace is unavailable (e.g. the live file doesn't exist
        ///      yet on first save) we fall back to a manual backup-then-move
        ///      sequence with the same end state.
        /// </summary>
        public static bool SavePlayerFile(string filePath, string json)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError($"[PlayerFileStorage v{VERSION}] filePath is null or empty - ABORTING");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[PlayerFileStorage v{VERSION}] json is null or empty - ABORTING");
                return false;
            }

            var fileName = Path.GetFileName(filePath);

            // Reject ANY Unknown* placeholder file (Unknown_, Unknown-, "Unknown ", …). The prior check was
            // Unknown_ only, so an "Unknown-<id>" variant slipped through / the source producing it stayed
            // hidden. On the FIRST rejection this session, dump the caller stack + the value so we can fix the
            // upstream timing that let an unresolved name reach here — the whole point is this call never happens.
            if (fileName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] REJECTED: Cannot save file with Unknown prefix: {fileName}");
                if (_rejectStackDumped.Add(fileName))
                    Debug.LogWarning($"[PlayerFileStorage] ↑ FIRST reject for '{fileName}' — caller stack so we can fix the timing:\n{Environment.StackTrace}");
                return false;
            }

            lock (FileLock)
            {
                string tmpFile = filePath + ".tmp";
                string bakFile = filePath + ".bak";

                try
                {
                    // Ensure directory exists
                    var dir = Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(dir))
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Could not extract directory from path: {filePath}");
                        return false;
                    }

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!Directory.Exists(dir))
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Directory creation FAILED: {dir}");
                        return false;
                    }

                    // Clean up Unknown_* files
                    DeleteUnknownPrefixedFiles();

                    // Remove any leftover temp file from a prior crash
                    if (File.Exists(tmpFile))
                    {
                        try { File.Delete(tmpFile); } catch { }
                    }

                    // 1. Write to temp file with explicit flush so a crash here
                    //    cannot leave a partial buffered .tmp confused with success.
                    using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.Write(json);
                        sw.Flush();
                        try { fs.Flush(true); } catch { /* Flush(true) not always supported; best effort */ }
                    }

                    if (!File.Exists(tmpFile))
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Temp file NOT created: {tmpFile}");
                        return false;
                    }

                    // 2. Validate the temp file parses as JSON before we touch the live file.
                    //    This is the safety net for the "save mysteriously wiped my companions"
                    //    class of bugs: if anything went wrong producing the JSON we'd rather
                    //    keep the previous good live file than overwrite it with garbage.
                    try
                    {
                        var raw = File.ReadAllText(tmpFile);
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            Debug.LogError($"[PlayerFileStorage v{VERSION}] Temp file is empty after write - REJECTING save for {fileName}");
                            try { File.Delete(tmpFile); } catch { }
                            return false;
                        }
                        // JToken.Parse will throw on malformed JSON
                        JToken.Parse(raw);
                    }
                    catch (Exception parseEx)
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Temp file is not valid JSON - REJECTING save for {fileName}: {parseEx.Message}");
                        try { File.Delete(tmpFile); } catch { }
                        return false;
                    }

                    // 3. Atomic swap with backup.
                    //    File.Replace requires the destination to exist; on first save
                    //    the live file isn't there yet so we just Move.
                    if (File.Exists(filePath))
                    {
                        // Remove any prior .bak so File.Replace can write a fresh one.
                        // (File.Replace overwrites the backup, but on some filesystems
                        // a stale read-only .bak can foil it; this is defensive.)
                        if (File.Exists(bakFile))
                        {
                            try { File.SetAttributes(bakFile, FileAttributes.Normal); } catch { }
                        }

                        try
                        {
                            File.Replace(tmpFile, filePath, bakFile, ignoreMetadataErrors: true);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // Non-NTFS or unusual host - fall back to manual backup + move.
                            ManualBackupAndMove(tmpFile, filePath, bakFile);
                        }
                        catch (IOException ioEx)
                        {
                            // Same drive replacement should not normally fail; if it does,
                            // try the manual fallback rather than losing the new data.
                            Debug.LogWarning($"[PlayerFileStorage v{VERSION}] File.Replace failed ({ioEx.Message}); falling back to manual backup+move");
                            ManualBackupAndMove(tmpFile, filePath, bakFile);
                        }
                    }
                    else
                    {
                        // First save for this player - no live file to back up yet.
                        File.Move(tmpFile, filePath);
                    }

                    // Verify final file
                    if (!File.Exists(filePath))
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Final file NOT created: {filePath}");
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayerFileStorage v{VERSION}] EXCEPTION saving {fileName}: {ex.Message}\n{ex.StackTrace}");
                    // Best-effort cleanup of the temp file so it doesn't accumulate.
                    try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
                    return false;
                }
            }
        }

        /// <summary>
        /// Manual fallback for File.Replace when it isn't available or errors out.
        /// Copies the live file to .bak, then moves the temp file into place.
        /// </summary>
        private static void ManualBackupAndMove(string tmpFile, string filePath, string bakFile)
        {
            // Back up the live file first so we can recover if the move dies.
            try
            {
                if (File.Exists(bakFile)) File.Delete(bakFile);
                File.Copy(filePath, bakFile, overwrite: true);
            }
            catch (Exception bakEx)
            {
                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] Backup copy failed ({bakEx.Message}); proceeding without .bak");
            }

            // Now replace the live file with the new temp.
            try { File.Delete(filePath); } catch { }
            File.Move(tmpFile, filePath);
        }
        
        /// <summary>
        /// Loads a player file and returns the JSON content, or null if not found/error.
        ///
        /// Crash-recovery: if the main file is missing OR present but unparseable
        /// (mid-write power loss could in theory leave a zero-byte main file even
        /// with the atomic-replace path), we fall back to <c>filePath.bak</c> which
        /// is the previous known-good copy written by SavePlayerFile. We also
        /// promote that .bak back into the main slot so subsequent saves continue
        /// to use a known-good baseline. Without this, a single corrupt main file
        /// would silently look like "the player has no companions" and the next
        /// save's companion-wipe safeguard would have nothing to compare against.
        /// </summary>
        public static string LoadPlayerFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
                }

                string bakFile = filePath + ".bak";

                // Try the main file first
                if (File.Exists(filePath))
                {
                    try
                    {
                        var content = File.ReadAllText(filePath);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            // Cheap parse-check; if it succeeds we're good.
                            JToken.Parse(content);
                            return content;
                        }
                        Debug.LogWarning($"[PlayerFileStorage v{VERSION}] Main player file is empty: {Path.GetFileName(filePath)} - attempting .bak");
                    }
                    catch (Exception parseEx)
                    {
                        Debug.LogWarning($"[PlayerFileStorage v{VERSION}] Main player file is corrupt ({parseEx.Message}) - attempting .bak for {Path.GetFileName(filePath)}");
                    }
                }

                // Main missing or corrupt - try the backup.
                if (File.Exists(bakFile))
                {
                    try
                    {
                        var bakContent = File.ReadAllText(bakFile);
                        if (!string.IsNullOrWhiteSpace(bakContent))
                        {
                            JToken.Parse(bakContent);

                            // Promote the backup back into the main slot so the next
                            // save has a sane baseline (and the companion-wipe
                            // safeguard in SavePlayerData has something to compare to).
                            try
                            {
                                if (File.Exists(filePath)) File.Delete(filePath);
                                File.Copy(bakFile, filePath);
                                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] Recovered {Path.GetFileName(filePath)} from .bak - companion data preserved across crash/corruption");
                            }
                            catch (Exception promoteEx)
                            {
                                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] Could not promote .bak back into main slot: {promoteEx.Message}");
                            }

                            return bakContent;
                        }
                    }
                    catch (Exception bakEx)
                    {
                        Debug.LogError($"[PlayerFileStorage v{VERSION}] Backup file is also corrupt for {Path.GetFileName(filePath)}: {bakEx.Message}");
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] LoadPlayerFile failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Checks if a player file exists.
        /// </summary>
        public static bool PlayerFileExists(string filePath)
        {
            try
            {
                return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Finds a player file by player ID. Returns the full path or null if not found.
        /// </summary>
        public static string FindPlayerFileById(long playerId)
        {
            try
            {
                var dir = PlayersDir;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return null;
                }

                // Look for name_id pattern
                var matches = Directory.GetFiles(dir, $"*_{playerId}.json");
                if (matches != null && matches.Length > 0)
                {
                    return matches[0];
                }

                // Legacy id-only file
                var legacy = Path.Combine(dir, $"{playerId}.json");
                if (File.Exists(legacy))
                {
                    return legacy;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] FindPlayerFileById failed: {ex.Message}");
            }

            return null;
        }
        
        /// <summary>
        /// Deletes a player file.
        /// </summary>
        public static bool DeletePlayerFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return false;
                }
                
                File.Delete(filePath);
                Debug.Log($"[PlayerFileStorage v{VERSION}] Deleted: {Path.GetFileName(filePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerFileStorage v{VERSION}] DeletePlayerFile failed: {ex.Message}");
                return false;
            }
        }
    }
}
