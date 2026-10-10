using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>Feed and level-up, server-checked (Tools\COMPANION_PET_SLOT_DESIGN.md §6 "Server authority").
    /// The client asks with the pet's creature, level and XP (and for a level-up, what it holds of each needed material); the
    /// server checks against ITS config (level cap, XP need, food XP, the material list) and answers yes/no with the XP to add
    /// or the reason. The client that owns the inventory then pays and writes the item, the vanilla crafting trust model
    /// (inventories are client-owned). Registered at ZNet.Awake beside the other RPGmaker RPCs.</summary>
    public static class PetProgressRpc
    {
        private const string RpcFeed = "FiresRPGmaker_PetFeed", RpcLevel = "FiresRPGmaker_PetLevelUp", RpcReply = "FiresRPGmaker_PetReply";
        // Credit routing (PetSkills): the owner of a dying creature / gathered object tells the attacker's client.
        private const string RpcExtra = "FiresRPGmaker_PetExtra", RpcKill = "FiresRPGmaker_PetKill";
        private const float ReplyTimeout = 5f;
        private static int s_nextId = 1;
        private static readonly Dictionary<int, Action<bool, int, string>> s_pending = new Dictionary<int, Action<bool, int, string>>();
        private static readonly Dictionary<int, float> s_sentAt = new Dictionary<int, float>();

        public static void Register()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register(RpcFeed, new Action<long, ZPackage>(RPC_Feed));
            ZRoutedRpc.instance.Register(RpcLevel, new Action<long, ZPackage>(RPC_Level));
            ZRoutedRpc.instance.Register(RpcReply, new Action<long, ZPackage>(RPC_Reply));
            ZRoutedRpc.instance.Register(RpcExtra, new Action<long, ZPackage>(RPC_Extra));
            ZRoutedRpc.instance.Register(RpcKill, new Action<long, ZPackage>(RPC_Kill));
            PetProgress.BindConfig();
        }

        // ── client side ──
        /// <summary>Asks to feed one of food to the pet. reply(ok, xpGained, reason).</summary>
        public static void RequestFeed(string creature, int level, int xp, string foodPrefab, Action<bool, int, string> reply)
        {
            var p = new ZPackage();
            int id = Track(reply);
            p.Write(id); p.Write(creature ?? ""); p.Write(level); p.Write(xp); p.Write(foodPrefab ?? "");
            Send(RpcFeed, p, id);
        }

        /// <summary>Asks to level up; held = what the bag holds of each NextNeeds prefab. reply(ok, newLevel, reason).</summary>
        public static void RequestLevelUp(string creature, int level, int xp, Dictionary<string, int> held, Action<bool, int, string> reply)
        {
            var p = new ZPackage();
            int id = Track(reply);
            p.Write(id); p.Write(creature ?? ""); p.Write(level); p.Write(xp);
            p.Write(held?.Count ?? 0);
            if (held != null) foreach (var kv in held) { p.Write(kv.Key); p.Write(kv.Value); }
            Send(RpcLevel, p, id);
        }

        private static int Track(Action<bool, int, string> reply) { int id = s_nextId++; s_pending[id] = reply; s_sentAt[id] = Time.unscaledTime; return id; }

        /// <summary>[classes-ui] review #9: a request with no answer in 5 s (old / vanilla server, lost packet) fails with a reason,
        /// so the panel never stays busy. PetBuffApplier calls this once a second.</summary>
        internal static void ExpireStale()
        {
            if (s_sentAt.Count == 0) return;
            foreach (var kv in new List<KeyValuePair<int, float>>(s_sentAt))
                if (Time.unscaledTime - kv.Value > ReplyTimeout) Answer(kv.Key, false, 0, "the server didn't answer (is RPGmaker on the server?)");
        }

        internal static void SendExtra(long peer, List<string> names, bool fromCreature)
        {
            if (ZRoutedRpc.instance == null || names == null || names.Count == 0) return;
            var p = new ZPackage();
            p.Write(fromCreature); p.Write(Math.Min(names.Count, 32));
            for (int i = 0; i < names.Count && i < 32; i++) p.Write(names[i]);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcExtra, p);
        }

        internal static void SendKill(long peer, string victimPrefab)
        {
            if (ZRoutedRpc.instance == null) return;
            var p = new ZPackage(); p.Write(victimPrefab ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcKill, p);
        }

        private static void RPC_Extra(long sender, ZPackage p)
        {
            bool fromCreature = p.ReadBool(); int n = p.ReadInt();
            var names = new List<string>();
            for (int i = 0; i < n && i < 32; i++) names.Add(p.ReadString());
            PetSkills.OnRemoteExtra(names, fromCreature);
        }

        private static void RPC_Kill(long sender, ZPackage p) => PetSkills.OnRemoteKill(p.ReadString());

        private static void Send(string rpc, ZPackage p, int id)
        {
            if (ZRoutedRpc.instance == null) { Answer(id, false, 0, "not connected"); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), rpc, p);
        }

        private static void RPC_Reply(long sender, ZPackage p)
        {
            int id = p.ReadInt(); bool ok = p.ReadBool(); int value = p.ReadInt(); string reason = p.ReadString();
            Answer(id, ok, value, reason);
        }

        private static void Answer(int id, bool ok, int value, string reason)
        {
            s_sentAt.Remove(id);
            if (!s_pending.TryGetValue(id, out var cb)) return;
            s_pending.Remove(id);
            try { cb?.Invoke(ok, value, reason); } catch (Exception ex) { FiresRPGmaker.Log?.LogWarning("[FiresPets] reply handler: " + ex.Message); }
        }

        // ── server side ──
        private static void RPC_Feed(long sender, ZPackage p)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            int id = p.ReadInt(); string creature = p.ReadString(); int level = p.ReadInt(); int xp = p.ReadInt(); string food = p.ReadString();
            string why = null; int gain = 0;
            var foodGo = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(food) : null;
            var foodItem = foodGo != null ? foodGo.GetComponent<ItemDrop>()?.m_itemData : null;
            if (ZNetScene.instance?.GetPrefab(creature) == null) why = "unknown pet";
            else if (level < 0 || level >= PetProgress.MaxLevel) why = level >= PetProgress.MaxLevel ? "already level 5" : "bad level";
            else if (foodItem == null) why = "unknown food";
            else if ((gain = PetProgress.FeedXp(creature, foodItem)) <= 0) why = "that isn't food";
            else if (xp >= PetProgress.XpFor(level)) why = "full XP: level it up first";
            if (why == null) gain = Mathf.Min(gain, PetProgress.XpFor(level) - Mathf.Max(0, xp));
            Reply(sender, id, why == null, why == null ? gain : 0, why ?? "");
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] server feed {creature} L{level} xp {xp} with {food}: {(why == null ? "+" + gain + " XP" : "no, " + why)}");
        }

        private static void RPC_Level(long sender, ZPackage p)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            int id = p.ReadInt(); string creature = p.ReadString(); int level = p.ReadInt(); int xp = p.ReadInt();
            var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int n = p.ReadInt();
            for (int i = 0; i < n && i < 16; i++) held[p.ReadString()] = p.ReadInt();
            string why = null;
            if (ZNetScene.instance?.GetPrefab(creature) == null) why = "unknown pet";
            else if (level < 0 || level >= PetProgress.MaxLevel) why = level >= PetProgress.MaxLevel ? "already level 5" : "bad level";
            else if (xp < PetProgress.XpFor(level)) why = $"needs {PetProgress.XpFor(level) - xp} more XP";
            else
                foreach (var need in PetProgress.NextNeeds(creature, level))
                    if (!held.TryGetValue(need.Prefab, out int have) || have < need.Count) { why = $"needs {need.Count} {need.Name} (have {(held.TryGetValue(need.Prefab, out int h) ? h : 0)})"; break; }
            Reply(sender, id, why == null, why == null ? level + 1 : level, why ?? "");
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] server level-up {creature} L{level} xp {xp}: {(why == null ? "yes » L" + (level + 1) : "no, " + why)}");
        }

        private static void Reply(long to, int id, bool ok, int value, string reason)
        {
            var r = new ZPackage();
            r.Write(id); r.Write(ok); r.Write(value); r.Write(reason ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(to, RpcReply, r);
        }

        // ── the client's half after a yes ──
        /// <summary>Feeds one food from the bag: asks the server, then takes the food and adds the XP. done(message, ok).</summary>
        public static void Feed(Player player, ItemDrop.ItemData pet, ItemDrop.ItemData food, Action<string, bool> done)
        {
            string creature = PetCatalog.CreatureOf(pet);
            if (player == null || pet == null || food?.m_dropPrefab == null) { done?.Invoke("Nothing to feed.", false); return; }
            string foodPrefab = food.m_dropPrefab.name;
            RequestFeed(creature, PetProgress.Level(pet), PetProgress.Xp(pet), foodPrefab, (ok, gain, why) =>
            {
                if (!ok) { done?.Invoke("Can't feed: " + why + ".", false); return; }
                if (!StillEquipped(player, pet)) { done?.Invoke("The pet left its slot; nothing was fed.", false); return; }
                var inv = player.GetInventory();
                if (PetProgress.CountInBag(inv, foodPrefab) < 1) { done?.Invoke("The food is gone from your bag.", false); return; }
                RemoveByPrefab(inv, foodPrefab, 1);
                int added = PetProgress.AddXp(pet, gain);
                FiresRPGmaker.Log?.LogDebug($"[FiresPets] fed {creature} with {foodPrefab}: +{added} XP » {PetProgress.Xp(pet)}/{PetProgress.XpFor(PetProgress.Level(pet))}");
                done?.Invoke($"Fed {PetProgress.ItemName(foodPrefab)}: +{added} XP.", true);
            });
        }

        /// <summary>Levels up: asks the server with what the bag holds, then takes the materials and writes level+1, XP 0.</summary>
        public static void LevelUp(Player player, ItemDrop.ItemData pet, Action<string, bool> done)
        {
            string creature = PetCatalog.CreatureOf(pet);
            if (player == null || pet == null) { done?.Invoke("No pet.", false); return; }
            int level = PetProgress.Level(pet);
            var inv = player.GetInventory();
            var needs = PetProgress.NextNeeds(creature, level);
            var held = new Dictionary<string, int>();
            foreach (var n in needs) held[n.Prefab] = PetProgress.CountInBag(inv, n.Prefab);
            RequestLevelUp(creature, level, PetProgress.Xp(pet), held, (ok, newLevel, why) =>
            {
                if (!ok) { done?.Invoke("Can't level up: " + why + ".", false); return; }
                if (!StillEquipped(player, pet) || PetProgress.Level(pet) != level) { done?.Invoke("The pet changed meanwhile; nothing was spent.", false); return; }
                foreach (var n in needs)
                    if (PetProgress.CountInBag(inv, n.Prefab) < n.Count) { done?.Invoke($"Can't level up: {n.Name} left your bag.", false); return; }
                foreach (var n in needs) RemoveByPrefab(inv, n.Prefab, n.Count);
                PetProgress.Write(pet, newLevel, 0);
                FiresRPGmaker.Log?.LogDebug($"[FiresPets] level up {creature}: L{level} » L{newLevel}");
                done?.Invoke($"{PetText.PetName(creature)} reached level {newLevel}!", true);
            });
        }

        // [classes-ui] review #11: the reply comes later; the pet must still be the one in the slot.
        private static bool StillEquipped(Player player, ItemDrop.ItemData pet) => player != null && pet != null && PetManager.LocalPetItem(player) == pet;

        private static void RemoveByPrefab(Inventory inv, string prefab, int count)
        {
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (count <= 0) break;
                if (it?.m_dropPrefab == null || !string.Equals(it.m_dropPrefab.name, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                int take = Mathf.Min(count, it.m_stack);
                inv.RemoveItem(it, take);
                count -= take;
            }
        }
    }
}
