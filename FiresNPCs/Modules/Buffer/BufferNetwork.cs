using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Server-authoritative buff grant. The client requests a buff sold by an NPC's profile; the SERVER
    /// validates the profile actually sells it and clamps duration/strength to the admin caps, then grants it
    /// back to the requesting client, which consumes the cost from its own inventory and applies it locally.
    ///
    /// Why the client does the final apply + cost: vanilla's SE RPC carries only a name hash (no custom
    /// ttl/strength), and Valheim inventories are client-authoritative — so the owner must apply + pay. The
    /// server still enforces the profile gate + caps, which KG's fully-client buffer never did.</summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class BufferNetwork
    {
        private const string RPC_REQUEST = "FiresRPGmaker_BufferRequest";
        private const string RPC_GRANT = "FiresRPGmaker_BufferGrant";
        // 0.0.532: registered per ZRoutedRpc instance. A process-wide flag skipped the registration after logging out
        // to the menu and joining again (a new ZRoutedRpc), so every buff buy after a relog did nothing.
        private static ZRoutedRpc _registeredOn;

        [HarmonyPostfix]
        private static void Register()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredOn, ZRoutedRpc.instance)) return;
            _registeredOn = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(RPC_REQUEST, new Action<long, ZPackage>(OnRequest));
            ZRoutedRpc.instance.Register<ZPackage>(RPC_GRANT, new Action<long, ZPackage>(OnGrant));
        }

        /// <summary>Client → server: ask to buy/apply a buff that the given profile sells. Pass profileKey empty
        /// for an admin/un-gated apply (server still clamps caps).</summary>
        public static void RequestApply(string profileKey, string uniqueName, int duration, float strength01)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(profileKey ?? "");
            pkg.Write(uniqueName ?? "");
            pkg.Write(duration);
            pkg.Write(strength01);
            ZRoutedRpc.instance.InvokeRoutedRPC(RPC_REQUEST, pkg);   // no target → routes to the server
        }

        private static void OnRequest(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                string profileKey = pkg.ReadString();
                string uniqueName = pkg.ReadString();
                int duration = pkg.ReadInt();
                float strength = pkg.ReadSingle();

                var buff = BufferManager.Resolve(uniqueName);
                if (buff == null) return;

                // The NPC's profile must actually sell this buff. An empty profile is the admin builder's un-gated apply:
                // 0.0.532 checks the sender is an admin (or the host itself); before, any player could buff themselves.
                if (string.IsNullOrEmpty(profileKey))
                {
                    bool admin = sender == ZNet.GetUID() || FiresCore.Sync.AdminSyncing.IsAdmin(sender);
                    if (!admin)
                    {
                        Debug.LogWarning($"[Buffer] {sender} requested '{uniqueName}' without a profile and is not an admin — denied");
                        return;
                    }
                }
                else
                {
                    bool sold = false;
                    foreach (var b in BufferManager.ForProfile(profileKey))
                        if (string.Equals(b.UniqueName, uniqueName, StringComparison.OrdinalIgnoreCase)) { sold = true; break; }
                    if (!sold)
                    {
                        Debug.LogWarning($"[Buffer] {sender} requested '{uniqueName}' not sold by profile '{profileKey}' — denied");
                        return;
                    }
                }

                int cap = buff.EffectiveDurationCap > 0 ? buff.EffectiveDurationCap : buff.Duration;
                int dur = Mathf.Clamp(duration > 0 ? duration : buff.Duration, 1, cap > 0 ? cap : 86400);
                float strCap = buff.StrengthCap <= 0f ? 1f : Mathf.Clamp01(buff.StrengthCap);
                float str = Mathf.Clamp(strength, 0f, strCap);

                var grant = new ZPackage();
                grant.Write(uniqueName);
                grant.Write(dur);
                grant.Write(str);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RPC_GRANT, grant);
            }
            catch (Exception ex) { Debug.LogWarning($"[Buffer] OnRequest error: {ex.Message}"); }
        }

        private static void OnGrant(long sender, ZPackage pkg)
        {
            try
            {
                string uniqueName = pkg.ReadString();
                int dur = pkg.ReadInt();
                float str = pkg.ReadSingle();

                var player = Player.m_localPlayer;
                if (player == null) return;
                var buff = BufferManager.Resolve(uniqueName);
                if (buff == null) return;

                // Consume the cost from the local inventory (client-authoritative).
                if (!string.IsNullOrEmpty(buff.NeededPrefab) && buff.NeededPrefabCount > 0)
                {
                    string itemName = ResolveItemSharedName(buff.NeededPrefab);
                    var inv = player.GetInventory();
                    if (itemName == null || inv == null || inv.CountItems(itemName) < buff.NeededPrefabCount)
                    {
                        player.Message(MessageHud.MessageType.Center, $"Not enough {buff.NeededPrefab}");
                        return;
                    }
                    inv.RemoveItem(itemName, buff.NeededPrefabCount);
                }

                if (BufferApplyEngine.Apply(player, buff, dur, str, out var msg))
                    player.Message(MessageHud.MessageType.TopLeft, msg);
                else
                    player.Message(MessageHud.MessageType.Center, "Buff failed: " + msg);
            }
            catch (Exception ex) { Debug.LogWarning($"[Buffer] OnGrant error: {ex.Message}"); }
        }

        private static string ResolveItemSharedName(string prefabName)
        {
            try
            {
                var go = ObjectDB.instance?.GetItemPrefab(prefabName);
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                return drop?.m_itemData?.m_shared?.m_name;
            }
            catch { return null; }
        }
    }
}
