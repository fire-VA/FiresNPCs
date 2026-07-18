using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.Minigames;

namespace FiresRPGmaker.UI.Screens.PlayerPanels.Minigames
{
    /// <summary>
    /// "Casting the Bones" — the gambler's dice minigame. A leather pouch on green felt: hold LMB to rattle it (harder
    /// the longer you hold), release to cast — the dice tumble out across the felt and settle. Pure presentation: the
    /// gambler panel draws the reward BEFORE opening this and grants it in <paramref name="onComplete"/>, so the dice
    /// faces are flavour and the configured payout is untouched (the "theater" model). Built on the shared FUC overlay
    /// (<see cref="FiresMinigameOverlay"/>) framed with <see cref="FiresRoundedSkin"/> to match the approved mockup.
    /// Client-only; the tumble tech here is reused by the Viking duel and the rune cast.
    /// </summary>
    public static class DicePouchGame
    {
        private static Sprite _pouch;
        private static Sprite[] _faces;
        private static Sprite _felt, _panel, _bar, _fill;
        private static FiresMinigameAudio _audio;
        private static bool _loaded;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>Open the dice cast for <paramref name="diceCount"/> dice; <paramref name="onComplete"/> fires once
        /// the dice settle (the caller reveals + grants there). Falls straight through on a headless server.</summary>
        public static void Open(int diceCount, string resultText, Action onComplete)
        {
            if (IsClientOnly()) { onComplete?.Invoke(); return; }
            if (!EnsureAssets()) { onComplete?.Invoke(); return; }

            var overlay = FiresMinigameOverlay.Create("FiresDicePouch", 6500);
            if (overlay == null) { onComplete?.Invoke(); return; }

            var ctrl = overlay.gameObject.AddComponent<DicePouchController>();
            ctrl.Build(overlay, _pouch, _faces, _felt, _panel, _bar, _fill, Mathf.Clamp(diceCount, 1, 5), _audio, resultText, onComplete);
        }

