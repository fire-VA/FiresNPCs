
// VARecipeManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using FiresCore.Sync;

namespace FiresRPGmaker.Managers
{
    public static class VARecipeManager
    {
        private static readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

        /// <summary>
        /// Call this after pieces are loaded and config is ready
        /// Applies recipe requirements to every enabled piece based on config
        /// </summary>
        public static void UpdatePieceRequirements()
        {
            // Prevent infinite reload loop from config saving
            if (ConfigSync.ProcessingServerUpdate)
                return;

            if (ZNetScene.instance == null || FiresRPGmaker.piecePrefabs.Count == 0)
            {
                Debug.Log($"{FiresRPGmaker.PluginName}: Cannot update piece requirements - ZNetScene or piece list not ready.");
                return;
            }


            if (ZNetScene.instance == null || FiresRPGmaker.piecePrefabs.Count == 0)
            {
                Debug.Log($"{FiresRPGmaker.PluginName}: Cannot update piece requirements - ZNetScene or piece list not ready.");
                return;
            }

            _prefabCache.Clear();

            // Cache all loaded piece prefabs by name (lowercase for reliable lookup)
            foreach (var prefab in FiresRPGmaker.piecePrefabs)
            {
                if (prefab != null)
                {
                    _prefabCache[prefab.name.ToLowerInvariant()] = prefab;
                }
            }

            bool anyApplied = false;

            foreach (var kvp in ConfigManager.Instance.configPerPieceEnabled)
            {
                string pieceName = kvp.Key;

                // Skip if disabled or global recipe switch is off
                if (!kvp.Value.Value || !ConfigManager.Instance.configServerAuthority.Value)
                    continue;

                if (!_prefabCache.TryGetValue(pieceName.ToLowerInvariant(), out GameObject piecePrefab))
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Piece '{pieceName}' not found in loaded prefabs. Skipping recipe.");
                    continue;
                }

                var piece = piecePrefab.GetComponent<Piece>();
                if (piece == null)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Piece prefab '{pieceName}' has no Piece component.");
                    continue;
                }

                string configValue = ConfigManager.Instance.configPerPieceRecipes[pieceName].Value;
                var requirements = ParseRequirements(configValue);

                if (requirements.Length == 0)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: No valid requirements parsed for '{pieceName}' from config: '{configValue}'");
                    continue;
                }

                // Every piece we own is admin-gated by INGREDIENT, not by a UI
                // check: the recipe requires a cheat-sword, which only an admin
                // can spawn. That holds even if the piece reaches a non-admin's
                // build menu. Forced here rather than left to config, so an
                // existing cfg carrying the old default (or an admin editing
                // the string) can't drop the gate.
                if (!FiresCore.Items.AdminRecipeGate.TryEnsureGated(requirements, out var gatedRequirements))
                {
                    // Gate item unresolved = ObjectDB not built yet, not a
                    // missing item. Leave the piece's existing recipe alone
                    // rather than write an ungated one; this pass re-runs on
                    // config reload and will apply then.
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: '{pieceName}' recipe NOT applied — " +
                                     $"{FiresCore.Items.AdminRecipeGate.GateItemName} not resolvable yet. " +
                                     "Existing recipe left intact; will retry on next reload.");
                    continue;
                }

                piece.m_resources = gatedRequirements;
                anyApplied = true;

                if (ConfigManager.Instance.configVerboseLogging.Value)
                {
                    string reqList = string.Join(" | ", gatedRequirements.Select(r => $"{r.m_resItem.m_itemData.m_shared.m_name} x{r.m_amount}"));
                    Debug.Log($"{FiresRPGmaker.PluginName}: Applied recipe to {pieceName}: {reqList}");
                }
            }

            if (anyApplied && ConfigManager.Instance.configVerboseLogging.Value)
                Debug.Log($"{FiresRPGmaker.PluginName}: Piece recipe update complete.");
        }

        /// <summary>
        /// Parses a config string like "Wood,5" or "Wood,10,2,Stone,5,1" into Piece.Requirement[]
        /// </summary>
        private static Piece.Requirement[] ParseRequirements(string configValue)
        {
            var requirements = new List<Piece.Requirement>();
            var parts = configValue.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

            int i = 0;
            while (i < parts.Length)
            {
                if (i + 1 >= parts.Length)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Recipe ended unexpectedly at '{parts[i]}'");
                    break;
                }

                string itemName = parts[i];
                if (!int.TryParse(parts[i + 1], out int amount))
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Invalid amount '{parts[i + 1]}' for item '{itemName}'");
                    i += 2;
                    continue;
                }

                // Optional: per-level amount (if present)
                int perLevel = 0;
                if (i + 2 < parts.Length && int.TryParse(parts[i + 2], out int parsedPerLevel))
                {
                    perLevel = parsedPerLevel;
                    i += 3;
                }
                else
                {
                    i += 2; // Just item + amount
                }

                // Find the item prefab
                GameObject itemPrefab = null;
                string lowerName = itemName.ToLowerInvariant();

                if (_prefabCache.TryGetValue(lowerName, out itemPrefab) ||
                    (itemPrefab = ZNetScene.instance?.GetPrefab(itemName)) != null ||
                    (itemPrefab = ObjectDB.instance?.GetItemPrefab(itemName)) != null)
                {
                    if (!_prefabCache.ContainsKey(lowerName))
                        _prefabCache[lowerName] = itemPrefab;
                }
                else
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Could not find item prefab for '{itemName}' in recipe.");
                    continue;
                }

                var itemDrop = itemPrefab.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: Prefab '{itemName}' has no ItemDrop component.");
                    continue;
                }

                requirements.Add(new Piece.Requirement
                {
                    m_resItem = itemDrop,
                    m_amount = amount,
                    m_amountPerLevel = perLevel,
                    m_recover = true
                });
            }

            return requirements.ToArray();
        }
    }
}