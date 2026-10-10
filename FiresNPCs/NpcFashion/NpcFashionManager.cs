using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using FiresRPGmaker.UI;

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
        public string Pet; // 0.0.559: cosmetic pet item or creature ("" = none)
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
        // a fixed property of the hair/beard prefab - finding it once and
        // memoizing avoids the linear scan of every prefab's children on every
        // companion spawn. Populated on first miss; never invalidated (the
        // prefabs themselves don't change at runtime).
        private static readonly Dictionary<int, GameObject> _attachChildCache = new Dictionary<int, GameObject>();

        // PeriodicSound fires off a FixedUpdate timer on EVERY NPC, and the lookup was
        // Resources.FindObjectsOfTypeAll<AudioClip>() - every loaded clip in the game - on every play that was
        // not already the source's current clip. A name that resolves to nothing rescanned forever and logged a
        // warning each time, so one mistyped sound name cost a full asset scan per NPC per interval. Hits are
        // cached for the session; MISSES ARE CACHED TOO, with a retry window, so a clip that a later bundle
        // brings in is still found without paying for the scan in between.
        private const float ClipMissRetrySeconds = 60f;
        private static readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> _clipMissRetryAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        internal static AudioClip ResolveClipCached(string soundName)
        {
            if (string.IsNullOrEmpty(soundName)) return null;

            if (_clipCache.TryGetValue(soundName, out var cached))
            {
                if (cached != null) return cached;
                if (_clipMissRetryAt.TryGetValue(soundName, out float retryAt) && Time.realtimeSinceStartup < retryAt) return null;
            }

            AudioClip found = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c != null && c.name.Equals(soundName, StringComparison.OrdinalIgnoreCase))
                {
                    found = c;
                    break;
                }
            }

            _clipCache[soundName] = found;
            if (found == null)
            {
                _clipMissRetryAt[soundName] = Time.realtimeSinceStartup + ClipMissRetrySeconds;
                Debug.LogWarning($"[NpcFashionComponent] Could not find audio clip: {soundName} - not retrying for {ClipMissRetrySeconds:F0}s");
            }
            return found;
        }

        private const string SkinnedAttachChildName = "attach_skin";
        private const string EquipOffsetChildName = "equipoffset";
        private const string EquippedEffectsChildName = "equiped";

        /// <summary>True when the prefab carries an "attach_skin" child. Vanilla VisEquipment.AttachItem
        /// dispatches on this name: "attach_skin" rebinds to the body skeleton, plain "attach" is a rigid
        /// parent to the joint. 1.0 hair and beard ship "attach", so they must not take the skinned path.</summary>
        private static bool UsesSkinnedAttach(GameObject prefab)
        {
            var attachChild = GetCachedAttachChild(prefab);
            return attachChild != null && attachChild.name == SkinnedAttachChildName;
        }

        /// <summary>The joint vanilla parents hair and beard to (VisEquipment.AttachItem passes m_helmet
        /// for both). Falls back to a name search only when the rig carries no VisEquipment.</summary>
        // The joint a rigid hair/beard hangs on, always under the ANIMATED head: VisEquipment's m_helmet when it sits under the
        // animator's Head bone, else a "Helmet_attach" under that bone, else the bone itself. A name lookup alone can land on a
        // same-named transform of the pre-1.0 bake that no animation moves, which is a style floating off the head.
        private static Transform ResolveHelmetJoint(GameObject npcRoot, Transform visual)
        {
            var animator = visual != null ? visual.GetComponentInChildren<Animator>() : null;
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var visEquipment = npcRoot != null ? npcRoot.GetComponent<VisEquipment>() : null;
            if (visEquipment != null && visEquipment.m_helmet != null && (head == null || visEquipment.m_helmet.IsChildOf(head)))
                return visEquipment.m_helmet;
            if (head != null)
            {
                if (visEquipment != null && visEquipment.m_helmet != null)
                    Debug.LogWarning($"[NpcFashionManager] '{(npcRoot != null ? npcRoot.name : "?")}': VisEquipment.m_helmet "
                                     + $"'{visEquipment.m_helmet.name}' is not under the animated Head bone; using the Head's own joint.");
                return FindTransformRecursive(head, "Helmet_attach") ?? head;
            }
            return FindTransformRecursive(visual, "Helmet_attach") ?? FindTransformRecursive(visual, "Head");
        }

        /// <summary>Mirrors the equipoffset step of vanilla AttachItem, including its use of the offset
        /// child's WORLD position and rotation rather than local ones.</summary>
        private static void ApplyEquipOffset(GameObject prefab, Transform instance)
        {
            var equipOffset = prefab.transform.Find(EquipOffsetChildName);
            if (equipOffset == null) return;
            instance.localPosition += equipOffset.position;
            instance.localRotation *= equipOffset.rotation;
        }

        private static void EnableEquippedEffects(GameObject instance)
        {
            var equipped = instance.transform.Find(EquippedEffectsChildName);
            if (equipped != null) equipped.gameObject.SetActive(true);
        }

        private static void DisableAttachedColliders(GameObject instance)
        {
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

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

                // kg parity: any item value may be a space-separated random list ("SwordIron AxeIron") —
                // pick one at apply time (NPC_Fashion.ApplyRandom). Resolve once, use for equip + comp.
                string helmetItem = PickRandomToken(data.HelmetItem);
                string chestItem = PickRandomToken(data.ChestItem);
                string legsItem = PickRandomToken(data.LegsItem);
                string capeItem = PickRandomToken(data.CapeItem);
                string leftItem = PickRandomToken(data.LeftItem);
                string rightItem = PickRandomToken(data.RightItem);
                string leftBackItem = PickRandomToken(data.LeftItemHidden);
                string rightBackItem = PickRandomToken(data.RightItemHidden);

                // Map items to npc equip fields (kg back items → our back slots).
                if (!string.IsNullOrEmpty(helmetItem)) npc.equipHelmet = helmetItem;
                if (!string.IsNullOrEmpty(chestItem)) npc.equipChest = chestItem;
                if (!string.IsNullOrEmpty(legsItem)) npc.equipLegs = legsItem;
                if (!string.IsNullOrEmpty(capeItem)) npc.equipShoulder = capeItem;
                if (!string.IsNullOrEmpty(leftItem)) npc.equipLeftHand = leftItem;
                if (!string.IsNullOrEmpty(rightItem)) npc.equipRightHand = rightItem;
                if (!string.IsNullOrEmpty(leftBackItem)) npc.equipLeftBack = leftBackItem;
                if (!string.IsNullOrEmpty(rightBackItem)) npc.equipRightBack = rightBackItem;

                // Hair/beard: kg stores the style WITHOUT the prefab prefix ("14" → prefab "Hair14" — kg
                // builds "Hair"+value at equip time). Normalize so kg-form and Fires-form (full prefab
                // name) values both work through this one apply path.
                string hairItem = NormalizeStyleName(PickRandomToken(data.HairItem), "Hair");
                string beardItem = NormalizeStyleName(PickRandomToken(data.BeardItem), "Beard");

                // Attach or update the fashion component which will handle animations/periodic behaviour
                var comp = npc.GetComponent<NpcFashionComponent>();
                if (comp == null) comp = npc.gameObject.AddComponent<NpcFashionComponent>();

                comp.HelmetItem = helmetItem;
                comp.ChestItem = chestItem;
                comp.LegsItem = legsItem;
                comp.CapeItem = capeItem;
                comp.LeftItem = leftItem;
                comp.RightItem = rightItem;

                // Hair and beard
                comp.HairItem = hairItem;
                comp.HairColor = data.HairColor;
                comp.BeardItem = beardItem;
                comp.BeardColor = data.BeardColor;

                // Skin color
                comp.SkinColor = data.SkinColor;

                // Eye color
                comp.EyeColor = data.EyeColor;

                // kg parity: a value may be a space-separated random list ("wave dance cheer") — kg's
                // ApplyRandom picks one at apply time; do the same so migrated kg NPCs behave identically.
                comp.GreetAnimation = PickRandomToken(data.GreetAnimation);
                comp.ByeAnimation = PickRandomToken(data.ByeAnimation);
                comp.InteractAnimation = PickRandomToken(data.InteractAnimation);
                comp.GreetText = data.GreetText;
                comp.ByeText = data.ByeText;
                // Sounds are NOT random lists in kg (ApplyRandom skips them) and clip names may contain spaces.
                comp.InteractSound = data.InteractAudioClip;
                int crafting; int.TryParse(PickRandomToken(data.CraftingAnimation), out crafting);
                comp.CraftingAnimation = crafting;

                comp.PeriodicAnimation = PickRandomToken(data.PeriodicAnimation);
                comp.PeriodicAnimationTime = ParseFloatOrDefault(data.PeriodicAnimationTime, 0f);

                comp.PeriodicSound = data.PeriodicSound;
                comp.PeriodicSoundTime = ParseFloatOrDefault(data.PeriodicSoundTime, 0f);

                comp.ModelScale = ParseFloatOrDefault(data.ModelScale, 1f);

                comp.Apply();

                // Initialize prefab cache if needed
                InitializePrefabCache();

                // Apply hair/beard visuals. Colors resolve preset names ("Dark Brown") AND kg HTML
                // strings ("#8B4513" / "red") through ResolveFashionColor, so migrated kg values render.
                var hairColor = ResolveFashionColor(data.HairColor, HairBeardColors.ColorPresets, Color.white);
                var beardColor = ResolveFashionColor(data.BeardColor, HairBeardColors.ColorPresets, Color.white);
                ApplyHairToNpc(npc, hairItem, ColorToString(hairColor));
                ApplyBeardToNpc(npc, beardItem, ColorToString(beardColor));

                // Apply skin color
                if (!string.IsNullOrEmpty(data.SkinColor) && data.SkinColor != "(none)")
                {
                    var skinColor = ResolveFashionColor(data.SkinColor, SkinToneColors.ColorPresets, SkinToneColors.ColorPresets["(none)"]);
                    ApplySkinColorToNpc(npc, skinColor);
                }

                // Apply eye color (attempts to find eye materials - may not work on all models)
                if (!string.IsNullOrEmpty(data.EyeColor) && data.EyeColor != "(none)")
                {
                    var eyeColor = ResolveFashionColor(data.EyeColor, EyeColors.ColorPresets, EyeColors.ColorPresets["(none)"]);
                    ApplyEyeColorToNpc(npc, eyeColor);
                }

 // Apply model scale - ALWAYS apply to ensure changes take effect immediately
     // even when resetting from a larger scale back to 1.0
     ApplyModelScale(npc, comp.ModelScale);

      // Configure look-at behavior using Valheim's LookAt component
   bool lookAtPlayer = !string.IsNullOrEmpty(data.LookAtPlayer) && 
   data.LookAtPlayer.Equals("true", StringComparison.OrdinalIgnoreCase);
    comp.LookAtPlayer = lookAtPlayer;
                // 0.0.559: the cosmetic pet rides in the fashion JSON (null = an older JSON, leave the pet alone)
                if (data.Pet != null)
                {
                    npc.equipPet = data.Pet;
                    var petView = npc.GetComponent<ZNetView>();
                    if (petView != null && petView.IsValid() && petView.IsOwner()) global::FiresRPGmaker.Modules.Pets.NpcPetSlot.WriteOnOwner(npc, "fashion apply");
                }
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
                // Cache ONLY valid player Customization items (the vanilla barber set), keyed by exact
                // prefab name. CustomStartsWith is case-sensitive, so rig prefabs (lowercase "hair_11",
                // "beard_hair") and armor/helmets that merely contain "hair" are EXCLUDED — that substring
                // scan was a source of "wrong object with its own built-in scale" being returned as hair.
                // Modded Customization styles are picked up automatically. Customization items live in
                // ObjectDB; the ZNetScene scan is dropped (it held non-customization world prefabs).
                var db = ObjectDB.instance;
                if (db != null)
                {
                    foreach (var d in db.GetAllItems(ItemDrop.ItemData.ItemType.Customization, "Hair"))
                    {
                        if (d == null || d.gameObject == null) continue;
                        var name = d.gameObject.name;
                        if (!string.IsNullOrEmpty(name) && !_hairPrefabCache.ContainsKey(name))
                            _hairPrefabCache[name] = d.gameObject;
                    }
                    foreach (var d in db.GetAllItems(ItemDrop.ItemData.ItemType.Customization, "Beard"))
                    {
                        if (d == null || d.gameObject == null) continue;
                        var name = d.gameObject.name;
                        if (!string.IsNullOrEmpty(name) && !_beardPrefabCache.ContainsKey(name))
                            _beardPrefabCache[name] = d.gameObject;
                    }
                }

                Debug.Log($"[NpcFashionManager] Initialized prefab cache: {_hairPrefabCache.Count} hair, {_beardPrefabCache.Count} beard (Customization items only)");
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

            // ObjectDB — accept ONLY if it's actually a Customization item, so a stale/wrong name can't
            // resolve to a non-customization prefab that carries its own (huge) built-in scale.
            var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
            if (prefab != null)
            {
                var drop = prefab.GetComponent<ItemDrop>();
                if (drop != null && drop.m_itemData?.m_shared != null
                    && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization)
                {
                    cache[itemName] = prefab;
                    return prefab;
                }
            }

            // Nearest-numbered fallback. Vanilla dropped Hair1/Hair2 (its live set starts at Hair3), so a
            // kg NPC saved with "Hair1" would otherwise resolve to nothing and render BALD. Rather than
            // fail, snap a "<Prefix><N>" style to the closest existing index in the cache so the NPC keeps
            // hair. Only triggers for numbered names that missed — an exact valid name returned above.
            string prefix = isHair ? "Hair" : "Beard";
            if (itemName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(itemName.Substring(prefix.Length), out int wantedIdx)
                && cache.Count > 0)
            {
                GameObject best = null; int bestDist = int.MaxValue;
                foreach (var kv in cache)
                {
                    if (kv.Value == null || !kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!int.TryParse(kv.Key.Substring(prefix.Length), out int haveIdx)) continue;
                    int dist = Math.Abs(haveIdx - wantedIdx);
                    if (dist < bestDist) { bestDist = dist; best = kv.Value; }
                }
                if (best != null)
                {
                    Debug.LogWarning($"[NpcFashionManager] {(isHair ? "hair" : "beard")} '{itemName}' is not a live style — using nearest '{best.name}'");
                    cache[itemName] = best;   // memoize so we don't re-search on every apply
                    return best;
                }
            }

            // Deliberately NO ZNetScene.GetPrefab / Resources.Load fallback: those can resolve an arbitrary
            // same-named non-Customization asset (the "wrong huge hair" source). Valid player hair/beard are
            // Customization items in ObjectDB; if it isn't one of those, it isn't a valid style.
            Debug.LogWarning($"[NpcFashionManager] No valid Customization {(isHair ? "hair" : "beard")} prefab named '{itemName}'");
            return null;
        }

        /// <summary>
        /// Applies a hair item to the NPC by instantiating the hair mesh.
        /// Hair in Valheim uses SkinnedMeshRenderer bound to head bones.
        /// </summary>
        /// <summary>NpcController shim - delegates to the GameObject overload.</summary>
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
                // A migrated NPC that renders bald usually lands HERE with an empty value. Log it ungated so
                // "called with no hair" is distinguishable from "called with a hair that failed to resolve"
                // (the latter warns from FindCustomizationPrefab). Together they pinpoint where hair is lost.
                Debug.Log($"[NpcFashionManager][hair] '{go.name}' ApplyHairToNpc got empty/none hair ('{hairItem ?? "<null>"}') — leaving bald");
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
                ScheduleOversizeSweep(go);

                var hairPrefab = FindCustomizationPrefab(hairItem, true);
                if (hairPrefab == null)
                {
                    Debug.LogWarning($"[NpcFashionManager] Hair prefab not found: {hairItem}");
                    return;
                }

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Found hair prefab: {hairPrefab.name}");

                Transform visual = go.transform.Find("Visual") ?? go.transform;

                if (UsesSkinnedAttach(hairPrefab) && TryApplySkinnedMeshToNpc(hairPrefab, "NPC_Hair_", hairItem, visual, hairColor))
                {
                    StampAppliedTag(visual, $"NPC_Hair_{hairItem}", hairItem, hairColor);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager] Successfully applied hair '{hairItem}' as skinned mesh to {go.name}");
                    return;
                }

                ApplyHairToHelmetJoint(go, visual, hairPrefab, hairItem, hairColor);
                StampAppliedTag(visual, $"NPC_Hair_{hairItem}", hairItem, hairColor);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NpcFashionManager] Failed to apply hair to {go.name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // -- Oversize forensics + containment -------------------------------------------------------
        // If ANY renderer on an NPC ends up grotesquely bigger than the body (the recurring "100x hair
        // blocks out the sun" class), do two things: never show it, and log a line that names the exact
        // object, mesh, and full scale chain so the SOURCE can be fixed instead of guessed at. Threshold
        // is far above any legit gear - a 1.3x giant is ~2.4m tall; a 100x attach is 30m+.
        private const float OVERSIZE_WORLD_LIMIT = 5f;
        private static readonly HashSet<int> _oversizeLogged = new HashSet<int>();

        private static void ScheduleOversizeSweep(GameObject go)
        {
            if (go == null) return;
            try
            {
                var host = go.GetComponent<MonoBehaviour>();
                if (host != null && host.isActiveAndEnabled)
                    host.StartCoroutine(OversizeSweepRoutine(go));
                else
                    DiagnoseAndContainOversized(go, "immediate");
            }
            catch { }
        }

        private static IEnumerator OversizeSweepRoutine(GameObject go)
        {
            yield return new WaitForSeconds(0.2f);
            if (go != null) DiagnoseAndContainOversized(go, "post-apply");
            yield return new WaitForSeconds(2.5f);
            if (go != null) DiagnoseAndContainOversized(go, "delayed");
        }

        public static void DiagnoseAndContainOversized(GameObject npc, string context)
        {
            if (npc == null) return;
            SkinnedMeshRenderer body = null;
            try { body = GetBodyModel(npc.transform.Find("Visual") ?? npc.transform); } catch { }

            foreach (var r in npc.GetComponentsInChildren<Renderer>())
            {
                if (r == null || ReferenceEquals(r, body)) continue;
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;

                Vector3 size = r.bounds.size;
                float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (maxDim <= OVERSIZE_WORLD_LIMIT) continue;

                r.enabled = false; // contain: whatever this is, it does not get to block out the sun

                if (!_oversizeLogged.Add(r.GetInstanceID())) continue;
                var smr = r as SkinnedMeshRenderer;
                string mesh = smr != null
                    ? smr.sharedMesh != null ? smr.sharedMesh.name : "<null>"
                    : r.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "<null>";
                string chain = "";
                var cur = r.transform;
                int guard = 0;
                while (cur != null && guard++ < 12)
                {
                    chain = cur.name + "(ls=" + cur.localScale.x.ToString("F3") + ")/" + chain;
                    cur = cur.parent;
                }
                Debug.LogWarning($"[FiresHairDiag] OVERSIZED renderer contained on '{npc.name}' ({context}): obj='{r.name}' " +
                                 $"mesh='{mesh}' boundsSize={size} lossyScale={r.transform.lossyScale} " +
                                 $"rootBone='{(smr != null && smr.rootBone != null ? smr.rootBone.name : "-")}' " +
                                 $"rootBoneLossy={(smr != null && smr.rootBone != null ? smr.rootBone.lossyScale.ToString() : "-")} " +
                                 $"chain={chain} - renderer disabled. REPORT THIS LINE.");
            }
        }

        /// <summary>Vanilla's rigid attach path (VisEquipment.AttachItem, the non-"attach_skin" branch):
        /// parent the prefab's attach child to the helmet joint, zero the local transform, apply equipoffset.
        /// 1.0 hair ships an "attach" child whose mesh has no bindposes, so this is its PRIMARY path.</summary>
        private static void ApplyHairToHelmetJoint(GameObject npcRoot, Transform visual, GameObject hairPrefab, string hairItem, string hairColor)
        {
            string ownerName = npcRoot != null ? npcRoot.name : "<null>";
            Transform helmetJoint = ResolveHelmetJoint(npcRoot, visual);

            if (helmetJoint == null)
            {
                Debug.LogError($"[NpcFashionManager] No helmet joint found on {ownerName}! Hair cannot be attached.");
                return;
            }

            // Find the visual to clone
            GameObject visualToClone = FindAttachVisual(hairPrefab);
            if (visualToClone == null)
            {
                Debug.LogWarning($"[NpcFashionManager] No visual found in hair prefab: {hairItem}");
                return;
            }

            // SetParent keeps worldPositionStays=TRUE and localScale is never written: forcing localScale=one
            // under the scaled joint is the 100x bug.
            var hairGO = UnityEngine.Object.Instantiate(visualToClone);
            hairGO.transform.SetParent(helmetJoint);
            hairGO.name = $"NPC_Hair_{hairItem}";
            hairGO.transform.localPosition = Vector3.zero;
            hairGO.transform.localRotation = Quaternion.identity;
            ApplyEquipOffset(hairPrefab, hairGO.transform);
            DisableAttachedColliders(hairGO);
            EnableEquippedEffects(hairGO);

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

            Debug.Log($"[NpcFashionManager] Attached hair '{hairItem}' to {ownerName} joint '{helmetJoint.name}' (rigid)");
            LogAttachEvidence(visual, hairGO, hairItem, "rigid (helmet joint)");
        }

        /// <summary>
        /// Applies a beard item to the NPC by instantiating the beard mesh.
        /// </summary>
        /// <summary>NpcController shim - delegates to the GameObject overload.</summary>
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
                ScheduleOversizeSweep(go);

                var beardPrefab = FindCustomizationPrefab(beardItem, false);
                if (beardPrefab == null)
                {
                    Debug.LogWarning($"[NpcFashionManager] Beard prefab not found: {beardItem}");
                    return;
                }

                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Found beard prefab: {beardPrefab.name}");

                Transform visual = go.transform.Find("Visual") ?? go.transform;

                if (UsesSkinnedAttach(beardPrefab) && TryApplySkinnedMeshToNpc(beardPrefab, "NPC_Beard_", beardItem, visual, beardColor))
                {
                    StampAppliedTag(visual, $"NPC_Beard_{beardItem}", beardItem, beardColor);
                    if (FiresLogger.VerboseEnabled)
                        Debug.Log($"[NpcFashionManager] Successfully applied beard '{beardItem}' as skinned mesh to {go.name}");
                    return;
                }

                ApplyBeardToHelmetJoint(go, visual, beardPrefab, beardItem, beardColor);
                StampAppliedTag(visual, $"NPC_Beard_{beardItem}", beardItem, beardColor);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NpcFashionManager] Failed to apply beard to {go.name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Vanilla's rigid attach path for beards -- VisEquipment.AttachItem passes m_helmet for beard as well as hair.
        /// </summary>
        private static void ApplyBeardToHelmetJoint(GameObject npcRoot, Transform visual, GameObject beardPrefab, string beardItem, string beardColor)
        {
            string ownerName = npcRoot != null ? npcRoot.name : "<null>";
            Transform helmetJoint = ResolveHelmetJoint(npcRoot, visual);

            if (helmetJoint == null)
            {
                Debug.LogError($"[NpcFashionManager] No helmet joint found on {ownerName}! Beard cannot be attached.");
                return;
            }

            GameObject visualToClone = FindAttachVisual(beardPrefab);
            if (visualToClone == null)
            {
                Debug.LogWarning($"[NpcFashionManager] No visual found in beard prefab: {beardItem}");
                return;
            }

            // localScale is never written: forcing localScale=one under the scaled joint is the 100x bug.
            var beardGO = UnityEngine.Object.Instantiate(visualToClone);
            beardGO.transform.SetParent(helmetJoint);
            beardGO.name = $"NPC_Beard_{beardItem}";
            beardGO.transform.localPosition = Vector3.zero;
            beardGO.transform.localRotation = Quaternion.identity;
            ApplyEquipOffset(beardPrefab, beardGO.transform);
            DisableAttachedColliders(beardGO);
            EnableEquippedEffects(beardGO);

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

            Debug.Log($"[NpcFashionManager] Attached beard '{beardItem}' to {ownerName} joint '{helmetJoint.name}' (rigid)");
            LogAttachEvidence(visual, beardGO, beardItem, "rigid (helmet joint)");
        }

        /// <summary>
        /// Finds the NPC's body SkinnedMeshRenderer via NpcVisEquipment.VisEquipment.m_bodyModel.
        /// This is the authoritative body mesh - Valheim uses it as the bone source and the
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
            Debug.LogWarning($"[NpcFashionManager] GetBodyModel: NOT FOUND for visualRoot={visualRoot.name} - color will only be applied to hair mesh material");
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
            // NOTE: DO NOT use mat.HasProperty("_HairColor") - in Valheim's Custom/Player shader
            // _HairColor is set programmatically and is NOT declared in the Properties {} block,
            // so HasProperty always returns false. Valheim's own VisEquipment.UpdateColors() calls
            // mat.SetColor("_HairColor", color) unconditionally with no HasProperty guard.
            if (FiresLogger.VerboseEnabled)
                Debug.Log($"[NpcFashionManager] ApplyHairColorToMeshAndBody: color={color}, bodyModel={(bodyModel != null ? bodyModel.gameObject.name : "<null>")}");

            // Step 1 - hair/beard mesh materials (mirrors VisEquipment.UpdateColors())
            foreach (var smr in meshRenderers)
            {
                if (smr == null) continue;
                var mats = smr.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    // Vanilla VisEquipment.UpdateColors + KG tint the hair INSTANCE's own material with
                    // "_SkinColor" (NOT "_HairColor"). With the hair keeping its own material, _SkinColor
                    // is the channel its shader reads.
                    mats[i].SetColor("_SkinColor", color);
                }
                smr.materials = mats;
            }

            // Step 2 - body material (VisEquipment.UpdateColors also sets _HairColor on baseMaterial)
            if (bodyModel != null)
            {
                // Hair color is applied to the hair INSTANCE's own material above (step 1, _SkinColor —
                // the vanilla/KG channel). Do NOT tint the body material with the hair color here:
                // bodyModel.material is materials[0] = the SKIN, so writing _SkinColor=hairColor would
                // dye the whole body. KG omits any body hair-color write entirely.
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

            // Cache the result, including null - a prefab that has neither
            // child will keep failing the same way; no point re-scanning.
            _attachChildCache[key] = attachChild;
            return attachChild;
        }

        /// <summary>
        /// Attaches a hair or beard prefab as a skinned mesh bound to the NPC's skeleton.
        /// Follows the Balrond / vanilla-Valheim VisEquipment.AttachHair() pattern:
        ///   - find the attach_skin child specifically
        ///   - parent to bodyModel.transform.parent (sibling of the body mesh)
        ///   - copy rootBone + bones array directly from the body model
        ///   - apply _HairColor on both the hair mesh material AND the body material
        /// </summary>
        private static bool TryApplySkinnedMeshToNpc(
            GameObject prefab, string namePrefix, string itemName,
            Transform visualRoot, string colorOverride)
        {
            // --- 1. Find attach_skin (Balrond / Valheim approach) ---
            // Memoized per-prefab - see _attachChildCache. This used to scan the
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
                // Match vanilla VisEquipment.AttachItem (attach_skin path, VisEquipment.cs:939-948):
                // instantiate UNPARENTED, then SetParent with worldPositionStays=TRUE so Unity recomputes
                // localScale to PRESERVE world scale — this cancels the rig's large armature import-scale.
                // Forcing localScale=Vector3.one under the scaled body parent rendered the hair at the raw
                // ~100x bone-space scale (the sun-blocking blob). Set position/rotation only, NEVER scale.
                var clonedGO = UnityEngine.Object.Instantiate(attachChild);
                clonedGO.transform.SetParent(parent);
                clonedGO.name = $"{namePrefix}{itemName}";
                clonedGO.transform.localPosition = Vector3.zero;
                clonedGO.transform.localRotation = Quaternion.identity;

                var smrs = clonedGO.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                {
                    UnityEngine.Object.Destroy(clonedGO);
                    return false;
                }

                // Strip Cloth components from the cloned tree BEFORE bone re-binding.
                // Some hair / beard prefabs (long flowing hair, capes) ship with a
                // Cloth on the SMR for sway physics. Cloth's per-vertex skin
                // coefficients are baked against the prefab's ORIGINAL skeleton -
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
                    // Validate rig compatibility BEFORE rebinding. (bone.localToWorld ×
                    // bindpose) is each vertex's net skin transform — for a mesh authored
                    // on the player rig it equals the character's world scale (same product
                    // the body mesh yields, ratio ≈ 1). A mesh authored on a foreign
                    // skeleton (cm-unit Maya rigs: ×100 armature + 0.01 mesh nodes, e.g.
                    // Balrond customization hair shadowing vanilla style names) lands at
                    // ~×100 — rebinding it renders a 30m+ sun-blocker that the oversize
                    // sweep then disables, leaving the NPC bald. Reject up front and let
                    // the rigid head-attach fallback handle it (visually fine for
                    // hair/beard: it follows the head bone).
                    // The bone-count check does not depend on the probe: Unity refuses a bones array whose length
                    // differs from the mesh's bindposes and keeps the clone's own, unanimated skeleton, so the style
                    // would hang in the air (Hair4_3: 55 bindposes against the player rig's 53).
                    float bodySkin = SkinScaleProbe(
                        bodyModel.sharedMesh != null ? bodyModel.sharedMesh.bindposes : null,
                        bodyModel.bones);
                    int bodyBoneCount = bodyModel.bones?.Length ?? 0;
                    foreach (var smr in smrs)
                    {
                        var poses = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                        bool countMismatch = poses == null || poses.Length != bodyBoneCount;
                        float ratio = 1f;
                        if (!countMismatch && bodySkin > 0f)
                        {
                            float itemSkin = SkinScaleProbe(poses, bodyModel.bones);
                            ratio = itemSkin > 0f ? itemSkin / bodySkin : -1f;
                        }
                        if (countMismatch || ratio < 0.33f || ratio > 3f)
                        {
                            Debug.Log($"[NpcFashionManager] '{itemName}' skeleton incompatible with the player rig ("
                                + (countMismatch
                                    ? $"bindposes={(poses?.Length ?? 0)} vs bones={bodyBoneCount}"
                                    : $"skin-scale x{ratio:F1}")
                                + ") — using rigid head attach instead");
                            UnityEngine.Object.Destroy(clonedGO);
                            return false;
                        }
                    }

                    foreach (var smr in smrs)
                    {
                        // Match vanilla VisEquipment.AttachItem / KG Market_NPC.AttachItem: copy ONLY
                        // rootBone + bones. Do NOT set localBounds — overwriting the hair mesh's
                        // head-sized bounds with the full-body bounds inflates it to a body-sized blob.
                        smr.rootBone          = bodyModel.rootBone;
                        smr.bones             = bodyModel.bones;
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

                // --- 4b. MagicaCloth, as vanilla AttachItem does right after the same rebind: the NPC's own
                // VisEquipment.SetupCloth (Initialize, DisableAutoBuild, its cloth colliders, ReplaceTransform onto the
                // character's bones by name, BuildAndRun). 1.0 hair and beards simulate with MagicaCloth2; without this
                // the cloth kept driving the mesh from the clone's own unanimated bone copies, so the hair floated off
                // the head and ignored every attack (Fire, 2026-09-29, Sigrid).
                string clothSetup = SetupClothLikeVanilla(visualRoot, clonedGO);

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
                        // gets their own copy - setting _HairColor won't bleed across NPCs.
                        // Keep the hair prefab's OWN material (vanilla VisEquipment.AttachItem + KG
                        // never swap to the body atlas — the hair UVs are authored for the dedicated
                        // hair texture, so the body atlas paints body skin onto the hair = the blob).
                        // Instance it only so per-NPC _SkinColor tint can't bleed across shared prefabs.
                        if (smr.sharedMaterial != null) smr.material = new Material(smr.sharedMaterial);
                        if (FiresLogger.VerboseEnabled)
                            Debug.Log($"[NpcFashionManager] Instanced hair's own material for {smr.name}");
                    }
                }

                // --- 6. DO NOT run the VanillaAssetResolver shader swap on hair/beard. ---
                // The hair keeps its own prefab material (step 5), whose shader is the real
                // vanilla Custom/Player shader that natively reads "_SkinColor" — exactly what
                // vanilla VisEquipment.UpdateColors (VisEquipment.cs:359-364) and KG Market_NPC
                // (Market_NPC.cs:766) tint, and NEITHER does any shader swap on the hair.
                // ApplyToPrefab(Shader) would swap that Custom/* shader for a cached vanilla
                // shader instance, and SwapShaderPreservingProperties only re-applies _SkinColor
                // when HasProperty("_SkinColor") is true. _SkinColor is set programmatically and
                // is NOT declared in the shader's Properties{} block, so HasProperty returns false
                // (same as _HairColor — see the note in ApplyHairColorToMeshAndBody) and the swap
                // silently drops the tint channel → hair renders untinted (white). Skipping the
                // swap matches the proven KG/vanilla path: original material, _SkinColor tint.

                // --- 7. Apply color using the EXACT Valheim UpdateColors() pattern ---
                if (!string.IsNullOrEmpty(colorOverride))
                    ApplyHairColorToMeshAndBody(smrs, bodyModel, ParseColor(colorOverride));

                EnableRenderers(clonedGO);
                clonedGO.SetActive(true);

                int totalBones = bodyModel?.bones?.Length ?? smrs[0]?.bones?.Length ?? 0;
                if (FiresLogger.VerboseEnabled)
                    Debug.Log($"[NpcFashionManager] Applied {itemName} as skinned mesh (bones: {bonesFound}/{totalBones})");
                LogAttachEvidence(visualRoot, clonedGO, itemName, $"skinned (bones {bonesFound}/{totalBones}, {clothSetup})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager] TryApplySkinnedMeshToNpc failed for {itemName}: {ex.Message}");
                return false;
            }
        }

        private static readonly System.Reflection.MethodInfo s_setupCloth =
            HarmonyLib.AccessTools.Method(typeof(VisEquipment), "SetupCloth", new[] { typeof(GameObject) });

        // Runs the NPC's own vanilla VisEquipment.SetupCloth on `item` when it carries MagicaCloth; says what happened.
        private static string SetupClothLikeVanilla(Transform visualRoot, GameObject item)
        {
            int cloths = item.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().FullName == "MagicaCloth2.MagicaCloth");
            if (cloths == 0) return "no MagicaCloth";
            var vis = visualRoot != null ? visualRoot.GetComponentInParent<VisEquipment>() : null;
            if (vis == null) return $"{cloths} MagicaCloth NOT set up: no VisEquipment on the NPC";
            if (s_setupCloth == null) return $"{cloths} MagicaCloth NOT set up: VisEquipment.SetupCloth not found";
            try
            {
                s_setupCloth.Invoke(vis, new object[] { item });
                return $"{cloths} MagicaCloth set up by vanilla SetupCloth";
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Debug.LogWarning($"[NpcFashionManager] SetupCloth failed for '{item.name}': {inner.GetType().Name}: {inner.Message}");
                return $"{cloths} MagicaCloth setup FAILED ({inner.GetType().Name})";
            }
        }

        // One ungated line per hair/beard put on an NPC: how, where it hangs, and whether that is under the ANIMATED head (the
        // animator's own Head bone, never a name lookup), with its bounds in the head's frame. A floating style shows here
        // as "under the animated Head: no" or a large head offset.
        private static void LogAttachEvidence(Transform visualRoot, GameObject item, string itemName, string how)
        {
            try
            {
                var npc = visualRoot != null ? (visualRoot.GetComponentInParent<ZNetView>()?.transform ?? visualRoot.root) : null;
                var animator = visualRoot != null ? visualRoot.GetComponentInChildren<Animator>() : null;
                var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                string under;
                var smr = item.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (head == null) under = "no animated Head bone found";
                else if (smr != null && smr.bones != null && smr.bones.Length > 0)
                    under = System.Array.IndexOf(smr.bones, head) >= 0 ? "skinned to the animated Head: yes" : "skinned to the animated Head: NO";
                else under = item.transform.IsChildOf(head) ? "under the animated Head: yes" : "under the animated Head: NO";
                var renderer = item.GetComponentInChildren<Renderer>(true);
                string offset = head != null && renderer != null
                    ? head.InverseTransformPoint(renderer.bounds.center).ToString("F2") : "-";
                string path = item.transform.parent != null ? item.transform.parent.name : "-";
                Debug.Log($"[NpcFashionManager][attach] '{itemName}' on '{(npc != null ? npc.name : "?")}': {how}, parent '{path}', {under}, "
                          + $"localPos {item.transform.localPosition:F2}, bounds centre in the head's frame {offset}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcFashionManager][attach] evidence failed for '{itemName}': {ex.Message}");
            }
        }

        // Average scale of the first valid (bone.localToWorld × bindpose) product — the
        // net transform skinning applies to a vertex with weight 1 on that bone. For a
        // mesh authored on the given skeleton this is the character's world scale;
        // wildly different values mean the bindposes belong to another rig.
        private static float SkinScaleProbe(Matrix4x4[] bindposes, Transform[] bones)
        {
            if (bindposes == null || bones == null) return -1f;
            int n = Mathf.Min(bindposes.Length, bones.Length);
            for (int i = 0; i < n; i++)
            {
                if (bones[i] == null) continue;
                Matrix4x4 m = bones[i].localToWorldMatrix * bindposes[i];
                float avg = (((Vector3)m.GetColumn(0)).magnitude
                           + ((Vector3)m.GetColumn(1)).magnitude
                           + ((Vector3)m.GetColumn(2)).magnitude) / 3f;
                if (avg > 1e-4f) return avg;
            }
            return -1f;
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
            if (scale <= 0.01f) scale = 1f;

            // kg parity: KGnpcScale scales ONLY the model visual. Our override clones (OverrideModel
            // container / creature-visual clone) are parented INSIDE root/Visual, so scaling Visual
            // covers the native body and every override case uniformly — and survives a later
            // ApplyModelOverride since the parent persists. The Character root must stay at 1:
            // scaling it inflates colliders/physics AND compounds with this visual scale
            // (root×visual = scale², the "giant migrated NPC" bug). Absolute set = idempotent.
            if (npc.transform.localScale != Vector3.one) npc.transform.localScale = Vector3.one;

            Transform visual = npc.transform.Find("Visual");
            if (visual != null)
            {
                visual.localScale = Vector3.one * scale;
                Debug.Log($"[NpcFashionManager] Applied model scale {scale} to Visual of NPC {npc.name}");
            }
            else
            {
                Debug.LogWarning($"[NpcFashionManager] No Visual on NPC {npc.name} - scale {scale} not applied (root is never scaled)");
            }
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
         // 0.0.546 ([visual]'s a7: head never followed): vanilla LookAt drives the head from OnAnimatorIK, which Unity calls
         // only on the Animator's own GameObject. The swapped/cloned model's Animator sits below "Visual", so put it there.
         Transform visual = npc.GetComponentInChildren<Animator>(true)?.transform ?? npc.transform;
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
                // 1.0 hair/beard cloth: vanilla keeps it and sets it up (SetupClothLikeVanilla), so do we.
                if (type.FullName == "MagicaCloth2.MagicaCloth") continue;

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

        // kg random-list semantics: a fashion value holding "a b c" means "pick one at apply time"
        // (NPC_Fashion.ApplyRandom / RandomSplitSpace). Single values pass through untouched.
        private static string PickRandomToken(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.Contains(" ")) return value;
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? value : parts[UnityEngine.Random.Range(0, parts.Length)];
        }

        // kg stores hair/beard styles WITHOUT the prefab prefix ("14" → prefab "Hair14" — kg builds
        // "Hair"+value at equip time). Fires-authored values are full prefab names already; anything that
        // starts with the prefix (or is empty / the "(none)" sentinel) passes through untouched.
        private static string NormalizeStyleName(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value) || value == "(none)") return value;
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value;
            return prefix + value;
        }

        // Resolve a fashion color that may be a Fires preset name ("Dark Brown"), a kg HTML string
        // ("#8B4513" / "red"), a bare hex ("8B4513"), an "r,g,b" triple, or a kg random list of any of
        // those. Preset lookup runs on the FULL string first because preset names contain spaces.
        private static Color ResolveFashionColor(string raw, Dictionary<string, Color> presets, Color fallback)
        {
            if (string.IsNullOrEmpty(raw) || raw == "(none)") return fallback;
            if (presets != null && presets.TryGetValue(raw, out var preset)) return preset;
            if (TryParseColorString(raw, out var parsed)) return parsed;
            if (raw.Contains(" "))
            {
                string token = PickRandomToken(raw);
                if (presets != null && presets.TryGetValue(token, out preset)) return preset;
                if (TryParseColorString(token, out parsed)) return parsed;
            }
            return fallback;
        }

        private static bool TryParseColorString(string s, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            if (ColorUtility.TryParseHtmlString(s, out color)) return true;                                 // "#RRGGBB" + named HTML colors
            if (!s.StartsWith("#") && !s.Contains(",") && ColorUtility.TryParseHtmlString("#" + s, out color)) return true; // bare hex
            var parts = s.Split(',');                                                                        // "r,g,b"
            if (parts.Length >= 3
                && float.TryParse(parts[0].Trim(), out float r)
                && float.TryParse(parts[1].Trim(), out float g)
                && float.TryParse(parts[2].Trim(), out float b))
            {
                if (r > 1f || g > 1f || b > 1f) { r /= 255f; g /= 255f; b /= 255f; }
                color = new Color(r, g, b);
                return true;
            }
            return false;
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

        // Extracts fashion data from an NPC into JSON for persistence. This is what SaveToZDO writes
        // into npc_fashion_json (preferred over the pending JSON), so it MUST echo EVERY field the
        // NPC carries — any field omitted here is silently wiped from the NPC on the next admin
        // Apply/save (the migrated-kg-NPC data-loss bug: GreetText/ByeText/InteractSound/
        // CraftingAnimation/back-items/TextSize/TextHeight all vanished on save).
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
                    data.GreetText = comp.GreetText;
                    data.ByeText = comp.ByeText;
                    data.InteractAudioClip = comp.InteractSound;
                    data.CraftingAnimation = comp.CraftingAnimation.ToString(CultureInfo.InvariantCulture);
                    data.PeriodicAnimation = comp.PeriodicAnimation;
                    data.PeriodicAnimationTime = comp.PeriodicAnimationTime.ToString(CultureInfo.InvariantCulture);
                    data.PeriodicSound = comp.PeriodicSound;
                    data.PeriodicSoundTime = comp.PeriodicSoundTime.ToString(CultureInfo.InvariantCulture);
                    data.ModelScale = comp.ModelScale.ToString(CultureInfo.InvariantCulture);
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

                // Fields whose authority lives on NpcController (own ZDO keys) — echoed so the JSON is
                // the complete kg-parity document: back items (kg LeftItemHidden/RightItemHidden map to
                // our back slots) and nameplate size/height (kg TextSize/TextHeight).
                data.LeftItemHidden = npc.equipLeftBack;
                data.RightItemHidden = npc.equipRightBack;
                data.TextSize = npc.nameplateScale.ToString(CultureInfo.InvariantCulture);
                data.TextHeight = npc.nameplateYOffset.ToString(CultureInfo.InvariantCulture);
                data.Pet = npc.equipPet;   // null = never set: the apply side leaves the pet alone

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

        // Speech bubbles (kg GreetText/ByeText parity) + interact sound clip
        public string GreetText;
        public string ByeText;
        public string InteractSound;

        // kg CraftingAnimation parity: >0 drives the player animator's "crafting" int, locking the NPC
        // into the corresponding work loop (1 = workbench crafting pose etc.).
        public int CraftingAnimation;

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

       // If still no audio source and we have periodic or interact sounds, create one
if (_audioSource == null && ((!string.IsNullOrEmpty(PeriodicSound) && PeriodicSound != "(none)")
                          || (!string.IsNullOrEmpty(InteractSound) && InteractSound != "(none)")))
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
      // 0.0.546: a LookAt that isn't on the Animator's GameObject never gets OnAnimatorIK (NPCs placed before this build carry one on "Visual").
      var animGo = GetComponentInChildren<Animator>(true)?.gameObject;
      if (_lookAt != null && animGo != null && _lookAt.gameObject != animGo) { Destroy(_lookAt); _lookAt = null; }
      if (_lookAt == null)
                {
      // Add LookAt component on the Animator's GameObject (OnAnimatorIK only fires there)
    Transform visual = animGo != null ? animGo.transform : transform;
         _lookAt = visual.gameObject.AddComponent<LookAt>();
         global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcFashionComponent] LookAt on '{visual.name}' (animator {(animGo != null ? "found" : "missing")})");
      Debug.Log($"[NpcFashionComponent] Added LookAt component for player tracking");
     }
}

  // Reset timers
            _periodicAnimationTimer = 999f;
    _periodicSoundTimer = 999f;
          _isApplied = true;

            // kg crafting pose: hold the animator's "crafting" int so the NPC works at its station
            // (1 = workbench loop etc.) — locked in place, matching kg's KGcraftingAnimation.
            if (CraftingAnimation > 0)
            {
                try
                {
                    var craftAnim = _animator ?? GetComponentInChildren<Animator>(true);
                    if (_zanim != null) _zanim.SetInt("crafting", CraftingAnimation);
                    else if (craftAnim != null) craftAnim.SetInteger("crafting", CraftingAnimation);
                }
                catch (Exception ex) { Debug.LogWarning("[NpcFashionComponent] crafting anim failed: " + ex.Message); }
            }

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

        // "sit" is the only looping player emote (Emotes enum [Emote(OneShot=false)]); the rest are one-shots.
        private static bool IsLoopingEmote(string name) => string.Equals(name, "sit", StringComparison.OrdinalIgnoreCase);

        // Plays a legit player emote on the NPC. The valid names are the emote_* params on the player
        // Animator (see the dressing-room dropdown filter). One-shots route through ZSyncAnimation so they
        // replicate to other clients and survive ZSyncAnimation's per-frame re-apply; the looping "sit"
        // is a held Bool. Falls back to a bare param/state name so older saved selections still fire.
        private void PlayAnimation(string animName)
        {
            if (string.IsNullOrEmpty(animName) || animName == "(none)") return;

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);

            bool looping = IsLoopingEmote(animName);
            string emoteParam = "emote_" + animName.ToLowerInvariant();
            var paramType = looping ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Trigger;

            bool rawBool = false;
            string param = HasParameter(_animator, emoteParam, paramType) ? emoteParam
                         : HasParameter(_animator, animName, AnimatorControllerParameterType.Trigger) ? animName
                         : null;
            // kg parity: saved kg NPCs use RAW player-animator states as animation values
            // (attach_chair = sit on chair, crouching, ...). kg fires them with
            // ZSyncAnimation.SetTrigger, which LATCHES a Bool param true on every client — the NPC
            // holds the pose. Accept any Bool param by its raw name the same way (this is what made
            // migrated sitting NPCs stand: 'attach_chair' was rejected as "no such emote").
            if (param == null && HasParameter(_animator, animName, AnimatorControllerParameterType.Bool))
            {
                param = animName;
                rawBool = true;
            }
            if (param == null)
            {
                Debug.LogWarning($"[NpcFashionComponent] No animator param for emote '{animName}' (model has no player emotes?)");
                return;
            }

            try
            {
                if (looping)
                {
                    // Held looping emote (sit) — raw Animator Bool (ZSyncAnimation exposes no Bool setter here).
                    if (_animator != null) { _animator.ResetTrigger("emote_stop"); _animator.SetBool(param, true); }
                }
                else if (rawBool)
                {
                    // kg's exact mechanism: SetTrigger on a Bool param latches it true, replicated.
                    if (_zanim != null) _zanim.SetTrigger(param);
                    else if (_animator != null) _animator.SetBool(param, true);
                }
                else if (_zanim != null)
                {
                    _zanim.SetTrigger(param);   // one-shot, replicated
                }
                else if (_animator != null)
                {
                    _animator.SetTrigger(param);
                }
                Debug.Log($"[NpcFashionComponent] Emote '{animName}' -> '{param}' (looping={looping}, rawBool={rawBool})");
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

            // Custom mp3 loaded from BepInEx/config/FiresNPCs_Sounds (KG-style) resolves first.
            FiresCore.Audio.FiresCustomAudioStore.Clips.TryGetValue(soundName, out clip);

          // Try to find the clip
            if (clip == null && _audioSource.clip != null && _audioSource.clip.name.Equals(soundName, StringComparison.OrdinalIgnoreCase))
       {
        clip = _audioSource.clip;
  }

                if (clip == null) clip = NpcFashionManager.ResolveClipCached(soundName);

                if (clip != null)
                {
                    _audioSource.clip = clip;
                    _audioSource.Play();
                    global::FiresRPGmaker.FiresLogger.LogVerbose($"[NpcFashionComponent] Played sound: {clip.name}");
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
        /// Greets an approaching player: speech bubble (GreetText) + emote (GreetAnimation). kg parity —
        /// Market_NPC shows Chat.SetNpcText(up*4, cull 20m, ttl 3s) and SetTriggers the emote.
        /// </summary>
        public void PlayGreetAnimation()
        {
            ShowNpcText(GreetText);
            if (string.IsNullOrEmpty(GreetAnimation) || GreetAnimation == "(none)") return;
            PlayAnimation(GreetAnimation);
        }

        /// <summary>
        /// Sends off a leaving player: speech bubble (ByeText) + emote (ByeAnimation).
        /// </summary>
        public void PlayByeAnimation()
        {
            ShowNpcText(ByeText);
            if (string.IsNullOrEmpty(ByeAnimation) || ByeAnimation == "(none)") return;
            PlayAnimation(ByeAnimation);
        }

        /// <summary>
        /// Plays the interact animation (and interact sound, if set) when a player interacts.
        /// </summary>
        public void PlayInteractAnimation()
        {
            PlaySound(InteractSound);
            if (string.IsNullOrEmpty(InteractAnimation) || InteractAnimation == "(none)") return;
            PlayAnimation(InteractAnimation);
        }

        // Floating NPC speech bubble, kg-style (client-local; Chat.instance is null on a dedicated server).
        private void ShowNpcText(string text)
        {
            if (string.IsNullOrEmpty(text) || text == "(none)") return;
            try { Chat.instance?.SetNpcText(gameObject, Vector3.up * 4f, 20f, 3f, "", text, false); }
            catch (Exception ex) { Debug.LogWarning($"[NpcFashionComponent] SetNpcText failed: {ex.Message}"); }
        }

        private void OnDestroy()
      {
    _isApplied = false;
        }
    }
}
