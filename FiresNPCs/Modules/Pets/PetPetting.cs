using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// Fire 10-08: "we should be able to pet our pets, and see lil hearts when we do." [E] Pet on every pocket pet, the vanilla
    /// tame path's look: "$hud_tamelove" + the creature's own Tameable.m_petEffect (Boar's hearts when it has none), a small
    /// hop, a short cooldown, no menu. The pet itself has no colliders (stripped so it never blocks anyone), so a TRIGGER on a
    /// child on piece_nonsolid (in vanilla's interact mask) is what the crosshair hovers; a trigger blocks nothing.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class PetPetting : MonoBehaviour, Hoverable, Interactable
    {
        private float _applied;         // the hop offset added last LateUpdate
        private bool _fitted;           // the trigger has its size; from then on Touchable() switches it every frame
        private const float Cooldown = 1.5f;
        private const float HopTime = 0.3f;

        private PetVisual _pet;
        private EffectList _hearts;
        private string _name;
        private CapsuleCollider _trigger;
        private float _sizeAt;          // when to fit the trigger to the pet's rendered bounds (its animated pose settles)
        private float _nextPet;
        private float _hopStart = -1f;
        private float _hopHeight;

        internal static void Attach(PetVisual pet, GameObject prefab, Character character)
        {
            var go = new GameObject("FiresPetTouch");
            go.layer = TouchLayer();
            go.transform.SetParent(pet.transform, false);
            var touch = go.AddComponent<PetPetting>();
            touch._pet = pet;
            touch._trigger = go.AddComponent<CapsuleCollider>();
            touch._trigger.isTrigger = true;
            touch._trigger.enabled = false;
            touch._sizeAt = Time.time + 1f;
            touch._name = character != null && Localization.instance != null ? Localization.instance.Localize(character.m_name) : pet.Creature;
            touch._hearts = Hearts(prefab);
        }

        private static int s_touchLayer = -1;

        /// <summary>
        /// Fire 10-09 "the camera gets glitchy": the touch trigger sat on piece_nonsolid, which the game camera's
        /// collision sphere-cast also hits (a trigger counts for that query), so a raven hovering between the camera and
        /// the player pulled the camera in. The layer is now one the crosshair's interact ray hits but the camera's block
        /// mask does not ("item" excluded: auto-pickup scans it). Chosen once, logged.
        /// </summary>
        private static int TouchLayer()
        {
            if (s_touchLayer >= 0) return s_touchLayer;
            int interact = LayerMask.GetMask("item", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "character_net", "terrain", "vehicle", "character_ghost");
            int block = GameCamera.instance != null ? GameCamera.instance.m_blockCameraMask.value : 0;
            foreach (string name in new[] { "character_ghost", "character_net", "character", "vehicle", "piece_nonsolid" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer < 0 || (interact & (1 << layer)) == 0 || (block & (1 << layer)) != 0) continue;
                s_touchLayer = layer;
                break;
            }
            if (s_touchLayer < 0) s_touchLayer = LayerMask.NameToLayer("character_ghost");
            if (GameCamera.instance != null)   // only cache once the camera's mask was known
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] pet touch layer: '{LayerMask.LayerToName(s_touchLayer)}' (not in the camera's block mask)");
            else { int l = s_touchLayer; s_touchLayer = -1; return l; }
            return s_touchLayer;
        }

        /// <summary>The creature's own pet effect, else the Boar's (the vanilla hearts).</summary>
        private static EffectList Hearts(GameObject prefab)
        {
            EffectList own = prefab != null ? prefab.GetComponent<Tameable>()?.m_petEffect : null;
            if (own != null && own.m_effectPrefabs != null && own.m_effectPrefabs.Length > 0) return own;
            GameObject boar = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Boar") : null;
            return boar != null ? boar.GetComponent<Tameable>()?.m_petEffect : null;
        }

        private void Update()
        {
            if (_sizeAt > 0f && Time.time >= _sizeAt) { _sizeAt = 0f; FitTrigger(); }
            if (_fitted) _trigger.enabled = Touchable();
            // Runs before PetVisual's Update (execution order): take last frame's hop offset back out, so PetVisual moves
            // the pet from its true spot and the offset never accumulates.
            if (_applied != 0f) { _pet.transform.position -= Vector3.up * _applied; _applied = 0f; }
        }

        private void LateUpdate()
        {
            if (_hopStart < 0f) return;
            float t = (Time.time - _hopStart) / HopTime;
            if (t >= 1f) { _hopStart = -1f; return; }
            _applied = Mathf.Sin(t * Mathf.PI) * _hopHeight;
            _pet.transform.position += Vector3.up * _applied;
        }

        /// <summary>
        /// Round 49: a pet perched on the shoulder sat in the interact ray and took E / R from the companion behind it. The touch
        /// only exists while the pet is not perched, is well clear of the camera and the player, and is the thing aimed at: no
        /// Character or other Interactable closer along the view ray than the pet (vanilla's hover takes the nearest hit).
        /// </summary>
        private bool Touchable()
        {
            if (_pet.Perched) return false;
            Player me = Player.m_localPlayer;
            Camera cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            if (me == null || cam == null) return false;
            Vector3 at = transform.position;
            if (Vector3.Distance(cam.transform.position, at) < 1.2f) return false;
            if (Vector3.Distance(me.transform.position, at) < 0.6f) return false;
            // Something else interactable in front of the camera, nearer than the pet or within a metre past it: leave the ray to it.
            float petDist = Vector3.Distance(cam.transform.position, at);
            foreach (RaycastHit hit in Physics.RaycastAll(cam.transform.position, cam.transform.forward, petDist + 1f, me.m_interactMask))
            {
                if (hit.collider == null || hit.collider == _trigger || hit.collider.transform.IsChildOf(_pet.transform)) continue;
                if (hit.collider.transform.IsChildOf(me.transform)) continue;
                if (hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Interactable>() != null) return false;
            }
            return true;
        }

        /// <summary>The trigger wraps the pet's rendered bounds (world size back to the pet's local scale).</summary>
        private void FitTrigger()
        {
            var renderers = _pet.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) if (r.enabled) b.Encapsulate(r.bounds);
            Vector3 s = _pet.transform.lossyScale;
            float k = Mathf.Max(0.0001f, s.y);
            transform.position = b.center;
            _trigger.direction = 1;
            _trigger.height = Mathf.Max(0.3f, b.size.y) / k;
            // Small: the body's core, not its full bounds (round 49: the touch stole E/R from a companion behind it).
            _trigger.height = Mathf.Max(0.2f, b.size.y * 0.6f) / k;
            _trigger.radius = Mathf.Max(0.1f, Mathf.Max(b.size.x, b.size.z) * 0.3f) / k;
            _fitted = true;
            _hopHeight = Mathf.Clamp(b.size.y * 0.15f, 0.04f, 0.2f);
        }

        public string GetHoverText() => Localization.instance.Localize(_name + "\n[<color=yellow><b>$KEY_Use</b></color>] Pet");

        public string GetHoverName() => _name;

        // Valheim 1.0's Hoverable addition (the hover text's vertical offset); none needed.
        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Time.time < _nextPet) return false;
            _nextPet = Time.time + Cooldown;
            Vector3 top = transform.position + Vector3.up * (_trigger.height * _pet.transform.lossyScale.y * 0.5f);
            var names = new System.Collections.Generic.List<string>();
            int spawned = SpawnLocal(_hearts, top, names);
            string source = "own";
            if (spawned == 0)
            {
                // 0.0.539: the raven's own list gave 0 hearts; the Boar's vanilla hearts stand in for any pet whose list gives none.
                spawned = SpawnLocal(BoarHearts(), top, names);
                source = "Boar";
            }
            _hopStart = Time.time;
            user?.Message(MessageHud.MessageType.Center, _name + " $hud_tamelove");
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {_pet.name} petted: {spawned} heart fx ({source}: {string.Join(", ", names)}), hop {_hopHeight:0.00} m");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private static EffectList BoarHearts()
        {
            GameObject boar = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Boar") : null;
            return boar != null ? boar.GetComponent<Tameable>()?.m_petEffect : null;
        }

        /// <summary>
        /// The effect prefabs, local only. One carrying a ZNetView is spawned with network init disabled and the ZNetView
        /// removed, so it stays a local visual instead of a networked object (0.0.539 skipped those and showed nothing).
        /// </summary>
        private static int SpawnLocal(EffectList list, Vector3 at, System.Collections.Generic.List<string> names)
        {
            if (list?.m_effectPrefabs == null) { names.Add("no list"); return 0; }
            int n = 0;
            foreach (EffectList.EffectData fx in list.m_effectPrefabs)
            {
                if (fx == null || fx.m_prefab == null) continue;
                if (!fx.m_enabled) { names.Add(fx.m_prefab.name + " (disabled)"); continue; }
                bool networked = fx.m_prefab.GetComponent<ZNetView>() != null;
                GameObject go;
                bool wasForced = ZNetView.m_forceDisableInit;
                try
                {
                    ZNetView.m_forceDisableInit = true;
                    go = Instantiate(fx.m_prefab, at, Quaternion.identity);
                }
                finally { ZNetView.m_forceDisableInit = wasForced; }
                if (networked)
                    foreach (ZNetView nv in go.GetComponentsInChildren<ZNetView>(true))
                        try { DestroyImmediate(nv); } catch { }   // a component that requires it keeps it: still never registered
                Destroy(go, 6f);   // a local copy never relies on the network to go away
                names.Add(fx.m_prefab.name + (networked ? " (local copy)" : ""));
                n++;
            }
            return n;
        }
    }
}
