using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Quest accept/turn-in + preview-image logic shared by the animated book quest UI (QuestBookJournal).
    /// The old flat MonoBehaviour quest screen was retired with the NpcMainPanel; only the static logic
    /// the book calls into survives here.
    /// </summary>
    public static class QuestInteractionScreenController
    {
        private static string RuntimeSpritesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestSprites);
        private static Dictionary<string, Texture2D> _spriteCache = new Dictionary<string, Texture2D>();

        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) { Debug.LogWarning("[QuestInteractionScreenController] Cannot show - NPC is null"); return; }

            // Quest selection runs entirely on the animated book UI (QuestBookJournal) now. The old flat
            // codegen quest screen was retired with the NpcMainPanel — there is no panel to fall back to,
            // so if the book can't open we just report it. Accept/turn-in logic still lives in this class's
            // static *Core methods, which the book calls; only the MonoBehaviour screen is dead.
            if (global::FiresRPGmaker.UI.Books.QuestBookJournal.OpenForNpc(npc)) return;

            if (string.IsNullOrEmpty(npc.GetEffectiveQuestProfile()))
            {
                var _loaded = global::FiresRPGmaker.UI.QuestProfileManager.GetAllProfileNames();
                Debug.Log($"[QuestInteractionScreenController] NPC '{npc.GetDisplayName()}' resolved NO quest profile (explicit='{npc.questProfile}'; {_loaded.Count} profiles loaded client-side: {string.Join(",", _loaded)})");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{npc.GetDisplayName()} has no quests available.");
                return;
            }

            // Has a profile but the book UI didn't open (missing/failed bundle). Nothing to fall back to.
            Debug.LogWarning($"[QuestInteractionScreenController] Quest book failed to open for '{npc.GetDisplayName()}' — book UI unavailable.");
        }

        /// <summary>
        /// Resolves and loads a quest's preview image from the runtime sprites folder (PreviewImage name, quest id,
        /// or safe quest name; .png/.jpg/.jpeg), cached. Shared by this screen and the book quest UI.
        /// </summary>
        public static Texture2D LoadQuestPreviewTexture(QuestDefinition quest)
        {
            if (quest == null) return null;
            string[] extensions = { ".png", ".jpg", ".jpeg" };
            if (!string.IsNullOrEmpty(quest.PreviewImage))
            {
                string previewName = quest.PreviewImage.Trim();
                string directPath = Path.Combine(RuntimeSpritesFolder, previewName);
                if (File.Exists(directPath)) return LoadTextureCached(directPath);
                foreach (var ext in extensions)
                {
                    string imagePath = Path.Combine(RuntimeSpritesFolder, previewName + ext);
                    if (File.Exists(imagePath)) return LoadTextureCached(imagePath);
                }
                string safePreviewName = MakeSafeFileName(previewName);
                foreach (var ext in extensions)
                {
                    string imagePath = Path.Combine(RuntimeSpritesFolder, safePreviewName + ext);
                    if (File.Exists(imagePath)) return LoadTextureCached(imagePath);
                }
            }
            foreach (var ext in extensions)
            {
                string imagePath = Path.Combine(RuntimeSpritesFolder, quest.QuestId + ext);
                if (File.Exists(imagePath)) return LoadTextureCached(imagePath);
            }
            string safeName = MakeSafeFileName(quest.Name ?? quest.QuestId);
            foreach (var ext in extensions)
            {
                string imagePath = Path.Combine(RuntimeSpritesFolder, safeName + ext);
                if (File.Exists(imagePath)) return LoadTextureCached(imagePath);
            }
            return null;
        }

        private static Texture2D LoadTextureCached(string filePath)
        {
            try
            {
                if (_spriteCache.TryGetValue(filePath, out var cachedTexture)) return cachedTexture;
                byte[] fileData = File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = false;
                var imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConversionType != null)
                {
                    var loadImageMethod = imageConversionType.GetMethod("LoadImage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                    if (loadImageMethod != null) loaded = (bool)loadImageMethod.Invoke(null, new object[] { texture, fileData, false });
                }
                if (!loaded)
                {
                    try
                    {
                        var loadImageDirect = typeof(Texture2D).GetMethod("LoadImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(byte[]) }, null);
                        if (loadImageDirect != null) loaded = (bool)loadImageDirect.Invoke(texture, new object[] { fileData });
                    }
                    catch { }
                }
                if (loaded && texture.width > 8) { texture.name = Path.GetFileNameWithoutExtension(filePath); _spriteCache[filePath] = texture; return texture; }
                UnityEngine.Object.Destroy(texture);
            }
            catch { }
            return null;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name) { if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c); else if (c == ' ') sb.Append('_'); }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        /// <summary>
        /// Accepts a quest for the local player: requirement checks, vault flag (routed RPC client-side / local
        /// apply server-side) and the OnAccept lifecycle. Shared by this screen and the book quest UI.
        /// </summary>
        public static bool AcceptQuestCore(QuestDefinition quest)
        {
            if (quest == null) return false;
            try
            {
                var localPlayer = Player.m_localPlayer;
                if (localPlayer == null) return false;
                long playerId = localPlayer.GetPlayerID();
                if (playerId <= 0) return false;
                string questId = quest.QuestId;
                var questManager = QuestManager.Instance;
                if (questManager != null)
                {
                    if (questManager.HasQuest(playerId, questId)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest already active!"); return false; }
                    if (questManager.HasCompletedQuest(playerId, questId) && quest.CooldownDays > 0) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest already completed!"); return false; }
                    List<string> missing;
                    if (!questManager.CheckRequirements(playerId, quest, out missing)) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Missing requirements: {string.Join(", ", missing)}"); return false; }
                }
                // kg MaxAcceptedQuests (MarketPlace.cfg, default 7; ≤0 = unlimited): cap the simultaneously
                // accepted set. Checked against the same store Accept writes, so the count can't drift.
                int maxAccepted = global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.MaxAcceptedQuests;
                if (maxAccepted > 0 && global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Count >= maxAccepted)
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"You cannot accept more than {maxAccepted} quests!");
                    return false;
                }
                // kg-parity: accept is a LOCAL, immediate, per-quest write via ActiveQuestStore — no server round-trip
                // (that round-trip caused the first-click race AND rebuilt the whole list from a stale copy, wiping
                // siblings). The store owns the client-local active set in per-quest m_customData keys.
                if (!global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Accept(questId, quest))
                    return false;
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest accepted: {quest.Name}");
                // KG-parity lifecycle: fire OnAcceptQuest commands.
                try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.Fire(quest, global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnAccept); } catch { }
                return true;
            }
            catch (Exception ex) { Debug.LogError($"[QuestInteractionScreenController] Failed to accept quest: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Abandons an active quest for the local player: clears the vault active flag (routed RPC client-side /
        /// local apply server-side) and fires the OnCancel lifecycle. Shared by this screen and the book quest UI.
        /// </summary>
        public static bool AbandonQuestCore(QuestDefinition quest)
        {
            if (quest == null) return false;
            try
            {
                var localPlayer = Player.m_localPlayer;
                if (localPlayer == null) return false;
                long playerId = localPlayer.GetPlayerID();
                if (playerId <= 0) return false;
                string questId = quest.QuestId;
                // kg-parity: abandon is a local per-quest removal (drops the m_customData key).
                global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Abandon(questId);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Quest abandoned: {quest.Name}");
                // KG-parity lifecycle: fire OnCancelQuest commands.
                try { global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.Fire(quest, global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnCancel); } catch { }
                return true;
            }
            catch (Exception ex) { Debug.LogError($"[QuestInteractionScreenController] Failed to abandon quest: {ex.Message}"); return false; }
        }
    }

    /// <summary>Drag handler on a popup's header strip; moves the assigned <see cref="target"/> by the pointer delta.</summary>
    internal class PopupDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform target;
        private Vector2 _dragOffset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target.parent as RectTransform, eventData.position, eventData.pressEventCamera, out _dragOffset);
            _dragOffset = target.anchoredPosition - _dragOffset;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target.parent as RectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
            target.anchoredPosition = localPoint + _dragOffset;
        }
    }
}
