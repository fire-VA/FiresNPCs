using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.Minigames;
using FiresRPGmaker.Modules.Poker;

namespace FiresRPGmaker.UI.Screens.PlayerPanels.Minigames
{
    /// <summary>
    /// The Texas Hold'em table — a standalone FiresRoundedSkin overlay that drives the verified <see cref="PokerGame"/>
    /// engine against NPC seats. You (seat 0, bottom) play against AI; hole cards are the Kenney CC0 deck, community
    /// cards deal to the centre, and Fold / Check-Call / Bet controls act for you while AI seats auto-act on a short
    /// timer. Local vs-AI for now; the multiplayer queue layers on the same engine next. Client-only.
    /// </summary>
    public static class PokerTableGame
    {
        private static readonly Dictionary<string, Sprite> _cards = new Dictionary<string, Sprite>();
        private static Sprite _back, _panel, _bar, _felt, _btn, _btnHi, _chip, _dealer;
        private static Sprite[] _chips;
        private static bool _loaded;

        /// <summary>Seconds a seated human has to act before auto-fold. Bound to config in Ascend.Setup; hot-reloads.</summary>
        public static BepInEx.Configuration.ConfigEntry<int> TurnSecondsCfg;
        /// <summary>Poker window scale + position (drag the title bar / corner grip in-game). Hot-reload.</summary>
        public static BepInEx.Configuration.ConfigEntry<float> UiScaleCfg, OffsetXCfg, OffsetYCfg;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Open(int startChips = 1000, int smallBlind = 10)
        {
            if (IsClientOnly()) return;
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresPokerTable", 6500);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<PokerTableController>();
            ctrl.Build(overlay, startChips, smallBlind);
        }

        /// <summary>Open the table bound to an online tournament — the server deals; this window renders snapshots.</summary>
        public static void OpenNet(string tableId)
        {
            if (IsClientOnly() || string.IsNullOrEmpty(tableId)) return;
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresPokerTable", 6500);
            if (overlay == null) return;
            var ctrl = overlay.gameObject.AddComponent<PokerTableController>();
            ctrl.BuildNet(overlay, tableId);
        }

        internal static Sprite CardFace(Card c)
        {
            string rank = c.Rank <= 10 ? c.Rank.ToString()
                        : c.Rank == 11 ? "J" : c.Rank == 12 ? "Q" : c.Rank == 13 ? "K" : "A";
            string name = $"card{c.Suit}{rank}.png";
            if (_cards.TryGetValue(name, out var s)) return s;
            s = FiresMinigameSprites.Load(Assembly.GetExecutingAssembly(), name);
            _cards[name] = s;
            return s;
        }

        internal static Sprite Back => _back;
        internal static Sprite Panel => _panel;
        internal static Sprite Bar => _bar;
        internal static Sprite Felt => _felt;
        internal static Sprite Btn => _btn;
        internal static Sprite BtnHi => _btnHi;
        internal static Sprite Chip => _chip;
        internal static Sprite Dealer => _dealer;
        internal static Sprite[] Chips => _chips;

