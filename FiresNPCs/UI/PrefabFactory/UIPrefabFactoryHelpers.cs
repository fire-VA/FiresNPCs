using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Helper methods for finding UI components.
    /// </summary>
    public static partial class UIPrefabFactory
    {
        #region Helper Methods
        
     private static void LogHierarchy(Transform t, string indent, int depth)
    {
    if (depth > 5) return;
     
            string components = "";
if (t.GetComponent<Button>() != null) components += "[Button]";
   if (t.GetComponent<TMP_Text>() != null) components += "[TMP_Text]";
   if (t.GetComponent<TMP_InputField>() != null) components += "[TMP_InputField]";
            if (t.GetComponent<ScrollRect>() != null) components += "[ScrollRect]";
     
  Debug.Log($"[UIPrefabFactory] Hierarchy: {indent}{t.name} {components}");
   
       foreach (Transform child in t)
         LogHierarchy(child, indent + "  ", depth + 1);
        }
   
        private static T FindComponent<T>(GameObject root, string path) where T : Component
   {
       var transform = root.transform.Find(path);
   return transform != null ? transform.GetComponent<T>() : null;
     }

        private static T FindComponentAnywhere<T>(GameObject root, string nameContains) where T : Component
        {
    var components = root.GetComponentsInChildren<T>(true);
 foreach (var c in components)
     {
           if (c.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
      return c;
     }
 return null;
 }
   
        private static Transform FindTransform(GameObject root, string path)
        {
  return root.transform.Find(path);
  }
 
        private static Transform FindTransformAnywhere(GameObject root, string nameContains)
      {
            var transforms = root.GetComponentsInChildren<Transform>(true);
          foreach (var t in transforms)
       {
         if (t.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
         return t;
        }
            return null;
  }
        
    private static GameObject FindGameObject(GameObject root, string path)
        {
 var transform = root.transform.Find(path);
      return transform?.gameObject;
        }
      
        private static GameObject FindGameObjectAnywhere(GameObject root, string nameContains)
        {
         var transforms = root.GetComponentsInChildren<Transform>(true);
   foreach (var t in transforms)
  {
           if (t.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
return t.gameObject;
     }
            return null;
        }

 #endregion
    }
}
