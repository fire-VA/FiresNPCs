using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Companions.NpcMode
{
    /// <summary>
    /// Server-side respawn timer for hammer-placed static NPCs. When a static NPC is killed (by any
    /// means — combat, the K/killall debug commands, etc.) the death patch captures a snapshot and
    /// hands it here; after <see cref="RespawnDelaySeconds"/> the NPC is re-instantiated at its placed
    /// position. Config (profiles/type/equipment) restores automatically by position via
    /// <see cref="SavedNpcManager"/>; only the randomized appearance lives ZDO-only, so the snapshot
    /// carries those keys forward and flags the new instance as already-initialized so it restores
    /// rather than re-randomizing.
    ///
    /// Only the server schedules + respawns (the death patch gates on IsServer); clients see the death
    /// and the fresh spawn through normal ZDO sync.
    /// </summary>
    public class StaticNpcRespawnManager : MonoBehaviour
    {
        public const float RespawnDelaySeconds = 60f;

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

        /// <summary>Everything needed to recreate a static NPC identically. Config comes back via
        /// SavedNpcManager (position-keyed); the appearance fields below are ZDO-only and must be
        /// replayed onto the new instance's ZDO.</summary>
        public class Snapshot
        {
            public int PrefabHash;
            public Vector3 Position;
            public Quaternion Rotation;
            public float RespawnAt;

            // Appearance (ZDO-only — not stored in SavedNpcData).
            public string NameOverride;
            public float Scale;
            public int ModelIndex;
            public bool IsFemale;
            public string Hair;
            public string Beard;
            public Vector3 HairColor;
            public Vector3 SkinColor;
            public Vector3 EyeColor;
        }

        private readonly List<Snapshot> _pending = new List<Snapshot>();
        private float _lastCheck;

        public void Schedule(Snapshot snap)
        {
            if (snap == null) return;
            snap.RespawnAt = Time.time + RespawnDelaySeconds;
            _pending.Add(snap);
            Debug.Log($"[StaticNpcRespawnManager] Scheduled static NPC '{snap.NameOverride}' respawn in {RespawnDelaySeconds}s at {snap.Position}");
        }

        private void Update()
        {
            if (_pending.Count == 0) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].RespawnAt) continue;
                var snap = _pending[i];
                _pending.RemoveAt(i);
                try { Respawn(snap); }
                catch (System.Exception ex) { Debug.LogError($"[StaticNpcRespawnManager] Respawn failed: {ex.Message}\n{ex.StackTrace}"); }
            }
        }

        private void Respawn(Snapshot snap)
        {
            if (ZNetScene.instance == null) { _pending.Add(snap); return; } // scene gone (loading) — retry next tick

            var prefab = ZNetScene.instance.GetPrefab(snap.PrefabHash);
            if (prefab == null) { Debug.LogError("[StaticNpcRespawnManager] prefab hash not found, can't respawn"); return; }

            var go = Object.Instantiate(prefab, snap.Position, snap.Rotation);
            var nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                Debug.LogError("[StaticNpcRespawnManager] respawned object has no valid ZNetView");
                return;
            }
            var zdo = nview.GetZDO();

            // Flag as a placed, already-initialized static NPC so StaticNpcInitializer restores from the
            // ZDO instead of re-running first-time randomization.
            zdo.Set("npc_initialized", true);
            zdo.Set("npc_static_placement", true);
            zdo.Set("npc_stationed", true);
            zdo.Set("npc_stationed_pos", snap.Position);
            zdo.Set("npc_stationed_rot", snap.Rotation);

            // Replay the ZDO-only appearance so the respawn looks identical.
            zdo.Set("npc_name_override", snap.NameOverride ?? "");
            zdo.Set("npc_scale", snap.Scale);
            zdo.Set(ZDOVars.s_modelIndex, snap.ModelIndex);
            zdo.Set("npc_isfemale", snap.IsFemale);
            zdo.Set("npc_hair", snap.Hair ?? "");
            zdo.Set("npc_beard", snap.Beard ?? "");
            zdo.Set(ZDOVars.s_hairColor, snap.HairColor);
            zdo.Set(ZDOVars.s_skinColor, snap.SkinColor);
            zdo.Set("npc_eyeColor", snap.EyeColor);

            // Restore config (profiles/type/equipment/fashion) by position, then push it to the ZDO so
            // the initializer's restore path reads it back.
            var npc = go.GetComponent<UI.NpcController>();
            if (npc != null)
            {
                SavedNpcManager.LoadIntoNpc(npc);
                npc.SaveToZDO();
            }

            Debug.Log($"[StaticNpcRespawnManager] Respawned static NPC '{snap.NameOverride}' at {snap.Position}");
        }

        /// <summary>Builds a snapshot from a dying static NPC's live ZDO + transform. Called by the
        /// death patch before vanilla destroys the body.</summary>
        public static Snapshot CaptureFrom(Character c)
        {
            var nview = c.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return null;
            var zdo = nview.GetZDO();
            if (zdo == null) return null;

            // Placed position/rotation — prefer the stationed home, fall back to current transform.
            Vector3 pos = zdo.GetVec3("npc_stationed_pos", c.transform.position);
            Quaternion rot = zdo.GetQuaternion("npc_stationed_rot", c.transform.rotation);

            return new Snapshot
            {
                PrefabHash   = zdo.GetPrefab(),
                Position     = pos,
                Rotation     = rot,
                NameOverride = zdo.GetString("npc_name_override", ""),
                Scale        = zdo.GetFloat("npc_scale", 1f),
                ModelIndex   = zdo.GetInt(ZDOVars.s_modelIndex, 0),
                IsFemale     = zdo.GetBool("npc_isfemale", false),
                Hair         = zdo.GetString("npc_hair", ""),
                Beard        = zdo.GetString("npc_beard", ""),
                HairColor    = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one),
                SkinColor    = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one),
                EyeColor     = zdo.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f)),
            };
        }
    }
}
