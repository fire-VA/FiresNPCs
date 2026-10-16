using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.Minigames;
using FiresRPGmaker.Modules.Fanorona;

namespace FiresRPGmaker.UI.Screens.PlayerPanels.Minigames
{
    /// <summary>
    /// Fanorona — the Malagasy stone duel on the carved 5×9 board (diced from the studio render, pebbles included).
    /// You play the light stones against Sigrid. Captures are mandatory; landing a capture that can chain keeps your
    /// turn — keep eating lines or press Stop. When a slide could capture by approach AND withdrawal, a chooser asks
    /// which line dies. The AI thinks on a worker thread against a clone of the verified engine.
    /// </summary>
    public static class FanoronaTableGame
    {
        private static Sprite _board, _stoneW, _stoneB, _panel, _bar, _btn, _btnHi;
        private static bool _loaded;

        /// <summary>Fanorona window scale + position (drag the title bar / corner grip in-game). Hot-reloads.</summary>
        public static BepInEx.Configuration.ConfigEntry<float> UiScaleCfg, OffsetXCfg, OffsetYCfg;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Open(int aiDepth = 3)
        {
            if (IsClientOnly()) return;
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresFanorona", 6500);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<FanoronaTableController>();
            ctrl.Build(overlay, Mathf.Clamp(aiDepth, 1, 5));
        }

        internal static Sprite BoardSprite => _board;
        internal static Sprite StoneW => _stoneW;
        internal static Sprite StoneB => _stoneB;
        internal static Sprite Panel => _panel;
        internal static Sprite Bar => _bar;
        internal static Sprite Btn => _btn;
        internal static Sprite BtnHi => _btnHi;

