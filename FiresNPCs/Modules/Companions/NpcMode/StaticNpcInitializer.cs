using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.Managers;
using FiresCore.Npc;
using FiresCore.Npc.AI;
using FiresCore.Npc.NpcMode;

namespace FiresRPGmaker.Modules.Companions.NpcMode
{
    /// <summary>
    /// Auto-initializes a hammer-placed static NPC.
    /// 
    /// When a "StaticNpc" prefab is placed via the hammer, this component runs on Start()
    /// and sets up the NPC as a stationed quest NPC at its placed position. This bypasses
    /// the normal companion pipeline (spawn companion ? claim ? Shift+P to station).
    /// 
    /// On first placement, the NPC gets randomized appearance and equipment matching
    /// the same style as companion NPCs (random Viking name, gender, hair, skin, eye
    /// color, scale, armor, and weapons appropriate for the biome).
    /// 
    /// Static NPCs are ownerless and server-authoritative. Their configuration lives
    /// in the SavedNPCs folder on the server. The ZDO "npc_initialized" flag prevents
    /// re-running first-time setup on subsequent loads.
    /// 
    /// Any connected player can read and display the NPC; only admins can edit via Shift+E.
    /// </summary>
    public class StaticNpcInitializer : MonoBehaviour
    {
        private ZNetView _nview;
        private Quaternion _placementRotation;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();

            // Read the placement rotation from the ZDO immediately before anything can overwrite it.
            _placementRotation = Quaternion.identity;
            if (_nview != null && _nview.IsValid())
            {
                var zdo = _nview.GetZDO();
                if (zdo != null)
                    _placementRotation = zdo.GetRotation();
            }

            // CRITICAL: Disable all components that fight to control rotation/position.
            // Character.UpdateRotation, BaseAI.UpdateAI, and ZSyncTransform all reset
            // rotation to identity (north-facing) before our DelayedInit has a chance to run.
            // We disable them now and selectively re-enable after setup is complete.
            var monsterAI = GetComponent<MonsterAI>();
            if (monsterAI != null) monsterAI.enabled = false;

            var humanoid = GetComponent<Humanoid>();
            if (humanoid != null) humanoid.enabled = false;

            var syncTransform = GetComponent<ZSyncTransform>();
            if (syncTransform != null) syncTransform.m_syncRotation = false;

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            // Apply the correct rotation immediately
            transform.rotation = _placementRotation;
        }

        private void Start()
        {
            StartCoroutine(DelayedInit());
        }

