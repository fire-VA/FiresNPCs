using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Mail (CMS) — port of KG's CentralizedMailSystem_UI flow:
    ///   • Tabs at top: Inbox / Send
    ///   • Inbox: scrollable list (left) + selected mail preview (right) with attachments + Claim All
    ///   • Send: form with To / Subject / Message + Send button. v1 stages the mail locally
    ///     (writes to the player's "drafts" pool — wiring cross-player RPC follows the LiteDB MailRepository
    ///     port, same shape as KG's RPC payload).
    /// </summary>
    public class MailPlayerPanel : BasePlayerModulePanel
    {
        private static MailPlayerPanel _instance;
        protected override string PanelTitle => "Mail";
        protected override Vector2 PanelSize => new Vector2(960f, 600f);

        private enum Tab { Inbox, Send }
        private Tab _tab = Tab.Inbox;

        private string _profileName;
        private MailEntry _selected;

        // Inbox tab widgets
        private GameObject _inboxView;
        private RectTransform _inboxContent;
        private TMP_Text _detailFrom, _detailSubject, _detailDate, _detailBody;
        private RectTransform _attachmentList;
        private TMP_Text _statusText;

        // Send tab widgets
        private GameObject _sendView;
        private TMP_InputField _sendTo, _sendSubject, _sendMessage;
        private TMP_Text _sendStatusText;

        private Button _tabInboxBtn, _tabSendBtn;

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

        protected override void BuildContent(RectTransform contentArea)
        {
            // Top tab strip
            _tabInboxBtn = MakeButton(contentArea, "Inbox",
                                      new Vector2(0f, 0.93f), new Vector2(0.18f, 1f),
                                      new Vector2(8f, 0f), new Vector2(-4f, 0f),
                                      bgColor: new Color(0.32f, 0.22f, 0.14f, 0.95f), fontSize: 14f);
            _tabInboxBtn.onClick.AddListener(() => { _tab = Tab.Inbox; Refresh(); });
            _tabSendBtn = MakeButton(contentArea, "Send",
                                     new Vector2(0.18f, 0.93f), new Vector2(0.36f, 1f),
                                     new Vector2(4f, 0f), new Vector2(-4f, 0f),
                                     bgColor: new Color(0.18f, 0.14f, 0.08f, 0.95f), fontSize: 14f);
            _tabSendBtn.onClick.AddListener(() => { _tab = Tab.Send; Refresh(); });

            BuildInboxView(contentArea);
            BuildSendView(contentArea);
        }

        private void BuildInboxView(RectTransform contentArea)
        {
            _inboxView = new GameObject("InboxView", typeof(RectTransform));
            _inboxView.transform.SetParent(contentArea, false);
            var ivrt = _inboxView.GetComponent<RectTransform>();
            ivrt.anchorMin = new Vector2(0f, 0f); ivrt.anchorMax = new Vector2(1f, 0.92f);
            ivrt.offsetMin = Vector2.zero; ivrt.offsetMax = Vector2.zero;

            MakeLabel(_inboxView.transform, "Inbox",
                      new Vector2(0f, 0.93f), new Vector2(0.36f, 1f),
                      new Vector2(8f, 0f), Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));
            _inboxContent = MakeVerticalScroll(_inboxView.transform,
                                               new Vector2(0f, 0.05f), new Vector2(0.36f, 0.92f),
                                               Vector2.zero, Vector2.zero,
                                               rowSpacing: 3f, scrollSensitivity: 100f);

            MakePanel(_inboxView.transform, "DetailPane", new Color(0.10f, 0.08f, 0.05f, 0.85f),
                      new Vector2(0.38f, 0.05f), new Vector2(1f, 0.92f), Vector2.zero, Vector2.zero);

            _detailSubject = MakeLabel(_inboxView.transform, "Select a message",
                                       new Vector2(0.40f, 0.84f), new Vector2(1f, 0.92f),
                                       Vector2.zero, new Vector2(-8f, 0f),
                                       size: 18f, color: new Color(1f, 0.85f, 0.55f));
            _detailFrom = MakeLabel(_inboxView.transform, "",
                                    new Vector2(0.40f, 0.77f), new Vector2(0.78f, 0.84f),
                                    Vector2.zero, Vector2.zero, size: 13f);
            _detailDate = MakeLabel(_inboxView.transform, "",
                                    new Vector2(0.78f, 0.77f), new Vector2(1f, 0.84f),
                                    Vector2.zero, new Vector2(-8f, 0f),
                                    size: 13f, align: TextAlignmentOptions.MidlineRight,
                                    color: new Color(0.85f, 0.78f, 0.62f));

            _detailBody = MakeLabel(_inboxView.transform, "",
                                    new Vector2(0.40f, 0.33f), new Vector2(1f, 0.76f),
                                    Vector2.zero, new Vector2(-8f, 0f),
                                    size: 13f, align: TextAlignmentOptions.TopLeft);

            MakeLabel(_inboxView.transform, "Attachments",
                      new Vector2(0.40f, 0.26f), new Vector2(1f, 0.33f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, color: new Color(1f, 0.85f, 0.55f));
            _attachmentList = MakeVerticalScroll(_inboxView.transform,
                                                 new Vector2(0.40f, 0.09f), new Vector2(0.85f, 0.26f),
                                                 Vector2.zero, Vector2.zero,
                                                 rowSpacing: 2f, scrollSensitivity: 80f);

            var claim = MakeButton(_inboxView.transform, "Claim All",
                                   new Vector2(0.86f, 0.09f), new Vector2(1f, 0.26f),
                                   Vector2.zero, new Vector2(-8f, 0f),
                                   bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 14f);
            claim.onClick.AddListener(OnClaimClicked);

            _statusText = MakeLabel(_inboxView.transform, "",
                                    new Vector2(0.40f, 0f), new Vector2(1f, 0.08f),
                                    Vector2.zero, new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        private void BuildSendView(RectTransform contentArea)
        {
            _sendView = new GameObject("SendView", typeof(RectTransform));
            _sendView.transform.SetParent(contentArea, false);
            var svrt = _sendView.GetComponent<RectTransform>();
            svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 0.92f);
            svrt.offsetMin = Vector2.zero; svrt.offsetMax = Vector2.zero;

            // To
            MakeLabel(_sendView.transform, "To (player name):",
                      new Vector2(0.05f, 0.84f), new Vector2(0.30f, 0.92f),
                      Vector2.zero, Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));
            _sendTo = BuildSendInput(_sendView.transform, "SendTo",
                                     new Vector2(0.30f, 0.84f), new Vector2(0.95f, 0.92f),
                                     singleLine: true);

            // Subject
            MakeLabel(_sendView.transform, "Subject:",
                      new Vector2(0.05f, 0.74f), new Vector2(0.30f, 0.82f),
                      Vector2.zero, Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));
            _sendSubject = BuildSendInput(_sendView.transform, "SendSubject",
                                          new Vector2(0.30f, 0.74f), new Vector2(0.95f, 0.82f),
                                          singleLine: true);

            // Message (multiline) — input top clamped to 0.64 so it stays BELOW the label's bottom
            // (0.66). The previous 0.72 made the input field occupy the same band as the "Message:"
            // label, which got eaten by the dark input box.
            MakeLabel(_sendView.transform, "Message:",
                      new Vector2(0.05f, 0.66f), new Vector2(0.30f, 0.72f),
                      Vector2.zero, Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));
            _sendMessage = BuildSendInput(_sendView.transform, "SendMessage",
                                          new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.64f),
                                          singleLine: false);

            // Send button
            var sendBtn = MakeButton(_sendView.transform, "Send",
                                     new Vector2(0.36f, 0.06f), new Vector2(0.64f, 0.16f),
                                     Vector2.zero, Vector2.zero,
                                     bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 16f);
            sendBtn.onClick.AddListener(OnSendClicked);

            _sendStatusText = MakeLabel(_sendView.transform, "",
                                        new Vector2(0.05f, 0f), new Vector2(0.95f, 0.06f),
                                        Vector2.zero, Vector2.zero,
                                        size: 12f, align: TextAlignmentOptions.Center,
                                        color: new Color(0.85f, 0.78f, 0.62f));
        }

        private TMP_InputField BuildSendInput(Transform parent, string name,
                                              Vector2 anchorMin, Vector2 anchorMax, bool singleLine)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(4f, 4f); rt.offsetMax = new Vector2(-4f, -4f);
            go.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.92f);

            var input = go.AddComponent<TMP_InputField>();
            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rt, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(8f, 4f); vrt.offsetMax = new Vector2(-8f, -4f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(vrt, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 14f; txt.color = new Color(0.96f, 0.91f, 0.76f);
            txt.alignment = singleLine ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.TopLeft;
            FiresCore.UI.UIBuilderHelper.ApplyValheimFont(txt);

            input.textViewport = vrt; input.textComponent = txt;
            input.lineType = singleLine ? TMP_InputField.LineType.SingleLine : TMP_InputField.LineType.MultiLineNewline;
            return input;
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selected = null;
            _tab = Tab.Inbox;
            Refresh();
        }

        private void Refresh()
        {
            HighlightTab(_tabInboxBtn, _tab == Tab.Inbox);
            HighlightTab(_tabSendBtn, _tab == Tab.Send);
            _inboxView.SetActive(_tab == Tab.Inbox);
            _sendView.SetActive(_tab == Tab.Send);
            if (_tab == Tab.Inbox) RefreshInbox();
        }

        private void HighlightTab(Button btn, bool active)
        {
            var img = btn?.GetComponent<Image>(); if (img == null) return;
            img.color = active ? new Color(0.32f, 0.22f, 0.14f, 0.95f) : new Color(0.18f, 0.14f, 0.08f, 0.95f);
        }

        // Envelope-stack constants — each mail is rendered as a full Envelope.png. Later siblings render
        // on top, covering all but the top STRIP of the prior envelope, so the inbox looks like a fanned
        // pile of letters. The LAST envelope is uncovered, so its full artwork (wax seal + stamps) shows.
        private const float ENV_WIDTH  = 180f;
        private const float ENV_HEIGHT = 200f;
        private const float ENV_STRIP  = 40f;   // Y-step between successive envelopes = visible label band

        private static Sprite _envelopeSprite;

        /// <summary>Loads <c>Envelope.png</c> once and caches the Sprite. Same embedded-resource + reflection
        /// path the trader book uses (UnityEngine.ImageConversion targets netstandard 2.1, our project is 2.0).
        /// Returns null on missing/corrupt PNG — call-site falls back to a flat-color envelope rect.</summary>
        private static Sprite LoadEnvelopeSprite()
        {
            if (_envelopeSprite != null) return _envelopeSprite;
            try
            {
                byte[] data = null;
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                try
                {
                    using (var stream = asm.GetManifestResourceStream("FiresRPGmaker.Resources.Envelope.png"))
                    {
                        if (stream != null)
                        {
                            data = new byte[stream.Length];
                            int off = 0, read;
                            while ((read = stream.Read(data, off, data.Length - off)) > 0) off += read;
                        }
                    }
                }
                catch { /* fall through to disk fallback */ }

                if (data == null)
                {
                    string asmPath = asm.Location;
                    string pluginDir = System.IO.Path.GetDirectoryName(asmPath);
                    string pngPath = System.IO.Path.Combine(pluginDir, "Envelope.png");
                    if (!System.IO.File.Exists(pngPath))
                    {
                        Debug.LogWarning("[MailPlayerPanel] Envelope.png not embedded AND missing on disk at " + pngPath);
                        return null;
                    }
                    data = System.IO.File.ReadAllBytes(pngPath);
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                bool loaded = false;
                var imgConv = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imgConv != null)
                {
                    var mi = imgConv.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (mi != null) loaded = (bool)mi.Invoke(null, new object[] { tex, data });
                }
                if (!loaded)
                {
                    Debug.LogWarning("[MailPlayerPanel] Envelope.png decode failed");
                    return null;
                }
                _envelopeSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (System.Exception ex) { Debug.LogError("[MailPlayerPanel] LoadEnvelopeSprite: " + ex.Message); }
            return _envelopeSprite;
        }

        private void RefreshInbox()
        {
            for (int i = _inboxContent.childCount - 1; i >= 0; i--)
                Destroy(_inboxContent.GetChild(i).gameObject);

            var inbox = MailProfileManager.GetMail(_profileName);
            if (inbox.Count == 0)
            {
                _detailSubject.text = "Inbox is empty.";
                _detailFrom.text = ""; _detailDate.text = "";
                _detailBody.text = "Drop a .cfg into BepInEx\\config\\firesnpcs\\mail\\ to seed sample messages.";
                ClearAttachments();
                _statusText.text = "";
                _inboxContent.sizeDelta = new Vector2(_inboxContent.sizeDelta.x, 0f);
                return;
            }

            // Build envelopes in inbox order — Unity renders later siblings on TOP, which is exactly the
            // fanned-stack we want (envelope[i+1] covers envelope[i]'s body, leaving only envelope[i]'s
            // top strip visible). The last envelope has nothing above it, so it shows full size.
            int n = inbox.Count;
            for (int i = 0; i < n; i++) BuildEnvelopeInStack(inbox[i], i);

            // Scroll content height = (N-1) strips + one full envelope at the bottom + a little breathing room.
            float stackHeight = (n - 1) * ENV_STRIP + ENV_HEIGHT + 12f;
            _inboxContent.sizeDelta = new Vector2(_inboxContent.sizeDelta.x, stackHeight);

            if (_selected == null) SelectMessage(inbox[0]);
            else SelectMessage(_selected);
        }

        private void BuildEnvelopeInStack(MailEntry msg, int index)
        {
            var go = new GameObject("Mail_" + msg.Id, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_inboxContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(index * ENV_STRIP));
            rt.sizeDelta = new Vector2(ENV_WIDTH, ENV_HEIGHT);

            var img = go.GetComponent<Image>();
            var sprite = LoadEnvelopeSprite();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.preserveAspect = false;
                // Selected envelope = warmer cream; others render at the PNG's native tones.
                img.color = (msg == _selected) ? new Color(1f, 0.92f, 0.78f) : Color.white;
            }
            else
            {
                img.color = (msg == _selected)
                    ? new Color(0.40f, 0.30f, 0.18f, 0.95f)
                    : new Color(0.20f, 0.15f, 0.08f, 0.90f);
            }
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { SelectMessage(msg); Refresh(); });

            // Label band — the top ENV_STRIP px of the envelope (only portion visible when overlapped).
            // Strip occupies normalised Y [stripBotFrac, 1.0] of the envelope rect.
            float stripBotFrac = 1f - (ENV_STRIP / ENV_HEIGHT);
            float stripMidFrac = stripBotFrac + (1f - stripBotFrac) * 0.55f;

            MakeLabel(go.transform, msg.Subject,
                      new Vector2(0.06f, stripMidFrac), new Vector2(0.94f, 0.98f),
                      Vector2.zero, Vector2.zero,
                      size: 12f, color: new Color(0.15f, 0.10f, 0.04f));
            MakeLabel(go.transform, msg.SenderName,
                      new Vector2(0.06f, stripBotFrac + 0.02f), new Vector2(0.60f, stripMidFrac),
                      Vector2.zero, Vector2.zero,
                      size: 10f, color: new Color(0.32f, 0.20f, 0.10f));
            MakeLabel(go.transform, msg.DateString,
                      new Vector2(0.60f, stripBotFrac + 0.02f), new Vector2(0.94f, stripMidFrac),
                      Vector2.zero, Vector2.zero,
                      size: 10f, align: TextAlignmentOptions.MidlineRight,
                      color: new Color(0.32f, 0.20f, 0.10f));
        }

        private void SelectMessage(MailEntry msg)
        {
            _selected = msg;
            _detailSubject.text = msg.Subject;
            _detailFrom.text = "From: " + msg.SenderName;
            _detailDate.text = msg.DateString;
            _detailBody.text = msg.Body;
            ClearAttachments();
            foreach (var att in msg.Attachments) BuildAttachmentRow(att);
            ReflowScroll(_attachmentList);
            _statusText.text = msg.Attachments.Count + " attachment(s)";
        }

        private void ClearAttachments()
        {
            for (int i = _attachmentList.childCount - 1; i >= 0; i--)
                Destroy(_attachmentList.GetChild(i).gameObject);
        }

        private void BuildAttachmentRow(MailAttachment att)
        {
            var rowGo = new GameObject("Att_" + att.Prefab, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_attachmentList, false);
            rowGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.05f, 0.85f);
            SetRowHeight(rowGo, 24f);
            MakeLabel(rowGo.transform, att.Prefab + "  x" + att.Count,
                      new Vector2(0f, 0f), new Vector2(1f, 1f),
                      new Vector2(8f, 0f), new Vector2(-8f, 0f),
                      size: 12f);
        }

        private void OnClaimClicked()
        {
            if (_selected == null) { _statusText.text = "Select a message first."; return; }
            if (_selected.Attachments.Count == 0) { _statusText.text = "No attachments on this mail."; return; }
            var player = Player.m_localPlayer; if (player == null) return;
            var inv = player.GetInventory();
            int granted = 0;
            foreach (var att in _selected.Attachments)
            {
                var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(att.Prefab) : null;
                var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                if (inv.CanAddItem(drop.m_itemData, att.Count))
                    inv.AddItem(att.Prefab, att.Count, 1, 0, 0L, "");
                else
                {
                    var go = Object.Instantiate(pf, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
                    var dr = go.GetComponent<ItemDrop>(); if (dr != null) dr.m_itemData.m_stack = att.Count;
                }
                granted++;
            }
            _selected.Attachments.Clear();
            ClearAttachments();
            _statusText.text = "Claimed " + granted + " attachment(s).";
        }

        private void OnSendClicked()
        {
            string to = _sendTo?.text?.Trim() ?? "";
            string subj = _sendSubject?.text?.Trim() ?? "";
            string body = _sendMessage?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(to))    { _sendStatusText.text = "Enter a recipient name."; return; }
            if (string.IsNullOrEmpty(subj))  { _sendStatusText.text = "Enter a subject.";        return; }
            if (string.IsNullOrEmpty(body))  { _sendStatusText.text = "Enter a message body.";   return; }
            // v1: stage the mail under the recipient's profile (acts like a local-only inbox).
            // Wires to the LiteDB MailRepository RPC in a follow-up — same payload shape as KG's CMS.
            MailProfileManager.AppendLocalMail(to.ToLowerInvariant(), new MailEntry
            {
                Id = MailProfileManager.NextLocalId(),
                SenderName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?",
                DateString = "just now",
                Subject = subj,
                Body = body,
                Attachments = new List<MailAttachment>(),
            });
            _sendStatusText.text = "Sent to '" + to + "' (local-only until cross-player wiring lands).";
            _sendTo.text = ""; _sendSubject.text = ""; _sendMessage.text = "";
        }
    }
}
