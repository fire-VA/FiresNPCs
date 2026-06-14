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

            [JsonIgnore] public Vector3 Position => new Vector3(PosX, PosY, PosZ);
            [JsonIgnore] public Quaternion Rotation => new Quaternion(RotX, RotY, RotZ, RotW);
        }

        private readonly List<PendingEntry> _pending = new List<PendingEntry>();
        private bool _loaded;
        private long _loadedWorldUid;
        private float _lastCheck;

        private static string StoreFolder => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "StaticRespawns");
        private static string StorePath(long worldUid) => Path.Combine(StoreFolder, worldUid + ".json");

        /// <summary>Called from a ZNet.Start server hook so pending respawns are loaded + their timers
        /// resume even after a restart where no fresh death occurred this session.</summary>
        public static void EnsureServerStarted()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            Instance.LoadIfNeeded();
        }

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            LoadIfNeeded();
            if (_pending.Count == 0) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            long now = DateTime.UtcNow.Ticks;
            bool changed = false;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now < _pending[i].DeadlineUtcTicks) continue;
                var entry = _pending[i];

                // Single-instance guard (covers crash-after-spawn-before-save): if a static NPC is already
                // standing at this spot, the respawn already happened — just clear the stale entry.
                if (StaticNpcAlreadyAt(entry.Position))
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
            entry.DeadlineUtcTicks = DateTime.UtcNow.AddSeconds(RespawnDelaySeconds).Ticks;
            _pending.Add(entry);
            Save();
            Debug.Log($"[StaticNpcRespawnManager] Scheduled '{entry.NameOverride}' respawn at {entry.Position} (deadline +{RespawnDelaySeconds:F0}s, persisted)");
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

            // Restore config (profiles/type/equipment/fashion) by position, then push it to the ZDO so the
            // initializer's restore path reads it back.
            var npc = go.GetComponent<UI.NpcController>();
            if (npc != null)
            {
                SavedNpcManager.LoadIntoNpc(npc);
                npc.SaveToZDO();
            }

            Debug.Log($"[StaticNpcRespawnManager] Respawned '{e.NameOverride}' at {e.Position}");
            return true;
        }

        private static bool StaticNpcAlreadyAt(Vector3 pos)
        {
            var modules = UnityEngine.Object.FindObjectsOfType<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();
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

            Vector3 pos = zdo.GetVec3("npc_stationed_pos", c.transform.position);
            Quaternion rot = zdo.GetQuaternion("npc_stationed_rot", c.transform.rotation);
            Vector3 hair = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one);
            Vector3 skin = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one);
            Vector3 eye = zdo.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f));

            return new PendingEntry
            {
                PrefabName   = prefabName,
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
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
    }
}
