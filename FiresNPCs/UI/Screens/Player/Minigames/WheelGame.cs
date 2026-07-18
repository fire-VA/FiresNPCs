using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresCore.UI;
using FiresCore.UI.Minigames;

namespace FiresRPGmaker.UI.Screens.PlayerPanels.Minigames
{
    /// <summary>
    /// "The Wheel of Fortune" — the gambler's wheel game. A segmented wheel labelled with the reward pool: grab the
    /// rim and flick it, release to let it spin, and it ratchets down to a stop. Deterministic — the reward is drawn
    /// up front (theater) and the wheel eases to land that wedge under the pointer; the flick only sets how many turns
    /// it takes. Built on the shared FUC overlay + FiresRoundedSkin cabinet to match the mockup. Client-only.
    /// </summary>
    public static class WheelGame
    {
        private static Sprite _panel, _bar, _hub;
        private static FiresMinigameAudio _audio;
        private static bool _loaded;

        private static bool IsClientOnly() => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>Open the wheel. <paramref name="labels"/> are the wedge captions; the wheel lands on
        /// <paramref name="targetIndex"/>; <paramref name="resultText"/> is shown on stop; then <paramref name="onComplete"/>.</summary>
        public static void Open(IList<string> labels, int targetIndex, string resultText, Action onComplete)
        {
            if (IsClientOnly()) { onComplete?.Invoke(); return; }
            EnsureAssets();
            var overlay = FiresMinigameOverlay.Create("FiresWheel", 6500);
            if (overlay == null) { onComplete?.Invoke(); return; }

            var ctrl = overlay.gameObject.AddComponent<WheelController>();
            ctrl.Build(overlay, _panel, _bar, _hub, _audio, new List<string>(labels), Mathf.Clamp(targetIndex, 0, Mathf.Max(0, labels.Count - 1)), resultText, onComplete);
        }

        private static void EnsureAssets()
        {
            if (_loaded) return;
            _loaded = true;
            _panel = FiresRoundedSkin.RoundedSprite(18, new Color(0.12f, 0.095f, 0.065f, 0.98f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2);
            _bar = FiresRoundedSkin.RoundedSprite(12, new Color(0.17f, 0.12f, 0.07f, 1f));
            _hub = FiresRoundedSkin.RoundedSprite(40, new Color(0.85f, 0.62f, 0.26f, 1f), new Color(0.24f, 0.16f, 0.07f, 1f), 3);
            _audio = new FiresMinigameAudio(Assembly.GetExecutingAssembly());
            _audio.RegisterSynth("tick", FiresMinigameAudio.MakeTick(), 22050);
            _audio.RegisterSynth("stop", FiresMinigameAudio.MakeThud(), 22050);
        }
    }

    internal sealed class WheelController : MonoBehaviour
    {
        private const float CabW = 640f, CabH = 520f;
        private const float WheelPx = 360f;         // on-screen wheel diameter
        private const float RefFlick = 520f;        // deg/sec of flick that counts as a "hard" spin
        private const float SpinTurnsMin = 3f, SpinTurnsMax = 7f;
        private const float ResultAutoClose = 6f;
        private static readonly Color[] Palette =
        {
            new Color(0.54f, 0.37f, 0.17f), new Color(0.25f, 0.42f, 0.27f),
            new Color(0.48f, 0.29f, 0.14f), new Color(0.73f, 0.54f, 0.23f), new Color(0.63f, 0.24f, 0.20f),
        };

        private enum Phase { Idle, Drag, Spin, Result, Done }
        private Phase _phase = Phase.Idle;

        private FiresMinigameOverlay _overlay;
        private FiresMinigameAudio _audio;
        private Action _onComplete;
        private RectTransform _wheelRT;
        private TMP_Text _hintText, _resultLabel;
        private GameObject _resultBanner;
        private string _result;

        private int _n, _target;
        private float _wedge;            // degrees per wedge
        private float _angle;            // current wheel rotation (deg, CCW)
        private float _spinStart, _spinFinal, _spinT, _spinDur;
        private int _lastTickWedge;
        private float _elapsed, _resultT;

        // drag tracking
        private bool _grabbed;
        private float _lastCursorAng, _dragVel;

