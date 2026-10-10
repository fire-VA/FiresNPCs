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
        public static bool AnyOpen => _openCount > 0;
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

            _panelGo = Object.Instantiate(prefab, _canvasGo.transform);
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
            BasePlayerModulePanel.FixCloseGlyph(_closeButton);   // 0.0.564
            _tabInboxBtn    = FindBtn(root, "Background/TabInbox");
            _tabSendBtn     = FindBtn(root, "Background/TabSend");

            _inboxView      = Find(root, "Background/ContentArea/InboxView");
            _sendView       = Find(root, "Background/ContentArea/SendView");

            // EnvelopeStack is parented to Background (sits BESIDE the parchment, not inside it).
            _envelopeStack    = Find(root, "Background/EnvelopeStack")?.GetComponent<RectTransform>();
            _envelopeTemplate = Find(root, "Background/EnvelopeStack/EnvelopeTemplate");
            if (_envelopeTemplate != null) _envelopeTemplate.SetActive(false);

            FitInsideParchment(Find(root, "Background")?.GetComponent<RectTransform>(), Find(root, "Background/ContentArea")?.GetComponent<RectTransform>());
            // 0.0.557 (Fire: the right-hand detail pane still touched the border): move it 20 px left.
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

        private void RefreshInbox()
        {
            // Tear down previous clones (NOT the hidden template itself).
            for (int i = _envelopeClones.Count - 1; i >= 0; i--)
            {
                if (_envelopeClones[i] != null) Destroy(_envelopeClones[i]);
            }
            _envelopeClones.Clear();

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
            if (_statusText != null) _statusText.text = msg.Attachments.Count + " attachment(s)";
        }

        // ── attachments ───────────────────────────────────────────────────────

        private void ClearAttachments()
        {
            if (_attachmentsContent == null) return;
            for (int i = _attachmentsContent.childCount - 1; i >= 0; i--)
                Destroy(_attachmentsContent.GetChild(i).gameObject);
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
            tmp.text = att.Prefab + "  x" + att.Count;
            tmp.fontSize = 12f; tmp.color = new Color(0.96f, 0.91f, 0.76f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Grow Content height so scrolling works.
            _attachmentsContent.sizeDelta = new Vector2(_attachmentsContent.sizeDelta.x, _attachmentsContent.childCount * 26f);
        }

        // ── actions ───────────────────────────────────────────────────────────

        // 0.0.548 (Fire, stream 10:20: the detail pane + Claim All ran into the parchment's right border): give the content the
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
