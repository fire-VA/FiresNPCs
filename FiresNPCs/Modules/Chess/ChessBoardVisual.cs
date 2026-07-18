using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Makes the placed chess board a LIVING board: watches the game state stored in its ZDO (written by the chess UI
    /// after every move) and drives the baked Lewis piece children to match — sliding movers, hiding the fallen. The
    /// square grid is derived from the baked start-position children themselves (corner rooks span the grid exactly),
    /// so it is correct at ANY scale the piece has been stretched to. State syncs via ZDO, so every nearby player
    /// watches the game live, and a game in progress persists with the world save.
    /// </summary>
    public sealed class ChessBoardVisual : MonoBehaviour
    {
        public const string ZdoKey = "fires_chess";

        private ZNetView _nview;
        private Transform _set;
        private readonly Dictionary<string, List<Transform>> _byKind = new Dictionary<string, List<Transform>>();
        private readonly Dictionary<Transform, float> _baseY = new Dictionary<Transform, float>();
        private Vector3 _origin, _fileStep, _rankStep;
        private bool _ready;
        private string _applied;
        private float _poll;

        private static readonly string[] Codes = { "", "pawn", "knight", "bishop", "rook", "queen", "king" };

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _set = transform.Find("ChessSet");
            if (_set == null) { enabled = false; return; }

            foreach (Transform child in _set)
            {
                var n = child.name;
                bool white = n.StartsWith("w_");
                bool black = n.StartsWith("b_");
                if (!white && !black) continue;
                int us = n.IndexOf('_', 2);
                if (us < 0) continue;
                string kind = (white ? "w_" : "b_") + n.Substring(2, us - 2);
                if (!_byKind.TryGetValue(kind, out var list)) { list = new List<Transform>(); _byKind[kind] = list; }
                list.Add(child);
                _baseY[child] = child.localPosition.y;
            }

            // Grid from the baked corners: a1/h1 white rooks + a8 black rook span the whole 8×8.
            var a1 = FindAt("w_rook_00"); var h1 = FindAt("w_rook_70"); var a8 = FindAt("b_rook_07");
            if (a1 == null || h1 == null || a8 == null) { enabled = false; return; }
            _origin = a1.localPosition; _origin.y = 0f;
            var p70 = h1.localPosition; p70.y = 0f;
            var p07 = a8.localPosition; p07.y = 0f;
            _fileStep = (p70 - _origin) / 7f;
            _rankStep = (p07 - _origin) / 7f;
            _ready = true;
        }

        private Transform FindAt(string name)
        {
            foreach (Transform child in _set) if (child.name == name) return child;
            return null;
        }

        // ── grid exposure for the 3D seat (world-space, live transforms — giant scaled boards included) ─────────
        internal bool GridReady => _ready;
        internal Vector3 WorldSquareCenter(int sq) => _set.TransformPoint(SquarePos(sq, null));
        internal Vector3 WorldBoardCenter => _set.TransformPoint(_origin + (_fileStep + _rankStep) * 3.5f);
        internal Vector3 WorldUp => _set.up;
        internal Vector3 WorldMySideDir(bool white)
        {
            var d = _set.TransformVector(_rankStep) * (white ? -1f : 1f);
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }
        internal float WorldSquareSize => _set.TransformVector(_fileStep).magnitude;
        internal float WorldBoardRadius => (_set.TransformVector(_fileStep) + _set.TransformVector(_rankStep)).magnitude * 4.35f;

        internal bool RayToSquare(Ray ray, out int sq)
        {
            sq = -1;
            if (!_ready) return false;
            var plane = new Plane(WorldUp, _set.TransformPoint(_origin));
            if (!plane.Raycast(ray, out float d)) return false;
            var local = _set.InverseTransformPoint(ray.GetPoint(d)) - _origin;
            local.y = 0f;
            float f = Vector3.Dot(local, _fileStep) / _fileStep.sqrMagnitude;
            float r = Vector3.Dot(local, _rankStep) / _rankStep.sqrMagnitude;
            int fi = Mathf.RoundToInt(f), ri = Mathf.RoundToInt(r);
            if (fi < 0 || fi > 7 || ri < 0 || ri > 7) return false;
            sq = ChessBoard.Sq(fi, ri);
            return true;
        }

        private void Update()
        {
            if (!_ready || _nview == null || !_nview.IsValid()) return;
            _poll += Time.deltaTime;
            if (_poll < 0.4f) return;
            _poll = 0f;

            string state = _nview.GetZDO().GetString(ZdoKey, "");
            if (state == _applied) return;
            _applied = state;
            Apply(state);
        }

        private void Apply(string state)
        {
            var b = new ChessBoard();
            if (!string.IsNullOrEmpty(state) && !ChessBoard.Deserialize(state, b)) return;   // empty = start position

            var used = new HashSet<Transform>();
            var wanted = new List<(int sq, string kind)>();
            for (int sq = 0; sq < 64; sq++)
            {
                int code = b.Squares[sq];
                if (code == 0) continue;
                var type = ChessBoard.TypeOf(code);
                wanted.Add((sq, (ChessBoard.ColorOf(code) == ChessColor.White ? "w_" : "b_") + Codes[(int)type]));
            }

            // Pass 1 — keep pieces already standing on their square (stable, no shuffling).
            var remaining = new List<(int sq, string kind)>();
            foreach (var w in wanted)
            {
                var t = ChildOnSquare(w.kind, w.sq, used);
                if (t != null) used.Add(t);
                else remaining.Add(w);
            }

            // Pass 2 — march free pieces of the kind (else borrow any hidden same-side piece, e.g. promotions).
            foreach (var w in remaining)
            {
                var t = FreeChild(w.kind, used) ?? FreeChild(w.kind.Substring(0, 2), used, prefixOnly: true);
                if (t == null) continue;
                used.Add(t);
                t.gameObject.SetActive(true);
                StartCoroutine(Slide(t, SquarePos(w.sq, t)));
            }

            var fallen = new List<Transform>();
            foreach (var list in _byKind.Values)
                foreach (var t in list)
                    if (!used.Contains(t) && t.gameObject.activeSelf)
                    {
                        fallen.Add(t);
                        t.gameObject.SetActive(false);   // captured
                    }

            // Exactly one piece falling = a capture — shatter it for everyone watching. The very first Apply
            // (area load resuming a saved game) and bulk resyncs hide many at once and stay silent.
            if (fallen.Count == 1 && _appliedOnce)
                ChessBreakFx.Spawn(fallen[0].position, WorldSquareSize);
            _appliedOnce = true;
        }

        private bool _appliedOnce;

        private Transform ChildOnSquare(string kind, int sq, HashSet<Transform> used)
        {
            if (!_byKind.TryGetValue(kind, out var list)) return null;
            var target = SquarePos(sq, null);
            foreach (var t in list)
            {
                if (used.Contains(t) || !t.gameObject.activeSelf) continue;
                var p = t.localPosition; p.y = 0f;
                var q = target; q.y = 0f;
                if ((p - q).sqrMagnitude < _fileStep.sqrMagnitude * 0.2f) return t;
            }
            return null;
        }

        private Transform FreeChild(string kind, HashSet<Transform> used, bool prefixOnly = false)
        {
            foreach (var kv in _byKind)
            {
                if (prefixOnly ? !kv.Key.StartsWith(kind) : kv.Key != kind) continue;
                foreach (var t in kv.Value)
                    if (!used.Contains(t)) return t;
            }
            return null;
        }

        private Vector3 SquarePos(int sq, Transform forPiece)
        {
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
            var p = _origin + _fileStep * f + _rankStep * r;
            if (forPiece != null && _baseY.TryGetValue(forPiece, out var y)) p.y = y;
            return p;
        }

        private IEnumerator Slide(Transform t, Vector3 to)
        {
            var from = t.localPosition;
            to.y = from.y;
            float tt = 0f;
            while (tt < 1f && t != null)
            {
                tt += Time.deltaTime / 0.5f;
                t.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tt)));
                yield return null;
            }
            if (t != null) t.localPosition = to;
        }
    }
}
