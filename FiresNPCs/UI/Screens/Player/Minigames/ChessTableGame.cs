using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.Minigames;
using FiresRPGmaker.Modules.Chess;

namespace FiresRPGmaker.UI.Screens.PlayerPanels.Minigames
{
    /// <summary>
    /// Chess — the Jarl's Board. The marble board (cropped from the studio render) with the Norse Lewis chessmen
    /// rendered from the real 3D set; the black side wears the historical red stain (an in-engine tint). You play
    /// White against the perft-verified engine + alpha-beta AI, which thinks on a worker thread against a board
    /// clone so the game never hitches. Click a piece, click a highlighted square; promotion offers the real pieces.
    /// </summary>
    public static class ChessTableGame
    {
        private static readonly Dictionary<string, Sprite> _pieces = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> _flat = new Dictionary<string, Sprite>();
        private static Sprite _board, _panel, _bar, _btn, _btnHi;
        private static bool _loaded;

        /// <summary>Chess window scale + position (drag the title bar / corner grip in-game). Hot-reloads.</summary>
        public static BepInEx.Configuration.ConfigEntry<float> UiScaleCfg, OffsetXCfg, OffsetYCfg;

        /// <summary>Skald's strength: Easy / Medium / Hard / Master (hot-reloads). An explicit depth overrides it.</summary>
        public static BepInEx.Configuration.ConfigEntry<string> DifficultyCfg;

        internal static void AiTier(int depthOverride, out int maxDepth, out int timeMs, out float errorChance)
        {
            if (depthOverride > 0) { maxDepth = depthOverride; timeMs = 4000; errorChance = 0f; return; }
            switch ((DifficultyCfg?.Value ?? "Medium").Trim().ToLowerInvariant())
            {
                case "easy": maxDepth = 2; timeMs = 250; errorChance = 0.30f; break;
                case "hard": maxDepth = 8; timeMs = 1800; errorChance = 0f; break;
                case "master": maxDepth = 12; timeMs = 3500; errorChance = 0f; break;
                default: maxDepth = 4; timeMs = 700; errorChance = 0.06f; break;
            }
        }

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>Whether this client last chose the 3D seat — rejoin/dock-chip reopen keeps the same view.</summary>
        internal static bool LastView3D;

        public static void Open(int aiDepth = 0, ZNetView boundBoard = null)
        {
            if (IsClientOnly()) return;
            if (boundBoard != null) LastView3D = false;
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresChessTable", 6500);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<ChessTableController>();
            ctrl.Build(overlay, aiDepth <= 0 ? 0 : Mathf.Clamp(aiDepth, 1, 12), boundBoard);
        }

        /// <summary>Open the board in board-bound multiplayer, seated as <paramref name="myColor"/>. The board flips
        /// for Black, turns are seat-gated, and moves relay through the board's ZDO.</summary>
        public static void OpenMp(ZNetView boundBoard, ChessColor myColor)
        {
            if (IsClientOnly() || boundBoard == null || !boundBoard.IsValid()) return;
            LastView3D = false;
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresChessTable", 6500);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<ChessTableController>();
            ctrl.BuildMp(overlay, boundBoard, myColor);
        }

        /// <summary>The 3D seat: same game, played on the placed board's real pieces. Falls back to the window if
        /// the board's living-piece grid isn't available.</summary>
        public static void Open3D(int aiDepth, ZNetView boundBoard)
        {
            if (IsClientOnly()) return;
            var visual = boundBoard != null && boundBoard.IsValid() ? boundBoard.GetComponent<ChessBoardVisual>() : null;
            if (visual == null || !visual.GridReady) { Open(aiDepth, boundBoard); return; }
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresChessTable3D", 6500, 0f);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<ChessTableController>();
            ctrl.Build3D(overlay, aiDepth <= 0 ? 0 : Mathf.Clamp(aiDepth, 1, 12), boundBoard);
            Attach3D(overlay, ctrl, boundBoard, visual, whiteSide: true);
        }

        public static void OpenMp3D(ZNetView boundBoard, ChessColor myColor)
        {
            if (IsClientOnly() || boundBoard == null || !boundBoard.IsValid()) return;
            var visual = boundBoard.GetComponent<ChessBoardVisual>();
            if (visual == null || !visual.GridReady) { OpenMp(boundBoard, myColor); return; }
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresChessTable3D", 6500, 0f);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<ChessTableController>();
            ctrl.BuildMp3D(overlay, boundBoard, myColor);
            Attach3D(overlay, ctrl, boundBoard, visual, whiteSide: myColor == ChessColor.White);
        }

        /// <summary>Rejoin/dock-chip path — reopen in whichever view this client used last.</summary>
        public static void OpenMpAuto(ZNetView boundBoard, ChessColor myColor)
        {
            if (LastView3D) OpenMp3D(boundBoard, myColor);
            else OpenMp(boundBoard, myColor);
        }

        private static void Attach3D(FiresMinigameOverlay overlay, ChessTableController ctrl, ZNetView board,
                                     ChessBoardVisual visual, bool whiteSide)
        {
            LastView3D = true;
            var session = Chess3DSession.Begin(board, visual, ctrl, whiteSide, () => overlay.Close());
            ctrl.Session3D = session;
            overlay.OnClosed += session.End;
        }

        internal static Sprite Piece(ChessPiece t)
        {
            string code = t == ChessPiece.Knight ? "N" : t == ChessPiece.King ? "K" : t == ChessPiece.Queen ? "Q"
                        : t == ChessPiece.Bishop ? "B" : t == ChessPiece.Rook ? "R" : "P";
            if (_pieces.TryGetValue(code, out var s)) return s;
            s = FiresMinigameSprites.Load(Assembly.GetExecutingAssembly(), "chess" + code + ".png");
            _pieces[code] = s;
            return s;
        }

        /// <summary>The standard flat piece silhouettes (Kenney CC0, white-on-alpha — tint per side).</summary>
        internal static Sprite FlatPiece(ChessPiece t)
        {
            string code = t == ChessPiece.Knight ? "N" : t == ChessPiece.King ? "K" : t == ChessPiece.Queen ? "Q"
                        : t == ChessPiece.Bishop ? "B" : t == ChessPiece.Rook ? "R" : "P";
            if (_flat.TryGetValue(code, out var s)) return s;
            s = FiresMinigameSprites.Load(Assembly.GetExecutingAssembly(), "flat" + code + ".png");
            _flat[code] = s;
            return s;
        }

