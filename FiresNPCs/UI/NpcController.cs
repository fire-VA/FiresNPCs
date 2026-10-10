using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc;
using FiresRPGmaker.NpcFashion;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Companions.NpcMode;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Marketplace;

namespace FiresRPGmaker.UI
{

    /// <summary>
    /// Main NPC controller component - attach this to your NPC prefab in Unity.
  /// Implements Valheim's Interactable and Hoverable interfaces for "Press E to interact".
    /// </summary>
    public class NpcController : MonoBehaviour, Interactable, Hoverable
    {
        [Header("NPC Identity")]
    public string npcProfileName = "NPC";
        public string displayNameOverride;

        [Header("Profiles")]
        public string dialogueProfile;

        // kg map pin parity (KGmarketPinned/KGmarketPinIcon): when pinned, NpcMapPins shows this NPC on
        // every player's map (server scans + broadcasts). Icon = an item prefab name; empty = default.
        public bool mapPinned;
        public string mapPinIcon;
        public string questProfile;
        public string infoProfile;
        public string traderProfile;
        public string bankerProfile;
        public string gamblerProfile;
        public string marketplaceProfile;
        public string achievementProfile;
        public string leaderboardProfile;
        public string bufferProfile;
        public string mailProfile;
        public string transmogProfile;
        public string teleporterProfile;

        // Name of the assigned patrol route (a file under BepInEx/config/FiresNPCs_PatrolRoutes/).
        // Empty = no patrol. Multiple NPCs can share one route name.
        public string patrolRouteName;

        // Name of the DBSM speed preset selected on the assigned route. Empty = Default (flat base walk).
        // Different NPCs sharing one route can run different presets.
        public string patrolSpeedPreset;

        [Header("Overrides")]
        public string modelOverride;
        public NpcType npcType = NpcType.None;

        [Header("Behavior")]
        public bool allowIdleWandering;

        [Header("Combat / Health")]
        public float maxHealth = 0f;          // 0 = use the prefab's natural health; >=20 = custom (slider 20–250000)
        public bool infiniteHealth = false;   // blocks all damage with no calculation
        public float respawnDelay = 60f;      // seconds before a killed patrol NPC respawns

        [Header("Nameplate")]
        public bool hideNameplate = false;    // hide the floating name
        public float nameplateScale = 1f;     // canvas scale of the floating plate
        public float nameplateYOffset = 0f;   // metres added to the plate height above the model

        // Equipment data (stored as prefab names)
        [Header("Equipment")]
        public string equipHelmet;
        public string equipChest;
 public string equipLegs;
        public string equipShoulder;
    public string equipUtility;
        public string equipRightHand;
        public string equipLeftHand;
        public string equipRightBack;
        public string equipLeftBack;
        public string equipPet;   // 0.0.559: cosmetic pet item/creature ("" none); owner writes "fires_pet" via NpcPetSlot

        // Full serialized ItemData (base64) for the hand weapons, so a static NPC fights with the EXACT item
        // the player equipped (quality/upgrades/crafter/customData/worldLevel) — not a quality-1 prefab clone.
        // Visuals still use the prefab-name fields above; these are combat-stat fidelity only.
        public string equipRightHandData;
        public string equipLeftHandData;

        // Appearance lives on the ZDO, not in the fashion JSON. The JSON is a transport format for
        // templates and the kg migrator; these four keys are what the dressing room reads and what
        // StaticNpcInitializer re-applies on every zone load, so they are the source of truth.
        public string hairItem;
        public string hairColor;
        public string beardItem;
        public string beardColor;

     private ZNetView _nview;
        private string _appliedModelOverride;
   private NpcVisEquipment _visEquipment;

     // Proximity detection for greet/bye animations
        private const float ProximityRange = 10f;      // greet when a player comes within this (kg: 10m)
        private const float ProximityExitRange = 12f;  // bye only past this — hysteresis stops boundary flapping (kg: 12m)
      private bool _playerWasInRange = false;
  private float _proximityCheckTimer = 0f;
        private const float ProximityCheckInterval = 0.25f;

        // Follow mode (companion system)
        private bool _isFollowing = false;
  private Player _followTarget = null;
  private float _followDistance = 3f;
        private float _followSpeed = 4f;
        private float _followStopDistance = 2f;
        private Animator _animator;
        private CharacterController _characterController;

        // Store pending fashion JSON for application after visual setup
        private string _pendingFashionJson;

        /// <summary>
        /// Set to true in Awake if the ZDO already has a model override.
        /// NpcVisEquipment checks this to avoid enabling VisEquipment prematurely
        /// for NPCs that will have their visual hierarchy replaced.
        /// </summary>
        internal bool HasPendingModelOverride { get; private set; }

        // SavedNpcManager keys live NPCs by placement id; registering here is what lets an incoming
        // NpcDataUpdate find its target without a scene-wide FindObjectsByType per RPC.
        private void OnEnable() => SavedNpcManager.RegisterLiveNpc(this);

        private void OnDisable() => SavedNpcManager.UnregisterLiveNpc(this);

        private void Awake()
     {
            _nview = GetComponent<ZNetView>();
            _animator = GetComponentInChildren<Animator>(true);
          _characterController = GetComponent<CharacterController>();

            // Read model override from ZDO immediately so NpcVisEquipment knows
            // this NPC will get a non-default model and can stay disabled until
            // the override is applied. This prevents the "spawn as humanoid then
            // swap" pattern that causes UpdateColors/UpdateVisuals errors.
            TryReadModelOverrideEarly();
        }

        /// <summary>
        /// Attempts to read the model override from ZDO or SavedNpcManager during Awake.
        /// If a model override is pending, NpcVisEquipment will keep VisEquipment disabled
        /// until ApplyModelOverride completes.
        /// </summary>
        private void TryReadModelOverrideEarly()
        {
            try
            {
                // Try ZDO first (available immediately on existing NPCs)
                var zdo = _nview?.GetZDO();
                if (zdo != null)
                {
                    string zdoModel = zdo.GetString("npc_model_override", "");
                    if (!string.IsNullOrEmpty(zdoModel))
                    {
                        modelOverride = zdoModel;
                        HasPendingModelOverride = true;
                        return;
                    }
                }

                // Also check SavedNpcManager cache (server files may have model override)
                if (_nview != null && _nview.IsValid())
                {
                    string savedModel = SavedNpcManager.GetModelOverrideForNpc(_nview);
                    if (!string.IsNullOrEmpty(savedModel))
                    {
                        modelOverride = savedModel;
                        HasPendingModelOverride = true;
                    }
                }
            }
            catch
            {
                // Non-critical - DelayedStart will handle it
            }
        }

  private void Start()
        {
            // Delay initialization to ensure server connection is established
            // This prevents timing issues with ZDO loading and visual equipment
            StartCoroutine(DelayedStart());
        }

        private System.Collections.IEnumerator DelayedStart()
        {
            // Wait for ZNetScene to be ready
            while (ZNetScene.instance == null)
            {
                yield return null;
            }
            
            // Wait for ObjectDB to be ready (needed for equipment prefab lookups)
            while (ObjectDB.instance == null)
            {
                yield return null;
            }
            
            // If connecting to a server, wait for connection to be established
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
            
            // Small additional delay to let skeleton/joints settle
            yield return new WaitForSeconds(0.5f);
            
            // Now do the actual initialization
            PerformStartInitialization();
        }
        
        private void PerformStartInitialization()
        {
            if (_nview != null && _nview.IsValid())
            {
                // Load ZDO data first (baseline)
                LoadFromZDO();

                // Always try the server-authoritative saved NPC data as the primary source.
                // SavedNpcManager holds the server files - they override ZDO values.
                SavedNpcManager.LoadIntoNpc(this);

                if (!string.IsNullOrEmpty(modelOverride) && modelOverride != _appliedModelOverride)
                {
                    ApplyModelOverride(modelOverride);
                }

                if (HasAnyEquipment())
                {
                    Invoke(nameof(ApplyVisualEquipment), 0.5f);
                }

                if (!string.IsNullOrEmpty(_pendingFashionJson))
                {
                    Invoke(nameof(ApplyPendingFashion), 0.55f);
                }
            }
        }

private void Update()
        {
      CheckPlayerProximity();

        if (_isFollowing && _followTarget != null)
   {
       UpdateFollowBehavior();
            }
   }