        private static bool EnsureAssets()
        {
            if (_loaded) return _pouch != null && _faces != null;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            _pouch = FiresMinigameSprites.Load(asm, "FiresDicePouch.png");
            _faces = new Sprite[6];
            for (int i = 0; i < 6; i++) _faces[i] = FiresMinigameSprites.Load(asm, $"FiresDie{i + 1}.png");
            _felt = MakeFelt();
            _panel = FiresRoundedSkin.RoundedSprite(18, new Color(0.12f, 0.095f, 0.065f, 0.98f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _fill = FiresRoundedSkin.RoundedSprite(8, new Color(0.85f, 0.62f, 0.26f, 1f));

            _audio = new FiresMinigameAudio(asm);
            _audio.RegisterClip("rattle", "FiresDiceRattle.wav");
            _audio.RegisterClip("clatter1", "FiresDiceClatter1.wav");
            _audio.RegisterClip("clatter2", "FiresDiceClatter2.wav");
            _audio.SetLoopClip("rattle");

            if (_pouch == null || _faces[0] == null) { FiresCore.Logging.FiresLogger.LogWarning("[Gambler] dice art missing — cast skipped."); return false; }
            return true;
        }

        // Green felt as a radial gradient (matches the approved mockup — bright centre, dark vignetted rim), with a
        // faint grain so it doesn't band. UI chrome, not sourced art.
        private static Sprite MakeFelt()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var center = new Color(0.133f, 0.255f, 0.165f);
            var edge = new Color(0.055f, 0.11f, 0.07f);
            var rng = new System.Random(4242);
            var c = new Vector2(S / 2f, S / 2f);
            float maxd = S * 0.62f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) / maxd);
                    d = d * d * (3f - 2f * d); // smoothstep vignette
                    var col = Color.Lerp(center, edge, d);
                    float g = (float)(rng.NextDouble() - 0.5) * 0.018f;
                    px[y * S + x] = new Color(col.r + g, col.g + g, col.b + g, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    internal sealed class DicePouchController : MonoBehaviour
    {
        // ── TUNING ────────────────────────────────────────────────────────────────────────────────────────────
        private const float CabW = 640f, CabH = 470f;
        private const float FeltW = 596f, FeltH = 300f;
        private const float PouchSize = 132f;
        private const float DieSize = 62f;
        private const float ShakeRefSpeed = 900f;   // felt-units/sec of up-down drag that counts as a full-energy shake
        private const float ShakeDecay = 1.1f;      // shake energy bleeds off this fast when the drag slows
        private const float DragBand = 70f;         // how far the pouch follows the drag from its rest, in any direction
        private const float MinShakeToCast = 0.10f; // need at least a little shake before a release dumps the dice
        private const float AutoCastAfter = 14f;    // safety: never hang if the player just stares
        private const float HopHeight = 70f;        // parabolic arc height of a cast die
        private const float SettleHold = 0.45f;     // rest after the last die lands, before the result banner
        private const float ResultAutoClose = 6f;   // auto-collect if the player doesn't click

        private enum Phase { Rattle, Cast, Settle, Result, Done }
        private Phase _phase = Phase.Rattle;

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private Action _onComplete;
        private Sprite[] _faces;
        private RectTransform _pouchRT, _feltRT;
        private Image _pouchImg, _fillImg;
        private Vector2 _pouchBase;
        private TMP_Text _hintText, _resultLabel;
        private GameObject _resultBanner;
        private string _result;
        private float _resultT;

        private int _n;
        private float _intensity, _elapsed, _peakShake;
        private bool _grabbed;
        private Vector2 _grabStart, _lastPos;

        private Die[] _dice;
        private int _landed;
        private float _settleT;

        private sealed class Die
        {
            public RectTransform rt; public Image img;
            public Vector2 from, to; public float delay, dur, t; public bool launched, done;
            public float spin, spinSpeed, faceTimer; public int face;
        }

        public void Build(FiresMinigameOverlay overlay, Sprite pouch, Sprite[] faces, Sprite felt, Sprite panel,
                          Sprite bar, Sprite fill, int diceCount, FiresMinigameAudio audio, string resultText, Action onComplete)
        {
            _overlay = overlay; _faces = faces; _n = diceCount; _audio = audio; _onComplete = onComplete; _result = resultText;
            var root = overlay.PlayField;

            // Cabinet (rounded brown panel) centred on the playfield.
            var cab = NewImage(root, "Cabinet", panel, Image.Type.Sliced);
            Size(cab.rectTransform, CabW, CabH, 0f, 0f);

            // Title bar.
            var titleBar = NewImage(cab.rectTransform, "TitleBar", bar, Image.Type.Sliced);
            var tbrt = titleBar.rectTransform; tbrt.anchorMin = new Vector2(0f, 1f); tbrt.anchorMax = new Vector2(1f, 1f); tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(-24f, 46f); tbrt.anchoredPosition = new Vector2(0f, -12f);
            var title = NewText(titleBar.rectTransform, "Title", "Casting the Bones", 22f, new Color(1f, 0.85f, 0.5f));
            Fill(title.rectTransform);

            // Felt stage.
            var feltImg = NewImage(cab.rectTransform, "Felt", felt, Image.Type.Simple);
            _feltRT = feltImg.rectTransform;
            Size(_feltRT, FeltW, FeltH, 0f, 6f);

            // Rattle meter (top-right of the felt).
            var meterBg = NewImage(_feltRT, "MeterBg", bar, Image.Type.Sliced);
            var mbrt = meterBg.rectTransform; mbrt.anchorMin = mbrt.anchorMax = new Vector2(1f, 1f); mbrt.pivot = new Vector2(1f, 1f);
            mbrt.sizeDelta = new Vector2(120f, 14f); mbrt.anchoredPosition = new Vector2(-12f, -12f);
            _fillImg = NewImage(meterBg.rectTransform, "Fill", fill, Image.Type.Filled);
            Fill(_fillImg.rectTransform);
            _fillImg.fillMethod = Image.FillMethod.Horizontal; _fillImg.fillOrigin = 0; _fillImg.fillAmount = 0f;
            var meterLbl = NewText(_feltRT, "MeterLbl", "RATTLE", 10f, new Color(0.8f, 0.72f, 0.56f));
            var mlrt = meterLbl.rectTransform; mlrt.anchorMin = mlrt.anchorMax = new Vector2(1f, 1f); mlrt.pivot = new Vector2(1f, 1f);
            mlrt.sizeDelta = new Vector2(120f, 14f); mlrt.anchoredPosition = new Vector2(-12f, -28f); meterLbl.alignment = TextAlignmentOptions.MidlineRight;

            // Pouch (upper-centre of the felt).
            _pouchImg = NewImage(_feltRT, "Pouch", pouch, Image.Type.Simple);
            _pouchRT = _pouchImg.rectTransform;
            _pouchRT.sizeDelta = new Vector2(PouchSize, PouchSize);
            _pouchBase = new Vector2(0f, 46f);
            _pouchRT.anchoredPosition = _pouchBase;
            _pouchImg.color = new Color(0.62f, 0.44f, 0.24f);   // leather tint over the flat CC0 glyph

            // Hint.
            var hint = NewText(cab.rectTransform, "Hint", "Grab the pouch with [LMB] and shake it  ·  release to dump the dice", 14f, new Color(0.8f, 0.82f, 0.85f));
            var hrt = hint.rectTransform; hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(-24f, 30f); hrt.anchoredPosition = new Vector2(0f, 14f);
            _hintText = hint;

            // Result banner — hidden until the dice settle, then names what was won before the panel closes.
            var banner = NewImage(_feltRT, "Result", bar, Image.Type.Sliced);
            var bart = banner.rectTransform; bart.anchorMin = bart.anchorMax = new Vector2(0.5f, 0.5f); bart.pivot = new Vector2(0.5f, 0.5f);
            bart.sizeDelta = new Vector2(FeltW - 56f, 56f); bart.anchoredPosition = new Vector2(0f, -FeltH * 0.5f + 40f);
            banner.color = new Color(0.1f, 0.08f, 0.05f, 0.97f);
            _resultLabel = NewText(banner.rectTransform, "ResultText", _result ?? "", 17f, new Color(0.82f, 0.96f, 0.62f));
            Fill(_resultLabel.rectTransform); _resultLabel.enableWordWrapping = true;
            banner.gameObject.SetActive(false);
            _resultBanner = banner.gameObject;

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;
        }

        private void Update()
        {
            if (_phase == Phase.Done || _overlay == null) return;
            _elapsed += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Rattle: UpdateRattle(); break;
                case Phase.Cast: UpdateCast(); break;
                case Phase.Settle:
                    _settleT += Time.deltaTime;
                    if (_settleT >= SettleHold) ShowResult();
                    break;
                case Phase.Result:
                    _resultT += Time.deltaTime;
                    if ((_resultT > 0.35f && Input.GetMouseButtonDown(0)) || _resultT >= ResultAutoClose) Finish();
                    break;
            }
        }

        private void ShowResult()
        {
            _phase = Phase.Result; _resultT = 0f;
            if (_resultBanner != null) { _resultBanner.transform.SetAsLastSibling(); _resultBanner.SetActive(true); }
            if (_hintText != null) _hintText.text = "Click to collect";
        }

        private void UpdateRattle()
        {
            bool held = Input.GetMouseButton(0);
            bool haveCursor = CursorLocal(out Vector2 cursor);

            if (Input.GetMouseButtonDown(0) && haveCursor) { _grabbed = true; _grabStart = cursor; _lastPos = cursor; }

            if (held && _grabbed && haveCursor)
            {
                // Energy is DRAG SPEED in ANY direction — you physically shake the bag; just holding does nothing.
                float speed = (cursor - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                float norm = Mathf.Clamp01(speed / ShakeRefSpeed);
                _intensity = norm > _intensity
                    ? Mathf.Lerp(_intensity, norm, 14f * Time.deltaTime)                              // snaps up as you shake
                    : Mathf.MoveTowards(_intensity, norm * 0.55f, ShakeDecay * Time.deltaTime);       // eases down as you slow
                _peakShake = Mathf.Max(_peakShake, _intensity);
                _lastPos = cursor;

                // The pouch follows the drag in any direction within a radius, plus a rattle wobble by shake strength.
                Vector2 off = Vector2.ClampMagnitude(cursor - _grabStart, DragBand);
                float wob = 3f * _intensity;
                _pouchRT.anchoredPosition = _pouchBase + off + new Vector2(Mathf.Sin(Time.time * 48f), Mathf.Cos(Time.time * 51f)) * wob;
                _pouchRT.localRotation = Quaternion.Euler(0f, 0f, -off.x * 0.05f + Mathf.Sin(Time.time * 43f) * 6f * _intensity);
                _audio.SetLoopIntensity(_intensity);
            }
            else
            {
                if (_grabbed && !held)
                {
                    if (_peakShake >= MinShakeToCast) { StartCast(); return; }
                    _grabbed = false;   // released without shaking — grab again and give it a shake
                }
                _intensity = Mathf.MoveTowards(_intensity, 0f, ShakeDecay * Time.deltaTime);
                _pouchRT.anchoredPosition = Vector2.Lerp(_pouchRT.anchoredPosition, _pouchBase, 12f * Time.deltaTime);
                _pouchRT.localRotation = Quaternion.Lerp(_pouchRT.localRotation, Quaternion.identity, 12f * Time.deltaTime);
                _audio.StopLoop();
            }
            if (_fillImg != null) _fillImg.fillAmount = _intensity;
            if (_elapsed >= AutoCastAfter) StartCast();
        }

        // Mouse position in felt-local space (the pouch's parent), so the drag maps 1:1 onto the bag.
        private bool CursorLocal(out Vector2 local)
        {
            local = Vector2.zero;
            if (_feltRT == null) return false;
            var canvas = _feltRT.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_feltRT, Input.mousePosition, cam, out local);
        }

        private void StartCast()
        {
            _audio.StopLoop();
            _phase = Phase.Cast;
            _pouchRT.localRotation = Quaternion.Euler(0f, 0f, 28f);   // tip the pouch to pour

            float energy = Mathf.Lerp(0.6f, 1f, _peakShake);
            var mouth = _pouchBase + new Vector2(6f, -PouchSize * 0.34f);
            _dice = new Die[_n];
            for (int i = 0; i < _n; i++)
            {
                var img = NewImage(_feltRT, "Die" + i, _faces[UnityEngine.Random.Range(0, 6)], Image.Type.Simple);
                img.rectTransform.sizeDelta = new Vector2(DieSize, DieSize);
                img.rectTransform.anchoredPosition = mouth;
                float spread = (_n == 1) ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(_n - 1));
                var target = new Vector2(spread * 150f + UnityEngine.Random.Range(-22f, 22f), -66f + UnityEngine.Random.Range(-30f, 30f));
                _dice[i] = new Die
                {
                    rt = img.rectTransform, img = img, from = mouth, to = target,
                    delay = i * 0.08f, dur = UnityEngine.Random.Range(0.5f, 0.66f) / energy,
                    spinSpeed = UnityEngine.Random.Range(360f, 900f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f),
                    face = UnityEngine.Random.Range(0, 6),
                };
            }
        }

