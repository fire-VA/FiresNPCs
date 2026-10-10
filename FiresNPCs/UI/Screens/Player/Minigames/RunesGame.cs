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
    /// "Cast the Runes" — grab the pouch, shake it (drag in any direction), release to scatter rune stones across the
    /// casting cloth. They tumble, settle glyph-up, and the winners flare gold. Same shake→cast→settle→result flow and
    /// theater model as <see cref="DicePouchGame"/> (the gambler panel draws + grants; the runes are flavour). Rune art
    /// is the Kenney CC0 rune pack (grey slabs); pouch + clatter reused from the dice game. Client-only.
    /// </summary>
    public static class RunesGame
    {
        private static Sprite _pouch;
        private static Sprite[] _runes;
        private static Sprite _cloth, _panel, _bar, _fill;
        private static FiresMinigameAudio _audio;
        private static bool _loaded;
        private const int RuneArt = 8;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Open(int runeCount, string resultText, Action onComplete)
        {
            if (IsClientOnly()) { onComplete?.Invoke(); return; }
            if (!EnsureAssets()) { onComplete?.Invoke(); return; }

            var overlay = FiresMinigameOverlay.Create("FiresRunes", 6500);
            if (overlay == null) { onComplete?.Invoke(); return; }

            var ctrl = overlay.gameObject.AddComponent<RunesController>();
            ctrl.Build(overlay, _pouch, _runes, _cloth, _panel, _bar, _fill, Mathf.Clamp(runeCount, 1, 5), _audio, resultText, onComplete);
        }

        private static bool EnsureAssets()
        {
            if (_loaded) return _pouch != null && _runes != null && _runes[0] != null;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            _pouch = FiresMinigameSprites.Load(asm, "FiresDicePouch.png");
            _runes = new Sprite[RuneArt];
            for (int i = 0; i < RuneArt; i++) _runes[i] = FiresMinigameSprites.Load(asm, $"FiresRune{i + 1}.png");
            _cloth = MakeCloth();
            _panel = FiresRoundedSkin.RoundedSprite(18, new Color(0.12f, 0.095f, 0.065f, 0.98f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _fill = FiresRoundedSkin.RoundedSprite(8, new Color(0.85f, 0.62f, 0.26f, 1f));

            _audio = new FiresMinigameAudio(asm);
            _audio.RegisterClip("rattle", "FiresDiceRattle.wav");
            _audio.RegisterClip("clatter1", "FiresDiceClatter1.wav");
            _audio.RegisterClip("clatter2", "FiresDiceClatter2.wav");
            _audio.SetLoopClip("rattle");

            if (_pouch == null || _runes[0] == null) { FiresCore.Logging.FiresLogger.LogWarning("[Gambler] rune art missing — cast skipped."); return false; }
            return true;
        }

        // Tan casting cloth — a warm woven surface (radial highlight, dark rim) to set the runes apart from the dice felt.
        private static Sprite MakeCloth()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var center = new Color(0.24f, 0.205f, 0.15f);
            var edge = new Color(0.09f, 0.072f, 0.05f);
            var rng = new System.Random(1177);
            var c = new Vector2(S / 2f, S / 2f);
            float maxd = S * 0.62f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) / maxd);
                    d = d * d * (3f - 2f * d);
                    var col = Color.Lerp(center, edge, d);
                    // faint woven weft: alternating row/col shading
                    float weave = (((x >> 1) + (y >> 1)) & 1) == 0 ? 0.012f : -0.012f;
                    float g = (float)(rng.NextDouble() - 0.5) * 0.015f + weave;
                    px[y * S + x] = new Color(col.r + g, col.g + g, col.b + g, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    internal sealed class RunesController : MonoBehaviour
    {
        private const float CabW = 640f, CabH = 470f;
        private const float ClothW = 596f, ClothH = 300f;
        private const float PouchSize = 132f;
        private const float RuneW = 46f, RuneH = 79f;
        private const float ShakeRefSpeed = 900f;
        private const float ShakeDecay = 1.1f;
        private const float DragBand = 70f;
        private const float MinShakeToCast = 0.10f;
        private const float AutoCastAfter = 14f;
        private const float HopHeight = 74f;
        private const float SettleHold = 0.5f;
        private const float ResultAutoClose = 6f;

        private enum Phase { Rattle, Cast, Settle, Result, Done }
        private Phase _phase = Phase.Rattle;

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private Action _onComplete;
        private Sprite[] _runes;
        private RectTransform _pouchRT, _clothRT;
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

        private Stone[] _stones;
        private int _landed;
        private float _settleT;

        private static readonly Color StoneGrey = new Color(0.86f, 0.84f, 0.8f);
        private static readonly Color StoneGlow = new Color(1f, 0.9f, 0.55f);

        private sealed class Stone
        {
            public RectTransform rt; public Image img;
            public Vector2 from, to; public float delay, dur, t; public bool launched, done;
            public float spin, spinSpeed, faceTimer; public int glyph;
        }

        public void Build(FiresMinigameOverlay overlay, Sprite pouch, Sprite[] runes, Sprite cloth, Sprite panel,
                          Sprite bar, Sprite fill, int runeCount, FiresMinigameAudio audio, string resultText, Action onComplete)
        {
            _overlay = overlay; _runes = runes; _n = runeCount; _audio = audio; _onComplete = onComplete; _result = resultText;
            var root = overlay.PlayField;

            var cab = NewImage(root, "Cabinet", panel, Image.Type.Sliced);
            Size(cab.rectTransform, CabW, CabH, 0f, 0f);

            var titleBar = NewImage(cab.rectTransform, "TitleBar", bar, Image.Type.Sliced);
            var tbrt = titleBar.rectTransform; tbrt.anchorMin = new Vector2(0f, 1f); tbrt.anchorMax = new Vector2(1f, 1f); tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(-24f, 46f); tbrt.anchoredPosition = new Vector2(0f, -12f);
            var title = NewText(titleBar.rectTransform, "Title", "Cast the Runes", 22f, new Color(1f, 0.85f, 0.5f));
            Fill(title.rectTransform);

            var clothImg = NewImage(cab.rectTransform, "Cloth", cloth, Image.Type.Simple);
            _clothRT = clothImg.rectTransform;
            Size(_clothRT, ClothW, ClothH, 0f, 6f);

            var meterBg = NewImage(_clothRT, "MeterBg", bar, Image.Type.Sliced);
            var mbrt = meterBg.rectTransform; mbrt.anchorMin = mbrt.anchorMax = new Vector2(1f, 1f); mbrt.pivot = new Vector2(1f, 1f);
            mbrt.sizeDelta = new Vector2(120f, 14f); mbrt.anchoredPosition = new Vector2(-12f, -12f);
            _fillImg = NewImage(meterBg.rectTransform, "Fill", fill, Image.Type.Filled);
            Fill(_fillImg.rectTransform);
            _fillImg.fillMethod = Image.FillMethod.Horizontal; _fillImg.fillOrigin = 0; _fillImg.fillAmount = 0f;
            var meterLbl = NewText(_clothRT, "MeterLbl", "SHAKE", 10f, new Color(0.8f, 0.72f, 0.56f));
            var mlrt = meterLbl.rectTransform; mlrt.anchorMin = mlrt.anchorMax = new Vector2(1f, 1f); mlrt.pivot = new Vector2(1f, 1f);
            mlrt.sizeDelta = new Vector2(120f, 14f); mlrt.anchoredPosition = new Vector2(-12f, -28f); meterLbl.alignment = TextAlignmentOptions.MidlineRight;

            _pouchImg = NewImage(_clothRT, "Pouch", pouch, Image.Type.Simple);
            _pouchRT = _pouchImg.rectTransform;
            _pouchRT.sizeDelta = new Vector2(PouchSize, PouchSize);
            _pouchBase = new Vector2(0f, 46f);
            _pouchRT.anchoredPosition = _pouchBase;
            _pouchImg.color = new Color(0.62f, 0.44f, 0.24f);

            var hint = NewText(cab.rectTransform, "Hint", "Grab the pouch with [LMB] and shake it  ·  release to cast the runes", 14f, new Color(0.8f, 0.82f, 0.85f));
            var hrt = hint.rectTransform; hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(-24f, 30f); hrt.anchoredPosition = new Vector2(0f, 14f);
            _hintText = hint;

            var banner = NewImage(_clothRT, "Result", bar, Image.Type.Sliced);
            var bart = banner.rectTransform; bart.anchorMin = bart.anchorMax = new Vector2(0.5f, 0.5f); bart.pivot = new Vector2(0.5f, 0.5f);
            bart.sizeDelta = new Vector2(ClothW - 56f, 56f); bart.anchoredPosition = new Vector2(0f, -ClothH * 0.5f + 40f);
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
                    PulseStones();
                    if (_settleT >= SettleHold) ShowResult();
                    break;
                case Phase.Result:
                    _resultT += Time.deltaTime;
                    PulseStones();
                    if ((_resultT > 0.35f && Input.GetMouseButtonDown(0)) || _resultT >= ResultAutoClose) Finish();
                    break;
            }
        }

        // Winners flare — a gentle synchronized gold pulse on the settled runes.
        private void PulseStones()
        {
            if (_stones == null) return;
            float p = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f);
            foreach (var s in _stones)
            {
                if (s == null || !s.done) continue;
                s.img.color = Color.Lerp(StoneGrey, StoneGlow, 0.35f + 0.65f * p);
                s.rt.localScale = Vector3.one * (1f + 0.06f * p);
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
                float speed = (cursor - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                float norm = Mathf.Clamp01(speed / ShakeRefSpeed);
                _intensity = norm > _intensity
                    ? Mathf.Lerp(_intensity, norm, 14f * Time.deltaTime)
                    : Mathf.MoveTowards(_intensity, norm * 0.55f, ShakeDecay * Time.deltaTime);
                _peakShake = Mathf.Max(_peakShake, _intensity);
                _lastPos = cursor;

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
                    _grabbed = false;
                }
                _intensity = Mathf.MoveTowards(_intensity, 0f, ShakeDecay * Time.deltaTime);
                _pouchRT.anchoredPosition = Vector2.Lerp(_pouchRT.anchoredPosition, _pouchBase, 12f * Time.deltaTime);
                _pouchRT.localRotation = Quaternion.Lerp(_pouchRT.localRotation, Quaternion.identity, 12f * Time.deltaTime);
                _audio.StopLoop();
            }
            if (_fillImg != null) _fillImg.fillAmount = _intensity;
            if (_elapsed >= AutoCastAfter) StartCast();
        }

        private bool CursorLocal(out Vector2 local)
        {
            local = Vector2.zero;
            if (_clothRT == null) return false;
            var canvas = _clothRT.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_clothRT, Input.mousePosition, cam, out local);
        }

        private void StartCast()
        {
            _audio.StopLoop();
            _phase = Phase.Cast;
            _pouchRT.localRotation = Quaternion.Euler(0f, 0f, 28f);

            float energy = Mathf.Lerp(0.6f, 1f, _peakShake);
            var mouth = _pouchBase + new Vector2(6f, -PouchSize * 0.34f);
            _stones = new Stone[_n];
            for (int i = 0; i < _n; i++)
            {
                var img = NewImage(_clothRT, "Rune" + i, _runes[UnityEngine.Random.Range(0, _runes.Length)], Image.Type.Simple);
                img.rectTransform.sizeDelta = new Vector2(RuneW, RuneH);
                img.rectTransform.anchoredPosition = mouth;
                img.color = StoneGrey;
                float spread = (_n == 1) ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(_n - 1));
                var target = new Vector2(spread * 160f + UnityEngine.Random.Range(-20f, 20f), -60f + UnityEngine.Random.Range(-34f, 34f));
                _stones[i] = new Stone
                {
                    rt = img.rectTransform, img = img, from = mouth, to = target,
                    delay = i * 0.09f, dur = UnityEngine.Random.Range(0.52f, 0.7f) / energy,
                    spinSpeed = UnityEngine.Random.Range(240f, 620f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f),
                    glyph = UnityEngine.Random.Range(0, _runes.Length),
                };
            }
        }

        private void UpdateCast()
        {
            _landed = 0;
            foreach (var s in _stones)
            {
                if (s.done) { _landed++; continue; }
                if (!s.launched)
                {
                    s.delay -= Time.deltaTime;
                    if (s.delay > 0f) continue;
                    s.launched = true;
                }
                s.t += Time.deltaTime / s.dur;
                float t = Mathf.Clamp01(s.t);

                Vector2 p = Vector2.Lerp(s.from, s.to, t);
                p.y += Mathf.Sin(t * Mathf.PI) * HopHeight;
                s.rt.anchoredPosition = p;

                s.spin += s.spinSpeed * (1f - t) * Time.deltaTime;
                s.rt.localRotation = Quaternion.Euler(0f, 0f, s.spin);

                if (t < 0.82f)
                {
                    s.faceTimer -= Time.deltaTime;
                    if (s.faceTimer <= 0f) { s.img.sprite = _runes[UnityEngine.Random.Range(0, _runes.Length)]; s.faceTimer = 0.06f; }
                }
                else s.img.sprite = _runes[s.glyph];

                if (t >= 1f)
                {
                    s.done = true; _landed++;
                    s.rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-10f, 10f));
                    _audio.PlayPitched(UnityEngine.Random.value < 0.5f ? "clatter1" : "clatter2", 0.85f, UnityEngine.Random.Range(0.85f, 1.05f));
                }
            }
            if (_landed >= _stones.Length) { _phase = Phase.Settle; _settleT = 0f; }
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
