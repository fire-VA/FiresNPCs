using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI.Screens;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Wiring layer for the codegen UI. Resolves sprites, fonts, and field references
    /// on the hierarchy produced by GeneratedUI_completecompanionsui.Instantiate().
    /// </summary>
    public static class CodegenUIWiring
    {
        /// <summary>
        /// Creates the entire UI from codegen, resolves assets, wires main panel, and
        /// wires all sub-screen controllers. Returns the root GameObject.
        /// </summary>
        public static GameObject CreateAndWireUI(Transform parent)
        {
            // 1. Load the baked prefab (one Instantiate + table rebind) instead of building the
            //    full hierarchy at runtime. Falls back to runtime codegen if the baked prefab/tables
            //    aren't present in this build.
            bool baked = true;
            var root = BakedUILoader.Load("completecompanionsui", typeof(GeneratedUI_completecompanionsui), parent);
            if (root == null)
            {
                baked = false;
                root = GeneratedUI_completecompanionsui.Instantiate(parent);
            }
            if (root == null)
            {
                Debug.LogError("[CodegenUIWiring] UI build returned null (baked + codegen both failed)!");
                return null;
            }

            // 1b. Safety-net cleanup for codegen artifacts — only the runtime-codegen path needs
            //     these; the baked prefab already has them applied at bake time.
            if (!baked)
            {
                CleanupDuplicateInputFieldChildren(root);
                CleanupDuplicateDropdownChildren(root);
                ResetInputFieldState(root);
                CleanupStaleContainerChildren(root);
                CleanupDuplicateToggleCheckmarks(root);
                FixPaginationButtonBackgrounds(root);
                FixTabButtonBackgrounds(root);
            }

            // 2. Resolve sprites and fonts via the codegen's ResolveAssets
            var primaryFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Primary);
            var bodyFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Body);
            var decorativeFont = UIFontConfig.GetFontForCategory(UIFontConfig.FontCategory.Decorative);

            GeneratedUI_completecompanionsui.ResolveAssets(
                ResolveSpriteByName,
                primaryFont,
                bodyFont,
                decorativeFont);

            // 3. Find the main panel node and add the controller there
            //    NpcMainPanelController uses transform.Find("Header") etc., so it must live
            //    on FiresRPGmaker_NpcPanel where Header is a direct child.
            var panelGo = GeneratedUI_completecompanionsui.FindByTag("FiresRPGmaker_NpcPanel");
            if (panelGo == null)
            {
                Debug.LogError("[CodegenUIWiring] FiresRPGmaker_NpcPanel not found in generated hierarchy!");
                return root;
            }

            // 3b. Reset NpcPanel's anchoredPosition � codegen bakes the editor position
            //     (-1054, -51) which is meaningless at runtime. EnsurePanelVisibility() +
            //     LoadPanelPosition() will set the real position from saved config.
            var panelRect = panelGo.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.anchoredPosition = Vector2.zero;
            }

            var controller = panelGo.AddComponent<NpcMainPanelController>();
            WireMainPanel(root, controller);

            // Wire button listeners now that all references are set
            controller.RewireButtonListeners();

            // 4. Performance: disable unnecessary raycast targets on non-interactive elements.
            //    Only needed for the codegen path — the baked prefab already has this applied.
            if (!baked) DisableUnnecessaryRaycasts(root);

            // 5. Performance:
            //    draw-call batching. Changes inside the panel won't dirty the entire Hud canvas.
            AddNestedCanvas(panelGo);

            // 6. Free the _nodes array � it's only needed during Instantiate/ResolveAssets.
            GeneratedUI_completecompanionsui.FreeNodes();

            Debug.Log($"[CodegenUIWiring] UI created and wired successfully (source: {(baked ? "BAKED PREFAB" : "runtime codegen")})");
            return root;
        }

        #region Sprite Resolver

        // Cached Valheim sprites
        private static Sprite _itemBackgroundSprite;
        private static Sprite _selectionFrameSprite;
        private static Sprite _noTeleportSprite;
        private static Sprite _npcUiBkgSprite;
        private static bool _spritesCached;

        /// <summary>
        /// Sprite resolver function passed to codegen ResolveAssets.
        /// Maps "cached:spriteName" strings to actual Sprite objects.
        /// </summary>
        private static Sprite ResolveSpriteByName(string spriteName)
        {
            CacheValheimSprites();

            if (string.IsNullOrEmpty(spriteName)) return null;

            // Strip "cached:" prefix if present
            string name = spriteName.StartsWith("cached:") ? spriteName.Substring(7) : spriteName;

            switch (name.ToLowerInvariant())
            {
                case "npcuibkg": return _npcUiBkgSprite;
                case "item_background": return _itemBackgroundSprite;
                case "selection_frame": return _selectionFrameSprite;
                case "noteleport": return _noTeleportSprite;
                default:
                    // Try VAMiscAssetManager as fallback
                    return VAMiscAssetManager.GetSprite(name);
            }
        }

        /// <summary>
        /// Public accessor for the sprite resolver, usable by other codegen consumers
        /// (e.g. DialogueUI's marketplace-style templates).
        /// </summary>
        public static Sprite ResolveSpriteByNamePublic(string spriteName)
        {
            return ResolveSpriteByName(spriteName);
        }

        private static void CacheValheimSprites()
        {
            if (_spritesCached) return;

            _npcUiBkgSprite = VAMiscAssetManager.GetSprite("npcuibkg");

            if (InventoryGui.instance != null)
            {
                var playerInventory = InventoryGui.instance.m_playerGrid;
                if (playerInventory != null && playerInventory.m_elementPrefab != null)
                {
                    var elemImages = playerInventory.m_elementPrefab.GetComponentsInChildren<Image>(true);
                    foreach (var img in elemImages)
                    {
                        if (img == null || img.sprite == null) continue;
                        string n = img.sprite.name;

                        if (n.Contains("item_background") || n.Contains("item_bg"))
                            _itemBackgroundSprite = img.sprite;
                        else if (n.Contains("selection") || n.Contains("selected"))
                            _selectionFrameSprite = img.sprite;
                        else if (n.Contains("noteleport"))
                            _noTeleportSprite = img.sprite;
                    }
                }
            }

            // Skip expensive Resources.FindObjectsOfTypeAll<Sprite>() fallback.
            // Missing sprites are cosmetic-only � the UI works without them.
            // They'll be found naturally if InventoryGui is available on subsequent opens.

            _spritesCached = true;
            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[CodegenUIWiring] Sprite cache: BG={_npcUiBkgSprite != null}, " +
                      $"ItemBg={_itemBackgroundSprite != null}, Selection={_selectionFrameSprite != null}, " +
                      $"NoTeleport={_noTeleportSprite != null}");
        }

        /// <summary>Clears cached sprites (call on scene change / logout).</summary>
        public static void ClearSpriteCache()
        {
            _spritesCached = false;
            _itemBackgroundSprite = null;
            _selectionFrameSprite = null;
            _noTeleportSprite = null;
            _npcUiBkgSprite = null;
        }

        #endregion

        #region Main Panel Wiring

        /// <summary>
        /// Wires NpcMainPanelController public fields to elements in the codegen hierarchy.
        /// </summary>
        public static void WireMainPanel(GameObject root, NpcMainPanelController controller)
        {
            if (root == null || controller == null) return;

            var panel = GeneratedUI_completecompanionsui.FindByTag("FiresRPGmaker_NpcPanel");
            if (panel == null)
            {
                Debug.LogError("[CodegenUIWiring] FiresRPGmaker_NpcPanel not found!");
                return;
            }

            var pt = panel.transform;

            // Header
            controller.headerText = FindTMP(pt, "Header/TitleText");

            // Buttons
            controller.closeButton = FindButton(pt, "Header/CloseButton");
            controller.questNpcTabButton = FindButton(pt, "TabsBar/QuestNpcTab");
            controller.infoNpcTabButton = FindButton(pt, "TabsBar/InfoNpcTab");
            controller.dialogueNpcTabButton = FindButton(pt, "TabsBar/DialogueNpcTab");
            controller.traderTabButton = FindButton(pt, "TabsBar/TraderTab");
            controller.sizeButton = FindButton(pt, "TabsBar/SizeButton");
            controller.sizeText = controller.sizeButton?.GetComponentInChildren<TMP_Text>();

            // Content area
            controller.contentParent = FindChild(pt, "TabContent/Content");

            // Detail panel
            var detailGo = FindChild(pt, "TabContent/Content/DetailPanel")?.gameObject
                        ?? FindChild(pt, "TabContent/DetailPanel")?.gameObject;
            controller.detailPanel = detailGo;

            if (detailGo != null)
            {
                controller.nameOverrideInput = FindInputField(detailGo.transform, "Container_NameOverrideInput");
                controller.profileInput = FindInputField(detailGo.transform, "Container_ProfileInput");
                controller.modelOverrideInput = FindInputField(detailGo.transform, "Container_ModelOverrideInput");
                controller.applyButton = FindButton(detailGo.transform, "ApplyButton");
            }

            // Pagination
            controller.prevPageButton = FindButton(pt, "Header/PageControls/PrevButton");
            controller.nextPageButton = FindButton(pt, "Header/PageControls/NextButton");
            controller.pageText = FindTMP(pt, "Header/PageControls/PageText");
            controller.backButton = controller.prevPageButton;


            if (controller.pageText != null)
            {
                controller.pageText.raycastTarget = true;
            }

            // Reactivate the CloseButton label � codegen hides it via SetActive(false)
            if (controller.closeButton != null)
            {
                for (int i = 0; i < controller.closeButton.transform.childCount; i++)
                {
                    var child = controller.closeButton.transform.GetChild(i);
                    if (!child.gameObject.activeSelf)
                        child.gameObject.SetActive(true);
                }
            }

            // Reactivate the ApplyButton label � codegen hides it via SetActive(false)
            if (controller.applyButton != null)
            {
                for (int i = 0; i < controller.applyButton.transform.childCount; i++)
                {
                    var child = controller.applyButton.transform.GetChild(i);
                    if (!child.gameObject.activeSelf)
                        child.gameObject.SetActive(true);
                }
            }

            // Search
            controller.searchInput = FindInputField(pt, "TabsBar/SearchBar");

            // Entry prefabs � still created from factory
            controller.dialogueEntryPrefab = UIPrefabFactory.GetDialogueEntryPrefab();
            controller.questEntryPrefab = UIPrefabFactory.GetQuestEntryPrefab();

            // Log wiring results
            Debug.Log($"[CodegenUIWiring] Main panel wired � " +
                      $"Header={controller.headerText != null}, Close={controller.closeButton != null}, " +
                      $"Content={controller.contentParent != null}, Detail={controller.detailPanel != null}, " +
                      $"NameInput={controller.nameOverrideInput != null}, Apply={controller.applyButton != null}, " +
                      $"Tabs: Q={controller.questNpcTabButton != null} I={controller.infoNpcTabButton != null} " +
                      $"D={controller.dialogueNpcTabButton != null} T={controller.traderTabButton != null}");
        }

        #endregion

        #region Sub-Screen Wiring

        /// <summary>
        /// Wires the InfoNpcScreenController to its pre-built codegen elements.
        /// </summary>
        public static InfoNpcScreenController WireInfoScreen(Transform panelRoot)
        {
            var screenGo = GeneratedUI_completecompanionsui.FindByTag("InfoNpcScreen");
            if (screenGo == null)
            {
                Debug.LogWarning("[CodegenUIWiring] InfoNpcScreen not found in codegen hierarchy");
                return null;
            }

            var controller = screenGo.AddComponent<InfoNpcScreenController>();

            // Find the apply button (sibling of InfoNpcScreen under the panel root)
            var applyGo = GeneratedUI_completecompanionsui.FindByTag("InfoApplyButton");
            Button applyButton = applyGo != null ? applyGo.GetComponent<Button>() : null;

            controller.WireFromCodegen(screenGo, applyButton);
            ClearSecondTabRow(screenGo);

            Debug.Log($"[CodegenUIWiring] InfoNpcScreen wired via WireFromCodegen");
            return controller;
        }

        /// <summary>The codegen layout for Quest / Info / DialogueEditor screens was baked when the
        /// panel only had ONE tab row. The Marketplace family added a second row of tabs (Market /
        /// Mail / Transmog) underneath, and the baked content rects now overlap that second row.
        /// Push each screen's top inset DOWN by ~60px so its content lands below BOTH tab rows.</summary>
        private static void ClearSecondTabRow(GameObject screenGo)
        {
            if (screenGo == null) return;
            var rt = screenGo.GetComponent<RectTransform>();
            if (rt == null) return;
            // Make the top inset at least 190px from the panel top (was -130 = 130px on baked rects).
            // Negative offsetMax.y INCREASES the inset, so we take the more-negative of the two values.
            var offMax = rt.offsetMax;
            offMax.y = Mathf.Min(offMax.y, -190f);
            rt.offsetMax = offMax;
        }

        /// <summary>
        /// Wires the QuestInteractionScreenController to its pre-built codegen elements.
        /// WireFromCodegen fixes the layout to match the programmatic version.
        /// </summary>
        public static QuestInteractionScreenController WireQuestInteractionScreen(Transform panelRoot)
        {
            var screenContainerGo = GeneratedUI_completecompanionsui.FindByTag("QuestScreenContainer");
            if (screenContainerGo == null)
            {
                Debug.LogWarning("[CodegenUIWiring] QuestScreenContainer not found in codegen hierarchy");
                return null;
            }

            // If a programmatic Create() already ran, destroy it � codegen is the source of truth
            var existingController = QuestInteractionScreenController.Instance;
            if (existingController != null && existingController.gameObject != screenContainerGo)
            {
                Debug.Log("[CodegenUIWiring] Destroying pre-existing programmatic QuestScreenContainer");
                Object.DestroyImmediate(existingController.gameObject);
                QuestInteractionScreenController.Instance = null;
            }

            // Skip if already wired
            var existing = screenContainerGo.GetComponent<QuestInteractionScreenController>();
            if (existing != null)
            {
                Debug.Log("[CodegenUIWiring] QuestInteractionScreen already wired");
                return existing;
            }

            var controller = screenContainerGo.AddComponent<QuestInteractionScreenController>();
            controller.screenRoot = screenContainerGo;
            controller.WireFromCodegen();

            screenContainerGo.SetActive(false);

            Debug.Log("[CodegenUIWiring] QuestInteractionScreen wired via WireFromCodegen");
            return controller;
        }

        /// <summary>
        /// Wires the DialogueEditorScreenController to its pre-built codegen elements.
        /// </summary>
        public static DialogueEditorScreenController WireDialogueEditorScreen(Transform panelRoot)
        {
            var screenGo = GeneratedUI_completecompanionsui.FindByTag("DialogueEditorScreen");
            if (screenGo == null)
            {
                Debug.LogWarning("[CodegenUIWiring] DialogueEditorScreen not found in codegen hierarchy");
                return null;
            }

            var controller = screenGo.AddComponent<DialogueEditorScreenController>();
            controller.WireFromCodegen(screenGo);
            ClearSecondTabRow(screenGo);

            Debug.Log("[CodegenUIWiring] DialogueEditorScreen wired via WireFromCodegen");
            return controller;
        }

        public static QuestConfigScreenController WireQuestConfigScreen(Transform panelRoot)
        {
            var screenGo = GeneratedUI_completecompanionsui.FindByTag("QuestConfigScreen");
            if (screenGo == null)
            {
                Debug.LogWarning("[CodegenUIWiring] QuestConfigScreen not found in codegen hierarchy");
                return null;
            }

            // Find the apply button (sibling tagged "QuestApplyButton")
            var applyGo = GeneratedUI_completecompanionsui.FindByTag("QuestApplyButton");
            Button applyButton = applyGo != null ? applyGo.GetComponent<Button>() : null;

            var controller = screenGo.AddComponent<QuestConfigScreenController>();
            controller.WireFromCodegen(screenGo, applyButton);
            ClearSecondTabRow(screenGo);

            Debug.Log("[CodegenUIWiring] QuestConfigScreen wired via WireFromCodegen");
            return controller;
        }

        public static DressingRoomScreenController WireDressingRoomScreen(Transform panelRoot)
        {
            var screenGo = GeneratedUI_completecompanionsui.FindByTag("DressingRoomScreen");
            if (screenGo == null)
            {
                Debug.LogWarning("[CodegenUIWiring] DressingRoomScreen not found in codegen hierarchy");
                return null;
            }

            var controller = screenGo.AddComponent<DressingRoomScreenController>();
            controller.WireFromCodegen(screenGo);

            Debug.Log("[CodegenUIWiring] DressingRoomScreen wired via WireFromCodegen");
            return controller;
        }

        public static TraderConfigScreenController WireTraderConfigScreen(Transform panelRoot)
        {
            // No baked "TraderConfigScreen" exists yet — create the screen container + apply button
            // programmatically (named so PanelElementManager.SetElementActive can toggle them). The
            // rects mirror the baked QuestConfigScreen / QuestApplyButton so the trader editor lands in
            // the same content area (below the tabs). When a baked trader screen is added to the prefab,
            // switch this to FindByTag like the others.
            var screenGo = new GameObject("TraderConfigScreen", typeof(RectTransform));
            screenGo.transform.SetParent(panelRoot, false);
            var srt = screenGo.GetComponent<RectTransform>();
            var questScreen = GeneratedUI_completecompanionsui.FindByTag("QuestConfigScreen");
            CopyRect(questScreen != null ? questScreen.GetComponent<RectTransform>() : null, srt,
                     fallbackMin: Vector2.zero, fallbackMax: Vector2.one,
                     fallbackOffMin: new Vector2(20f, 70f), fallbackOffMax: new Vector2(-20f, -190f));

            var applyGo = new GameObject("TraderApplyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            applyGo.transform.SetParent(panelRoot, false);
            var art = applyGo.GetComponent<RectTransform>();
            var questApply = GeneratedUI_completecompanionsui.FindByTag("QuestApplyButton");
            CopyRect(questApply != null ? questApply.GetComponent<RectTransform>() : null, art,
                     fallbackMin: new Vector2(0.3f, 0.02f), fallbackMax: new Vector2(0.7f, 0.08f),
                     fallbackOffMin: Vector2.zero, fallbackOffMax: Vector2.zero);
            applyGo.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.22f, 0.95f);
            var applyButton = applyGo.GetComponent<Button>();
            var applyLabel = new GameObject("Text", typeof(RectTransform));
            applyLabel.transform.SetParent(art, false);
            var alrt = applyLabel.GetComponent<RectTransform>(); alrt.anchorMin = Vector2.zero; alrt.anchorMax = Vector2.one; alrt.offsetMin = Vector2.zero; alrt.offsetMax = Vector2.zero;
            var alt = applyLabel.AddComponent<TextMeshProUGUI>(); alt.font = TMP_Settings.defaultFontAsset; alt.text = "Apply"; alt.fontSize = 18f; alt.alignment = TextAlignmentOptions.Center; alt.color = new Color(0.96f, 0.91f, 0.76f);

            var controller = screenGo.AddComponent<TraderConfigScreenController>();
            controller.BuildForm(applyButton);
            screenGo.SetActive(false);
            applyGo.SetActive(false);

            Debug.Log("[CodegenUIWiring] TraderConfigScreen created programmatically (mirrors QuestConfigScreen rect)");
            return controller;
        }

        public static BankerConfigScreenController WireBankerConfigScreen(Transform panelRoot)
        {
            // Banker screen + Apply button created programmatically (no baked banker screen yet). The screen
            // rect is explicit (NOT copied from QuestConfigScreen) — QuestConfigScreen gets shrunk by
            // ClearSecondTabRow to clear the 2-row tab bar, and the resulting short rect makes percent-anchored
            // content land in the lower half of the panel. We anchor the rect from the bottom-of-tabs (160 below
            // panel top) to just above the Apply button (95 above panel bottom).
            var screenGo = new GameObject("BankerConfigScreen", typeof(RectTransform));
            screenGo.transform.SetParent(panelRoot, false);
            var srt = screenGo.GetComponent<RectTransform>();
            // Side insets pull the rect away from the parchment border (the painted ornament occupies
            // ~50px on each side); SIDE_PAD inside the controller adds another 32 so the form text sits
            // well inside the content area, not running into the celtic knot pattern.
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(50f, 95f);
            srt.offsetMax = new Vector2(-50f, -160f);

            var applyGo = new GameObject("BankerApplyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            applyGo.transform.SetParent(panelRoot, false);
            var art = applyGo.GetComponent<RectTransform>();
            var questApply = GeneratedUI_completecompanionsui.FindByTag("QuestApplyButton");
            CopyRect(questApply != null ? questApply.GetComponent<RectTransform>() : null, art,
                     fallbackMin: new Vector2(0.3f, 0.02f), fallbackMax: new Vector2(0.7f, 0.08f),
                     fallbackOffMin: Vector2.zero, fallbackOffMax: Vector2.zero);
            applyGo.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.22f, 0.95f);
            var applyButton = applyGo.GetComponent<Button>();
            var applyLabel = new GameObject("Text", typeof(RectTransform));
            applyLabel.transform.SetParent(art, false);
            var alrt = applyLabel.GetComponent<RectTransform>(); alrt.anchorMin = Vector2.zero; alrt.anchorMax = Vector2.one; alrt.offsetMin = Vector2.zero; alrt.offsetMax = Vector2.zero;
            var alt = applyLabel.AddComponent<TextMeshProUGUI>(); alt.font = TMP_Settings.defaultFontAsset; alt.text = "Apply"; alt.fontSize = 18f; alt.alignment = TextAlignmentOptions.Center; alt.color = new Color(0.96f, 0.91f, 0.76f);

            var controller = screenGo.AddComponent<BankerConfigScreenController>();
            controller.BuildForm(applyButton);
            screenGo.SetActive(false);
            applyGo.SetActive(false);

            Debug.Log("[CodegenUIWiring] BankerConfigScreen created programmatically");
            return controller;
        }

        /// <summary>
        /// Wires a v1-stub admin-config screen for any of the Marketplace modules (Gambler, Marketplace,
        /// Achievement, Leaderboard, Mail, Transmog). Mirrors the Banker wiring but routes through
        /// GenericModuleConfigScreenController so we don't need a controller-per-module.
        /// </summary>
        public static GenericModuleConfigScreenController WireGenericModuleConfigScreen(
            Transform panelRoot, FiresCore.Npc.NpcType type, string fieldName, string label,
            string screenName, string applyName)
        {
            // Explicit rect (NOT copied from QuestConfigScreen) — same reasoning as WireBankerConfigScreen.
            var screenGo = new GameObject(screenName, typeof(RectTransform));
            screenGo.transform.SetParent(panelRoot, false);
            var srt = screenGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(50f, 95f);
            srt.offsetMax = new Vector2(-50f, -160f);

            var applyGo = new GameObject(applyName, typeof(RectTransform), typeof(Image), typeof(Button));
            applyGo.transform.SetParent(panelRoot, false);
            var art = applyGo.GetComponent<RectTransform>();
            var questApply = GeneratedUI_completecompanionsui.FindByTag("QuestApplyButton");
            CopyRect(questApply != null ? questApply.GetComponent<RectTransform>() : null, art,
                     fallbackMin: new Vector2(0.3f, 0.02f), fallbackMax: new Vector2(0.7f, 0.08f),
                     fallbackOffMin: Vector2.zero, fallbackOffMax: Vector2.zero);
            applyGo.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.22f, 0.95f);
            var applyButton = applyGo.GetComponent<Button>();
            var applyLabel = new GameObject("Text", typeof(RectTransform));
            applyLabel.transform.SetParent(art, false);
            var alrt = applyLabel.GetComponent<RectTransform>(); alrt.anchorMin = Vector2.zero; alrt.anchorMax = Vector2.one; alrt.offsetMin = Vector2.zero; alrt.offsetMax = Vector2.zero;
            var alt = applyLabel.AddComponent<TextMeshProUGUI>(); alt.font = TMP_Settings.defaultFontAsset; alt.text = "Apply"; alt.fontSize = 18f; alt.alignment = TextAlignmentOptions.Center; alt.color = new Color(0.96f, 0.91f, 0.76f);

            var controller = screenGo.AddComponent<GenericModuleConfigScreenController>();
            controller.Initialize(type, fieldName, label);
            controller.BuildForm(applyButton);
            screenGo.SetActive(false);
            applyGo.SetActive(false);

            Debug.Log("[CodegenUIWiring] " + screenName + " created programmatically (generic stub)");
            return controller;
        }

        /// <summary>Copies a source RectTransform's anchors/pivot/offsets onto dst; uses the fallbacks when src is null.</summary>
        private static void CopyRect(RectTransform src, RectTransform dst,
                                     Vector2 fallbackMin, Vector2 fallbackMax, Vector2 fallbackOffMin, Vector2 fallbackOffMax)
        {
            if (src != null)
            {
                dst.anchorMin = src.anchorMin; dst.anchorMax = src.anchorMax; dst.pivot = src.pivot;
                dst.offsetMin = src.offsetMin; dst.offsetMax = src.offsetMax;
            }
            else
            {
                dst.anchorMin = fallbackMin; dst.anchorMax = fallbackMax;
                dst.offsetMin = fallbackOffMin; dst.offsetMax = fallbackOffMax;
            }
        }

        #endregion

        #region Helpers

        /// <summary>Finds a child transform by path, searching recursively if direct path fails.</summary>
        public static Transform FindChild(Transform parent, string path)
        {
            if (parent == null) return null;

            var result = parent.Find(path);
            if (result != null) return result;

            string lastName = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
            return FindDeep(parent, lastName);
        }

        /// <summary>Recursive depth-first search by name.</summary>
        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;

                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public static TMP_Text FindTMP(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponent<TMP_Text>() ?? t?.GetComponentInChildren<TMP_Text>(true);
        }

        public static Button FindButton(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponent<Button>() ?? t?.GetComponentInChildren<Button>(true);
        }

        public static TMP_InputField FindInputField(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            if (t == null) return null;

            // Prefer active InputFields � the codegen creates both an inactive
            // legacy input (e.g. "NameOverrideInput") and the real transparent
            // one ("Transparent_NameOverrideInput"). GetComponentInChildren(true)
            // finds the inactive one first, which breaks all input.
            var active = t.GetComponentInChildren<TMP_InputField>(false);
            if (active != null) return active;

            // Fallback to include inactive (for fields not yet activated)
            return t.GetComponentInChildren<TMP_InputField>(true);
        }

        public static TMP_Dropdown FindDropdown(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<TMP_Dropdown>(true);
        }

        public static Toggle FindToggle(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<Toggle>(true);
        }

        public static ScrollRect FindScrollRect(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<ScrollRect>(true);
        }

        #endregion

        #region Codegen Cleanup

        /// <summary>
        /// Removes duplicate Viewport children from InputFields.
        /// The codegen creates the InputField's internal Viewport/Text/Placeholder,
        /// then also emits them again as tracked layout nodes. The duplicates sit on
        /// top of the real ones, block interaction, and have stale baked text.
        /// We keep the Viewport that the TMP_InputField.textViewport points to and
        /// destroy any other Viewport children (plus their Caret/Text/Placeholder).
        /// </summary>
        private static void CleanupDuplicateInputFieldChildren(GameObject root)
        {
            int removed = 0;
            var inputFields = root.GetComponentsInChildren<TMP_InputField>(true);

            foreach (var input in inputFields)
            {
                if (input == null || input.textViewport == null) continue;

                var wiredViewport = input.textViewport.gameObject;
                var parent = input.transform;
                var toDestroy = new List<GameObject>();

                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var child = parent.GetChild(i);
                    // Destroy any Viewport that ISN'T the one wired into the InputField
                    if (child.name == "Viewport" && child.gameObject != wiredViewport)
                    {
                        toDestroy.Add(child.gameObject);
                    }
                    // Also destroy stray Caret objects that the codegen created
                    // (TMP_InputField creates its own caret at runtime)
                    if (child.name == "Caret" && child.parent == parent)
                    {
                        toDestroy.Add(child.gameObject);
                    }
                }

                foreach (var go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[CodegenUIWiring] Removed {removed} duplicate InputField children");
        }

        /// <summary>
        /// Removes duplicate Label/Arrow/Template children from Dropdowns.
        /// Same issue as InputFields � codegen creates internal children for the
        /// TMP_Dropdown, then also emits them from the layout as separate nodes.
        /// </summary>
        private static void CleanupDuplicateDropdownChildren(GameObject root)
        {
            int removed = 0;
            var dropdowns = root.GetComponentsInChildren<TMP_Dropdown>(true);

            foreach (var dropdown in dropdowns)
            {
                if (dropdown == null) continue;

                var parent = dropdown.transform;
                var toDestroy = new List<GameObject>();

                // Collect the set of GameObjects that are wired into the dropdown.
                // captionText lives on the internal "Label" GO; template is the internal "Template" GO.
                var wiredSet = new HashSet<GameObject>();
                if (dropdown.captionText != null)
                    wiredSet.Add(dropdown.captionText.gameObject);
                if (dropdown.template != null)
                    wiredSet.Add(dropdown.template.gameObject);

                // The dropdown's internal Arrow is created right after the Label.
                // It has a TextMeshProUGUI with text "v" (or similar).
                // Find the first Arrow child � that's the wired one.
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child.name == "Arrow" && child.GetComponent<TextMeshProUGUI>() != null)
                    {
                        wiredSet.Add(child.gameObject);
                        break;
                    }
                }

                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var child = parent.GetChild(i);
                    bool isDuplicate = false;

                    if (child.name == "Label" && !wiredSet.Contains(child.gameObject))
                        isDuplicate = true;
                    if (child.name == "Arrow" && !wiredSet.Contains(child.gameObject))
                        isDuplicate = true;
                    if (child.name == "Template" && !wiredSet.Contains(child.gameObject))
                        isDuplicate = true;

                    if (isDuplicate)
                    {
                        toDestroy.Add(child.gameObject);
                    }
                }

                foreach (var go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[CodegenUIWiring] Removed {removed} duplicate Dropdown children");
        }

        /// <summary>
        /// Resets all InputField state after codegen instantiation.
        /// The codegen bakes whatever text was in the fields at export time
        /// (e.g. "StaticNpc") and may disable placeholder components.
        /// Clear all text and ensure placeholders are enabled so they
        /// show up properly when fields are empty.
        /// </summary>
        private static void ResetInputFieldState(GameObject root)
        {
            var inputFields = root.GetComponentsInChildren<TMP_InputField>(true);
            int reset = 0;

            foreach (var input in inputFields)
            {
                if (input == null) continue;

                // Clear baked text
                input.text = "";
                if (input.textComponent != null)
                    input.textComponent.text = "";

                // Ensure placeholder is enabled and visible
                if (input.placeholder != null)
                {
                    var phGo = input.placeholder.gameObject;
                    if (!phGo.activeSelf) phGo.SetActive(true);

                    var phText = input.placeholder as TMP_Text;
                    if (phText != null && !phText.enabled)
                        phText.enabled = true;
                }

                reset++;
            }

            Debug.Log($"[CodegenUIWiring] Reset {reset} InputField states (cleared text, enabled placeholders)");
        }

        /// <summary>
        /// Removes stale codegen children from input containers.
        /// Each Container_*Input has:
        ///   - An inactive old InputField (e.g. "NameOverrideInput") � obsolete, blocks FindInputField
        ///   - A stray "Placeholder (1)" TextMeshProUGUI with baked editor text ("BOB SAGGAT")
        ///   - A stray "Caret" Image
        /// The active "Transparent_*" InputField is the real one and must be kept.
        /// </summary>
        private static void CleanupStaleContainerChildren(GameObject root)
        {
            int removed = 0;
            string[] containerNames = {
                "Container_NameOverrideInput",
                "Container_ProfileInput",
                "Container_ModelOverrideInput"
            };

            foreach (var containerName in containerNames)
            {
                var container = FindDeep(root.transform, containerName);
                if (container == null) continue;

                var toDestroy = new List<GameObject>();
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i);

                    // Remove inactive old InputField GOs (e.g. "NameOverrideInput")
                    var inputField = child.GetComponent<TMP_InputField>();
                    if (inputField != null && !child.gameObject.activeSelf)
                    {
                        toDestroy.Add(child.gameObject);
                        continue;
                    }

                    // Remove stray Placeholder nodes ("Placeholder (1)" etc.)
                    if (child.name.StartsWith("Placeholder"))
                    {
                        toDestroy.Add(child.gameObject);
                        continue;
                    }

                    // Remove stray Caret nodes that aren't inside an InputField
                    if (child.name == "Caret" && child.GetComponent<TMP_InputField>() == null)
                    {
                        toDestroy.Add(child.gameObject);
                        continue;
                    }
                }

                foreach (var go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[CodegenUIWiring] Removed {removed} stale container children (old inputs, placeholders, carets)");
        }

        /// <summary>
        /// Removes duplicate Checkmark children from Toggle components.
        /// The codegen creates the Toggle's internal tglCheckGo (a TextMeshProUGUI with "?"
        /// wired to toggle.graphic so Unity shows/hides it based on isOn). It also emits a
        /// second "Checkmark" node as a tracked layout child � this duplicate is always active
        /// and makes the checkbox look permanently checked. We find Toggles and destroy any
        /// Checkmark children that are NOT the one referenced by toggle.graphic.
        /// </summary>
        private static void CleanupDuplicateToggleCheckmarks(GameObject root)
        {
            int removed = 0;
            foreach (var toggle in root.GetComponentsInChildren<Toggle>(true))
            {
                var toggleT = toggle.transform;
                var graphicGO = toggle.graphic != null ? toggle.graphic.gameObject : null;

                var toDestroy = new List<GameObject>();
                for (int i = toggleT.childCount - 1; i >= 0; i--)
                {
                    var child = toggleT.GetChild(i);
                    if (child.name != "Checkmark") continue;

                    // Keep the one wired to toggle.graphic
                    if (child.gameObject == graphicGO) continue;

                    // Also keep a Panel-type Checkmark that has an Image and is SetActive(false)
                    // � that's the proper image-based checkmark some toggles use.
                    var img = child.GetComponent<Image>();
                    if (img != null && !child.gameObject.activeSelf) continue;

                    // This is the duplicate always-visible text Checkmark � destroy it
                    toDestroy.Add(child.gameObject);
                }

                foreach (var go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    removed++;
                }
            }

            if (removed > 0)
                Debug.Log($"[CodegenUIWiring] Removed {removed} duplicate Toggle Checkmark children");
        }

        /// <summary>
        /// Disables raycastTarget on Image and TMP_Text components that don't need it.
        /// The codegen sets raycastTarget=true on most elements, but only interactive
        /// components (Buttons, InputFields, ScrollRects, Dropdowns, Toggles, Scrollbars)
        /// actually need raycasting. Disabling it on decorative/layout elements removes
        /// hundreds of unnecessary hit-test candidates per frame.
        /// </summary>
        private static void DisableUnnecessaryRaycasts(GameObject root)
        {
            // Collect all interactive GameObjects into a HashSet for fast lookup
            var interactiveGOs = new HashSet<GameObject>();

            foreach (var btn in root.GetComponentsInChildren<Button>(true))
                interactiveGOs.Add(btn.gameObject);
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
                interactiveGOs.Add(input.gameObject);
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(true))
                interactiveGOs.Add(scroll.gameObject);
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                interactiveGOs.Add(dropdown.gameObject);
            foreach (var toggle in root.GetComponentsInChildren<Toggle>(true))
                interactiveGOs.Add(toggle.gameObject);
            foreach (var scrollbar in root.GetComponentsInChildren<Scrollbar>(true))
                interactiveGOs.Add(scrollbar.gameObject);

            // Also keep raycasts on the NpcPanel root (for drag handling)
            var panelGo = GeneratedUI_completecompanionsui.FindByTag("FiresRPGmaker_NpcPanel");
            if (panelGo != null) interactiveGOs.Add(panelGo);

            // Keep raycasts on the Header and PageControls/PageText for drag handling.
            // PanelDragHandler is added later (in NpcMainPanelController.Initialize),
            // but we need the Images/TMP to keep raycastTarget=true so dragging works.
            var headerGo = GeneratedUI_completecompanionsui.FindByTag("Header");
            if (headerGo != null) interactiveGOs.Add(headerGo);
            var pageTextT = panelGo?.transform.Find("Header/PageControls/PageText");
            if (pageTextT != null) interactiveGOs.Add(pageTextT.gameObject);

            int disabledImages = 0;
            int disabledTexts = 0;

            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.raycastTarget && !interactiveGOs.Contains(img.gameObject))
                {
                    img.raycastTarget = false;
                    disabledImages++;
                }
            }

            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp.raycastTarget && !interactiveGOs.Contains(tmp.gameObject))
                {
                    tmp.raycastTarget = false;
                    disabledTexts++;
                }
            }

            Debug.Log($"[CodegenUIWiring] Disabled raycastTarget on {disabledImages} Images and {disabledTexts} TMP_Text components");
        }

        /// <summary>
        /// Adds a nested Canvas + GraphicRaycaster on the NPC panel so it gets its own
        /// draw-call batch. When any child element changes (text, color, visibility),
        /// only this sub-canvas is rebuilt instead of the entire Hud canvas with all
        /// its vanilla UI elements.
        /// </summary>
        private static void AddNestedCanvas(GameObject panelGo)
        {
            if (panelGo == null) return;
            if (panelGo.GetComponent<Canvas>() != null) return; // already has one

            var canvas = panelGo.AddComponent<Canvas>();
            // No need to set renderMode � nested canvases inherit from parent
            canvas.overrideSorting = false;

            panelGo.AddComponent<GraphicRaycaster>();

            Debug.Log("[CodegenUIWiring] Added nested Canvas for batching isolation on NPC panel");
        }

        /// <summary>
        /// Makes tab button Images fully transparent and removes codegen-baked _Highlight children.
        /// The codegen bakes color = white (1,1,1,1) on the tab button Images with no sprite,
        /// creating solid grey rectangles behind the text. The NpcMainPanelController creates
        /// its own runtime highlights, so the codegen ones are duplicates.
        /// </summary>
        private static void FixTabButtonBackgrounds(GameObject root)
        {
            string[] tabNames = { "QuestNpcTab", "InfoNpcTab", "DialogueNpcTab", "TraderTab" };
            int fixed_count = 0;
            foreach (var name in tabNames)
            {
                var tab = FindDeep(root.transform, name);
                if (tab == null) continue;

                // Make the tab button Image fully transparent
                var img = tab.GetComponent<Image>();
                if (img != null)
                    img.color = new Color(1f, 1f, 1f, 0f);

                // Also fix the ColorBlock so all states are transparent
                var btn = tab.GetComponent<Button>();
                if (btn != null)
                {
                    var colors = btn.colors;
                    colors.normalColor = new Color(1f, 1f, 1f, 0f);
                    colors.highlightedColor = new Color(1f, 1f, 1f, 0f);
                    colors.pressedColor = new Color(1f, 1f, 1f, 0f);
                    colors.selectedColor = new Color(1f, 1f, 1f, 0f);
                    colors.disabledColor = new Color(1f, 1f, 1f, 0f);
                    btn.colors = colors;
                }

                // Destroy codegen-baked _Highlight children � the runtime CreateTabHighlight
                // method creates its own, so these are duplicates with potentially wrong offsets
                var toDestroy = new List<GameObject>();
                for (int i = tab.childCount - 1; i >= 0; i--)
                {
                    var child = tab.GetChild(i);
                    if (child.name.EndsWith("_Highlight"))
                        toDestroy.Add(child.gameObject);
                }
                foreach (var go in toDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    fixed_count++;
                }
            }
            if (fixed_count > 0)
                Debug.Log($"[CodegenUIWiring] Fixed {fixed_count} tab button backgrounds/highlights");
        }

        /// <summary>
        /// Makes PrevButton/NextButton Image backgrounds fully transparent.
        /// The codegen bakes color = white (1,1,1,1) on the button Image. The normal
        /// ColorBlock tints it to (1,1,1,0) when enabled, but the disabled tint
        /// (0.78, 0.78, 0.78, 0.5) makes the white base visible as a grey rectangle.
        /// Setting the Image color to transparent fixes both states.
        /// </summary>
        private static void FixPaginationButtonBackgrounds(GameObject root)
        {
            string[] buttonNames = { "PrevButton", "NextButton" };
            foreach (var name in buttonNames)
            {
                var btn = FindDeep(root.transform, name);
                if (btn == null) continue;

                var img = btn.GetComponent<Image>();
                if (img != null)
                    img.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        #endregion
    }
}