        internal static Sprite Board => _board;
        internal static Sprite Panel => _panel;
        internal static Sprite Bar => _bar;
        internal static Sprite Btn => _btn;
        internal static Sprite BtnHi => _btnHi;

        private static void EnsureAssets()
        {
            if (_loaded) return;
            _loaded = true;
            _board = MakeChecker();   // clean standard 8×8 — the carved-set look lives on the in-world prefab
            _panel = FiresRoundedSkin.RoundedSprite(20, new Color(0.11f, 0.088f, 0.06f, 0.99f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _btn = FiresRoundedSkin.RoundedSprite(10, new Color(0.34f, 0.23f, 0.10f, 1f), new Color(0.62f, 0.42f, 0.16f, 0.8f), 1);
            _btnHi = FiresRoundedSkin.RoundedSprite(10, new Color(0.47f, 0.32f, 0.14f, 1f), new Color(0.85f, 0.62f, 0.26f, 1f), 1);
        }

        // Standard online-chess board: flat light/dark squares, a1 dark. Generated at runtime — no art needed.
        private static Sprite MakeChecker()
        {
            const int S = 768, sq = S / 8;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var light = new Color(0.91f, 0.90f, 0.85f);
            var dark = new Color(0.36f, 0.37f, 0.40f);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    px[y * S + x] = (((x / sq) + (y / sq)) & 1) == 0 ? dark : light;
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    internal sealed class ChessTableController : MonoBehaviour
    {
        private const float CabW = 1040f, CabH = 660f;
        private const float BoardW = 560f, BoardH = 560f;
        // The generated checker fills its sprite edge to edge — squares span the full image.
        private const float FX0 = 0f, FX1 = 1f;
        private const float FY0 = 0f, FY1 = 1f;

        // Standard 2D set: white pieces with a dark halo, black pieces with a light halo — readable on both squares.
        private static readonly Color WhiteTint = new Color(0.98f, 0.97f, 0.94f);
        private static readonly Color BlackTint = new Color(0.15f, 0.14f, 0.16f);
        private static readonly Color WhiteHalo = new Color(0.08f, 0.08f, 0.10f, 0.60f);
        private static readonly Color BlackHalo = new Color(0.95f, 0.94f, 0.90f, 0.60f);

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private ChessBoard _game;
        private System.Random _rng;
        private int _aiDepth;

        private RectTransform _boardRT;
        private readonly Image[] _pieceImgs = new Image[64];       // one slot per square (pooled)
        private readonly Image[] _dots = new Image[32];
        private Image _selImg, _lastFromImg, _lastToImg, _checkImg;
        private TMP_Text _statusText;
        private GameObject _promoPanel;
        private readonly List<int> _capturedByWhite = new List<int>();   // black piece codes taken by White
        private readonly List<int> _capturedByBlack = new List<int>();
        private readonly Image[] _trayW = new Image[15];
        private readonly Image[] _trayB = new Image[15];

        private readonly Image[] _pieceHalos = new Image[64];
        private readonly List<string> _moveLog = new List<string>();
        private TMP_Text _logText;
        private ScrollRect _logScroll;
        private RectTransform _cabRT;
        private float _lastScale = -1f;

        private int _selected = -1;
        private List<ChessMove> _legal = new List<ChessMove>();
        private bool _busy, _over;
        private int _lastFrom = -1, _lastTo = -1;
        private Task<object> _aiTask;
        private readonly List<ulong> _posKeys = new List<ulong>();   // position hashes for threefold + AI shuffle-avoidance
        private ZNetView _boundBoard;   // the placed world board this game lives on (null = free-floating game)

        // ── board-bound multiplayer ─────────────────────────────────────────────────────────────────────────────
        private bool _mp;
        private ChessColor _myColor = ChessColor.White;
        private bool _flipView;                    // Black sees the board from their side
        private string _oppName = "your opponent";
        private bool _oppLive;
        private float _mpPollT, _mpBeatT;
        private int _mvSeq;                        // sequence of the latest move written/applied (fires_chess_last)
        private string _lastAppliedLast = "";
        private bool _minimized;
        private TMP_Text _titleText;

        // ── the 3D seat (Chess3DSession drives camera/clicks/hint stones; this controller stays the game brain) ──
        internal bool ThreeD;
        internal Chess3DSession Session3D;
        internal ChessBoard Game => _game;
        internal int SelectedSq => _selected;
        internal List<ChessMove> LegalNow => _legal;
        internal bool OverNow => _over;
        internal void ClickSquare(int sq) => OnCell(sq);

        public void BuildMp(FiresMinigameOverlay overlay, ZNetView boundBoard, ChessColor myColor)
        {
            _mp = true;
            _myColor = myColor;
            _flipView = myColor == ChessColor.Black;
            Build(overlay, 0, boundBoard);
        }

        public void BuildMp3D(FiresMinigameOverlay overlay, ZNetView boundBoard, ChessColor myColor)
        {
            _mp = true;
            _myColor = myColor;
            Build3D(overlay, 0, boundBoard);
        }

        public void Build3D(FiresMinigameOverlay overlay, int aiDepth, ZNetView boundBoard)
        {
            ThreeD = true;
            _overlay = overlay;
            _aiDepth = aiDepth;
            _boundBoard = boundBoard;
            _rng = new System.Random(unchecked(Environment.TickCount * 17 + 3));
            _audio = new FiresMinigameAudio(Assembly.GetExecutingAssembly());
            _audio.RegisterClip("move1", "FiresPokerFlip1.wav"); _audio.RegisterClip("move2", "FiresPokerFlip2.wav");
            _audio.RegisterClip("take1", "FiresPokerChips1.wav"); _audio.RegisterClip("take2", "FiresPokerChips2.wav");
            _audio.RegisterClip("start", "FiresPokerDeal1.wav");

            _game = new ChessBoard();
            _posKeys.Add(ChessAI.HashPosition(_game));

            Build3DPanels(overlay.PlayField);

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;

            if (_boundBoard != null && _boundBoard.IsValid())
            {
                string saved = _boundBoard.GetZDO().GetString(ChessBoardVisual.ZdoKey, "");
                string last = _boundBoard.GetZDO().GetString(ChessSeats.KeyLast, "");
                _lastAppliedLast = last;
                _mvSeq = ParseSeq(last);
                if (!string.IsNullOrEmpty(saved) && ChessBoard.Deserialize(saved, _game))
                {
                    _posKeys.Clear();
                    _posKeys.Add(ChessAI.HashPosition(_game));
                    if (!_mp)
                    {
                        _statusText.text = "The game resumes where it stood.";
                        if (!CheckEnd() && _game.SideToMove == ChessColor.Black) StartCoroutine(AiTurn());
                    }
                    else CheckEnd();
                }
            }

            _audio?.PlayPitched("start", 0.8f, 1f);
            RefreshBoard();
            if (_mp) UpdateMpStatus();
        }

        // Translucent edge panels — the window's data without the window: move log on the left, status/trays/
        // actions on the right, promotion centred. The middle of the screen belongs to the real board.
        private void Build3DPanels(RectTransform root)
        {
            var dimBar = new Color(1f, 1f, 1f, 0.86f);

            var title = NewImg(root, "TitleStrip", ChessTableGame.Bar, Image.Type.Sliced);
            title.color = dimBar;
            title.raycastTarget = false;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f); trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(430f, 40f); trt.anchoredPosition = new Vector2(0f, -8f);
            _titleText = NewTxt(trt, "Chess — the Jarl's Board", 19, new Color(1f, 0.85f, 0.5f));
            FillRect(_titleText.rectTransform);

            var left = NewImg(root, "LeftPanel", ChessTableGame.Bar, Image.Type.Sliced);
            left.color = dimBar;
            var lrt = left.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f); lrt.pivot = new Vector2(0f, 0.5f);
            lrt.sizeDelta = new Vector2(250f, 480f); lrt.anchoredPosition = new Vector2(14f, 0f);

            var movesLbl = NewTxt(lrt, "Moves", 15, new Color(1f, 0.85f, 0.5f));
            Place(movesLbl.rectTransform, 200, 20, 0, 218);

            var vp = NewImg(lrt, "LogViewport", null, Image.Type.Simple);
            vp.color = new Color(0f, 0f, 0f, 0.30f);
            vp.raycastTarget = true;
            Place(vp.rectTransform, 226, 410, 0, -14);
            vp.gameObject.AddComponent<RectMask2D>();
            var logContent = new GameObject("LogContent", typeof(RectTransform)).GetComponent<RectTransform>();
            logContent.SetParent(vp.rectTransform, false);
            logContent.anchorMin = new Vector2(0f, 1f); logContent.anchorMax = new Vector2(1f, 1f);
            logContent.pivot = new Vector2(0.5f, 1f); logContent.sizeDelta = new Vector2(0f, 10f);
            _logText = logContent.gameObject.AddComponent<TextMeshProUGUI>();
            _logText.fontSize = 12.5f; _logText.color = new Color(0.9f, 0.86f, 0.74f);
            _logText.alignment = TextAlignmentOptions.TopLeft; _logText.enableWordWrapping = true;
            _logText.margin = new Vector4(8f, 6f, 8f, 6f);
            _logText.raycastTarget = false;
            logContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _logScroll = vp.gameObject.AddComponent<ScrollRect>();
            _logScroll.content = logContent; _logScroll.viewport = vp.rectTransform;
            _logScroll.horizontal = false; _logScroll.vertical = true;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.scrollSensitivity = 18f;

            var right = NewImg(root, "RightPanel", ChessTableGame.Bar, Image.Type.Sliced);
            right.color = dimBar;
            var rrt = right.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f); rrt.pivot = new Vector2(1f, 0.5f);
            rrt.sizeDelta = new Vector2(250f, 480f); rrt.anchoredPosition = new Vector2(-14f, 0f);

            _statusText = NewTxt(rrt, "Your move.", 15, new Color(0.92f, 0.88f, 0.75f));
            Place(_statusText.rectTransform, 214, 110, 0, 168);
            _statusText.enableWordWrapping = true;

            bool meWhiteSide = !_mp || _myColor == ChessColor.White;
            var tookLbl = NewTxt(rrt, meWhiteSide ? "You took" : "They took", 13, new Color(0.78f, 0.7f, 0.55f));
            Place(tookLbl.rectTransform, 200, 18, 0, 96);
            var lostLbl = NewTxt(rrt, _mp ? (meWhiteSide ? "They took" : "You took") : "Skald took", 13, new Color(0.78f, 0.7f, 0.55f));
            Place(lostLbl.rectTransform, 200, 18, 0, -6);
            for (int i = 0; i < _trayW.Length; i++)
            {
                _trayW[i] = NewImg(rrt, "Tw" + i, null, Image.Type.Simple);
                Place(_trayW[i].rectTransform, 25, 25, -88 + (i % 8) * 25, 68 - (i / 8) * 27);
                _trayW[i].gameObject.SetActive(false);
                _trayB[i] = NewImg(rrt, "Tb" + i, null, Image.Type.Simple);
                Place(_trayB[i].rectTransform, 25, 25, -88 + (i % 8) * 25, -34 - (i / 8) * 27);
                _trayB[i].gameObject.SetActive(false);
            }

            MakeButton(rrt, "New Game", new Vector2(0.5f, 0.5f), new Vector2(150, 38), new Vector2(0, -118), NewGame);
            if (_mp) MakeButton(rrt, "Minimize", new Vector2(0.5f, 0.5f), new Vector2(150, 34), new Vector2(0, -158), MinimizeToChip);
            MakeButton(rrt, "Stand Up", new Vector2(0.5f, 0.5f), new Vector2(150, 34), new Vector2(0, _mp ? -196f : -158f), () => _overlay.Close());

            var hint = NewTxt(rrt, "Click a piece — click a stone\nScroll zooms — Esc stands up", 12, new Color(0.62f, 0.6f, 0.55f));
            Place(hint.rectTransform, 220, 36, 0, -226);
            hint.enableWordWrapping = true;

            BuildPromoPanel(root);
        }

        public void Build(FiresMinigameOverlay overlay, int aiDepth, ZNetView boundBoard = null)
        {
            _overlay = overlay;
            _aiDepth = aiDepth;
            _boundBoard = boundBoard;
            _rng = new System.Random(unchecked(Environment.TickCount * 17 + 3));
            _audio = new FiresMinigameAudio(Assembly.GetExecutingAssembly());
            _audio.RegisterClip("move1", "FiresPokerFlip1.wav"); _audio.RegisterClip("move2", "FiresPokerFlip2.wav");
            _audio.RegisterClip("take1", "FiresPokerChips1.wav"); _audio.RegisterClip("take2", "FiresPokerChips2.wav");
            _audio.RegisterClip("start", "FiresPokerDeal1.wav");

            _game = new ChessBoard();
            _posKeys.Add(ChessAI.HashPosition(_game));

            var root = overlay.PlayField;
            var cab = NewImg(root, "Cabinet", ChessTableGame.Panel, Image.Type.Sliced);
            Place(cab.rectTransform, CabW, CabH, 0, 0);

            var titleBar = NewImg(cab.rectTransform, "TitleBar", ChessTableGame.Bar, Image.Type.Sliced);
            var tb = titleBar.rectTransform; tb.anchorMin = new Vector2(0, 1); tb.anchorMax = new Vector2(1, 1); tb.pivot = new Vector2(0.5f, 1);
            tb.sizeDelta = new Vector2(-24, 44); tb.anchoredPosition = new Vector2(0, -12);
            _titleText = NewTxt(titleBar.rectTransform, "Chess — the Jarl's Board", 22, new Color(1f, 0.85f, 0.5f));
            FillRect(_titleText.rectTransform);

            titleBar.raycastTarget = true;
            AddDrag(titleBar.gameObject, d =>
            {
                float sc = Mathf.Max(0.6f, _lastScale);
                if (ChessTableGame.OffsetXCfg != null) ChessTableGame.OffsetXCfg.Value += d.delta.x / sc;
                if (ChessTableGame.OffsetYCfg != null) ChessTableGame.OffsetYCfg.Value += d.delta.y / sc;
            });
            var grip = NewImg(cab.rectTransform, "ScaleGrip", FiresRoundedSkin.RoundedSprite(6, new Color(0.85f, 0.62f, 0.26f, 0.55f)), Image.Type.Sliced);
            var griprt = grip.rectTransform;
            griprt.anchorMin = griprt.anchorMax = new Vector2(0f, 0f); griprt.pivot = new Vector2(0f, 0f);
            griprt.sizeDelta = new Vector2(18f, 18f); griprt.anchoredPosition = new Vector2(4f, 4f);
            grip.raycastTarget = true;
            AddDrag(grip.gameObject, d =>
            {
                if (ChessTableGame.UiScaleCfg == null) return;
                ChessTableGame.UiScaleCfg.Value = Mathf.Clamp(ChessTableGame.UiScaleCfg.Value - (d.delta.x + d.delta.y) * 0.003f, 0.6f, 1.6f);
            });

            _cabRT = cab.rectTransform;
            var boardFrame = NewImg(cab.rectTransform, "BoardFrame", ChessTableGame.Bar, Image.Type.Sliced);
            Place(boardFrame.rectTransform, 606, 606, 35, -18);
            var board = NewImg(cab.rectTransform, "Board", ChessTableGame.Board, Image.Type.Simple);
            Place(board.rectTransform, BoardW, BoardH, 35, -18);
            _boardRT = board.rectTransform;

            // Highlights under the pieces.
            _lastFromImg = MakeHighlight(new Color(0.95f, 0.82f, 0.35f, 0.20f));
            _lastToImg = MakeHighlight(new Color(0.95f, 0.82f, 0.35f, 0.28f));
            _checkImg = MakeHighlight(new Color(0.95f, 0.25f, 0.18f, 0.38f));
            _selImg = MakeHighlight(new Color(0.95f, 0.82f, 0.35f, 0.40f));

            // Click cells + piece images.
            for (int sq = 0; sq < 64; sq++)
            {
                int captured = sq;
                var cell = NewImg(_boardRT, "Cell" + sq, null, Image.Type.Simple);
                cell.color = new Color(0, 0, 0, 0);
                cell.raycastTarget = true;
                PlaceSquare(cell.rectTransform, sq, 1.0f);
                var btn = cell.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => OnCell(captured));

                var halo = NewImg(_boardRT, "Halo" + sq, null, Image.Type.Simple);
                halo.preserveAspect = true;
                PlaceSquare(halo.rectTransform, sq, 0.98f);
                halo.gameObject.SetActive(false);
                _pieceHalos[sq] = halo;

                var img = NewImg(_boardRT, "Piece" + sq, null, Image.Type.Simple);
                img.preserveAspect = true;
                PlaceSquare(img.rectTransform, sq, 0.86f);
                img.gameObject.SetActive(false);
                _pieceImgs[sq] = img;
            }

            // Board coordinates — files along the bottom, ranks up the left, on the frame.
            var coordCol = new Color(0.74f, 0.66f, 0.5f);
            for (int f = 0; f < 8; f++)
            {
                var t = NewTxt(_boardRT, ((char)('a' + f)).ToString(), 12, coordCol);
                Place(t.rectTransform, 20, 16, 0, 0);
                t.rectTransform.anchoredPosition = new Vector2(SquareLocal(ChessBoard.Sq(f, 0)).x, -BoardH / 2f - 13f);
            }
            for (int r = 0; r < 8; r++)
            {
                var t = NewTxt(_boardRT, (r + 1).ToString(), 12, coordCol);
                Place(t.rectTransform, 16, 16, 0, 0);
                t.rectTransform.anchoredPosition = new Vector2(-BoardW / 2f - 13f, SquareLocal(ChessBoard.Sq(0, r)).y);
            }
            for (int i = 0; i < _dots.Length; i++)
            {
                var d = NewImg(_boardRT, "Dot" + i, FiresRoundedSkin.RoundedSprite(9, new Color(0.98f, 0.84f, 0.42f, 0.85f)), Image.Type.Simple);
                Place(d.rectTransform, 18, 18, 0, 0);
                d.gameObject.SetActive(false);
                _dots[i] = d;
            }

            // Left panel — chat-style move log on top, captured pieces below.
            var leftBkg = NewImg(cab.rectTransform, "LeftPanel", ChessTableGame.Bar, Image.Type.Sliced);
            Place(leftBkg.rectTransform, 232, 560, -388, -18);

            var movesLbl = NewTxt(cab.rectTransform, "Moves", 15, new Color(1f, 0.85f, 0.5f));
            Place(movesLbl.rectTransform, 200, 20, -388, 244);

            var vp = NewImg(cab.rectTransform, "LogViewport", null, Image.Type.Simple);
            vp.color = new Color(0f, 0f, 0f, 0.28f);
            vp.raycastTarget = true;   // needed for wheel scrolling
            Place(vp.rectTransform, 208, 262, -388, 96);
            vp.gameObject.AddComponent<RectMask2D>();
            var logContent = new GameObject("LogContent", typeof(RectTransform)).GetComponent<RectTransform>();
            logContent.SetParent(vp.rectTransform, false);
            logContent.anchorMin = new Vector2(0f, 1f); logContent.anchorMax = new Vector2(1f, 1f);
            logContent.pivot = new Vector2(0.5f, 1f); logContent.sizeDelta = new Vector2(0f, 10f);
            _logText = logContent.gameObject.AddComponent<TextMeshProUGUI>();
            _logText.fontSize = 12.5f; _logText.color = new Color(0.9f, 0.86f, 0.74f);
            _logText.alignment = TextAlignmentOptions.TopLeft; _logText.enableWordWrapping = true;
            _logText.margin = new Vector4(8f, 6f, 8f, 6f);
            _logText.raycastTarget = false;
            logContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _logScroll = vp.gameObject.AddComponent<ScrollRect>();
            _logScroll.content = logContent; _logScroll.viewport = vp.rectTransform;
            _logScroll.horizontal = false; _logScroll.vertical = true;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.scrollSensitivity = 18f;

            bool meWhiteSide = !_mp || _myColor == ChessColor.White;
            var tookLbl = NewTxt(cab.rectTransform, meWhiteSide ? "You took" : "They took", 13, new Color(0.78f, 0.7f, 0.55f));
            Place(tookLbl.rectTransform, 200, 18, -388, -74);
            var lostLbl = NewTxt(cab.rectTransform, _mp ? (meWhiteSide ? "They took" : "You took") : "Skald took", 13, new Color(0.78f, 0.7f, 0.55f));
            Place(lostLbl.rectTransform, 200, 18, -388, -176);
            for (int i = 0; i < _trayW.Length; i++)
            {
                _trayW[i] = NewImg(cab.rectTransform, "Tw" + i, null, Image.Type.Simple);
                Place(_trayW[i].rectTransform, 26, 26, -478 + (i % 8) * 26, -102 - (i / 8) * 28);
                _trayW[i].gameObject.SetActive(false);
                _trayB[i] = NewImg(cab.rectTransform, "Tb" + i, null, Image.Type.Simple);
                Place(_trayB[i].rectTransform, 26, 26, -478 + (i % 8) * 26, -204 - (i / 8) * 28);
                _trayB[i].gameObject.SetActive(false);
            }

            // Right strip — status + New Game.
            _statusText = NewTxt(cab.rectTransform, "Your move.", 16, new Color(0.92f, 0.88f, 0.75f));
            Place(_statusText.rectTransform, 180, 240, 420, 130);
            _statusText.enableWordWrapping = true;

            MakeButton(cab.rectTransform, "New Game", new Vector2(0.5f, 0.5f), new Vector2(150, 42), new Vector2(420, -240), NewGame);
            MakeButton(cab.rectTransform, "Leave", new Vector2(1, 1), new Vector2(96, 34), new Vector2(-62, -31), () => _overlay.Close());
            if (_mp)
                MakeButton(cab.rectTransform, "Minimize", new Vector2(1, 1), new Vector2(110, 34), new Vector2(-173, -31), MinimizeToChip);

            BuildPromoPanel(cab.rectTransform);

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;

            // Bound to a world board carrying a saved game → resume it right where it stood.
            if (_boundBoard != null && _boundBoard.IsValid())
            {
                string saved = _boundBoard.GetZDO().GetString(ChessBoardVisual.ZdoKey, "");
                string last = _boundBoard.GetZDO().GetString(ChessSeats.KeyLast, "");
                _lastAppliedLast = last;
                _mvSeq = ParseSeq(last);
                if (!string.IsNullOrEmpty(saved) && ChessBoard.Deserialize(saved, _game))
                {
                    _posKeys.Clear();
                    _posKeys.Add(ChessAI.HashPosition(_game));
                    if (!_mp)
                    {
                        _statusText.text = "The game resumes where it stood.";
                        if (!CheckEnd() && _game.SideToMove == ChessColor.Black) StartCoroutine(AiTurn());
                    }
                    else CheckEnd();
                }
            }

            _audio?.PlayPitched("start", 0.8f, 1f);
            RefreshBoard();
            if (_mp) UpdateMpStatus();
        }

