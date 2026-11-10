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
            ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcReq), RpcReq, wrapped);
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
                pkg.ReadString();   // client-sent name: ignored (spoofable); the name comes from the sender's peer
                string playerName = EconomySenderGuard.SenderName(senderId);
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
                        // Template mail carries item attachments: only seed it for a sender standing at a mail NPC
                        // that really has this profile (its ZDO "npc_mail_profile"), or an admin. The inbox is still sent.
                        if (!string.IsNullOrEmpty(profile))
                        {
                            if (global::FiresCore.Sync.AdminSyncing.IsAdmin(senderId) || IsNearMailNpc(senderId, profile))
                                SeedProfileMail(ownerId, playerName, profile);
                            else
                                Debug.LogWarning($"{LogPrefix} profile mail '{profile}' not seeded for sender {senderId}: no RPGM NPC within {MailNpcRadius} m");
                        }
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

        /// <summary>How close (metres) the sender's character must be to a mail NPC for its profile mail to seed.</summary>
        private const float MailNpcRadius = 15f;
        private const string MailProfileZdoKey = "npc_mail_profile";

        private const string NpcProfileZdoKey = "npc_profile";
        private const string NpcDialogueProfileZdoKey = "npc_dialogue_profile";

        // A mail NPC with this profile, OR any RPGM NPC: a dialogue "mail,<profile>" command sets the profile only on
        // the client (never on the ZDO), so any saved NPC within range counts. A sender near no NPC is still refused.
        private static bool IsNearMailNpc(long senderId, string profile) =>
            EconomySenderGuard.IsSenderNear(senderId, MailNpcRadius,
                z => string.Equals(z.GetString(MailProfileZdoKey, ""), profile, StringComparison.OrdinalIgnoreCase)
                     || !string.IsNullOrEmpty(z.GetString(NpcProfileZdoKey, ""))
                     || !string.IsNullOrEmpty(z.GetString(NpcDialogueProfileZdoKey, "")));

        private static void UpsertUser(string ownerId, string playerName)
        {
            if (string.IsNullOrEmpty(ownerId) || string.IsNullOrWhiteSpace(playerName)) return;   // never register an empty name
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
            var ids = users.Where(u => u != null && !string.IsNullOrEmpty(u.UserID) && u.Name != null && u.Name.Trim().Equals(want, StringComparison.OrdinalIgnoreCase))
                           .Select(u => u.UserID).Distinct(StringComparer.Ordinal).ToList();
            // Character names are player-chosen: two accounts with the same name must not let one receive the
            // other's mail, so an ambiguous name resolves to nobody.
            if (ids.Count > 1)
            {
                Debug.LogWarning($"{LogPrefix} recipient name '{want}' is shared by {ids.Count} accounts — not delivered by name");
                return null;
            }
            return ids.Count == 1 ? ids[0] : null;
        }

        /// <summary>Server-side system mail to an owner KEY (the economy owner id the server recorded, e.g. a
        /// listing's SellerUserID). Preferred over <see cref="DeliverSystemMail"/>: names are not identities.</summary>
        public static bool DeliverSystemMailToOwner(string ownerId, string sender, string subject, string body, List<StoredItem> attachments)
        {
            if (!IsServer || string.IsNullOrEmpty(ownerId)) return false;
            MailRepository.AddMail(new FiresCore.Storage.MailEntry
            {
                Owner = ownerId,
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
            // 0.0.763 trailing block (old clients ignore it): per attachment, the item's fidelity, so an escrowed
            // marketplace item returned by mail keeps its quality/variant/durability/crafter/customData.
            foreach (var a in list)
            {
                pkg.Write(a.Quality);
                pkg.Write(a.Variant);
                pkg.Write(a.DurabilityPercent);
                pkg.Write(a.CrafterName ?? "");
                pkg.Write(a.CrafterID);
                pkg.Write(a.CUSTOMdata ?? "{}");
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        // Fidelity of one claimed attachment (Quality 0 = plain mail item: fresh quality-1 stack, as before).
        private sealed class AttFidelity { public int Quality, Variant; public byte DurabilityPct; public string CrafterName = "", CustomData = "{}"; public long CrafterId; }

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
                        if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.IsFromServer(senderId))
                        { Debug.LogWarning($"{LogPrefix} ClaimGrant ignored: sender {senderId} is not the server"); break; }
                        // Optional 0.0.763 fidelity block (absent from an older server).
                        var fid = new List<AttFidelity>();
                        if (pkg.GetPos() < pkg.Size())
                            for (int a = 0; a < nAtt; a++)
                                fid.Add(new AttFidelity
                                {
                                    Quality = pkg.ReadInt(), Variant = pkg.ReadInt(), DurabilityPct = pkg.ReadByte(),
                                    CrafterName = pkg.ReadString(), CrafterId = pkg.ReadLong(), CustomData = pkg.ReadString(),
                                });
                        int granted = GrantToInventory(grant, fid);
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

        // Inventory full: the dropped copy keeps the escrowed item's fidelity too.
        private static ItemDrop.ItemData WithFidelity(ItemDrop.ItemData template, AttFidelity f)
        {
            if (f == null) return template;
            var d = template.Clone();
            d.m_quality = f.Quality; d.m_variant = f.Variant; d.m_crafterID = f.CrafterId; d.m_crafterName = f.CrafterName ?? "";
            if (d.m_shared != null && d.m_shared.m_useDurability) d.m_durability = Mathf.Clamp01(f.DurabilityPct / 100f) * d.GetMaxDurability(f.Quality);
            return d;
        }

        private static int GrantToInventory(List<MailAttachment> grant, List<AttFidelity> fid = null)
        {
            var player = Player.m_localPlayer;
            if (player == null || grant == null) return 0;
            var inv = player.GetInventory();
            int granted = 0;
            for (int i = 0; i < grant.Count; i++)
            {
                var att = grant[i];
                var f = fid != null && i < fid.Count && fid[i].Quality > 0 ? fid[i] : null;
                var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(att.Prefab) : null;
                var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                if (inv.CanAddItem(drop.m_itemData, att.Count))
                {
                    var added = f != null
                        ? inv.AddItem(att.Prefab, att.Count, f.Quality, f.Variant, f.CrafterId, f.CrafterName ?? "", false)
                        : inv.AddItem(att.Prefab, att.Count, 1, 0, 0L, "", false);
                    // Durability + customData only on a single item (equipment never stacks) — same rule as the market grant.
                    if (f != null && added != null && att.Count == 1)
                    {
                        try
                        {
                            if (added.m_shared != null && added.m_shared.m_useDurability)
                                added.m_durability = Mathf.Clamp01(f.DurabilityPct / 100f) * added.GetMaxDurability(f.Quality);
                            if (!string.IsNullOrEmpty(f.CustomData) && f.CustomData != "{}")
                            {
                                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(f.CustomData);
                                if (dict != null) foreach (var kv in dict) added.m_customData[kv.Key] = kv.Value;
                            }
                        }
                        catch { }
                    }
                }
                else
                    ItemDrop.DropItem(WithFidelity(drop.m_itemData, f), att.Count, player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.5f, Quaternion.identity);
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