        private static void EnsureAssets()
        {
            if (_loaded) return;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            _back = FiresMinigameSprites.Load(asm, "cardBack.png");
            _chip = FiresMinigameSprites.Load(asm, "chipRed.png");
            _chips = new[]
            {
                FiresMinigameSprites.Load(asm, "chipRed.png"), FiresMinigameSprites.Load(asm, "chipBlue.png"),
                FiresMinigameSprites.Load(asm, "chipGreen.png"), FiresMinigameSprites.Load(asm, "chipWhite.png"),
            };
            _panel = FiresRoundedSkin.RoundedSprite(20, new Color(0.11f, 0.088f, 0.06f, 0.99f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _btn = FiresRoundedSkin.RoundedSprite(10, new Color(0.34f, 0.23f, 0.10f, 1f), new Color(0.62f, 0.42f, 0.16f, 0.8f), 1);
            _btnHi = FiresRoundedSkin.RoundedSprite(10, new Color(0.47f, 0.32f, 0.14f, 1f), new Color(0.85f, 0.62f, 0.26f, 1f), 1);
            _dealer = FiresRoundedSkin.RoundedSprite(15, new Color(0.93f, 0.82f, 0.55f, 1f), new Color(0.28f, 0.19f, 0.09f, 1f), 2);
            _felt = MakeFelt();
        }

        private static Sprite MakeFelt()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var center = new Color(0.13f, 0.28f, 0.18f);
            var edge = new Color(0.05f, 0.11f, 0.07f);
            var rng = new System.Random(909);
            var c = new Vector2(S / 2f, S / 2f);
            float maxd = S * 0.66f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) / maxd);
                    d = d * d * (3f - 2f * d);
                    var col = Color.Lerp(center, edge, d);
                    float g = (float)(rng.NextDouble() - 0.5) * 0.015f;
                    px[y * S + x] = new Color(col.r + g, col.g + g, col.b + g, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    internal sealed class PokerTableController : MonoBehaviour
    {
        private const float CabW = 1040f, CabH = 660f;
        private const float CardW = 60f, CardH = 81f;
        private const float BoardCardW = 66f, BoardCardH = 90f;
        private const float AiThinkMin = 2.0f, AiThinkMax = 3.5f, ShowdownHold = 5f;
        private const int DefaultTurnSeconds = 60;

        private FiresMinigameOverlay _overlay;
        private PokerGame _game;
        private System.Random _rng;
        private RectTransform _table;
        private TMP_Text _potText, _statusText;
        private readonly Image[] _board = new Image[5];
        private SeatUI[] _seats;
        private GameObject _controls;
        private Button _foldBtn, _callBtn, _halfBtn, _potBtn, _allInBtn;
        private TMP_Text _callLabel, _halfLabel, _potLabel;
        private TMP_InputField _betInput;
        private readonly Button[] _presetBtns = new Button[5];
        private Slider _betSlider;
        private bool _sliderGuard;
        private Image _timerBg, _timerFill;
        private TMP_Text _timerText;
        private RectTransform _cabRT;
        private float _lastUiScale = -1f;
        private int _feedHand = -1, _feedCursor;
        private Button _minusBtn, _plusBtn, _raiseBtn;
        private TMP_Text _raiseLabel;
        private int _betAmount;
        private TMP_Text _yourChipsText;
        private FiresMinigameAudio _audio;
        private Image[] _potChips;
        private int _prevBoardCount = -1, _prevPot = -1;

        private float _aiTimer, _phaseTimer, _turnTimer;
        private int _lastToAct = -2;
        private int _humanStartChips;
        private bool _revealed;
        private bool _tableClosed;
        private bool _showdownStarted, _showdownDone;
        private readonly bool[] _shown = new bool[4];
        private int _showdownChoice;   // 0 pending, 1 show, 2 muck
        private Button _showBtn, _muckBtn;

        // ── online table (server-run PokerNetHost; this window renders per-viewer snapshots) ────────────────────
        private bool _net;
        private string _tableId;
        private int _mySeat;
        private bool _netMyTurn, _netCanCheck, _netCanRaise;
        private int _netToCall, _netMin, _netMax;
        private float _netTurnRemain = -1f;
        private readonly byte[] _mirA = new byte[8], _mirB = new byte[8];   // wire card bytes (255 = hidden)
        private float _pingT;
        private int _feedCursorAbs;
        private bool _quit;
        private bool _cashMode;                 // open table: real currency — chip art swaps to the coin's own icon
        private Sprite _coinSprite;
        private Image _chipIconImg;
        private TMP_Text _titleText;

        // Board reveal pacing + deal slide: streets flip in one at a time (suspense gaps on all-in runouts, with
        // opponents' cards and the winner line held back until the river), and fresh hole cards slide from the deck.
        private int _visBoard;
        private bool _revealingBoard;
        private Coroutine _revealCo;
        private bool _dealing;
        private Coroutine _dealCo;
        private bool _firstSnap = true;

        public void BuildNet(FiresMinigameOverlay overlay, string tableId)
        {
            _net = true;
            _tableId = tableId;
            Build(overlay, 1000, 5);
            overlay.OnClosed += () =>
            {
                // Esc/minimize keeps your seat — the dock chip takes over the heartbeat. Leave quits for real.
                if (!_quit && PokerNetClient.ActiveTable != null) PokerNetClient.ParkChip();
            };
        }

        private static int TurnSeconds => PokerTableGame.TurnSecondsCfg != null ? Mathf.Max(10, PokerTableGame.TurnSecondsCfg.Value) : DefaultTurnSeconds;
        private float NextAiDelay() => UnityEngine.Random.Range(AiThinkMin, AiThinkMax);

        private sealed class SeatUI
        {
            public TMP_Text name; public Image[] cards = new Image[2]; public TMP_Text bet; public TMP_Text action;
            public GameObject dealer; public Image ring; public Image[] chipStack;
        }

        public void Build(FiresMinigameOverlay overlay, int startChips, int smallBlind)
        {
            _overlay = overlay;
            _rng = new System.Random(unchecked(Environment.TickCount * 31 + 17));
            _audio = new FiresMinigameAudio(Assembly.GetExecutingAssembly());
            _audio.RegisterClip("deal1", "FiresPokerDeal1.wav"); _audio.RegisterClip("deal2", "FiresPokerDeal2.wav");
            _audio.RegisterClip("flip1", "FiresPokerFlip1.wav"); _audio.RegisterClip("flip2", "FiresPokerFlip2.wav");
            _audio.RegisterClip("chips1", "FiresPokerChips1.wav"); _audio.RegisterClip("chips2", "FiresPokerChips2.wav"); _audio.RegisterClip("chips3", "FiresPokerChips3.wav");

            PokerGame.IntegrityAlarm = msg =>
            {
                Debug.LogError(msg);
                if (_statusText != null) _statusText.text = "<color=#FF6A5A>CARD ERROR — see log (F5)</color>";
            };

            _game = new PokerGame(_rng) { SmallBlind = smallBlind, BigBlind = smallBlind * 2 };
            _game.Seats.Add(new PokerSeat { Index = 0, Name = PlayerName(), IsAI = false, Chips = startChips });
            _game.Seats.Add(new PokerSeat { Index = 1, Name = "Bjorn", IsAI = true, Chips = startChips });
            _game.Seats.Add(new PokerSeat { Index = 2, Name = "Sigrid", IsAI = true, Chips = startChips });
            _game.Seats.Add(new PokerSeat { Index = 3, Name = "Hakon", IsAI = true, Chips = startChips });

            var root = overlay.PlayField;
            var cab = NewImg(root, "Cabinet", PokerTableGame.Panel, Image.Type.Sliced);
            Place(cab.rectTransform, CabW, CabH, 0, 0);
            _cabRT = cab.rectTransform;
            cab.raycastTarget = true;

            // The table's action feed rides the chat window as a "Poker" tab while we're seated.
            global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerFeedReset();
            global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerActive = true;

            var titleBar = NewImg(cab.rectTransform, "TitleBar", PokerTableGame.Bar, Image.Type.Sliced);
            var tb = titleBar.rectTransform; tb.anchorMin = new Vector2(0, 1); tb.anchorMax = new Vector2(1, 1); tb.pivot = new Vector2(0.5f, 1);
            tb.sizeDelta = new Vector2(-24, 44); tb.anchoredPosition = new Vector2(0, -12);
            _titleText = NewTxt(titleBar.rectTransform, _net ? "Hold'em — Online Table" : "Hold'em — the Gambler's Table", 22, new Color(1f, 0.85f, 0.5f));
            FillRect(_titleText.rectTransform);

            // Drag the title bar to move; drag the corner grip to scale. Both persist via config.
            titleBar.raycastTarget = true;
            AddDrag(titleBar.gameObject, d =>
            {
                float s = Mathf.Max(0.6f, _lastUiScale);
                if (PokerTableGame.OffsetXCfg != null) PokerTableGame.OffsetXCfg.Value += d.delta.x / s;
                if (PokerTableGame.OffsetYCfg != null) PokerTableGame.OffsetYCfg.Value += d.delta.y / s;
            });
            var grip = NewImg(cab.rectTransform, "ScaleGrip", FiresRoundedSkin.RoundedSprite(6, new Color(0.85f, 0.62f, 0.26f, 0.55f)), Image.Type.Sliced);
            var griprt = grip.rectTransform;
            griprt.anchorMin = griprt.anchorMax = new Vector2(0f, 0f); griprt.pivot = new Vector2(0f, 0f);
            griprt.sizeDelta = new Vector2(18f, 18f); griprt.anchoredPosition = new Vector2(4f, 4f);
            grip.raycastTarget = true;
            AddDrag(grip.gameObject, d =>
            {
                if (PokerTableGame.UiScaleCfg == null) return;
                float delta = -(d.delta.x + d.delta.y) * 0.003f;
                PokerTableGame.UiScaleCfg.Value = Mathf.Clamp(PokerTableGame.UiScaleCfg.Value + delta, 0.6f, 1.6f);
            });

            // Oval-ish felt table.
            var felt = NewImg(cab.rectTransform, "Felt", PokerTableGame.Felt, Image.Type.Simple);
            Place(felt.rectTransform, 820, 400, 0, 30);

            // Pot + status in the centre — dark plate under the total, online-table style.
            var potPlate = NewImg(felt.rectTransform, "PotPlate", FiresRoundedSkin.RoundedSprite(9, new Color(0.05f, 0.045f, 0.04f, 0.8f)), Image.Type.Sliced);
            Place(potPlate.rectTransform, 176, 30, 0, -44);
            _potText = NewTxt(felt.rectTransform, "Total : 0", 19, new Color(1f, 0.88f, 0.55f));
            Place(_potText.rectTransform, 300, 28, 0, -44);   // just below the board cards (clear of the top seat's text)
            _potChips = new Image[15];   // pile to the left of the board; splits into 2 then 3 short stacks as the pot grows
            for (int i = 0; i < _potChips.Length; i++)
            {
                int col = i / 5, row = i % 5;
                var c = NewImg(felt.rectTransform, "PotChip" + i, PokerTableGame.Chip, Image.Type.Simple);
                Place(c.rectTransform, 26, 26, -250 + col * 24, 6 + row * 5); c.gameObject.SetActive(false); _potChips[i] = c;
            }
            _statusText = NewTxt(felt.rectTransform, "", 15, new Color(0.85f, 0.9f, 0.8f));
            Place(_statusText.rectTransform, 640, 24, 0, -95);   // just under the pot — clear of your name/action row

            // Community cards.
            for (int i = 0; i < 5; i++)
            {
                var card = NewImg(felt.rectTransform, "Board" + i, PokerTableGame.Back, Image.Type.Simple);
                Place(card.rectTransform, BoardCardW, BoardCardH, (i - 2) * (BoardCardW + 8), 24);
                card.gameObject.SetActive(false);
                _board[i] = card;
            }

            // Seats: 0 bottom (you), 1 left, 2 top, 3 right.
            var pos = new[] { new Vector2(0, -224), new Vector2(-372, 36), new Vector2(0, 214), new Vector2(372, 36) };
            _seats = new SeatUI[4];
            for (int i = 0; i < 4; i++) _seats[i] = BuildSeat(cab.rectTransform, i, pos[i]);

            // Your chip readout — left side, clear of the bet slider's end and the turn-timer disc.
            _chipIconImg = NewImg(cab.rectTransform, "YourChipIcon", PokerTableGame.Chip, Image.Type.Simple);
            Place(_chipIconImg.rectTransform, 40, 40, -368, -232);
            _yourChipsText = NewTxt(cab.rectTransform, "0", 22, new Color(1f, 0.88f, 0.55f));
            Place(_yourChipsText.rectTransform, 110, 28, -288, -232);

            BuildControls(cab.rectTransform);

            // Leave button, top-right of the cabinet. Online: leaving quits the tournament; Minimize keeps the seat.
            var leave = MakeButton(cab.rectTransform, "Leave", new Vector2(1, 1), new Vector2(96, 34), new Vector2(-62, -31), () =>
            {
                if (_net) { _quit = true; PokerNetClient.Quit(); }
                _overlay.Close();
            });
            if (_net)
                MakeButton(cab.rectTransform, "Minimize", new Vector2(1, 1), new Vector2(110, 34), new Vector2(-173, -31), () => _overlay.Close());

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;

            if (_net) InitNet();
            else StartNextHand();
        }

        private void InitNet()
        {
            _game.Phase = PokerPhase.NotStarted;
            foreach (var s in _game.Seats) { s.HasCards = false; s.Chips = 0; s.Bet = 0; s.Committed = 0; }
            for (int i = 0; i < _mirA.Length; i++) { _mirA[i] = 255; _mirB[i] = 255; }
            if (_controls != null) _controls.SetActive(false);
            if (_statusText != null) _statusText.text = "Taking your seat…";
            PokerNetClient.StateForUi += OnNetState;
            PokerNetClient.EventForUi += OnNetEvent;
            var cached = PokerNetClient.Latest;
            if (cached != null) { try { ApplySnapshot(cached); } catch { } }
            else
            {
                var pkg = new ZPackage();
                pkg.Write(PokerNetHost.ActResync);
                global::FiresCore.Tables.FiresTableNet.SendAction(_tableId, pkg);
            }
        }

        private SeatUI BuildSeat(Transform parent, int index, Vector2 at)
        {
            var s = new SeatUI();
            var holder = new GameObject("Seat" + index, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(parent, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f); holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = at; holder.sizeDelta = new Vector2(200, 150);

            s.ring = NewImg(holder, "Ring", PokerTableGame.Bar, Image.Type.Sliced);
            Place(s.ring.rectTransform, 176, 40, 0, -46);
            s.ring.color = new Color(0.85f, 0.62f, 0.26f, 0f);

            bool human = index == 0;
            for (int k = 0; k < 2; k++)
            {
                var card = NewImg(holder, "Card" + k, PokerTableGame.Back, Image.Type.Simple);
                Place(card.rectTransform, CardW, CardH, (k == 0 ? -1 : 1) * (CardW * 0.55f), human ? 60 : 14);   // human cards ride high, clear of the bet slider
                card.gameObject.SetActive(false);
                s.cards[k] = card;
            }

            // Dark nameplate behind name + stack (the online-table look).
            var plate = NewImg(holder, "Plate", FiresRoundedSkin.RoundedSprite(9, new Color(0.05f, 0.045f, 0.04f, 0.88f), new Color(0.62f, 0.42f, 0.16f, 0.5f), 1), Image.Type.Sliced);
            Place(plate.rectTransform, 150, human ? 28 : 46, 0, human ? 118 : -54);

            s.name = NewTxt(holder, "", 15, human ? new Color(0.62f, 0.85f, 1f) : new Color(0.62f, 0.85f, 1f));
            Place(s.name.rectTransform, 190, 22, 0, human ? 118 : -54);   // human name up top; AI names dropped for card padding
            // Bottom (you) and top seats: bet + action live in a side column NEXT TO the cards (dead-center they
            // sat on the cards/nameplate — and the top seat's text crowded the board). You = left of your cards;
            // top seat = right of theirs. The side seats keep the toward-the-centre layout.
            bool top = index == 2;
            float betY = human ? 28f : top ? -32f : -86f;
            float chipX = human ? -158f : top ? 158f : -30f;
            // The bet text (120 wide, centred, left-aligned) starts 6 px right of the 22 px chip stack; the old fixed X
            // put "bet 20" on the chips' edge (Fire 10-08: Sigrid's chip overlapped its text).
            float betX = chipX + 11f + 6f + 60f;
            s.bet = NewTxt(holder, "", 14, new Color(1f, 0.85f, 0.5f));
            Place(s.bet.rectTransform, 120, 20, betX, betY); s.bet.alignment = TextAlignmentOptions.MidlineLeft;   // number sits clear to the right of the chip stack
            s.chipStack = new Image[4];
            for (int k = 0; k < s.chipStack.Length; k++)
            {
                var c = NewImg(holder, "Chip" + k, PokerTableGame.Chip, Image.Type.Simple);
                Place(c.rectTransform, 22, 22, chipX, betY + k * 4); c.gameObject.SetActive(false); s.chipStack[k] = c;
            }
            s.action = NewTxt(holder, "", 14, new Color(0.72f, 0.92f, 1f)); // last decision, called out beside the seat
            float actW = human ? 170 : top ? 160 : 190;
            float actX = human ? -152 : top ? 155 : 0;
            float actY = human ? 92 : top ? 44 : -110;
            Place(s.action.rectTransform, actW, 22, actX, actY);

            // Dealer button — a small gold disc, parked per-seat toward the table centre and clear of that seat's
            // name/cards/bet so it never overlaps anything as it moves round the table.
            s.dealer = NewImg(holder, "Dealer", PokerTableGame.Dealer, Image.Type.Sliced).gameObject;
            var dl = NewTxt(s.dealer.transform, "D", 13, new Color(0.2f, 0.13f, 0.06f)); FillRect(dl.rectTransform);
            Vector2 dOff =
                index == 0 ? new Vector2(-104f, 56f) :  // you (bottom): clear to the left of your (raised) cards
                index == 1 ? new Vector2(96f, 8f) :     // Bjorn (left): toward centre
                index == 2 ? new Vector2(98f, 6f) :     // Sigrid (top): to the side
                             new Vector2(-96f, 8f);      // Hakon (right): toward centre
            Place((RectTransform)s.dealer.transform, 26, 26, dOff.x, dOff.y);
            s.dealer.SetActive(false);
            return s;
        }

        private void BuildControls(Transform parent)
        {
            _controls = new GameObject("Controls", typeof(RectTransform));
            var rt = (RectTransform)_controls.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 14); rt.sizeDelta = new Vector2(CabW, 48);

            // Fold | Call | [ − amount + ] | Raise | All-In  (symmetric around the bet input)
            _foldBtn  = MakeButton(rt, "Fold",   new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(-305, 0), OnFold);
            _callBtn  = MakeButton(rt, "Check",  new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(-157, 0), OnCheckCall);
            _minusBtn = MakeButton(rt, "−", new Vector2(0.5f, 0.5f), new Vector2(34, 42),  new Vector2(-62, 0),  OnBetMinus);
            _betInput = MakeInput (rt,           new Vector2(90, 42),  new Vector2(0, 0));
            _plusBtn  = MakeButton(rt, "+",      new Vector2(0.5f, 0.5f), new Vector2(34, 42),  new Vector2(62, 0),   OnBetPlus);
            _raiseBtn = MakeButton(rt, "Raise",  new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(157, 0),  OnRaiseTo);
            _allInBtn = MakeButton(rt, "All-In", new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(305, 0),  OnAllIn);
            _callLabel = _callBtn.GetComponentInChildren<TMP_Text>();
            _raiseLabel = _raiseBtn.GetComponentInChildren<TMP_Text>();

            // ACR-style sizing presets — each sets the bet box; Raise confirms.
            string[] pn = { "Min", "½ Pot", "¾ Pot", "Pot", "All-In" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                _presetBtns[i] = MakeButton(rt, pn[i], new Vector2(0.5f, 0.5f), new Vector2(60, 26), new Vector2(-132 + i * 66, 40), () => OnPreset(idx));
                var pt = _presetBtns[i].GetComponentInChildren<TMP_Text>();
                if (pt != null) pt.fontSize = 12;
            }

            // Bet slider — sweeps min-raise to all-in, feeding the bet box live.
            var sgo = new GameObject("BetSlider", typeof(RectTransform));
            var srt = (RectTransform)sgo.transform;
            srt.SetParent(rt, false);
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f); srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(420, 22); srt.anchoredPosition = new Vector2(0, 68);
            var track = NewImg(srt, "Track", FiresRoundedSkin.RoundedSprite(5, new Color(0.1f, 0.075f, 0.05f, 1f), new Color(0.62f, 0.42f, 0.16f, 0.6f), 1), Image.Type.Sliced);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(0f, 10f); trt.anchoredPosition = Vector2.zero;
            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(srt, false);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f); handleArea.offsetMax = new Vector2(-8f, 0f);
            var handle = NewImg(handleArea, "Handle", FiresRoundedSkin.RoundedSprite(8, new Color(0.93f, 0.78f, 0.42f, 1f), new Color(0.28f, 0.19f, 0.09f, 1f), 1), Image.Type.Sliced);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(16f, 24f);
            _betSlider = sgo.AddComponent<Slider>();
            _betSlider.targetGraphic = handle;
            _betSlider.handleRect = handle.rectTransform;
            _betSlider.direction = Slider.Direction.LeftToRight;
            _betSlider.wholeNumbers = true;
            _betSlider.onValueChanged.AddListener(v => { if (!_sliderGuard && MyTurn) SetBet(Mathf.RoundToInt(v)); });

            // Time bank — radial countdown by the Fold button.
            _timerBg = NewImg(rt, "TimerBg", FiresRoundedSkin.RoundedSprite(24, new Color(0.07f, 0.055f, 0.04f, 0.95f), new Color(0.62f, 0.42f, 0.16f, 0.8f), 1), Image.Type.Sliced);
            Place(_timerBg.rectTransform, 48, 48, -412, 8);
            _timerFill = NewImg(rt, "TimerFill", FiresRoundedSkin.RoundedSprite(22, new Color(0.85f, 0.68f, 0.3f, 0.95f)), Image.Type.Simple);
            _timerFill.type = Image.Type.Filled;
            _timerFill.fillMethod = Image.FillMethod.Radial360;
            _timerFill.fillOrigin = (int)Image.Origin360.Top;
            _timerFill.fillClockwise = false;
            Place(_timerFill.rectTransform, 42, 42, -412, 8);
            // Light with a dark outline: readable over the gold fill AND the dark disc (Fire 10-08: the "3" was hidden).
            _timerText = NewTxt(rt, "", 15, new Color(1f, 0.96f, 0.84f));
            _timerText.fontStyle = FontStyles.Bold;
            _timerText.outlineWidth = 0.25f;
            _timerText.outlineColor = new Color32(20, 14, 8, 255);
            _timerText.transform.SetAsLastSibling();
            Place(_timerText.rectTransform, 48, 20, -412, 8);
            SetTimerVisible(false);

            // Showdown choice buttons — flanking the cards, only visible while the choice is live.
            _showBtn = MakeButton(rt, "Show", new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(-240, 124), () => _showdownChoice = 1);
            _muckBtn = MakeButton(rt, "Muck", new Vector2(0.5f, 0.5f), new Vector2(140, 42), new Vector2(240, 124), () => _showdownChoice = 2);
            _showBtn.gameObject.SetActive(false);
            _muckBtn.gameObject.SetActive(false);
        }

