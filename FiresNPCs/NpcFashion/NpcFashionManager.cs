using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using FiresRPGmaker.UI;
using FiresRPGmaker.Utilities;

namespace FiresRPGmaker.NpcFashion
{
    /// <summary>
    /// Tag MonoBehaviour stamped onto an applied hair / beard child so that
    /// repeat calls to ApplyHairToNpc / ApplyBeardToNpc with the SAME item +
    /// color can short-circuit. This prevents the per-frame storm of identical
    /// re-applies that came from the 2.5s/4s/6s ApplyVisualEquipment chain plus
    /// the periodic remote-equipment-hash sync on non-owner clients.
    /// </summary>
    internal class NpcFashionAppliedTag : MonoBehaviour
    {
        public string ItemName;
        public string ColorString;
    }

    // Lightweight data model for NPC fashion/appearance customization
    [Serializable]
    public class NPCFashionData
    {
        public string LeftItem;
        public string RightItem;
        public string HelmetItem;
        public string ChestItem;
        public string LegsItem;
        public string CapeItem;
        public string HairItem;
        public string HairColor;
        public string ModelScale;
        public string LeftItemHidden;
        public string RightItemHidden;
        public string InteractAnimation;
        public string GreetAnimation;
        public string ByeAnimation;
        public string GreetText;
        public string ByeText;
        public string SkinColor;
        public string EyeColor;
        public string CraftingAnimation;
        public string BeardItem;
        public string BeardColor;
        public string InteractAudioClip;
        public string TextSize;
        public string TextHeight;
        public string PeriodicAnimation;
        public string PeriodicAnimationTime;
        public string PeriodicSound;
        public string PeriodicSoundTime;
        public string LookAtPlayer; // "true" or "false" - whether NPC looks at nearby players
    }

    // Predefined color options for hair/beard
    public static class HairBeardColors
    {
        public static readonly Dictionary<string, Color> ColorPresets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
         { "(none)", Color.white },
         { "Black", new Color(0.1f, 0.1f, 0.1f) },
         { "Dark Brown", new Color(0.2f, 0.15f, 0.1f) },
         { "Brown", new Color(0.4f, 0.3f, 0.2f) },
         { "Light Brown", new Color(0.6f, 0.5f, 0.4f) },
         { "Auburn", new Color(0.6f, 0.3f, 0.2f) },
         { "Red", new Color(0.7f, 0.2f, 0.1f) },
         { "Ginger", new Color(0.8f, 0.4f, 0.2f) },
         { "Strawberry Blonde", new Color(0.9f, 0.7f, 0.5f) },
         { "Blonde", new Color(0.9f, 0.8f, 0.6f) },
         { "Platinum Blonde", new Color(0.95f, 0.95f, 0.85f) },
         { "White", new Color(1f, 1f, 1f) },
         { "Gray", new Color(0.6f, 0.6f, 0.6f) },
         { "Silver", new Color(0.8f, 0.8f, 0.85f) },
         { "Blue", new Color(0.3f, 0.4f, 0.8f) },
         { "Purple", new Color(0.5f, 0.2f, 0.6f) },
         { "Pink", new Color(0.9f, 0.5f, 0.6f) },
         { "Green", new Color(0.2f, 0.6f, 0.3f) },
         { "Teal", new Color(0.2f, 0.6f, 0.6f) },
      };

        public static List<string> GetColorNames() => new List<string>(ColorPresets.Keys);

