using UnityEngine;
using FiresCore.UI.ContextMenu;

namespace FiresRPGmaker.UI
{
    /// <summary>Identifies an NPC anchor orb under the cursor; carried on a ContextTarget.</summary>
    public sealed class NpcAnchorMarker
    {
        public string Guid;
        public Vector3 AnchorPos;
        public string NpcName;
    }

    /// <summary>
    /// Admin-only client visualization of ONE NPC's spawn anchor: a golden orb at the anchor
    /// position (PatrolRouteVisualizer's collider-less sphere pattern), pickable by the shared
    /// hold-Alt right-click context menu via a ray-vs-sphere marker source. Toggled from the NPC
    /// book's "Show Anchor" button; auto-clears on world unload.
    /// </summary>
    public sealed class NpcAnchorOrbViz : MonoBehaviour
    {
        private const float OrbScale = 0.45f;
        private const float PickRadius = 0.6f;
        private static readonly Color OrbColor = new Color(1f, 0.78f, 0.25f, 1f);

        private static NpcAnchorOrbViz _instance;

        private Vector3 _world;
        private string _guid;
        private string _npcName;
        private Transform _orb;
        private FiresContextMenu.MarkerHitTester _tester;

        public static bool IsShowing => _instance != null;
        public static string ActiveGuid => _instance != null ? _instance._guid : null;
        public static Vector3 CurrentWorld => _instance != null ? _instance._world : Vector3.zero;

        /// <summary>Live grab-mode preview: moves the orb without committing anything.</summary>
        public static void PreviewWorld(Vector3 world)
        {
            if (_instance == null) return;
            _instance._world = world;
            if (_instance._orb != null) _instance._orb.position = world;
        }

        /// <summary>Shows the orb for this NPC's anchor (reads the body ZDO), replacing any prior orb.</summary>
        public static void Show(NpcController npc)
        {
            Hide();
            if (npc == null) return;
            var nview = npc.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return;

            string guid = zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "");
            Vector3 anchor = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", npc.transform.position));
            if (string.IsNullOrEmpty(guid))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "This NPC has no anchor yet (old placement) — it gains one on next server load.");
                return;
            }

            var go = new GameObject("FiresNpcAnchorOrbViz");
            _instance = go.AddComponent<NpcAnchorOrbViz>();
            _instance._world = anchor + Vector3.up * 0.35f;
            _instance._guid = guid;
            _instance._npcName = npc.GetDisplayName();
            _instance.BuildOrb();
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Anchor shown — hold Alt + right-click the orb to move/remove it.");
        }

        public static void Hide()
        {
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        public static void Toggle(NpcController npc)
        {
            if (IsShowing) Hide();
            else Show(npc);
        }

        private void BuildOrb()
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "AnchorOrb";
            s.transform.SetParent(transform, false);
            s.transform.position = _world;
            _orb = s.transform;
            s.transform.localScale = Vector3.one * OrbScale;
            var col = s.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = s.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                mr.sharedMaterial = new Material(shader) { color = OrbColor };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _tester = HitTest;
            FiresContextMenu.RegisterMarkerSource(_tester);
        }

        private void Update()
        {
            // World gone (logout/menu) — the marker is world-space and meaningless without it.
            if (ZNetScene.instance == null) Hide();
        }

        private void OnDestroy()
        {
            if (_tester != null) FiresContextMenu.UnregisterMarkerSource(_tester);
            _tester = null;
            if (_instance == this) _instance = null;
        }

        private bool HitTest(Ray ray, out ContextTarget target)
        {
            target = null;
            if (!RaySphere(ray, _world, PickRadius, out float d)) return false;
            target = new ContextTarget
            {
                Point = _world,
                Distance = d,
                Marker = new NpcAnchorMarker { Guid = _guid, AnchorPos = _world - Vector3.up * 0.35f, NpcName = _npcName },
                MarkerKind = "NpcAnchor",
            };
            return true;
        }

        private static bool RaySphere(Ray ray, Vector3 center, float radius, out float dist)
        {
            dist = 0f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t = -b - sq;
            if (t < 0f) t = -b + sq;
            if (t < 0f) return false;
            dist = t;
            return true;
        }
    }
}
