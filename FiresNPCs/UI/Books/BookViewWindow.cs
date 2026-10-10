using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Books
{
    /// <summary>
    /// A minimal preview window that shows a firesbooks book (default the animated opening book) rendered live by
    /// <see cref="FiresBookView"/> on a centered RawImage. For confirming the render/animation/orientation in-game
    /// before wiring the book into a real panel (the quest journal). Toggled by the firesnpcs_bookview command.
    /// </summary>
    public static class BookViewWindow
    {
        private static GameObject _canvas;
        private static RawImage _img;
        private static string _prefab = "FiresBook_Opening";

        public static bool IsOpen => _canvas != null && _canvas.activeSelf;

        public static void Toggle(string prefab = null)
        {
            if (IsOpen) Close();
            else Open(prefab);
        }

        // Console dispatcher:
        //   firesnpcs_bookview                          -> toggle (default FiresBook_Opening)
        //   firesnpcs_bookview <PrefabName>             -> toggle that prefab
        //   firesnpcs_bookview capture                  -> capture render + state now
        //   firesnpcs_bookview set y p distMul hMul ex ey ez  -> set framing, reshow, capture (tune without redeploy)
        public static void Command(string[] a)
        {
            if (a == null || a.Length <= 1) { Toggle(); return; }
            string sub = a[1].ToLowerInvariant();
            if (sub == "capture") { FiresBookView.Ensure().Capture(9); return; }
            if (sub == "set" && a.Length >= 9)
            {
                var v = FiresBookView.Ensure();
                v.Yaw = P(a[2]); v.Pitch = P(a[3]); v.DistanceMul = P(a[4]); v.HeightMul = P(a[5]);
                v.BookEuler = new Vector3(P(a[6]), P(a[7]), P(a[8]));
                Open(_prefab);   // re-show applies BookEuler at spawn + re-triggers auto-capture
                return;
            }
            Toggle(a[1]);   // otherwise treat as a prefab name
        }

        private static float P(string s) => float.TryParse(s, out var f) ? f : 0f;

        public static void Open(string prefab)
        {
            if (!string.IsNullOrEmpty(prefab)) _prefab = prefab;
            Ensure();
            var view = FiresBookView.Ensure();
            if (!view.Show(_prefab))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "book prefab not found: " + _prefab);
                return;
            }
            _img.texture = view.Texture;
            _canvas.SetActive(true);
            try { FiresCore.UI.InputBlock.Block(true); } catch { }
        }

        public static void Close()
        {
            if (_canvas != null) _canvas.SetActive(false);
            FiresBookView.Instance?.Clear();
            try { FiresCore.UI.InputBlock.Block(false); } catch { }
        }

        private static void Ensure()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("FiresBookViewCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(_canvas);
            var c = _canvas.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 6000;
            var s = _canvas.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;

            var imgGo = new GameObject("Book", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(_canvas.transform, false);
            _img = imgGo.GetComponent<RawImage>();
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, 900f);

            var hint = new GameObject("Hint", typeof(RectTransform));
            hint.transform.SetParent(_canvas.transform, false);
            var ht = hint.AddComponent<TMPro.TextMeshProUGUI>();
            ht.text = "Book preview — Esc to close";
            ht.fontSize = 22f; ht.alignment = TMPro.TextAlignmentOptions.Center; ht.color = new Color(0.95f, 0.9f, 0.75f);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = new Vector2(0.5f, 0f); hrt.anchorMax = new Vector2(0.5f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 60f); hrt.sizeDelta = new Vector2(800f, 40f);

            _canvas.AddComponent<BookViewEsc>();
        }
    }

    internal sealed class BookViewEsc : MonoBehaviour
    {
        private void Update()
        {
            if (BookViewWindow.IsOpen && Input.GetKeyDown(KeyCode.Escape)) BookViewWindow.Close();
        }
    }
}