        /// <summary>
      /// Checks if the local player is within proximity range and triggers greet/bye animations.
     /// </summary>
        private void CheckPlayerProximity()
   {
            _proximityCheckTimer += Time.deltaTime;
            if (_proximityCheckTimer < ProximityCheckInterval) return;
 _proximityCheckTimer = 0f;

   var player = Player.m_localPlayer;
   if (player == null) return;

      var fashionComp = GetComponent<NpcFashionComponent>();

       float distance = Vector3.Distance(transform.position, player.transform.position);
        bool playerInRange = _playerWasInRange ? distance <= ProximityExitRange : distance <= ProximityRange;

       if (playerInRange && !_playerWasInRange)
      {
       fashionComp?.PlayGreetAnimation();
       FireProximityDialogueInteractions(InteractionType.InRange);
           if (FiresLogger.VerboseEnabled)
               Debug.Log($"[NpcController] Player entered range of {GetDisplayName()} - playing greet animation");
      }
      else if (!playerInRange && _playerWasInRange)
 {
     fashionComp?.PlayByeAnimation();
     FireProximityDialogueInteractions(InteractionType.OutRange);
 if (FiresLogger.VerboseEnabled)
     Debug.Log($"[NpcController] Player left range of {GetDisplayName()} - playing bye animation");
            }

            _playerWasInRange = playerInRange;
        }

        /// <summary>
        /// kg InRange/OutRange dialogue interactions (Market_NPC FixedUpdate parity, same 10m enter /
        /// 12m exit hysteresis this proximity check already uses for greet/bye): on the range transition,
        /// every option of the NPC's dialogue with the matching InteractionType whose conditions pass
        /// fires its commands. Auto-fire only — these options never render in the dialogue window.
        /// </summary>
        private void FireProximityDialogueInteractions(InteractionType type)
        {
            try
            {
                if (string.IsNullOrEmpty(dialogueProfile)) return;
                var dlg = DialogueCodex.Get(dialogueProfile.Trim().ToLowerInvariant());
                if (dlg?.Interactions == null) return;
                foreach (var opt in dlg.Interactions)
                {
                    if (opt == null || opt.Type != type) continue;
                    if (!DialogueConditionEngine.EvaluateAll(opt.Conditions, out _)) continue;
                    if (opt.Commands != null && opt.Commands.Length > 0)
                        DialogueCommandEngine.ExecuteAll(opt.Commands, _nview);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[NpcController] proximity dialogue interactions failed: {ex.Message}"); }
        }

        #region Follow Mode (Companion System)

        /// <summary>
        /// Toggles follow mode for this NPC.
        /// </summary>
  public void ToggleFollowMode(Player player)
        {
if (_isFollowing && _followTarget == player)
     {
        StopFollowing();
           MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{GetDisplayName()} stops following you.");
      }
            else
      {
    StartFollowing(player);
         MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{GetDisplayName()} is now following you.");
         }
        }

   /// <summary>
        /// Starts following the specified player.
        /// </summary>
        public void StartFollowing(Player player)
    {
            if (player == null) return;

  _isFollowing = true;
        _followTarget = player;

            var fashionComp = GetComponent<NpcFashionComponent>();
            fashionComp?.PlayGreetAnimation();

            Debug.Log($"[NpcController] {GetDisplayName()} started following {player.GetPlayerName()}");
 }

     /// <summary>
     /// Stops following the current target.
        /// </summary>
        public void StopFollowing()
        {
            _isFollowing = false;
        _followTarget = null;

          if (_animator != null)
            {
    if (HasParameter(_animator, "moving"))
           _animator.SetBool("moving", false);
      if (HasParameter(_animator, "forward_speed"))
      _animator.SetFloat("forward_speed", 0f);
            }

            Debug.Log($"[NpcController] {GetDisplayName()} stopped following");
        }

        /// <summary>
        /// Updates the NPC position to follow the target player.
        /// </summary>
        private void UpdateFollowBehavior()
     {
            if (_followTarget == null || !_isFollowing)
     {
            StopFollowing();
   return;
   }

            if (_followTarget.IsDead())
     {
        StopFollowing();
      return;
            }

          Vector3 targetPos = _followTarget.transform.position;
            Vector3 myPos = transform.position;

            Vector3 toTarget = targetPos - myPos;
            toTarget.y = 0;
            float distance = toTarget.magnitude;

            if (distance > 50f)
    {
        transform.position = targetPos + UnityEngine.Random.insideUnitSphere * _followDistance;
         transform.position = new Vector3(transform.position.x, targetPos.y, transform.position.z);
            Debug.Log($"[NpcController] {GetDisplayName()} teleported to follow target");
       return;
            }

        if (distance <= _followStopDistance)
         {
    if (_animator != null)
                {
          if (HasParameter(_animator, "moving"))
    _animator.SetBool("moving", false);
           if (HasParameter(_animator, "forward_speed"))
           _animator.SetFloat("forward_speed", 0f);
     }
        return;
      }

      Vector3 moveDir = toTarget.normalized;

   if (moveDir != Vector3.zero)
            {
 Quaternion targetRotation = Quaternion.LookRotation(moveDir);
     transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
     }

         float moveSpeed = _followSpeed;

   if (distance < _followDistance)
            {
         moveSpeed *= (distance / _followDistance);
            }

     Vector3 movement = moveDir * moveSpeed * Time.deltaTime;

     if (_characterController != null && _characterController.enabled)
   {
     movement.y = -9.8f * Time.deltaTime;
         _characterController.Move(movement);
          }
            else
    {
    transform.position += movement;
    }

            if (_animator != null)
       {
                if (HasParameter(_animator, "moving"))
        _animator.SetBool("moving", true);
                if (HasParameter(_animator, "forward_speed"))
     _animator.SetFloat("forward_speed", moveSpeed / _followSpeed);
       }
        }

        public bool IsFollowing => _isFollowing;
     public Player FollowTarget => _followTarget;

        #endregion

        #region Equipment Management

   public bool HasAnyEquipment()
        {
      return !string.IsNullOrEmpty(equipHelmet) ||
  !string.IsNullOrEmpty(equipChest) ||
         !string.IsNullOrEmpty(equipLegs) ||
    !string.IsNullOrEmpty(equipShoulder) ||
       !string.IsNullOrEmpty(equipUtility) ||
       !string.IsNullOrEmpty(equipRightHand) ||
         !string.IsNullOrEmpty(equipLeftHand) ||
         !string.IsNullOrEmpty(equipRightBack) ||
   !string.IsNullOrEmpty(equipLeftBack);
  }

    public Dictionary<DressingRoomScreenController.EquipmentSlot, string> GetEquipmentData()
   {
     var equipment = new Dictionary<DressingRoomScreenController.EquipmentSlot, string>();
     equipment[DressingRoomScreenController.EquipmentSlot.Helmet] = equipHelmet ?? "";
  equipment[DressingRoomScreenController.EquipmentSlot.Chest] = equipChest ?? "";
     equipment[DressingRoomScreenController.EquipmentSlot.Legs] = equipLegs ?? "";
         equipment[DressingRoomScreenController.EquipmentSlot.Shoulder] = equipShoulder ?? "";
            equipment[DressingRoomScreenController.EquipmentSlot.Utility] = equipUtility ?? "";
     equipment[DressingRoomScreenController.EquipmentSlot.RightHand] = equipRightHand ?? "";
            equipment[DressingRoomScreenController.EquipmentSlot.LeftHand] = equipLeftHand ?? "";
            equipment[DressingRoomScreenController.EquipmentSlot.RightBack] = equipRightBack ?? "";
        equipment[DressingRoomScreenController.EquipmentSlot.LeftBack] = equipLeftBack ?? "";
    return equipment;
 }

        public void SetEquipmentData(Dictionary<DressingRoomScreenController.EquipmentSlot, string> equipment)
     {
    equipHelmet = "";
    equipChest = "";
            equipLegs = "";
  equipShoulder = "";
        equipUtility = "";
          equipRightHand = "";
            equipLeftHand = "";
            equipRightBack = "";
            equipLeftBack = "";

        if (equipment == null) return;

        foreach (var kvp in equipment)
        {
         switch (kvp.Key)
            {
       case DressingRoomScreenController.EquipmentSlot.Helmet: equipHelmet = kvp.Value; break;
              case DressingRoomScreenController.EquipmentSlot.Chest: equipChest = kvp.Value; break;
         case DressingRoomScreenController.EquipmentSlot.Legs: equipLegs = kvp.Value; break;
       case DressingRoomScreenController.EquipmentSlot.Shoulder: equipShoulder = kvp.Value; break;
           case DressingRoomScreenController.EquipmentSlot.Utility: equipUtility = kvp.Value; break;
        case DressingRoomScreenController.EquipmentSlot.RightHand: equipRightHand = kvp.Value; break;
  case DressingRoomScreenController.EquipmentSlot.LeftHand: equipLeftHand = kvp.Value; break;
             case DressingRoomScreenController.EquipmentSlot.RightBack: equipRightBack = kvp.Value; break;
       case DressingRoomScreenController.EquipmentSlot.LeftBack: equipLeftBack = kvp.Value; break;
}
        }

            // Drop the serialized combat-item blob for any hand slot that is now empty (weapon removed).
            if (string.IsNullOrEmpty(equipRightHand)) equipRightHandData = "";
            if (string.IsNullOrEmpty(equipLeftHand)) equipLeftHandData = "";
        }

        /// <summary>Stores the full serialized ItemData (base64) for a hand slot so combat uses the real weapon
        /// stats. Visuals are unaffected (they use the prefab-name fields). Empty/null clears it.</summary>
        public void SetEquipmentItemData(DressingRoomScreenController.EquipmentSlot slot, string base64)
        {
            if (slot == DressingRoomScreenController.EquipmentSlot.RightHand) equipRightHandData = base64 ?? "";
            else if (slot == DressingRoomScreenController.EquipmentSlot.LeftHand) equipLeftHandData = base64 ?? "";
        }

        public void ApplyVisualEquipment()
        {
   if (_visEquipment == null)
 {
   _visEquipment = GetComponent<NpcVisEquipment>();
        if (_visEquipment == null)
        {
         _visEquipment = gameObject.AddComponent<NpcVisEquipment>();
   }
           _visEquipment.ForceReinitialize();
            }

          _visEquipment.ApplyEquipment(GetEquipmentData().ToDictionary(kv => (CompanionInventory.EquipmentSlot)(int)kv.Key, kv => kv.Value));
      FiresCore.Services.VanillaAssetResolver.RepairBrokenShaders(gameObject, $"npc '{GetDisplayName()}' after equip");
      Debug.Log($"[NpcController] Applied visual equipment to {GetDisplayName()}");
        }

        /// <summary>
        /// Attaches/refreshes the Core PatrolAssignment from <see cref="patrolRouteName"/>. A static NPC has
        /// normal companion physics, so no freeze/gravity flips are needed — PatrolBehavior just walks it.
        /// Runs on every machine from LoadFromZDO; the patrol movement itself runs on the owner.
        /// </summary>
        public void SyncPatrolAssignment()
        {
            var pa = GetComponent<FiresCore.Npc.Patrol.PatrolAssignment>();
            if (string.IsNullOrEmpty(patrolRouteName))
            {
                if (pa != null) { pa.RouteName = ""; pa.PresetName = ""; }
                return;
            }
            if (pa == null) pa = gameObject.AddComponent<FiresCore.Npc.Patrol.PatrolAssignment>();
            pa.RouteName = patrolRouteName;
            pa.PresetName = patrolSpeedPreset ?? "";
            // Patrol runs through CompanionIdleBehavior, which is disabled on a non-wandering static NPC —
            // enable it so the route actually drives. No physics changes needed: the NPC already has normal
            // companion physics (gravity on), so PatrolBehavior just walks it like any companion.
            var idle = GetComponent<CompanionIdleBehavior>();
            if (idle != null) idle.enabled = true;

            // Ensure the body is dynamic so PatrolBehavior can actually walk it. On a client relog the route can
            // resolve HERE after StaticNpcInitializer's willMove kinematic-clear already ran with no route yet,
            // leaving the NPC kinematic → the companion movement prefixes drop its walk and it gets dragged.
            // Clearing it whenever a route is present closes that race.
            var rb = GetComponent<Rigidbody>();
            if (rb != null && rb.isKinematic) { rb.isKinematic = false; rb.useGravity = true; }
        }

        /// <summary>Pushes the configured max health onto the Humanoid. Static NPCs are server-authoritative,
        /// so this only writes on the server (claiming ownership if needed). Tops up current health when the
        /// bar was full so raising the cap actually fills it.</summary>
        public void ApplyMaxHealth()
        {
            if (maxHealth < 20f) return; // unset / below the slider floor → leave the prefab's natural health
            var hum = GetComponent<Humanoid>();
            if (hum == null || _nview == null || !_nview.IsValid()) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            bool wasFull = hum.GetHealth() >= hum.GetMaxHealth() - 0.01f;
            hum.SetMaxHealth(maxHealth);
            if (wasFull) hum.SetHealth(maxHealth);
        }

        #endregion

        #region Valheim Interactable Interface

   public bool Interact(Humanoid user, bool hold, bool alt)
      {
    if (hold) return false;

   var player = user as Player;
  if (player == null) return false;

     Debug.Log($"[NpcController] Player interacted with NPC: {GetDisplayName()}");

      var fashionComp = GetComponent<NpcFashionComponent>();
         fashionComp?.PlayInteractAnimation();

   bool isAdmin = ZNet.instance?.IsServer() == true ||
            FiresRPGmaker.Instance?.configSync?.IsAdmin == true;

            // alt: vanilla's own modifier for this Interact (its AltPlace binding, Shift by default; rebinds and gamepad follow).
            bool shiftHeld = alt || ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);

       // Admin Shift: open the config book (the old flat NpcMainPanelController is retired).
            if (isAdmin && shiftHeld)
   {
                NpcBookController.ShowForNpc(this);
    return true;
   }

            HandleNormalInteraction(player);
         return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
         return false;
        }

        #endregion

        #region Valheim Hoverable Interface

        // Valheim 1.0 added GetHoverOffset to the Hoverable interface; it is ADDED to
        // Player.m_maxInteractDistance, so 0 preserves the previous interact range exactly.
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (GUIManager.IsCustomPanelOpen())
  {
      return string.Empty;
    }

            string name = GetDisplayName();

          bool isServer = ZNet.instance?.IsServer() == true;
            bool isConfigAdmin = FiresRPGmaker.Instance?.configSync?.IsAdmin == true;
     bool isAdmin = isServer || isConfigAdmin;

      string text = $"<color=yellow><b>{name}</b></color>";

         if (_isFollowing)
        {
      text += " <color=lime>(Following)</color>";
     }

   string interactionHint = npcType switch
 {
              NpcType.QuestNpc => "Quests",
      NpcType.InfoNpc => "Info",
          NpcType.DialogueNpc => "Talk",
   NpcType.Trader => "Trade",
              NpcType.None => null,          // no role assigned — nothing to interact with
             _ => "Interact"
   };

     if (interactionHint != null)
         text += $"\n[<color=yellow><b>$KEY_Use</b></color>] {interactionHint}";

 if (isAdmin)
        {
  text += "\n[<color=#00FFFF><b>L.Shift + $KEY_Use</b></color>] Configure";
      }

         return Localization.instance.Localize(text);
        }

