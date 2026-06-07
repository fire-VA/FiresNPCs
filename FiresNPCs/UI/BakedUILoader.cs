using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    // Loads baked UI prefabs from the dedicated 'firesnpcsui' bundle + their sidecar lookup tables
    // (embedded JSON), instantiates, and rebinds the matching generated class's static lookups via
    // reflection — replacing the per-open runtime hierarchy build. Returns null if the baked prefab /
    // tables aren't present, so callers fall back to runtime codegen (zero regression).
    internal static class BakedUILoader
    {
        public static GameObject Load(string name, Type generatedType, Transform parent)
        {
            try
            {
                var prefab = LoadPrefab(name);
                if (prefab == null) return null;
                var tables = LoadTables(name);
                if (tables.nodePaths == null) return null;

                var root = UnityEngine.Object.Instantiate(prefab, parent);
                BindGenerated(generatedType, root, tables.nodePaths, tables.tags, tables.tagLists);
                Debug.Log($"[BakedUILoader] '{name}' loaded from baked prefab ({tables.nodePaths.Count} nodes)");
                return root;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BakedUILoader] '{name}' baked load failed ({ex.Message}); falling back to codegen.");
                return null;
            }
        }

        private static GameObject LoadPrefab(string name)
        {
            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) { Debug.LogWarning("[BakedUILoader] uiBundle (firesnpcsui) is null"); return null; }
            string suffix = "/" + name.ToLowerInvariant() + ".prefab";
            foreach (var an in bundle.GetAllAssetNames())
            {
                if (an.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || an.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    var go = bundle.LoadAsset<GameObject>(an);
                    if (go != null) return go;
                }
            }
            Debug.LogWarning($"[BakedUILoader] prefab '{name}' not found in firesnpcsui bundle");
            return null;
        }

        private static (List<string> nodePaths, Dictionary<string, string> tags, Dictionary<string, List<string>> tagLists)
            LoadTables(string name)
        {
            var asm = typeof(FiresRPGmaker).Assembly;
            using var stream = asm.GetManifestResourceStream($"FiresRPGmaker.BundledLayouts.{name}.uitables.json");
            if (stream == null) return (null, null, null);
            using var reader = new StreamReader(stream);
            var o = JObject.Parse(reader.ReadToEnd());
            return (
                o["nodePaths"]?.ToObject<List<string>>(),
                o["tags"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>(),
                o["tagLists"]?.ToObject<Dictionary<string, List<string>>>() ?? new Dictionary<string, List<string>>());
        }

        // Rebinds the generated class's private static lookups (_nodes/_tagLookup/_tagListLookup/
        // _pathLookup/_root) to the loaded prefab instance, so FindByTag/ResolveAssets behave exactly
        // as after a fresh Instantiate(). Reflection keeps this generic across every generated screen.
        private static void BindGenerated(Type gen, GameObject root, List<string> nodePaths,
            Dictionary<string, string> tags, Dictionary<string, List<string>> tagLists)
        {
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Static;
            var tagLookup = (IDictionary)gen.GetField("_tagLookup", F).GetValue(null);
            var tagListLookup = (IDictionary)gen.GetField("_tagListLookup", F).GetValue(null);
            var pathLookup = (IDictionary)gen.GetField("_pathLookup", F).GetValue(null);
            tagLookup.Clear();
            tagListLookup.Clear();
            pathLookup.Clear();

            var nodes = new GameObject[nodePaths.Count];
            for (int i = 0; i < nodePaths.Count; i++)
            {
                var go = Resolve(root, nodePaths[i]);
                nodes[i] = go;
                if (go != null && !string.IsNullOrEmpty(nodePaths[i])) pathLookup[nodePaths[i]] = go;
            }
            gen.GetField("_nodes", F).SetValue(null, nodes);
            gen.GetField("_root", F)?.SetValue(null, root);

            foreach (var kv in tags)
            {
                var go = Resolve(root, kv.Value);
                if (go != null) tagLookup[kv.Key] = go;
            }
            foreach (var kv in tagLists)
            {
                var list = new List<GameObject>();
                foreach (var p in kv.Value) { var go = Resolve(root, p); if (go != null) list.Add(go); }
                tagListLookup[kv.Key] = list;
            }
        }

        // Bake-time paths are rooted at the prefab's own root node ("<RootName>/..."); strip the first
        // segment since `root` IS that node, so this works regardless of the root's name per screen.
        private static GameObject Resolve(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            if (slash < 0) return root;
            var t = root.transform.Find(path.Substring(slash + 1));
            return t != null ? t.gameObject : null;
        }
    }
}
