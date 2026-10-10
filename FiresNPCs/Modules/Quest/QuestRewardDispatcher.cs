using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// KG-parity quest-reward dispatcher. KG's <c>QuestRewardType</c> enum has 12 values; ours uses a
    /// free-form <see cref="QuestReward.Type"/> string. This dispatcher recognises every KG name
    /// (case-insensitive) plus our existing "item" / "skill_exp" aliases and routes the reward to the
    /// right delivery channel (bank, immediate skill grant, companion tame, custom-data write,
    /// mod-bridge log, etc.).
    ///
    /// Delivery channels:
    ///   • BANK — reward is queued in the player's BankItems and shown in the Banker UI ("Item" / "RandomItem")
    ///   • IMMEDIATE — applied to the live player (Skill, Skill_EXP, Pet, SetCustomValue, AddCustomValue)
    ///   • MOD-BRIDGE — stubbed log line until the partner mod ships (EpicMMO/Cozyheim/Battlepass/RustyClasses/Guild)
    /// </summary>
    public static class QuestRewardDispatcher
    {
        /// <summary>Grants one reward. <paramref name="data"/> is the vault row to bank into;
        /// <paramref name="player"/> is the live player for immediate delivery (may be null for offline grants).</summary>
        public static void Grant(PlayerVaultData data, Player player, QuestReward reward)
        {
            var tally = new RewardTally();
            Grant(data, player, reward, tally);
            tally.ShowSummary();
        }

        private static void Grant(PlayerVaultData data, Player player, QuestReward reward, RewardTally tally)
        {
            if (reward == null) return;
            string type = (reward.Type ?? "item").Trim().ToLowerInvariant();
            try
            {
                switch (type)
                {
                    case "item":
                        GiveItemToPlayer(player, reward.Target, reward.Amount, reward.Level, tally);
                        break;
                    case "randomitem":
                        GiveRandomItemToPlayer(player, reward.Target, reward.Amount, reward.Level, tally);
                        break;
                    case "skill":
                        GrantSkillLevel(player, reward.Target, reward.Amount);
                        break;
                    case "skill_exp":
                        GrantSkillExp(player, reward.Target, reward.Amount);
                        break;
                    case "pet":
                        GrantPet(player, reward.Target);
                        break;
                    case "setcustomvalue":
                        SetCustomValue(player, reward.Target, reward.Amount);
                        break;
                    case "addcustomvalue":
                        AddCustomValue(player, reward.Target, reward.Amount);
                        break;
                    case "guildaddlevel":
                        ModBridgeStub("GuildAddLevel", reward, "Guild leveling system not implemented");
                        break;
                    case "epicmmo_exp":
                        ModBridgeStub("EpicMMO_EXP", reward, "EpicMMO bridge not wired");
                        break;
                    case "cozyheim_exp":
                        ModBridgeStub("Cozyheim_EXP", reward, "Cozyheim bridge not wired");
                        break;
                    case "battlepass_exp":
                        ModBridgeStub("Battlepass_EXP", reward, "Battlepass bridge not wired");
                        break;
                    case "rustyclasses_exp":
                        ModBridgeStub("RustyClasses_EXP", reward, "RustyClasses bridge not wired");
                        break;
                    default:
                        // Unknown reward type — deliver as a plain item so the player still gets SOMETHING.
                        Debug.LogWarning("[QuestReward] Unknown reward type '" + reward.Type + "' — delivering as item.");
                        GiveItemToPlayer(player, reward.Target, reward.Amount, reward.Level, tally);
                        break;
                }
            }
            catch (Exception ex) { Debug.LogError("[QuestReward] Grant '" + type + "' threw: " + ex.Message); }
        }

        /// <summary>Convenience: grant every reward in a quest. Shows ONE center-screen summary telling the
        /// player where the items went (inventory / dropped); bank/mail overflow confirmations arrive from
        /// the server as their own message once actually stored.</summary>
        public static void GrantAll(PlayerVaultData data, Player player, IEnumerable<QuestReward> rewards)
        {
            if (rewards == null) return;
            var tally = new RewardTally();
            foreach (var r in rewards) Grant(data, player, r, tally);
            tally.ShowSummary();
        }

        // Per-grant destination counts for the summary message.
        private sealed class RewardTally
        {
            public int Inv, Ground;
            public void ShowSummary()
            {
                if (Inv <= 0 && Ground <= 0) return;
                var parts = new List<string>();
                if (Inv > 0) parts.Add($"{Inv} item(s) to your inventory");
                if (Ground > 0) parts.Add($"{Ground} item(s) DROPPED at your feet — pick them up!");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest rewards: " + string.Join(", ", parts));
            }
        }

        // ─── ITEM channel (KG parity: Marketplace.Utils.AddItemToPlayer) ──────
        // Spawn the reward in the world in front of the player, pull it into the inventory if there's room,
        // otherwise LEAVE IT ON THE GROUND. This is exactly what kg does — no bank, no mail. Banking (our old
        // path) wrote to PlayerVaultData.BankItems which SavePlayerData never persists on a client, so every
        // item reward silently vanished on turn-in.

        private static void GiveItemToPlayer(Player player, string prefabName, int count, int level, RewardTally tally)
        {
            if (player == null || string.IsNullOrEmpty(prefabName) || count <= 0) return;
            if (ZNetScene.instance == null) { Debug.LogWarning("[QuestReward] no ZNetScene — cannot grant " + prefabName); return; }
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) { Debug.LogWarning("[QuestReward] reward prefab not found: '" + prefabName + "' (quest data may need admin fix)"); return; }
            if (prefab.GetComponent<ItemDrop>() == null) { Debug.LogWarning("[QuestReward] reward prefab is not an item: " + prefabName); return; }

            int q = Mathf.Max(1, level);
            var basePos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f;
            int intoInv = 0, onGround = 0, overflow = 0;
            string mode = OverflowMode();

            bool stackable = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize > 1;
            int spawns = stackable ? 1 : count;
            for (int i = 0; i < spawns; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab, basePos, Quaternion.identity);
                var drop = go.GetComponent<ItemDrop>();
                drop.m_itemData.m_quality = q;
                if (stackable) drop.m_itemData.m_stack = count;
                drop.m_itemData.m_durability = drop.m_itemData.GetMaxDurability();
                drop.Save();
                if (player.m_inventory.CanAddItem(go))
                {
                    player.m_inventory.AddItem(drop.m_itemData);
                    ZNetScene.instance.Destroy(go);
                    intoInv += stackable ? count : 1;
                }
                else if (mode == "Ground")
                {
                    onGround += stackable ? count : 1;   // kg behavior — the spawned object stays in the world
                }
                else
                {
                    // Bank/Mail: the server owns both stores — despawn the overflow and ship it over RPC.
                    ZNetScene.instance.Destroy(go);
                    overflow += stackable ? count : 1;
                }
            }
            if (overflow > 0) SendOverflowToServer(player, mode, prefabName, overflow, q);
            if (tally != null) { tally.Inv += intoInv; tally.Ground += onGround; }
            Debug.Log($"[QuestReward] gave {count}x {prefabName} (q{q}) — inv={intoInv} ground={onGround} {mode.ToLowerInvariant()}={overflow}");
        }

        /// <summary>RandomItem: <c>Target</c> is a comma-/semicolon-separated pool; pick one and deliver it.</summary>
        private static void GiveRandomItemToPlayer(Player player, string pool, int count, int level, RewardTally tally)
        {
            if (string.IsNullOrEmpty(pool)) return;
            var choices = pool.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (choices.Length == 0) return;
            string pick = choices[UnityEngine.Random.Range(0, choices.Length)].Trim();
            GiveItemToPlayer(player, pick, count, level, tally);
        }

        // ─── overflow routing (inventory full → Mail / Bank / Ground, configurable) ──────────────────────

        private static string OverflowMode()
        {
            try
            {
                string v = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configQuestRewardOverflow?.Value;
                if (string.Equals(v, "Bank", StringComparison.OrdinalIgnoreCase)) return "Bank";
                if (string.Equals(v, "Ground", StringComparison.OrdinalIgnoreCase)) return "Ground";
            }
            catch { }
            return "Mail";
        }

        private static void SendOverflowToServer(Player player, string mode, string prefab, int amount, int level)
        {
            try
            {
                // Single-player / host: no wire hop — run the server routine directly.
                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    var res = ServerStoreOverflow(0L, player.GetPlayerID(), mode, prefab, amount, level);
                    ShowOverflowMessage(res, prefab, amount, level);
                    return;
                }
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(player.GetPlayerID());
                pkg.Write(mode);
                pkg.Write(prefab);
                pkg.Write(amount);
                pkg.Write(level);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcOverflow, pkg);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] overflow send failed: " + ex.Message); }
        }

        public const string RpcOverflow = "FiresRPGmaker_QuestRewardOverflow";
        public const string RpcOverflowResult = "FiresRPGmaker_QuestRewardOverflowResult";

        /// <summary>SERVER: store overflow rewards in the requested channel; falls back Mail↔Bank so items
        /// are never silently lost. Returns the channel actually used ("Mail"/"Bank"), or "Ground" if both
        /// failed (client re-drops locally).</summary>
        public static void RPC_Overflow(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                long pid = pkg.ReadLong();
                string mode = pkg.ReadString();
                string prefab = pkg.ReadString();
                int amount = pkg.ReadInt();
                int level = pkg.ReadInt();
                string result = ServerStoreOverflow(sender, pid, mode, prefab, amount, level);

                var reply = new ZPackage();
                reply.Write(result);
                reply.Write(prefab);
                reply.Write(amount);
                reply.Write(level);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcOverflowResult, reply);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] RPC_Overflow failed: " + ex.Message); }
        }

        private static string ServerStoreOverflow(long senderPeer, long pid, string mode, string prefab, int amount, int level)
        {
            bool preferBank = string.Equals(mode, "Bank", StringComparison.OrdinalIgnoreCase);
            // Try preferred channel, then the other — never lose the items to a single-store failure.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool bank = preferBank ? attempt == 0 : attempt == 1;
                try
                {
                    if (bank)
                    {
                        var vok = VaultOfKnowledge.Instance;
                        var pdata = vok?.GetPlayerData(pid);
                        if (pdata == null || string.IsNullOrWhiteSpace(pdata.PlayerName)) continue;
                        pdata.AddBankItem("item", prefab, amount, Math.Max(1, level), -1f);
                        pdata.IsDirty = true;
                        vok.SavePlayerData(pid, pdata);
                        Debug.Log($"[QuestReward] overflow → BANK: {amount}x {prefab} for player {pid}");
                        return "Bank";
                    }
                    else
                    {
                        string ownerId = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(senderPeer, pid);
                        if (string.IsNullOrEmpty(ownerId)) continue;
                        global::FiresCore.Storage.MailRepository.AddMail(new global::FiresCore.Storage.MailEntry
                        {
                            Owner = ownerId,
                            Sender = "Quest Rewards",
                            Topic = "Your inventory was full",
                            Message = $"{amount}x {prefab} from a quest reward is enclosed — your inventory had no room.",
                            Created = DateTime.UtcNow.Ticks,
                            Attachments = new List<global::FiresCore.Storage.StoredItem>
                            {
                                new global::FiresCore.Storage.StoredItem { ItemPrefab = prefab, Count = amount, Quality = Math.Max(1, level) }
                            },
                            Links = new List<global::FiresCore.Storage.StoredItem>(),
                            WasRead = false,
                            AttachmentsTaken = false,
                        });
                        Debug.Log($"[QuestReward] overflow → MAIL: {amount}x {prefab} for player {pid}");
                        return "Mail";
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[QuestReward] overflow {(bank ? "bank" : "mail")} store failed: {ex.Message}"); }
            }
            Debug.LogWarning($"[QuestReward] overflow could not be stored — telling client to drop {amount}x {prefab} on the ground");
            return "Ground";
        }

        /// <summary>CLIENT: server's verdict on where the overflow actually went — alert the player (and
        /// re-drop locally if the server couldn't store it anywhere).</summary>
        public static void RPC_OverflowResult(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                string mode = pkg.ReadString();
                string prefab = pkg.ReadString();
                int amount = pkg.ReadInt();
                int level = pkg.ReadInt();
                ShowOverflowMessage(mode, prefab, amount, level);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] RPC_OverflowResult failed: " + ex.Message); }
        }

        private static void ShowOverflowMessage(string mode, string prefab, int amount, int level)
        {
            string disp = DisplayName(prefab);
            if (string.Equals(mode, "Mail", StringComparison.OrdinalIgnoreCase))
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Inventory full — {amount}x {disp} MAILED to you. Visit a mailbox!");
            else if (string.Equals(mode, "Bank", StringComparison.OrdinalIgnoreCase))
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Inventory full — {amount}x {disp} sent to your BANK. Visit a banker!");
            else
            {
                // Server couldn't store it anywhere — materialize the items at the player's feet so nothing is lost.
                var player = Player.m_localPlayer;
                var prefabGo = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                if (player != null && prefabGo != null && prefabGo.GetComponent<ItemDrop>() != null)
                {
                    var pos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f;
                    bool stackable = prefabGo.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize > 1;
                    int spawns = stackable ? 1 : amount;
                    for (int i = 0; i < spawns; i++)
                    {
                        var go = UnityEngine.Object.Instantiate(prefabGo, pos, Quaternion.identity);
                        var drop = go.GetComponent<ItemDrop>();
                        drop.m_itemData.m_quality = Mathf.Max(1, level);
                        if (stackable) drop.m_itemData.m_stack = amount;
                        drop.m_itemData.m_durability = drop.m_itemData.GetMaxDurability();
                        drop.Save();
                    }
                }
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Inventory full — {amount}x {disp} DROPPED at your feet! Pick them up!");
            }
        }

        private static string DisplayName(string prefab)
        {
            try
            {
                var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                var shared = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared != null && !string.IsNullOrEmpty(shared.m_name))
                    return Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name;
            }
            catch { }
            return prefab;
        }

        // ─── IMMEDIATE channel ────────────────────────────────────────────

        /// <summary>Skill: SET the player's skill level to <c>Amount</c> (KG's "Skill" reward overrides current).</summary>
        private static void GrantSkillLevel(Player player, string skillName, int level)
        {
            if (player == null || string.IsNullOrEmpty(skillName)) return;
            if (!Enum.TryParse(skillName, ignoreCase: true, out Skills.SkillType type))
            { Debug.LogWarning("[QuestReward] Skill type not found: " + skillName); return; }
            var skills = player.GetSkills();
            if (skills == null) return;
            // Skills.Skill is the per-skill record. We force its m_level via the skills dict lookup.
            try
            {
                var data = skills.GetSkill(type);
                if (data != null) data.m_level = Mathf.Clamp(level, 0, 100);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] Skill set failed: " + ex.Message); }
        }

        /// <summary>Skill_EXP: ADD experience to the named skill (KG's standard XP grant).</summary>
        private static void GrantSkillExp(Player player, string skillName, int xp)
        {
            if (player == null || string.IsNullOrEmpty(skillName) || xp <= 0) return;
            if (!Enum.TryParse(skillName, ignoreCase: true, out Skills.SkillType type))
            { Debug.LogWarning("[QuestReward] Skill type not found: " + skillName); return; }
            try { player.RaiseSkill(type, xp); }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] RaiseSkill failed: " + ex.Message); }
        }

        /// <summary>Pet: spawn a Tameable creature at the player's feet, already tamed (KG's "Pet" reward).</summary>
        private static void GrantPet(Player player, string prefab)
        {
            if (player == null || string.IsNullOrEmpty(prefab) || ZNetScene.instance == null) return;
            var pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null) { Debug.LogWarning("[QuestReward] Pet prefab missing: " + prefab); return; }
            var spawnPos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 0.5f;
            var go = UnityEngine.Object.Instantiate(pf, spawnPos, Quaternion.identity);
            var tame = go.GetComponent<Tameable>();
            if (tame != null) tame.Tame();
        }

        /// <summary>SetCustomValue: write <c>Target</c>=<c>Amount</c> to player.m_customData.</summary>
        private static void SetCustomValue(Player player, string key, int value)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            try
            {
                player.m_customData[key] = value.ToString();
                RpgMakerApi.FireCustomValue(player.GetPlayerID(), key, value, "set");
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] SetCustomValue failed: " + ex.Message); }
        }

        /// <summary>AddCustomValue: increments the int stored at player.m_customData[key] by <c>Amount</c>.</summary>
        private static void AddCustomValue(Player player, string key, int delta)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            try
            {
                int current = 0;
                if (player.m_customData.TryGetValue(key, out var raw)) int.TryParse(raw, out current);
                int updated = current + delta;
                player.m_customData[key] = updated.ToString();
                RpgMakerApi.FireCustomValue(player.GetPlayerID(), key, updated, "add");
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] AddCustomValue failed: " + ex.Message); }
        }

        // ─── MOD-BRIDGE channel (stubs) ───────────────────────────────────

        private static void ModBridgeStub(string typeName, QuestReward reward, string reason)
        {
            Debug.LogWarning("[QuestReward] " + typeName + " skipped — " + reason
                + ". Target=" + (reward.Target ?? "") + " Amount=" + reward.Amount);
        }
    }
}
