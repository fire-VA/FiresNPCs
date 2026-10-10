using HarmonyLib;
using UnityEngine;
using FiresCore.UI;
using FiresCore.UI.ContextMenu;

namespace FiresRPGmaker.UI.ContextMenu
{
    using Input = UnityEngine.Input;

    /// <summary>
    /// Anchor move — the FiresDungeonMaster blueprint-place pattern, launched from the orb's context
    /// menu: the player's Hammer is equipped, camera + movement stay FREE (walk around to line it
    /// up), and a ghost of the ACTUAL NPC (visual snapshot of the live body) rides the CROSSHAIR
    /// with the golden orb at its feet. SCROLL rotates in build-piece steps (the hammer's own build
    /// is suppressed, so the wheel is ours), PageUp/PageDown nudge height (Shift = large steps),
    /// LEFT-CLICK places (admin-gated server RPC re-homes anchor + body + saved record with the
    /// chosen rotation), RIGHT-CLICK / Esc cancels, Keypad0/Home resets height.
    ///
    /// CRITICAL handover detail (the v268 freeze): when a sub-mode sets
    /// <see cref="FiresContextMenu.SuppressDriver"/>, the context-menu DRIVER hands its InputBlock to
    /// the sub-mode — "the sub-mode owns it now and releases it on exit" (FiresContextMenuDriver:61).
    /// The patrol grab keeps that block ON deliberately (frozen-character UX) and releases it on End;
    /// THIS mode wants free movement, so Begin releases the handed-over block immediately — miss that
    /// call and the character stays input-locked forever (the alt-F4 freeze).
    /// </summary>
    public sealed class NpcAnchorGrabController : MonoBehaviour
    {
        public static NpcAnchorGrabController Instance { get; private set; }
        public static bool IsActive => Instance != null;

        private const float StepSmall = 0.5f, StepLarge = 2f;
        private const float VisualLift = 0.35f;
        private const float RotateStepDegrees = 22.5f;   // vanilla build-piece rotation step

        private static readonly int PlacementMask =
            LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle", "Default_small");

        private string _guid;
        private GameObject _preview;      // root: orb at feet + NPC visual snapshot
        private Vector3 _aim, _pos;
        private float _height;
        private float _yaw;
        private bool _armed;   // wait for the menu click to release before a click can place

        public static void Begin(string guid)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return; // headless
            if (string.IsNullOrEmpty(guid) || Player.m_localPlayer == null) return;
            if (Instance != null) Instance.End();

            var go = new GameObject("FiresNpcAnchorGrab");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NpcAnchorGrabController>();
            Instance._guid = guid;
            Instance._aim = NpcAnchorOrbViz.IsShowing && NpcAnchorOrbViz.ActiveGuid == guid
                ? NpcAnchorOrbViz.CurrentWorld - Vector3.up * VisualLift
                : Player.m_localPlayer.transform.position;

            FiresContextMenu.Close();
            FiresContextMenu.SuppressDriver = true;   // driver hands us its input gate...
            InputBlock.Block(false);                  // ...which we release: free camera + movement (DM UX)
            EquipHammer();                            // the placing left-click swings the build tool

            // The hammer keeps whatever ghost its last selected piece built — a workbench floating
            // beside our anchor preview reads as "the mover shows the last thing on the hammer".
            // Destroy it; our UpdatePlacement suppressor keeps vanilla from rebuilding it while
            // grabbing, and vanilla recreates it on the first UpdatePlacement after End.
            var player = Player.m_localPlayer;
            if (player != null && player.m_placementGhost != null)
                Destroy(player.m_placementGhost);

