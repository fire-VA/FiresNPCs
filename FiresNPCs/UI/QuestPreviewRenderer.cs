using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Renders 3D prefab previews into a RenderTexture for display in UI panels.
    /// Spawns the prefab 2000m above the player inside an invisible collision container
    /// so it cannot fall. A dedicated camera renders to a RenderTexture that is displayed
    /// in a RawImage. Matches the proven PrefabPreviewStage/PrefabPreviewCamera pattern.
    /// </summary>
    public class QuestPreviewRenderer : MonoBehaviour
    {
        private static QuestPreviewRenderer _instance;
        public static QuestPreviewRenderer Instance => _instance;

        private const int RTWidth = 512;
        private const int RTHeight = 384;
        private const float ContainerPadding = 5f;
        private const float MinContainerSize = 10f;
        private const float WallThickness = 1f;

        private Camera _camera;
        private RenderTexture _renderTexture;
        private GameObject _stagedClone;
        private GameObject _container;
        private Bounds _stagedBounds;

        // Orbit state
        private float _yaw = 135f;
        private float _pitch = 25f;
        private float _distance = 5f;
        private Vector3 _focusPoint;

        private const float MinDistance = 0.3f;
        private const float MaxDistance = 100f;

        /// <summary>The RenderTexture to assign to a RawImage.</summary>
        public RenderTexture Texture => _renderTexture;

        /// <summary>Whether a prefab is currently staged for preview.</summary>
        public bool HasPreview => _stagedClone != null;

        #region Lifecycle

        public static QuestPreviewRenderer EnsureInstance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("QuestPreviewRenderer");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<QuestPreviewRenderer>();
            return _instance;
        }

        public static void Cleanup()
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            CreateCamera();
        }

        private void OnDestroy()
        {
            ClearStage();
            ReleaseRenderTexture();
            if (_camera != null) Destroy(_camera.gameObject);
            if (_instance == this) _instance = null;
        }

        private void LateUpdate()
        {
            if (_camera != null && _camera.enabled)
                ApplyCameraTransform();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Stages a prefab for 3D preview rendering. Spawns 2000m above the player
        /// inside an invisible collision container, matching the PrefabPreviewStage pattern.
        /// </summary>
        public bool StagePreview(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;

            ClearStage();

            // Resolve source prefab
            GameObject source = null;
            if (ZNetScene.instance != null)
                source = ZNetScene.instance.GetPrefab(prefabName);
            if (source == null && ObjectDB.instance != null)
                source = ObjectDB.instance.GetItemPrefab(prefabName);
            if (source == null) return false;

            try
            {
                // Measure source to size the container
                Bounds prefabBounds = CalculateBounds(source);
                float innerX = Mathf.Max(prefabBounds.size.x + ContainerPadding * 2f, MinContainerSize);
                float innerY = Mathf.Max(prefabBounds.size.y + ContainerPadding * 2f, MinContainerSize);
                float innerZ = Mathf.Max(prefabBounds.size.z + ContainerPadding * 2f, MinContainerSize);

                // Position: 2000m directly above the player
                Vector3 playerPos = Player.m_localPlayer != null
                    ? Player.m_localPlayer.transform.position
                    : Vector3.zero;
                Vector3 center = new Vector3(playerPos.x, playerPos.y + 2000f, playerPos.z);

                // Build 6-sided invisible container (floor, ceiling, 4 walls)
                _container = new GameObject("QuestPreviewContainer");
                _container.transform.position = center;
                CreateWall(_container.transform, "Floor",
                    Vector3.down * (innerY * 0.5f + WallThickness * 0.5f),
                    new Vector3(innerX + WallThickness * 2f, WallThickness, innerZ + WallThickness * 2f));
                CreateWall(_container.transform, "Ceiling",
                    Vector3.up * (innerY * 0.5f + WallThickness * 0.5f),
                    new Vector3(innerX + WallThickness * 2f, WallThickness, innerZ + WallThickness * 2f));
                CreateWall(_container.transform, "WallNX",
                    Vector3.left * (innerX * 0.5f + WallThickness * 0.5f),
                    new Vector3(WallThickness, innerY, innerZ));
                CreateWall(_container.transform, "WallPX",
                    Vector3.right * (innerX * 0.5f + WallThickness * 0.5f),
                    new Vector3(WallThickness, innerY, innerZ));
                CreateWall(_container.transform, "WallNZ",
                    Vector3.back * (innerZ * 0.5f + WallThickness * 0.5f),
                    new Vector3(innerX, innerY, WallThickness));
                CreateWall(_container.transform, "WallPZ",
                    Vector3.forward * (innerZ * 0.5f + WallThickness * 0.5f),
                    new Vector3(innerX, innerY, WallThickness));

                // Spawn position: on the floor of the container
                float floorTopY = center.y - innerY * 0.5f;
                Vector3 spawnPos = new Vector3(center.x, floorTopY + 0.1f, center.z);

                // Disable dangerous Awake behaviours on SOURCE before instantiate
                var disabledOnSource = new List<MonoBehaviour>();
                DisableDangerousComponents(source, disabledOnSource);

                _stagedClone = Instantiate(source, spawnPos, Quaternion.identity);

                // Re-enable on source immediately
                foreach (var mb in disabledOnSource)
                    if (mb != null) mb.enabled = true;

                if (_stagedClone == null) return false;

                // Ensure active
                if (!_stagedClone.activeSelf)
                    _stagedClone.SetActive(true);
                _stagedClone.name = prefabName + "_questpreview";

                // Freeze ALL physics and disable AI immediately
                FreezePhysics(_stagedClone);

                // Prepare visuals: enable renderers, force LOD0
                PrepareVisuals(_stagedClone);

                // Calculate bounds and frame camera
                _stagedBounds = CalculateBounds(_stagedClone);
                _focusPoint = _stagedBounds.center;
                float size = _stagedBounds.size.magnitude;
                _distance = Mathf.Clamp(Mathf.Max(size * 1.5f, 3f), MinDistance, MaxDistance);
                _yaw = 135f;
                _pitch = 25f;

                EnsureRenderTexture();
                if (_camera != null)
                {
                    _camera.nearClipPlane = _distance * 0.01f;
                    _camera.farClipPlane = _distance * 10f;
                    _camera.enabled = true;
                }
                ApplyCameraTransform();

                // Render one explicit frame, then schedule a second render after
                // the animator has had a frame to bind skinned meshes
                if (_camera != null)
                    _camera.Render();
                StartCoroutine(ReframeAfterDelay());

                Debug.Log($"[QuestPreviewRenderer] Staged '{prefabName}' at {spawnPos}, " +
                    $"bounds={_stagedBounds.size}, distance={_distance}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[QuestPreviewRenderer] Failed to stage '{prefabName}': {ex.Message}");
                ClearStage();
                return false;
            }
        }

        /// <summary>
        /// After 2 frames, re-enable renderers, recalc bounds, reframe, and render again.
        /// This catches SkinnedMeshRenderers that needed animator ticks to bind bones.
        /// </summary>
        private IEnumerator ReframeAfterDelay()
        {
            yield return null;
            yield return null;

            if (_stagedClone == null) yield break;

            // Force renderers on again (some may have been toggled by LOD or animator)
            foreach (var r in _stagedClone.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
            foreach (var lod in _stagedClone.GetComponentsInChildren<LODGroup>(true))
                lod.ForceLOD(0);

            // Recalculate bounds - skinned meshes may report better bounds now
            var newBounds = CalculateBounds(_stagedClone);
            if (newBounds.size.sqrMagnitude > _stagedBounds.size.sqrMagnitude)
            {
                _stagedBounds = newBounds;
                _focusPoint = _stagedBounds.center;
                float size = _stagedBounds.size.magnitude;
                _distance = Mathf.Clamp(Mathf.Max(size * 1.5f, 3f), MinDistance, MaxDistance);
                if (_camera != null)
                {
                    _camera.nearClipPlane = _distance * 0.01f;
                    _camera.farClipPlane = _distance * 10f;
                }
                ApplyCameraTransform();
            }

            if (_camera != null)
                _camera.Render();
        }

        /// <summary>Clears the current preview clone and container, disables the camera.</summary>
        public void ClearStage()
        {
            StopAllCoroutines();

            if (_stagedClone != null)
            {
                var nv = _stagedClone.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid() && ZNetScene.instance != null)
                {
                    try { ZNetScene.instance.Destroy(_stagedClone); }
                    catch { Destroy(_stagedClone); }
                }
                else
                {
                    Destroy(_stagedClone);
                }
                _stagedClone = null;
            }

            if (_container != null)
            {
                Destroy(_container);
                _container = null;
            }

            if (_camera != null)
                _camera.enabled = false;
        }

        /// <summary>Orbits the preview camera.</summary>
        public void Orbit(float yawDelta, float pitchDelta)
        {
            _yaw += yawDelta * 0.4f;
            _pitch = Mathf.Clamp(_pitch - pitchDelta * 0.4f, -80f, 80f);
        }

        /// <summary>Zooms the preview camera.</summary>
        public void Zoom(float delta)
        {
            _distance -= delta * _distance * 1.0f;
            _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
        }

        /// <summary>
        /// Renders a static icon sprite synchronously. Returns null if it can't be rendered.
        /// </summary>
        public static Sprite RenderIconSprite(string prefabName, int size = 128)
        {
            var inst = EnsureInstance();
            if (!inst.StagePreview(prefabName)) return null;

            try
            {
                var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 2;
                rt.Create();

                inst._camera.targetTexture = rt;
                inst._camera.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                inst._camera.targetTexture = inst._renderTexture;
                rt.Release();
                Destroy(rt);

                return Sprite.Create(tex, new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            catch
            {
                return null;
            }
            finally
            {
                inst.ClearStage();
            }
        }

        #endregion

        #region Camera

        private void CreateCamera()
        {
            var camGo = new GameObject("QuestPreviewCam");
            camGo.transform.SetParent(transform, false);

            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            _camera.fieldOfView = 40f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 500f;
            _camera.depth = -15;
            _camera.enabled = false;
            _camera.cullingMask = ~0; // All layers - clone is 2000m above player

            EnsureRenderTexture();
            _camera.targetTexture = _renderTexture;
        }

        private void EnsureRenderTexture()
        {
            if (_renderTexture != null && _renderTexture.IsCreated()) return;
            ReleaseRenderTexture();
            _renderTexture = new RenderTexture(RTWidth, RTHeight, 16, RenderTextureFormat.ARGB32);
            _renderTexture.antiAliasing = 2;
            _renderTexture.Create();
            if (_camera != null) _camera.targetTexture = _renderTexture;
        }

        private void ReleaseRenderTexture()
        {
            if (_renderTexture != null)
            {
                if (_camera != null && _camera.targetTexture == _renderTexture)
                    _camera.targetTexture = null;
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }

        private void ApplyCameraTransform()
        {
            if (_camera == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 dir = rot * Vector3.forward;
            Vector3 target = _focusPoint;
            _camera.transform.position = target - dir * _distance;
            _camera.transform.LookAt(target);
        }

        #endregion

        #region Container & Physics Helpers

        private static void CreateWall(Transform parent, string name, Vector3 localPos, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPos;
            wall.layer = LayerMask.NameToLayer("Default");
            var box = wall.AddComponent<BoxCollider>();
            box.size = size;
        }

        /// <summary>
        /// Disables components on the SOURCE prefab that crash during Instantiate.
        /// Re-enable these immediately after Instantiate returns.
        /// </summary>
        private static void DisableDangerousComponents(GameObject source, List<MonoBehaviour> disabled)
        {
            foreach (var mb in source.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.enabled) continue;
                string name = mb.GetType().Name;
                switch (name)
                {
                    case "Ragdoll":
                    case "BaseAI":
                    case "AnimalAI":
                    case "MonsterAI":
                    case "Projectile":
                    case "Aoe":
                    case "Ship":
                    case "Fish":
                    case "FootStep":
                        mb.enabled = false;
                        disabled.Add(mb);
                        break;
                }
            }
        }

        /// <summary>
        /// Freezes all physics on the clone: rigidbodies kinematic + disables
        /// all MonoBehaviours that cause movement/physics/AI side effects.
        /// Matching PrefabPreviewStage.FreezePhysics exactly.
        /// </summary>
        private static void FreezePhysics(GameObject go)
        {
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb is Renderer) continue;
                string typeName = mb.GetType().Name;
                switch (typeName)
                {
                    case "StaticPhysics":
                    case "Floating":
                    case "Ship":
                    case "ShipEffects":
                    case "Fish":
                    case "Projectile":
                    case "Aoe":
                    case "BaseAI":
                    case "AnimalAI":
                    case "MonsterAI":
                    case "Humanoid":
                    case "Character":
                    case "Destructible":
                    case "TreeBase":
                    case "TreeLog":
                    case "Ragdoll":
                    case "FootStep":
                    case "Windmill":
                    case "WaterVolume":
                    case "ZNetView":
                        mb.enabled = false;
                        break;
                }
            }
        }

        /// <summary>
        /// Prepares visuals: forces LOD0, enables renderers, plays particles.
        /// Matching PrefabPreviewStage.PrepareVisuals.
        /// </summary>
        private static void PrepareVisuals(GameObject go)
        {
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
                lod.ForceLOD(0);

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.activeInHierarchy)
                    ps.Play(true);
            }
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            Bounds? result = null;
            float maxExtent = 100f;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (renderers[i] is ParticleSystemRenderer) continue;

                Bounds rb = renderers[i].bounds;
                if (rb.size.x > maxExtent || rb.size.y > maxExtent || rb.size.z > maxExtent) continue;
                float dist = Vector3.Distance(rb.center, go.transform.position);
                if (dist > maxExtent * 2f) continue;

                if (result == null) result = rb;
                else
                {
                    var b = result.Value;
                    b.Encapsulate(rb);
                    result = b;
                }
            }

            if (result == null)
            {
                var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    if (meshFilters[i]?.sharedMesh == null) continue;
                    var mb = meshFilters[i].sharedMesh.bounds;
                    Vector3 wc = meshFilters[i].transform.TransformPoint(mb.center);
                    Vector3 ws = Vector3.Scale(mb.size, meshFilters[i].transform.lossyScale);
                    var wb = new Bounds(wc, ws);
                    if (result == null) result = wb;
                    else
                    {
                        var b = result.Value;
                        b.Encapsulate(wb);
                        result = b;
                    }
                }
            }

            if (result == null)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = result.Value;
            if (bounds.size.sqrMagnitude < 0.01f)
                bounds.size = Vector3.one;
            return bounds;
        }

        #endregion
    }
}
    