        private IEnumerator DelayedInit()
        {
            // Wait for ZNetScene and networking to be ready
            while (ZNetScene.instance == null)
                yield return null;

            while (ObjectDB.instance == null)
                yield return null;

            // If connecting to a server, wait for connection
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                float timeout = 10f;
                float elapsed = 0f;
                while (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            // Small delay to let ZDO sync and SavedNpcManager data arrive from server
            // Wait for ZDO sync. Clients poll npc_initialized for up to 10 seconds so a
            // re-logging client never reads a stale ZDO and silently skips the restore path.
            {
                bool isServerLocal = ZNet.instance != null && ZNet.instance.IsServer();
                if (!isServerLocal)
                {
                    // Initial pause to let the first ZDO batch arrive
                    yield return new WaitForSeconds(1f);
                    float waited = 1f;
                    const float pollInterval = 0.25f;
                    const float maxWait = 10f;

                    while (waited < maxWait)
                    {
                        if (_nview == null || !_nview.IsValid()) break;
                        var z = _nview.GetZDO();
                        if (z != null && z.GetBool("npc_initialized", false)) break;
                        yield return new WaitForSeconds(pollInterval);
                        waited += pollInterval;
                    }
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }
            }

            if (_nview == null || !_nview.IsValid())
                yield break;

            var zdo = _nview.GetZDO();
            if (zdo == null)
                yield break;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            // Check if already initialized (from a previous session load)
            bool alreadyInitialized = zdo.GetBool("npc_initialized", false);
            if (alreadyInitialized)
            {
                // Already set up â€” restore appearance from ZDO on every machine
                Debug.Log($"[StaticNpcInitializer] {gameObject.name} already initialized, restoring appearance");
                EnsureCompanionNpcComponents(zdo);
                StartCoroutine(RestoreAppearanceFromZDO(zdo));
                yield break;
            }

            // CRITICAL: Always set up the companion NPC component stack on EVERY machine
            // (client and server). Without this, clients never get CompanionNpcModule,
            // isStationedAsNpc is never true, and interaction/hover/wandering all break.
            EnsureCompanionNpcComponents(zdo);

            // Non-server clients: components are set up but ZDO writes and first-time
            // randomization belong to the server. Restore whatever data is already in
            // the ZDO (may be partial on brand-new placement â€” server will write it shortly).
            if (!isServer)
            {
                StartCoroutine(RestoreAppearanceFromZDO(zdo));
                yield break;
            }

            // Mark as initialized so we don't re-run on future loads
            zdo.Set("npc_initialized", true);

            // Generate random appearance and equipment
            RandomizeNpc(zdo);

            // Configure the NpcController as a stationed quest NPC
            var npcController = GetComponent<NpcController>();
            if (npcController != null)
            {
                npcController.npcProfileName = "StaticNpc";
                npcController.npcType = NpcType.QuestNpc;
                npcController.SaveToZDO();

                // Load any previously saved data for this position
                // (admin may have pre-configured via SavedNpcManager)
                SavedNpcManager.LoadIntoNpc(npcController);

                // Re-save so the loaded data (or defaults) are persisted in both
                // the ZDO and the server-side SavedNPCs file
                npcController.SaveToZDO();
            }

            Debug.Log($"[StaticNpcInitializer] Initialized stationed quest NPC at {transform.position}");

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                "Static Quest NPC placed. Use Shift+E to configure.");
        }

        /// <summary>
        /// Ensures the static NPC's companion components are configured as a pre-stationed NPC.
        /// The prefab already has the full companion component stack (Humanoid, CompanionAI,
        /// CompanionController, CompanionIdleBehavior, etc.) from SetupCompanionPrefab.
        /// This method configures the runtime instance so it behaves like a companion that
        /// was stationed via Shift+P, with the sole restriction that it cannot be unstated.
        /// </summary>
        private void EnsureCompanionNpcComponents(ZDO zdo)
        {
            // --- CompanionController: mark as tamed so idle behaviors treat it like a companion ---
            var companion = GetComponent<CompanionController>();
            if (companion != null)
            {
                companion.isTamed = true;
                var npcController = GetComponent<NpcController>();
                if (npcController != null && !string.IsNullOrEmpty(npcController.displayNameOverride))
                    companion.companionName = npcController.displayNameOverride;
            }

            // --- CompanionAI: put into stay mode at current position ---
            var companionAI = GetComponent<CompanionAI>();
            if (companionAI != null)
            {
                companionAI.SetStayPosition(transform.position);
            }

            // --- CompanionIdleBehavior: MUST be added BEFORE CompanionNpcModule so that
            // CompanionNpcModule.Awake() can find it via GetComponent ---
            var idleBehavior = GetComponent<CompanionIdleBehavior>();
            if (idleBehavior == null)
            {
                idleBehavior = gameObject.AddComponent<CompanionIdleBehavior>();
                Debug.Log($"[StaticNpcInitializer] Added CompanionIdleBehavior to {gameObject.name}");
            }

            // --- CompanionNpcModule: configure as static stationed NPC ---
            var npcModule = GetComponent<CompanionNpcModule>();
            if (npcModule == null)
            {
                npcModule = gameObject.AddComponent<CompanionNpcModule>();
                Debug.Log($"[StaticNpcInitializer] Added CompanionNpcModule to {gameObject.name}");
            }

            npcModule.isStationedAsNpc = true;
            npcModule.isStaticPlacement = true;

            // Sync NPC identity from NpcController
            var npcCtrl = GetComponent<NpcController>();
            if (npcCtrl != null)
            {
                npcModule.npcDisplayName = npcCtrl.displayNameOverride;
                npcModule.npcType = npcCtrl.npcType;
                npcModule.dialogueProfile = npcCtrl.dialogueProfile;
                npcModule.questProfile = npcCtrl.questProfile;
                npcModule.allowIdleWandering = npcCtrl.allowIdleWandering;
            }

            // Set stationed position and apply state directly (no message, no vault save).
            // Use _placementRotation captured in Awake() from the ZDO â€” transform.rotation
            // cannot be trusted since Character/AI systems may have overwritten it.
            npcModule.isStationedAsNpc = true;
            npcModule.SetStationedPositionDirect(transform.position, _placementRotation);
            npcModule.ApplyStationedState();

            // Re-enable Humanoid so the NPC can animate and be interacted with.
            var hum = GetComponent<Humanoid>();
            if (hum != null) hum.enabled = true;

            // Re-enable MonsterAI only if idle wandering is allowed
            if (npcModule.allowIdleWandering)
            {
                var mai = GetComponent<MonsterAI>();
                if (mai != null) mai.enabled = true;

                var rb = GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
            }

            // Force the placement rotation one final time after all setup
            transform.rotation = _placementRotation;

            // Only the server should write to ZDO â€” clients can't write ZDOs they don't own
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            if (isServer)
            {
                zdo.Set("npc_static_placement", true);
                npcModule.SaveToZDO();
            }

            // --- Configure idle behavior home position and enable if wandering ---
            idleBehavior.enabled = npcModule.allowIdleWandering;
            if (npcModule.allowIdleWandering)
            {
                idleBehavior.SetHomePosition(transform.position, npcModule.idleWanderRadius);
            }
        }

