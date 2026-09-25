using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Splatform;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// Saved-NPC appearance templates as hammer pieces (the Marketplace pattern): every name-keyed
    /// template in the admin's CLIENT-LOCAL store becomes a placeable piece under a shared
    /// "Fires NPCs" tab, alongside the base random StaticNpc.
    ///
    /// Templates are appearance-only by contract — placing one runs the normal static-NPC flow with
    /// the template's LOOK pre-seeded; module profiles (banker/quests/…) are never in templates and
    /// are assigned per-placement via the admin book.
    ///
    /// Mechanics: each template piece is an INACTIVE clone of the loaded StaticNpc piece prefab
    /// (ghost shows the actual NPC model). Template clones exist only on clients that hold the
    /// template files, so a placed instance's ZDO would be an unresolvable prefab everywhere else —
    /// the <see cref="NpcTemplatePlacedSwapper"/> therefore swaps the placed instance for a REAL
    /// "StaticNpc" body in the same Awake call stack (the template ZDO never survives the frame,
    /// and the server only ever sees the ordinary StaticNpc body).
    ///
    /// Tab: FAP's shared VACategoryRegistry via reflection when FAP is loaded (shared category int +
    /// FAP's Hud tab-strip renders it), self-contained fallback otherwise — the proven
    /// FiresDungeonMaster CategoryProvider recipe.
    /// </summary>
    public static class NpcTemplatePieces
    {
        internal const string CategoryName = "Fires NPCs";
        private const string PiecePrefix = "FiresNpcTpl_";

        private static readonly Dictionary<string, GameObject> _clones = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static GameObject _holder;

        // Cached from AddToHammer so save/delete can mutate the live table without a relog —
        // this pass at hammer-build time is otherwise the ONLY thing that adds template pieces.
        private static PieceTable _table;
        private static GameObject _baseNpcPiece;
        private static bool _templatesPending;

        /// <summary>Called from VAPieceManager.AddPiecesToHammer once the hammer table is ready.
        /// Re-homes the base StaticNpc piece onto the shared tab and arms the deferred template
        /// build — the clones themselves (a full dressed NPC rig each) are only built the first
        /// time the table is actually opened, not on every login.</summary>
        public static void AddToHammer(PieceTable table, List<GameObject> basePiecePrefabs)
        {
            if (table == null || Application.isBatchMode) return;   // client-side feature (templates are client-local)
            try
            {
                Piece.PieceCategory category = ResolveCategory();
                AddCategoryToTable(table, category, CategoryName);

                // Base random StaticNpc moves onto the tab with the templates.
                GameObject baseNpc = basePiecePrefabs?.Find(p => p != null && p.name == "StaticNpc");
                var basePiece = baseNpc != null ? baseNpc.GetComponent<Piece>() : null;
                if (basePiece != null) basePiece.m_category = category;

                if (baseNpc == null || ZNetScene.instance == null) return;
                _table = table;
                _baseNpcPiece = baseNpc;
                _templatesPending = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] AddToHammer failed: {ex.Message}");
            }
        }

        /// <summary>Deferred template build: instantiating + dressing a clone per template is real
        /// main-thread work (skinned rig, armor/hair attach), so it waits for the first
        /// PieceTable.UpdateAvailable on the hammer table instead of running during login.</summary>
        internal static void EnsureTemplatesBuilt(PieceTable table)
        {
            if (!_templatesPending || table == null || table != _table) return;
            if (_baseNpcPiece == null || ZNetScene.instance == null) return;
            _templatesPending = false;
            try
            {
                Piece.PieceCategory category = ResolveCategory();
                foreach (string templateName in SavedNpcManager.GetAllTemplateNames())
                {
                    var record = SavedNpcManager.GetTemplate(templateName);
                    if (record == null) continue;

                    GameObject clone = GetOrBuildClone(_baseNpcPiece, templateName, category);
                    if (clone == null) continue;

                    // Name-based dedupe (mirrors VAPieceManager) then add.
                    _table.m_pieces.RemoveAll(p => p != null && p != clone && p.name == clone.name);
                    if (!_table.m_pieces.Contains(clone)) _table.m_pieces.Add(clone);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] deferred template build failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        private static class PieceTable_UpdateAvailable_BuildTemplates_Patch
        {
            private static void Prefix(PieceTable __instance) => EnsureTemplatesBuilt(__instance);
        }

        private static GameObject GetOrBuildClone(GameObject baseNpc, string templateName, Piece.PieceCategory category)
        {
            if (_clones.TryGetValue(templateName, out var existing) && existing != null) return existing;

            if (_holder == null)
            {
                _holder = new GameObject("FiresNpcTemplatePieces");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }

            // Inactive clone: components must not Awake (a live ZNetView would mint a stray ZDO).
            GameObject clone = FiresCore.Net.NetworkObjectHelper.InstantiateInactiveClone(baseNpc);
            if (clone == null) return null;
            clone.name = PiecePrefix + MakeSafe(templateName);
            clone.transform.SetParent(_holder.transform, false);

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = templateName;
                piece.m_description = "Saved NPC appearance template. Places a static NPC with this look - assign its role via Shift+E.";
                piece.m_category = category;
                piece.m_canBeRemoved = false;

                var record = SavedNpcManager.GetTemplate(templateName);

                // Dress the clone with the template's saved look — the placement ghost IS an
                // Instantiate of this clone, so this is what rides the crosshair (Marketplace parity).
                NpcTemplateAppearance.DressPieceClone(clone, record);

                // Marketplace-style icon: the dressed-NPC render captured at save time; when the
                // template predates icon capture — or carries a POISONED BLANK from the naked-clone
                // era (IsMeaningfulIconPng screens those out) — render one NOW from the dressed clone
                // and persist it, but never persist another blank.
                bool storedIconUsable = record != null && NpcTemplateAppearance.IsMeaningfulIconPng(record.IconPng);
                var icon = storedIconUsable ? SpriteFromBase64(record.IconPng) : null;
                if (icon == null && record != null)
                {
                    icon = NpcTemplateAppearance.RenderIconFromClone(clone);
                    string png = NpcTemplateAppearance.SpriteToBase64(icon);
                    if (NpcTemplateAppearance.IsMeaningfulIconPng(png))
                    {
                        record.IconPng = png;
                        SavedNpcManager.SaveTemplate(templateName, record);
                        Debug.Log($"[NpcTemplatePieces] rendered + persisted portrait icon for template '{templateName}'");
                    }
                    else if (icon != null)
                    {
                        Debug.LogWarning($"[NpcTemplatePieces] icon render for '{templateName}' came out blank — not persisting");
                        icon = null;
                    }
                }
                if (icon != null) piece.m_icon = icon;
                else Debug.LogWarning($"[NpcTemplatePieces] no usable icon for template '{templateName}' — tile shows the default sprite");
            }

            var marker = clone.GetComponent<NpcTemplateMarker>() ?? clone.AddComponent<NpcTemplateMarker>();
            marker.TemplateName = templateName;
            if (clone.GetComponent<NpcTemplatePlacedSwapper>() == null)
                clone.AddComponent<NpcTemplatePlacedSwapper>();

            RegisterWithZNetScene(clone);
            _clones[templateName] = clone;
            return clone;
        }

        /// <summary>Swaps a live template piece's hammer icon after a capture/backfill, so the tab
        /// shows the dressed-NPC render on the next hammer open (no relog needed).</summary>
        public static void UpdateIcon(string templateName, string iconPngBase64)
        {
            if (string.IsNullOrEmpty(templateName) || string.IsNullOrEmpty(iconPngBase64)) return;
            if (!_clones.TryGetValue(templateName, out var clone) || clone == null) return;
            var sprite = SpriteFromBase64(iconPngBase64);
            var piece = clone.GetComponent<Piece>();
            if (sprite != null && piece != null) piece.m_icon = sprite;
        }

        /// <summary>FAP right-click row target (reflection-dispatched): permanently deletes a saved
        /// preset — memory + client-local JSON + the live hammer piece.</summary>
        public static void TileDelete(string templateName)
        {
            bool gone = SavedNpcManager.DeleteTemplate(templateName);
            Msg(gone ? $"Deleted preset '{templateName}'" : $"Preset '{templateName}' not found");
        }

        /// <summary>FAP right-click row target (reflection-dispatched): clears the stored portrait
        /// and rebuilds the piece so the icon re-renders through the photo pipeline.</summary>
        public static void TileRerenderIcon(string templateName)
        {
            var record = SavedNpcManager.GetTemplate(templateName);
            if (record == null) { Msg($"Preset '{templateName}' not found"); return; }
            record.IconPng = null;
            SavedNpcManager.SaveTemplate(templateName, record);
            AddOrRefreshTemplateLive(templateName);
            Msg($"Re-rendered icon for '{templateName}'");
        }

        private static void Msg(string text)
        {
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, text); } catch { }
        }

        /// <summary>
        /// Registers (or fully rebuilds) one template's hammer piece LIVE — called by the explicit
        /// "Save to hammer" action so the tile + dressed ghost + icon appear without a relog.
        /// Re-saving an existing template rebuilds its clone: dressing is bake-once, so a fresh
        /// wardrobe needs a fresh clone.
        /// </summary>
        public static void AddOrRefreshTemplateLive(string templateName)
        {
            if (string.IsNullOrEmpty(templateName) || Application.isBatchMode) return;
            if (_table == null || _baseNpcPiece == null || ZNetScene.instance == null)
            {
                Debug.LogWarning($"[NpcTemplatePieces] live add for '{templateName}' skipped — hammer table not built yet (it will appear at next login)");
                return;
            }
            try
            {
                if (_clones.TryGetValue(templateName, out var stale) && stale != null)
                    DestroyClone(templateName, stale);

                GameObject clone = GetOrBuildClone(_baseNpcPiece, templateName, ResolveCategory());
                if (clone == null) return;
                _table.m_pieces.RemoveAll(p => p == null || (p != clone && p.name == clone.name));
                if (!_table.m_pieces.Contains(clone)) _table.m_pieces.Add(clone);
                RefreshLocalBuildTable();
                Debug.Log($"[NpcTemplatePieces] live-registered template piece '{templateName}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] live add failed for '{templateName}': {ex.Message}");
            }
        }

        /// <summary>Removes one template's hammer piece LIVE (delete flow): table entry, ZNetScene
        /// registration and the parked clone all go; the tab refreshes immediately.</summary>
        public static void RemoveTemplateLive(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return;
            try
            {
                _clones.TryGetValue(templateName, out var clone);
                string pieceName = clone != null ? clone.name : PiecePrefix + MakeSafe(templateName);
                if (_table != null)
                    _table.m_pieces.RemoveAll(p => p == null || p.name == pieceName);
                if (clone != null) DestroyClone(templateName, clone);
                else _clones.Remove(templateName);
                RefreshLocalBuildTable();
                Debug.Log($"[NpcTemplatePieces] removed template piece '{templateName}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] live remove failed for '{templateName}': {ex.Message}");
            }
        }

        private static void DestroyClone(string templateName, GameObject clone)
        {
            _clones.Remove(templateName);
            DeregisterFromZNetScene(clone);
            UnityEngine.Object.Destroy(clone);
        }

        private static void DeregisterFromZNetScene(GameObject prefab)
        {
            var znet = ZNetScene.instance;
            if (znet == null || prefab == null) return;
            znet.m_prefabs.Remove(prefab);
            try
            {
                var namedField = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
                if (namedField != null && namedField.GetValue(znet) is Dictionary<int, GameObject> dict)
                    dict.Remove(prefab.name.GetStableHashCode());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] named-prefab deregistration failed for {prefab.name}: {ex.Message}");
            }
        }

        /// <summary>Re-runs the local player's available-piece scan so a mutated table shows up in the
        /// already-open build HUD (vanilla only refreshes on hammer equip).</summary>
        private static void RefreshLocalBuildTable()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            try
            {
                // Unconditional, mirroring FAP's live-change refresh — refreshing a non-hammer
                // table is harmless, and the next hammer equip rebuilds anyway. Private in
                // vanilla (no publicized assembly in this build), hence reflection.
                AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList")?.Invoke(player, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] build-table refresh failed: {ex.Message}");
            }
        }

        private static Sprite SpriteFromBase64(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] png = Convert.FromBase64String(base64);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // ImageConversion via reflection — direct module reference breaks the net48 build
                // (netstandard 2.1 metadata; same trap as AudioModule).
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var load = ic?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                bool ok = load != null && load.Invoke(null, new object[] { tex, png }) is bool b && b;
                if (!ok) { UnityEngine.Object.Destroy(tex); return null; }
                tex.name = "FiresNpcTplIcon";
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch { return null; }
        }

        private static string MakeSafe(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "Template";
        }

        /// <summary>Client-side ZNetScene registration so PlacePiece can instantiate by hash locally.
        /// (The placed instance is swapped for a real StaticNpc within its own Awake, so no other
        /// machine ever needs to resolve this name.)</summary>
        private static void RegisterWithZNetScene(GameObject prefab)
        {
            var znet = ZNetScene.instance;
            if (znet == null || znet.GetPrefab(prefab.name) != null) return;
            znet.m_prefabs.Add(prefab);
            try
            {
                var namedField = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
                if (namedField != null && namedField.GetValue(znet) is Dictionary<int, GameObject> dict)
                {
                    int hash = prefab.name.GetStableHashCode();
                    if (!dict.ContainsKey(hash)) dict.Add(hash, prefab);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplatePieces] named-prefab registration failed for {prefab.name}: {ex.Message}");
            }
        }

        // ── FAP-shared category (FiresDungeonMaster CategoryProvider recipe) ──────────────────────

        private static MethodInfo _fapGetOrAdd;
        private static bool _fapProbed;

        private static Piece.PieceCategory ResolveCategory()
        {
            ProbeFap();
            if (_fapGetOrAdd != null)
            {
                try
                {
                    object value = _fapGetOrAdd.Invoke(null, new object[] { CategoryName });
                    if (value is Piece.PieceCategory pc) return pc;
                    return (Piece.PieceCategory)Convert.ToInt32(value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcTemplatePieces] FAP VACategoryRegistry.GetOrAdd failed, using fallback: {ex.Message}");
                }
            }
            return FallbackCategoryRegistry.GetOrAdd(CategoryName);
        }

        private static void ProbeFap()
        {
            if (_fapProbed) return;
            _fapProbed = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType("VerdantsAscent.Patches.VACategoryRegistry", throwOnError: false); }
                catch { t = null; }
                if (t == null) continue;
                _fapGetOrAdd = t.GetMethod("GetOrAdd", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (_fapGetOrAdd != null) return;
            }
        }

        private static void AddCategoryToTable(PieceTable table, Piece.PieceCategory cat, string label)
        {
            int needed = (int)cat + 1;
            while (PieceTableAccess.ByCategory(table).Count < needed)
                PieceTableAccess.ByCategory(table).Add(new List<Piece>());
            if (!table.m_categories.Contains(cat))
            {
                table.m_categories.Add(cat);
                table.m_categoryLabels.Add(label);
            }
        }

        /// <summary>Fallback allocator when FAP is absent — same numbering scheme (ints above
        /// Piece.PieceCategory.Max) so a world that later adds FAP keeps disjoint values.</summary>
        private static class FallbackCategoryRegistry
        {
            private static readonly Dictionary<string, Piece.PieceCategory> _byName = new Dictionary<string, Piece.PieceCategory>(StringComparer.Ordinal);
            private static int _next = (int)Piece.PieceCategory.Max + 1;

            internal static Piece.PieceCategory GetOrAdd(string name)
            {
                if (_byName.TryGetValue(name, out var existing)) return existing;
                var value = (Piece.PieceCategory)(_next++);
                _byName[name] = value;
                return value;
            }

            // Vanilla UpdateAvailable creates its (int)PieceCategory.Max per-category lists only while
            // m_availablePiecesByCategory is empty, then indexes all of them for every All-category piece —
            // so pre-grow for custom categories but never below Max (9 since 1.0 added DeepNorth).
            // Cheap no-op when FAP (which has the identical guard) is present.
            [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
            private static class PieceTable_UpdateAvailable_Grow_Patch
            {
                private static void Prefix(PieceTable __instance)
                {
                    if (__instance == null || PieceTableAccess.ByCategory(__instance) == null || __instance.m_categories == null) return;
                    int needed = (int)Piece.PieceCategory.Max;
                    for (int i = 0; i < __instance.m_categories.Count; i++)
                        needed = Math.Max(needed, (int)__instance.m_categories[i] + 1);
                    while (PieceTableAccess.ByCategory(__instance).Count < needed)
                        PieceTableAccess.ByCategory(__instance).Add(new List<Piece>());
                }
            }
        }
    }

    /// <summary>Carries the template name on a template piece clone (and its placed instance).</summary>
    public sealed class NpcTemplateMarker : MonoBehaviour
    {
        public string TemplateName;
    }

    /// <summary>
    /// On the PLACED template instance: swaps it for a real "StaticNpc" body seeded with the
    /// template's appearance, inside the same Awake call stack — the template-named ZDO never
    /// survives the frame (its prefab hash only resolves on this client). Ghost instances
    /// (invalid ZNetView) no-op.
    /// </summary>
    public sealed class NpcTemplatePlacedSwapper : MonoBehaviour
    {
        private void Awake()
        {
            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;   // placement ghost / parked clone

            try
            {
                string templateName = GetComponent<NpcTemplateMarker>()?.TemplateName;
                var record = SavedNpcManager.GetTemplate(templateName);

                var basePrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("StaticNpc") : null;
                if (basePrefab == null)
                {
                    Debug.LogError("[NpcTemplatePieces] StaticNpc prefab missing — cannot place template");
                    return;
                }

                var body = UnityEngine.Object.Instantiate(basePrefab, transform.position, transform.rotation);
                // Player.PlacePiece sets the creator on the template only after this Awake, so the body takes it from the
                // placer here: without one it had no ward or private-chest rights as a worker.
                var placer = Player.m_localPlayer;
                var bodyPiece = body.GetComponent<Piece>();
                if (placer != null && bodyPiece != null) bodyPiece.SetCreator(placer.GetPlayerID(), PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                var bodyView = body.GetComponent<ZNetView>();
                var zdo = bodyView != null && bodyView.IsValid() ? bodyView.GetZDO() : null;
                if (zdo != null && record != null)
                {
                    // APPEARANCE ONLY — profiles/type stay unassigned (admin sets them in the book).
                    // npc_initialized=true routes the body through the restore path (no randomize);
                    // the server's instance mints the anchor on its own initializer pass.
                    zdo.Set("npc_initialized", true);
                    zdo.Set("npc_static_placement", true);
                    zdo.Set("npc_stationed", true);
                    zdo.Set("npc_stationed_pos", transform.position);
                    zdo.Set("npc_stationed_rot", transform.rotation);
                    zdo.Set("npc_placed_anchor", transform.position);
                    zdo.Set("npc_name_override", record.DisplayNameOverride ?? "");
                    zdo.Set("npc_model_override", record.ModelOverride ?? "");
                    if (!string.IsNullOrEmpty(record.FashionJson)) zdo.Set("npc_fashion_json", record.FashionJson);
                    zdo.Set("npc_equip_helmet", record.EquipHelmet ?? "");
                    zdo.Set("npc_equip_chest", record.EquipChest ?? "");
                    zdo.Set("npc_equip_legs", record.EquipLegs ?? "");
                    zdo.Set("npc_equip_shoulder", record.EquipShoulder ?? "");
                    zdo.Set("npc_equip_utility", record.EquipUtility ?? "");
                    zdo.Set("npc_equip_righthand", record.EquipRightHand ?? "");
                    zdo.Set("npc_equip_lefthand", record.EquipLeftHand ?? "");
                    zdo.Set("npc_equip_rightback", record.EquipRightBack ?? "");
                    zdo.Set("npc_equip_leftback", record.EquipLeftBack ?? "");
                    zdo.Set("npc_equip_righthand_data", record.EquipRightHandData ?? "");
                    zdo.Set("npc_equip_lefthand_data", record.EquipLeftHandData ?? "");

                    // Vanilla look (item hashes + model index + colors): the appearance of randomly
                    // dressed source NPCs lives ONLY here — seed it or the placed body spawns naked
                    // and re-randomizes.
                    NpcTemplateAppearance.SeedLookOntoBody(zdo, record.LookJson);
                }

                Debug.Log($"[NpcTemplatePieces] placed template '{templateName}' → StaticNpc at {transform.position}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NpcTemplatePieces] template swap failed: {ex.Message}");
            }
            finally
            {
                // The template-named instance must not outlive this frame.
                if (ZNetScene.instance != null) ZNetScene.instance.Destroy(gameObject);
                else Destroy(gameObject);
            }
        }
    }
}
