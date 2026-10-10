using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.UI.QuestJournal;
using ActiveQuestData = FiresRPGmaker.Modules.Vault.ActiveQuestData;

namespace FiresRPGmaker.UI.Books
{
    /// <summary>
    /// Quest journal driven by the baked FiresQuestJournalPanel prefab (firesnpcsui bundle) over the live animated
    /// book. The baked BookBg image is swapped for the FiresBookView RenderTexture; the leather LEFT page holds the
    /// table-of-contents list, the parchment RIGHT page the selected quest's details + preview. Data comes from
    /// VaultOfKnowledge / QuestManager / QuestProgressTracker (same as the old journal).
    /// </summary>
    public static class QuestBookJournal
    {
        private static GameObject _canvas, _panel;
        private static Camera _uiCam;
        private static int _uiLayer = 5;
        private static bool _built;
        private static RawImage _bookRaw;
        private static TMP_Text _tocHeader, _questName, _descText, _previewPlaceholder, _tocToggleLabel;
        private static RawImage _previewImage;
        private static RectTransform _tocContent, _descContent;
        private static ScrollRect _descScroll;
        private const float DescBottomInset = 40f; // raise the desc scroll off the page's inner bottom edge
        // Negative = extend the scroll ABOVE the baked anchor: the anchor top sat a wide band below the
        // DETAILS heading, so the text started far from it. -80 ≈ 5 extra lines of viewport, text starts
        // right under the heading (the in-text top padding still guards the angled-page cutoff).
        private const float DescTopInset = -80f;
        private static GameObject _rowTemplate;
        private static GameObject _contentRoot;
        private static TMP_FontAsset _averia;

        // The book is static from ~3.5s at 1x; 5s gives margin. Both the animation speed and this delay scale
        // together via the QuestBookOpenSpeed config so the content always reveals on the settled pose.
        private const float BaseRevealDelay = 5.0f;
        internal static float OpenSpeed
        {
            get { float s = 1f; try { s = Managers.ConfigManager.Instance?.configQuestBookOpenSpeed?.Value ?? 1f; } catch { } return Mathf.Clamp(s, 0.1f, 10f); }
        }
        internal static float RevealDelay => BaseRevealDelay / OpenSpeed;

        private static bool _showCompleted;
        private static string _selected;
        private static readonly List<GameObject> _rows = new List<GameObject>();

        // NPC quest-selection mode: same book, ToC = the NPC's quest profile, the toggle button becomes "Accept Quest".
        private static bool _npcMode;
        private static string _npcName;
        private static List<QuestDefinition> _npcQuests;

        public static bool IsOpen => _built && _canvas != null && _canvas.activeSelf;
        public static void Toggle() { if (IsOpen) Close(); else Open(); }

        public static void Open()
        {
            _npcMode = false; _npcName = null; _npcQuests = null;
            OpenInternal();
        }

        /// <summary>Open the book as the quest-selection screen for a quest NPC. Returns false if it can't (caller falls back).</summary>
        public static bool OpenForNpc(NpcController npc)
        {
            if (npc == null) return false;
            var effProfile = npc.GetEffectiveQuestProfile();
            if (string.IsNullOrEmpty(effProfile)) return false;
            var quests = new List<QuestDefinition>();
            try
            {
                QuestProfileManager.Initialize();
                var qm = QuestManager.Instance; if (qm == null) return false;
                var profile = QuestProfileManager.GetProfile(effProfile);
                List<string> ids = profile != null && profile.QuestIds != null && profile.QuestIds.Count > 0
                    ? profile.QuestIds
                    : (qm.GetQuestsForProfile(effProfile) ?? new List<string>());
                foreach (var qid in ids) { var q = qm.GetQuestDefinition(qid); if (q != null) quests.Add(q); }

                // kg HideOtherQuestRequirementQuests (MarketPlace.cfg, default false): while a quest
                // still carries an unmet finished-quest requirement, keep it out of the offered list
                // entirely instead of showing it greyed with the missing-requirement message.
                if (global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.HideOtherQuestRequirementQuests)
                {
                    long pid = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
                    quests.RemoveAll(q =>
                    {
                        try
                        {
                            if (q.Requirements == null) return false;
                            bool hasQuestReq = q.Requirements.Any(r =>
                                r != null && (r.IndexOf("QuestFinished", StringComparison.OrdinalIgnoreCase) >= 0
                                           || r.IndexOf("OtherQuest", StringComparison.OrdinalIgnoreCase) >= 0));
                            if (!hasQuestReq) return false;
                            return !qm.CheckRequirements(pid, q, out _);
                        }
                        catch { return false; }
                    });
                }
            }
            catch (Exception ex) { Debug.LogWarning("[QuestBookJournal] npc quests: " + ex.Message); return false; }
            if (quests.Count == 0)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has no quests available.");
                return true;   // handled — nothing to show, no fallback screen either
            }

            _npcMode = true; _npcName = npc.GetDisplayName(); _npcQuests = quests;
            OpenInternal();
            return IsOpen;
        }

