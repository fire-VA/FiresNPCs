using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Progression
{
    /// <summary>
    /// Harmony patches for the private key progression system.
    ///
    /// Core behaviour:
    ///  - Boss kill keys are blocked from the global list and sent to nearby players as private keys
    ///  - GetGlobalKey returns private key state when private keys are enabled
    ///  - Raids use per-player private keys instead of global keys
    ///  - Player save/load persists private keys in m_customData
    ///  - Equipment blocking only applies to cheat items by default
    ///
    /// All locking features (craft, build, cook, eat, tame, guardian, summon) default to OFF.
    /// Server admins can enable them in config if desired.
    /// </summary>
    [HarmonyPatch]
    public static class ProgressionPatches
    {
        // ==============================================================
        // Global Key Interception - redirect boss keys to private
        // ==============================================================

        [HarmonyPatch(typeof(ZoneSystem), "SetGlobalKey", new[] { typeof(string) })]
        [HarmonyPriority(Priority.High)]
        public static class Patch_ZoneSystem_SetGlobalKey
        {
            static bool Prefix(string name)
            {
                string key = name.ToLower();
                if (PrivateKeyManager.ShouldBlockGlobalKey(key))
                {
                    Debug.Log($"[Progression] Blocking global key '{key}' - redirecting to private keys");
                    return false; // prevent global key from being set
                }
                return true;
            }

            static void Postfix(string name, bool __runOriginal)
            {
                string key = name.ToLower();
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateKeys) return;
                if (PrivateKeyManager.ShouldBlockPrivateKey(key)) return;

                // Send private key to all nearby players
                if (Player.m_localPlayer != null)
                {
                    var players = new List<Player>();
                    Player.GetPlayersInRange(Player.m_localPlayer.transform.position, 100f, players);

                    foreach (var p in players)
                    {
                        string pName = p.GetPlayerName();
                        if (!string.IsNullOrEmpty(pName))
                        {
                            Debug.Log($"[Progression] Sending private key '{key}' to player '{pName}'");
                            PrivateKeyManager.SendKeyToPlayer(pName, key);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), "RPC_SetGlobalKey")]
        [HarmonyPriority(Priority.High)]
        public static class Patch_ZoneSystem_RPC_SetGlobalKey
        {
            static bool Prefix(string name)
            {
                string key = name.ToLower();
                if (PrivateKeyManager.ShouldBlockGlobalKey(key))
                {
                    // Re-broadcast cleaned global keys so clients don't get stale data
                    ZoneSystem.instance.SendGlobalKeys(ZRoutedRpc.Everybody);
                    return false;
                }
                return true;
            }
        }

        // ==============================================================
        // GetGlobalKey - return private key state when enabled
        // ==============================================================

        [HarmonyPatch(typeof(ZoneSystem), "GetGlobalKey", new[] { typeof(string) })]
        public static class Patch_ZoneSystem_GetGlobalKey
        {
            static bool Prefix(string name, ref bool __result)
            {
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateKeys) return true;
                if (ZNet.instance != null && ZNet.instance.IsDedicated()) return true;

                string key = name.ToLower();
                if (PrivateKeyManager.IsWorldModifier(key)) return true;

                // Check private keys first
                if (PrivateKeyManager.HasPrivateKey(key))
                {
                    __result = true;
                    return false;
                }

                return true; // fall through to vanilla global key check
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), "GetGlobalKeys")]
        public static class Patch_ZoneSystem_GetGlobalKeys
        {
            static void Postfix(ref List<string> __result)
            {
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateKeys) return;
                if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;

                // Merge private keys into the result so UI and other systems see them
                var merged = new HashSet<string>(__result, StringComparer.OrdinalIgnoreCase);
                foreach (var pk in PrivateKeyManager.PrivateKeys)
                    merged.Add(pk);
                __result = new List<string>(merged);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), "RPC_RemoveGlobalKey")]
        public static class Patch_ZoneSystem_RPC_RemoveGlobalKey
        {
            static void Postfix(string name)
            {
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateKeys) return;
                if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;

                PrivateKeyManager.RemovePrivateKey(name.ToLower());
            }
        }

        // ==============================================================
        // Player Save / Load
        // ==============================================================

        [HarmonyPatch(typeof(Player), "Save")]
        public static class Patch_Player_Save
        {
            static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                PrivateKeyManager.SaveToPlayer(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), "EquipInventoryItems")]
        public static class Patch_Player_EquipInventoryItems
        {
            static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                if (!IsInMainScene()) return;

                Debug.Log("[Progression] Loading private keys for player");
                PrivateKeyManager.LoadFromPlayer(__instance);
                PrivateKeyManager.RegisterRPCs();
                Dialogues.EwpDataBridge.RegisterRPCs();

                // Sync keys to server
                if (ZRoutedRpc.instance != null)
                {
                    long playerId = __instance.GetPlayerID();
                    string keysStr = string.Join(",", PrivateKeyManager.PrivateKeys);
                    ZRoutedRpc.instance.InvokeRoutedRPC(PrivateKeyManager.RPC_ServerSetKeys, keysStr, playerId);
                }
            }
        }

        // ==============================================================
        // Server Init - clean global keys + register RPCs
        // ==============================================================

        [HarmonyPatch(typeof(ZoneSystem), "Load")]
        public static class Patch_ZoneSystem_Load
        {
            static void Postfix()
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                Debug.Log("[Progression] Server init: cleaning global keys and registering RPCs");

                var cfg = ProgressionConfig.Instance;
                if (cfg == null) return;

                // Remove blocked global keys
                var toRemove = new List<string>();
                foreach (var key in ZoneSystem.instance.m_globalKeys)
                {
                    if (PrivateKeyManager.ShouldBlockGlobalKey(key))
                        toRemove.Add(key);
                }
                foreach (var key in toRemove)
                    ZoneSystem.instance.m_globalKeys.Remove(key);

                // Add enforced global keys
                foreach (var key in cfg.EnforcedGlobalKeys)
                    ZoneSystem.instance.m_globalKeys.Add(key);

                // Enable player events for private raids
                if (cfg.UsePrivateRaids && cfg.UsePrivateKeys)
                    ZoneSystem.instance.m_globalKeysEnums.Add(GlobalKeys.PlayerEvents);

                PrivateKeyManager.RegisterRPCs();
                Dialogues.EwpDataBridge.RegisterRPCs();
            }
        }

        // ==============================================================
        // Private Raids - per-player event data
        // ==============================================================

        [HarmonyPatch(typeof(RandEventSystem), "RefreshPlayerEventData")]
        public static class Patch_RandEventSystem_RefreshPlayerEventData
        {
            static bool Prefix()
            {
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateRaids || !cfg.UsePrivateKeys) return true;

                RandEventSystem.s_randomEventNeedsRefresh = false;
                RandEventSystem.s_playerEventDatas.Clear();

                // Host player
                if (!ZNet.instance.IsDedicated() && Player.m_localPlayer != null)
                {
                    RandEventSystem.s_playerEventDatas.Add(BuildPlayerEventData(
                        ZNet.instance.GetReferencePosition(),
                        PrivateKeyManager.PrivateKeys,
                        Player.m_localPlayer.m_nview.GetZDO().GetInt(ZDOVars.s_baseValue)
                    ));
                }

                // Remote players
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    if (!peer.IsReady()) continue;

                    // Get persistent player ID from ZDO
                    long persistentId = 0L;
                    var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                    if (zdo != null) persistentId = zdo.GetLong(ZDOVars.s_playerID);

                    var playerKeys = PrivateKeyManager.GetServerKeysForPlayer(persistentId);
                    int baseValue = 0;
                    if (peer.m_serverSyncedPlayerData.TryGetValue("baseValue", out string bvStr))
                        int.TryParse(bvStr, out baseValue);

                    RandEventSystem.s_playerEventDatas.Add(BuildPlayerEventData(
                        peer.m_refPos, playerKeys, baseValue
                    ));
                }

                return false; // skip vanilla
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), "HaveGlobalKeys")]
        public static class Patch_RandEventSystem_HaveGlobalKeys
        {
            static bool Prefix(ref bool __result)
            {
                var cfg = ProgressionConfig.Instance;
                if (cfg == null || !cfg.UsePrivateRaids || !cfg.UsePrivateKeys) return true;

                // Always return true - event filtering is done per-player in RefreshPlayerEventData
                __result = true;
                return false;
            }
        }

        // ==============================================================
        // Equipment Blocking - only cheat items by default
        // ==============================================================

        [HarmonyPatch(typeof(Humanoid), "EquipItem")]
        [HarmonyPriority(Priority.High)]
        public static class Patch_Humanoid_EquipItem
        {
            static bool Prefix(Humanoid __instance, ref bool __result, ItemDrop.ItemData item)
            {
                if (item == null) return true;
                if ((UnityEngine.Object)__instance != (UnityEngine.Object)Player.m_localPlayer) return true;
                if (IsAdminBypassing()) return true;

                // Always block cheat items
                string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                if (PrivateKeyManager.IsItemBlocked(prefabName))
                {
                    ShowBlockedMessage();
                    __result = false;
                    return false;
                }

                // Check key-based equipment lock
                var cfg = ProgressionConfig.Instance;
                if (cfg != null && cfg.IsLockedByKey(cfg.LockEquipment, cfg.LockEquipmentKey))
                {
                    ShowBlockedMessage();
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        // ==============================================================
        // Helpers
        // ==============================================================

        private static bool IsInMainScene()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Equals("main");
        }

        private static bool IsAdminBypassing()
        {
            var cfg = ProgressionConfig.Instance;
            if (cfg == null || !cfg.AdminBypass) return false;
            return FiresCore.Sync.AdminSyncing.IsLocalAdmin();
        }

        private static void ShowBlockedMessage()
        {
            var cfg = ProgressionConfig.Instance;
            if (cfg == null) return;

            if (cfg.UseBlockedEffect && Player.m_localPlayer != null)
                Player.m_localPlayer.GetSEMan()?.AddStatusEffect("Burning".GetStableHashCode());

            if (cfg.UseBlockedMessage)
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, cfg.BlockedMessage);
        }

        private static RandEventSystem.PlayerEventData BuildPlayerEventData(
            Vector3 position, HashSet<string> keys, int baseValue)
        {
            var possibleEvents = new HashSet<string>();
            if (RandEventSystem.instance != null)
            {
                foreach (var re in RandEventSystem.instance.m_events)
                {
                    if (IsValidEventForPlayer(re, keys))
                        possibleEvents.Add(re.m_name);
                }
            }

            return new RandEventSystem.PlayerEventData
            {
                position = position,
                possibleEvents = possibleEvents,
                baseValue = baseValue
            };
        }

        private static bool IsValidEventForPlayer(RandomEvent re, HashSet<string> playerKeys)
        {
            // Mirror vanilla RandEventSystem.HaveGlobalKeys' either/or: when an event carries any
            // of the alt (PlayerEvents-mode) gating lists, its eligibility is decided ENTIRELY by
            // those per-player conditions; otherwise it falls back to the required/not-required
            // global-key lists. The original implementation only checked the global-key lists, so
            // any event gated the correct player-based way (alt player keys) — or a modded event
            // with no required global keys — passed for every player regardless of progression,
            // firing raids like the Mistlands dvergr ambush minutes into a fresh character.
            bool hasAltGating =
                re.m_altRequiredKnownItems.Count > 0 ||
                re.m_altRequiredNotKnownItems.Count > 0 ||
                re.m_altNotRequiredPlayerKeys.Count > 0 ||
                re.m_altRequiredPlayerKeysAny.Count > 0 ||
                re.m_altRequiredPlayerKeysAll.Count > 0;

            if (hasAltGating)
                return PlayerReadyByKeys(re, playerKeys);

            foreach (var req in re.m_requiredGlobalKeys)
            {
                if (!playerKeys.Contains(req.ToLower()))
                    return false;
            }
            foreach (var notReq in re.m_notRequiredGlobalKeys)
            {
                if (playerKeys.Contains(notReq.ToLower()))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Server-side mirror of vanilla RandEventSystem.PlayerIsReadyForEvent, evaluated against a
        /// player's private key set instead of a live Player. Known-item conditions
        /// (m_altRequiredKnownItems / m_altRequiredNotKnownItems) can't be resolved from keys alone
        /// on the server, so they neither auto-qualify nor disqualify — the player-key lists decide.
        /// </summary>
        private static bool PlayerReadyByKeys(RandomEvent re, HashSet<string> playerKeys)
        {
            foreach (var notReqKey in re.m_altNotRequiredPlayerKeys)
            {
                if (playerKeys.Contains(notReqKey.ToLower()))
                    return false;
            }
            foreach (var anyKey in re.m_altRequiredPlayerKeysAny)
            {
                if (playerKeys.Contains(anyKey.ToLower()))
                    return true;
            }
            foreach (var allKey in re.m_altRequiredPlayerKeysAll)
            {
                if (!playerKeys.Contains(allKey.ToLower()))
                    return false;
            }
            return re.m_altRequiredKnownItems.Count <= 0 && re.m_altRequiredPlayerKeysAny.Count <= 0;
        }
    }
}
