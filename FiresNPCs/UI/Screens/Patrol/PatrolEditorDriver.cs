using UnityEngine;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI.Screens.Patrol
{
    /// <summary>Client-only admin driver: while a patrol route is visualized, <b>Shift+Right-click</b> a node opens
    /// the baked <see cref="PatrolEditorPanel"/> on it (crosshair pick when closed; free-cursor mouse pick when the
    /// book is already open, so Shift+RMB a second node completes a section mark). Replaces the old hold-Alt node
    /// context menu.</summary>
    public class PatrolEditorDriver : MonoBehaviour
    {
        private static PatrolEditorDriver _instance;
        private const float NodePickRadius = 0.6f;

        public static void Ensure()
        {
            if (_instance != null || Application.isBatchMode) return;
            var go = new GameObject("FiresPatrolEditorDriver");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PatrolEditorDriver>();
        }

        private void Update()
        {
            if (global::FiresRPGmaker.UI.ContextMenu.PatrolNodeGrabController.IsActive) return; // a grab owns the cursor

            // Shift+Alt+Left-click a node → pick it straight onto the cursor for mouse-driven placement (skip the UI).
            if (ShiftHeld() && AltHeld() && Input.GetMouseButtonDown(0))
            {
                BeginMouseGrab();
                return;
            }

            // Shift+Right-click a node → open the baked editor on it.
            if (!ShiftHeld() || AltHeld() || !Input.GetMouseButtonDown(1)) return;
            string route = PatrolRouteVisualizer.ActiveRouteName;
            if (string.IsNullOrEmpty(route)) return;   // only while a route is shown
            if (!IsLocalAdmin()) return;

            var cam = Camera.main;
            if (cam == null) return;
            // Closed → aim with the crosshair (locked cursor); open → the free cursor is the pointer.
            Vector3 screenPt = PatrolEditorPanel.IsOpen
                ? Input.mousePosition
                : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            int node = PickNode(cam.ScreenPointToRay(screenPt), route);
            if (node < 0) return;
            PatrolEditorPanel.Open(route, node);
        }

        // Ray-pick a node under the pointer and hand it to the grab controller in mouse mode. Uses the free cursor
        // when the editor is open, else the crosshair. Closes the editor first so the two don't fight the gate.
        private void BeginMouseGrab()
        {
            string route = PatrolRouteVisualizer.ActiveRouteName;
            if (string.IsNullOrEmpty(route) || !IsLocalAdmin()) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 screenPt = PatrolEditorPanel.IsOpen
                ? Input.mousePosition
                : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            int node = PickNode(cam.ScreenPointToRay(screenPt), route);
            if (node < 0) return;
            if (PatrolEditorPanel.IsOpen) PatrolEditorPanel.Close();
            global::FiresRPGmaker.UI.ContextMenu.PatrolNodeGrabController.Begin(route, node, mouse: true);
        }

        private static int PickNode(Ray ray, string route)
        {
            int count = PatrolRouteVisualizer.NodeCount;
            float best = float.MaxValue; int bestIdx = -1;
            for (int i = 0; i < count; i++)
            {
                if (!PatrolRouteVisualizer.TryGetNodeWorld(i, out var w)) continue;
                if (RaySphere(ray, w, NodePickRadius, out float d) && d < best) { best = d; bestIdx = i; }
            }
            return bestIdx;
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

        private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool AltHeld() => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        private static bool IsLocalAdmin() => FiresCore.Sync.AdminSyncing.IsLocalAdmin();
    }
}