        private void SetTimerVisible(bool on)
        {
            if (_timerBg != null) _timerBg.gameObject.SetActive(on);
            if (_timerFill != null) _timerFill.gameObject.SetActive(on);
            if (_timerText != null) _timerText.gameObject.SetActive(on);
        }

        private static void AddDrag(GameObject go, Action<UnityEngine.EventSystems.PointerEventData> onDrag)
        {
            var trig = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Drag };
            entry.callback.AddListener(d => onDrag((UnityEngine.EventSystems.PointerEventData)d));
            trig.triggers.Add(entry);
        }

        // Preset → raise-to amounts per the standard online sizing row (mirror math; bounds clamp in SetBet).
        private void OnPreset(int idx)
        {
            if (!MyTurn) return;
            switch (idx)
            {
                case 0: SetBet(_net ? _netMin : _game.MinRaiseTo(_game.Seats[0])); break;
                case 1: SetBet(_game.CurrentBet + Mathf.Max(_game.MinRaise, Mathf.RoundToInt(_game.Pot * 0.5f))); break;
                case 2: SetBet(_game.CurrentBet + Mathf.Max(_game.MinRaise, Mathf.RoundToInt(_game.Pot * 0.75f))); break;
                case 3: SetBet(_game.CurrentBet + Mathf.Max(_game.MinRaise, Mathf.RoundToInt(_game.Pot * 1f))); break;
                case 4: SetBet(_net ? _netMax : _game.MaxRaiseTo(_game.Seats[0])); break;
            }
        }