        public string GetHoverName()
        {
     if (GUIManager.IsCustomPanelOpen())
     {
       return string.Empty;
          }
   return GetDisplayName();
    }

      #endregion

        #region ZDO Persistence

 public void LoadFromZDO()
        {
            var zdo = _nview?.GetZDO();
        if (zdo == null) return;

            npcProfileName = zdo.GetString("npc_profile", npcProfileName ?? "NPC");
      displayNameOverride = zdo.GetString("npc_name_override", displayNameOverride ?? "");
  dialogueProfile = zdo.GetString("npc_dialogue_profile", dialogueProfile ?? "");
            mapPinned = zdo.GetBool("npc_map_pinned", mapPinned);
            mapPinIcon = zdo.GetString("npc_map_pin_icon", mapPinIcon ?? "");
     questProfile = zdo.GetString("npc_quest_profile", questProfile ?? "");
    infoProfile = zdo.GetString("npc_info_profile", infoProfile ?? "");
   traderProfile = zdo.GetString("npc_trader_profile", traderProfile ?? "");
            bankerProfile = zdo.GetString("npc_banker_profile", bankerProfile ?? "");
            gamblerProfile = zdo.GetString("npc_gambler_profile", gamblerProfile ?? "");
            marketplaceProfile = zdo.GetString("npc_marketplace_profile", marketplaceProfile ?? "");
            achievementProfile = zdo.GetString("npc_achievement_profile", achievementProfile ?? "");
            leaderboardProfile = zdo.GetString("npc_leaderboard_profile", leaderboardProfile ?? "");
            bufferProfile = zdo.GetString("npc_buffer_profile", bufferProfile ?? "");
            mailProfile = zdo.GetString("npc_mail_profile", mailProfile ?? "");
            transmogProfile = zdo.GetString("npc_transmog_profile", transmogProfile ?? "");
            teleporterProfile = zdo.GetString("npc_teleporter_profile", teleporterProfile ?? "");
            patrolRouteName = zdo.GetString("npc_patrol_route", patrolRouteName ?? "");
            patrolSpeedPreset = zdo.GetString("npc_patrol_preset", patrolSpeedPreset ?? "");
            SyncPatrolAssignment();
   modelOverride = zdo.GetString("npc_model_override", modelOverride ?? "");
     npcType = (NpcType)zdo.GetInt("npc_type", (int)npcType);

      allowIdleWandering = zdo.GetBool("npc_allow_idle_wander", false);

            maxHealth = zdo.GetFloat("npc_max_health", 0f);
            infiniteHealth = zdo.GetBool("npc_infinite_health", false);
            respawnDelay = zdo.GetFloat("npc_respawn_delay", respawnDelay);
            hideNameplate = zdo.GetBool("npc_name_hidden", false);
            nameplateScale = zdo.GetFloat("npc_text_size", 1f);
            nameplateYOffset = zdo.GetFloat("npc_text_height", 0f);
            // kg-style hover-name label (NameplateStyle=HoverName). Self-managing: builds lazily,
            // hides itself in the other modes and on a dedicated server (no camera).
            if (GetComponent<NpcHoverNameplate>() == null) gameObject.AddComponent<NpcHoverNameplate>();
            ApplyMaxHealth();

      equipHelmet = zdo.GetString("npc_equip_helmet", "");
  equipChest = zdo.GetString("npc_equip_chest", "");
      equipLegs = zdo.GetString("npc_equip_legs", "");
  equipShoulder = zdo.GetString("npc_equip_shoulder", "");
            equipUtility = zdo.GetString("npc_equip_utility", "");
            equipRightHand = zdo.GetString("npc_equip_righthand", "");
        equipLeftHand = zdo.GetString("npc_equip_lefthand", "");
   equipRightBack = zdo.GetString("npc_equip_rightback", "");
          equipLeftBack = zdo.GetString("npc_equip_leftback", "");
            equipRightHandData = zdo.GetString("npc_equip_righthand_data", "");
            equipLeftHandData = zdo.GetString("npc_equip_lefthand_data", "");

            hairItem = zdo.GetString("npc_hair", "");
            hairColor = zdo.GetString("npc_hair_color", "");
            beardItem = zdo.GetString("npc_beard", "");
            beardColor = zdo.GetString("npc_beard_color", "");

string fashionJson = zdo.GetString("npc_fashion_json", "");
            if (!string.IsNullOrEmpty(fashionJson))
   {
   _pendingFashionJson = fashionJson;
            }

            Debug.Log($"[NpcController] Loaded from ZDO - Name: {npcProfileName}, Type: {npcType}");
        }