        public static Color GetColor(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "(none)") return Color.white;
            if (ColorPresets.TryGetValue(name, out var color)) return color;
            // Try parsing as hex or r,g,b
            return ParseColor(name);
        }

        private static Color ParseColor(string colorStr)
        {
            if (string.IsNullOrEmpty(colorStr)) return Color.white;

            // Try parsing as hex (#RRGGBB or RRGGBB)
            if (colorStr.StartsWith("#"))
                colorStr = colorStr.Substring(1);

            if (colorStr.Length == 6 &&
             int.TryParse(colorStr.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) &&
                    int.TryParse(colorStr.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) &&
        int.TryParse(colorStr.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
            {
                return new Color(r / 255f, g / 255f, b / 255f);
            }

            // Try parsing as "r,g,b" format
            var parts = colorStr.Split(',');
            if (parts.Length >= 3 &&
         float.TryParse(parts[0].Trim(), out float fr) &&
      float.TryParse(parts[1].Trim(), out float fg) &&
           float.TryParse(parts[2].Trim(), out float fb))
            {
                if (fr > 1f || fg > 1f || fb > 1f)
                {
                    fr /= 255f;
                    fg /= 255f;
                    fb /= 255f;
                }
                return new Color(fr, fg, fb);
            }

            return Color.white;
        }
    }

    // Predefined skin tone colors
    public static class SkinToneColors
    {
        public static readonly Dictionary<string, Color> ColorPresets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
{
{ "(none)", new Color(1f, 0.87f, 0.77f) }, // Default light skin
          { "Pale", new Color(1f, 0.94f, 0.91f) },
            { "Fair", new Color(1f, 0.89f, 0.82f) },
            { "Light", new Color(1f, 0.87f, 0.77f) },
            { "Medium Light", new Color(0.96f, 0.80f, 0.69f) },
         { "Medium", new Color(0.87f, 0.72f, 0.58f) },
            { "Olive", new Color(0.80f, 0.68f, 0.50f) },
   { "Medium Dark", new Color(0.72f, 0.55f, 0.40f) },
    { "Tan", new Color(0.82f, 0.64f, 0.48f) },
    { "Brown", new Color(0.62f, 0.44f, 0.31f) },
       { "Dark Brown", new Color(0.45f, 0.30f, 0.20f) },
            { "Deep", new Color(0.36f, 0.24f, 0.17f) },
      { "Ebony", new Color(0.24f, 0.16f, 0.12f) },
            // Fantasy skin tones
            { "Ashen", new Color(0.75f, 0.75f, 0.78f) },
  { "Corpse", new Color(0.65f, 0.70f, 0.65f) },
            { "Orc Green", new Color(0.45f, 0.55f, 0.35f) },
            { "Goblin", new Color(0.55f, 0.65f, 0.40f) },
    { "Troll Blue", new Color(0.50f, 0.55f, 0.65f) },
{ "Demon Red", new Color(0.70f, 0.35f, 0.30f) },
       { "Undead Purple", new Color(0.55f, 0.45f, 0.55f) },
            { "Frost", new Color(0.80f, 0.85f, 0.95f) },
    };

        public static List<string> GetColorNames() => new List<string>(ColorPresets.Keys);

        public static Color GetColor(string name)
        {
  if (string.IsNullOrEmpty(name) || name == "(none)")
                return ColorPresets["(none)"]; // Return default skin tone
  if (ColorPresets.TryGetValue(name, out var color)) return color;
        return ColorPresets["(none)"];
     }
    }

    // Predefined eye color options
    public static class EyeColors
    {
        public static readonly Dictionary<string, Color> ColorPresets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
      { "(none)", new Color(0.4f, 0.3f, 0.2f) }, // Default brown
        { "Brown", new Color(0.4f, 0.3f, 0.2f) },
            { "Dark Brown", new Color(0.25f, 0.15f, 0.1f) },
            { "Hazel", new Color(0.55f, 0.45f, 0.25f) },
    { "Amber", new Color(0.7f, 0.5f, 0.2f) },
            { "Green", new Color(0.3f, 0.5f, 0.3f) },
          { "Blue", new Color(0.3f, 0.5f, 0.7f) },
            { "Light Blue", new Color(0.5f, 0.7f, 0.9f) },
        { "Gray", new Color(0.5f, 0.55f, 0.6f) },
       { "Ice Blue", new Color(0.7f, 0.85f, 0.95f) },
    // Fantasy eye colors
    { "Gold", new Color(0.85f, 0.7f, 0.2f) },
            { "Silver", new Color(0.75f, 0.78f, 0.82f) },
         { "Violet", new Color(0.5f, 0.3f, 0.6f) },
            { "Red", new Color(0.7f, 0.15f, 0.1f) },
            { "Crimson", new Color(0.6f, 0.1f, 0.15f) },
  { "Orange", new Color(0.85f, 0.4f, 0.1f) },
    { "Yellow", new Color(0.9f, 0.85f, 0.2f) },
            { "White", new Color(0.95f, 0.95f, 0.95f) },
            { "Black", new Color(0.1f, 0.1f, 0.1f) },
            { "Glowing Blue", new Color(0.3f, 0.6f, 1f) },
 { "Glowing Green", new Color(0.3f, 1f, 0.4f) },
        };

        public static List<string> GetColorNames() => new List<string>(ColorPresets.Keys);

        public static Color GetColor(string name)
        {
       if (string.IsNullOrEmpty(name) || name == "(none)")
return ColorPresets["(none)"];
            if (ColorPresets.TryGetValue(name, out var color)) return color;
            return ColorPresets["(none)"];
        }
    }

    public static class NpcFashionManager
    {
        // Cache of hair and beard prefabs found in player model
        private static Dictionary<string, GameObject> _hairPrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, GameObject> _beardPrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static bool _prefabCacheInitialized = false;

        // Per-prefab attach_skin/attach child lookup cache. The attach child is
        // a fixed property of the hair/beard prefab â€” finding it once and
        // memoizing avoids the linear scan of every prefab's children on every
        // companion spawn. Populated on first miss; never invalidated (the
        // prefabs themselves don't change at runtime).
        private static readonly Dictionary<int, GameObject> _attachChildCache = new Dictionary<int, GameObject>();

        // Applies serialized fashion JSON to an NPC: sets equipment and attaches an NpcFashionComponent
        public static void ApplyFashionJsonToNpc(string json, NpcController npc)
        {
            if (npc == null || string.IsNullOrEmpty(json)) return;
            try
            {
                var data = JsonConvert.DeserializeObject<NPCFashionData>(json);
                if (data == null) return;

                Debug.Log($"[NpcFashionManager] Applying fashion JSON to NPC {npc.name}");
                Debug.Log($"[NpcFashionManager] Hair: {data.HairItem}, Beard: {data.BeardItem}");
                Debug.Log($"[NpcFashionManager] PeriodicAnim: {data.PeriodicAnimation}, Time: {data.PeriodicAnimationTime}");

                // Map items to npc equip fields
                if (!string.IsNullOrEmpty(data.HelmetItem)) npc.equipHelmet = data.HelmetItem;
                if (!string.IsNullOrEmpty(data.ChestItem)) npc.equipChest = data.ChestItem;
                if (!string.IsNullOrEmpty(data.LegsItem)) npc.equipLegs = data.LegsItem;
                if (!string.IsNullOrEmpty(data.CapeItem)) npc.equipShoulder = data.CapeItem;
                if (!string.IsNullOrEmpty(data.LeftItem)) npc.equipLeftHand = data.LeftItem;
                if (!string.IsNullOrEmpty(data.RightItem)) npc.equipRightHand = data.RightItem;

                // Attach or update the fashion component which will handle animations/periodic behaviour
                var comp = npc.GetComponent<NpcFashionComponent>();
                if (comp == null) comp = npc.gameObject.AddComponent<NpcFashionComponent>();

                comp.HelmetItem = data.HelmetItem;
                comp.ChestItem = data.ChestItem;
                comp.LegsItem = data.LegsItem;
                comp.CapeItem = data.CapeItem;
                comp.LeftItem = data.LeftItem;
                comp.RightItem = data.RightItem;

                // Hair and beard
                comp.HairItem = data.HairItem;
                comp.HairColor = data.HairColor;
                comp.BeardItem = data.BeardItem;
                comp.BeardColor = data.BeardColor;

                // Skin color
                comp.SkinColor = data.SkinColor;

                // Eye color
                comp.EyeColor = data.EyeColor;

                comp.GreetAnimation = data.GreetAnimation;
                comp.ByeAnimation = data.ByeAnimation;
                comp.InteractAnimation = data.InteractAnimation;

                comp.PeriodicAnimation = data.PeriodicAnimation;
                comp.PeriodicAnimationTime = ParseFloatOrDefault(data.PeriodicAnimationTime, 0f);

                comp.PeriodicSound = data.PeriodicSound;
                comp.PeriodicSoundTime = ParseFloatOrDefault(data.PeriodicSoundTime, 0f);

                comp.ModelScale = ParseFloatOrDefault(data.ModelScale, 1f);

                comp.Apply();

                // Initialize prefab cache if needed
                InitializePrefabCache();

                // Apply hair/beard visuals - use HairBeardColors for color lookup
                var hairColor = HairBeardColors.GetColor(data.HairColor);
                var beardColor = HairBeardColors.GetColor(data.BeardColor);
                ApplyHairToNpc(npc, data.HairItem, ColorToString(hairColor));
                ApplyBeardToNpc(npc, data.BeardItem, ColorToString(beardColor));

                // Apply skin color
if (!string.IsNullOrEmpty(data.SkinColor) && data.SkinColor != "(none)")
   {
       var skinColor = SkinToneColors.GetColor(data.SkinColor);
    ApplySkinColorToNpc(npc, skinColor);
   }

    // Apply eye color (attempts to find eye materials - may not work on all models)
  if (!string.IsNullOrEmpty(data.EyeColor) && data.EyeColor != "(none)")
    {
        var eyeColor = EyeColors.GetColor(data.EyeColor);
 ApplyEyeColorToNpc(npc, eyeColor);
    }

 // Apply model scale - ALWAYS apply to ensure changes take effect immediately
     // even when resetting from a larger scale back to 1.0
     ApplyModelScale(npc, comp.ModelScale);

      // Configure look-at behavior using Valheim's LookAt component
   bool lookAtPlayer = !string.IsNullOrEmpty(data.LookAtPlayer) && 
   data.LookAtPlayer.Equals("true", StringComparison.OrdinalIgnoreCase);
    comp.LookAtPlayer = lookAtPlayer;
    ConfigureLookAtComponent(npc, lookAtPlayer);

            // Suppress LookAt errors on equipped items (torches, etc.)
            SuppressLookAtErrors(npc);

                // Clean up any player-specific effects (dev_smoke, etc.)
                CleanupPlayerModelEffects(npc.gameObject);

                // Refresh visuals
                npc.ApplyVisualEquipment();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Failed to apply fashion json: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a Color to a string representation for storage.
        /// </summary>
        public static string ColorToString(Color color)
        {
            if (color == Color.white) return null;
            return $"{color.r:F2},{color.g:F2},{color.b:F2}";
        }

        /// <summary>
        /// Suppresses LookAt rotation errors on equipped items that have look-at components.
        /// The "Look rotation viewing vector is zero" error occurs when items like torches
        /// have LookAt scripts trying to face the camera/player with zero direction vectors.
        /// </summary>
        private static void SuppressLookAtErrors(NpcController npc)
        {
            if (npc == null) return;

            try
            {
                // Find all light components and their look-at behaviors
                foreach (var light in npc.GetComponentsInChildren<Light>(true))
                {
                    if (light == null) continue;

                    // Disable any LookAt or Billboard components that cause the error
                    var lookAtComponents = light.GetComponents<MonoBehaviour>();
                    foreach (var comp in lookAtComponents)
                    {
                        if (comp == null) continue;
                        var typeName = comp.GetType().Name;
                        if (typeName.Contains("LookAt") || typeName.Contains("Billboard") || typeName.Contains("FaceCamera"))
                        {
                            comp.enabled = false;
                            Debug.Log($"[NpcFashionManager] Disabled {typeName} on {light.gameObject.name} to suppress rotation errors");
                        }
                    }
                }

                // Also check for LookAt components on the equipped items themselves
                foreach (Transform child in npc.transform)
                {
                    if (child == null) continue;
                    if (child.name.StartsWith("Equipped_"))
                    {
                        foreach (var mono in child.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (mono == null) continue;
                            var typeName = mono.GetType().Name;
                            if (typeName.Contains("LookAt") || typeName.Contains("Billboard") || typeName.Contains("FaceCamera") ||
                                   typeName.Contains("LookRotation"))
                            {
                                mono.enabled = false;
                                Debug.Log($"[NpcFashionManager] Disabled {typeName} on {mono.gameObject.name}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Error suppressing LookAt: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the prefab cache by scanning for hair and beard prefabs.
        /// </summary>
        private static void InitializePrefabCache()
        {
            if (_prefabCacheInitialized) return;
            _prefabCacheInitialized = true;

            try
            {
                // Scan ObjectDB for hair/beard prefabs
                if (ObjectDB.instance != null)
                {
                    foreach (var itemPrefab in ObjectDB.instance.m_items)
                    {
                        if (itemPrefab == null) continue;
                        var name = itemPrefab.name;
                        if (string.IsNullOrEmpty(name)) continue;

                        var nameLower = name.ToLowerInvariant();
                        if (nameLower.Contains("hair") && !_hairPrefabCache.ContainsKey(name))
                        {
                            _hairPrefabCache[name] = itemPrefab;
                        }
                        else if (nameLower.Contains("beard") && !_beardPrefabCache.ContainsKey(name))
                        {
                            _beardPrefabCache[name] = itemPrefab;
                        }
                    }
                }

                // Scan ZNetScene for hair/beard prefabs
                if (ZNetScene.instance != null)
                {
                    foreach (var prefab in ZNetScene.instance.m_prefabs)
                    {
                        if (prefab == null) continue;
                        var name = prefab.name;
                        if (string.IsNullOrEmpty(name)) continue;

                        var nameLower = name.ToLowerInvariant();
                        if (nameLower.Contains("hair") && !_hairPrefabCache.ContainsKey(name))
                        {
                            _hairPrefabCache[name] = prefab;
                        }
                        else if (nameLower.Contains("beard") && !_beardPrefabCache.ContainsKey(name))
                        {
                            _beardPrefabCache[name] = prefab;
                        }
                    }
                }

                Debug.Log($"[NpcFashionManager] Initialized prefab cache: {_hairPrefabCache.Count} hair, {_beardPrefabCache.Count} beard prefabs");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Error initializing prefab cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a hair or beard prefab by name from cache or ObjectDB.
        /// </summary>
        private static GameObject FindCustomizationPrefab(string itemName, bool isHair)
        {
            if (string.IsNullOrEmpty(itemName) || itemName == "(none)") return null;

            // Check cache first
            var cache = isHair ? _hairPrefabCache : _beardPrefabCache;
            if (cache.TryGetValue(itemName, out var cachedPrefab) && cachedPrefab != null)
            {
                return cachedPrefab;
            }

            // Try ObjectDB
            var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
            if (prefab != null)
            {
                cache[itemName] = prefab;
                return prefab;
            }

            // Try ZNetScene
            prefab = ZNetScene.instance?.GetPrefab(itemName);
            if (prefab != null)
            {
                cache[itemName] = prefab;
                return prefab;
            }

            // Try Resources
            prefab = Resources.Load<GameObject>(itemName);
            if (prefab != null)
            {
                cache[itemName] = prefab;
                return prefab;
            }

            Debug.LogWarning($"[NpcFashionManager] Could not find {(isHair ? "hair" : "beard")} prefab: {itemName}");
            return null;
        }

        /// <summary>
        /// Applies a hair item to the NPC by instantiating the hair mesh.
        /// Hair in Valheim uses SkinnedMeshRenderer bound to head bones.
        /// </summary>
        /// <summary>NpcController shim ï¿½ delegates to the GameObject overload.</summary>
        public static void ApplyHairToNpc(NpcController npc, string hairItem, string hairColor = null)
        {
            if (npc == null) return;
            ApplyHairToNpc(npc.gameObject, hairItem, hairColor);
        }

        /// <summary>
        /// Applies a hair item to any GameObject (static NPC, wild companion, hammer-placed companion)
        /// by cloning the prefab and rebinding bones directly. Does NOT rely on VisEquipment.
        /// </summary>
        public static void ApplyHairToNpc(GameObject go, string hairItem, string hairColor = null)
        {
            if (go == null) return;

            if (string.IsNullOrEmpty(hairItem) || hairItem == "(none)" || hairItem == "HairNone")
            {
                if (HasAnyAppliedHair(go))
                {
                    Debug.Log($"[NpcFashionManager] Clearing hair ('{hairItem}' selected)");
                    ClearHairFromNpc(go);
                }
                return;
            }

            // DEDUPE: skip if the exact same hair+color is already applied.
            if (IsAlreadyApplied(go, $"NPC_Hair_{hairItem}", hairItem, hairColor))
                return;

            try
            {
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] ApplyHairToNpc called for '{hairItem}' on {go.name} | colorOverride={(hairColor ?? "<null>")}");

                InitializePrefabCache();
                ClearHairFromNpc(go);

                var hairPrefab = FindCustomizationPrefab(hairItem, true);
                if (hairPrefab == null)
                {
                    Debug.LogWarning($"[NpcFashionManager] Hair prefab not found: {hairItem}");
                    return;
                }

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Found hair prefab: {hairPrefab.name}");

                Transform visual = go.transform.Find("Visual") ?? go.transform;

                if (TryApplySkinnedMeshToNpc(hairPrefab, "NPC_Hair_", hairItem, visual, hairColor))
                {
                    StampAppliedTag(visual, $"NPC_Hair_{hairItem}", hairItem, hairColor);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager] Successfully applied hair '{hairItem}' as skinned mesh to {go.name}");
                    return;
                }

                ApplyHairFallback(go.name, visual, hairPrefab, hairItem, hairColor);
                StampAppliedTag(visual, $"NPC_Hair_{hairItem}", hairItem, hairColor);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NpcFashionManager] Failed to apply hair to {go.name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Fallback method to attach hair directly to head bone (for non-skinned hair).
        /// </summary>
        private static void ApplyHairFallback(string ownerName, Transform visual, GameObject hairPrefab, string hairItem, string hairColor)
        {
            // Find head bone
            Transform headAttach = FindTransformRecursive(visual, "Head")
               ?? FindTransformRecursive(visual, "Helmet_attach");

            if (headAttach == null)
            {
                Debug.LogError($"[NpcFashionManager] No head bone found on {ownerName}! Hair cannot be attached.");
                return;
            }

            // Find the visual to clone
            GameObject visualToClone = FindAttachVisual(hairPrefab);
            if (visualToClone == null)
            {
                Debug.LogWarning($"[NpcFashionManager] No visual found in hair prefab: {hairItem}");
                return;
            }

            // Instantiate the hair visual
            var hairGO = UnityEngine.Object.Instantiate(visualToClone, headAttach, false);
            hairGO.name = $"NPC_Hair_{hairItem}";
            hairGO.transform.localPosition = Vector3.zero;
            hairGO.transform.localRotation = Quaternion.identity;
            hairGO.transform.localScale = Vector3.one;

            // Strip unwanted components
            StripUnwantedComponents(hairGO);

            // Fix shaders first, then tint.
            try
            {
                FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(hairGO, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Shader replacement failed for hair: {ex.Message}");
            }

            // Apply hair color after shader is in final state.
            if (!string.IsNullOrEmpty(hairColor))
            {
                ApplyColorToRenderers(hairGO, ParseColor(hairColor));
            }

            // Enable all renderers
            EnableRenderers(hairGO);
            hairGO.SetActive(true);

            Debug.Log($"[NpcFashionManager] Applied hair '{hairItem}' to {ownerName} via fallback method");
        }

        /// <summary>
        /// Applies a beard item to the NPC by instantiating the beard mesh.
        /// </summary>
        /// <summary>NpcController shim ï¿½ delegates to the GameObject overload.</summary>
        public static void ApplyBeardToNpc(NpcController npc, string beardItem, string beardColor = null)
        {
            if (npc == null) return;
            ApplyBeardToNpc(npc.gameObject, beardItem, beardColor);
        }

        /// <summary>
        /// Applies a beard item to any GameObject by cloning the prefab and rebinding bones directly.
        /// Does NOT rely on VisEquipment. Beards should only be passed for male companions.
        /// </summary>
        public static void ApplyBeardToNpc(GameObject go, string beardItem, string beardColor = null)
        {
            if (go == null) return;

            if (string.IsNullOrEmpty(beardItem) || beardItem == "(none)" || beardItem == "BeardNone")
            {
                if (HasAnyAppliedBeard(go))
                {
                    Debug.Log($"[NpcFashionManager] Clearing beard ('{beardItem}' selected)");
                    ClearBeardFromNpc(go);
                }
                return;
            }

            // DEDUPE: skip if the exact same beard+color is already applied.
            if (IsAlreadyApplied(go, $"NPC_Beard_{beardItem}", beardItem, beardColor))
                return;

            try
            {
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] ApplyBeardToNpc called for '{beardItem}' on {go.name}");

                InitializePrefabCache();
                ClearBeardFromNpc(go);

                var beardPrefab = FindCustomizationPrefab(beardItem, false);
                if (beardPrefab == null)
                {
                    Debug.LogWarning($"[NpcFashionManager] Beard prefab not found: {beardItem}");
                    return;
                }

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Found beard prefab: {beardPrefab.name}");

                Transform visual = go.transform.Find("Visual") ?? go.transform;

                if (TryApplySkinnedMeshToNpc(beardPrefab, "NPC_Beard_", beardItem, visual, beardColor))
                {
                    StampAppliedTag(visual, $"NPC_Beard_{beardItem}", beardItem, beardColor);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager] Successfully applied beard '{beardItem}' as skinned mesh to {go.name}");
                    return;
                }

                ApplyBeardFallback(go.name, visual, beardPrefab, beardItem, beardColor);
                StampAppliedTag(visual, $"NPC_Beard_{beardItem}", beardItem, beardColor);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NpcFashionManager] Failed to apply beard to {go.name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Fallback method to attach beard directly to head bone.
        /// </summary>
        private static void ApplyBeardFallback(string ownerName, Transform visual, GameObject beardPrefab, string beardItem, string beardColor)
        {
            Transform headAttach = FindTransformRecursive(visual, "Head")
                ?? FindTransformRecursive(visual, "Helmet_attach");

            if (headAttach == null)
            {
                Debug.LogError($"[NpcFashionManager] No head bone found on {ownerName}! Beard cannot be attached.");
                return;
            }

            GameObject visualToClone = FindAttachVisual(beardPrefab);
            if (visualToClone == null)
            {
                Debug.LogWarning($"[NpcFashionManager] No visual found in beard prefab: {beardItem}");
                return;
            }

            var beardGO = UnityEngine.Object.Instantiate(visualToClone, headAttach, false);
            beardGO.name = $"NPC_Beard_{beardItem}";
            beardGO.transform.localPosition = Vector3.zero;
            beardGO.transform.localRotation = Quaternion.identity;
            beardGO.transform.localScale = Vector3.one;

            StripUnwantedComponents(beardGO);

            // Fix shaders first, then tint.
            try
            {
                FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(beardGO, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Shader replacement failed for beard: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(beardColor))
            {
                ApplyColorToRenderers(beardGO, ParseColor(beardColor));
            }

            EnableRenderers(beardGO);
            beardGO.SetActive(true);

            Debug.Log($"[NpcFashionManager] Applied beard '{beardItem}' to {ownerName} via fallback method");
        }

        /// <summary>
        /// Finds the NPC's body SkinnedMeshRenderer via NpcVisEquipment.VisEquipment.m_bodyModel.
        /// This is the authoritative body mesh ï¿½ Valheim uses it as the bone source and the
        /// primary material target for _HairColor / _SkinColor in VisEquipment.UpdateColors().
        /// </summary>
        private static SkinnedMeshRenderer GetBodyModel(Transform visualRoot)
        {
            var npcVE = visualRoot.GetComponentInParent<FiresCore.Npc.NpcVisEquipment>();
            if (npcVE?.VisEquipment?.m_bodyModel != null)
            {
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] GetBodyModel: found via NpcVisEquipment on {npcVE.gameObject.name}, bodyModel={npcVE.VisEquipment.m_bodyModel.gameObject.name}");
                return npcVE.VisEquipment.m_bodyModel;
            }
            Debug.LogWarning($"[NpcFashionManager] GetBodyModel: NOT FOUND for visualRoot={visualRoot.name} ï¿½ color will only be applied to hair mesh material");
            return null;
        }

        /// <summary>
        /// Applies hair/beard color the same way Valheim's VisEquipment.UpdateColors() does:
        ///   1. Set _HairColor on every material of the hair/beard mesh (with explicit array reassign).
        ///   2. Set _HairColor on the NPC body material so the shader tints correctly from both sides.
        /// </summary>
        private static void ApplyHairColorToMeshAndBody(
            IEnumerable<SkinnedMeshRenderer> meshRenderers,
            SkinnedMeshRenderer bodyModel,
            Color color)
        {
            // NOTE: DO NOT use mat.HasProperty("_HairColor") ï¿½ in Valheim's Custom/Player shader
            // _HairColor is set programmatically and is NOT declared in the Properties {} block,
            // so HasProperty always returns false. Valheim's own VisEquipment.UpdateColors() calls
            // mat.SetColor("_HairColor", color) unconditionally with no HasProperty guard.
            if (FiresLogger.VerboseEnabled)
                Debug.Log($"[NpcFashionManager] ApplyHairColorToMeshAndBody: color={color}, bodyModel={(bodyModel != null ? bodyModel.gameObject.name : "<null>")}");

            // Step 1 ï¿½ hair/beard mesh materials (mirrors VisEquipment.UpdateColors())
            foreach (var smr in meshRenderers)
            {
                if (smr == null) continue;
                var mats = smr.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    mats[i].SetColor("_HairColor", color);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager]   SET _HairColor={color} on {mats[i].name} (shader: {mats[i].shader?.name})");
                }
                smr.materials = mats;
            }

            // Step 2 ï¿½ body material (VisEquipment.UpdateColors also sets _HairColor on baseMaterial)
            if (bodyModel != null)
            {
                var bodyMat = bodyModel.material;
                if (bodyMat != null)
                {
                    bodyMat.SetColor("_HairColor", color);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager]   SET body _HairColor={color} on {bodyMat.name}");
                }
            }
        }

        /// <summary>
        /// Resolves the attach_skin / attach child of a hair or beard prefab,
        /// memoized per prefab in <see cref="_attachChildCache"/>. The first
        /// call for a given prefab walks its immediate children (and falls
        /// back to GetComponentInChildren&lt;SkinnedMeshRenderer&gt; if neither
        /// "attach_skin" nor "attach" exists). Subsequent calls are O(1).
        /// Returns null if neither lookup yields anything.
        /// </summary>
        private static GameObject GetCachedAttachChild(GameObject prefab)
        {
            if (prefab == null) return null;

            int key = prefab.GetInstanceID();
            if (_attachChildCache.TryGetValue(key, out var cached))
                return cached;

            GameObject attachChild = null;
            foreach (Transform child in prefab.transform)
            {
                if (child.name == "attach_skin" || child.name == "attach")
                {
                    attachChild = child.gameObject;
                    break;
                }
            }
            if (attachChild == null)
            {
                var anySMR = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (anySMR != null) attachChild = anySMR.gameObject;
            }

            // Cache the result, including null â€” a prefab that has neither
            // child will keep failing the same way; no point re-scanning.
            _attachChildCache[key] = attachChild;
            return attachChild;
        }

        /// <summary>
        /// Attaches a hair or beard prefab as a skinned mesh bound to the NPC's skeleton.
        /// Follows the Balrond / vanilla-Valheim VisEquipment.AttachHair() pattern:
        ///   ï¿½ find the attach_skin child specifically
        ///   ï¿½ parent to bodyModel.transform.parent (sibling of the body mesh)
        ///   ï¿½ copy rootBone + bones array directly from the body model
        ///   ï¿½ apply _HairColor on both the hair mesh material AND the body material
        /// </summary>
        private static bool TryApplySkinnedMeshToNpc(
            GameObject prefab, string namePrefix, string itemName,
            Transform visualRoot, string colorOverride)
        {
            // --- 1. Find attach_skin (Balrond / Valheim approach) ---
            // Memoized per-prefab â€” see _attachChildCache. This used to scan the
            // hair/beard prefab's children on every companion spawn.
            GameObject attachChild = GetCachedAttachChild(prefab);
            if (attachChild == null)
            {
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] No attach_skin/SMR found in {itemName}, trying fallback");
                return false;
            }

            // --- 2. Body model (bone source + secondary color target) ---
            SkinnedMeshRenderer bodyModel = GetBodyModel(visualRoot);

            try
            {
                // --- 3. Instantiate as sibling of body mesh (Balrond approach) ---
                Transform parent = bodyModel != null ? bodyModel.transform.parent : visualRoot;
                var clonedGO = UnityEngine.Object.Instantiate(attachChild, parent, false);
                clonedGO.name = $"{namePrefix}{itemName}";
                clonedGO.transform.localPosition = Vector3.zero;
                clonedGO.transform.localRotation = Quaternion.identity;
                clonedGO.transform.localScale    = Vector3.one;

                var smrs = clonedGO.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                {
                    UnityEngine.Object.Destroy(clonedGO);
                    return false;
                }

                // Strip Cloth components from the cloned tree BEFORE bone re-binding.
                // Some hair / beard prefabs (long flowing hair, capes) ship with a
                // Cloth on the SMR for sway physics. Cloth's per-vertex skin
                // coefficients are baked against the prefab's ORIGINAL skeleton â€”
                // step 4 below reassigns smr.bones to the body skeleton, which makes
                // those coefficients meaningless and forces Unity to disable the
                // Cloth on the next frame with a per-NPC log line:
                //   "Unable skin the associated mesh with bone count: 53,
                //    Cloth component disabled."
                // We don't want hair/beard cloth physics on NPCs anyway (they're not
                // wired to the player wind/skin-coefficient setup), so kill them up
                // front. StripUnwantedComponents below only walks root components,
                // so it can't see Cloth on child SMRs.
                //
                // Belt-and-braces: disable first (so any Update queued for this
                // frame becomes a no-op), then DestroyImmediate so the component
                // is gone before LateUpdate can try to skin a now-bone-mismatched
                // mesh. The diagnostic log lets us confirm in field reports
                // whether this branch actually fires for a given hair prefab.
                var clothsOnClone = clonedGO.GetComponentsInChildren<Cloth>(true);
                if (clothsOnClone.Length > 0)
                {
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager] Stripping {clothsOnClone.Length} Cloth component(s) from cloned {itemName} before bone re-bind");
                    foreach (var cloth in clothsOnClone)
                    {
                        if (cloth == null) continue;
                        cloth.enabled = false;
                        UnityEngine.Object.DestroyImmediate(cloth);
                    }
                }

                // --- 4. Bind bones (Balrond approach: copy directly from bodyModel) ---
                int bonesFound = 0;
                if (bodyModel != null)
                {
                    foreach (var smr in smrs)
                    {
                        smr.rootBone          = bodyModel.rootBone;
                        smr.bones             = bodyModel.bones;
                        smr.localBounds       = bodyModel.localBounds;
                        smr.updateWhenOffscreen = true;
                    }
                    bonesFound = bodyModel.bones?.Length ?? 0;
                }
                else
                {
                    // Fallback: name-based bone search (original path)
                    var sourceSMR = attachChild.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (sourceSMR != null)
                    {
                        foreach (var smr in smrs)
                        {
                            var src = sourceSMR.bones;
                            var dst = new Transform[src.Length];
                            for (int i = 0; i < src.Length; i++)
                            {
                                if (src[i] == null) continue;
                                var found = FindTransformRecursive(visualRoot, src[i].name);
                                dst[i] = found ?? visualRoot;
                                if (found != null) bonesFound++;
                            }
                            smr.bones             = dst;
                            smr.updateWhenOffscreen = true;
                            if (sourceSMR.rootBone != null)
                                smr.rootBone = FindTransformRecursive(visualRoot, sourceSMR.rootBone.name) ?? visualRoot;
                        }
                    }
                }

                StripUnwantedComponents(clonedGO);

                // --- 5. Replace the hair mesh's material with an instanced copy of the ---
                // body material, EXACTLY like VisEquipment.AttachHair() does.
                // Custom/Player's _HairColor tinting only works when the mesh carries
                // the body's texture atlas (which has the hair-area mask). The prefab's
                // own "PlayerHair" material has no mask, so _HairColor has zero visual
                // effect on it regardless of what value we write.
                if (bodyModel != null && bodyModel.sharedMaterial != null)
                {
                    foreach (var smr in smrs)
                    {
                        // new Material() creates an independent instance so each NPC
                        // gets their own copy ï¿½ setting _HairColor won't bleed across NPCs.
                        smr.sharedMaterial = new Material(bodyModel.sharedMaterial);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[NpcFashionManager] Assigned body material copy to {smr.name}");
                    }
                }

                // --- 6. Shader replacement is now a no-op (material is already the body ---
                // material which has the real compiled Custom/Player shader), but keep the
                // call as a safety net for any non-body-material fallback paths.
                try { FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(clonedGO, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader); } catch { }

                // --- 7. Apply color using the EXACT Valheim UpdateColors() pattern ---
                if (!string.IsNullOrEmpty(colorOverride))
                    ApplyHairColorToMeshAndBody(smrs, bodyModel, ParseColor(colorOverride));

                EnableRenderers(clonedGO);
                clonedGO.SetActive(true);

                int totalBones = bodyModel?.bones?.Length ?? smrs[0]?.bones?.Length ?? 0;
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Applied {itemName} as skinned mesh (bones: {bonesFound}/{totalBones})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] TryApplySkinnedMeshToNpc failed for {itemName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds the visual/attach child in a prefab.
        /// </summary>
        private static GameObject FindAttachVisual(GameObject prefab)
        {
            // Look for attach child first
            foreach (Transform child in prefab.transform)
            {
                if (child.name.StartsWith("attach", StringComparison.OrdinalIgnoreCase))
                {
                    return child.gameObject;
                }
            }

            // Look for a child with a renderer
            foreach (Transform child in prefab.transform)
            {
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                    return child.gameObject;

                renderer = child.GetComponentInChildren<Renderer>(true);
                if (renderer != null)
                    return child.gameObject;
            }

            // Use the prefab itself if it has renderers
            if (prefab.GetComponentInChildren<Renderer>(true) != null)
                return prefab;

            return null;
        }

        /// <summary>Clears any existing hair from the NPC. NpcController shim.</summary>
        public static void ClearHairFromNpc(NpcController npc)
        {
            if (npc == null) return;
            ClearHairFromNpc(npc.gameObject);
        }

        /// <summary>Clears any existing hair from a GameObject (works for all companion types).</summary>
        public static void ClearHairFromNpc(GameObject go)
        {
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("NPC_Hair_"))
                    UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        // --- Dedupe helpers --------------------------------------------------
        // Each applied hair / beard child has a tiny NpcFashionAppliedTag stamped
        // on it recording (item, color). Re-applies with identical state are
        // skipped, which kills the storm of redundant ApplyHairToNpc calls
        // produced by RestoreModelStateFromZDO being invoked at 1s, 2.5s, 4s,
        // and 6s plus on every remote-equipment-hash-change tick.

        private static bool IsAlreadyApplied(GameObject go, string childName, string item, string color)
        {
            if (go == null) return false;
            var t = FindChildByName(go.transform, childName);
            if (t == null) return false;
            var tag = t.GetComponent<NpcFashionAppliedTag>();
            if (tag == null) return false;
            return tag.ItemName == item && tag.ColorString == (color ?? string.Empty);
        }

        private static void StampAppliedTag(Transform visualRoot, string childName, string item, string color)
        {
            if (visualRoot == null) return;
            var t = FindChildByName(visualRoot, childName);
            if (t == null) return;
            var tag = t.GetComponent<NpcFashionAppliedTag>() ?? t.gameObject.AddComponent<NpcFashionAppliedTag>();
            tag.ItemName = item;
            tag.ColorString = color ?? string.Empty;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == name) return t;
            }
            return null;
        }

        private static bool HasAnyAppliedHair(GameObject go)
        {
            if (go == null) return false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("NPC_Hair_")) return true;
            }
            return false;
        }

        private static bool HasAnyAppliedBeard(GameObject go)
        {
            if (go == null) return false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("NPC_Beard_")) return true;
            }
            return false;
        }

        /// <summary>Clears any existing beard from the NPC. NpcController shim.</summary>
        public static void ClearBeardFromNpc(NpcController npc)
        {
            if (npc == null) return;
            ClearBeardFromNpc(npc.gameObject);
        }

        /// <summary>Clears any existing beard from a GameObject (works for all companion types).</summary>
        public static void ClearBeardFromNpc(GameObject go)
        {
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("NPC_Beard_"))
                    UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        /// <summary>
        /// Applies model scale to the NPC.
        /// </summary>
        public static void ApplyModelScale(NpcController npc, float scale)
        {
            if (npc == null) return;

        // If scale is 0 or very close to 0, reset to default (1.0)
        if (scale <= 0.01f)
       {
     scale = 1f;
          }

   Transform visual = npc.transform.Find("Visual");
            if (visual != null)
{
       visual.localScale = Vector3.one * scale;
     }
    else
      {
     npc.transform.localScale = Vector3.one * scale;
     }

  Debug.Log($"[NpcFashionManager] Applied model scale {scale} to NPC {npc.name}");
 }

   /// <summary>
        /// Resets model scale to default (1.0) immediately.
        /// </summary>
        public static void ResetModelScale(NpcController npc)
    {
       if (npc == null) return;
    ApplyModelScale(npc, 1f);
 }

        /// <summary>
        /// Configures the LookAt component on an NPC to look at nearby players.
   /// Uses Valheim's built-in LookAt component (same as Haldor/Hildr traders).
      /// </summary>
  public static void ConfigureLookAtComponent(NpcController npc, bool enabled)
     {
   if (npc == null) return;

         try
      {
   // Find or add the LookAt component
  var lookAt = npc.GetComponentInChildren<LookAt>(true);
       
    if (enabled)
    {
        if (lookAt == null)
{
        // Add LookAt component to the NPC
     // It should be on the Visual or a child that has the head bone
         Transform visual = npc.transform.Find("Visual") ?? npc.transform;
   lookAt = visual.gameObject.AddComponent<LookAt>();
       Debug.Log($"[NpcFashionManager] Added LookAt component to NPC {npc.name}");
     }
    lookAt.enabled = true;
  }
 else
       {
   if (lookAt != null)
     {
        lookAt.ResetTarget();
     lookAt.enabled = false;
           }
      }
    }

  catch (Exception ex)
       {
      Debug.LogWarning($"[NpcFashionManager] Failed to configure LookAt: {ex.Message}");
   }
        }

        private static Transform FindTransformRecursive(Transform parent, string name)
        {
            if (parent == null) return null;

            // Check direct children
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return child;

                // Recursive search in child
                var found = FindTransformRecursive(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Applies hair color to all renderers on a GameObject.
        /// Used by fallback (non-skinned) attach paths.
        /// For skinned-mesh paths use ApplyHairColorToMeshAndBody() instead.
        /// </summary>
        private static void ApplyColorToRenderers(GameObject obj, Color color)
        {
            if (obj == null) return;
            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.GetType().Name.Contains("Particle")) continue;
                var mats = renderer.materials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;
                    string shaderName = mat.shader?.name ?? "";
                    if (shaderName.Contains("Particles")) continue;
                    if (mat.HasProperty("_HairColor")) { mat.SetColor("_HairColor", color); changed = true; }
                    else if (mat.HasProperty("_Color"))  { mat.SetColor("_Color",     color); changed = true; }
                }
                if (changed) renderer.materials = mats;
            }
            Debug.Log($"[NpcFashionManager] ApplyColorToRenderers: {obj.name} color={color} renderers={renderers.Length}");
        }

        /// <summary>
        /// Strips unwanted components from a GameObject (e.g., colliders, scripts).
        /// Keeps the transform and Renderer components by default.
        /// </summary>
        private static void StripUnwantedComponents(GameObject obj)
        {
            if (obj == null) return;

            // Get all components on the GameObject
            var components = obj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;

                var type = comp.GetType();

                // Don't destroy these components
                if (type == typeof(Transform) || type == typeof(Renderer) || type == typeof(MeshFilter) ||
                    type == typeof(SkinnedMeshRenderer) || type == typeof(Light))
                    continue;

                // Destroy the component
                UnityEngine.Object.Destroy(comp);
            }
        }

        private static float ParseFloatOrDefault(string value, float defaultValue)
        {
            if (string.IsNullOrEmpty(value)) return defaultValue;
            if (float.TryParse(value, out var result)) return result;
            return defaultValue;
        }

        private static Color ParseColor(string colorStr)
        {
            // For now, just parse as hex or fallback to white
            if (string.IsNullOrEmpty(colorStr)) return Color.white;
            if (colorStr.StartsWith("#"))
            {
                // Hex format #RRGGBB
                colorStr = colorStr.Substring(1);
                if (colorStr.Length == 6 &&
                    int.TryParse(colorStr.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) &&
                    int.TryParse(colorStr.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) &&
                    int.TryParse(colorStr.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
                {
                    return new Color(r / 255f, g / 255f, b / 255f);
                }
            }
            else
            {
                // CSV format r,g,b
                var parts = colorStr.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0].Trim(), out float fr) &&
                    float.TryParse(parts[1].Trim(), out float fg) &&
                    float.TryParse(parts[2].Trim(), out float fb))
                {
                    return new Color(fr, fg, fb);
                }
            }

            return Color.white;
        }

        /// <summary>
        /// Applies skin color to the NPC's body meshes using Valheim's shader system.
        /// Valheim uses _SkinColor on materials[0] of the body model.
        /// </summary>
        public static void ApplySkinColorToNpc(NpcController npc, Color skinColor)
        {
      if (npc == null) return;

       try
   {
         Transform visual = npc.transform.Find("Visual") ?? npc.transform;
        int appliedCount = 0;

       // Find all skinned mesh renderers (body meshes)
    var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

     foreach (var renderer in renderers)
            {
    if (renderer == null) continue;

    // Skip hair and beard meshes
string objName = renderer.gameObject.name.ToLowerInvariant();
   if (objName.Contains("hair") || objName.Contains("beard") ||
       objName.StartsWith("npc_hair") || objName.StartsWith("npc_beard"))
        continue;

        // Apply _SkinColor to all materials that have it (Valheim's approach)
         foreach (var mat in renderer.materials)
        {
       if (mat == null) continue;

         if (mat.HasProperty("_SkinColor"))
     {
  mat.SetColor("_SkinColor", skinColor);
      appliedCount++;
        }
      }
          }

       Debug.Log($"[NpcFashionManager] Applied skin color to {appliedCount} materials on NPC {npc.name}");
            }
          catch (Exception ex)
        {
            Debug.LogWarning($"[NpcFashionManager] Failed to apply skin color: {ex.Message}");
     }
        }

        /// <summary>
        /// Applies eye color to the NPC's eye meshes/materials.
        /// Valheim eyes are typically part of the head mesh texture, but some models may have
/// separate eye meshes or materials with _EyeColor or similar properties.
        /// </summary>
        public static void ApplyEyeColorToNpc(NpcController npc, Color eyeColor)
        {
  if (npc == null) return;

         try
            {
        Transform visual = npc.transform.Find("Visual") ?? npc.transform;
            int appliedCount = 0;

    // Find all renderers that might have eye materials
    var renderers = visual.GetComponentsInChildren<Renderer>(true);

         foreach (var renderer in renderers)
     {
    if (renderer == null) continue;

           string objName = renderer.gameObject.name.ToLowerInvariant();

       foreach (var mat in renderer.materials)
    {
          if (mat == null) continue;
                string matName = mat.name.ToLowerInvariant();

      // Check for eye-related material names or properties
bool isEyeMaterial = matName.Contains("eye") || objName.Contains("eye");
            
              if (isEyeMaterial)
             {
  Debug.Log($"[NpcFashionManager] Found potential eye material: {mat.name} on {renderer.gameObject.name}");
  
           // Try various eye color properties
                if (mat.HasProperty("_Color"))
    {
  mat.SetColor("_Color", eyeColor);
            appliedCount++;
   Debug.Log($"[NpcFashionManager] Set _Color on {mat.name}");
   }
       if (mat.HasProperty("_EyeColor"))
     {
           mat.SetColor("_EyeColor", eyeColor);
         appliedCount++;
           Debug.Log($"[NpcFashionManager] Set _EyeColor on {mat.name}");
           }
         if (mat.HasProperty("_EmissionColor"))
  {
  // For glowing eyes, set emission too
         if (eyeColor.r > 0.8f || eyeColor.g > 0.8f || eyeColor.b > 0.8f)
    {
  mat.SetColor("_EmissionColor", eyeColor * 0.5f);
        mat.EnableKeyword("_EMISSION");
    Debug.Log($"[NpcFashionManager] Set _EmissionColor on {mat.name}");
            }
      }
         }
   
              // Also check the second material slot on body models (index 1 is often for details)
         // In Valheim, materials[1] on the body is the hair material which uses _SkinColor for tinting
          }
      }

   if (appliedCount > 0)
    {
         Debug.Log($"[NpcFashionManager] Applied eye color to {appliedCount} materials on NPC {npc.name}");
          }
       else
                {
                 Debug.Log($"[NpcFashionManager] No eye materials found on NPC {npc.name} - eye color not applied (this is normal for most Valheim models)");
      }
    }
            catch (Exception ex)
   {
           Debug.LogWarning($"[NpcFashionManager] Failed to apply eye color: {ex.Message}");
   }
        }

        private static void EnableRenderers(GameObject obj)
        {
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
      {
       if (renderer != null)
       {
   renderer.enabled = true;
           renderer.gameObject.SetActive(true);
         }
         }
        }

        /// <summary>
        /// Cleans up the dev_smoke effect from player models that causes material errors on NPCs.
        /// This is a targeted fix for the specific "dev_smoke" particle effect.
        /// </summary>
        public static void CleanupPlayerModelEffects(GameObject root)
        {
            if (root == null) return;

            try
            {
                // Only look for and remove dev_smoke specifically
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || child.gameObject == root) continue;

                    // Only remove dev_smoke - this is the specific effect causing the material error
                    if (child.name == "dev_smoke")
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                        Debug.Log($"[NpcFashionManager] Removed dev_smoke effect from player model");
                        break; // There's typically only one
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] Error cleaning up dev_smoke: {ex.Message}");
            }
        }

    // Extracts fashion data from an NPC into JSON for persistence
  public static string ExtractFashionJsonFromNpc(NpcController npc)
      {
      if (npc == null) return null;
    try
{
            var comp = npc.GetComponent<NpcFashionComponent>();
        var data = new NPCFashionData();

         // Populate from component if present, otherwise fall back to npc equip fields
    if (comp != null)
  {
        data.LeftItem = comp.LeftItem;
              data.RightItem = comp.RightItem;
    data.HelmetItem = comp.HelmetItem;
         data.ChestItem = comp.ChestItem;
  data.LegsItem = comp.LegsItem;
    data.CapeItem = comp.CapeItem;
        data.HairItem = comp.HairItem;
    data.HairColor = comp.HairColor;
  data.BeardItem = comp.BeardItem;
        data.BeardColor = comp.BeardColor;
           data.SkinColor = comp.SkinColor;
  data.EyeColor = comp.EyeColor;

   data.GreetAnimation = comp.GreetAnimation;
         data.ByeAnimation = comp.ByeAnimation;
  data.InteractAnimation = comp.InteractAnimation;
 data.PeriodicAnimation = comp.PeriodicAnimation;
         data.PeriodicAnimationTime = comp.PeriodicAnimationTime.ToString();
     data.PeriodicSound = comp.PeriodicSound;
   data.PeriodicSoundTime = comp.PeriodicSoundTime.ToString();
    data.ModelScale = comp.ModelScale.ToString();
      data.LookAtPlayer = comp.LookAtPlayer ? "true" : "false";
  }
            else
 {
       data.HelmetItem = npc.equipHelmet;
          data.ChestItem = npc.equipChest;
   data.LegsItem = npc.equipLegs;
        data.CapeItem = npc.equipShoulder;
              data.LeftItem = npc.equipLeftHand;
           data.RightItem = npc.equipRightHand;
        }

    return JsonConvert.SerializeObject(data);
  }
    catch (Exception ex)
      {
    Debug.LogWarning($"[NpcFashionManager] Failed to extract fashion json: {ex.Message}");
  return null;
    }
        }
    }

    /// <summary>
    /// Component attached to NPC to hold fashion/appearance data and handle periodic animations/sounds.
    /// Uses Valheim's LookAt component for look-at-player behavior (like Haldor/Hildr traders).
    /// </summary>
    public class NpcFashionComponent : MonoBehaviour
    {
        // Items (useful for UI/inspection)
        public string LeftItem;
  public string RightItem;
 public string HelmetItem;
  public string ChestItem;
        public string LegsItem;
  public string CapeItem;

        // Hair and beard
        public string HairItem;
        public string HairColor;
        public string BeardItem;
        public string BeardColor;

    // Skin and eye color
        public string SkinColor;
 public string EyeColor;

        // Animations
        public string InteractAnimation;
        public string GreetAnimation;
        public string ByeAnimation;

        // Periodic behaviors
        public string PeriodicAnimation;
        public float PeriodicAnimationTime;
        public string PeriodicSound;
        public float PeriodicSoundTime;

        // Model scale
      public float ModelScale = 1f;

        // Look at player behavior (uses Valheim's LookAt component like Trader)
  public bool LookAtPlayer = false;
        private const float LookAtRange = 10f;  // Distance within which NPC looks at player
private const float LookAtAngle = 90f;  // View cone angle (90 degrees = 45 on each side)
        private LookAt _lookAt;  // Valheim's built-in LookAt component

        // Use ZSyncAnimation if available, otherwise fall back to Animator
        private ZSyncAnimation _zanim;
        private Animator _animator;
        private AudioSource _audioSource;
        private float _periodicAnimationTimer = 999f;
        private float _periodicSoundTimer = 999f;
        private bool _isApplied;
   private ZNetView _znv;

        public void Apply()
     {
         // Get ZNetView for ownership check
    _znv = GetComponentInParent<ZNetView>();
            Debug.Log($"[NpcFashionComponent] Apply() called on {gameObject.name}, ZNetView: {(_znv != null ? "found" : "null")}");

            // Find ZSyncAnimation first (Valheim's networked animation system)
      _zanim = GetComponent<ZSyncAnimation>();
          if (_zanim == null)
      {
          _zanim = GetComponentInChildren<ZSyncAnimation>(true);
       }

       // Log what we found
      if (_zanim != null)
  {
         Debug.Log($"[NpcFashionComponent] Found existing ZSyncAnimation on: {_zanim.gameObject.name}");
         }
        else
         {
      Debug.Log($"[NpcFashionComponent] No ZSyncAnimation found, will use direct Animator control");

   // Find the animator directly
       _animator = GetComponent<Animator>();
         if (_animator == null)
       {
          _animator = GetComponentInChildren<Animator>(true);
    }

  if (_animator != null)
{
  Debug.Log($"[NpcFashionComponent] Found Animator on: {_animator.gameObject.name}");
     }
  else
    {
             Debug.LogWarning($"[NpcFashionComponent] No Animator found on NPC! Animations will not work.");
          }
        }

            // Find or create audio source for periodic sounds
       _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
       _audioSource = GetComponentInChildren<AudioSource>(true);
            }

       // If still no audio source and we have periodic sounds, create one
if (_audioSource == null && !string.IsNullOrEmpty(PeriodicSound) && PeriodicSound != "(none)")
            {
_audioSource = gameObject.AddComponent<AudioSource>();
     _audioSource.spatialBlend = 1f;
 _audioSource.maxDistance = 20f;
  _audioSource.volume = 0.5f;
       Debug.Log($"[NpcFashionComponent] Created AudioSource for periodic sounds");
            }

   // Find or create LookAt component for look-at-player behavior
          if (LookAtPlayer)
        {
    _lookAt = GetComponentInChildren<LookAt>(true);
      if (_lookAt == null)
                {
      // Add LookAt component - it will automatically find the head bone
    Transform visual = transform.Find("Visual") ?? transform;
         _lookAt = visual.gameObject.AddComponent<LookAt>();
      Debug.Log($"[NpcFashionComponent] Added LookAt component for player tracking");
     }
}

  // Reset timers
            _periodicAnimationTimer = 999f;
    _periodicSoundTimer = 999f;
          _isApplied = true;

            Debug.Log($"[NpcFashionComponent] Apply() complete - " +
    $"ZSyncAnim: {(_zanim != null ? _zanim.gameObject.name : "null")}, " +
        $"Animator: {(_animator != null ? _animator.gameObject.name : "null")}, " +
                $"PeriodicAnim: '{PeriodicAnimation}', Time: {PeriodicAnimationTime}, " +
      $"PeriodicSound: '{PeriodicSound}', SoundTime: {PeriodicSoundTime}, " +
     $"LookAtPlayer: {LookAtPlayer}, LookAt: {(_lookAt != null ? "found" : "null")}");
        }

        private void FixedUpdate()
        {
      if (!_isApplied) return;

         // Handle periodic animations (only if we're the owner)
   bool isOwner = _znv == null || !_znv.IsValid() || _znv.IsOwner();

   if (isOwner && PeriodicAnimationTime > 0f && !string.IsNullOrEmpty(PeriodicAnimation) && PeriodicAnimation != "(none)")
 {
 _periodicAnimationTimer += Time.fixedDeltaTime;
       if (_periodicAnimationTimer >= PeriodicAnimationTime)
   {
    _periodicAnimationTimer = 0f;
     PlayAnimation(PeriodicAnimation);
   }
  }

  // Handle periodic sounds (all clients can play sounds)
            if (PeriodicSoundTime > 0f && !string.IsNullOrEmpty(PeriodicSound) && PeriodicSound != "(none)")
            {
          _periodicSoundTimer += Time.fixedDeltaTime;
if (_periodicSoundTimer >= PeriodicSoundTime)
     {
   _periodicSoundTimer = 0f;
   PlaySound(PeriodicSound);
    }
            }
        }

        /// <summary>
   /// LateUpdate handles the look-at-player behavior using Valheim's LookAt component.
        /// </summary>
     private void LateUpdate()
        {
            if (!_isApplied || !LookAtPlayer) return;

            // Find LookAt component if we don't have it cached
      if (_lookAt == null)
        {
     _lookAt = GetComponentInChildren<LookAt>(true);
        if (_lookAt == null) return;
  }

         var player = Player.m_localPlayer;
   if (player == null)
            {
    _lookAt.ResetTarget();
       return;
            }

   Vector3 npcPos = transform.position;
            Vector3 playerPos = player.transform.position;
     float distance = Vector3.Distance(npcPos, playerPos);

          // Check if player is within range (like Trader does with m_standRange)
       if (distance > LookAtRange)
            {
        _lookAt.ResetTarget();
           return;
            }

  // Check if player is within view cone (in front of NPC)
            Vector3 toPlayer = playerPos - npcPos;
            toPlayer.y = 0; // Ignore vertical difference for angle check
            Vector3 npcForward = transform.forward;
            npcForward.y = 0;

          float angle = Vector3.Angle(npcForward, toPlayer);
     if (angle > LookAtAngle)
   {
       _lookAt.ResetTarget();
           return;
  }

            // Set LookAt target to player's head (like Trader does)
     _lookAt.SetLoockAtTarget(player.GetHeadPoint());
        }

        private void PlayAnimation(string animName)
 {
            if (string.IsNullOrEmpty(animName) || animName == "(none)") return;

            // Use ZSyncAnimation if available
        if (_zanim != null)
     {
      try
         {
        _zanim.SetTrigger(animName);
 Debug.Log($"[NpcFashionComponent] Triggered via ZSyncAnimation: {animName}");
          return;
      }
          catch (Exception ex)
      {
         Debug.LogWarning($"[NpcFashionComponent] ZSyncAnimation.SetTrigger failed: {ex.Message}");
    }
        }

    // Fall back to direct Animator
            if (_animator == null)
        {
        _animator = GetComponentInChildren<Animator>(true);
      if (_animator == null)
      {
                 Debug.LogWarning($"[NpcFashionComponent] No Animator available for animation: {animName}");
      return;
   }
  }

          try
        {
       // Try SetTrigger first
     if (HasParameter(_animator, animName, AnimatorControllerParameterType.Trigger))
       {
        _animator.SetTrigger(animName);
           Debug.Log($"[NpcFashionComponent] Triggered via SetTrigger: {animName}");
      return;
                }

       // Try CrossFade to state
        int stateHash = Animator.StringToHash(animName);
                if (_animator.HasState(0, stateHash))
     {
           _animator.CrossFadeInFixedTime(stateHash, 0.15f, 0);
    Debug.Log($"[NpcFashionComponent] CrossFade to state: {animName}");
        return;
                }

         Debug.LogWarning($"[NpcFashionComponent] Animation '{animName}' not found");
 }
       catch (Exception ex)
   {
            Debug.LogWarning($"[NpcFashionComponent] Animation failed for '{animName}': {ex.Message}");
          }
        }

        private void PlaySound(string soundName)
        {
            if (string.IsNullOrEmpty(soundName) || soundName == "(none)") return;
    if (_audioSource == null) return;
         if (_audioSource.isPlaying) return;

     try
        {
    AudioClip clip = null;

          // Try to find the clip
            if (_audioSource.clip != null && _audioSource.clip.name.Equals(soundName, StringComparison.OrdinalIgnoreCase))
       {
        clip = _audioSource.clip;
  }

      if (clip == null)
                {
             var allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
       foreach (var c in allClips)
             {
        if (c != null && c.name.Equals(soundName, StringComparison.OrdinalIgnoreCase))
     {
         clip = c;
       break;
       }
        }
     }

        if (clip != null)
       {
  _audioSource.clip = clip;
             _audioSource.Play();
        Debug.Log($"[NpcFashionComponent] Played sound: {clip.name}");
    }
      else
    {
            Debug.LogWarning($"[NpcFashionComponent] Could not find audio clip: {soundName}");
       }
            }
         catch (Exception ex)
      {
            Debug.LogWarning($"[NpcFashionComponent] Sound failed: {ex.Message}");
            }
 }

        private bool HasParameter(Animator animator, string paramName, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.parameters == null) return false;
            foreach (var param in animator.parameters)
 {
            if (param.name == paramName && param.type == type)
          return true;
  }
     return false;
        }

        /// <summary>
        /// Plays the greet animation when a player approaches.
        /// </summary>
        public void PlayGreetAnimation()
        {
          if (string.IsNullOrEmpty(GreetAnimation) || GreetAnimation == "(none)") return;
        PlayAnimation(GreetAnimation);
        }

        /// <summary>
        /// Plays the bye animation when a player leaves.
        /// </summary>
  public void PlayByeAnimation()
     {
        if (string.IsNullOrEmpty(ByeAnimation) || ByeAnimation == "(none)") return;
   PlayAnimation(ByeAnimation);
        }

   /// <summary>
        /// Plays the interact animation when a player interacts.
    /// </summary>
        public void PlayInteractAnimation()
   {
          if (string.IsNullOrEmpty(InteractAnimation) || InteractAnimation == "(none)") return;
            PlayAnimation(InteractAnimation);
        }

        private void OnDestroy()
      {
    _isApplied = false;
        }
    }
}