        private static void EnsureAssets()
        {
            if (_loaded) return;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            _board = FiresMinigameSprites.Load(asm, "fanoronaBoard.png");
            _stoneW = FiresMinigameSprites.Load(asm, "fanStoneWhite.png");
            _stoneB = FiresMinigameSprites.Load(asm, "fanStoneBlack.png");
            _panel = FiresRoundedSkin.RoundedSprite(20, new Color(0.11f, 0.088f, 0.06f, 0.99f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _btn = FiresRoundedSkin.RoundedSprite(10, new Color(0.34f, 0.23f, 0.10f, 1f), new Color(0.62f, 0.42f, 0.16f, 0.8f), 1);
            _btnHi = FiresRoundedSkin.RoundedSprite(10, new Color(0.47f, 0.32f, 0.14f, 1f), new Color(0.85f, 0.62f, 0.26f, 1f), 1);
        }
    }

    internal sealed class FanoronaTableController : MonoBehaviour
    {
        private const float CabW = 1040f, CabH = 660f;
        private const float BoardW = 880f, BoardH = 492f;
        // Circle-grid area of the carved board render (fractions of the deskewed/keyed crop; image top-down).
        private const float FX0 = 0.157f, FX1 = 0.837f;
        private const float FY0 = 0.143f, FY1 = 0.859f;

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private FanoronaGame _game;
        private System.Random _rng;
        private int _aiDepth;

        private RectTransform _boardRT, _cabRT;
        private readonly Image[] _stones = new Image[FanoronaGame.Points];
        private readonly Image[] _dots = new Image[10];
        private readonly Image[] _wellB = new Image[22];   // black stones you took — pile in the left well
        private readonly Image[] _wellW = new Image[22];   // white stones Sigrid took — pile in the right well
        private Image _selRing;
        private TMP_Text _statusText, _youTook, _foeTook;
        private Button _stopBtn;
        private GameObject _chooser;
        private int _chFrom = -1, _chTo = -1;

        private int _selected = -1;
        private List<FanMove> _legal = new List<FanMove>();
        private bool _busy, _over;
        private float _lastScale = -1f;
        private Task<object> _aiTask;

        public void Build(FiresMinigameOverlay overlay, int aiDepth)
        {
            _overlay = overlay;
            _aiDepth = aiDepth;
            _rng = new System.Random(unchecked(Environment.TickCount * 23 + 7));
            _audio = new FiresMinigameAudio(Assembly.GetExecutingAssembly());
            _audio.RegisterClip("move1", "FiresPokerFlip1.wav"); _audio.RegisterClip("move2", "FiresPokerFlip2.wav");
            _audio.RegisterClip("take1", "FiresPokerChips1.wav"); _audio.RegisterClip("take2", "FiresPokerChips2.wav");
            _audio.RegisterClip("start", "FiresPokerDeal1.wav");

            _game = new FanoronaGame();

            var root = overlay.PlayField;
            var cab = NewImg(root, "Cabinet", FanoronaTableGame.Panel, Image.Type.Sliced);
            Place(cab.rectTransform, CabW, CabH, 0, 0);
            _cabRT = cab.rectTransform;

            var titleBar = NewImg(cab.rectTransform, "TitleBar", FanoronaTableGame.Bar, Image.Type.Sliced);
            var tb = titleBar.rectTransform; tb.anchorMin = new Vector2(0, 1); tb.anchorMax = new Vector2(1, 1); tb.pivot = new Vector2(0.5f, 1);
            tb.sizeDelta = new Vector2(-24, 44); tb.anchoredPosition = new Vector2(0, -12);
            var title = NewTxt(titleBar.rectTransform, "Fanorona — the Stone Duel", 22, new Color(1f, 0.85f, 0.5f));
            FillRect(title.rectTransform);

            titleBar.raycastTarget = true;
            AddDrag(titleBar.gameObject, d =>
            {
                float sc = Mathf.Max(0.6f, _lastScale);
                if (FanoronaTableGame.OffsetXCfg != null) FanoronaTableGame.OffsetXCfg.Value += d.delta.x / sc;
                if (FanoronaTableGame.OffsetYCfg != null) FanoronaTableGame.OffsetYCfg.Value += d.delta.y / sc;
            });
            var grip = NewImg(cab.rectTransform, "ScaleGrip", FiresRoundedSkin.RoundedSprite(6, new Color(0.85f, 0.62f, 0.26f, 0.55f)), Image.Type.Sliced);
            var griprt = grip.rectTransform;
            griprt.anchorMin = griprt.anchorMax = new Vector2(0f, 0f); griprt.pivot = new Vector2(0f, 0f);
            griprt.sizeDelta = new Vector2(18f, 18f); griprt.anchoredPosition = new Vector2(4f, 4f);
            grip.raycastTarget = true;
            AddDrag(grip.gameObject, d =>
            {
                if (FanoronaTableGame.UiScaleCfg == null) return;
                FanoronaTableGame.UiScaleCfg.Value = Mathf.Clamp(FanoronaTableGame.UiScaleCfg.Value - (d.delta.x + d.delta.y) * 0.003f, 0.6f, 1.6f);
            });

            var board = NewImg(cab.rectTransform, "Board", FanoronaTableGame.BoardSprite, Image.Type.Simple);
            Place(board.rectTransform, BoardW, BoardH, 0, -8);
            _boardRT = board.rectTransform;

            _selRing = NewImg(_boardRT, "Sel", FiresRoundedSkin.RoundedSprite(14, new Color(0f, 0f, 0f, 0f), new Color(0.98f, 0.84f, 0.42f, 0.95f), 3), Image.Type.Sliced);
            Place(_selRing.rectTransform, 58, 58, 0, 0);
            _selRing.gameObject.SetActive(false);

            for (int p = 0; p < FanoronaGame.Points; p++)
            {
                int captured = p;
                var cell = NewImg(_boardRT, "Cell" + p, null, Image.Type.Simple);
                cell.color = new Color(0, 0, 0, 0);
                cell.raycastTarget = true;
                var pos = PointLocal(p);
                Place(cell.rectTransform, 64, 74, pos.x, pos.y);
                var btn = cell.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => OnCell(captured));

                var st = NewImg(_boardRT, "Stone" + p, FanoronaTableGame.StoneW, Image.Type.Simple);
                st.preserveAspect = true;
                Place(st.rectTransform, 52, 52, pos.x, pos.y + 2f);
                st.gameObject.SetActive(false);
                _stones[p] = st;
            }
            for (int i = 0; i < _dots.Length; i++)
            {
                var d = NewImg(_boardRT, "Dot" + i, FiresRoundedSkin.RoundedSprite(9, new Color(0.98f, 0.84f, 0.42f, 0.9f)), Image.Type.Simple);
                Place(d.rectTransform, 20, 20, 0, 0);
                d.gameObject.SetActive(false);
                _dots[i] = d;
            }

            // Captured stones pile up in the carved side wells — jittered + rotated like they were tossed aside.
            for (int i = 0; i < 22; i++)
            {
                _wellB[i] = MakeWellStone(FanoronaTableGame.StoneB, -374f, i, 101);
                _wellW[i] = MakeWellStone(FanoronaTableGame.StoneW, 374f, i, 202);
            }
            _youTook = NewTxt(cab.rectTransform, "× 0", 18, new Color(1f, 0.88f, 0.55f));
            Place(_youTook.rectTransform, 80, 24, -374, -186);
            _foeTook = NewTxt(cab.rectTransform, "× 0", 18, new Color(1f, 0.88f, 0.55f));
            Place(_foeTook.rectTransform, 80, 24, 374, -186);

            _statusText = NewTxt(cab.rectTransform, "Your move — captures are mandatory.", 17, new Color(0.92f, 0.88f, 0.75f));
            Place(_statusText.rectTransform, 620, 26, 0, -292);

            _stopBtn = MakeButton(cab.rectTransform, "Stop Chain", new Vector2(0.5f, 0.5f), new Vector2(140, 40), new Vector2(-430, -292), OnStopChain);
            _stopBtn.gameObject.SetActive(false);
            MakeButton(cab.rectTransform, "New Game", new Vector2(0.5f, 0.5f), new Vector2(140, 40), new Vector2(430, -292), NewGame);
            MakeButton(cab.rectTransform, "Leave", new Vector2(1, 1), new Vector2(96, 34), new Vector2(-62, -31), () => _overlay.Close());

            BuildChooser(cab.rectTransform);

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;

            _audio?.PlayPitched("start", 0.8f, 1f);
            _legal = _game.LegalMoves();
            RefreshBoard();
        }

        // One captured stone in a well — deterministic per-index jitter so the pile never reshuffles between frames.
        private Image MakeWellStone(Sprite sprite, float wellX, int index, int seed)
        {
            var img = NewImg(_boardRT, "Well" + seed + "_" + index, sprite, Image.Type.Simple);
            img.preserveAspect = true;
            var rng = new System.Random(seed * 1009 + index * 7919);
            float x = wellX + (float)(rng.NextDouble() * 14.0 - 7.0);
            float y = -128f + index * 11.5f + (float)(rng.NextDouble() * 6.0 - 3.0);
            Place(img.rectTransform, 30, 30, x, y);
            img.rectTransform.localEulerAngles = new Vector3(0f, 0f, (float)(rng.NextDouble() * 70.0 - 35.0));
            img.gameObject.SetActive(false);
            return img;
        }

        // Engine row 0 (White's home) renders at the BOTTOM; the image fractions are top-down.
        private static Vector2 PointLocal(int p)
        {
            int x = FanoronaGame.PX(p), y = FanoronaGame.PY(p);
            float px = -BoardW / 2f + (FX0 + x * (FX1 - FX0) / 8f) * BoardW;
            float py = -BoardH / 2f + ((1f - FY1) + y * (FY1 - FY0) / 4f) * BoardH;
            return new Vector2(px, py);
        }

        private void RefreshBoard()
        {
            for (int p = 0; p < FanoronaGame.Points; p++)
            {
                int v = _game.Board[p];
                var st = _stones[p];
                if (v == 0) { st.gameObject.SetActive(false); continue; }
                st.gameObject.SetActive(true);
                st.sprite = v == 1 ? FanoronaTableGame.StoneW : FanoronaTableGame.StoneB;
            }

            if (_selected >= 0)
            {
                _selRing.gameObject.SetActive(true);
                _selRing.rectTransform.anchoredPosition = PointLocal(_selected);
            }
            else _selRing.gameObject.SetActive(false);

            int d = 0;
            if (_selected >= 0)
            {
                var seen = new HashSet<int>();
                foreach (var m in _legal)
                    if (!m.IsStop && m.From == _selected && seen.Add(m.To) && d < _dots.Length)
                    {
                        var dot = _dots[d++];
                        dot.gameObject.SetActive(true);
                        dot.color = m.IsCapture || HasCapture(_selected, m.To)
                            ? new Color(0.95f, 0.35f, 0.25f, 0.92f)
                            : new Color(0.98f, 0.84f, 0.42f, 0.9f);
                        dot.rectTransform.anchoredPosition = PointLocal(m.To);
                    }
            }
            for (; d < _dots.Length; d++) _dots[d].gameObject.SetActive(false);

            int tookB = 22 - _game.CountStones(FanColor.Black);
            int tookW = 22 - _game.CountStones(FanColor.White);
            for (int i = 0; i < 22; i++)
            {
                _wellB[i].gameObject.SetActive(i < tookB);
                _wellW[i].gameObject.SetActive(i < tookW);
            }
            _youTook.text = "× " + tookB;
            _foeTook.text = "× " + tookW;
        }

        private bool HasCapture(int from, int to)
        {
            foreach (var m in _legal) if (m.From == from && m.To == to && m.IsCapture) return true;
            return false;
        }

        // ── input ───────────────────────────────────────────────────────────────────────────────────────────────
        private void OnCell(int p)
        {
            if (_busy || _over || _game.SideToMove != FanColor.White || (_chooser != null && _chooser.activeSelf)) return;

            bool chaining = _game.ChainStone >= 0;
            if (chaining) _selected = _game.ChainStone;   // mid-chain the moving stone is locked

            if (_selected >= 0)
            {
                FanMove app = default, wd = default; bool hasApp = false, hasWd = false, hasPaika = false; FanMove paika = default;
                foreach (var m in _legal)
                {
                    if (m.IsStop || m.From != _selected || m.To != p) continue;
                    if (m.Approach) { app = m; hasApp = true; }
                    else if (m.Withdrawal) { wd = m; hasWd = true; }
                    else { paika = m; hasPaika = true; }
                }
                if (hasApp && hasWd) { _chFrom = _selected; _chTo = p; _chooser.SetActive(true); _chooser.transform.SetAsLastSibling(); return; }
                if (hasApp) { StartCoroutine(PlayMove(app)); return; }
                if (hasWd) { StartCoroutine(PlayMove(wd)); return; }
                if (hasPaika) { StartCoroutine(PlayMove(paika)); return; }
            }

            if (!chaining)
            {
                if (p == _selected) { _selected = -1; RefreshBoard(); return; }    // re-click deselects
                if (_game.Board[p] == 1) _selected = p;
                else _selected = -1;
            }
            RefreshBoard();
        }

        private void OnChoose(bool withdrawal)
        {
            _chooser.SetActive(false);
            foreach (var m in _legal)
                if (!m.IsStop && m.From == _chFrom && m.To == _chTo && (withdrawal ? m.Withdrawal : m.Approach))
                { StartCoroutine(PlayMove(m)); return; }
        }

        private void OnStopChain()
        {
            if (_busy || _over || _game.ChainStone < 0 || _game.SideToMove != FanColor.White) return;
            foreach (var m in _legal)
                if (m.IsStop) { _stopBtn.gameObject.SetActive(false); _selected = -1; StartCoroutine(PlayMove(m)); return; }
        }

        // ── move execution ──────────────────────────────────────────────────────────────────────────────────────
        private IEnumerator PlayMove(FanMove m)
        {
            _busy = true;
            _stopBtn.gameObject.SetActive(false);

            if (!m.IsStop)
            {
                var img = _stones[m.From];
                if (img != null && img.gameObject.activeSelf)
                {
                    Vector2 a = PointLocal(m.From), b = PointLocal(m.To);
                    float t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime / 0.15f;
                        img.rectTransform.anchoredPosition = Vector2.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                        yield return null;
                    }
                    img.rectTransform.anchoredPosition = a;   // pool image snaps home; board state drives rendering
                }
            }

            _game.Apply(m);
            if (!m.IsStop)
            {
                if (_game.LastRemoved > 0) _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "take1" : "take2", 0.85f, UnityEngine.Random.Range(0.94f, 1.06f));
                else _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "move1" : "move2", 0.7f, UnityEngine.Random.Range(0.95f, 1.08f));
            }

