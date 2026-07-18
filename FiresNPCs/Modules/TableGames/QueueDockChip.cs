using System;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.TableGames
{
    /// <summary>
    /// The queued-but-free dock chip: a small pill parked bottom-center on its own high-order canvas while the player
    /// waits for a match (chess seat wait, tournament queue). It heartbeats the claim, polls readiness, and when the
    /// match forms it pulses gold and flips to the ready label. Clickable whenever the cursor is free (Esc menu,
    /// inventory — the canvas sits above them); click reopens/joins, the ✕ or right-click cancels. One chip at a time.
    /// </summary>
    public sealed class QueueDockChip : MonoBehaviour
    {
        private static QueueDockChip _current;
        public static bool Active => _current != null;

        private Func<bool> _isReady;
        private Action _onClick, _onCancel, _onBeat;
        private string _label, _readyLabel;

        private Image _bkg;
        private TMP_Text _txt;
        private float _beatT, _pollT, _pulseT;
        private bool _ready;

        private static readonly Color Idle = new Color(0.13f, 0.10f, 0.07f, 0.97f);
        private static readonly Color IdleEdge = new Color(0.62f, 0.42f, 0.16f, 0.9f);
        private static readonly Color Ready1 = new Color(0.45f, 0.31f, 0.10f, 1f);
        private static readonly Color Ready2 = new Color(0.72f, 0.52f, 0.16f, 1f);

        public static void Show(string label, string readyLabel, Func<bool> isReady, Action onClick, Action onCancel, Action onBeat)
        {
            Hide();
            var go = new GameObject("FiresQueueDockChip");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6800;                       // above Esc menu + inventory — clickable from both
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var chip = go.AddComponent<QueueDockChip>();
            chip._label = label; chip._readyLabel = readyLabel;
            chip._isReady = isReady; chip._onClick = onClick; chip._onCancel = onCancel; chip._onBeat = onBeat;
            chip.BuildUi();
            _current = chip;
        }

        public static void Hide()
        {
            if (_current == null) return;
            var c = _current;
            _current = null;
            if (c != null && c.gameObject != null) Destroy(c.gameObject);
        }

        private void BuildUi()
        {
            _bkg = NewImg(transform, FiresRoundedSkin.RoundedSprite(16, Idle, IdleEdge, 2));
            var rt = _bkg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(340f, 44f);
            rt.anchoredPosition = new Vector2(0f, 14f);
            _bkg.raycastTarget = true;

            var btn = _bkg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { var a = _onClick; Hide(); a?.Invoke(); });

            _txt = NewTxt(rt, _label, 15f, new Color(0.95f, 0.88f, 0.7f));
            var trt = _txt.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 0f); trt.offsetMax = new Vector2(-34f, 0f);
            _txt.alignment = TextAlignmentOptions.Left;

            var x = NewImg(rt, FiresRoundedSkin.RoundedSprite(9, new Color(0.30f, 0.14f, 0.10f, 1f), new Color(0.7f, 0.35f, 0.25f, 0.9f), 1));
            var xrt = x.rectTransform;
            xrt.anchorMin = xrt.anchorMax = new Vector2(1f, 0.5f);
            xrt.pivot = new Vector2(1f, 0.5f);
            xrt.sizeDelta = new Vector2(24f, 24f);
            xrt.anchoredPosition = new Vector2(-8f, 0f);
            x.raycastTarget = true;
            var xb = x.gameObject.AddComponent<Button>();
            xb.transition = Selectable.Transition.None;
            xb.onClick.AddListener(() => { var a = _onCancel; Hide(); a?.Invoke(); });
            var xt = NewTxt(xrt, "×", 16f, new Color(0.95f, 0.75f, 0.65f));
            var xtr = xt.rectTransform; xtr.anchorMin = Vector2.zero; xtr.anchorMax = Vector2.one; xtr.offsetMin = xtr.offsetMax = Vector2.zero;

            var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
            if (averia != null) foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = averia;
        }

        private void Update()
        {
            // Logout / world change — drop the chip, the seat heartbeat lapses on its own.
            if (ZNet.instance == null || Player.m_localPlayer == null) { Hide(); return; }

            _beatT += Time.unscaledDeltaTime;
            if (_beatT > 5f) { _beatT = 0f; try { _onBeat?.Invoke(); } catch { } }

            _pollT += Time.unscaledDeltaTime;
            if (_pollT > 0.6f && !_ready)
            {
                _pollT = 0f;
                bool ready = false;
                try { ready = _isReady != null && _isReady(); } catch { }
                if (ready)
                {
                    _ready = true;
                    _txt.text = _readyLabel;
                    _txt.color = new Color(1f, 0.92f, 0.6f);
                }
            }

            if (_ready)
            {
                _pulseT += Time.unscaledDeltaTime * 3.2f;
                float k = (Mathf.Sin(_pulseT) + 1f) * 0.5f;
                _bkg.color = Color.Lerp(Ready1, Ready2, k);
            }
        }

        private static Image NewImg(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text NewTxt(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }
    }
}
