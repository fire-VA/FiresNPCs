using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker.UI.Books
{
    /// <summary>
    /// Renders one of the firesbooks 3D book prefabs (default the animated opening book) into a RenderTexture for
    /// display on a UI RawImage. Spawns the book 3000m above the player (out of sight, nothing to collide with),
    /// points a dedicated camera front-above at the open pages, and plays its Animator clip live so the book opens
    /// on the panel. Modeled on QuestPreviewRenderer but bundle-sourced (booksBundle) and animation-driven.
    ///
    /// Framing (yaw/pitch/dist/height + light) is exposed so we can tune it from in-game screenshots while the
    /// quest journal is polished.
    /// </summary>
    public class FiresBookView : MonoBehaviour
    {
        private static FiresBookView _instance;
        public static FiresBookView Instance => _instance;

        private const int RTWidth = 1024;
        private const int RTHeight = 768;

        // Tunable framing — adjust from screenshots.
        public float Yaw = 0f;        // around the book (0 = front)
        public float Pitch = 58f;     // look-down angle onto the open pages
        public float DistanceMul = 1.15f;
        public float HeightMul = 0.15f;
        public Vector3 BookEuler = new Vector3(180f, 0f, 0f);  // flip so the pages/spread face the camera
        public Color Background = new Color(0f, 0f, 0f, 0f);   // transparent so the panel shows through
        public float OpenSpeed = 1f;   // opening-animation speed multiplier (config QuestBookOpenSpeed)
        public static Color MatTint = new Color(0.55f, 0.53f, 0.48f, 1f);   // albedo multiplier on the book mats to fight the game-sun blow-out
        public static Color LeatherTint = new Color(0.62f, 0.28f, 0.20f, 1f); // red-brown multiplier for the leather cover (parchment keeps MatTint)

        // DEV TOOL, off by default (bound from [Debug] BookViewCaptures in Ascend). When on, opening a book view
        // dumps render PNGs + a state file to FiresConfigPaths.Debug/BookView so framing can be tuned. Left the login-capture stall
        // + [FiresBookView] captured spam when it was always-on.
        public static bool DebugCapture = false;

        private Camera _camera;
        private RenderTexture _rt;
        private GameObject _book;
        private Bounds _bounds;
        private Vector3 _focus;
        private float _distance;
        private string _prefabName;

        public RenderTexture Texture => _rt;
        public bool HasBook => _book != null;

        public static FiresBookView Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("FiresBookView");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<FiresBookView>();
            _instance.CreateCamera();
            return _instance;
        }

        /// <summary>Load + stage a book prefab from the firesbooks bundle. Default = the animated opening book.</summary>
        public bool Show(string prefabName = "FiresBook_Opening")
        {
            Clear();
            var prefab = LoadPrefab(prefabName);
            if (prefab == null) { Debug.LogError("[FiresBookView] prefab not in booksBundle: " + prefabName); return false; }
            _prefabName = prefabName;

            Vector3 basePos = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            Vector3 spawn = new Vector3(basePos.x, basePos.y + 3000f, basePos.z);

            _book = Instantiate(prefab, spawn, Quaternion.Euler(BookEuler));
            _book.name = prefabName + "_bookview";
            _book.SetActive(true);
            foreach (var lod in _book.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);
            foreach (var r in _book.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            var anim = _book.GetComponentInChildren<Animator>();
            if (anim != null) { anim.enabled = true; anim.Rebind(); anim.Update(0f); anim.speed = Mathf.Max(0.1f, OpenSpeed); }

            FixShaders(_book);   // re-point bundled materials to the live in-game shaders (bundle variant strip → magenta)
            EnsureLight(spawn);  // dedicated fill light so the book reads leather/parchment regardless of game time/weather

            _bounds = CalcBounds(_book, spawn);
            _focus = _bounds.center;
            _distance = Mathf.Max(_bounds.size.magnitude * DistanceMul, 0.15f);

            EnsureRT();
            if (_camera != null)
            {
                _camera.backgroundColor = Background;
                _camera.nearClipPlane = Mathf.Max(_distance * 0.01f, 0.001f);
                _camera.farClipPlane = _distance * 12f;
                _camera.enabled = true;   // continuous render so the open animation plays live
            }
            ApplyCamera();
            StopAllCoroutines();
            if (DebugCapture)
                StartCoroutine(AutoCapture());   // dev tool (gated): dump render PNGs + a state file to tune framing
            return true;
        }

        private static string CaptureDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug, "BookView");

        private IEnumerator AutoCapture()
        {
            // Snapshot at a few points across the 8.3s loop so the whole open motion is visible.
            float[] times = { 1.0f, 3.5f, 6.5f };
            for (int i = 0; i < times.Length; i++)
            {
                yield return new WaitForSeconds(i == 0 ? times[0] : times[i] - times[i - 1]);
                yield return new WaitForEndOfFrame();
                Capture(i + 1);
            }
        }

        /// <summary>Reads the RenderTexture to a PNG + writes a state .txt (bounds/orientation/animator/materials).</summary>
        public void Capture(int idx)
        {
            try
            {
                string captureDir = CaptureDir;
                Directory.CreateDirectory(captureDir);
                if (_camera != null) _camera.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                byte[] png = EncodePng(tex);
                Destroy(tex);
                if (png != null) File.WriteAllBytes(Path.Combine(captureDir, $"capture_{idx}.png"), png);
                File.WriteAllText(Path.Combine(captureDir, $"state_{idx}.txt"), StateString(idx));
                Debug.Log($"[FiresBookView] captured #{idx} -> {captureDir}");
            }
            catch (Exception e) { Debug.LogWarning("[FiresBookView] capture failed: " + e.Message); }
        }

        private string StateString(int idx)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# capture {idx}  prefab={_prefabName}");
            sb.AppendLine($"bounds center={_bounds.center} size={_bounds.size}");
            sb.AppendLine($"distance={_distance:0.000} BookEuler={BookEuler} Yaw={Yaw} Pitch={Pitch} DistanceMul={DistanceMul} HeightMul={HeightMul}");
            if (_camera != null)
                sb.AppendLine($"camera pos={_camera.transform.position} rot={_camera.transform.eulerAngles} fov={_camera.fieldOfView} rt={_rt.width}x{_rt.height}");
            var anim = _book != null ? _book.GetComponentInChildren<Animator>() : null;
            if (anim != null)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                sb.AppendLine($"animator enabled={anim.enabled} speed={anim.speed} normalizedTime={st.normalizedTime:0.000} clipHash={st.fullPathHash} hasController={(anim.runtimeAnimatorController != null)}");
            }
            else sb.AppendLine("animator: NONE");
            if (_book != null)
                foreach (var r in _book.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine($"rend {r.name} enabled={r.enabled} boundsSize={r.bounds.size} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "null")} shader={(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "?")} supported={(r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.isSupported)} tex={(r.sharedMaterial != null && r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : "none")}");
            sb.AppendLine("shaderFix:\n" + _lastShaderLog);
            return sb.ToString();
        }

        private static byte[] EncodePng(Texture2D tex)
        {
            try
            {
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var m = ic?.GetMethod("EncodeToPNG", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(Texture2D) }, null);
                return m != null ? (byte[])m.Invoke(null, new object[] { tex }) : null;
            }
            catch { return null; }
        }

        /// <summary>Stop the open animation on its current (fully-open) frame so it holds rather than looping closed.</summary>
        public void Freeze()
        {
            var anim = _book != null ? _book.GetComponentInChildren<Animator>() : null;
            if (anim != null) anim.speed = 0f;
        }

        public void Clear()
        {
            if (_book != null) { Destroy(_book); _book = null; }
            if (_camera != null) _camera.enabled = false;
            if (_light != null) _light.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_camera != null && _camera.enabled) ApplyCamera();
        }

        private void OnDestroy()
        {
            Clear();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_camera != null) Destroy(_camera.gameObject);
            if (_instance == this) _instance = null;
        }

        private void CreateCamera()
        {
            var camGo = new GameObject("FiresBookViewCam");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            _camera.fieldOfView = 35f;
            _camera.depth = -12;
            _camera.enabled = false;
            _camera.cullingMask = ~0;   // book is 3000m up, nothing else is there
            EnsureRT();
            _camera.targetTexture = _rt;
        }

        private void EnsureRT()
        {
            if (_rt != null && _rt.IsCreated()) return;
            _rt = new RenderTexture(RTWidth, RTHeight, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _rt.Create();
            if (_camera != null) _camera.targetTexture = _rt;
        }

        private void ApplyCamera()
        {
            if (_camera == null) return;
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 dir = rot * Vector3.forward;
            Vector3 target = _focus + Vector3.up * (_bounds.size.magnitude * HeightMul);
            _camera.transform.position = target - dir * _distance;
            _camera.transform.LookAt(target);
        }

        // The book materials use our CUSTOM Custom/FiresPiece shader. A bundle-baked shader can lose variants and
        // render magenta, so prefer the LIVE Custom/FiresPiece that FiresTossinShade loaded (full variants). NEVER
        // fall back to vanilla Custom/Piece — this is our shader. If the live one isn't available we keep the
        // material's bundled Custom/FiresPiece and log it so it's obvious FTS didn't provide the shader.
        private const string CustomPieceShader = "Custom/FiresPiece";
        private static void FixShaders(GameObject go)
        {
            var live = Shader.Find(CustomPieceShader);
            bool haveLive = live != null && live.isSupported;
            var log = new StringBuilder();
            log.AppendLine($"live '{CustomPieceShader}' found={(live != null)} supported={(live != null && live.isSupported)}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string before = m.shader != null ? m.shader.name : "null";
                    bool beforeSup = m.shader != null && m.shader.isSupported;
                    if (haveLive && m.shader != live) m.shader = live;
                    // Matte + always-dry so the leather/parchment albedo reads instead of blowing out under the
                    // bright sky reflection (FiresPiece is a wet/PBR piece shader).
                    // 0.0.562 ([worldgen] baked Norse cover): a material with a baked metallic map keeps its own
                    // metallic/gloss and a white _Color (the albedo is final), so metal corners and the clasp stay bright.
                    bool baked = (m.HasProperty("_MetallicTex") && m.GetTexture("_MetallicTex") != null) || (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null);   // FiresPiece: _MetallicTex
                    if (!baked && m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
                    if (!baked && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_FiresWetScale")) m.SetFloat("_FiresWetScale", 0f);
                    if (baked) { if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white); }
                    else if (m.HasProperty("_Color"))
                    {
                        // Parchment keeps the neutral MatTint; the leather cover gets a red-brown tint so it reads as leather, not grey.
                        string nm = (r.name + "/" + m.name).ToLowerInvariant();
                        bool isParchment = nm.Contains("parch") || nm.Contains("paper") || nm.Contains("page");
                        bool isLeather = !isParchment && (nm.Contains("leather") || nm.Contains("cover") || nm.Contains("hide") || nm.Contains("bind") || nm.Contains("spine"));
                        m.SetColor("_Color", isLeather ? LeatherTint : MatTint);
                    }
                    Debug.Log($"[FiresBookView][mat] {r.name}/{m.name} tex={(m.mainTexture != null ? m.mainTexture.name : "none")} baked={baked}");
                    log.AppendLine($"  {r.name}/{m.name}: {before} supported={beforeSup} -> {(m.shader != null ? m.shader.name : "null")} supported={(m.shader != null && m.shader.isSupported)}");
                }
            }
            _lastShaderLog = log.ToString();
            if (DebugCapture) Debug.Log("[FiresBookView] shader fix:\n" + log);
        }

        private GameObject _light;
        private void EnsureLight(Vector3 near)
        {
            if (_light == null)
            {
                _light = new GameObject("FiresBookViewLight");
                UnityEngine.Object.DontDestroyOnLoad(_light);
                var l = _light.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 8f;
                l.intensity = 1.2f;
                l.color = new Color(1f, 0.97f, 0.9f);
            }
            // Sit above-and-front of the book (book is 3000m up, alone, so this only lights the book).
            _light.transform.position = near + new Vector3(0f, 1.2f, -0.8f);
            _light.SetActive(true);
        }

        private static string _lastShaderLog = "";

        private static GameObject LoadPrefab(string name)
        {
            var bundle = FiresRPGmaker.booksBundle;
            if (bundle == null) return null;
            foreach (var an in bundle.GetAllAssetNames())
                if (an.EndsWith("/" + name.ToLowerInvariant() + ".prefab", System.StringComparison.OrdinalIgnoreCase))
                    return bundle.LoadAsset<GameObject>(an);
            return null;
        }

        private static Bounds CalcBounds(GameObject go, Vector3 fallback)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            Bounds? b = null;
            foreach (var r in rs)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds; else { var v = b.Value; v.Encapsulate(r.bounds); b = v; }
            }
            if (b == null) return new Bounds(fallback, Vector3.one);
            var res = b.Value;
            if (res.size.sqrMagnitude < 1e-4f) res.size = Vector3.one;
            return res;
        }
    }
}
