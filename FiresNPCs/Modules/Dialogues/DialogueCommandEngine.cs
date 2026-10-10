using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Progression;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using Random = UnityEngine.Random;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Executes dialogue option commands at runtime.
    /// 
    /// Command format: "CommandName,Param1,Param2,..." (comma or colon separated).
    /// Supports: GiveItem, GiveItemWithData, RemoveItem, GiveQuest, RemoveQuest, FinishQuest,
    /// Teleport, Spawn, SpawnRaw, SpawnXYZ, SpawnWithData, SpawnXYZWithData, SetPlayerData,
    /// Damage, Heal, GiveBuff, AddPlayerKey, RemovePlayerKey, AddCustomPlayerKey,
    /// RemoveCustomPlayerKey, AddCustomValue, SetCustomValue, PlayAnimation, PlaySound,
    /// ConsoleCommand, AddPin, PingMap, OpenUI, EnterPassword, SendWebhook, PlayVideo,
    /// GiveTerritory, RemoveTerritory, SetNPCModelLocal/Global, SetNPCNameLocal/Global,
    /// SetNPCPatrol. Partner-mod/Factions verbs parse but no-op with a warning until wired.
    /// </summary>
    public static class DialogueCommandEngine
    {
        /// <summary>Text replacement tokens applied to dialogue/option text and ConsoleCommand.</summary>
        public static readonly Dictionary<string, Func<string>> TextReplacements = new Dictionary<string, Func<string>>
        {
            { "%playername%", () => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Player" }
        };

        /// <summary>Applies text replacements (e.g., %playername%) to a string.</summary>
        public static string ApplyReplacements(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var kvp in TextReplacements)
                text = text.Replace(kvp.Key, kvp.Value());
            return text;
        }

        /// <summary>Executes all commands in the array sequentially.</summary>
        public static void ExecuteAll(string[] commands, ZNetView npcView = null)
        {
            if (commands == null) return;
            foreach (var cmd in commands)
                ExecuteSingle(cmd, npcView);
        }

        /// <summary>Executes random commands - each entry has a percentage chance to fire.</summary>
        public static void ExecuteRandom(List<KeyValuePair<int, string>> randomCommands, ZNetView npcView = null)
        {
            if (randomCommands == null) return;
            foreach (var kvp in randomCommands)
            {
                int roll = Random.Range(0, 100);
                if (roll < kvp.Key)
                    ExecuteSingle(kvp.Value, npcView);
            }
        }

        /// <summary>Parses and executes a single command string.</summary>
        public static void ExecuteSingle(string command, ZNetView npcView = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            try
            {
                string[] parts = command.Replace(":", ",").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;

                string cmdName = parts[0].Trim();
                DispatchCommand(cmdName, parts, npcView);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueCommandEngine] Error executing command '{command}': {ex.Message}");
            }
        }

        private static void DispatchCommand(string cmdName, string[] parts, ZNetView npcView)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            switch (cmdName.ToLowerInvariant())
            {
                case "giveitem": Cmd_GiveItem(player, parts); break;
                case "removeitem": Cmd_RemoveItem(player, parts); break;
                case "givequest": Cmd_GiveQuest(player, parts); break;
                case "removequest": Cmd_RemoveQuest(player, parts); break;
                case "finishquest": Cmd_FinishQuest(player, parts); break;
                case "teleport": Cmd_Teleport(player, parts); break;
                case "spawn": Cmd_Spawn(player, parts); break;
                case "spawnraw": Cmd_SpawnRaw(player, parts); break;
                case "spawnxyz": Cmd_SpawnXYZ(parts); break;
                case "damage": Cmd_Damage(player, parts); break;
                case "heal": Cmd_Heal(player, parts); break;
                case "givebuff": Cmd_GiveBuff(player, parts); break;
                case "addplayerkey": Cmd_AddPlayerKey(player, parts); break;
                case "removeplayerkey": Cmd_RemovePlayerKey(player, parts); break;
                case "addcustomplayerkey": Cmd_AddCustomPlayerKey(player, parts); break;
                case "removecustomplayerkey": Cmd_RemoveCustomPlayerKey(player, parts); break;
                case "playanimation": Cmd_PlayAnimation(npcView, parts); break;
                case "playsound": Cmd_PlaySound(npcView, parts); break;
                case "consolecommand": Cmd_ConsoleCommand(player, parts); break;
                case "addpin": Cmd_AddPin(parts); break;
                case "pingmap": Cmd_PingMap(parts); break;
                case "openui": Cmd_OpenUI(npcView, parts); break;
                case "enterpassword": Cmd_EnterPassword(npcView, parts); break;
                case "sendwebhook": Cmd_SendWebhook(player, parts); break;
                case "giveterritory": Cmd_GiveTerritory(parts); break;
                case "removeterritory": Cmd_RemoveTerritory(parts); break;
                // ── MP-parity verbs (drop-in names; semantics from Dialogues_DataTypes.ParseCommands) ──
                case "giveitemwithdata": Cmd_GiveItemWithData(player, parts); break;
                case "setplayerdata": Cmd_SetPlayerData(player, parts); break;
                case "spawnwithdata": Cmd_SpawnWithData(player, parts); break;
                case "spawnxyzwithdata": Cmd_SpawnXYZWithData(parts); break;
                case "addcustomvalue": Cmd_AddCustomValue(player, parts, add: true); break;
                case "setcustomvalue": Cmd_AddCustomValue(player, parts, add: false); break;
                case "playvideo": Cmd_PlayVideo(parts); break;
                case "setnpcmodellocal": Cmd_SetNpcModel(npcView, parts, global: false); break;
                case "setnpcmodelglobal": Cmd_SetNpcModel(npcView, parts, global: true); break;
                case "setnpcnamelocal": Cmd_SetNpcName(npcView, parts, global: false); break;
                case "setnpcnameglobal": Cmd_SetNpcName(npcView, parts, global: true); break;
                case "setnpcpatrol": Cmd_SetNpcPatrol(npcView, parts); break;
                // Partner-mod bridges + Factions verbs: accepted (so MP configs parse) but honest
                // no-ops until their systems land — see the parity matrix.
                case "addepicmmoexp":
                case "addcozyheimexp":
                case "battlepass_exp":
                case "addrustyclassesexp":
                case "guildaddlevel":
                case "setplayerguild":
                case "setplayerguildrank":
                    Debug.LogWarning($"[DialogueCommandEngine] '{cmdName}' requires a partner system that isn't wired yet — command ignored");
                    break;
                case "setfaction":
                    if (parts.Length > 1) global::FiresRPGmaker.Modules.Factions.FactionMembership.SetLocal(parts[1].Trim());
                    break;
                case "removefaction":
                    global::FiresRPGmaker.Modules.Factions.FactionMembership.ClearLocal();
                    break;
                default:
                    Debug.LogWarning($"[DialogueCommandEngine] Unknown command: {cmdName}");
                    break;
            }
        }

        #region Command Implementations

        private static void Cmd_GiveItem(Player player, string[] parts)
        {
            // GiveItem,PrefabID,Amount,Level
            if (parts.Length < 4) return;
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            if (!int.TryParse(parts[3].Trim(), out int level)) level = 1;

            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
            {
                Debug.LogWarning($"[DialogueCommandEngine] GiveItem: not found or not an item: {prefabName}");
                return;
            }

            var inv = player.GetInventory();
            if (inv == null) return;

            var itemDrop = prefab.GetComponent<ItemDrop>();
            for (int i = 0; i < amount; i++)
            {
                var itemData = itemDrop.m_itemData.Clone();
                itemData.m_quality = Mathf.Max(1, level);
                itemData.m_stack = 1;
                if (!inv.AddItem(itemData))
                {
                    ItemDrop.DropItem(itemData, 1, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
                }
            }
        }

        private static void Cmd_GiveTerritory(string[] parts)
        {
            // GiveTerritory,TerritoryName — grants ownership of the named territory to the talking player.
            // Server-validated: non-admins only succeed for territories whose Type is ForSale.
            if (parts.Length < 2) return;
            string name = parts[1].Trim();
            if (string.IsNullOrEmpty(name)) return;
            global::FiresRPGmaker.Modules.Territories.TerritoryOwnership.RequestGrant(name, true);
        }

        private static void Cmd_RemoveTerritory(string[] parts)
        {
            // RemoveTerritory,TerritoryName — releases the talking player's ownership of the named territory.
            if (parts.Length < 2) return;
            string name = parts[1].Trim();
            if (string.IsNullOrEmpty(name)) return;
            global::FiresRPGmaker.Modules.Territories.TerritoryOwnership.RequestGrant(name, false);
        }

        private static void Cmd_RemoveItem(Player player, string[] parts)
        {
            // RemoveItem,PrefabID,Amount
            if (parts.Length < 3) return;
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;

            var inv = player.GetInventory();
            if (inv == null) return;

            int remaining = amount;
            var allItems = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (var item in allItems)
            {
                if (remaining <= 0) break;
                bool match = (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName);
                if (!match)
                {
                    var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
                    if (prefab != null)
                    {
                        var drop = prefab.GetComponent<ItemDrop>();
                        match = drop != null && item.m_shared.m_name == drop.m_itemData.m_shared.m_name;
                    }
                }
                if (!match) continue;

                int removeCount = Mathf.Min(item.m_stack, remaining);
                inv.RemoveItem(item, removeCount);
                remaining -= removeCount;
            }
        }

        private static void Cmd_GiveQuest(Player player, string[] parts)
        {
            // GiveQuest,QuestID - adds quest to player's active quests via VaultOfKnowledge
            if (parts.Length < 2) return;
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();

            var qm = QuestManager.Instance;
            if (qm == null) { Debug.LogWarning("[DialogueCommandEngine] GiveQuest: QuestManager not initialized"); return; }

            // Check the quest exists
            var def = qm.GetQuestDefinition(questId);
            if (def == null) { Debug.LogWarning($"[DialogueCommandEngine] GiveQuest: quest '{questId}' not found"); return; }

            // Check not already active
            if (qm.HasQuest(playerId, questId)) return;

            // kg-parity: client-local, per-quest accept (no vault blob).
            global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Accept(questId, def);

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest accepted: {def.Name ?? questId}");

            // KG-parity lifecycle: fire OnAcceptQuest commands.
            try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.Fire(def, global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnAccept); } catch { }
        }

        private static void Cmd_RemoveQuest(Player player, string[] parts)
        {
            // RemoveQuest,QuestID
            if (parts.Length < 2) return;
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();

            // kg-parity: client-local per-quest removal.
            global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Abandon(questId);
        }

        private static void Cmd_FinishQuest(Player player, string[] parts)
        {
            // FinishQuest,QuestID - force-completes a quest
            if (parts.Length < 2) return;
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();

            // kg-parity: drop the active quest from the client-local store; keep the completion record in the vault.
            global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Abandon(questId);
            var vaultData = VaultOfKnowledge.Instance?.GetPlayerData(playerId);
            if (vaultData != null)
            {
                vaultData.AddCompletedQuest(questId, 1);
                vaultData.IsDirty = true;
                VaultOfKnowledge.Instance.SavePlayerData(playerId, vaultData);
            }

            var def = QuestManager.Instance?.GetQuestDefinition(questId);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest completed: {def?.Name ?? questId}");
        }

        private static void Cmd_Teleport(Player player, string[] parts)
        {
            // Teleport,X,Y,Z[,AllowOre]
            if (parts.Length < 4) return;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;
            bool allowOre = parts.Length > 4 && bool.TryParse(parts[4].Trim(), out bool ao) && ao;

            if (!allowOre && !player.IsTeleportable(allowAllItems: false))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_noteleport"));
                return;
            }

            player.TeleportTo(new Vector3(x, y, z), player.transform.rotation, true);
        }

        private static void Cmd_Spawn(Player player, string[] parts)
        {
            // Spawn,Prefab[,Amount[,Level]] — near the player. Amount/Level optional (default 1).
            if (parts.Length < 2) { Debug.LogWarning("[DialogueCommandEngine] Spawn needs a prefab name: Spawn,Prefab[,Amount,Level]"); return; }
            string prefabName = parts[1].Trim();
            int amount = parts.Length > 2 && int.TryParse(parts[2].Trim(), out int a2) ? Mathf.Max(1, a2) : 1;
            int level = parts.Length > 3 && int.TryParse(parts[3].Trim(), out int l2) ? l2 : 1;

            var prefab = ResolvePrefab(prefabName);
            if (prefab == null) { Debug.LogWarning($"[DialogueCommandEngine] Spawn: prefab '{prefabName}' not found (try exact case, e.g. Karve)"); return; }

            Vector3 basePos = player.transform.position;
            for (int i = 0; i < amount; i++)
            {
                float rx = Random.Range(-15f, 15f);
                float rz = Random.Range(-15f, 15f);
                Vector3 pos = new Vector3(basePos.x + rx, basePos.y, basePos.z + rz);
                if (ZoneSystem.instance != null) ZoneSystem.instance.FindFloor(pos, out pos.y);

                var spawned = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                var character = spawned.GetComponent<Character>();
                if (character != null) character.SetLevel(Mathf.Max(1, level + 1));
                var itemDrop = spawned.GetComponent<ItemDrop>();
                if (itemDrop != null) itemDrop.SetQuality(level);
            }
        }

        private static void Cmd_SpawnRaw(Player player, string[] parts)
        {
            // SpawnRaw,PrefabName,Amount,Level - uses ZNetScene.Instantiate for proper ZDO network spawning
            if (parts.Length < 2) return;
            string prefabName = parts[1].Trim();
            int amount = parts.Length > 2 && int.TryParse(parts[2].Trim(), out int a) ? a : 1;
            int level = parts.Length > 3 && int.TryParse(parts[3].Trim(), out int l) ? l : 1;

            if (ZNetScene.instance == null) return;
            var prefab = ResolvePrefab(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[DialogueCommandEngine] SpawnRaw: prefab '{prefabName}' not found (try exact case, e.g. Karve)");
                return;
            }

            Vector3 basePos = player.transform.position;
            for (int i = 0; i < amount; i++)
            {
                float rx = Random.Range(-15f, 15f);
                float rz = Random.Range(-15f, 15f);
                Vector3 pos = new Vector3(basePos.x + rx, basePos.y, basePos.z + rz);
                if (ZoneSystem.instance != null) ZoneSystem.instance.FindFloor(pos, out pos.y);

                var spawned = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                if (spawned == null) continue;
                var character = spawned.GetComponent<Character>();
                if (character != null) character.SetLevel(Mathf.Max(1, level + 1));
                var itemDropComp = spawned.GetComponent<ItemDrop>();
                if (itemDropComp != null) itemDropComp.SetQuality(level);
            }
        }

        private static void Cmd_SpawnXYZ(string[] parts)
        {
            // SpawnXYZ,Prefab,Amount,Level,X,Y,Z,MaxDistance
            if (parts.Length < 8) { Debug.LogWarning("[DialogueCommandEngine] SpawnXYZ format is Prefab,Amount,Level,X,Y,Z,MaxDist (7 values after the name)"); return; }
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            if (!int.TryParse(parts[3].Trim(), out int level)) level = 1;
            if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            if (!float.TryParse(parts[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;
            if (!int.TryParse(parts[7].Trim(), out int maxDist)) maxDist = 10;

            var prefab = ResolvePrefab(prefabName);
            if (prefab == null) { Debug.LogWarning($"[DialogueCommandEngine] SpawnXYZ: prefab '{prefabName}' not found (try exact case, e.g. Karve)"); return; }

            for (int i = 0; i < amount; i++)
            {
                float rx = Random.Range(-maxDist, maxDist);
                float rz = Random.Range(-maxDist, maxDist);
                Vector3 pos = new Vector3(x + rx, y, z + rz);
                var spawned = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                var character = spawned.GetComponent<Character>();
                if (character != null) character.SetLevel(Mathf.Max(1, level + 1));
                var itemDrop = spawned.GetComponent<ItemDrop>();
                if (itemDrop != null) itemDrop.SetQuality(level);
            }
        }

        // Resolve a prefab by exact name first (fast hash lookup), then case-insensitively so "karve" still
        // finds "Karve" — vanilla GetPrefab is case-sensitive, which silently bit dialogue authors.
        private static GameObject ResolvePrefab(string name)
        {
            if (ZNetScene.instance == null || string.IsNullOrEmpty(name)) return null;
            var p = ZNetScene.instance.GetPrefab(name);
            if (p != null) return p;
            var list = ZNetScene.instance.m_prefabs;
            if (list != null)
                foreach (var go in list)
                    if (go != null && string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase)) return go;
            return null;
        }

        private static void Cmd_Damage(Player player, string[] parts)
        {
            if (parts.Length < 2) return;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dmg)) return;
            var hitData = new HitData();
            hitData.m_damage.m_damage = dmg;
            player.Damage(hitData);
        }

        private static void Cmd_Heal(Player player, string[] parts)
        {
            if (parts.Length < 2) return;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float heal)) return;
            player.Heal(heal);
        }

        private static void Cmd_GiveBuff(Player player, string[] parts)
        {
            if (parts.Length < 2) return;
            string seName = parts[1].Trim();
            int hash = seName.GetStableHashCode();
            player.GetSEMan().RemoveStatusEffect(hash, true);
            var se = player.GetSEMan().AddStatusEffect(hash, true);
            if (se != null && parts.Length > 2 && float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ttl))
                se.m_ttl = Mathf.Max(1f, ttl);
        }

        private static void Cmd_AddPlayerKey(Player player, string[] parts)
        {
            if (parts.Length < 2) return;
            string key = parts[1].Trim();
            player.AddUniqueKey(key);
            PrivateKeyManager.AddPrivateKey(key);
            EwpDataBridge.AddKey(EwpDataBridge.GetLocalSteamId(), key);
        }

        private static void Cmd_RemovePlayerKey(Player player, string[] parts)
        {
            if (parts.Length < 2) return;
            string key = parts[1].Trim();
            player.RemoveUniqueKey(key);
            PrivateKeyManager.RemovePrivateKey(key);
            EwpDataBridge.RemoveKey(EwpDataBridge.GetLocalSteamId(), key);
        }

        private static void Cmd_AddCustomPlayerKey(Player player, string[] parts)
        {
            // AddCustomPlayerKey,Key          - presence key (steamId/Key in EWP, ZDO=1)
            // AddCustomPlayerKey,Key/++       - increment by 1
            // AddCustomPlayerKey,Key/--       - decrement by 1
            // AddCustomPlayerKey,Key/++,5     - increment by 5
            // AddCustomPlayerKey,Key/--,3     - decrement by 3
            if (parts.Length < 2) return;
            string raw = parts[1].Trim();

            var nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            string steamId = EwpDataBridge.GetLocalSteamId();

            // Check for /++ or /-- suffix
            if (raw.EndsWith("/++") || raw.EndsWith("/--"))
            {
                bool isIncrement = raw.EndsWith("/++");
                string key = raw.Substring(0, raw.Length - 3);
                int amount = 1;
                if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out int parsed))
                    amount = parsed;

                int current = nview.GetZDO().GetInt(key);
                int newValue = isIncrement ? current + amount : current - amount;
                nview.GetZDO().Set(key, newValue);
                EwpDataBridge.WriteKey(steamId, key, newValue);
            }
            else
            {
                // Presence key - just mark it as existing
                nview.GetZDO().Set(raw, 1);
                EwpDataBridge.AddKey(steamId, raw);
            }
        }

        private static void Cmd_RemoveCustomPlayerKey(Player player, string[] parts)
        {
            // RemoveCustomPlayerKey,Key - removes the key from player ZDO + EWP data
            if (parts.Length < 2) return;
            string key = parts[1].Trim();
            var nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            nview.GetZDO().Set(key, 0);

            // Remove from expand_world/ewp_data.yaml
            string steamId = EwpDataBridge.GetLocalSteamId();
            EwpDataBridge.RemoveKey(steamId, key);
        }

        private static void Cmd_PlayAnimation(ZNetView npcView, string[] parts)
        {
            if (parts.Length < 2 || npcView == null) return;
            var zanim = npcView.GetComponent<ZSyncAnimation>();
            if (zanim != null)
                zanim.SetTrigger(parts[1].Trim());
        }

        private static void Cmd_ConsoleCommand(Player player, string[] parts)
        {
            // ConsoleCommand,command args
            if (parts.Length < 2) return;
            string fullCmd = string.Join(" ", parts.Skip(1).Select(p => p.Trim()));
            fullCmd = fullCmd.Replace("{playername}", player.GetPlayerName());
            fullCmd = ApplyReplacements(fullCmd);

            // Use Console.TryRunCommand which is the public API
            if (Console.instance != null)
                Console.instance.TryRunCommand(fullCmd);
        }

        private static void Cmd_AddPin(string[] parts)
        {
            if (parts.Length < 5) return;
            string label = parts[1].Trim();
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;

            Vector3 pos = new Vector3(x, y, z);
            if (Minimap.instance != null)
            {
                Minimap.instance.AddPin(pos, Minimap.PinType.Icon3, label, true, false);
                Minimap.instance.ShowPointOnMap(pos);
            }
        }

        private static void Cmd_PingMap(string[] parts)
        {
            if (parts.Length < 5) return;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;

            Vector3 pos = new Vector3(x, y, z);
            if (Minimap.instance != null)
                Minimap.instance.ShowPointOnMap(pos);
        }

        private static void Cmd_OpenUI(ZNetView npcView, string[] parts)
        {
            // OpenUI,UIType,Profile - opens an NPC UI screen from a dialogue command.
            string uiType = parts.Length > 1 ? parts[1].Trim() : "";
            string profile = parts.Length > 2 ? parts[2].Trim() : "";
            Debug.Log($"[DialogueCommandEngine] OpenUI requested: type={uiType}, profile={profile}");

            // Close the dialogue first so it doesn't overlap with the new screen
            DialogueUI.Hide();

            // Get or create an NpcController on the NPC so we can pass it to the screen
            NpcController npc = null;
            if (npcView != null)
            {
                npc = npcView.GetComponent<NpcController>();
                if (npc == null)
                {
                    // Companion NPC or other entity without NpcController - add a temporary one
                    npc = npcView.gameObject.AddComponent<NpcController>();
                    npc.npcProfileName = npcView.GetComponent<Character>()?.m_name ?? "NPC";
                }
            }

            switch (uiType.ToLowerInvariant())
            {
                case "quests":
                    if (npc != null && !string.IsNullOrEmpty(profile))
                    {
                        npc.questProfile = profile;
                        QuestInteractionScreenController.ShowForNpc(npc);
                    }
                    else
                    {
                        Debug.LogWarning($"[DialogueCommandEngine] OpenUI Quests - missing NPC or profile");
                    }
                    break;

                case "info":
                    if (!string.IsNullOrEmpty(profile))
                    {
                        var infoProfile = InfoNpcScreenController.GetProfileForName(profile);
                        string displayName = npc != null ? npc.GetDisplayName() : "NPC";
                        InfoNpcPlayerPanel.Show(displayName, infoProfile);
                    }
                    break;

                case "trader":
                    if (npc != null && !string.IsNullOrEmpty(profile))
                    {
                        npc.traderProfile = profile;
                        TraderUIController.ShowForNpc(npc);
                    }
                    else if (!string.IsNullOrEmpty(profile))
                    {
                        string traderName = npc != null ? npc.GetDisplayName() : "Trader";
                        TraderUIController.ShowForCompanion(traderName, profile);
                    }
                    break;

                case "teleporter":
                case "teleport":
                case "teleporthub":
                    // Drive the FiresWayshrines network (soft dep): opens the hub's destination
                    // chooser; profile = the Teleporters/*.cfg hub name. One travel system.
                    global::FiresRPGmaker.Bridges.WayshrinesBridge.OpenSelectionForProfile(
                        Player.m_localPlayer, profile ?? "");
                    break;

                case "marketplace":
                    if (npc != null && !string.IsNullOrEmpty(profile)) npc.marketplaceProfile = profile;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.MarketplacePlayerPanel.Show(profile ?? "", npc != null ? npc.GetDisplayName() : "Marketplace");
                    break;
                case "banker":
                    if (npc != null && !string.IsNullOrEmpty(profile)) npc.bankerProfile = profile;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.BankerPlayerPanel.Show(profile ?? "", npc != null ? npc.GetDisplayName() : "Banker");
                    break;
                case "mail":
                    if (npc != null && !string.IsNullOrEmpty(profile)) npc.mailProfile = profile;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.MailPlayerPanel.Show(profile ?? "", npc != null ? npc.GetDisplayName() : "Mail");
                    break;
                case "transmog":
                case "transmogrification":
                    if (npc != null && !string.IsNullOrEmpty(profile)) npc.transmogProfile = profile;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.TransmogPlayerPanel.Show(profile ?? "", npc != null ? npc.GetDisplayName() : "Transmog");
                    break;

                default:
                    Debug.LogWarning($"[DialogueCommandEngine] OpenUI - unknown UI type '{uiType}'");
                    break;
            }
        }

        private static void Cmd_PlaySound(ZNetView npcView, string[] parts)
        {
            // PlaySound,ClipName - plays a sound effect at the NPC's position
            if (parts.Length < 2) return;
            string clipName = parts[1].Trim();

            if (npcView == null && Player.m_localPlayer == null) return;
            Vector3 pos = npcView != null ? npcView.transform.position : Player.m_localPlayer.transform.position;

            try
            {
                // Try to find the effect prefab in ZNetScene (e.g., "sfx_greydwarf_idle")
                var prefab = ResolvePrefab(clipName);
                if (prefab != null)
                {
                    UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                    return;
                }

                // Bare clip name. Custom mp3s win, then the shared cached scan. This was
                // Resources.Load<AudioClip>, which only sees a Resources folder - Valheim ships none, so
                // every plain clip name resolved to null and the command silently did nothing.
                AudioClip clip;
                if (!FiresCore.Audio.FiresCustomAudioStore.Clips.TryGetValue(clipName, out clip) || clip == null)
                    clip = global::FiresRPGmaker.NpcFashion.NpcFashionManager.ResolveClipCached(clipName);

                // ResolveClipCached warns once per name and caches the miss, so no warning here.
                if (clip == null) return;

                // NpcFashionComponent only creates an AudioSource when PeriodicSound or InteractSound is
                // set, so an NPC that only uses this command has none - play positionally instead.
                var audioSource = npcView != null ? npcView.GetComponentInChildren<AudioSource>() : null;
                if (audioSource != null)
                    audioSource.PlayOneShot(clip);
                else
                    AudioSource.PlayClipAtPoint(clip, pos);

                global::FiresRPGmaker.FiresLogger.LogVerbose($"[DialogueCommandEngine] PlaySound '{clipName}' -> {clip.name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueCommandEngine] PlaySound '{clipName}' failed: {ex.Message}");
            }
        }

        private static void Cmd_EnterPassword(ZNetView npcView, string[] parts)
        {
            // EnterPassword,Title,Password,OnSuccessUID,OnFailUID
            // Opens TextInput dialog; transitions based on whether input matches password.
            if (parts.Length < 5) return;
            string title = parts[1].Trim();
            string password = parts[2].Trim();
            string onSuccess = parts[3].Trim().ToLower();
            string onFail = parts[4].Trim().ToLower();

            // Use Valheim's TextInput component to prompt the player
            if (TextInput.instance != null)
            {
                TextInput.instance.RequestText(new TextInputHandler(title, password, onSuccess, onFail, npcView), title, 256);
            }
            else
            {
                Debug.LogWarning("[DialogueCommandEngine] EnterPassword: TextInput.instance is null");
            }
        }

        /// <summary>
        /// Handler for the EnterPassword TextInput callback.
        /// Implements TextReceiver to receive the player's input.
        /// </summary>
        private class TextInputHandler : TextReceiver
        {
            private readonly string _title;
            private readonly string _password;
            private readonly string _onSuccess;
            private readonly string _onFail;
            private readonly ZNetView _npcView;

            public TextInputHandler(string title, string password, string onSuccess, string onFail, ZNetView npcView)
            {
                _title = title;
                _password = password;
                _onSuccess = onSuccess;
                _onFail = onFail;
                _npcView = npcView;
            }

            public string GetText() => "";

            public void SetText(string text)
            {
                bool match = string.Equals(text?.Trim(), _password, StringComparison.OrdinalIgnoreCase);
                string nextUID = match ? _onSuccess : _onFail;
                if (!string.IsNullOrEmpty(nextUID))
                    DialogueUI.TransitionTo(nextUID);
                else
                    DialogueUI.Hide();
            }
        }

        // ── MP-parity implementations ────────────────────────────────────────

        // The Marketplace custom-value store: numeric values in m_customData under this prefix.
        // Characters migrating from MP carry these keys, so the prefix must match exactly.
        private const string MpCustomValuePrefix = "kgMarketplaceValue@";

        internal static int GetMpCustomValue(Player player, string key)
        {
            key = key.Replace("CUSTOMVALUE@", "");
            return player != null && player.m_customData != null
                && player.m_customData.TryGetValue(MpCustomValuePrefix + key, out string s)
                && int.TryParse(s, out int v) ? v : 0;
        }

        private static void Cmd_AddCustomValue(Player player, string[] parts, bool add)
        {
            // AddCustomValue,Key,Amount (adds) / SetCustomValue,Key,Value (0 removes) — MP exact.
            if (parts.Length < 3) return;
            string key = parts[1].Trim().Replace("CUSTOMVALUE@", "");
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            string fullKey = MpCustomValuePrefix + key;

            int value = add ? GetMpCustomValue(player, key) + amount : amount;
            if (value == 0 && !add) player.m_customData.Remove(fullKey);
            else player.m_customData[fullKey] = value.ToString();
        }

        private static bool TryGetBlob(string[] parts, int index, string verb, out CustomSpawnDataStore.Blob blob)
        {
            blob = null;
            if (parts.Length <= index) { Debug.LogWarning($"[DialogueCommandEngine] {verb}: missing CustomSpawnData key"); return false; }
            string key = parts[index].Trim().ToLowerInvariant();
            if (CustomSpawnDataStore.TryGet(key, out blob)) return true;
            Debug.LogWarning($"[DialogueCommandEngine] {verb}: no CustomSpawnData found for key '{key}' " +
                             "(FiresRPGmaker/CustomSpawnData/*.yml or Marketplace/Configs/CustomSpawnData/*.yml)");
            return false;
        }

        private static void Cmd_GiveItemWithData(Player player, string[] parts)
        {
            // GiveItemWithData,Prefab,Amount,Level,DataKey — item lands in the inventory carrying
            // the blob's ZDO payload (decoded through the item's own external-ZDO loader).
            if (parts.Length < 5) return;
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            if (!int.TryParse(parts[3].Trim(), out int level)) level = 1;
            if (!TryGetBlob(parts, 4, "GiveItemWithData", out var blob)) return;

            var prefab = ResolvePrefab(prefabName);
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
            {
                Debug.LogWarning($"[DialogueCommandEngine] GiveItemWithData: not found or not an item: {prefabName}");
                return;
            }

            var inv = player.GetInventory();
            if (inv == null) return;
            for (int i = 0; i < amount; i++)
            {
                var spawned = UnityEngine.Object.Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
                var drop = spawned.GetComponent<ItemDrop>();
                var nview = spawned.GetComponent<ZNetView>();
                if (drop != null && nview != null && nview.IsValid())
                {
                    drop.SetQuality(Mathf.Max(1, level));
                    blob.ApplyTo(nview.GetZDO());
                    drop.LoadFromExternalZDO(nview.GetZDO());
                    var itemData = drop.m_itemData.Clone();
                    itemData.m_stack = 1;
                    if (inv.AddItem(itemData))
                    {
                        if (ZNetScene.instance != null) ZNetScene.instance.Destroy(spawned);
                        else UnityEngine.Object.Destroy(spawned);
                        continue;
                    }
                }
                // Inventory full (or no view): the data-carrying drop stays on the ground.
            }
        }

        private static void Cmd_SetPlayerData(Player player, string[] parts)
        {
            // SetPlayerData,DataKey — applies the named blob directly to the player's ZDO (MP exact).
            if (!TryGetBlob(parts, 1, "SetPlayerData", out var blob)) return;
            var nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            blob.ApplyTo(nview.GetZDO());
        }

        private static void Cmd_SpawnWithData(Player player, string[] parts)
        {
            // SpawnWithData,Prefab,Amount,Level,DataKey — near-player scatter; level/quality set
            // BEFORE the blob (MP ordering: the blob may deliberately override 'level').
            if (parts.Length < 5) return;
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            if (!int.TryParse(parts[3].Trim(), out int level)) level = 1;
            if (!TryGetBlob(parts, 4, "SpawnWithData", out var blob)) return;

            var prefab = ResolvePrefab(prefabName);
            if (prefab == null) { Debug.LogWarning($"[DialogueCommandEngine] SpawnWithData: prefab '{prefabName}' not found"); return; }

            Vector3 basePos = player.transform.position;
            for (int i = 0; i < amount; i++)
            {
                Vector3 pos = new Vector3(basePos.x + Random.Range(-15f, 15f), basePos.y, basePos.z + Random.Range(-15f, 15f));
                if (ZoneSystem.instance != null) ZoneSystem.instance.FindFloor(pos, out pos.y);
                var spawned = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                var nview = spawned.GetComponent<ZNetView>();
                spawned.GetComponent<Character>()?.SetLevel(Mathf.Max(1, level + 1));
                var drop = spawned.GetComponent<ItemDrop>();
                if (nview != null && nview.IsValid())
                {
                    drop?.SetQuality(level);
                    blob.ApplyTo(nview.GetZDO());
                    drop?.LoadFromExternalZDO(nview.GetZDO());
                }
            }
        }

        private static void Cmd_SpawnXYZWithData(string[] parts)
        {
            // SpawnXYZWithData,Prefab,Amount,Level,X,Y,Z,MaxDist,DataKey — blob FIRST, then
            // level/quality (MP's ordering differs from SpawnWithData; kept faithfully).
            if (parts.Length < 9) { Debug.LogWarning("[DialogueCommandEngine] SpawnXYZWithData format: Prefab,Amount,Level,X,Y,Z,MaxDist,DataKey"); return; }
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int amount)) return;
            if (!int.TryParse(parts[3].Trim(), out int level)) level = 1;
            if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return;
            if (!float.TryParse(parts[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;
            if (!int.TryParse(parts[7].Trim(), out int maxDist)) maxDist = 10;
            if (!TryGetBlob(parts, 8, "SpawnXYZWithData", out var blob)) return;

            var prefab = ResolvePrefab(prefabName);
            if (prefab == null) { Debug.LogWarning($"[DialogueCommandEngine] SpawnXYZWithData: prefab '{prefabName}' not found"); return; }

            for (int i = 0; i < amount; i++)
            {
                Vector3 pos = new Vector3(x + Random.Range(-maxDist, maxDist), y, z + Random.Range(-maxDist, maxDist));
                var spawned = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                var nview = spawned.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid()) blob.ApplyTo(nview.GetZDO());
                spawned.GetComponent<Character>()?.SetLevel(Mathf.Max(1, level + 1));
                var drop = spawned.GetComponent<ItemDrop>();
                if (drop != null)
                {
                    drop.SetQuality(level);
                    if (nview != null && nview.IsValid()) drop.LoadFromExternalZDO(nview.GetZDO());
                }
            }
        }

        private static void Cmd_PlayVideo(string[] parts)
        {
            // PlayVideo,Url[,Volume0-100] — fullscreen in-game video, Esc closes (MP parity).
            if (parts.Length < 2) return;
            string url = parts[1].Trim();
            float volume = parts.Length > 2 && int.TryParse(parts[2].Trim(), out int v) ? v / 100f : 0.5f;
            DialogueVideoPlayer.Play(url, volume);
        }

        private static void Cmd_SetNpcModel(ZNetView npcView, string[] parts, bool global)
        {
            // SetNPCModelLocal/Global,Prefab — Local changes only this client's view (until the NPC
            // reloads); Global persists via the NPC's ZDO and syncs to everyone.
            if (parts.Length < 2 || npcView == null) return;
            string prefabName = parts[1].Trim();
            if (ResolvePrefab(prefabName) == null)
            {
                Debug.LogWarning($"[DialogueCommandEngine] SetNPCModel: prefab '{prefabName}' not found");
                return;
            }
            var npc = npcView.GetComponent<NpcController>();
            if (npc == null) return;
            npc.modelOverride = prefabName;              // controller live-applies on change
            if (global) npc.SaveToZDO();
            DialogueConditionEngine.CurrentNpcModel = prefabName;   // same-conversation NPCModelEquals sees it
        }

        private static void Cmd_SetNpcName(ZNetView npcView, string[] parts, bool global)
        {
            // SetNPCNameLocal/Global,Name — underscores become spaces (MP convention, since commas
            // and colons are separators).
            if (parts.Length < 2 || npcView == null) return;
            string name = parts[1].Trim().Replace("_", " ");
            var npc = npcView.GetComponent<NpcController>();
            if (npc == null) return;
            npc.displayNameOverride = name;
            try { FiresCore.Bridge.NpcCompanionBridge.RaiseSetDisplayName(npc.gameObject, name); } catch { }
            if (global) npc.SaveToZDO();
            DialogueConditionEngine.CurrentNpcName = name;
        }

        private static void Cmd_SetNpcPatrol(ZNetView npcView, string[] parts)
        {
            // SetNPCPatrol,RouteName[,SpeedPreset] — assigns an EXISTING Fires patrol route (authored
            // via the patrol editor); replaces MP's inline-waypoint payload.
            if (parts.Length < 2 || npcView == null) return;
            string route = parts[1].Trim();
            string preset = parts.Length > 2 ? parts[2].Trim() : "";
            var npc = npcView.GetComponent<NpcController>();
            if (npc == null) return;
            if (global::FiresCore.Npc.Patrol.PatrolRouteManager.GetRoute(route) == null)
            {
                Debug.LogWarning($"[DialogueCommandEngine] SetNPCPatrol: route '{route}' not found");
                return;
            }
            global::FiresRPGmaker.UI.PatrolRouteSync.AssignExisting(route, preset, npc);
        }

        private static void Cmd_SendWebhook(Player player, string[] parts)
        {
            // SendWebhook,WebhookURL,"Message"
            // URL is provided without https:// prefix (Marketplace convention).
            if (parts.Length < 3) return;
            string url = parts[1].Trim();
            string message = string.Join(",", parts.Skip(2).Select(p => p.Trim()));
            message = message.Replace("{playername}", player.GetPlayerName());
            message = ApplyReplacements(message);

            PostWebhook(url, message);
        }

        /// <summary>Fire-and-forget Discord webhook POST (shared by the SendWebhook verb + the Feedback
        /// module). Accepts a URL with or without the https:// prefix.</summary>
        public static void PostWebhook(string url, string message)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
            try
            {
                string json = $"{{\"content\":\"{EscapeJsonString(message)}\"}}";
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 5000;
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                request.ContentLength = data.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(data, 0, data.Length);
                request.GetResponse().Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueCommandEngine] webhook POST failed: {ex.Message}");
            }
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        #endregion
    }
}
