using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.UI.Screens.PlayerPanels.Minigames;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// The 3D seat: play the placed board itself instead of the window. The camera locks above the real pieces
    /// (fit to the whole board at any scale — tabletop or arena — with scroll zoom), clicks raycast onto real
    /// squares, legal moves appear as the patrol-orb hint stones (gold = move, red = take), hovering a piece names
    /// it, and the translucent edge panels carry the move log / trays / status from the same controller that runs
    /// the window game. The physical pieces are already the living board — this just lets you reach into it.
    /// </summary>
    public sealed class Chess3DSession : MonoBehaviour
    {
        internal static Chess3DSession Active;

        private ZNetView _board;
        private ChessBoardVisual _visual;
        private ChessTableController _ctrl;
        private Action _endRequest;
        private bool _whiteSide;

        private float _zoom = 1f;
        private readonly List<Transform> _orbs = new List<Transform>();
        private Material _matMove, _matTake, _matCheck;
        private int _orbsShown;

        private GameObject _labelCanvas;
        private RectTransform _labelRt;
        private TextMeshProUGUI _labelText;
        private Image _labelBg;

        internal static Chess3DSession Begin(ZNetView board, ChessBoardVisual visual, ChessTableController ctrl,
                                             bool whiteSide, Action endRequest)
        {
            var go = new GameObject("FiresChess3DSession");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<Chess3DSession>();
            s._board = board;
            s._visual = visual;
            s._ctrl = ctrl;
            s._whiteSide = whiteSide;
            s._endRequest = endRequest;
            s.BuildLabel();
            Active = s;
            FiresCore.UI.MiniGameCamera.Begin(s.BoardPose, ease: 9f, tuneKey: "chess");
            return s;
        }

        internal void End()
        {
            if (Active == this) Active = null;
            FiresCore.UI.MiniGameCamera.End();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Active == this) { Active = null; FiresCore.UI.MiniGameCamera.End(); }
            if (_matMove != null) Destroy(_matMove);
            if (_matTake != null) Destroy(_matTake);
            if (_matCheck != null) Destroy(_matCheck);
            if (_labelCanvas != null) Destroy(_labelCanvas);
        }

        // ── per-frame ──────────────────────────────────────────────────────────────────────────────────────────
        private void Update()
        {
            var player = Player.m_localPlayer;
            if (_ctrl == null || _board == null || !_board.IsValid() || _visual == null || !_visual.GridReady ||
                player == null || player.IsDead() ||
                Vector3.Distance(player.transform.position, _visual.WorldBoardCenter) > _visual.WorldBoardRadius + 14f)
            {
                _endRequest?.Invoke();
                _endRequest = null;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { _endRequest?.Invoke(); _endRequest = null; return; }

            // Hold Ctrl+Alt to reposition the board camera — skip the board's own zoom/clicks so the drag and wheel
            // drive the tuner instead of moving pieces.
            if (FiresCore.UI.MiniGameCamera.IsTuning) { HideLabel(); return; }

            bool overUi = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            if (!overUi)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) _zoom = Mathf.Clamp(_zoom * (1f - wheel * 0.08f), 0.55f, 2.1f);
            }

            UpdateHoverAndClicks(overUi);
        }

        private void UpdateHoverAndClicks(bool overUi)
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.m_camera : Camera.main;
            int hoverSq = -1;
            if (cam != null && !overUi)
            {
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                if (!_visual.RayToSquare(ray, out hoverSq)) hoverSq = -1;
            }

            int code = hoverSq >= 0 ? _ctrl.Game.Squares[hoverSq] : 0;
            if (code != 0)
            {
                bool white = ChessBoard.ColorOf(code) == ChessColor.White;
                ShowLabel((white ? "White " : "Black ") + KindName(ChessBoard.TypeOf(code)) + " — " + ChessBoard.SquareName(hoverSq));
            }
            else if (hoverSq >= 0 && _ctrl.SelectedSq >= 0 && IsLegalTarget(hoverSq, out bool take))
                ShowLabel(take ? "Take — " + ChessBoard.SquareName(hoverSq) : "Move — " + ChessBoard.SquareName(hoverSq));
            else HideLabel();

            if (!overUi && Input.GetMouseButtonDown(0) && hoverSq >= 0)
                _ctrl.ClickSquare(hoverSq);
            if (!overUi && Input.GetMouseButtonDown(1) && _ctrl.SelectedSq >= 0)
                _ctrl.ClickSquare(_ctrl.SelectedSq);   // re-click deselects
        }

        private bool IsLegalTarget(int sq, out bool take)
        {
            take = false;
            foreach (var m in _ctrl.LegalNow)
                if (m.From == _ctrl.SelectedSq && m.To == sq)
                {
                    take = _ctrl.Game.Squares[sq] != 0 || m.IsEnPassant;
                    return true;
                }
            return false;
        }

        private static string KindName(ChessPiece t)
        {
            switch (t)
            {
                case ChessPiece.Pawn: return "Pawn";
                case ChessPiece.Knight: return "Knight";
                case ChessPiece.Bishop: return "Bishop";
                case ChessPiece.Rook: return "Rook";
                case ChessPiece.Queen: return "Queen";
                case ChessPiece.King: return "King";
                default: return "Piece";
            }
        }

        // ── hint stones (the patrol-orb recipe: unlit spheres, no colliders, no shadows) ───────────────────────
        internal void OnStateChanged()
        {
            EnsureMats();
            _orbsShown = 0;
            var game = _ctrl.Game;

            if (!_ctrl.OverNow && _ctrl.SelectedSq >= 0)
                foreach (var m in _ctrl.LegalNow)
                    if (m.From == _ctrl.SelectedSq)
                    {
                        bool take = game.Squares[m.To] != 0 || m.IsEnPassant;
                        PlaceOrb(m.To, take ? _matTake : _matMove, take ? 1.25f : 1f);
                    }

            if (!_ctrl.OverNow && ChessMoveGen.InCheck(game, game.SideToMove))
                PlaceOrb(game.KingSquare(game.SideToMove), _matCheck, 1.45f);

            for (int i = _orbsShown; i < _orbs.Count; i++)
                if (_orbs[i] != null) _orbs[i].gameObject.SetActive(false);
        }

        private void PlaceOrb(int sq, Material mat, float scale)
        {
            Transform orb;
            if (_orbsShown < _orbs.Count) orb = _orbs[_orbsShown];
            else
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "ChessHintOrb";
                s.transform.SetParent(transform, false);
                var col = s.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mr = s.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _orbs.Add(s.transform);
                orb = s.transform;
            }
            _orbsShown++;

            float size = _visual.WorldSquareSize * 0.22f * scale;
            orb.gameObject.SetActive(true);
            orb.position = _visual.WorldSquareCenter(sq) + _visual.WorldUp * size * 0.9f;
            orb.localScale = Vector3.one * size;
            orb.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void EnsureMats()
        {
            if (_matMove != null) return;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _matMove = new Material(shader) { color = new Color(0.98f, 0.84f, 0.42f) };
            _matTake = new Material(shader) { color = new Color(0.95f, 0.32f, 0.22f) };
            _matCheck = new Material(shader) { color = new Color(1f, 0.12f, 0.08f) };
        }

        // ── hover label ────────────────────────────────────────────────────────────────────────────────────────
        private void BuildLabel()
        {
            _labelCanvas = new GameObject("FiresChess3DLabel");
            _labelCanvas.transform.SetParent(transform, false);
            var canvas = _labelCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6510;
            var scaler = _labelCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("Tag", typeof(RectTransform));
            go.transform.SetParent(_labelCanvas.transform, false);
            _labelBg = go.AddComponent<Image>();
            _labelBg.color = new Color(0.07f, 0.05f, 0.04f, 0.85f);
            _labelBg.raycastTarget = false;
            _labelRt = _labelBg.rectTransform;
            _labelRt.pivot = new Vector2(0f, 1f);
            _labelRt.sizeDelta = new Vector2(210f, 30f);

            var tgo = new GameObject("Txt", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            _labelText = tgo.AddComponent<TextMeshProUGUI>();
            _labelText.fontSize = 15f;
            _labelText.color = new Color(0.96f, 0.9f, 0.75f);
            _labelText.alignment = TextAlignmentOptions.Center;
            _labelText.raycastTarget = false;
            _labelText.enableWordWrapping = false;
            var trt = _labelText.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var font = FiresCore.UI.UIFontConfig.GetFont(FiresCore.UI.UIFontConfig.FontStyle.AveriaLibre);
            if (font != null) _labelText.font = font;
            _labelBg.gameObject.SetActive(false);
        }

        private void ShowLabel(string text)
        {
            if (_labelBg == null) return;
            _labelBg.gameObject.SetActive(true);
            _labelText.text = text;
            _labelRt.sizeDelta = new Vector2(Mathf.Max(120f, _labelText.preferredWidth + 26f), 30f);
            _labelRt.position = Input.mousePosition + new Vector3(20f, -16f, 0f);
        }

        private void HideLabel()
        {
            if (_labelBg != null) _labelBg.gameObject.SetActive(false);
        }

        // ── camera: the shared FiresCore.UI.MiniGameCamera drives the shot (Valcast technique; InputBlock's pin
        // yields to it). We hand it a pose each frame that fits the whole board from my side at a fixed FOV, tilted
        // toward my seat, scaled by the scroll zoom. ─────────────────────────────────────────────────────────────
        private const float BoardFov = 55f;

        private FiresCore.UI.MiniGameCamera.Pose? BoardPose()
        {
            if (_visual == null || !_visual.GridReady) return null;
            var cam = GameCamera.instance != null ? GameCamera.instance.m_camera : null;
            if (cam == null) return null;

            var center = _visual.WorldBoardCenter;
            var up = _visual.WorldUp;
            var side = _visual.WorldMySideDir(_whiteSide);
            float vFov = BoardFov * Mathf.Deg2Rad;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * cam.aspect);
            float fit = Mathf.Min(vFov, hFov);
            float dist = _visual.WorldBoardRadius / Mathf.Sin(fit * 0.5f) * _zoom;

            const float tilt = 27f * Mathf.Deg2Rad;   // from straight-down, toward my seat — depth without losing the far rank
            var offset = up * Mathf.Cos(tilt) + side * Mathf.Sin(tilt);
            var pos = center + offset * dist;
            return new FiresCore.UI.MiniGameCamera.Pose(pos, Quaternion.LookRotation((center - pos).normalized, side), BoardFov);
        }
    }

    /// <summary>
    /// Client-local capture shatter: clones the vanilla rock-break vfx once (network components stripped so a
    /// cosmetic burst never registers a ZDO), then stamps copies scaled to the board's square size. Vanilla
    /// prefabs carry vanilla shaders — nothing to swap, nothing magenta.
    /// </summary>
    internal static class ChessBreakFx
    {
        private static GameObject _vfx, _sfx;
        private static bool _resolved;

        internal static void Spawn(Vector3 pos, float squareSize)
        {
            if (ZNetScene.instance == null) return;
            Resolve();
            float scale = Mathf.Clamp(squareSize / 0.35f, 0.35f, 6f);
            Stamp(_vfx, pos, scale);
            Stamp(_sfx, pos, 1f);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _vfx = Template(new[] { "vfx_RockDestroyed", "vfx_Rock_destroyed", "vfx_RockHit" });
            _sfx = Template(new[] { "sfx_rock_destroyed", "sfx_rock_hit" });
            if (_vfx == null) Debug.LogWarning("[FiresRPGmaker] chess capture vfx prefab not found — captures stay quiet.");
        }

        private static GameObject Template(string[] candidates)
        {
            foreach (var name in candidates)
            {
                var pf = ZNetScene.instance.GetPrefab(name);
                if (pf == null) continue;
                var clone = FiresCore.Net.NetworkObjectHelper.InstantiateInactiveClone(pf, "fires_chess_" + name);
                if (clone == null) continue;
                foreach (var nv in clone.GetComponentsInChildren<ZNetView>(true)) UnityEngine.Object.DestroyImmediate(nv);
                foreach (var zs in clone.GetComponentsInChildren<ZSyncTransform>(true)) UnityEngine.Object.DestroyImmediate(zs);
                return clone;
            }
            return null;
        }

        private static void Stamp(GameObject template, Vector3 pos, float scale)
        {
            if (template == null) return;
            var go = UnityEngine.Object.Instantiate(template, pos, Quaternion.identity);
            go.transform.localScale = template.transform.localScale * scale;
            go.SetActive(true);
            UnityEngine.Object.Destroy(go, 8f);
        }
    }
}
