using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets
{
    /// <summary>
    /// One pet on screen: a local, visual-only copy of the creature (no ZDO, no AI, no colliders, no damage), shrunk to
    /// pocket size. Each client builds its own from the owner's player ZDO or from the stand's item (FIRES_PETS_PLAN §9b).
    /// Follows a player (leash 1-2.5 m, teleport past 15 m) or sits by a stand (1.5 m, idle emotes only, never teleports).
    /// </summary>
    internal sealed class PetVisual : MonoBehaviour
    {
        internal string Creature;
        internal Transform Anchor;      // the player or the item stand
        internal bool Leashed;          // true on a stand
        internal int LastEmoteCounter = -1;

        private Animator _animator;
        private float _nextIdleEmote;
        private bool _flier, _swimmer;

        internal static PetVisual Build(string creature, Transform anchor, bool leashed)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
            if (prefab == null) return null;
            var character = prefab.GetComponent<Character>();

            // Instantiated under an INACTIVE parent, so no Awake runs at all: no Character registers itself, no AI, no
            // ZNetView. 0.0.511-513 instantiated it active, and a Humanoid that CharacterDrop pinned survived the strip with
            // its Rigidbody and colliders (the player got stuck on full-size pets; Fire, 10-07).
            if (_staging == null)
            {
                _staging = new GameObject("FiresPets_Staging");
                _staging.SetActive(false);
                DontDestroyOnLoad(_staging);
            }
            GameObject go;
            bool wasForced = ZNetView.m_forceDisableInit;
            try
            {
                ZNetView.m_forceDisableInit = true;
                go = Instantiate(prefab, _staging.transform, false);
            }
            finally { ZNetView.m_forceDisableInit = wasForced; }
            go.name = "FiresPet_" + creature;
            int removed = Strip(go, out int left, out int colliders);
            go.transform.SetParent(null, false);
            go.transform.SetPositionAndRotation(anchor.position + anchor.right * 1.2f, anchor.rotation);
            int scenery = HideScenery(go);

            var pet = go.AddComponent<PetVisual>();
            pet._stripReport = $"{removed} components stripped, {left} could not be ({colliders} colliders left)"
                + (scenery > 0 ? $", {scenery} scenery renderers hidden (sized by its body)" : "");
            pet.Creature = creature;
            pet.Anchor = anchor;
            pet.Leashed = leashed;
            pet._flier = character != null && character.m_flying;
            pet._swimmer = character != null && character.m_canSwim && prefab.name.IndexOf("Serpent", StringComparison.OrdinalIgnoreCase) >= 0;
            pet._animator = go.GetComponentInChildren<Animator>();
            // Hugin / Munin: Core's raven wears the raven model over a hidden Bat; the pet plays the RAVEN's animator.
            pet._raven = go.GetComponentInChildren<FiresCore.Ravens.RavenAnimDriver>(true);
            if (pet._raven != null) ShowRavenModel(pet._raven.gameObject, go.name);
            if (pet._raven != null)
            {
                // The raven plays RavenModel's Animator (Idle / Fly / talk), never the hidden Bat body's 'bat_animator', which
                // GetComponentInChildren finds first ([generator] 10-09). Look on RavenModel itself if the driver has none cached.
                Animator ravenAnim = pet._raven.Animator ?? pet._raven.GetComponent<Animator>() ?? pet._raven.GetComponentInChildren<Animator>(true);
                if (ravenAnim != null)
                {
                    pet._animator = ravenAnim;
                    ravenAnim.updateMode = AnimatorUpdateMode.Normal;
                    ravenAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    // The tutorial raven's controller starts in a hidden state until an appear trigger (its parameters:
                    // flyin, flyaway, poff, talk, teleportin, anglevel, idle). Fire 'teleportin' once, so it shows by itself
                    // instead of LateUpdate re-enabling the mesh every frame against it (Fire 10-09: glitchy).
                    if (ravenAnim.runtimeAnimatorController != null)
                        foreach (AnimatorControllerParameter p in ravenAnim.parameters)
                            if (p.name == "teleportin" && p.type == AnimatorControllerParameterType.Trigger) ravenAnim.SetTrigger("teleportin");
                }
                // Vanilla's raven model faces backwards along the body's forward (Fire 10-09: it sat facing behind him).
                pet._raven.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * pet._raven.transform.localRotation;
                foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
                    if (a != ravenAnim) a.enabled = false;   // the Bat's animator never moves anything visible
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {go.name}: raven animator '{(ravenAnim != null && ravenAnim.runtimeAnimatorController != null ? ravenAnim.runtimeAnimatorController.name : "NONE")}' on '{(ravenAnim != null ? ravenAnim.name : "-")}'");
            }
            if (pet._animator != null)
            {
                pet._animator.enabled = true;
                pet._animator.fireEvents = false;          // no footstep/attack events: nothing to receive them
                pet._animator.applyRootMotion = false;     // the pet moves itself; root motion would fight it
                pet._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (character != null)
            {
                pet._walkSpeed = character.m_walkSpeed > 0.1f ? character.m_walkSpeed : 2f;
                pet._runSpeed = Mathf.Max(pet._walkSpeed, character.m_runSpeed > 0.1f ? character.m_runSpeed : character.m_speed);
            }
            pet.ReadAttacks(prefab);
            // Fire (10-07): a fixed scale of the original, 1/4 up to Lox size, 1/8 above it and for every boss.
            // Then the height is clamped to MinHeight..MaxHeight, and a per-pet override in metres beats both (Fire: the
            // ratio is the idea, not exact).
            float originalHeight = ModelHeight(prefab);
            pet._big = (character != null && character.IsBoss()) || originalHeight > ThresholdHeight();
            float ratio = pet._big ? (BigScale?.Value ?? 0.2f) : (NormalScale?.Value ?? 0.25f);
            float target = originalHeight * ratio;
            pet._rule = "ratio";
            if (TryOverride(creature, out float wanted)) { target = wanted; pet._rule = "override"; }
            else if (pet._raven != null)
            {
                // Fire (10-08): "HALFWAY between pocket size and real size" (pocket = 1/4, real = 1).
                target = originalHeight * RavenScale;
                pet._rule = "raven";
            }
            else
            {
                float min = MinHeight?.Value ?? 0.25f, max = MaxHeight?.Value ?? 1.5f;
                float clamped = Mathf.Clamp(target, min, Mathf.Max(min, max));
                if (!Mathf.Approximately(clamped, target)) { target = clamped; pet._rule = "clamped"; }
            }
            pet._scale = originalHeight > 0.01f ? target / originalHeight : ratio;
            // FDT 583: Hugin came out 1.85 m and Munin 1.35 m from the SAME model: the raven's measured height depends on the
            // pose its animator is in when measured (wings), so the computed scale wandered (0.41 / 0.42 / 0.575). Ravens get the
            // scale that measured right (Munin 0.411 → 1.36 m, Hugin on a stand 0.420 → 1.28 m).
            if (pet._raven != null && pet._rule == "raven") pet._scale = RavenFixedScale;
            pet._targetHeight = target;
            pet._originalHeight = originalHeight;
            go.transform.localScale = prefab.transform.localScale * pet._scale;
            pet._reportAt = Time.time + 1f;
            pet._nextIdleEmote = Time.time + UnityEngine.Random.Range(25f, 50f);
            PetPetting.Attach(pet, prefab, character);
            return pet;
        }

        private static GameObject _staging;
        private string _stripReport;
        private bool _big;
        private float _scale, _originalHeight, _targetHeight;
        private string _rule;
        private float _reportAt = -1f;

        // Bound in Ascend's Awake (section "Pets"); the defaults stand in if binding failed.
        internal static BepInEx.Configuration.ConfigEntry<float> NormalScale;
        internal static BepInEx.Configuration.ConfigEntry<float> BigScale;
        internal static BepInEx.Configuration.ConfigEntry<string> ThresholdCreature;
        internal static BepInEx.Configuration.ConfigEntry<float> MinHeight;
        internal static BepInEx.Configuration.ConfigEntry<float> MaxHeight;
        internal static BepInEx.Configuration.ConfigEntry<string> Overrides;

        /// <summary>
        /// Built-in heights, used when the config names no height for that creature (a config default would never reach
        /// an existing .cfg). Fire (10-08): "the hive can be as large as 4m tall to make it easier to see whats happening".
        /// </summary>
        private const string BuiltInOverrides = "TheHive: 4";

        /// <summary>"Troll: 0.6, Neck: 0.3": a creature prefab name and its pet's height in metres. The config wins.</summary>
        private static bool TryOverride(string creature, out float height) =>
            TryOverride(Overrides?.Value, creature, out height) || TryOverride(BuiltInOverrides, creature, out height);

        private static bool TryOverride(string list, string creature, out float height)
        {
            height = 0f;
            if (string.IsNullOrEmpty(list)) return false;
            foreach (string entry in list.Split(','))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) continue;
                if (!string.Equals(entry.Substring(0, colon).Trim(), creature, StringComparison.OrdinalIgnoreCase)) continue;
                return float.TryParse(entry.Substring(colon + 1).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out height) && height > 0f;
            }
            return false;
        }

        internal static void BindConfig(BepInEx.Configuration.ConfigFile config)
        {
            NormalScale = config.Bind("Pets", "NormalScale", 0.25f,
                "Pet size as a fraction of the original creature, for creatures up to the threshold creature's size.");
            BigScale = config.Bind("Pets", "BigScale", 0.2f,
                "Pet size as a fraction of the original, for creatures taller than the threshold creature and every boss.");
            ThresholdCreature = config.Bind("Pets", "BigThresholdCreature", "Lox",
                "Prefab of the creature that sets the size threshold: anything taller than it uses BigScale.");
            MinHeight = config.Bind("Pets", "MinHeight", 0.25f,
                "Smallest a pet may be, in metres, after the scale is applied (overrides ignore it).");
            MaxHeight = config.Bind("Pets", "MaxHeight", 1.5f,
                "Tallest a pet may be, in metres, after the scale is applied (overrides ignore it).");
            Overrides = config.Bind("Pets", "HeightOverrides", "",
                "Per-pet heights in metres, comma separated, e.g. Troll: 0.6, Neck: 0.3 (creature prefab names). Applies to new pets. "
                + "Built in: TheHive: 4; a creature named here replaces its built-in height.");
        }

        private static readonly Dictionary<string, float> Heights = new Dictionary<string, float>();

        private static float ThresholdHeight()
        {
            string name = ThresholdCreature?.Value ?? "Lox";
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            return prefab != null ? ModelHeight(prefab) : float.MaxValue;
        }

        /// <summary>
        /// The original creature's height as it renders: a stripped copy is made live far below the world, its ACTIVE
        /// renderers' bounds are read, and it's destroyed; cached per creature. 0.0.514-515 read the prefab's meshes in
        /// bind pose, which also counted inactive parts (the Lox came out 9.90 m against its real 4.65 m).
        /// </summary>
        internal static float ModelHeight(GameObject prefab)
        {
            if (Heights.TryGetValue(prefab.name, out float cached)) return cached;
            GameObject probe = null;
            float height = 0f;
            bool wasForced = ZNetView.m_forceDisableInit;
            try
            {
                ZNetView.m_forceDisableInit = true;
                probe = Instantiate(prefab, _staging.transform, false);
                ZNetView.m_forceDisableInit = wasForced;
                Strip(probe, out _, out _);
                probe.transform.SetParent(null, false);
                probe.transform.position = new Vector3(0f, -5000f, 0f);
                HideScenery(probe);
                height = RenderedHeight(probe);
            }
            catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] height probe for {prefab.name} failed: {ex.Message}"); }
            finally
            {
                ZNetView.m_forceDisableInit = wasForced;
                if (probe != null) DestroyImmediate(probe);
            }
            Heights[prefab.name] = height;
            return height;
        }

        private static bool Keep(Component c) =>
            c is Transform || c is Animator || c is Renderer || c is MeshFilter || c is LODGroup || c is FiresCore.Ravens.RavenAnimDriver;

        /// <summary>
        /// Keeps only transforms, the Animator, LODGroups and the renderers; everything else could act (AI, drops, ZDO,
        /// sounds) or block the player (colliders, Rigidbody). Removes in dependency order: a component goes only once no
        /// remaining component on its object [RequireComponent]s its type, so Unity never refuses ("Can't remove Humanoid
        /// because CharacterDrop depends on it").
        /// </summary>
        private static int Strip(GameObject go, out int left, out int colliders)
        {
            var pending = new List<Component>();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c != null && !Keep(c)) pending.Add(c);

            int removed = 0;
            bool progress = true;
            while (pending.Count > 0 && progress)
            {
                progress = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Component c = pending[i];
                    if (c == null) { pending.RemoveAt(i); continue; }
                    if (IsRequired(c)) continue;
                    DestroyImmediate(c);
                    pending.RemoveAt(i);
                    removed++;
                    progress = true;
                }
            }
            // Whatever is still pinned is switched off rather than left live.
            foreach (var c in pending)
            {
                if (c is Behaviour b) b.enabled = false;
                else if (c is Collider col) col.enabled = false;
                else if (c is Rigidbody rb) { rb.isKinematic = true; rb.detectCollisions = false; }
            }
            left = pending.Count;
            colliders = 0;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) if (col.enabled) colliders++;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            return removed;
        }

        /// <summary>True when another component on the same object still requires this one's type.</summary>
        private static bool IsRequired(Component target)
        {
            Type type = target.GetType();
            foreach (var other in target.GetComponents<Component>())
            {
                if (other == null || other == target) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (Requires(req.m_Type0, type) || Requires(req.m_Type1, type) || Requires(req.m_Type2, type))
                        return true;
                }
            }
            return false;
        }

        private static bool Requires(Type required, Type actual) => required != null && required.IsAssignableFrom(actual);

        /// <summary>The proof line, 1 s after the spawn: class, scale and the rendered height it came out at.</summary>
        private void ReportOnce()
        {
            if (_reportAt < 0f || Time.time < _reportAt) return;

            // Once, 1 s in: the animated pose can stand taller or lower than the probe's rest pose (GoblinKing 0.66,
            // FrozenKing_p3 0.85 against 0.75 in 0.0.516), so correct the scale to the target on the live pose, then log.
            float rendered = MeasureHeight();
            // Ravens keep their fixed scale: their live height is pose-dependent, so a re-fit would undo it ([classes-ui] review).
            if (_raven == null && !_corrected && rendered > 0.01f && Mathf.Abs(rendered - _targetHeight) > _targetHeight * 0.05f)
            {
                _corrected = true;
                float k = _targetHeight / rendered;
                transform.localScale *= k;
                _scale *= k;
                _reportAt = Time.time + 0.5f;
                return;
            }
            _reportAt = float.MaxValue;
            if (_raven != null) DumpRenderers();
            float bigRatio = BigScale?.Value ?? 0.2f, normalRatio = NormalScale?.Value ?? 0.25f;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug(
                $"[FiresPets] {name}: class {(_big ? Fraction(bigRatio) + " (bigger than " + (ThresholdCreature?.Value ?? "Lox") + " or a boss)" : Fraction(normalRatio))}, "
                + $"rule {_rule}, target height {_targetHeight:0.00} m, scale {_scale:0.000}, localScale {transform.localScale.x:0.000}, "
                + $"original model {_originalHeight:0.00} m, rendered height {MeasureHeight():0.00} m, "
                + $"pet scale {_scale:0.000}, fx scale {FxScale:0.000} (= pet scale), {_stripReport}");
        }

        private bool _corrected;

        private Renderer[] _ravenBody;      // the raven's mesh renderers (no particles), kept visible every frame
        private int _rescues;

        // A safety net only since 'teleportin' (0.0.556): if the controller still hides the mesh, say so once, then every 300 times.
        private void NoteRescue(string what)
        {
            if (_rescues++ % 300 == 0)
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: the raven controller hid '{what}'; shown again ({_rescues} time(s))");
        }
        private bool _ravenParamsLogged;

        /// <summary>
        /// Round 10-09 (0.0.553): at 1 s the raven's 'model' was INACTIVE while its own 'raven' controller ran. Vanilla's
        /// tutorial raven starts hidden and its animation keys the model's active state, so the controller switches the mesh
        /// off every frame. After the animator (LateUpdate), the raven's mesh chain is switched back on.
        /// </summary>
        private void LateUpdate()
        {
            if (_raven == null) return;
            PlayPendingTalk();
            if (_ravenBody == null)
                _ravenBody = _raven.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            foreach (Renderer r in _ravenBody)
            {
                if (r == null) continue;
                for (Transform t = r.transform; t != null && t != transform; t = t.parent)
                    if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); NoteRescue(t.name); }
                if (!r.enabled) { r.enabled = true; NoteRescue(r.name + " (renderer)"); }
            }
            if (!_ravenParamsLogged && _animator != null && _animator.isActiveAndEnabled)
            {
                _ravenParamsLogged = true;
                string ps = string.Join(", ", _animator.parameters.Select(p => p.name + ":" + p.type));
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: raven controller parameters [{ps}]; mesh kept visible after the animator");
            }
        }

        /// <summary>
        /// Raven pets, 1 s in (Debug): every renderer under the pet NOW, with its path, state, mesh (+ vertex count), material and
        /// world bounds. Round 10-09: the build dump held only the raven 'model', yet 1 s later the raven measured 0.00 m while the
        /// test saw a 2.21 m humanoid on the pet: something changes the renderers after the build; this names it.
        /// </summary>
        private void DumpRenderers()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                string path = r.transform.name;
                for (Transform t = r.transform.parent; t != null && t != transform; t = t.parent) path = t.name + "/" + path;
                sb.Append($"\n  {path}: {r.GetType().Name} {(r.enabled ? "enabled" : "DISABLED")}, {(r.gameObject.activeInHierarchy ? "active" : "INACTIVE")}, "
                    + $"mesh '{(mesh != null ? mesh.name : "NONE")}' ({(mesh != null ? mesh.vertexCount : 0)} verts), "
                    + $"material '{(r.sharedMaterial != null ? r.sharedMaterial.name : "NONE")}', bounds {r.bounds.size.x:0.##}x{r.bounds.size.y:0.##}x{r.bounds.size.z:0.##}");
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: renderers 1 s in (animator '{(_animator != null ? _animator.name : "-")}'):{sb}");
        }

        private static string Fraction(float ratio) => ratio > 0.001f ? "1/" + Mathf.RoundToInt(1f / ratio) : ratio.ToString("0.###");

        private float MeasureHeight() => RenderedHeight(gameObject);

        /// <summary>
        /// Some "creatures" are a scene with a creature in it: TheHive is pines, rocks and a mound around two skinned
        /// hatchlings, so its model read 74 m and the hatchlings came out a few centimetres tall (round 7). When the
        /// static meshes make the model more than twice as tall as the skinned body, the body is the creature: every
        /// static mesh away from it is switched off, so the pet is sized and seen by its body. Meshes on or next to the
        /// body (a weapon, a crown) stay. Returns how many were hidden; 0 for an ordinary creature.
        /// </summary>
        /// <summary>
        /// Fire 10-08: pocket Hugin / Munin built, followed and perched but drew nothing ("rendered height 0.00 m"). The raven
        /// model comes from vanilla's tutorial raven, whose visual sits hidden until Raven shows it, so the copy can arrive
        /// inactive or with its renderers off. Wake the model's chain and its renderers; log what it found.
        /// </summary>
        private static void ShowRavenModel(GameObject model, string petName)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            int inactive = renderers.Count(r => !r.gameObject.activeSelf), off = renderers.Count(r => !r.enabled);
            for (Transform t = model.transform; t != null && t.gameObject.name != petName; t = t.parent)
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                for (Transform t = r.transform; t != null && t != model.transform; t = t.parent)
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                r.enabled = true;
            }
            // 0.0.539: all renderers were on, yet the pet measured 0.00 m tall: a zero scale on the chain (vanilla's hidden raven
            // visual) or an empty mesh. Any near-zero scale between the model and the pet root goes back to one.
            int rescaled = 0;
            for (Transform t = model.transform; t != null && t.gameObject.name != petName; t = t.parent)
            {
                Vector3 s = t.localScale;
                if (Mathf.Abs(s.x) < 0.001f || Mathf.Abs(s.y) < 0.001f || Mathf.Abs(s.z) < 0.001f) { t.localScale = Vector3.one; rescaled++; }
            }
            foreach (Renderer r in renderers)
            {
                Vector3 s = r.transform.localScale;
                if (Mathf.Abs(s.x) < 0.001f || Mathf.Abs(s.y) < 0.001f || Mathf.Abs(s.z) < 0.001f) { r.transform.localScale = Vector3.one; rescaled++; }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {petName}: raven model '{model.name}': {renderers.Length} renderer(s), "
                + $"{inactive} on an inactive object, {off} disabled, {rescaled} zero scale(s) reset; all shown");
            DumpHierarchy(model.transform.root, petName);
        }

        /// <summary>Once per raven pet at build (Debug): every object under the pet with active, scale and its renderer's state.</summary>
        private static void DumpHierarchy(Transform root, string petName)
        {
            var sb = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                Renderer r = t.GetComponent<Renderer>();
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : t.GetComponent<MeshFilter>()?.sharedMesh;
                sb.Append('\n').Append(' ', depth * 2).Append(t.name)
                  .Append(t.gameObject.activeSelf ? " [on]" : " [OFF]")
                  .Append($" scale {t.localScale.x:0.###}/{t.localScale.y:0.###}/{t.localScale.z:0.###}");
                if (r != null)
                    sb.Append($" | {r.GetType().Name} {(r.enabled ? "enabled" : "DISABLED")}, mesh '{(mesh != null ? mesh.name : "NONE")}', "
                        + $"bounds {r.bounds.size.x:0.##}x{r.bounds.size.y:0.##}x{r.bounds.size.z:0.##}, materials {r.sharedMaterials.Length}");
                foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(root, 0);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {petName}: hierarchy at build:{sb}");
        }

        private static int HideScenery(GameObject root)
        {
            bool anyBody = false, anyAll = false;
            Bounds body = default, all = default;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || !r.enabled) continue;
                if (!anyAll) { all = r.bounds; anyAll = true; } else all.Encapsulate(r.bounds);
                if (!(r is SkinnedMeshRenderer)) continue;
                if (!anyBody) { body = r.bounds; anyBody = true; } else body.Encapsulate(r.bounds);
            }
            if (!anyBody || all.size.y <= body.size.y * 2f) return 0;

            Bounds near = body;
            near.Expand(body.size * 0.5f);
            int hidden = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is SkinnedMeshRenderer || r is ParticleSystemRenderer || !r.enabled || near.Intersects(r.bounds)) continue;
                r.enabled = false;
                hidden++;
            }
            return hidden;
        }

        /// <summary>The height of the union of the object's active, enabled renderers (particles and trails left out).</summary>
        private static float RenderedHeight(GameObject root)
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.size.y : 0f;
        }


        // ---- movement: the pet WALKS to its owner (Fire, 10-07: no more dragging) -------------------------------------

        private const float FollowNear = 1.5f;      // stop inside this
        private const float FollowFar = 2.5f;       // start walking past this
        private const float RunPast = 6f;           // run past this
        private const float CatchUp = 15f;          // teleport only past this (or with no ground)

        private float _walkSpeed = 2f, _runSpeed = 6f;   // the ORIGINAL creature's gait, m/s
        private float _speed;                            // current world speed, m/s
        private float _yawRate;                          // deg/s, fed to turn_speed
        private string _gait = "idle";
        private float _nextGaitLog;
        private Vector3 _slot;                           // where beside/behind the owner this pet heads
        private bool _moving;

        private void Update()
        {
            if (Anchor == null) { Destroy(gameObject); return; }
            ReportOnce();
            if (_raven != null) UpdateRaven();
            else if (Leashed) UpdateLeashed();
            else UpdateFollow();
            Animate();

            if (Time.time >= _nextIdleEmote)
            {
                _nextIdleEmote = Time.time + UnityEngine.Random.Range(25f, 50f);
                PlayEmote(UnityEngine.Random.Range(0, 16), combat: false);
            }
        }

        private void UpdateFollow()
        {
            float dist = Flat(Anchor.position - transform.position, false).magnitude;
            if (dist > CatchUp || !TryGround(transform.position, out _))
            {
                // Far catch-up only: lands beside the owner, facing the same way.
                Vector3 p = Anchor.position - Anchor.forward * 1.2f + Anchor.right * 0.8f;
                p.y = TryGround(p, out float gy) ? gy : Anchor.position.y;
                transform.SetPositionAndRotation(p, Quaternion.LookRotation(Flat(Anchor.forward, true)));
                _speed = 0f;
                _moving = false;
                SetGait("teleport");
                return;
            }

            if (WanderOwner()) { Wander(); return; }

            // Hysteresis: start past FollowFar, stop inside FollowNear, so it doesn't stutter at the edge.
            if (!_moving && dist > FollowFar) _moving = true;
            if (_moving && dist < FollowNear) _moving = false;

            // Its spot: behind and to the side of the owner, picked once per walk so it doesn't orbit.
            if (!_moving || _slot == Vector3.zero)
                _slot = Anchor.InverseTransformPoint(transform.position).x >= 0f ? new Vector3(0.8f, 0f, -1.2f) : new Vector3(-0.8f, 0f, -1.2f);
            Vector3 goal = Anchor.TransformPoint(_slot);

            float ownerSpeed = OwnerSpeed();
            float wanted = 0f;
            if (_moving)
            {
                bool run = dist > RunPast || ownerSpeed > 3f;
                // Its own gait at its size, but never slower than the owner (or it falls behind and teleports).
                float gait = (run ? _runSpeed : _walkSpeed) * _scale;
                wanted = Mathf.Max(gait, run ? ownerSpeed * 1.15f : Mathf.Min(ownerSpeed, 2.5f), run ? 3f : 1.2f);
                SetGait(run ? "run" : "walk");
            }
            else SetGait("idle");
            _speed = Mathf.MoveTowards(_speed, wanted, (wanted > _speed ? 8f : 12f) * Time.deltaTime);

            Vector3 to = Flat(goal - transform.position, false);
            Vector3 dir = to.sqrMagnitude > 0.0004f ? to.normalized : Vector3.zero;
            Vector3 next = transform.position + dir * Mathf.Min(_speed * Time.deltaTime, to.magnitude);
            if (TryGround(next, out float y)) next.y = Mathf.Lerp(transform.position.y, y, Time.deltaTime * 12f);
            transform.position = next;

            // Faces where it walks; idle, it turns to look at its owner.
            Turn(_speed > 0.05f && dir != Vector3.zero ? dir : Flat(Anchor.position - transform.position, false));
        }

        // ---- NPC owners (Fire 10-09): a ground pet wanders near the NPC instead of trailing at 1.5 m ----

        private const float WanderRadius = 4f;
        private int _wanderOwnerState = -1;   // -1 unknown, 0 follow (player / companion), 1 wander (NPC)
        private Vector3 _wanderGoal;
        private float _nextWander;

        /// <summary>An owner that is neither a player nor a companion (an RPGmaker NPC): its ground pet wanders.</summary>
        private bool WanderOwner()
        {
            if (_wanderOwnerState < 0)
            {
                bool npc = Anchor.GetComponent<Player>() == null && Anchor.GetComponent("CompanionController") == null
                    && Anchor.GetComponent<Character>() != null;
                _wanderOwnerState = npc ? 1 : 0;
                if (npc) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: owner '{Anchor.name}' is an NPC, the pet wanders within {WanderRadius:0} m");
            }
            return _wanderOwnerState == 1;
        }

        private void Wander()
        {
            Vector3 flatTo = Flat(_wanderGoal - transform.position, false);
            bool arrived = _wanderGoal == Vector3.zero || flatTo.sqrMagnitude < 0.09f;
            // a new spot every few seconds, or when the NPC walked away from the old one
            if (Time.time >= _nextWander || Flat(_wanderGoal - Anchor.position, false).magnitude > WanderRadius + 1f)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * WanderRadius;
                _wanderGoal = Anchor.position + new Vector3(r.x, 0f, r.y);
                _nextWander = Time.time + UnityEngine.Random.Range(3f, 6f);
                flatTo = Flat(_wanderGoal - transform.position, false);
                arrived = false;
            }
            float wanted = arrived ? 0f : _walkSpeed * _scale;
            _speed = Mathf.MoveTowards(_speed, wanted, 8f * Time.deltaTime);
            SetGait(_speed > 0.05f ? "walk" : "idle");
            Vector3 dir = flatTo.sqrMagnitude > 0.0004f ? flatTo.normalized : Vector3.zero;
            Vector3 next = transform.position + dir * Mathf.Min(_speed * Time.deltaTime, flatTo.magnitude);
            if (TryGround(next, out float y)) next.y = Mathf.Lerp(transform.position.y, y, Time.deltaTime * 12f);
            transform.position = next;
            Turn(_speed > 0.05f && dir != Vector3.zero ? dir : Flat(Anchor.position - transform.position, false));
        }

        // ---- Hugin / Munin: fly while the owner moves, perch on the shoulder once they've stood still a moment (Fire 10-08) ----

        private const float RavenFixedScale = 0.415f;   // ~1.35 m for both ravens, independent of the measured pose
        private const float RavenScale = 0.53f;      // Fire 10-09: "a little smaller": ~1.35 m (was 0.625, ~1.6 m)
        private const float PerchAfter = 2f;         // seconds standing still before it lands on the shoulder
        private FiresCore.Ravens.RavenAnimDriver _raven;
        private float _ownerStillSince;
        private bool _perched;
        internal bool Perched => _perched;
        private Transform _shoulder;

        private const float TalkHold = 1.2f;
        private static readonly int TalkStateHash = Animator.StringToHash("talk");
        private float _talkUntil;
        private bool _talkPending;
        private int _talkFrame;
        private bool Croaking => Time.time < _talkUntil;

        // The croak: the raven controller's "talk" state played directly (its trigger if the state is missing).
        private void PlayPendingTalk()
        {
            if (!_talkPending || _animator == null || !_animator.isActiveAndEnabled) return;
            // FDT 583 (Hugin idle, in flight): a croak on the SAME frame as the driver's fly→idle crossfade lost to it. Wait
            // one frame after the switch so the driver has done its single Idle crossfade first (it then leaves the state alone).
            if (Time.frameCount < _talkFrame) return;
            _talkPending = false;
            bool has = _animator.HasState(0, TalkStateHash);
            if (has) _animator.CrossFade(TalkStateHash, 0.08f, 0, 0f);
            else _raven.Talk();
            string where = _perched ? "perched" : "in flight, held " + TalkHold + " s";
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: croak state 'talk' {(has ? "played" : "missing, trigger sent")} ({where})");
        }

        private void UpdateRaven()
        {
            if (Leashed)
            {
                // On a stand: perched on top of it.
                _raven.FlyingOverride = false;
                transform.position = Vector3.MoveTowards(transform.position, Anchor.position + Vector3.up * 0.55f, 6f * Time.deltaTime);
                Turn(Flat(Anchor.forward, true));
                return;
            }
            // Fire 10-09: a PROPER attach. Landed, the raven is parented to the owner's shoulder bone and moves with every
            // step, run, jump and turn (no lag, no sliding). Walking and turning never shake it off; it takes off only when
            // the owner SPRINTS, JUMPS or ATTACKS, and lands again after PerchAfter calm seconds.
            string reason = TakeOffReason(Anchor.GetComponent<Player>());
            if (reason != null || _calmSince <= 0f) _calmSince = reason != null ? Time.time : Time.time - PerchAfter;
            if (_perched)
            {
                if (reason == null) { _raven.FlyingOverride = false; return; }   // locked on the shoulder
                Detach(reason);
            }
            bool wantPerch = reason == null && Time.time - _calmSince >= PerchAfter
                && Flat(Anchor.position - transform.position, false).magnitude < CatchUp;
            if (wantPerch)
            {
                Vector3 seat = ShoulderPoint();
                transform.position = Vector3.MoveTowards(transform.position, seat, 6f * Time.deltaTime);   // swoops in
                Turn(Flat(seat - transform.position, false));
                _raven.FlyingOverride = !Croaking;
                if ((transform.position - seat).sqrMagnitude < 0.0025f) Attach(seat);
                return;
            }
            _raven.FlyingOverride = !Croaking;
            UpdateFollow();
        }

        private float _calmSince;

        /// <summary>Why the raven leaves the shoulder now, or null: sprinting, a jump (off the ground) or an attack.</summary>
        private static string TakeOffReason(Player p)
        {
            if (p == null) return null;
            if (p.InAttack()) return "attack";
            if (!p.IsOnGround()) return "jump";
            if (p.IsRunning()) return "sprint";
            return null;
        }

        private void Attach(Vector3 seat)
        {
            Transform bone = _shoulder != null ? _shoulder : Anchor;
            transform.SetParent(bone, true);
            transform.position = seat;
            transform.rotation = Quaternion.LookRotation(Flat(Anchor.forward, true));
            _perched = true;
            _raven.FlyingOverride = false;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: perched on the shoulder, attached to '{bone.name}' "
                + $"(local {transform.localPosition.x:0.00}/{transform.localPosition.y:0.00}/{transform.localPosition.z:0.00})");
        }

        private void Detach(string reason)
        {
            transform.SetParent(null, true);
            _perched = false;
            _raven.FlyingOverride = true;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: took off ({reason})");
        }

        /// <summary>The owner's right shoulder (the player rig's bone), or a point above it.</summary>
        private Vector3 ShoulderPoint()
        {
            if (_shoulder == null) _shoulder = Utils.FindChild(Anchor, "RightShoulder") ?? Utils.FindChild(Anchor, "Shoulder.r");
            return _shoulder != null
                ? _shoulder.position + Vector3.up * 0.08f
                : Anchor.position + Vector3.up * 1.55f + Anchor.right * 0.22f;
        }

        private float _lastOwnerSample;
        private Vector3 _lastOwnerPos;
        private float _ownerSpeed;

        private float OwnerSpeed()
        {
            if (Time.time - _lastOwnerSample >= 0.1f)
            {
                float dt = Time.time - _lastOwnerSample;
                if (_lastOwnerSample > 0f && dt > 0f)
                    _ownerSpeed = Flat(Anchor.position - _lastOwnerPos, false).magnitude / dt;
                _lastOwnerSample = Time.time;
                _lastOwnerPos = Anchor.position;
            }
            return _ownerSpeed;
        }

        private void UpdateLeashed()
        {
            Vector3 flat = Flat(transform.position - Anchor.position, false);
            if (flat.magnitude > 1.5f || flat.magnitude < 0.3f)
                transform.position = Anchor.position + (flat.sqrMagnitude > 0.01f ? flat.normalized : Anchor.right) * 0.9f;
            Vector3 p = transform.position;
            if (TryGround(p, out float y)) p.y = y;
            transform.position = p;
            _speed = 0f;
            SetGait("idle");
            Turn(Flat(Anchor.position - transform.position, false));
        }

        /// <summary>The ground (or water / flying height) under a point: a downward ray on the solid layers.</summary>
        private bool TryGround(Vector3 p, out float y)
        {
            y = 0f;
            if (_swimmer) { y = Floating.GetWaterLevel(p, ref _dummyWater) - 0.2f; return true; }
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(p, out float solid)) return false;
            y = solid;
            if (_flier) y += Mathf.Max(0.6f, 1.2f * _scale * 4f) + Mathf.Sin(Time.time * 1.7f) * 0.15f;
            return true;
        }

        private WaterVolume _dummyWater;

        private void Turn(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) { _yawRate = 0f; return; }
            Quaternion want = Quaternion.LookRotation(dir);
            float before = transform.eulerAngles.y;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 360f * Time.deltaTime);
            _yawRate = Mathf.DeltaAngle(before, transform.eulerAngles.y) / Mathf.Max(Time.deltaTime, 0.001f);
        }

        private void SetGait(string gait)
        {
            if (gait != _gait)
            {
                _gait = gait;
                _nextGaitLog = 0f;
            }
            if (Time.time < _nextGaitLog) return;
            _nextGaitLog = Time.time + 5f;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: gait {_gait}, speed {_speed:0.00} m/s, "
                + $"animator {(_animator == null ? "none" : _animator.enabled ? "enabled" : "DISABLED")}, forward_speed {_fedForward:0.00}, anim speed {_animSpeed:0.00}");
        }

        // ---- animation: fed like vanilla's Character does --------------------------------------------------------------

        private float _fedForward, _animSpeed = 1f;

        /// <summary>
        /// Vanilla feeds forward_speed in the creature's own m/s (Character.UpdateWalking). The pet moves at world speed
        /// on a 1/4 or 1/8 body, so the value fed is world speed / scale: the creature-sized speed that matches its legs.
        /// Past its run speed the run cycle plays faster instead (animator.speed) so the feet keep up.
        /// </summary>
        private void Animate()
        {
            if (_animator == null || _raven != null) return;   // a raven's animator is played by its RavenAnimDriver
            if (!_animator.enabled) _animator.enabled = true;
            float creatureSpeed = _scale > 0.001f ? _speed / _scale : _speed;
            _fedForward = Mathf.Min(creatureSpeed, _runSpeed);
            _animSpeed = creatureSpeed > _runSpeed && _runSpeed > 0.1f ? Mathf.Min(creatureSpeed / _runSpeed, 2.5f) : 1f;
            _animator.speed = _animSpeed;
            TrySetFloat(Character.s_forwardSpeed, _fedForward);
            TrySetFloat(Character.s_sidewaySpeed, 0f);
            TrySetFloat(Character.s_turnSpeed, _yawRate * Mathf.Deg2Rad);
            TrySetBool(Character.s_onGround, !_flier);
            TrySetBool(Character.s_flying, _flier);
            TrySetBool(Character.s_inWater, _swimmer);
            TrySetBool(Character.s_encumbered, false);
        }

        // ---- emotes: the creature's own attack animations and effects, harmless ------------------------------------

        private readonly List<Attack> _attacks = new List<Attack>();

        /// <summary>The weapon item each attack came from: most creature effects live on its shared data, not the Attack.</summary>
        private readonly Dictionary<Attack, ItemDrop.ItemData.SharedData> _sharedOf = new Dictionary<Attack, ItemDrop.ItemData.SharedData>();
        internal string LastTrigger = "";

        private int _sounds;
        private string _variant = "";
        private Color? _variantFx;

        /// <summary>
        /// The pet's colour or material variant (Fire 10-08, essences). Core draws it on this local clone; the pet's attack FX
        /// then take the variant's FX colour. "" = its own look. Re-applied only when the id changes.
        /// </summary>
        internal void SetVariant(string id)
        {
            id = id ?? "";
            if (id == _variant) return;
            _variant = id;
            bool applied = FiresCore.Variants.VariantApply.Apply(gameObject, id);
            _variantFx = FiresCore.Variants.VariantTable.TryGet(id, out FiresCore.Variants.VariantDef def) ? def.FxColor : (Color?)null;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: variant '{(id.Length > 0 ? id : "none")}'"
                + (id.Length > 0 ? (applied ? " applied" : " NOT applied (unknown id or no body renderer)") : " (own look)"));
        }

        /// <summary>Its attacks, read from the ORIGINAL prefab's weapons (the clone's Humanoid is stripped).</summary>
        private void ReadAttacks(GameObject prefab)
        {
            var humanoid = prefab.GetComponent<Humanoid>();
            if (humanoid == null) return;
            var seen = new HashSet<string>();
            void Add(GameObject item)
            {
                var shared = item != null ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null) return;
                foreach (Attack a in new[] { shared.m_attack, shared.m_secondaryAttack })
                    if (a != null && !string.IsNullOrEmpty(a.m_attackAnimation) && seen.Add(a.m_attackAnimation))
                    {
                        _attacks.Add(a);
                        _sharedOf[a] = shared;
                    }
            }
            if (humanoid.m_defaultItems != null) foreach (var i in humanoid.m_defaultItems) Add(i);
            if (humanoid.m_randomWeapon != null) foreach (var i in humanoid.m_randomWeapon) Add(i);
        }

        /// <summary>
        /// Plays one of the creature's attack animations (the trigger vanilla's Attack.Start sets) plus its start / trigger
        /// effects at pet size. Nothing networked is spawned (effect prefabs with a ZNetView are skipped), no damage, no
        /// projectiles. Combat emotes turn toward the owner's aim first.
        /// </summary>
        internal void PlayEmote(int index, bool combat, Vector3? lookAt = null)
        {
            if (_animator == null) return;
            if (_raven != null)
            {
                // A raven has no attack (Fire: no attacks): its emote is the croak. Same line format as every pet's for FDT.
                // [wishbone] 10-09 (FDT 582: Munin "no attack state"): Core's RavenAnimDriver crossfades any non-Fly state back to
                // Fly on the next frame while FlyingOverride is true, so a croak triggered in flight (the owner's attack makes it
                // take off) never played. The croak now holds the bird out of flight for TalkHold s and plays the "talk" state
                // itself in LateUpdate, after the driver's Update (overriding its Idle crossfade on that frame).
                _talkUntil = Time.time + TalkHold;
                _talkPending = true;
                _talkFrame = Time.frameCount + (_perched ? 0 : 2);
                LastTrigger = "talk";
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: {(combat ? "combat" : "idle")} emote, trigger 'talk', "
                    + $"animator {(_animator.enabled ? "enabled" : "DISABLED")}, culling {_animator.cullingMode} (raven croak), fx spawned 0, scaled x{FxScale:0.00}");
                return;
            }
            if (lookAt.HasValue) transform.rotation = Quaternion.LookRotation(Flat(lookAt.Value - transform.position, true));
            for (int i = 0; i < _attacks.Count; i++)
            {
                Attack attack = _attacks[(index + i) % _attacks.Count];
                string trigger = HasParam(attack.m_attackAnimation, AnimatorControllerParameterType.Trigger) ? attack.m_attackAnimation
                    : HasParam(attack.m_attackAnimation + "0", AnimatorControllerParameterType.Trigger) ? attack.m_attackAnimation + "0" : null;
                if (trigger == null) continue;
                PlayTrigger(trigger, attack, combat, "");
                return;
            }

            // The attack names don't match this animator (TheHive's 'hatchling_animator', pad round 4): play one of the
            // animator's own action triggers instead, with the first attack's effects.
            List<string> own = OwnActionTriggers();
            if (own.Count > 0)
            {
                PlayTrigger(own[index % own.Count], _attacks.Count > 0 ? _attacks[index % _attacks.Count] : null, combat, " (animator's own trigger)");
                return;
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: no attack trigger to play ({_attacks.Count} attacks read: "
                + string.Join(", ", _attacks.Select(a => a.m_attackAnimation)) + "; animator triggers: "
                + string.Join(", ", _animator.parameters.Where(p => p.type == AnimatorControllerParameterType.Trigger).Select(p => p.name)) + ")");
        }

        private void PlayTrigger(string trigger, Attack attack, bool combat, string note)
        {
            _animator.SetTrigger(trigger);
            LastTrigger = trigger;
            _sounds = 0;
            int fx = attack != null ? SpawnAttackFx(attack) : 0;
            // Format kept stable for FDT's pets_test ("... emote, trigger 'T', animator ..."); the fx count is appended.
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: {(combat ? "combat" : "idle")} emote, trigger '{trigger}', "
                + $"animator {(_animator.enabled ? "enabled" : "DISABLED")}, culling {_animator.cullingMode}{note}, fx spawned {fx}, scaled x{FxScale:0.00}");
            StartCoroutine(EnsureAttackState(trigger));
            // Its own line, so the emote line FDT parses stays as it is: the attack's real sounds (ZSFX / AudioSource) it played.
            // The attack this emote played, and whether its effect lists carry any sound at all: a 0 here with "lists sfx
            // False" is that attack being silent in vanilla too, not a lost sound (round 21, Fader / FrozenKing).
            string attackName = attack != null ? attack.m_attackAnimation : "none";
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: emote sounds {_sounds} (of the {fx} fx); "
                + $"attack '{attackName}', lists sfx {(attack != null && AttackListsSound(attack))}");
        }

        /// <summary>
        /// Round 12: Bonemass's pet took 'aoe' but never left its state (no attack-tagged state entered), while the same
        /// Any State trigger works for the other bosses. Its controller starts in "Check" / "wakeup" (tagged freeze), which
        /// vanilla's AI drives out of and a pet never does. So 0.3 s after a trigger, if no attack-tagged state is playing or
        /// coming, the trigger is reset and the pet cross-fades straight into the state named after it (Bonemass: 'aoe',
        /// 'spawn'), or out of a freeze state into its locomotion first. Logged either way, for the next run.
        /// </summary>
        private System.Collections.IEnumerator EnsureAttackState(string trigger)
        {
            yield return new WaitForSeconds(0.3f);
            if (_animator == null || !_animator.isActiveAndEnabled) yield break;
            AnimatorStateInfo now = _animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (now.IsTag("attack") || next.IsTag("attack")) yield break;

            string from = now.IsTag("freeze") ? "freeze" : now.IsTag("idle") ? "idle" : "untagged";
            _animator.ResetTrigger(trigger);
            int hash = Animator.StringToHash(trigger);
            string action;
            if (_animator.HasState(0, hash))
            {
                _animator.CrossFadeInFixedTime(hash, 0.1f, 0);
                action = $"cross-faded into state '{trigger}'";
            }
            else if (now.IsTag("freeze") && _animator.HasState(0, Animator.StringToHash("Movement")))
            {
                _animator.CrossFadeInFixedTime("Movement", 0.1f, 0);
                action = "no state of that name; left the freeze state for 'Movement' (the next emote can play)";
            }
            else action = "no state of that name to play";
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: trigger '{trigger}' started no attack state in 0.3 s "
                + $"(was in a {from} state); {action}");
        }

        /// <summary>Triggers that look like an action: not movement, hits, death or equipment.</summary>
        private List<string> OwnActionTriggers()
        {
            if (_ownTriggers != null) return _ownTriggers;
            string[] skip = { "stagger", "hit", "death", "die", "jump", "land", "dodge", "equip", "sheath", "flying", "takeoff", "blink", "interrupt", "stun", "wakeup", "sleep" };
            _ownTriggers = _animator.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Trigger)
                .Select(p => p.name)
                .Where(n => !skip.Any(s => n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            return _ownTriggers;
        }

        private List<string> _ownTriggers;

        /// <summary>
        /// Fire (10-08): "attack fx size should scale according to their relative size compared to the original". The
        /// effects are authored for the full-size creature, so they scale by exactly the body's ratio: no floor, no extra.
        /// </summary>
        private float FxScale => _scale;

        /// <summary>
        /// The attack's REAL effects at pet size (Fire 10-07: "pocket Eikthyr should play a mini lightning stomp"):
        /// start, trigger and burst effects at the pet, hit / terrain-hit effects and the spawned object (an AoE, a stomp)
        /// at the point it strikes. Every one is built VISUAL-ONLY (see <see cref="SpawnVisual"/>). Returns how many spawned.
        /// </summary>
        private int SpawnAttackFx(Attack attack)
        {
            Vector3 center = transform.position + Vector3.up * _targetHeight * 0.5f;
            Vector3 strike = transform.position + transform.forward * attack.m_attackRange * FxScale;
            int spawned = 0;
            // Vanilla's Attack plays BOTH levels: the Attack's own lists and its weapon item's shared ones. For most creatures
            // the effects are only on the item (The Elder's stomp: every Attack list empty, the item's start + trigger set;
            // round 5 showed "fx spawned 0").
            _sharedOf.TryGetValue(attack, out ItemDrop.ItemData.SharedData shared);
            spawned += SpawnEffects(attack.m_startEffect, center);
            spawned += SpawnEffects(shared?.m_startEffect, center);
            spawned += SpawnEffects(attack.m_triggerEffect, center);
            spawned += SpawnEffects(shared?.m_triggerEffect, center);
            spawned += SpawnEffects(attack.m_burstEffect, center);
            spawned += SpawnEffects(attack.m_hitEffect, strike);
            spawned += SpawnEffects(shared?.m_hitEffect, strike);
            spawned += SpawnEffects(attack.m_hitTerrainEffect, strike);
            spawned += SpawnEffects(shared?.m_hitTerrainEffect, strike);
            if (attack.m_spawnOnTrigger != null && SpawnVisual(attack.m_spawnOnTrigger, strike, transform.rotation, false) != null) spawned++;
            // A projectile attack's look is its projectile (The Elder's spit, its root spawn): shown inert at the strike point.
            if (attack.m_attackProjectile != null && SpawnVisual(attack.m_attackProjectile, strike, transform.rotation, false) != null) spawned++;
            return spawned;
        }

        /// <summary>True when any of the attack's effect lists (its own and its weapon item's) has a prefab with a sound.</summary>
        private bool AttackListsSound(Attack attack)
        {
            _sharedOf.TryGetValue(attack, out ItemDrop.ItemData.SharedData shared);
            foreach (EffectList list in new[] { attack.m_startEffect, attack.m_triggerEffect, attack.m_burstEffect, attack.m_hitEffect,
                attack.m_hitTerrainEffect, shared?.m_startEffect, shared?.m_triggerEffect, shared?.m_hitEffect, shared?.m_hitTerrainEffect })
            {
                if (list?.m_effectPrefabs == null) continue;
                foreach (EffectList.EffectData e in list.m_effectPrefabs)
                    if (e != null && e.m_enabled && e.m_prefab != null
                        && (e.m_prefab.GetComponentInChildren<ZSFX>(true) != null || e.m_prefab.GetComponentInChildren<AudioSource>(true) != null))
                        return true;
            }
            return false;
        }

        private int SpawnEffects(EffectList list, Vector3 at)
        {
            if (list?.m_effectPrefabs == null) return 0;
            int spawned = 0;
            foreach (EffectList.EffectData effect in list.m_effectPrefabs)
            {
                if (effect == null || !effect.m_enabled || effect.m_prefab == null) continue;
                if (SpawnVisual(effect.m_prefab, at, transform.rotation, effect.m_attach || effect.m_follow) != null) spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// A visual-only copy of an effect prefab, scaled to the pet. It is instantiated under an INACTIVE holder, so no Awake
        /// runs (no ZDO from a ZNetView, no Aoe registering). Then everything that could act is removed: every Valheim
        /// component except the purely visual / audio ones (so no Aoe, Projectile, EffectArea or CamShaker: no damage, status
        /// effects or camera shake on anyone), and every Collider and Rigidbody. Then it's scaled and switched on, and it's
        /// gone after 6 s.
        /// </summary>
        private GameObject SpawnVisual(GameObject prefab, Vector3 at, Quaternion rotation, bool attach)
        {
            GameObject fx = null;
            try
            {
                if (_fxHolder == null)
                {
                    _fxHolder = new GameObject("FiresPetFxHolder");
                    _fxHolder.SetActive(false);
                    DontDestroyOnLoad(_fxHolder);
                }
                fx = Instantiate(prefab, at, rotation, _fxHolder.transform);
                fx.name = prefab.name + " (pet fx)";
                StripToVisual(fx);
                ScaleFx(fx, FxScale);
                if (_variantFx.HasValue) FiresCore.Variants.VariantFx.Recolour(fx, _variantFx.Value);
                fx.transform.SetParent(attach ? transform : null, true);
                fx.SetActive(true);
                if (fx.GetComponentInChildren<ZSFX>(true) != null || fx.GetComponentInChildren<AudioSource>(true) != null) _sounds++;
                Destroy(fx, 6f);
                return fx;
            }
            catch (Exception e)
            {
                if (fx != null) Destroy(fx);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresPets] {name}: fx '{prefab.name}' skipped: {e.Message}");
                return null;
            }
        }

        private static GameObject _fxHolder;

        /// <summary>Valheim components that only draw or sound; everything else of Valheim's is removed from a pet fx.</summary>
        private static readonly HashSet<string> VisualKeep = new HashSet<string>
        {
            "ZSFX", "TimedDestruction", "LightFlicker", "LightLod", "ParticleDecal", "InstanceRenderer", "LineConnect",
            "AnimateLight", "Billboard", "RandomAnimation", "RandomSpeed",
        };

        private static void StripToVisual(GameObject fx)
        {
            // Two passes: Valheim's components first, then the physics they may [RequireComponent] (removing a Rigidbody while
            // a Projectile still needs it logs a Unity error, and the tests count errors).
            System.Reflection.Assembly game = typeof(ZNetView).Assembly;
            foreach (Component c in fx.GetComponentsInChildren<Component>(true))
                if (c != null && !(c is Transform) && c.GetType().Assembly == game && !VisualKeep.Contains(c.GetType().Name))
                    DestroyImmediate(c);
            foreach (Component c in fx.GetComponentsInChildren<Component>(true))
                if (c is Collider || c is Joint) DestroyImmediate(c);
            foreach (Rigidbody body in fx.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        }

        /// <summary>
        /// Shrinks an effect by exactly the pet's ratio. Only the root transform is scaled, and every particle system is
        /// switched to Hierarchy scaling so its size, shape and emission follow that one scale (scaling them as well, as
        /// 0.0.519-0.0.522 did, shrank Hierarchy systems twice). Whatever a transform doesn't scale is scaled by hand: a
        /// world-space system's speed and gravity, lights' range, trail and line widths. Debris stays, at the same ratio.
        /// </summary>
        private static void ScaleFx(GameObject fx, float s)
        {
            fx.transform.localScale *= s;
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
                {
                    main.startSpeedMultiplier *= s;
                    main.gravityModifierMultiplier *= s;
                }
            }
            foreach (Light light in fx.GetComponentsInChildren<Light>(true)) light.range *= s;
            foreach (TrailRenderer trail in fx.GetComponentsInChildren<TrailRenderer>(true)) trail.widthMultiplier *= s;
            foreach (LineRenderer line in fx.GetComponentsInChildren<LineRenderer>(true)) line.widthMultiplier *= s;
            float volume = Mathf.Clamp(0.4f + s, 0.4f, 1f);
            foreach (AudioSource audio in fx.GetComponentsInChildren<AudioSource>(true)) audio.volume *= volume;
            foreach (ZSFX sfx in fx.GetComponentsInChildren<ZSFX>(true))
            {
                sfx.m_minVol *= volume;
                sfx.m_maxVol *= volume;
            }
        }

        private static Vector3 Flat(Vector3 v, bool nonZero)
        {
            v.y = 0f;
            return nonZero && v.sqrMagnitude < 0.0001f ? Vector3.forward : v;
        }

        private readonly Dictionary<int, AnimatorControllerParameterType> _params = new Dictionary<int, AnimatorControllerParameterType>();

        private bool HasParam(string name, AnimatorControllerParameterType type) => HasParam(Animator.StringToHash(name), type);

        private bool HasParam(int hash, AnimatorControllerParameterType type)
        {
            if (_params.Count == 0 && _animator.runtimeAnimatorController != null)
                foreach (var p in _animator.parameters) _params[p.nameHash] = p.type;
            return _params.TryGetValue(hash, out var t) && t == type;
        }

        private void TrySetFloat(int hash, float v) { if (HasParam(hash, AnimatorControllerParameterType.Float)) _animator.SetFloat(hash, v); }
        private void TrySetBool(int hash, bool v) { if (HasParam(hash, AnimatorControllerParameterType.Bool)) _animator.SetBool(hash, v); }
    }
}
