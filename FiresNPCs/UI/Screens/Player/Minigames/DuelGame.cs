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
    /// "The Gamble for Hísing" — the Viking dice duel (double-or-nothing best-of-3 vs the Jarl NPC). You shake your
    /// pouch and cast; the Jarl casts back; higher total takes the round. Grounded in the saga where Olaf II won the
    /// island when his die split to show six AND one (13, beating double-six) — a rare "split die" crit here can add a
    /// pip and steal a round. Reuses the shared dice tumble on the FUC overlay. <c>onComplete(true)</c> if you win the
    /// duel. Presentation only — the panel handles the stake/pot. Client-only.
    /// </summary>
    public static class DuelGame
    {
        private static Sprite _pouch, _felt, _panel, _bar;
        private static Sprite[] _faces;
        private static FiresMinigameAudio _audio;
        private static bool _loaded;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Open(Action<bool> onComplete)
        {
            if (IsClientOnly()) { onComplete?.Invoke(false); return; }
            if (!EnsureAssets()) { onComplete?.Invoke(false); return; }
            var overlay = FiresMinigameOverlay.Create("FiresDuel", 6500);
            if (overlay == null) { onComplete?.Invoke(false); return; }
            var ctrl = overlay.gameObject.AddComponent<DuelController>();
            ctrl.Build(overlay, _pouch, _faces, _felt, _panel, _bar, _audio, onComplete);
        }

        private static bool EnsureAssets()
        {
            if (_loaded) return _pouch != null && _faces != null && _faces[0] != null;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            _pouch = FiresMinigameSprites.Load(asm, "FiresDicePouch.png");
            _faces = new Sprite[6];
            for (int i = 0; i < 6; i++) _faces[i] = FiresMinigameSprites.Load(asm, $"FiresDie{i + 1}.png");
            _felt = MakeFelt();
            _panel = FiresRoundedSkin.RoundedSprite(18, new Color(0.12f, 0.095f, 0.065f, 0.98f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _audio = new FiresMinigameAudio(asm);
            _audio.RegisterClip("rattle", "FiresDiceRattle.wav");
            _audio.RegisterClip("clatter1", "FiresDiceClatter1.wav");
            _audio.RegisterClip("clatter2", "FiresDiceClatter2.wav");
            _audio.SetLoopClip("rattle");
            return _pouch != null && _faces[0] != null;
        }

        // Blue-grey duel felt (matches mockup III).
        private static Sprite MakeFelt()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var center = new Color(0.168f, 0.227f, 0.267f);
            var edge = new Color(0.055f, 0.086f, 0.11f);
            var rng = new System.Random(7);
            var c = new Vector2(S / 2f, S / 2f); float maxd = S * 0.62f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) / maxd); d = d * d * (3f - 2f * d);
                    var col = Color.Lerp(center, edge, d);
                    float g = (float)(rng.NextDouble() - 0.5) * 0.018f;
                    px[y * S + x] = new Color(col.r + g, col.g + g, col.b + g, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    internal sealed class DuelController : MonoBehaviour
    {
        private const float CabW = 660f, CabH = 560f, FeltW = 616f, FeltH = 372f;
        private const float PouchSize = 116f, DieSize = 56f;
        private const float ShakeRefSpeed = 900f, ShakeDecay = 1.1f, DragBand = 60f, MinShakeToCast = 0.10f;
        private const float HopHeight = 60f, RoundBeatHold = 2.1f;
        private const int WinsNeeded = 2;               // best of 3
        private const float SplitCritChance = 0.14f;    // the legendary Hísing split die (+1)

        private enum Phase { Ready, Cast, RoundBeat, End, Done }
        private Phase _phase = Phase.Ready;

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private Action<bool> _onComplete;
        private Sprite[] _faces;
        private RectTransform _feltRT, _pouchRT;
        private Image _pouchImg;
        private Vector2 _pouchBase;
        private TMP_Text _hintText, _scoreText, _bannerText;
        private GameObject _banner;

        private int _youWins, _jarlWins;
        private float _intensity, _peakShake, _elapsed;
        private bool _grabbed; private Vector2 _grabStart, _lastPos;

        private Die[] _dice; private int _landed; private float _beatT; private bool _youSplit;

        private sealed class Die { public RectTransform rt; public Image img; public bool mine; public Vector2 from, to; public float delay, dur, t, spin, spinSpeed, faceTimer; public bool launched, done; public int face; }

        public void Build(FiresMinigameOverlay overlay, Sprite pouch, Sprite[] faces, Sprite felt, Sprite panel, Sprite bar, FiresMinigameAudio audio, Action<bool> onComplete)
        {
            _overlay = overlay; _faces = faces; _audio = audio; _onComplete = onComplete;
            var root = overlay.PlayField;

            var cab = NewImage(root, "Cabinet", panel, Image.Type.Sliced);
            Size(cab.rectTransform, CabW, CabH, 0f, 0f);

            var titleBar = NewImage(cab.rectTransform, "TitleBar", bar, Image.Type.Sliced);
            var tbrt = titleBar.rectTransform; tbrt.anchorMin = new Vector2(0f, 1f); tbrt.anchorMax = new Vector2(1f, 1f); tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(-24f, 44f); tbrt.anchoredPosition = new Vector2(0f, -10f);
            var title = NewText(titleBar.rectTransform, "Title", "The Gamble for Hísing", 21f, new Color(1f, 0.85f, 0.5f));
            Fill(title.rectTransform);

            _scoreText = NewText(cab.rectTransform, "Score", "You  0  —  0  Jarl", 16f, new Color(0.9f, 0.86f, 0.7f));
            var srt = _scoreText.rectTransform; srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(-24f, 24f); srt.anchoredPosition = new Vector2(0f, -58f);

            var feltImg = NewImage(cab.rectTransform, "Felt", felt, Image.Type.Simple);
            _feltRT = feltImg.rectTransform;
            Size(_feltRT, FeltW, FeltH, 0f, -14f);

            var jarl = NewText(_feltRT, "JarlLabel", "The Jarl", 14f, new Color(0.86f, 0.8f, 0.7f));
            var jrt = jarl.rectTransform; jrt.anchorMin = jrt.anchorMax = new Vector2(0.5f, 1f); jrt.pivot = new Vector2(0.5f, 1f);
            jrt.sizeDelta = new Vector2(200f, 22f); jrt.anchoredPosition = new Vector2(0f, -8f);

            _pouchImg = NewImage(_feltRT, "Pouch", pouch, Image.Type.Simple);
            _pouchRT = _pouchImg.rectTransform;
            _pouchRT.sizeDelta = new Vector2(PouchSize, PouchSize);
            _pouchBase = new Vector2(0f, -FeltH * 0.5f + 74f);
            _pouchRT.anchoredPosition = _pouchBase;
            _pouchImg.color = new Color(0.62f, 0.44f, 0.24f);

            var hint = NewText(cab.rectTransform, "Hint", "Shake your pouch with [LMB] and release to roll against the Jarl", 14f, new Color(0.8f, 0.82f, 0.85f));
            var hi = hint.rectTransform; hi.anchorMin = new Vector2(0f, 0f); hi.anchorMax = new Vector2(1f, 0f); hi.pivot = new Vector2(0.5f, 0f);
            hi.sizeDelta = new Vector2(-24f, 30f); hi.anchoredPosition = new Vector2(0f, 12f);
            _hintText = hint;

            _banner = NewImage(cab.rectTransform, "Banner", bar, Image.Type.Sliced).gameObject;
            var brt = _banner.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(CabW - 90f, 54f); brt.anchoredPosition = new Vector2(0f, -14f);
            _banner.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.05f, 0.97f);
            _bannerText = NewText(_banner.transform, "BannerText", "", 18f, new Color(0.9f, 0.9f, 0.8f));
            Fill(_bannerText.rectTransform); _bannerText.enableWordWrapping = true;
            _banner.SetActive(false);

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;
        }

        private void Update()
        {
            if (_phase == Phase.Done || _overlay == null) return;
            _elapsed += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Ready: UpdateShake(); break;
                case Phase.Cast: UpdateCast(); break;
                case Phase.RoundBeat:
                    _beatT += Time.deltaTime;
                    if (_beatT >= RoundBeatHold) AfterRound();
                    break;
                case Phase.End:
                    if (Input.GetMouseButtonDown(0) || _elapsed - _beatT > 8f) Finish();
                    break;
            }
        }

        private void UpdateShake()
        {
            bool held = Input.GetMouseButton(0);
            bool have = CursorLocal(out Vector2 cur);
            if (Input.GetMouseButtonDown(0) && have) { _grabbed = true; _grabStart = cur; _lastPos = cur; }

            if (held && _grabbed && have)
            {
                float speed = (cur - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                float norm = Mathf.Clamp01(speed / ShakeRefSpeed);
                _intensity = norm > _intensity ? Mathf.Lerp(_intensity, norm, 14f * Time.deltaTime) : Mathf.MoveTowards(_intensity, norm * 0.55f, ShakeDecay * Time.deltaTime);
                _peakShake = Mathf.Max(_peakShake, _intensity);
                _lastPos = cur;
                Vector2 off = Vector2.ClampMagnitude(cur - _grabStart, DragBand);
                _pouchRT.anchoredPosition = _pouchBase + off + new Vector2(Mathf.Sin(Time.time * 48f), Mathf.Cos(Time.time * 51f)) * (4f * _intensity);
                _pouchRT.localRotation = Quaternion.Euler(0f, 0f, -off.x * 0.06f + Mathf.Sin(Time.time * 43f) * 6f * _intensity);
                _audio.SetLoopIntensity(_intensity);
            }
            else
            {
                if (_grabbed && !held) { if (_peakShake >= MinShakeToCast) { StartCast(); return; } _grabbed = false; }
                _intensity = Mathf.MoveTowards(_intensity, 0f, ShakeDecay * Time.deltaTime);
                _pouchRT.anchoredPosition = Vector2.Lerp(_pouchRT.anchoredPosition, _pouchBase, 12f * Time.deltaTime);
                _pouchRT.localRotation = Quaternion.Lerp(_pouchRT.localRotation, Quaternion.identity, 12f * Time.deltaTime);
                _audio.StopLoop();
            }
        }

        private void StartCast()
        {
            _audio.StopLoop();
            _phase = Phase.Cast;
            _pouchRT.localRotation = Quaternion.Euler(0f, 0f, 26f);
            _youSplit = UnityEngine.Random.value < SplitCritChance;
            float energy = Mathf.Lerp(0.6f, 1f, _peakShake);

            var youMouth = _pouchBase + new Vector2(4f, PouchSize * 0.32f);
            var jarlMouth = new Vector2(0f, FeltH * 0.5f - 46f);
            _dice = new Die[4];
            for (int k = 0; k < 4; k++)
            {
                bool mine = k < 2;
                var from = mine ? youMouth : jarlMouth;
                var img = NewImage(_feltRT, "D" + k, _faces[UnityEngine.Random.Range(0, 6)], Image.Type.Simple);
                img.rectTransform.sizeDelta = new Vector2(DieSize, DieSize);
                img.rectTransform.anchoredPosition = from;
                float side = (k % 2 == 0) ? -1f : 1f;
                float ty = mine ? -FeltH * 0.28f : FeltH * 0.16f;
                var to = new Vector2(side * 62f + UnityEngine.Random.Range(-14f, 14f), ty + UnityEngine.Random.Range(-16f, 16f));
                _dice[k] = new Die
                {
                    rt = img.rectTransform, img = img, mine = mine, from = from, to = to,
                    delay = (mine ? 0f : 0.22f) + (k % 2) * 0.07f,
                    dur = UnityEngine.Random.Range(0.5f, 0.66f) / energy,
                    spinSpeed = UnityEngine.Random.Range(360f, 860f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f),
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
                if (!d.launched) { d.delay -= Time.deltaTime; if (d.delay > 0f) continue; d.launched = true; }
                d.t += Time.deltaTime / d.dur;
                float t = Mathf.Clamp01(d.t);
                Vector2 p = Vector2.Lerp(d.from, d.to, t); p.y += Mathf.Sin(t * Mathf.PI) * HopHeight;
                d.rt.anchoredPosition = p;
                d.spin += d.spinSpeed * (1f - t) * Time.deltaTime;
                d.rt.localRotation = Quaternion.Euler(0f, 0f, d.spin);
                if (t < 0.82f) { d.faceTimer -= Time.deltaTime; if (d.faceTimer <= 0f) { d.img.sprite = _faces[UnityEngine.Random.Range(0, 6)]; d.faceTimer = 0.05f; } }
                else d.img.sprite = _faces[d.face];
                if (t >= 1f)
                {
                    d.done = true; _landed++;
                    d.rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-12f, 12f));
                    if (d.mine && _youSplit && d == _dice[0]) d.img.color = new Color(1f, 0.9f, 0.5f);   // mark the split die gold
                    _audio.PlayPitched(UnityEngine.Random.value < 0.5f ? "clatter1" : "clatter2", 0.9f, UnityEngine.Random.Range(0.9f, 1.12f));
                }
            }
            if (_landed >= _dice.Length) ResolveRound();
        }

        private void ResolveRound()
        {
            int you = (_dice[0].face + 1) + (_dice[1].face + 1) + (_youSplit ? 1 : 0);
            int jarl = (_dice[2].face + 1) + (_dice[3].face + 1);
            string split = _youSplit ? "  <color=#f0c452>(split die +1!)</color>" : "";
            string outcome;
            if (you > jarl) { _youWins++; outcome = "<color=#7cff7c>Round to you</color>"; }
            else if (you < jarl) { _jarlWins++; outcome = "<color=#ff8080>Round to the Jarl</color>"; }
            else outcome = "<color=#f0d060>Push — roll again</color>";
            _scoreText.text = $"You  {_youWins}  —  {_jarlWins}  Jarl";
            ShowBanner($"You {you}{split}   ·   Jarl {jarl}\n{outcome}");
            _phase = Phase.RoundBeat; _beatT = 0f;
        }

        private void AfterRound()
        {
            _banner.SetActive(false);
            for (int i = 0; i < _dice.Length; i++) if (_dice[i]?.rt != null) Destroy(_dice[i].rt.gameObject);
            _dice = null;

            if (_youWins >= WinsNeeded || _jarlWins >= WinsNeeded)
            {
                _phase = Phase.End; _beatT = _elapsed;
                bool won = _youWins > _jarlWins;
                _hintText.text = "Click to collect";
                ShowBanner(won
                    ? "<color=#7cff7c>You take Hísing — the pot is yours!</color>"
                    : "<color=#ff8080>The Jarl claims the pot.</color>");
                return;
            }

            // Next round.
            _peakShake = 0f; _intensity = 0f; _grabbed = false; _youSplit = false;
            _pouchRT.anchoredPosition = _pouchBase; _pouchRT.localRotation = Quaternion.identity;
            _phase = Phase.Ready;
        }

        private void ShowBanner(string text)
        {
            _bannerText.text = text;
            _banner.transform.SetAsLastSibling();
            _banner.SetActive(true);
        }

        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            _audio.StopLoop();
            bool won = _youWins > _jarlWins;
            var cb = _onComplete; _onComplete = null;
            _overlay.Close();
            cb?.Invoke(won);
        }

        private bool CursorLocal(out Vector2 local)
        {
            local = Vector2.zero;
            if (_feltRT == null) return false;
            var canvas = _feltRT.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_feltRT, Input.mousePosition, cam, out local);
        }

        private static Image NewImage(Transform parent, string name, Sprite sprite, Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.sprite = sprite; img.type = type; img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.enableWordWrapping = false;
            return t;
        }

        private static void Size(RectTransform rt, float w, float h, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
