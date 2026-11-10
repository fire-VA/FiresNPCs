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
                ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer("FiresRPGmaker_SyncFiles"), "FiresRPGmaker_SyncFiles", pkg);
                global::FiresRPGmaker.FiresLogger.LogDebug($"[ConfigSync] pushed {folder}/{fileName} ({(content ?? string.Empty).Length} chars) to server");

                // The saving admin's own QuestManager would otherwise never have
                // it: a pure client reads these folders ONLY from the synced store, and the server leaves the sender out of the re-broadcast
                // (ServerConfigFileWatcher.MarkFileAsAdminSynced). Put what was just pushed into this client's store, as the broadcast would,
                // so the saving admin sees the save at once (callers reload right after the push). Same keys as DialogueSyncRPC's receive.
                if (FiresConfigFiles.UseStore)
                {
                    SyncedConfigStore.Apply(folder, fileName, content);
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[ConfigSync] {folder}/{fileName}: this client's synced copy updated ({(content ?? string.Empty).Length} chars) - the server does not echo a save back to its sender");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConfigSync] push {folder}/{fileName} failed: {ex.Message}");
            }
        }
    }
}
