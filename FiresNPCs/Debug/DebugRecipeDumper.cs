using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker
{
    /// <summary>
    /// Debug utility to dump all craftable item recipes to a text file.
    /// Organized by crafting station, showing prefab IDs and resource requirements.
    ///
    /// USAGE:
    /// 1. Open console (~)
    /// 2. Type: dumprecipes
    /// 3. Check BepInEx/config/FiresRPGmaker/Debug/RecipeDump.txt
    /// </summary>
    public static class DebugRecipeDumper
    {
        private static bool _commandRegistered;

        public static void RegisterCommands()
        {
            if (_commandRegistered) return;

            new Terminal.ConsoleCommand("dumprecipes",
                "Dumps all craftable item recipes (grouped by crafting station) to a text file",
                (Terminal.ConsoleEventArgs args) =>
                {
                    try
                    {
                        DumpAllRecipes(args);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DebugRecipeDumper] dumprecipes exception: {ex}");
                        args.Context.AddString($"[FiresRPGmaker] dumprecipes error: {ex.Message}");
                    }
                });

            _commandRegistered = true;
            Debug.Log("[DebugRecipeDumper] Console command registered: dumprecipes");
        }

        private static void DumpAllRecipes(Terminal.ConsoleEventArgs args)
        {
            if (ObjectDB.instance == null)
            {
                args.Context.AddString("[FiresRPGmaker] ObjectDB not available - are you in a world?");
                return;
            }

            var recipes = ObjectDB.instance.m_recipes;
            if (recipes == null || recipes.Count == 0)
            {
                args.Context.AddString("[FiresRPGmaker] No recipes found in ObjectDB");
                return;
            }

            args.Context.AddString("[FiresRPGmaker] Scanning recipes...");

            // Group recipes by crafting station name.
            // Skip recipes that have no item (piece-only or invalid) and
            // skip entries where the item has a Piece component (piece recipes).
            var grouped = new SortedDictionary<string, List<Recipe>>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;

            foreach (var recipe in recipes)
            {
                if (recipe == null) continue;
                if (recipe.m_item == null) { skipped++; continue; }

                // Skip piece recipes - we only want craftable items
                if (recipe.m_item.GetComponent<Piece>() != null) { skipped++; continue; }

                string stationName = recipe.m_craftingStation != null
                    ? recipe.m_craftingStation.gameObject.name
                    : "No Station (Hand Craft)";

                if (!grouped.ContainsKey(stationName))
                    grouped[stationName] = new List<Recipe>();

                grouped[stationName].Add(recipe);
            }

            // Build output
            var sb = new StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM CRAFTABLE ITEM RECIPE DUMP");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total recipes: {recipes.Count}  (skipped {skipped} piece/invalid)");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            int totalDumped = 0;

            foreach (var kvp in grouped)
            {
                string station = kvp.Key;
                var stationRecipes = kvp.Value.OrderBy(r => r.m_item.name).ToList();

                sb.AppendLine("==============================================================");
                sb.AppendLine($"  CRAFTING STATION: {station}");
                sb.AppendLine($"  Recipes: {stationRecipes.Count}");
                sb.AppendLine("==============================================================");
                sb.AppendLine();

                foreach (var recipe in stationRecipes)
                {
                    totalDumped++;
                    string itemPrefab = recipe.m_item.name;
                    int minStationLevel = recipe.m_minStationLevel;
                    int amount = recipe.m_amount;

                    sb.AppendLine($"prefabID: {itemPrefab}");
                    if (amount > 1)
                        sb.AppendLine($"amount: {amount}");
                    if (minStationLevel > 1)
                        sb.AppendLine($"minStationLevel: {minStationLevel}");

                    // Base recipe resources
                    if (recipe.m_resources != null && recipe.m_resources.Length > 0)
                    {
                        sb.AppendLine("recipe:");
                        foreach (var req in recipe.m_resources)
                        {
                            if (req == null || req.m_resItem == null) continue;
                            string resPrefab = req.m_resItem.name;
                            int resAmount = req.m_amount;
                            sb.AppendLine($"  ({resPrefab}, {resAmount})");
                        }
                    }
                    else
                    {
                        sb.AppendLine("recipe: (none)");
                    }

                    // Upgrade costs (per-level amount increase)
                    bool hasUpgrade = false;
                    if (recipe.m_resources != null && recipe.m_resources.Length > 0)
                    {
                        foreach (var req in recipe.m_resources)
                        {
                            if (req == null || req.m_resItem == null) continue;
                            if (req.m_amountPerLevel > 0)
                            {
                                if (!hasUpgrade)
                                {
                                    sb.AppendLine("upgrade:");
                                    hasUpgrade = true;
                                }
                                string resPrefab = req.m_resItem.name;
                                sb.AppendLine($"  ({resPrefab}, {req.m_amountPerLevel})");
                            }
                        }
                    }

                    sb.AppendLine();
                }
            }

            // Summary
            sb.AppendLine("=============================================================");
            sb.AppendLine("SUMMARY");
            sb.AppendLine("=============================================================");
            sb.AppendLine();
            foreach (var kvp in grouped)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value.Count} recipes");
            }
            sb.AppendLine($"  TOTAL DUMPED: {totalDumped}");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "RecipeDump.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Dumped {totalDumped} recipes across {grouped.Count} station(s)");
            args.Context.AddString($"[FiresRPGmaker] Saved to: {outputPath}");

            // Show station counts in console
            foreach (var kvp in grouped)
            {
                args.Context.AddString($"  {kvp.Key}: {kvp.Value.Count}");
            }
        }
    }
}
