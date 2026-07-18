using System;
using FiresCore.UI;
using FiresCore.UI.Minigames;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.TableGames
{
    /// <summary>
    /// The little mode-select popup every game board funnels through: a title, a flavor line, and a stacked column of
    /// big option buttons (Solo vs Computer / Multiplayer / Join …). Built on <see cref="FiresMinigameOverlay"/> so it
    /// owns the cursor + input block and Esc simply closes it. Picking an option closes the popup first, then runs it.
    /// </summary>
    public static class GameModePopup
    {
        public static void Show(string title, string subtitle, (string Label, string Sub, Action Pick)[] options)
        {
            var overlay = FiresMinigameOverlay.Create("FiresGameModePopup", 6700);
            if (overlay == null || options == null || options.Length == 0) return;

            float h = 150f + options.Length * 92f;
            var panel = NewImg(overlay.PlayField,
                FiresRoundedSkin.RoundedSprite(20, new Color(0.11f, 0.088f, 0.06f, 0.99f), new Color(0.62f, 0.42f, 0.16f, 0.9f), 2));
            Place(panel.rectTransform, 460f, h, 0f, 0f);

            var titleTxt = NewTxt(panel.rectTransform, title, 24, new Color(1f, 0.85f, 0.5f));
            Place(titleTxt.rectTransform, 420f, 34f, 0f, h / 2f - 36f);
            var subTxt = NewTxt(panel.rectTransform, subtitle, 14, new Color(0.78f, 0.7f, 0.55f));
            Place(subTxt.rectTransform, 420f, 22f, 0f, h / 2f - 66f);

            for (int i = 0; i < options.Length; i++)
            {
                var opt = options[i];
                float y = h / 2f - 132f - i * 92f;

                var btn = NewImg(panel.rectTransform,
                    FiresRoundedSkin.RoundedSprite(12, new Color(0.34f, 0.23f, 0.10f, 1f), new Color(0.62f, 0.42f, 0.16f, 0.8f), 1));
                Place(btn.rectTransform, 400f, 80f, 0f, y);
                btn.raycastTarget = true;
                var b = btn.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.ColorTint;
                b.targetGraphic = btn;
                var cols = b.colors; cols.highlightedColor = new Color(1.25f, 1.2f, 1.05f); cols.pressedColor = new Color(0.85f, 0.8f, 0.7f); b.colors = cols;
                b.onClick.AddListener(() => { overlay.Close(); opt.Pick?.Invoke(); });

                var lbl = NewTxt(btn.rectTransform, opt.Label, 19, new Color(0.98f, 0.92f, 0.78f));
                Place(lbl.rectTransform, 380f, 26f, 0f, 14f);
                var sub = NewTxt(btn.rectTransform, opt.Sub, 12.5f, new Color(0.74f, 0.66f, 0.5f));
                Place(sub.rectTransform, 372f, 34f, 0f, -16f);
                sub.enableWordWrapping = true;
            }

            var cancel = NewTxt(panel.rectTransform, "Esc — never mind", 12, new Color(0.55f, 0.5f, 0.4f));
            Place(cancel.rectTransform, 300f, 18f, 0f, -h / 2f + 20f);

            overlay.ApplyFont();
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

        private static TextMeshProUGUI NewTxt(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        private static void Place(RectTransform rt, float w, float hgt, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, hgt);
            rt.anchoredPosition = new Vector2(x, y);
        }
    }
}