        public void Build(FiresMinigameOverlay overlay, Sprite panel, Sprite bar, Sprite hub, FiresMinigameAudio audio,
                          List<string> labels, int targetIndex, string resultText, Action onComplete)
        {
            _overlay = overlay; _audio = audio; _onComplete = onComplete; _result = resultText;
            _n = Mathf.Max(2, labels.Count); _target = targetIndex; _wedge = 360f / _n;
            var root = overlay.PlayField;

            var cab = NewImage(root, "Cabinet", panel, Image.Type.Sliced);
            Size(cab.rectTransform, CabW, CabH, 0f, 0f);

            var titleBar = NewImage(cab.rectTransform, "TitleBar", bar, Image.Type.Sliced);
            var tbrt = titleBar.rectTransform; tbrt.anchorMin = new Vector2(0f, 1f); tbrt.anchorMax = new Vector2(1f, 1f); tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(-24f, 46f); tbrt.anchoredPosition = new Vector2(0f, -12f);
            var title = NewText(titleBar.rectTransform, "Title", "The Wheel of Fortune", 22f, new Color(1f, 0.85f, 0.5f));
            Fill(title.rectTransform);

            // Wheel (rotating container: sector texture + wedge labels).
            _wheelRT = new GameObject("Wheel", typeof(RectTransform)).GetComponent<RectTransform>();
            _wheelRT.SetParent(cab.rectTransform, false);
            _wheelRT.anchorMin = _wheelRT.anchorMax = new Vector2(0.5f, 0.5f); _wheelRT.pivot = new Vector2(0.5f, 0.5f);
            _wheelRT.sizeDelta = new Vector2(WheelPx, WheelPx); _wheelRT.anchoredPosition = new Vector2(0f, 6f);

            var wheelImg = NewImage(_wheelRT, "Face", MakeWheelSprite(512, _n), Image.Type.Simple);
            Fill(wheelImg.rectTransform);

            float labelR = WheelPx * 0.34f;
            for (int i = 0; i < _n; i++)
            {
                float phi = (90f - (i + 0.5f) * _wedge) * Mathf.Deg2Rad;
                var lbl = NewText(_wheelRT, "W" + i, labels[i], 14f, new Color(1f, 0.96f, 0.85f));
                var lrt = lbl.rectTransform; lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f); lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(96f, 26f);
                lrt.anchoredPosition = new Vector2(Mathf.Cos(phi) * labelR, Mathf.Sin(phi) * labelR);
                lrt.localRotation = Quaternion.Euler(0f, 0f, (90f - (i + 0.5f) * _wedge) - 90f);   // radial-ish
            }

            var hubImg = NewImage(cab.rectTransform, "Hub", hub, Image.Type.Sliced);
            var hrt = hubImg.rectTransform; hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f); hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(46f, 46f); hrt.anchoredPosition = new Vector2(0f, 6f);

            // Fixed pointer at the top, aimed into the wheel.
            var pointer = NewText(cab.rectTransform, "Pointer", "▼", 34f, new Color(1f, 0.85f, 0.5f));
            var prt = pointer.rectTransform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(50f, 50f); prt.anchoredPosition = new Vector2(0f, 6f + WheelPx * 0.5f - 6f);

            var hint = NewText(cab.rectTransform, "Hint", "Grab the wheel with [LMB] and flick it  ·  release to spin", 14f, new Color(0.8f, 0.82f, 0.85f));
            var hi = hint.rectTransform; hi.anchorMin = new Vector2(0f, 0f); hi.anchorMax = new Vector2(1f, 0f); hi.pivot = new Vector2(0.5f, 0f);
            hi.sizeDelta = new Vector2(-24f, 30f); hi.anchoredPosition = new Vector2(0f, 14f);
            _hintText = hint;