        private static void OpenInternal()
        {
            EnsureBuilt();
            if (_canvas == null) return;
            var view = FiresBookView.Ensure();
            view.Pitch = 62f; view.Yaw = 0f; view.DistanceMul = 1.12f; view.HeightMul = 0.0f;   // match the baked book framing
            view.OpenSpeed = OpenSpeed;
            view.Show("FiresBook_Opening");
            if (_bookRaw != null) _bookRaw.texture = view.Texture;
            _canvas.SetActive(true);
            if (_uiCam != null) _uiCam.enabled = true;
            _showCompleted = false; _selected = null;
            PopulateToc();
            ShowDetails(null);
            NpcHoverHudSuppressor.SuppressAll(true);   // NPC/monster nameplates bleed through the 3D book render
            FiresCore.Bridge.ModUiRegistry.Register("FiresQuestBookJournal", () => IsOpen, Close, () => _canvas);
        }

        public static void Close()
        {
            if (_canvas != null) _canvas.SetActive(false);
            if (_uiCam != null) _uiCam.enabled = false;
            _npcMode = false; _npcName = null; _npcQuests = null;
            NpcHoverHudSuppressor.SuppressAll(false);
            FiresBookView.Instance?.Clear();
            FiresCore.Bridge.ModUiRegistry.Unregister("FiresQuestBookJournal");
        }

        // ── build / bind ──
        private static void EnsureBuilt()
        {
            if (_built) return;
            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) { Debug.LogError("[QuestBookJournal] uiBundle null"); return; }
            GameObject prefab = null;
            foreach (var an in bundle.GetAllAssetNames())
                if (an.EndsWith("/firesquestjournalpanel.prefab", StringComparison.OrdinalIgnoreCase)) { prefab = bundle.LoadAsset<GameObject>(an); break; }
            if (prefab == null) { Debug.LogError("[QuestBookJournal] FiresQuestJournalPanel.prefab not in bundle — rebuild it"); return; }

            _uiLayer = FreeLayer();

            // Dedicated perspective camera (FOV 42, planeDistance 10). The baked page poses were solved for exactly
            // this projection so the pages read as the intended trapezoid; a ScreenSpaceOverlay canvas renders
            // orthographically and would flatten them. clearFlags=Depth leaves the game visible behind the book.
            _uiCam = new GameObject("FiresQuestBookCam", typeof(Camera)).GetComponent<Camera>();
            UnityEngine.Object.DontDestroyOnLoad(_uiCam.gameObject);
            _uiCam.orthographic = false; _uiCam.fieldOfView = 42f;
            _uiCam.clearFlags = CameraClearFlags.Depth; _uiCam.depth = 90f;
            _uiCam.cullingMask = 1 << _uiLayer;
            _uiCam.nearClipPlane = 0.1f; _uiCam.farClipPlane = 100f;
            _uiCam.enabled = false;

            _canvas = new GameObject("FiresQuestBookCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.SetActive(false);   // build while inactive so QuestBookCursorKeeper.OnEnable (which starts the reveal) fires from Open's SetActive(true) AFTER _built=true — else IsOpen is false and the first-open reveal is skipped
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var c = _canvas.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = _uiCam; c.planeDistance = 10f; c.sortingOrder = 6300;
            var s = _canvas.GetComponent<CanvasScaler>(); s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920f, 1080f); s.matchWidthOrHeight = 1f;

            _panel = UnityEngine.Object.Instantiate(prefab, _canvas.transform);
            var rt = _panel.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.localScale = Vector3.one;

