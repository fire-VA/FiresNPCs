using System;
using System.Reflection;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Vault;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// 0.0.560: FiresLoreBooks reward hooks, soft (no reference; LoreBooks may be absent). On the server, LoreBooks runs
    /// "QuestStart id", "QuestAdvance id [amount] [target]" and "GrantAchievement key" with the reader's player id; quest and
    /// achievement state is per client, so the server routes each one to that player's peer and the client applies it.
    /// LoreBooks' Ledger.LoreCollected (server) is routed the same way and stored as a player key for achievements.
    /// Achievement cfg: TriggerType "LoreCollected" (Prefab = a book id, or blank = how many books) or "Granted"
    /// (Prefab = the GrantAchievement key), MinAmount as usual. Player keys: fires_lore_&lt;book&gt;, fires_ach_grant_&lt;key&gt;.
    /// </summary>
    public static class LoreBooksBridge
    {
        private const string Rpc = "FiresRPGmaker_LoreHook";   // server -> reader's client: verb, args
        public const string LoreKeyPrefix = "fires_lore_";
        public const string GrantKeyPrefix = "fires_ach_grant_";
        private static ZRoutedRpc s_registeredOn;
        private static bool s_hooked;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_LoreBooks
        {
            private static void Postfix()
            {
                try { Register(); } catch (Exception ex) { Debug.LogWarning($"[LoreBooksBridge] register failed: {ex.Message}"); }
            }
        }

        private static void Register()
        {
            if (ZRoutedRpc.instance != null && s_registeredOn != ZRoutedRpc.instance)
            {
                ZRoutedRpc.instance.Register<string, string>(Rpc, RPC_OnLoreHook);
                s_registeredOn = ZRoutedRpc.instance;
            }
            if (s_hooked) return;
            Type hooks = AccessTools.TypeByName("FiresLoreBooks.Books.Hooks");
            Type ledger = AccessTools.TypeByName("FiresLoreBooks.Books.Ledger");
            if (hooks == null) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[LoreBooksBridge] FiresLoreBooks not loaded, hooks skipped"); return; }
            MethodInfo reg = hooks.GetMethod("Register", new[] { typeof(string), typeof(Action<long, string[]>) });
            if (reg == null) { Debug.LogWarning("[LoreBooksBridge] Hooks.Register(string, Action<long,string[]>) not found"); return; }
            foreach (string verb in new[] { "QuestStart", "QuestAdvance", "GrantAchievement" })
            {
                string v = verb;
                reg.Invoke(null, new object[] { v, new Action<long, string[]>((pid, args) => Route(pid, v, args)) });
            }
            EventInfo ev = ledger?.GetEvent("LoreCollected");
            if (ev != null) ev.AddEventHandler(null, new Action<long, string, int>((pid, book, count) => Route(pid, "LoreCollected", new[] { book ?? "" })));
            s_hooked = true;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LoreBooksBridge] registered QuestStart/QuestAdvance/GrantAchievement, LoreCollected {(ev != null ? "subscribed" : "MISSING")}");
        }

        // Server: find the reader's peer by player id and send the hook there (or run it here for the host's own player).
        private static void Route(long playerId, string verb, string[] args)
        {
            string joined = string.Join(" ", args ?? new string[0]);
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId) { Apply(verb, joined); return; }
            if (ZNet.instance == null || ZRoutedRpc.instance == null) return;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                var zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
                if (zdo == null || zdo.GetLong(ZDOVars.s_playerID) != playerId) continue;
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, Rpc, verb, joined);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LoreBooksBridge] {verb} '{joined}' -> peer {peer.m_uid} (player {playerId})");
                return;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LoreBooksBridge] {verb} '{joined}': player {playerId} not connected, dropped");
        }

        private static void RPC_OnLoreHook(long sender, string verb, string joined)
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID()) return;   // server only
            Apply(verb, joined);
        }

        // Client: apply one hook for the local player.
        private static void Apply(string verb, string joined)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            string[] a = (joined ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string first = a.Length > 0 ? a[0] : "";
            switch (verb)
            {
                case "QuestStart":
                    if (first.Length > 0) DialogueCommandEngine.ExecuteSingle("GiveQuest," + first);
                    break;
                case "QuestAdvance":
                {
                    var aq = ActiveQuestStore.Get(first);
                    if (aq == null) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LoreBooksBridge] QuestAdvance '{first}': not active, skipped"); return; }
                    int amount = a.Length > 1 && int.TryParse(a[1], out int n) ? n : 1;
                    int index = a.Length > 2 && int.TryParse(a[2], out int t) ? t : 0;
                    int targets = Mathf.Max(1, QuestManager.Instance?.GetQuestDefinition(first)?.QuestTargets?.Count ?? 1);
                    ActiveQuestStore.SetTargetProgress(first, index, aq.GetTargetProgress(index, targets) + amount, targets);
                    break;
                }
                case "GrantAchievement":
                    if (first.Length > 0) p.m_customData[GrantKeyPrefix + first] = "1";
                    break;
                case "LoreCollected":
                    if (first.Length > 0) p.m_customData[LoreKeyPrefix + first] = "1";
                    break;
                default: return;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[LoreBooksBridge] applied {verb} '{joined}' for '{p.GetPlayerName()}'");
        }

        /// <summary>Achievement progress for the local player's lore triggers (LeaderboardTracker.GetProgress).</summary>
        public static int LocalProgress(string triggerType, string prefab)
        {
            var p = Player.m_localPlayer;
            if (p?.m_customData == null) return 0;
            if (triggerType == "Granted") return !string.IsNullOrEmpty(prefab) && p.m_customData.ContainsKey(GrantKeyPrefix + prefab) ? 1 : 0;
            if (!string.IsNullOrEmpty(prefab)) return p.m_customData.ContainsKey(LoreKeyPrefix + prefab) ? 1 : 0;
            int n = 0;
            foreach (var k in p.m_customData.Keys) if (k.StartsWith(LoreKeyPrefix, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