            _legal = _game.LegalMoves();
            if (CheckEnd()) { _selected = -1; RefreshBoard(); _busy = false; yield break; }

            if (_game.SideToMove == FanColor.White)
            {
                if (_game.ChainStone >= 0)
                {
                    _selected = _game.ChainStone;
                    _statusText.text = "Chain! Capture again — or Stop.";
                    _stopBtn.gameObject.SetActive(true);
                }
                else
                {
                    _selected = -1;
                    _statusText.text = AnyCapture(_legal) ? "Your move — a capture is mandatory." : "Your move.";
                }
                RefreshBoard();
                _busy = false;
            }
            else
            {
                _selected = -1;
                RefreshBoard();
                StartCoroutine(AiTurn());
            }
        }

        private static bool AnyCapture(List<FanMove> moves)
        {
            foreach (var m in moves) if (m.IsCapture) return true;
            return false;
        }

        private IEnumerator AiTurn()
        {
            _busy = true;
            _statusText.text = "Sigrid ponders…";
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 1.1f));

            while (_game.SideToMove == FanColor.Black && !_over)
            {
                var clone = _game.Clone();
                int depth = _aiDepth;
                int seed = _rng.Next();
                _aiTask = Task.Run<object>(() =>
                {
                    var rng = new System.Random(seed);
                    return FanoronaAI.FindBest(clone, depth, rng, out var best) ? (object)best : null;
                });
                while (!_aiTask.IsCompleted) yield return null;
                var result = _aiTask.Result;
                _aiTask = null;
                if (result == null) break;

                var m = (FanMove)result;
                if (!m.IsStop)
                {
                    var img = _stones[m.From];
                    if (img != null && img.gameObject.activeSelf)
                    {
                        Vector2 a = PointLocal(m.From), b = PointLocal(m.To);
                        float t = 0f;
                        while (t < 1f)
                        {
                            t += Time.deltaTime / 0.15f;
                            img.rectTransform.anchoredPosition = Vector2.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                            yield return null;
                        }
                        img.rectTransform.anchoredPosition = a;
                    }
                }
                _game.Apply(m);
                if (!m.IsStop)
                {
                    if (_game.LastRemoved > 0) _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "take1" : "take2", 0.85f, UnityEngine.Random.Range(0.94f, 1.06f));
                    else _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "move1" : "move2", 0.7f, UnityEngine.Random.Range(0.95f, 1.08f));
                }
                RefreshBoard();
                if (_game.SideToMove == FanColor.Black) yield return new WaitForSeconds(0.45f);   // chain beat
            }

            _legal = _game.LegalMoves();
            if (!CheckEnd())
                _statusText.text = AnyCapture(_legal) ? "Your move — a capture is mandatory." : "Your move.";
            RefreshBoard();
            _busy = false;
        }

        private bool CheckEnd()
        {
            var w = _game.Winner();
            if (w == null) return false;
            _over = true;
            _statusText.text = w == FanColor.White
                ? "You win — Sigrid's stones are swept from the board!"
                : "Sigrid wins — your stones are gone.";
            return true;
        }

        private void NewGame()
        {
            StopAllCoroutines();
            _game.SetStart();
            _selected = -1; _busy = false; _over = false;
            _stopBtn.gameObject.SetActive(false);
            if (_chooser != null) _chooser.SetActive(false);
            _legal = _game.LegalMoves();
            _statusText.text = "Your move — captures are mandatory.";
            _audio?.PlayPitched("start", 0.8f, 1f);
            RefreshBoard();
        }

        private void BuildChooser(Transform parent)
        {
            _chooser = NewImg(parent, "Chooser", FanoronaTableGame.Panel, Image.Type.Sliced).gameObject;
            Place((RectTransform)_chooser.transform, 340, 104, 0, 0);
            var lbl = NewTxt(_chooser.transform, "Capture which line?", 15, new Color(1f, 0.88f, 0.6f));
            Place(lbl.rectTransform, 320, 22, 0, 32);
            MakeButton(_chooser.transform, "Approach", new Vector2(0.5f, 0.5f), new Vector2(140, 40), new Vector2(-78, -14), () => OnChoose(false));
            MakeButton(_chooser.transform, "Withdraw", new Vector2(0.5f, 0.5f), new Vector2(140, 40), new Vector2(78, -14), () => OnChoose(true));
            _chooser.SetActive(false);
        }

        private void Update()
        {
            if (_cabRT == null) return;
            float s = FanoronaTableGame.UiScaleCfg != null ? Mathf.Clamp(FanoronaTableGame.UiScaleCfg.Value, 0.6f, 1.6f) : 1f;
            if (!Mathf.Approximately(s, _lastScale)) { _lastScale = s; _cabRT.localScale = new Vector3(s, s, 1f); }
            float ox = FanoronaTableGame.OffsetXCfg != null ? FanoronaTableGame.OffsetXCfg.Value : 0f;
            float oy = FanoronaTableGame.OffsetYCfg != null ? FanoronaTableGame.OffsetYCfg.Value : 0f;
            _cabRT.anchoredPosition = new Vector2(ox, oy);
        }

        private static void AddDrag(GameObject go, Action<UnityEngine.EventSystems.PointerEventData> onDrag)
        {
            var trig = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Drag };
            entry.callback.AddListener(d => onDrag((UnityEngine.EventSystems.PointerEventData)d));
            trig.triggers.Add(entry);
        }

        private void OnDestroy() { _audio?.Dispose(); }

        // ── tiny UI helpers (chess-table idiom) ─────────────────────────────────────────────────────────────────
        private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>(); img.sprite = FanoronaTableGame.Btn; img.type = Image.Type.Sliced;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb = btn.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.fadeDuration = 0.06f; btn.colors = cb;
            btn.onClick.AddListener(onClick);
            if (!string.IsNullOrEmpty(label))
            {
                var t = NewTxt(go.transform, label, 15, new Color(1f, 0.9f, 0.65f));
                FillRect(t.rectTransform);
            }
            return btn;
        }

        private Image NewImg(Transform parent, string name, Sprite sprite, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.sprite = sprite; img.type = type; img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI NewTxt(Transform parent, string text, float size, Color c)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false; t.enableWordWrapping = false; t.richText = true;
            return t;
        }

        private static void Place(RectTransform rt, float w, float h, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
        }

        private static void FillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
