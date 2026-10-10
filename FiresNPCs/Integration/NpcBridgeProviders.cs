using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using FiresCore.Bridge;
using FiresCore.Npc;
using FiresCore.Npc.NpcMode;
using FiresCore.Npc.Archetypes;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Companions;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Compendium;
using FiresRPGmaker.Modules.Territories;
using FiresRPGmaker.NpcFashion;

namespace FiresRPGmaker.Integration
{
    /// <summary>
    /// Wires the Core NPC engine's host-service seams (FiresCore.Bridge.*) to their RPGMaker
    /// implementations. Core's companion/NPC cluster calls these bridges; while unregistered each is
    /// a null-safe no-op. RegisterAll() runs once at plugin init, before any companion patch fires.
    /// </summary>
    internal static class NpcBridgeProviders
    {
        internal static void RegisterAll()
        {
            // config values + admin check (key = ConfigManager field name minus the "config" prefix)
            NpcConfigBridge.GetFloatValue = (key, fallback) => GetConfigValue(key, fallback);
            NpcConfigBridge.GetBoolValue = (key, fallback) => GetConfigValue(key, fallback);
            NpcConfigBridge.AdminCheck = () => FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false;

            // anchor⇄body payload copier (appearance + module profiles + config) for the NPC anchor system
            NpcAnchorDataBridge.Register();

            // anchor orb context menu (Move/Remove) — client-side UI, admin-gated rows + server-gated RPCs
            if (!UnityEngine.Application.isBatchMode)
            {
                FiresCore.UI.ContextMenu.FiresContextMenu.Register(new UI.ContextMenu.NpcAnchorContextProvider());
                // static NPC BODY menu (pick up / show anchor / show route / edit) — same driver, same gating
                FiresCore.UI.ContextMenu.FiresContextMenu.Register(new UI.ContextMenu.NpcBodyContextProvider());
            }

            // companion prefab bundle (ships in the host mod)
            NpcAssetBridge.LoadPrefab = path =>
                FiresRPGmaker.assetBundle != null ? FiresRPGmaker.assetBundle.LoadAsset<GameObject>(path) : null;

            // hair / beard appearance through the fashion system
            NpcFashionBridge.ColorToStringFn = NpcFashionManager.ColorToString;
            NpcFashionBridge.ApplyHairFn = (npc, style, color) => NpcFashionManager.ApplyHairToNpc(npc, style, color);
            NpcFashionBridge.ApplyBeardFn = (npc, style, color) => NpcFashionManager.ApplyBeardToNpc(npc, style, color);

            // read-only probes against the host interaction component (NpcController)
            NpcInteractionBridge.HasController = npc => npc.GetComponent<NpcController>() != null;
            NpcInteractionBridge.ModelOverridePending = npc => npc.GetComponent<NpcController>()?.HasPendingModelOverride ?? false;
            NpcInteractionBridge.HoverText = npc => npc.GetComponent<NpcController>()?.GetHoverText();
            NpcInteractionBridge.StaticInteract = (npc, user, hold, alt) =>
            {
                var controller = npc.GetComponent<NpcController>();
                return controller != null ? controller.Interact(user, hold, alt) : (bool?)null;
            };

            // stationed-NPC integrated mode: admin config panel, quest/dialogue/info/trader routing, territory bounds
            NpcModeBridge.OpenConfigPanel = OpenConfigPanel;
            NpcModeBridge.Interaction = HandleInteraction;
            NpcModeBridge.TerritoryRadiusAt = pos =>
            {
                var territory = TerritorySyncedState.GetCurrent(pos);
                return territory != null ? (float?)territory.Radius : null;
            };
            NpcModeBridge.TerritoryNameAt = pos => TerritorySyncedState.GetCurrent(pos)?.Name;

            // Companion inventory/stats UI bridges removed (RPGMaker decouple) — companion gear/stat
            // screens belong to FiresCompanions. Static NPCs use the Shift+E admin panel + Dressing
            // Room, not these. Leaving the NpcUiBridge delegates unset (null-safe in Core).

            // COMPANION STORE DE-REGISTERED (RPGMaker decouple, 2026-06-14): FiresRPGmaker no longer
            // registers its VaultOfKnowledge as the companion persistence provider. Companion ownership
            // now lives entirely in FiresCompanions' kennel (the NpcDormancyBridge provider). While the
            // RPGMaker vault registered CompanionVaultBridge.IsAvailable=true, Core's single restore
            // engine preferred it and the kennel auto-deferred ("vault provider present"); removing the
            // store delegates lets the kennel become the sole companion manager. The VaultOfKnowledge
            // companion methods are now dead and get removed in the full companion-frontend strip.
            //
            // KEPT — these are store-AGNOSTIC engine signals the restore engine needs for EITHER store
            // (config-sync gate + "player joined" trigger), not vault-store ops. RPGMaker owns the
            // AnnouncePlayerInfo RPC that raises PlayerAnnouncedToServer, so these stay here.
            CompanionVaultBridge.RestoreReadyForPeer = ServerConfigFileWatcher.HasCompletedConfigSync;
            CompanionVaultBridge.ClearRestoreGate = ServerConfigFileWatcher.ClearConfigSyncComplete;

            // host server lifecycle (saved-NPC routed RPCs + config-file push to a joining peer)
            NpcHostBridge.RegisterServerRpcsFn = SavedNpcManager.RegisterRPCs;
            NpcHostBridge.ResetServerRpcsFn = SavedNpcManager.ResetRPCState;
            NpcHostBridge.PushConfigsToClientFn = ServerConfigFileWatcher.SendAllConfigsToClient;

            // companion-progression events → Compendium discovery tracker
            CompanionEventBridge.CompanionRecruited = CompendiumDiscoveryTracker.OnCompanionRecruited;
            CompanionEventBridge.ArchetypeSeen = (player, archetype) =>
                CompendiumDiscoveryTracker.OnArchetypeSeen(player, (ArchetypeClass)archetype);
            CompanionEventBridge.HybridUnlocked = (player, main, sub) =>
                CompendiumDiscoveryTracker.OnHybridUnlocked(player, (ArchetypeClass)main, (ArchetypeClass)sub);

            // eye emission texture (cached during prefab build) + teleport-suppression gate
            NpcCompanionBridge.EyeEmissionTexture = CompanionPrefabManager.GetCachedEyeEmissionTexture;
            CompanionBridge.TeleportsSuppressed = CompanionPatches.AreCompanionTeleportsSuppressed;

            // Live billboard / EnemyHud name update when an NPC is renamed in the UI. This delegate was never
            // assigned, so RaiseSetDisplayName was a silent no-op — the reason renaming never changed the
            // floating name. CompanionController.UpdateCharacterName writes ZDOVars.s_tamedName, the field the
            // tamed-NPC nameplate actually reads.
            NpcCompanionBridge.SetDisplayName = (npc, name) =>
                npc.GetComponent<CompanionController>()?.UpdateCharacterName(name);
        }

