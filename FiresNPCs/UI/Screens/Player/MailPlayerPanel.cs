using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Player-facing Mail / CMS UI: left-pane inbox list, right-pane selected message + attachments.
    /// Matches KG's CMS shape — sender, subject, body, dated attachments (prefab+count). v1 reads sample
    /// inbox entries from <see cref="MailProfileManager"/>; cross-player send/claim wiring + LiteDB mailbox
    /// persistence ship in a follow-up.
    /// </summary>
    public class MailPlayerPanel : BasePlayerModulePanel
    {
        private static MailPlayerPanel _instance;
        protected override string PanelTitle => "Mail";
        protected override Vector2 PanelSize => new Vector2(900f, 560f);

        private string _profileName;
        private RectTransform _inboxContent;
        private TMP_Text _detailFrom;
        private TMP_Text _detailSubject;
        private TMP_Text _detailDate;
        private TMP_Text _detailBody;
        private RectTransform _attachmentList;
        private TMP_Text _statusText;
        private MailEntry _selected;

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
            // Inbox list (left 38%)
            MakeLabel(contentArea, "Inbox",
                      new Vector2(0f, 0.94f), new Vector2(0.36f, 1f),
                      new Vector2(8f, 0f), Vector2.zero,
                      size: 14f, color: new Color(1f, 0.85f, 0.55f));
            _inboxContent = MakeVerticalScroll(contentArea,
                                               new Vector2(0f, 0.05f), new Vector2(0.36f, 0.93f),
                                               Vector2.zero, Vector2.zero,
                                               rowSpacing: 3f, scrollSensitivity: 100f);

            // Detail pane (right 62%)
            MakePanel(contentArea, "DetailPane", new Color(0.10f, 0.08f, 0.05f, 0.85f),
                      new Vector2(0.38f, 0.05f), new Vector2(1f, 0.93f), Vector2.zero, Vector2.zero);

            _detailSubject = MakeLabel(contentArea, "Select a message",
                                       new Vector2(0.40f, 0.85f), new Vector2(1f, 0.93f),
                                       new Vector2(0f, 0f), new Vector2(-8f, 0f),
                                       size: 18f, color: new Color(1f, 0.85f, 0.55f));
            _detailFrom = MakeLabel(contentArea, "",
                                    new Vector2(0.40f, 0.78f), new Vector2(0.78f, 0.85f),
                                    Vector2.zero, Vector2.zero, size: 13f);
            _detailDate = MakeLabel(contentArea, "",
                                    new Vector2(0.78f, 0.78f), new Vector2(1f, 0.85f),
                                    new Vector2(0f, 0f), new Vector2(-8f, 0f),
                                    size: 13f, align: TextAlignmentOptions.MidlineRight,
                                    color: new Color(0.85f, 0.78f, 0.62f));

            _detailBody = MakeLabel(contentArea, "",
                                    new Vector2(0.40f, 0.34f), new Vector2(1f, 0.77f),
                                    new Vector2(0f, 0f), new Vector2(-8f, 0f),
                                    size: 13f, align: TextAlignmentOptions.TopLeft);

            // Attachments label + list (bottom of detail pane)
            MakeLabel(contentArea, "Attachments",
                      new Vector2(0.40f, 0.27f), new Vector2(1f, 0.34f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, color: new Color(1f, 0.85f, 0.55f));
            _attachmentList = MakeVerticalScroll(contentArea,
                                                 new Vector2(0.40f, 0.10f), new Vector2(0.85f, 0.27f),
                                                 Vector2.zero, Vector2.zero,
                                                 rowSpacing: 2f, scrollSensitivity: 80f);

            // Claim button
            var claim = MakeButton(contentArea, "Claim All",
                                   new Vector2(0.86f, 0.10f), new Vector2(1f, 0.27f),
                                   new Vector2(0f, 0f), new Vector2(-8f, 0f),
                                   bgColor: new Color(0.22f, 0.42f, 0.22f, 0.95f), fontSize: 14f);
            claim.onClick.AddListener(OnClaimClicked);

            _statusText = MakeLabel(contentArea, "",
                                    new Vector2(0.40f, 0.00f), new Vector2(1f, 0.09f),
                                    new Vector2(0f, 0f), new Vector2(-8f, 0f),
                                    size: 12f, color: new Color(0.85f, 0.78f, 0.62f));
        }

        protected override void OnOpen(string profileName, string npcDisplayName)
        {
            _profileName = profileName;
            _selected = null;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = _inboxContent.childCount - 1; i >= 0; i--)
                Destroy(_inboxContent.GetChild(i).gameObject);

            var inbox = MailProfileManager.GetMail(_profileName);
            if (inbox.Count == 0)
            {
                _detailSubject.text = "Inbox is empty.";
                _detailFrom.text = ""; _detailDate.text = "";
                _detailBody.text = "Drop a .cfg into config\\firesnpcs\\mail\\ to seed sample messages.";
                ClearAttachments();
                _statusText.text = "";
                return;
            }
            foreach (var msg in inbox) BuildInboxRow(msg);
            if (_selected == null) SelectMessage(inbox[0]);
        }

        private void BuildInboxRow(MailEntry msg)
        {
            var rowGo = new GameObject("Mail_" + msg.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            rowGo.transform.SetParent(_inboxContent, false);
            rowGo.GetComponent<Image>().color = msg == _selected
                ? new Color(0.32f, 0.22f, 0.10f, 0.95f)
                : new Color(0.12f, 0.10f, 0.06f, 0.85f);
            SetRowHeight(rowGo, 48f);
            rowGo.GetComponent<Button>().onClick.AddListener(() => { SelectMessage(msg); Refresh(); });

            MakeLabel(rowGo.transform, msg.Subject,
                      new Vector2(0.02f, 0.50f), new Vector2(0.98f, 1f),
                      Vector2.zero, Vector2.zero,
                      size: 13f, color: new Color(0.96f, 0.91f, 0.76f));
            MakeLabel(rowGo.transform, "From: " + msg.SenderName,
                      new Vector2(0.02f, 0f), new Vector2(0.65f, 0.50f),
                      Vector2.zero, Vector2.zero,
                      size: 11f, color: new Color(0.85f, 0.78f, 0.62f));
            MakeLabel(rowGo.transform, msg.DateString,
                      new Vector2(0.65f, 0f), new Vector2(0.98f, 0.50f),
                      Vector2.zero, Vector2.zero,
                      size: 11f, align: TextAlignmentOptions.MidlineRight,
                      color: new Color(0.85f, 0.78f, 0.62f));
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
            _statusText.text = "Claimed " + _selected.Attachments.Count + " attachment(s) (server flow not wired yet).";
        }
    }
}
