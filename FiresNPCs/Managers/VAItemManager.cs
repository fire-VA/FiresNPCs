using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    public static class VAItemManager
    {
        public static readonly List<string> ItemNameList = new List<string>();

        public static void LoadItems(string mainPath, FiresRPGmaker modInstance)
        {
            if (FiresRPGmaker.assetBundle == null)
            {
                Debug.LogError($"{FiresRPGmaker.PluginName}: AssetBundle is null.");
                return;
            }

            FiresRPGmaker.itemPrefabs.Clear();
            var allAssets = FiresRPGmaker.assetBundle.GetAllAssetNames();
            FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: AssetBundle contains {allAssets.Length} assets: {string.Join(", ", allAssets)}");

            // Bundle authors have used both "Assets/Custom/NPCs/items/" and
            // "Assets/Custom/VAitems/items/" historically. Try the configured
            // mainPath first, then fall back to the alternate folder so older
            // bundles still load and new prefabs added under VAitems work too.
            string[] candidatePaths = BuildCandidatePaths(mainPath);

            int failed = 0;
            foreach (var itemName in ItemNameList)
            {
                GameObject itemPrefab = null;
                string foundPath = null;
                foreach (var basePath in candidatePaths)
                {
                    string prefabPath = basePath + "items/" + itemName + ".prefab";
                    string normalizedPath = prefabPath.Replace('\\', '/').ToLower();
                    itemPrefab = FiresRPGmaker.assetBundle.LoadAsset<GameObject>(normalizedPath);
                    if (itemPrefab != null) { foundPath = normalizedPath; break; }
                }

                if (itemPrefab == null)
                {
                    Debug.LogError($"{FiresRPGmaker.PluginName}: Failed to load item prefab '{itemName}'. " +
                                   $"Tried: {string.Join(", ", candidatePaths.Select(p => (p + "items/" + itemName + ".prefab").ToLower()))}");
                    failed++;
                    continue;
                }

                var itemDrop = itemPrefab.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    Debug.LogError($"{FiresRPGmaker.PluginName}: {itemName} - ItemDrop component missing!");
                    UnityEngine.Object.Destroy(itemPrefab);
                    failed++;
                    continue;
                }

                itemPrefab.SetActive(true);
                // CRITICAL: DontDestroyOnLoad protects the bundle prefab from being
                // destroyed when the game scene unloads on logout. Without this, our
                // static itemPrefabs list (which survives scene unload) ends up holding
                // Unity fake-null references on the next session, which we then re-add
                // to ObjectDB.m_items / ZNetScene.m_namedPrefabs / m_itemByHash. Other
                // mods iterating those collections during their own ZNetScene.Awake or
                // ObjectDB.Awake patches dereference the fake-null and NRE — none of
                // our code shows up in the stack trace because we're not the iterator.
                UnityEngine.Object.DontDestroyOnLoad(itemPrefab);
                FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(itemPrefab, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader);
                FiresRPGmaker.itemPrefabs.Add(itemPrefab);
                FiresLogger.LogVerbose($"{FiresRPGmaker.PluginName}: Loaded item prefab: {itemName} (path: {foundPath}), ItemDrop present: {itemDrop != null}, m_dropPrefab: {(itemDrop.m_itemData.m_dropPrefab != null ? itemDrop.m_itemData.m_dropPrefab.name : "null")}");
            }

            Debug.Log($"{FiresRPGmaker.PluginName}: Loaded {FiresRPGmaker.itemPrefabs.Count} items. Failed: {failed}");
        }

        /// <summary>
        /// Builds the list of candidate folder prefixes used when locating an
        /// item prefab inside the asset bundle. Adds "Assets/Custom/VAitems/"
        /// as a fallback when the configured main path is the legacy
        /// "Assets/Custom/NPCs/" (and vice versa).
        /// </summary>
        private static string[] BuildCandidatePaths(string mainPath)
        {
            const string LEGACY = "Assets/Custom/NPCs/";
            const string VAITEMS = "Assets/Custom/VAitems/";

            // Start with the explicit one.
            var list = new List<string> { mainPath };

            // Add the alternate if it isn't already the configured one.
            string trimmed = mainPath.Trim();
            if (!string.Equals(trimmed, LEGACY, System.StringComparison.OrdinalIgnoreCase))
                list.Add(LEGACY);
            if (!string.Equals(trimmed, VAITEMS, System.StringComparison.OrdinalIgnoreCase))
                list.Add(VAITEMS);

            return list.ToArray();
        }
    }
}