        /// <summary>
        /// Randomizes appearance (name, gender, hair, skin, eye color, scale) and equipment
        /// for a freshly placed static NPC, using the same data and logic as companion NPCs.
        /// </summary>
        private void RandomizeNpc(ZDO zdo)
        {
            // --- Name & Gender ---
            string vikingName = CompanionRandomLoadout.GetRandomVikingName();
            bool isFemale = CompanionRandomLoadout.IsFemaleVikingName(vikingName);

            var npcController = GetComponent<NpcController>();
            if (npcController != null)
            {
                npcController.displayNameOverride = vikingName;
            }
            zdo.Set("npc_name_override", vikingName);

            // --- Scale ---
            float scale = GenerateRandomScale();
            transform.localScale = new Vector3(scale, scale, scale);
            zdo.Set("npc_scale", scale);

            // --- Appearance colors ---
            Color hairColor = CompanionRandomLoadout.GetRandomHairColorAsColor();
            Color skinColor = CompanionRandomLoadout.GetRandomSkinColorAsColor();
            Color eyeColor = CompanionRandomLoadout.GetRandomEyeColorAsColor();

            zdo.Set(ZDOVars.s_hairColor, new Vector3(hairColor.r, hairColor.g, hairColor.b));
            zdo.Set(ZDOVars.s_skinColor, new Vector3(skinColor.r, skinColor.g, skinColor.b));
            zdo.Set("npc_eyeColor", new Vector3(eyeColor.r, eyeColor.g, eyeColor.b));

            // --- Hair & Beard ---
            int modelIndex = isFemale ? 1 : 0;
            zdo.Set(ZDOVars.s_modelIndex, modelIndex);
            zdo.Set("npc_isfemale", isFemale);

            string hairStyle = PickRandomHairStyle();
            string beardStyle = (!isFemale && Random.value < 0.7f) ? PickRandomBeardStyle() : "";

            zdo.Set("npc_hair", hairStyle);
            zdo.Set("npc_beard", beardStyle);

            // --- Equipment ---
            RandomizeEquipment(zdo, npcController);

            // --- Apply visuals after a short delay to let NpcVisEquipment initialize ---
            StartCoroutine(ApplyAppearance(zdo));

            Debug.Log($"[StaticNpcInitializer] Randomized NPC: {vikingName}, Female={isFemale}, Scale={scale:F2}");
        }