        // Push the position to the bound world board — its pieces mirror the game and the save persists it.
        // A move also writes "<seq>|from|to|promo" so the multiplayer opponent replays it through the normal path.
        private void WriteBoardState(ChessMove? moved = null)
        {
            if (_boundBoard == null || !_boundBoard.IsValid()) return;
            try
            {
                _boundBoard.ClaimOwnership();
                var zdo = _boundBoard.GetZDO();
                zdo.Set(ChessBoardVisual.ZdoKey, ChessBoard.Serialize(_game));
                if (moved.HasValue)
                {
                    _mvSeq++;
                    _lastAppliedLast = _mvSeq + "|" + moved.Value.From + "|" + moved.Value.To + "|" + (int)moved.Value.Promo;
                    zdo.Set(ChessSeats.KeyLast, _lastAppliedLast);
                }
                else
                {
                    _mvSeq = 0;
                    _lastAppliedLast = "";
                    zdo.Set(ChessSeats.KeyLast, "");
                }
            }
            catch { }
        }

        private static int ParseSeq(string last)
        {
            if (string.IsNullOrEmpty(last)) return 0;
            int bar = last.IndexOf('|');
            if (bar <= 0) return 0;
            return int.TryParse(last.Substring(0, bar), out int seq) ? seq : 0;
        }

