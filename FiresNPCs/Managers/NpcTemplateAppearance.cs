using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// The template's VANILLA look, captured from a live body's ZDO at save time. Randomly-dressed
    /// NPCs carry their entire appearance in vanilla VisEquipment vars (item hashes + model index +
    /// colors) — NOT in FashionJson or NpcController equip fields — so templates must snapshot these
    /// or they contain no appearance at all (the naked-ghost / blank-icon bug).
    /// </summary>
    [Serializable]
    internal sealed class NpcLook
    {
        public int ModelIndex;
        public float HairR = 1f, HairG = 1f, HairB = 1f;
        public float SkinR = 1f, SkinG = 1f, SkinB = 1f;
        public string Hair, Beard;
        public string Helmet, Chest, Legs, Shoulder, Utility;
        public string RightHand, LeftHand, RightBack, LeftBack;
    }

    /// <summary>
    /// Dresses NON-networked NPC clones (template piece prefabs, placement previews) with a saved
    /// appearance, and renders portrait icons from them — the Marketplace pattern (their hammer NPC
    /// pieces show the dressed model as both ghost and icon). Everything here works WITHOUT a ZDO:
    /// model swap + armor textures + skinned-armor bone rebinds are the exact Market_NPC.FashionApply /
    /// EquipItemsOnModel mechanics; hair/beard ride NpcFashionManager's GameObject paths.
    /// </summary>
    internal static class NpcTemplateAppearance
    {
        // ── look capture / seed (vanilla VisEquipment ZDO vars) ────────────────────────────────

        /// <summary>Snapshot the vanilla look off a live body's ZDO as JSON (null if nothing there).</summary>
        internal static string CaptureLookJson(ZDO zdo)
        {
            if (zdo == null) return null;
            try
            {
                var look = new NpcLook
                {
                    ModelIndex = zdo.GetInt(ZDOVars.s_modelIndex, 0),
                    Hair = ItemNameFromHash(zdo.GetInt(ZDOVars.s_hairItem, 0)),
                    Beard = ItemNameFromHash(zdo.GetInt(ZDOVars.s_beardItem, 0)),
                    Helmet = ItemNameFromHash(zdo.GetInt(ZDOVars.s_helmetItem, 0)),
                    Chest = ItemNameFromHash(zdo.GetInt(ZDOVars.s_chestItem, 0)),
                    Legs = ItemNameFromHash(zdo.GetInt(ZDOVars.s_legItem, 0)),
                    Shoulder = ItemNameFromHash(zdo.GetInt(ZDOVars.s_shoulderItem, 0)),
                    Utility = ItemNameFromHash(zdo.GetInt(ZDOVars.s_utilityItem, 0)),
                    RightHand = ItemNameFromHash(zdo.GetInt(ZDOVars.s_rightItem, 0)),
                    LeftHand = ItemNameFromHash(zdo.GetInt(ZDOVars.s_leftItem, 0)),
                    RightBack = ItemNameFromHash(zdo.GetInt(ZDOVars.s_rightBackItem, 0)),
                    LeftBack = ItemNameFromHash(zdo.GetInt(ZDOVars.s_leftBackItem, 0)),
                };
                Vector3 hair = zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one);
                Vector3 skin = zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one);
                look.HairR = hair.x; look.HairG = hair.y; look.HairB = hair.z;
                look.SkinR = skin.x; look.SkinG = skin.y; look.SkinB = skin.z;
                return JsonConvert.SerializeObject(look);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] look capture failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Seed a placed body's ZDO with the template look so the vanilla visual sync dresses
        /// it identically to the source NPC (name→hash per slot + model index + colors).</summary>
        internal static void SeedLookOntoBody(ZDO zdo, string lookJson)
        {
            var look = Deserialize(lookJson);
            if (zdo == null || look == null) return;
            try
            {
                zdo.Set(ZDOVars.s_modelIndex, look.ModelIndex);
                zdo.Set("npc_isfemale", look.ModelIndex == 1);
                zdo.Set(ZDOVars.s_hairColor, new Vector3(look.HairR, look.HairG, look.HairB));
                zdo.Set(ZDOVars.s_skinColor, new Vector3(look.SkinR, look.SkinG, look.SkinB));
                zdo.Set(ZDOVars.s_hairItem, HashFromItemName(look.Hair));
                zdo.Set(ZDOVars.s_beardItem, HashFromItemName(look.Beard));
                zdo.Set(ZDOVars.s_helmetItem, HashFromItemName(look.Helmet));
                zdo.Set(ZDOVars.s_chestItem, HashFromItemName(look.Chest));
                zdo.Set(ZDOVars.s_legItem, HashFromItemName(look.Legs));
                zdo.Set(ZDOVars.s_shoulderItem, HashFromItemName(look.Shoulder));
                zdo.Set(ZDOVars.s_utilityItem, HashFromItemName(look.Utility));
                zdo.Set(ZDOVars.s_rightItem, HashFromItemName(look.RightHand));
                zdo.Set(ZDOVars.s_leftItem, HashFromItemName(look.LeftHand));
                zdo.Set(ZDOVars.s_rightBackItem, HashFromItemName(look.RightBack));
                zdo.Set(ZDOVars.s_leftBackItem, HashFromItemName(look.LeftBack));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] look seed failed: {ex.Message}");
            }
        }

        private static NpcLook Deserialize(string lookJson)
        {
            if (string.IsNullOrEmpty(lookJson)) return null;
            try { return JsonConvert.DeserializeObject<NpcLook>(lookJson); }
            catch { return null; }
        }

        private static string ItemNameFromHash(int hash)
        {
            if (hash == 0 || ObjectDB.instance == null) return null;
            var prefab = ObjectDB.instance.GetItemPrefab(hash);
            return prefab != null ? prefab.name : null;
        }

        private static int HashFromItemName(string name)
            => string.IsNullOrEmpty(name) ? 0 : name.GetStableHashCode();

        // ── clone dressing (Market_NPC.FashionApply / EquipItemsOnModel mechanics, ZDO-free) ────

        /// <summary>Dress an INACTIVE piece-prefab clone with a template's saved look. The clone is
        /// what SetupPlacementGhost instantiates, so this is exactly what the hammer ghost shows.</summary>
        internal static void DressPieceClone(GameObject clone, SavedNpcData record)
        {
            if (clone == null || record == null) return;
            try
            {
                var look = Deserialize(record.LookJson);
                if (look != null)
                {
                    DressFromLook(clone, look);
                }
                else if (!string.IsNullOrEmpty(record.FashionJson))
                {
                    // Fashion-authored templates (admin used the fashion UI) — the legacy path.
                    var npcController = clone.GetComponent<global::FiresRPGmaker.UI.NpcController>();
                    if (npcController != null)
                        NpcFashion.NpcFashionManager.ApplyFashionJsonToNpc(record.FashionJson, npcController);
                    Debug.Log($"[NpcTemplateAppearance] dressed '{clone.name}' via legacy FashionJson (no LookJson — re-save the NPC to capture its full look)");
                }
                else
                {
                    Debug.LogWarning($"[NpcTemplateAppearance] template for '{clone.name}' carries NO look data " +
                        "(saved before look capture existed) — ghost/icon show the undressed body until re-saved");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] DressPieceClone failed for '{clone.name}': {ex.Message}");
            }
        }

        private static void DressFromLook(GameObject clone, NpcLook look)
        {
            var vis = clone.GetComponentInChildren<VisEquipment>(true);
            var body = vis != null ? vis.m_bodyModel : clone.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (body == null)
            {
                Debug.LogWarning("[NpcTemplateAppearance] no body SkinnedMeshRenderer on clone — cannot dress");
                return;
            }

            // One instance, read once: Renderer.materials hands back a new array on every access, and every
            // write below goes to the same material. A body running a non-player shader (1.0's ShadowPerson is
            // on Unity's Standard) has none of these properties, and Unity logs an error per missing write.
            var bodyMaterials = body.materials;
            var bodyMat = bodyMaterials.Length > 0 ? bodyMaterials[0] : null;
            if (bodyMat == null)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] body renderer on '{clone.name}' has no material — cannot dress");
                return;
            }

            // Model swap (female) — Market_NPC.cs:777-782: mesh + main/bump textures from m_models.
            if (look.ModelIndex > 0 && vis != null && vis.m_models != null && look.ModelIndex < vis.m_models.Length
                && vis.m_models[look.ModelIndex].m_baseMaterial != null)
            {
                var model = vis.m_models[look.ModelIndex];
                // Guard the mesh swap like NpcVisEquipment.TryAssignBodyMesh, in the same candidate
                // order: the clone's OWN model-table mesh first — on baked bundles rig, body and bodyfem
                // are rip-consistent (53 bones/bindposes) and are the only meshes guaranteed to skin on
                // that rig. The LIVE Player mesh is a legacy-table fallback only: live 'bodyfem' has
                // drifted from the ripped rig (same bindpose count, different vertex layout) and Unity
                // rejects that pairing at skin time — the "vertex stride … rendering stopped" spam +
                // invisible body on female templates (males never enter this branch — ModelIndex 0).
                // NpcBodyMeshGuard also blacklists any pairing Unity rejected earlier this session.
                var mesh = model.m_mesh;
                if (!FiresCore.Npc.NpcBodyMeshGuard.IsAssignable(mesh, body))
                {
                    try
                    {
                        var pp = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
                        var pvis = pp != null ? pp.GetComponent<VisEquipment>() : null;
                        var live = pvis != null && pvis.m_models != null && look.ModelIndex < pvis.m_models.Length
                            ? pvis.m_models[look.ModelIndex].m_mesh : null;
                        if (FiresCore.Npc.NpcBodyMeshGuard.IsAssignable(live, body)) mesh = live;
                    }
                    catch { }
                }
                int bones = body.bones != null ? body.bones.Length : 0;
                int binds = mesh != null && mesh.bindposes != null ? mesh.bindposes.Length : 0;
                bool compatible = FiresCore.Npc.NpcBodyMeshGuard.IsAssignable(mesh, body);
                if (compatible) body.sharedMesh = mesh;
                Debug.Log($"[NpcTemplateAppearance] female mesh '{(mesh != null ? mesh.name : "null")}' on '{clone.name}': " +
                          $"rendererBones={bones} meshBindposes={binds} → {(compatible ? "assigned" : "SKIPPED (no skinnable candidate — native mesh kept)")}");
                CopyTexture(bodyMat, model.m_baseMaterial, "_MainTex");
                CopyTexture(bodyMat, model.m_baseMaterial, "_SkinBumpMap");
            }

            // Skin tint.
            var skinColor = new Color(look.SkinR, look.SkinG, look.SkinB);
            if (bodyMat.HasProperty("_SkinColor")) bodyMat.SetColor("_SkinColor", skinColor);

            // Chest/legs armor textures on the body material (Market_NPC.EquipItemsOnModel).
            ApplyArmorTextures(bodyMat, look.Chest, "_ChestTex", "_ChestBumpMap", "_ChestMetal", ItemDrop.ItemData.ItemType.Chest);
            ApplyArmorTextures(bodyMat, look.Legs, "_LegsTex", "_LegsBumpMap", "_LegsMetal", ItemDrop.ItemData.ItemType.Legs);

            // Skinned armor meshes (capes, armor overlays) — bone-rebound onto the body skeleton.
            AttachSkinnedArmor(body, look.Chest);
            AttachSkinnedArmor(body, look.Legs);
            AttachSkinnedArmor(body, look.Shoulder);

            // Joint attaches: helmet + hand/back items ride VisEquipment's authored joints.
            if (vis != null)
            {
                AttachJointItem(vis.m_helmet, look.Helmet);
                AttachJointItem(vis.m_rightHand, look.RightHand);
                AttachJointItem(vis.m_leftHand, look.LeftHand);
                AttachJointItem(vis.m_backMelee, look.RightBack);
                AttachJointItem(vis.m_backShield, look.LeftBack);
            }

            // Hair + beard via the proven GameObject-level fashion paths (bone-rebinding clones),
            // then tint whatever renderers they added (diff before/after — the fashion color API is
            // preset-name-keyed and can't take our raw color).
            var hairColor = new Color(look.HairR, look.HairG, look.HairB);
            TintNewRenderers(clone, () => FiresCore.Bridge.NpcFashionBridge.ApplyHair(clone, look.Hair, null), hairColor);
            TintNewRenderers(clone, () => FiresCore.Bridge.NpcFashionBridge.ApplyBeard(clone, look.Beard, null), hairColor);

            Debug.Log($"[NpcTemplateAppearance] dressed '{clone.name}' from look (model={look.ModelIndex} hair={look.Hair} " +
                      $"chest={look.Chest} legs={look.Legs} helmet={look.Helmet})");
        }

        private static void ApplyArmorTextures(Material bodyMat, string itemName,
            string texProp, string bumpProp, string metalProp, ItemDrop.ItemData.ItemType slot)
        {
            var item = GetItemDrop(itemName);
            var armorMat = item != null && item.m_itemData.m_shared.m_itemType == slot
                ? item.m_itemData.m_shared.m_armorMaterial : null;
            if (armorMat == null) return;
            CopyTexture(bodyMat, armorMat, texProp);
            CopyTexture(bodyMat, armorMat, bumpProp);
            CopyTexture(bodyMat, armorMat, metalProp);
        }

        // Only the Valheim character shader carries these slots; writing one it doesn't have logs an error per call.
        private static void CopyTexture(Material target, Material source, string property)
        {
            if (!target.HasProperty(property) || !source.HasProperty(property)) return;
            target.SetTexture(property, source.GetTexture(property));
        }

        private static void AttachSkinnedArmor(SkinnedMeshRenderer body, string itemName)
        {
            var item = GetItemDrop(itemName);
            if (item == null) return;
            foreach (var srcSmr in item.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var piece = UnityEngine.Object.Instantiate(srcSmr.gameObject, body.transform.parent, false);
                piece.name = itemName + "_armor";
                foreach (var smr in piece.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.rootBone = body.rootBone;
                    smr.bones = body.bones;
                }
            }
        }

        private static void AttachJointItem(Transform joint, string itemName)
        {
            var item = GetItemDrop(itemName);
            if (joint == null || item == null) return;
            // Vanilla item prefabs carry their equipped visual under an "attach" child; fall back to
            // the whole prefab minus physics when absent (matches VisEquipment.AttachItem behavior).
            Transform attach = item.transform.Find("attach") ?? item.transform.Find("attach_skin");
            if (attach == null) return;
            var go = UnityEngine.Object.Instantiate(attach.gameObject, joint, false);
            go.name = itemName + "_attach";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
        }

        private static ItemDrop GetItemDrop(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || ObjectDB.instance == null) return null;
            var prefab = ObjectDB.instance.GetItemPrefab(itemName);
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }

        private static void TintNewRenderers(GameObject root, Action addStep, Color color)
        {
            try
            {
                var before = new HashSet<Renderer>(root.GetComponentsInChildren<Renderer>(true));
                addStep();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (before.Contains(r)) continue;
                    foreach (var m in r.materials) m.SetColor("_SkinColor", color);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[NpcTemplateAppearance] hair/beard tint step skipped: {ex.Message}");
            }
        }

        // ── icon snapshot ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Visual-only copy of a (possibly inactive) NPC: every MonoBehaviour/collider/rigidbody is
        /// stripped so activating it can never run companion logic (no Awake storm, no ground snap,
        /// no stray ZDOs) — just bones, meshes and materials. Caller destroys it.
        /// </summary>
        internal static GameObject BuildVisualSnapshot(GameObject source)
        {
            if (source == null) return null;
            GameObject snap = null;
            try
            {
                bool prev = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true;
                try { snap = UnityEngine.Object.Instantiate(source); }
                finally { ZNetView.m_forceDisableInit = prev; }
                snap.name = source.name + "_visual";

                // Two passes: RequireComponent chains make some components refuse to die first.
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (var mb in snap.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        try { UnityEngine.Object.DestroyImmediate(mb); } catch { }
                    }
                }
                foreach (var col in snap.GetComponentsInChildren<Collider>(true))
                {
                    try { UnityEngine.Object.DestroyImmediate(col); } catch { }
                }
                foreach (var rb in snap.GetComponentsInChildren<Rigidbody>(true))
                {
                    try { UnityEngine.Object.DestroyImmediate(rb); } catch { }
                }
                snap.SetActive(true);
                return snap;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] BuildVisualSnapshot failed: {ex.Message}");
                if (snap != null) UnityEngine.Object.Destroy(snap);
                return null;
            }
        }

        /// <summary>Portrait icon from a dressed clone via the Marketplace photo pipeline (Core
        /// NpcPhotoManager strips + poses + shoots internally — inactive clones are fine as-is).
        /// Null on failure — caller falls back to the default piece icon.</summary>
        internal static Sprite RenderIconFromClone(GameObject dressedClone)
        {
            try
            {
                var sprite = FiresCore.UI.NpcPhotoManager.MakeSprite(dressedClone);
                Debug.Log($"[NpcTemplateAppearance] icon render for '{dressedClone.name}': {(sprite != null ? "OK" : "NULL (photo manager declined)")}");
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcTemplateAppearance] RenderIconFromClone failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Encode a rendered sprite back to base64 PNG (reflection — direct ImageConversion
        /// reference breaks the net48 build).</summary>
        internal static string SpriteToBase64(Sprite sprite)
        {
            try
            {
                if (sprite == null || sprite.texture == null) return null;
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var encode = ic?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                byte[] png = encode != null ? encode.Invoke(null, new object[] { sprite.texture }) as byte[] : null;
                return png != null && png.Length > 0 ? Convert.ToBase64String(png) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// True when the stored icon actually shows something. Early piece-build renders captured a
        /// NAKED/EMPTY model and PERSISTED it — a poisoned blank that then blocked every backfill
        /// (which only runs when IconPng is empty). Sampling a coarse grid for opaque pixels lets those
        /// blanks read as "missing" so they regenerate.
        /// </summary>
        internal static bool IsMeaningfulIconPng(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return false;
            Texture2D tex = null;
            try
            {
                byte[] png = Convert.FromBase64String(base64);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var load = ic?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                bool ok = load != null && load.Invoke(null, new object[] { tex, png }) is bool b && b;
                if (!ok) return false;
                int opaque = 0;
                const int Grid = 16;
                for (int y = 0; y < Grid; y++)
                    for (int x = 0; x < Grid; x++)
                        if (tex.GetPixel(tex.width * x / Grid, tex.height * y / Grid).a > 0.1f) opaque++;
                return opaque >= 8;   // a real portrait fills far more than 8/256 samples
            }
            catch { return false; }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