  public string GetPendingFashionJson() => _pendingFashionJson;
        public void SetPendingFashionJson(string json) => _pendingFashionJson = json;

        public void SaveToZDO()
     {
      var zdo = _nview?.GetZDO();
      if (zdo == null) return;

            zdo.Set("npc_profile", npcProfileName ?? "");
      zdo.Set("npc_name_override", displayNameOverride ?? "");
            zdo.Set("npc_dialogue_profile", dialogueProfile ?? "");
            zdo.Set("npc_map_pinned", mapPinned);
            zdo.Set("npc_map_pin_icon", mapPinIcon ?? "");
            zdo.Set("npc_quest_profile", questProfile ?? "");
            zdo.Set("npc_info_profile", infoProfile ?? "");
    zdo.Set("npc_trader_profile", traderProfile ?? "");
            zdo.Set("npc_banker_profile", bankerProfile ?? "");
            zdo.Set("npc_gambler_profile", gamblerProfile ?? "");
            zdo.Set("npc_marketplace_profile", marketplaceProfile ?? "");
            zdo.Set("npc_achievement_profile", achievementProfile ?? "");
            zdo.Set("npc_leaderboard_profile", leaderboardProfile ?? "");
            zdo.Set("npc_buffer_profile", bufferProfile ?? "");
            zdo.Set("npc_mail_profile", mailProfile ?? "");
            zdo.Set("npc_transmog_profile", transmogProfile ?? "");
            zdo.Set("npc_teleporter_profile", teleporterProfile ?? "");
            zdo.Set("npc_patrol_route", patrolRouteName ?? "");
            zdo.Set("npc_patrol_preset", patrolSpeedPreset ?? "");
    zdo.Set("npc_model_override", modelOverride ?? "");
   zdo.Set("npc_type", (int)npcType);

            zdo.Set("npc_allow_idle_wander", allowIdleWandering);

            zdo.Set("npc_max_health", maxHealth);
            zdo.Set("npc_infinite_health", infiniteHealth);
            zdo.Set("npc_respawn_delay", respawnDelay);
            zdo.Set("npc_name_hidden", hideNameplate);
            zdo.Set("npc_text_size", nameplateScale);
            zdo.Set("npc_text_height", nameplateYOffset);

            zdo.Set("npc_equip_helmet", equipHelmet ?? "");
            zdo.Set("npc_equip_chest", equipChest ?? "");
            zdo.Set("npc_equip_legs", equipLegs ?? "");
        zdo.Set("npc_equip_shoulder", equipShoulder ?? "");
    zdo.Set("npc_equip_utility", equipUtility ?? "");
  zdo.Set("npc_equip_righthand", equipRightHand ?? "");
       zdo.Set("npc_equip_lefthand", equipLeftHand ?? "");
        zdo.Set("npc_equip_rightback", equipRightBack ?? "");
  zdo.Set("npc_equip_leftback", equipLeftBack ?? "");
            zdo.Set("npc_equip_righthand_data", equipRightHandData ?? "");
            zdo.Set("npc_equip_lefthand_data", equipLeftHandData ?? "");

            zdo.Set("npc_hair", hairItem ?? "");
            zdo.Set("npc_hair_color", hairColor ?? "");
            zdo.Set("npc_beard", beardItem ?? "");
            zdo.Set("npc_beard_color", beardColor ?? "");

         string fashionJson = null;
        try
   {
     fashionJson = NpcFashionManager.ExtractFashionJsonFromNpc(this);
      }
      catch (Exception ex)
      {
        Debug.LogWarning($"[NpcController] Failed to extract FashionJson: {ex.Message}");
       }

            if (string.IsNullOrEmpty(fashionJson))
            {
   fashionJson = _pendingFashionJson;
   }

     if (!string.IsNullOrEmpty(fashionJson))
      {
   zdo.Set("npc_fashion_json", fashionJson);
            }

            // If this NPC is a companion stationed as an NPC, let the companion mod sync the
            // saved profile into its module (no-op without the companion mod installed).
            FiresCore.Bridge.NpcCompanionBridge.RaiseSyncSavedProfile(gameObject);

            // Re-resolve the body stage LIVE so an idle-wander toggle or patrol assignment takes
            // effect immediately — previously the NPC stayed frozen/ghosted until a zone reload or a
            // death-respawn re-ran the initializer. Idempotent and cheap; covers every book tab save.
            try { GetComponent<global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcInitializer>()?.ReapplyBodyStage(); }
            catch (Exception ex) { Debug.LogWarning($"[NpcController] live stage re-apply failed: {ex.Message}"); }

            SavedNpcManager.SaveFromNpc(this);
            Debug.Log($"[NpcController] Saved to ZDO - Name: {npcProfileName}, Type: {npcType}");

            // Tell any open config screen showing THIS NPC to re-read its profile field. There's one
            // field per type, so this carries no value — it just keeps the left "Configure NPC" page
            // and the active module tab in lockstep ("save on one side = shows on the other").
            ProfileChanged?.Invoke(this);
        }

        #region Profile accessors & cross-screen sync