        private Image MakeHighlight(Color c)
        {
            var img = NewImg(_boardRT, "Hl", FiresRoundedSkin.RoundedSprite(6, c), Image.Type.Sliced);
            PlaceSquare(img.rectTransform, 0, 1.0f);
            img.gameObject.SetActive(false);
            return img;
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────────────────────────
        private static float SqW => (FX1 - FX0) * BoardW / 8f;
        private static float SqH => (FY1 - FY0) * BoardH / 8f;

        private Vector2 SquareLocal(int sq)
        {
            if (_flipView) sq = 63 - sq;               // Black plays from their own side of the board
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
            float x = -BoardW / 2f + (FX0 + (f + 0.5f) * (FX1 - FX0) / 8f) * BoardW;
            float y = -BoardH / 2f + ((1f - FY1) + (r + 0.5f) * (FY1 - FY0) / 8f) * BoardH;
            return new Vector2(x, y);
        }

        private void PlaceSquare(RectTransform rt, int sq, float scale)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(SqW * scale, SqH * scale);
            rt.anchoredPosition = SquareLocal(sq);
        }

        // ── render ──────────────────────────────────────────────────────────────────────────────────────────────
        private void RefreshBoard()
        {
            if (ThreeD)
            {
                // The real pieces ARE the board (ChessBoardVisual mirrors the ZDO) — refresh the trays and hand
                // selection/legal state to the session's hint stones.
                RefreshTrays();
                Session3D?.OnStateChanged();
                return;
            }

            for (int sq = 0; sq < 64; sq++)
            {
                int code = _game.Squares[sq];
                var img = _pieceImgs[sq];
                var halo = _pieceHalos[sq];
                if (code == 0)
                {
                    img.gameObject.SetActive(false);
                    halo.gameObject.SetActive(false);
                    continue;
                }
                bool isWhite = ChessBoard.ColorOf(code) == ChessColor.White;
                var spr = ChessTableGame.FlatPiece(ChessBoard.TypeOf(code));
                halo.gameObject.SetActive(true);
                halo.sprite = spr;
                halo.color = isWhite ? WhiteHalo : BlackHalo;
                PlaceSquare(halo.rectTransform, sq, 0.98f);
                img.gameObject.SetActive(true);
                img.sprite = spr;
                img.color = isWhite ? WhiteTint : BlackTint;
                img.raycastTarget = false;
                PlaceSquare(img.rectTransform, sq, 0.86f);
            }

            SetHl(_selImg, _selected);
            SetHl(_lastFromImg, _lastFrom);
            SetHl(_lastToImg, _lastTo);
            int checkSq = ChessMoveGen.InCheck(_game, _game.SideToMove) ? _game.KingSquare(_game.SideToMove) : -1;
            SetHl(_checkImg, checkSq);

            int d = 0;
            if (_selected >= 0)
                foreach (var m in _legal)
                    if (m.From == _selected && d < _dots.Length)
                    {
                        var dot = _dots[d++];
                        dot.gameObject.SetActive(true);
                        bool cap = _game.Squares[m.To] != 0 || m.IsEnPassant;
                        dot.color = cap ? new Color(0.95f, 0.35f, 0.25f, 0.9f) : new Color(0.98f, 0.84f, 0.42f, 0.85f);
                        dot.rectTransform.anchoredPosition = SquareLocal(m.To);
                    }
            for (; d < _dots.Length; d++) _dots[d].gameObject.SetActive(false);

            RefreshTrays();
        }

