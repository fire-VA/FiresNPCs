using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.Modules.Companions.NpcMode;
using FiresRPGmaker.UI;
using FiresRPGmaker.Utilities;

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

        public static void LoadPieces(string mainPath, string modInstance)
        {
            if (FiresRPGmaker.assetBundle == null)
            {
                Debug.LogError($"{FiresRPGmaker.PluginName}: AssetBundle is null.");
                return;
            }

            FiresRPGmaker.piecePrefabs.Clear();
            var allAssets = FiresRPGmaker.assetBundle.GetAllAssetNames();
            Debug.Log($"{FiresRPGmaker.PluginName}: AssetBundle contains {allAssets.Length} assets: {string.Join(", ", allAssets)}");

            int failed = 0;

            // Load regular pieces from pieces/ folder
            foreach (var pieceName in PieceNames)
            {
                var prefab = LoadPiecePrefab(mainPath + "pieces/" + pieceName + ".prefab", pieceName);
                if (prefab != null)
                {
                    SetupPieceDefaults(prefab, pieceName);
                    FiresRPGmaker.piecePrefabs.Add(prefab);
                    Debug.Log($"{FiresRPGmaker.PluginName}: Loaded piece prefab: {pieceName}");
                }
                else
                {
                    failed++;
                }
            }

            // Load NPC prefabs from npc/ folder (in vaitems)
            // StaticNpc is a dedicated bundle prefab for quest NPCs Ã¢â‚¬â€ safe to modify directly.
            // BaseNpc is handled separately by CompanionPrefabManager for companion placement.
            foreach (var npcName in NpcPrefabNames)
            {
                var prefab = LoadNpcPrefab(npcName);
                if (prefab != null)
                {
                    SetupNpcPieceDefaults(prefab, npcName);
                    FiresRPGmaker.piecePrefabs.Add(prefab);
                    Debug.Log($"{FiresRPGmaker.PluginName}: Loaded NPC prefab: {npcName}");
                }
                else
                {
                    failed++;
                }
            }

            Debug.Log($"{FiresRPGmaker.PluginName}: Loaded {FiresRPGmaker.piecePrefabs.Count} pieces. Failed: {failed}");
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

                    Debug.Log($"{FiresRPGmaker.PluginName}: Found piece prefab at path: {normalizedPath}");
                    // CRITICAL: protect the bundle prefab from scene-unload destruction.
                    // See VAItemManager.LoadItems for the full rationale Ã¢â‚¬â€ the static
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
                    Debug.Log($"{FiresRPGmaker.PluginName}: Found NPC prefab at path: {normalizedPath}");
                    // Protect from scene-unload destruction Ã¢â‚¬â€ see LoadPiecePrefab.
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
            Debug.Log($"{FiresRPGmaker.PluginName}: {pieceName} loaded with category={piece.m_category} from Unity");

            
            

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
            // because they don't ship with a Piece in the bundle Ã¢â‚¬â€ they get one added
            // fresh via AddComponent in AddCompanionsToPieceTable. We do the same here.
            var existingPiece = npcPrefab.GetComponent<Piece>();
            if (existingPiece != null)
                UnityEngine.Object.DestroyImmediate(existingPiece);

            var piece = npcPrefab.AddComponent<Piece>();

            // Set piece properties - StaticNpc gets its own name/description
            if (npcName.Equals("StaticNpc", StringComparison.OrdinalIgnoreCase))
            {
                piece.m_name = "Static Quest NPC";
                piece.m_description = "A stationed quest NPC. Place from hammer Ã¢â‚¬â€ no companion setup required. Use Shift+E to configure.";
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
            piece.m_canBeRemoved = true;
            piece.m_clipEverything = true;   // Allow placing anywhere without collision checks
            piece.m_clipGround = true;          // Allow clipping into ground
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
                Debug.Log($"{FiresRPGmaker.PluginName}: Added NpcController component to NPC: {npcName}");
            }

            // StaticNpc: attach StaticNpcInitializer so it auto-initializes as a
            // stationed quest NPC when placed via hammer (skips companion pipeline)
            if (npcName.Equals("StaticNpc", StringComparison.OrdinalIgnoreCase))
            {
                var initializer = npcPrefab.GetComponent<StaticNpcInitializer>();
                if (initializer == null)
                {
                    initializer = npcPrefab.AddComponent<StaticNpcInitializer>();
                    Debug.Log($"{FiresRPGmaker.PluginName}: Added StaticNpcInitializer to StaticNpc prefab");
                }
            }

            // Disable VisEquipment and add NpcVisEquipment at prefab-build time.
            // VisEquipment.OnEnable registers with MonoUpdaters, which then calls
            // CustomUpdate Ã¢â€ â€™ UpdateVisuals Ã¢â€ â€™ UpdateColors every frame. On a freshly
            // spawned NPC those methods crash because the body shader is wrong and
            // the model arrays haven't been set up yet. Disabling it here means
            // OnEnable never fires, MonoUpdaters never gets the component, and there
            // is no update to suppress. NpcVisEquipment.Awake() fires synchronously
            // during Instantiate() and owns the full re-enable + init sequence.
            var visEquipment = npcPrefab.GetComponent<VisEquipment>();
            if (visEquipment != null)
            {
                visEquipment.enabled = false;
                Debug.Log($"{FiresRPGmaker.PluginName}: Disabled VisEquipment on {npcName} prefab Ã¢â‚¬â€ NpcVisEquipment owns init");
            }

            var npcVisEquipment = npcPrefab.GetComponent<FiresCore.Npc.NpcVisEquipment>();
            if (npcVisEquipment == null)
            {
                npcPrefab.AddComponent<FiresCore.Npc.NpcVisEquipment>();
                Debug.Log($"{FiresRPGmaker.PluginName}: Added NpcVisEquipment to {npcName} prefab");
            }

            // Enable scale sync for Infinity Hammer placement
            var zNetView = npcPrefab.GetComponent<ZNetView>();
            if (zNetView != null)
                zNetView.m_syncInitialScale = true;

            // Strip WearNTear if the bundle prefab shipped one Ã¢â‚¬â€ its RPC registrations
            // conflict with the companion component stack added by SetupCompanionPrefab.
            var wearNTear = npcPrefab.GetComponent<WearNTear>();
            if (wearNTear != null)
            {
                UnityEngine.Object.DestroyImmediate(wearNTear);
            }

            Debug.Log($"{FiresRPGmaker.PluginName}: Setup NPC piece defaults for: {npcName}");
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
                        Debug.Log($"{FiresRPGmaker.PluginName}: Stripping {typeName} from {prefab.name}/{comp.gameObject.name}");
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
                    Debug.Log($"{FiresRPGmaker.PluginName}: Stripping orphaned Animator from {prefab.name}/{animator.gameObject.name}");
                    UnityEngine.Object.DestroyImmediate(animator);
                    stripped++;
                }
            }

            if (stripped > 0)
            {
                Debug.Log($"{FiresRPGmaker.PluginName}: Stripped {stripped} character components from static NPC: {prefab.name}");
            }
        }


        /// <summary>
        /// Registers every loaded piece prefab with <c>ZNetScene.m_prefabs</c>
        /// and <c>m_namedPrefabs</c> at <c>ZNetScene.Awake</c> time, BEFORE
        /// <c>ZDOMan.Load</c> instantiates persistent ZDOs. Without this,
        /// vanilla's <c>ZNetScene.CreateObject</c> sees the wayshrine ZDO
        /// hash, doesn't find the prefab, and runs
        /// <c>"Destroyed invalid predab ZDO:..."</c> Ã¢â‚¬â€ wiping placed shrines
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
                Debug.Log($"{FiresRPGmaker.PluginName}: Scrubbed {znetDead} fake-null entries from ZNetScene.m_prefabs (early piece pass)");

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
                        Debug.Log($"{FiresRPGmaker.PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs (early piece pass)");
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

            // CRITICAL: PieceTable is a ScriptableObject Ã¢â‚¬â€ it survives scene unload,
            // unlike the GameObjects it references. On a second world load in the same
            // game session, any unprotected piece prefab in m_pieces becomes a Unity
            // fake-null. Other mods (Jotunn, Balrond, etc.) iterate this list during
            // their ZNetScene.Awake patches and NRE on the dead reference. Purge them
            // BEFORE we re-add our (now DDOL-protected) prefabs.
            int deadCount = pieceTable.m_pieces.RemoveAll(p => p == null);
            if (deadCount > 0)
                Debug.Log($"{FiresRPGmaker.PluginName}: Scrubbed {deadCount} fake-null entries from Hammer PieceTable.m_pieces");

            // Also scrub ZNetScene.m_prefabs since AddPrefabToPieceTable adds to it
            // and any leftover fake-nulls there cause the same downstream NRE pattern.
            if (ZNetScene.instance != null)
            {
                int znetDead = ZNetScene.instance.m_prefabs.RemoveAll(p => p == null);
                if (znetDead > 0)
                    Debug.Log($"{FiresRPGmaker.PluginName}: Scrubbed {znetDead} fake-null entries from ZNetScene.m_prefabs (piece pass)");

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
                            Debug.Log($"{FiresRPGmaker.PluginName}: Scrubbed {dead.Count} fake-null entries from ZNetScene.m_namedPrefabs (piece pass)");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Could not scrub ZNetScene.m_namedPrefabs (piece pass): {ex.Message}");
                }
            }

            // Add regular pieces and static NPCs
            foreach (var piecePrefab in FiresRPGmaker.piecePrefabs)
            {
                AddPrefabToPieceTable(piecePrefab, pieceTable);
            }

            // Add companion prefabs to hammer as well
            AddCompanionsToPieceTable(pieceTable);
        }

        private static void AddPrefabToPieceTable(GameObject piecePrefab, PieceTable pieceTable)
        {
            if (piecePrefab == null) return;

            // Skip if already in piece table
            if (pieceTable.m_pieces.Contains(piecePrefab))
            {
                Debug.Log($"{FiresRPGmaker.PluginName}: {piecePrefab.name} already in Hammer PieceTable");
                return;
            }

            // Add Piece component if missing Ã¢â‚¬â€ same pattern as companion registration
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
                Debug.Log($"{FiresRPGmaker.PluginName}: Added Piece component to: {piecePrefab.name}");
            }

            // Ensure ZNetView exists
            var zNetView = piecePrefab.GetComponent<ZNetView>();
            if (zNetView == null)
            {
                zNetView = piecePrefab.AddComponent<ZNetView>();
                zNetView.m_persistent = true;
            }

            // Register to ZNetScene at the same time as hammer Ã¢â‚¬â€ mirrors companion pattern.
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
            Debug.Log($"{FiresRPGmaker.PluginName}: Added {piecePrefab.name} to Hammer PieceTable");
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
                Debug.Log($"{FiresRPGmaker.PluginName}: No companion prefabs to add to hammer");
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
                    Debug.Log($"{FiresRPGmaker.PluginName}: Companion {prefab.name} already in Hammer PieceTable");
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
                    Debug.Log($"{FiresRPGmaker.PluginName}: Added Piece component to companion: {prefab.name}");
                }

                // Add to piece table
                pieceTable.m_pieces.Add(prefab);
                Debug.Log($"{FiresRPGmaker.PluginName}: Added companion {prefab.name} to Hammer PieceTable");
            }
        }
    }
}