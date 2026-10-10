using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace FiresRPGmaker.Modules.Sounds
{
    /// <summary>
    /// 0.0.544 (Fire 10-09: custom NPC sound files synced to every peer). The server's NPCs/Sounds folder is the source of
    /// truth. A client sends its manifest (name + MD5); the server replies with each missing or changed file in 400 KB
    /// chunks; the client writes it into its own NPCs/Sounds and loads it with Core's FiresCustomAudioStore.LoadFileCoroutine
    /// (.mp3/.wav/.ogg, Core 0.2.373+). Files are capped at 10 MB. Files the server lacks are left alone on the client.
    /// </summary>
    internal static class NpcSoundSync
    {
        private const string RpcRequest = "FiresRPGmaker_SoundsRequest";
        private const string RpcChunk = "FiresRPGmaker_SoundsChunk";
        private const int ChunkBytes = 400 * 1024;
        private const long MaxFileBytes = 10L * 1024 * 1024;
        private static readonly string[] Extensions = { ".mp3", ".wav", ".ogg" };

        private static bool s_registered;
        private static readonly Dictionary<string, MemoryStream> s_pending = new Dictionary<string, MemoryStream>(StringComparer.OrdinalIgnoreCase);
        private static int s_received;
        private static long s_receivedBytes;

        private static string Folder => FiresCore.Storage.FiresConfigPaths.Sounds;

        public static void ResetForLogout() { s_registered = false; s_pending.Clear(); }

        private static void EnsureRegistered()
        {
            if (s_registered || ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(RpcRequest, RPC_Request);
            ZRoutedRpc.instance.Register<ZPackage>(RpcChunk, RPC_Chunk);
            s_registered = true;
        }

        public static void Init() => EnsureRegistered();

        private static IEnumerable<string> LocalFiles()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (var f in Directory.GetFiles(Folder))
                if (Array.IndexOf(Extensions, Path.GetExtension(f).ToLowerInvariant()) >= 0) yield return f;
        }

        private static string Md5(string path)
        {
            using (var md5 = MD5.Create()) using (var s = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "");
        }

        /// <summary>Client: send my manifest to the server; it replies with what I lack.</summary>
        public static void RequestFromServer()
        {
            EnsureRegistered();
            if (ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
            var pkg = new ZPackage();
            var files = new List<string>(LocalFiles());
            pkg.Write(files.Count);
            foreach (var f in files) { pkg.Write(Path.GetFileName(f)); pkg.Write(Md5(f)); }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRequest, pkg);
        }

        // Server: compare the client's manifest and stream what differs.
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var have = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) have[pkg.ReadString()] = pkg.ReadString();
            var send = new List<string>();
            foreach (var f in LocalFiles())
            {
                if (new FileInfo(f).Length > MaxFileBytes) { Debug.LogWarning($"[NpcSounds] '{Path.GetFileName(f)}' is over 10 MB; not synced"); continue; }
                if (!have.TryGetValue(Path.GetFileName(f), out var h) || !string.Equals(h, Md5(f), StringComparison.OrdinalIgnoreCase)) send.Add(f);
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcSounds] peer {sender}: {send.Count} file(s) to send of {n} it has");
            if (send.Count > 0 && global::FiresRPGmaker.FiresRPGmaker.Instance != null) global::FiresRPGmaker.FiresRPGmaker.Instance.StartCoroutine(SendFiles(sender, send));
        }

        private static IEnumerator SendFiles(long target, List<string> files)
        {
            foreach (var f in files)
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(f); } catch (Exception ex) { Debug.LogWarning($"[NpcSounds] read '{f}' failed: {ex.Message}"); continue; }
                int chunks = Math.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
                for (int i = 0; i < chunks; i++)
                {
                    int off = i * ChunkBytes, len = Math.Min(ChunkBytes, bytes.Length - off);
                    var part = new byte[len];
                    System.Buffer.BlockCopy(bytes, off, part, 0, len);
                    var pkg = new ZPackage();
                    pkg.Write(Path.GetFileName(f)); pkg.Write(i); pkg.Write(chunks); pkg.Write(part);
                    ZRoutedRpc.instance?.InvokeRoutedRPC(target, RpcChunk, pkg);
                    yield return new WaitForSeconds(0.05f);
                }
            }
        }

        // Client: collect chunks, write the file, load it.
        private static void RPC_Chunk(long sender, ZPackage pkg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            try
            {
                string name = Path.GetFileName(pkg.ReadString());
                int index = pkg.ReadInt(), count = pkg.ReadInt();
                byte[] part = pkg.ReadByteArray();
                if (Array.IndexOf(Extensions, Path.GetExtension(name).ToLowerInvariant()) < 0) return;
                if (index == 0) s_pending[name] = new MemoryStream();
                if (!s_pending.TryGetValue(name, out var ms)) return;
                ms.Write(part, 0, part.Length);
                if (ms.Length > MaxFileBytes) { s_pending.Remove(name); return; }
                if (index < count - 1) return;
                s_pending.Remove(name);
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, name);
                File.WriteAllBytes(path, ms.ToArray());
                s_received++; s_receivedBytes += ms.Length;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcSounds] synced '{name}' ({ms.Length} bytes); {s_received} file(s), {s_receivedBytes} bytes this session");
                if (global::FiresRPGmaker.FiresRPGmaker.Instance != null)
                    global::FiresRPGmaker.FiresRPGmaker.Instance.StartCoroutine(FiresCore.Audio.FiresCustomAudioStore.LoadFileCoroutine(path));
            }
            catch (Exception ex) { Debug.LogWarning($"[NpcSounds] chunk failed: {ex.Message}"); }
        }
    }
}
