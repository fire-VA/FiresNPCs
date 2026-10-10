using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Restyles the shared vanilla <see cref="UITooltip"/> popup to match the
    /// CompanionInventoryScreen tooltip style (flat dark-brown background,
    /// gold outline, bold gold title, warm-tan body).
    ///
    /// Width is left to vanilla's own sizing (the popup keeps its prefab
    /// default width and vanilla resizes the height based on text bounds).
    /// We tried a dynamic-width version that read TMP preferredWidth and
    /// clamped per-show; it didn't match the look the user wanted, so we
    /// reverted to vanilla width.
    ///
    /// Style constants mirror CompanionInventoryScreen.CreateTooltipPanel
    /// exactly. The popup is a SINGLE shared static GameObject
    /// (<c>UITooltip.m_tooltip</c>); we restyle it once (dedupe via
    /// <see cref="TooltipStyleMarker"/>).
    /// </summary>
    public static class UITooltipStyleOverride
    {
        // Background
        private static readonly Color BackgroundColor = new Color(0.08f, 0.06f, 0.04f, 0.95f);

        // Gold outline (border)
        private static readonly Color OutlineColor = new Color(0.5f, 0.4f, 0.25f, 0.8f);
        private static readonly Vector2 OutlineDistance = new Vector2(2f, -2f);

        // Title (topic) - bold gold
        private static readonly Color TitleColor = new Color(1f, 0.85f, 0.4f);
        private const float TitleFontSize = 14f;

        // Body (text) - warm tan
        private static readonly Color BodyColor = new Color(0.9f, 0.85f, 0.75f);
        private const float BodyFontSize = 11f;
        private const float BodyLineSpacing = 15f;

        private static bool _hierarchyLogged;

        /// <summary>
        /// One-time styling pass — colors, fonts, outline, decorative-image
        /// disable. Idempotent via <see cref="TooltipStyleMarker"/>.
        /// </summary>
        public static void Apply(GameObject root)
        {
            if (root == null) return;
            if (root.GetComponent<TooltipStyleMarker>() != null) return;
            var marker = root.AddComponent<TooltipStyleMarker>();

            // First call: dump hierarchy so we can verify child names match our assumptions
            if (!_hierarchyLogged)
            {
                _hierarchyLogged = true;
                LogHierarchy(root.transform, 0);
            }

            // Background: try named child "Background" first (vanilla pattern, see SettingsTooltip),
            // then root's own Image, then any descendant Image as last resort.
            Image bg = FindBackground(root);
            if (bg != null)
            {
                bg.type = Image.Type.Simple;
                bg.sprite = null;
                bg.color = BackgroundColor;

                var shadow = bg.GetComponent<Shadow>();
                if (shadow != null && !(shadow is Outline))
                    shadow.enabled = false;

                var outline = bg.GetComponent<Outline>();
                if (outline == null)
                    outline = bg.gameObject.AddComponent<Outline>();
                outline.effectColor = OutlineColor;
                outline.effectDistance = OutlineDistance;
                outline.enabled = true;
            }

            // Disable other decorative Images (parchment corners, frames) so the
            // flat dark fill + gold outline shows cleanly.
            var allImages = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < allImages.Length; i++)
            {
                if (allImages[i] == bg) continue;
                allImages[i].enabled = false;
            }

            // Title and body: name-match first ("Topic" / "Text"), then by hierarchy order.
            // Vanilla popup tends to put body first in children, so a blind index-0 = title
            // assumption flips the sizes. Name match is the source of truth here.
            TMP_Text title = FindNamedText(root.transform, "Topic");
            TMP_Text body = FindNamedText(root.transform, "Text");

            if (title == null || body == null)
            {
                var texts = root.GetComponentsInChildren<TMP_Text>(true);
                foreach (var tx in texts)
                {
                    if (title == null && tx != body && IsLikelyTitle(tx.gameObject.name)) title = tx;
                    if (body == null && tx != title && IsLikelyBody(tx.gameObject.name)) body = tx;
                }
                // Last-resort positional fallback: pick the smaller TMP as title (header is shorter).
                if ((title == null || body == null) && texts.Length >= 2)
                {
                    if (title == null) title = texts[0];
                    if (body == null) body = texts[1] != title ? texts[1] : (texts.Length > 2 ? texts[2] : null);
                }
            }

            if (title != null) StyleTitle(title);
            if (body != null) StyleBody(body);
        }

        private static Image FindBackground(GameObject root)
        {
            var bgChild = Utils.FindChild(root.transform, "Background");
            if (bgChild != null)
            {
                var img = bgChild.GetComponent<Image>();
                if (img != null) return img;
            }
            var rootImg = root.GetComponent<Image>();
            if (rootImg != null) return rootImg;
            return root.GetComponentInChildren<Image>(true);
        }

        private static TMP_Text FindNamedText(Transform root, string name)
        {
            var child = Utils.FindChild(root, name);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static bool IsLikelyTitle(string name)
        {
            return name.IndexOf("topic", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("header", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyBody(string name)
        {
            return name.Equals("text", System.StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("desc", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void StyleTitle(TMP_Text t)
        {
            t.enableAutoSizing = false;
            t.color = TitleColor;
            t.fontStyle = FontStyles.Bold;
            t.fontSize = TitleFontSize;
            UIBuilderHelper.ApplyValheimFont(t);
        }

        private static void StyleBody(TMP_Text t)
        {
            t.enableAutoSizing = false;
            t.color = BodyColor;
            t.fontStyle = FontStyles.Normal;
            t.fontSize = BodyFontSize;
            t.lineSpacing = BodyLineSpacing;
            UIBuilderHelper.ApplyValheimFont(t);
        }

        private static void LogHierarchy(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            string components = "";
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                if (components.Length > 0) components += ", ";
                components += c.GetType().Name;
            }
            FiresLogger.LogInfo($"[TooltipStyle] {indent}{t.name} [{components}]");
            for (int i = 0; i < t.childCount; i++)
                LogHierarchy(t.GetChild(i), depth + 1);
        }
    }

    /// <summary>
    /// Tag MonoBehaviour added to the shared popup once so subsequent show
    /// callbacks skip re-styling.
    /// </summary>
    internal class TooltipStyleMarker : MonoBehaviour { }

    /// <summary>
    /// Postfixes <see cref="UITooltip.OnHoverStart"/> — the moment the shared
    /// popup is parented and shown. We restyle the static
    /// <c>UITooltip.m_tooltip</c> GameObject so the new look applies to every
    /// tooltip in the game (item hovers, crafting reqs, building hammer,
    /// FejdStartup world summary, etc.) and recompute its width every time
    /// since the text content changes per item.
    /// </summary>
    [HarmonyPatch(typeof(UITooltip))]
    internal static class UITooltip_StylePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnHoverStart", typeof(GameObject))]
        private static void OnHoverStart_Postfix()
        {
            if (UITooltip.m_tooltip != null)
                UITooltipStyleOverride.Apply(UITooltip.m_tooltip);
        }

        // Vanilla signature: Set(string topic, string text, RectTransform anchor, Vector2 fixedPosition).
        [HarmonyPostfix]
        [HarmonyPatch(nameof(UITooltip.Set), typeof(string), typeof(string), typeof(RectTransform), typeof(Vector2))]
        private static void Set_Postfix()
        {
            if (UITooltip.m_tooltip != null)
                UITooltipStyleOverride.Apply(UITooltip.m_tooltip);
        }
    }
}