            var banner = NewImage(cab.rectTransform, "Result", bar, Image.Type.Sliced);
            var ba = banner.rectTransform; ba.anchorMin = ba.anchorMax = new Vector2(0.5f, 0.5f); ba.pivot = new Vector2(0.5f, 0.5f);
            ba.sizeDelta = new Vector2(CabW - 60f, 52f); ba.anchoredPosition = new Vector2(0f, -CabH * 0.5f + 66f);
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
                case Phase.Idle:
                case Phase.Drag:
                    UpdateDrag();
                    if (_elapsed >= 12f && _phase != Phase.Spin) StartSpin(0.3f);   // safety auto-spin
                    break;
                case Phase.Spin: UpdateSpin(); break;
                case Phase.Result:
                    _resultT += Time.deltaTime;
                    if ((_resultT > 0.35f && Input.GetMouseButtonDown(0)) || _resultT >= ResultAutoClose) Finish();
                    break;
            }
        }

        private void UpdateDrag()
        {
            bool held = Input.GetMouseButton(0);
            if (!CursorAngle(out float ang)) { if (!held && _grabbed) ReleaseFlick(); return; }

            if (Input.GetMouseButtonDown(0)) { _grabbed = true; _phase = Phase.Drag; _lastCursorAng = ang; _dragVel = 0f; }
            else if (held && _grabbed)
            {
                float d = Mathf.DeltaAngle(_lastCursorAng, ang);
                _angle += d;
                _dragVel = Mathf.Lerp(_dragVel, d / Mathf.Max(Time.deltaTime, 1e-4f), 0.4f);
                _lastCursorAng = ang;
                _wheelRT.localRotation = Quaternion.Euler(0f, 0f, _angle);
                Tick();
            }
            else if (!held && _grabbed) ReleaseFlick();
        }

        private void ReleaseFlick()
        {
            _grabbed = false;
            StartSpin(Mathf.Clamp01(Mathf.Abs(_dragVel) / RefFlick));
        }

        private void StartSpin(float strength)
        {
            _phase = Phase.Spin;
            float turns = Mathf.Lerp(SpinTurnsMin, SpinTurnsMax, strength);
            _spinStart = _angle;
            // Land the target wedge centre under the top pointer: final ≡ (target+0.5)*wedge (mod 360), CCW.
            float targetMod = (_target + 0.5f) * _wedge;
            float basis = _spinStart + turns * 360f;
            float add = Mathf.Repeat(targetMod - Mathf.Repeat(basis, 360f), 360f);
            _spinFinal = basis + add;
            _spinDur = Mathf.Lerp(2.8f, 4.4f, strength);
            _spinT = 0f;
            _lastTickWedge = Mathf.FloorToInt(_angle / _wedge);
        }

        private void UpdateSpin()
        {
            _spinT += Time.deltaTime / _spinDur;
            float t = Mathf.Clamp01(_spinT);
            float e = 1f - Mathf.Pow(1f - t, 3f);   // easeOutCubic
            _angle = Mathf.Lerp(_spinStart, _spinFinal, e);
            _wheelRT.localRotation = Quaternion.Euler(0f, 0f, _angle);
            Tick();
            if (t >= 1f)
            {
                _audio.PlayOneShot("stop", 0.9f);
                ShowResult();
            }
        }

        private void Tick()
        {
            int w = Mathf.FloorToInt(_angle / _wedge);
            if (w != _lastTickWedge)
            {
                _lastTickWedge = w;
                float sp = _phase == Phase.Spin ? Mathf.Clamp01((1f - _spinT)) : 0.5f;
                _audio.PlayPitched("tick", 0.5f, Mathf.Lerp(0.9f, 1.25f, sp));
            }
        }

        private void ShowResult()
        {
            _phase = Phase.Result; _resultT = 0f;
            if (_resultBanner != null) { _resultBanner.transform.SetAsLastSibling(); _resultBanner.SetActive(true); }
            if (_hintText != null) _hintText.text = "Click to collect";
        }

        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            var cb = _onComplete; _onComplete = null;
            _overlay.Close();
            cb?.Invoke();
        }

        // Cursor angle (deg, CCW from +X) around the wheel centre, in the cabinet's local space.
        private bool CursorAngle(out float deg)
        {
            deg = 0f;
            var parent = _wheelRT.parent as RectTransform;
            if (parent == null) return false;
            var canvas = parent.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, cam, out Vector2 lp)) return false;
            Vector2 v = lp - _wheelRT.anchoredPosition;
            if (v.sqrMagnitude < 4f) return false;
            deg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return true;
        }

        // Sector texture: wedge i (matching the label mapping φ_i = 90-(i+0.5)*wedge) gets a palette colour; hub + rim shaded.
        private Sprite MakeWheelSprite(int S, int wedges)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float cx = S / 2f, cy = S / 2f, R = S * 0.48f, wedgeDeg = 360f / wedges;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - cx, dy = y - cy, dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > R) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }
                    float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    int sector = Mathf.FloorToInt(Mathf.Repeat(90f - a, 360f) / wedgeDeg) % wedges;
                    var c = Palette[sector % Palette.Length];
                    float edge = Mathf.SmoothStep(1f, 0.82f, Mathf.Clamp01((dist - R * 0.8f) / (R * 0.2f)));   // darken rim
                    px[y * S + x] = new Color(c.r * edge, c.g * edge, c.b * edge, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
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
