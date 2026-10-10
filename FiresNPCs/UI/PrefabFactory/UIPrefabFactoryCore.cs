using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FiresRPGmaker.Managers;
using Object = UnityEngine.Object;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Core factory for creating/loading UI prefabs.
    /// Split into partial classes for organization.
    /// </summary>
    public static partial class UIPrefabFactory
    {
        private static GameObject _dialogueEntryPrefab;
   private static GameObject _questEntryPrefab;
  
        // Cached background sprite
        private static Sprite _backgroundSprite;
   private static bool _backgroundLoaded;

        public static AssetBundle UIAssetBundle { get; set; }

        #region Font Helper

        private static TextMeshProUGUI CreateTMPText(GameObject go)
        {
   var tmp = go.AddComponent<TextMeshProUGUI>();
      GUIManager.ApplyGameFont(tmp);
    return tmp;
        }

        public static void ApplyFontsToHierarchy(GameObject root)
        {
            if (root == null) return;
            GUIManager.ApplyGameFontToAll(root.transform);
        }

        #endregion

        #region Background Sprite Loading

        public static Sprite GetBackgroundSprite()
        {
   if (_backgroundLoaded)
           return _backgroundSprite;
    
   _backgroundLoaded = true;
       _backgroundSprite = LoadBackgroundFromAssetBundle();
     
         if (_backgroundSprite != null)
          {
      Debug.Log($"[UIPrefabFactory] Loaded background sprite: npcuibkg");
            }
            else
            {
        Debug.LogWarning("[UIPrefabFactory] Background sprite 'npcuibkg' not found in asset bundle");
     }
 
            return _backgroundSprite;
        }
    
        private static Sprite LoadBackgroundFromAssetBundle()
        {
  var bundle = FiresRPGmaker.assetBundle;
     if (bundle == null)
                return null;
     
   string[] searchPaths = {
       "Assets/Custom/VAitems/Icons/npcuibkg",
    "Assets/Custom/VAitems/Icons/npcuibkg.png",
    "Assets/Custom/VAitems/Icons/npcuibkg.jpg",
       "Assets/Custom/VAitems/UI/npcuibkg",
           "Assets/Custom/VAitems/UI/npcuibkg.png",
            "npcuibkg",
                "npcuibkg.png"
   };
          
            foreach (var path in searchPaths)
    {
      try
    {
        var sprite = bundle.LoadAsset<Sprite>(path);
       if (sprite != null)
              {
       Debug.Log($"[UIPrefabFactory] Found background sprite at path: {path}");
     return sprite;
         }
  
           var texture = bundle.LoadAsset<Texture2D>(path);
            if (texture != null)
 {
          sprite = Sprite.Create(
        texture,
            new Rect(0, 0, texture.width, texture.height),
      new Vector2(0.5f, 0.5f),
       100f);
         sprite.name = "npcuibkg";
       Debug.Log($"[UIPrefabFactory] Created sprite from texture at path: {path}");
   return sprite;
        }
}
     catch (Exception ex)
           {
       Debug.LogWarning($"[UIPrefabFactory] Error loading background from {path}: {ex.Message}");
         }
    }
       
            var managerSprite = VAMiscAssetManager.GetSprite("npcuibkg");
        if (managerSprite != null)
     {
 Debug.Log("[UIPrefabFactory] Found background sprite via VAMiscAssetManager");
 return managerSprite;
          }
  
 return null;
  }
        
        public static void ApplyBackgroundSprite(Image image)
   {
            if (image == null) return;
   
            var sprite = GetBackgroundSprite();
            if (sprite != null)
   {
    image.sprite = sprite;
       image.type = Image.Type.Sliced;
      image.color = Color.white;
            }
      else
        {
       image.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
            }
        }
      
   public static void ApplyBackgroundToPanel(GameObject panel)
        {
    if (panel == null) return;
       
       var backgroundTransform = panel.transform.Find("Background");
            if (backgroundTransform != null)
     {
        var backgroundImage = backgroundTransform.GetComponent<Image>();
      if (backgroundImage != null)
                {
  ApplyBackgroundSprite(backgroundImage);
       Debug.Log("[UIPrefabFactory] Applied background sprite to Background object");
   return;
              }
            }
        
   var images = panel.GetComponentsInChildren<Image>(true);
       foreach (var img in images)
   {
        if (img.gameObject.name.Equals("Background", StringComparison.OrdinalIgnoreCase))
      {
         ApplyBackgroundSprite(img);
        Debug.Log($"[UIPrefabFactory] Applied background sprite to: {img.gameObject.name}");
        return;
     }
       }
   
   var rootImage = panel.GetComponent<Image>();
            if (rootImage != null)
        {
                ApplyBackgroundSprite(rootImage);
         Debug.Log("[UIPrefabFactory] Applied background sprite to root panel");
            }
        }

        #endregion

        #region Main Panel

        public static GameObject GetDialogueEntryPrefab()
        {
     if (_dialogueEntryPrefab != null)
         return _dialogueEntryPrefab;

            _dialogueEntryPrefab = VAMiscAssetManager.LoadUIPrefab("DialogueEntryPrefab")
                ?? TryLoadFromAssetBundle("DialogueEntryPrefab");

         if (_dialogueEntryPrefab != null)
 {
    Debug.Log("[UIPrefabFactory] Loaded DialogueEntryPrefab from asset bundle");
      return _dialogueEntryPrefab;
          }

            _dialogueEntryPrefab = CreateEntryPrefab("DialogueEntryPrefab", new Color(0.2f, 0.25f, 0.3f, 0.9f));
            return _dialogueEntryPrefab;
        }

        public static GameObject GetQuestEntryPrefab()
        {
         if (_questEntryPrefab != null)
            return _questEntryPrefab;

    _questEntryPrefab = VAMiscAssetManager.LoadUIPrefab("QuestEntryPrefab")
   ?? TryLoadFromAssetBundle("QuestEntryPrefab");

      if (_questEntryPrefab != null)
            {
          Debug.Log("[UIPrefabFactory] Loaded QuestEntryPrefab from asset bundle");
      return _questEntryPrefab;
            }

            _questEntryPrefab = CreateEntryPrefab("QuestEntryPrefab", new Color(0.25f, 0.2f, 0.15f, 0.9f));
       return _questEntryPrefab;
        }

   #endregion

      #region Asset Bundle Loading

        private static GameObject TryLoadFromAssetBundle(string assetName)
      {
        try
      {
                if (UIAssetBundle != null)
     {
     var asset = UIAssetBundle.LoadAsset<GameObject>(assetName);
  if (asset != null)
       return asset;
    }

    var firesAssets = FiresRPGmaker.Instance?.GetAssetBundle();
                if (firesAssets != null)
        {
         string[] paths = {
               $"Assets/Custom/VAitems/UI/{assetName}",
          $"Assets/Custom/VAitems/UI/{assetName}.prefab",
           $"Assets/Custom/VAitems/UI/{assetName.ToLower()}",
         $"Assets/Custom/VAitems/UI/{assetName.ToLower()}.prefab",
   $"Assets/Custom/NPCs/UI/{assetName}",
           $"Assets/Custom/NPCs/UI/{assetName}.prefab",
              assetName,
          $"{assetName}.prefab"
  };

       foreach (var path in paths)
      {
                var asset = firesAssets.LoadAsset<GameObject>(path);
     if (asset != null)
      {
   Debug.Log($"[UIPrefabFactory] Found prefab at path: {path}");
  return asset;
       }
   }
          }
         }
    catch (Exception ex)
   {
 Debug.LogWarning($"[UIPrefabFactory] Failed to load {assetName} from asset bundle: {ex.Message}");
     }

            return null;
      }

        #endregion

        #region Cleanup

        public static void ClearCache()
        {
  _dialogueEntryPrefab = null;
            _questEntryPrefab = null;
            VAMiscAssetManager.ClearPrefabCache();
     }

        #endregion
    }
}