        // The single source of truth for "the profile name" is the per-type field selected by npcType.
        // npcProfileName is identity/display only (the "Npc Name" field), NOT a profile selector. Every
        // config screen — the left Configure-NPC page included — reads and writes the active profile
        // through these accessors, so the left field and a module tab can never hold conflicting values.
        public string GetProfileForType(NpcType type) => type switch
        {
            NpcType.QuestNpc    => questProfile ?? "",
            NpcType.InfoNpc     => infoProfile ?? "",
            NpcType.DialogueNpc => dialogueProfile ?? "",
            NpcType.Trader      => traderProfile ?? "",
            NpcType.Banker      => bankerProfile ?? "",
            NpcType.Gambler     => gamblerProfile ?? "",
            NpcType.Marketplace => marketplaceProfile ?? "",
            NpcType.Achievement => achievementProfile ?? "",
            NpcType.Leaderboard => leaderboardProfile ?? "",
            NpcType.Buffer => bufferProfile ?? "",
            NpcType.Mail        => mailProfile ?? "",
            NpcType.Transmog    => transmogProfile ?? "",
            NpcType.Teleporter  => teleporterProfile ?? "",
            _ => ""
        };

        public void SetProfileForType(NpcType type, string profile)
        {
            switch (type)
            {
                case NpcType.QuestNpc:    questProfile = profile;       break;
                case NpcType.InfoNpc:     infoProfile = profile;        break;
                case NpcType.DialogueNpc: dialogueProfile = profile;    break;
                case NpcType.Trader:      traderProfile = profile;      break;
                case NpcType.Banker:      bankerProfile = profile;      break;
                case NpcType.Gambler:     gamblerProfile = profile;     break;
                case NpcType.Marketplace: marketplaceProfile = profile; break;
                case NpcType.Achievement: achievementProfile = profile; break;
                case NpcType.Leaderboard: leaderboardProfile = profile; break;
                case NpcType.Buffer:      bufferProfile = profile;      break;
                case NpcType.Mail:        mailProfile = profile;        break;
                case NpcType.Transmog:    transmogProfile = profile;    break;
                case NpcType.Teleporter:  teleporterProfile = profile;  break;
            }
        }

        public string GetActiveProfile() => GetProfileForType(npcType);
        public void SetActiveProfile(string profile) => SetProfileForType(npcType, profile);

        // Raised after SaveToZDO so open config screens refresh the visible profile field for THIS NPC.
        public static event Action<NpcController> ProfileChanged;

        // Saved-profile names available for a module type — the source for every profile dropdown.
        // Mirrors the interaction dispatch (one module per type); managers auto-load on access.
        public static IEnumerable<string> GetProfileNamesForType(NpcType type)
        {
            try
            {
                switch (type)
                {
                    case NpcType.QuestNpc:    return QuestProfileManager.GetAllProfileNames();
                    case NpcType.DialogueNpc: return DialogueManager.GetAllProfileNames();
                    case NpcType.InfoNpc:     return Screens.InfoNpcScreenController.GetAllProfileNames();
                    case NpcType.Trader:      return TraderProfileManager.GetAllProfileNames();
                    case NpcType.Banker:      return BankerProfileManager.AllProfileNames;
                    case NpcType.Gambler:     return GamblerProfileManager.AllProfileNames;
                    case NpcType.Mail:        return MailProfileManager.GetLoadedProfileNames();
                    case NpcType.Transmog:    return TransmogProfileManager.GetLoadedProfileNames();
                    case NpcType.Marketplace: return MarketplaceListingManager.GetLoadedProfileNames();
                    case NpcType.Achievement: return AchievementProfileManager.GetLoadedProfileNames();
                    case NpcType.Leaderboard: return LeaderboardProfileManager.GetLoadedProfileNames();
                    case NpcType.Buffer:      return global::FiresRPGmaker.Modules.Buffer.BufferManager.Profiles.Keys;
                    case NpcType.Teleporter:  return Bridges.WayshrinesBridge.GetProfileNames();
                    default: return Enumerable.Empty<string>();
                }
            }
            catch { return Enumerable.Empty<string>(); }
        }

        public IEnumerable<string> GetActiveProfileNames() => GetProfileNamesForType(npcType);

        #endregion

        // ─── Per-NPC trader gold pool (used by TraderUIController) ───
        // The gold pool is keyed on each NPC's ZDO so two NPCs sharing the same trader profile
        // each have their own balance. Float for "last restock" lets us compare against ZNet.GetTime().

        /// <summary>Current trader gold pool — what this NPC has available to pay out for player sells.
        /// Returns int.MaxValue when MaxGold is -1 (unlimited).</summary>
        public int GetTraderGold(int maxGoldFallback)
        {
            if (maxGoldFallback < 0) return int.MaxValue;
            var zdo = _nview?.GetZDO();
            if (zdo == null) return maxGoldFallback;
            // First read returns 0 by default — only fall back to MaxGold if the ZDO has never been seeded.
            if (!zdo.GetBool("npc_trader_gold_init"))
            {
                zdo.Set("npc_trader_gold", maxGoldFallback);
                zdo.Set("npc_trader_gold_init", true);
                return maxGoldFallback;
            }
            return zdo.GetInt("npc_trader_gold", maxGoldFallback);
        }

        /// <summary>Decrement the gold pool when the NPC pays out a player's sale. Persists to ZDO.</summary>
        public void SpendTraderGold(int amount, int maxGold)
        {
            if (amount <= 0 || maxGold < 0) return;
            var zdo = _nview?.GetZDO();
            if (zdo == null) return;
            int current = GetTraderGold(maxGold);
            int next = UnityEngine.Mathf.Max(0, current - amount);
            zdo.Set("npc_trader_gold", next);
        }

        /// <summary>Auto-restock pass — if <paramref name="restockSeconds"/> have elapsed since the last
        /// restock, refill the pool to MaxGold. Idempotent; safe to call every UI open.</summary>
        public void TickTraderGoldRestock(int maxGold, int restockSeconds)
        {
            if (maxGold < 0 || restockSeconds <= 0) return;
            var zdo = _nview?.GetZDO();
            if (zdo == null) return;
            long nowTicks = ZNet.instance != null ? ZNet.instance.GetTime().Ticks : System.DateTime.UtcNow.Ticks;
            long lastTicks = zdo.GetLong("npc_trader_gold_restock_ticks", 0L);
            if (lastTicks == 0L) { zdo.Set("npc_trader_gold_restock_ticks", nowTicks); return; }
            double elapsedSec = new System.TimeSpan(nowTicks - lastTicks).TotalSeconds;
            if (elapsedSec < restockSeconds) return;
            zdo.Set("npc_trader_gold", maxGold);
            zdo.Set("npc_trader_gold_init", true);
            zdo.Set("npc_trader_gold_restock_ticks", nowTicks);
        }

        #endregion

        #region Model Override

