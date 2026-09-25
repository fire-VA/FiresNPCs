using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using FiresRPGmaker.NpcFashion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// Manages persistent NPC configuration data.
    /// Server-authoritative: each NPC gets its own JSON file in SavedNPCs/.
    /// When an admin edits an NPC, the data is written to disk on the server
    /// and broadcast to all clients via RPC so they see the update immediately.
    /// On client connect the full set of NPC files is synced just like other config folders.
    /// </summary>
    public static class SavedNpcManager
    {
        private static Dictionary<string, SavedNpcData> _savedNpcs = new Dictionary<string, SavedNpcData>(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;
        private static bool _rpcsRegistered;

        /// <summary>Namespace key used by <see cref="ManifestDiffSync"/>
        /// so the server can query the client's local SavedNPCs/ hashes and skip unchanged entries.</summary>
        private const string ManifestNamespace = "SavedNPCs";

        // The server owns the canonical flat folder. A CLIENT caches received NPCs in a
        // per-server subfolder so one r2modman profile reused across servers never mixes
        // another server's saved NPCs — the diff-sync manifest is built from this folder,
        // so it must be per-server to report the right {file->hash} set.
        private static string SavedNpcsFolder
        {
            get
            {
                string flat = Path.Combine(FiresCore.Storage.FiresConfigPaths.SavedNpcs);
                return (ZNet.instance != null && ZNet.instance.IsServer())
                    ? flat
                    : FiresCore.Sync.FiresServerScope.ScopedDir(flat);
            }
        }

        // Legacy single-file path (for migration)
        private static string LegacySavedNpcsFile => Path.Combine(SavedNpcsFolder, "npcs.json");

        /// <summary>
        /// Register the SavedNPCs/ folder with <see cref="ManifestDiffSync"/>
        /// so peers can be asked for a {filename -&gt; sha256} manifest of their local copies.
        /// Called once at mod init from <c>Ascend.cs</c> (after ZRoutedRpc is up).
        /// Idempotent - re-registration simply overwrites the folder binding.
        /// </summary>
        public static void RegisterManifestFolder()
        {
            ManifestDiffSync.RegisterFolder(
                ManifestNamespace,
                () => SavedNpcsFolder,
                "*.json",
                SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// Resets the RPC registration flag so RegisterRPCs() will run again on next login.
        /// Called on logout because ZRoutedRpc.instance gets destroyed during scene transition,
        /// making the old RPC registrations invalid.
        /// </summary>
        public static void ResetRPCState()
        {
            _rpcsRegistered = false;
        }

        private const string RPC_NPC_DATA_UPDATE = "FiresRPGmaker_NpcDataUpdate";
        private const string RPC_NPC_DATA_DELETE = "FiresRPGmaker_NpcDataDelete";
        private const string RPC_NPC_ANCHOR_MOVE = "FiresRPGmaker_NpcAnchorMove";

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new Vector3Converter() }
        };

        /// <summary>
        /// Initializes the SavedNpcManager and loads saved data from per-NPC files.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            EnsureFolderExists();
            MigrateLegacyFile();
            LoadAllNpcFiles();

            FiresLogger.LogVerbose($"[SavedNpcManager] Initialized with {_savedNpcs.Count} saved NPCs");
        }

        /// <summary>
        /// Registers RPCs for NPC data sync. Call once when ZRoutedRpc is ready.
        /// </summary>
        public static void RegisterRPCs()
        {
            if (_rpcsRegistered) return;
            if (ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.Register<ZPackage>(RPC_NPC_DATA_UPDATE, new Action<long, ZPackage>(RPC_OnNpcDataUpdate));
            ZRoutedRpc.instance.Register<ZPackage>(RPC_NPC_DATA_DELETE, new Action<long, ZPackage>(RPC_OnNpcDataDelete));
            ZRoutedRpc.instance.Register<ZPackage>(RPC_NPC_ANCHOR_MOVE, new Action<long, ZPackage>(RPC_OnNpcAnchorMove));
            _rpcsRegistered = true;
            FiresLogger.LogVerbose("[SavedNpcManager] Registered NPC data sync RPCs");
        }

        /// <summary>
        /// Generates a unique identifier for an NPC based on its position and the world seed.
        /// </summary>
        public static string GenerateNpcId(Vector3 position)
        {
            long worldSeed = 0;
            try
            {
                if (ZNet.instance != null)
                    worldSeed = ZNet.instance.GetWorldUID();
            }
            catch { }

            int x = Mathf.RoundToInt(position.x);
            int y = Mathf.RoundToInt(position.y);
            int z = Mathf.RoundToInt(position.z);

            return $"{worldSeed}_{x}_{y}_{z}";
        }

        /// <summary>
        /// Converts an NPC id to a safe filename.
        /// </summary>
        private static string NpcIdToFileName(string npcId)
        {
            return npcId + ".json";
        }

        private static string NpcFilePath(string npcId)
        {
            return Path.Combine(SavedNpcsFolder, NpcIdToFileName(npcId));
        }

        /// <summary>
        /// Saves NPC data to the in-memory store and writes the per-NPC file to disk.
        /// </summary>
        public static void SaveNpc(string npcId, SavedNpcData data)
        {
            if (string.IsNullOrEmpty(npcId)) return;

            Initialize();

            data.LastModified = DateTime.UtcNow;
            _savedNpcs[npcId] = data;

            WriteNpcFile(npcId, data);

            Debug.Log($"[SavedNpcManager] Saved NPC data: {npcId} - Profile: {data.NpcProfileName}, QuestProfile: {data.QuestProfile}");
        }

        /// <summary>
        /// Gets saved NPC data by ID.
        /// </summary>
        public static SavedNpcData GetNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;

            Initialize();

            _savedNpcs.TryGetValue(npcId, out var data);
            return data;
        }

        /// <summary>
        /// Quick lookup for model override by ZNetView position.
        /// Used by NpcController.Awake() to detect pending model overrides before VisEquipment runs.
        /// Returns null/empty if no model override is saved.
        /// </summary>
        public static string GetModelOverrideForNpc(ZNetView nview)
        {
            if (nview == null) return null;

            try
            {
                if (!_initialized) Initialize();

                string npcId = GenerateNpcId(nview.transform.position);
                if (_savedNpcs.TryGetValue(npcId, out var data))
                {
                    return data.ModelOverride;
                }
            }
            catch
            {
                // Non-critical - caller will handle null
            }

            return null;
        }

        /// <summary>
        /// Loads NPC data into an NpcController from the saved store.
        /// Returns true if data was loaded.
        /// </summary>
        public static bool LoadIntoNpc(UI.NpcController npc) =>
            npc != null && LoadIntoNpc(npc, npc.transform.position);

        /// <summary>As <see cref="LoadIntoNpc(UI.NpcController)"/>, but resolves the saved record by an
        /// explicit position. Used when an NPC respawns somewhere other than where it was placed (e.g. a
        /// patrol checkpoint) but its config is still keyed by the original placed position.</summary>
        public static bool LoadIntoNpc(UI.NpcController npc, Vector3 lookupPosition)
        {
            if (npc == null) return false;

            if (ZNet.instance == null || (!ZNet.instance.IsServer() && ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected))
                return false;

            Initialize();

            string npcId = GenerateNpcId(lookupPosition);
            var savedData = GetNpc(npcId);

            if (savedData == null) return false;

            // Apply saved data to the NPC
            if (!string.IsNullOrEmpty(savedData.NpcProfileName))
                npc.npcProfileName = savedData.NpcProfileName;

            if (!string.IsNullOrEmpty(savedData.DisplayNameOverride))
                npc.displayNameOverride = savedData.DisplayNameOverride;

            if (!string.IsNullOrEmpty(savedData.QuestProfile))
                npc.questProfile = savedData.QuestProfile;

            if (!string.IsNullOrEmpty(savedData.DialogueProfile))
                npc.dialogueProfile = savedData.DialogueProfile;

            if (!string.IsNullOrEmpty(savedData.InfoProfile))
                npc.infoProfile = savedData.InfoProfile;

            if (!string.IsNullOrEmpty(savedData.TraderProfile))
                npc.traderProfile = savedData.TraderProfile;

            if (!string.IsNullOrEmpty(savedData.ModelOverride))
                npc.modelOverride = savedData.ModelOverride;

            npc.npcType = savedData.NpcType;

            // Load equipment data
            if (!string.IsNullOrEmpty(savedData.EquipHelmet))
                npc.equipHelmet = savedData.EquipHelmet;
            if (!string.IsNullOrEmpty(savedData.EquipChest))
                npc.equipChest = savedData.EquipChest;
            if (!string.IsNullOrEmpty(savedData.EquipLegs))
                npc.equipLegs = savedData.EquipLegs;
            if (!string.IsNullOrEmpty(savedData.EquipShoulder))
                npc.equipShoulder = savedData.EquipShoulder;
            if (!string.IsNullOrEmpty(savedData.EquipUtility))
                npc.equipUtility = savedData.EquipUtility;
            if (!string.IsNullOrEmpty(savedData.EquipRightHand))
                npc.equipRightHand = savedData.EquipRightHand;
            if (!string.IsNullOrEmpty(savedData.EquipLeftHand))
                npc.equipLeftHand = savedData.EquipLeftHand;
            if (!string.IsNullOrEmpty(savedData.EquipRightHandData))
                npc.equipRightHandData = savedData.EquipRightHandData;
            if (!string.IsNullOrEmpty(savedData.EquipLeftHandData))
                npc.equipLeftHandData = savedData.EquipLeftHandData;
            if (!string.IsNullOrEmpty(savedData.EquipRightBack))
                npc.equipRightBack = savedData.EquipRightBack;
            if (!string.IsNullOrEmpty(savedData.EquipLeftBack))
                npc.equipLeftBack = savedData.EquipLeftBack;

            // Apply fashion/marketplace data
            if (!string.IsNullOrEmpty(savedData.FashionJson))
            {
                try
                {
                    NpcFashionManager.ApplyFashionJsonToNpc(savedData.FashionJson, npc);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SavedNpcManager] Failed to apply FashionJson: {ex.Message}");
                }
            }

            FiresLogger.LogVerbose($"[SavedNpcManager] Loaded saved data into NPC at {npc.transform.position}: {savedData.NpcProfileName}, HasEquipment: {npc.HasAnyEquipment()}");
            return true;
        }

        /// <summary>
        /// Saves NPC data from an NpcController to the persistent store.
        /// If running on the server (or host), writes to disk directly.
        /// If running on an admin client, pushes the file to the server via RPC.
        /// </summary>
        public static void SaveFromNpc(UI.NpcController npc)
        {
            if (npc == null) return;

            Initialize();

            string npcId = GenerateNpcId(npc.transform.position);

            var data = new SavedNpcData
            {
                NpcId = npcId,
                Position = npc.transform.position,
                NpcProfileName = npc.npcProfileName,
                DisplayNameOverride = npc.displayNameOverride,
                QuestProfile = npc.questProfile,
                DialogueProfile = npc.dialogueProfile,
                InfoProfile = npc.infoProfile,
                TraderProfile = npc.traderProfile,
                ModelOverride = npc.modelOverride,
                NpcType = npc.npcType,
                EquipHelmet = npc.equipHelmet,
                EquipChest = npc.equipChest,
                EquipLegs = npc.equipLegs,
                EquipShoulder = npc.equipShoulder,
                EquipUtility = npc.equipUtility,
                EquipRightHand = npc.equipRightHand,
                EquipLeftHand = npc.equipLeftHand,
                EquipRightBack = npc.equipRightBack,
                EquipLeftBack = npc.equipLeftBack,
                EquipRightHandData = npc.equipRightHandData,
                EquipLeftHandData = npc.equipLeftHandData
            };

            // Capture marketplace fashion data
            try
            {
                data.FashionJson = NpcFashionManager.ExtractFashionJsonFromNpc(npc);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Failed to extract FashionJson: {ex.Message}");
            }

            // Always update in-memory cache
            data.LastModified = DateTime.UtcNow;
            _savedNpcs[npcId] = data;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            if (isServer)
            {
                // Server: write to disk and broadcast to all clients
                WriteNpcFile(npcId, data);
                BroadcastNpcDataToClients(npcId, data);
            }
            else
            {
                // Admin client: push the file to the server via the existing SyncFiles RPC
                // The server's RPC_SyncFiles handler writes to disk, then the FileWatcher
                // broadcasts to all other clients
                PushNpcFileToServer(npcId, data);
            }

            // Templates are OPT-IN: only the explicit "Save to hammer" action creates one
            // (SaveNpcAsTemplate below). Auto-saving every named NPC flooded the hammer tab.
        }

        /// <summary>
        /// EXPLICIT "Save to hammer": snapshots this NPC's appearance (incl. the vanilla-look capture)
        /// into a name-keyed CLIENT-LOCAL template, photographs the live dressed body for the tile
        /// icon, and registers the hammer piece immediately — no relog. Returns the template name,
        /// or null with a reason when the NPC can't be saved yet.
        /// </summary>
        public static string SaveNpcAsTemplate(UI.NpcController npc, out string failReason)
        {
            failReason = null;
            if (npc == null) { failReason = "No NPC."; return null; }
            string displayName = npc.GetDisplayName();
            if (IsDefaultNpcName(displayName))
            {
                failReason = "Name the NPC first (Shift+E) so the preset has a name.";
                return null;
            }

            string templateName = MakeTemplateName(displayName);
            var snapshot = SnapshotFromNpc(npc);
            // Marketplace-style icon: photograph the LIVE dressed body (the best possible source —
            // it is fully equipped and posed right in front of the admin).
            TryCaptureTemplateIcon(npc, snapshot);
            SaveTemplate(templateName, snapshot);
            NpcTemplatePieces.AddOrRefreshTemplateLive(templateName);
            Debug.Log($"[SavedNpcManager] Saved NPC '{displayName}' to hammer as template '{templateName}'");
            return templateName;
        }

        /// <summary>
        /// Permanently deletes a saved template: memory + the client-local JSON + the live hammer
        /// piece. Nothing server-side to purge — templates never leave this machine.
        /// </summary>
        public static bool DeleteTemplate(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return false;
            EnsureTemplatesLoaded();
            bool existed = _templates.Remove(templateName);
            try
            {
                string filePath = Path.Combine(TemplatesFolder, templateName + ".json");
                if (File.Exists(filePath)) { File.Delete(filePath); existed = true; }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Failed to delete template file '{templateName}': {ex.Message}");
            }
            NpcTemplatePieces.RemoveTemplateLive(templateName);
            if (existed) Debug.Log($"[SavedNpcManager] Deleted NPC template '{templateName}'");
            return existed;
        }

        /// <summary>
        /// Renders the live NPC body to a 128px transparent icon and stores it as base64 PNG on the
        /// record. No-op on headless / render failure (existing icon is kept). Also used by the
        /// organic backfill for templates saved before icons existed.
        /// </summary>
        internal static void TryCaptureTemplateIcon(UI.NpcController npc, SavedNpcData record)
        {
            if (npc == null || record == null) return;
            try
            {
                // Marketplace photo pipeline (Core NpcPhotoManager): photographs a stripped visual
                // copy of the LIVE dressed body — the same recipe kg uses for its NPC previews.
                var sprite = FiresCore.UI.NpcPhotoManager.MakeSprite(npc.gameObject);
                if (sprite == null || sprite.texture == null)
                {
                    Debug.LogWarning($"[TemplateIcon] renderer returned null for '{npc.name}' — icon not captured (headless or render failure)");
                    return;
                }
                // ImageConversion via reflection — a direct reference to UnityEngine.ImageConversionModule
                // breaks the net48 build (module metadata demands netstandard 2.1; same trap as AudioModule).
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var encode = ic?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                byte[] png = encode != null ? encode.Invoke(null, new object[] { sprite.texture }) as byte[] : null;
                if (png != null && png.Length > 0)
                {
                    record.IconPng = Convert.ToBase64String(png);
                    Debug.Log($"[TemplateIcon] captured portrait icon from live NPC '{npc.name}' ({png.Length} bytes)");
                }
                else
                {
                    Debug.LogWarning($"[TemplateIcon] EncodeToPNG produced nothing for '{npc.name}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TemplateIcon] capture failed for '{npc?.name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Organic icon backfill for pre-icon templates: called client-side when a live static NPC
        /// finishes restoring its appearance. If its template exists without an icon, capture one now
        /// (the body is dressed and loaded right in front of the player).
        /// </summary>
        public static void BackfillTemplateIcon(UI.NpcController npc)
        {
            if (npc == null || Application.isBatchMode) return;
            string displayName = npc.GetDisplayName();
            if (IsDefaultNpcName(displayName)) return;
            string templateName = MakeTemplateName(displayName);
            var record = GetTemplate(templateName);
            // Blank-screened, not just null-screened: the naked-clone era persisted EMPTY icons,
            // and a poisoned blank must not block this live-body capture (the best icon source).
            if (record == null || NpcTemplateAppearance.IsMeaningfulIconPng(record.IconPng)) return;
            TryCaptureTemplateIcon(npc, record);
            if (!NpcTemplateAppearance.IsMeaningfulIconPng(record.IconPng)) return;
            SaveTemplate(templateName, record);
            NpcTemplatePieces.UpdateIcon(templateName, record.IconPng);
        }

        /// <summary>
        /// Names that are considered "default" and shouldn't auto-create templates.
        /// The user must rename the NPC first.
        /// </summary>
        private static readonly HashSet<string> _defaultNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NPC", "StaticNpc", "Static Quest NPC", "Static NPC", "CompanionNpc", "BaseNpc"
        };

        /// <summary>
        /// Returns true if the name is a default/generic NPC name that hasn't been renamed.
        /// </summary>
        public static bool IsDefaultNpcName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            return _defaultNames.Contains(name.Trim());
        }

        /// <summary>
        /// Removes saved data for an NPC.
        /// </summary>
        public static void RemoveNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;

            Initialize();

            if (_savedNpcs.Remove(npcId))
            {
                // Delete the per-NPC file
                try
                {
                    string filePath = NpcFilePath(npcId);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SavedNpcManager] Failed to delete NPC file: {ex.Message}");
                }

                Debug.Log($"[SavedNpcManager] Removed saved NPC: {npcId}");
            }
        }

        /// <summary>The single chokepoint for an admin DELIBERATELY removing a placed static NPC. It prunes the
        /// config record AND drops the desired-state placement entry + writes a delete tombstone (so the
        /// never-vanish reconciler never resurrects it) + cancels any pending death-respawn. On a dedicated
        /// server the admin is a remote client, so the client routes the delete to the server (the canonical
        /// writer), which prunes its files/registry and re-broadcasts the delete so every client mirror prunes.
        /// Key off the placement ANCHOR (npc_stationed_pos), never the live transform (a patrol NPC drifts).</summary>
        public static void AdminDeletePlacedNpc(Vector3 anchor) => AdminDeletePlacedNpc(anchor, null);

        /// <summary>
        /// <paramref name="anchorGuid"/> is the body's npc_anchor_guid back-ref when the caller has it —
        /// the EXACT identity of the owning anchor. Cell matching alone misses anchors whose Y drifted
        /// (head-height legacy mints healed down to feet level land in a different integer-meter cell),
        /// which left orphaned anchors behind hammer deletes.
        /// </summary>
        public static void AdminDeletePlacedNpc(Vector3 anchor, string anchorGuid)
        {
            Initialize();
            string npcId = GenerateNpcId(anchor);
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            RemoveNpc(npcId);   // prune our own copy immediately on either side

            if (isServer)
            {
                global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.Instance.RemovePlacement(anchor);
                DestroyAnchors(anchor, anchorGuid);
                BroadcastNpcDeleteToClients(npcId);
            }
            else
            {
                SendNpcDeleteToServer(npcId, anchor, anchorGuid);
            }
        }

        /// <summary>Two positions count as the same placement cell when they agree horizontally within
        /// 1.5m and vertically within 3m. NEVER compare GenerateNpcId strings for cell identity — the id
        /// rounds to integer meters INCLUDING Y, so feet-pin probes, anchor height healing, and plain
        /// rounding boundaries all silently break exact-string matches (proven: orphaned anchors on
        /// delete, missed record re-keys on move).</summary>
        internal static bool SamePlacementCell(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (dx * dx + dz * dz) <= 1.5f * 1.5f && Mathf.Abs(a.y - b.y) <= 3f;
        }

        /// <summary>
        /// Server-side: permanently deletes this NPC's anchor(s) — by exact guid when the caller knows
        /// it, else any anchor within the placement cell — AND each linked body, wherever it roamed.
        /// Without this, an admin delete removes the body while the anchor keeper resurrects it next
        /// tick. Destroys via the live instance when loaded, else directly at the ZDO.
        /// </summary>
        private static void DestroyAnchors(Vector3 position, string anchorGuid)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
                var buf = new List<ZDO>();
                int idx = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, buf, ref idx)) { }
                int destroyed = 0;
                foreach (var azdo in buf)
                {
                    if (azdo == null || !azdo.IsValid()) continue;
                    string guid = azdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, "");
                    bool match = !string.IsNullOrEmpty(anchorGuid)
                        ? guid == anchorGuid
                        : SamePlacementCell(azdo.GetPosition(), position);
                    if (!match) continue;

                    // Linked body first (a patrol body may be far from the aimed-at corpse the hammer hit).
                    var bodyId = azdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                    if (!bodyId.IsNone())
                    {
                        var bodyZdo = ZDOMan.instance.GetZDO(bodyId);
                        if (bodyZdo != null) DestroyZdoOrInstance(bodyZdo);
                    }

                    DestroyZdoOrInstance(azdo);
                    destroyed++;
                    Debug.Log($"[AnchorDelete] destroyed anchor {guid} at {azdo.GetPosition()} (requested pos={position} guid={(anchorGuid ?? "<cell match>")})");
                }
                if (destroyed == 0)
                    Debug.LogWarning($"[AnchorDelete] NO anchor matched pos={position} guid={(anchorGuid ?? "<none>")} — " +
                        $"scanned {buf.Count} anchor ZDO(s); an orphaned anchor will keep respawning this NPC");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AnchorDelete] anchor delete failed at pos={position}: {ex}");
            }
        }

        /// <summary>Destroys a networked object through its live instance when one exists (proper
        /// networked destroy), else claims the ZDO and removes it directly (unloaded zone).
        /// Suppresses the deliberate-removal catch-all — this IS the delete flow.</summary>
        private static void DestroyZdoOrInstance(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;   // double-destroy guard (delete + move can overlap)
            bool prior = global::FiresRPGmaker.UI.Patches.StaticNpc_DeliberateRemoveCatchAll.SuppressRouting;
            global::FiresRPGmaker.UI.Patches.StaticNpc_DeliberateRemoveCatchAll.SuppressRouting = true;
            try
            {
                var instance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                if (instance != null)
                {
                    instance.ClaimOwnership();
                    ZNetScene.instance.Destroy(instance.gameObject);
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(zdo);
                }
            }
            finally
            {
                global::FiresRPGmaker.UI.Patches.StaticNpc_DeliberateRemoveCatchAll.SuppressRouting = prior;
            }
        }

        /// <summary>
        /// Admin action: relocate an NPC's anchor (and its body + saved record) to a new position.
        /// Client-callable — routes to the server, which validates admin and applies. Local-host
        /// servers apply directly.
        /// </summary>
        public static void RequestAnchorMove(string anchorGuid, Vector3 newPos, float yawDegrees = float.NaN)
        {
            if (string.IsNullOrEmpty(anchorGuid)) return;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ApplyAnchorMove(anchorGuid, newPos, yawDegrees);
                return;
            }
            try
            {
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(anchorGuid);
                pkg.Write(newPos.x); pkg.Write(newPos.y); pkg.Write(newPos.z);
                pkg.Write(yawDegrees);   // optional trailing field — old servers never read this far
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_NPC_ANCHOR_MOVE, pkg);   // no target == route to server
                Debug.Log($"[SavedNpcManager] Sent anchor move '{anchorGuid}' → {newPos} yaw={yawDegrees} to server");
            }
            catch (Exception ex) { Debug.LogWarning($"[SavedNpcManager] anchor-move send failed: {ex.Message}"); }
        }

        /// <summary>Server handler for an admin client's anchor-move request (admin-gated).</summary>
        private static void RPC_OnNpcAnchorMove(long sender, ZPackage pkg)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                string guid = pkg.ReadString();
                float x = pkg.ReadSingle(), y = pkg.ReadSingle(), z = pkg.ReadSingle();
                float yaw = float.NaN;
                try { if (pkg.GetPos() < pkg.Size()) yaw = pkg.ReadSingle(); } catch { }
                if (!AdminSyncing.IsAdmin(sender))
                {
                    Debug.LogWarning($"[SavedNpcManager] non-admin {sender} attempted anchor move '{guid}' — denied");
                    return;
                }
                ApplyAnchorMove(guid, new Vector3(x, y, z), yaw);
            }
            catch (Exception ex) { Debug.LogError($"[SavedNpcManager] RPC_OnNpcAnchorMove error: {ex}"); }
        }

        /// <summary>Tolerant record lookup: exact position-keyed id first, then a proximity scan over
        /// the cached records — exact ids go stale whenever the anchor's Y drifts (height healing,
        /// feet probes), which silently orphaned config on anchor moves.</summary>
        private static SavedNpcData FindNpcRecordNear(Vector3 pos, out string foundId)
        {
            foundId = GenerateNpcId(pos);
            var exact = GetNpc(foundId);
            if (exact != null) return exact;
            foreach (var kv in _savedNpcs)
            {
                if (kv.Value == null) continue;
                if (SamePlacementCell(kv.Value.Position, pos)) { foundId = kv.Key; return kv.Value; }
            }
            return null;
        }

        /// <summary>
        /// Server-side: repositions the anchor ZDO (+ live instance), re-homes the linked body by
        /// destroy-and-keeper-respawn, purges the LEGACY registry entry at the old home (the reconciler
        /// revived an unnamed duplicate there otherwise), and re-keys the position-keyed saved record.
        /// Every stage is individually guarded and logged — a mid-move throw previously left half-moved
        /// state behind with no clue which stage died.
        /// </summary>
        private static void ApplyAnchorMove(string anchorGuid, Vector3 newPos, float yawDegrees = float.NaN)
        {
            if (ZDOMan.instance == null) return;
            var buf = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, buf, ref idx)) { }
            foreach (var azdo in buf)
            {
                if (azdo == null || !azdo.IsValid()) continue;
                if (azdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, "") != anchorGuid) continue;

                Vector3 oldPos = azdo.GetPosition();
                string stage = "reposition-anchor";
                try
                {
                    azdo.SetPosition(newPos);
                    if (!float.IsNaN(yawDegrees))
                        azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Rotation, Quaternion.Euler(0f, yawDegrees, 0f));
                    var anchorInstance = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(azdo) : null;
                    if (anchorInstance != null)
                    {
                        anchorInstance.transform.position = newPos;
                        if (!float.IsNaN(yawDegrees)) anchorInstance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
                    }

                    // Re-home by RESPAWN, not teleport. The old transform-teleport was written to a
                    // NON-OWNER instance (the owner client's ZSyncTransform immediately re-asserted the
                    // old position), so the NPC never visibly moved — and the half-moved state produced
                    // a floating duplicate after a zone reload. Destroying the body and letting the
                    // anchor keeper re-create it at the moved anchor is atomic: one authority, full
                    // payload, exact new position.
                    stage = "destroy-body";
                    var bodyId = azdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                    var bodyZdo = bodyId.IsNone() ? null : ZDOMan.instance.GetZDO(bodyId);
                    if (bodyZdo != null && bodyZdo.IsValid()) DestroyZdoOrInstance(bodyZdo);

                    stage = "stamp-respawn";
                    azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.DeathPos, Vector3.zero);
                    // Ancient missing-stamp: any respawn delay (wander/patrol) reads as already elapsed,
                    // so the keeper re-creates the body on its next tick (~5s).
                    azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.MissingSince, 1L);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AnchorMove] {anchorGuid} FAILED at stage '{stage}' ({oldPos} → {newPos}): {ex}");
                    return;
                }

                // Purge the legacy registry at the OLD home — with the anchor gone from that cell the
                // reconciler otherwise revives an unnamed NPC there (which then mints its own anchor:
                // the observed move-duplication). Guarded separately: registry trouble must never undo
                // the completed move above.
                try
                {
                    global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.Instance.RemovePlacement(oldPos);
                }
                catch (Exception ex) { Debug.LogWarning($"[AnchorMove] legacy-registry purge at {oldPos} failed: {ex.Message}"); }

                // Re-key the position-keyed saved record to the new cell (tolerant lookup — exact ids
                // go stale when the anchor's Y drifts).
                try
                {
                    var record = FindNpcRecordNear(oldPos, out string oldId);
                    string newCell = GenerateNpcId(newPos);
                    if (record != null && !string.Equals(oldId, newCell, StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveNpc(oldId);
                        record.NpcId = newCell;
                        record.Position = newPos;
                        _savedNpcs[newCell] = record;
                        WriteNpcFile(newCell, record);
                        BroadcastNpcDataToClients(newCell, record);
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[AnchorMove] saved-record re-key failed: {ex.Message}"); }

                Debug.Log($"[AnchorMove] moved anchor {anchorGuid}: {oldPos} → {newPos}" +
                          (float.IsNaN(yawDegrees) ? "" : $" yaw={yawDegrees:F0}°"));
                return;
            }
            Debug.LogWarning($"[AnchorMove] anchor move: guid '{anchorGuid}' not found");
        }

        /// <summary>Admin client → server: ask the canonical writer to delete this NPC's record + registry.</summary>
        private static void SendNpcDeleteToServer(string npcId, Vector3 anchor, string anchorGuid = null)
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(npcId);
                pkg.Write(anchor.x); pkg.Write(anchor.y); pkg.Write(anchor.z);
                pkg.Write(anchorGuid ?? "");   // optional trailing field — old servers never read this far
                ZRoutedRpc.instance.InvokeRoutedRPC(RPC_NPC_DATA_DELETE, pkg);   // no target == route to server
                Debug.Log($"[SavedNpcManager] Sent NPC delete '{npcId}' (anchor guid {(string.IsNullOrEmpty(anchorGuid) ? "<none>" : anchorGuid)}) to server");
            }
            catch (Exception ex) { Debug.LogWarning($"[SavedNpcManager] delete-to-server failed: {ex.Message}"); }
        }

        /// <summary>Server → every client: prune this NPC from each client's mirror folder + in-memory cache.</summary>
        private static void BroadcastNpcDeleteToClients(string npcId)
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                var peers = ZNet.instance.GetPeers();
                if (peers == null) return;
                foreach (var peer in peers)
                {
                    if (peer == null || !peer.IsReady()) continue;
                    try
                    {
                        var pkg = new ZPackage();
                        pkg.Write(npcId);
                        pkg.Write(0f); pkg.Write(0f); pkg.Write(0f);   // anchor unused on the client side
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RPC_NPC_DATA_DELETE, pkg);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[SavedNpcManager] broadcast delete failed: {ex.Message}"); }
        }

        /// <summary>Receives a delete. On the SERVER (a request from an admin client) it prunes the canonical
        /// record + registry and fans the delete out to all peers. On a CLIENT it prunes the local mirror.</summary>
        private static void RPC_OnNpcDataDelete(long sender, ZPackage pkg)
        {
            try
            {
                string npcId = pkg.ReadString();
                float ax = pkg.ReadSingle(), ay = pkg.ReadSingle(), az = pkg.ReadSingle();
                if (string.IsNullOrEmpty(npcId)) return;

                bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
                if (isServer)
                {
                    // Destructive + permanent (writes a tombstone the reconciler honors), so verify the
                    // requester is a synced admin — matching the gate the hammer-remove already applies
                    // client-side, and the IsAdmin(sender) pattern used at Ascend.cs / TerritoryRuntime.cs.
                    if (!AdminSyncing.IsAdmin(sender))
                    {
                        Debug.LogWarning($"[SavedNpcManager] non-admin {sender} attempted static-NPC delete '{npcId}' — denied");
                        return;
                    }
                    // Optional trailing anchor guid (newer clients) — old packages simply end here.
                    string anchorGuid = null;
                    try { if (pkg.GetPos() < pkg.Size()) anchorGuid = pkg.ReadString(); } catch { }

                    RemoveNpc(npcId);
                    global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.Instance
                        .RemovePlacement(new Vector3(ax, ay, az));
                    DestroyAnchors(new Vector3(ax, ay, az), string.IsNullOrEmpty(anchorGuid) ? null : anchorGuid);
                    BroadcastNpcDeleteToClients(npcId);
                }
                else
                {
                    Initialize();
                    _savedNpcs.Remove(npcId);
                    try { string fp = NpcFilePath(npcId); if (File.Exists(fp)) File.Delete(fp); } catch { }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[SavedNpcManager] RPC_OnNpcDataDelete error: {ex.Message}"); }
        }

        /// <summary>
        /// Gets all saved NPC data.
        /// </summary>
        public static IEnumerable<SavedNpcData> GetAllSavedNpcs()
        {
            Initialize();
            return _savedNpcs.Values;
        }

        #region Disk I/O - Per-NPC Files

        private static void EnsureFolderExists()
        {
            try
            {
                if (!Directory.Exists(SavedNpcsFolder))
                {
                    Directory.CreateDirectory(SavedNpcsFolder);
                    FiresLogger.LogVerbose($"[SavedNpcManager] Created SavedNPCs folder: {SavedNpcsFolder}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SavedNpcManager] Failed to create folder: {ex.Message}");
            }
        }

        /// <summary>
        /// Migrates the legacy single npcs.json into per-NPC files, then renames the old file.
        /// </summary>
        private static void MigrateLegacyFile()
        {
            try
            {
                if (!File.Exists(LegacySavedNpcsFile)) return;

                string json = File.ReadAllText(LegacySavedNpcsFile);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, SavedNpcData>>(json, _jsonSettings);
                if (loaded == null || loaded.Count == 0)
                {
                    // Nothing to migrate - just rename
                    File.Move(LegacySavedNpcsFile, LegacySavedNpcsFile + ".bak");
                    return;
                }

                int migrated = 0;
                foreach (var kvp in loaded)
                {
                    string filePath = NpcFilePath(kvp.Key);
                    if (!File.Exists(filePath))
                    {
                        string npcJson = JsonConvert.SerializeObject(kvp.Value, _jsonSettings);
                        File.WriteAllText(filePath, npcJson);
                        migrated++;
                    }
                }

                // Rename old file so migration doesn't run again
                File.Move(LegacySavedNpcsFile, LegacySavedNpcsFile + ".bak");
                Debug.Log($"[SavedNpcManager] Migrated {migrated} NPCs from legacy npcs.json to per-NPC files");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Legacy migration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads all per-NPC JSON files from the SavedNPCs folder.
        /// </summary>
        private static void LoadAllNpcFiles()
        {
            _savedNpcs.Clear();

            try
            {
                if (!Directory.Exists(SavedNpcsFolder)) return;

                var files = Directory.GetFiles(SavedNpcsFolder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var data = JsonConvert.DeserializeObject<SavedNpcData>(json, _jsonSettings);
                        if (data != null && !string.IsNullOrEmpty(data.NpcId))
                        {
                            _savedNpcs[data.NpcId] = data;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SavedNpcManager] Failed to load NPC file {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                FiresLogger.LogVerbose($"[SavedNpcManager] Loaded {_savedNpcs.Count} NPCs from per-NPC files");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SavedNpcManager] Failed to load NPC files: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a single NPC's data to its own JSON file.
        /// </summary>
        private static void WriteNpcFile(string npcId, SavedNpcData data)
        {
            try
            {
                EnsureFolderExists();
                string json = JsonConvert.SerializeObject(data, _jsonSettings);
                File.WriteAllText(NpcFilePath(npcId), json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SavedNpcManager] Failed to write NPC file {npcId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces all in-memory NPCs to disk (call on world save).
        /// </summary>
        public static void ForceSave()
        {
            foreach (var kvp in _savedNpcs)
            {
                WriteNpcFile(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Reloads saved NPCs from disk.
        /// </summary>
        public static void Reload()
        {
            _savedNpcs.Clear();
            _initialized = false;
            Initialize();
        }

        #endregion

        #region NPC Templates - Name-Keyed Saved Configs

        private static Dictionary<string, SavedNpcData> _templates = new Dictionary<string, SavedNpcData>(StringComparer.OrdinalIgnoreCase);
        private static bool _templatesLoaded;

        private static string TemplatesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.SavedNpcs, "templates");

        /// <summary>
        /// Sanitises a display name into a safe file-system key.
        /// </summary>
        public static string MakeTemplateName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "unnamed";
            var sb = new System.Text.StringBuilder();
            foreach (char c in displayName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "unnamed";
        }

        /// <summary>
        /// Returns true if a template with this name already exists on disk.
        /// </summary>
        public static bool TemplateExists(string templateName)
        {
            EnsureTemplatesLoaded();
            return _templates.ContainsKey(templateName);
        }

        /// <summary>
        /// Gets a read-only view of all loaded template names.
        /// </summary>
        public static IEnumerable<string> GetAllTemplateNames()
        {
            EnsureTemplatesLoaded();
            return _templates.Keys;
        }

        /// <summary>
        /// Gets a template by name.
        /// </summary>
        public static SavedNpcData GetTemplate(string templateName)
        {
            EnsureTemplatesLoaded();
            _templates.TryGetValue(templateName, out var data);
            return data;
        }

        /// <summary>
        /// Saves (or overwrites) a template by name. Writes JSON to templates/ subfolder
        /// and syncs to the server if we're a client.
        /// </summary>
        public static void SaveTemplate(string templateName, SavedNpcData data)
        {
            if (string.IsNullOrEmpty(templateName) || data == null) return;

            EnsureTemplatesLoaded();

            data.LastModified = DateTime.UtcNow;
            // Clear position-specific data - templates are name-keyed, not position-keyed
            data.NpcId = "template_" + templateName;
            data.Position = Vector3.zero;

            _templates[templateName] = data;
            WriteTemplateFile(templateName, data);
            // Client-local BY DESIGN (Marketplace model): the appearance is baked into the anchor at
            // placement time and THAT syncs — the template itself never leaves this machine. The old
            // PushTemplateToServer broadcast landed in other clients' position-keyed store as junk
            // ("template_*" ids) without ever becoming usable templates there.

            Debug.Log($"[SavedNpcManager] Saved NPC template: {templateName}");
        }

        /// <summary>
        /// Creates a SavedNpcData snapshot from a live NpcController (for template saves).
        /// </summary>
        public static SavedNpcData SnapshotFromNpc(UI.NpcController npc)
        {
            if (npc == null) return null;

            var data = new SavedNpcData
            {
                NpcProfileName = npc.npcProfileName,
                DisplayNameOverride = npc.displayNameOverride,
                QuestProfile = npc.questProfile,
                DialogueProfile = npc.dialogueProfile,
                InfoProfile = npc.infoProfile,
                TraderProfile = npc.traderProfile,
                ModelOverride = npc.modelOverride,
                NpcType = npc.npcType,
                EquipHelmet = npc.equipHelmet,
                EquipChest = npc.equipChest,
                EquipLegs = npc.equipLegs,
                EquipShoulder = npc.equipShoulder,
                EquipUtility = npc.equipUtility,
                EquipRightHand = npc.equipRightHand,
                EquipLeftHand = npc.equipLeftHand,
                EquipRightBack = npc.equipRightBack,
                EquipLeftBack = npc.equipLeftBack,
                EquipRightHandData = npc.equipRightHandData,
                EquipLeftHandData = npc.equipLeftHandData
            };

            try
            {
                data.FashionJson = NpcFashionManager.ExtractFashionJsonFromNpc(npc);
            }
            catch { }

            if (string.IsNullOrEmpty(data.FashionJson))
                data.FashionJson = npc.GetPendingFashionJson();

            // The VANILLA look (item hashes, model index, colors) — for randomly-dressed NPCs this is
            // the ONLY place their appearance exists; without it the template is naked.
            try
            {
                var nview = npc.GetComponent<ZNetView>();
                var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                data.LookJson = NpcTemplateAppearance.CaptureLookJson(zdo);
            }
            catch { }

            return data;
        }

        private static void EnsureTemplatesLoaded()
        {
            if (_templatesLoaded) return;
            _templatesLoaded = true;

            _templates.Clear();

            try
            {
                if (!Directory.Exists(TemplatesFolder))
                {
                    Directory.CreateDirectory(TemplatesFolder);
                    return;
                }

                var files = Directory.GetFiles(TemplatesFolder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var data = JsonConvert.DeserializeObject<SavedNpcData>(json, _jsonSettings);
                        if (data != null)
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            _templates[name] = data;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SavedNpcManager] Failed to load template {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                FiresLogger.LogVerbose($"[SavedNpcManager] Loaded {_templates.Count} NPC templates");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SavedNpcManager] Failed to load templates: {ex.Message}");
            }
        }

        private static void WriteTemplateFile(string templateName, SavedNpcData data)
        {
            try
            {
                if (!Directory.Exists(TemplatesFolder))
                    Directory.CreateDirectory(TemplatesFolder);

                string json = JsonConvert.SerializeObject(data, _jsonSettings);
                string filePath = Path.Combine(TemplatesFolder, templateName + ".json");
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SavedNpcManager] Failed to write template {templateName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reloads templates from disk.
        /// </summary>
        public static void ReloadTemplates()
        {
            _templatesLoaded = false;
            EnsureTemplatesLoaded();
        }

        #endregion

        #region RPC Sync - Server Authoritative

        /// <summary>
        /// Admin client pushes an NPC file to the server via the existing SyncFiles RPC.
        /// The server writes the file, the FileWatcher detects the change and broadcasts
        /// to all other clients.
        /// </summary>
        private static void PushNpcFileToServer(string npcId, SavedNpcData data)
        {
            try
            {
                if (ZRoutedRpc.instance == null) return;

                string json = JsonConvert.SerializeObject(data, _jsonSettings);
                string fileName = NpcIdToFileName(npcId);

                var pkg = new ZPackage();
                pkg.Write("SavedNPCs");
                pkg.Write(fileName);
                pkg.Write(json);

                ZRoutedRpc.instance.InvokeRoutedRPC("FiresRPGmaker_SyncFiles", pkg);
                Debug.Log($"[SavedNpcManager] Pushed NPC {npcId} to server via SyncFiles RPC");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Failed to push NPC to server: {ex.Message}");
            }
        }

        /// <summary>
        /// Server broadcasts a single NPC's data to all connected clients.
        /// </summary>
        private static void BroadcastNpcDataToClients(string npcId, SavedNpcData data)
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            try
            {
                string json = JsonConvert.SerializeObject(data, _jsonSettings);

                var peers = ZNet.instance.GetPeers();
                if (peers == null) return;

                foreach (var peer in peers)
                {
                    if (peer == null || !peer.IsReady()) continue;

                    try
                    {
                        var pkg = new ZPackage();
                        pkg.Write(npcId);
                        pkg.Write(json);
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RPC_NPC_DATA_UPDATE, pkg);
                    }
                    catch { }
                }

                Debug.Log($"[SavedNpcManager] Broadcast NPC {npcId} to {peers.Count} clients");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Failed to broadcast NPC data: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends all NPC files to a specific client (on connect).
        /// Uses <see cref="ManifestDiffSync"/> to skip any NPC
        /// whose on-disk JSON already hashes identically on the client, so logins
        /// don't re-ship the entire saved-NPC set every time. A short manifest wait
        /// is performed first; if the client is running an older build (or the reply
        /// never arrives), we fall back to the legacy "send everything" behaviour so
        /// the client ends up synced regardless.
        /// </summary>
        public static void SendAllNpcDataToClient(long peerUid)
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            Initialize();

            // Kick off the manifest request up front and drive the actual per-file
            // dispatch from a coroutine so we can yield on the reply without blocking
            // the calling frame. FiresRPGmaker.Instance is the MonoBehaviour host.
            try
            {
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(SendAllNpcDataToClientCoroutine(peerUid));
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] Could not start diff-sync coroutine, falling back: {ex.Message}");
            }

            // Fallback: original "blast everything" path if we somehow can't reach
            // a coroutine host. Still correct, just noisier on the wire.
            SendAllNpcDataBlind(peerUid);
        }

        private static IEnumerator SendAllNpcDataToClientCoroutine(long peerUid)
        {
            ManifestDiffSync.RequestFromPeer(peerUid, ManifestNamespace);
            yield return ManifestDiffSync.WaitForManifest(peerUid, ManifestNamespace, 3f);

            // Server-side hashing MUST match what the client sees on its own disk, or
            // every login would re-send everything. We hash the actual file bytes on
            // disk rather than re-serialising the in-memory dictionary: round-tripping
            // a SavedNpcData through JsonConvert.Deserialize->Serialize is NOT
            // guaranteed to produce byte-identical output (dictionary iteration order,
            // null/default emission, whitespace), so "server re-serialises from memory"
            // vs "client hashes the file" drifts on every connection and triggers a
            // useless full retransmit. Reading the file here (same JSON that was
            // originally written by WriteNpcFile) keeps both sides byte-comparable.
            int sent = 0, skipped = 0, missing = 0;
            foreach (var kvp in _savedNpcs)
            {
                string relativePath = NpcIdToFileName(kvp.Key);
                string filePath = NpcFilePath(kvp.Key);

                string json;
                try
                {
                    if (!File.Exists(filePath))
                    {
                        // File wasn't persisted yet (edge case: created in memory
                        // without WriteNpcFile having run). Fall back to a one-shot
                        // serialisation just for this entry. It may still drift on the
                        // client's hash check but at least the client gets the data.
                        json = JsonConvert.SerializeObject(kvp.Value, _jsonSettings);
                        missing++;
                    }
                    else
                    {
                        json = File.ReadAllText(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SavedNpcManager] Read '{kvp.Key}' failed: {ex.Message}");
                    continue;
                }

                string localHash = ManifestDiffSync.ComputeSha256(json);
                if (ManifestDiffSync.HasSameHash(peerUid, ManifestNamespace, relativePath, localHash))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(kvp.Key);
                    pkg.Write(json);
                    SafeRoutedRpc.InvokeSafe(peerUid, RPC_NPC_DATA_UPDATE, pkg);
                    sent++;
                }
                catch { }

                // Yield between files so the Steam send queue flushes each RPC as its
                // own message (see SafeRoutedRpc notes) and login CPU cost is spread.
                yield return null;
            }

            FiresLogger.LogVerbose($"[SavedNpcManager] Diff-sync to client {peerUid}: sent {sent}, skipped {skipped} unchanged" +
                      (missing > 0 ? $", {missing} missing-on-disk" : "") +
                      $" (of {_savedNpcs.Count} total)");
        }

        private static void SendAllNpcDataBlind(long peerUid)
        {
            foreach (var kvp in _savedNpcs)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(kvp.Value, _jsonSettings);

                    var pkg = new ZPackage();
                    pkg.Write(kvp.Key);
                    pkg.Write(json);
                    SafeRoutedRpc.InvokeSafe(peerUid, RPC_NPC_DATA_UPDATE, pkg);
                }
                catch { }
            }

            FiresLogger.LogVerbose($"[SavedNpcManager] Sent {_savedNpcs.Count} NPC entries to client {peerUid} (blind fallback)");
        }

        /// <summary>
        /// RPC handler - receives NPC data from the server (on clients) or could receive
        /// from an admin on the server (handled via SyncFiles instead).
        /// </summary>
        private static void RPC_OnNpcDataUpdate(long sender, ZPackage pkg)
        {
            try
            {
                string npcId = pkg.ReadString();
                string json = pkg.ReadString();

                if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(json)) return;

                // Templates are client-local; servers running older builds still broadcast
                // "template_*" records — drop them (they only ever polluted _savedNpcs).
                if (npcId.StartsWith("template_", StringComparison.OrdinalIgnoreCase)) return;

                var data = JsonConvert.DeserializeObject<SavedNpcData>(json, _jsonSettings);
                if (data == null) return;

                // Update in-memory cache
                Initialize();
                _savedNpcs[npcId] = data;

                // Persist the exact bytes we just received to disk ON CLIENTS so the
                // next login's ManifestDiffSync manifest reports the SAME SHA-256 as
                // the server's on-disk file, letting the diff-sync actually skip the
                // re-send. If we only updated _savedNpcs in memory, the local file
                // either stayed stale or never existed, and every login would blindly
                // pull all NPCs over the wire again.
                //
                // We skip this write on the server because (a) the server already owns
                // the canonical file - the RPC handler is only hit there when an admin
                // pushes an edit via SyncFiles, which itself writes to disk and
                // triggers the FileWatcher, and (b) writing here would re-fire the
                // FileWatcher and potentially loop.
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    try
                    {
                        EnsureFolderExists();
                        File.WriteAllText(NpcFilePath(npcId), json);
                    }
                    catch (Exception writeEx)
                    {
                        Debug.LogWarning($"[SavedNpcManager] Failed to persist received NPC '{npcId}' to disk: {writeEx.Message}");
                    }
                }

                // Try to find any live NPC at this position and refresh it
                RefreshLiveNpc(data);

                // Demoted to verbose: one of these fires per NPC per join /
                // per zone load, which floods the BepInEx console (dozens of
                // lines whenever a player teleports or a wild companion
                // spawns near a populated area). Useful for debugging RPC
                // delivery, not useful in normal play.
                FiresLogger.LogVerbose($"[SavedNpcManager] Received NPC data update: {npcId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] RPC_OnNpcDataUpdate error: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a live NpcController near the saved position and re-applies the data.
        /// </summary>
        private static void RefreshLiveNpc(SavedNpcData data)
        {
            if (data == null) return;

            try
            {
                var npcs = UnityEngine.Object.FindObjectsByType<UI.NpcController>(UnityEngine.FindObjectsSortMode.None);
                foreach (var npc in npcs)
                {
                    if (npc == null) continue;

                    string id = GenerateNpcId(npc.transform.position);
                    if (string.Equals(id, data.NpcId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Apply saved data to the live NPC WITHOUT triggering a save-back.
                        // LoadIntoNpc sets properties, then we write to ZDO directly
                        // instead of calling npc.SaveToZDO() which would push back to server
                        // and create an infinite loop.
                        LoadIntoNpc(npc);
                        var zdo = npc.GetComponent<ZNetView>()?.GetZDO();
                        if (zdo != null)
                        {
                            zdo.Set("npc_profile", npc.npcProfileName ?? "");
                            zdo.Set("npc_name_override", npc.displayNameOverride ?? "");
                            zdo.Set("npc_dialogue_profile", npc.dialogueProfile ?? "");
                            zdo.Set("npc_quest_profile", npc.questProfile ?? "");
                            zdo.Set("npc_info_profile", npc.infoProfile ?? "");
                            zdo.Set("npc_trader_profile", npc.traderProfile ?? "");
                            zdo.Set("npc_model_override", npc.modelOverride ?? "");
                            zdo.Set("npc_type", (int)npc.npcType);
                            zdo.Set("npc_allow_idle_wander", npc.allowIdleWandering);
                            zdo.Set("npc_equip_helmet", npc.equipHelmet ?? "");
                            zdo.Set("npc_equip_chest", npc.equipChest ?? "");
                            zdo.Set("npc_equip_legs", npc.equipLegs ?? "");
                            zdo.Set("npc_equip_shoulder", npc.equipShoulder ?? "");
                            zdo.Set("npc_equip_utility", npc.equipUtility ?? "");
                            zdo.Set("npc_equip_righthand", npc.equipRightHand ?? "");
                            zdo.Set("npc_equip_lefthand", npc.equipLeftHand ?? "");
                            zdo.Set("npc_equip_rightback", npc.equipRightBack ?? "");
                            zdo.Set("npc_equip_leftback", npc.equipLeftBack ?? "");
                            if (data.FashionJson != null)
                                zdo.Set("npc_fashion_json", data.FashionJson);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] RefreshLiveNpc error: {ex.Message}");
            }
        }

        private static Dictionary<string, float> _recentServerWrites = new Dictionary<string, float>();
        private const float SERVER_WRITE_COOLDOWN = 2f;

        /// <summary>
        /// Called from ServerConfigFileWatcher when a SavedNPCs/*.json file changes on the server.
        /// Reloads that single NPC from disk and broadcasts to all clients.
        /// Debounced: ignores duplicate FileWatcher events within a short cooldown.
        /// </summary>
        public static void OnServerFileChanged(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            // Server-side SavedNPCs/templates/*.json are fossils of the pre-278 template push —
            // templates are client-local; never re-ingest or broadcast them (they polluted
            // _savedNpcs with template_* records on every file event).
            if (fileName.Replace('\\', '/').StartsWith("templates/", StringComparison.OrdinalIgnoreCase)) return;

            // Debounce: FileSystemWatcher fires multiple events per write
            float now = Time.realtimeSinceStartup;
            if (_recentServerWrites.TryGetValue(fileName, out float lastTime) && (now - lastTime) < SERVER_WRITE_COOLDOWN)
                return;
            _recentServerWrites[fileName] = now;

            Initialize();

            string filePath = Path.Combine(SavedNpcsFolder, fileName);
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var data = JsonConvert.DeserializeObject<SavedNpcData>(json, _jsonSettings);
                if (data != null && !string.IsNullOrEmpty(data.NpcId))
                {
                    _savedNpcs[data.NpcId] = data;
                    BroadcastNpcDataToClients(data.NpcId, data);
                    RefreshLiveNpc(data);
                    Debug.Log($"[SavedNpcManager] Server reloaded and broadcast NPC: {data.NpcId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SavedNpcManager] OnServerFileChanged error for {fileName}: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Custom JSON converter for Unity Vector3 to avoid self-referencing loop on 'normalized' property.
    /// </summary>
    public class Vector3Converter : JsonConverter<Vector3>
    {
    public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
     {
    if (reader.TokenType == JsonToken.Null)
       return Vector3.zero;

      var obj = JObject.Load(reader);
   return new Vector3(
        obj["x"]?.Value<float>() ?? 0f,
         obj["y"]?.Value<float>() ?? 0f,
          obj["z"]?.Value<float>() ?? 0f
            );
        }

      public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
          writer.WritePropertyName("x");
 writer.WriteValue(value.x);
        writer.WritePropertyName("y");
            writer.WriteValue(value.y);
        writer.WritePropertyName("z");
       writer.WriteValue(value.z);
   writer.WriteEndObject();
     }
    }
}