        private void UpdateCast()
        {
            _landed = 0;
            foreach (var d in _dice)
            {
                if (d.done) { _landed++; continue; }
                if (!d.launched)
                {
                    d.delay -= Time.deltaTime;
                    if (d.delay > 0f) continue;
                    d.launched = true;
                }
                d.t += Time.deltaTime / d.dur;
                float t = Mathf.Clamp01(d.t);

                Vector2 p = Vector2.Lerp(d.from, d.to, t);
                p.y += Mathf.Sin(t * Mathf.PI) * HopHeight;
                d.rt.anchoredPosition = p;

                d.spin += d.spinSpeed * (1f - t) * Time.deltaTime;
                d.rt.localRotation = Quaternion.Euler(0f, 0f, d.spin);

                // Flicker faces mid-flight, lock to the final face as it lands.
                if (t < 0.82f)
                {
                    d.faceTimer -= Time.deltaTime;
                    if (d.faceTimer <= 0f) { d.img.sprite = _faces[UnityEngine.Random.Range(0, 6)]; d.faceTimer = 0.05f; }
                }
                else d.img.sprite = _faces[d.face];

                if (t >= 1f)
                {
                    d.done = true; _landed++;
                    d.rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-12f, 12f));
                    _audio.PlayPitched(UnityEngine.Random.value < 0.5f ? "clatter1" : "clatter2", 0.9f, UnityEngine.Random.Range(0.9f, 1.12f));
                }
            }
            if (_landed >= _dice.Length) { _phase = Phase.Settle; _settleT = 0f; }
        }

        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            _audio.StopLoop();
            var cb = _onComplete; _onComplete = null;
            _overlay.Close();
            cb?.Invoke();
        }

        // ── tiny UI helpers (mirror the lockpick panel's) ──────────────────────────────────────────────────────
        private static Image NewImage(Transform parent, string name, Sprite sprite, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite; img.type = type; img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false; t.enableWordWrapping = false;
            return t;
        }

        private static void Size(RectTransform rt, float w, float h, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