        /// <summary>
        /// Generates a random scale using the same weighted distribution as companions.
        /// </summary>
        private float GenerateRandomScale()
        {
            float randomValue = Random.value;
            if (randomValue < 0.15f)
                return Random.Range(CompanionRandomLoadout.MIN_SCALE, CompanionRandomLoadout.DWARF_THRESHOLD);
            else if (randomValue > 0.85f)
                return Random.Range(CompanionRandomLoadout.GIANT_THRESHOLD, CompanionRandomLoadout.MAX_SCALE);
            else
                return Random.Range(CompanionRandomLoadout.DWARF_THRESHOLD, CompanionRandomLoadout.GIANT_THRESHOLD);
        }

        private string PickRandomHairStyle()
        {
            var valid = CompanionRandomLoadout.KnownHairStyles.Where(h => h != "HairNone").ToArray();
            if (valid.Length == 0 || Random.value < 0.1f) return "";
            return valid[Random.Range(0, valid.Length)];
        }

        private string PickRandomBeardStyle()
        {
            var valid = CompanionRandomLoadout.KnownBeardStyles.Where(b => b != "BeardNone").ToArray();
            if (valid.Length == 0) return "";
            return valid[Random.Range(0, valid.Length)];
        }

        /// <summary>
        /// Randomizes visual equipment on the NPC using the same biome-tiered item lists
        /// that companions use. Equipment is stored in NpcController fields and ZDO.
        /// </summary>
        private void RandomizeEquipment(ZDO zdo, NpcController npcController)
        {
            // Ensure the shared item cache is populated
            CompanionRandomLoadout.CacheItemLists();
            if (!CompanionRandomLoadout._itemListsCached) return;

            var biome = Heightmap.FindBiome(transform.position);

            var helmets = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allHelmets, biome);
            var chests = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allChestArmor, biome);
            var legs = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allLegArmor, biome);
            var capes = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allCapes, biome);
            var shoulders = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allShoulders, biome);
            var shields = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allShields, biome);
            var weapons = CompanionRandomLoadout.GetTierFilteredItems(CompanionRandomLoadout._allWeapons, biome);

            string helmet = (Random.value < 0.4f && helmets.Count > 0) ? helmets[Random.Range(0, helmets.Count)] : "";
            string chest = (Random.value < 0.7f && chests.Count > 0) ? chests[Random.Range(0, chests.Count)] : "";
            string leg = (Random.value < 0.6f && legs.Count > 0) ? legs[Random.Range(0, legs.Count)] : "";
            string shoulder = "";
            if (Random.value < 0.3f && capes.Count > 0)
                shoulder = capes[Random.Range(0, capes.Count)];
            else if (Random.value < 0.25f && shoulders.Count > 0)
                shoulder = shoulders[Random.Range(0, shoulders.Count)];

            string rightHand = (weapons.Count > 0) ? weapons[Random.Range(0, weapons.Count)] : "";
            string leftHand = "";
            if (!string.IsNullOrEmpty(rightHand) && Random.value < 0.4f && shields.Count > 0)
            {
                // Only give shield if primary weapon is one-handed
                var prefab = ZNetScene.instance?.GetPrefab(rightHand);
                if (prefab != null)
                {
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop?.m_itemData?.m_shared != null)
                    {
                        var t = itemDrop.m_itemData.m_shared.m_itemType;
                        if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon)
                            leftHand = shields[Random.Range(0, shields.Count)];
                    }
                }
            }

            // Store in NpcController
            if (npcController != null)
            {
                npcController.equipHelmet = helmet;
                npcController.equipChest = chest;
                npcController.equipLegs = leg;
                npcController.equipShoulder = shoulder;
                npcController.equipRightHand = rightHand;
                npcController.equipLeftHand = leftHand;
            }

            // Persist to ZDO
            zdo.Set("npc_equip_helmet", helmet);
            zdo.Set("npc_equip_chest", chest);
            zdo.Set("npc_equip_legs", leg);
            zdo.Set("npc_equip_shoulder", shoulder);
            zdo.Set("npc_equip_righthand", rightHand);
            zdo.Set("npc_equip_lefthand", leftHand);
        }

        /// <summary>
        /// Applies the randomized appearance to the NpcVisEquipment after it has initialized.
        /// </summary>
        private IEnumerator ApplyAppearance(ZDO zdo)
        {
            // Wait for NpcVisEquipment to finish its own delayed init
            yield return new WaitForSeconds(1.5f);

            var visEquip = GetComponent<NpcVisEquipment>();
            if (visEquip == null)
            {
                Debug.LogWarning("[StaticNpcInitializer] No NpcVisEquipment found, cannot apply appearance");
                yield break;
            }

            int modelIndex = zdo.GetInt(ZDOVars.s_modelIndex, 0);
            string hair = zdo.GetString("npc_hair", "");
            string beard = zdo.GetString("npc_beard", "");
            Vector3 hairColorVec = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one);
            Vector3 skinColorVec = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one);
            Vector3 eyeColorVec = zdo.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f));

            visEquip.SetHairColor(hairColorVec);
            visEquip.SetSkinColor(skinColorVec);
            visEquip.SetEyeColor(new Color(eyeColorVec.x, eyeColorVec.y, eyeColorVec.z));
            visEquip.SetAppearance(modelIndex, hair, modelIndex == 1 ? "" : beard);

            // Re-load NpcController from ZDO so it picks up the randomized equipment
            // (NpcController.PerformStartInitialization runs earlier and reads empty ZDO)
            var npcController = GetComponent<NpcController>();
            if (npcController != null)
            {
                npcController.LoadFromZDO();
                if (npcController.HasAnyEquipment())
                {
                    npcController.ApplyVisualEquipment();
                }
            }

            // Restore scale
            float scale = zdo.GetFloat("npc_scale", 1f);
            if (scale > 0.1f && Mathf.Abs(scale - 1f) > 0.01f)
            {
                transform.localScale = new Vector3(scale, scale, scale);
            }

            Debug.Log($"[StaticNpcInitializer] Applied appearance and equipment visuals");
        }

        /// <summary>
        /// Restores appearance from ZDO for already-initialized NPCs (e.g. on world reload).
        /// </summary>
        private IEnumerator RestoreAppearanceFromZDO(ZDO zdo)
        {
            // Wait for NpcVisEquipment to finish its own delayed init
            yield return new WaitForSeconds(1.5f);

            var visEquip = GetComponent<NpcVisEquipment>();
            if (visEquip == null) yield break;

            int modelIndex = zdo.GetInt(ZDOVars.s_modelIndex, 0);
            string hair = zdo.GetString("npc_hair", "");
            string beard = zdo.GetString("npc_beard", "");
            Vector3 hairColorVec = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one);
            Vector3 skinColorVec = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one);
            Vector3 eyeColorVec = zdo.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f));

            visEquip.SetHairColor(hairColorVec);
            visEquip.SetSkinColor(skinColorVec);
            visEquip.SetEyeColor(new Color(eyeColorVec.x, eyeColorVec.y, eyeColorVec.z));
            visEquip.SetAppearance(modelIndex, hair, modelIndex == 1 ? "" : beard);

            // Re-load NpcController from ZDO so it picks up the persisted equipment
            var npcController = GetComponent<NpcController>();
            if (npcController != null)
            {
                npcController.LoadFromZDO();
                if (npcController.HasAnyEquipment())
                {
                    npcController.ApplyVisualEquipment();
                }
            }

            // Restore scale
            float scale = zdo.GetFloat("npc_scale", 1f);
            if (scale > 0.1f && Mathf.Abs(scale - 1f) > 0.01f)
            {
                transform.localScale = new Vector3(scale, scale, scale);
            }
        }
    }
}
