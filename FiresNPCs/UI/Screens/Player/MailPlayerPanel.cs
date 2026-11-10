using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Mail panel. Loads <c>FiresMailPanel.prefab</c> from the <c>firesnpcsui</c>
    /// bundle (baked from Unity via <c>Valheim → Build Fires Mail prefab</c> + <c>Valheim → Build
    /// AssetBundles</c>) and binds runtime behaviour to the named children documented in
    /// FiresMailBuilder.cs.
    ///
    /// Inbox rendering: for each mail, clone <c>ContentArea/InboxView/EnvelopeStack/EnvelopeTemplate</c>
    /// (a UI Image carrying Envelope.png) and position it at <c>y = -index × strip</c>. Later siblings
    /// render on top so each prior envelope shows only its top label strip; the last envelope
    /// renders in full so the wax seal + stamps anchor the bottom of the stack.
    /// </summary>
    public class MailPlayerPanel : MonoBehaviour
    {
        private static MailPlayerPanel _instance;
        private static int _openCount;
        // same stale-counter guard as BasePlayerModulePanel.AnyOpen (pause menu blocked after the NPC book)
        public static bool AnyOpen
        {
            get
            {
                if (_openCount <= 0) return false;
                if (_instance != null && _instance._panelGo != null && _instance._panelGo.activeInHierarchy) return true;
                Debug.LogWarning("[FiresRPGmaker] [Panels] Mail counted open but not on screen; counter reset (pause menu unblocked)");
                _openCount = 0; if (_instance != null) _instance._opened = false;
                return false;
            }
        }
        private bool _opened;
        // Closed by the central Menu.Show Escape hook (GUIManager.CloseAllPanels) — no self-close on Escape.
        public static void CloseAny() { try { if (_instance != null && _instance.IsOpen) _instance.Close(); } catch { } }

        private GameObject _canvasGo;
        private GameObject _panelGo;

        private string _profileName;
        private MailEntry _selected;
        private enum Tab { Inbox, Send }
        private Tab _tab = Tab.Inbox;

        // Bound prefab nodes.
        private TMP_Text _titleText, _subtitleText;
        private Button _closeButton;
        private Button _tabInboxBtn, _tabSendBtn;
        private GameObject _inboxView, _sendView;
        private RectTransform _envelopeStack;
        private GameObject _envelopeTemplate;
        private TMP_Text _detailSubject, _detailFrom, _detailDate, _detailBody, _statusText;
        private RectTransform _attachmentsContent;
        private Button _claimAllButton;
        private TMP_InputField _sendTo, _sendSubject, _sendMessage;
        private Button _sendButton;
        private TMP_Text _sendStatusText;

        // Fan-stack tuning — must match the EnvelopeTemplate height in the prefab. 240 × 0.18 ≈ 43.
        // The LAST envelope renders 25% taller (ENV_LAST_HEIGHT) so its proportions read as a real
        // envelope sitting on the table, not a flat square.
        private const float ENV_HEIGHT      = 240f;
        private const float ENV_LAST_HEIGHT = 300f;
        private const float ENV_STRIP       = 43f;

        public static void Show(string profileName, string npcDisplayName)
        {
            if (_instance == null)
            {
                var go = new GameObject("FiresMailPanel");
                _instance = go.AddComponent<MailPlayerPanel>();
                DontDestroyOnLoad(go);
            }
            _instance.Open(profileName, npcDisplayName);
        }

        // Toggle open/closed — used by the physical Mailbox piece's interact.
        public static void Toggle(string profileName, string npcDisplayName)
        {
            if (AnyOpen && _instance != null) _instance.Close();
            else Show(profileName, npcDisplayName);
        }

        private void Open(string profileName, string npcDisplayName)
        {
            if (!EnsureBuilt()) return;
            _profileName = profileName;
            _selected = null;
            _tab = Tab.Inbox;
            if (_subtitleText != null)
                _subtitleText.text = npcDisplayName + "  -  Profile: " + (string.IsNullOrEmpty(profileName) ? "<none>" : profileName);
            _panelGo.SetActive(true);
            MailNetwork.OnInbox = OnInboxUpdated;
            MailNetwork.OnResult = OnMailResult;
            Refresh();
            MailNetwork.RequestInbox(_profileName); // server delivers profile mail once, returns the real inbox
            if (!_opened) { _opened = true; _openCount++; }
            try { GUIManager.BlockInput(true); } catch { }
            try { GUIManager.ApplyGameFontToAll(_panelGo.transform); } catch { }
        }

        // Fired on the local client when the server inbox reply lands (also after a claim). Repaints the stack
        // and refreshes the unread badge in the title.
        private static void OnInboxUpdated()
        {
            if (_instance == null || !_instance.IsOpen) return;
            if (_instance._titleText != null)
                _instance._titleText.text = MailNetwork.ClientUnread > 0 ? $"Mail ({MailNetwork.ClientUnread})" : "Mail";
            if (_instance._tab == Tab.Inbox) _instance.RefreshInbox();
        }

        // Fired with a status line after a send or claim reply.
        private static void OnMailResult(string status)
        {
            if (_instance == null || !_instance.IsOpen || string.IsNullOrEmpty(status)) return;
            if (_instance._statusText != null) _instance._statusText.text = status;
            if (_instance._sendStatusText != null) _instance._sendStatusText.text = status;
        }

        private void Close()
        {
            if (_panelGo != null) _panelGo.SetActive(false);
            if (_opened) { _opened = false; _openCount = Mathf.Max(0, _openCount - 1); }
            if (_openCount == 0) { try { GUIManager.BlockInput(false); } catch { } }
        }

        public bool IsOpen => _panelGo != null && _panelGo.activeSelf;

        private void Update()
        {
            if (!IsOpen) return;
            // Escape owned by the central Menu.Show hook — no self-close here (avoids the menu-opens race).
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // the kit inbox column's wheel scroll, as a real UI scroll handler on the column, so a physical wheel and an
        // EventSystem scroll event (rpg_pane_shots_test) reach it the same way; row clicks still go to the rows' Buttons.
        // the inbox column was fitted ONCE
        // (_kitFitted). This watcher sits on the panel's Background; when its size changes (a corner resize, a resolution change) the column
        // is fitted again and the rows re-laid in the same frame. Both steps measure from the Background each time, so re-running them is safe.
        private sealed class KitResizeWatcher : UnityEngine.EventSystems.UIBehaviour
        {
            internal MailPlayerPanel Panel;
            private Vector2 _last;
            protected override void OnRectTransformDimensionsChange()
            {
                if (Panel == null || !KitOn || Panel._envelopeStack == null) return;
                var size = ((RectTransform)transform).rect.size;
                if ((size - _last).sqrMagnitude < 1f) return;
                _last = size;
                Panel._kitFitted = false;
                Panel.FitKitColumn();
                Panel.LayoutKitRows();
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] panel resized to {size.x:0}x{size.y:0}: inbox column refitted");
            }
        }

        private sealed class KitScrollSink : MonoBehaviour, UnityEngine.EventSystems.IScrollHandler
        {
            internal MailPlayerPanel Panel;
            public void OnScroll(UnityEngine.EventSystems.PointerEventData e)
            {
                if (Panel == null || !KitOn || Panel._envelopeStack == null) return;
                float before = Panel._kitScroll;
                Panel._kitScroll -= e.scrollDelta.y * (KIT_ROW + KIT_GAP);
                Panel.LayoutKitRows();
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] kit inbox scroll {before:0} -> {Panel._kitScroll:0} of {Mathf.Max(0f, Panel._kitContent - Panel._envelopeStack.rect.height):0}");
            }
        }

        /// <summary>Builds the canvas and instantiates the FiresMailPanel prefab from the
        /// <c>firesnpcsui</c> bundle, then binds every runtime widget to its named child.
        /// Returns false if the bundle is missing or the prefab isn't packed into it — caller
        /// gates Open() on the result so the panel never half-builds.</summary>
        private bool EnsureBuilt()
        {
            if (_panelGo != null) return true;

            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) { Debug.LogError("[MailPlayerPanel] uiBundle is null — can't load FiresMailPanel.prefab"); return false; }

            GameObject prefab = null;
            foreach (var an in bundle.GetAllAssetNames())
            {
                if (an.EndsWith("/firesmailpanel.prefab", System.StringComparison.OrdinalIgnoreCase))
                { prefab = bundle.LoadAsset<GameObject>(an); break; }
            }
            if (prefab == null)
            {
                Debug.LogError("[MailPlayerPanel] FiresMailPanel.prefab not in firesnpcsui bundle — rebuild the bundle from Unity (Valheim/Build AssetBundles).");
                return false;
            }

            _canvasGo = new GameObject("MailCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panelGo = Object.Instantiate(prefab, _canvasGo.transform); global::FiresRPGmaker.UI.NpcBookController.FixNullFonts(_panelGo);
            _panelGo.name = "FiresMailPanel";
            _panelGo.SetActive(false);

            BindAll();
            WireListeners();
            return true;
        }

        // ── prefab binding ────────────────────────────────────────────────────

        private void BindAll()
        {
            var root = _panelGo.transform;
            _titleText      = FindTmp(root, "Background/TitleBar/TitleText");
            _subtitleText   = FindTmp(root, "Background/TitleBar/SubtitleText");
            _closeButton    = FindBtn(root, "Background/TitleBar/CloseButton");
            BasePlayerModulePanel.FixCloseGlyph(_closeButton);
            _tabInboxBtn    = FindBtn(root, "Background/TabInbox");
            _tabSendBtn     = FindBtn(root, "Background/TabSend");

            _inboxView      = Find(root, "Background/ContentArea/InboxView");
            _sendView       = Find(root, "Background/ContentArea/SendView");

            // EnvelopeStack is parented to Background (sits BESIDE the parchment, not inside it).
            _envelopeStack    = Find(root, "Background/EnvelopeStack")?.GetComponent<RectTransform>();
            _envelopeTemplate = Find(root, "Background/EnvelopeStack/EnvelopeTemplate");
            if (_envelopeTemplate != null) _envelopeTemplate.SetActive(false);

            FitInsideParchment(Find(root, "Background")?.GetComponent<RectTransform>(), Find(root, "Background/ContentArea")?.GetComponent<RectTransform>());
            // move it 20 px left.
            var detailPane = Find(root, "DetailPane")?.GetComponent<RectTransform>();
            if (detailPane != null) { detailPane.anchoredPosition -= new Vector2(20f, 0f); global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[MailPanel] DetailPane moved 20 px left"); }

            // Subtitle (NPC name + profile) is irrelevant for Mail and was overlapping "Attachments".
            if (_subtitleText != null) _subtitleText.gameObject.SetActive(false);

            // Use recursive-by-name (these are uniquely-named nodes) since the user hand-moved several
            // of them out of DetailPane up to InboxView level — fully-qualified paths would miss.
            _detailSubject     = FindTmp(root, "DetailSubject");
            _detailFrom        = FindTmp(root, "DetailFrom");
            _detailDate        = FindTmp(root, "DetailDate");
            _detailBody        = FindTmp(root, "DetailBody");
            _statusText        = FindTmp(root, "StatusText");
            _claimAllButton    = FindBtn(root, "ClaimAllButton");

            // AttachmentsScroll/Viewport/Content — find the scroll by name first, then drill into its
            // own Viewport/Content so we don't accidentally bind to the InboxScroll's Content node
            // (which is no longer used but still in the prefab).
            var attachScroll = Find(root, "AttachmentsScroll");
            if (attachScroll != null)
            {
                var viewport = attachScroll.transform.Find("Viewport");
                if (viewport != null) _attachmentsContent = viewport.Find("Content") as RectTransform;
                if (viewport != null) ClipWithRect(viewport);
            }

            _sendTo            = FindInput(root, "Background/ContentArea/SendView/SendTo");
            _sendSubject       = FindInput(root, "Background/ContentArea/SendView/SendSubject");
            _sendMessage       = FindInput(root, "Background/ContentArea/SendView/SendMessage");
            _sendButton        = FindBtn(root, "Background/ContentArea/SendView/SendButton");
            _sendStatusText    = FindTmp(root, "Background/ContentArea/SendView/SendStatusText");

            // Teach admins the broadcast address (server enforces the admin gate; non-admins just see the
            // normal placeholder).
            if (_sendTo != null && _sendTo.placeholder is TMP_Text toPh && global::FiresCore.Sync.AdminSyncing.IsLocalAdmin())
                toPh.text = "Player name (* = everyone)";

            // The "Inbox" header is now rendered as the TOP envelope tab inside the stack (see
            // BuildHeaderTab), so the separate floating InboxLabel text + the envelope-behind-label
            // image are redundant. Blank the label's TEXT (keep its GameObject active — the stack is
            // its child and would vanish if we disabled it) and hide the InboxEnvelopeTab image.
            var inboxLabel = Find(root, "InboxLabel");
            if (inboxLabel != null)
            {
                var lt = inboxLabel.GetComponent<TMP_Text>();
                if (lt != null) lt.text = "";
            }
            var inboxTab = Find(root, "InboxEnvelopeTab");
            if (inboxTab != null) inboxTab.SetActive(false);

            if (_envelopeTemplate == null) Debug.LogWarning("[MailPlayerPanel] EnvelopeTemplate not found — rebake the prefab from Unity (Valheim/Build Fires Mail prefab).");
            if (_titleText != null) _titleText.text = "Mail";
        }

        private void WireListeners()
        {
            if (_closeButton    != null) { _closeButton.onClick.RemoveAllListeners();    _closeButton.onClick.AddListener(Close); }
            if (_tabInboxBtn    != null) { _tabInboxBtn.onClick.RemoveAllListeners();    _tabInboxBtn.onClick.AddListener(() => { _tab = Tab.Inbox; Refresh(); }); }
            if (_tabSendBtn     != null) { _tabSendBtn.onClick.RemoveAllListeners();     _tabSendBtn.onClick.AddListener(() => { _tab = Tab.Send; Refresh(); }); }
            if (_claimAllButton != null) { _claimAllButton.onClick.RemoveAllListeners(); _claimAllButton.onClick.AddListener(OnClaimClicked); }
            if (_sendButton     != null) { _sendButton.onClick.RemoveAllListeners();     _sendButton.onClick.AddListener(OnSendClicked); }
        }

        // ── tab refresh ───────────────────────────────────────────────────────

        private void Refresh()
        {
            HighlightTab(_tabInboxBtn, _tab == Tab.Inbox);
            HighlightTab(_tabSendBtn,  _tab == Tab.Send);
            if (_inboxView != null) _inboxView.SetActive(_tab == Tab.Inbox);
            if (_sendView  != null) _sendView.SetActive(_tab == Tab.Send);
            if (_tab == Tab.Inbox) RefreshInbox();
        }

        private void HighlightTab(Button btn, bool active)
        {
            var img = btn?.GetComponent<Image>(); if (img == null) return;
            img.color = active ? new Color(0.32f, 0.22f, 0.14f, 0.95f) : new Color(0.18f, 0.14f, 0.08f, 0.95f);
        }

        // ── envelope fan-stack ────────────────────────────────────────────────

        private readonly List<GameObject> _envelopeClones = new List<GameObject>();
        private RectTransform _kitRows;   // the rows viewport under the pinned INBOX header

        private void RefreshInbox()
        {
            // Tear down previous clones (NOT the hidden template itself).
            for (int i = _envelopeClones.Count - 1; i >= 0; i--)
            {
                if (_envelopeClones[i] != null) Destroy(_envelopeClones[i]);
            }
            _envelopeClones.Clear();
            _kitRows = null;

            var inbox = MailNetwork.ClientInbox;
            if (inbox.Count == 0)
            {
                if (_detailSubject != null) _detailSubject.text = "Inbox is empty.";
                if (_detailFrom    != null) _detailFrom.text = "";
                if (_detailDate    != null) _detailDate.text = "";
                if (_detailBody    != null) _detailBody.text = "No mail yet. Messages from other players and NPCs arrive here.";
                ClearAttachments();
                if (_statusText != null) _statusText.text = "";
                return;
            }

            if (_envelopeTemplate == null || _envelopeStack == null)
            {
                Debug.LogWarning("[MailPlayerPanel] No EnvelopeTemplate or EnvelopeStack — bake the prefab + rebuild the bundle.");
                return;
            }

            // with the unified look ON the inbox is a contained,
            // scrollable kit list instead of the parchment envelope fan (which ran past the window's bottom edge).
            if (KitOn)
            {
                BuildKitList(inbox);
                var keep = _selected != null ? inbox.FirstOrDefault(m => m.Id == _selected.Id) : null;
                SelectMessage(keep ?? inbox[0]);
                KitDetail();
                return;
            }

            // Index 0 of the stack is a static "Inbox" header tab (non-clickable). The real mail
            // starts at index 1 and fans down below it. Sibling order = ASC so later siblings render
            // ON TOP — each envelope covers the one above it below its top strip; the last has nothing
            // above it so it shows full size.
            BuildHeaderTab();
            int n = inbox.Count;
            for (int i = 0; i < n; i++) BuildEnvelopeClone(inbox[i], i + 1, isLast: i == n - 1);

            // The reply rebuilds the list with fresh objects each refresh, so re-resolve the selection by Id.
            var toSelect = _selected != null ? inbox.FirstOrDefault(m => m.Id == _selected.Id) : null;
            SelectMessage(toSelect ?? inbox[0]);
        }

        // ── unified (kit) inbox ───────────────────────────────────────
        // Rows are clones of the envelope template (they carry SubjectText / SenderText / DateText) with the envelope art swapped
        // for a rounded kit fill and every decorative child image (stamps, wax seal) off. The column is clipped to the window
        // (RectMask2D, bottom kept 24 px above the window's own bottom edge) and scrolls with the mouse wheel.
        private static bool KitOn { get { try { return global::FiresRPGmaker.UI.FiresKitBookSkin.On; } catch { return false; } } }
        private const float KIT_HEADER = 34f, KIT_ROW = 54f, KIT_GAP = 6f, KIT_MARGIN = 24f;
        // rows sit KIT_PAD_X inside the column on each side, the first
        // row KIT_PAD_TOP under the pinned header and the list ends KIT_PAD_BOTTOM above the column bottom; the header text starts at the rows' text.
        private const float KIT_PAD_X = 10f, KIT_PAD_TOP = 4f, KIT_PAD_BOTTOM = 8f, KIT_HEADER_GAP = 10f, KIT_HEADER_LIFT = 24f;   // the header strip is lifted KIT_HEADER_LIFT above the column top, into the empty band under the tabs; the scroll viewport starts KIT_HEADER_GAP under the header strip (an empty band), rows KIT_PAD_TOP inside it
        private float _kitScroll, _kitContent;
        private bool _kitFitted;

        private void BuildKitList(List<MailEntry> inbox)
        {
            FitKitColumn();
            var sel = (_selected != null ? inbox.FirstOrDefault(m => m.Id == _selected.Id) : null) ?? inbox[0];

            var head = new GameObject("KitInboxHeader", typeof(RectTransform));
            head.transform.SetParent(_envelopeStack, false);
            _envelopeClones.Add(head);
            var hrt = (RectTransform)head.transform;
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f); hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(-2f * (KIT_PAD_X + 12f), KIT_HEADER - 6f);   // text starts where the row text does (row inset + 12)
            var ht = head.AddComponent<TextMeshProUGUI>();
            ht.font = global::FiresRPGmaker.UI.NpcBookController.GameFont();
            // say both
            ht.text = MailNetwork.ClientUnread > 0 ? $"INBOX  {inbox.Count}  ·  {MailNetwork.ClientUnread} UNREAD" : $"INBOX  {inbox.Count}";
            ht.fontSize = 15f; ht.characterSpacing = 6f; ht.color = FiresCore.UI.Shell.Kit.FiresTokens.Gold;
            ht.alignment = TextAlignmentOptions.MidlineLeft; ht.raycastTarget = false;

            // the rows
            // live in their own viewport that starts at the header's bottom edge and clips there (RectMask2D), so the header never sits on a row.
            var vpGo = new GameObject("KitRowsViewport", typeof(RectTransform));
            vpGo.transform.SetParent(_envelopeStack, false);
            _envelopeClones.Add(vpGo);
            _kitRows = (RectTransform)vpGo.transform;
            _kitRows.anchorMin = Vector2.zero; _kitRows.anchorMax = Vector2.one; _kitRows.pivot = new Vector2(0.5f, 1f);
            _kitRows.offsetMin = Vector2.zero; _kitRows.offsetMax = new Vector2(0f, -(KIT_HEADER - KIT_HEADER_LIFT + KIT_HEADER_GAP));   // header bottom (lifted) + the gap   // a clear gap between the header strip and the clipped list
            vpGo.AddComponent<RectMask2D>();

            for (int i = 0; i < inbox.Count; i++)
            {
                var msg = inbox[i];
                var clone = Object.Instantiate(_envelopeTemplate, _kitRows != null ? _kitRows : _envelopeStack);   // inside the rows viewport
                clone.name = "Mail_" + msg.Id;
                clone.SetActive(true);
                _envelopeClones.Add(clone);
                var rt = (RectTransform)clone.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-2f * KIT_PAD_X, KIT_ROW);   // KIT_PAD_X inside the column on each side

                foreach (var child in clone.GetComponentsInChildren<Image>(true)) if (child.gameObject != clone) child.enabled = false;   // stamps, seal
                var sh = clone.GetComponent<Shadow>(); if (sh != null) Destroy(sh);
                var img = clone.GetComponent<Image>();
                bool on = msg.Id == sel.Id;
                if (img != null)
                {
                    img.sprite = FiresCore.UI.Shell.Kit.KitSprites.RoundedFill(10f); img.type = Image.Type.Sliced; img.preserveAspect = false;
                    img.color = on ? FiresCore.UI.Shell.Kit.FiresTokens.Selected : FiresCore.UI.Shell.Kit.FiresTokens.Well;
                }
                if (on)
                {
                    var edge = new GameObject("SelectedEdge", typeof(RectTransform), typeof(Image));
                    edge.transform.SetParent(clone.transform, false);
                    var ert = (RectTransform)edge.transform; ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one; ert.offsetMin = ert.offsetMax = Vector2.zero;
                    var ei = edge.GetComponent<Image>(); ei.sprite = FiresCore.UI.Shell.Kit.KitSprites.RoundPill(10f, true); ei.type = Image.Type.Sliced;
                    ei.color = FiresCore.UI.Shell.Kit.FiresTokens.Gold; ei.raycastTarget = false;
                }

                KitText(FindTmp(clone.transform, "SubjectText"), msg.Subject, 16f, on ? FiresCore.UI.Shell.Kit.FiresTokens.GoldBright : FiresCore.UI.Shell.Kit.FiresTokens.Text, 0f, 0.5f);
                KitText(FindTmp(clone.transform, "SenderText"), msg.SenderName, 13f, FiresCore.UI.Shell.Kit.FiresTokens.TextMuted, 0.5f, 1f, left: true);
                KitText(FindTmp(clone.transform, "DateText"), msg.DateString, 13f, FiresCore.UI.Shell.Kit.FiresTokens.TextMuted, 0.5f, 1f, left: false);

                var btn = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => { SelectMessage(msg); Refresh(); });
            }
            _kitContent = KIT_PAD_TOP + inbox.Count * (KIT_ROW + KIT_GAP) + KIT_PAD_BOTTOM;   // the rows viewport (under the header) holds the rows + top / bottom padding
            // the pinned header covers rows scrolling up beneath it (an opaque backing in the window's own Panel colour, last sibling)
            // (a separate object: the header already carries a TMP Graphic, and one GameObject can hold only one Graphic)
            var back = new GameObject("KitInboxHeaderBack", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(_envelopeStack, false);
            _envelopeClones.Add(back);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(-2f * KIT_PAD_X, KIT_HEADER);   // the strip lines up with the rows, not the column edge
            var bi = back.GetComponent<Image>();
            var pc = FiresCore.UI.Shell.Kit.FiresTokens.Panel;
            bi.color = new Color(pc.r, pc.g, pc.b, 1f); bi.raycastTarget = false;
            back.transform.SetAsLastSibling();
            head.transform.SetAsLastSibling();
            LayoutKitRows();
        }

        // One row text: anchored inside the row (top half = subject across the full width; bottom half = sender left, date right),
        // one line, ellipsis instead of spilling past the row.
        private static void KitText(TMP_Text t, string s, float size, Color c, float yMin, float yMax, bool? left = null)
        {
            if (t == null) return;
            t.text = s ?? "";
            t.font = global::FiresRPGmaker.UI.NpcBookController.GameFont();
            t.fontSize = size; t.color = c; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
            var rt = t.rectTransform;
            float x0 = left == false ? 0.55f : 0f, x1 = left == true ? 0.6f : 1f;
            rt.anchorMin = new Vector2(x0, yMin); rt.anchorMax = new Vector2(x1, yMax); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(12f, yMin > 0f ? 2f : 4f); rt.offsetMax = new Vector2(-12f, yMin > 0f ? -4f : -2f);
            t.alignment = left == false ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        }

        private void LayoutKitRows()
        {
            if (_envelopeStack == null) return;
            float view = _kitRows != null ? _kitRows.rect.height : _envelopeStack.rect.height;   // the rows viewport under the header
            _kitScroll = Mathf.Clamp(_kitScroll, 0f, Mathf.Max(0f, _kitContent - view));
            // rows hang from the column's top edge (y = 0 down to -content); scrolling DOWN lifts them, so the start is
            // +scroll (had -scroll and pushed every row below the clip: an empty list after any scroll).
            // the header stays at the top (y = 0, drawn
            // last over an opaque Panel backing); only the rows move, starting under it.
            float y = _kitScroll - KIT_PAD_TOP;   // rows hang from the viewport top (= the header bottom), padded
            foreach (var go in _envelopeClones)
            {
                if (go == null) continue;
                var rt = (RectTransform)go.transform;
                if (go.name == "KitInboxHeader" || go.name == "KitInboxHeaderBack") { rt.anchoredPosition = new Vector2(0f, KIT_HEADER_LIFT); continue; }   // lifted
                if (go.name == "KitRowsViewport") continue;   // stretched under the header, never moved
                rt.anchoredPosition = new Vector2(0f, y);
                y -= KIT_ROW + KIT_GAP;
            }
        }

        // The column: same left edge, top where the envelope stack starts, bottom KIT_MARGIN above the window's bottom edge;
        // clipped so a long inbox scrolls instead of spilling out. Measured in world corners, so it holds at any resolution.
        private void FitKitColumn()
        {
            if (_envelopeStack == null) return;
            // no mask on the whole column any more (it clipped the lifted header); the rows clip in their own KitRowsViewport
            { var colMask = _envelopeStack.GetComponent<RectMask2D>(); if (colMask != null) Destroy(colMask); }
            var sink = _envelopeStack.GetComponent<KitScrollSink>() ?? _envelopeStack.gameObject.AddComponent<KitScrollSink>();
            sink.Panel = this;
            // the column needs a raycast target of its own so the wheel reaches it between / below rows
            if (_envelopeStack.GetComponent<Graphic>() == null) { var hit = _envelopeStack.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); }
            if (_kitFitted) return;
            var bg = Find(_panelGo.transform, "Background")?.GetComponent<RectTransform>();
            if (bg == null) return;
            var watch = bg.GetComponent<KitResizeWatcher>() ?? bg.gameObject.AddComponent<KitResizeWatcher>();   // live refit on resize
            watch.Panel = this;
            Canvas.ForceUpdateCanvases();
            var b = new Vector3[4]; var s = new Vector3[4];
            bg.GetWorldCorners(b); _envelopeStack.GetWorldCorners(s);
            float scale = _envelopeStack.lossyScale.y > 0f ? _envelopeStack.lossyScale.y : 1f;
            float wantBottom = b[0].y + KIT_MARGIN * scale;
            float delta = (wantBottom - s[0].y) / scale;   // >0: the stack's bottom sits below the wanted line
            // a stack anchored by its top: grow / shrink its height so the bottom lands on the line
            var sd = _envelopeStack.sizeDelta;
            if (Mathf.Abs(_envelopeStack.anchorMin.y - _envelopeStack.anchorMax.y) < 0.0001f)
            {
                // point anchors: height h -> h' = h - delta, top edge kept (top = pos + (1 - pivot) * h)
                float h = sd.y, h2 = Mathf.Max(80f, h - delta);
                _envelopeStack.sizeDelta = new Vector2(sd.x, h2);
                _envelopeStack.anchoredPosition += new Vector2(0f, (1f - _envelopeStack.pivot.y) * (h - h2));
            }
            else _envelopeStack.offsetMin = new Vector2(_envelopeStack.offsetMin.x, _envelopeStack.offsetMin.y + delta);
            // the column's right edge stops
            // 12 px left of the detail pane's left edge (left edge kept).
            var detail = Find(_panelGo.transform, "DetailPane")?.GetComponent<RectTransform>();
            if (detail != null)
            {
                Canvas.ForceUpdateCanvases();
                var dc = new Vector3[4]; detail.GetWorldCorners(dc); _envelopeStack.GetWorldCorners(s);
                float scaleX = _envelopeStack.lossyScale.x > 0f ? _envelopeStack.lossyScale.x : 1f;
                float over = (s[2].x - (dc[0].x - 12f * scaleX)) / scaleX;   // >0: the column reaches into the gap / pane
                if (over > 0f)
                {
                    if (Mathf.Abs(_envelopeStack.anchorMin.x - _envelopeStack.anchorMax.x) < 0.0001f)
                    {
                        var sz = _envelopeStack.sizeDelta;
                        float w2 = Mathf.Max(120f, sz.x - over);
                        _envelopeStack.anchoredPosition -= new Vector2(_envelopeStack.pivot.x * (sz.x - w2), 0f);   // left edge = pos - pivot * w, kept
                        _envelopeStack.sizeDelta = new Vector2(w2, sz.y);
                    }
                    else _envelopeStack.offsetMax = new Vector2(_envelopeStack.offsetMax.x - over, _envelopeStack.offsetMax.y);
                }
            }
            _kitFitted = true;
            Canvas.ForceUpdateCanvases();
            _envelopeStack.GetWorldCorners(s);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] kit inbox column fitted: bottom {s[0].y:0} (window bottom {b[0].y:0}, margin {KIT_MARGIN} px), height {_envelopeStack.rect.height:0}");
        }

        // Detail pane in the kit look: subject one line with ellipsis, body wraps and truncates inside its box, Claim All a kit button.
        private void KitDetail()
        {
            if (_detailSubject != null) { _detailSubject.enableWordWrapping = false; _detailSubject.overflowMode = TextOverflowModes.Ellipsis; }
            if (_detailFrom != null) { _detailFrom.enableWordWrapping = false; _detailFrom.overflowMode = TextOverflowModes.Ellipsis; }
            if (_detailBody != null) { _detailBody.enableWordWrapping = true; _detailBody.overflowMode = TextOverflowModes.Ellipsis; }
            if (_claimAllButton != null)
            {
                var img = _claimAllButton.GetComponent<Image>();
                if (img != null) { img.sprite = FiresCore.UI.Shell.Kit.KitSprites.RoundedFill(10f); img.type = Image.Type.Sliced; img.color = FiresCore.UI.Shell.Kit.FiresTokens.PanelRaised; }
                var lbl = _claimAllButton.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) { lbl.color = FiresCore.UI.Shell.Kit.FiresTokens.Gold; lbl.enableWordWrapping = false; lbl.text = "Claim All"; }
            }
        }

        /// <summary>Builds the static "Inbox" tab as the top envelope (index 0) of the stack. Shows
        /// only "Inbox" in its label strip, no sender/date, and has no Button — it's a pure header,
        /// not selectable. The first real mail (index 1) covers everything below this tab's strip.</summary>
        private void BuildHeaderTab()
        {
            var clone = Object.Instantiate(_envelopeTemplate, _envelopeStack);
            clone.name = "InboxHeaderTab";
            clone.SetActive(true);
            _envelopeClones.Add(clone);

            var rt = (RectTransform)clone.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);   // index 0 — top of the stack
            float w = rt.sizeDelta.x > 0 ? rt.sizeDelta.x : 180f;
            rt.sizeDelta = new Vector2(w, ENV_HEIGHT);

            var img = clone.GetComponent<Image>();
            if (img != null) img.color = Color.white;
            AddDepthShadow(clone);

            var subject = FindTmp(clone.transform, "SubjectText");
            var sender  = FindTmp(clone.transform, "SenderText");
            var date    = FindTmp(clone.transform, "DateText");
            if (subject != null) subject.text = "Inbox";
            if (sender  != null) sender.text  = "";
            if (date    != null) date.text    = "";

            // Pure header — strip the Button the template carries so it can't be clicked/highlighted.
            var btn = clone.GetComponent<Button>();
            if (btn != null) Destroy(btn);
        }

        /// <summary>Adds a contact shadow to an envelope so the fan reads as stacked layers instead of a
        /// flat sheet. The shadow is offset UPWARD a few px, so it peeks above this envelope's top edge
        /// and lands on the envelope behind-and-above it — exactly where two stacked papers meet — making
        /// each seam visibly darker. Soft (semi-transparent), follows the sprite alpha.</summary>
        private static void AddDepthShadow(GameObject clone)
        {
            if (clone.GetComponent<Image>() == null) return;
            var shadow = clone.GetComponent<Shadow>() ?? clone.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.5f, 5f);   // mostly up (seam), a touch right for body
            shadow.useGraphicAlpha = true;
        }

        private void BuildEnvelopeClone(MailEntry msg, int index, bool isLast)
        {
            var clone = Object.Instantiate(_envelopeTemplate, _envelopeStack);
            clone.name = "Mail_" + msg.Id;
            clone.SetActive(true);
            _envelopeClones.Add(clone);

            // Re-anchor cloned envelope to the top of EnvelopeStack and step down by ENV_STRIP per index.
            // Last envelope renders 25% taller so its proportions read like a real envelope at rest.
            var rt = (RectTransform)clone.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(index * ENV_STRIP));
            float w = rt.sizeDelta.x > 0 ? rt.sizeDelta.x : 180f;
            rt.sizeDelta = new Vector2(w, isLast ? ENV_LAST_HEIGHT : ENV_HEIGHT);

            // Selected envelope tints slightly warmer — others render at the sprite's native tones.
            var img = clone.GetComponent<Image>();
            if (img != null) img.color = (msg == _selected) ? new Color(1f, 0.92f, 0.78f) : Color.white;
            AddDepthShadow(clone);   // contact shadow at the seam so the fan reads as stacked layers

            var subject = FindTmp(clone.transform, "SubjectText");
            var sender  = FindTmp(clone.transform, "SenderText");
            var date    = FindTmp(clone.transform, "DateText");
            if (subject != null) subject.text = msg.Subject;
            if (sender  != null) sender.text  = msg.SenderName;
            if (date    != null) date.text    = msg.DateString;
            // dark ink on the envelope
            var ink = new Color(0.20f, 0.13f, 0.07f, 1f);
            if (subject != null) subject.color = ink;
            if (sender  != null) sender.color  = new Color(ink.r, ink.g, ink.b, 0.85f);
            if (date    != null) date.color    = new Color(ink.r, ink.g, ink.b, 0.75f);

            var btn = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => { SelectMessage(msg); Refresh(); });
        }

        private void SelectMessage(MailEntry msg)
        {
            _selected = msg;
            if (msg.Id > 0) MailNetwork.RequestMarkRead(msg.Id); // viewing a message reads it (server marks it)
            if (_detailSubject != null) _detailSubject.text = msg.Subject;
            if (_detailFrom    != null) _detailFrom.text    = "From: " + msg.SenderName;
            if (_detailDate    != null) _detailDate.text    = msg.DateString;
            if (_detailBody    != null) _detailBody.text    = msg.Body;
            ClearAttachments();
            foreach (var att in msg.Attachments) BuildAttachmentRow(att);
            // say where the rows went, so the next round names the cause
            if (msg.Attachments.Count > 0)
            {
                string where = _attachmentsContent == null ? "NO AttachmentsScroll/Viewport/Content node (rows not built)"
                    : $"content '{_attachmentsContent.name}' active {_attachmentsContent.gameObject.activeInHierarchy}, {_attachmentsContent.childCount} row(s), size {_attachmentsContent.rect.size}, viewport {(_attachmentsContent.parent as RectTransform)?.rect.size}";
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] '{msg.Subject}': {msg.Attachments.Count} attachment(s) -> {where}");
            }
            if (_statusText != null) _statusText.text = msg.Attachments.Count + " attachment(s)";
            if (msg.Attachments.Count > 0) LogAttachmentPlacement(msg.Subject);
        }

        // name where Content and each
        // row actually sit against the viewport after layout, plus what could hide them (mask, layout group, label font). Debug only.
        private void LogAttachmentPlacement(string subject)
        {
            try
            {
                Canvas.ForceUpdateCanvases();
                var vp = _attachmentsContent.parent as RectTransform;
                var v = new Vector3[4]; vp.GetWorldCorners(v);
                var sb = new System.Text.StringBuilder();
                sb.Append($"[MailPanel] '{subject}' placement: viewport world ({v[0].x:F0},{v[0].y:F0})-({v[2].x:F0},{v[2].y:F0}) mask {(vp.GetComponent<Mask>() != null ? "Mask" : vp.GetComponent<RectMask2D>() != null ? "RectMask2D" : "none")}");
                sb.Append($"; content anchors {_attachmentsContent.anchorMin}-{_attachmentsContent.anchorMax} pivot {_attachmentsContent.pivot} pos {_attachmentsContent.anchoredPosition} scale {_attachmentsContent.localScale}");
                foreach (var comp in _attachmentsContent.GetComponents<Component>()) sb.Append(" +" + comp.GetType().Name);
                for (int i = 0; i < _attachmentsContent.childCount; i++)
                {
                    var r = (RectTransform)_attachmentsContent.GetChild(i);
                    var k = new Vector3[4]; r.GetWorldCorners(k);
                    var lbl = r.GetComponentInChildren<TextMeshProUGUI>(true);
                    sb.Append($"; row {i} '{r.name}' active {r.gameObject.activeInHierarchy} world ({k[0].x:F0},{k[0].y:F0})-({k[2].x:F0},{k[2].y:F0}) font {(lbl != null && lbl.font != null ? lbl.font.name : "NULL")} alpha {(lbl != null ? lbl.color.a : -1f):F2}");
                }
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug(sb.ToString());
            }
            catch (System.Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] placement log failed: {ex.Message}"); }
        }

        // ── attachments ───────────────────────────────────────────────────────

        // rpg_mail4, 's placement line: 3 rows active, alpha 1, font set, world rects INSIDE the viewport's (1638,310)-
        // (2035,425) "mask Mask" - yet the box drew empty): a stencil Mask only clips to what its own Graphic draws; a viewport Image the
        // kit leaves transparent with cullTransparentMesh on writes no stencil and the Mask hides everything. Clip by rect instead
        // (RectMask2D needs no graphic). One Debug line names the old Mask's graphic so the round shows which it was.
        private static void ClipWithRect(Transform viewport)
        {
            try
            {
                var mask = viewport.GetComponent<Mask>();
                if (mask == null || viewport.GetComponent<RectMask2D>() != null) return;
                var g = mask.graphic;
                var cr = viewport.GetComponent<CanvasRenderer>();
                string was = g == null ? "no graphic" : $"graphic {g.GetType().Name} enabled {g.enabled}, alpha {g.color.a:0.00}, showMaskGraphic {mask.showMaskGraphic}, cullTransparentMesh {(cr != null && cr.cullTransparentMesh)}";
                UnityEngine.Object.DestroyImmediate(mask);
                viewport.gameObject.AddComponent<RectMask2D>();
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] attachments viewport: stencil Mask ({was}) replaced by RectMask2D");
            }
            catch (System.Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] attachments viewport clip swap failed: {ex.Message}"); }
        }

        private void ClearAttachments()
        {
            if (_attachmentsContent == null) return;
            // Destroy waits for the frame end, so the
            // old rows were still children when the new ones were placed by childCount - below the 74 px box - and the visible old
            // ones then vanished. Detach first (team rule: kit redraw detaches before Destroy).
            for (int i = _attachmentsContent.childCount - 1; i >= 0; i--)
            {
                var c = _attachmentsContent.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
            _attachmentsContent.sizeDelta = new Vector2(_attachmentsContent.sizeDelta.x, 0f);
        }

        private void BuildAttachmentRow(MailAttachment att)
        {
            if (_attachmentsContent == null) return;
            var rowGo = new GameObject("Att_" + att.Prefab, typeof(RectTransform), typeof(Image));
            rowGo.transform.SetParent(_attachmentsContent, false);
            var rt = (RectTransform)rowGo.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 24f);
            rt.anchoredPosition = new Vector2(0f, -(_attachmentsContent.childCount - 1) * 26f);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.65f);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(rowGo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-8f, 0f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.font = global::FiresRPGmaker.UI.NpcBookController.GameFont();   // a code-made TMP has no font -> invisible ("2 attachment(s)", no rows)
            tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.text = att.Prefab + "  x" + att.Count;
            tmp.fontSize = 12f; tmp.color = new Color(0.96f, 0.91f, 0.76f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Grow Content height so scrolling works.
            _attachmentsContent.sizeDelta = new Vector2(_attachmentsContent.sizeDelta.x, _attachmentsContent.childCount * 26f);
        }

        // ── actions ───────────────────────────────────────────────────────────

        // give the content the
        // same right margin as its left one inside Background, then pull any Inbox/Send child that still pokes past the content's
        // right edge back inside it. Done once per build, in each node's own parent space; one Debug line with the numbers.
        private static void FitInsideParchment(RectTransform bg, RectTransform content)
        {
            if (bg == null || content == null) return;
            try
            {
                Canvas.ForceUpdateCanvases();
                var b = new Vector3[4]; var c = new Vector3[4];
                bg.GetWorldCorners(b); content.GetWorldCorners(c);
                float scale = Mathf.Max(0.0001f, content.lossyScale.x);
                float leftGap = (c[0].x - b[0].x) / scale, rightGap = (b[2].x - c[2].x) / scale;
                float pull = leftGap - rightGap;
                if (pull > 0.5f) content.offsetMax -= new Vector2(pull, 0f);
                int clamped = 0;
                content.GetWorldCorners(c);
                foreach (RectTransform view in content)
                    foreach (RectTransform child in view)
                    {
                        var k = new Vector3[4]; child.GetWorldCorners(k);
                        float over = (k[2].x - c[2].x) / Mathf.Max(0.0001f, child.lossyScale.x);
                        if (over > 0.5f) { child.anchoredPosition -= new Vector2(over, 0f); clamped++; }
                    }
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[MailPanel] parchment margins: left {leftGap:F0}, right {rightGap:F0} -> pulled content in by {Mathf.Max(0f, pull):F0}; {clamped} child(ren) moved back inside");
            }
            catch (System.Exception ex) { Debug.LogWarning($"[MailPanel] fit failed: {ex.Message}"); }
        }

        private void OnClaimClicked()
        {
            if (_selected == null) { if (_statusText != null) _statusText.text = "Select a message first."; return; }
            if (_selected.Attachments.Count == 0) { if (_statusText != null) _statusText.text = "No attachments on this mail."; return; }
            // Server-authoritative: it flips AttachmentsTaken and replies with the items to grant, so the same
            // mail can never be claimed twice (the old local grant reset on reload and could be re-claimed).
            MailNetwork.RequestClaim(_selected.Id);
            if (_statusText != null) _statusText.text = "Claiming…";
        }

        private void OnSendClicked()
        {
            string to   = _sendTo?.text?.Trim() ?? "";
            string subj = _sendSubject?.text?.Trim() ?? "";
            string body = _sendMessage?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(to))   { if (_sendStatusText != null) _sendStatusText.text = "Enter a recipient name."; return; }
            if (string.IsNullOrEmpty(subj)) { if (_sendStatusText != null) _sendStatusText.text = "Enter a subject.";        return; }
            if (string.IsNullOrEmpty(body)) { if (_sendStatusText != null) _sendStatusText.text = "Enter a message body.";   return; }
            // Server resolves the recipient by name and stores the mail in their vault mailbox (cross-player,
            // persistent). The SendResult reply lands on OnMailResult with success/failure text.
            MailNetwork.RequestSend(to, subj, body);
            if (_sendStatusText != null) _sendStatusText.text = "Sending…";
            if (_sendTo      != null) _sendTo.text = "";
            if (_sendSubject != null) _sendSubject.text = "";
            if (_sendMessage != null) _sendMessage.text = "";
        }

        // ── tiny Find helpers ─────────────────────────────────────────────────

        private static GameObject Find(Transform root, string path)
        {
            var t = root.Find(path); return t != null ? t.gameObject : FindRecursive(root, GetLast(path));
        }
        private static TMP_Text       FindTmp  (Transform root, string path) { var go = Find(root, path); return go != null ? go.GetComponent<TMP_Text>() : null; }
        private static Button         FindBtn  (Transform root, string path) { var go = Find(root, path); return go != null ? go.GetComponent<Button>()  : null; }
        private static TMP_InputField FindInput(Transform root, string path) { var go = Find(root, path); return go != null ? go.GetComponent<TMP_InputField>() : null; }

        private static string GetLast(string path)
        {
            int slash = path.LastIndexOf('/'); return slash < 0 ? path : path.Substring(slash + 1);
        }
        private static GameObject FindRecursive(Transform root, string name)
        {
            if (root.name == name) return root.gameObject;
            for (int i = 0; i < root.childCount; i++)
            {
                var got = FindRecursive(root.GetChild(i), name);
                if (got != null) return got;
            }
            return null;
        }
    }
}