        private void SetHl(Image img, int sq)
        {
            if (img == null) return;
            if (sq < 0) { img.gameObject.SetActive(false); return; }
            img.gameObject.SetActive(true);
            img.rectTransform.anchoredPosition = SquareLocal(sq);
        }

        private void RefreshTrays()
        {
            for (int i = 0; i < _trayW.Length; i++)
            {
                bool w = i < _capturedByWhite.Count;
                _trayW[i].gameObject.SetActive(w);
                if (w) { _trayW[i].sprite = ChessTableGame.FlatPiece(ChessBoard.TypeOf(_capturedByWhite[i])); _trayW[i].preserveAspect = true; _trayW[i].color = new Color(0.55f, 0.50f, 0.52f); }
                bool bl = i < _capturedByBlack.Count;
                _trayB[i].gameObject.SetActive(bl);
                if (bl) { _trayB[i].sprite = ChessTableGame.FlatPiece(ChessBoard.TypeOf(_capturedByBlack[i])); _trayB[i].preserveAspect = true; _trayB[i].color = new Color(0.93f, 0.90f, 0.82f); }
            }
        }

        // ── input ───────────────────────────────────────────────────────────────────────────────────────────────
        private void OnCell(int sq)
        {
            var mySide = _mp ? _myColor : ChessColor.White;
            if (_busy || _over || _game.SideToMove != mySide) return;

            if (sq == _selected) { _selected = -1; RefreshBoard(); return; }   // re-click deselects

            if (_selected >= 0)
            {
                var targets = new List<ChessMove>();
                foreach (var m in _legal) if (m.From == _selected && m.To == sq) targets.Add(m);
                if (targets.Count > 0)
                {
                    if (targets.Count > 1) { StartCoroutine(AwaitPromotion(targets)); return; }
                    StartCoroutine(PlayMove(targets[0], thenAi: !_mp));
                    return;
                }
            }

            int code = _game.Squares[sq];
            if (code != 0 && ChessBoard.ColorOf(code) == mySide)
            {
                _selected = sq;
                _legal = ChessMoveGen.GenerateLegal(_game);
            }
            else _selected = -1;
            RefreshBoard();
        }