        public void ApplyModelOverride(string modelPrefabName)
        {
            if (string.IsNullOrEmpty(modelPrefabName)) return;
     if (modelPrefabName == _appliedModelOverride) return;

            // kg-Marketplace name syntax (drop-in parity — kg NPC configs/ZDOs use these verbatim):
            //   "<prefab>@humanoid"  → use the creature's MODEL but drive it with the PLAYER's animator
            //                          controller, so player emotes (greet/bye/periodic) play on it.
            //   "Player_Female"      → kg's virtual prefab: the Player model with the female body mesh.
            string resolvedName = modelPrefabName;
            bool forcePlayerController = false;
            bool femalePlayerModel = false;
            if (resolvedName.IndexOf("@humanoid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                forcePlayerController = true;
                resolvedName = System.Text.RegularExpressions.Regex.Replace(resolvedName, "@humanoid", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            }
            if (string.Equals(resolvedName, "Player_Female", StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = "Player";
                femalePlayerModel = true;
            }

      GameObject sourcePrefab = ZNetScene.instance?.GetPrefab(resolvedName);
            if (sourcePrefab == null)
            {
       Debug.LogWarning($"[NpcController] Model prefab '{resolvedName}' not found");
       return;
    }

            try
            {
    Transform targetVisual = transform.Find("Visual");
     if (targetVisual == null)
       {
             var visualGO = new GameObject("Visual");
visualGO.transform.SetParent(transform, false);
      targetVisual = visualGO.transform;
      }

                // CRITICAL: Disable VisEquipment BEFORE hiding old visual children.
                // VisEquipment.CustomUpdate() is called every frame by MonoUpdaters and
                // accesses m_bodyModel.sharedMesh. If we hide the old SkinnedMeshRenderer
                // without disabling VisEquipment first, it will throw NullReferenceException
                // on every frame until the new model is set up.
                var existingVisEquip = GetComponent<VisEquipment>();
                if (existingVisEquip != null)
                {
                    existingVisEquip.enabled = false;
                    existingVisEquip.m_bodyModel = null;
                }

                // CRITICAL: Instead of destroying old Visual children (which kills bone
                // Transforms that Character/Humanoid still reference for eye rotation,
                // head look-at, etc.), HIDE them by toggling visibility off. This preserves
                // all Transform references that Valheim's built-in systems hold.
                for (int i = targetVisual.childCount - 1; i >= 0; i--)
                {
                    var child = targetVisual.GetChild(i);
                    // Skip previously hidden originals - don't stack hidden copies
                    if (child.name.StartsWith("__OriginalVisual_")) continue;
                    child.gameObject.SetActive(false);
                    child.name = "__OriginalVisual_" + child.name;
                }

                // Also disable CharacterAnimEvent components so MonoUpdaters doesn't
                // call CustomLateUpdate on them with stale state
                foreach (var animEvt in GetComponentsInChildren<CharacterAnimEvent>(true))
                {
                    if (animEvt != null) animEvt.enabled = false;
                }

                var oldAnimator = GetComponent<Animator>();
        if (oldAnimator != null) DestroyImmediate(oldAnimator);

         Transform sourceVisual = sourcePrefab.transform.Find("Visual") ?? sourcePrefab.transform.Find("visual");
         bool sourceIsCharacter = sourcePrefab.GetComponent<Character>() != null;
         bool cloneWholeModel = false;

         if (sourceVisual == null)
         {
             // kg parity: a creature without a "Visual" child clones its Animator-bearing child (the rig
             // root); a NON-Character prefab (props / Balrond IdleActors ms_npc_* etc.) clones the WHOLE
             // model — every top-level child — because its meshes/armature can be split across siblings
             // (the old first-renderer-child pick cloned partial models).
             if (sourceIsCharacter)
                 sourceVisual = sourcePrefab.GetComponentInChildren<Animator>(true)?.transform;
             if (sourceVisual == null)
                 cloneWholeModel = true;
         }

  if (cloneWholeModel)
  {
      var container = new GameObject("OverrideModel");
      container.transform.SetParent(targetVisual, false);
      container.layer = LayerMask.NameToLayer("character");
      for (int i = 0; i < sourcePrefab.transform.childCount; i++)
      {
          var srcChild = sourcePrefab.transform.GetChild(i);
          var childClone = Instantiate(srcChild.gameObject, container.transform);
          childClone.name = srcChild.name;
          childClone.transform.localPosition = srcChild.localPosition;
          childClone.transform.localRotation = srcChild.localRotation;
          childClone.transform.localScale = srcChild.localScale;
          childClone.SetActive(true);
      }
      StripGameplayComponentsOnly(container);
      CleanupModelCloneArtifacts(container, disableParticles: false);   // kg keeps prop particles (torches etc.)
  }
  else if (sourceVisual != null)
  {
      // CRITICAL: We need to DESTROY CharacterAnimEvent components on the SOURCE before cloning
      // Just disabling them doesn't work because Instantiate() creates new instances
      // and Unity calls Awake() on the NEW cloned components, not the source ones.
      // 
      // Solution: Clone the source visual's hierarchy manually, or accept that we must
      // destroy the components immediately after instantiation but before they can run.
      // Since Awake() runs synchronously during Instantiate(), we'll use a workaround:
      // Deactivate the source, clone it, then immediately destroy problematic components.

      bool wasActive = sourceVisual.gameObject.activeSelf;
      sourceVisual.gameObject.SetActive(false); // Deactivate source to prevent side effects

      var clonedVisual = Instantiate(sourceVisual.gameObject, targetVisual);

      // Restore source state
      sourceVisual.gameObject.SetActive(wasActive);

      // IMMEDIATELY destroy CharacterAnimEvent components before they can cause issues
      // Awake() has already run, but we can prevent Start() and Update()
      var clonedAnimEvents = clonedVisual.GetComponentsInChildren<CharacterAnimEvent>(true);
      foreach (var animEvent in clonedAnimEvents)
      {
          if (animEvent != null)
          {
              DestroyImmediate(animEvent); // Use DestroyImmediate to remove it NOW
          }
      }

      clonedVisual.name = sourceVisual.name;
      clonedVisual.transform.localPosition = sourceVisual.localPosition;
      clonedVisual.transform.localRotation = sourceVisual.localRotation;
      clonedVisual.transform.localScale = sourceVisual.localScale;
      clonedVisual.SetActive(true);
      StripGameplayComponentsOnly(clonedVisual);

      // Remove player-specific visual effects that cause errors on NPCs
      CleanupPlayerModelEffects(clonedVisual);
      // kg clone hygiene: kill LODGroups (they cull the clone oddly after the strip) and, for creature
      // sources, stop particle emission (auras/trails don't belong on a shopkeeper).
      CleanupModelCloneArtifacts(clonedVisual, disableParticles: sourceIsCharacter);
  }

         CopyAnimatorFromPrefab(sourcePrefab, targetVisual);

            // kg parity post-steps:
            //   @humanoid → the creature rig plays the PLAYER's animation set (emotes work).
            //   Player_Female → swap in the female body mesh/textures from the Player's VisEquipment.
            //   Always bind the NPC to whatever animator now drives the model, so locomotion and
            //   networked emote triggers (greet/bye/periodic) play on overridden models.
            if (forcePlayerController) ApplyPlayerAnimatorController(targetVisual);
            if (femalePlayerModel) ApplyFemalePlayerBodyModel(targetVisual);
            BindModelAnimator(targetVisual);
       EnableAllRenderers(targetVisual.gameObject);
            FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(gameObject, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);

 _appliedModelOverride = modelPrefabName;

                // CRITICAL: Always reinitialize NpcVisEquipment after a model change.
                // The old VisEquipment.m_bodyModel pointed to the destroyed SkinnedMeshRenderer.
                // NpcVisEquipment.ForceReinitialize() will re-discover the new body model,
                // re-enable the VisEquipment component, and set up attachment points.
                var npcVisEquip = GetComponent<NpcVisEquipment>();
                if (npcVisEquip != null)
                {
                    npcVisEquip.ForceReinitialize();
                }
                else if (existingVisEquip != null)
                {
                    // No NpcVisEquipment wrapper - point VisEquipment at the new body,
                    // but only enable it if the model uses the Custom/Player shader.
                    // Non-humanoid overrides (e.g. Boar) use Standard shader which
                    // lacks _SkinBumpMap/_ChestTex/_LegsTex and causes
                    // IndexOutOfRangeException in UpdateColors every frame.
                    SkinnedMeshRenderer newBody = null;
                    foreach (var smr in targetVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr == null) continue;
                        bool isUnderHidden = false;
                        Transform check = smr.transform;
                        while (check != null && check != targetVisual)
                        {
                            if (!check.gameObject.activeSelf && check.name.StartsWith("__OriginalVisual_"))
                            { isUnderHidden = true; break; }
                            check = check.parent;
                        }
                        if (!isUnderHidden) { newBody = smr; break; }
                    }
                    existingVisEquip.m_bodyModel = newBody;

                    bool shaderOk = newBody != null
                        && newBody.sharedMaterial != null
                        && newBody.sharedMaterial.shader != null
                        && newBody.sharedMaterial.shader.name == "Custom/Player";
                    existingVisEquip.enabled = shaderOk;

                    if (!shaderOk)
                    {
                        // Clear m_models to prevent stale index access from
                        // MonoUpdaters calling CustomUpdate on the disabled component.
                        existingVisEquip.m_models = new VisEquipment.PlayerModel[0];
                    }
                }

                if (HasAnyEquipment())
     {
    _visEquipment = null;
            Invoke(nameof(ApplyVisualEquipment), 0.2f);
        }

      Debug.Log($"[NpcController] Applied model override '{modelPrefabName}'");
         }
     catch (Exception ex)
     {
        Debug.LogError($"[NpcController] Failed to apply model: {ex.Message}");
            }
        }

  public void ResetModelToDefault()
 {
            Debug.Log("[NpcController] Resetting model to default");
            _appliedModelOverride = null;
 modelOverride = "";

            // Disable the underlying VisEquipment to prevent NRE from accessing
            // a potentially stale m_bodyModel reference while the model is being reset.
            var baseVisEquip = GetComponent<VisEquipment>();
            if (baseVisEquip != null)
            {
                baseVisEquip.enabled = false;
                baseVisEquip.m_bodyModel = null;
            }

            if (_visEquipment != null)
       {
  Destroy(_visEquipment);
   _visEquipment = null;
        }
    }

    #endregion

        #region Helper Methods

  public string GetDisplayName()
   {
     if (!string.IsNullOrEmpty(displayNameOverride))
             return displayNameOverride;
            if (!string.IsNullOrEmpty(npcProfileName))
         return npcProfileName;
   return gameObject.name;
        }

        // GetActiveProfile()/GetProfileForType() live in the "Profile accessors & cross-screen sync" region.

      private void HandleNormalInteraction(Player player)
        {
       switch (npcType)
 {
case NpcType.QuestNpc:
      HandleQuestNpcInteraction(player);
             break;
         case NpcType.InfoNpc:
HandleInfoNpcInteraction(player);
    break;
   case NpcType.DialogueNpc:
  HandleDialogueNpcInteraction(player);
        break;
      case NpcType.Trader:
          HandleTraderInteraction(player);
      break;
      case NpcType.Banker:
          Screens.PlayerPanels.BankerPlayerPanel.Show(bankerProfile, GetDisplayName());
      break;
      case NpcType.Buffer:       Screens.PlayerPanels.BufferPlayerPanel.Show(bufferProfile, GetDisplayName());           break;
      case NpcType.Gambler:      Screens.PlayerPanels.GamblerPlayerPanel.Show(gamblerProfile, GetDisplayName());           break;
      case NpcType.Marketplace:  Screens.PlayerPanels.MarketplacePlayerPanel.Show(marketplaceProfile, GetDisplayName());   break;
      case NpcType.Mail:         Screens.PlayerPanels.MailPlayerPanel.Show(mailProfile, GetDisplayName());                 break;
      case NpcType.Transmog:     Screens.PlayerPanels.TransmogPlayerPanel.Show(transmogProfile, GetDisplayName());         break;
      case NpcType.Teleporter:   Bridges.WayshrinesBridge.OpenSelectionForProfile(player, teleporterProfile);            break;
      case NpcType.Feedback:     Modules.Fringe.FeedbackModule.PromptAndSubmit(GetDisplayName());                        break;
      // Achievement + Leaderboard are legacy global panels now, and None = an unconfigured NPC
      // (freshly hammer-placed template). Plain E must NEVER open a config UI — that's Shift+E's
      // job (the book). A player poking an NPC with no player-facing role just gets a neutral line.
      case NpcType.Achievement:
      case NpcType.Leaderboard:
      case NpcType.None:
          default:
                 MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{GetDisplayName()} has nothing to say.");
   break;
     }
        }

        /// <summary>Effective quest profile: explicit assignment if set, else recovered by matching this NPC's
        /// name to a loaded profile (migrated kg quest NPCs commonly lost their profile assignment), else "default".</summary>
        public string GetEffectiveQuestProfile()
            => QuestProfileManager.ResolveProfileName(questProfile, GetDisplayName());

        private void HandleQuestNpcInteraction(Player player)
        {
         if (!string.IsNullOrEmpty(dialogueProfile))
          {
             var profile = DialogueManager.GetProfile(dialogueProfile);
       if (!string.IsNullOrEmpty(profile?.DefaultGreeting))
       {
      MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, profile.DefaultGreeting);
             }
  }

         if (!string.IsNullOrEmpty(GetEffectiveQuestProfile()))
     {
             SuppressHoverHud(); QuestInteractionScreenController.ShowForNpc(this);
            }
 else
            {
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
         $"{GetDisplayName()} has no quests available.");
            }
      }