        private static T GetConfigValue<T>(string key, T fallback)
        {
            try
            {
                var field = typeof(ConfigManager).GetField("config" + key);
                if (field == null) return fallback;
                return field.GetValue(ConfigManager.Instance) is ConfigEntry<T> entry ? entry.Value : fallback;
            }
            catch { return fallback; }
        }

        // ── stationed-NPC interaction glue (lifted from the pre-Core CompanionNpcModule) ──
        // Core's CompanionNpcModule holds the NPC data (npcType, profile strings) and routes the
        // host-specific actions here through NpcModeBridge.

        private static void OpenConfigPanel(GameObject npc)
        {
            var module = npc.GetComponent<CompanionNpcModule>();
            if (module == null) return;
            var controller = GetOrCreateNpcControllerWrapper(module);
            if (controller != null) global::FiresRPGmaker.UI.NpcBookController.ShowForNpc(controller);   // book UI; old flat panel retired
        }

        private static bool HandleInteraction(GameObject npc, Player player)
        {
            var module = npc.GetComponent<CompanionNpcModule>();
            if (module == null) return false;
            switch (module.npcType)
            {
                case NpcType.QuestNpc: HandleQuest(module); return true;
                case NpcType.InfoNpc: HandleInfo(module); return true;
                case NpcType.DialogueNpc: HandleDialogue(module); return true;
                case NpcType.Trader: HandleTrader(module); return true;
                default: return false;
            }
        }

