using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Server-authoritative mail over Core's LiteDB <see cref="MailRepository"/> (per-world vault).
    ///
    /// A mailbox is keyed by the recipient's persistent m_playerID (string Owner). Cross-player send resolves
    /// a recipient NAME → id through the vault's Users registry, which we upsert from every request wrapper —
    /// so anyone who has opened their mail at least once is addressable, online or not (MP's CMS_User model).
    /// Profile-templated mail (admin .cfg) is delivered into the viewer's real inbox once per (sender,topic,body)
    /// signature, turning the old read-only sample display into persistent, claimable, once-only mail.
    /// Claims are server-authoritative (AttachmentsTaken flips on the server) so items can't be double-claimed.
    /// </summary>
    public static class MailNetwork
    {
        public const string LogPrefix = "[MailNet]";

        private const string RpcReq = "FiresRPGmaker_MailReq";
        private const string RpcRes = "FiresRPGmaker_MailRes";

        private enum Op  : byte { GetInbox = 1, Send = 2, Claim = 3, MarkRead = 4 }
        private enum Res : byte { Inbox = 1, ClaimGrant = 2, SendResult = 3 }

        // Client-side hooks. The open MailPlayerPanel subscribes.
        public static Action OnInbox;            // fired after the client inbox cache is refreshed
        public static Action<string> OnResult;   // status line (send result / claim result)

        // Client inbox cache (local player only), newest first, filled by Inbox replies.
        public static readonly List<MailEntry> ClientInbox = new List<MailEntry>();
        public static int ClientUnread;

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;

        public static void Initialize()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_registered && _registeredOn == current) return;

            current.Register<ZPackage>(RpcReq, new Action<long, ZPackage>(RPC_MailReq));
            current.Register<ZPackage>(RpcRes, new Action<long, ZPackage>(RPC_MailRes));
            _registered   = true;
            _registeredOn = current;
            ClientInbox.Clear();
            ClientUnread = 0;
            FiresLogger.LogVerbose($"{LogPrefix} Registered on ZRoutedRpc {current.GetHashCode()}.");
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── Client → server sends ──────────────────────────────────────────────────────────────────────────
        public static void RequestInbox(string profile)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.GetInbox);
            inner.Write(profile ?? "");
            Send(inner);
        }

        public static void RequestSend(string toName, string subject, string body)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.Send);
            inner.Write(toName ?? "");
            inner.Write(subject ?? "");
            inner.Write(body ?? "");
            Send(inner);
        }

        public static void RequestClaim(int mailId)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.Claim);
            inner.Write(mailId);
            Send(inner);
        }

        public static void RequestMarkRead(int mailId)
        {
            var inner = new ZPackage();
            inner.Write((byte)Op.MarkRead);
            inner.Write(mailId);
            Send(inner);
        }

        private static void Send(ZPackage inner)
        {
            if (ZRoutedRpc.instance == null) return;
            var local = Player.m_localPlayer;
            long playerId = local != null ? local.GetPlayerID() : 0L;
            if (playerId == 0L) return;

            var wrapped = new ZPackage();
            wrapped.Write(playerId);
            wrapped.Write(local != null ? (local.GetPlayerName() ?? "") : "");
            wrapped.Write(inner.GetArray());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcReq, wrapped);
        }

        // ── Server handler ─────────────────────────────────────────────────────────────────────────────────
        private static void RPC_MailReq(long senderId, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            // Same guarantee as the banker: the shared Core vault must be configured before any mail read/write,
            // even if an earlier server-init step threw before VaultOfKnowledge configured it (idempotent no-op).
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.EnsureVaultConfigured();
            try
            {
                long playerId    = pkg.ReadLong();
                string playerName = pkg.ReadString();
                var inner = new ZPackage(pkg.ReadByteArray());
                // kg-parity owner key: platform user id, server-derived from the sender's socket
                // (account-scoped like kg — a migrated DB.db resolves without remapping; unspoofable).
                string ownerId = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(senderId, playerId);

                // Keep the name→id registry current so this player is addressable by name for offline delivery.
                UpsertUser(ownerId, playerName);

                var op = (Op)inner.ReadByte();
                switch (op)
                {
                    case Op.GetInbox:
                    {
                        string profile = inner.ReadString();
                        SeedProfileMail(ownerId, playerName, profile);
                        ReplyInbox(senderId, ownerId);
                        break;
                    }
                    case Op.Send:
                    {
                        string toName  = inner.ReadString();
                        string subject = inner.ReadString();
                        string body    = inner.ReadString();
                        ServerSend(senderId, playerName, toName, subject, body);
                        break;
                    }
                    case Op.Claim:
                        ServerClaim(senderId, ownerId, inner.ReadInt());
                        break;
                    case Op.MarkRead:
                        MailRepository.MarkRead(ownerId, inner.ReadInt());
                        break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_MailReq error: {ex.Message}"); }
        }

        private static void UpsertUser(string ownerId, string playerName)
        {
            try
            {
                MailRepository.UpsertUser(new MailUser
                {
                    Name = playerName ?? "",
                    UserID = ownerId,
                    LastOnline = (long)(ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0),
                    IsOnline = true,
                });
            }
            catch { }
        }

        private static void ServerSend(long senderId, string senderName, string toName, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(toName) || string.IsNullOrWhiteSpace(subject))
            { ReplySendResult(senderId, false, "Recipient and subject are required."); return; }

            // kg CMS admin broadcast parity: recipient "*" (or "all") fans the mail out to EVERY player the
            // vault has ever registered (CentralizedMailSystem_Server loops its Users the same way). Admin-only.
            string to = toName.Trim();
            if (to == "*" || to.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (!global::FiresCore.Sync.AdminSyncing.IsAdmin(senderId))
                { ReplySendResult(senderId, false, "Only admins can broadcast mail."); return; }
                int sent = 0;
                foreach (var u in MailRepository.GetAllUsers())
                {
                    if (u == null || string.IsNullOrEmpty(u.UserID)) continue;
                    MailRepository.AddMail(new FiresCore.Storage.MailEntry
                    {
                        Owner = u.UserID,
                        Sender = senderName ?? "",
                        Topic = subject.Trim(),
                        Message = (body ?? "").Trim(),
                        Created = DateTime.UtcNow.Ticks,
                        Attachments = new List<StoredItem>(),
                        Links = new List<StoredItem>(),
                        WasRead = false,
                        AttachmentsTaken = false,
                    });
                    sent++;
                }
                ReplySendResult(senderId, true, $"Broadcast to {sent} player(s).");
                return;
            }

            string toOwner = ResolveOwnerByName(toName);
            if (string.IsNullOrEmpty(toOwner))
            { ReplySendResult(senderId, false, $"Unknown recipient '{toName}' (they must have opened their mail at least once)."); return; }

            MailRepository.AddMail(new FiresCore.Storage.MailEntry
            {
                Owner = toOwner,
                Sender = senderName ?? "",
                Topic = subject.Trim(),
                Message = (body ?? "").Trim(),
                Created = DateTime.UtcNow.Ticks,
                Attachments = new List<StoredItem>(),
                Links = new List<StoredItem>(),
                WasRead = false,
                AttachmentsTaken = false,
            });
            ReplySendResult(senderId, true, $"Sent to '{toName}'.");
        }

        private static void ServerClaim(long senderId, string ownerId, int mailId)
        {
            var mail = MailRepository.GetMail(ownerId, mailId);
            if (mail == null) { ReplyClaimGrant(senderId, mailId, null); return; }
            if (mail.AttachmentsTaken || mail.Attachments == null || mail.Attachments.Count == 0)
            { ReplyClaimGrant(senderId, mailId, null); return; }

            var grant = mail.Attachments.ToList();
            MailRepository.MarkAttachmentsTaken(ownerId, mailId);
            ReplyClaimGrant(senderId, mailId, grant);
        }

        // Deliver an admin profile's templated messages into this player's real inbox once, deduped by the
        // (sender, topic, body) signature so reopening the same mail NPC never stacks duplicates.
        private static void SeedProfileMail(string ownerId, string playerName, string profile)
        {
            if (string.IsNullOrEmpty(profile)) return;
            var templates = MailProfileManager.GetMail(profile);
            if (templates == null || templates.Count == 0) return;

            var existing = MailRepository.GetUserMails(ownerId);
            foreach (var t in templates)
            {
                bool already = existing.Any(m =>
                    string.Equals(m.Sender, t.SenderName, StringComparison.Ordinal) &&
                    string.Equals(m.Topic, t.Subject, StringComparison.Ordinal) &&
                    string.Equals(m.Message, t.Body, StringComparison.Ordinal));
                if (already) continue;

                MailRepository.AddMail(new FiresCore.Storage.MailEntry
                {
                    Owner = ownerId,
                    Sender = t.SenderName ?? "",
                    Topic = t.Subject ?? "",
                    Message = t.Body ?? "",
                    Created = DateTime.UtcNow.Ticks,
                    Attachments = (t.Attachments ?? new List<MailAttachment>())
                        .Select(a => new StoredItem { ItemPrefab = a.Prefab, Count = a.Count }).ToList(),
                    Links = new List<StoredItem>(),
                    WasRead = false,
                    AttachmentsTaken = false,
                });
            }
        }

        private static string ResolveOwnerByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string want = name.Trim();
            var users = MailRepository.GetAllUsers();
            var hit = users.FirstOrDefault(u => u.Name != null && u.Name.Trim().Equals(want, StringComparison.OrdinalIgnoreCase));
            return hit?.UserID;
        }

        /// <summary>Public server-side entry for system mail (e.g. marketplace seller payout in E3). Delivers
        /// one mail with optional item attachments to a recipient resolved by name; returns false if unknown.</summary>
        public static bool DeliverSystemMail(string toName, string sender, string subject, string body, List<StoredItem> attachments)
        {
            if (!IsServer) return false;
            string toOwner = ResolveOwnerByName(toName);
            if (string.IsNullOrEmpty(toOwner)) return false;
            MailRepository.AddMail(new FiresCore.Storage.MailEntry
            {
                Owner = toOwner,
                Sender = sender ?? "System",
                Topic = subject ?? "",
                Message = body ?? "",
                Created = DateTime.UtcNow.Ticks,
                Attachments = attachments ?? new List<StoredItem>(),
                Links = new List<StoredItem>(),
                WasRead = false,
                AttachmentsTaken = false,
            });
            return true;
        }

        // ── Server → client replies ────────────────────────────────────────────────────────────────────────
        private static void ReplyInbox(long senderId, string ownerId)
        {
            var mails = MailRepository.GetUserMails(ownerId)
                .OrderByDescending(m => m.Created).ToList();
            int unread = mails.Count(m => !m.WasRead);

            var pkg = new ZPackage();
            pkg.Write((byte)Res.Inbox);
            pkg.Write(unread);
            pkg.Write(mails.Count);
            foreach (var m in mails)
            {
                pkg.Write(m._id);
                pkg.Write(m.Sender ?? "");
                pkg.Write(m.Created);
                pkg.Write(m.Topic ?? "");
                pkg.Write(m.Message ?? "");
                pkg.Write(m.WasRead);
                pkg.Write(m.AttachmentsTaken);
                var atts = m.AttachmentsTaken || m.Attachments == null ? new List<StoredItem>() : m.Attachments;
                pkg.Write(atts.Count);
                foreach (var a in atts) { pkg.Write(a.ItemPrefab ?? ""); pkg.Write(a.Count); }
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        private static void ReplyClaimGrant(long senderId, int mailId, List<StoredItem> grant)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.ClaimGrant);
            pkg.Write(mailId);
            var list = grant ?? new List<StoredItem>();
            pkg.Write(list.Count);
            foreach (var a in list) { pkg.Write(a.ItemPrefab ?? ""); pkg.Write(a.Count); }
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        private static void ReplySendResult(long senderId, bool ok, string status)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.SendResult);
            pkg.Write(ok);
            pkg.Write(status ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        // ── Client handler ─────────────────────────────────────────────────────────────────────────────────
        private static void RPC_MailRes(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var res = (Res)pkg.ReadByte();
                switch (res)
                {
                    case Res.Inbox:
                    {
                        ClientUnread = pkg.ReadInt();
                        int count = pkg.ReadInt();
                        ClientInbox.Clear();
                        for (int i = 0; i < count; i++)
                        {
                            int id = pkg.ReadInt();
                            string sender = pkg.ReadString();
                            long created = pkg.ReadLong();
                            string topic = pkg.ReadString();
                            string message = pkg.ReadString();
                            bool wasRead = pkg.ReadBool();
                            bool taken = pkg.ReadBool();
                            int nAtt = pkg.ReadInt();
                            var atts = new List<MailAttachment>();
                            for (int a = 0; a < nAtt; a++)
                            {
                                string prefab = pkg.ReadString();
                                int cnt = pkg.ReadInt();
                                atts.Add(new MailAttachment { Prefab = prefab, Count = cnt });
                            }
                            ClientInbox.Add(new MailEntry
                            {
                                Id = id,
                                SenderName = sender,
                                DateString = FormatDate(created),
                                Subject = topic,
                                Body = message,
                                Attachments = atts,
                            });
                        }
                        OnInbox?.Invoke();
                        break;
                    }
                    case Res.ClaimGrant:
                    {
                        int mailId = pkg.ReadInt();
                        int nAtt = pkg.ReadInt();
                        var grant = new List<MailAttachment>();
                        for (int a = 0; a < nAtt; a++)
                        {
                            string prefab = pkg.ReadString();
                            int cnt = pkg.ReadInt();
                            grant.Add(new MailAttachment { Prefab = prefab, Count = cnt });
                        }
                        int granted = GrantToInventory(grant);
                        // Reflect the claim locally so the panel clears the attachment list without a round-trip.
                        var m = ClientInbox.FirstOrDefault(e => e.Id == mailId);
                        if (m != null) m.Attachments.Clear();
                        OnResult?.Invoke(granted > 0 ? $"Claimed {granted} attachment(s)." :
                            nAtt == 0 ? "Nothing to claim." : "No room — items dropped at your feet.");
                        OnInbox?.Invoke();
                        break;
                    }
                    case Res.SendResult:
                    {
                        bool ok = pkg.ReadBool();
                        string status = pkg.ReadString();
                        OnResult?.Invoke(status);
                        break;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_MailRes error: {ex.Message}"); }
        }

        private static int GrantToInventory(List<MailAttachment> grant)
        {
            var player = Player.m_localPlayer;
            if (player == null || grant == null) return 0;
            var inv = player.GetInventory();
            int granted = 0;
            foreach (var att in grant)
            {
                var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(att.Prefab) : null;
                var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                if (inv.CanAddItem(drop.m_itemData, att.Count))
                    inv.AddItem(att.Prefab, att.Count, 1, 0, 0L, "", false);
                else
                    ItemDrop.DropItem(drop.m_itemData, att.Count, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
                granted++;
            }
            return granted;
        }

        private static string FormatDate(long createdTicks)
        {
            if (createdTicks <= 0) return "recently";
            try { return new DateTime(createdTicks, DateTimeKind.Utc).ToLocalTime().ToString("MMM d, HH:mm"); }
            catch { return "recently"; }
        }
    }
}