        private void HandleInfoNpcInteraction(Player player)
   {
       if (!string.IsNullOrEmpty(infoProfile))
     {
       var profile = Screens.InfoNpcScreenController.GetProfileForName(infoProfile);
         SuppressHoverHud(); Screens.InfoNpcPlayerPanel.Show(GetDisplayName(), profile);
            }
  else
     {
         MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{GetDisplayName()} has nothing to say.");
            }
        }

private void HandleDialogueNpcInteraction(Player player)
       {
    if (!string.IsNullOrEmpty(dialogueProfile))
           {
               // Try the new DialogueCodex system first (Marketplace-compatible branching dialogues)
               DialogueCodex.Init();
               string uid = dialogueProfile.Trim().ToLowerInvariant();
               var codexDialogue = DialogueCodex.Get(uid);
               if (codexDialogue != null)
               {
                   SuppressHoverHud(); DialogueUI.Show(GetDisplayName(), uid, _nview);
                   return;
               }

               // Fallback to legacy DialogueManager
     var lines = DialogueManager.GetDialogueLinesForProfile(dialogueProfile);
            if (lines != null && lines.Count > 0)
  {
  var profile = DialogueManager.GetProfile(dialogueProfile);
     if (!string.IsNullOrEmpty(profile?.DefaultGreeting))
     {
     MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, profile.DefaultGreeting);
  return;
   }
}
           }
    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{GetDisplayName()} has nothing to say.");
   }

        private void HandleTraderInteraction(Player player)
        {
            SuppressHoverHud(); TraderUIController.ShowForNpc(this);
        }

        private void HandleGenericModuleStub(Player player, string label, string profile)
        {
            // Stub-routed for modules whose REAL player UI hasn't shipped (Gambler, Marketplace,
            // Achievement, Leaderboard, Mail, Transmog). Opens the shared StubPlayerUIController so
            // the NPC interaction at least pops a panel — full per-module UIs replace each dispatch
            // as their prefabs + controllers land.
            Screens.StubPlayerUIController.Show(label, profile);
        }

        private void HandleBankerInteraction(Player player)
        {
            // Banker player UI is the next module — for now, confirm the NPC is set up as a Banker so admins
            // can see their config took. The full deposit/withdraw UI lands in a follow-up alongside the
            // dormant FiresCore.Storage.BankRepository wiring.
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                string.IsNullOrEmpty(bankerProfile)
                    ? $"{GetDisplayName()}: banker profile not configured."
                    : $"{GetDisplayName()}: banker '{bankerProfile}' (UI coming soon)");
        }

        private void SuppressHoverHud()
        {
            NpcHoverHudSuppressor.Clear();
            NpcHoverHudSuppressor.Suppress(GetComponent<Character>() ?? GetComponentInChildren<Character>());
            // SuppressAll so nearby NPC / Companion / wild-monster nameplates don't bleed through the panel.
            NpcHoverHudSuppressor.SuppressAll(true);
        }

        private void ApplyPendingFashion()
        {
        if (string.IsNullOrEmpty(_pendingFashionJson)) return;

            try
            {
    NpcFashionManager.ApplyFashionJsonToNpc(_pendingFashionJson, this);
    Debug.Log($"[NpcController] Applied pending fashion JSON");
          }
            catch (Exception ex)
{
      Debug.LogWarning($"[NpcController] Failed to apply pending fashion: {ex.Message}");
      }
        }

        private void StripGameplayComponentsOnly(GameObject obj)
        {
            var toRemove = new List<Component>();

            string[] typesToStrip = {
         "Character", "Humanoid", "MonsterAI", "BaseAI", "AnimalAI", "Tameable",
   "Procreation", "CharacterDrop", "CharacterTimedDestruction", "Growup",
        "LevelEffects", "CharacterAnimEvent", "FootStep", "ZNetView",
  "ZSyncTransform", "ZSyncAnimation", "Ragdoll", "Player", "PlayerController",
        "Attack", "ItemDrop", "Container", "Destructible", "WearNTear",
        "SpawnArea", "CreatureSpawner", "Spawner", "VisEquipment"
     };

            // CRITICAL: First, immediately disable CharacterAnimEvent components before they can run Awake()
            // This prevents the NullReferenceException in CharacterAnimEvent.Awake() when cloning Player models
            foreach (var animEvent in obj.GetComponentsInChildren<CharacterAnimEvent>(true))
            {
                if (animEvent != null)
                {
                    // Disable the component immediately to prevent its Start/Update from running
                    animEvent.enabled = false;
                    toRemove.Add(animEvent);
                }
            }

foreach (var mono in obj.GetComponentsInChildren<MonoBehaviour>(true))
     {
        if (mono == null) continue;

        var typeName = mono.GetType().Name;
        bool shouldStrip = false;
       foreach (var stripType in typesToStrip)
         {
           if (typeName == stripType)
         {
 shouldStrip = true;
             break;
           }
           }

 if (shouldStrip) toRemove.Add(mono);
            }

         foreach (var rb in obj.GetComponentsInChildren<Rigidbody>(true))
            {
    if (rb != null && !rb.isKinematic) toRemove.Add(rb);
      }

          toRemove.AddRange(obj.GetComponentsInChildren<CharacterController>(true));
    toRemove.AddRange(obj.GetComponentsInChildren<CharacterJoint>(true));

   foreach (var col in obj.GetComponentsInChildren<Collider>(true))
       {
  if (col != null && !col.isTrigger) toRemove.Add(col);
            }

foreach (var comp in toRemove)
            {
            if (comp != null) Destroy(comp);
     }

         SetupIdleAnimations(obj);
     }

        private void SetupIdleAnimations(GameObject obj)
        {
     var animators = obj.GetComponentsInChildren<Animator>(true);
      foreach (var animator in animators)
       {
       if (animator == null || animator.runtimeAnimatorController == null) continue;

animator.enabled = true;
          animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

    try
         {
            if (HasParameter(animator, "moving"))
             animator.SetBool("moving", false);
        if (HasParameter(animator, "forward_speed"))
   animator.SetFloat("forward_speed", 0f);
            }
      catch { }
            }
        }