            Bind(_panel.transform);
            SetLayerRecursive(_canvas, _uiLayer);   // after Bind so the runtime scroll objects are on the culled layer too
            _canvas.AddComponent<QuestBookCursorKeeper>();
            _built = true;
        }

        // A layer the dedicated UI camera can cull to exclusively (so it renders only the journal, over the game).
        private static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 5;
        }
        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++) SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        private static void Bind(Transform t)
        {
            // Book background: turn the baked sprite Image into a RawImage carrying the live RenderTexture.
            var bookGo = Find(t, "BookBg");
            if (bookGo != null)
            {
                var im = bookGo.GetComponent<Image>(); if (im != null) UnityEngine.Object.DestroyImmediate(im);   // immediate: else the RawImage add fails (two Graphics) and _bookRaw stays null
                _bookRaw = bookGo.AddComponent<RawImage>();
            }

            _tocHeader = Tmp(t, "TocHeader");
            _questName = Tmp(t, "QuestName");
            _previewPlaceholder = Tmp(t, "PreviewPlaceholder");
            var pv = Find(t, "PreviewImage");
            if (pv != null)
            {
                var pim = pv.GetComponent<Image>(); if (pim != null) UnityEngine.Object.DestroyImmediate(pim);
                _previewImage = pv.AddComponent<RawImage>();
                if (_previewImage != null)
                {
                    _previewImage.color = Color.white; _previewImage.enabled = false;
                    _previewImage.raycastTarget = true;
                    pv.AddComponent<QuestPreviewOrbitController>();   // drag = spin the 3D preview, wheel = zoom (same as the old screen)
                }
            }

            Click(t, "CloseButton", Close);
            var toggle = Find(t, "TocToggle"); if (toggle != null) { _tocToggleLabel = toggle.GetComponentInChildren<TMP_Text>(); var b = toggle.GetComponent<Button>(); if (b != null) b.onClick.AddListener(OnToggle); }

            _contentRoot = Find(t, "ContentRoot");
            _rowTemplate = Find(t, "TocRowTemplate");
            _tocContent = BuildScroll(Find(t, "TocPool")?.transform, false);
            // clean up baked sample rows
            var pool = Find(t, "TocPool");
            if (pool != null) for (int i = pool.transform.childCount - 1; i >= 0; i--) { var ch = pool.transform.GetChild(i); if (ch.name.StartsWith("TocSample")) UnityEngine.Object.Destroy(ch.gameObject); }

            // Description: wrap the baked DescText in a live scroll.
            _descText = Tmp(t, "DescText");
            _descContent = BuildDescScroll(Find(t, "DescScroll")?.transform);

            // The taller desc scroll (DescTopInset -80) now covers the band the baked DETAILS caption sat
            // in — lift the caption to sit between the preview PNG box and the scroll top. Local-space math
            // via the caption's parent so the page/canvas transform can't skew it.
            try
            {
                TMP_Text detailsHdr = null;
                foreach (var tx in t.GetComponentsInChildren<TMP_Text>(true))
                    if (tx != null && tx != _descText && tx.text != null && tx.text.Trim().Equals("DETAILS", StringComparison.OrdinalIgnoreCase)) { detailsHdr = tx; break; }
                var pvGo = Find(t, "PreviewImage");
                if (detailsHdr != null && pvGo != null)
                {
                    var parent = detailsHdr.rectTransform.parent;
                    var pc = new Vector3[4]; ((RectTransform)pvGo.transform).GetWorldCorners(pc);
                    var hc = new Vector3[4]; detailsHdr.rectTransform.GetWorldCorners(hc);
                    float pvBotL = Mathf.Min(parent.InverseTransformPoint(pc[0]).y, parent.InverseTransformPoint(pc[3]).y);
                    float hdrTopL = Mathf.Max(parent.InverseTransformPoint(hc[1]).y, parent.InverseTransformPoint(hc[2]).y);
                    float hdrBotL = Mathf.Min(parent.InverseTransformPoint(hc[0]).y, parent.InverseTransformPoint(hc[3]).y);
                    float hdrH = hdrTopL - hdrBotL;
                    float delta = (pvBotL - hdrH * 0.45f) - hdrTopL;   // heading top sits a small gap below the preview
                    detailsHdr.rectTransform.localPosition += new Vector3(0f, delta, 0f);
                    Debug.Log($"[QuestBookJournal] DETAILS caption lifted delta={delta:F1} (pvBot={pvBotL:F1} hdrTop={hdrTopL:F1} hdrH={hdrH:F1})");
                }
            }
            catch (Exception ex) { Debug.LogWarning("[QuestBookJournal] DETAILS caption lift failed: " + ex.Message); }
            if (_descText != null)
            {
                // Re-assert wrap/alignment: the Unity-6-baked TMP state doesn't survive into the game's older TMP
                // (in-game the text rendered as ONE unwrapped right-pinned line, clipping its head off the box).
                _descText.enableWordWrapping = true;
                _descText.overflowMode = TextOverflowModes.Overflow;
                _descText.alignment = TextAlignmentOptions.TopLeft;
                _descText.margin = new Vector4(32f, 20f, 32f, 6f);   // padding off both page edges under the perspective narrowing
            }

            ApplyAveria(t);
        }

        // All journal text is Averia Sans (UIFontConfig.AveriaLibre = Valheim-AveriaSansLibre). The baked TMP default
        // resolves to LiberationSans in the bundle, so reassign the font at runtime on every TMP_Text.
        private static void ApplyAveria(Transform root)
        {
            if (root == null) return;
            if (_averia == null) { try { _averia = FiresCore.UI.UIFontConfig.GetFont(FiresCore.UI.UIFontConfig.FontStyle.AveriaLibre); } catch { } }
            if (_averia == null) return;
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true)) tmp.font = _averia;
        }

        // Turn a baked ScrollPool anchor into a live RectMask2D vertical scroll; returns the Content rect for rows.
        private static RectTransform BuildScroll(Transform anchor, bool _)
        {
            if (anchor == null) return null;
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect)); scroll.transform.SetParent(anchor, false);
            var srt = (RectTransform)scroll.transform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image)); vp.transform.SetParent(scroll.transform, false);
            var vrt = (RectTransform)vp.transform; vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero; vp.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var content = new GameObject("Content", typeof(RectTransform)); content.transform.SetParent(vp.transform, false);
            var crt = (RectTransform)content.transform; crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;   // a fresh RectTransform defaults to (100,100): with stretch anchors that made content 100px WIDER than the viewport (rows/highlights hanging 50px past each edge)
            // Top/bottom padding = the tilt rise across a row's width: RectMask2D clips along canvas-horizontal
            // lines (it cannot rotate with the page plane), so a row resting at the very top/bottom of the viewport
            // pokes its raised corner past the clip line and gets sheared. Padding keeps resting rows clear of it.
            var vlg = content.AddComponent<VerticalLayoutGroup>(); vlg.childControlWidth = vlg.childForceExpandWidth = true; vlg.childControlHeight = true; vlg.childForceExpandHeight = false; vlg.spacing = 3f; vlg.padding = new RectOffset(2, 2, 26, 26);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.GetComponent<ScrollRect>(); sr.viewport = vrt; sr.content = crt; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 90f;
            return crt;
        }

        private static RectTransform BuildDescScroll(Transform anchor)
        {
            if (anchor == null || _descText == null) return null;
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect)); scroll.transform.SetParent(anchor, false);
            // The baked DescScroll anchor runs past the page's inner bottom edge, so the clip region (and the
            // scrollable text with it) spilled below the page. Inset the live scroll to bring it onto the page.
            var srt = (RectTransform)scroll.transform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = new Vector2(0f, DescBottomInset); srt.offsetMax = new Vector2(0f, -DescTopInset);
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image)); vp.transform.SetParent(scroll.transform, false);
            var vrt = (RectTransform)vp.transform; vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero; vp.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            // The text itself is the scroll content: its size fitter grows the height with the quest text, the
            // viewport clips, wheel/drag scrolls. (The old intermediate Content had no layout source, so its
            // fitter kept it at height 0 — nothing ever scrolled — and its unzeroed default sizeDelta made the
            // text 100px wider than the viewport, clipping the head of every line.)
            _descText.transform.SetParent(vp.transform, false);
            var drt = _descText.rectTransform; drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0.5f, 1); drt.sizeDelta = Vector2.zero; drt.anchoredPosition = Vector2.zero;
            _descText.enableAutoSizing = false;
            var fit = _descText.gameObject.GetComponent<ContentSizeFitter>() ?? _descText.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.GetComponent<ScrollRect>(); sr.viewport = vrt; sr.content = drt; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 320f; _descScroll = sr;
            return drt;
        }

        // ── data ──
        private static (List<ActiveQuestData> active, List<string> completed) LoadQuests()
        {
            var active = new List<ActiveQuestData>(); var completed = new List<string>();
            try
            {
                var p = Player.m_localPlayer; if (p == null) return (active, completed);
                long id = p.GetPlayerID(); if (id <= 0) return (active, completed);
                var data = VaultOfKnowledge.Instance?.GetPlayerData(id); if (data == null) return (active, completed);
                if (data.ActiveQuests != null) active.AddRange(data.ActiveQuests);
                if (data.CompletedQuests != null) completed.AddRange(data.CompletedQuests.Keys);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestBookJournal] load: " + ex.Message); }
            return (active, completed);
        }

        private static void PopulateToc()
        {
            foreach (var r in _rows) if (r != null) UnityEngine.Object.Destroy(r);
            _rows.Clear();
            if (_tocHeader != null) _tocHeader.text = _npcMode ? (_npcName ?? "Available Quests") : (_showCompleted ? "Completed Quests" : "Table of Contents");
            if (_tocToggleLabel != null) _tocToggleLabel.text = _npcMode ? "Accept Quest" : (_showCompleted ? "Show Active" : "Show Completed");
            if (_tocContent == null || _rowTemplate == null) return;

            var (active, completed) = LoadQuests();
            var defs = new List<QuestDefinition>();
            if (_npcMode)
            {
                if (_npcQuests != null) defs.AddRange(_npcQuests);
            }
            else
            {
                IEnumerable<string> ids = _showCompleted ? completed : active.Select(a => a.QuestId);
                foreach (var qid in ids) { var d = QuestManager.Instance?.GetQuestDefinition(qid); if (d != null) defs.Add(d); }
            }
            foreach (var def in defs)
            {
                var row = UnityEngine.Object.Instantiate(_rowTemplate, _tocContent);
                row.name = "Row_" + def.QuestId; row.SetActive(true);

                // The BAKED row children misplace in-game (rects/TMP state don't survive the Unity-version gap:
                // the highlight band sat offset from its label). Replace the baked bg with a runtime-built one and
                // re-assert the label's rect + TMP layout.
                var bakedBg = row.transform.Find("RowBg"); if (bakedBg != null) UnityEngine.Object.DestroyImmediate(bakedBg.gameObject);
                var hl = new GameObject("RowHighlight", typeof(RectTransform), typeof(Image));
                hl.transform.SetParent(row.transform, false); hl.transform.SetAsFirstSibling();
                var hrt = (RectTransform)hl.transform;
                var him = hl.GetComponent<Image>(); him.color = new Color(0, 0, 0, 0); him.raycastTarget = true;

                var label = row.GetComponentInChildren<TMP_Text>(true);
                float pillW = 160f;
                if (label != null)
                {
                    string suffix = "";
                    if (_npcMode)
                    {
                        if (active.Any(a => a.QuestId == def.QuestId)) suffix = " <color=#F4C24A><b>[Active]</b></color>";
                        else if (completed.Contains(def.QuestId)) suffix = " <color=#7BD86B><b>[Done]</b></color>";
                    }
                    label.text = (def.Name ?? def.QuestId) + suffix;
                    label.alignment = TextAlignmentOptions.Center;
                    label.enableWordWrapping = false;
                    var lrt = label.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(6, 0); lrt.offsetMax = new Vector2(-6, 0);
                    label.ForceMeshUpdate();
                    pillW = label.preferredWidth + 28f;
                }
                // The highlight is a pill hugging the text, not a full-width band — the row spans the whole solved
                // plane, and a plane-wide band reads as jutting past the visible list column on the tilted page.
                hrt.anchorMin = new Vector2(0.5f, 0f); hrt.anchorMax = new Vector2(0.5f, 1f); hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(Mathf.Min(pillW, 232f), -6f); hrt.anchoredPosition = Vector2.zero;
                var captured = def;
                var btn = row.GetComponent<Button>() ?? row.AddComponent<Button>();
                btn.targetGraphic = him;
                btn.onClick.AddListener(() => { _selected = captured.QuestId; Highlight(); ShowDetails(captured); UpdateNpcActionButton(); });
                _rows.Add(row);
            }
            if (_canvas != null) SetLayerRecursive(_canvas, _uiLayer);   // newly-cloned rows default to layer 0; move them onto the culled layer
            ApplyAveria(_tocContent);   // cloned rows carry the template's font, but re-assert in case the bundle default leaked in
            if (_tocContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_tocContent);   // Bind built the canvas inactive, so the layout group never ran before first paint
                Canvas.ForceUpdateCanvases();
                if (_rows.Count > 0)
                {
                    var vp = _tocContent.parent as RectTransform;
                    var r0 = _rows[0].transform as RectTransform;
                    Debug.Log($"[QuestBookJournal] toc rects viewport={vp?.rect.size} content={_tocContent.rect.size} row0={r0?.rect.size} row0pos={r0?.anchoredPosition}");
                }
            }
            Highlight();
        }

        private static void Highlight()
        {
            foreach (var r in _rows)
            {
                if (r == null) continue;
                var img = r.GetComponentInChildren<Image>(true);
                if (img != null) img.color = (r.name == "Row_" + _selected) ? new Color(0.85f, 0.72f, 0.42f, 0.35f) : new Color(0, 0, 0, 0);
            }
        }

        private static void OnToggle()
        {
            if (_npcMode) { AcceptSelected(); return; }
            _showCompleted = !_showCompleted; _selected = null; PopulateToc(); ShowDetails(null);
        }

        private static string FirstPreviewPrefab(QuestDefinition quest)
        {
            if (quest?.QuestTargets != null)
                foreach (var t in quest.QuestTargets) if (!string.IsNullOrEmpty(t.Prefab)) return t.Prefab;
            if (quest?.Rewards != null)
                foreach (var r in quest.Rewards) if (!r.IsSkillExp && !string.IsNullOrEmpty(r.Target)) return r.Target;
            return null;
        }

        // NPC-mode action button: "Accept Quest" normally, "Abandon Quest" when the selected quest is already active.
        private static bool SelectedQuestIsActive()
        {
            try
            {
                var p = Player.m_localPlayer;
                if (p == null || string.IsNullOrEmpty(_selected)) return false;
                return QuestManager.Instance?.HasQuest(p.GetPlayerID(), _selected) ?? false;
            }
            catch { return false; }
        }

        // True when the selected quest is active AND every objective meets its target amount.
        private static bool SelectedQuestIsComplete()
        {
            try
            {
                if (!SelectedQuestIsActive()) return false;
                var def = _npcQuests?.FirstOrDefault(q => q.QuestId == _selected);
                if (def?.QuestTargets == null || def.QuestTargets.Count == 0) return false;
                var p = Player.m_localPlayer;
                var entry = VaultOfKnowledge.Instance?.GetPlayerData(p.GetPlayerID())?.ActiveQuests?.FirstOrDefault(a => a.QuestId == def.QuestId);
                if (entry == null) return false;
                foreach (var tg in def.QuestTargets)
                    if (QuestProgressTracker.GetProgressForTarget(def, entry, tg) < tg.Amount) return false;
                return true;
            }
            catch { return false; }
        }

        private static void UpdateNpcActionButton()
        {
            if (!_npcMode || _tocToggleLabel == null) return;
            if (SelectedQuestIsComplete()) _tocToggleLabel.text = "Turn In Quest";
            else _tocToggleLabel.text = SelectedQuestIsActive() ? "Abandon Quest" : "Accept Quest";
        }

        private static void AcceptSelected()
        {
            var def = _npcQuests?.FirstOrDefault(q => q.QuestId == _selected);
            if (def == null) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Select a quest first."); return; }
            if (SelectedQuestIsComplete()) { CompleteSelected(def); return; }
            bool wasActive = SelectedQuestIsActive();
            bool ok = wasActive
                ? global::FiresRPGmaker.UI.Screens.QuestInteractionScreenController.AbandonQuestCore(def)
                : global::FiresRPGmaker.UI.Screens.QuestInteractionScreenController.AcceptQuestCore(def);
            if (!ok) return;
            bool expectActive = !wasActive;
            if (_tocToggleLabel != null) _tocToggleLabel.text = expectActive ? "Abandon Quest" : "Accept Quest";   // optimistic; held until the vault converges
            ShowDetails(def);
            ScheduleTocRefresh(def.QuestId, expectActive);
        }

        // Turn in a completed active quest: verify, take the collect/item objectives from inventory, grant rewards,
        // mark completed, drop it from the client-local store, then fire OnComplete (+ chain follow-up).
        private static void CompleteSelected(QuestDefinition def)
        {
            var player = Player.m_localPlayer;
            if (player == null || def == null) return;
            long pid = player.GetPlayerID();
            var vok = VaultOfKnowledge.Instance;
            var data = vok?.GetPlayerData(pid);
            var entry = data?.ActiveQuests?.FirstOrDefault(a => a.QuestId == def.QuestId);
            if (entry == null) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest not active."); return; }
            foreach (var tg in def.QuestTargets ?? new List<QuestTarget>())
                if (QuestProgressTracker.GetProgressForTarget(def, entry, tg) < tg.Amount)
                { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "You don't have everything yet."); return; }

            // Take the collect/item objectives (the NPC keeps them).
            try
            {
                var inv = player.GetInventory();
                if (inv != null && def.QuestTargets != null)
                    foreach (var tg in def.QuestTargets)
                    {
                        string ot = (!string.IsNullOrEmpty(tg.Type) ? tg.Type : def.QuestType)?.ToLowerInvariant();
                        if (ot == "collect" || ot == "item" || ot == "gather" || ot == "harvest")
                            RemoveByPrefab(inv, tg.Prefab, tg.Amount);
                    }
            }
            catch (Exception ex) { Debug.LogWarning("[QuestPipe] turn-in take failed: " + ex.Message); }

            try { if (def.Rewards != null && def.Rewards.Count > 0) global::FiresRPGmaker.Modules.Quest.QuestRewardDispatcher.GrantAll(data, player, def.Rewards); }
            catch (Exception ex) { Debug.LogWarning("[QuestPipe] reward grant failed: " + ex.Message); }

            // Persist the completion CLIENT-LOCAL — SavePlayerData no-ops on clients, so without this the completion
            // vanishes on the next GetPlayerData and QuestFinished chain gates (SnorriOne → SnorriOne2) never unlock.
            try { global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.MarkCompleted(def.QuestId); } catch { }
            try { data?.AddCompletedQuest(def.QuestId, 1); if (data != null) { data.IsDirty = true; vok.SavePlayerData(pid, data); } } catch { }
            global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Abandon(def.QuestId);

            try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.OnCompleted(def, data, player); } catch { }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Quest completed: {def.Name ?? def.QuestId}");
            Debug.Log($"[QuestPipe] TurnIn OK {def.QuestId}");
            PopulateToc();
            ShowDetails(null);
        }

        private static void RemoveByPrefab(Inventory inv, string prefab, int amount)
        {
            if (inv == null || string.IsNullOrEmpty(prefab) || amount <= 0) return;
            int remaining = amount;
            foreach (var it in inv.GetAllItems().ToList())
            {
                if (remaining <= 0) break;
                string ip = it?.m_dropPrefab != null ? it.m_dropPrefab.name : null;
                if (string.IsNullOrEmpty(ip) || !string.Equals(ip, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                int take = Math.Min(it.m_stack, remaining);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
        }

        private static void ScheduleTocRefresh(string questId, bool expectActive)
        {
            var keeper = _canvas != null ? _canvas.GetComponent<QuestBookCursorKeeper>() : null;
            if (keeper != null && keeper.isActiveAndEnabled) keeper.StartCoroutine(RefreshWhenSynced(questId, expectActive));
        }

        // Poll until the client vault reflects the action — the authoritative copy lands 1-2 round-trips after the
        // flag RPC. A fixed-delay refresh raced it and flipped the UI back to "Accept", making successful accepts
        // look failed (and inviting repeat clicks). On timeout we repopulate with whatever is actually true.
        private static System.Collections.IEnumerator RefreshWhenSynced(string questId, bool expectActive)
        {
            float deadline = Time.unscaledTime + 6f;
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForSeconds(0.4f);
                if (!IsOpen) yield break;
                bool nowActive = false;
                try { var p = Player.m_localPlayer; if (p != null) nowActive = QuestManager.Instance?.HasQuest(p.GetPlayerID(), questId) ?? false; } catch { }
                if (nowActive == expectActive) break;
            }
            if (!IsOpen) yield break;
            PopulateToc();
            UpdateNpcActionButton();
        }

        /// <summary>
        /// "Time Limit: 30 min" for quests not yet accepted; "Time Left: 12m 40s" once active (from
        /// the vault's StartedAt stamp); "Time expired!" past the limit. Reads the client's synced
        /// vault copy — same source the journal's [Active] tags use.
        /// </summary>
        private static string FormatTimeLimitLine(QuestDefinition quest, string gold, string gray)
        {
            try
            {
                var p = Player.m_localPlayer;
                if (p != null && global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.Instance != null)
                {
                    var data = global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.Instance.GetPlayerQuestData(p.GetPlayerID());
                    var entry = data?.ActiveQuests?.FirstOrDefault(q => q.QuestId == quest.QuestId);
                    if (entry != null && entry.StartedAt != default)
                    {
                        double left = quest.TimeLimitMinutes * 60.0 - (DateTime.UtcNow - entry.StartedAt).TotalSeconds;
                        if (left <= 0)
                            return $"<b><color={gold}>Time Left:</color></b> <color=#c46060>Time expired!</color>";
                        int m = (int)(left / 60.0), s = (int)(left % 60.0);
                        return $"<b><color={gold}>Time Left:</color></b> <color={gray}>{m}m {s:00}s</color>";
                    }
                }
            }
            catch { }
            return $"<b><color={gold}>Time Limit:</color></b> <color={gray}>{quest.TimeLimitMinutes} min</color>";
        }

        private static void ShowDetails(QuestDefinition quest)
        {
            if (_previewImage != null) _previewImage.enabled = false;
            if (_previewPlaceholder != null) _previewPlaceholder.gameObject.SetActive(true);
            if (quest == null)
            {
                QuestPreviewRenderer.Instance?.ClearStage();
                if (_questName != null) _questName.text = "Select a quest";
                if (_descText != null) _descText.text = "<i>Choose an entry from the table of contents.</i>";
                return;
            }
            var (active, _) = LoadQuests();
            var entry = active.FirstOrDefault(a => a.QuestId == quest.QuestId);
            bool completed = entry == null && _showCompleted;
            if (_questName != null) _questName.text = quest.Name ?? quest.QuestId;

            var previewTex = global::FiresRPGmaker.UI.Screens.QuestInteractionScreenController.LoadQuestPreviewTexture(quest);
            if (previewTex != null && _previewImage != null)
            {
                QuestPreviewRenderer.Instance?.ClearStage();
                _previewImage.texture = previewTex; _previewImage.enabled = true;
                if (_previewPlaceholder != null) _previewPlaceholder.gameObject.SetActive(false);
            }
            else
            {
                // No assigned image: live 3D preview of the first target/reward prefab (same renderer as the old screen).
                string prefabName = FirstPreviewPrefab(quest);
                if (prefabName != null && _previewImage != null)
                {
                    var pr = QuestPreviewRenderer.EnsureInstance();
                    if (pr != null && pr.StagePreview(prefabName) && pr.Texture != null)
                    {
                        _previewImage.texture = pr.Texture; _previewImage.enabled = true;
                        if (_previewPlaceholder != null) _previewPlaceholder.gameObject.SetActive(false);
                    }
                }
            }

            const string gold = "#6B4A1E", gray = "#4A3A22", green = "#2E6B2E", yellow = "#8A6A1E";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{quest.Description ?? "No description."}\n");
            if (quest.QuestTargets != null && quest.QuestTargets.Count > 0)
            {
                sb.AppendLine($"<b><color={gold}>Objectives:</color></b>");
                foreach (var tg in quest.QuestTargets)
                {
                    string prog = "";
                    if (!completed && entry != null) { int cur = QuestProgressTracker.GetProgressForTarget(quest, entry, tg); prog = $" <color={(cur >= tg.Amount ? green : yellow)}>[{cur}/{tg.Amount}]</color>"; }
                    else if (completed) prog = $" <color={green}>[{tg.Amount}/{tg.Amount}]</color>";
                    // Verb from THIS objective's own type ("Item:" collect etc.), else the quest-level type.
                    string verb = QuestVerb(!string.IsNullOrEmpty(tg.Type) ? tg.Type : quest.QuestType);
                    sb.AppendLine($"<color={gray}>{verb}{QuestProgressTracker.GetTargetDisplayName(tg)} x{tg.Amount}</color>{prog}");
                }
                sb.AppendLine();
            }
            if (quest.Rewards != null && quest.Rewards.Count > 0)
            {
                sb.AppendLine($"<b><color={gold}>Rewards:</color></b>");
                bool anyUnresolved = false;
                foreach (var r in quest.Rewards)
                {
                    string line = RewardLine(r, out bool unresolved);
                    if (unresolved) anyUnresolved = true;
                    sb.AppendLine($"<color={gray}>{line}</color>");
                }
                if (anyUnresolved)
                    sb.AppendLine($"<color=#8A5A1E><i>(?) = reward not recognized — this quest may need an admin to fix its reward line.</i></color>");
                sb.AppendLine();
            }
            if (quest.TimeLimitMinutes > 0) sb.AppendLine(FormatTimeLimitLine(quest, gold, gray));
            if (completed) sb.AppendLine($"<b><color={green}>Completed!</color></b>");
            if (_descText != null)
            {
                _descText.text = sb.ToString();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_descText.rectTransform);   // grow content height NOW so the scroll knows its extent
            }
            Canvas.ForceUpdateCanvases();
            if (_descScroll != null) _descScroll.verticalNormalizedPosition = 1f;
        }

        // Objective verb from a type token, with a trailing space (empty for unknown so the line still reads).
        private static string QuestVerb(string type)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "kill":                       return "Kill ";
                case "collect": case "item": case "gather": return "Gather ";
                case "harvest":                    return "Harvest ";
                case "craft":                      return "Craft ";
                case "build":                      return "Build ";
                case "talk":                       return "Talk to ";
                case "move": case "travel":        return "Reach ";
                default:                            return "";
            }
        }

        // Honest reward line: resolve what we actually can (skill-exp, real ObjectDB items, typed non-item rewards);
        // anything we can't verify (a bare number, an unknown prefab) is marked "(?)" — never invented.
        private static string RewardLine(QuestReward r, out bool unresolved)
        {
            unresolved = false;
            if (r == null) return "";
            if (r.IsSkillExp) return $"{r.Target} XP +{r.Amount}";
            bool isItem = string.IsNullOrEmpty(r.Type) || r.Type.Equals("Item", StringComparison.OrdinalIgnoreCase);
            if (isItem)
            {
                if (IsRealItem(r.Target))
                    return $"{QuestProgressTracker.GetTargetDisplayName(new QuestTarget { Prefab = r.Target })} x{r.Amount}";
                unresolved = true;
                return $"{r.Target} (?)";
            }
            // Typed non-item reward (Skill / Pet / SetValue / …): show it plainly rather than guess.
            return $"{r.Type}: {r.Target}" + (r.Amount != 0 ? $" x{r.Amount}" : "");
        }

        private static bool IsRealItem(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            try { return ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(prefab) != null; }
            catch { return false; }
        }

        // ── open animation gating ──
        // Hide the page content while the book plays its open animation (the BookBg RenderTexture stays visible so the
        // animation shows), then reveal it once open. Called by QuestBookCursorKeeper's reveal coroutine.
        internal static void SetContentHidden(bool hidden)
        {
            if (_contentRoot != null) _contentRoot.SetActive(!hidden);
        }
        internal static void OnBookOpened()
        {
            FiresBookView.Instance?.Freeze();   // hold on the fully-open frame instead of looping closed
            SetContentHidden(false);
        }

        // ── helpers ──
        private static GameObject Find(Transform root, string name) { if (root == null) return null; if (root.name == name) return root.gameObject; for (int i = 0; i < root.childCount; i++) { var r = Find(root.GetChild(i), name); if (r != null) return r; } return null; }
        private static TMP_Text Tmp(Transform r, string n) { var g = Find(r, n); return g != null ? g.GetComponent<TMP_Text>() : null; }
        private static void Click(Transform r, string n, Action a) { var b = Find(r, n)?.GetComponent<Button>(); if (b != null) b.onClick.AddListener(() => a()); }
    }

    internal sealed class QuestBookCursorKeeper : MonoBehaviour
    {
        private readonly List<Graphic> _cross = new List<Graphic>();
        private GameObject _crossGo;
        private void OnEnable() { try { FiresCore.UI.InputBlock.Block(true); } catch { } try { FiresCore.UI.CrosshairRaycast.Suppress(_cross); } catch { } Hide(true); StartCoroutine(Reveal()); }
        private System.Collections.IEnumerator Reveal()
        {
            QuestBookJournal.SetContentHidden(true);
            float t = 0f;
            while (t < QuestBookJournal.RevealDelay && QuestBookJournal.IsOpen) { t += Time.deltaTime; yield return null; }
            if (QuestBookJournal.IsOpen) QuestBookJournal.OnBookOpened();
        }
        private void OnDisable() { try { FiresCore.UI.InputBlock.Block(false); } catch { } try { FiresCore.UI.CrosshairRaycast.Restore(_cross); } catch { } Hide(false); }
        private void OnGUI() { if (!QuestBookJournal.IsOpen) return; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void Update() { if (!QuestBookJournal.IsOpen) return; if (Input.GetKeyDown(KeyCode.Escape)) { QuestBookJournal.Close(); return; } try { FiresCore.UI.CrosshairRaycast.Reassert(_cross); } catch { } Hide(true); }
        private void Hide(bool hidden) { if (_crossGo == null && global::Hud.instance != null) _crossGo = F(global::Hud.instance.transform, "crosshair"); if (_crossGo != null && _crossGo.activeSelf == hidden) _crossGo.SetActive(!hidden); }
        private static GameObject F(Transform r, string n) { if (r == null) return null; if (r.name == n) return r.gameObject; for (int i = 0; i < r.childCount; i++) { var c = F(r.GetChild(i), n); if (c != null) return c; } return null; }
    }
}