        // ── move execution ──────────────────────────────────────────────────────────────────────────────────────
        private IEnumerator PlayMove(ChessMove m, bool thenAi)
        {
            _busy = true;
            _selected = -1;

            int capturedCode = m.IsEnPassant
                ? _game.Squares[_game.SideToMove == ChessColor.White ? m.To - 8 : m.To + 8]
                : _game.Squares[m.To];
            bool byWhite = _game.SideToMove == ChessColor.White;
            var moverType = ChessBoard.TypeOf(_game.Squares[m.From]);

            // slide animation
            var img = _pieceImgs[m.From];
            if (img != null && img.gameObject.activeSelf)
            {
                if (_pieceHalos[m.From] != null) _pieceHalos[m.From].gameObject.SetActive(false);   // halo snaps on Refresh
                Vector2 a = SquareLocal(m.From), b = SquareLocal(m.To);
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / 0.16f;
                    img.rectTransform.anchoredPosition = Vector2.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                    yield return null;
                }
            }

            _game.MakeMove(m);
            _lastFrom = m.From; _lastTo = m.To;
            if (_game.HalfmoveClock == 0) _posKeys.Clear();   // pawn move / capture — earlier positions can't recur
            _posKeys.Add(ChessAI.HashPosition(_game));

            bool localMove = !_mp || byWhite == (_myColor == ChessColor.White);
            string who = _mp ? (localMove ? "You" : _oppName) : (byWhite ? "You" : "Skald");
            string line;
            if (m.IsCastle) line = who + ": castle " + (ChessBoard.FileOf(m.To) == 6 ? "king" : "queen") + "-side";
            else
            {
                line = who + ": " + PieceName(moverType)
                     + (capturedCode != 0 ? " takes " + PieceName(ChessBoard.TypeOf(capturedCode)) : "")
                     + " → " + ChessBoard.SquareName(m.To);
                if (m.Promo != ChessPiece.None) line += " = " + PieceName(m.Promo);
            }
            if (ChessMoveGen.InCheck(_game, _game.SideToMove)) line += "  +";
            AddMoveLog(line);
            if (localMove) WriteBoardState(m);   // the opponent's client already wrote their own move
            if (capturedCode != 0)
            {
                (byWhite ? _capturedByWhite : _capturedByBlack).Add(capturedCode);
                _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "take1" : "take2", 0.85f, UnityEngine.Random.Range(0.95f, 1.06f));
            }
            else _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "move1" : "move2", 0.75f, UnityEngine.Random.Range(0.95f, 1.08f));

            RefreshBoard();
            if (CheckEnd()) { _busy = false; yield break; }

            if (thenAi) StartCoroutine(AiTurn());
            else
            {
                if (_mp) UpdateMpStatus();
                else _statusText.text = ChessMoveGen.InCheck(_game, ChessColor.White) ? "Check! Your move." : "Your move.";
                _busy = false;
            }
        }

        private IEnumerator AiTurn()
        {
            _busy = true;
            _statusText.text = "Skald ponders…";
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 1.2f));

            var clone = ChessAI.Clone(_game);
            ChessTableGame.AiTier(_aiDepth, out int maxDepth, out int timeMs, out float err);
            var keys = new List<ulong>(_posKeys);
            var seed = _rng.Next();
            _aiTask = Task.Run<object>(() =>
            {
                var rng = new System.Random(seed);
                return ChessAI.FindBestTimed(clone, maxDepth, timeMs, rng, err, keys, out var best) ? (object)best : null;
            });
            while (!_aiTask.IsCompleted) yield return null;

            var result = _aiTask.Result;
            _aiTask = null;
            if (result == null) { CheckEnd(); _busy = false; yield break; }
            yield return PlayMove((ChessMove)result, thenAi: false);
        }

        private bool CheckEnd()
        {
            var legal = ChessMoveGen.GenerateLegal(_game);
            string outcome = ChessAI.Outcome(_game, legal);
            if (outcome == null)
            {
                ulong cur = ChessAI.HashPosition(_game);
                int reps = 0;
                for (int i = 0; i < _posKeys.Count; i++) if (_posKeys[i] == cur) reps++;
                if (reps >= 3) outcome = "Draw — threefold repetition.";
            }
            if (outcome == null) return false;
            _over = true;
            _statusText.text = outcome + "   —   New Game to play again.";
            return true;
        }

        private void NewGame()
        {
            StopAllCoroutines();
            _game.SetStartPosition();
            _posKeys.Clear();
            _posKeys.Add(ChessAI.HashPosition(_game));
            _capturedByWhite.Clear(); _capturedByBlack.Clear();
            _moveLog.Clear();
            if (_logText != null) _logText.text = "";
            _selected = -1; _lastFrom = -1; _lastTo = -1;
            _busy = false; _over = false;
            if (_promoPanel != null) _promoPanel.SetActive(false);
            _statusText.text = "Your move.";
            _audio?.PlayPitched("start", 0.8f, 1f);
            WriteBoardState();   // reset the world board too
            RefreshBoard();
        }

        // ── promotion ───────────────────────────────────────────────────────────────────────────────────────────
        private int _promoChoice;

        private void BuildPromoPanel(Transform parent)
        {
            _promoPanel = NewImg(parent, "PromoPanel", ChessTableGame.Panel, Image.Type.Sliced).gameObject;
            Place((RectTransform)_promoPanel.transform, 320, 110, 35, 0);
            var lbl = NewTxt(_promoPanel.transform, "Promote to…", 15, new Color(1f, 0.88f, 0.6f));
            Place(lbl.rectTransform, 300, 22, 0, 36);
            var kinds = new[] { ChessPiece.Queen, ChessPiece.Rook, ChessPiece.Bishop, ChessPiece.Knight };
            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var b = MakeButton(_promoPanel.transform, "", new Vector2(0.5f, 0.5f), new Vector2(64, 64), new Vector2(-114 + i * 76, -12), () => _promoChoice = (int)kind);
                var pi = NewImg(b.transform, "P", ChessTableGame.FlatPiece(kind), Image.Type.Simple);
                pi.preserveAspect = true;
                pi.color = _mp && _myColor == ChessColor.Black ? BlackTint : WhiteTint;
                Place(pi.rectTransform, 52, 52, 0, 0);
            }
            _promoPanel.SetActive(false);
        }

        private IEnumerator AwaitPromotion(List<ChessMove> options)
        {
            _busy = true;
            _promoChoice = 0;
            _promoPanel.SetActive(true);
            _promoPanel.transform.SetAsLastSibling();
            while (_promoChoice == 0) yield return null;
            _promoPanel.SetActive(false);

            foreach (var m in options)
                if ((int)m.Promo == _promoChoice) { yield return PlayMove(m, thenAi: true); yield break; }
            _busy = false;
        }

        private void Update()
        {
            // Config-driven window scale + position (title-bar drag / corner grip; also F8) — hot applied.
            // The 3D seat has no cabinet, but the multiplayer relay must keep ticking either way.
            if (_cabRT != null)
            {
                float s = ChessTableGame.UiScaleCfg != null ? Mathf.Clamp(ChessTableGame.UiScaleCfg.Value, 0.6f, 1.6f) : 1f;
                if (!Mathf.Approximately(s, _lastScale)) { _lastScale = s; _cabRT.localScale = new Vector3(s, s, 1f); }
                float ox = ChessTableGame.OffsetXCfg != null ? ChessTableGame.OffsetXCfg.Value : 0f;
                float oy = ChessTableGame.OffsetYCfg != null ? ChessTableGame.OffsetYCfg.Value : 0f;
                _cabRT.anchoredPosition = new Vector2(ox, oy);
            }

            if (_mp) MpTick();
        }

        // ── multiplayer heartbeat + move relay ──────────────────────────────────────────────────────────────────
        private void MpTick()
        {
            if (_boundBoard == null || !_boundBoard.IsValid()) return;
            bool meWhite = _myColor == ChessColor.White;

            _mpBeatT += Time.deltaTime;
            if (_mpBeatT > 5f) { _mpBeatT = 0f; ChessSeats.Heartbeat(_boundBoard, meWhite); }

            _mpPollT += Time.deltaTime;
            if (_mpPollT < 0.35f) return;
            _mpPollT = 0f;

            var opp = ChessSeats.Read(_boundBoard, !meWhite);
            bool oppLiveNow = ChessSeats.IsLive(opp);
            if (oppLiveNow && !string.IsNullOrEmpty(opp.Name)) _oppName = opp.Name;
            if (oppLiveNow != _oppLive)
            {
                _oppLive = oppLiveNow;
                if (_titleText != null) _titleText.text = _oppLive ? "Chess — vs " + _oppName : "Chess — the Jarl's Board";
                UpdateMpStatus();
            }

            if (_busy || _over) return;

            string last = _boundBoard.GetZDO().GetString(ChessSeats.KeyLast, "");
            if (last == _lastAppliedLast) return;

            int seq = ParseSeq(last);
            if (string.IsNullOrEmpty(last) || seq < _mvSeq)
            {
                ResyncFromZdo(freshGame: true);   // opponent hit New Game (or the state rolled back)
                return;
            }

            // A new move landed — replay it through the normal path when it lines up; otherwise snap to the state.
            var parts = last.Split('|');
            if (parts.Length >= 4 && seq == _mvSeq + 1 && _game.SideToMove != _myColor
                && int.TryParse(parts[1], out int from) && int.TryParse(parts[2], out int to) && int.TryParse(parts[3], out int promo))
            {
                foreach (var lm in ChessMoveGen.GenerateLegal(_game))
                    if (lm.From == from && lm.To == to && (int)lm.Promo == promo)
                    {
                        _mvSeq = seq;
                        _lastAppliedLast = last;
                        StartCoroutine(PlayMove(lm, thenAi: false));
                        return;
                    }
            }
            ResyncFromZdo(freshGame: false);
        }

        private void UpdateMpStatus()
        {
            if (!_mp || _statusText == null || _over) return;
            if (!_oppLive)
            {
                _statusText.text = "Waiting for an opponent — a second player uses this board and picks Join. Minimize to keep playing.";
                return;
            }
            bool mine = _game.SideToMove == _myColor;
            bool chk = ChessMoveGen.InCheck(_game, _game.SideToMove);
            _statusText.text = mine ? (chk ? "Check! Your move." : "Your move.") : _oppName + (chk ? " is in check…" : " is thinking…");
        }

        private void ResyncFromZdo(bool freshGame)
        {
            if (_boundBoard == null || !_boundBoard.IsValid()) return;
            string saved = _boundBoard.GetZDO().GetString(ChessBoardVisual.ZdoKey, "");
            string last = _boundBoard.GetZDO().GetString(ChessSeats.KeyLast, "");
            if (string.IsNullOrEmpty(saved) || !ChessBoard.Deserialize(saved, _game)) return;
            _mvSeq = ParseSeq(last);
            _lastAppliedLast = last;
            _posKeys.Clear();
            _posKeys.Add(ChessAI.HashPosition(_game));
            _selected = -1; _lastFrom = -1; _lastTo = -1;
            _busy = false; _over = false;
            if (freshGame)
            {
                _capturedByWhite.Clear(); _capturedByBlack.Clear();
                _moveLog.Clear();
                if (_logText != null) _logText.text = "";
                _audio?.PlayPitched("start", 0.8f, 1f);
            }
            else AddMoveLog("— resynced —");
            if (_promoPanel != null) _promoPanel.SetActive(false);
            if (!CheckEnd()) UpdateMpStatus();
            RefreshBoard();
        }

        private void MinimizeToChip()
        {
            if (!_mp || _boundBoard == null || !_boundBoard.IsValid()) return;
            var nv = _boundBoard;
            var color = _myColor;
            bool meWhite = color == ChessColor.White;
            bool gameOn = _oppLive;
            _minimized = true;
            _overlay.Close();
            Modules.TableGames.QueueDockChip.Show(
                gameOn ? "Chess — game in progress" : "Chess — waiting for an opponent…",
                gameOn ? "Chess — your move! Click to return." : "Chess — opponent found! Click to play.",
                () =>
                {
                    if (!gameOn) return ChessSeats.IsLive(ChessSeats.Read(nv, !meWhite));
                    var st = nv.IsValid() ? nv.GetZDO().GetString(ChessBoardVisual.ZdoKey, "") : "";
                    var pp = st.Split('|');
                    return pp.Length > 1 && pp[1] == (meWhite ? "0" : "1");   // ready = it's my turn
                },
                () => ChessTableGame.OpenMpAuto(nv, color),
                () => ChessSeats.Release(nv, meWhite),
                () => ChessSeats.Heartbeat(nv, meWhite));
        }

        private static void AddDrag(GameObject go, Action<UnityEngine.EventSystems.PointerEventData> onDrag)
        {
            var trig = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Drag };
            entry.callback.AddListener(d => onDrag((UnityEngine.EventSystems.PointerEventData)d));
            trig.triggers.Add(entry);
        }

        private static string PieceName(ChessPiece t)
        {
            switch (t)
            {
                case ChessPiece.Pawn: return "Pawn";
                case ChessPiece.Knight: return "Knight";
                case ChessPiece.Bishop: return "Bishop";
                case ChessPiece.Rook: return "Rook";
                case ChessPiece.Queen: return "Queen";
                case ChessPiece.King: return "King";
                default: return "?";
            }
        }

        private void AddMoveLog(string line)
        {
            _moveLog.Add(line);
            if (_moveLog.Count > 200) _moveLog.RemoveAt(0);
            if (_logText == null) return;
            _logText.text = string.Join("\n", _moveLog);
            Canvas.ForceUpdateCanvases();
            if (_logScroll != null) _logScroll.verticalNormalizedPosition = 0f;   // pin to the newest line
        }

        private void OnDestroy()
        {
            _audio?.Dispose();
            // Leaving for real drops the seat; minimizing hands the heartbeat to the dock chip instead.
            if (_mp && !_minimized && _boundBoard != null)
                ChessSeats.Release(_boundBoard, _myColor == ChessColor.White);
        }

        // ── tiny UI helpers (poker-table idiom) ─────────────────────────────────────────────────────────────────
        private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>(); img.sprite = ChessTableGame.Btn; img.type = Image.Type.Sliced;
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