            Instance.BuildPreview();
            Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                "aim + left-click to place NPC · scroll rotate · PageUp/PageDown height · right-click / Esc cancel");
        }

        // Equip the Hammer from the player's inventory so a left-click while grabbing swings the
        // build tool (which our UpdatePlacement suppressor eats) instead of punching. No-op without a hammer.
        private static void EquipHammer()
        {
            var player = Player.m_localPlayer;
            var inv = player != null ? player.GetInventory() : null;
            var hammer = inv != null ? inv.GetItem("$item_hammer") : null;
            if (hammer != null && player.GetRightItem() != hammer)
                player.EquipItem(hammer);
        }

        private void BuildPreview()
        {
            _preview = new GameObject("FiresNpcAnchorGhost");

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "AnchorOrb";
            orb.transform.SetParent(_preview.transform, false);
            orb.transform.localPosition = Vector3.up * VisualLift;
            orb.transform.localScale = Vector3.one * 0.5f;
            var col = orb.GetComponent<Collider>(); if (col != null) Destroy(col);
            var mr = orb.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(sh) { color = new Color(1f, 0.78f, 0.25f, 0.5f) };
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Ghost of the ACTUAL NPC: visual-only snapshot of the live anchored body (it stands
            // right next to its orb whenever this mode is reachable). Also seeds the current yaw so
            // rotation starts from how the NPC faces now. Falls back to orb-only when unloaded.
            var body = FindLiveBody(_guid);
            if (body != null)
            {
                _yaw = body.transform.rotation.eulerAngles.y;
                var snapshot = Managers.NpcTemplateAppearance.BuildVisualSnapshot(body);
                if (snapshot != null)
                {
                    snapshot.name = "NpcGhost";
                    int ghostLayer = LayerMask.NameToLayer("ghost");
                    if (ghostLayer >= 0)
                        foreach (var t in snapshot.GetComponentsInChildren<Transform>(true))
                            t.gameObject.layer = ghostLayer;
                    snapshot.transform.SetParent(_preview.transform, false);
                    snapshot.transform.localPosition = Vector3.zero;
                    snapshot.transform.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>The loaded body owned by this anchor (matched by the synced guid back-ref).</summary>
        private static GameObject FindLiveBody(string guid)
        {
            try
            {
                foreach (var module in FindObjectsByType<FiresCore.Npc.NpcMode.CompanionNpcModule>(FindObjectsSortMode.None))
                {
                    if (module == null) continue;
                    var nview = module.GetComponent<ZNetView>();
                    var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                    if (zdo == null) continue;
                    if (zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "") == guid)
                        return module.gameObject;
                }
            }
            catch { }
            return null;
        }

        private void Update()
        {
            if (Player.m_localPlayer == null) { End(); return; }

            // Aim from the crosshair (camera-forward ray) like the hammer's place-ray — camera stays free.
            var cam = Camera.main;
            if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 3000f, PlacementMask, QueryTriggerInteraction.Ignore))
                _aim = hit.point;

            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? StepLarge : StepSmall;
            if (!FiresCore.Input.FiresInputBlock.IsTyping()) // 0.0.550 typing gate (Fire 10-09)
            {
                if (Input.GetKeyDown(KeyCode.PageUp)) _height += step;
                if (Input.GetKeyDown(KeyCode.PageDown)) _height -= step;
                if (Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Home)) _height = 0f;
            }

            // Build-piece rotation on the wheel (vanilla camera zoom is already off in place mode,
            // and our UpdatePlacement suppressor keeps vanilla from consuming the scroll itself).
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0.0001f) _yaw += RotateStepDegrees;
            else if (scroll < -0.0001f) _yaw -= RotateStepDegrees;

            _pos = _aim + Vector3.up * _height;
            if (_preview != null)
            {
                _preview.transform.position = _pos;
                _preview.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            if (!Input.GetMouseButton(0)) _armed = true;
            if (_armed && Input.GetMouseButtonDown(0)) { Place(); return; }
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { End(); return; }
        }

        private void Place()
        {
            Managers.SavedNpcManager.RequestAnchorMove(_guid, _pos, _yaw);
            if (NpcAnchorOrbViz.IsShowing && NpcAnchorOrbViz.ActiveGuid == _guid)
                NpcAnchorOrbViz.PreviewWorld(_pos + Vector3.up * VisualLift);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Anchor placed — the NPC re-homes there within a few seconds.");
            End();
        }

        private void End()
        {
            if (_preview != null) Destroy(_preview);
            _preview = null;
            FiresContextMenu.SuppressDriver = false;
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        // While grabbing, suppress the hammer's own placement so the placing left-click drops OUR
        // anchor, not whatever piece the hammer had selected.
        [HarmonyPatch(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) })]
        private static class Player_UpdatePlacement_GrabBlock
        {
            private static bool Prefix() => Instance == null;
        }
    }
}