private bool HasParameter(Animator animator, string paramName)
      {
          if (animator == null || animator.parameters == null) return false;
            foreach (var param in animator.parameters)
            {
         if (param.name == paramName) return true;
            }
            return false;
    }

        private void CopyAnimatorFromPrefab(GameObject sourcePrefab, Transform targetVisual)
        {
     var sourceAnimator = sourcePrefab.GetComponent<Animator>()
    ?? sourcePrefab.GetComponentInChildren<Animator>(true);

            if (sourceAnimator == null || sourceAnimator.runtimeAnimatorController == null) return;

            var cloneAnimator = FindCloneAnimator(targetVisual);
            if (cloneAnimator != null && cloneAnimator.runtimeAnimatorController != null)
            {
                cloneAnimator.enabled = true;
                cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                cloneAnimator.applyRootMotion = false;
                return;
            }

            var targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null)
            {
     targetAnimator = gameObject.AddComponent<Animator>();
            }

    targetAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
          targetAnimator.avatar = sourceAnimator.avatar;
  targetAnimator.applyRootMotion = false;
  targetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
     targetAnimator.Rebind();
        }

        // The animator that actually drives the override model — the clone's own animator when it has a
        // controller (self-animated props / creature rigs), else the root animator CopyAnimatorFromPrefab set up.
        private Animator FindActiveModelAnimator(Transform targetVisual)
        {
            return FindCloneAnimator(targetVisual) ?? GetComponent<Animator>();
        }

        // The clone's animator: Visual's own Animator belongs to the hidden original rig, as does anything under an
        // __OriginalVisual_ child.
        private static Animator FindCloneAnimator(Transform targetVisual)
        {
            if (targetVisual == null) return null;
            foreach (var anim in targetVisual.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null || anim.transform == targetVisual) continue;
                bool underHidden = false;
                Transform check = anim.transform;
                while (check != null && check != targetVisual)
                {
                    if (check.name.StartsWith("__OriginalVisual_")) { underHidden = true; break; }
                    check = check.parent;
                }
                if (!underHidden) return anim;
            }
            return null;
        }

        // "<prefab>@humanoid" (kg syntax): drive the creature rig with the PLAYER's animator controller so
        // the full player emote set (greet/bye/periodic) plays on it — kg Market_NPC.TryOverrideModel parity.
        private void ApplyPlayerAnimatorController(Transform targetVisual)
        {
            try
            {
                var playerAnimator = ZNetScene.instance?.GetPrefab("Player")?.GetComponentInChildren<Animator>(true);
                if (playerAnimator == null || playerAnimator.runtimeAnimatorController == null)
                {
                    Debug.LogWarning("[NpcController] @humanoid: Player animator controller unavailable");
                    return;
                }
                var anim = FindActiveModelAnimator(targetVisual);
                if (anim == null) return;
                anim.runtimeAnimatorController = playerAnimator.runtimeAnimatorController;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.Rebind();
            }
            catch (Exception ex) { Debug.LogWarning("[NpcController] @humanoid controller swap failed: " + ex.Message); }
        }

        // "Player_Female" (kg virtual prefab): swap the female body mesh + skin textures from the Player's
        // VisEquipment model table onto the cloned PLAYER rig (same rig — safe; never done to bundle rigs).
        private void ApplyFemalePlayerBodyModel(Transform targetVisual)
        {
            try
            {
                var vis = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<VisEquipment>();
                if (vis == null || vis.m_models == null || vis.m_models.Length < 2) return;
                var female = vis.m_models[1];

                SkinnedMeshRenderer body = null;
                foreach (var smr in targetVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null) continue;
                    bool hidden = false;
                    Transform check = smr.transform;
                    while (check != null && check != targetVisual)
                    {
                        if (check.name.StartsWith("__OriginalVisual_")) { hidden = true; break; }
                        check = check.parent;
                    }
                    if (!hidden) { body = smr; break; }
                }
                if (body == null) return;

                body.sharedMesh = female.m_mesh;
                if (female.m_baseMaterial != null && body.materials.Length > 0)
                {
                    body.materials[0].SetTexture("_MainTex", female.m_baseMaterial.GetTexture("_MainTex"));
                    body.materials[0].SetTexture("_SkinBumpMap", female.m_baseMaterial.GetTexture("_SkinBumpMap"));
                }
            }
            catch (Exception ex) { Debug.LogWarning("[NpcController] Player_Female body swap failed: " + ex.Message); }
        }

        // Everything that animates the NPC moves to the animator driving the model. ZSyncAnimation (kg rebinds
        // zanim.m_animator the same way) and vanilla Character (state tags, tilt) cached the original rig's animator
        // at Awake. That animator now only moves hidden bones, so it is switched off.
        private void BindModelAnimator(Transform targetVisual)
        {
            var anim = FindActiveModelAnimator(targetVisual);
            if (anim == null) return;
            anim.logWarnings = false;
            var zanim = GetComponent<ZSyncAnimation>();
            if (zanim != null) zanim.m_animator = anim;
            var character = GetComponent<Character>();
            if (character != null) character.m_animator = anim;
            var original = targetVisual.GetComponent<Animator>();
            if (original != null && original != anim) original.enabled = false;
            _animator = anim;
        }

        // LODGroups on a stripped clone mis-cull, and creature particle systems (auras/trails) shouldn't
        // follow a model onto a shopkeeper. kg destroys/disables both in TryOverrideModel.
        private static void CleanupModelCloneArtifacts(GameObject root, bool disableParticles)
        {
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
                Destroy(lod);
            if (!disableParticles) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var em = ps.emission;
                em.enabled = false;
            }
        }

        private void EnableAllRenderers(GameObject root)
    {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
 if (renderer != null)
  {
      // Skip renderers under hidden original visuals
      if (renderer.transform.parent != null && renderer.transform.parent.name.StartsWith("__OriginalVisual_"))
          continue;
      // Also check grandparent (renderer may be deeper in hierarchy)
      bool isUnderHidden = false;
      Transform check = renderer.transform;
      while (check != null && check != root.transform)
      {
          if (!check.gameObject.activeSelf)
          {
              isUnderHidden = true;
              break;
          }
          check = check.parent;
      }
      if (isUnderHidden) continue;
      renderer.enabled = true;
       }
       }
        }

        /// <summary>
        /// Cleans up the dev_smoke effect from player models that causes material errors on NPCs.
        /// This is a targeted fix for the specific "dev_smoke" particle effect.
        /// </summary>
        private void CleanupPlayerModelEffects(GameObject root)
        {
            if (root == null) return;

            try
            {
                // Only look for and remove dev_smoke specifically
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || child.gameObject == root) continue;

                    // Only remove dev_smoke - this is the specific effect causing the material error
                    if (child.name == "dev_smoke")
                    {
                        Destroy(child.gameObject);
                        Debug.Log($"[NpcController] Removed dev_smoke effect from player model");
                        break; // There's typically only one
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcController] Error cleaning up dev_smoke: {ex.Message}");
            }
        }

     #endregion
 }
}
