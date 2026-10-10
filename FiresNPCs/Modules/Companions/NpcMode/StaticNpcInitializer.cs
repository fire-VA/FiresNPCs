using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.UI;
using FiresRPGmaker.UI.Screens;
using FiresRPGmaker.Managers;
using FiresRPGmaker.NpcFashion;
using FiresRPGmaker.Diagnostics;
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
    /// the normal companion pipeline (spawn companion -> claim -> Shift+P to station).
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
        private Vector3 _awakePosition;   // 0.0.509
        private string _creationStack;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();

            // Ghost/placement tracer: logs EVERY instance the moment it exists — ghosts (invalid
            // nview) included — so a placed-but-never-initialized body can't hide.
            Debug.Log($"[GhostDiag:awake] '{gameObject.name}' pos={transform.position} activeInHierarchy={gameObject.activeInHierarchy} " +
                      $"nviewValid={_nview != null && _nview.IsValid()}");

            // Creator fingerprint (frames only, no file info — cheap): every legitimate body creator is
            // known (PlacePiece, NpcAnchorKeeper.SpawnBody, ZNetScene zone load, reconciler Respawn).
            // If a body ever materializes seeded-but-unanchored, EnsureAnchorForBody logs this stack and
            // the rogue creator names itself instead of us guessing at duplicate sources.
            try { _creationStack = new System.Diagnostics.StackTrace(1, false).ToString(); }
            catch { _creationStack = "<unavailable>"; }

            // Read the placement rotation from the ZDO immediately before anything can overwrite it.
            _placementRotation = Quaternion.identity;
            if (_nview != null && _nview.IsValid())
            {
                var zdo = _nview.GetZDO();
                if (zdo != null)
                    { _placementRotation = zdo.GetRotation(); _awakePosition = zdo.GetPosition(); }   // 0.0.509: the spawn spot, before anyone can possess it
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

            // Freeze physics for the whole init window (zeroed velocities + FreezeAll constraints, NOT
            // isKinematic — velocity writes on kinematic bodies spam Unity 6 warnings). Between Awake and
            // the staged config in DelayedInit (~1s+) the body has no AI and free physics: on a zone
            // reload it fell through not-yet-built terrain (historically DYING to fall damage — the
            // original "NPCs don't persist" killer), and command-spawned NPCs slid down slopes like ice
            // until setup locked them. ApplyBodyStage sets the final per-stage constraints afterwards.
            var frozenRb = GetComponent<Rigidbody>();
            if (frozenRb != null)
            {
                frozenRb.isKinematic = false;
                frozenRb.linearVelocity = Vector3.zero;
                frozenRb.angularVelocity = Vector3.zero;
                frozenRb.constraints = RigidbodyConstraints.FreezeAll;
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

            // ZDO-lifecycle diagnostic (see StaticNpcZdoDiagnostics): every body materialization logs its
            // persistence contract so a later vanish can be correlated against DestroyZDO traces.
            Debug.Log("[StaticNpcZdoDiag] body materialized - " + StaticNpcZdoDiagnostics.Describe(zdo));

            // Anti-duplication (server, both paths): a hammer-delete makes the anchor respawn the body, then
            // an undo restores the original — TWO bodies sharing one anchor guid. The anchor owns exactly one;
            // the other is a zombie. If our anchor already points at a DIFFERENT live body, we're the stray →
            // destroy self before doing any setup. Only fires on the exact duplicate condition (a legit body's
            // anchor points at itself or doesn't exist yet), so it can never delete a real NPC.
            if (isServer && IsDuplicateOfAnchoredBody(zdo))
            {
                Debug.LogWarning($"[StaticNpcInitializer] '{gameObject.name}' at {transform.position} is a DUPLICATE (its anchor already owns another live body) — self-destructing to resolve the hammer-delete/undo dupe");
                if (_nview != null && _nview.IsValid())
                {
                    _nview.ClaimOwnership();
                    ZNetScene.instance?.Destroy(gameObject);
                }
                yield break;
            }

            // Check if already initialized (from a previous session load)
            bool alreadyInitialized = zdo.GetBool("npc_initialized", false);
            if (alreadyInitialized)
            {
                // Already set up - restore appearance from ZDO on every machine.
                // [persist] Dump the identity keys we're restoring FROM so a relog that loses dialogue/name/
                // visuals is provable: if these are populated here but the NPC still shows wrong, the loss is
                // downstream (a clobbering SaveToZDO / a parallel respawn body); if they're EMPTY here, the
                // migrator's ZDO writes didn't persist to the .db. Either way the log names which.
                Debug.Log(string.Format("[StaticNpcInitializer][persist] RESTORE '{0}' pos=({1:F0},{2:F0}) " +
                    "name='{3}' type={4} dialogue='{5}' trader='{6}' quest='{7}' fashionJson={8}B model='{9}' hair='{10}' server={11}",
                    gameObject.name, transform.position.x, transform.position.z,
                    zdo.GetString("npc_name_override", ""), zdo.GetInt("npc_type", 0),
                    zdo.GetString("npc_dialogue_profile", ""), zdo.GetString("npc_trader_profile", ""),
                    zdo.GetString("npc_quest_profile", ""), zdo.GetString("npc_fashion_json", "").Length,
                    zdo.GetString("npc_model_override", ""), zdo.GetString("npc_hair", ""), isServer));
                // Load the controller's persisted fields (equipment, patrol route, name) FROM the ZDO BEFORE
                // wiring components. EnsureCompanionNpcComponents reads equipRightHand* (LoadCombatHand) and
                // patrolRouteName (the willMove kinematic gate); on a client relog those controller fields are
                // still empty until a later coroutine loads the ZDO ~1.5s on, so without this the NPC arms no
                // weapon AND is left KINEMATIC (can't walk → dragged). Loading here makes both reads see the
                // real data immediately. The ZDO is intact (server-owned; static NPCs skip the logout-destroy).
                yield return WaitOutPossession(zdo);   // 0.0.509: never set up (and re-anchor) a body a player is driving
                GetComponent<NpcController>()?.LoadFromZDO();
                EnsureCompanionNpcComponents(zdo);
                // Organic migration: a pre-anchor body that loads on the server gets its anchor here.
                if (isServer) EnsureAnchorForBody(zdo, organicMigration: true);
                StartCoroutine(RestoreAppearanceFromZDO(zdo));
                // Organic template-icon backfill (client): once the body is dressed, capture the
                // hammer icon for pre-icon templates. Cheap no-op when the icon already exists.
                if (!isServer) StartCoroutine(BackfillIconWhenDressed());
                yield break;
            }

            yield return WaitOutPossession(zdo);   // 0.0.509

            // CRITICAL: Always set up the companion NPC component stack on EVERY machine
            // (client and server). Without this, clients never get CompanionNpcModule,
            // isStationedAsNpc is never true, and interaction/hover/wandering all break.
            EnsureCompanionNpcComponents(zdo);

            // Non-server clients: components are set up but ZDO writes and first-time
            // randomization belong to the server. Restore whatever data is already in
            // the ZDO (may be partial on brand-new placement - server will write it shortly).
            if (!isServer)
            {
                StartCoroutine(RestoreAppearanceFromZDO(zdo));
                yield break;
            }

            // [persist] SMOKING-GUN log: a body reaches here ONLY when npc_initialized read FALSE. For a
            // MIGRATED body that is the bug — it means either the migrator's ZDO (incl. npc_initialized) never
            // persisted to the .db, or this is a NEW/parallel body (respawn manager) spawned without the data.
            // If any migration key survives here while npc_initialized is false, the persistence is partial.
            // This randomize path then overwrites it into a generic QuestNpc ("Eir the Wanderer" raven) — the
            // exact scramble seen on relog. Loud WARNING so it can never hide.
            bool looksMigrated = !string.IsNullOrEmpty(zdo.GetString("npc_name_override", ""))
                                 || !string.IsNullOrEmpty(zdo.GetString("npc_dialogue_profile", ""))
                                 || !string.IsNullOrEmpty(zdo.GetString("npc_fashion_json", ""));
            Debug.LogWarning(string.Format("[StaticNpcInitializer][persist] FIRST-INIT (npc_initialized=false) at " +
                "({0:F0},{1:F0}) — RANDOMIZING. looksMigrated={2} name='{3}' dialogue='{4}' type={5}. " +
                "If looksMigrated=true this is the relog persistence bug (migrator ZDO didn't fully persist).",
                transform.position.x, transform.position.z, looksMigrated,
                zdo.GetString("npc_name_override", ""), zdo.GetString("npc_dialogue_profile", ""),
                zdo.GetInt("npc_type", 0)));

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

                // Pin the STABLE placement anchor once (never re-cemented, unlike npc_stationed_pos), then seed
                // the desired-state registry so a brand-new NPC that vanishes within the first reconcile
                // interval is still re-materializable. ClearTombstone first so re-placing a NEW NPC where one
                // was deleted un-deletes that cell — the periodic refresh stays tombstone-respecting. The
                // reconciler refreshes this snapshot from the live NPC each pass, so route/appearance assigned
                // later stay current.
                try
                {
                    // Feet-probe the home position before stamping it: the placement point is
                    // authoritative but the transform may already be perturbed by spawn-time
                    // physics, and the anchor + wander home + static feet-pin all inherit this
                    // value — it must sit ON the surface (build piece OR terrain) underfoot.
                    TryProbeSurface(transform.position, out var placedFeet);
                    zdo.Set("npc_placed_anchor", placedFeet);
                    var ch = GetComponent<Character>();
                    if (ch != null)
                    {
                        var snap = StaticNpcRespawnManager.CaptureFrom(ch);
                        if (snap != null)
                        {
                            StaticNpcRespawnManager.Instance.ClearTombstone(snap.HomePosition);
                            StaticNpcRespawnManager.Instance.RegisterPlacement(snap);
                        }
                    }
                }
                catch (System.Exception ex) { Debug.LogError("[StaticNpcInitializer] placement-registry seed failed: " + ex.Message); }
            }

            // Anchor-owned lifecycle: fresh placement mints this NPC's anchor — the tiny persistent
            // ZDO that IS the durable record. From here on the keeper guarantees the body exists.
            EnsureAnchorForBody(zdo);

            Debug.Log($"[StaticNpcInitializer] Initialized stationed quest NPC at {transform.position}");

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft,
                "Static Quest NPC placed. Use Shift+E to configure.");
        }

        /// <summary>
        /// Server-side: guarantees this body has an owning anchor. Fresh placements mint one (guid +
        /// payload capture + Spawned connection); pre-anchor bodies from old worlds migrate organically
        /// the first time they load. Idempotent — a body already carrying an anchor guid is left alone.
        /// </summary>
        /// <summary>Server-side duplicate check: true when this body's anchor guid is already owned by a
        /// DIFFERENT live body — the hammer-delete → anchor-respawn → undo race that leaves two bodies on one
        /// anchor. Bounded scan over the small anchor set, once at init. Never trips for a legit body: an
        /// un-anchored body has no guid, and a real body's anchor connection points back at itself.</summary>
        private bool IsDuplicateOfAnchoredBody(ZDO zdo)
        {
            try
            {
                if (zdo == null) return false;
                string guid = zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "");
                if (string.IsNullOrEmpty(guid)) return false;   // un-anchored (e.g. hand-placed) — can't be an anchor dupe
                var anchors = new List<ZDO>();
                int idx = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, anchors, ref idx)) { }
                foreach (var a in anchors)
                {
                    if (a == null || !a.IsValid()) continue;
                    if (a.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, "") != guid) continue;
                    var linked = a.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                    if (linked.IsNone() || linked == zdo.m_uid) return false;   // anchor owns us / no one — we're legit
                    var other = ZDOMan.instance.GetZDO(linked);
                    return other != null && other.IsValid();                    // anchor owns a DIFFERENT live body → we're the stray
                }
                return false;   // no anchor carries our guid — not an anchor duplicate
            }
            catch { return false; }
        }

        private void EnsureAnchorForBody(ZDO bodyZdo, bool organicMigration = false)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (bodyZdo == null) return;
                string existingGuid = bodyZdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "");
                if (!string.IsNullOrEmpty(existingGuid))
                {
                    // Already anchored — but anchors minted before the ground-snap fix sit at the
                    // body's LIFTED pre-init position (head height). Re-seat to the surface underfoot.
                    HealAnchorHeight(bodyZdo, existingGuid);
                    return;
                }

                var anchorPrefab = ZNetScene.instance != null
                    ? ZNetScene.instance.GetPrefab(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName)
                    : null;
                if (anchorPrefab == null)
                {
                    Debug.LogWarning("[StaticNpcInitializer] FiresNpcAnchor prefab not registered — body stays un-anchored (old bundle?)");
                    return;
                }

                // A body that arrives ALREADY-INITIALIZED but UNANCHORED was created by something
                // outside the anchor system (every keeper spawn carries its back-ref; fresh placements
                // mint on their own first-init path). That is the duplicate-NPC signature — name the
                // creator loudly before minting.
                if (organicMigration)
                    Debug.LogWarning($"[StaticNpcInitializer] UNANCHORED pre-seeded body '{gameObject.name}' at {transform.position} " +
                        $"is getting a fresh anchor — creator stack:\n{_creationStack}");

                Vector3 anchorPos = bodyZdo.GetVec3("npc_placed_anchor", transform.position);
                // The orb must sit on whatever the NPC stands on (build piece or terrain), never at
                // head height — legacy npc_placed_anchor values predate the feet-probe.
                TryProbeSurface(anchorPos, out anchorPos);
                var anchorGo = UnityEngine.Object.Instantiate(anchorPrefab, anchorPos, _placementRotation);
                var anchorView = anchorGo.GetComponent<ZNetView>();
                if (anchorView == null || !anchorView.IsValid())
                {
                    Debug.LogError("[StaticNpcInitializer] spawned anchor has no valid ZNetView — destroying");
                    UnityEngine.Object.Destroy(anchorGo);
                    return;
                }
                var anchorZdo = anchorView.GetZDO();

                string guid = System.Guid.NewGuid().ToString("N");
                anchorZdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, guid);
                anchorZdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyPrefab, global::Utils.GetPrefabName(gameObject));
                anchorZdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Rotation, _placementRotation);

                // Link FIRST, then snapshot: the capture mirrors the body onto the anchor, and the
                // anchor's first payload must already contain the back-ref so a keeper seed can never
                // hand out a guid-less body (a body without its back-ref re-anchors itself = duplicate).
                bodyZdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, guid);
                global::FiresCore.Bridge.NpcAnchorBridge.InvokeCaptureToAnchor(bodyZdo, anchorZdo);
                anchorZdo.SetConnection(ZDOExtraData.ConnectionType.Spawned, bodyZdo.m_uid);

                Debug.Log($"[StaticNpcInitializer] minted anchor {guid} at {anchorPos} for '{gameObject.name}'");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[StaticNpcInitializer] EnsureAnchorForBody failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Finds the walkable surface underfoot — build pieces AND terrain (piece/static_solid/terrain
        /// layers), unlike ZoneSystem.GetGroundHeight which is heightmap-only and can neither see floors
        /// nor custom terrain. Returns pos with y snapped to the surface; false (pos unchanged) when no
        /// surface exists within 8m below.
        /// </summary>
        private static bool TryProbeSurface(Vector3 pos, out Vector3 feet)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "Default_small");
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 8f, mask, QueryTriggerInteraction.Ignore))
            {
                feet = new Vector3(pos.x, hit.point.y, pos.z);
                return true;
            }
            feet = pos;
            return false;
        }

        /// <summary>
        /// Server-side: re-seats an EXISTING anchor onto the surface underfoot. Anchors minted before
        /// the ground-snap fix inherited the body's lifted pre-init position (orb at head height);
        /// this runs on every server materialization of an anchored body and moves the anchor ZDO +
        /// live orb down to the surface the NPC actually stands on. No-op once seated.
        /// </summary>
        private void HealAnchorHeight(ZDO bodyZdo, string guid)
        {
            try
            {
                foreach (var keeper in UnityEngine.Object.FindObjectsByType<global::FiresCore.Npc.Anchor.NpcAnchorKeeper>(FindObjectsSortMode.None))
                {
                    var view = keeper.GetComponent<ZNetView>();
                    var anchorZdo = view != null && view.IsValid() ? view.GetZDO() : null;
                    if (anchorZdo == null) continue;
                    if (anchorZdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, "") != guid) continue;

                    Vector3 anchorPos = anchorZdo.GetPosition();
                    if (!TryProbeSurface(anchorPos, out var feet)) return;
                    if (Mathf.Abs(anchorPos.y - feet.y) <= 0.4f) return;

                    anchorZdo.SetPosition(feet);
                    keeper.transform.position = feet;
                    bodyZdo.Set("npc_placed_anchor", feet);
                    Debug.Log($"[StaticNpcInitializer] healed anchor height {guid}: {anchorPos} -> {feet}");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[StaticNpcInitializer] anchor height heal failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Ensures the static NPC's companion components are configured as a pre-stationed NPC.
        /// The prefab already has the full companion component stack (Humanoid, CompanionAI,
        /// CompanionController, CompanionIdleBehavior, etc.) from SetupCompanionPrefab.
        /// This method configures the runtime instance so it behaves like a companion that
        /// was stationed via Shift+P, with the sole restriction that it cannot be unstated.
        /// </summary>
        // 0.0.509 (the lead 18:3x, possession_test 3/4: "walked 9.4 m off its anchor; Core: walked in 0.0 s"): the anchor a static
        // NPC is stationed at is where it was PLACED: npc_placed_anchor (written once at placement), else npc_stationed_pos, else
        // the spot the body was spawned at (read in Awake, before anyone could possess it). Never the live body's position.
        private Vector3 PlacedAnchor(ZDO zdo)
        {
            Vector3 fallback = _awakePosition != Vector3.zero ? _awakePosition : transform.position;
            if (zdo == null) return fallback;
            return zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", fallback));
        }

        // A player can possess the body before this setup runs (it waits for networking and the ZDO). Setting up then would
        // re-anchor it where the player walked it and freeze it under them. So: give Core's module the PLACED anchor now (its
        // release walks the body home to it; the body stays where the player has it), and run the setup only once the
        // possession has ended and the walk home is done (back within 1.5 m, or 20 s).
        private IEnumerator WaitOutPossession(ZDO zdo)
        {
            if (!FiresCore.Npc.CompanionPossession.IsPossessed(_nview)) yield break;
            Vector3 anchor = PlacedAnchor(zdo);
            var module = GetComponent<CompanionNpcModule>();
            if (module != null)
            {
                Vector3 bodyAt = transform.position;
                var rb = GetComponent<Rigidbody>();
                module.isStationedAsNpc = true;
                module.isStaticPlacement = true;
                module.SetStationedPositionDirect(anchor, _placementRotation);   // sets the anchor (and moves the transform: put back below)
                transform.position = bodyAt;
                if (rb != null) rb.position = bodyAt;
            }
            FiresCore.Logging.FiresLogger.LogDebug($"[StaticNpcInitializer] '{gameObject.name}' is possessed: setup waits; anchor {anchor} (placed), body at {transform.position}");
            while (FiresCore.Npc.CompanionPossession.IsPossessed(_nview)) yield return null;
            float until = Time.time + 20f;
            while (Time.time < until && Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(anchor.x, anchor.z)) > 1.5f)
                yield return null;
            FiresCore.Logging.FiresLogger.LogDebug($"[StaticNpcInitializer] '{gameObject.name}' possession over: {Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(anchor.x, anchor.z)):0.0} m from its anchor, setup runs");
        }

        private void EnsureCompanionNpcComponents(ZDO zdo)
        {
            // --- CompanionController: mark as tamed so idle behaviors treat it like a companion ---
            var companion = GetComponent<CompanionController>();
            if (companion != null)
            {
                companion.isTamed = true;
                var npcController = GetComponent<NpcController>();
                if (npcController != null && !string.IsNullOrEmpty(npcController.displayNameOverride))
                {
                    companion.companionName = npcController.displayNameOverride;
                    // Push the display name onto Character.m_name / Humanoid.m_name so the
                    // EnemyHud floating label reads "Gunnar" instead of the bundle prefab
                    // name "StaticNpc". Without this, only the hover prompt (which reads
                    // GetHoverName → displayNameOverride) shows the right name; the floating
                    // label above the NPC stays as whatever the prefab was baked with.
                    companion.UpdateCharacterName(npcController.displayNameOverride);
                }
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
            // Use _placementRotation captured in Awake() from the ZDO - transform.rotation
            // cannot be trusted since Character/AI systems may have overwritten it.
            npcModule.isStationedAsNpc = true;
            // 0.0.509: a STATIC fixture is stationed at its PLACED anchor, never the live body (a possessed body walked 9 m and
            // became its own anchor). A wanderer / patrol NPC keeps the live spot as before: the placed anchor would teleport it
            // home from mid-route on every load.
            bool fixtureNow = !npcModule.allowIdleWandering
                              && GetComponent<FiresCore.Npc.Patrol.PatrolAssignment>()?.HasRoute != true
                              && (npcCtrl == null || string.IsNullOrEmpty(npcCtrl.patrolRouteName));
            npcModule.SetStationedPositionDirect(fixtureNow ? PlacedAnchor(zdo) : transform.position, _placementRotation);
            npcModule.ApplyStationedState();

            // Re-enable Humanoid so the NPC can animate and be interacted with.
            var hum = GetComponent<Humanoid>();
            if (hum != null) hum.enabled = true;

            bool willMove = npcModule.allowIdleWandering
                            || GetComponent<FiresCore.Npc.Patrol.PatrolAssignment>()?.HasRoute == true
                            || (npcCtrl != null && !string.IsNullOrEmpty(npcCtrl.patrolRouteName));

            // ── Body stage (the three-stage contract) ────────────────────────────────────────────
            // STATIC (no wander, no route): a Marketplace-style fixture. Ghost-flagged so enemy AI
            // never targets or alerts on it (vanilla Character.m_aiSkipTarget — the BaseAI.FindEnemy
            // skip), every AI brain disabled, and the body pinned by zeroed velocities + XZ/rotation
            // freeze constraints. Deliberately NOT isKinematic: the companion movement stack writes
            // velocities, and Unity 6 warns on every velocity write to a kinematic body. Gravity stays
            // on (Y free) so the body settles onto whatever it was placed on — terrain OR a floor.
            // WANDER/PATROL: full companion behavior (fights, defends, dies); the anchor keeper owns
            // death respawn. Patrol keeps MonsterAI OFF — the route mover drives, MonsterAI would
            // fight it (see the old stand-still-and-get-dragged glitch).
            ApplyBodyStage(npcModule, willMove);

            // Force the placement rotation one final time after all setup
            transform.rotation = _placementRotation;

            // Ground-truth rotation trace (client side, end of the path): the yaw the ZDO carried and the
            // yaw the body ACTUALLY ends up facing after every AI/Character system has had its say. Pairs
            // with the server-side [fires_migrate_kg][rot] line — if these two yaws match kg's original and
            // the NPC still looks backward, the offset is in the BODY's forward axis, not the data. The
            // Visual child's LOCAL yaw is the smoking gun: if the baked mesh sits ~180 off the root, THAT
            // is why NPCs look backward no matter what root yaw we write — provable, not eyeballed.
            var visualT = transform.Find("Visual");
            string visualYaw = visualT != null
                ? visualT.localRotation.eulerAngles.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                : "<no Visual>";
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[StaticNpc][rot] '{0}' zdoYaw={1:F1} finalTransformYaw={2:F1} visualLocalYaw={3} pos=({4:F0},{5:F0})",
                gameObject.name, _placementRotation.eulerAngles.y, transform.rotation.eulerAngles.y,
                visualYaw, transform.position.x, transform.position.z));

            // Only the server should write to ZDO - clients can't write ZDOs they don't own
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            if (isServer)
            {
                zdo.Set("npc_static_placement", true);
                npcModule.SaveToZDO();
            }

            // --- Configure idle behavior home position and enable if wandering OR on a patrol route ---
            // A stationary patrol NPC has allowIdleWandering=false, but its patrol is driven through this
            // same CompanionIdleBehavior. Enabling only on wandering here would clobber the enable that
            // NpcController.SyncPatrolAssignment set 0.5s earlier (this init runs at ~1s) — that's exactly
            // why patrol didn't auto-resume on relog. Keep it enabled whenever a route is assigned.
            idleBehavior.enabled = willMove;   // wandering OR on a patrol route (computed above)
            if (npcModule.allowIdleWandering)
            {
                // Wander is clamped around the ANCHOR (the durable placement record), not wherever the
                // body happens to stand right now — a respawned/knocked-around wanderer keeps its
                // original territory. Radius defaults to 20m (idleWanderRadius default).
                Vector3 wanderHome = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", transform.position));
                idleBehavior.SetHomePosition(wanderHome, npcModule.idleWanderRadius);
            }

            // Load the NPC's hand weapon(s) into CompanionInventory as real combat ItemData so it can actually
            // attack + weapon-swap when defending its post. Static-NPC gear is otherwise visual-only
            // (NpcVisEquipment), which leaves the combat system with no weapon. Silent = no visual repaint.
            if (npcCtrl != null)
            {
                var inv = GetComponent<global::FiresCore.Npc.CompanionInventory>();
                if (inv != null)
                {
                    LoadCombatHand(inv, global::FiresCore.Npc.CompanionInventory.EquipmentSlot.RightHand,
                                   npcCtrl.equipRightHandData, npcCtrl.equipRightHand);
                    LoadCombatHand(inv, global::FiresCore.Npc.CompanionInventory.EquipmentSlot.LeftHand,
                                   npcCtrl.equipLeftHandData, npcCtrl.equipLeftHand);
                }
            }
        }

        /// <summary>
        /// Configures the body for its current stage. STATIC = Marketplace fixture: ghost flag
        /// (m_aiSkipTarget), all AI brains off, zeroed velocities + XZ/rotation freeze (non-kinematic —
        /// velocity writes on a kinematic body spam Unity 6 warnings). WANDER = CompanionAI + MonsterAI
        /// + free physics. PATROL = CompanionAI + free physics, MonsterAI off (the route mover drives).
        /// Runs on EVERY machine — m_aiSkipTarget must be set wherever an attacking monster is owned.
        /// </summary>
        private void ApplyBodyStage(CompanionNpcModule npcModule, bool willMove)
        {
            var character = GetComponent<Character>();
            if (character != null) character.m_aiSkipTarget = !willMove;

            var companionAI = GetComponent<CompanionAI>();
            var mai = GetComponent<MonsterAI>();
            var rb = GetComponent<Rigidbody>();

            if (!willMove)
            {
                if (mai != null) mai.enabled = false;
                if (companionAI != null) companionAI.enabled = false;
                if (rb != null)
                {
                    // FULLY frozen (all axes): a static fixture never moves, its position IS the ZDO
                    // position, so terrain readiness can never drop it through the world and nothing
                    // can shove it (mobs, rocks, depenetration). Y deliberately included — the old
                    // gravity-settle let zone reloads drop the body under the terrain before the
                    // heightmap was built (then vanilla's rescue popped it back up).
                    rb.isKinematic = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                // Pin the FEET exactly at the anchor point. Something in the companion spawn stack
                // lifts fresh bodies several meters (logs: spawn Y 32.7 → ZDO Y 35+ before init
                // finished), which after an anchor move read as "respawns 5m above the orb and
                // floats down". The anchor position IS the truth — snap to it, with a short
                // downward probe (pieces + terrain) so a floor placement lands ON the floor.
                var pinZdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                if (pinZdo != null)
                {
                    Vector3 home = pinZdo.GetVec3("npc_placed_anchor", pinZdo.GetVec3("npc_stationed_pos", transform.position));
                    // Migrated kg NPCs carry an EXACT position (bench-seat height for sitters, counter
                    // height for crafters). Probing to the terrain surface would drop a sitting NPC to
                    // the ground / leave her floating above the seat — use kg's Y verbatim.
                    Vector3 pinned = pinZdo.GetBool("npc_exact_placement", false) ? home : (TryProbeSurface(home, out var probed) ? probed : home);
                    transform.position = pinned;
                    if (rb != null) rb.position = pinned;
                }
            }
            else
            {
                if (companionAI != null) companionAI.enabled = true;
                if (mai != null) mai.enabled = npcModule != null && npcModule.allowIdleWandering;
                if (rb != null)
                {
                    rb.constraints = RigidbodyConstraints.FreezeRotation;
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }
            }

            // [ai] Ground-truth: prove whether this body is a static fixture (willMove=false → AI OFF +
            // aiSkipTarget) or a mover on THIS spawn, and expose the exact willMove inputs. A migrated
            // non-patrol NPC that shows willMove=true, or AI enabled after this, is the "NPC dodges/looks at
            // enemies on relog" bug — this line names the cause (bad willMove data) vs a downstream re-enable.
            Debug.Log(string.Format(
                "[StaticNpc][ai] '{0}' willMove={1} (wander={2} hasRoute={3} route='{4}') static={5} -> aiSkip={6} companionAI={7} monsterAI={8}",
                gameObject.name, willMove,
                npcModule != null && npcModule.allowIdleWandering,
                GetComponent<FiresCore.Npc.Patrol.PatrolAssignment>()?.HasRoute == true,
                GetComponent<NpcController>()?.patrolRouteName ?? "",
                npcModule != null && npcModule.isStaticPlacement,
                character != null && character.m_aiSkipTarget,
                companionAI != null && companionAI.enabled,
                mai != null && mai.enabled));
        }

        /// <summary>
        /// Re-resolves and re-applies the body stage after an admin config change (idle-wander toggle or
        /// patrol route assignment from the book). Public so the book's Apply path can flip a live body
        /// between fixture and companion behavior without a respawn.
        /// </summary>
        public void ReapplyBodyStage()
        {
            var npcModule = GetComponent<CompanionNpcModule>();
            var npcCtrl = GetComponent<NpcController>();
            bool willMove = (npcModule != null && npcModule.allowIdleWandering)
                            || GetComponent<FiresCore.Npc.Patrol.PatrolAssignment>()?.HasRoute == true
                            || (npcCtrl != null && !string.IsNullOrEmpty(npcCtrl.patrolRouteName));
            ApplyBodyStage(npcModule, willMove);

            var idleBehavior = GetComponent<CompanionIdleBehavior>();
            if (idleBehavior != null)
            {
                idleBehavior.enabled = willMove;
                if (npcModule != null && npcModule.allowIdleWandering)
                {
                    var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                    Vector3 wanderHome = zdo != null
                        ? zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", transform.position))
                        : transform.position;
                    idleBehavior.SetHomePosition(wanderHome, npcModule.idleWanderRadius);
                }
            }
        }

        /// <summary>Waits for the appearance restore to finish dressing the body, then backfills the
        /// hammer-tab template icon for templates saved before icons existed (client-side render of
        /// the live dressed NPC). SavedNpcManager skips it instantly when an icon already exists.</summary>
        private IEnumerator BackfillIconWhenDressed()
        {
            yield return new WaitForSeconds(4f);   // RestoreAppearanceFromZDO + gear visuals settle
            try
            {
                var ctrl = GetComponent<NpcController>();
                if (ctrl != null) SavedNpcManager.BackfillTemplateIcon(ctrl);
            }
            catch (System.Exception ex)
            {
                FiresLogger.LogVerbose("[StaticNpcInitializer] icon backfill failed: " + ex.Message);
            }
        }

        /// <summary>Arms a static NPC's hand slot for combat: prefer the full serialized ItemData (real
        /// quality/upgrades/customData the player equipped); fall back to a prefab clone (quality 1) for
        /// randomized/legacy NPCs that never stored full data.</summary>
        private static void LoadCombatHand(global::FiresCore.Npc.CompanionInventory inv,
                                           global::FiresCore.Npc.CompanionInventory.EquipmentSlot slot,
                                           string itemBlob, string prefabName)
        {
            if (!string.IsNullOrEmpty(itemBlob))
            {
                var item = global::FiresCore.Npc.CompanionInventory.DeserializeItemData(itemBlob);
                if (item != null && inv.LoadCombatItemSilent(slot, item)) return;
            }
            if (!string.IsNullOrEmpty(prefabName))
                inv.LoadCombatWeaponSilent(slot, prefabName);
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
            var valid = CompanionRandomLoadout.GetAvailableHairStyles().Where(h => h != "HairNone").ToArray();
            if (valid.Length == 0 || Random.value < 0.1f) return "";
            return valid[Random.Range(0, valid.Length)];
        }

        private string PickRandomBeardStyle()
        {
            var valid = CompanionRandomLoadout.GetAvailableBeardStyles().Where(b => b != "BeardNone").ToArray();
            if (valid.Length == 0) return "";
            return valid[Random.Range(0, valid.Length)];
        }

        private const int OutfitSeedSalt = 0x51A7_1C03;
        private static readonly CompanionKitChances OutfitChances = new CompanionKitChances { Helmet = 0.4f, Chest = 0.7f, Legs = 0.6f, Cape = 0.3f };

        /// <summary>
        /// Dresses the NPC in one archetype's kit from the companion gear table, at its biome's tier (seeded by the ZDO).
        /// Equipment is stored in NpcController fields and ZDO.
        /// </summary>
        private void RandomizeEquipment(ZDO zdo, NpcController npcController)
        {
            if (!CompanionGearTable.Prepare()) return;

            var rng = new System.Random(unchecked(zdo.m_uid.GetHashCode() ^ OutfitSeedSalt));
            int biomeTier = CompanionGearTable.BiomeTier(Heightmap.FindBiome(transform.position));
            var archetype = CompanionGearTable.RollArchetype(biomeTier, rng);
            var kit = CompanionGearTable.BuildKit(archetype, CompanionGearTable.RollKitTier(biomeTier, rng), rng, OutfitChances);

            string helmet = kit.Helmet ?? "";
            string chest = kit.Chest ?? "";
            string leg = kit.Legs ?? "";
            string shoulder = kit.Shoulder ?? "";
            string rightHand = kit.RightHand ?? "";
            string leftHand = kit.LeftHand ?? "";

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
            // Set the body MODEL via NpcVisEquipment (and clear any vanilla hair/beard), but route HAIR and
            // BEARD through the SAME fixed NpcFashionManager bone-binding pipeline companions use. The vanilla
            // VisEquipment.SetHairItem path needs m_isPlayer=true + an active update loop and mis-scales NPC
            // hair (the 100x "block out the sun" bug we fixed for companions); static NPCs now share the fixed
            // pipeline so they get correctly-sized hair/beard.
            visEquip.SetAppearance(modelIndex, "", "");
            string beardForGender = modelIndex == 1 ? "" : beard;
            string npcHairColor = NpcFashionManager.ColorToString(new Color(hairColorVec.x, hairColorVec.y, hairColorVec.z));
            FiresCore.Bridge.NpcFashionBridge.ApplyHair(gameObject, hair, npcHairColor);
            FiresCore.Bridge.NpcFashionBridge.ApplyBeard(gameObject, beardForGender, npcHairColor);

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

            // Restore scale — visual-only (kg parity). Root scaling compounded with the fashion
            // ModelScale into scale² giants; ApplyModelScale owns the single application point.
            float scale = zdo.GetFloat("npc_scale", 1f);
            NpcFashionManager.ApplyModelScale(npcController, scale);

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
            // Set the body MODEL via NpcVisEquipment (and clear any vanilla hair/beard), but route HAIR and
            // BEARD through the SAME fixed NpcFashionManager bone-binding pipeline companions use. The vanilla
            // VisEquipment.SetHairItem path needs m_isPlayer=true + an active update loop and mis-scales NPC
            // hair (the 100x "block out the sun" bug we fixed for companions); static NPCs now share the fixed
            // pipeline so they get correctly-sized hair/beard.
            visEquip.SetAppearance(modelIndex, "", "");
            string beardForGender = modelIndex == 1 ? "" : beard;
            string npcHairColor = NpcFashionManager.ColorToString(new Color(hairColorVec.x, hairColorVec.y, hairColorVec.z));
            FiresCore.Bridge.NpcFashionBridge.ApplyHair(gameObject, hair, npcHairColor);
            FiresCore.Bridge.NpcFashionBridge.ApplyBeard(gameObject, beardForGender, npcHairColor);

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

            // Restore scale — visual-only (kg parity), same single application point as fresh init.
            float scale = zdo.GetFloat("npc_scale", 1f);
            NpcFashionManager.ApplyModelScale(npcController, scale);
        }
    }
}
