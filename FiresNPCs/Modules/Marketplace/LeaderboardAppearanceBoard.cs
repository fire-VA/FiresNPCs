using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Bridge;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Appearance READ path — LAZY per-row fetch, mirroring <see cref="LeaderboardBoard"/>'s
    /// reply-to-<c>sender</c> idiom. Inlining ~150-300 B/row into the board snapshot would cross this
    /// server's payload-sensitivity thresholds; instead, opening a remote row issues one
    /// <c>Fires_Leaderboard_RequestAppearance(name)</c> RPC and the server ships back exactly that player's
    /// record (<c>Fires_Leaderboard_Appearance</c>, targeted to the requesting peer only). The client caches
    /// the record by name (OrdinalIgnoreCase) and raises <see cref="AppearanceReceived"/> so an open preview
    /// binds. A player who never captured an appearance yields an empty record cached as an "unavailable"
    /// marker — the preview shows a clean unavailable state, never a throw. Pairs with the write path in
    /// <see cref="LeaderboardAppearanceFeed"/>.
    /// </summary>
    public static class LeaderboardAppearanceBoard
    {
        public const string RequestRpc = "Fires_Leaderboard_RequestAppearance";
        public const string ReplyRpc = "Fires_Leaderboard_Appearance";

        // Client-side cache keyed by player name. An entry with no Owner/PlayerName is the "unavailable" marker.
        private static readonly Dictionary<string, PlayerAppearance> _cache =
            new Dictionary<string, PlayerAppearance>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on the client with the requested player name once that player's record arrives.</summary>
        public static event Action<string> AppearanceReceived;

        public static void RegisterRpcs(ZRoutedRpc rpc)
        {
            if (rpc == null) return;
            rpc.Register(RequestRpc, new Action<long, ZPackage>(RPC_Request));
            rpc.Register(ReplyRpc, new Action<long, ZPackage>(RPC_Reply));
        }

        /// <summary>Client: ask the server for a player's appearance (call when opening a remote row).</summary>
        public static void RequestAppearance(string name)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(name)) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(name);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RequestRpc, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RequestAppearance failed: {ex.Message}"); }
        }

        /// <summary>Client: read a cached appearance. Returns false if not fetched yet.</summary>
        public static bool TryGet(string name, out PlayerAppearance record)
        {
            record = null;
            if (string.IsNullOrEmpty(name)) return false;
            return _cache.TryGetValue(name, out record);
        }

        // Server: look up the requested player's record and send it back to the requesting peer only.
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string name = pkg.ReadString();
                if (string.IsNullOrEmpty(name)) return;

                string ownerKey = LeaderboardServerFeed.OwnerKey(name);
                var record = PlayerAppearanceBridge.Get(ownerKey);

                var outPkg = new ZPackage();
                // Echo the requested name so the client can resolve the cache key / raise the event even when
                // the record is empty (player never captured).
                outPkg.Write(name);
                if (record == null)
                {
                    outPkg.Write(false);
                }
                else
                {
                    outPkg.Write(true);
                    record.Pack(outPkg);
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReplyRpc, outPkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Appearance RPC_Request failed: {ex.Message}"); }
        }

        // Client: cache the record (or an unavailable marker) and notify any open preview.
        private static void RPC_Reply(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                string name = pkg.ReadString();
                if (string.IsNullOrEmpty(name)) return;

                bool present = pkg.ReadBool();
                PlayerAppearance record;
                if (present)
                {
                    record = PlayerAppearance.Unpack(pkg);
                }
                else
                {
                    // Unavailable marker: empty record so callers can distinguish "fetched, nothing there".
                    record = new PlayerAppearance { PlayerName = name };
                }

                _cache[name] = record;
                try { AppearanceReceived?.Invoke(name); } catch { }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Appearance RPC_Reply failed: {ex.Message}"); }
        }
    }
}