        private TMP_InputField MakeInput(Transform parent, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("BetInput", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var bg = go.AddComponent<Image>(); bg.sprite = PokerTableGame.Btn; bg.type = Image.Type.Sliced; bg.color = new Color(0.13f, 0.09f, 0.05f, 1f);

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(go.transform, false); FillRect(viewport); viewport.offsetMin = new Vector2(8, 4); viewport.offsetMax = new Vector2(-8, -4);
            var txt = NewTxt(viewport, "0", 18, new Color(1f, 0.92f, 0.7f)); FillRect(txt.rectTransform); txt.raycastTarget = true;
            input.textViewport = viewport; input.textComponent = txt;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.caretColor = new Color(1f, 0.92f, 0.7f); input.customCaretColor = true; input.caretWidth = 2;
            input.onEndEdit.AddListener(OnBetEdited);
            input.text = "0";
            return input;
        }

        // ── turn driver ─────────────────────────────────────────────────────────────────────────────────────────
        private void Update()
        {
            // Window scale + position (title-bar drag / corner grip; also editable in F8) — hot applied.
            if (_cabRT != null)
            {
                float s = PokerTableGame.UiScaleCfg != null ? Mathf.Clamp(PokerTableGame.UiScaleCfg.Value, 0.6f, 1.6f) : 1f;
                if (!Mathf.Approximately(s, _lastUiScale)) { _lastUiScale = s; _cabRT.localScale = new Vector3(s, s, 1f); }
                float ox = PokerTableGame.OffsetXCfg != null ? PokerTableGame.OffsetXCfg.Value : 0f;
                float oy = PokerTableGame.OffsetYCfg != null ? PokerTableGame.OffsetYCfg.Value : 0f;
                _cabRT.anchoredPosition = new Vector2(ox, oy);
            }

            if (_tableClosed || _game == null) return;

            if (_net) { NetTick(); return; }

            if (_game.Phase == PokerPhase.Complete)
            {
                if (_revealingBoard) return;   // the runout is still flipping — hold the showdown for the river
                if (!_showdownStarted) { _showdownStarted = true; StartCoroutine(ShowdownSequence()); }
                else if (_showdownDone)
                {
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) StartNextHand();
                }
                return;
            }

            var cur = _game.Current;
            if (cur == null) return;

            // New actor this frame → start their clock (each AI thinks a beat; the human gets the turn timer).
            if (_game.ToAct != _lastToAct)
            {
                _lastToAct = _game.ToAct;
                if (cur.IsAI) _aiTimer = NextAiDelay();
                else { _turnTimer = TurnSeconds; SetBet(_game.MinRaiseTo(_game.Seats[0])); }
                Refresh();
            }

            if (cur.IsAI)
            {
                SetTimerVisible(false);
                _aiTimer -= Time.deltaTime;
                if (_aiTimer <= 0f) { PokerAI.TakeTurn(_game, _rng); Refresh(); }
            }
            else
            {
                _turnTimer -= Time.deltaTime;
                if (_turnTimer <= 0f) { SetTimerVisible(false); AutoAct(); return; }
                if (_statusText != null) _statusText.text = "Your turn.";
                SetTimerVisible(true);
                float frac = Mathf.Clamp01(_turnTimer / TurnSeconds);
                if (_timerFill != null)
                {
                    _timerFill.fillAmount = frac;
                    _timerFill.color = Color.Lerp(new Color(0.85f, 0.3f, 0.2f, 0.95f), new Color(0.85f, 0.68f, 0.3f, 0.95f), frac);
                }
                if (_timerText != null) _timerText.text = Mathf.CeilToInt(Mathf.Max(0f, _turnTimer)).ToString();
            }
        }

