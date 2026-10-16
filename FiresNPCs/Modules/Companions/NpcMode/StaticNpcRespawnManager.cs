using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Companions.NpcMode
{
    /// <summary>
    /// Server-side, crash-safe respawn timer for hammer-placed static NPCs. Static NPCs are ownerless,
    /// world-scoped entities — deliberately OUTSIDE the player-keyed companion dormancy seam
    /// (NpcDormancyBridge, which guards on playerId != 0 and explicitly skips npc_stationed). So they get
    /// their own dormant store, modelled on the same hardening invariants the kennel uses:
    ///
    ///   • <b>Persistent</b> — pending respawns are written to a world-UID-keyed JSON file
    ///     (<c>FiresRPGmaker/StaticRespawns/&lt;worldUID&gt;.json</c>), so a server crash/restart during a
    ///     death window does NOT lose the respawn.
    ///   • <b>Absolute-UTC deadlines</b> — each entry stores <c>DeadlineUtcTicks</c> (wall-clock), not a
    ///     session-relative <c>Time.time</c> offset, so a restart resumes the timer with zero drift.
    ///   • <b>Single live instance</b> — before spawning, we skip if a static NPC already exists at that
    ///     position (covers crash-after-spawn-before-save: the stale entry is cleared, never doubled).
    ///   • <b>No silent loss</b> — a spawn that can't complete yet (prefab not registered) keeps its entry
    ///     for the next tick instead of dropping it.
    ///
    /// Config (profiles/type/equipment/fashion) restores by position via <see cref="SavedNpcManager"/>;
    /// the ZDO-only appearance travels in the persisted entry.
    /// </summary>
    public class StaticNpcRespawnManager : MonoBehaviour
    {
        public const float RespawnDelaySeconds = 60f;
        private const float DupePositionEpsilon = 1.5f;

        private static StaticNpcRespawnManager _instance;
        public static StaticNpcRespawnManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("StaticNpcRespawnManager");
                    _instance = go.AddComponent<StaticNpcRespawnManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>One pending static-NPC respawn — JSON-serializable (primitives only) so it survives a
        /// restart. Carries the prefab NAME (stable across sessions, unlike the runtime hash), placed
        /// transform, deadline, and the ZDO-only appearance.</summary>
        public class PendingEntry
        {
            public long DeadlineUtcTicks;
            public string PrefabName;
            public float PosX, PosY, PosZ;
            public float RotX, RotY, RotZ, RotW;
            public string NameOverride;
            public float Scale = 1f;
            public int ModelIndex;
            public bool IsFemale;
            public string Hair, Beard;
            public float HairR, HairG, HairB;
            public float SkinR, SkinG, SkinB;
            public float EyeR, EyeG, EyeB;

            // Patrol respawn: Pos is WHERE it respawns (last checkpoint if patrolling, else home); Home is the
            // placed position used to look up the position-keyed saved config. RespawnDelay is per-NPC.
            public float HomeX, HomeY, HomeZ;
            public string PatrolRouteName;
            public float RespawnDelay = RespawnDelaySeconds;

            [JsonIgnore] public Vector3 Position => new Vector3(PosX, PosY, PosZ);
            [JsonIgnore] public Quaternion Rotation => new Quaternion(RotX, RotY, RotZ, RotW);
            [JsonIgnore] public Vector3 HomePosition => new Vector3(HomeX, HomeY, HomeZ);
        }

        private readonly List<PendingEntry> _pending = new List<PendingEntry>();
        private bool _loaded;
        private long _loadedWorldUid;
        private float _lastCheck;

        // ── desired-state registry (the never-vanish safety net) ──────────────
        // _placements is the authoritative "this static NPC SHOULD exist at this anchor" set, keyed by the
        // placement (anchor) cell. A BUG-vanish (FGN ownership release, clutter cleanup, void/water fall —
        // none of which run Character.OnDeath) leaves the entry intact, so the reconciler re-materializes it.
        // An ADMIN delete removes the entry AND writes a _tombstones marker, so the reconciler never
        // resurrects a deliberately-removed NPC. Both persist per world, server-side only.
        private readonly Dictionary<string, PendingEntry> _placements = new Dictionary<string, PendingEntry>();
        private readonly Dictionary<string, long> _tombstones = new Dictionary<string, long>();
        private bool _placementsDirty;
        private bool _tombstonesDirty;
        private float _lastReconcile;
        private float _reconcileNotBefore = -1f;
        private const float ReconcileIntervalSeconds = 60f;
        private const float ReconcileInitialGraceSeconds = 45f;   // let vanilla reload active-area ZDOs first
        private const int MaxReconcileSpawnsPerPass = 5;          // throttle to avoid a load-time spawn storm
        private const float RecentSpawnCooldownSeconds = 8f;      // suppress re-spawn during a body's ~1s init window
        private readonly Dictionary<string, float> _recentSpawn = new Dictionary<string, float>();
        private const int RequiredMissStreak = 2;                 // revive only after N consecutive confirmed-missing passes
        private readonly Dictionary<string, int> _missStreak = new Dictionary<string, int>();

        private static string StoreFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.StaticRespawns);
        private static string StorePath(long worldUid) => Path.Combine(StoreFolder, worldUid + ".json");

        private static string PlacementsFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.StaticPlacements);
        private static string PlacementsPath(long worldUid) => Path.Combine(PlacementsFolder, worldUid + ".json");
        private static string TombstonesPath(long worldUid) => Path.Combine(PlacementsFolder, "tombstones_" + worldUid + ".json");

        /// <summary>Called from a ZNet.Start server hook so pending respawns are loaded + their timers
        /// resume even after a restart where no fresh death occurred this session.</summary>
        public static void EnsureServerStarted()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            Instance.LoadIfNeeded();
            Instance.MigrateRegistryToAnchors();
        }

        /// <summary>
        /// One-time registry→anchor conversion (idempotent by existence check). Registry entries whose
        /// bodies already vanished can't migrate organically through StaticNpcInitializer, so each
        /// placement record without a nearby anchor gets one minted here, seeded from the PendingEntry
        /// snapshot + the SavedNPCs record (profiles/equipment). The anchor keeper then re-materializes
        /// the body when the zone next loads. Tombstoned cells are skipped (admin deleted them).
        /// </summary>
        private void MigrateRegistryToAnchors()
        {
            try
            {
                if (_placements.Count == 0 || ZNetScene.instance == null || ZDOMan.instance == null) return;
                var anchorPrefab = ZNetScene.instance.GetPrefab(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName);
                if (anchorPrefab == null) return;   // old bundle — anchor system dormant

                // Existing anchors as POSITIONS with tolerant matching — never exact cell strings.
                // The id rounds to integer meters including Y; anchor height healing and feet probes
                // shift Y across rounding boundaries, and an exact-string check here re-minted a
                // duplicate anchor (and thus a duplicate NPC) for every drifted entry on each restart.
                var anchorPositions = new List<Vector3>();
                var buf = new List<ZDO>();
                int idx = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, buf, ref idx)) { }
                foreach (var z in buf)
                    if (z != null && z.IsValid()) anchorPositions.Add(z.GetPosition());

                int migrated = 0;
                foreach (var kv in _placements)
                {
                    var e = kv.Value;
                    if (e == null || string.IsNullOrEmpty(e.PrefabName)) continue;
                    Vector3 home = e.HomePosition;
                    string cell = SavedNpcManager.GenerateNpcId(home);
                    if (_tombstones.ContainsKey(cell)) continue;
                    bool hasAnchor = false;
                    foreach (var ap in anchorPositions)
                        if (SavedNpcManager.SamePlacementCell(ap, home)) { hasAnchor = true; break; }
                    if (hasAnchor) continue;

                    var go = UnityEngine.Object.Instantiate(anchorPrefab, home, e.Rotation);
                    var nview = go.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid()) { UnityEngine.Object.Destroy(go); continue; }
                    var azdo = nview.GetZDO();

                    string guid = Guid.NewGuid().ToString("N");
                    azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, guid);
                    azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyPrefab, e.PrefabName);
                    azdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Rotation, e.Rotation);

                    // Appearance from the registry snapshot (same fields Respawn used to replay).
                    azdo.Set("npc_name_override", e.NameOverride ?? "");
                    azdo.Set("npc_scale", e.Scale);
                    azdo.Set(ZDOVars.s_modelIndex, e.ModelIndex);
                    azdo.Set("npc_isfemale", e.IsFemale);
                    azdo.Set("npc_hair", e.Hair ?? "");
                    azdo.Set("npc_beard", e.Beard ?? "");
                    azdo.Set(ZDOVars.s_hairColor, new Vector3(e.HairR, e.HairG, e.HairB));
                    azdo.Set(ZDOVars.s_skinColor, new Vector3(e.SkinR, e.SkinG, e.SkinB));
                    azdo.Set("npc_eyeColor", new Vector3(e.EyeR, e.EyeG, e.EyeB));
                    azdo.Set("npc_patrol_route", e.PatrolRouteName ?? "");
                    azdo.Set("npc_respawn_delay", e.RespawnDelay);

                    // Profiles/equipment/type from the position-keyed SavedNPCs record, if one exists.
                    var saved = SavedNpcManager.GetNpc(cell);
                    if (saved != null)
                    {
                        azdo.Set("npc_profile", saved.NpcProfileName ?? "");
                        azdo.Set("npc_quest_profile", saved.QuestProfile ?? "");
                        azdo.Set("npc_dialogue_profile", saved.DialogueProfile ?? "");
                        azdo.Set("npc_info_profile", saved.InfoProfile ?? "");
                        azdo.Set("npc_trader_profile", saved.TraderProfile ?? "");
                        azdo.Set("npc_model_override", saved.ModelOverride ?? "");
                        azdo.Set("npc_type", (int)saved.NpcType);
                        azdo.Set("npc_equip_helmet", saved.EquipHelmet ?? "");
                        azdo.Set("npc_equip_chest", saved.EquipChest ?? "");
                        azdo.Set("npc_equip_legs", saved.EquipLegs ?? "");
                        azdo.Set("npc_equip_shoulder", saved.EquipShoulder ?? "");
                        azdo.Set("npc_equip_utility", saved.EquipUtility ?? "");
                        azdo.Set("npc_equip_righthand", saved.EquipRightHand ?? "");
                        azdo.Set("npc_equip_lefthand", saved.EquipLeftHand ?? "");
                        azdo.Set("npc_equip_rightback", saved.EquipRightBack ?? "");
                        azdo.Set("npc_equip_leftback", saved.EquipLeftBack ?? "");
                        azdo.Set("npc_equip_righthand_data", saved.EquipRightHandData ?? "");
                        azdo.Set("npc_equip_lefthand_data", saved.EquipLeftHandData ?? "");
                        if (!string.IsNullOrEmpty(saved.FashionJson)) azdo.Set("npc_fashion_json", saved.FashionJson);
                    }

                    migrated++;
                }

                if (migrated > 0)
                    Debug.Log($"[StaticNpcRespawnManager] migrated {migrated} placement record(s) to NPC anchors");
            }
            catch (Exception ex)
            {
                Debug.LogError("[StaticNpcRespawnManager] registry→anchor migration failed: " + ex.Message);
            }
        }

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            LoadIfNeeded();
            ReconcileTick();                 // never-vanish safety net (self-throttled to its own cadence)
            if (_pending.Count == 0) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            long now = DateTime.UtcNow.Ticks;
            bool changed = false;
            // One scan for the whole tick, built on first need: the dupe guard used to scan the scene per due
            // entry, so a batch of expiring respawns paid for a full FindObjectsOfType each.
            global::FiresCore.Npc.NpcMode.CompanionNpcModule[] liveModules = null;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now < _pending[i].DeadlineUtcTicks) continue;
                var entry = _pending[i];
                liveModules ??= UnityEngine.Object.FindObjectsOfType<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();

                // Single-instance guard (covers crash-after-spawn-before-save): if a static NPC is already
                // standing at this spot, the respawn already happened — just clear the stale entry.
                if (StaticNpcAlreadyAt(liveModules, entry.Position))
                {
                    _pending.RemoveAt(i); changed = true; continue;
                }

                bool spawned;
                try { spawned = Respawn(entry); }
                catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] Respawn threw: {ex.Message}\n{ex.StackTrace}"); spawned = false; }

                if (spawned) { _pending.RemoveAt(i); changed = true; }
                // else: leave it for a retry next tick (e.g. prefab not registered yet) — no silent loss.
            }
            if (changed) Save();
        }

        /// <summary>Adds a captured death snapshot to the persistent store with a fresh 60s wall-clock
        /// deadline. Called by the OnDeath patch (server-side only).</summary>
        public void Schedule(PendingEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PrefabName)) return;
            LoadIfNeeded();
            float delay = entry.RespawnDelay > 0f ? entry.RespawnDelay : RespawnDelaySeconds;
            entry.DeadlineUtcTicks = DateTime.UtcNow.AddSeconds(delay).Ticks;
            _pending.Add(entry);
            Save();
            Debug.Log($"[StaticNpcRespawnManager] Scheduled '{entry.NameOverride}' respawn at {entry.Position} (deadline +{delay:F0}s, persisted)");
        }

        // ── respawn ───────────────────────────────────────────────────────────

        private bool Respawn(PendingEntry e)
        {
            if (ZNetScene.instance == null) return false;

            var prefab = ZNetScene.instance.GetPrefab(e.PrefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[StaticNpcRespawnManager] prefab '{e.PrefabName}' not registered yet — retry next tick");
                return false;
            }

            var go = UnityEngine.Object.Instantiate(prefab, e.Position, e.Rotation);
            var nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                Debug.LogError("[StaticNpcRespawnManager] respawned object has no valid ZNetView");
                return false;
            }
            var zdo = nview.GetZDO();

            // Flag as a placed, already-initialized static NPC so StaticNpcInitializer restores from the
            // ZDO instead of re-running first-time randomization.
            zdo.Set("npc_initialized", true);
            zdo.Set("npc_static_placement", true);
            zdo.Set("npc_stationed", true);
            zdo.Set("npc_stationed_pos", e.Position);
            zdo.Set("npc_stationed_rot", e.Rotation);
            // Carry the STABLE placement anchor onto the respawned body so the reconciler keys it identically
            // even though a patrol NPC's body spawns at its checkpoint (e.Position), not its home. Unlike
            // npc_stationed_pos (which Core re-cements to the live transform on relog), this is never rewritten.
            zdo.Set("npc_placed_anchor", (e.HomeX == 0f && e.HomeY == 0f && e.HomeZ == 0f) ? e.Position : e.HomePosition);

            // Replay the ZDO-only appearance so the respawn looks identical.
            zdo.Set("npc_name_override", e.NameOverride ?? "");
            zdo.Set("npc_scale", e.Scale);
            zdo.Set(ZDOVars.s_modelIndex, e.ModelIndex);
            zdo.Set("npc_isfemale", e.IsFemale);
            zdo.Set("npc_hair", e.Hair ?? "");
            zdo.Set("npc_beard", e.Beard ?? "");
            zdo.Set(ZDOVars.s_hairColor, new Vector3(e.HairR, e.HairG, e.HairB));
            zdo.Set(ZDOVars.s_skinColor, new Vector3(e.SkinR, e.SkinG, e.SkinB));
            zdo.Set("npc_eyeColor", new Vector3(e.EyeR, e.EyeG, e.EyeB));

            // Restore config (profiles/type/equipment/fashion). The saved record is position-keyed by the
            // PLACED HOME, so look it up by home even though the body respawned at the patrol checkpoint.
            // Re-apply the patrol route + per-NPC respawn delay (not carried in the position-keyed file).
            var npc = go.GetComponent<UI.NpcController>();
            if (npc != null)
            {
                Vector3 lookup = (e.HomeX == 0f && e.HomeY == 0f && e.HomeZ == 0f) ? e.Position : e.HomePosition;
                SavedNpcManager.LoadIntoNpc(npc, lookup);
                if (!string.IsNullOrEmpty(e.PatrolRouteName)) npc.patrolRouteName = e.PatrolRouteName;
                if (e.RespawnDelay > 0f) npc.respawnDelay = e.RespawnDelay;
                npc.SaveToZDO();
                // Re-wire the PatrolAssignment component so a respawned NPC RESUMES its route instead of standing
                // idle — SaveToZDO only persists the route name in the ZDO, it does not attach the component.
                if (!string.IsNullOrEmpty(e.PatrolRouteName)) npc.SyncPatrolAssignment();
            }

            Debug.Log($"[StaticNpcRespawnManager] Respawned '{e.NameOverride}' at {e.Position}");
            // Mark this placement cell as just-spawned so the reconciler won't duplicate it during the ~1s
            // window before isStaticPlacement is set (covers both reconciler-at-home and death-at-checkpoint).
            Vector3 anchorPos = (e.HomeX == 0f && e.HomeY == 0f && e.HomeZ == 0f) ? e.Position : e.HomePosition;
            _recentSpawn[SavedNpcManager.GenerateNpcId(anchorPos)] = Time.time;
            return true;
        }

        private static bool StaticNpcAlreadyAt(global::FiresCore.Npc.NpcMode.CompanionNpcModule[] modules, Vector3 pos)
        {
            if (modules == null) return false;
            foreach (var m in modules)
            {
                if (m == null || !m.isStaticPlacement) continue;
                if (Vector3.Distance(m.StationedPosition, pos) <= DupePositionEpsilon) return true;
                if (Vector3.Distance(m.transform.position, pos) <= DupePositionEpsilon) return true;
            }
            return false;
        }

        // ── capture (from the dying NPC) ──────────────────────────────────────

        /// <summary>Builds a persistent entry from a dying static NPC's live ZDO + transform. Called by
        /// the death patch before vanilla destroys the body. Captures the prefab NAME (not the runtime
        /// hash) so it survives a restart.</summary>
        public static PendingEntry CaptureFrom(Character c)
        {
            var nview = c.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return null;
            var zdo = nview.GetZDO();
            if (zdo == null) return null;

            string prefabName = null;
            try { prefabName = ZNetScene.instance?.GetPrefab(zdo.GetPrefab())?.name; } catch { }
            if (string.IsNullOrEmpty(prefabName))
                prefabName = global::Utils.GetPrefabName(c.gameObject);
            if (string.IsNullOrEmpty(prefabName)) return null;

            // The PLACED ANCHOR is the stable identity + respawn home. npc_placed_anchor is written once at
            // placement and never re-cemented (unlike npc_stationed_pos, which Core's SetStationedPositionDirect
            // overwrites with the live transform on every relog → a patrol NPC's anchor would drift). Fall back
            // to npc_stationed_pos for NPCs placed before this field existed.
            Vector3 home = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", c.transform.position));
            Quaternion rot = zdo.GetQuaternion("npc_stationed_rot", c.transform.rotation);
            string route = zdo.GetString("npc_patrol_route", "");
            // A patrolling NPC respawns at its last reached checkpoint; everything else at its placed home.
            Vector3 pos = string.IsNullOrEmpty(route) ? home : zdo.GetVec3("npc_patrol_checkpoint", home);
            Vector3 hair = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one);
            Vector3 skin = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one);
            Vector3 eye = zdo.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f));

            return new PendingEntry
            {
                PrefabName   = prefabName,
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                HomeX = home.x, HomeY = home.y, HomeZ = home.z,
                PatrolRouteName = route,
                RespawnDelay = zdo.GetFloat("npc_respawn_delay", RespawnDelaySeconds),
                RotX = rot.x, RotY = rot.y, RotZ = rot.z, RotW = rot.w,
                NameOverride = zdo.GetString("npc_name_override", ""),
                Scale        = zdo.GetFloat("npc_scale", 1f),
                ModelIndex   = zdo.GetInt(ZDOVars.s_modelIndex, 0),
                IsFemale     = zdo.GetBool("npc_isfemale", false),
                Hair         = zdo.GetString("npc_hair", ""),
                Beard        = zdo.GetString("npc_beard", ""),
                HairR = hair.x, HairG = hair.y, HairB = hair.z,
                SkinR = skin.x, SkinG = skin.y, SkinB = skin.z,
                EyeR = eye.x,   EyeG = eye.y,   EyeB = eye.z,
            };
        }

        // ── desired-state registry + reconciler ───────────────────────────────

        /// <summary>Record / refresh a placed static NPC in the desired-state registry, keyed by its anchor
        /// (placement) cell. Re-placing in a cell clears any prior delete tombstone there. Server-side only.</summary>
        public void RegisterPlacement(PendingEntry snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.PrefabName)) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            LoadIfNeeded();
            string key = SavedNpcManager.GenerateNpcId(snapshot.HomePosition);
            // Tombstone-RESPECTING: the periodic live-refresh must never re-add (and never un-delete) a cell an
            // admin deleted — only an explicit first-placement (ClearTombstone) revives one. This closes the
            // delete race where a refresh lands between the tombstone write and the body's actual destroy.
            if (_tombstones.ContainsKey(key)) return;
            // Only dirty the store when the snapshot actually changed, so a steady-state world doesn't rewrite
            // the placements file every pass (the refresh calls this for every live NPC each tick).
            if (_placements.TryGetValue(key, out var existing) && SnapshotsEqual(existing, snapshot))
            {
                _placements[key] = snapshot;
                return;
            }
            _placements[key] = snapshot;
            _placementsDirty = true;
        }

        /// <summary>Clears a delete tombstone for a cell. Called ONLY from the genuine first-placement seed
        /// path, so an admin re-placing a NEW NPC where one was deleted un-deletes that cell — while the
        /// periodic refresh (RegisterPlacement) stays tombstone-respecting.</summary>
        public void ClearTombstone(Vector3 anchor)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            LoadIfNeeded();
            if (_tombstones.Remove(SavedNpcManager.GenerateNpcId(anchor))) { _tombstonesDirty = true; SaveTombstones(); }
        }

        /// <summary>Tombstones are now a short RACE-GUARD only (marks "deleted here recently" so an
        /// in-flight death-respawn can't land after a delete) — the anchor system owns respawn truth.
        /// TTL keeps them from stacking up forever in the per-world file.</summary>
        private const double TombstoneTtlMinutes = 10.0;

        private void PruneExpiredTombstones()
        {
            if (_tombstones.Count == 0) return;
            long cutoff = DateTime.UtcNow.AddMinutes(-TombstoneTtlMinutes).Ticks;
            List<string> expired = null;
            foreach (var kv in _tombstones)
            {
                if (kv.Value < cutoff) (expired = expired ?? new List<string>()).Add(kv.Key);
            }
            if (expired == null) return;
            foreach (var k in expired) _tombstones.Remove(k);
            _tombstonesDirty = true;
        }

        /// <summary>An admin DELIBERATELY removed the NPC at this anchor: drop its registry entry and write a
        /// tombstone so the reconciler NEVER re-materializes it, and cancel any pending death-respawn for it.</summary>
        public void RemovePlacement(Vector3 anchor)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            LoadIfNeeded();
            // Tolerant removal: sweep every entry whose HOME is this placement cell (position match,
            // not exact id string — Y drift from feet probes/height healing breaks exact keys), and
            // tombstone each entry's ACTUAL key so the reconciler skip agrees with what was removed.
            string key = SavedNpcManager.GenerateNpcId(anchor);
            var stale = new List<string>();
            foreach (var kv in _placements)
            {
                if (kv.Value == null) continue;
                if (kv.Key == key || SavedNpcManager.SamePlacementCell(kv.Value.HomePosition, anchor))
                    stale.Add(kv.Key);
            }
            foreach (var k in stale)
            {
                _placements.Remove(k);
                _tombstones[k] = DateTime.UtcNow.Ticks;
            }
            if (stale.Count > 0) _placementsDirty = true;
            _tombstones[key] = DateTime.UtcNow.Ticks;
            _tombstonesDirty = true;
            CancelForAnchor(anchor);
            if (_placementsDirty) SavePlacements();
            if (_tombstonesDirty) SaveTombstones();
            Debug.Log($"[StaticNpcRespawnManager] Tombstoned + removed {stale.Count} placement entr(ies) at {anchor} (admin delete/move)");
        }

        public bool IsTombstoned(Vector3 anchor)
        {
            LoadIfNeeded();
            return _tombstones.ContainsKey(SavedNpcManager.GenerateNpcId(anchor));
        }

        /// <summary>Remove any pending death-respawn whose placed home matches this anchor (admin delete).
        /// Keyed by placement CELL (GenerateNpcId) so it agrees with the registry/tombstone identity rather
        /// than a separate distance epsilon that could disagree at a cell boundary.</summary>
        public void CancelForAnchor(Vector3 anchor)
        {
            LoadIfNeeded();
            string key = SavedNpcManager.GenerateNpcId(anchor);
            int removed = _pending.RemoveAll(e => SavedNpcManager.GenerateNpcId(e.HomePosition) == key);
            if (removed > 0) Save();
        }

        public bool HasPendingForAnchor(Vector3 anchor)
        {
            string key = SavedNpcManager.GenerateNpcId(anchor);
            return _pending.Exists(e => SavedNpcManager.GenerateNpcId(e.HomePosition) == key);
        }

        private static bool IsAnchorZoneLoaded(Vector3 pos)
        {
            var zs = ZoneSystem.instance;
            return zs != null && zs.IsZoneLoaded(ZoneSystem.GetZone(pos));
        }

        private static bool SnapshotsEqual(PendingEntry a, PendingEntry b)
        {
            if (a == null || b == null) return false;
            try { return JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b); }
            catch { return false; }
        }

        /// <summary>The master switch. When on (default), static NPCs can't die and are never auto-revived, so
        /// admin deletes stick. Defaults to true if the config isn't available yet.</summary>
        private static bool StaticNpcsInvulnerable()
        {
            try { return global::FiresRPGmaker.Managers.ConfigManager.Instance?.configStaticNpcInvulnerable?.Value ?? true; }
            catch { return true; }
        }

        /// <summary>The never-vanish safety net (server-side, self-throttled). Each pass: (1) refresh the
        /// registry from every LIVE static NPC the server can see — keeping each entry's anchor / route /
        /// appearance current on ANY topology (incl. a dedicated server where edits originate on admin
        /// clients); (2) re-materialize any registry entry with no live instance, UNLESS it is tombstoned
        /// (admin-deleted), its zone isn't loaded (vanilla owns that reload), or it is inside its
        /// death-respawn window. Identity is the stable placement cell, so a patrolling body is matched
        /// wherever it currently stands.</summary>
        private void ReconcileTick()
        {
            if (_reconcileNotBefore < 0f) _reconcileNotBefore = Time.time + ReconcileInitialGraceSeconds;
            if (Time.time < _reconcileNotBefore) return;
            if (Time.time - _lastReconcile < ReconcileIntervalSeconds) return;
            _lastReconcile = Time.time;

            // One full-scene scan per pass, shared by the live-refresh and the presence check (perf).
            var modules = UnityEngine.Object.FindObjectsOfType<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();
            RefreshPlacementsFromLive(modules);
            PruneExpiredTombstones();   // TTL race-guards, never a stack of permanent graves

            // Master switch: when static NPCs are invulnerable they are permanent fixtures managed by hand
            // (place / hammer-delete). They can't die, so a "missing" entry means an admin deliberately removed
            // it — by ANY tool, including ones that bypass our hammer gate and so leave no tombstone. Never
            // auto-revive in this mode, or deletes would resurrect. We still ran RefreshPlacementsFromLive above
            // to keep route/appearance current. Flip StaticNpcInvulnerable off to restore the never-vanish net.
            if (StaticNpcsInvulnerable())
            {
                if (_placementsDirty) SavePlacements();
                if (_tombstonesDirty) SaveTombstones();
                return;
            }

            // Anchor-owned cells are fully retired from this reconciler — the NpcAnchorKeeper is their
            // single respawn authority (reviving here would double-spawn during the keeper's respawn
            // delay window). Tolerant POSITION matching, never exact cell strings (Y drift from height
            // healing / feet probes broke the exact match — proven duplicate source after anchor moves).
            var anchoredPositions = new List<Vector3>();
            try
            {
                if (ZDOMan.instance != null)
                {
                    var abuf = new List<ZDO>();
                    int aidx = 0;
                    while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, abuf, ref aidx)) { }
                    foreach (var az in abuf)
                        if (az != null && az.IsValid()) anchoredPositions.Add(az.GetPosition());
                }
            }
            catch { }

            int spawned = 0;
            var entries = new List<PendingEntry>(_placements.Values);
            foreach (var snap in entries)
            {
                if (spawned >= MaxReconcileSpawnsPerPass) break;
                Vector3 home = snap.HomePosition;
                string key = SavedNpcManager.GenerateNpcId(home);
                bool anchorOwned = false;
                foreach (var ap in anchoredPositions)
                    if (SavedNpcManager.SamePlacementCell(ap, home)) { anchorOwned = true; break; }
                if (anchorOwned) { _missStreak.Remove(key); continue; }                        // anchor-owned — keeper is the authority
                if (_tombstones.ContainsKey(key)) { _missStreak.Remove(key); continue; }      // admin-deleted — never resurrect
                if (!IsAnchorZoneLoaded(home)) continue;                                       // zone not loaded — defer (keep streak)
                if (StaticNpcPresentForKey(key, home, modules)) { _missStreak.Remove(key); continue; }  // live / just-spawned / ZDO present
                if (HasPendingForAnchor(home)) { _missStreak.Remove(key); continue; }         // mid death-respawn — don't double-spawn

                // Confirmed missing. Require it to be missing for TWO consecutive passes before reviving, so a
                // just-deleted NPC whose tombstone RPC is still in flight on a dedicated server is never
                // resurrected — by the next pass the tombstone has landed and we skip it above.
                int streak = (_missStreak.TryGetValue(key, out var v) ? v : 0) + 1;
                if (streak < RequiredMissStreak) { _missStreak[key] = streak; continue; }
                _missStreak.Remove(key);

                bool ok;
                try { ok = Respawn(snap); }
                catch (Exception ex) { Debug.LogError($"[StaticNpcReconciler] respawn threw: {ex.Message}"); ok = false; }
                if (ok)
                {
                    spawned++;
                    Debug.LogWarning($"[StaticNpcReconciler] re-materialized vanished static NPC '{snap.NameOverride}' at {home}");
                }
            }

            if (_placementsDirty) SavePlacements();
            if (_tombstonesDirty) SaveTombstones();
        }

        /// <summary>True if a static NPC for this placement cell already exists — checked three ways so a body
        /// is never duplicated: (a) we spawned one for this cell within the last few seconds (covers the
        /// &lt;1s window before the module flag is set); (b) a LIVE static NPC whose placement cell matches is
        /// loaded anywhere — position-independent, so a patrolling body counts; (c) a persistent ZDO with the
        /// matching placement cell exists in the home sector but isn't instantiated into a GameObject yet.</summary>
        private bool StaticNpcPresentForKey(string key, Vector3 home, global::FiresCore.Npc.NpcMode.CompanionNpcModule[] modules)
        {
            if (_recentSpawn.TryGetValue(key, out var t) && Time.time - t < RecentSpawnCooldownSeconds) return true;

            if (modules != null)
            {
                foreach (var m in modules)
                {
                    if (m == null) continue;
                    var nv = m.GetComponent<ZNetView>();
                    if (nv == null || !nv.IsValid()) continue;
                    var z = nv.GetZDO();
                    if (z == null) continue;
                    if (!m.isStaticPlacement && !z.GetBool("npc_static_placement", false)) continue;
                    Vector3 a = z.GetVec3("npc_placed_anchor", z.GetVec3("npc_stationed_pos", m.transform.position));
                    if (SavedNpcManager.GenerateNpcId(a) == key) return true;
                }
            }

            return StaticNpcZdoAnchorExistsAt(home, key);
        }

        /// <summary>Scans the home sector's ZDOs for a static-NPC ZDO whose placement cell matches — catches a
        /// persistent ZDO vanilla has loaded but not yet instantiated into a GameObject.</summary>
        private static bool StaticNpcZdoAnchorExistsAt(Vector3 home, string key)
        {
            var zm = ZDOMan.instance;
            if (zm == null) return false;
            var near = new List<ZDO>();
            var distant = new List<ZDO>();
            try { zm.FindSectorObjects(ZoneSystem.GetZone(home), new SimulationDistance(1, 0), near, distant); }
            catch { return false; }
            foreach (var z in near)
            {
                if (z == null) continue;
                if (!z.GetBool("npc_static_placement", false)) continue;
                Vector3 a = z.GetVec3("npc_placed_anchor", z.GetVec3("npc_stationed_pos", z.GetPosition()));
                if (SavedNpcManager.GenerateNpcId(a) == key) return true;
            }
            return false;
        }

        /// <summary>Capture every live static NPC into the registry so the desired-state snapshot tracks the
        /// latest anchor / route / appearance. Cheap (ZDO reads); runs at most once per reconcile interval.</summary>
        private void RefreshPlacementsFromLive(global::FiresCore.Npc.NpcMode.CompanionNpcModule[] modules)
        {
            if (modules == null) return;
            foreach (var m in modules)
            {
                if (m == null || !m.isStaticPlacement) continue;
                // Anchor-owned bodies NEVER enter this registry — the anchor keeper is their sole
                // respawn authority. Registering them here left stale home entries behind every
                // anchor move, and the reconciler then revived an unnamed NPC at the old spot
                // (which minted itself a fresh anchor = permanent duplicate). Legacy pre-anchor
                // bodies carry no back-ref and keep the full never-vanish net.
                var nvRef = m.GetComponent<ZNetView>();
                var zRef = nvRef != null && nvRef.IsValid() ? nvRef.GetZDO() : null;
                if (zRef != null && !string.IsNullOrEmpty(zRef.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "")))
                    continue;
                var ch = m.GetComponent<Character>();
                if (ch == null) continue;
                var snap = CaptureFrom(ch);
                if (snap != null) RegisterPlacement(snap);
            }
        }

        // ── persistence (world-UID-keyed JSON, crash-safe) ────────────────────

        private void LoadIfNeeded()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;
            if (uid == 0L) return;                       // world not ready yet
            if (_loaded && _loadedWorldUid == uid) return;

            _pending.Clear();
            try
            {
                string path = StorePath(uid);
                if (File.Exists(path))
                {
                    var list = JsonConvert.DeserializeObject<List<PendingEntry>>(File.ReadAllText(path));
                    if (list != null) _pending.AddRange(list);
                    Debug.Log($"[StaticNpcRespawnManager] Loaded {_pending.Count} pending respawn(s) for world {uid}");
                }
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] load failed: {ex.Message}"); }

            _placements.Clear();
            try
            {
                string pp = PlacementsPath(uid);
                if (File.Exists(pp))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, PendingEntry>>(File.ReadAllText(pp));
                    if (dict != null) foreach (var kv in dict) _placements[kv.Key] = kv.Value;
                    Debug.Log($"[StaticNpcRespawnManager] Loaded {_placements.Count} placement record(s) for world {uid}");
                }
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] placements load failed: {ex.Message}"); }

            _tombstones.Clear();
            try
            {
                string tp = TombstonesPath(uid);
                if (File.Exists(tp))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText(tp));
                    if (dict != null) foreach (var kv in dict) _tombstones[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] tombstones load failed: {ex.Message}"); }

            _loaded = true;
            _loadedWorldUid = uid;
        }

        private void Save()
        {
            if (_loadedWorldUid == 0L) return;
            try
            {
                if (!Directory.Exists(StoreFolder)) Directory.CreateDirectory(StoreFolder);
                File.WriteAllText(StorePath(_loadedWorldUid), JsonConvert.SerializeObject(_pending, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] save failed: {ex.Message}"); }
        }

        private void SavePlacements()
        {
            if (_loadedWorldUid == 0L) return;
            try
            {
                if (!Directory.Exists(PlacementsFolder)) Directory.CreateDirectory(PlacementsFolder);
                File.WriteAllText(PlacementsPath(_loadedWorldUid), JsonConvert.SerializeObject(_placements, Formatting.Indented));
                _placementsDirty = false;
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] placements save failed: {ex.Message}"); }
        }

        private void SaveTombstones()
        {
            if (_loadedWorldUid == 0L) return;
            try
            {
                if (!Directory.Exists(PlacementsFolder)) Directory.CreateDirectory(PlacementsFolder);
                File.WriteAllText(TombstonesPath(_loadedWorldUid), JsonConvert.SerializeObject(_tombstones, Formatting.Indented));
                _tombstonesDirty = false;
            }
            catch (Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] tombstones save failed: {ex.Message}"); }
        }
    }
}