        private static NpcController GetOrCreateNpcControllerWrapper(CompanionNpcModule module)
        {
            var existing = module.GetComponent<NpcController>();
            if (existing != null) { SyncToNpcController(module, existing); return existing; }
            var created = module.gameObject.AddComponent<NpcController>();
            SyncToNpcController(module, created);
            return created;
        }

        private static void SyncToNpcController(CompanionNpcModule module, NpcController npc)
        {
            npc.npcProfileName = module.DisplayName;
            npc.displayNameOverride = module.npcDisplayName;
            npc.npcType = module.npcType;
            npc.dialogueProfile = module.dialogueProfile;
            npc.questProfile = module.questProfile;
            npc.infoProfile = module.infoProfile;
            npc.traderProfile = module.traderProfile;

            var fashionComp = module.GetComponent<NpcFashionComponent>();
            if (fashionComp != null)
            {
                if (!string.IsNullOrEmpty(module.greetAnimation)) fashionComp.GreetAnimation = module.greetAnimation;
                if (!string.IsNullOrEmpty(module.byeAnimation)) fashionComp.ByeAnimation = module.byeAnimation;
            }

            var inventory = module.GetComponent<CompanionInventory>();
            var equipment = inventory?.GetEquipmentDataForVisuals();
            if (equipment != null)
            {
                foreach (var kvp in equipment)
                {
                    if (string.IsNullOrEmpty(kvp.Value)) continue;
                    switch (kvp.Key)
                    {
                        case CompanionInventory.EquipmentSlot.Helmet: npc.equipHelmet = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.Chest: npc.equipChest = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.Legs: npc.equipLegs = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.Shoulder: npc.equipShoulder = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.Utility: npc.equipUtility = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.RightHand: npc.equipRightHand = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.LeftHand: npc.equipLeftHand = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.RightBack: npc.equipRightBack = kvp.Value; break;
                        case CompanionInventory.EquipmentSlot.LeftBack: npc.equipLeftBack = kvp.Value; break;
                    }
                }
            }
        }

        private static void HandleQuest(CompanionNpcModule module)
        {
            if (!string.IsNullOrEmpty(module.questProfile))
                QuestInteractionScreenController.ShowForNpc(GetOrCreateNpcControllerWrapper(module));
            else
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{module.DisplayName} has no quests available.");
        }

        private static void HandleInfo(CompanionNpcModule module)
        {
            if (!string.IsNullOrEmpty(module.infoProfile))
            {
                var profile = InfoNpcScreenController.GetProfileForName(module.infoProfile);
                InfoNpcPlayerPanel.Show(module.DisplayName, profile);
            }
            else
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{module.DisplayName} has nothing to say.");
            }
        }

        private static void HandleDialogue(CompanionNpcModule module)
        {
            if (!string.IsNullOrEmpty(module.dialogueProfile))
            {
                DialogueCodex.Init();
                string uid = module.dialogueProfile.Trim().ToLowerInvariant();
                if (DialogueCodex.Get(uid) != null)
                {
                    DialogueUI.Show(module.DisplayName, uid, module.GetComponent<ZNetView>());
                    return;
                }
                var lines = DialogueManager.GetDialogueLinesForProfile(module.dialogueProfile);
                if (lines != null && lines.Count > 0)
                {
                    var profile = DialogueManager.GetProfile(module.dialogueProfile);
                    if (!string.IsNullOrEmpty(profile?.DefaultGreeting))
                    {
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, profile.DefaultGreeting);
                        return;
                    }
                }
                // Name the broken link instead of failing silently: is the codex empty (sync problem)
                // or does it just not contain this uid (data problem)?
                Debug.LogWarning($"[NpcBridge] '{module.DisplayName}' dialogue '{module.dialogueProfile}' NOT FOUND - DialogueCodex holds {DialogueCodex.Dialogues.Count} dialogue(s) on this machine");
            }
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{module.DisplayName} has nothing to say.");
        }

        private static void HandleTrader(CompanionNpcModule module)
        {
            TraderUIController.ShowForCompanion(module.DisplayName, module.traderProfile);
        }
    }
}
