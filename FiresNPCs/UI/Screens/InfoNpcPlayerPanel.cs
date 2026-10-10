using System;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Player-facing Info NPC panel (read-only display mode).
    /// Shows info text and optional image to the player when interacting with an Info NPC.
    /// Uses the same background/border style as the dialogue panel (crafting_panel_bkg).
    ///
    /// Layout modes:
    ///   NO IMAGE  - Header at top, scrollable text fills the remaining area below.
    ///   WITH IMAGE - Header at top, then image and scrollable text split the content
    ///                area vertically (image top half, text bottom half).
    ///
    /// Hyperlinks:
    ///   Use TMP rich-text link tags in the info text to embed clickable URLs:
    ///     &lt;link="https://discord.gg/xxx"&gt;&lt;u&gt;Join our Discord&lt;/u&gt;&lt;/link&gt;
    ///   Clicking opens the URL in the default browser.
    /// </summary>
    public static class InfoNpcPlayerPanel
    {
        private static GameObject _panelInstance;
        private static GameObject _standaloneCanvas;
        private static TMP_Text _headerText;
        private static RawImage _previewImage;
        private static GameObject _imageContainer;
        private static TMP_Text _contentText;
        private static TMP_Text _sizeButtonText;
        private static GameObject _scrollArea;

        // Version number - increment this to force panel recreation after code changes
        private const int PANEL_VERSION = 14;
        private static int _createdVersion = 0;

        private static string RuntimeSpritesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestSprites);

        // Size state
        private enum PanelSize { Large, Medium, Small }
        private static PanelSize _currentSize = PanelSize.Large;

        /// <summary>
        /// Shows the info panel to the player.
        /// </summary>
        public static void Show(string npcName, InfoNpcScreenController.InfoProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.InfoText))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npcName} has nothing to say.");
                return;
            }

            EnsurePanelCreated();

            if (_panelInstance == null)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, profile.InfoText);
                return;
            }

            // Set header
            if (_headerText != null)
            {
                _headerText.text = !string.IsNullOrEmpty(profile.HeaderText) ? profile.HeaderText : npcName;
            }

            // Handle image
            bool hasImage = !string.IsNullOrEmpty(profile.PngPath);
            bool imageLoaded = false;

            if (hasImage && _previewImage != null)
            {
                var texture = LoadTexture(profile.PngPath);
                if (texture != null)
                {
                    _previewImage.texture = texture;
                    _previewImage.enabled = true;
                    imageLoaded = true;
                }
            }

            // Show or hide image container
            if (_imageContainer != null)
            {
                _imageContainer.SetActive(imageLoaded);
            }

            // Adjust scroll area anchors based on whether image is shown
            if (_scrollArea != null)
            {
                var scrollRect = _scrollArea.GetComponent<RectTransform>();
                if (scrollRect != null)
                {
                    if (imageLoaded)
                    {
                        // Image shown: scroll area in bottom half of content area
                        scrollRect.anchorMin = new Vector2(0.05f, 0.04f);
                        scrollRect.anchorMax = new Vector2(0.95f, 0.48f);
                    }
                    else
                    {
                        // No image: scroll area fills the full content area below header
                        scrollRect.anchorMin = new Vector2(0.05f, 0.04f);
                        scrollRect.anchorMax = new Vector2(0.95f, 0.87f);
                    }
                    scrollRect.offsetMin = Vector2.zero;
                    scrollRect.offsetMax = Vector2.zero;
                }
            }

            // Render the ordered text/image block stack (kg ServerInfo inline `<image=...>` grammar; a
            // profile with no image markers is just one text block — same path).
            RenderContentBlocks(profile.InfoText);

            // Reset scroll position to top
            var scroll = _scrollArea?.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 1f;
            }

            // Show and center
            _panelInstance.SetActive(true);
            CenterPanel();
            GUIManager.BlockInput(true);
        }

        /// <summary>
        /// Hides the info panel.
        /// </summary>
        public static void Hide()
        {
            if (_panelInstance != null)
            {
                _panelInstance.SetActive(false);
            }
            NpcHoverHudSuppressor.Clear();
            GUIManager.BlockInput(false);
        }

        /// <summary>
        /// Returns true if the panel is currently visible.
        /// </summary>
        public static bool IsVisible => _panelInstance != null && _panelInstance.activeSelf;

        /// <summary>
        /// Forces recreation of the panel (useful after code changes).
        /// </summary>
        public static void DestroyPanel()
        {
            // Destroy the standalone canvas — it owns the panel as a child.
            if (_standaloneCanvas != null)
            {
                UnityEngine.Object.Destroy(_standaloneCanvas);
                _standaloneCanvas = null;
            }
            else if (_panelInstance != null)
            {
                UnityEngine.Object.Destroy(_panelInstance);
            }
            _panelInstance = null;
            _createdVersion = 0;
        }

        /// <summary>
        /// Cleans up the standalone canvas on logout/scene change.
        /// Called from UIInitializer.ResetInitialization.
        /// </summary>
        public static void Cleanup()
        {
            DestroyPanel();
        }

        private static void EnsurePanelCreated()
        {
            if (_panelInstance != null && _createdVersion != PANEL_VERSION)
            {
                Debug.Log($"[InfoNpcPlayerPanel] Panel version mismatch ({_createdVersion} vs {PANEL_VERSION}), recreating...");
                DestroyPanel();
            }

            if (_panelInstance != null) return;

            if (Hud.instance == null)
            {
                Debug.LogWarning("[InfoNpcPlayerPanel] Hud not ready");
                return;
            }

            // Build a standalone, top-level Canvas (matches the help panel pattern).
            // Parenting under Hud's canvas dirties the inventory grid every show/hide
            // and tanks fps. The canvas is unparented so it's a true render root.
            var canvasGo = new GameObject("InfoNpcPlayerPanel_Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above Valheim's IngameGui sibling canvases (HudMessage etc.) so MessageHud
            // toasts don't render over the info panel. Stays below the help panel (300).
            canvas.sortingOrder = 210;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            _standaloneCanvas = canvasGo;
            CreatePanel(canvasGo.transform);
            _createdVersion = PANEL_VERSION;
        }

        private static void CreatePanel(Transform parent)
        {
            // Main panel
            _panelInstance = new GameObject("InfoNpcPlayerPanel");
            _panelInstance.transform.SetParent(parent, false);

            var panelRect = _panelInstance.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(500, 600);
            panelRect.anchoredPosition = Vector2.zero;

            // Background - use crafting_panel_bkg to match the dialogue panel style
            var bgImage = _panelInstance.AddComponent<Image>();
            var bgSprite = CodegenUIWiring.ResolveSpriteByNamePublic("crafting_panel_bkg");
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
                bgImage.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else
            {
                var fallbackSprite = UIPrefabFactory.GetBackgroundSprite();
                if (fallbackSprite != null)
                {
                    bgImage.sprite = fallbackSprite;
                    bgImage.type = Image.Type.Sliced;
                    bgImage.color = new Color(1f, 1f, 1f, 0.85f);
                }
                else
                {
                    bgImage.color = UIFontConfig.Colors.PanelBackground;
                }
            }
            // Admin-tunable background opacity (UI.Dialogue/InfoBackgroundOpacity) scales whatever
            // alpha the style above chose.
            try
            {
                float infoOpacity = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configInfoBgOpacity?.Value ?? 1f;
                var c = bgImage.color;
                c.a *= Mathf.Clamp(infoOpacity, 0.1f, 1f);
                bgImage.color = c;
            }
            catch { }
            bgImage.raycastTarget = true;

            // === ELEMENT 1: Header Text (top band) ===
            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(_panelInstance.transform, false);
            var headerRect = headerGO.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.05f, 0.90f);
            headerRect.anchorMax = new Vector2(0.95f, 0.98f);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;

            _headerText = headerGO.AddComponent<TextMeshProUGUI>();
            _headerText.text = "Info";
            _headerText.fontSize = 24;
            _headerText.color = UIFontConfig.Colors.BrightGold;
            _headerText.alignment = TextAlignmentOptions.Center;
            _headerText.fontStyle = FontStyles.Bold;
            UIBuilderHelper.ApplyValheimFont(_headerText);

            // === ELEMENT 2: Image Container (upper content area: 50% to 88%) ===
            _imageContainer = new GameObject("ImageContainer");
            _imageContainer.transform.SetParent(_panelInstance.transform, false);
            var imageContainerRect = _imageContainer.AddComponent<RectTransform>();
            imageContainerRect.anchorMin = new Vector2(0.05f, 0.50f);
            imageContainerRect.anchorMax = new Vector2(0.95f, 0.88f);
            imageContainerRect.offsetMin = Vector2.zero;
            imageContainerRect.offsetMax = Vector2.zero;

            var imageGO = new GameObject("Image");
            imageGO.transform.SetParent(_imageContainer.transform, false);
            var imageRect = imageGO.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            _previewImage = imageGO.AddComponent<RawImage>();
            _previewImage.raycastTarget = false;

            _imageContainer.SetActive(false);

            // === ELEMENT 3: Scrollable Content Text ===
            // ScrollRect container - anchors are adjusted at Show-time
            _scrollArea = new GameObject("ScrollArea");
            _scrollArea.transform.SetParent(_panelInstance.transform, false);
            var scrollAreaRect = _scrollArea.AddComponent<RectTransform>();
            scrollAreaRect.anchorMin = new Vector2(0.05f, 0.04f);
            scrollAreaRect.anchorMax = new Vector2(0.95f, 0.87f);
            scrollAreaRect.offsetMin = Vector2.zero;
            scrollAreaRect.offsetMax = Vector2.zero;

            var scrollRectComp = _scrollArea.AddComponent<ScrollRect>();
            scrollRectComp.horizontal = false;
            scrollRectComp.vertical = true;
            scrollRectComp.movementType = ScrollRect.MovementType.Clamped;
            scrollRectComp.scrollSensitivity = 20f;

            // Viewport (mask clips text to scroll area bounds)
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(_scrollArea.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            viewportGO.AddComponent<RectMask2D>();
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = Color.clear;
            viewportImg.raycastTarget = true;

            scrollRectComp.viewport = viewportRect;

            // Content container (expands vertically to fit text)
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // kg ServerInfo parity: the page is an ORDERED STACK of text runs and inline images (the
            // `<image=...>` blocks), so Content is a vertical layout of block children — _contentText is
            // block 0 and RenderContentBlocks appends the rest per profile.
            var contentVlg = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = true;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.spacing = 8f;

            scrollRectComp.content = contentRect;
            _blocksRoot = contentGO.transform;

            var textBlock0 = new GameObject("TextBlock0");
            textBlock0.transform.SetParent(contentGO.transform, false);
            textBlock0.AddComponent<RectTransform>();
            _contentText = textBlock0.AddComponent<TextMeshProUGUI>();
            _contentText.text = "";
            _contentText.fontSize = 16;
            _contentText.color = UIFontConfig.Colors.Gold;
            _contentText.alignment = TextAlignmentOptions.TopLeft;
            _contentText.textWrappingMode = TextWrappingModes.Normal;
            _contentText.richText = true;
            _contentText.overflowMode = TextOverflowModes.Overflow;
            UIBuilderHelper.ApplyValheimFont(_contentText);

            // Link handler - detects clicks on <link="url"> tags and opens URLs
            var linkHandler = _scrollArea.AddComponent<InfoPanelLinkHandler>();
            linkHandler.Initialize(_contentText);

            // Close button (top-right)
            CreateCloseButton(_panelInstance);

            // Size button (top-left)
            CreateSizeButton(_panelInstance);

            // Drag handler
            var dragHandler = _panelInstance.AddComponent<InfoPanelDragHandler>();
            dragHandler.Initialize(_panelInstance);

            // Escape handler
            _panelInstance.AddComponent<InfoPlayerPanelEscapeHandler>();

            // Canvas group
            var canvasGroup = _panelInstance.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            // Nested Canvas for batching isolation
            if (_panelInstance.GetComponent<Canvas>() == null)
            {
                var nestedCanvas = _panelInstance.AddComponent<Canvas>();
                nestedCanvas.overrideSorting = false;
                _panelInstance.AddComponent<GraphicRaycaster>();
            }

            _panelInstance.SetActive(false);
            Debug.Log($"[InfoNpcPlayerPanel] Created player info panel (version {PANEL_VERSION}) with scroll + hyperlink support");
        }

        private static void CreateCloseButton(GameObject panel)
        {
            var buttonGO = new GameObject("CloseButton");
            buttonGO.transform.SetParent(panel.transform, false);

            var buttonRect = buttonGO.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1, 1);
            buttonRect.anchorMax = new Vector2(1, 1);
            buttonRect.pivot = new Vector2(1, 1);
            buttonRect.sizeDelta = new Vector2(30, 30);
            buttonRect.anchoredPosition = new Vector2(-10, -10);

            // Match the dialogue theme: rounded dark chip + thin gold inset border (same as the number badge),
            // reddens on hover. Click stays on InfoPlayerPanelCloseHandler (the panel's proven click path).
            var buttonImg = buttonGO.AddComponent<Image>();
            var closeNormal = new Color(0.14f, 0.11f, 0.07f, 0.98f);
            buttonImg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(7, Color.white, new Color(0.72f, 0.55f, 0.28f, 1f), 1);
            buttonImg.type = Image.Type.Sliced;
            buttonImg.color = closeNormal;
            buttonImg.raycastTarget = true;

            buttonGO.AddComponent<InfoPlayerPanelCloseHandler>();
            var chover = buttonGO.AddComponent<global::FiresRPGmaker.UI.CloseXHover>();
            chover.Img = buttonImg; chover.Normal = closeNormal; chover.Hover = new Color(0.45f, 0.16f, 0.11f, 0.98f);
            buttonGO.transform.SetAsLastSibling();

            var xGO = new GameObject("X");
            xGO.transform.SetParent(buttonGO.transform, false);
            var xRect = xGO.AddComponent<RectTransform>();
            xRect.anchorMin = Vector2.zero;
            xRect.anchorMax = Vector2.one;
            xRect.offsetMin = Vector2.zero;
            xRect.offsetMax = Vector2.zero;
            var xText = xGO.AddComponent<TextMeshProUGUI>();
            xText.text = "\u00d7";   // U+00D7 (in the font; U+2715 \u2715 is not)
            xText.fontSize = 20;
            xText.color = new Color(0.93f, 0.82f, 0.52f);
            xText.fontStyle = FontStyles.Bold;
            xText.alignment = TextAlignmentOptions.Center;
            xText.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(xText);
        }

        private static void CreateSizeButton(GameObject panel)
        {
            var buttonGO = new GameObject("SizeButton");
            buttonGO.transform.SetParent(panel.transform, false);

            var buttonRect = buttonGO.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0, 1);
            buttonRect.anchorMax = new Vector2(0, 1);
            buttonRect.pivot = new Vector2(0, 1);
            buttonRect.sizeDelta = new Vector2(40, 40);
            buttonRect.anchoredPosition = new Vector2(5, -5);

            var buttonImg = buttonGO.AddComponent<Image>();
            buttonImg.color = Color.clear;
            buttonImg.raycastTarget = true;

            var handler = buttonGO.AddComponent<InfoPlayerPanelSizeHandler>();
            handler.OnClicked = OnSizeClicked;
            buttonGO.transform.SetAsLastSibling();

            var sizeGO = new GameObject("SizeText");
            sizeGO.transform.SetParent(buttonGO.transform, false);
            var sizeRect = sizeGO.AddComponent<RectTransform>();
            sizeRect.anchorMin = Vector2.zero;
            sizeRect.anchorMax = Vector2.one;
            sizeRect.offsetMin = Vector2.zero;
            sizeRect.offsetMax = Vector2.zero;
            _sizeButtonText = sizeGO.AddComponent<TextMeshProUGUI>();
            _sizeButtonText.text = "L";
            _sizeButtonText.fontSize = 18;
            _sizeButtonText.color = new Color(0.8f, 0.6f, 0.4f, 0.8f);
            _sizeButtonText.alignment = TextAlignmentOptions.Center;
            _sizeButtonText.raycastTarget = false;
            UIBuilderHelper.ApplyValheimFont(_sizeButtonText);
        }

        private static void OnSizeClicked()
        {
            _currentSize = _currentSize switch
            {
                PanelSize.Large => PanelSize.Medium,
                PanelSize.Medium => PanelSize.Small,
                PanelSize.Small => PanelSize.Large,
                _ => PanelSize.Large
            };

            ApplySize();
            UpdateSizeText();
        }

        private static void ApplySize()
        {
            if (_panelInstance == null) return;

            float scale = _currentSize switch
            {
                PanelSize.Small => 0.75f,
                PanelSize.Medium => 0.9f,
                PanelSize.Large => 1f,
                _ => 1f
            };

            _panelInstance.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static void UpdateSizeText()
        {
            if (_sizeButtonText != null)
            {
                _sizeButtonText.text = _currentSize switch
                {
                    PanelSize.Small => "S",
                    PanelSize.Medium => "M",
                    PanelSize.Large => "L",
                    _ => "L"
                };
            }
        }

        private static void CenterPanel()
        {
            if (_panelInstance == null) return;

            var rect = _panelInstance.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private static Transform _blocksRoot;

        /// <summary>
        /// kg ServerInfo block grammar (schema ServerInfo.md): split the text on `<image=SRC>` markers
        /// into an ordered Text / Image / Text… stack. Block 0 reuses _contentText; extra blocks are
        /// (re)built per Show. Image sources resolve like kg: a path under config/Marketplace (e.g.
        /// Marketplace_CachedImages/x.png), a runtime-sprites name, or an http(s) URL fetched once into
        /// kg's Marketplace_CachedImages folder.
        /// </summary>
        private static void RenderContentBlocks(string infoText)
        {
            if (_contentText == null) return;
            // Drop every block after TextBlock0 from the previous profile.
            if (_blocksRoot != null)
                for (int i = _blocksRoot.childCount - 1; i >= 1; i--)
                    UnityEngine.Object.Destroy(_blocksRoot.GetChild(i).gameObject);

            string text = infoText ?? "";
            int firstMarker = text.IndexOf("<image=", StringComparison.OrdinalIgnoreCase);
            if (firstMarker < 0 || _blocksRoot == null)
            {
                _contentText.text = ParseVariables(text);
                return;
            }

            var segments = text.Split(new[] { "<image=" }, StringSplitOptions.None);
            _contentText.text = ParseVariables(segments[0].TrimEnd('\n'));
            for (int i = 1; i < segments.Length; i++)
            {
                string seg = segments[i];
                int close = seg.IndexOf('>');
                if (close < 0) { AddTextBlock("<image=" + seg); continue; }   // malformed — show verbatim
                AddImageBlock(seg.Substring(0, close).Trim());
                string rest = seg.Substring(close + 1).Trim('\n');
                if (rest.Length > 0) AddTextBlock(rest);
            }
        }

        private static void AddTextBlock(string text)
        {
            var go = new GameObject("TextBlock");
            go.transform.SetParent(_blocksRoot, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = ParseVariables(text);
            tmp.fontSize = _contentText.fontSize;
            tmp.color = _contentText.color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.richText = true;
            UIBuilderHelper.ApplyValheimFont(tmp);
            if (_scrollArea != null) _scrollArea.AddComponent<InfoPanelLinkHandler>().Initialize(tmp);
        }

        private static void AddImageBlock(string source)
        {
            var go = new GameObject("ImageBlock");
            go.transform.SetParent(_blocksRoot, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<RawImage>();
            img.color = new Color(1f, 1f, 1f, 0.06f);   // faint placeholder until the texture lands
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 220f;

            var tex = ResolveBlockTexture(source);
            if (tex != null) ApplyBlockTexture(img, le, tex);
            else if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Game.instance != null) Game.instance.StartCoroutine(FetchBlockTexture(source, img, le));
            }
        }

        private static void ApplyBlockTexture(RawImage img, LayoutElement le, Texture2D tex)
        {
            if (img == null || tex == null) return;
            img.texture = tex;
            img.color = Color.white;
            // Keep the block at a readable height, aspect preserved via preferred height off the viewport width.
            float aspect = tex.height > 0 ? (float)tex.height / tex.width : 0.5f;
            le.preferredHeight = Mathf.Clamp(520f * aspect, 60f, 320f);
        }

        private static string KgCachedImagesFolder => Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", "Marketplace_CachedImages");

        private static Texture2D ResolveBlockTexture(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            // kg-relative path ("Marketplace_CachedImages/wolf.png") under config/Marketplace.
            try
            {
                string kgRel = Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", source.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(kgRel)) return LoadTexture(kgRel);
            }
            catch { }
            // URL → previously fetched cache file (named by the URL's file name).
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string cached = Path.Combine(KgCachedImagesFolder, UrlCacheName(source));
                    if (File.Exists(cached)) return LoadTexture(cached);
                }
                catch { }
                return null;
            }
            // Plain name → the runtime sprites folder (existing loader).
            return LoadTexture(source);
        }

        private static string UrlCacheName(string url)
        {
            string name = url;
            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);
            int q = name.IndexOf('?');
            if (q >= 0) name = name.Substring(0, q);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length == 0) name = "image";
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                name += ".png";
            return name;
        }

        // One-time URL fetch → kg's cache folder → apply if the block still exists (panel may have closed).
        private static System.Collections.IEnumerator FetchBlockTexture(string url, RawImage img, LayoutElement le)
        {
            using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;
                var tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
                if (tex == null) yield break;
                try
                {
                    Directory.CreateDirectory(KgCachedImagesFolder);
                    File.WriteAllBytes(Path.Combine(KgCachedImagesFolder, UrlCacheName(url)), tex.EncodeToPNG());
                }
                catch { }
                if (img != null) ApplyBlockTexture(img, le, tex);
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                if (!Directory.Exists(RuntimeSpritesFolder))
                    Directory.CreateDirectory(RuntimeSpritesFolder);

                string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(RuntimeSpritesFolder, path);

                string[] extensions = { "", ".png", ".jpg", ".jpeg" };
                string foundPath = null;

                foreach (var ext in extensions)
                {
                    string testPath = fullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? fullPath : fullPath + ext;
                    if (File.Exists(testPath))
                    {
                        foundPath = testPath;
                        break;
                    }
                }

                if (foundPath == null)
                {
                    Debug.LogWarning($"[InfoNpcPlayerPanel] Image not found: {fullPath}");
                    return null;
                }

                byte[] data = File.ReadAllBytes(foundPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = false;

                var convType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (convType != null)
                {
                    var method = convType.GetMethod("LoadImage",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                        null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                    if (method != null)
                        loaded = (bool)method.Invoke(null, new object[] { texture, data, false });
                }

                if (!loaded)
                {
                    try
                    {
                        var method = typeof(Texture2D).GetMethod("LoadImage",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                            null, new[] { typeof(byte[]) }, null);
                        if (method != null)
                            loaded = (bool)method.Invoke(texture, new object[] { data });
                    }
                    catch { }
                }

                if (loaded && texture.width > 8)
                {
                    texture.name = Path.GetFileNameWithoutExtension(foundPath);
                    return texture;
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InfoNpcPlayerPanel] Failed to load texture: {ex.Message}");
            }

            return null;
        }

        private static string ParseVariables(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string result = text;

            if (result.IndexOf("%playername%", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string playerName = "Viking";
                try
                {
                    if (Player.m_localPlayer != null)
                        playerName = Player.m_localPlayer.GetPlayerName();
                }
                catch { }

                result = System.Text.RegularExpressions.Regex.Replace(
                    result, "%playername%", playerName,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return result;
        }
    }

    /// <summary>
    /// Escape key handler for player info panel.
    /// </summary>
    public class InfoPlayerPanelEscapeHandler : MonoBehaviour
    {
        private void Update()
        {
            // Escape owned by the central Menu.Show hook (CloseAllPanels → InfoNpcPlayerPanel.Hide) — no
            // self-close on Escape here, so the panel is still open when the hook intercepts the menu open.
            // SPACEBAR closes it (owner request; info panel has no text field, so a direct self-close is safe).
            if (gameObject.activeSelf && Input.GetKeyDown(KeyCode.Space)
                && !FiresCore.Input.FiresInputBlock.IsTyping()) // 0.0.550 typing gate (Fire 10-09)
            {
                InfoNpcPlayerPanel.Hide();
            }
        }
    }

    /// <summary>
    /// Close button handler for player info panel.
    /// </summary>
    public class InfoPlayerPanelCloseHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            InfoNpcPlayerPanel.Hide();
        }
    }

    /// <summary>
    /// Size button handler for player info panel.
    /// </summary>
    public class InfoPlayerPanelSizeHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public Action OnClicked;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            OnClicked?.Invoke();
        }
    }

    /// <summary>
    /// Detects clicks on TMP &lt;link&gt; tags in the info panel text and opens the URL.
    /// 
    /// Usage in info text:
    ///   &lt;link="https://discord.gg/xxx"&gt;&lt;color=#5865F2&gt;&lt;u&gt;Join our Discord&lt;/u&gt;&lt;/color&gt;&lt;/link&gt;
    ///   &lt;link="https://example.com"&gt;&lt;u&gt;Visit website&lt;/u&gt;&lt;/link&gt;
    /// 
    /// The link ID (the value in quotes after link=) is used as the URL.
    /// Wrap link text in &lt;u&gt; for underline to make it look clickable.
    /// </summary>
    public class InfoPanelLinkHandler : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        private TMP_Text _text;
        private Camera _camera;

        public void Initialize(TMP_Text text)
        {
            _text = text;
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_text == null) return;

            if (_camera == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                _camera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera
                    : null;
            }

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, _camera);
            if (linkIndex < 0) return;

            var linkInfo = _text.textInfo.linkInfo[linkIndex];
            string linkId = linkInfo.GetLinkID();

            if (string.IsNullOrEmpty(linkId)) return;

            // Only open http/https URLs for safety
            if (linkId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                linkId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[InfoNpcPlayerPanel] Opening link: {linkId}");
                Application.OpenURL(linkId);
            }
            else
            {
                Debug.LogWarning($"[InfoNpcPlayerPanel] Blocked non-http link: {linkId}");
            }
        }
    }

    /// <summary>
    /// Drag handler for the player info panel.
    /// Allows the panel to be dragged around the screen.
    /// </summary>
    public class InfoPanelDragHandler : MonoBehaviour, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IBeginDragHandler
    {
        private RectTransform _panelRect;
        private Canvas _canvas;

        public void Initialize(GameObject panel)
        {
            _panelRect = panel?.GetComponent<RectTransform>();
            _canvas = panel?.GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData eventData)
        {
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_panelRect == null || _canvas == null) return;

            float scaleFactor = _canvas.scaleFactor;
            if (scaleFactor <= 0) scaleFactor = 1f;

            _panelRect.anchoredPosition += eventData.delta / scaleFactor;
        }
    }
}
