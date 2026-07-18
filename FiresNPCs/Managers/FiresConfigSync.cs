using System;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    /// <summary>Shared admin-save → server push. Every admin on a dedicated server is a CLIENT, so a config
    /// written with File.WriteAllText only lands in that client's config dir — it never reaches the server. This
    /// pushes the saved file body to the server via the canonical <c>FiresRPGmaker_SyncFiles</c> RPC; the server
    /// validates admin + the folder allowlist (DialogueSyncRPC.RPC_SyncFiles), writes it, and the file watcher
    /// reloads the server cache + rebroadcasts to the other clients. On a listen-server host (IsServer) the local
    /// write already IS the server copy, so this is a no-op.
    ///
    /// CONTRACT: the target <paramref name="folder"/> root MUST be in BOTH DialogueSyncRPC.AllowedSyncFolders
    /// (so the server accepts the push) AND ServerConfigFileWatcher.WatchedFolders (so it rebroadcasts), with a
    /// reload case in ServerConfigFileWatcher.ReloadServerData + DialogueSyncRPC.ExecutePendingReloads.</summary>
    public static class FiresConfigSync
    {
        public static void PushSavedConfig(string folder, string fileName, string content)
        {
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) return;   // host already wrote the canonical copy
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(folder);
                pkg.Write(fileName);
                pkg.Write(content ?? string.Empty);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_SyncFiles", pkg);
                Debug.Log($"[ConfigSync] pushed {folder}/{fileName} ({(content ?? string.Empty).Length} chars) to server");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConfigSync] push {folder}/{fileName} failed: {ex.Message}");
            }
        }
    }
}
