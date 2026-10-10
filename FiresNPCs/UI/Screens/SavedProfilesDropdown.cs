using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Shared "Saved Profiles" dropdown widget used by every admin config screen (Trader / Banker /
    /// Gambler / Marketplace / Transmog / Mail / Achievement / Leaderboard). The dropdown shows
    /// "Saved Profiles" as its caption when closed; opening it lists every name from the supplied
    /// profile-name source, alphabetized. Clicking an option fires <c>onSelect(name)</c> with the
    /// chosen profile so the host screen can write it into its input field and load whatever data
    /// it needs to.
    ///
    /// Widget shape mirrors <c>MarketplacePlayerPanel.BuildCurrencyDropdown</c> (Label + Arrow +
    /// Template + Viewport + Content + Item template) — one dropdown construction across the project
    /// rather than copy-pasting per screen.
    /// </summary>
    public static class SavedProfilesDropdown
    {
        private const float ScrollbarWidth = 10f;

        /// <summary>Constructs the dropdown anchored to <paramref name="parent"/> at the supplied
        /// fractional rect (anchor min/max). Returns the TMP_Dropdown so the host can stash it for
        /// later <see cref="Refresh"/> calls. The <paramref name="onSelect"/> callback runs whenever
        /// the player picks an option (NOT the header row); the dropdown auto-resets its caption to
        /// "Saved Profiles" so the user can pick again without it latching onto the previous pick.</summary>
        public static TMP_Dropdown Build(
            RectTransform parent,
            float xMin, float xMax, float yMin, float yMax,
            Action<string> onSelect)
        {
            var go = new GameObject("SavedProfilesDropdown", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.10f, 0.07f, 0.92f);

            var dd = go.AddComponent<TMP_Dropdown>();

            // Caption label — what the closed dropdown shows.
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-22f, -2f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 14f; label.color = new Color(0.96f, 0.91f, 0.76f);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            // Arrow chevron on the right.
            var arrowGo = new GameObject("Arrow", typeof(RectTransform));
            arrowGo.transform.SetParent(go.transform, false);
            var art = arrowGo.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(1f, 0.5f); art.anchorMax = new Vector2(1f, 0.5f);
            art.pivot = new Vector2(1f, 0.5f);
            art.sizeDelta = new Vector2(20f, 20f); art.anchoredPosition = new Vector2(-4f, 0f);
            var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowText.font = TMP_Settings.defaultFontAsset;
            arrowText.text = "v"; arrowText.fontSize = 16f; arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.color = new Color(1f, 0.85f, 0.55f);

            // Template — the popup panel shown when the dropdown opens.
            var template = new GameObject("Template", typeof(RectTransform), typeof(Image));
            template.transform.SetParent(go.transform, false);
            var trt = template.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 0f); trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 240f); trt.anchoredPosition = new Vector2(0f, -2f);
            template.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.98f);

            var scroll = template.AddComponent<ScrollRect>();
            // RectMask2D (NOT Mask+Image) — the alpha-0-sprite-null Image pattern that ships in Unity's
            // default dropdown template can fail to write the stencil bit on some setups, hiding the
            // option list. RectMask2D is a pure rect clip with no Graphic dependency.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(template.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0f, 28f);

            // Item template — TMP_Dropdown clones this once per option at open time.
            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            var irt = item.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(1f, 0.5f);
            irt.sizeDelta = new Vector2(0f, 26f);

            var itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(Image));
            itemBg.transform.SetParent(item.transform, false);
            var ibgRt = itemBg.GetComponent<RectTransform>();
            ibgRt.anchorMin = Vector2.zero; ibgRt.anchorMax = Vector2.one;
            ibgRt.offsetMin = Vector2.zero; ibgRt.offsetMax = Vector2.zero;
            itemBg.GetComponent<Image>().color = new Color(0.18f, 0.14f, 0.09f, 0.95f);

            var itemChecked = new GameObject("Item Checkmark", typeof(RectTransform), typeof(Image));
            itemChecked.transform.SetParent(item.transform, false);
            var icRt = itemChecked.GetComponent<RectTransform>();
            icRt.anchorMin = new Vector2(0f, 0.5f); icRt.anchorMax = new Vector2(0f, 0.5f);
            icRt.sizeDelta = new Vector2(18f, 18f); icRt.anchoredPosition = new Vector2(12f, 0f);
            itemChecked.GetComponent<Image>().color = new Color(1f, 0.85f, 0.55f);

            var itemLabelGo = new GameObject("Item Label", typeof(RectTransform));
            itemLabelGo.transform.SetParent(item.transform, false);
            var ilrt = itemLabelGo.GetComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero; ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(28f, 2f); ilrt.offsetMax = new Vector2(-4f, -2f);
            var itemLabelText = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabelText.font = TMP_Settings.defaultFontAsset;
            itemLabelText.fontSize = 13f; itemLabelText.color = new Color(0.96f, 0.91f, 0.76f);
            itemLabelText.alignment = TextAlignmentOptions.MidlineLeft;

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBg.GetComponent<Image>();
            toggle.graphic = itemChecked.GetComponent<Image>();
            toggle.isOn = true;

            scroll.content = crt; scroll.viewport = vrt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            // Scrollbar on the right edge so long profile lists (60+) scroll properly.
            var scrollbar = FiresCore.UI.UIBuilderHelper.CreateVerticalScrollbar(template.transform, ScrollbarWidth);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            dd.template = trt;
            dd.captionText = label;
            dd.itemText = itemLabelText;
            template.SetActive(false);

            // Wire the selection callback. Index 0 is always the "Saved Profiles" header row → no-op;
            // any other index passes the picked option text to the host's onSelect.
            dd.onValueChanged.AddListener(idx =>
            {
                if (dd == null || idx <= 0) return;
                var opt = idx < dd.options.Count ? dd.options[idx] : null;
                string picked = opt?.text;
                // Reset the caption back to the header so the user can pick again without the
                // dropdown latching onto the previous selection.
                dd.SetValueWithoutNotify(0);
                if (dd.captionText != null) dd.captionText.text = "Saved Profiles";
                if (!string.IsNullOrEmpty(picked)) onSelect?.Invoke(picked);
            });
            return dd;
        }

        /// <summary>Pixel-offset variant for screens that anchor their content to the TOP of the screen
        /// rect with absolute pixel offsets (Banker/Gambler/Mail/Transmog/Marketplace). The dropdown
        /// occupies the right band at fractional <paramref name="xMin"/> to a right inset of
        /// <paramref name="rightPx"/>, top-anchored at <paramref name="topPx"/> with fixed
        /// <paramref name="height"/>.</summary>
        public static TMP_Dropdown BuildTopAnchored(
            RectTransform parent,
            float xMin, float rightPx, float topPx, float height,
            Action<string> onSelect)
        {
            var dd = Build(parent, xMin, 1f, 0f, 1f, onSelect);
            var rt = dd.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(topPx + height));
            rt.offsetMax = new Vector2(-rightPx, -topPx);
            return dd;
        }

        /// <summary>Builds a dropdown bound to a profile <see cref="TMP_InputField"/>: picking an option
        /// writes the chosen name into <paramref name="target"/> and runs <paramref name="afterPick"/>
        /// (e.g. load that profile's body). The option list is seeded from <paramref name="source"/> and
        /// re-seeded on each <see cref="Refresh"/>. This is the one-liner every "profile input + arrow"
        /// screen uses so the glue lives here, not copy-pasted per screen.</summary>
        public static TMP_Dropdown AttachToInput(
            RectTransform parent,
            float xMin, float xMax, float yMin, float yMax,
            TMP_InputField target,
            Func<IEnumerable<string>> source,
            Action<string> afterPick = null)
        {
            var dd = Build(parent, xMin, xMax, yMin, yMax, name =>
            {
                if (target != null) target.SetTextWithoutNotify(name);
                afterPick?.Invoke(name);
            });
            if (source != null) Refresh(dd, source());
            return dd;
        }

        /// <summary>Top-anchored variant of <see cref="AttachToInput"/> for pixel-offset screens.</summary>
        public static TMP_Dropdown AttachToInputTopAnchored(
            RectTransform parent,
            float xMin, float rightPx, float topPx, float height,
            TMP_InputField target,
            Func<IEnumerable<string>> source,
            Action<string> afterPick = null)
        {
            var dd = BuildTopAnchored(parent, xMin, rightPx, topPx, height, name =>
            {
                if (target != null) target.SetTextWithoutNotify(name);
                afterPick?.Invoke(name);
            });
            if (source != null) Refresh(dd, source());
            return dd;
        }

        /// <summary>Rebuilds the option list. Pass the result of the host's GetAllProfileNames-style
        /// call. The first option is always the "Saved Profiles" header (index 0, no-op on selection).</summary>
        public static void Refresh(TMP_Dropdown dd, IEnumerable<string> profileNames)
        {
            if (dd == null) return;
            var sorted = (profileNames ?? Enumerable.Empty<string>()).OrderBy(n => n).ToList();
            var options = new List<TMP_Dropdown.OptionData>(sorted.Count + 1);
            options.Add(new TMP_Dropdown.OptionData("Saved Profiles"));
            foreach (var n in sorted) options.Add(new TMP_Dropdown.OptionData(n));
            dd.ClearOptions();
            dd.AddOptions(options);
            dd.SetValueWithoutNotify(0);
            if (dd.captionText != null) dd.captionText.text = "Saved Profiles";
        }
    }
}
