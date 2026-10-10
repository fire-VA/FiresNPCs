using System;
using System.IO;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// 0.0.504 ([generator]'s read of the 10-06 gravity_test): drawterritory / removeterritory / the surveyors wrote the
    /// territory .cfg only into the CLIENT's config dir and reloaded the client ledger; BroadcastTerritories() returns at
    /// once unless IsServer, and the runtime (gravity, scale, every flag) reads the server-synced state. So a territory
    /// drawn or edited from a client on a dedicated server never existed for anyone. Now every such write or delete also
    /// goes to the server through the paint brush's proven path (FiresConfigSync -> FiresRPGmaker_SyncFiles: the server
    /// checks admin and the folder allowlist, writes or - for empty content - deletes, and its file watcher reloads and
    /// rebroadcasts). The server is the one source of truth; the client's local file is a working copy that the server's
    /// echo overwrites with the same content.
    /// </summary>
    internal static class TerritoryFileSync
    {
        private const string Folder = "Territories";

        private static bool IsClientOfServer => ZNet.instance != null && !ZNet.instance.IsServer();

        /// <summary>A non-admin on a dedicated server's client would get a silent refusal from the server (it only logs a
        /// Warning on its side) while the territory looked drawn locally. So the commands refuse up front, plainly, and
        /// write nothing. True = refused.</summary>
        internal static bool RefuseIfNotAdmin(Terminal context, string tag)
        {
            if (!IsClientOfServer) return false;   // host / single player: the local write is the server's copy
            bool admin;
            try { admin = FiresCore.Sync.AdminSyncing.IsLocalAdmin(); } catch { admin = false; }
            if (admin) return false;
            const string Msg = "not saved: server admin required";
            context?.AddString($"{tag} {Msg}");
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Territory " + Msg); } catch { }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritorySync] {tag} refused on this client: {Msg}");
            return true;
        }

        /// <summary>After a territory file was written locally: on a host, reload + broadcast as before; on a client, push it.</summary>
        internal static void Saved(string fullPath)
        {
            if (IsClientOfServer)
            {
                string rel = RelPath(fullPath);
                if (rel == null) { Debug.LogWarning($"[TerritorySync] {fullPath} is outside the Territories folder; not pushed"); return; }
                string body;
                try { body = File.ReadAllText(fullPath); }
                catch (Exception e) { Debug.LogWarning($"[TerritorySync] could not read {rel}: {e.Message}"); return; }
                if (body.Length == 0) return;   // an empty file means "delete" to the server; a surveyor's fresh file waits for its first node
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig(Folder, rel, body);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritorySync] pushed {rel} ({body.Length} chars) to the server; it writes, reloads and broadcasts");
                return;
            }
            // host / single player: the caller's own reload + broadcast is the server's copy already
        }

        /// <summary>After a territory file was deleted locally: on a host, reload + broadcast; on a client, ask the server to delete it.</summary>
        internal static void Deleted(string fullPath)
        {
            if (IsClientOfServer)
            {
                string rel = RelPath(fullPath);
                if (rel == null) return;
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig(Folder, rel, string.Empty);   // empty = delete on the server
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritorySync] asked the server to delete {rel}");
                return;
            }
            // host / single player: the caller's own reload + broadcast covers it
        }

        // "drawn/Name.cfg": the path under the Territories root with forward slashes, or null if it isn't under it.
        private static string RelPath(string fullPath)
        {
            try
            {
                string root = Path.GetFullPath(FiresCore.Storage.FiresConfigPaths.Territories).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(fullPath);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return full.Substring(root.Length).Replace('\\', '/');
            }
            catch { return null; }
        }
    }
}