        private void StartNextHand()
        {
            int solvent = 0; foreach (var s in _game.Seats) if (s.Chips > 0) solvent++;
            if (_game.Seats[0].Chips <= 0) { EndTable("You're out of chips."); return; }
            if (solvent < 2) { EndTable("You cleaned out the table!"); return; }

            _revealed = false;
            _showdownStarted = false; _showdownDone = false; _showdownChoice = 0;
            System.Array.Clear(_shown, 0, _shown.Length);
            if (_showBtn != null) _showBtn.gameObject.SetActive(false);
            if (_muckBtn != null) _muckBtn.gameObject.SetActive(false);
            _lastToAct = -2;
            _humanStartChips = _game.Seats[0].Chips;
            _prevBoardCount = -1; _prevPot = -1;   // don't fire flip/chip cues on the fresh deal's first render
            _visBoard = 0;
            _game.StartHand();
            _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "deal1" : "deal2", 0.85f, UnityEngine.Random.Range(0.97f, 1.05f));
            Refresh();
            StartDealSlide();
        }

        private void EndTable(string why)
        {
            if (_statusText != null) _statusText.text = why + "  —  Leave when ready.";
            if (_controls != null) _controls.SetActive(false);
            _game.Phase = PokerPhase.NotStarted;   // stop the driver; Leave button closes
        }

        // ── human actions (online: the buttons SEND; the server is the only dealer) ─────────────────────────────
        private PokerSeat Me => _game.Seats[_net ? Mathf.Clamp(_mySeat, 0, _game.Seats.Count - 1) : 0];
        private bool MyTurn => _net
            ? _netMyTurn && _game != null && _game.Phase != PokerPhase.Complete && _game.Phase != PokerPhase.NotStarted
            : _game != null && _game.Phase != PokerPhase.Complete && _game.Current != null && !_game.Current.IsAI;

        private void SendNetAct(byte code, int arg = 0)
        {
            var pkg = new ZPackage();
            pkg.Write(code);
            if (code == PokerNetHost.ActRaiseTo) pkg.Write(arg);
            global::FiresCore.Tables.FiresTableNet.SendAction(_tableId, pkg);
            _netMyTurn = false;
            SetTimerVisible(false);
            RefreshControls();
        }

        private void OnFold()
        {
            if (!MyTurn) return;
            if (_net) { SendNetAct(PokerNetHost.ActFold); return; }
            _game.Fold(); After();
        }
        private void OnCheckCall()
        {
            if (!MyTurn) return;
            if (_net) { SendNetAct(PokerNetHost.ActCheckCall); return; }
            _game.CheckCall(); After();
        }
        private void OnAllIn()
        {
            if (!MyTurn) return;
            if (_net) { SendNetAct(PokerNetHost.ActRaiseTo, _netMax); return; }
            _game.Raise(_game.MaxRaiseTo(_game.Current)); After();
        }
        private void OnRaise(float potFrac)
        {
            if (!MyTurn || _net) return;
            var s = _game.Current;
            int inc = Mathf.Max(_game.MinRaise, Mathf.RoundToInt(_game.Pot * potFrac));
            _game.Raise(Mathf.Min(_game.MaxRaiseTo(s), _game.CurrentBet + inc));
            After();
        }
        private void After() { _lastToAct = -2; Refresh(); }

        // ── bet input (− amount + , custom + clamped to legal bounds) ────────────────────────────────────────────
        private int BetStep => _game != null ? Mathf.Max(_game.BigBlind, 1) : 1;
        private void OnBetMinus() { if (MyTurn) SetBet(_betAmount - BetStep); }
        private void OnBetPlus() { if (MyTurn) SetBet(_betAmount + BetStep); }
        private void OnBetEdited(string s) { if (int.TryParse(s, out var v)) SetBet(v); else SetBetInput(); }
        private void OnRaiseTo()
        {
            if (!MyTurn) return;
            if (_net)
            {
                if (_netCanRaise) SendNetAct(PokerNetHost.ActRaiseTo, Mathf.Clamp(_betAmount, _netMin, _netMax));
                return;
            }
            if (!_game.CanRaise(_game.Seats[0])) return;
            var me = _game.Seats[0];
            _game.Raise(Mathf.Clamp(_betAmount, _game.MinRaiseTo(me), _game.MaxRaiseTo(me)));
            After();
        }
        private void SetBet(int v)
        {
            if (_game == null) return;
            int min = _net ? _netMin : _game.MinRaiseTo(_game.Seats[0]);
            int max = _net ? _netMax : _game.MaxRaiseTo(_game.Seats[0]);
            _betAmount = Mathf.Clamp(v, min, max);
            SetBetInput();
        }
        private void SetBetInput()
        {
            if (_betInput != null) _betInput.SetTextWithoutNotify(_betAmount.ToString());
            if (_betSlider != null) { _sliderGuard = true; _betSlider.SetValueWithoutNotify(_betAmount); _sliderGuard = false; }
        }

        // ── showdown theater: aggressor shows first, losers may muck, uncontested winner may show ────────────────
        private IEnumerator ShowdownSequence()
        {
            var g = _game;
            if (g.ShowOrder.Count > 1)
            {
                foreach (int idx in g.ShowOrder)
                {
                    var seat = g.Seats[idx];
                    bool winner = seat.Won > 0;
                    bool mustShow = idx == g.ShowOrder[0];   // last aggressor (or first left of button) shows
                    if (!seat.IsAI)
                    {
                        if (winner || mustShow) { _shown[idx] = true; seat.LastAction = "shows"; }
                        else yield return AwaitShowMuck(seat, false);
                    }
                    else if (winner || mustShow || UnityEngine.Random.value < 0.2f)
                    {
                        _shown[idx] = true; seat.LastAction = "shows";
                        _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "flip1" : "flip2", 0.8f, UnityEngine.Random.Range(0.95f, 1.06f));
                        foreach (var c in _seats[idx].cards) StartCoroutine(FlipIn(c));
                    }
                    else { seat.Mucked = true; seat.LastAction = "mucks"; }
                    Refresh();
                    yield return new WaitForSeconds(0.7f);
                }
                _audio?.PlayPitched("chips" + UnityEngine.Random.Range(1, 4), 0.85f, 0.95f);   // pot pushed
            }
            else
            {
                // Uncontested — everyone else folded. The winner may show or keep it hidden.
                PokerSeat winner = null;
                foreach (var s in g.Seats) if (s.InHand) { winner = s; break; }
                if (winner != null && !winner.IsAI) yield return AwaitShowMuck(winner, true);
                else if (winner != null && UnityEngine.Random.value < 0.12f) { _shown[winner.Index] = true; winner.LastAction = "shows"; }
                Refresh();
            }
            _revealed = true;
            _showdownDone = true;
            _phaseTimer = ShowdownHold;
            Refresh();
        }

        private IEnumerator AwaitShowMuck(PokerSeat seat, bool uncontested)
        {
            _showdownChoice = 0;
            var showLbl = _showBtn.GetComponentInChildren<TMP_Text>();
            var muckLbl = _muckBtn.GetComponentInChildren<TMP_Text>();
            if (showLbl != null) showLbl.text = "Show";
            if (muckLbl != null) muckLbl.text = uncontested ? "Keep Hidden" : "Muck";
            _showBtn.gameObject.SetActive(true); _muckBtn.gameObject.SetActive(true);
            if (_statusText != null) _statusText.text = uncontested ? "Show your winning hand?" : "Show or muck your hand?";

            float t = 8f;
            while (_showdownChoice == 0 && t > 0f) { t -= Time.deltaTime; yield return null; }
            _showBtn.gameObject.SetActive(false); _muckBtn.gameObject.SetActive(false);

            if (_showdownChoice == 1) { _shown[seat.Index] = true; seat.LastAction = "shows"; }
            else if (!uncontested) { seat.Mucked = true; seat.LastAction = "mucks"; }
        }

        // ── online: ping, local turn-clock display, snapshot apply ──────────────────────────────────────────────
        private void NetTick()
        {
            _pingT += Time.deltaTime;
            if (_pingT > 4f) { _pingT = 0f; global::FiresCore.Tables.FiresTableNet.Ping(_tableId); }

            if (_netMyTurn && _netTurnRemain >= 0f && _game.Phase != PokerPhase.Complete)
            {
                _netTurnRemain -= Time.deltaTime;
                SetTimerVisible(true);
                float frac = Mathf.Clamp01(_netTurnRemain / Mathf.Max(1f, TurnSeconds));
                if (_timerFill != null)
                {
                    _timerFill.fillAmount = frac;
                    _timerFill.color = Color.Lerp(new Color(0.85f, 0.3f, 0.2f, 0.95f), new Color(0.85f, 0.68f, 0.3f, 0.95f), frac);
                }
                if (_timerText != null) _timerText.text = Mathf.CeilToInt(Mathf.Max(0f, _netTurnRemain)).ToString();
            }
            else SetTimerVisible(false);
        }

        private void OnNetState(string tableId, int version, ZPackage pkg)
        {
            if (!_net || tableId != _tableId) return;
            try { ApplySnapshot(pkg); }
            catch (Exception ex) { Debug.LogWarning("[Poker] snapshot apply: " + ex.Message); }
        }

        // Mirror the per-viewer snapshot into the local PokerGame shell — the whole render layer reads it unchanged.
        private void ApplySnapshot(ZPackage p)
        {
            if (p.ReadByte() != 2) return;
            var g = _game;
            g.Phase = (PokerPhase)p.ReadByte();
            int hand = p.ReadInt();
            g.Button = p.ReadInt(); g.ToAct = p.ReadInt(); g.CurrentBet = p.ReadInt(); g.MinRaise = p.ReadInt();
            g.SmallBlind = p.ReadInt(); g.BigBlind = p.ReadInt(); p.ReadInt(); _mySeat = p.ReadInt();
            bool cash = p.ReadBool();
            string currency = p.ReadString();
            if (cash && !_cashMode) EnterCashMode(currency);

            bool newHand = hand != g.HandNumber;
            bool dealNow = newHand && !_firstSnap;
            g.HandNumber = hand;
            if (newHand)
            {
                _prevBoardCount = -1; _prevPot = -1;
                _feedCursorAbs = 0;
                _visBoard = 0;
                _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "deal1" : "deal2", 0.85f, UnityEngine.Random.Range(0.97f, 1.05f));
            }

            int n = p.ReadInt();
            for (int i = 0; i < n; i++)
            {
                int idx = p.ReadInt();
                string name = p.ReadString();
                bool isAI = p.ReadBool(); bool occ = p.ReadBool();
                int chips = p.ReadInt(); int bet = p.ReadInt(); int committed = p.ReadInt();
                bool hasCards = p.ReadBool(); bool folded = p.ReadBool(); bool mucked = p.ReadBool(); bool allIn = p.ReadBool();
                int won = p.ReadInt(); string lastAction = p.ReadString();
                byte a = p.ReadByte(); byte b = p.ReadByte();
                if (idx < 0 || idx >= g.Seats.Count) continue;
                var s = g.Seats[idx];
                s.Name = name; s.IsAI = isAI; s.Occupied = occ; s.Chips = chips; s.Bet = bet; s.Committed = committed;
                s.HasCards = hasCards; s.Folded = folded; s.Mucked = mucked; s.AllIn = allIn; s.Won = won;
                s.LastAction = string.IsNullOrEmpty(lastAction) ? null : lastAction;
                if (idx < _mirA.Length) { _mirA[idx] = a; _mirB[idx] = b; }
                if (a != 255) s.A = Card.FromIndex(a);
                if (b != 255) s.B = Card.FromIndex(b);
            }

            g.Board.Clear();
            int bc = p.ReadInt();
            for (int i = 0; i < bc; i++) g.Board.Add(Card.FromIndex(p.ReadByte()));

            bool wasMyTurn = _netMyTurn;
            _netMyTurn = p.ReadBool();
            _netToCall = p.ReadInt();
            _netCanCheck = p.ReadBool();
            _netCanRaise = p.ReadBool();
            _netMin = p.ReadInt();
            _netMax = p.ReadInt();
            _netTurnRemain = p.ReadSingle();

            int logStart = p.ReadInt();
            int lc = p.ReadInt();
            g.Log.Clear();
            for (int i = 0; i < lc; i++)
            {
                string line = p.ReadString();
                g.Log.Add(line);
                int abs = logStart + i;
                if (abs >= _feedCursorAbs)
                {
                    global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerFeedAdd(line);
                    _feedCursorAbs = abs + 1;
                }
            }

            g.Results.Clear();
            int rc = p.ReadInt();
            for (int i = 0; i < rc; i++)
            {
                var r = new PotResult { Desc = p.ReadString(), Amount = p.ReadInt() };
                int wc = p.ReadInt();
                for (int j = 0; j < wc; j++) r.Winners.Add(p.ReadInt());
                g.Results.Add(r);
            }
            g.ShowOrder.Clear();
            int sc = p.ReadInt();
            for (int i = 0; i < sc; i++) g.ShowOrder.Add(p.ReadInt());

            if (newHand) _humanStartChips = Me.Chips + Me.Committed;   // snapshot lands post-blinds
            if (_netMyTurn && !wasMyTurn && _netCanRaise) SetBet(_netMin);
            if (_controls != null) _controls.SetActive(g.Phase != PokerPhase.NotStarted);
            if (_firstSnap) { _firstSnap = false; _visBoard = g.Board.Count; }   // joining mid-hand — no reveal theater
            Refresh();
            if (dealNow) StartDealSlide();
        }

        private void OnNetEvent(string tableId, int code, ZPackage pkg)
        {
            if (!_net || tableId != _tableId) return;
            bool closed = code == global::FiresCore.Tables.FiresTableNet.EvtClosed;
            bool over = code == PokerNetHost.EvtTournamentOver;
            bool unseated = code == global::FiresCore.Tables.FiresTableNet.EvtUnseated;
            if (!closed && !over && !unseated) return;

            string line = unseated ? "You left the table." : "The table has closed.";
            if (over)
            {
                line = "The table has closed.";
                try
                {
                    string name = pkg.ReadString();
                    int chips = pkg.ReadInt();
                    if (!string.IsNullOrEmpty(name)) line = name + " wins the table with " + chips + " chips!";
                }
                catch { }
            }
            if (_statusText != null) _statusText.text = line + "   —   Leave when ready.";
            if (_controls != null) _controls.SetActive(false);
            _game.Phase = PokerPhase.NotStarted;
            _netMyTurn = false;
            SetTimerVisible(false);
        }

        // Open (cash) table: the money is real, so the chip art becomes the actual currency item's icon.
        private void EnterCashMode(string currency)
        {
            _cashMode = true;
            string display = currency;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(currency) : null;
            var item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (item != null && item.m_itemData != null)
            {
                try { _coinSprite = item.m_itemData.GetIcon(); } catch { }
                if (item.m_itemData.m_shared != null && !string.IsNullOrEmpty(item.m_itemData.m_shared.m_name))
                    display = Localization.instance.Localize(item.m_itemData.m_shared.m_name);
            }
            if (_titleText != null) _titleText.text = "Hold'em — Open Table (" + display + ")";
            if (_coinSprite != null && _chipIconImg != null) _chipIconImg.sprite = _coinSprite;
        }

        // Human turn timer expired → check if it's free, otherwise fold.
        private void AutoAct()
        {
            if (!MyTurn) return;
            var me = _game.Current;
            if (_game.ToCall(me) == 0) _game.CheckCall(); else _game.Fold();
            After();
        }

        // Online: rotate the view so YOUR engine seat always renders at the bottom position.
        private int View(int engineIdx) => _net ? (engineIdx - _mySeat + _seats.Length) % _seats.Length : engineIdx;

        // ── render ──────────────────────────────────────────────────────────────────────────────────────────────
        private void Refresh()
        {
            // Publish new engine-log lines to the chat window's Poker tab (online publishes in ApplySnapshot).
            if (!_net)
            {
                if (_game.HandNumber != _feedHand) { _feedHand = _game.HandNumber; _feedCursor = 0; }
                while (_feedCursor < _game.Log.Count)
                    global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerFeedAdd(_game.Log[_feedCursor++]);
            }

            _potText.text = "Total : " + _game.Pot;

            SyncBoardReveal();
            for (int i = 0; i < 5; i++)
            {
                bool show = i < _visBoard && i < _game.Board.Count;
                _board[i].gameObject.SetActive(show);
                if (show) _board[i].sprite = PokerTableGame.CardFace(_game.Board[i]);
            }

            for (int i = 0; i < _seats.Length; i++)
            {
                var g = _game.Seats[i]; var v = _seats[View(i)];
                int di = View(i);
                v.name.text = di == 0 ? g.Name : g.Name + "\n<color=#8BE28B>" + g.Chips + "</color>";   // stack in green, online-style
                // A fold shows once: the action line already reads "Fold" (Fire 10-08: Hakon showed "FOLD" and "Fold").
                bool actionShown = v.action != null && !string.IsNullOrEmpty(g.LastAction);
                v.bet.text = g.Bet > 0 ? "bet " + g.Bet
                    : g.Folded ? (actionShown ? "" : "<color=#E8D06A>FOLD</color>")
                    : g.AllIn ? "all-in" : "";
                if (v.action != null) v.action.text = g.LastAction ?? "";
                v.dealer.SetActive(i == _game.Button && g.Occupied);

                bool toAct = _game.Current == g && _game.Phase != PokerPhase.Complete;
                v.ring.color = new Color(0.85f, 0.62f, 0.26f, toAct ? 0.5f : 0f);

                if (_dealing) continue;   // the slide-in owns the card visuals until the deal lands

                // Online face-up truth is the wire byte (the server already redacted); solo keeps the theater rules.
                bool faceUp = _net ? (i < _mirA.Length && _mirA[i] != 255) : (!g.IsAI || (_shown[i] && g.InHand));
                if (_revealingBoard && _net && i != _mySeat) faceUp = false;   // no spoilers while the board runs out
                bool hideFolded = (g.Folded || g.Mucked) && (_net ? i != _mySeat : g.IsAI);
                for (int k = 0; k < 2; k++)
                {
                    var card = v.cards[k];
                    if (!g.HasCards || hideFolded) { card.gameObject.SetActive(false); continue; }
                    card.gameObject.SetActive(true);
                    card.color = g.Folded ? new Color(1, 1, 1, 0.35f) : Color.white;
                    card.sprite = faceUp ? PokerTableGame.CardFace(k == 0 ? g.A : g.B) : PokerTableGame.Back;
                }
            }

            for (int i = 0; i < _seats.Length; i++) SetChipStack(_seats[View(i)].chipStack, _game.Seats[i].Bet, 50);
            SetPotStacks(_game.Pot);

            if (_prevPot >= 0 && _game.Pot > _prevPot) _audio?.PlayPitched("chips" + UnityEngine.Random.Range(1, 4), 0.7f, UnityEngine.Random.Range(0.95f, 1.08f));
            _prevBoardCount = _game.Board.Count;
            _prevPot = _game.Pot;

            if (_yourChipsText != null) _yourChipsText.text = Me.Chips.ToString();
            RefreshControls();
        }

        // Height of a chip stack scales with the amount (1 chip per `perChip`, capped to the pre-made images).
        private void SetChipStack(Image[] stack, int amount, int perChip)
        {
            if (stack == null) return;
            int n = amount <= 0 ? 0 : Mathf.Clamp(1 + amount / perChip, 1, stack.Length);
            var chips = PokerTableGame.Chips;
            for (int i = 0; i < stack.Length; i++)
            {
                bool on = i < n;
                stack[i].gameObject.SetActive(on);
                if (on) stack[i].sprite = _cashMode && _coinSprite != null ? _coinSprite
                    : chips != null && chips.Length > 0 ? chips[i % chips.Length] ?? PokerTableGame.Chip : PokerTableGame.Chip;
            }
        }

        // Pot chips pile left of the board and split into 2 then 3 short stacks (columns of 5) as the pot climbs.
        private void SetPotStacks(int amount)
        {
            if (_potChips == null) return;
            int n = amount <= 0 ? 0 : Mathf.Clamp(1 + amount / 30, 1, _potChips.Length);
            var chips = PokerTableGame.Chips;
            for (int i = 0; i < _potChips.Length; i++)
            {
                bool on = i < n;
                _potChips[i].gameObject.SetActive(on);
                if (on) _potChips[i].sprite = _cashMode && _coinSprite != null ? _coinSprite
                    : chips != null && chips.Length > 0 ? chips[(i / 5) % chips.Length] ?? PokerTableGame.Chip : PokerTableGame.Chip;
            }
        }

        // Streets flip in one group at a time; when several land at once (an all-in runout) the gaps stretch for
        // suspense and the showdown holds until the river is down.
        private void SyncBoardReveal()
        {
            int target = _game.Board.Count;
            if (target < _visBoard)
            {
                if (_revealCo != null) { StopCoroutine(_revealCo); _revealCo = null; }
                _revealingBoard = false;
                _visBoard = target;
                return;
            }
            if (target == _visBoard || _revealingBoard) return;
            _revealCo = StartCoroutine(RevealBoard());
        }

        private IEnumerator RevealBoard()
        {
            _revealingBoard = true;
            while (_visBoard < _game.Board.Count)
            {
                int next = _visBoard < 3 ? Mathf.Min(3, _game.Board.Count) : _visBoard + 1;
                for (int i = _visBoard; i < next && i < 5; i++)
                {
                    _board[i].gameObject.SetActive(true);
                    _board[i].sprite = PokerTableGame.CardFace(_game.Board[i]);
                    StartCoroutine(FlipIn(_board[i]));
                }
                _visBoard = next;
                _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "flip1" : "flip2", 0.8f, UnityEngine.Random.Range(0.95f, 1.06f));
                if (_visBoard < _game.Board.Count)
                {
                    if (_statusText != null) _statusText.text = "All in — running it out…";
                    yield return new WaitForSeconds(2.2f);
                }
            }
            _revealingBoard = false;
            _revealCo = null;
            Refresh();
        }

        // Fresh hole cards slide out from the deck to each seat, two passes round the table.
        private void StartDealSlide()
        {
            if (_dealCo != null) StopCoroutine(_dealCo);
            _dealCo = StartCoroutine(DealSlide());
        }

        private IEnumerator DealSlide()
        {
            _dealing = true;
            Vector3 deckFrom = _board[2] != null ? _board[2].rectTransform.position : Vector3.zero;
            foreach (var s in _seats)
                foreach (var c in s.cards) c.gameObject.SetActive(false);

            for (int k = 0; k < 2; k++)
            {
                for (int off = 1; off <= _seats.Length; off++)
                {
                    int i = ((_game.Button < 0 ? 0 : _game.Button) + off) % _seats.Length;
                    if (i >= _game.Seats.Count) continue;
                    var g = _game.Seats[i];
                    if (!g.HasCards) continue;
                    var card = _seats[View(i)].cards[k];
                    var rt = card.rectTransform;
                    Vector3 target = rt.localPosition;
                    Vector3 start = rt.parent.InverseTransformPoint(deckFrom);
                    rt.localPosition = start;
                    card.sprite = PokerTableGame.Back;
                    card.color = Color.white;
                    card.gameObject.SetActive(true);
                    if ((k + off) % 2 == 0) _audio?.PlayPitched(UnityEngine.Random.value < 0.5f ? "deal1" : "deal2", 0.5f, UnityEngine.Random.Range(1.02f, 1.12f));
                    float t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime / 0.11f;
                        rt.localPosition = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                        yield return null;
                    }
                    rt.localPosition = target;
                }
            }
            _dealing = false;
            _dealCo = null;
            Refresh();   // own cards flip face-up through the normal render
        }

        // A card flips/deals into view — scale-X 0→1.
        private IEnumerator FlipIn(Image img)
        {
            if (img == null) yield break;
            var rt = img.rectTransform;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.22f;
                rt.localScale = new Vector3(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)), 1f, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private void OnDestroy()
        {
            global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerActive = false;
            global::FiresRPGmaker.Modules.ChatSystem.ChatChannelUI.PokerFeedVersion++;
            if (_net)
            {
                PokerNetClient.StateForUi -= OnNetState;
                PokerNetClient.EventForUi -= OnNetEvent;
            }
            _audio?.Dispose();
        }

        private void RefreshControls()
        {
            bool mine = MyTurn;
            _controls.SetActive(_game.Phase != PokerPhase.NotStarted);
            SetInteractable(_foldBtn, mine);
            SetInteractable(_callBtn, mine);
            var me = Me;
            bool canRaise = mine && (_net ? _netCanRaise : _game.CanRaise(me));
            SetInteractable(_minusBtn, canRaise);
            SetInteractable(_plusBtn, canRaise);
            SetInteractable(_raiseBtn, canRaise);
            foreach (var pb in _presetBtns) SetInteractable(pb, canRaise);
            if (_betSlider != null)
            {
                _betSlider.interactable = canRaise;
                if (canRaise)
                {
                    _sliderGuard = true;
                    _betSlider.minValue = _net ? _netMin : _game.MinRaiseTo(me);
                    _betSlider.maxValue = _net ? _netMax : _game.MaxRaiseTo(me);
                    _betSlider.SetValueWithoutNotify(Mathf.Clamp(_betAmount, _betSlider.minValue, _betSlider.maxValue));
                    _sliderGuard = false;
                }
            }
            if (_betInput != null) _betInput.interactable = canRaise;
            SetInteractable(_allInBtn, mine && me.Chips > 0);

            if (mine)
            {
                int call = _net ? _netToCall : _game.ToCall(me);
                _callLabel.text = call > 0 ? "Call " + call : "Check";
                if (canRaise) SetBet(_betAmount);   // re-clamp to current legal bounds
                // The amount lives in the box — the button just says what it does.
                if (_raiseLabel != null) _raiseLabel.text = call > 0 ? "Raise" : "Bet";
                if (_net && _statusText != null) _statusText.text = "Your turn.";
                // solo: the "Your turn — Ns" countdown is written per-frame in Update
            }
            else if (_game.Phase == PokerPhase.Complete)
            {
                if (_revealingBoard) { _statusText.text = "All in — running it out…"; return; }   // no spoilers pre-river
                // Net over the whole hand — so an all-in that only wins a side pot / gets a refund reads as the loss
                // it actually is, not a false "you won".
                var me0 = Me;
                int net = me0.Chips - _humanStartChips;
                string mine2 = me0.Folded ? "You folded."
                             : net > 0 ? $"<color=#9CC24A>You won {net}.</color>"
                             : net < 0 ? $"<color=#E0907A>You lost {-net}.</color>  ({me0.Chips} chips left)"
                             : "You broke even.";
                string table = _game.Log.Count > 0 ? _game.Log[_game.Log.Count - 1] : "";
                _statusText.text = mine2 + (string.IsNullOrEmpty(table) ? "" : "   ·   " + table);
            }
            else if (_game.Current != null) _statusText.text = _game.Current.Name + " is thinking…";
        }

        // ── tiny UI helpers ─────────────────────────────────────────────────────────────────────────────────────
        private static string PlayerName() => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "You";

        private Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>(); img.sprite = PokerTableGame.Btn; img.type = Image.Type.Sliced;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb = btn.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.fadeDuration = 0.06f; btn.colors = cb;
            btn.onClick.AddListener(onClick);
            var t = NewTxt(go.transform, label, 15, new Color(1f, 0.9f, 0.65f)); FillRect(t.rectTransform);
            return btn;
        }

        private static void SetInteractable(Button b, bool on)
        {
            if (b == null) return;
            b.interactable = on;
            var img = b.targetGraphic as Image;
            if (img != null) img.sprite = on ? PokerTableGame.BtnHi : PokerTableGame.Btn;
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
