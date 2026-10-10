using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.Modules.Companions.NpcMode;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Managers
{
    public static class VAPieceManager
    {
        // All pieces that go in the hammer (from pieces/ folder)
        public static readonly List<string> PieceNames = new List<string>
        {
        };

        // NPC prefabs (from npc/ folder) - these also go in hammer
        public static readonly List<string> NpcPrefabNames = new List<string>
        {
            "StaticNpc"
        };

        // Network-only prefabs (from npc/ folder): ZNetScene-registered on every machine so their
        // persistent ZDOs survive world loads, but NOT added to the hammer piece table. The NPC anchor
        // is placed indirectly (placing an NPC piece creates its anchor) and moved via its context menu.
        public static readonly List<string> NetworkOnlyPrefabNames = new List<string>
        {
            FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName
        };
        public static readonly List<GameObject> NetworkOnlyPrefabs = new List<GameObject>();

        /// <summary>
        /// The GameObject the hammer actually places for "Static Quest NPC" — a runtime clone of
        /// Core's WORKING CompanionNpc prefab, renamed "StaticNpc". Companions place perfectly and
        /// static NPCs are the same prefab in Unity, but the firesnpcs bundle's StaticNpc asset is
        /// rotten (baked Piece + broken body material) — so placement gets companion-parity BY
        /// CONSTRUCTION: same donor asset, same component stack, plus StaticNpcInitializer. The
        /// clone keeps the name "StaticNpc" so a placed body's ZDO hash resolves to the (repaired)
        /// registered bundle prefab on every future zone load — proven working by `spawn StaticNpc`.
        /// The bundle prefab stays ZNetScene-registered for exactly that resolution; it is no longer
        /// what the hammer places.
        /// </summary>
        public static GameObject StaticNpcPiecePrefab { get; private set; }
        private static GameObject _staticNpcPieceHolder;

        /// <summary>Builds <see cref="StaticNpcPiecePrefab"/> once Core's companion prefabs exist.</summary>
        public static GameObject EnsureStaticNpcPiecePrefab()
        {
            if (StaticNpcPiecePrefab != null) return StaticNpcPiecePrefab;
            try
            {
                var donor = CompanionPrefabManager.GetLoadedCompanionPrefabs()
                    ?.Find(p => p != null && p.name == "CompanionNpc");
                if (donor == null)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: CompanionNpc prefab not loaded — static NPC piece unavailable");
                    return null;
                }

                if (_staticNpcPieceHolder == null)
                {
                    _staticNpcPieceHolder = new GameObject("FiresStaticNpcPieceHolder");
                    _staticNpcPieceHolder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_staticNpcPieceHolder);
                }

                var clone = FiresCore.Net.NetworkObjectHelper.InstantiateInactiveClone(donor);
                if (clone == null) return null;
                clone.name = "StaticNpc";
                clone.transform.SetParent(_staticNpcPieceHolder.transform, false);

                SetupNpcPieceDefaults(clone, "StaticNpc");
                StaticNpcPiecePrefab = clone;
                Debug.Log($"{FiresRPGmaker.PluginName}: built StaticNpc hammer piece from the CompanionNpc donor (companion-parity placement)");
                return clone;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{FiresRPGmaker.PluginName}: EnsureStaticNpcPiecePrefab failed: {ex.Message}");
                return null;
            }
        }

        public static void LoadPieces(string mainPath, string modInstance)
        {
            if (FiresRPGmaker.assetBundle == null)
            {
                Debug.LogError($"{FiresRPGmaker.PluginName}: AssetBundle is null.");
                return;
            }

            FiresRPGmaker.piecePrefabs.Clear();
            var allAssets = FiresRPGmaker.assetBundle.GetAllAssetNames();
            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: AssetBundle contains {allAssets.Length} assets: {string.Join(", ", allAssets)}");

            int failed = 0;

            // Load regular pieces from pieces/ folder
            foreach (var pieceName in PieceNames)
            {
                var prefab = LoadPiecePrefab(mainPath + "pieces/" + pieceName + ".prefab", pieceName);
                if (prefab != null)
                {
                    SetupPieceDefaults(prefab, pieceName);
                    FiresRPGmaker.piecePrefabs.Add(prefab);
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Loaded piece prefab: {pieceName}");
                }
                else
                {
                    failed++;
                }
            }

            // Load NPC prefabs from npc/ folder (in vaitems)
            // StaticNpc is a dedicated bundle prefab for quest NPCs - safe to modify directly.
            // BaseNpc is handled separately by CompanionPrefabManager for companion placement.
            foreach (var npcName in NpcPrefabNames)
            {
                var prefab = LoadNpcPrefab(npcName);
                if (prefab != null)
                {
                    SetupNpcPieceDefaults(prefab, npcName);
                    FiresRPGmaker.piecePrefabs.Add(prefab);
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Loaded NPC prefab: {npcName}");
                }
                else
                {
                    failed++;
                }
            }

            // Network-only prefabs (the NPC anchor). Lenient: an older embedded bundle without the
            // prefab logs a single warning instead of the multi-path error spam, and the anchor
            // system simply stays dormant until the bundle carries it.
            NetworkOnlyPrefabs.Clear();
            foreach (var netName in NetworkOnlyPrefabNames)
            {
                var prefab = FiresRPGmaker.assetBundle.LoadAsset<GameObject>($"assets/custom/vaitems/npc/{netName.ToLower()}.prefab")
                             ?? FiresRPGmaker.assetBundle.LoadAsset<GameObject>(netName);
                if (prefab == null)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: network-only prefab '{netName}' not in bundle (old bundle?) — anchor system dormant");
                    continue;
                }
                UnityEngine.Object.DontDestroyOnLoad(prefab);   // same fake-null protection as the pieces
                SetupAnchorPrefabDefaults(prefab);
                NetworkOnlyPrefabs.Add(prefab);
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Loaded network-only prefab: {netName}");
            }

            Debug.Log($"{FiresRPGmaker.PluginName}: Loaded {FiresRPGmaker.piecePrefabs.Count} pieces. Failed: {failed}");
        }

        /// <summary>
        /// The FiresNpcAnchor recipe (baked-empty-prefab + runtime-attach, per the FiresEnvBox lesson —
        /// a bare new GameObject in m_prefabs is the empty-AssetID trap): persistent ZNetView so the
        /// anchor ZDO IS the NPC's durable record, plus the Core keeper that guarantees its body exists.
        /// </summary>
        private static void SetupAnchorPrefabDefaults(GameObject prefab)
        {
            var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;

            if (prefab.GetComponent<FiresCore.Npc.Anchor.NpcAnchorKeeper>() == null)
                prefab.AddComponent<FiresCore.Npc.Anchor.NpcAnchorKeeper>();
        }

        private static GameObject LoadPiecePrefab(string prefabPath, string pieceName)
        {
            // Try multiple paths where the piece might be located
            string[] searchPaths = {
                // Primary location - VAitems/pieces folder
                $"assets/custom/vaitems/pieces/{pieceName}.prefab",
                $"assets/custom/vaitems/pieces/{pieceName.ToLower()}.prefab",
                // Fallback to passed-in path
                prefabPath,
                // Other possible locations
                $"Assets/Custom/VAitems/pieces/{pieceName}.prefab",
                $"Assets/Custom/NPCs/pieces/{pieceName}.prefab",
                $"assets/custom/npcs/pieces/{pieceName}.prefab",
                $"{pieceName}.prefab",
                pieceName
            };

            foreach (var path in searchPaths)
            {
                string normalizedPath = path.Replace('\\', '/').ToLower();
                GameObject piecePrefab = FiresRPGmaker.assetBundle.LoadAsset<GameObject>(normalizedPath);
                if (piecePrefab != null)
                {
                    var piece = piecePrefab.GetComponent<Piece>();
                    if (piece == null)
                    {
                        Debug.LogError($"{FiresRPGmaker.PluginName}: {pieceName} - Piece component missing!");
                        UnityEngine.Object.Destroy(piecePrefab);
                        return null;
                    }

                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Found piece prefab at path: {normalizedPath}");
                    // CRITICAL: protect the bundle prefab from scene-unload destruction.
                    // See VAItemManager.LoadItems for the full rationale - the static
                    // piecePrefabs list survives logout but the bundle GameObjects do
                    // not unless DDOL'd, which leaves Unity fake-null references that
                    // crash other mods on the next world load.
                    UnityEngine.Object.DontDestroyOnLoad(piecePrefab);
                    return piecePrefab;
                }
            }

            // Log all assets to help debug
            Debug.LogError($"{FiresRPGmaker.PluginName}: Failed to load piece prefab '{pieceName}'. Tried paths:");
            foreach (var path in searchPaths)
            {
                Debug.LogError($"  - {path.ToLower()}");
            }

            return null;
        }

        private static GameObject LoadNpcPrefab(string npcName)
        {
            // Try multiple paths where the NPC might be located
            string[] searchPaths = {
                $"assets/custom/vaitems/npc/{npcName}.prefab",
                $"assets/custom/vaitems/npc/{npcName.ToLower()}.prefab",
                $"Assets/Custom/VAitems/NPC/{npcName}.prefab",
                $"Assets/Custom/VAitems/npc/{npcName}.prefab",
                $"assets/custom/npcs/{npcName}.prefab",
                $"Assets/Custom/NPCs/npc/{npcName}.prefab",
                $"{npcName}.prefab",
                npcName
            };

            foreach (var path in searchPaths)
            {
                string normalizedPath = path.Replace('\\', '/').ToLower();
                GameObject prefab = FiresRPGmaker.assetBundle.LoadAsset<GameObject>(normalizedPath);
                if (prefab != null)
                {
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Found NPC prefab at path: {normalizedPath}");
                    // Protect from scene-unload destruction - see LoadPiecePrefab.
                    UnityEngine.Object.DontDestroyOnLoad(prefab);
                    return prefab;
                }
            }

            // Log all assets to help debug
            Debug.LogError($"{FiresRPGmaker.PluginName}: Failed to load NPC prefab '{npcName}'. Tried paths:");
            foreach (var path in searchPaths)
            {
                Debug.LogError($"  - {path.ToLower()}");
            }

            return null;
        }

        private static void SetupPieceDefaults(GameObject piecePrefab, string pieceName)
        {
            var piece = piecePrefab.GetComponent<Piece>();
            if (piece == null) return;

            // Log the category from Unity
            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: {pieceName} loaded with category={piece.m_category} from Unity");

            
            

            // Default category for other pieces only if not set in Unity
            if (piece.m_category == Piece.PieceCategory.Max)
                piece.m_category = Piece.PieceCategory.Furniture;

            if (piece.m_craftingStation == null)
            {
                var workbench = ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                piece.m_craftingStation = workbench;
            }
        }

        private static void SetupNpcPieceDefaults(GameObject npcPrefab, string npcName)
        {
            // Give the StaticNpc the same full companion component stack (Humanoid, CompanionAI,
            // CompanionController, CompanionIdleBehavior, etc.) so it can wander, emote, sit,
            // and do everything a stationed companion can. StaticNpcInitializer configures the
            // instance at runtime as a pre-stationed NPC that cannot be converted to a follower.
            CompanionPrefabManager.SetupCompanionPrefab(npcPrefab, npcName);

            // Mark as static placement so CompanionPatches routes interaction to NpcController
            var companionCtrl = npcPrefab.GetComponent<CompanionController>();
            if (companionCtrl != null)
                companionCtrl.isStaticPlacement = true;

            // Disable Tameable so wild companion taming logic never runs
            var tameable = npcPrefab.GetComponent<Tameable>();
            if (tameable != null)
                tameable.enabled = false;

            // CRITICAL: Disable CompanionIdleBehavior on the prefab so static NPCs
            // default to standing still. StaticNpcInitializer.EnsureCompanionNpcComponents()
            // will enable it later ONLY if allowIdleWandering is true in the ZDO.
            var idleBehavior = npcPrefab.GetComponent<CompanionIdleBehavior>();
            if (idleBehavior != null)
                idleBehavior.enabled = false;

            // CRITICAL: Destroy any bundle-baked Piece component and add a fresh one.
            // The bundle prefab carries a Piece with broken cross-bundle EffectList
            // prefab references that cause "The Object you want to instantiate is null"
            // in Player.PlacePiece. Companions (BaseNpc/CompanionNpc) never hit this
            // because they don't ship with a Piece in the bundle - they get one added
            // fresh via AddComponent in AddCompanionsToPieceTable. We do the same here.
            // IN-PLACE CONFIGURATION (2026-07): the old DestroyImmediate-and-re-add here silently
            // FAILED — the loaded bundle prefab is a persistent ASSET, and Unity refuses component
            // destruction on assets. The baked Piece (red-shield icon + "Static talkers" description
            // authored in Unity) survived while AddComponent stacked a SECOND Piece: the hammer showed
            // two "StaticNpc" tiles and placement ran whichever component won. Configure the baked
            // Piece in place (keeps its icon, guarantees ONE Piece); the broken cross-bundle
            // EffectList refs are cured by the m_placeEffect REPLACEMENT below, which was always the
            // real fix. Belt: strip runtime-ADDED extras (those destroy fine) from older DLL runs.
            var allPieces = npcPrefab.GetComponents<Piece>();
            for (int i = 1; i < allPieces.Length; i++)
            {
                try { UnityEngine.Object.DestroyImmediate(allPieces[i]); }
                catch { /* asset-baked extras can't be destroyed; first wins below */ }
            }
            var piece = npcPrefab.GetComponent<Piece>();
            if (piece == null)
                piece = npcPrefab.AddComponent<Piece>();

            // Set piece properties - StaticNpc gets its own name/description
            if (npcName.Equals("StaticNpc", StringComparison.OrdinalIgnoreCase))
            {
                piece.m_name = "Static NPC";
                piece.m_description = "A stationed NPC with a random look and no role. Place from hammer, dress it up, then assign a role via Shift+E if wanted.";
            }
            else
            {
                piece.m_name = "$piece_npc";
                piece.m_description = "$piece_npc_desc";
            }

            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_resources = new Piece.Requirement[0];
            piece.m_placeEffect = new EffectList();

            // CRITICAL: Configure placement settings for Infinity Hammer compatibility
            piece.m_allowedInDungeons = true;
            piece.m_repairPiece = false;
            // Vanilla removal path is CLOSED for everyone — StaticNpc_RemoveGate's RemovePiece prefix
            // runs before this check and is the one admin-gated delete path (routes to the anchor
            // delete). Admins with Infinity Hammer "remove anything" can still act; a body-only IH
            // destroy just gets re-created by the anchor keeper.
            piece.m_canBeRemoved = false;
            // m_clipEverything=true is REQUIRED: UpdatePlacementGhost then pivots the ghost at the raw
            // PieceRayTest hit point — and m_placeRayMask includes the "piece" layer, so that point lands
            // on floors/roofs just as well as terrain. This is the branch every working companion piece
            // uses. With all clip/ground flags false vanilla instead runs the collider-fit branch, which
            // measures the ghost's own ENABLED colliders — but SetupPlacementGhost disables every collider
            // not on a placement layer (an NPC body is all "character" layer, so ALL of them), the
            // ClosestPoint loop matches nothing, and the ghost gets positioned at point + (point+normal*50)
            // ≈ twice the world position: invisible ghost, placement lands kilometers away.
            piece.m_clipEverything = true;
            piece.m_clipGround = false;
            piece.m_groundPiece = false;        // Not a ground-only piece
            piece.m_groundOnly = false;         // Can be placed anywhere
            piece.m_cultivatedGroundOnly = false;
            piece.m_waterPiece = false;
            piece.m_noInWater = false;          // Allow in water
            piece.m_notOnWood = false;
            piece.m_notOnTiltingSurface = false;
            piece.m_notOnFloor = false;
            piece.m_onlyInTeleportArea = false;
            piece.m_allowRotatedOverlap = true; // Allow overlapping when rotated
            piece.m_inCeilingOnly = false;
            piece.m_onlyInBiome = Heightmap.Biome.None; // Allow in all biomes
            piece.m_enabled = true;

            // Ensure NpcController component exists
            var npcController = npcPrefab.GetComponent<NpcController>();
            if (npcController == null)
            {
                npcController = npcPrefab.AddComponent<NpcController>();
                npcController.npcProfileName = npcName;
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added NpcController component to NPC: {npcName}");
            }

            // StaticNpc: attach StaticNpcInitializer so it auto-initializes as a
            // stationed quest NPC when placed via hammer (skips companion pipeline)
            if (npcName.Equals("StaticNpc", StringComparison.OrdinalIgnoreCase))
            {
                var initializer = npcPrefab.GetComponent<StaticNpcInitializer>();
                if (initializer == null)
                {
                    initializer = npcPrefab.AddComponent<StaticNpcInitializer>();
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added StaticNpcInitializer to StaticNpc prefab");
                }
            }

            // Disable VisEquipment and add NpcVisEquipment at prefab-build time.
            // VisEquipment.OnEnable registers with MonoUpdaters, which then calls
            // CustomUpdate -> UpdateVisuals -> UpdateColors every frame. On a freshly
            // spawned NPC those methods crash because the body shader is wrong and
            // the model arrays haven't been set up yet. Disabling it here means
            // OnEnable never fires, MonoUpdaters never gets the component, and there
            // is no update to suppress. NpcVisEquipment.Awake() fires synchronously
            // during Instantiate() and owns the full re-enable + init sequence.
            var visEquipment = npcPrefab.GetComponent<VisEquipment>();
            if (visEquipment != null)
            {
                visEquipment.enabled = false;
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Disabled VisEquipment on {npcName} prefab - NpcVisEquipment owns init");
            }

            var npcVisEquipment = npcPrefab.GetComponent<FiresCore.Npc.NpcVisEquipment>();
            if (npcVisEquipment == null)
            {
                npcPrefab.AddComponent<FiresCore.Npc.NpcVisEquipment>();
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added NpcVisEquipment to {npcName} prefab");
            }

            // Enable scale sync for Infinity Hammer placement
            var zNetView = npcPrefab.GetComponent<ZNetView>();
            if (zNetView != null)
                zNetView.m_syncInitialScale = true;

            // Strip WearNTear if the bundle prefab shipped one - its RPC registrations
            // conflict with the companion component stack added by SetupCompanionPrefab.
            var wearNTear = npcPrefab.GetComponent<WearNTear>();
            if (wearNTear != null)
            {
                UnityEngine.Object.DestroyImmediate(wearNTear);
            }

            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Setup NPC piece defaults for: {npcName}");
        }

        /// <summary>
        /// Strips character-related components that cause NullReferenceExceptions on static NPCs.
        /// These components expect a full Character setup with Animator, AI, etc.
        /// For future AI NPCs, use a different prefab type that has these properly configured.
        /// NOTE: We preserve VisEquipment for the Dressing Room feature - it will be configured at runtime.
        /// </summary>
        private static void StripCharacterComponents(GameObject prefab)
        {
            int stripped = 0;

            // These components require a full Character setup and will crash without it
            // NOTE: VisEquipment is NOT stripped - we configure it at runtime for NPC equipment display
            var typesToStrip = new[]
            {
                "CharacterAnimEvent",
                "FootStep",
                "CharacterDrop",
                "Growup",
                "LevelEffects",
                "RandomAnimation",
                // AI components - not needed for static NPCs
                "BaseAI",
                "MonsterAI",
                "AnimalAI",
                "Tameable",
                "Procreation",
                // Character/combat - not needed for static quest NPCs
                "Humanoid",
                "Character",
                "CharacterTimedDestruction"
            };

            foreach (var typeName in typesToStrip)
            {
                // Check root and all children
                var components = prefab.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    if (comp.GetType().Name == typeName)
                    {
                        FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Stripping {typeName} from {prefab.name}/{comp.gameObject.name}");
                        UnityEngine.Object.DestroyImmediate(comp);
                        stripped++;
                    }
                }
            }

            // Also strip Animator if it would cause issues (no controller assigned)
            var animators = prefab.GetComponentsInChildren<Animator>(true);
            foreach (var animator in animators)
            {
                if (animator != null && animator.runtimeAnimatorController == null)
                {
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Stripping orphaned Animator from {prefab.name}/{animator.gameObject.name}");
                    UnityEngine.Object.DestroyImmediate(animator);
                    stripped++;
                }
            }

            if (stripped > 0)
            {
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Stripped {stripped} character components from static NPC: {prefab.name}");
            }
        }


        /// <summary>
        /// Registers every loaded piece prefab with <c>ZNetScene.m_prefabs</c>
        /// and <c>m_namedPrefabs</c> at <c>ZNetScene.Awake</c> time, BEFORE
        /// <c>ZDOMan.Load</c> instantiates persistent ZDOs. Without this,
        /// vanilla's <c>ZNetScene.CreateObject</c> sees the wayshrine ZDO
        /// hash, doesn't find the prefab, and runs
        /// <c>"Destroyed invalid predab ZDO:..."</c> - wiping placed shrines
        /// from the world on every world load. Items + companions register
        /// here for the same reason; pieces have to too. PieceTable
        /// registration stays in <see cref="AddPiecesToHammer"/> because
        /// that one needs ObjectDB (Hammer) to be ready.
        /// </summary>
        public static void RegisterPiecesWithZNetScene()
        {
            if (ZNetScene.instance == null) return;
            var znet = ZNetScene.instance;

            // Scrub fake-nulls left over from a previous session. Mirrors the
            // same scrub logic in AddPiecesToHammer so the two paths can run
            // independently without leaving the dictionary half-cleaned.
            int znetDead = znet.m_prefabs.RemoveAll(p => p == null);
            if (znetDead > 0)
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Scrubbed {znetDead} fake-null entries from ZNetScene.m_prefabs (early piece pass)");

            System.Collections.Generic.Dictionary<int, GameObject> namedDict = null;
            try
            {
                var namedField = typeof(ZNetScene).GetField("m_namedPrefabs",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (namedField != null)
                    namedDict = namedField.GetValue(znet) as System.Collections.Generic.Dictionary<int, GameObject>;
                if (namedDict != null)
                {
                    var dead = new List<int>();
                    foreach (var kvp in namedDict)
                        if (kvp.Value == null) dead.Add(kvp.Key);
                    foreach (var k in dead) namedDict.Remove(k);
                    if (dead.Count > 0)
                        FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs (early piece pass)");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: Could not access ZNetScene.m_namedPrefabs in early piece pass: {ex.Message}");
            }

            int registered = 0;
            foreach (var piecePrefab in FiresRPGmaker.piecePrefabs)
            {
                if (piecePrefab == null) continue;
                if (znet.GetPrefab(piecePrefab.name) != null) continue;

                znet.m_prefabs.Add(piecePrefab);
                if (namedDict != null)
                {
                    int hash = piecePrefab.name.GetStableHashCode();
                    if (!namedDict.ContainsKey(hash))
                        namedDict.Add(hash, piecePrefab);
                }
                registered++;
            }

            // Network-only prefabs (the NPC anchor) MUST land in this same early pass: a persistent
            // anchor ZDO whose prefab hash can't resolve would be wiped by vanilla's
            // "Destroyed invalid predab ZDO" on the very first zone load.
            foreach (var netPrefab in NetworkOnlyPrefabs)
            {
                if (netPrefab == null) continue;
                if (znet.GetPrefab(netPrefab.name) != null) continue;

                znet.m_prefabs.Add(netPrefab);
                if (namedDict != null)
                {
                    int hash = netPrefab.name.GetStableHashCode();
                    if (!namedDict.ContainsKey(hash))
                        namedDict.Add(hash, netPrefab);
                }
                registered++;
            }

            if (registered > 0)
                Debug.Log($"{FiresRPGmaker.PluginName}: Pre-registered {registered} piece prefab(s) with ZNetScene before ZDO load");
        }

        public static void AddPiecesToHammer()
        {
            if (!FiresRPGmaker.IsObjectDBValid() || ObjectDB.instance == null)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: ObjectDB not valid, cannot add pieces to Hammer");
                return;
            }

            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            if (hammer == null)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: Hammer prefab not found");
                return;
            }

            var itemDrop = hammer.GetComponent<ItemDrop>();
            if (itemDrop?.m_itemData?.m_shared?.m_buildPieces == null)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: Hammer has no build pieces table");
                return;
            }

            var pieceTable = itemDrop.m_itemData.m_shared.m_buildPieces;

            // CRITICAL: PieceTable is a ScriptableObject - it survives scene unload,
            // unlike the GameObjects it references. On a second world load in the same
            // game session, any unprotected piece prefab in m_pieces becomes a Unity
            // fake-null. Other mods (Jotunn, Balrond, etc.) iterate this list during
            // their ZNetScene.Awake patches and NRE on the dead reference. Purge them
            // BEFORE we re-add our (now DDOL-protected) prefabs.
            int deadCount = pieceTable.m_pieces.RemoveAll(p => p == null);
            if (deadCount > 0)
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Scrubbed {deadCount} fake-null entries from Hammer PieceTable.m_pieces");

            // Also scrub ZNetScene.m_prefabs since AddPrefabToPieceTable adds to it
            // and any leftover fake-nulls there cause the same downstream NRE pattern.
            if (ZNetScene.instance != null)
            {
                int znetDead = ZNetScene.instance.m_prefabs.RemoveAll(p => p == null);
                if (znetDead > 0)
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Scrubbed {znetDead} fake-null entries from ZNetScene.m_prefabs (piece pass)");

                try
                {
                    var namedField = typeof(ZNetScene).GetField("m_namedPrefabs",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (namedField != null && namedField.GetValue(ZNetScene.instance) is System.Collections.Generic.Dictionary<int, GameObject> dict)
                    {
                        var dead = new List<int>();
                        foreach (var kvp in dict)
                            if (kvp.Value == null) dead.Add(kvp.Key);
                        foreach (var k in dead) dict.Remove(k);
                        if (dead.Count > 0)
                            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs (piece pass)");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Could not scrub ZNetScene.m_namedPrefabs (piece pass): {ex.Message}");
                }
            }

            // Add regular pieces and static NPCs. The BUNDLE StaticNpc prefab is deliberately NOT a
            // hammer piece anymore — it stays ZNetScene-registered so world ZDOs resolve, but the
            // hammer places the CompanionNpc-derived clone (companion-parity placement).
            foreach (var piecePrefab in FiresRPGmaker.piecePrefabs)
            {
                if (piecePrefab != null && piecePrefab.name == "StaticNpc") continue;
                AddPrefabToPieceTable(piecePrefab, pieceTable);
            }

            // Add companion prefabs to hammer as well
            AddCompanionsToPieceTable(pieceTable);

            // Repair the BUNDLE StaticNpc prefab's body material — it is what ZNetScene re-instantiates
            // for every placed body on zone load, and the rebuilt bundle shipped it broken (bare
            // Standard, no textures).
            var staticNpcPrefab = FiresRPGmaker.piecePrefabs.Find(p => p != null && p.name == "StaticNpc");
            if (staticNpcPrefab != null)
                FiresCore.Npc.NpcVisEquipment.FixBodyMaterialOnPrefab(staticNpcPrefab);

            // The hammer piece: a clone of the WORKING CompanionNpc prefab named "StaticNpc" (see
            // EnsureStaticNpcPiecePrefab). Sweep any OTHER StaticNpc-named table entry first — stale
            // copies (the rotten bundle asset, other injectors) were what the user kept placing:
            // baked tooltip, invisible ghost, dead placement.
            var npcPiece = EnsureStaticNpcPiecePrefab();
            int swept = pieceTable.m_pieces.RemoveAll(p => p != null && p.name == "StaticNpc" && p != npcPiece);
            if (swept > 0)
                Debug.Log($"{FiresRPGmaker.PluginName}: swept {swept} stale StaticNpc piece-table entr(ies)");
            if (npcPiece != null && !pieceTable.m_pieces.Contains(npcPiece))
                pieceTable.m_pieces.Add(npcPiece);

            // Saved-NPC appearance templates as placeable pieces under the shared "Fires NPCs" tab
            // (also re-homes the base StaticNpc piece onto that tab). Client-side; no-op headless.
            NpcTemplatePieces.AddToHammer(pieceTable, npcPiece != null ? new List<GameObject> { npcPiece } : FiresRPGmaker.piecePrefabs);

            // Late injectors (FAP's piece coroutine and friends) can re-seat a stale StaticNpc tile
            // AFTER this pass — sweep once more when they've had time to run.
            if (npcPiece != null && FiresRPGmaker.Instance != null)
                FiresRPGmaker.Instance.StartCoroutine(LateStaticNpcSweep(pieceTable, npcPiece));
        }

        private static System.Collections.IEnumerator LateStaticNpcSweep(PieceTable pieceTable, GameObject keeper)
        {
            yield return new UnityEngine.WaitForSeconds(8f);
            if (pieceTable == null || pieceTable.m_pieces == null || keeper == null) yield break;
            int swept = pieceTable.m_pieces.RemoveAll(p => p != null && p.name == "StaticNpc" && p != keeper);
            if (!pieceTable.m_pieces.Contains(keeper)) pieceTable.m_pieces.Add(keeper);
            if (swept > 0)
                Debug.Log($"{FiresRPGmaker.PluginName}: late sweep removed {swept} stale StaticNpc piece-table entr(ies)");
        }

        private static void AddPrefabToPieceTable(GameObject piecePrefab, PieceTable pieceTable)
        {
            if (piecePrefab == null) return;

            // Dedupe by NAME, not reference: the PieceTable is a ScriptableObject that outlives world
            // reloads, and stale same-name copies (older DLL runs, other loaders) produced duplicate
            // "StaticNpc" tiles in the hammer. Keep exactly one entry — ours.
            int dupes = pieceTable.m_pieces.RemoveAll(p => p != null && p != piecePrefab && p.name == piecePrefab.name);
            if (dupes > 0)
                Debug.Log($"{FiresRPGmaker.PluginName}: removed {dupes} duplicate '{piecePrefab.name}' piece-table entr(ies)");

            // Skip if already in piece table
            if (pieceTable.m_pieces.Contains(piecePrefab))
            {
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: {piecePrefab.name} already in Hammer PieceTable");
                return;
            }

            // Add Piece component if missing - same pattern as companion registration
            var piece = piecePrefab.GetComponent<Piece>();
            if (piece == null)
            {
                piece = piecePrefab.AddComponent<Piece>();
                piece.m_name = piecePrefab.name;
                piece.m_description = "";
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_enabled = true;
                piece.m_allowedInDungeons = true;
                piece.m_canBeRemoved = true;
                piece.m_clipEverything = true;
                piece.m_clipGround = true;
                piece.m_groundPiece = false;
                piece.m_groundOnly = false;
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added Piece component to: {piecePrefab.name}");
            }

            // Ensure ZNetView exists
            var zNetView = piecePrefab.GetComponent<ZNetView>();
            if (zNetView == null)
            {
                zNetView = piecePrefab.AddComponent<ZNetView>();
                zNetView.m_persistent = true;
            }

            // Register to ZNetScene at the same time as hammer - mirrors companion pattern.
            // This prevents other mods from claiming our prefabs before we add them to the hammer.
            if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(piecePrefab.name) == null)
            {
                ZNetScene.instance.m_prefabs.Add(piecePrefab);

                int stableHashCode = piecePrefab.name.GetStableHashCode();
                try
                {
                    var namedPrefabsField = typeof(ZNetScene).GetField("m_namedPrefabs",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (namedPrefabsField != null)
                    {
                        var dict = namedPrefabsField.GetValue(ZNetScene.instance) as System.Collections.Generic.Dictionary<int, GameObject>;
                        if (dict != null && !dict.ContainsKey(stableHashCode))
                        {
                            dict.Add(stableHashCode, piecePrefab);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Failed to add {piecePrefab.name} to ZNetScene hash: {ex.Message}");
                }
            }

            // Add to piece table
            pieceTable.m_pieces.Add(piecePrefab);
            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added {piecePrefab.name} to Hammer PieceTable");
        }

        /// <summary>
        /// Adds companion prefabs to the hammer's piece table.
        /// Companions need a Piece component added for hammer placement.
        /// </summary>
        private static void AddCompanionsToPieceTable(PieceTable pieceTable)
        {
            var companionPrefabs = CompanionPrefabManager.GetLoadedCompanionPrefabs();
            if (companionPrefabs == null || companionPrefabs.Count == 0)
            {
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: No companion prefabs to add to hammer");
                return;
            }

            foreach (var prefab in companionPrefabs)
            {
                if (prefab == null) continue;

                // The CompanionNpc_Wild variant is a spawn-only prefab driven
                // by SpawnSystem; it should NOT show up in the hammer build
                // menu (admins place via `spawn`, not via hammer). It rides
                // the same registration pipeline as the regular companions
                // but is excluded from PieceTable here.
                if (string.Equals(prefab.name, FiresCore.Npc.WildSpawn.WildCompanionPrefabs.WildPrefabName,
                                  System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip if already in piece table
                if (pieceTable.m_pieces.Contains(prefab))
                {
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Companion {prefab.name} already in Hammer PieceTable");
                    continue;
                }

                // Add Piece component if missing (companions are primarily AI NPCs but can also be placed)
                var piece = prefab.GetComponent<Piece>();
                if (piece == null)
                {
                    piece = prefab.AddComponent<Piece>();
                    piece.m_name = "$piece_companion";
                    piece.m_description = "$piece_companion_desc";
                    piece.m_category = Piece.PieceCategory.Misc;
                    piece.m_enabled = true;
                    piece.m_allowedInDungeons = true;
                    piece.m_canBeRemoved = true;
                    piece.m_clipEverything = true;
                    piece.m_clipGround = true;
                    piece.m_groundPiece = false;
                    piece.m_groundOnly = false;
                    FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added Piece component to companion: {prefab.name}");
                }

                // Add to piece table
                pieceTable.m_pieces.Add(prefab);
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Added companion {prefab.name} to Hammer PieceTable");
            }
        }
    }
}